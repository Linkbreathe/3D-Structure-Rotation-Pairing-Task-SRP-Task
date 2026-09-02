using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StimGen
{
    /// <summary>4 种基础零件形状，每个物体各用 2 个。</summary>
    public enum PartShape
    {
        Cube = 0,
        Cylinder = 1,
        Capsule = 2,
        Ellipsoid = 3,
    }

    /// <summary>零件长轴对齐到哪个世界轴（物体局部坐标系）。</summary>
    public enum PartAxis
    {
        X = 0,
        Y = 1,
        Z = 2,
    }

    /// <summary>连接方向：父零件的 6 个面。</summary>
    public enum Dir6
    {
        XPlus = 0,
        XMinus = 1,
        YPlus = 2,
        YMinus = 3,
        ZPlus = 4,
        ZMinus = 5,
    }

    /// <summary>当前 segment 使用的 structural similarity context。</summary>
    public enum SimilarityLevel
    {
        Identical = 0, // Target：完全相同的物体 ID
        High = 1,      // 保留 6/7 条连接
        Medium = 2,    // 保留 4-5/7 条连接
        Low = 3,       // 保留 0-3/7 条连接
    }

    public static class Dir6Util
    {
        public static readonly Dir6[] All =
        {
            Dir6.XPlus, Dir6.XMinus, Dir6.YPlus, Dir6.YMinus, Dir6.ZPlus, Dir6.ZMinus,
        };

        public static Vector3 ToVector(Dir6 d)
        {
            switch (d)
            {
                case Dir6.XPlus: return Vector3.right;
                case Dir6.XMinus: return Vector3.left;
                case Dir6.YPlus: return Vector3.up;
                case Dir6.YMinus: return Vector3.down;
                case Dir6.ZPlus: return Vector3.forward;
                default: return Vector3.back;
            }
        }

        public static Dir6 Opposite(Dir6 d)
        {
            switch (d)
            {
                case Dir6.XPlus: return Dir6.XMinus;
                case Dir6.XMinus: return Dir6.XPlus;
                case Dir6.YPlus: return Dir6.YMinus;
                case Dir6.YMinus: return Dir6.YPlus;
                case Dir6.ZPlus: return Dir6.ZMinus;
                default: return Dir6.ZPlus;
            }
        }

        /// <summary>方向对应的轴索引：0=X, 1=Y, 2=Z。</summary>
        public static int AxisIndex(Dir6 d)
        {
            return (int)d / 2;
        }
    }

    /// <summary>
    /// 一个零件节点。index 是零件的稳定身份：变体只允许改变 parentIndex / direction / axis，
    /// 绝不允许增删或替换零件，因此 index 与 shape 在同一族物体里始终一一对应。
    /// </summary>
    [Serializable]
    public class PartNode
    {
        public int index;
        public PartShape shape;
        public int parentIndex = -1;   // 根零件为 -1
        public Dir6 direction;         // 从父零件看，本零件挂在哪个面（根零件无意义）
        public PartAxis axis = PartAxis.Y;

        // 由 ObjectLayout 计算得到，方便直接实例化 / 存档，不参与相似度比较
        public Vector3 localPosition;
        public Vector3 localEuler;

        public PartNode Clone()
        {
            return new PartNode
            {
                index = index,
                shape = shape,
                parentIndex = parentIndex,
                direction = direction,
                axis = axis,
                localPosition = localPosition,
                localEuler = localEuler,
            };
        }

        /// <summary>连接的唯一标识。相似度 = 两个物体之间相同 EdgeKey 的数量。</summary>
        public string EdgeKey()
        {
            return index + ":" + parentIndex + ":" + (int)direction + ":" + (int)axis;
        }
    }

    /// <summary>
    /// 刺激物体的构成参数。改这里就能整体切换零件数，
    /// 相似度分级、形状配额、序列长度都会跟着自动调整。
    ///
    /// 默认 4 个零件（4 种形状各 1 个）→ 3 条连接。
    /// 想回到 4 种形状各 2 个共 8 个零件，把 CopiesPerShape 改成 2 即可。
    /// </summary>
    public static class StimConfig
    {
        /// <summary>参与拼装的形状种类。</summary>
        public static PartShape[] ShapesInUse =
        {
            PartShape.Cube, PartShape.Cylinder, PartShape.Capsule, PartShape.Ellipsoid,
        };

        /// <summary>每种形状重复几次。为 1 时每个零件都是独一无二的。</summary>
        public static int CopiesPerShape = 1;

        public static int PartCount { get { return ShapesInUse.Length * CopiesPerShape; } }

        /// <summary>树状结构的连接数，永远比零件数少 1。</summary>
        public static int EdgeCount { get { return PartCount - 1; } }

        /// <summary>
        /// 各相似度等级"改变几条连接"的区间（含端点）。
        /// 按连接总数按比例推导，所以 3 条连接和 7 条连接用的是同一套规则：
        ///   7 条 → 高 1 / 中 2-3 / 低 4-7   （保留 6 / 4-5 / 0-3）
        ///   3 条 → 高 1 / 中 2   / 低 3     （保留 2 / 1   / 0）
        /// </summary>
        public static void ChangedRange(SimilarityLevel level, out int min, out int max)
        {
            int e = EdgeCount;
            int mediumMax = Mathf.Max(2, Mathf.RoundToInt(e * 0.43f));
            if (mediumMax >= e) mediumMax = Mathf.Max(1, e - 1);

            switch (level)
            {
                case SimilarityLevel.High:
                    min = 1; max = 1; break;
                case SimilarityLevel.Medium:
                    min = Mathf.Min(2, e); max = Mathf.Min(mediumMax, e); break;
                case SimilarityLevel.Low:
                    min = Mathf.Min(mediumMax + 1, e); max = e; break;
                default:
                    min = 0; max = 0; break;
            }
        }

        /// <summary>各等级"保留几条连接"的区间，供检查与报表使用。</summary>
        public static void RetainedRange(SimilarityLevel level, out int min, out int max)
        {
            if (level == SimilarityLevel.Identical) { min = EdgeCount; max = EdgeCount; return; }
            int changedMin, changedMax;
            ChangedRange(level, out changedMin, out changedMax);
            min = EdgeCount - changedMax;
            max = EdgeCount - changedMin;
        }

        /// <summary>把当前配置写成一行，用于日志与报表。</summary>
        public static string Describe()
        {
            return PartCount + " 个零件（" + ShapesInUse.Length + " 种形状 × " +
                   CopiesPerShape + "），" + EdgeCount + " 条连接";
        }
    }

    /// <summary>
    /// 一条"空间关系"的规范化标识。
    ///
    /// 关系 = 哪两个零件相连 + 子零件位于父零件的哪个方向。
    /// 关键点是**与谁是树根无关**：同一个空间摆法，用方块当根搭出来
    /// （"圆柱在方块上方"）和用圆柱当根搭出来（"方块在圆柱下方"）
    /// 必须算作同一条关系，否则跨物体比较会得出荒唐的结果。
    ///
    /// 做法：把形状对按枚举序排好，如果需要交换顺序就同时把方向取反。
    /// </summary>
    public static class RelationKey
    {
        public static string Make(PartShape a, PartShape b, Dir6 dirFromAToB)
        {
            if ((int)a <= (int)b)
                return (int)a + "_" + (int)b + "_" + (int)dirFromAToB;
            return (int)b + "_" + (int)a + "_" + (int)Dir6Util.Opposite(dirFromAToB);
        }

        /// <summary>给人看的形式，例如 Cube&gt;Cylinder@YPlus。</summary>
        public static string Readable(PartShape a, PartShape b, Dir6 dirFromAToB)
        {
            if ((int)a <= (int)b) return a + ">" + b + "@" + dirFromAToB;
            return b + ">" + a + "@" + Dir6Util.Opposite(dirFromAToB);
        }
    }

    /// <summary>Reference A 与 Comparison B 之间的配对类型。</summary>
    public enum PairClass
    {
        /// <summary>完全相同的 Object ID，只改变呈现角度。</summary>
        Target = 0,
        HighNonTarget = 1,
        MediumNonTarget = 2,
        LowNonTarget = 3,
        /// <summary>不可用：关系数不对，或没通过轮廓检查，或结构相同但零件朝向不同。</summary>
        Invalid = 4,
    }

    /// <summary>
    /// 一个完整刺激物体的全部定义。这是实验真正使用的标准，
    /// 自然语言说明书只是它的可读投影。
    /// </summary>
    [Serializable]
    public class ObjectDefinition
    {
        public string objectId;
        public int seed;

        /// <summary>所属物体家族。同一家族的所有版本使用完全相同的四个零件。</summary>
        public string familyId = "";

        /// <summary>零件清单标识。第一版所有正式家族共用同一份清单。</summary>
        public string partSetId = "";

        /// <summary>练习物体不得进入正式实验。</summary>
        public bool isPractice;

        /// <summary>基础物体为空；变体记录它是从哪个物体、按哪个等级派生出来的。</summary>
        public string baseObjectId = "";
        public SimilarityLevel derivedLevel = SimilarityLevel.Identical;

        /// <summary>相对 baseObjectId 保留了几条空间关系。仅作生成期记录，正式判定看配对矩阵。</summary>
        public int retainedEdges = -1;

        public List<PartNode> parts = new List<PartNode>();

        public ObjectDefinition Clone()
        {
            var c = new ObjectDefinition
            {
                objectId = objectId,
                seed = seed,
                familyId = familyId,
                partSetId = partSetId,
                isPractice = isPractice,
                baseObjectId = baseObjectId,
                derivedLevel = derivedLevel,
                retainedEdges = retainedEdges,
                parts = new List<PartNode>(parts.Count),
            };
            for (int i = 0; i < parts.Count; i++) c.parts.Add(parts[i].Clone());
            return c;
        }

        public int RootIndex()
        {
            for (int i = 0; i < parts.Count; i++)
                if (parts[i].parentIndex < 0) return i;
            return -1;
        }

        public List<int> ChildrenOf(int index)
        {
            var result = new List<int>();
            for (int i = 0; i < parts.Count; i++)
                if (parts[i].parentIndex == index) result.Add(i);
            return result;
        }

        public bool IsLeaf(int index)
        {
            for (int i = 0; i < parts.Count; i++)
                if (parts[i].parentIndex == index) return false;
            return true;
        }

        public PartNode PartByIndex(int index)
        {
            for (int i = 0; i < parts.Count; i++)
                if (parts[i].index == index) return parts[i];
            return null;
        }

        /// <summary>
        /// 本物体的空间关系集合（4 个零件 → 3 条）。
        ///
        /// 每种形状各 1 个时用"形状对 + 方向"，与树根无关，可以跨物体比较——
        /// 配对矩阵就靠这个。每种形状有多个副本时形状不再能唯一标识零件，
        /// 退回到基于零件编号的形式（此时只在同一家族内部有意义）。
        /// </summary>
        public HashSet<string> RelationSet()
        {
            var set = new HashSet<string>();
            bool shapeUnique = StimConfig.CopiesPerShape == 1;

            for (int i = 0; i < parts.Count; i++)
            {
                PartNode child = parts[i];
                if (child.parentIndex < 0) continue;

                if (shapeUnique)
                {
                    PartNode parent = PartByIndex(child.parentIndex);
                    if (parent == null) continue;
                    set.Add(RelationKey.Make(parent.shape, child.shape, child.direction));
                }
                else
                {
                    set.Add(child.EdgeKey());
                }
            }
            return set;
        }

        /// <summary>与另一个物体共有的空间关系数（0..3）。集合求交，因此是对称的。</summary>
        public int RetainedRelationsAgainst(ObjectDefinition other)
        {
            var a = RelationSet();
            a.IntersectWith(other.RelationSet());
            return a.Count;
        }

        /// <summary>可读的关系签名，写进日志用。</summary>
        public string RelationSignature()
        {
            var keys = new List<string>();
            bool shapeUnique = StimConfig.CopiesPerShape == 1;
            for (int i = 0; i < parts.Count; i++)
            {
                PartNode child = parts[i];
                if (child.parentIndex < 0) continue;
                PartNode parent = PartByIndex(child.parentIndex);
                if (parent == null) continue;
                keys.Add(shapeUnique
                    ? RelationKey.Readable(parent.shape, child.shape, child.direction)
                    : child.EdgeKey());
            }
            keys.Sort(StringComparer.Ordinal);
            return string.Join("|", keys.ToArray());
        }

        /// <summary>零件朝向签名。关系相同但朝向不同的两个物体，看起来并不一样。</summary>
        public string AxisSignature()
        {
            var keys = new List<string>();
            for (int i = 0; i < parts.Count; i++)
                keys.Add((int)parts[i].shape + ":" + (int)parts[i].axis);
            keys.Sort(StringComparer.Ordinal);
            return string.Join(",", keys.ToArray());
        }

        /// <summary>某个零件连了几条边（含通往父零件的那条）。用来统计"中心零件 / 末端零件"。</summary>
        public int DegreeOf(int partIndex)
        {
            int degree = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].index == partIndex && parts[i].parentIndex >= 0) degree++;
                if (parts[i].parentIndex == partIndex) degree++;
            }
            return degree;
        }

        /// <summary>结构指纹，用于去重（同一棵树 + 同样的朝向 = 同一个物体）。</summary>
        public string StructureHash()
        {
            var keys = new List<string>();
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                keys.Add(p.index + "|" + (int)p.shape + "|" + p.parentIndex + "|" +
                         (int)p.direction + "|" + (int)p.axis);
            }
            keys.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            for (int i = 0; i < keys.Count; i++) sb.Append(keys[i]).Append(';');
            return sb.ToString();
        }

        /// <summary>给人看的说明书（不用于实验判定）。</summary>
        public string ToReadableSpec()
        {
            var sb = new StringBuilder();
            sb.AppendLine("ObjectId: " + objectId + "  seed: " + seed);
            if (!string.IsNullOrEmpty(baseObjectId))
                sb.AppendLine("derived from " + baseObjectId + " (" + derivedLevel +
                              ", retained " + retainedEdges + "/7)");
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p.parentIndex < 0)
                    sb.AppendLine("  #" + p.index + " " + p.shape + " (axis " + p.axis + ")  [ROOT]");
                else
                    sb.AppendLine("  #" + p.index + " " + p.shape + " (axis " + p.axis + ")  -> #" +
                                  p.parentIndex + " on " + p.direction);
            }
            return sb.ToString();
        }
    }

    /// <summary>一批物体（基础物体 + 变体）的存档容器，可直接 JsonUtility 序列化。</summary>
    [Serializable]
    public class ObjectLibrary
    {
        public List<ObjectDefinition> objects = new List<ObjectDefinition>();

        public ObjectDefinition Find(string id)
        {
            for (int i = 0; i < objects.Count; i++)
                if (objects[i].objectId == id) return objects[i];
            return null;
        }
    }
}
