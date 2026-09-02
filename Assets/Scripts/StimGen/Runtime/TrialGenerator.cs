using System;
using System.Collections.Generic;
using UnityEngine;

namespace StimGen
{
    [Serializable]
    public class TrialGeneratorSettings
    {
        [Tooltip("最多允许连续几个 Target")]
        public int maxConsecutiveTargets = 2;

        [Tooltip("最多允许连续几个 Non-target")]
        public int maxConsecutiveNonTargets = 6;

        [Tooltip("同一 Comparison object 在最近多少个 trial 内不得再次出现")]
        public int noRepeatWindow = 4;

        [Tooltip("同一 family 在最近多少个 trial 内不得再次作为 Comparison 出现，0 = 不限制")]
        public int familyCooldown = 2;

        [Tooltip("整个 block 生成失败时重试几次")]
        public int blockAttempts = 200;

        [Tooltip("每个 Reference object 在每个 structural similarity level 下至少要有几个候选")]
        public int minCandidatesPerLevel = 2;
    }

    /// <summary>
    /// Pairing trial generator。
    /// 每个 trial 独立生成 Reference A 和 Comparison B；不维护 t-2、ChainID 或 memory chain。
    /// Non-target pair 从冻结的 StimulusBank 配对表中选择，保证 High / Low 的结构定义。
    /// </summary>
    public static class TrialGenerator
    {
        public static SessionPlan BuildSession(StimulusBank bank, string participantId,
                                               int participantNumber, int masterSeed,
                                               TrialGeneratorSettings settings,
                                               out string error)
        {
            error = null;
            if (bank == null)
            {
                error = "刺激库为空";
                return null;
            }

            if (settings == null) settings = new TrialGeneratorSettings();
            bank.BuildIndex();

            var plan = new SessionPlan
            {
                participantId = participantId,
                participantNumber = participantNumber,
                masterSeed = masterSeed,
                generatedUtc = DateTime.UtcNow.ToString("o",
                    System.Globalization.CultureInfo.InvariantCulture),
                bankId = bank.generatedUtc,
                taskProtocolVersion = ExperimentDesign.TaskProtocolVersion,
                rotationProtocolVersion = ExperimentDesign.RotationProtocolVersion,
                swapResponseKeys = ExperimentDesign.SwapResponseKeysFor(participantNumber),
            };

            int[] order = ExperimentDesign.BlockOrderFor(participantNumber);
            plan.blockOrder.AddRange(order);

            // 跨 block 平衡两端物体和 family 的使用次数。
            var objectUsage = new Dictionary<string, int>();
            var familyUsage = new Dictionary<string, int>();
            var rng = new System.Random(masterSeed ^ (participantNumber * 7919));

            for (int b = 0; b < order.Length; b++)
            {
                BlockPlan block = BuildBlock(bank, b, order[b], rng, settings,
                                             objectUsage, familyUsage, out error);
                if (block == null)
                {
                    error = "Block " + b + "（sequence " +
                            ExperimentDesign.BlockSequenceIds[order[b]] + "）生成失败：" + error;
                    return null;
                }
                plan.blocks.Add(block);
            }

            // Session 必须自包含：Reference 和 Comparison 两端都写入 object definitions。
            var used = new List<string>();
            for (int b = 0; b < plan.blocks.Count; b++)
            {
                for (int i = 0; i < plan.blocks[b].presentations.Count; i++)
                {
                    PresentationRecord p = plan.blocks[b].presentations[i];
                    AddUnique(used, p.referenceObjectId);
                    AddUnique(used, p.comparisonObjectId);
                }
            }
            used.Sort(StringComparer.Ordinal);
            for (int i = 0; i < used.Count; i++)
            {
                ObjectDefinition def = bank.Find(used[i]);
                if (def != null) plan.objects.Add(def);
            }

            int global = 0;
            for (int b = 0; b < plan.blocks.Count; b++)
            {
                for (int i = 0; i < plan.blocks[b].presentations.Count; i++)
                {
                    if (plan.blocks[b].presentations[i].scored)
                        plan.blocks[b].presentations[i].trialIndexGlobal = global++;
                }
            }

            PopulateExposureFields(plan);
            plan.BuildIndex();
            return plan;
        }

