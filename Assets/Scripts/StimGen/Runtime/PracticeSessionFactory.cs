using System;
using System.Collections.Generic;
using UnityEngine;

namespace StimGen
{
    /// <summary>从冻结题库中的独立练习物体生成一个固定、可复现的 Pairing 练习 Session。</summary>
    public static class PracticeSessionFactory
    {
        public const int DefaultScoredTrials = 12;
        public const int MinimumScoredTrials = 10;
        public const int MaximumScoredTrials = 15;
        public const string PracticeParticipantId = "PRACTICE";

        public static SessionPlan Build(StimulusBank bank,
                                        int scoredTrials = DefaultScoredTrials)
        {
            if (bank == null || bank.practiceObjects == null ||
                bank.practiceObjects.Count < 4)
                throw new InvalidOperationException("题库中至少需要 4 个独立练习物体。");

            scoredTrials = Mathf.Clamp(scoredTrials, MinimumScoredTrials,
                                       MaximumScoredTrials);
            bank.BuildIndex();

            var plan = new SessionPlan
            {
                participantId = PracticeParticipantId,
                participantNumber = 0,
                masterSeed = bank.masterSeed + 91027,
                generatedUtc = DateTime.UtcNow.ToString("o",
                    System.Globalization.CultureInfo.InvariantCulture),
                bankId = bank.generatedUtc,
                taskProtocolVersion = ExperimentDesign.TaskProtocolVersion,
                rotationProtocolVersion = ExperimentDesign.RotationProtocolVersion,
                swapResponseKeys = false,
            };
            plan.blockOrder.Add(0);

            var block = new BlockPlan
            {
                blockIndex = 0,
                sequenceIndex = -1,
                sequenceId = "PRACTICE",
            };
            block.segmentSimilarity.Add(SimilarityLevel.Identical);
            block.segmentLengths.Add(scoredTrials);

            SimilarityLevel[] desiredLevels = ExperimentDesign.ActiveSimilarityLevels;
            var objectExposures = new Dictionary<string, int>();
            var familyExposures = new Dictionary<string, int>();
            var lastObjectTrial = new Dictionary<string, int>();
            var lastFamilyTrial = new Dictionary<string, int>();
            var used = new List<string>();

            for (int scored = 0; scored < scoredTrials; scored++)
            {
                bool target = scored % 3 == 1;
                ObjectDefinition reference;
                ObjectDefinition comparison;
                if (target)
                {
                    reference = bank.practiceObjects[scored % bank.practiceObjects.Count];
                    comparison = reference;
                }
                else
                {
                    SimilarityLevel desired =
                        desiredLevels[(scored / 3) % desiredLevels.Length];
                    if (!TryFindPracticePair(bank.practiceObjects, desired, scored,
                                             out reference, out comparison))
                        throw new InvalidOperationException(
                            "练习物体中找不到 " + desired +
                            " structural-similarity Non-target 配对。");
                }

                PairClass pairClass = PairClassifier.ClassifyStructural(reference, comparison);
                if (pairClass == PairClass.Invalid ||
                    (target && pairClass != PairClass.Target) ||
                    (!target && pairClass == PairClass.Target))
                    throw new InvalidOperationException("练习 pair 的结构分类无效。");

                float delta = ExperimentDesign.RotationOptions[
                    scored % ExperimentDesign.RotationOptions.Length];
                int retained = pairClass == PairClass.Target
                    ? StimConfig.EdgeCount : reference.RetainedRelationsAgainst(comparison);
                int referencePrior = GetCount(objectExposures, reference.objectId);
                int comparisonPrior = GetCount(objectExposures, comparison.objectId);
                int referenceFamilyPrior = GetCount(familyExposures, reference.familyId);
                int comparisonFamilyPrior = GetCount(familyExposures, comparison.familyId);

                var record = new PresentationRecord
                {
                    blockIndex = 0,
                    blockSequenceId = block.sequenceId,
                    presentationIndexInBlock = scored,
                    scored = true,
                    trialIndexGlobal = scored,
                    trialIndexWithinBlock = scored,
                    segmentIndex = 0,
                    trialIndexWithinSegment = scored,

                    previousSegmentSimilarity = SimilarityLevel.Identical,
                    segmentSimilarity = PairClassifier.ToLevel(pairClass),
                    similarityTransition = "",
                    isNoOpBoundary = false,
                    isFirstTrialAfterBoundary = false,
                    trialsSinceTransition = -1,
                    boundaryPositionWithinBlock = -1,

                    referenceObjectId = reference.objectId,
                    comparisonObjectId = comparison.objectId,
                    referenceFamilyId = reference.familyId,
                    comparisonFamilyId = comparison.familyId,
                    partSetId = comparison.partSetId,
                    stimulusBankVersion = bank.generatedUtc,
                    stimulusSeed = comparison.seed,
                    partCount = comparison.parts.Count,
                    referenceRelationSignature = reference.RelationSignature(),
                    comparisonRelationSignature = comparison.RelationSignature(),
                    trialPairType = pairClass,
                    retainedRelations = retained,
                    structuralDistance = StimConfig.EdgeCount - retained,

                    referenceRotationX = 0f,
                    comparisonRotationX = Mathf.Repeat(delta, 360f),
                    rotationDeltaX = delta,
                    conditionRotationAxis = ExperimentDesign.ConditionRotationAxis,
                    presentationAnimationAxis = ExperimentDesign.PresentationAnimationAxis,

                    referenceObjectPriorExposures = referencePrior,
                    comparisonObjectPriorExposures = comparisonPrior,
                    referenceFamilyPriorExposures = referenceFamilyPrior,
                    comparisonFamilyPriorExposures = comparisonFamilyPrior,
                    trialsSinceReferenceObjectLastSeen = Since(
                        lastObjectTrial, reference.objectId, scored),
                    trialsSinceComparisonObjectLastSeen = Since(
                        lastObjectTrial, comparison.objectId, scored),
                    trialsSinceReferenceFamilyLastSeen = Since(
                        lastFamilyTrial, reference.familyId, scored),
                    trialsSinceComparisonFamilyLastSeen = Since(
                        lastFamilyTrial, comparison.familyId, scored),
                    expectedSame = pairClass == PairClass.Target,
                };

                block.presentations.Add(record);
                AddUnique(used, reference.objectId);
                AddUnique(used, comparison.objectId);

                Increment(objectExposures, reference.objectId);
                Increment(objectExposures, comparison.objectId);
                Increment(familyExposures, reference.familyId);
                Increment(familyExposures, comparison.familyId);
                lastObjectTrial[reference.objectId] = scored;
                lastObjectTrial[comparison.objectId] = scored;
                lastFamilyTrial[reference.familyId] = scored;
                lastFamilyTrial[comparison.familyId] = scored;
            }

            plan.blocks.Add(block);
            used.Sort(StringComparer.Ordinal);
            for (int i = 0; i < used.Count; i++)
            {
                ObjectDefinition def = bank.Find(used[i]);
                if (def != null) plan.objects.Add(def);
            }
            plan.BuildIndex();
            return plan;
        }

