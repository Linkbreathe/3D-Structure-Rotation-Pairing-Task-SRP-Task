using System;
using System.Collections.Generic;
using UnityEngine;

namespace StimGen
{
    /// <summary>
    /// 一个物体家族：一个基准物体，加上从它派生的 High / Medium / Low 版本。
    /// 同一家族的所有版本使用完全相同的四个零件。
    /// </summary>
    [Serializable]
    public class FamilyDefinition
    {
        public string familyId;
        public int seed;
        public string baseObjectId;
        public List<string> memberObjectIds = new List<string>();
    }

    /// <summary>
    /// 配对矩阵的一格：参照物体 a 与当前物体 b 之间已验证的关系。
    /// 只保存可用的配对（High / Medium / Low），Invalid 不入库。
    /// </summary>
    [Serializable]
    public class PairEntry
    {
        public string a;
        public string b;
        public SimilarityLevel level;
        public int retainedRelations;
        public float iouMin = -1f;
        public float iouMax = -1f;
    }

    /// <summary>
    /// 冻结后的刺激库：模型 + Object ID + Family ID + Seed + 配对关系表。
    ///
    /// 关键设计：Pairing 里任意正式物体都可能成为 Reference，
    /// 所以必须有**全部正式物体两两之间**的关系表。
    /// Trial Generator 只允许从这张表里挑候选。
    /// </summary>
    [Serializable]
    public class StimulusBank
    {
        public string generatedUtc = "";
        public int masterSeed;
        public string partSetId = "";
        public int partCount;
        public bool visualChecksRun;
        public string buildReport = "";

        public List<ObjectDefinition> objects = new List<ObjectDefinition>();
        public List<ObjectDefinition> practiceObjects = new List<ObjectDefinition>();
        public List<FamilyDefinition> families = new List<FamilyDefinition>();

        /// <summary>已验证可用的配对。对称存储：a→b 和 b→a 各存一条，查表更快。</summary>
        public List<PairEntry> pairs = new List<PairEntry>();

        [NonSerialized] private Dictionary<string, ObjectDefinition> _byId;
        [NonSerialized] private Dictionary<string, List<string>[]> _candidates;

        /// <summary>反序列化后必须调用一次，重建查表索引。</summary>
        public void BuildIndex()
        {
            _byId = new Dictionary<string, ObjectDefinition>(objects.Count + practiceObjects.Count);
            for (int i = 0; i < objects.Count; i++) _byId[objects[i].objectId] = objects[i];
            for (int i = 0; i < practiceObjects.Count; i++) _byId[practiceObjects[i].objectId] = practiceObjects[i];

            _candidates = new Dictionary<string, List<string>[]>(objects.Count);
            for (int i = 0; i < objects.Count; i++)
                _candidates[objects[i].objectId] = new[]
                {
                    new List<string>(), new List<string>(), new List<string>(), new List<string>(),
                };

            for (int i = 0; i < pairs.Count; i++)
            {
                PairEntry p = pairs[i];
                List<string>[] byLevel;
                if (_candidates.TryGetValue(p.a, out byLevel))
                    byLevel[(int)p.level].Add(p.b);
            }
        }

        public ObjectDefinition Find(string objectId)
        {
            if (_byId == null) BuildIndex();
            ObjectDefinition def;
            return _byId.TryGetValue(objectId, out def) ? def : null;
        }

        /// <summary>参照物体 referenceId 在指定相似度下，所有已验证的合法候选。</summary>
        public List<string> CandidatesFor(string referenceId, SimilarityLevel level)
        {
            if (_candidates == null) BuildIndex();
            List<string>[] byLevel;
            if (!_candidates.TryGetValue(referenceId, out byLevel)) return new List<string>();
            return byLevel[(int)level];
        }

        public int CandidateCount(string referenceId, SimilarityLevel level)
        {
            return CandidatesFor(referenceId, level).Count;
        }

