using UnityEngine;

namespace StimGen
{
    /// <summary>
    /// 4 种零件的解析有向距离场，用来判定"重叠"与"接触"。
    ///
    /// 为什么不用 Collider：SphereCollider 不支持非均匀缩放（椭球会退化成球），
    /// 内置 CapsuleCollider 同理。SDF 直接算真实几何，且不需要进 PlayMode。
    /// </summary>
    public static class ShapeSdf
    {
        /// <summary>
        /// 点到零件表面的有向距离。内部为负。
        /// pLocal 是零件自身坐标系下的点（长轴 = Y）。
        /// </summary>
        public static float Evaluate(PartShape shape, Vector3 pLocal)
        {
            switch (shape)
            {
                case PartShape.Cube:
                    return Box(pLocal, ShapeMetrics.BaseHalfExtents(PartShape.Cube));
                case PartShape.Cylinder:
                    return Cylinder(pLocal, ShapeMetrics.CylinderRadius,
                                    ShapeMetrics.CylinderHeight * 0.5f);
                case PartShape.Capsule:
                    return Capsule(pLocal, ShapeMetrics.CapsuleRadius,
                                   ShapeMetrics.CapsuleSegmentHalf);
                default:
                    return Ellipsoid(pLocal, new Vector3(ShapeMetrics.EllipsoidRadiusXZ,
                                                         ShapeMetrics.EllipsoidRadiusY,
                                                         ShapeMetrics.EllipsoidRadiusXZ));
            }
        }

        /// <summary>把物体局部坐标的点，换算到某个零件自身坐标系后求 SDF。</summary>
        public static float EvaluateAtPart(PartNode part, Vector3 pObject)
        {
            Vector3 local = Quaternion.Inverse(ShapeMetrics.AxisRotation(part.axis)) *
                            (pObject - part.localPosition);
            return Evaluate(part.shape, local);
        }

        public static float Box(Vector3 p, Vector3 b)
        {
            Vector3 q = new Vector3(Mathf.Abs(p.x) - b.x,
                                    Mathf.Abs(p.y) - b.y,
                                    Mathf.Abs(p.z) - b.z);
            Vector3 outside = new Vector3(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f), Mathf.Max(q.z, 0f));
            float inside = Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
            return outside.magnitude + inside;
        }

        /// <summary>沿 Y 轴的圆柱，半径 r，半高 h。</summary>
        public static float Cylinder(Vector3 p, float r, float h)
        {
            float dx = new Vector2(p.x, p.z).magnitude - r;
            float dy = Mathf.Abs(p.y) - h;
            float outside = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude;
            float inside = Mathf.Min(Mathf.Max(dx, dy), 0f);
            return outside + inside;
        }

        /// <summary>沿 Y 轴的胶囊，半径 r，线段从 -segHalf 到 +segHalf。</summary>
        public static float Capsule(Vector3 p, float r, float segHalf)
        {
            float y = Mathf.Clamp(p.y, -segHalf, segHalf);
            return (p - new Vector3(0f, y, 0f)).magnitude - r;
        }

        /// <summary>椭球（半轴 rad）。这是常用的近似 SDF，零等值面精确。</summary>
        public static float Ellipsoid(Vector3 p, Vector3 rad)
        {
            Vector3 a = new Vector3(p.x / rad.x, p.y / rad.y, p.z / rad.z);
            float k0 = a.magnitude;
            Vector3 b = new Vector3(p.x / (rad.x * rad.x),
                                    p.y / (rad.y * rad.y),
                                    p.z / (rad.z * rad.z));
            float k1 = b.magnitude;
            if (k1 < 1e-6f) return -Mathf.Min(rad.x, Mathf.Min(rad.y, rad.z));
            return k0 * (k0 - 1f) / k1;
        }

        /// <summary>零件在物体局部坐标下的轴对齐包围盒。</summary>
        public static Bounds PartBounds(PartNode part)
        {
            Vector3 h = ShapeMetrics.HalfExtents(part.shape, part.axis);
            return new Bounds(part.localPosition, h * 2f);
        }

        /// <summary>
        /// 用体素采样估算两个零件的交叠体积。
        /// step 越小越准，0.02 对本项目的零件尺度（~0.7）足够。
        /// </summary>
        public static float OverlapVolume(PartNode a, PartNode b, float step = 0.02f)
        {
            Bounds ba = PartBounds(a);
            Bounds bb = PartBounds(b);
            if (!ba.Intersects(bb)) return 0f;

            Vector3 lo = Vector3.Max(ba.min, bb.min);
            Vector3 hi = Vector3.Min(ba.max, bb.max);

            int nx = Mathf.Max(1, Mathf.CeilToInt((hi.x - lo.x) / step));
            int ny = Mathf.Max(1, Mathf.CeilToInt((hi.y - lo.y) / step));
            int nz = Mathf.Max(1, Mathf.CeilToInt((hi.z - lo.z) / step));

            float sx = (hi.x - lo.x) / nx;
            float sy = (hi.y - lo.y) / ny;
            float sz = (hi.z - lo.z) / nz;
            float cellVolume = sx * sy * sz;

            int hits = 0;
            for (int ix = 0; ix < nx; ix++)
            {
                float x = lo.x + (ix + 0.5f) * sx;
                for (int iy = 0; iy < ny; iy++)
                {
                    float y = lo.y + (iy + 0.5f) * sy;
                    for (int iz = 0; iz < nz; iz++)
                    {
                        float z = lo.z + (iz + 0.5f) * sz;
                        Vector3 p = new Vector3(x, y, z);
                        if (EvaluateAtPart(a, p) < 0f && EvaluateAtPart(b, p) < 0f) hits++;
                    }
                }
            }
            return hits * cellVolume;
        }
    }
}