        public static bool Validate(SessionPlan plan, out string report)
        {
            var errors = new List<string>();
            if (plan == null) errors.Add("Session 为空");
            else
            {
                if (!string.Equals(plan.participantId, PracticeParticipantId,
                                   StringComparison.OrdinalIgnoreCase))
                    errors.Add("ParticipantID 不是 PRACTICE");
                if (plan.taskProtocolVersion != ExperimentDesign.TaskProtocolVersion)
                    errors.Add("任务协议不匹配");
                if (plan.rotationProtocolVersion != ExperimentDesign.RotationProtocolVersion)
                    errors.Add("旋转协议不匹配");
                if (plan.blocks == null || plan.blocks.Count != 1)
                    errors.Add("练习 Session 必须只有 1 个 Block");
                if (plan.ScoredTrialCount() < MinimumScoredTrials ||
                    plan.ScoredTrialCount() > MaximumScoredTrials)
                    errors.Add("练习 scored trials 必须为 10–15 个");

                plan.BuildIndex();
                for (int i = 0; i < plan.objects.Count; i++)
                    if (!plan.objects[i].isPractice)
                        errors.Add("练习 Session 混入正式物体 " + plan.objects[i].objectId);

                if (plan.blocks != null && plan.blocks.Count == 1)
                {
                    List<PresentationRecord> rows = plan.blocks[0].presentations;
                    if (rows.Count != plan.ScoredTrialCount())
                        errors.Add("练习 Session 必须全部由完整 Pairing trials 组成");

                    int targetCount = 0;
                    int highCount = 0;
                    int lowCount = 0;
                    int rotation0Count = 0;
                    int rotation180Count = 0;
                    for (int i = 0; i < rows.Count; i++)
                    {
                        PresentationRecord p = rows[i];
                        if (!p.scored || p.presentationIndexInBlock != i ||
                            p.trialIndexWithinBlock != i || p.trialIndexGlobal != i)
                            errors.Add("trial " + i + " 的 index/scored 标记不正确");
                        ObjectDefinition reference = plan.Find(p.referenceObjectId);
                        ObjectDefinition comparison = plan.Find(p.comparisonObjectId);
                        if (reference == null || comparison == null)
                        {
                            errors.Add("trial " + i + " 的 Reference/Comparison 不在 Session 物体表中");
                            continue;
                        }

                        PairClass actual = PairClassifier.ClassifyStructural(reference, comparison);
                        if (actual != p.trialPairType)
                            errors.Add("trial " + i + " 的 pair 分类不正确");
                        if (p.expectedSame != (actual == PairClass.Target))
                            errors.Add("trial " + i + " 的正确答案不正确");
                        if (actual != PairClass.Target &&
                            !IsActiveSimilarity(PairClassifier.ToLevel(actual)))
                            errors.Add("trial " + i + " 使用了非活动的 similarity level");
                        if (actual == PairClass.Target) targetCount++;
                        else if (PairClassifier.ToLevel(actual) == SimilarityLevel.High)
                            highCount++;
                        else if (PairClassifier.ToLevel(actual) == SimilarityLevel.Low)
                            lowCount++;
                        if (!IsRotationOption(p.rotationDeltaX))
                            errors.Add("trial " + i + " 使用了计划外的 rotation delta");
                        if (Mathf.Approximately(p.rotationDeltaX, 0f)) rotation0Count++;
                        if (Mathf.Approximately(p.rotationDeltaX, 180f)) rotation180Count++;
                        float expectedX = Mathf.Repeat(
                            p.referenceRotationX + p.rotationDeltaX, 360f);
                        if (Mathf.Abs(Mathf.DeltaAngle(expectedX,
                                                       p.comparisonRotationX)) > 0.001f)
                            errors.Add("trial " + i + " 的 pair rotation 不正确");
                    }

                    if (rows.Count == DefaultScoredTrials)
                    {
                        if (targetCount != 4 || highCount != 4 || lowCount != 4)
                            errors.Add("12-trial 练习必须包含 4 Target / 4 High / 4 Low");
                        if (rotation0Count != 6 || rotation180Count != 6)
                            errors.Add("12-trial 练习必须包含 6 个 0° / 6 个 180°");
                    }
                }
            }

            report = errors.Count == 0
                ? "PASSED：独立 Pairing 练习 Session；" + plan.ScoredTrialCount() +
                  " 个完整 trials；无初始化呈现；全部使用练习物体。"
                : "FAILED：" + string.Join("；", errors.ToArray());
            return errors.Count == 0;
        }

