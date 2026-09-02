using System.Collections;
using System.IO;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
#endif
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StimGen
{
    public enum ResponseFeedbackMode
    {
        None = 0,
        RecordedOnly = 1,
        CorrectnessPracticeOnly = 2,
    }

    /// <summary>
    /// 按会话计划播放实验：注视点 → Reference A → Comparison B → 空白，收按键，写日志。
    ///
    /// 当前正式时序：
    ///   注视点 → Reference A → Comparison B（只在 B 阶段接受回答）→ 空白
    ///   超时记为 Timeout，不当作错误按键，序列照常推进。
    ///   正式实验不给正确性反馈，只提供中性的输入确认。
    ///
    /// 运行期不做任何随机，也不根据被试表现改变刺激。
    /// </summary>
    [AddComponentMenu("StimGen/Experiment Runner")]
    public class ExperimentRunner : MonoBehaviour
    {
        [Header("会话来源")]
        public TextAsset sessionJson;
        [Tooltip("留空则用 sessionJson；否则从这个绝对路径读取")]
        public string sessionJsonPath;

        [Header("Pairing 呈现")]
        public Transform stimulusAnchor;
        public RotationController rotationController;
        public GameObject fixationVisual;

        [Tooltip("注视点时长（秒）")]
        public float fixationDuration = 0.5f;
        [Tooltip("Reference A 呈现时长（秒）")]
        [Min(0f)] public float referenceDuration = 2f;
        [Tooltip("Comparison B 呈现时长和回答窗口（秒）")]
        [Min(0f)] public float comparisonDuration = 3f;
        [Tooltip("物体消失后的空白间隔（秒）")]
        public float interTrialInterval = 0.4f;

        [Header("物体姿态")]
        [Tooltip("开启后，每个物体在保持X轴实验姿态的同时，绕外层世界Y轴匀速自转。")]
        public bool rotateDuringPresentation = true;
        [Tooltip("每次呈现的Y轴观看动画圈数。1 表示完整转一圈并回到动画起点。")]
        [Min(0f)] public float revolutionsPerPresentation = 1f;
        [Header("反馈")]
        [Tooltip("正式实验自动使用 RecordedOnly；练习 Session 自动使用逐题对错反馈。")]
        public ResponseFeedbackMode feedbackMode = ResponseFeedbackMode.RecordedOnly;
        [Tooltip("练习模式允许显示 Correct / Incorrect；关闭时对错模式会自动降级为 Recorded。")]
        public bool practiceMode;
        [Tooltip("有效回答后立即显示这么久；未作答提示会被限制在 Inter Trial Interval 内。")]
        [Min(0f)] public float feedbackDuration = 1.5f;
        [Tooltip("超时时是否显示 No response。Launcher 会在练习和正式模式中开启。")]
        public bool showTimeoutFeedback;
        [Range(12, 240)] public int feedbackFontSize = 120;
        public string recordedFeedbackText = "RESPONSE RECORDED";
        public string correctFeedbackText = "CORRECT";
        public string incorrectFeedbackText = "INCORRECT";
        public string timeoutFeedbackText = "NO RESPONSE";
        public Color recordedFeedbackColor = new Color(0.45f, 0.9f, 1f, 1f);
        public Color correctFeedbackColor = new Color(0.25f, 0.9f, 0.35f, 1f);
        public Color incorrectFeedbackColor = new Color(1f, 0.3f, 0.3f, 1f);
        public Color timeoutFeedbackColor = new Color(1f, 0.75f, 0.2f, 1f);

        [Header("流程提示")]
        [Min(0f)] public float modeBannerDuration = 2f;
        [Min(0f)] public float blockReadyDuration = 2f;
        [Tooltip("正式 Session 每个 Block 结束后等待 Launcher 录入评分并继续。")]
        public bool pauseForBlockRating = true;

        [Header("头显提示")]
        [Tooltip("Fixation Visual 未指定时，自动在头显中显示注视十字。")]
        public bool showVrFixationCross = true;
        public string fixationText = "+";
        public Color fixationColor = Color.white;

        [Header("实验员等待提示")]
        [Tooltip("实验未开始或已经结束时，在头显里显示等待提示。启动操作在 Tools/StimGen/Experiment Launcher 中完成。")]
        public bool showWaitingMessageInHeadset = true;
        public string waitingMessage = "Waiting for experimenter";
        public Color waitingMessageColor = Color.white;

        [Header("被试")]
        public string participantId = "P001";
        [Tooltip("从会话计划读取；为 true 时 Same/Different 按键左右互换")]
        public bool swapResponseKeys;

#if ENABLE_INPUT_SYSTEM
        public Key sameKey = Key.J;
        public Key differentKey = Key.F;
#else
        public KeyCode sameKey = KeyCode.J;
        public KeyCode differentKey = KeyCode.F;
#endif
        [Tooltip("Windows/Quest Link 下即使 Unity Game 窗口失去焦点，也继续读取 F/J。")]
        public bool captureWindowsKeyboardWithoutFocus = true;
        [Tooltip("启用 Meta Quest 右手柄回答：B = Same，A = Different。")]
        public bool acceptQuestRightControllerButtons = true;

        [Header("状态（只读）")]
        public int currentBlock = -1;
        public int currentPresentation = -1;
        public bool running;
        public bool paused;
        public bool pauseRequested;
        public bool waitingForBlockRating;
        public int completedBlockIndex = -1;

        public string OperatorStatus => _operatorStatus;
        public bool IsPracticeSession => _isPracticeSession;
        public int PracticeCorrect => _practiceCorrect;
        public int PracticeScored => _practiceScored;
        public float PracticeAccuracyPercent => _practiceScored > 0
            ? 100f * _practiceCorrect / _practiceScored : 0f;
        public bool PracticePassed => _practiceScored > 0 && PracticeAccuracyPercent >= 80f;
        public float CurrentPauseSeconds => paused
            ? Mathf.Max(0f, Now() - _pauseStartedAt) : 0f;
        public string SessionLabel
        {
            get
            {
                if (!string.IsNullOrEmpty(sessionJsonPath))
                    return Path.GetFileNameWithoutExtension(sessionJsonPath);
                return sessionJson != null ? sessionJson.name : "Not assigned";
            }
        }

        private SessionPlan _plan;
        private ExperimentLogger _logger;
        private StimulusObject _current;
        private IMarkerSink _markers;
        private bool _feedbackVisible;
        private string _feedbackText = "";
        private Color _feedbackColor = Color.white;
        private XrPromptPresenter _xrPrompt;
        private string _operatorStatus = "Ready";
        private bool _isPracticeSession;
        private int _practiceCorrect;
        private int _practiceScored;
        private float _pauseStartedAt;
        private int _submittedEffortRating;
        private string _submittedBlockNote = "";
        private BlockPlan _completedBlock;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        private bool _sameWindowsKeyWasDown;
        private bool _differentWindowsKeyWasDown;
#endif

        private void Awake()
        {
            running = false;
            paused = false;
            pauseRequested = false;
            waitingForBlockRating = false;
            currentBlock = -1;
            currentPresentation = -1;
            completedBlockIndex = -1;
            EnsureRuntimeDependencies();
        }

        private void Start()
        {
            ShowWaitingPrompt();
        }

        private void EnsureRuntimeDependencies()
        {
            if (rotationController == null)
                rotationController = GetComponent<RotationController>();
            if (rotationController == null)
                rotationController = gameObject.AddComponent<RotationController>();
            if (stimulusAnchor == null) stimulusAnchor = transform;
            if (_markers == null) _markers = new DebugMarkerSink();
            if (_xrPrompt == null) _xrPrompt = new XrPromptPresenter();
        }

        public void SetMarkerSink(IMarkerSink sink)
        {
            if (sink != null) _markers = sink;
        }

        public void RunSession()
        {
            if (!CanStart()) return;
            _operatorStatus = "Loading full session...";
            _plan = LoadPlan();
            if (_plan == null)
            {
                _operatorStatus = "Cannot load session - check Console";
                Debug.LogError("[StimGen] 没有可运行的会话计划：请指定 sessionJson 或 sessionJsonPath。");
                ShowWaitingPrompt();
                return;
            }
            swapResponseKeys = _plan.swapResponseKeys;
            if (!string.IsNullOrEmpty(_plan.participantId)) participantId = _plan.participantId;
            ConfigureModeForPlan();
            ClearFeedback();
            _operatorStatus = _isPracticeSession
                ? "Starting practice" : "Running full session";
            StartCoroutine(RunRoutine());
        }

        public void RunFirstBlockOnly()
        {
            if (!CanStart()) return;
            _operatorStatus = "Loading first-block test...";
            _plan = LoadPlan();
            if (_plan == null)
            {
                _operatorStatus = "Cannot load session - check Console";
                ShowWaitingPrompt();
                return;
            }
            swapResponseKeys = _plan.swapResponseKeys;
            if (!string.IsNullOrEmpty(_plan.participantId)) participantId = _plan.participantId;
            ConfigureModeForPlan();
            ClearFeedback();
            _operatorStatus = "Running first-block test";
            StartCoroutine(RunRoutine(1));
        }

        public void StopSession()
        {
            bool wasRunning = running;
            if (wasRunning && _logger != null)
                _logger.LogEvent("SessionStopped", currentBlock,
                    _completedBlock != null ? _completedBlock.sequenceId : "",
                    currentPresentation, -1, -1, Now(), "Stopped by experimenter");
            StopAllCoroutines();
            Hide();
            ClearFeedback();

            if (_logger != null)
            {
                _logger.Dispose();
                _logger = null;
            }

            running = false;
            paused = false;
            pauseRequested = false;
            waitingForBlockRating = false;
            currentBlock = -1;
            currentPresentation = -1;
            completedBlockIndex = -1;
            _completedBlock = null;
            _operatorStatus = wasRunning ? "Stopped by experimenter" : "Ready";
            ShowWaitingPrompt(wasRunning ? "Experiment stopped" : null);

            if (wasRunning)
                Debug.LogWarning("[StimGen] 实验员已停止当前 session；已完成的数据仍保留在日志中。");
        }

        public void RequestPause()
        {
            if (!running || paused || waitingForBlockRating) return;
            pauseRequested = true;
            _operatorStatus = "Pause requested - waiting for current presentation to finish";
            if (_logger != null)
                _logger.LogEvent("PauseRequested", currentBlock, "",
                    currentPresentation, -1, -1, Now(), "Safe pause after presentation");
        }

        public void ResumeSession()
        {
            if (!running || !paused || waitingForBlockRating) return;
            paused = false;
            _operatorStatus = "Resuming session";
        }

        public bool SubmitBlockRatingAndContinue(int effortRating, string note)
        {
            if (!running || !waitingForBlockRating) return false;
            if (effortRating < 1 || effortRating > 7)
            {
                Debug.LogWarning("[StimGen] 心理努力评分必须在 1–7 之间。");
                return false;
            }

            _submittedEffortRating = effortRating;
            _submittedBlockNote = note ?? "";
            waitingForBlockRating = false;
            return true;
        }

        public void RestartSession()
        {
            if (!Application.isPlaying) return;
            if (running) StopSession();
            RunSession();
        }

        private bool CanStart()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[StimGen] 请先点击 Unity 的 Play，再运行 session。");
                return false;
            }
            if (running)
            {
                Debug.LogWarning("[StimGen] session 已经在运行，不能重复启动。");
                return false;
            }
            EnsureRuntimeDependencies();
            return true;
        }

        private void ConfigureModeForPlan()
        {
            _isPracticeSession = _plan != null &&
                string.Equals(_plan.participantId, "PRACTICE",
                              System.StringComparison.OrdinalIgnoreCase);

            if (!_isPracticeSession && _plan != null && _plan.objects.Count > 0)
            {
                _isPracticeSession = true;
                for (int i = 0; i < _plan.objects.Count; i++)
                {
                    if (!_plan.objects[i].isPractice)
                    {
                        _isPracticeSession = false;
                        break;
                    }
                }
            }

            practiceMode = _isPracticeSession;
            feedbackMode = _isPracticeSession
                ? ResponseFeedbackMode.CorrectnessPracticeOnly
                : ResponseFeedbackMode.RecordedOnly;
            showTimeoutFeedback = true;
            feedbackFontSize = Mathf.Max(120, feedbackFontSize);
            recordedFeedbackText = "RESPONSE RECORDED";
            correctFeedbackText = "CORRECT";
            incorrectFeedbackText = "INCORRECT";
            timeoutFeedbackText = "NO RESPONSE";
            recordedFeedbackColor = new Color(0.45f, 0.9f, 1f, 1f);
            correctFeedbackColor = new Color(0.25f, 1f, 0.35f, 1f);
            incorrectFeedbackColor = new Color(1f, 0.22f, 0.22f, 1f);
            timeoutFeedbackColor = new Color(1f, 0.65f, 0.1f, 1f);
        }

        private SessionPlan LoadPlan()
        {
            string json = null;
            if (!string.IsNullOrEmpty(sessionJsonPath) && File.Exists(sessionJsonPath))
                json = File.ReadAllText(sessionJsonPath);
            else if (sessionJson != null)
                json = sessionJson.text;

            if (string.IsNullOrEmpty(json)) return null;

            var plan = JsonUtility.FromJson<SessionPlan>(json);
            if (plan == null || plan.blocks.Count == 0) return null;
            if (plan.taskProtocolVersion != ExperimentDesign.TaskProtocolVersion)
            {
                Debug.LogError("[StimGen] session 任务协议不匹配。文件为 '" +
                               plan.taskProtocolVersion + "'，程序要求 '" +
                               ExperimentDesign.TaskProtocolVersion +
                               "'。请重新生成 Pairing session。");
                return null;
            }
            if (plan.rotationProtocolVersion != ExperimentDesign.RotationProtocolVersion)
            {
                Debug.LogError("[StimGen] session 旋转协议不匹配。文件为 '" +
                               plan.rotationProtocolVersion + "'，程序要求 '" +
                               ExperimentDesign.RotationProtocolVersion +
                               "'。请重新生成 Pairing session。");
                return null;
            }
            plan.BuildIndex();
            return plan;
        }

        private IEnumerator RunRoutine(int blockLimit = int.MaxValue)
        {
            running = true;
            paused = false;
            pauseRequested = false;
            waitingForBlockRating = false;
            completedBlockIndex = -1;
            _completedBlock = null;
            _practiceCorrect = 0;
            _practiceScored = 0;
            _logger = new ExperimentLogger(participantId);
            _logger.SaveSessionPlan(_plan);
            Debug.Log("[StimGen] 记录文件：" + _logger.FilePath);
            Debug.Log("[StimGen] 事件时间表：" + _logger.EventFilePath);

            float sessionStart = Now();
            _logger.LogEvent("SessionStart", -1, "", -1, -1, -1, sessionStart,
                _isPracticeSession ? "Mode=Practice" : "Mode=Formal");

            string modeMessage = _isPracticeSession
                ? "PRACTICE MODE\n\nREFERENCE A → COMPARISON B\n" +
                  "ANSWER SAME / DIFFERENT"
                : "FORMAL SESSION\n\nREFERENCE A → COMPARISON B\n" +
                  "ANSWER SAME / DIFFERENT";
            Color modeColor = _isPracticeSession
                ? new Color(0.45f, 0.9f, 1f, 1f) : Color.white;
            float modeOnset = Now();
            ShowPrompt(modeMessage, modeColor, feedbackFontSize);
            _logger.LogEvent("ModeBannerOnset", -1, "", -1, -1, -1, modeOnset,
                _isPracticeSession ? "Practice" : "Formal");
            yield return WaitRealtime(modeBannerDuration);
            ClearFeedback();
            _logger.LogEvent("ModeBannerOffset", -1, "", -1, -1, -1, Now(), "");

            int blocksToRun = Mathf.Min(blockLimit, _plan.blocks.Count);

            for (int b = 0; b < blocksToRun; b++)
            {
                BlockPlan block = _plan.blocks[b];
                currentBlock = b;
                _operatorStatus = "Running block " + (b + 1) + " / " + blocksToRun;
                float segmentBoundaryTime = -1f;

                string readyMessage = _isPracticeSession
                    ? "PRACTICE\nPRACTICE WILL START"
                    : "BLOCK " + (b + 1) + " / " + blocksToRun + "\nGET READY";
                ShowPrompt(readyMessage, Color.white, feedbackFontSize);
                _logger.LogEvent("BlockReadyOnset", b, block.sequenceId,
                    -1, -1, -1, Now(), readyMessage.Replace('\n', ' '));
                yield return WaitRealtime(blockReadyDuration);
                ClearFeedback();
                _logger.LogEvent("BlockReadyOffset", b, block.sequenceId,
                    -1, -1, -1, Now(), "");
                _logger.LogEvent("BlockStart", b, block.sequenceId,
                    -1, -1, -1, Now(), "");

                for (int i = 0; i < block.presentations.Count; i++)
                {
                    PresentationRecord p = block.presentations[i];
                    currentPresentation = i;
                    _operatorStatus = "Running block " + (b + 1) + " / " + blocksToRun +
                                      ", trial " + (i + 1) + " / " +
                                      block.presentations.Count;

                    if (p.isFirstTrialAfterBoundary)
                    {
                        segmentBoundaryTime = Now();
                        _logger.LogEvent("SegmentBoundary", p, segmentBoundaryTime,
                            p.similarityTransition);
                    }

                    ObjectDefinition reference = _plan.Find(p.referenceObjectId);
                    ObjectDefinition comparison = _plan.Find(p.comparisonObjectId);
                    if (reference == null || comparison == null)
                    {
                        Debug.LogError("[StimGen] 会话里找不到 pair 物体：Reference=" +
                                       p.referenceObjectId + ", Comparison=" +
                                       p.comparisonObjectId);
                        break;
                    }

                    TrialResponse response = new TrialResponse();
                    yield return RunPairingTrial(p, reference, comparison,
                                                 r => response = r);

                    if (_isPracticeSession && p.scored)
                    {
                        _practiceScored++;
                        if (response.responded && response.saidSame == p.expectedSame)
                            _practiceCorrect++;
                    }

                    _logger.Log(p, response,
                                p.isFirstTrialAfterBoundary ? segmentBoundaryTime : -1f,
                                presentationAnimationEnabled: rotateDuringPresentation,
                                presentationAnimationRevolutions: revolutionsPerPresentation);

                    float feedbackElapsedAfterStimulus = response.feedbackShown
                         ? Mathf.Max(0f, response.feedbackOffset -
                            Mathf.Max(response.feedbackOnset, response.comparisonOffset))
                         : 0f;
                    float isiOnset = Now();
                    _logger.LogEvent("InterTrialOnset", p, isiOnset, "");
                    float isiEnd = isiOnset + Mathf.Max(
                        0f, interTrialInterval - feedbackElapsedAfterStimulus);
                    while (Now() < isiEnd) yield return null;
                    _logger.LogEvent("InterTrialOffset", p, Now(), "");

                    if (pauseRequested)
                        yield return PauseAtSafePoint(p);
                }

                _completedBlock = block;
                completedBlockIndex = b;
                _logger.LogEvent("BlockEnd", b, block.sequenceId,
                    block.presentations.Count - 1, -1, -1, Now(), "");
                Debug.Log("[StimGen] block " + b + "（sequence " + block.sequenceId + "）结束。" +
                          "请采集心理努力评分后继续。");

                if (!_isPracticeSession && pauseForBlockRating)
                    yield return WaitForBlockRating(block, b, blocksToRun);
            }

            _logger.LogEvent("SessionEnd", -1, "", -1, -1, -1, Now(),
                _isPracticeSession
                    ? "PracticeAccuracy=" + PracticeAccuracyPercent.ToString("F1") + "%"
                    : "Completed");
            _logger.Dispose();
            _logger = null;
            running = false;
            paused = false;
            pauseRequested = false;
            waitingForBlockRating = false;
            currentBlock = -1;
            currentPresentation = -1;
            bool firstBlockTest = blocksToRun == 1 && _plan.blocks.Count > 1;
            if (_isPracticeSession)
            {
                _operatorStatus = PracticePassed
                    ? "Practice passed (" + PracticeAccuracyPercent.ToString("F0") + "%)"
                    : "Practice below 80% - repeat recommended (" +
                      PracticeAccuracyPercent.ToString("F0") + "%)";
                ShowWaitingPrompt((PracticePassed ? "PRACTICE PASSED" : "PLEASE REPEAT PRACTICE") +
                    "\nPractice accuracy: " + PracticeAccuracyPercent.ToString("F0") + "%");
            }
            else
            {
                _operatorStatus = firstBlockTest
                    ? "First-block test completed" : "Session completed";
                ShowWaitingPrompt(firstBlockTest ? "Block complete" : "Session complete");
            }
            Debug.Log("[StimGen] 会话结束。");
        }

        private IEnumerator WaitRealtime(float duration)
        {
            float end = Now() + Mathf.Max(0f, duration);
            while (Now() < end) yield return null;
        }

        private IEnumerator PauseAtSafePoint(PresentationRecord record)
        {
            pauseRequested = false;
            paused = true;
            _pauseStartedAt = Now();
            _operatorStatus = "Paused safely after presentation " +
                              (record.presentationIndexInBlock + 1);
            ShowPrompt("EXPERIMENT PAUSED\n\nPlease wait for the experimenter",
                       waitingMessageColor, feedbackFontSize);
            if (_logger != null)
                _logger.LogEvent("PauseOnset", record, _pauseStartedAt, "Manual safe pause");

            while (paused && running) yield return null;

            if (!running) yield break;
            float pauseEnd = Now();
            if (_logger != null)
                _logger.LogEvent("PauseOffset", record, pauseEnd,
                    "PauseSeconds=" + Mathf.Max(0f, pauseEnd - _pauseStartedAt).ToString("F3"));
            ClearFeedback();
            _operatorStatus = "Running block " + (currentBlock + 1);
        }

        private IEnumerator WaitForBlockRating(BlockPlan block, int blockIndex,
                                               int blocksToRun)
        {
            pauseRequested = false;
            paused = true;
            waitingForBlockRating = true;
            _submittedEffortRating = 0;
            _submittedBlockNote = "";
            _pauseStartedAt = Now();
            _operatorStatus = "Waiting for effort rating after block " + (blockIndex + 1);

            string message = "BLOCK " + (blockIndex + 1) + " COMPLETE\n\n" +
                             "Please rest and wait for the experimenter";
            ShowPrompt(message, new Color(0.45f, 0.9f, 1f, 1f), feedbackFontSize);
            _logger.LogEvent("BlockPauseOnset", blockIndex, block.sequenceId,
                block.presentations.Count - 1, -1, -1, _pauseStartedAt,
                "Awaiting effort rating 1-7");

            while (waitingForBlockRating && running) yield return null;
            if (!running) yield break;

            float ratingTime = Now();
            float restSeconds = Mathf.Max(0f, ratingTime - _pauseStartedAt);
            _logger.LogBlockSummary(blockIndex, block.sequenceId,
                _submittedEffortRating, restSeconds, _submittedBlockNote, ratingTime);
            _logger.LogEvent("BlockRatingSubmitted", blockIndex, block.sequenceId,
                block.presentations.Count - 1, -1, -1, ratingTime,
                "Effort=" + _submittedEffortRating +
                ";RestSeconds=" + restSeconds.ToString("F3") +
                ";Note=" + _submittedBlockNote);
            _logger.LogEvent("BlockPauseOffset", blockIndex, block.sequenceId,
                block.presentations.Count - 1, -1, -1, ratingTime, "");

            paused = false;
            ClearFeedback();
            _operatorStatus = blockIndex + 1 < blocksToRun
                ? "Preparing next block" : "Finishing session";
        }

        private IEnumerator RunPairingTrial(PresentationRecord p,
                                             ObjectDefinition reference,
                                             ObjectDefinition comparison,
                                             System.Action<TrialResponse> onDone)
        {
            var response = new TrialResponse();
            ClearFeedback();
            float responseFeedbackEnd = -1f;

            // ---- 注视点 ----
            ShowFixation(true);
            response.fixationOnset = Now();
            _markers.SendMarker("FixationOnset", p, response.fixationOnset);
            _logger.LogEvent("FixationOnset", p, response.fixationOnset, "");

            float fixEnd = response.fixationOnset + fixationDuration;
            while (Now() < fixEnd) yield return null;
            ShowFixation(false);
            _logger.LogEvent("FixationOffset", p, Now(), "");

            // ---- Reference A：只呈现，不接受 Same/Different 回答 ----
            Show(reference, p.referenceRotationX);
            response.referenceOnset = Now();
            _markers.SendMarker("ReferenceOnset", p, response.referenceOnset);
            _logger.LogEvent("ReferenceOnset", p, response.referenceOnset,
                "Object=" + p.referenceObjectId +
                ";RotationX=" + p.referenceRotationX.ToString("F1"));

            float referenceDurationSeconds = Mathf.Max(0f, referenceDuration);
            float referenceEnd = response.referenceOnset + referenceDurationSeconds;
            PrimeWindowsKeyState();

            while (Now() < referenceEnd)
            {
                if (rotateDuringPresentation && referenceDurationSeconds > 0f && _current != null)
                {
                    float progress = (Now() - response.referenceOnset) /
                                    referenceDurationSeconds;
                    rotationController.ApplyPresentationSpin(progress,
                                                             revolutionsPerPresentation);
                }
                yield return null;
            }

            Hide();
            response.referenceOffset = Now();
            _markers.SendMarker("ReferenceOffset", p, response.referenceOffset);
            _logger.LogEvent("ReferenceOffset", p, response.referenceOffset, "");

            // ---- Comparison B：从这里开始接受回答，RT 以 B onset 为零点 ----
            Show(comparison, p.comparisonRotationX);
            response.comparisonOnset = Now();
            _markers.SendMarker("ComparisonOnset", p, response.comparisonOnset);
            // 保留通用 Stimulus 标记名，方便旧的外部同步脚本继续对齐 B 阶段。
            _markers.SendMarker("StimulusOnset", p, response.comparisonOnset);
            _logger.LogEvent("ComparisonOnset", p, response.comparisonOnset,
                "Object=" + p.comparisonObjectId +
                ";RotationX=" + p.comparisonRotationX.ToString("F1"));
            _logger.LogEvent("StimulusOnset", p, response.comparisonOnset,
                "Phase=Comparison;Object=" + p.comparisonObjectId);

            float comparisonDurationSeconds = Mathf.Max(0f, comparisonDuration);
            float comparisonEnd = response.comparisonOnset + comparisonDurationSeconds;
            PrimeWindowsKeyState();

            while (Now() < comparisonEnd)
            {
                if (rotateDuringPresentation && comparisonDurationSeconds > 0f && _current != null)
                {
                    float progress = (Now() - response.comparisonOnset) /
                                     comparisonDurationSeconds;
                    rotationController.ApplyPresentationSpin(progress, revolutionsPerPresentation);
                }

                if (p.scored && !response.responded)
                {
                    bool pressedSame, pressedDifferent;
                    ReadKeys(out pressedSame, out pressedDifferent);
                    if (pressedSame || pressedDifferent)
                    {
                        response.responded = true;
                        response.saidSame = pressedSame;
                        response.responseTimestamp = Now();
                        response.reactionTime = response.responseTimestamp -
                                                response.comparisonOnset;
                        _markers.SendMarker("Response", p, response.responseTimestamp);
                        _logger.LogEvent("Response", p, response.responseTimestamp,
                            response.saidSame ? "Same" : "Different");

                        string immediateMode, immediateMessage;
                        Color immediateColor;
                        if (TryGetFeedback(p, response, out immediateMode,
                                           out immediateMessage, out immediateColor))
                        {
                            response.feedbackShown = true;
                            response.feedbackMode = immediateMode;
                            response.feedbackText = immediateMessage;
                            response.feedbackOnset = Now();
                            responseFeedbackEnd = response.feedbackOnset +
                                Mathf.Max(0f, feedbackDuration);
                            ShowPrompt(immediateMessage, immediateColor, feedbackFontSize);
                            _markers.SendMarker("FeedbackOnset", p, response.feedbackOnset);
                            _logger.LogEvent("FeedbackOnset", p, response.feedbackOnset,
                                immediateMode + ";" + immediateMessage.Replace('\n', ' '));
                        }
                    }
                }

                if (response.feedbackShown && response.feedbackOffset <= 0f &&
                    responseFeedbackEnd >= 0f && Now() >= responseFeedbackEnd)
                {
                    ClearFeedback();
                    response.feedbackOffset = Now();
                    _markers.SendMarker("FeedbackOffset", p, response.feedbackOffset);
                    _logger.LogEvent("FeedbackOffset", p, response.feedbackOffset,
                        response.feedbackMode);
                }
                yield return null;
            }

            if (response.feedbackShown && response.feedbackOffset <= 0f)
            {
                ClearFeedback();
                response.feedbackOffset = Now();
                _markers.SendMarker("FeedbackOffset", p, response.feedbackOffset);
                _logger.LogEvent("FeedbackOffset", p, response.feedbackOffset,
                    response.feedbackMode);
            }

            Hide();
            response.comparisonOffset = Now();
            _markers.SendMarker("ComparisonOffset", p, response.comparisonOffset);
            _markers.SendMarker("StimulusOffset", p, response.comparisonOffset);
            _logger.LogEvent("ComparisonOffset", p, response.comparisonOffset, "");
            _logger.LogEvent("StimulusOffset", p, response.comparisonOffset,
                "Phase=Comparison");

            // 超时不算错误按键，只标记为缺失
            if (p.scored && !response.responded) response.timeout = true;

            // 有效回答的反馈已经在按键后立刻显示；超时提示在刺激结束后显示，
            // 并限制在 ITI 内，避免改变下一题的开始时间。
            if (p.scored && !response.responded)
            {
                string timeoutMode, timeoutMessage;
                Color timeoutColor;
                float timeoutWindow = Mathf.Min(Mathf.Max(0f, feedbackDuration),
                                                Mathf.Max(0f, interTrialInterval));
                if (timeoutWindow > 0f && TryGetFeedback(
                        p, response, out timeoutMode, out timeoutMessage, out timeoutColor))
                {
                    response.feedbackShown = true;
                    response.feedbackMode = timeoutMode;
                    response.feedbackText = timeoutMessage;
                    response.feedbackOnset = Now();
                    ShowPrompt(timeoutMessage, timeoutColor, feedbackFontSize);
                    _markers.SendMarker("FeedbackOnset", p, response.feedbackOnset);
                    _logger.LogEvent("FeedbackOnset", p, response.feedbackOnset,
                        timeoutMode + ";" + timeoutMessage.Replace('\n', ' '));

                    float feedbackEnd = response.feedbackOnset + timeoutWindow;
                    while (Now() < feedbackEnd) yield return null;

                    ClearFeedback();
                    response.feedbackOffset = Now();
                    _markers.SendMarker("FeedbackOffset", p, response.feedbackOffset);
                    _logger.LogEvent("FeedbackOffset", p, response.feedbackOffset,
                        timeoutMode);
                }
            }

            if (!response.feedbackShown)
            {
                response.feedbackMode = ResponseFeedbackMode.None.ToString();
            }

            onDone(response);
        }

        private bool TryGetFeedback(PresentationRecord p, TrialResponse response,
                                    out string effectiveMode, out string message,
                                    out Color color)
        {
            effectiveMode = ResponseFeedbackMode.None.ToString();
            message = "";
            color = recordedFeedbackColor;

            if (!p.scored || feedbackMode == ResponseFeedbackMode.None) return false;

            if (!response.responded)
            {
                if (!showTimeoutFeedback) return false;
                effectiveMode = "Timeout";
                message = timeoutFeedbackText;
                color = timeoutFeedbackColor;
                return !string.IsNullOrEmpty(message);
            }

            if (feedbackMode == ResponseFeedbackMode.CorrectnessPracticeOnly && practiceMode)
            {
                bool correct = response.saidSame == p.expectedSame;
                effectiveMode = ResponseFeedbackMode.CorrectnessPracticeOnly.ToString();
                message = correct ? correctFeedbackText : incorrectFeedbackText;
                color = correct ? correctFeedbackColor : incorrectFeedbackColor;
            }
            else
            {
                effectiveMode = ResponseFeedbackMode.RecordedOnly.ToString();
                message = recordedFeedbackText;
                color = recordedFeedbackColor;
            }
            return !string.IsNullOrEmpty(message);
        }

        private void ClearFeedback()
        {
            _feedbackVisible = false;
            _feedbackText = "";
            if (_xrPrompt != null) _xrPrompt.Hide();
        }

        private void ShowPrompt(string message, Color color, int fontSize)
        {
            _feedbackText = message;
            _feedbackColor = color;

            // World-space text is rendered by CenterEyeAnchor and is therefore visible
            // in both eyes. OnGUI remains active only as a desktop fallback.
            bool shownInXr = _xrPrompt != null &&
                             _xrPrompt.Show(message, color, fontSize, stimulusAnchor);
            _feedbackVisible = !shownInXr;
        }

        private void ShowWaitingPrompt(string messageOverride = null)
        {
            if (!Application.isPlaying || running || !showWaitingMessageInHeadset) return;
            EnsureRuntimeDependencies();

            string message = string.IsNullOrEmpty(messageOverride)
                ? waitingMessage : messageOverride;
            if (!string.IsNullOrEmpty(message) && _xrPrompt != null)
                _xrPrompt.Show(message, waitingMessageColor,
                               Mathf.Max(60, feedbackFontSize), stimulusAnchor);
        }

        private void OnGUI()
        {
            if (!_feedbackVisible || string.IsNullOrEmpty(_feedbackText)) return;

            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Clamp(feedbackFontSize, 12, 240),
                fontStyle = FontStyle.Bold,
                wordWrap = true,
            };
            style.normal.textColor = _feedbackColor;

            float width = Mathf.Min(Screen.width * 0.9f, 1000f);
            float height = Mathf.Min(Screen.height * 0.6f, 520f);
            var rect = new Rect((Screen.width - width) * 0.5f,
                                (Screen.height - height) * 0.5f, width, height);
            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.78f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = previousColor;
            GUI.Label(rect, _feedbackText, style);
        }

        private void OnDisable()
        {
            Hide();
            ClearFeedback();
            if (_xrPrompt != null)
            {
                _xrPrompt.Dispose();
                _xrPrompt = null;
            }
            if (_logger != null)
            {
                _logger.Dispose();
                _logger = null;
            }
            running = false;
            paused = false;
            pauseRequested = false;
            waitingForBlockRating = false;
            currentBlock = -1;
            currentPresentation = -1;
            completedBlockIndex = -1;
            _completedBlock = null;
        }

        private void Show(ObjectDefinition def, float rotationX)
        {
            Hide();
            _current = ObjectAssembler.Build(def, stimulusAnchor);
            _current.transform.localPosition = Vector3.zero;
            rotationController.Bind(_current, rotationX);
        }

        private void Hide()
        {
            if (_current == null) return;
            if (rotationController != null && rotationController.IsBoundTo(_current))
                rotationController.ReleasePresentationRig();
            else
                Destroy(_current.gameObject);
            _current = null;
        }

        private void ShowFixation(bool visible)
        {
            if (fixationVisual != null)
            {
                fixationVisual.SetActive(visible);
                return;
            }

            if (!showVrFixationCross) return;
            if (visible)
            {
                ShowPrompt(fixationText, fixationColor, Mathf.Max(60, feedbackFontSize));
            }
            else
            {
                ClearFeedback();
            }
        }

        private static float Now()
        {
            return Time.realtimeSinceStartup;
        }

        private void ReadKeys(out bool pressedSame, out bool pressedDifferent)
        {
            bool a = KeyPressed(sameKey);
            bool b = KeyPressed(differentKey);
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (captureWindowsKeyboardWithoutFocus)
            {
                a |= WindowsKeyPressed(ToWindowsVirtualKey(sameKey), ref _sameWindowsKeyWasDown);
                b |= WindowsKeyPressed(ToWindowsVirtualKey(differentKey), ref _differentWindowsKeyWasDown);
            }
#endif
            bool keyboardSame = swapResponseKeys ? b : a;
            bool keyboardDifferent = swapResponseKeys ? a : b;

            bool controllerSame = false;
            bool controllerDifferent = false;
            if (acceptQuestRightControllerButtons)
            {
                // On RTouch, virtual Button.Two is B and Button.One is A.
                // This mapping is intentionally fixed and is not affected by
                // the participant's keyboard counterbalancing setting.
                controllerSame = OVRInput.GetDown(OVRInput.Button.Two,
                                                  OVRInput.Controller.RTouch);
                controllerDifferent = OVRInput.GetDown(OVRInput.Button.One,
                                                       OVRInput.Controller.RTouch);
            }

            pressedSame = keyboardSame || controllerSame;
            pressedDifferent = keyboardDifferent || controllerDifferent;
        }

        private void PrimeWindowsKeyState()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (!captureWindowsKeyboardWithoutFocus) return;
            _sameWindowsKeyWasDown = WindowsKeyIsDown(ToWindowsVirtualKey(sameKey));
            _differentWindowsKeyWasDown = WindowsKeyIsDown(ToWindowsVirtualKey(differentKey));
