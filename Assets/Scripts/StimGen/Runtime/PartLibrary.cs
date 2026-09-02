using UnityEngine;

namespace StimGen
{
    /// <summary>
    /// 模块 1：保存 4 种基础几何零件（mesh + 缩放 + 共用材质）。
    ///
    /// 关键点：内置 Capsule 做非均匀缩放会把两端的半球压成半椭球，
    /// 所以胶囊用程序化 mesh 精确生成；立方体 / 圆柱 / 椭球做仿射缩放在数学上是精确的
    /// （椭球本来就是球的仿射像），可以直接用内置 mesh。
    /// </summary>
    public static class PartLibrary
    {
        private static Mesh _cube, _cylinder, _sphere, _capsule;
        private static Material _sharedMaterial;

        /// <summary>
        /// 所有零件共用的纯色。默认纯白。
        ///
        /// 注意：纯白 + 强光很容易把高光打爆，边缘和曲面的明暗过渡一起丢掉，
        /// 而这套刺激全靠形状区分，明暗就是唯一的形状线索。
        /// 如果预览时觉得"糊成一片白"，先调场景里 Directional Light 的强度，
        /// 或者把这里改成 0.90 左右的近白色，而不是去动材质别的参数。
        /// </summary>
        public static Color PartColor = new Color(1f, 1f, 1f, 1f);

        /// <summary>粗糙度。压得比较低是为了避免镜面高光盖住形状。</summary>
        public static float PartSmoothness = 0.15f;

        public static Mesh GetMesh(PartShape shape)
        {
            switch (shape)
            {
                case PartShape.Cube:
                    if (_cube == null) _cube = BuiltinMesh(PrimitiveType.Cube);
                    return _cube;
                case PartShape.Cylinder:
                    if (_cylinder == null) _cylinder = BuiltinMesh(PrimitiveType.Cylinder);
                    return _cylinder;
                case PartShape.Ellipsoid:
                    if (_sphere == null) _sphere = BuiltinMesh(PrimitiveType.Sphere);
                    return _sphere;
                default:
                    if (_capsule == null)
                        _capsule = BuildCapsuleMesh(ShapeMetrics.CapsuleRadius,
                                                    ShapeMetrics.CapsuleSegmentHalf);
                    return _capsule;
            }
        }

        /// <summary>
        /// 把标准 mesh 缩放到 ShapeMetrics 规定的等体积尺寸。
        /// 内置 Cube 边长 1，Cylinder 半径 0.5 高 2，Sphere 半径 0.5；胶囊 mesh 已是实际尺寸。
        /// </summary>
        public static Vector3 GetMeshScale(PartShape shape)
        {
            switch (shape)
            {
                case PartShape.Cube:
                {
                    float s = ShapeMetrics.CubeSide;
                    return new Vector3(s, s, s);
                }
                case PartShape.Cylinder:
                {
                    float d = ShapeMetrics.CylinderRadius * 2f;
                    return new Vector3(d, ShapeMetrics.CylinderHeight * 0.5f, d);
                }
                case PartShape.Ellipsoid:
                    return new Vector3(ShapeMetrics.EllipsoidRadiusXZ * 2f,
                                       ShapeMetrics.EllipsoidRadiusY * 2f,
                                       ShapeMetrics.EllipsoidRadiusXZ * 2f);
                default:
                    return Vector3.one;
            }
        }

        /// <summary>所有零件共用的纯色材质。可在外部用 SetSharedMaterial 替换成项目资源。</summary>
        public static Material SharedMaterial
        {
            get
            {
                if (_sharedMaterial == null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                    if (shader == null) shader = Shader.Find("Standard");
                    if (shader == null) shader = Shader.Find("Diffuse");
                    _sharedMaterial = new Material(shader) { name = "StimulusPart (runtime)" };
                    ApplyPartColor(_sharedMaterial);
                }
                return _sharedMaterial;
            }
        }

