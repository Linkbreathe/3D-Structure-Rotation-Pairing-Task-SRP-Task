using System;
using System.Collections.Generic;
using UnityEngine;

namespace StimGen
{
    /// <summary>模块 2：自动拼装 8 个零件。</summary>
    public static class ObjectGenerator
    {
        /// <summary>
        /// 生成一个合格的基础物体。失败返回 null（seed 太"背"时调用方换 seed 即可）。
        /// 流程：放第 1 个零件 → 在已有零件的空面上接第 2 个 → …… → 接满 8 个 → 检查是否合格。
        /// </summary>
        public static ObjectDefinition Generate(int seed, ValidationSettings settings,
                                                int maxAttempts = 60, string objectId = null)
        {
            var rng = new System.Random(seed);

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                ObjectDefinition def = TryAssemble(rng);
                if (def == null) continue;

                def.seed = seed;
                def.objectId = string.IsNullOrEmpty(objectId) ? MakeId(seed) : objectId;

                ValidationReport report = ObjectValidator.Validate(def, settings);
                if (report.passed) return def;
            }
            return null;
        }

        public static string MakeId(int seed)
        {
            return "OBJ" + (seed & 0x7fffffff).ToString("D8");
        }

        /// <summary>一次拼装尝试：逐个连接零件，任何一步无处可放就放弃本次尝试。</summary>
        private static ObjectDefinition TryAssemble(System.Random rng)
        {
            var shapes = ShapePool(rng);
            var def = new ObjectDefinition();

            var root = new PartNode
            {
                index = 0,
                shape = shapes[0],
                parentIndex = -1,
                axis = RandomAxis(rng, shapes[0]),
                localPosition = Vector3.zero,
            };
            root.localEuler = ShapeMetrics.AxisEuler(root.axis);
            def.parts.Add(root);

            for (int i = 1; i < StimConfig.PartCount; i++)
            {
                if (!TryAttachOne(def, shapes[i], i, rng)) return null;
            }
            return def;
        }

        /// <summary>在当前结构的所有空面里找一个能放下新零件的位置。</summary>
        private static bool TryAttachOne(ObjectDefinition def, PartShape shape, int newIndex,
                                         System.Random rng)
        {
            List<KeyValuePair<int, Dir6>> slots = ObjectLayout.FreeSlots(def);
            Shuffle(slots, rng);

            var axes = CandidateAxes(shape);
            for (int si = 0; si < slots.Count; si++)
            {
                int parentIndex = slots[si].Key;
                Dir6 dir = slots[si].Value;
                PartNode parent = FindByIndex(def, parentIndex);

                Shuffle(axes, rng);
                for (int ai = 0; ai < axes.Count; ai++)
                {
                    var candidate = new PartNode
                    {
                        index = newIndex,
                        shape = shape,
                        parentIndex = parentIndex,
                        direction = dir,
                        axis = axes[ai],
                    };
                    candidate.localPosition = ChildPosition(parent, candidate);
                    candidate.localEuler = ShapeMetrics.AxisEuler(candidate.axis);

                    if (FitsWithout(def, candidate, parentIndex))
                    {
                        def.parts.Add(candidate);
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>子零件中心 = 父中心 + 方向 ×(父半长 + 子半长 − 咬合量)。</summary>
        public static Vector3 ChildPosition(PartNode parent, PartNode child)
        {
            Vector3 parentHalf = ShapeMetrics.HalfExtents(parent.shape, parent.axis);
            Vector3 childHalf = ShapeMetrics.HalfExtents(child.shape, child.axis);
            int axisIdx = Dir6Util.AxisIndex(child.direction);
            float distance = parentHalf[axisIdx] + childHalf[axisIdx] - ShapeMetrics.ContactOverlap;
            return parent.localPosition + Dir6Util.ToVector(child.direction) * distance;
        }

        /// <summary>候选零件是否与除父零件外的所有已放置零件都不重叠。</summary>
        public static bool FitsWithout(ObjectDefinition def, PartNode candidate, int parentIndex)
        {
            float budget = ShapeMetrics.TargetVolume * 0.02f;
            for (int i = 0; i < def.parts.Count; i++)
            {
                var other = def.parts[i];
                if (other.index == parentIndex) continue;
                if (other.index == candidate.index) continue;
                if (ShapeSdf.OverlapVolume(other, candidate, 0.03f) > budget) return false;
            }
            return true;
        }

        /// <summary>按 StimConfig 的配额取零件（默认 4 种形状各 1 个），打乱顺序。</summary>
        private static List<PartShape> ShapePool(System.Random rng)
        {
            var pool = new List<PartShape>(StimConfig.PartCount);
            for (int s = 0; s < StimConfig.ShapesInUse.Length; s++)
                for (int c = 0; c < StimConfig.CopiesPerShape; c++)
                    pool.Add(StimConfig.ShapesInUse[s]);
            Shuffle(pool, rng);
            return pool;
        }

        public static List<PartAxis> CandidateAxes(PartShape shape)
        {
            if (!ShapeMetrics.AxisMatters(shape)) return new List<PartAxis> { PartAxis.Y };
            return new List<PartAxis> { PartAxis.X, PartAxis.Y, PartAxis.Z };
        }

        public static PartAxis RandomAxis(System.Random rng, PartShape shape)
        {
            var axes = CandidateAxes(shape);
            return axes[rng.Next(axes.Count)];
        }

        public static PartNode FindByIndex(ObjectDefinition def, int index)
        {
            for (int i = 0; i < def.parts.Count; i++)
                if (def.parts[i].index == index) return def.parts[i];
            return null;
        }

        public static void Shuffle<T>(IList<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }
}
