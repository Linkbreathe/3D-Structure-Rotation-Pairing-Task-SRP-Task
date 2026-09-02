using System;
using System.Collections.Generic;
using UnityEngine;

namespace StimGen
{
    [Serializable]
    public class BankBuildSettings
    {
        [Tooltip("正式物体家族数")]
        public int formalFamilies = 24;

        [Tooltip("练习家族数。练习物体不得进入正式实验")]
        public int practiceFamilies = 5;

        [Tooltip("每个家族在每个相似度等级下生成几个变体")]
        public int variantsPerLevel = 1;

        [Tooltip("配对覆盖不足时，最多补充生成多少个物体")]
        public int maxTopUpObjects = 60;

        [Tooltip("每个物体每个等级至少需要的候选数")]
        public int minCandidatesPerLevel = 2;

        [Tooltip("生成一个家族最多尝试几次")]
        public int familyAttempts = 40;
    }

    /// <summary>批量生成过程中的进度回调。返回 false 表示用户取消。</summary>
    public delegate bool BankProgress(string stage, float fraction);

    /// <summary>
    /// 建库流程：
    ///   ① 生成家族（Base + High/Medium/Low 变体），每个成员单独过几何 + 遮挡检查
    ///   ② 给每个物体缓存 Y轴 0°/45°/90° 的建库观察轮廓（独立于正式任务的 X轴条件）
    ///   ③ 计算**全部正式物体两两之间**的配对类型（结构关系 + 轮廓双重验证）
    ///   ④ 覆盖度不足时补充生成，直到每个物体每级都有足够候选或达到上限
    ///
    /// 第 ③ 步保证 Pairing trial 中任意物体都可以作为 Reference，
    /// 所以只有"基准→变体"这一层关系是不够的。
    /// </summary>
    public static class StimulusBankBuilder
    {
        public static StimulusBank Build(BankBuildSettings bankSettings,
                                         ValidationSettings validation,
                                         VisualCheckSettings visual,
                                         bool runVisualChecks,
                                         int masterSeed,
                                         BankProgress progress,
                                         out string log)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Visual gate: " + (runVisualChecks
                ? (visual.enforceLevelIouBands
                    ? "occlusion + level IoU bands"
                    : "occlusion + view-consistency sanity; level labels remain structural and require pilot calibration")
                : "disabled"));
            var bank = new StimulusBank
            {
                generatedUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                masterSeed = masterSeed,
                partSetId = PartSetId(),
                partCount = StimConfig.PartCount,
            };

            int seed = masterSeed;
            var captures = new Dictionary<string, ViewCapture[]>();

            // ---------- ① 正式家族 ----------
            int familyRejected = 0;
            for (int f = 0; f < bankSettings.formalFamilies; f++)
            {
                if (progress != null &&
                    !progress("生成正式家族 " + (f + 1) + "/" + bankSettings.formalFamilies,
                              f / (float)bankSettings.formalFamilies * 0.45f))
                {
                    log = sb + "\n已取消。";
                    return bank;
                }

                FamilyDefinition family = BuildFamily(bank, "F" + f.ToString("D3"), ref seed,
                                                      bankSettings, validation, visual,
                                                      runVisualChecks, captures, false);
                if (family == null) { familyRejected++; continue; }
                bank.families.Add(family);
            }

            // ---------- 练习家族 ----------
            for (int f = 0; f < bankSettings.practiceFamilies; f++)
            {
                if (progress != null &&
                    !progress("生成练习家族 " + (f + 1) + "/" + bankSettings.practiceFamilies, 0.45f))
                {
                    log = sb + "\n已取消。";
                    return bank;
                }
                BuildFamily(bank, "P" + f.ToString("D3"), ref seed, bankSettings, validation,
                            visual, runVisualChecks, captures, true);
            }

            sb.AppendLine("正式家族 " + bank.families.Count + " 个（放弃 " + familyRejected + " 个）");
            sb.AppendLine("正式物体 " + bank.objects.Count + " 个，练习物体 " + bank.practiceObjects.Count + " 个");

            // ---------- ③ 配对矩阵 ----------
            if (progress != null) progress("计算配对矩阵", 0.55f);
            int invalidByStructure, invalidByVisual;
            BuildPairMatrix(bank, visual, runVisualChecks, captures, progress,
                            out invalidByStructure, out invalidByVisual);
            bank.BuildIndex();

            sb.AppendLine("配对矩阵：合法配对 " + bank.pairs.Count / 2 + " 对" +
                          "（结构不符 " + invalidByStructure + "，轮廓不符 " + invalidByVisual + "）");

            // ---------- ④ 覆盖度补充 ----------
            int added = TopUpCoverage(bank, ref seed, bankSettings, validation, visual,
                                      runVisualChecks, captures, progress);
            if (added > 0)
            {
                bank.BuildIndex();
                sb.AppendLine("覆盖度补充：新增 " + added + " 个物体");
            }

            sb.AppendLine();
            sb.Append(bank.CoverageReport(bankSettings.minCandidatesPerLevel));
            sb.AppendLine();
            sb.Append(bank.PartRoleBalanceReport());

            log = sb.ToString();
            bank.visualChecksRun = runVisualChecks;
            bank.buildReport = log;
            return bank;
        }

        public static string PartSetId()
        {
            var parts = new List<string>();
            for (int i = 0; i < StimConfig.ShapesInUse.Length; i++)
                parts.Add(StimConfig.ShapesInUse[i].ToString());
            return string.Join("+", parts.ToArray()) + "x" + StimConfig.CopiesPerShape;
        }

        // ------------------------------------------------------------------ 家族

        private static FamilyDefinition BuildFamily(StimulusBank bank, string familyId, ref int seed,
                                                    BankBuildSettings bankSettings,
                                                    ValidationSettings validation,
                                                    VisualCheckSettings visual, bool runVisualChecks,
                                                    Dictionary<string, ViewCapture[]> captures,
                                                    bool practice)
        {
            var levels = new[] { SimilarityLevel.High, SimilarityLevel.Medium, SimilarityLevel.Low };

            for (int attempt = 0; attempt < bankSettings.familyAttempts; attempt++)
            {
                ObjectDefinition baseDef = ObjectGenerator.Generate(seed++, validation);
                if (baseDef == null) continue;

                baseDef.familyId = familyId;
                baseDef.partSetId = bank.partSetId;
                baseDef.isPractice = practice;
                baseDef.objectId = familyId + "_B";

                ViewCapture[] baseViews = null;
                if (runVisualChecks)
                {
                    baseViews = SilhouetteAnalyzer.Capture(baseDef, visual);
                    if (!SilhouetteAnalyzer.Evaluate(baseViews, null, SimilarityLevel.Identical, visual).passed)
                        continue;
                }

                var members = new List<ObjectDefinition> { baseDef };
                var memberViews = new List<ViewCapture[]> { baseViews };
                bool ok = true;

                for (int li = 0; li < levels.Length && ok; li++)
                {
                    for (int v = 0; v < bankSettings.variantsPerLevel && ok; v++)
                    {
                        ObjectDefinition variant = null;
                        for (int tries = 0; tries < 30 && variant == null; tries++)
                        {
                            variant = VariantGenerator.Generate(baseDef, levels[li], seed++, validation);
                            if (variant == null) continue;

                            // 家族内部不能出现重复结构
                            for (int m = 0; m < members.Count; m++)
                            {
                                if (members[m].StructureHash() == variant.StructureHash())
                                {
                                    variant = null;
                                    break;
                                }
                            }
                            if (variant == null) continue;

                            if (runVisualChecks)
                            {
                                ViewCapture[] vv = SilhouetteAnalyzer.Capture(variant, visual);
                                if (!SilhouetteAnalyzer.Evaluate(vv, baseViews, levels[li], visual).passed)
                                {
                                    variant = null;
                                    continue;
                                }
                                memberViews.Add(vv);
                            }
                            else memberViews.Add(null);
                        }

                        if (variant == null) { ok = false; break; }

                        variant.familyId = familyId;
                        variant.partSetId = bank.partSetId;
                        variant.isPractice = practice;
                        variant.objectId = familyId + "_" + VariantGenerator.LevelSuffix(levels[li]) +
                                           (bankSettings.variantsPerLevel > 1 ? (v + 1).ToString() : "");
                        members.Add(variant);
                    }
                }

                if (!ok) continue;

                var family = new FamilyDefinition
                {
                    familyId = familyId,
                    seed = baseDef.seed,
                    baseObjectId = baseDef.objectId,
                };
                for (int m = 0; m < members.Count; m++)
                {
                    family.memberObjectIds.Add(members[m].objectId);
                    if (practice) bank.practiceObjects.Add(members[m]);
                    else bank.objects.Add(members[m]);
                    if (memberViews[m] != null) captures[members[m].objectId] = memberViews[m];
                }
                return family;
            }
            return null;
        }

        // ------------------------------------------------------------------ 配对矩阵

        private static void BuildPairMatrix(StimulusBank bank, VisualCheckSettings visual,
                                            bool runVisualChecks,
                                            Dictionary<string, ViewCapture[]> captures,
                                            BankProgress progress,
                                            out int invalidByStructure, out int invalidByVisual)
        {
            invalidByStructure = 0;
            invalidByVisual = 0;
            bank.pairs.Clear();

            int n = bank.objects.Count;
            for (int i = 0; i < n; i++)
            {
                if (progress != null && (i % 4 == 0))
                    progress("计算配对矩阵 " + (i + 1) + "/" + n, 0.55f + 0.3f * i / Mathf.Max(1, n));

                for (int j = i + 1; j < n; j++)
                {
                    ObjectDefinition a = bank.objects[i];
                    ObjectDefinition b = bank.objects[j];

                    PairClass pc = PairClassifier.ClassifyStructural(a, b);
                    if (pc == PairClass.Invalid || pc == PairClass.Target)
                    {
                        invalidByStructure++;
                        continue;
                    }

                    SimilarityLevel level = PairClassifier.ToLevel(pc);
                    float iouMin = -1f, iouMax = -1f;

                    if (runVisualChecks)
                    {
                        ViewCapture[] va, vb;
                        if (!captures.TryGetValue(a.objectId, out va) ||
                            !captures.TryGetValue(b.objectId, out vb))
                        {
                            invalidByVisual++;
                            continue;
                        }

                        VisualReport report = SilhouetteAnalyzer.Evaluate(vb, va, level, visual);
                        if (!report.passed) { invalidByVisual++; continue; }
                        iouMin = report.minIou;
                        iouMax = report.maxIou;
                    }

                    int retained = a.RetainedRelationsAgainst(b);
                    bank.pairs.Add(new PairEntry
                    {
                        a = a.objectId, b = b.objectId, level = level,
                        retainedRelations = retained, iouMin = iouMin, iouMax = iouMax,
                    });
                    bank.pairs.Add(new PairEntry
                    {
                        a = b.objectId, b = a.objectId, level = level,
                        retainedRelations = retained, iouMin = iouMin, iouMax = iouMax,
                    });
                }
            }
        }

        // ------------------------------------------------------------------ 覆盖度补充

        /// <summary>
        /// 对候选不足的 (物体, 等级) 组合，直接从该物体派生新的变体加入库中。
        /// 新物体同时也会成为别人的候选，所以补一个往往能修好好几个缺口。
        /// </summary>
        private static int TopUpCoverage(StimulusBank bank, ref int seed,
                                         BankBuildSettings bankSettings,
                                         ValidationSettings validation,
                                         VisualCheckSettings visual, bool runVisualChecks,
                                         Dictionary<string, ViewCapture[]> captures,
                                         BankProgress progress)
        {
            var levels = new[] { SimilarityLevel.High, SimilarityLevel.Medium, SimilarityLevel.Low };
            int added = 0;

            for (int round = 0; round < 6 && added < bankSettings.maxTopUpObjects; round++)
            {
                bank.BuildIndex();

                var deficits = new List<KeyValuePair<string, SimilarityLevel>>();
                for (int i = 0; i < bank.objects.Count; i++)
                {
                    string id = bank.objects[i].objectId;
                    for (int li = 0; li < levels.Length; li++)
                        if (bank.CandidateCount(id, levels[li]) < bankSettings.minCandidatesPerLevel)
                            deficits.Add(new KeyValuePair<string, SimilarityLevel>(id, levels[li]));
                }
                if (deficits.Count == 0) break;

                if (progress != null &&
                    !progress("补充覆盖度：还缺 " + deficits.Count + " 处", 0.85f + 0.1f * round / 6f))
                    break;

                int addedThisRound = 0;
                for (int d = 0; d < deficits.Count && added < bankSettings.maxTopUpObjects; d++)
                {
                    ObjectDefinition reference = bank.Find(deficits[d].Key);
                    if (reference == null) continue;

                    ObjectDefinition extra = null;
                    for (int tries = 0; tries < 25 && extra == null; tries++)
                    {
                        extra = VariantGenerator.Generate(reference, deficits[d].Value, seed++, validation);
                        if (extra == null) continue;
                        if (FindByStructure(bank, extra) != null) { extra = null; continue; }

                        extra.objectId = reference.familyId + "_X" + added.ToString("D3");

                        if (runVisualChecks)
                        {
                            ViewCapture[] refViews;
                            if (!captures.TryGetValue(reference.objectId, out refViews)) { extra = null; break; }

                            ViewCapture[] extraViews = SilhouetteAnalyzer.Capture(extra, visual);
                            if (!SilhouetteAnalyzer.Evaluate(extraViews, refViews, deficits[d].Value, visual).passed)
                            {
                                extra = null;
                                continue;
                            }
                            captures[extra.objectId] = extraViews;
                        }
                    }
                    if (extra == null) continue;

                    extra.familyId = reference.familyId;
                    extra.partSetId = bank.partSetId;
                    extra.isPractice = false;

                    // 把新物体和库里所有物体配一遍，直接补进 pairs
                    AddObjectToMatrix(bank, extra, visual, runVisualChecks, captures);
                    bank.objects.Add(extra);

                    FamilyDefinition fam = bank.families.Find(x => x.familyId == extra.familyId);
                    if (fam != null) fam.memberObjectIds.Add(extra.objectId);

                    added++;
                    addedThisRound++;
                    bank.BuildIndex();
                }
                if (addedThisRound == 0) break;
            }
            return added;
        }

        private static ObjectDefinition FindByStructure(StimulusBank bank, ObjectDefinition def)
        {
            string hash = def.StructureHash();
            for (int i = 0; i < bank.objects.Count; i++)
                if (bank.objects[i].StructureHash() == hash) return bank.objects[i];
            for (int i = 0; i < bank.practiceObjects.Count; i++)
                if (bank.practiceObjects[i].StructureHash() == hash) return bank.practiceObjects[i];
            return null;
        }

        private static void AddObjectToMatrix(StimulusBank bank, ObjectDefinition extra,
                                              VisualCheckSettings visual, bool runVisualChecks,
                                              Dictionary<string, ViewCapture[]> captures)
        {
            for (int i = 0; i < bank.objects.Count; i++)
            {
                ObjectDefinition other = bank.objects[i];
                PairClass pc = PairClassifier.ClassifyStructural(other, extra);
                if (pc == PairClass.Invalid || pc == PairClass.Target) continue;

                SimilarityLevel level = PairClassifier.ToLevel(pc);
                float iouMin = -1f, iouMax = -1f;

                if (runVisualChecks)
                {
                    ViewCapture[] vo, ve;
                    if (!captures.TryGetValue(other.objectId, out vo) ||
                        !captures.TryGetValue(extra.objectId, out ve)) continue;
                    VisualReport report = SilhouetteAnalyzer.Evaluate(ve, vo, level, visual);
                    if (!report.passed) continue;
                    iouMin = report.minIou;
                    iouMax = report.maxIou;
                }

                int retained = other.RetainedRelationsAgainst(extra);
                bank.pairs.Add(new PairEntry
                {
                    a = other.objectId, b = extra.objectId, level = level,
                    retainedRelations = retained, iouMin = iouMin, iouMax = iouMax,
                });
                bank.pairs.Add(new PairEntry
                {
                    a = extra.objectId, b = other.objectId, level = level,
                    retainedRelations = retained, iouMin = iouMin, iouMax = iouMax,
                });
            }
        }
    }
}
