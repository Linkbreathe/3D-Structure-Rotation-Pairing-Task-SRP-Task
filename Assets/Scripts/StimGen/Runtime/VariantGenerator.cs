using System.Collections.Generic;
using UnityEngine;

namespace StimGen
{
    /// <summary>
    /// 模块 3：生成高 / 中 / 低相似变体。
    ///
    /// 一次"移动" = 把一个零件（低相似时可以是一整个分支）从原位置取下，
    /// 接到另一个空位置。零件本身绝不增删或替换，所以 8 个零件的身份与形状始终不变，
    /// 只有 7 条连接里的若干条发生变化。
    ///
    ///   高相似：改 1 条 → 保留 6/7
    ///   中相似：改 2-3 条 → 保留 4-5/7
    ///   低相似：改 4 条以上，或直接重建骨架 → 保留 0-3/7
    /// </summary>
    public static class VariantGenerator
    {
        /// <summary>各等级允许保留的连接数区间（含端点），随零件数自动伸缩。</summary>
        public static void RetainedRange(SimilarityLevel level, out int min, out int max)
        {
            StimConfig.RetainedRange(level, out min, out max);
        }

        /// <summary>
        /// 从 baseDef 派生一个指定相似度的变体。失败返回 null。
        /// </summary>
        public static ObjectDefinition Generate(ObjectDefinition baseDef, SimilarityLevel level,
                                                int seed, ValidationSettings settings,
                                                int maxAttempts = 120, string objectId = null)
        {
            if (level == SimilarityLevel.Identical) return baseDef;

            var rng = new System.Random(seed);
            int minRetained, maxRetained;
            RetainedRange(level, out minRetained, out maxRetained);

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                ObjectDefinition candidate;

                // 低相似有一定概率直接重建骨架（保持同样的 8 个零件）
                bool rebuild = level == SimilarityLevel.Low && rng.Next(100) < 35;
                candidate = rebuild
                    ? RebuildBackbone(baseDef, rng, settings)
                    : ApplyMoves(baseDef, level, rng, settings);

                if (candidate == null) continue;

                int retained = candidate.RetainedRelationsAgainst(baseDef);
                if (retained < minRetained || retained > maxRetained) continue;
                if (candidate.StructureHash() == baseDef.StructureHash()) continue;

                if (!ObjectValidator.Validate(candidate, settings).passed) continue;

                candidate.baseObjectId = baseDef.objectId;
                candidate.derivedLevel = level;
                candidate.retainedEdges = retained;
                candidate.seed = seed;
                candidate.objectId = string.IsNullOrEmpty(objectId)
                    ? baseDef.objectId + "_" + LevelSuffix(level) + (seed & 0xffff).ToString("X4")
                    : objectId;
                return candidate;
            }
            return null;
        }

        public static string LevelSuffix(SimilarityLevel level)
        {
            switch (level)
            {
                case SimilarityLevel.High: return "H";
                case SimilarityLevel.Medium: return "M";
                case SimilarityLevel.Low: return "L";
                default: return "T";
            }
        }

        /// <summary>按等级决定移动几个零件，然后逐个移动。</summary>
        private static ObjectDefinition ApplyMoves(ObjectDefinition baseDef, SimilarityLevel level,
                                                   System.Random rng, ValidationSettings settings)
        {
            int changedMin, changedMax;
            StimConfig.ChangedRange(level, out changedMin, out changedMax);
            int moveCount = changedMin + rng.Next(changedMax - changedMin + 1);

            // 高/中相似优先动"末端零件"；零件少时末端不够用，才退而移动整个分支
            // （移动一个分支同样只改变 1 条连接，零件构成不变）
            bool preferLeaves = level != SimilarityLevel.Low;

            ObjectDefinition def = baseDef.Clone();
            var alreadyMoved = new HashSet<int>();

            for (int m = 0; m < moveCount; m++)
            {
                var movable = MovableIndices(def, preferLeaves, alreadyMoved);
                if (movable.Count == 0 && preferLeaves)
                    movable = MovableIndices(def, false, alreadyMoved);
                if (movable.Count == 0) return null;

                ObjectGenerator.Shuffle(movable, rng);
                bool moved = false;
                for (int i = 0; i < movable.Count && !moved; i++)
                {
                    if (TryMove(def, movable[i], rng))
                    {
                        alreadyMoved.Add(movable[i]);
                        moved = true;
                    }
                }
                if (!moved) return null;
            }

            return ObjectLayout.Solve(def) ? def : null;
        }

        /// <summary>可以被移动的零件：根零件不能动；高/中相似只动末端零件。</summary>
        private static List<int> MovableIndices(ObjectDefinition def, bool leavesOnly,
                                                HashSet<int> exclude)
        {
            var result = new List<int>();
            for (int i = 0; i < def.parts.Count; i++)
            {
                var p = def.parts[i];
                if (p.parentIndex < 0) continue;
                if (exclude.Contains(p.index)) continue;
                if (leavesOnly && !def.IsLeaf(p.index)) continue;
                result.Add(p.index);
            }
            return result;
        }

