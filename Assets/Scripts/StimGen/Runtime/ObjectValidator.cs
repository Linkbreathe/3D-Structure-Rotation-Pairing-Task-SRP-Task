using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StimGen
{
    /// <summary>几何合格标准。轮廓/遮挡这类需要渲染的检查见 SilhouetteAnalyzer。</summary>
    [Serializable]
    public class ValidationSettings
    {
        [Header("重叠")]
        [Tooltip("非相邻零件允许的交叠体积（占单个零件体积的比例）")]
        public float maxOverlapRatio = 0.02f;

        [Tooltip("相邻零件至少要有的交叠体积（占单个零件体积的比例），保证真正连上")]
        public float minContactRatio = 0.0005f;

        [Header("整体大小")]
        // 4 个零件时实测自然分布是 1.19–1.44，这个窗口留了余量又能挡掉离群值。
        // 改零件数后要重新量一遍（Builder 会把实测范围打印出来）。
        public float minBoundingRadius = 1.05f;
        public float maxBoundingRadius = 1.55f;

        [Tooltip("包围盒最长边 / 最短边，防止长成一根棍子")]
        public float maxAspectRatio = 2.60f;

        [Header("对称性")]
        [Tooltip("任一对称操作下匹配上的零件比例超过该值即判为'高度对称'")]
        public float maxSymmetryScore = 0.74f;

        [Tooltip("判断两个零件位置是否重合的容差")]
        public float symmetryTolerance = 0.06f;

        [Header("立体性")]
        [Tooltip("零件中心在最薄的那个方向上的跨度下限。0 表示不检查。\n" +
                 "零件种类各只有 1 个时，镜像对称检查基本失效，\n" +
                 "此时'所有零件排在同一平面上'才是真正的风险：这种物体转 45° 会完全变样。")]
        public float minCenterSpread = 0.25f;

        [Header("形状配额")]
        [Tooltip("要求每种形状的个数都等于 StimConfig.CopiesPerShape")]
        public bool requireExactShapeQuota = true;
    }

    public class ValidationReport
    {
        public bool passed = true;
        public readonly List<string> failures = new List<string>();
        public float worstOverlapRatio;
        public float symmetryScore;
        public float boundingRadius;
        public float aspectRatio;
        public float centerSpread;

        public void Fail(string reason)
        {
            passed = false;
            failures.Add(reason);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(passed ? "PASS" : "FAIL");
            sb.Append("  r=").Append(boundingRadius.ToString("F3"));
            sb.Append(" aspect=").Append(aspectRatio.ToString("F2"));
            sb.Append(" sym=").Append(symmetryScore.ToString("F2"));
            sb.Append(" spread=").Append(centerSpread.ToString("F2"));
            sb.Append(" overlap=").Append(worstOverlapRatio.ToString("F4"));
            for (int i = 0; i < failures.Count; i++) sb.Append("\n  - ").Append(failures[i]);
            return sb.ToString();
        }
    }

    /// <summary>模块 4（几何部分）：检查连接、重叠、悬空、大小与对称。</summary>
    public static class ObjectValidator
    {
        /// <summary>7 个刚体对称操作（都是坐标轴的符号翻转），足以覆盖"高度对称"的常见情形。</summary>
        private static readonly Vector3[] SymmetryOps =
        {
            new Vector3(-1f,  1f,  1f), // 镜像 YZ 平面
            new Vector3( 1f, -1f,  1f), // 镜像 XZ 平面
            new Vector3( 1f,  1f, -1f), // 镜像 XY 平面
            new Vector3( 1f, -1f, -1f), // 绕 X 轴 180°
            new Vector3(-1f,  1f, -1f), // 绕 Y 轴 180°
            new Vector3(-1f, -1f,  1f), // 绕 Z 轴 180°
            new Vector3(-1f, -1f, -1f), // 中心反演
        };

        public static ValidationReport Validate(ObjectDefinition def, ValidationSettings s)
        {
            var report = new ValidationReport();

            // --- 零件数量与形状配额 ---
            if (def.parts.Count != StimConfig.PartCount)
            {
                report.Fail("零件数不是 " + StimConfig.PartCount + "（实际 " + def.parts.Count + "）");
                return report;
            }

            if (s.requireExactShapeQuota)
            {
                var histogram = new int[4];
                for (int i = 0; i < def.parts.Count; i++) histogram[(int)def.parts[i].shape]++;
                for (int i = 0; i < 4; i++)
                {
                    int expected = System.Array.IndexOf(StimConfig.ShapesInUse, (PartShape)i) >= 0
                        ? StimConfig.CopiesPerShape : 0;
                    if (histogram[i] != expected)
                    {
                        report.Fail("形状配额错误：" + (PartShape)i + " × " + histogram[i] +
                                    "（应为 " + expected + "）");
                        return report;
                    }
                }
            }

            // --- 拓扑必须是一棵能解算的树 ---
            if (!ObjectLayout.Solve(def))
            {
                report.Fail("拓扑非法：不是单根连通树（森林 / 成环 / 缺根）");
                return report;
            }

            // --- 连接与重叠 ---
            float partVolume = ShapeMetrics.TargetVolume;
            for (int i = 0; i < def.parts.Count; i++)
            {
                for (int j = i + 1; j < def.parts.Count; j++)
                {
                    var a = def.parts[i];
                    var b = def.parts[j];
                    bool adjacent = ObjectLayout.AreAdjacent(def, a.index, b.index);
                    float overlap = ShapeSdf.OverlapVolume(a, b) / partVolume;

                    if (adjacent)
                    {
                        if (overlap < s.minContactRatio)
                            report.Fail("零件 #" + a.index + " 与 #" + b.index + " 名义相连但没有实际接触");
                    }
                    else
                    {
                        report.worstOverlapRatio = Mathf.Max(report.worstOverlapRatio, overlap);
                        if (overlap > s.maxOverlapRatio)
                            report.Fail("非相邻零件 #" + a.index + " 与 #" + b.index +
                                        " 重叠 " + (overlap * 100f).ToString("F1") + "%");
                    }
                }
            }

            // --- 整体大小 ---
            Bounds bounds = ObjectLayout.ComputeBounds(def);
            Vector3 size = bounds.size;
            float maxDim = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float minDim = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            report.aspectRatio = minDim > 1e-4f ? maxDim / minDim : 999f;
            report.boundingRadius = ObjectLayout.BoundingRadius(def);

            if (report.boundingRadius < s.minBoundingRadius)
                report.Fail("整体过小：包围半径 " + report.boundingRadius.ToString("F3"));
            if (report.boundingRadius > s.maxBoundingRadius)
                report.Fail("整体过大：包围半径 " + report.boundingRadius.ToString("F3"));
            if (report.aspectRatio > s.maxAspectRatio)
                report.Fail("过于细长：长宽比 " + report.aspectRatio.ToString("F2"));

            // --- 对称性 ---
            report.symmetryScore = SymmetryScore(def, s.symmetryTolerance);
            if (report.symmetryScore > s.maxSymmetryScore)
                report.Fail("高度对称：对称得分 " + report.symmetryScore.ToString("F2"));

            // --- 立体性：零件中心不能全部落在同一个平面上 ---
            report.centerSpread = CenterSpread(def);
            if (s.minCenterSpread > 0f && report.centerSpread < s.minCenterSpread)
                report.Fail("零件近似共面：最薄方向跨度只有 " + report.centerSpread.ToString("F2"));

            return report;
        }

        /// <summary>
        /// 零件中心在最"薄"的那个坐标方向上的跨度。
        /// 接近 0 表示所有零件排在同一个平面上，物体是"片"而不是"块"。
        /// </summary>
        public static float CenterSpread(ObjectDefinition def)
        {
            Vector3 lo = def.parts[0].localPosition;
            Vector3 hi = lo;
            for (int i = 1; i < def.parts.Count; i++)
            {
                lo = Vector3.Min(lo, def.parts[i].localPosition);
                hi = Vector3.Max(hi, def.parts[i].localPosition);
            }
            Vector3 span = hi - lo;
            return Mathf.Min(span.x, Mathf.Min(span.y, span.z));
        }

        /// <summary>
        /// 最高对称得分（0..1）。1 表示某个对称操作下全部零件一一对应。
        /// 位置已在 Solve 里居中过，所以这些操作都以包围盒中心为原点。
        /// </summary>
        public static float SymmetryScore(ObjectDefinition def, float tolerance)
        {
            float best = 0f;
            for (int op = 0; op < SymmetryOps.Length; op++)
            {
                Vector3 m = SymmetryOps[op];
                var used = new bool[def.parts.Count];
                int matched = 0;

                for (int i = 0; i < def.parts.Count; i++)
                {
                    Vector3 target = Vector3.Scale(def.parts[i].localPosition, m);
                    for (int j = 0; j < def.parts.Count; j++)
                    {
                        if (used[j]) continue;
                        if (def.parts[j].shape != def.parts[i].shape) continue;
                        // 符号翻转不改变轴对齐包围盒，所以朝向也必须一致才算真对称
                        if (def.parts[j].axis != def.parts[i].axis) continue;
                        if ((def.parts[j].localPosition - target).sqrMagnitude <= tolerance * tolerance)
                        {
                            used[j] = true;
                            matched++;
                            break;
                        }
                    }
                }
                best = Mathf.Max(best, matched / (float)def.parts.Count);
            }
            return best;
        }
    }
}