#endif
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        private static bool WindowsKeyPressed(int virtualKey, ref bool wasDown)
        {
            if (virtualKey == 0) return false;
            bool isDown = WindowsKeyIsDown(virtualKey);
            bool pressed = isDown && !wasDown;
            wasDown = isDown;
            return pressed;
        }

        private static bool WindowsKeyIsDown(int virtualKey)
        {
            return virtualKey != 0 && (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

#if ENABLE_INPUT_SYSTEM
        private static int ToWindowsVirtualKey(Key key)
        {
            int value = (int)key;
            int firstLetter = (int)Key.A;
            int lastLetter = (int)Key.Z;
            if (value >= firstLetter && value <= lastLetter)
                return 0x41 + value - firstLetter;

            switch (key)
            {
                case Key.Space: return 0x20;
                case Key.Enter: return 0x0D;
                case Key.NumpadEnter: return 0x0D;
                case Key.LeftArrow: return 0x25;
                case Key.UpArrow: return 0x26;
                case Key.RightArrow: return 0x27;
                case Key.DownArrow: return 0x28;
                default: return 0;
            }
        }
#else
        private static int ToWindowsVirtualKey(KeyCode key)
        {
            int value = (int)key;
            if (value >= (int)KeyCode.A && value <= (int)KeyCode.Z) return value;
            switch (key)
            {
                case KeyCode.Space: return 0x20;
                case KeyCode.Return:
                case KeyCode.KeypadEnter: return 0x0D;
                case KeyCode.LeftArrow: return 0x25;
                case KeyCode.UpArrow: return 0x26;
                case KeyCode.RightArrow: return 0x27;
                case KeyCode.DownArrow: return 0x28;
                default: return 0;
            }
        }
#endif
#endif

#if ENABLE_INPUT_SYSTEM
        private static bool KeyPressed(Key key)
        {
            Keyboard kb = Keyboard.current;
            return kb != null && kb[key].wasPressedThisFrame;
        }
#else
        private static bool KeyPressed(KeyCode key)
        {
            return Input.GetKeyDown(key);
        }
#endif
    }

    /// <summary>
    /// Creates a head-tracked world-space canvas at the stimulus viewing distance.
    /// This is deliberately runtime-generated so the experiment also works when no
    /// UI prefab has been assigned in the scene.
    /// </summary>
    internal sealed class XrPromptPresenter
    {
        private const float DefaultDistance = 2.5f;
        private const float MinimumDistance = 1f;
        private const float MaximumDistance = 20f;
        private const float AngularScale = 0.00018f;

        private GameObject _root;
        private Canvas _canvas;
        private Image _background;
        private TextMeshProUGUI _label;
        private Camera _camera;
        private bool _warnedAboutCamera;

        public bool Show(string message, Color color, int requestedFontSize,
                         Transform stimulusAnchor)
        {
            Camera camera = FindPresentationCamera();
            if (camera == null)
            {
                if (!_warnedAboutCamera)
                {
                    Debug.LogWarning("[StimGen] 找不到可用的 VR 摄像机；提示暂时只显示在电脑 Game 窗口。");
                    _warnedAboutCamera = true;
                }
                return false;
            }

            EnsureVisual(camera);
            float distance = ResolveDistance(camera, stimulusAnchor);
            // Keep the world-space prompt slightly nearer than the stimulus so that
            // immediate feedback cannot z-fight with, or be hidden by, the object.
            float promptDistance = Mathf.Max(camera.nearClipPlane + 0.2f,
                                             distance - 0.25f);

            Transform t = _root.transform;
            if (t.parent != camera.transform)
                t.SetParent(camera.transform, false);
            t.localPosition = new Vector3(0f, 0f, promptDistance);
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one * (promptDistance * AngularScale);

            _canvas.worldCamera = camera;
            _label.text = string.IsNullOrEmpty(message) ? " " : message;
            _label.color = color;
            float maximumFontSize = Mathf.Clamp(requestedFontSize * 2f, 96f, 240f);
            _label.fontSizeMax = maximumFontSize;
            _label.fontSizeMin = Mathf.Min(64f, maximumFontSize);
            _label.fontSize = maximumFontSize;
            _root.SetActive(true);
            return true;
        }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        public void Dispose()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _canvas = null;
            _background = null;
            _label = null;
            _camera = null;
        }

        private void EnsureVisual(Camera camera)
        {
            if (_root != null)
            {
                _camera = camera;
                return;
            }

            _camera = camera;
            _root = new GameObject("[StimGen] XR Prompt",
                                   typeof(RectTransform), typeof(Canvas),
                                   typeof(CanvasScaler));

            RectTransform canvasRect = _root.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(1200f, 650f);

            _canvas = _root.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = camera;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = short.MaxValue;

            CanvasScaler scaler = _root.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;

            var backgroundObject = new GameObject("Prompt Background",
                                                  typeof(RectTransform),
                                                  typeof(CanvasRenderer),
                                                  typeof(Image));
            backgroundObject.transform.SetParent(_root.transform, false);
            _background = backgroundObject.GetComponent<Image>();
            RectTransform backgroundRect = _background.rectTransform;
            backgroundRect.anchorMin = new Vector2(0.03f, 0.08f);
            backgroundRect.anchorMax = new Vector2(0.97f, 0.92f);
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            _background.color = new Color(0f, 0f, 0f, 0.78f);
            _background.raycastTarget = false;

            var labelObject = new GameObject("Prompt Text",
                                             typeof(RectTransform),
                                             typeof(CanvasRenderer),
                                             typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(_root.transform, false);

            _label = labelObject.GetComponent<TextMeshProUGUI>();
            RectTransform labelRect = _label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            _label.alignment = TextAlignmentOptions.Center;
            _label.fontStyle = FontStyles.Bold;
            _label.enableAutoSizing = true;
            _label.textWrappingMode = TextWrappingModes.Normal;
            _label.raycastTarget = false;
            _label.outlineColor = Color.black;
            _label.outlineWidth = 0.18f;

            _root.transform.SetParent(camera.transform, false);
            _root.SetActive(false);
        }

        private static Camera FindPresentationCamera()
        {
            Camera[] cameras = Camera.allCameras;

            // OVRCameraRig's stereo output camera.
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera candidate = cameras[i];
                if (candidate != null && candidate.isActiveAndEnabled &&
                    candidate.name == "CenterEyeAnchor")
                    return candidate;
            }

            Camera main = Camera.main;
            if (main != null && main.isActiveAndEnabled) return main;

            for (int i = 0; i < cameras.Length; i++)
            {
                Camera candidate = cameras[i];
                if (candidate != null && candidate.isActiveAndEnabled)
                    return candidate;
            }
            return null;
        }

        private static float ResolveDistance(Camera camera, Transform stimulusAnchor)
        {
            float distance = DefaultDistance;
            if (stimulusAnchor != null)
            {
                Vector3 toStimulus = stimulusAnchor.position - camera.transform.position;
                float forwardDistance = Vector3.Dot(toStimulus, camera.transform.forward);
                if (forwardDistance > camera.nearClipPlane + 0.1f)
                    distance = forwardDistance;
            }
            return Mathf.Clamp(distance, MinimumDistance, MaximumDistance);
        }
    }
}