        /// <summary>
        /// 把一个零件（连同它的分支）改挂到另一个空位置。成功返回 true。
        /// 只改变这一个零件的 (父零件, 方向, 朝向)，因此恰好改变 1 条连接。
        /// </summary>
        public static bool TryMove(ObjectDefinition def, int partIndex, System.Random rng)
        {
            PartNode node = ObjectGenerator.FindByIndex(def, partIndex);
            if (node == null || node.parentIndex < 0) return false;

            int originalParent = node.parentIndex;
            Dir6 originalDir = node.direction;
            PartAxis originalAxis = node.axis;

            // 这个零件自己已经被子零件占用的面，不能再用来对接新的父零件
            var occupiedByChildren = new HashSet<Dir6>();
            for (int i = 0; i < def.parts.Count; i++)
                if (def.parts[i].parentIndex == partIndex)
                    occupiedByChildren.Add(def.parts[i].direction);

            List<KeyValuePair<int, Dir6>> slots = ObjectLayout.FreeSlots(def, partIndex);
            ObjectGenerator.Shuffle(slots, rng);

            var axes = ObjectGenerator.CandidateAxes(node.shape);

            for (int si = 0; si < slots.Count; si++)
            {
                int newParent = slots[si].Key;
                Dir6 newDir = slots[si].Value;

                // 必须是"另一个"空位置，不能原地不动
                if (newParent == originalParent && newDir == originalDir) continue;
                // 对接面不能已经被自己的子零件占着
                if (occupiedByChildren.Contains(Dir6Util.Opposite(newDir))) continue;

                ObjectGenerator.Shuffle(axes, rng);
                for (int ai = 0; ai < axes.Count; ai++)
                {
                    node.parentIndex = newParent;
                    node.direction = newDir;
                    node.axis = axes[ai];

                    if (ObjectLayout.Solve(def) && NoOverlapExceptTree(def))
                        return true;
                }
            }

            node.parentIndex = originalParent;
            node.direction = originalDir;
            node.axis = originalAxis;
            ObjectLayout.Solve(def);
            return false;
        }

        /// <summary>快速重叠预筛（正式判定仍由 ObjectValidator 负责）。</summary>
        private static bool NoOverlapExceptTree(ObjectDefinition def)
        {
            float budget = ShapeMetrics.TargetVolume * 0.02f;
            for (int i = 0; i < def.parts.Count; i++)
            {
                for (int j = i + 1; j < def.parts.Count; j++)
                {
                    if (ObjectLayout.AreAdjacent(def, def.parts[i].index, def.parts[j].index)) continue;
                    if (ShapeSdf.OverlapVolume(def.parts[i], def.parts[j], 0.03f) > budget) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 低相似的另一条路径：保留同样的 8 个零件（身份与形状不变），
        /// 但把整棵连接树重新随机搭一遍。
        /// </summary>
        private static ObjectDefinition RebuildBackbone(ObjectDefinition baseDef, System.Random rng,
                                                        ValidationSettings settings)
        {
            var shapesByIndex = new Dictionary<int, PartShape>();
            for (int i = 0; i < baseDef.parts.Count; i++)
                shapesByIndex[baseDef.parts[i].index] = baseDef.parts[i].shape;

            var order = new List<int>(shapesByIndex.Keys);
            ObjectGenerator.Shuffle(order, rng);

            var def = new ObjectDefinition();
            var rootNode = new PartNode
            {
                index = order[0],
                shape = shapesByIndex[order[0]],
                parentIndex = -1,
                axis = ObjectGenerator.RandomAxis(rng, shapesByIndex[order[0]]),
                localPosition = Vector3.zero,
            };
            rootNode.localEuler = ShapeMetrics.AxisEuler(rootNode.axis);
            def.parts.Add(rootNode);

            for (int i = 1; i < order.Count; i++)
            {
                if (!AttachExisting(def, order[i], shapesByIndex[order[i]], rng)) return null;
            }
            return ObjectLayout.Solve(def) ? def : null;
        }

        private static bool AttachExisting(ObjectDefinition def, int index, PartShape shape,
                                           System.Random rng)
        {
            List<KeyValuePair<int, Dir6>> slots = ObjectLayout.FreeSlots(def);
            ObjectGenerator.Shuffle(slots, rng);
            var axes = ObjectGenerator.CandidateAxes(shape);

            for (int si = 0; si < slots.Count; si++)
            {
                PartNode parent = ObjectGenerator.FindByIndex(def, slots[si].Key);
                ObjectGenerator.Shuffle(axes, rng);
                for (int ai = 0; ai < axes.Count; ai++)
                {
                    var candidate = new PartNode
                    {
                        index = index,
                        shape = shape,
                        parentIndex = slots[si].Key,
                        direction = slots[si].Value,
                        axis = axes[ai],
                    };
                    candidate.localPosition = ObjectGenerator.ChildPosition(parent, candidate);
                    candidate.localEuler = ShapeMetrics.AxisEuler(candidate.axis);

                    if (ObjectGenerator.FitsWithout(def, candidate, slots[si].Key))
                    {
                        def.parts.Add(candidate);
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