        /// <summary>
        /// 在 pair trial 开始前计算两端物体此前在 session 中出现过的次数和间隔。
        /// 一个 Target pair 会显示同一个 object 两次，因此该 object 的 exposure 会增加两次。
        /// </summary>
        private static void PopulateExposureFields(SessionPlan plan)
        {
            var objectExposures = new Dictionary<string, int>();
            var familyExposures = new Dictionary<string, int>();
            var lastObjectTrial = new Dictionary<string, int>();
            var lastFamilyTrial = new Dictionary<string, int>();
            int trialOrdinal = 0;

            for (int b = 0; b < plan.blocks.Count; b++)
            {
                BlockPlan block = plan.blocks[b];
                for (int i = 0; i < block.presentations.Count; i++)
                {
                    PresentationRecord p = block.presentations[i];
                    ObjectDefinition reference = plan.Find(p.referenceObjectId);
                    ObjectDefinition comparison = plan.Find(p.comparisonObjectId);

                    p.stimulusBankVersion = plan.bankId;
                    p.referenceFamilyId = reference != null ? reference.familyId : "";
                    p.comparisonFamilyId = comparison != null ? comparison.familyId : "";
                    p.conditionRotationAxis = ExperimentDesign.ConditionRotationAxis;
                    p.presentationAnimationAxis = ExperimentDesign.PresentationAnimationAxis;
                    p.boundaryPositionWithinBlock = p.isFirstTrialAfterBoundary
                        ? p.segmentIndex : -1;

                    p.referenceObjectPriorExposures = reference == null
                        ? -1 : GetCount(objectExposures, reference.objectId);
                    p.comparisonObjectPriorExposures = comparison == null
                        ? -1 : GetCount(objectExposures, comparison.objectId);
                    p.referenceFamilyPriorExposures = reference == null
                        ? -1 : GetCount(familyExposures, reference.familyId);
                    p.comparisonFamilyPriorExposures = comparison == null
                        ? -1 : GetCount(familyExposures, comparison.familyId);

                    p.trialsSinceReferenceObjectLastSeen = reference == null
                        ? -1 : Since(lastObjectTrial, reference.objectId, trialOrdinal);
                    p.trialsSinceComparisonObjectLastSeen = comparison == null
                        ? -1 : Since(lastObjectTrial, comparison.objectId, trialOrdinal);
                    p.trialsSinceReferenceFamilyLastSeen = reference == null
                        ? -1 : Since(lastFamilyTrial, reference.familyId, trialOrdinal);
                    p.trialsSinceComparisonFamilyLastSeen = comparison == null
                        ? -1 : Since(lastFamilyTrial, comparison.familyId, trialOrdinal);

                    Increment(objectExposures, p.referenceObjectId);
                    Increment(objectExposures, p.comparisonObjectId);
                    if (reference != null) Increment(familyExposures, reference.familyId);
                    if (comparison != null) Increment(familyExposures, comparison.familyId);
                    if (!string.IsNullOrEmpty(p.referenceObjectId))
                        lastObjectTrial[p.referenceObjectId] = trialOrdinal;
                    if (!string.IsNullOrEmpty(p.comparisonObjectId))
                        lastObjectTrial[p.comparisonObjectId] = trialOrdinal;
                    if (reference != null && !string.IsNullOrEmpty(reference.familyId))
                        lastFamilyTrial[reference.familyId] = trialOrdinal;
                    if (comparison != null && !string.IsNullOrEmpty(comparison.familyId))
                        lastFamilyTrial[comparison.familyId] = trialOrdinal;
                    trialOrdinal++;
                }
            }
        }

        private static int GetCount(Dictionary<string, int> map, string key)
        {
            if (string.IsNullOrEmpty(key)) return 0;
            int value;
            return map.TryGetValue(key, out value) ? value : 0;
        }

        private static int Since(Dictionary<string, int> lastSeen, string key, int currentOrdinal)
        {
            if (string.IsNullOrEmpty(key)) return -1;
            int previous;
            return lastSeen.TryGetValue(key, out previous)
                ? currentOrdinal - previous - 1 : -1;
        }

