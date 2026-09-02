using UnityEngine;

namespace StimGen
{
    /// <summary>
    /// 4 种零件的尺寸标准。所有形状体积严格相等（= TargetVolume），
    /// 尺寸由"长径比"反解，因此改 TargetVolume 只会整体缩放，不会破坏比例。
    ///
    /// 基准姿态：长轴 = 局部 Y 轴。PartAxis 只是把这个基准姿态整体旋转 90°。
    /// </summary>
    public static class ShapeMetrics
    {
        /// <summary>每个零件的体积（单位³）。8 个零件总体积 = 8 × 该值。</summary>
        public const float TargetVolume = 0.30f;

        /// <summary>圆柱长径比 H/(2r)。</summary>
        public const float CylinderAspect = 1.38f;

        /// <summary>胶囊总长径比 H/(2r)（含两个半球帽）。</summary>
        public const float CapsuleAspect = 1.66f;

        /// <summary>椭球长短半轴比 ry/rx（rz = rx）。</summary>
        public const float EllipsoidAspect = 1.25f;

        /// <summary>相邻零件沿连接轴的咬合深度，保证曲面之间不是"点接触"。</summary>
        public const float ContactOverlap = 0.04f;

        // ---- 基准尺寸（长轴 = Y）------------------------------------------------

        /// <summary>立方体边长。</summary>
        public static float CubeSide
        {
            get { return Mathf.Pow(TargetVolume, 1f / 3f); }
        }

        /// <summary>圆柱半径。V = π r² · (2r·asp) = 2π·asp·r³</summary>
        public static float CylinderRadius
        {
            get { return Mathf.Pow(TargetVolume / (2f * Mathf.PI * CylinderAspect), 1f / 3f); }
        }

        public static float CylinderHeight
        {
            get { return 2f * CylinderRadius * CylinderAspect; }
        }

        /// <summary>
        /// 胶囊半径。H = 2r·asp，圆柱段 hc = H - 2r = 2r(asp-1)，
        /// V = π r²·hc + (4/3)π r³ = π r³ (2(asp-1) + 4/3)
        /// </summary>
        public static float CapsuleRadius
        {
            get
            {
                float k = Mathf.PI * (2f * (CapsuleAspect - 1f) + 4f / 3f);
                return Mathf.Pow(TargetVolume / k, 1f / 3f);
            }
        }

        /// <summary>胶囊总高（含两个半球帽）。</summary>
        public static float CapsuleHeight
        {
            get { return 2f * CapsuleRadius * CapsuleAspect; }
        }

        /// <summary>胶囊圆柱段的半高（球心到球心距离的一半）。</summary>
        public static float CapsuleSegmentHalf
        {
            get { return CapsuleHeight * 0.5f - CapsuleRadius; }
        }

        /// <summary>椭球短半轴 rx = rz。V = (4/3)π·rx²·(asp·rx)</summary>
        public static float EllipsoidRadiusXZ
        {
            get { return Mathf.Pow(TargetVolume / (4f / 3f * Mathf.PI * EllipsoidAspect), 1f / 3f); }
        }

        public static float EllipsoidRadiusY
        {
            get { return EllipsoidRadiusXZ * EllipsoidAspect; }
        }

        /// <summary>基准姿态（长轴 = Y）下的半尺寸（包围盒半长）。</summary>
        public static Vector3 BaseHalfExtents(PartShape shape)
        {
            switch (shape)
            {
                case PartShape.Cube:
                {
                    float h = CubeSide * 0.5f;
                    return new Vector3(h, h, h);
                }
                case PartShape.Cylinder:
                    return new Vector3(CylinderRadius, CylinderHeight * 0.5f, CylinderRadius);
                case PartShape.Capsule:
                    return new Vector3(CapsuleRadius, CapsuleHeight * 0.5f, CapsuleRadius);
                default:
                    return new Vector3(EllipsoidRadiusXZ, EllipsoidRadiusY, EllipsoidRadiusXZ);
            }
        }

        /// <summary>把基准半尺寸按 PartAxis 置换到物体局部坐标系。</summary>
        public static Vector3 HalfExtents(PartShape shape, PartAxis axis)
        {
            Vector3 b = BaseHalfExtents(shape);
            switch (axis)
            {
                case PartAxis.X: return new Vector3(b.y, b.x, b.z);
                case PartAxis.Z: return new Vector3(b.x, b.z, b.y);
                default: return b;
            }
        }

        /// <summary>PartAxis 对应的欧拉角（把局部 Y 长轴转到目标轴）。</summary>
        public static Vector3 AxisEuler(PartAxis axis)
        {
            switch (axis)
            {
                case PartAxis.X: return new Vector3(0f, 0f, 90f);
                case PartAxis.Z: return new Vector3(90f, 0f, 0f);
                default: return Vector3.zero;
            }
        }

        public static Quaternion AxisRotation(PartAxis axis)
        {
            return Quaternion.Euler(AxisEuler(axis));
        }

        /// <summary>
        /// 立方体各向同性，朝向没有视觉意义；统一归一到 Y，
        /// 避免"改了 axis 但看不出来"的假变化污染相似度计数。
        /// </summary>
        public static bool AxisMatters(PartShape shape)
        {
            return shape != PartShape.Cube;
        }

        public static PartAxis NormalizeAxis(PartShape shape, PartAxis axis)
        {
            return AxisMatters(shape) ? axis : PartAxis.Y;
        }
    }
}
