using System.Collections.Generic;
using UnityEngine;

namespace StimGen
{
    /// <summary>
    /// 把"树 + 方向"的拓扑描述解算成实际坐标。
    ///
    /// 摆放规则：子零件中心 = 父零件中心 + 方向 ×(父半长 + 子半长 − 咬合量)。
    /// 因为连接轴穿过两个零件的中心，而轴对齐二次曲面沿主轴的外延正好等于半长，
    /// 所以两个相邻零件一定沿这条轴真实相交 ContactOverlap 那么深 ——
    /// "全部连接、不悬空"是几何保证的，不靠碰撞试错。
    /// </summary>
    public static class ObjectLayout
    {
        /// <summary>
        /// 就地解算所有零件的 localPosition / localEuler，并把整体重心化到包围盒中心。
        /// 拓扑非法（森林、成环、缺根）时返回 false。
        /// </summary>
        public static bool Solve(ObjectDefinition def)
        {
            int n = def.parts.Count;
            if (n == 0) return false;

            int root = def.RootIndex();
            if (root < 0) return false;

            var byIndex = new Dictionary<int, PartNode>(n);
            for (int i = 0; i < n; i++)
            {
                if (byIndex.ContainsKey(def.parts[i].index)) return false;
                byIndex[def.parts[i].index] = def.parts[i];
            }

            var childrenOf = new Dictionary<int, List<int>>(n);
            for (int i = 0; i < n; i++)
            {
                var p = def.parts[i];
                if (p.parentIndex < 0) continue;
                if (!byIndex.ContainsKey(p.parentIndex)) return false;
                List<int> list;
                if (!childrenOf.TryGetValue(p.parentIndex, out list))
                {
                    list = new List<int>();
                    childrenOf[p.parentIndex] = list;
                }
                list.Add(p.index);
            }

            var rootNode = byIndex[def.parts[root].index];
            rootNode.localPosition = Vector3.zero;
            rootNode.localEuler = ShapeMetrics.AxisEuler(rootNode.axis);

            int visited = 1;
            var queue = new Queue<int>();
            queue.Enqueue(rootNode.index);

            while (queue.Count > 0)
            {
                int pi = queue.Dequeue();
                var parent = byIndex[pi];
                List<int> kids;
                if (!childrenOf.TryGetValue(pi, out kids)) continue;

                Vector3 parentHalf = ShapeMetrics.HalfExtents(parent.shape, parent.axis);
                for (int k = 0; k < kids.Count; k++)
                {
                    var child = byIndex[kids[k]];
                    Vector3 childHalf = ShapeMetrics.HalfExtents(child.shape, child.axis);
                    int axisIdx = Dir6Util.AxisIndex(child.direction);
                    float distance = parentHalf[axisIdx] + childHalf[axisIdx] - ShapeMetrics.ContactOverlap;

                    child.localPosition = parent.localPosition +
                                          Dir6Util.ToVector(child.direction) * distance;
                    child.localEuler = ShapeMetrics.AxisEuler(child.axis);

                    visited++;
                    if (visited > n) return false; // 成环
                    queue.Enqueue(child.index);
                }
            }

            if (visited != n) return false; // 森林 / 孤立零件

            Recenter(def);
            return true;
        }

        /// <summary>把整体平移，使包围盒中心落在原点（旋转与轮廓比较都以此为基准）。</summary>
        public static void Recenter(ObjectDefinition def)
        {
            Bounds b = ComputeBounds(def);
            Vector3 offset = b.center;
            for (int i = 0; i < def.parts.Count; i++)
                def.parts[i].localPosition -= offset;
        }

        /// <summary>所有零件包围盒的并集。</summary>
        public static Bounds ComputeBounds(ObjectDefinition def)
        {
            Bounds b = ShapeSdf.PartBounds(def.parts[0]);
            for (int i = 1; i < def.parts.Count; i++)
                b.Encapsulate(ShapeSdf.PartBounds(def.parts[i]));
            return b;
        }

        /// <summary>包围球半径（从原点算），用于统一相机距离与"整体大小一致"检查。</summary>
        public static float BoundingRadius(ObjectDefinition def)
        {
            float max = 0f;
            for (int i = 0; i < def.parts.Count; i++)
            {
                var p = def.parts[i];
                Vector3 h = ShapeMetrics.HalfExtents(p.shape, p.axis);
                max = Mathf.Max(max, p.localPosition.magnitude + h.magnitude);
            }
            return max;
        }

        /// <summary>某个零件的某个面是否已被占用（被父零件或某个子零件占着）。</summary>
        public static bool IsSlotOccupied(ObjectDefinition def, int partIndex, Dir6 dir)
        {
            for (int i = 0; i < def.parts.Count; i++)
            {
                var p = def.parts[i];
                if (p.index == partIndex && p.parentIndex >= 0 &&
                    Dir6Util.Opposite(p.direction) == dir) return true;   // 朝向自己父亲的那一面
                if (p.parentIndex == partIndex && p.direction == dir) return true; // 已有子零件
            }
            return false;
        }

        /// <summary>收集所有 (零件, 空闲方向) 的可挂载位置。</summary>
        public static List<KeyValuePair<int, Dir6>> FreeSlots(ObjectDefinition def, int excludeSubtreeRoot = -1)
        {
            var excluded = excludeSubtreeRoot >= 0 ? SubtreeIndices(def, excludeSubtreeRoot) : null;
            var slots = new List<KeyValuePair<int, Dir6>>();
            for (int i = 0; i < def.parts.Count; i++)
            {
                int idx = def.parts[i].index;
                if (excluded != null && excluded.Contains(idx)) continue;
                for (int d = 0; d < Dir6Util.All.Length; d++)
                {
                    Dir6 dir = Dir6Util.All[d];
                    if (!IsSlotOccupied(def, idx, dir))
                        slots.Add(new KeyValuePair<int, Dir6>(idx, dir));
                }
            }
            return slots;
        }

        /// <summary>某个零件及其全部后代的索引集合。</summary>
        public static HashSet<int> SubtreeIndices(ObjectDefinition def, int rootIndex)
        {
            var set = new HashSet<int> { rootIndex };
            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int i = 0; i < def.parts.Count; i++)
                {
                    var p = def.parts[i];
                    if (p.parentIndex >= 0 && set.Contains(p.parentIndex) && !set.Contains(p.index))
                    {
                        set.Add(p.index);
                        grew = true;
                    }
                }
            }
            return set;
        }

        /// <summary>两个零件在树上是否直接相连。</summary>
        public static bool AreAdjacent(ObjectDefinition def, int a, int b)
        {
            for (int i = 0; i < def.parts.Count; i++)
            {
                var p = def.parts[i];
                if (p.index == a && p.parentIndex == b) return true;
                if (p.index == b && p.parentIndex == a) return true;
            }
            return false;
        }
    }
}