        // ------------------------------------------------------------------ single block

        private static BlockPlan BuildBlock(StimulusBank bank, int blockIndex, int sequenceIndex,
                                            System.Random rng, TrialGeneratorSettings settings,
                                            Dictionary<string, int> objectUsage,
                                            Dictionary<string, int> familyUsage,
                                            out string error)
        {
            error = null;
            SimilarityLevel[] levels = ExperimentDesign.BlockSequences[sequenceIndex];
            int trialCount = ExperimentDesign.ScoredTrialsPerBlock;

            var segmentOf = new int[trialCount];
            var withinSegment = new int[trialCount];
            for (int i = 0; i < trialCount; i++)
                ExperimentDesign.LocateSegment(i, out segmentOf[i], out withinSegment[i]);

            for (int attempt = 0; attempt < Mathf.Max(1, settings.blockAttempts); attempt++)
            {
                bool[] isTarget = BuildTargetPattern(trialCount, rng, settings);
                if (isTarget == null)
                {
                    error = "无法排出满足约束的 Target 模式";
                    continue;
                }

                float[] deltas = AssignRotations(blockIndex, sequenceIndex, trialCount, segmentOf,
                                                 withinSegment, levels, isTarget, rng);
                string[] references = new string[trialCount];
                string[] comparisons = new string[trialCount];
                string failure;
                if (!AssignPairs(bank, rng, settings, segmentOf, levels, isTarget,
                                 references, comparisons, objectUsage, familyUsage,
                                 out failure))
                {
                    error = failure;
                    continue;
                }

                // Reference A 使用固定基准姿态；Comparison B = A + delta。
                // 因此记录的 RotationDeltaX 始终是 pair 内真实的角度差，不依赖历史 trial。
                var referenceRotations = new float[trialCount];
                var comparisonRotations = new float[trialCount];
                for (int i = 0; i < trialCount; i++)
                {
                    referenceRotations[i] = 0f;
                    comparisonRotations[i] = Mathf.Repeat(
                        referenceRotations[i] + deltas[i], 360f);
                }

                return Assemble(bank, blockIndex, sequenceIndex, levels, segmentOf,
                                withinSegment, isTarget, references, comparisons,
                                referenceRotations, comparisonRotations, deltas);
            }

            if (error == null) error = "未知原因";
            return null;
        }

        // ------------------------------------------------------------------ target pattern

        /// <summary>Target 比例保持每个 block 10/30；不再因 segment boundary 强制 Non-target。</summary>
        private static bool[] BuildTargetPattern(int trialCount, System.Random rng,
                                                  TrialGeneratorSettings settings)
        {
            int targets = ExperimentDesign.TargetsPerBlock;
            if (targets < 0 || targets > trialCount) return null;

            var positions = new List<int>(trialCount);
            for (int i = 0; i < trialCount; i++) positions.Add(i);

            for (int attempt = 0; attempt < 4000; attempt++)
            {
                var pattern = new bool[trialCount];
                var shuffled = new List<int>(positions);
                ObjectGenerator.Shuffle(shuffled, rng);
                for (int i = 0; i < targets; i++) pattern[shuffled[i]] = true;
                if (RunLengthOk(pattern, settings)) return pattern;
            }
            return null;
        }