        public static void SetSharedMaterial(Material material)
        {
            _sharedMaterial = material;
        }

        /// <summary>给任意 URP/Standard 材质刷上统一颜色与统一粗糙度。</summary>
        public static void ApplyPartColor(Material m)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", PartColor);
            if (m.HasProperty("_Color")) m.SetColor("_Color", PartColor);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", PartSmoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", PartSmoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        }

        /// <summary>生成一个零件 GameObject（不含碰撞体，实验里用不到物理）。</summary>
        public static GameObject CreatePart(PartNode node, Transform parent)
        {
            var go = new GameObject("Part" + node.index + "_" + node.shape);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = node.localPosition;
            go.transform.localEulerAngles = node.localEuler;
            go.transform.localScale = GetMeshScale(node.shape);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = GetMesh(node.shape);

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = SharedMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var tag = go.AddComponent<PartTag>();
            tag.partIndex = node.index;
            tag.shape = node.shape;
            return go;
        }

        private static Mesh BuiltinMesh(PrimitiveType type)
        {
            var temp = GameObject.CreatePrimitive(type);
            Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying) Object.Destroy(temp);
            else Object.DestroyImmediate(temp);
            return mesh;
        }

        /// <summary>
        /// 精确胶囊：上半球 + 圆柱段 + 下半球。segHalf 是两个球心到中点的距离。
        /// </summary>
        public static Mesh BuildCapsuleMesh(float radius, float segHalf, int longitude = 32, int stacks = 10)
        {
            int ringCount = 2 * (stacks + 1);
            int perRing = longitude + 1; // 接缝顶点重复一次，UV 才连续

            var verts = new Vector3[ringCount * perRing];
            var normals = new Vector3[ringCount * perRing];
            var uvs = new Vector2[ringCount * perRing];

            for (int k = 0; k < ringCount; k++)
            {
                bool topHemisphere = k <= stacks;
                float a;      // 0 = 与圆柱段的接缝，PI/2 = 极点
                float sign;
                if (topHemisphere)
                {
                    a = (stacks - k) / (float)stacks * (Mathf.PI * 0.5f);
                    sign = 1f;
                }
                else
                {
                    a = (k - stacks - 1) / (float)stacks * (Mathf.PI * 0.5f);
                    sign = -1f;
                }

                float ringRadius = radius * Mathf.Cos(a);
                float y = sign * (segHalf + radius * Mathf.Sin(a));
                float ny = sign * Mathf.Sin(a);
                float nr = Mathf.Cos(a);

                for (int j = 0; j < perRing; j++)
                {
                    float t = j / (float)longitude * Mathf.PI * 2f;
                    float ct = Mathf.Cos(t);
                    float st = Mathf.Sin(t);
                    int vi = k * perRing + j;
                    verts[vi] = new Vector3(ringRadius * ct, y, ringRadius * st);
                    normals[vi] = new Vector3(nr * ct, ny, nr * st).normalized;
                    uvs[vi] = new Vector2(j / (float)longitude, 1f - k / (float)(ringCount - 1));
                }
            }

            var tris = new int[(ringCount - 1) * longitude * 6];
            int ti = 0;
            for (int k = 0; k < ringCount - 1; k++)
            {
                for (int j = 0; j < longitude; j++)
                {
                    int a = k * perRing + j;         // 左上
                    int b = a + 1;                   // 右上
                    int c = a + perRing;             // 左下
                    int d = c + 1;                   // 右下
                    tris[ti++] = a; tris[ti++] = b; tris[ti++] = d;
                    tris[ti++] = a; tris[ti++] = d; tris[ti++] = c;
                }
            }

            var mesh = new Mesh { name = "StimCapsule" };
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>挂在每个零件上，供遮挡检测按零件回读像素。</summary>
    public class PartTag : MonoBehaviour
    {
        public int partIndex;
        public PartShape shape;
    }
}
