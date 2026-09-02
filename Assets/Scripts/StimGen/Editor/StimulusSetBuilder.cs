using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StimGen.EditorTools
{
    /// <summary>
    /// StimGen 建库与排程工具。菜单：Tools ▸ StimGen ▸ Builder
    ///
    /// 三个阶段，必须按顺序做：
    ///   ① 建刺激库   生成家族 → 检查 → 算全物体两两配对矩阵 → 冻结成 bank.json
    ///   ② 排会话     用配对矩阵为每个参与者排 4 个 block 的完整序列
    ///   ③ 运行前检查 任何不平衡或不合法的序列在这里被拦下
    /// </summary>
    public class StimulusSetBuilder : EditorWindow
    {
        [Header("物体构成")]
        public int copiesPerShape = 1;

        [Header("外观")]
        public Color partColor = Color.white;
        public float partSmoothness = 0.15f;

        [Header("建库")]
        public BankBuildSettings bankSettings = new BankBuildSettings();
        public int masterSeed = 20260823;
        public bool runVisualChecks = true;

        [Header("排程")]
        public int firstParticipantNumber = 1;
        public int participantCount = 24;
        public string participantIdPrefix = "P";
        public TrialGeneratorSettings trialSettings = new TrialGeneratorSettings();

        public ValidationSettings validation = new ValidationSettings();
        public VisualCheckSettings visual = new VisualCheckSettings();

        [Header("输出")]
        public string outputFolder = "Assets/StimulusSets";

        private Vector2 _scroll;
        private string _status = "尚未生成。";
        private StimulusBank _bank;

        private const string PreviewRootName = "StimGen Preview";
        private const string MaterialPath = "Assets/Materials/StimulusPart.mat";
        private const string BankFileName = "stimulus_bank.json";

        [MenuItem("Tools/StimGen/Builder")]
        public static void Open()
        {
            GetWindow<StimulusSetBuilder>(false, "StimGen Builder", true).minSize =
                new Vector2(440f, 560f);
        }

        /// <summary>
        /// 用最终计划的默认参数一次性生成正式刺激库和 P001-P024 session。
        /// 也可在 Unity 菜单中执行，或用 -executeMethod 调用进行可复现生成。
        /// </summary>
        [MenuItem("Tools/StimGen/Generate Final Bank + 24 Sessions")]
        public static void GenerateFinalAssets()
        {
            StimulusSetBuilder builder = CreateInstance<StimulusSetBuilder>();
            builder.masterSeed = 20260823;
            builder.runVisualChecks = true;
            // The active 2 x 2 plan defines High/Low structurally (2/3 and
            // 0/3 retained relations). A pilot must establish the perceptual
            // gradient, so the automated gate only removes gross visibility
            // and view-consistency failures here.
            builder.visual.enforceLevelIouBands = false;
            builder.visual.maxIouSpread = 0.50f;
            builder.firstParticipantNumber = 1;
            builder.participantCount = 24;
            builder.participantIdPrefix = "P";
            builder.outputFolder = "Assets/StimulusSets";
            try
            {
                builder.BuildBank();
                builder.BuildSessions();
                PracticeSessionAssetUtility.EnsureCurrent();
            }
            finally
            {
                DestroyImmediate(builder);
            }
        }

        /// <summary>
        /// 保留已经冻结的刺激库，只按当前 ExperimentDesign 重新生成 P001-P024。
        /// 用于旋转协议或排程规则改变，但物体几何与配对矩阵不改变的情况。
        /// </summary>
        [MenuItem("Tools/StimGen/Regenerate 24 Sessions From Existing Bank")]
        public static void RegenerateFinalSessionsFromExistingBank()
        {
            StimulusSetBuilder builder = CreateInstance<StimulusSetBuilder>();
            builder.masterSeed = 20260823;
            builder.firstParticipantNumber = 1;
            builder.participantCount = 24;
            builder.participantIdPrefix = "P";
            builder.outputFolder = "Assets/StimulusSets";
            try
            {
                builder.BuildSessions();
                PracticeSessionAssetUtility.EnsureCurrent();
            }
            finally
            {
                DestroyImmediate(builder);
            }
        }

        /// <summary>生成不做轮廓检查的候选库，用于诊断几何生成是否正常。</summary>
        [MenuItem("Tools/StimGen/Generate Pure Geometry Diagnostic")]
        public static void GeneratePureGeometryDiagnostic()
        {
            StimulusSetBuilder builder = CreateInstance<StimulusSetBuilder>();
            builder.runVisualChecks = false;
            builder.participantCount = 0;
            builder.outputFolder = "Assets/StimulusSets";
            try { builder.BuildBank(); }
            finally { DestroyImmediate(builder); }
        }

        /// <summary>
        /// 用诊断阶段观察到的宽阈值生成候选库。此命令只用于评估是否能形成可检查的
        /// 视觉分布，不应在 pilot 前直接替代最终阈值。
        /// </summary>
        [MenuItem("Tools/StimGen/Generate Broad-Threshold Candidate")]
        public static void GenerateBroadThresholdCandidate()
        {
            StimulusSetBuilder builder = CreateInstance<StimulusSetBuilder>();
            builder.runVisualChecks = true;
            builder.participantCount = 24;
            builder.visual.highMinIou = 0.25f;
            builder.visual.lowMaxIou = 0.80f;
            builder.visual.maxIouSpread = 0.50f;
            builder.outputFolder = "Assets/StimulusSets";
            try
            {
                builder.BuildBank();
                builder.BuildSessions();
            }
            finally { DestroyImmediate(builder); }
        }

        /// <summary>输出一个基础物体和 High/Low 变体的视觉检查细节，帮助校准 IoU/遮挡阈值。</summary>
        [MenuItem("Tools/StimGen/Diagnose Visual Defaults")]
        public static void DiagnoseVisualDefaults()
        {
            StimulusSetBuilder builder = CreateInstance<StimulusSetBuilder>();
            try
            {
                ObjectDefinition baseDef = null;
                int seed = builder.masterSeed;
                for (int i = 0; i < 100 && baseDef == null; i++)
                    baseDef = ObjectGenerator.Generate(seed++, builder.validation);

                if (baseDef == null)
                {
                    Debug.LogError("[StimGen] 视觉诊断：找不到几何合格的基础物体。");
                    return;
                }

                ViewCapture[] baseViews = SilhouetteAnalyzer.Capture(baseDef, builder.visual);
                Debug.Log("[StimGen] 视觉诊断基础物体：" +
                          SilhouetteAnalyzer.Evaluate(baseViews, null,
                              SimilarityLevel.Identical, builder.visual));

                SimilarityLevel[] levels = ExperimentDesign.ActiveSimilarityLevels;
                for (int i = 0; i < levels.Length; i++)
                {
                    ObjectDefinition variant = VariantGenerator.Generate(baseDef, levels[i], seed++, builder.validation);
                    if (variant == null)
                    {
                        Debug.LogWarning("[StimGen] 视觉诊断：" + levels[i] + " 变体生成失败。");
                        continue;
                    }
                    ViewCapture[] views = SilhouetteAnalyzer.Capture(variant, builder.visual);
                    Debug.Log("[StimGen] 视觉诊断 " + levels[i] + "：" +
                              SilhouetteAnalyzer.Evaluate(views, baseViews, levels[i], builder.visual));
                }
            }
            finally { DestroyImmediate(builder); }
        }

        /// <summary>在纯几何候选库上估计各结构等级的轮廓 IoU 分布。</summary>
        [MenuItem("Tools/StimGen/Analyze Visual Distribution")]
        public static void AnalyzeVisualDistribution()
        {
            StimulusSetBuilder builder = CreateInstance<StimulusSetBuilder>();
            try
            {
                string path = Path.Combine(builder.outputFolder, "stimulus_bank.json");
                if (!File.Exists(path))
                {
                    Debug.LogError("[StimGen] 视觉分布分析：找不到 " + path + "，请先生成纯几何候选库。");
                    return;
                }

                StimulusBank bank = JsonUtility.FromJson<StimulusBank>(File.ReadAllText(path));
                if (bank == null || bank.objects.Count == 0)
                {
                    Debug.LogError("[StimGen] 视觉分布分析：题库为空。");
                    return;
                }

                // 诊断用较低分辨率，先看分布是否有可分离的区间；正式建库仍使用 Builder 中的分辨率。
                builder.visual.resolution = 128;
                var views = new Dictionary<string, ViewCapture[]>();
                for (int i = 0; i < bank.objects.Count; i++)
                {
                    ObjectDefinition def = bank.objects[i];
                    views[def.objectId] = SilhouetteAnalyzer.Capture(def, builder.visual);
                }

                var samples = new Dictionary<SimilarityLevel, List<float>>
                {
                    { SimilarityLevel.High, new List<float>() },
                    { SimilarityLevel.Low, new List<float>() },
                };
                var spreads = new Dictionary<SimilarityLevel, List<float>>
                {
                    { SimilarityLevel.High, new List<float>() },
                    { SimilarityLevel.Low, new List<float>() },
                };
                const int maxSamplesPerLevel = 500;

                for (int i = 0; i < bank.objects.Count; i++)
                {
                    for (int j = i + 1; j < bank.objects.Count; j++)
                    {
                        PairClass pc = PairClassifier.ClassifyStructural(bank.objects[i], bank.objects[j]);
                        if (pc == PairClass.Invalid || pc == PairClass.Target) continue;
                        SimilarityLevel level = PairClassifier.ToLevel(pc);
                        if (samples[level].Count >= maxSamplesPerLevel) continue;

                        ViewCapture[] a = views[bank.objects[i].objectId];
                        ViewCapture[] b = views[bank.objects[j].objectId];
                        float min = 1f, max = 0f;
                        for (int angle = 0; angle < a.Length; angle++)
                        {
                            float iou = SilhouetteAnalyzer.IoU(a[angle].silhouette, b[angle].silhouette);
                            min = Mathf.Min(min, iou);
                            max = Mathf.Max(max, iou);
                        }
                        samples[level].Add(min);
                        spreads[level].Add(max - min);
                    }

                    if (samples[SimilarityLevel.High].Count >= maxSamplesPerLevel &&
                        samples[SimilarityLevel.Low].Count >= maxSamplesPerLevel)
                        break;
                }

                SimilarityLevel[] levels = ExperimentDesign.ActiveSimilarityLevels;
                for (int i = 0; i < levels.Length; i++)
                {
                    List<float> values = samples[levels[i]];
                    List<float> spread = spreads[levels[i]];
                    values.Sort();
                    spread.Sort();
                    Debug.Log("[StimGen] IoU 分布 " + levels[i] +
                              " n=" + values.Count +
                              " minIoU q10/q50/q90=" + Quantile(values, 0.10f).ToString("F3") + "/" +
                              Quantile(values, 0.50f).ToString("F3") + "/" + Quantile(values, 0.90f).ToString("F3") +
                              " spread q50/q90=" + Quantile(spread, 0.50f).ToString("F3") + "/" +
                              Quantile(spread, 0.90f).ToString("F3"));
                }
            }
            finally { DestroyImmediate(builder); }
        }

        private static float Quantile(List<float> sorted, float q)
        {
            if (sorted == null || sorted.Count == 0) return 0f;
            float position = Mathf.Clamp01(q) * (sorted.Count - 1);
            int lo = Mathf.FloorToInt(position);
            int hi = Mathf.Min(sorted.Count - 1, lo + 1);
            return Mathf.Lerp(sorted[lo], sorted[hi], position - lo);
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawDesignSummary();
            EditorGUILayout.Space();
            DrawComposition();
            EditorGUILayout.Space();
            DrawBankSettings();
            EditorGUILayout.Space();
            DrawCriteria();
            EditorGUILayout.Space();
            DrawScheduling();
            EditorGUILayout.Space();

            outputFolder = EditorGUILayout.TextField(GC("输出目录",
                "bank / session JSON 存放位置。放在 Assets 下面，session 才能作为 TextAsset " +
                "拖进 ExperimentRunner。"), outputFolder);

            EditorGUILayout.Space();
            if (GUILayout.Button("创建 / 刷新零件材质")) CreatePartMaterial();
            if (GUILayout.Button("在场景中预览 12 个样例")) PreviewSamples(12);
            if (GUILayout.Button("清除场景中的预览")) ClearPreview();

            EditorGUILayout.Space();
            GUI.backgroundColor = new Color(0.6f, 0.85f, 0.6f);
            if (GUILayout.Button("① 建刺激库", GUILayout.Height(30f))) BuildBank();
            GUI.backgroundColor = new Color(0.6f, 0.75f, 0.95f);
            if (GUILayout.Button("② 排会话 + 运行前检查", GUILayout.Height(30f))) BuildSessions();
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(_status, MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------ UI 分区

        private void DrawDesignSummary()
        {
            EditorGUILayout.LabelField("实验设计（写死在 ExperimentDesign.cs，不在此处改）",
                                       EditorStyles.boldLabel);
            EditorGUILayout.LabelField("  " + ExperimentDesign.BlocksPerParticipant + " blocks × " +
                ExperimentDesign.ScoredTrialsPerBlock + " scored trials = " +
                ExperimentDesign.BlocksPerParticipant * ExperimentDesign.ScoredTrialsPerBlock +
                " trials/人", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  segment 长度 " +
                string.Join("/", Array.ConvertAll(ExperimentDesign.SegmentLengths, x => x.ToString())) +
                "，每 block " + (ExperimentDesign.SegmentCount - 1) + " 个边界，" +
                "每人 " + ExperimentDesign.BlocksPerParticipant * (ExperimentDesign.SegmentCount - 1) +
                " 个（8 真 + 4 No-op）", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  X轴 RotationDelta：0° / 180°；" +
                "Y轴观看动画由 ExperimentRunner 统一控制", EditorStyles.miniLabel);
        }

        private void DrawComposition()
        {
            EditorGUILayout.LabelField("物体构成", EditorStyles.boldLabel);
            copiesPerShape = EditorGUILayout.IntSlider(GC("每种形状重复",
                "每种形状放几个。计划规定为 1（方块/圆柱/胶囊/椭球各一个），4 零件 3 条空间关系。\n\n" +
                "改成 2 会变成 8 零件 7 条关系，相似度分级会自动跟着变，但那不是本方案。"),
                copiesPerShape, 1, 3);
            ApplyConfig();
            EditorGUILayout.LabelField(" ", StimConfig.Describe() + "　PartSetID=" +
                                       StimulusBankBuilder.PartSetId());

            partColor = EditorGUILayout.ColorField(GC("零件颜色",
                "所有零件共用的颜色。纯白 + 强光会把曲面明暗冲掉，而明暗是唯一的形状线索。" +
                "改完要点一次「创建 / 刷新零件材质」。"), partColor);
            partSmoothness = EditorGUILayout.Slider(GC("粗糙度 Smoothness",
                "0 = 哑光。镜面高光会盖住形状，建议不超过 0.2。"), partSmoothness, 0f, 1f);
        }

        private void DrawBankSettings()
        {
            EditorGUILayout.LabelField("① 刺激库", EditorStyles.boldLabel);
            bankSettings.formalFamilies = EditorGUILayout.IntField(GC("正式家族数",
                "计划建议 24 个。每个家族 = 1 个基准物体 + High/Low 变体。"),
                bankSettings.formalFamilies);
            bankSettings.practiceFamilies = EditorGUILayout.IntField(GC("练习家族数",
                "计划建议 4–6 个。练习物体单独存放，绝不会进入正式实验。"),
                bankSettings.practiceFamilies);
            bankSettings.variantsPerLevel = EditorGUILayout.IntSlider(GC("每级变体数",
                "每个家族在 High/Low 各生成几个变体。\n\n" +
                "1 → 24 家族 × 3 = 72 个正式物体（计划的基准规模）。\n" +
                "如果配对覆盖度不足，提高这个数比依赖自动补充更可控。"),
                bankSettings.variantsPerLevel, 1, 3);
            bankSettings.minCandidatesPerLevel = EditorGUILayout.IntSlider(GC("每级最少候选数",
                "每个物体在每个相似度等级下至少要有几个合法候选。\n\n" +
                "计划要求 ≥2：每个物体都可能在 Pairing trial 中作为 Reference，" +
                "候选不足的物体不会进入正式排程。"),
                bankSettings.minCandidatesPerLevel, 1, 6);
            bankSettings.maxTopUpObjects = EditorGUILayout.IntField(GC("最多补充物体数",
                "覆盖度不足时自动补充生成的上限。补出来的物体会加入库并进入配对矩阵。"),
                bankSettings.maxTopUpObjects);

            masterSeed = EditorGUILayout.IntField(GC("主随机种子",
                "同种子 + 同参数 = 完全相同的刺激库。正式实验必须记录这个数。"), masterSeed);
            runVisualChecks = EditorGUILayout.Toggle(GC("执行轮廓/遮挡检查",
                "关：只做几何检查，快，用于确认管线通不通。\n" +
                "开：每个物体渲染建库用的 Y轴 0°/45°/90° 观察视图，检查零件遮挡，并且**配对矩阵的每一对**都要过轮廓区间。它独立于正式任务的 X轴条件。\n\n" +
                "正式建库必须开。"), runVisualChecks);
        }

        private void DrawCriteria()
        {
            EditorGUILayout.LabelField("几何合格标准", EditorStyles.boldLabel);
            validation.maxOverlapRatio = EditorGUILayout.Slider(GC("非相邻最大重叠",
                "没有连接关系的两个零件允许穿模多少（占单个零件体积）。"),
                validation.maxOverlapRatio, 0f, 0.1f);
            validation.minBoundingRadius = EditorGUILayout.FloatField(GC("最小包围半径",
                "整体大小窗口下限。这是「整体尺寸、中心点和视觉大小统一」的落地方式。"),
                validation.minBoundingRadius);
            validation.maxBoundingRadius = EditorGUILayout.FloatField(GC("最大包围半径",
                "整体大小窗口上限。4 零件实测自然分布约 1.19–1.44。"),
                validation.maxBoundingRadius);
            validation.maxAspectRatio = EditorGUILayout.Slider(GC("最大长宽比",
                "防止长成棍子。棍状物体转 90° 轮廓变化极大，会把旋转和相似度混在一起。"),
                validation.maxAspectRatio, 1.2f, 5f);
            validation.maxSymmetryScore = EditorGUILayout.Slider(GC("最大对称得分",
                "挡对称物体。注意：每种形状只有 1 个时这个检查几乎失效，" +
                "真正起作用的是下面的中心跨度。"), validation.maxSymmetryScore, 0.3f, 1f);
            validation.minCenterSpread = EditorGUILayout.Slider(GC("最小中心跨度（防共面）",
                "挡零件全排在一个平面上的扁片物体——这种东西转 45° 会完全变样。"),
                validation.minCenterSpread, 0f, 0.8f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("视觉检查", EditorStyles.boldLabel);
            visual.resolution = EditorGUILayout.IntPopup(GC("截图分辨率",
                "只影响检查精度和速度，不影响被试看到的画面。"), visual.resolution,
                new[] { new GUIContent("128"), new GUIContent("256"), new GUIContent("512") },
                new[] { 128, 256, 512 });
            visual.orthographicSize = EditorGUILayout.FloatField(GC("正交相机半高",
                "离屏检查相机的取景范围，必须大于最大包围半径。"), visual.orthographicSize);
            EditorGUILayout.LabelField("  IoU = 两个轮廓的交集面积 ÷ 并集面积", EditorStyles.miniLabel);
            visual.enforceLevelIouBands = EditorGUILayout.Toggle(
                new GUIContent("Enforce level IoU bands",
                    "关闭时只做遮挡/视角一致性淘汰；High/Low 的感知难度留给 pilot 校准。"),
                visual.enforceLevelIouBands);
            visual.highMinIou = EditorGUILayout.Slider(GC("High IoU ≥",
                "改 1 条关系但看起来已经差很多 → 淘汰。"), visual.highMinIou, 0.5f, 0.99f);
            visual.lowMaxIou = EditorGUILayout.Slider(GC("Low IoU ≤",
                "最容易卡住的一根。等体积零件剪影天然重合度高，压不下去就往上调。"),
                visual.lowMaxIou, 0.1f, 0.8f);
            visual.maxIouSpread = EditorGUILayout.Slider(GC("三角度最大跨度",
                "建库时 Y轴 0°/45°/90° 三个观察视图的 IoU 极差上限。超过说明差别只在某个角度看得见。"),
                visual.maxIouSpread, 0.02f, 0.5f);
            visual.minPartVisibleRatio = EditorGUILayout.Slider(GC("零件最小可见比例",
                "每个零件在每个角度下的最小可见像素占比。这是「没有完全遮挡」的落地方式。"),
                visual.minPartVisibleRatio, 0f, 0.05f);
        }

        private void DrawScheduling()
        {
            EditorGUILayout.LabelField("② 会话排程", EditorStyles.boldLabel);
            firstParticipantNumber = EditorGUILayout.IntField(GC("起始参与者编号",
                "编号决定 block 顺序（循环拉丁方）和左右手按键映射，必须真实对应被试。"),
                firstParticipantNumber);
            participantCount = EditorGUILayout.IntField(GC("生成几个参与者",
                "计划目标 24 名有效参与者，建议招募 28–30。每人一个 session JSON。"),
                participantCount);
            participantIdPrefix = EditorGUILayout.TextField(GC("参与者 ID 前缀",
                "文件名与日志里的 ParticipantID，例如 P001。"), participantIdPrefix);

            trialSettings.maxConsecutiveTargets = EditorGUILayout.IntSlider(GC("最多连续 Target",
                "防止被试学会节奏。"), trialSettings.maxConsecutiveTargets, 1, 4);
            trialSettings.noRepeatWindow = EditorGUILayout.IntSlider(GC("模型不重复窗口",
                "同一个模型在这么多次呈现之内不得再次出现（计划中的 Target 除外）。"),
                trialSettings.noRepeatWindow, 2, 8);
            trialSettings.familyCooldown = EditorGUILayout.IntSlider(GC("家族冷却窗口",
                "同一家族的物体在这么多次呈现之内不得重复出现。0 = 不限制。"),
                trialSettings.familyCooldown, 0, 6);
        }

        // ------------------------------------------------------------------ 动作

        private void BuildBank()
        {
            ApplyConfig();
            EnsureSharedMaterial();

            string log;
            bool cancelled = false;
            StimulusBank bank;

            try
            {
                bank = StimulusBankBuilder.Build(bankSettings, validation, visual, runVisualChecks,
                    masterSeed,
                    (stage, fraction) =>
                    {
                        if (EditorUtility.DisplayCancelableProgressBar("建刺激库", stage, fraction))
                        {
                            cancelled = true;
                            return false;
                        }
                        return true;
                    },
                    out log);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            _bank = bank;
            Directory.CreateDirectory(outputFolder);
            string path = Path.Combine(outputFolder, BankFileName);
            File.WriteAllText(path, JsonUtility.ToJson(bank, true));
            AssetDatabase.Refresh();

            _status = (cancelled ? "已取消（结果仍已保存）。\n" : "") +
                      StimConfig.Describe() + "\n" + log + "\n已保存到 " + path;
            Debug.Log("[StimGen] " + _status);
        }

        private void BuildSessions()
        {
            ApplyConfig();

            StimulusBank bank = _bank ?? LoadBank();
            if (bank == null)
            {
                _status = "找不到刺激库，请先点①建库。";
                return;
            }
            bank.BuildIndex();

            Directory.CreateDirectory(outputFolder);
            var lines = new List<string>();
            int failed = 0, preflightFailed = 0;
            bool cancelled = false;

            try
            {
                for (int i = 0; i < participantCount; i++)
                {
                    int number = firstParticipantNumber + i;
                    string pid = participantIdPrefix + number.ToString("D3");

                    if (EditorUtility.DisplayCancelableProgressBar("排会话",
                            pid + "（" + (i + 1) + "/" + participantCount + "）",
                            i / (float)Mathf.Max(1, participantCount)))
                    {
                        cancelled = true;
                        break;
                    }

                    string error;
                    SessionPlan plan = TrialGenerator.BuildSession(bank, pid, number,
                        masterSeed + number, trialSettings, out error);

                    if (plan == null)
                    {
                        lines.Add(pid + "：生成失败 —— " + error);
                        failed++;
                        continue;
                    }

                    PreflightResult pre = PreflightValidator.Validate(plan, bank);
                    string path = Path.Combine(outputFolder, "session_" + pid + ".json");
                    File.WriteAllText(path, JsonUtility.ToJson(plan, true));

                    if (!pre.passed)
                    {
                        preflightFailed++;
                        lines.Add(pid + "：运行前检查未通过 —— " + string.Join("；", pre.errors.ToArray()));
                    }
                    else if (i == 0)
                    {
                        lines.Add(pid + "：通过。" + pre.ToString());
                    }
                    else
                    {
                        lines.Add(pid + "：通过（" + plan.ScoredTrialCount() + " trials，" +
                                  "block 顺序 " + FormatOrder(plan.blockOrder) + "）");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }

            _status = (cancelled ? "已取消。\n" : "") +
                      "生成 " + (participantCount - failed) + " 个会话，失败 " + failed +
                      "，运行前检查未通过 " + preflightFailed + "\n\n" +
                      string.Join("\n", lines.ToArray());
            Debug.Log("[StimGen] " + _status);
        }

        private static string FormatOrder(List<int> order)
        {
            var names = new List<string>();
            for (int i = 0; i < order.Count; i++)
                names.Add(ExperimentDesign.BlockSequenceIds[order[i]]);
            return string.Join("→", names.ToArray());
        }

        private StimulusBank LoadBank()
        {
            string path = Path.Combine(outputFolder, BankFileName);
            if (!File.Exists(path)) return null;
            var bank = JsonUtility.FromJson<StimulusBank>(File.ReadAllText(path));
            if (bank != null) bank.BuildIndex();
            return bank;
        }

        // ------------------------------------------------------------------ 预览与材质

        private void PreviewSamples(int count)
        {
            ApplyConfig();
            EnsureSharedMaterial();
            ClearPreview();

            var root = new GameObject(PreviewRootName);
            int seed = masterSeed;
            int placed = 0, columns = 4;
            float spacing = 3.2f;

            for (int i = 0; i < count * 8 && placed < count; i++)
            {
                ObjectDefinition def = ObjectGenerator.Generate(seed++, validation);
                if (def == null) continue;

                StimulusObject stim = ObjectAssembler.Build(def, root.transform);
                stim.transform.localPosition = new Vector3(
                    (placed % columns) * spacing, 0f, -(placed / columns) * spacing);
                placed++;
            }

            Selection.activeGameObject = root;
            _status = "已在场景中预览 " + placed + " 个物体（临时对象，不会保存）。";
        }

        private void ClearPreview()
        {
            GameObject existing = GameObject.Find(PreviewRootName);
            while (existing != null)
            {
                DestroyImmediate(existing);
                existing = GameObject.Find(PreviewRootName);
            }
        }

        private void CreatePartMaterial()
        {
            Directory.CreateDirectory("Assets/Materials");

            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                existing = new Material(shader);
                AssetDatabase.CreateAsset(existing, MaterialPath);
            }
            PartLibrary.PartColor = partColor;
            PartLibrary.PartSmoothness = partSmoothness;
            PartLibrary.ApplyPartColor(existing);
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();
            PartLibrary.SetSharedMaterial(existing);

            _status = "零件材质已就绪：" + MaterialPath +
                      "\n场景里已生成的预览不会自动变色，重新点一次预览即可。";
        }

        private void EnsureSharedMaterial()
        {
            PartLibrary.PartColor = partColor;
            PartLibrary.PartSmoothness = partSmoothness;

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat != null)
            {
                PartLibrary.ApplyPartColor(mat);
                PartLibrary.SetSharedMaterial(mat);
            }
        }

        private void ApplyConfig()
        {
            StimConfig.CopiesPerShape = Mathf.Max(1, copiesPerShape);
            PartLibrary.PartColor = partColor;
            PartLibrary.PartSmoothness = partSmoothness;
        }

        private static GUIContent GC(string label, string tooltip)
        {
            return new GUIContent(label, tooltip);
        }
    }
}