        private static bool RunLengthOk(bool[] pattern, TrialGeneratorSettings settings)
        {
            int targetRun = 0, nonRun = 0;
            for (int i = 0; i < pattern.Length; i++)
            {
                if (pattern[i]) { targetRun++; nonRun = 0; }
                else { nonRun++; targetRun = 0; }
                if (targetRun > settings.maxConsecutiveTargets) return false;
                if (nonRun > settings.maxConsecutiveNonTargets) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ pair rotations

        /// <summary>
        /// 把 0°/180° 在 High/Low context 内均衡分配，并让 boundary trial 也覆盖不同角度。
        /// 奇数 cell 用 sequence index 交替额外配额；四种 sequence 合计后，
        /// 每个 Similarity × Rotation cell 恰好有 30 个 trial，且不受 block 顺序影响。
        /// </summary>
        private static float[] AssignRotations(int blockIndex, int sequenceIndex, int trialCount,
                                               int[] segmentOf,
                                               int[] withinSegment, SimilarityLevel[] levels,
                                               bool[] isTarget,
                                               System.Random rng)
        {
            float[] options = ExperimentDesign.RotationOptions;
            int optionCount = options.Length;
            SimilarityLevel[] activeLevels = ExperimentDesign.ActiveSimilarityLevels;
            int levelCount = activeLevels.Length;
            var deltas = new float[trialCount];
            var assigned = new bool[trialCount];

            var levelOfTrial = new int[trialCount];
            var levelTotals = new int[levelCount];
            for (int i = 0; i < trialCount; i++)
            {
                SimilarityLevel level = levels[segmentOf[i]];
                int levelIndex = ActiveLevelIndex(activeLevels, level);
                if (levelIndex < 0)
                    throw new InvalidOperationException(
                        "Block sequence contains inactive similarity level: " + level);
                levelOfTrial[i] = levelIndex;
                levelTotals[levelIndex]++;
            }

            var remaining = new int[levelCount, optionCount];
            for (int level = 0; level < levelCount; level++)
            {
                int perOption = levelTotals[level] / optionCount;
                for (int option = 0; option < optionCount; option++)
                    remaining[level, option] = perOption;
                int extras = levelTotals[level] - perOption * optionCount;
                for (int extra = 0; extra < extras; extra++)
                {
                    int option = (sequenceIndex + level + extra) % optionCount;
                    remaining[level, option]++;
                }
            }

            int boundaryOrdinal = 0;
            for (int i = 0; i < trialCount; i++)
            {
                if (segmentOf[i] == 0 || withinSegment[i] != 0) continue;
                int level = levelOfTrial[i];
                int pick = (blockIndex + boundaryOrdinal) % optionCount;
                for (int k = 0; k < optionCount && remaining[level, pick] == 0; k++)
                    pick = (pick + 1) % optionCount;
                deltas[i] = options[pick];
                assigned[i] = true;
                remaining[level, pick]--;
                boundaryOrdinal++;
            }

            var used = new int[2, levelCount, optionCount];
            var order = new List<int>();
            for (int i = 0; i < trialCount; i++) if (!assigned[i]) order.Add(i);
            ObjectGenerator.Shuffle(order, rng);

            for (int oi = 0; oi < order.Count; oi++)
            {
                int i = order[oi];
                int group = isTarget[i] ? 1 : 0;
                int level = levelOfTrial[i];
                int best = -1, bestUsed = int.MaxValue;
                for (int k = 0; k < optionCount; k++)
                {
                    if (remaining[level, k] == 0) continue;
                    if (used[group, level, k] < bestUsed ||
                        (used[group, level, k] == bestUsed && rng.Next(2) == 0))
                    {
                        best = k;
                        bestUsed = used[group, level, k];
                    }
                }
                if (best < 0)
                    throw new InvalidOperationException(
                        "Rotation quota exhausted before all trials were assigned.");
                deltas[i] = options[best];
                remaining[level, best]--;
                used[group, level, best]++;
            }
            return deltas;
        }

        private static int ActiveLevelIndex(SimilarityLevel[] levels, SimilarityLevel value)
        {
            for (int i = 0; i < levels.Length; i++)
                if (levels[i] == value) return i;
            return -1;
        }

        // ------------------------------------------------------------------ pair selection

        private static bool AssignPairs(StimulusBank bank, System.Random rng,
                                         TrialGeneratorSettings settings,
                                         int[] segmentOf, SimilarityLevel[] levels,
                                         bool[] isTarget, string[] references,
                                         string[] comparisons,
                                         Dictionary<string, int> objectUsage,
                                         Dictionary<string, int> familyUsage,
                                         out string error)
        {
            error = null;
            List<string> eligibleReferences = EligibleReferences(bank, settings);
            if (eligibleReferences.Count == 0)
            {
                error = "刺激库里没有可作为 Reference 的物体";
                return false;
            }

            for (int i = 0; i < references.Length; i++)
            {
                var referenceOrder = new List<string>(eligibleReferences);
                SortByUsage(referenceOrder, objectUsage, rng);
                bool assigned = false;
                SimilarityLevel level = levels[segmentOf[i]];

                for (int ri = 0; ri < referenceOrder.Count && !assigned; ri++)
                {
                    string referenceId = referenceOrder[ri];
                    if (RecentlyUsed(comparisons, i, referenceId, settings.noRepeatWindow))
                        continue;
                    if (settings.familyCooldown > 0 &&
                        FamilyRecentlyUsed(bank, comparisons, i, referenceId,
                                           settings.familyCooldown))
                        continue;

                    string comparisonId = referenceId;
                    if (!isTarget[i])
                    {
                        List<string> candidates = bank.CandidatesFor(referenceId, level);
                        var viable = new List<string>();
                        for (int ci = 0; ci < candidates.Count; ci++)
                        {
                            string candidate = candidates[ci];
                            if (string.IsNullOrEmpty(candidate) || candidate == referenceId)
                                continue;
                            if (RecentlyUsed(comparisons, i, candidate,
                                             settings.noRepeatWindow)) continue;
                            if (settings.familyCooldown > 0 &&
                                FamilyRecentlyUsed(bank, comparisons, i, candidate,
                                                   settings.familyCooldown)) continue;
                            // 该 candidate 未来也可能成为 Reference，因此要求覆盖完整。
                            if (!HasEnoughCandidates(bank, candidate,
                                                     settings.minCandidatesPerLevel)) continue;
                            viable.Add(candidate);
                        }

                        if (viable.Count == 0) continue;
                        SortByUsage(viable, objectUsage, rng);
                        int poolSize = Mathf.Max(1, Mathf.Min(viable.Count, 4));
                        comparisonId = viable[rng.Next(poolSize)];
                    }

                    references[i] = referenceId;
                    comparisons[i] = comparisonId;
                    assigned = true;
                }

                if (!assigned)
                {
                    error = "第 " + i + " 个 pair 找不到满足冷却和结构条件的 Reference/Comparison";
                    return false;
                }
            }

            for (int i = 0; i < references.Length; i++)
            {
                Increment(objectUsage, references[i]);
                Increment(objectUsage, comparisons[i]);
                ObjectDefinition reference = bank.Find(references[i]);
                ObjectDefinition comparison = bank.Find(comparisons[i]);
                if (reference != null) Increment(familyUsage, reference.familyId);
                if (comparison != null) Increment(familyUsage, comparison.familyId);
            }
            return true;
        }

        private static List<string> EligibleReferences(StimulusBank bank,
                                                        TrialGeneratorSettings settings)
        {
            var result = new List<string>();
            for (int i = 0; i < bank.objects.Count; i++)
            {
                string id = bank.objects[i].objectId;
                if (HasEnoughCandidates(bank, id, settings.minCandidatesPerLevel))
                    result.Add(id);
            }
            return result;
        }

        private static bool HasEnoughCandidates(StimulusBank bank, string id, int minPerLevel)
        {
            SimilarityLevel[] levels = ExperimentDesign.ActiveSimilarityLevels;
            for (int i = 0; i < levels.Length; i++)
                if (bank.CandidateCount(id, levels[i]) < minPerLevel) return false;
            return true;
        }

        private static bool RecentlyUsed(string[] comparisons, int upTo,
                                         string candidate, int window)
        {
            if (window <= 0 || string.IsNullOrEmpty(candidate)) return false;
            for (int k = 1; k <= window; k++)
            {
                int index = upTo - k;
                if (index < 0) break;
                if (comparisons[index] == candidate) return true;
            }
            return false;
        }

        private static bool FamilyRecentlyUsed(StimulusBank bank, string[] comparisons,
                                               int upTo, string candidate, int window)
        {
            ObjectDefinition c = bank.Find(candidate);
            if (c == null || string.IsNullOrEmpty(c.familyId) || window <= 0) return false;
            for (int k = 1; k <= window; k++)
            {
                int index = upTo - k;
                if (index < 0) break;
                if (string.IsNullOrEmpty(comparisons[index])) continue;
                ObjectDefinition o = bank.Find(comparisons[index]);
                if (o != null && o.familyId == c.familyId) return true;
            }
            return false;
        }

        private static void SortByUsage(List<string> ids, Dictionary<string, int> usage,
                                        System.Random rng)
        {
            ObjectGenerator.Shuffle(ids, rng);
            ids.Sort((x, y) =>
            {
                int ux, uy;
                usage.TryGetValue(x, out ux);
                usage.TryGetValue(y, out uy);
                return ux.CompareTo(uy);
            });
        }

        private static void AddUnique(List<string> ids, string id)
        {
            if (!string.IsNullOrEmpty(id) && !ids.Contains(id)) ids.Add(id);
        }

        private static void Increment(Dictionary<string, int> map, string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            int value;
            map.TryGetValue(key, out value);
            map[key] = value + 1;
        }

        // ------------------------------------------------------------------ record assembly

        private static BlockPlan Assemble(StimulusBank bank, int blockIndex, int sequenceIndex,
                                          SimilarityLevel[] levels, int[] segmentOf,
                                          int[] withinSegment, bool[] isTarget,
                                          string[] references, string[] comparisons,
                                          float[] referenceRotations,
                                          float[] comparisonRotations, float[] deltas)
        {
            var block = new BlockPlan
            {
                blockIndex = blockIndex,
                sequenceIndex = sequenceIndex,
                sequenceId = ExperimentDesign.BlockSequenceIds[sequenceIndex],
            };
            block.segmentSimilarity.AddRange(levels);
            block.segmentLengths.AddRange(ExperimentDesign.SegmentLengths);

            for (int i = 0; i < references.Length; i++)
            {
                ObjectDefinition reference = bank.Find(references[i]);
                ObjectDefinition comparison = bank.Find(comparisons[i]);
                int segment = segmentOf[i];
                bool atBoundary = segment > 0 && withinSegment[i] == 0;

                var record = new PresentationRecord
                {
                    blockIndex = blockIndex,
                    blockSequenceId = block.sequenceId,
                    presentationIndexInBlock = i,
                    scored = true,
                    trialIndexWithinBlock = i,
                    segmentIndex = segment,
                    trialIndexWithinSegment = withinSegment[i],

                    previousSegmentSimilarity = segment > 0
                        ? levels[segment - 1] : SimilarityLevel.Identical,
                    segmentSimilarity = levels[segment],
                    similarityTransition = segment > 0
                        ? ExperimentDesign.TransitionLabel(levels[segment - 1], levels[segment])
                        : "",
                    isNoOpBoundary = segment > 0 &&
                        ExperimentDesign.IsNoOp(levels[segment - 1], levels[segment]),
                    isFirstTrialAfterBoundary = atBoundary,
                    trialsSinceTransition = segment > 0 ? withinSegment[i] : -1,
                    boundaryPositionWithinBlock = atBoundary ? segment : -1,

                    referenceObjectId = references[i],
                    comparisonObjectId = comparisons[i],
                    referenceFamilyId = reference != null ? reference.familyId : "",
                    comparisonFamilyId = comparison != null ? comparison.familyId : "",
                    partSetId = comparison != null ? comparison.partSetId : "",
                    stimulusSeed = comparison != null ? comparison.seed : 0,
                    partCount = comparison != null ? comparison.parts.Count : 0,
                    referenceRelationSignature = reference != null
                        ? reference.RelationSignature() : "",
                    comparisonRelationSignature = comparison != null
                        ? comparison.RelationSignature() : "",

                    trialPairType = isTarget[i]
                        ? PairClass.Target : PairClassifier.FromLevel(levels[segment]),
                    retainedRelations = reference != null && comparison != null
                        ? (isTarget[i] ? StimConfig.EdgeCount
                                       : reference.RetainedRelationsAgainst(comparison))
                        : -1,

                    referenceRotationX = referenceRotations[i],
                    comparisonRotationX = comparisonRotations[i],
                    rotationDeltaX = deltas[i],
                    conditionRotationAxis = ExperimentDesign.ConditionRotationAxis,
                    presentationAnimationAxis = ExperimentDesign.PresentationAnimationAxis,
                };

                record.structuralDistance = record.retainedRelations >= 0
                    ? StimConfig.EdgeCount - record.retainedRelations : -1;
                record.expectedSame = isTarget[i];
                block.presentations.Add(record);
            }
            return block;
        }
    }
}
