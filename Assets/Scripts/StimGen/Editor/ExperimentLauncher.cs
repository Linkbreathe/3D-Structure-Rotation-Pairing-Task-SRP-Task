using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StimGen
{
    /// <summary>
    /// 独立的实验员控制窗口。它不渲染到 Game 画面，也不会进入头显。
    /// 在编辑模式点击启动按钮时，会自动进入 Play，并在场景初始化后执行实验。
    /// </summary>
    public sealed class ExperimentLauncher : EditorWindow
    {
        private readonly List<TextAsset> _sessions = new List<TextAsset>();
        private string[] _sessionNames = Array.Empty<string>();
        private Vector2 _scroll;
        private string _windowMessage = "请选择受试者 Session，然后启动实验。";
        private int _effortRating = 4;
        private string _blockNote = "";
        private string _lastPreflightReport = "尚未检查。";
        private string _lastPreflightPath = "";

        [MenuItem("Tools/StimGen/Experiment Launcher")]
        public static void Open()
        {
            GetWindow<ExperimentLauncher>(false, "StimGen Launcher", true).minSize =
                new Vector2(440f, 430f);
        }

        private void OnEnable()
        {
            RefreshSessions();
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("实验启动面板", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "这是实验员专用的 Unity 编辑器窗口，不会显示在电脑 Game 画面或 VR 头显中。" +
                "在非 Play 状态点击启动按钮时，Unity 会自动进入 Play。",
                MessageType.Info);

            ExperimentRunner runner = ExperimentLaunchCoordinator.FindRunner();
            if (runner == null)
            {
                EditorGUILayout.HelpBox(
                    "当前打开的场景里没有 ExperimentRunner。请先打开 SampleScene。",
                    MessageType.Error);
                if (GUILayout.Button("重新查找场景中的 ExperimentRunner")) Repaint();
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("① 选择受试者", EditorStyles.boldLabel);
            DrawSessionSelector(runner);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("② 确认运行状态", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Experiment Runner", runner,
                                            typeof(ExperimentRunner), true);
            EditorGUILayout.LabelField("受试者编号", runner.participantId);
            EditorGUILayout.LabelField("当前 Session", runner.SessionLabel);
            bool practice = ExperimentPreflightGate.IsPracticeSession(runner);
            EditorGUILayout.HelpBox(practice
                ? "PRACTICE MODE / 练习模式：逐题显示正确、错误或未作答。"
                : "FORMAL MODE / 正式模式：有效回答只显示中性的“已记录”；" +
                  "未作答会明确提示，但不会透露正式题目的正确答案。",
                practice ? MessageType.Info : MessageType.None);
            EditorGUILayout.LabelField("旋转协议", "Pair 内 X轴差 0° / 90° / 180° + Y轴观看自转");
            EditorGUILayout.LabelField("Pairing 时序", "A " +
                runner.referenceDuration.ToString("F1") + " 秒 → B " +
                runner.comparisonDuration.ToString("F1") + " 秒");
            EditorGUILayout.LabelField("Unity 状态", UnityStateLabel());
            EditorGUILayout.LabelField("实验状态", runner.OperatorStatus);
            if (runner.running)
            {
                EditorGUILayout.LabelField("Block", (runner.currentBlock + 1).ToString());
                EditorGUILayout.LabelField("Trial",
                    (runner.currentPresentation + 1).ToString());
                if (runner.paused)
                    EditorGUILayout.LabelField("已暂停",
                        runner.CurrentPauseSeconds.ToString("F1") + " 秒");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("③ 启动或停止", EditorStyles.boldLabel);
            DrawRunControls(runner);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("④ 运行前检查", EditorStyles.boldLabel);
            if (GUILayout.Button("检查当前 Session 并保存报告"))
            {
                bool passed = ExperimentPreflightGate.ValidateAndSave(
                    runner, out _lastPreflightReport, out _lastPreflightPath);
                _windowMessage = passed
                    ? "运行前检查通过。报告：" + _lastPreflightPath
                    : "运行前检查失败，实验已被拦截。";
            }
            EditorGUILayout.HelpBox(_lastPreflightReport,
                _lastPreflightReport.StartsWith("PASSED")
                    ? MessageType.Info : MessageType.Warning);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(_windowMessage, MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        private void DrawSessionSelector(ExperimentRunner runner)
        {
            bool canChange = !runner.running && !EditorApplication.isCompiling;
            using (new EditorGUI.DisabledScope(!canChange))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    int current = _sessions.IndexOf(runner.sessionJson) + 1;
                    int selected = EditorGUILayout.Popup("受试者 Session", current, _sessionNames);
                    if (selected != current && selected > 0)
                        AssignSession(runner, _sessions[selected - 1]);

                    if (GUILayout.Button("刷新", GUILayout.Width(54f)))
                        RefreshSessions();
                }
            }

            if (!string.IsNullOrEmpty(runner.sessionJsonPath))
            {
                EditorGUILayout.HelpBox(
                    "当前设置了外部 sessionJsonPath，它的优先级高于上面的 Session。" +
                    "从下拉框重新选择一次会自动清除此路径。",
                    MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(!canChange))
            {
                TextAsset manuallySelected = (TextAsset)EditorGUILayout.ObjectField(
                    "或直接选择 JSON", runner.sessionJson, typeof(TextAsset), false);
                if (manuallySelected != runner.sessionJson)
                    AssignSession(runner, manuallySelected);
            }
        }

        private void DrawRunControls(ExperimentRunner runner)
        {
            bool hasSession = (!string.IsNullOrEmpty(runner.sessionJsonPath) &&
                               File.Exists(runner.sessionJsonPath)) || runner.sessionJson != null;
            bool practice = ExperimentPreflightGate.IsPracticeSession(runner);
            bool changingPlayMode = EditorApplication.isPlayingOrWillChangePlaymode &&
                                    !EditorApplication.isPlaying;
            bool canStart = hasSession && !runner.running &&
                            !EditorApplication.isCompiling && !changingPlayMode;

            Color oldBackground = GUI.backgroundColor;
            using (new EditorGUI.DisabledScope(!canStart))
            {
                if (practice)
                {
                    GUI.backgroundColor = new Color(0.55f, 0.9f, 0.68f);
                    if (GUILayout.Button("运行 Pairing 练习（12 个 trial）",
                                         GUILayout.Height(44f)))
                    {
                        ExperimentLaunchCoordinator.Request(
                            ExperimentLaunchCoordinator.LaunchAction.FullSession);
                        _windowMessage = EditorApplication.isPlaying
                            ? "正在启动练习 Session。"
                            : "正在进入 Play；场景准备好后会自动启动练习。";
                    }
                }
                else
                {
                    GUI.backgroundColor = new Color(0.65f, 0.82f, 1f);
                    if (GUILayout.Button("测试首个 Block", GUILayout.Height(40f)))
                    {
                        ExperimentLaunchCoordinator.Request(
                            ExperimentLaunchCoordinator.LaunchAction.FirstBlock);
                        _windowMessage = EditorApplication.isPlaying
                            ? "正在启动首个 Block 测试。"
                            : "正在进入 Play；场景准备好后会自动启动首个 Block。";
                    }

                    GUI.backgroundColor = new Color(0.65f, 0.9f, 0.68f);
                    if (GUILayout.Button("运行完整 Session", GUILayout.Height(40f)))
                    {
                        ExperimentLaunchCoordinator.Request(
                            ExperimentLaunchCoordinator.LaunchAction.FullSession);
                        _windowMessage = EditorApplication.isPlaying
                            ? "正在启动完整 Session。"
                            : "正在进入 Play；场景准备好后会自动启动完整 Session。";
                    }
                }
            }

            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || !runner.running))
            {
                GUI.backgroundColor = new Color(1f, 0.82f, 0.48f);
                if (!runner.paused && !runner.pauseRequested &&
                    GUILayout.Button("当前呈现结束后安全暂停", GUILayout.Height(34f)))
                {
                    runner.RequestPause();
                    _windowMessage = "暂停请求已收到；当前物体呈现结束后会进入暂停。";
                }

                if (runner.paused && !runner.waitingForBlockRating &&
                    GUILayout.Button("继续当前实验", GUILayout.Height(34f)))
                {
                    runner.ResumeSession();
                    _windowMessage = "实验继续。";
                }
            }

            if (runner.waitingForBlockRating)
            {
                GUI.backgroundColor = new Color(0.55f, 0.9f, 1f);
                EditorGUILayout.HelpBox(
                    "Block " + (runner.completedBlockIndex + 1) +
                    " 已结束。请在电脑上询问并记录受试者的心理努力评分。\n" +
                    "1 = 非常轻松，7 = 非常费力。",
                    MessageType.Info);
                _effortRating = EditorGUILayout.IntSlider(
                    "心理努力评分", _effortRating, 1, 7);
                _blockNote = EditorGUILayout.TextField("备注（可留空）", _blockNote);
                if (GUILayout.Button("保存评分并继续", GUILayout.Height(42f)))
                {
                    if (runner.SubmitBlockRatingAndContinue(_effortRating, _blockNote))
                    {
                        _windowMessage = "评分已保存，正在继续。";
                        _blockNote = "";
                    }
                }
            }

            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || !runner.running))
            {
                GUI.backgroundColor = new Color(1f, 0.72f, 0.65f);
                if (GUILayout.Button("停止当前实验", GUILayout.Height(34f)))
                {
                    runner.StopSession();
                    _windowMessage = "实验已停止；此前已经写入的记录仍然保留。";
                }

                if (GUILayout.Button("从头重新开始当前 Session"))
                {
                    bool confirmed = EditorUtility.DisplayDialog(
                        "重新开始 Session",
                        "当前运行会停止并从 Block 1 重新开始。已经写入的部分数据会保留在旧日志中。",
                        "重新开始", "取消");
                    if (confirmed)
                    {
                        runner.RestartSession();
                        _windowMessage = "已经创建新日志并从头重新开始。";
                    }
                }
            }

            GUI.backgroundColor = oldBackground;

            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || runner.running))
            {
                if (GUILayout.Button("退出 Play 模式"))
                    EditorApplication.isPlaying = false;
            }

            if (!hasSession)
                EditorGUILayout.HelpBox("必须先选择一个 session JSON。", MessageType.Warning);

            if (!runner.running && runner.IsPracticeSession && runner.PracticeScored > 0)
            {
                EditorGUILayout.HelpBox(
                    "练习结果：" + runner.PracticeCorrect + " / " +
                    runner.PracticeScored + "（" +
                    runner.PracticeAccuracyPercent.ToString("F0") + "%）\n" +
                    (runner.PracticePassed ? "已达到建议的 80% 标准。"
                                           : "建议重新练习后再进入正式实验。"),
                    runner.PracticePassed ? MessageType.Info : MessageType.Warning);
            }
        }

        private void AssignSession(ExperimentRunner runner, TextAsset session)
        {
            if (runner == null || runner.running) return;

            if (!EditorApplication.isPlaying) Undo.RecordObject(runner, "Select StimGen Session");
            runner.sessionJson = session;
            runner.sessionJsonPath = string.Empty;

            if (session != null)
            {
                try
                {
                    SessionPlan plan = JsonUtility.FromJson<SessionPlan>(session.text);
                    if (plan != null && !string.IsNullOrEmpty(plan.participantId))
                        runner.participantId = plan.participantId;
                    bool practice = plan != null && string.Equals(
                        plan.participantId, PracticeSessionFactory.PracticeParticipantId,
                        StringComparison.OrdinalIgnoreCase);
                    runner.practiceMode = practice;
                    runner.feedbackMode = practice
                        ? ResponseFeedbackMode.CorrectnessPracticeOnly
                        : ResponseFeedbackMode.RecordedOnly;
                    runner.showTimeoutFeedback = true;
                    runner.feedbackFontSize = Mathf.Max(120, runner.feedbackFontSize);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[StimGen] 无法读取所选 Session：" + exception.Message);
                }
            }

            if (!EditorApplication.isPlaying)
            {
                EditorUtility.SetDirty(runner);
                if (runner.gameObject.scene.IsValid())
                    EditorSceneManager.MarkSceneDirty(runner.gameObject.scene);
            }

            _windowMessage = session == null
                ? "尚未选择 Session。"
                : "已选择 " + session.name + "；受试者编号为 " + runner.participantId + "。";
            _lastPreflightReport = "尚未检查。";
            _lastPreflightPath = "";
        }

        private void RefreshSessions()
        {
            PracticeSessionAssetUtility.EnsureCurrent();
            _sessions.Clear();
            string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { "Assets/StimulusSets" });
            var paths = new List<string>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileName(path);
                bool formalSession = fileName.StartsWith(
                    "session_", StringComparison.OrdinalIgnoreCase);
                bool practiceSession = fileName.Equals(
                    PracticeSessionAssetUtility.FileName,
                    StringComparison.OrdinalIgnoreCase);
                if ((formalSession || practiceSession) &&
                    fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    paths.Add(path);
            }
            paths.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string path in paths)
            {
                TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (asset != null) _sessions.Add(asset);
            }

            _sessionNames = new string[_sessions.Count + 1];
            _sessionNames[0] = "— 请选择 —";
            for (int i = 0; i < _sessions.Count; i++)
                _sessionNames[i + 1] = _sessions[i].name == "practice_session"
                    ? "PRACTICE — 12 Pairing trials（练习）"
                    : _sessions[i].name;
        }

        private static string UnityStateLabel()
        {
            if (EditorApplication.isCompiling) return "正在编译";
            if (EditorApplication.isPlaying) return "Play 模式";
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "正在进入 Play";
            return "编辑模式（点击启动按钮会自动进入 Play）";
        }
    }

    internal static class PracticeSessionAssetUtility
    {
        internal const string FileName = "practice_session.json";
        private const string BankPath = "Assets/StimulusSets/stimulus_bank.json";
        private const string PracticePath = "Assets/StimulusSets/practice_session.json";

        [InitializeOnLoadMethod]
        private static void InitializeAfterScriptsReload()
        {
            EditorApplication.delayCall += EnsureCurrent;
        }

        internal static void EnsureCurrent()
        {
            TextAsset bankAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(BankPath);
            if (bankAsset == null) return;

            StimulusBank bank = JsonUtility.FromJson<StimulusBank>(bankAsset.text);
            if (bank == null) return;

            bool regenerate = true;
            TextAsset existing = AssetDatabase.LoadAssetAtPath<TextAsset>(PracticePath);
            if (existing != null)
            {
                try
                {
                    SessionPlan current = JsonUtility.FromJson<SessionPlan>(existing.text);
                    string report;
                     regenerate = current == null || current.bankId != bank.generatedUtc ||
                                  current.taskProtocolVersion != ExperimentDesign.TaskProtocolVersion ||
                                  current.rotationProtocolVersion !=
                                     ExperimentDesign.RotationProtocolVersion ||
                                 !PracticeSessionFactory.Validate(current, out report);
                }
                catch
                {
                    regenerate = true;
                }
            }

            if (!regenerate) return;

            SessionPlan practice = PracticeSessionFactory.Build(
                bank, PracticeSessionFactory.DefaultScoredTrials);
            string validationReport;
            if (!PracticeSessionFactory.Validate(practice, out validationReport))
            {
                Debug.LogError("[StimGen] 练习 Session 生成失败：" + validationReport);
                return;
            }

            File.WriteAllText(PracticePath, JsonUtility.ToJson(practice, true),
                              new UTF8Encoding(true));
            AssetDatabase.ImportAsset(PracticePath, ImportAssetOptions.ForceUpdate);
            Debug.Log("[StimGen] 已生成独立练习 Session：" + validationReport);
        }
    }

    internal static class ExperimentPreflightGate
    {
        private const string BankPath = "Assets/StimulusSets/stimulus_bank.json";

        internal static bool IsPracticeSession(ExperimentRunner runner)
        {
            if (runner == null) return false;
            if (string.Equals(runner.participantId,
                    PracticeSessionFactory.PracticeParticipantId,
                    StringComparison.OrdinalIgnoreCase)) return true;
            return runner.sessionJson != null && string.Equals(
                runner.sessionJson.name, "practice_session",
                StringComparison.OrdinalIgnoreCase);
        }

        internal static bool ValidateAndSave(ExperimentRunner runner,
                                             out string summary,
                                             out string reportPath)
        {
            reportPath = "";
            SessionPlan plan;
            string readError;
            if (!TryReadPlan(runner, out plan, out readError))
            {
                summary = "FAILED：" + readError;
                return false;
            }

            bool practice = string.Equals(plan.participantId,
                PracticeSessionFactory.PracticeParticipantId,
                StringComparison.OrdinalIgnoreCase);
            bool passed;
            string detail;

            if (practice)
            {
                passed = PracticeSessionFactory.Validate(plan, out detail);
            }
            else
            {
                TextAsset bankAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(BankPath);
                if (bankAsset == null)
                {
                    summary = "FAILED：找不到 " + BankPath;
                    return false;
                }

                StimulusBank bank = JsonUtility.FromJson<StimulusBank>(bankAsset.text);
                PreflightResult result = PreflightValidator.Validate(plan, bank);
                passed = result.passed;
                detail = result.ToString();
            }

            if (runner.referenceDuration <= 0f || runner.comparisonDuration <= 0f)
            {
                passed = false;
                detail += "\nERROR：Reference 和 Comparison 的呈现时长都必须大于 0 秒。";
            }
            detail += "\nPairTimingSeconds: Reference=" +
                      runner.referenceDuration.ToString("F3") +
                      ", Comparison=" + runner.comparisonDuration.ToString("F3") +
                      ", Pair=" + (runner.referenceDuration + runner.comparisonDuration).ToString("F3");

            summary = (passed ? "PASSED" : "FAILED") + "：" +
                      (practice ? "Practice" : "Formal") + " / " +
                      plan.participantId + " / " + plan.ScoredTrialCount() +
                      " scored trials。";

            var report = new StringBuilder();
            report.AppendLine("StimGen Preflight Report");
            report.AppendLine("GeneratedUTC: " + DateTime.UtcNow.ToString("O"));
            report.AppendLine("Result: " + (passed ? "PASSED" : "FAILED"));
            report.AppendLine("Mode: " + (practice ? "Practice" : "Formal"));
            report.AppendLine("ParticipantID: " + plan.participantId);
            report.AppendLine("SessionSource: " + runner.SessionLabel);
            report.AppendLine("RotationProtocol: " + plan.rotationProtocolVersion);
            report.AppendLine("ReferenceDurationSeconds: " +
                              runner.referenceDuration.ToString("F3"));
            report.AppendLine("ComparisonDurationSeconds: " +
                              runner.comparisonDuration.ToString("F3"));
            report.AppendLine("PairDurationSeconds: " +
                              (runner.referenceDuration + runner.comparisonDuration).ToString("F3"));
            report.AppendLine("FeedbackPolicy: " + (practice
                ? "Immediate Correct/Incorrect/NoResponse"
                : "Immediate neutral Recorded; explicit NoResponse; no correctness"));
            report.AppendLine();
            report.AppendLine(detail);

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string reportDirectory = Path.Combine(projectRoot, "PreflightReports");
            Directory.CreateDirectory(reportDirectory);
            string safeSession = string.IsNullOrEmpty(runner.SessionLabel)
                ? "unknown" : runner.SessionLabel.Replace(' ', '_');
            reportPath = Path.Combine(reportDirectory,
                safeSession + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") +
                "_preflight.txt");
            File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(true));

            summary += passed
                ? " 检查报告已保存。"
                : " 请查看检查报告并修复后再启动。";
            return passed;
        }

        private static bool TryReadPlan(ExperimentRunner runner, out SessionPlan plan,
                                        out string error)
        {
            plan = null;
            error = "";
            if (runner == null)
            {
                error = "ExperimentRunner 不存在";
                return false;
            }

            string json = null;
            if (!string.IsNullOrEmpty(runner.sessionJsonPath) &&
                File.Exists(runner.sessionJsonPath))
                json = File.ReadAllText(runner.sessionJsonPath);
            else if (runner.sessionJson != null)
                json = runner.sessionJson.text;

            if (string.IsNullOrEmpty(json))
            {
                error = "没有选择 Session JSON";
                return false;
            }

            try
            {
                plan = JsonUtility.FromJson<SessionPlan>(json);
                if (plan == null || plan.blocks == null || plan.blocks.Count == 0)
                {
                    error = "Session 内容为空或没有 Block";
                    return false;
                }
            }
            catch (Exception exception)
            {
                error = "无法解析 Session：" + exception.Message;
                return false;
            }
            return true;
        }
    }

    [InitializeOnLoad]
    internal static class ExperimentLaunchCoordinator
    {
        internal enum LaunchAction
        {
            None = 0,
            FirstBlock = 1,
            FullSession = 2,
        }

        private const string PendingActionKey = "StimGen.ExperimentLauncher.PendingAction";
        private static double _executeAfter;

        static ExperimentLaunchCoordinator()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        internal static ExperimentRunner FindRunner()
        {
            return UnityEngine.Object.FindFirstObjectByType<ExperimentRunner>(
                FindObjectsInactive.Include);
        }

        internal static void Request(LaunchAction action)
        {
            ExperimentRunner runner = FindRunner();
            if (runner == null)
            {
                Debug.LogError("[StimGen] 当前场景里没有 ExperimentRunner。");
                return;
            }

            string preflightSummary;
            string preflightPath;
            if (!ExperimentPreflightGate.ValidateAndSave(
                    runner, out preflightSummary, out preflightPath))
            {
                Debug.LogError("[StimGen] " + preflightSummary +
                               " 报告：" + preflightPath);
                EditorUtility.DisplayDialog("StimGen 运行前检查失败",
                    preflightSummary + "\n\n报告：" + preflightPath, "确定");
                return;
            }
            Debug.Log("[StimGen] " + preflightSummary + " 报告：" + preflightPath);

            if (EditorApplication.isPlaying)
            {
                Execute(runner, action);
                return;
            }

            SessionState.SetInt(PendingActionKey, (int)action);
            _executeAfter = 0d;
            EditorApplication.isPlaying = true;
        }

        private static void Update()
        {
            LaunchAction pending = (LaunchAction)SessionState.GetInt(
                PendingActionKey, (int)LaunchAction.None);
            if (pending == LaunchAction.None || !EditorApplication.isPlaying) return;

            if (_executeAfter <= 0d)
            {
                _executeAfter = EditorApplication.timeSinceStartup + 0.6d;
                return;
            }
            if (EditorApplication.timeSinceStartup < _executeAfter) return;

            ExperimentRunner runner = FindRunner();
            if (runner == null) return;

            SessionState.EraseInt(PendingActionKey);
            _executeAfter = 0d;
            Execute(runner, pending);
        }

        private static void Execute(ExperimentRunner runner, LaunchAction action)
        {
            if (runner == null || runner.running) return;
            if (action == LaunchAction.FirstBlock) runner.RunFirstBlockOnly();
            else if (action == LaunchAction.FullSession) runner.RunSession();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
                _executeAfter = 0d;
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.EraseInt(PendingActionKey);
                _executeAfter = 0d;
            }
        }
    }
}