        /// <summary>
        /// 覆盖度报告：每个正式物体在三个等级上各有几个候选。
        /// 计划要求每个物体每级至少 2 个，否则它不能出现在会继续成为参照的位置。
        /// </summary>
        public string CoverageReport(int minRequired = 2)
        {
            var levels = new[] { SimilarityLevel.High, SimilarityLevel.Medium, SimilarityLevel.Low };
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("配对覆盖度（每个正式物体的候选数）：");

            for (int li = 0; li < levels.Length; li++)
            {
                int deficient = 0, zero = 0, total = 0, min = int.MaxValue, max = 0;
                for (int i = 0; i < objects.Count; i++)
                {
                    int n = CandidateCount(objects[i].objectId, levels[li]);
                    total += n;
                    if (n < min) min = n;
                    if (n > max) max = n;
                    if (n == 0) zero++;
                    if (n < minRequired) deficient++;
                }
                if (objects.Count == 0) { min = 0; }
                sb.AppendLine("  " + levels[li] + "：平均 " +
                              (objects.Count > 0 ? (total / (float)objects.Count).ToString("F1") : "0") +
                              "，最少 " + (min == int.MaxValue ? 0 : min) + "，最多 " + max +
                              "，不足 " + minRequired + " 个的物体 " + deficient +
                              "（其中 0 个候选的 " + zero + "）");
            }
            return sb.ToString();
        }

        /// <summary>
        /// "每种零件作为中心零件 / 末端零件的次数尽量相同"的实测统计。
        /// 度数 1 = 末端零件；度数 ≥2 = 中心零件。
        /// </summary>
        public string PartRoleBalanceReport()
        {
            int shapeCount = StimConfig.ShapesInUse.Length;
            var central = new int[4];
            var terminal = new int[4];

            for (int i = 0; i < objects.Count; i++)
            {
                ObjectDefinition o = objects[i];
                for (int p = 0; p < o.parts.Count; p++)
                {
                    int degree = o.DegreeOf(o.parts[p].index);
                    if (degree >= 2) central[(int)o.parts[p].shape]++;
                    else terminal[(int)o.parts[p].shape]++;
                }
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("零件角色平衡（中心 = 度数≥2，末端 = 度数1）：");
            for (int s = 0; s < shapeCount; s++)
            {
                PartShape shape = StimConfig.ShapesInUse[s];
                int c = central[(int)shape], t = terminal[(int)shape];
                int sum = c + t;
                sb.AppendLine("  " + shape + "：中心 " + c + " / 末端 " + t +
                              (sum > 0 ? "（中心占 " + (100f * c / sum).ToString("F0") + "%）" : ""));
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// 计算两个物体之间的配对类型。
    ///
    /// 这里同时执行两道关卡：
    ///   程序关卡 —— 保留的空间关系数必须正好是 2 / 1 / 0；
    ///   视觉关卡 —— 建库时 Y轴 0°/45°/90° 三个观察角度的轮廓重合度必须落在该等级区间且三角度一致。
    /// 两道都过才算合法配对，否则一律 Invalid。
    /// </summary>
    public static class PairClassifier
    {
        /// <summary>只看结构关系，不做视觉检查。</summary>
        public static PairClass ClassifyStructural(ObjectDefinition a, ObjectDefinition b)
        {
            if (a.objectId == b.objectId) return PairClass.Target;

            int retained = a.RetainedRelationsAgainst(b);

            // 关系完全相同但零件朝向不同：结构上是同一个东西，看起来却不一样。
            // 这种配对既不能当 Target（不是同一个 Object ID），也不该当 Non-target，直接排除。
            if (retained >= StimConfig.EdgeCount) return PairClass.Invalid;

            var levels = new[] { SimilarityLevel.High, SimilarityLevel.Medium, SimilarityLevel.Low };
            for (int i = 0; i < levels.Length; i++)
            {
                int min, max;
                StimConfig.RetainedRange(levels[i], out min, out max);
                if (retained >= min && retained <= max) return FromLevel(levels[i]);
            }
            return PairClass.Invalid;
        }

        public static SimilarityLevel ToLevel(PairClass pc)
        {
            switch (pc)
            {
                case PairClass.HighNonTarget: return SimilarityLevel.High;
                case PairClass.MediumNonTarget: return SimilarityLevel.Medium;
                case PairClass.LowNonTarget: return SimilarityLevel.Low;
                default: return SimilarityLevel.Identical;
            }
        }

        public static PairClass FromLevel(SimilarityLevel level)
        {
            switch (level)
            {
                case SimilarityLevel.High: return PairClass.HighNonTarget;
                case SimilarityLevel.Medium: return PairClass.MediumNonTarget;
                case SimilarityLevel.Low: return PairClass.LowNonTarget;
                default: return PairClass.Target;
            }
        }
    }
}