        private static bool IsActiveSimilarity(SimilarityLevel level)
        {
            SimilarityLevel[] levels = ExperimentDesign.ActiveSimilarityLevels;
            for (int i = 0; i < levels.Length; i++)
                if (levels[i] == level) return true;
            return false;
        }

        private static bool IsRotationOption(float angle)
        {
            float[] options = ExperimentDesign.RotationOptions;
            for (int i = 0; i < options.Length; i++)
                if (Mathf.Approximately(options[i], angle)) return true;
            return false;
        }

        private static bool TryFindPracticePair(
            List<ObjectDefinition> objects, SimilarityLevel desired, int offset,
            out ObjectDefinition reference, out ObjectDefinition comparison)
        {
            reference = null;
            comparison = null;
            for (int r = 0; r < objects.Count; r++)
            {
                ObjectDefinition candidateReference =
                    objects[(offset + r) % objects.Count];
                for (int n = 0; n < objects.Count; n++)
                {
                    ObjectDefinition candidate =
                        objects[(offset + r + n + 1) % objects.Count];
                    PairClass pc = PairClassifier.ClassifyStructural(
                        candidateReference, candidate);
                    if (pc == PairClass.Invalid || pc == PairClass.Target) continue;
                    if (PairClassifier.ToLevel(pc) != desired) continue;
                    reference = candidateReference;
                    comparison = candidate;
                    return true;
                }
            }
            return false;
        }

        private static int GetCount(Dictionary<string, int> map, string key)
        {
            int value;
            return map.TryGetValue(key, out value) ? value : 0;
        }

        private static int Since(Dictionary<string, int> lastSeen, string key, int current)
        {
            int previous;
            return lastSeen.TryGetValue(key, out previous) ? current - previous - 1 : -1;
        }

        private static void Increment(Dictionary<string, int> map, string key)
        {
            int value;
            map.TryGetValue(key, out value);
            map[key] = value + 1;
        }

        private static void AddUnique(List<string> ids, string id)
        {
            if (!ids.Contains(id)) ids.Add(id);
        }
    }
}
