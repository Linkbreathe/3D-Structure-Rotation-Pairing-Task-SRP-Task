using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StimGen
{
    public class PreflightResult
    {
        public bool passed = true;
        public readonly List<string> errors = new List<string>();
        public readonly List<string> warnings = new List<string>();
        public readonly List<string> info = new List<string>();

        public void Error(string message) { passed = false; errors.Add(message); }
        public void Warn(string message) { warnings.Add(message); }
        public void Info(string message) { info.Add(message); }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine(passed ? "PREFLIGHT PASS" : "PREFLIGHT FAIL");
            for (int i = 0; i < errors.Count; i++) sb.AppendLine("  [错误] " + errors[i]);
            for (int i = 0; i < warnings.Count; i++) sb.AppendLine("  [警告] " + warnings[i]);
            for (int i = 0; i < info.Count; i++) sb.AppendLine("  " + info[i]);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Formal Pairing session 的运行前检查。
    /// 检查每个 trial 是否是独立 A/B pair，并验证 structural similarity、rotation
    /// difference、segment transition 和 session coverage；不再检查 t-2 链。
    /// </summary>
    public static class PreflightValidator
    {
        public static PreflightResult Validate(SessionPlan plan, StimulusBank bank,
                                               int targetTolerance = 0)
        {
            var r = new PreflightResult();
            if (plan == null)
            {
                r.Error("session plan 为空");
                return r;
            }
            if (bank == null)
            {
                r.Error("stimulus bank 为空");
                return r;
            }

            bank.BuildIndex();
            plan.BuildIndex();

            if (plan.taskProtocolVersion != ExperimentDesign.TaskProtocolVersion)
                r.Error("任务协议 " + plan.taskProtocolVersion + "，应为 " +
                        ExperimentDesign.TaskProtocolVersion);
            if (plan.rotationProtocolVersion != ExperimentDesign.RotationProtocolVersion)
                r.Error("旋转协议 " + plan.rotationProtocolVersion + "，应为 " +
                        ExperimentDesign.RotationProtocolVersion);
            if (plan.blocks == null)
            {
                r.Error("session blocks 为空");
                return r;
            }
            if (plan.blocks.Count != ExperimentDesign.BlocksPerParticipant)
                r.Error("block 数 " + plan.blocks.Count + "，应为 " +
                        ExperimentDesign.BlocksPerParticipant);

            int scoredTotal = 0;
            int expectedGlobalTrial = 0;
            var levelTrials = new Dictionary<SimilarityLevel, int>();
            var rotationTrials = new Dictionary<float, int>();
            var transitionCounts = new Dictionary<string, int>();
            int boundaryCount = 0;
            int noOpCount = 0;

            for (int b = 0; b < plan.blocks.Count; b++)
            {
                BlockPlan block = plan.blocks[b];
                if (block == null || block.presentations == null)
                {
                    r.Error("block " + b + " 为空");
                    continue;
                }

                if (block.presentations.Count != ExperimentDesign.PresentationsPerBlock)
                    r.Error("block " + b + " trial 数 " + block.presentations.Count +
                            "，应为 " + ExperimentDesign.PresentationsPerBlock);
                if (block.segmentLengths.Count != ExperimentDesign.SegmentCount)
                    r.Error("block " + b + " segment 数不正确");

                int scored = 0;
                int targets = 0;
                int nonTargets = 0;
                for (int i = 0; i < block.presentations.Count; i++)
                {
                    PresentationRecord p = block.presentations[i];
                    if (p.conditionRotationAxis != ExperimentDesign.ConditionRotationAxis)
                        r.Error("block " + b + " trial " + i + " 的实验旋转轴为 " +
                                p.conditionRotationAxis + "，应为 X");
                    if (p.presentationAnimationAxis != ExperimentDesign.PresentationAnimationAxis)
                        r.Error("block " + b + " trial " + i + " 的观看动画轴为 " +
                                p.presentationAnimationAxis + "，应为 Y");
                    if (!p.scored)
                    {
                        r.Error("block " + b + " trial " + i + " 不是 scored trial；Pairing 不允许初始化呈现");
                        continue;
                    }

                    scored++;
                    scoredTotal++;
                    if (p.trialIndexGlobal != expectedGlobalTrial)
                        r.Error("block " + b + " trial " + i + " 的 global trial index 不连续");
                    expectedGlobalTrial++;
                    if (p.presentationIndexInBlock != i || p.trialIndexWithinBlock != i)
                        r.Error("block " + b + " trial " + i + " 的 block index 不正确");

                    if (p.IsTarget) targets++; else nonTargets++;
                    Increment(levelTrials, p.segmentSimilarity);
                    Increment(rotationTrials, p.rotationDeltaX);

                    if (p.isFirstTrialAfterBoundary)
                    {
                        boundaryCount++;
                        if (p.isNoOpBoundary) noOpCount++;
                        Increment(transitionCounts, p.similarityTransition);
                    }

                    ValidateTrial(plan, bank, block, b, i, p, r);
                }

                if (scored != ExperimentDesign.ScoredTrialsPerBlock)
                    r.Error("block " + b + " scored trials " + scored +
                            "，应为 " + ExperimentDesign.ScoredTrialsPerBlock);
                if (Mathf.Abs(targets - ExperimentDesign.TargetsPerBlock) > targetTolerance)
                    r.Error("block " + b + " Target " + targets + "，应为 " +
                            ExperimentDesign.TargetsPerBlock + "±" + targetTolerance);
                int expectedNonTargets = ExperimentDesign.ScoredTrialsPerBlock -
                                         ExperimentDesign.TargetsPerBlock;
                if (Mathf.Abs(nonTargets - expectedNonTargets) > targetTolerance)
                    r.Error("block " + b + " Non-target " + nonTargets + "，应为 " +
                            expectedNonTargets);
            }

            int expectedTotal = ExperimentDesign.BlocksPerParticipant *
                                ExperimentDesign.ScoredTrialsPerBlock;
            if (scoredTotal != expectedTotal)
                r.Error("总 scored trials " + scoredTotal + "，应为 " + expectedTotal);

            int expectedBoundaries = ExperimentDesign.BlocksPerParticipant *
                                     (ExperimentDesign.SegmentCount - 1);
            if (boundaryCount != expectedBoundaries)
                r.Error("边界数 " + boundaryCount + "，应为 " + expectedBoundaries);
            if (noOpCount != expectedBoundaries / 3)
                r.Error("No-op 边界数 " + noOpCount + "，应为 " + expectedBoundaries / 3);

            string[] directed =
            {
                "Low_to_Medium", "Medium_to_Low", "Medium_to_High",
                "High_to_Medium", "Low_to_High", "High_to_Low",
            };
            string[] noOps = { "Low_to_Low", "Medium_to_Medium", "High_to_High" };
            for (int i = 0; i < directed.Length; i++)
                CheckTransition(r, transitionCounts, directed[i], 2);
            for (int i = 0; i < noOps.Length; i++)
                CheckTransition(r, transitionCounts, noOps[i], 2);

            // 三个 context level 各 60 个 trial，rotation difference 各 60 个 trial。
            CheckLevel(r, levelTrials, SimilarityLevel.Low, 60);
            CheckLevel(r, levelTrials, SimilarityLevel.Medium, 60);
            CheckLevel(r, levelTrials, SimilarityLevel.High, 60);
            for (int i = 0; i < ExperimentDesign.RotationOptions.Length; i++)
            {
                float rotation = ExperimentDesign.RotationOptions[i];
                int n;
                rotationTrials.TryGetValue(rotation, out n);
                r.Info("Pair RotationDelta " + rotation + "° trials：" + n);
                if (n != 60)
                    r.Error("Pair RotationDelta " + rotation + "° trials " + n +
                            "，应为 60");
            }
            foreach (float actual in rotationTrials.Keys)
                if (!IsRotationOption(actual))
                    r.Error("出现计划外的 Pair RotationDelta：" + actual + "°");

            ValidateSessionObjects(plan, r);
            r.Info("Target 总数：" + CountTargets(plan) + "，Non-target 总数：" +
                   (scoredTotal - CountTargets(plan)));
            return r;
        }

        private static void ValidateTrial(SessionPlan plan, StimulusBank bank,
                                          BlockPlan block, int blockIndex, int trialIndex,
                                          PresentationRecord p, PreflightResult r)
        {
            int expectedSegment;
            int expectedWithinSegment;
            ExperimentDesign.LocateSegment(trialIndex, out expectedSegment,
                                           out expectedWithinSegment);
            if (p.segmentIndex != expectedSegment ||
                p.trialIndexWithinSegment != expectedWithinSegment)
                r.Error("block " + blockIndex + " trial " + trialIndex +
                        " 的 segment index 不正确");

            SimilarityLevel expectedLevel = block.segmentSimilarity[expectedSegment];
            if (p.segmentSimilarity != expectedLevel)
                r.Error("block " + blockIndex + " trial " + trialIndex +
                        " 的 segment structural similarity 不正确");

            if (expectedSegment == 0)
            {
                if (p.similarityTransition != "" || p.isFirstTrialAfterBoundary ||
                    p.isNoOpBoundary || p.trialsSinceTransition != -1)
                    r.Error("block " + blockIndex + " trial " + trialIndex +
                            " 不应带有 transition 标记");
            }
            else
            {
                string transition = ExperimentDesign.TransitionLabel(
                    block.segmentSimilarity[expectedSegment - 1], expectedLevel);
                bool noOp = ExperimentDesign.IsNoOp(
                    block.segmentSimilarity[expectedSegment - 1], expectedLevel);
                if (p.similarityTransition != transition ||
                    p.isFirstTrialAfterBoundary != (expectedWithinSegment == 0) ||
                    p.isNoOpBoundary != noOp ||
                    p.trialsSinceTransition != expectedWithinSegment)
                    r.Error("block " + blockIndex + " trial " + trialIndex +
                            " 的 transition 标记不正确");
            }

            if (string.IsNullOrEmpty(p.referenceObjectId) ||
                string.IsNullOrEmpty(p.comparisonObjectId))
            {
                r.Error("block " + blockIndex + " trial " + trialIndex +
                        " 缺少 Reference 或 Comparison object");
                return;
            }

            ObjectDefinition reference = plan.Find(p.referenceObjectId);
            ObjectDefinition comparison = plan.Find(p.comparisonObjectId);
            if (reference == null || comparison == null)
            {
                r.Error("block " + blockIndex + " trial " + trialIndex +
                        " 的 Reference/Comparison 不在 Session 物体表中");
                return;
            }
            if (reference.isPractice || comparison.isPractice)
                r.Error("正式 session 混入练习物体：block " + blockIndex + " trial " + trialIndex);

            PairClass actual = PairClassifier.ClassifyStructural(reference, comparison);
            if (actual != p.trialPairType)
                r.Error("block " + blockIndex + " trial " + trialIndex +
                        " 的 pair 类型不正确");
            if (p.expectedSame != (actual == PairClass.Target))
                r.Error("block " + blockIndex + " trial " + trialIndex +
                        " 的 ExpectedAnswer 不正确");
            if (p.IsTarget && p.referenceObjectId != p.comparisonObjectId)
                r.Error("block " + blockIndex + " trial " + trialIndex +
                        " 标为 Target 但 A/B object ID 不同");
            if (!p.IsTarget && p.referenceObjectId == p.comparisonObjectId)
                r.Error("block " + blockIndex + " trial " + trialIndex +
                        " 标为 Non-target 但 A/B object ID 相同");

            int expectedRetained = p.IsTarget
                ? StimConfig.EdgeCount : reference.RetainedRelationsAgainst(comparison);
            if (p.retainedRelations != expectedRetained)
                r.Error("block " + blockIndex + " trial " + trialIndex +
                        " 的 RetainedRelations 不正确");
            if (p.structuralDistance != StimConfig.EdgeCount - expectedRetained)
                r.Error("block " + blockIndex + " trial " + trialIndex +
                        " 的 StructuralDistance 不正确");

            if (!p.IsTarget)
            {
                List<string> candidates = bank.CandidatesFor(p.referenceObjectId,
                                                              p.segmentSimilarity);
                if (!candidates.Contains(p.comparisonObjectId))
                    r.Error("block " + blockIndex + " trial " + trialIndex +
                            " 的 Non-target 不在冻结配对表中");
            }

            float expectedComparisonRotation = Mathf.Repeat(
                p.referenceRotationX + p.rotationDeltaX, 360f);
            if (Mathf.Abs(Mathf.DeltaAngle(expectedComparisonRotation,
                                           p.comparisonRotationX)) > 0.001f)
                r.Error("block " + blockIndex + " trial " + trialIndex +
                        " 的 Comparison rotation 不等于 Reference + delta");
            if (!IsRotationOption(p.rotationDeltaX))
                r.Error("block " + blockIndex + " trial " + trialIndex +
                        " 的 RotationDelta 不在计划集合中");

            if (trialIndex > 0)
            {
                PresentationRecord previous = block.presentations[trialIndex - 1];
                if (p.comparisonObjectId == previous.comparisonObjectId)
                    r.Error("block " + blockIndex + " trial " + trialIndex +
                            " 与前一 trial 重复 Comparison object");
            }
        }

        private static void ValidateSessionObjects(SessionPlan plan, PreflightResult r)
        {
            var ids = new HashSet<string>();
            for (int b = 0; b < plan.blocks.Count; b++)
            {
                for (int i = 0; i < plan.blocks[b].presentations.Count; i++)
                {
                    PresentationRecord p = plan.blocks[b].presentations[i];
                    ids.Add(p.referenceObjectId);
                    ids.Add(p.comparisonObjectId);
                }
            }
            for (int i = 0; i < plan.objects.Count; i++)
                if (!ids.Contains(plan.objects[i].objectId))
                    r.Warn("Session 物体表中有未使用物体：" + plan.objects[i].objectId);
            foreach (string id in ids)
                if (plan.Find(id) == null)
                    r.Error("pair 使用了未写入 Session 物体表的 object：" + id);
        }

        private static void CheckTransition(PreflightResult r,
                                             Dictionary<string, int> counts,
                                             string label, int expected)
        {
            int n;
            counts.TryGetValue(label, out n);
            if (n != expected)
                r.Error("transition " + label + " 出现 " + n + " 次，应为 " + expected);
        }

        private static void CheckLevel(PreflightResult r,
                                       Dictionary<SimilarityLevel, int> counts,
                                       SimilarityLevel level, int expected)
        {
            int n;
            counts.TryGetValue(level, out n);
            r.Info(level + " context trials：" + n);
            if (n != expected)
                r.Error(level + " context trials " + n + "，应为 " + expected);
        }

        private static int CountTargets(SessionPlan plan)
        {
            int n = 0;
            for (int b = 0; b < plan.blocks.Count; b++) n += plan.blocks[b].TargetCount();
            return n;
        }

        private static bool IsRotationOption(float angle)
        {
            for (int i = 0; i < ExperimentDesign.RotationOptions.Length; i++)
                if (Mathf.Approximately(angle, ExperimentDesign.RotationOptions[i])) return true;
            return false;
        }

        private static void Increment(Dictionary<SimilarityLevel, int> counts,
                                      SimilarityLevel key)
        {
            int value;
            counts.TryGetValue(key, out value);
            counts[key] = value + 1;
        }

        private static void Increment(Dictionary<float, int> counts, float key)
        {
            int value;
            counts.TryGetValue(key, out value);
            counts[key] = value + 1;
        }

        private static void Increment(Dictionary<string, int> counts, string key)
        {
            int value;
            counts.TryGetValue(key, out value);
            counts[key] = value + 1;
        }
    }
}
