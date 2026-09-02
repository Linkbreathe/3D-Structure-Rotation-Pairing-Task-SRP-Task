using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace StimGen
{
    /// <summary>轮廓 / 遮挡这类需要真正渲染一遍的检查参数。</summary>
    [Serializable]
    public class VisualCheckSettings
    {
        [Header("离屏渲染")]
        public int resolution = 256;
        public float[] angles = { 0f, 45f, 90f };
        [Tooltip("正交相机半高。所有物体共用同一个取景范围，轮廓面积才可比")]
        public float orthographicSize = 1.7f;
        [Tooltip("用于离屏渲染的专用 Layer（不要和场景里其他东西共用）")]
        public int captureLayer = 31;

        [Header("遮挡：每个零件都不能被完全挡住")]
        [Tooltip("单个零件可见像素占整体轮廓的最小比例")]
        public float minPartVisibleRatio = 0.010f;
        [Tooltip("单个零件可见像素的绝对下限")]
        public int minPartVisiblePixels = 40;

        [Header("轮廓重合度阈值（IoU）")]
        public float highMinIou = 0.80f;
        public float mediumMinIou = 0.60f;
        public float mediumMaxIou = 0.75f;
        public float lowMaxIou = 0.50f;

        // The active protocol treats High/Low as structural conditions and
        // requires pilot data to calibrate their perceptual difficulty. Medium
        // thresholds remain serialized for legacy data only. Keep the IoU bands
        // available for diagnostics and silhouette/occlusion as a sanity gate.
        public bool enforceLevelIouBands = true;

        [Tooltip("三个角度之间 IoU 的最大允许跨度，超过说明角度之间不一致")]
        public float maxIouSpread = 0.15f;
    }

    public class VisualReport
    {
        public bool passed = true;
        public float[] iouPerAngle;
        public float minIou, maxIou, spread;
        public readonly List<string> failures = new List<string>();

        public void Fail(string reason)
        {
            passed = false;
            failures.Add(reason);
        }

        public override string ToString()
        {
            string ious = iouPerAngle == null ? "-" : string.Join(", ",
                Array.ConvertAll(iouPerAngle, v => v.ToString("F3")));
            return (passed ? "PASS" : "FAIL") + "  IoU[" + ious + "] spread=" +
                   spread.ToString("F3") +
                   (failures.Count > 0 ? "\n  - " + string.Join("\n  - ", failures.ToArray()) : "");
        }
    }

    /// <summary>一个物体在一个角度下的渲染结果。</summary>
    public class ViewCapture
    {
        public bool[] silhouette;      // 该像素是否属于物体
        public int silhouettePixels;
        public int[] partPixels;       // 每个零件的可见像素数（下标 = partIndex）
    }

    /// <summary>
    /// 模块 4（视觉部分）：从 Y轴 0°/45°/90° 自动截图并比较建库轮廓，
    /// 这里的角度只是刺激库视觉筛选视图，不是正式任务的 X轴 RotationDelta。
    /// 同时确认每个零件都没有被完全挡住。
    ///
    /// "连接变化数量负责生成相似度，轮廓检查负责确认人是否真的感觉到这种相似度"——
    /// 所以这一层只做淘汰，不参与生成。
    /// </summary>
    public static class SilhouetteAnalyzer
    {
        /// <summary>8 个零件的身份色，彼此分得很开，回读时按最近邻分类。</summary>
        private static readonly Color[] IdColors =
        {
            new Color(1f, 0f, 0f), new Color(0f, 1f, 0f), new Color(0f, 0f, 1f),
            new Color(1f, 1f, 0f), new Color(1f, 0f, 1f), new Color(0f, 1f, 1f),
            new Color(1f, 1f, 1f), new Color(0.5f, 0.5f, 0.5f),
        };

        /// <summary>把一个物体在若干角度下渲染出来。调用方负责在主线程 / 编辑器里调用。</summary>
        public static ViewCapture[] Capture(ObjectDefinition def, VisualCheckSettings s)
        {
            var captures = new ViewCapture[s.angles.Length];

            GameObject holder = new GameObject("~StimCapture");
            holder.hideFlags = HideFlags.HideAndDontSave;
            holder.transform.position = new Vector3(10000f, 10000f, 10000f);

            StimulusObject stim = ObjectAssembler.Build(def, holder.transform);
            stim.transform.localPosition = Vector3.zero;
            ObjectAssembler.SetLayerRecursive(stim.gameObject, s.captureLayer);

            Material unlit = CreateUnlitMaterial();
            var blocks = new MaterialPropertyBlock();
            for (int i = 0; i < stim.parts.Count; i++)
            {
                var mr = stim.parts[i].GetComponent<MeshRenderer>();
                mr.sharedMaterial = unlit;
                blocks.Clear();
                blocks.SetColor("_BaseColor", IdColors[stim.parts[i].partIndex % IdColors.Length]);
                blocks.SetColor("_Color", IdColors[stim.parts[i].partIndex % IdColors.Length]);
                mr.SetPropertyBlock(blocks);
            }

            RenderTexture rt = new RenderTexture(s.resolution, s.resolution, 24,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            rt.antiAliasing = 1;
            rt.filterMode = FilterMode.Point;

            GameObject camGo = new GameObject("~StimCaptureCam");
            camGo.hideFlags = HideFlags.HideAndDontSave;
            camGo.transform.position = holder.transform.position + new Vector3(0f, 0f, -10f);
            camGo.transform.LookAt(holder.transform.position);

            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = s.orthographicSize;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = 1 << s.captureLayer;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.targetTexture = rt;
            cam.enabled = false;

            var urp = camGo.GetComponent<UniversalAdditionalCameraData>();
            if (urp == null) urp = camGo.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = false;
            urp.antialiasing = AntialiasingMode.None;
            urp.renderShadows = false;
            urp.requiresDepthOption = CameraOverrideOption.Off;
            urp.requiresColorOption = CameraOverrideOption.Off;

            var readback = new Texture2D(s.resolution, s.resolution, TextureFormat.RGBA32, false, true);

            try
            {
                for (int a = 0; a < s.angles.Length; a++)
                {
                    stim.SetYaw(s.angles[a]);
                    cam.Render();

                    RenderTexture prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    readback.ReadPixels(new Rect(0, 0, s.resolution, s.resolution), 0, 0);
                    readback.Apply(false);
                    RenderTexture.active = prev;

                    captures[a] = Classify(readback.GetPixels32(), def.parts.Count);
                }
            }
            finally
            {
                cam.targetTexture = null;
                DestroySafe(readback);
                DestroySafe(rt);
                DestroySafe(unlit);
                DestroySafe(camGo);
                DestroySafe(holder);
            }
            return captures;
        }

        /// <summary>把回读到的像素分类到"背景"或某个零件。</summary>
        private static ViewCapture Classify(Color32[] pixels, int partCount)
        {
            var view = new ViewCapture
            {
                silhouette = new bool[pixels.Length],
                partPixels = new int[partCount],
            };

            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 c = pixels[i];
                // 背景是纯黑且 alpha=0；任何被渲染到的像素 alpha 都是 1
                if (c.a < 128) continue;

                float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f;
                if (r + g + b < 0.08f) continue; // 保险：接近纯黑的当背景

                view.silhouette[i] = true;
                view.silhouettePixels++;

                int best = -1;
                float bestDist = float.MaxValue;
                for (int k = 0; k < partCount && k < IdColors.Length; k++)
                {
                    Color id = IdColors[k];
                    float d = (id.r - r) * (id.r - r) + (id.g - g) * (id.g - g) + (id.b - b) * (id.b - b);
                    if (d < bestDist) { bestDist = d; best = k; }
                }
                if (best >= 0) view.partPixels[best]++;
            }
            return view;
        }

        /// <summary>每个零件在每个角度下都要露出来一点。</summary>
        public static bool CheckOcclusion(ViewCapture[] views, VisualCheckSettings s,
                                          List<string> failures)
        {
            bool ok = true;
            for (int a = 0; a < views.Length; a++)
            {
                var v = views[a];
                if (v.silhouettePixels == 0)
                {
                    failures.Add("角度 " + s.angles[a] + "° 渲染为空");
                    ok = false;
                    continue;
                }
                for (int p = 0; p < v.partPixels.Length; p++)
                {
                    float ratio = v.partPixels[p] / (float)v.silhouettePixels;
                    if (v.partPixels[p] < s.minPartVisiblePixels || ratio < s.minPartVisibleRatio)
                    {
                        failures.Add("角度 " + s.angles[a] + "°：零件 #" + p + " 几乎被完全挡住（" +
                                     v.partPixels[p] + " px, " + (ratio * 100f).ToString("F1") + "%）");
                        ok = false;
                    }
                }
            }
            return ok;
        }

        public static float IoU(bool[] a, bool[] b)
        {
            int inter = 0, union = 0;
            for (int i = 0; i < a.Length; i++)
            {
                bool x = a[i], y = b[i];
                if (x || y) union++;
                if (x && y) inter++;
            }
            return union == 0 ? 0f : inter / (float)union;
        }

        /// <summary>
        /// 完整视觉检查：物体自身的遮挡 + 与基础物体的轮廓重合度。
        /// baseViews 传 null 时只做遮挡检查（用于基础物体本身）。
        /// </summary>
        public static VisualReport Evaluate(ViewCapture[] variantViews, ViewCapture[] baseViews,
                                            SimilarityLevel level, VisualCheckSettings s)
        {
            var report = new VisualReport();
            CheckOcclusion(variantViews, s, report.failures);
            if (report.failures.Count > 0) report.passed = false;

            if (baseViews == null) return report;

            report.iouPerAngle = new float[variantViews.Length];
            report.minIou = 1f;
            report.maxIou = 0f;
            for (int a = 0; a < variantViews.Length; a++)
            {
                float iou = IoU(variantViews[a].silhouette, baseViews[a].silhouette);
                report.iouPerAngle[a] = iou;
                report.minIou = Mathf.Min(report.minIou, iou);
                report.maxIou = Mathf.Max(report.maxIou, iou);
            }
            report.spread = report.maxIou - report.minIou;

            if (report.spread > s.maxIouSpread)
                report.Fail("三个角度的轮廓重合度不一致（跨度 " + report.spread.ToString("F3") + "）");

            if (!s.enforceLevelIouBands) return report;

            for (int a = 0; a < report.iouPerAngle.Length; a++)
            {
                float iou = report.iouPerAngle[a];
                bool inBand;
                switch (level)
                {
                    case SimilarityLevel.High: inBand = iou >= s.highMinIou; break;
                    case SimilarityLevel.Medium: inBand = iou >= s.mediumMinIou && iou <= s.mediumMaxIou; break;
                    case SimilarityLevel.Low: inBand = iou <= s.lowMaxIou; break;
                    default: inBand = true; break;
                }
                if (!inBand)
                    report.Fail("角度 " + s.angles[a] + "° 轮廓重合度 " + iou.ToString("F3") +
                                " 不在 " + level + " 的目标区间内");
            }
            return report;
        }

        private static Material CreateUnlitMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var m = new Material(shader) { name = "~StimIdUnlit", hideFlags = HideFlags.HideAndDontSave };
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 0f);
            return m;
        }

        private static void DestroySafe(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
