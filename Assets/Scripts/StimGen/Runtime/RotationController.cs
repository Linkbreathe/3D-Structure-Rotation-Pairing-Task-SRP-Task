using UnityEngine;

namespace StimGen
{
    /// <summary>
    /// 把实验条件与观看动画放在两个独立层级：
    /// PresentationAnimationY（外层世界 Y 轴）→ ConditionRotationX（内层局部 X 轴）→ Object。
    ///
    /// RotationDeltaX 只表示 Comparison B 相对 Reference A 的 X 轴姿态差；Y 轴自转只帮助观看，
    /// 不参与 RotationDeltaX 的计算，也不改变 Same / Different 的正确答案。
    /// </summary>
    public class RotationController : MonoBehaviour
    {
        private Transform _presentationAnimationRoot;
        private Transform _conditionRotationRoot;
        private Transform _rigParent;
        private StimulusObject _boundStimulus;

        public bool HasPresentationRig
        {
            get { return _presentationAnimationRoot != null; }
        }

        /// <summary>为本次刺激创建 Y 动画外层和 X 条件内层，并施加绝对 X 姿态。</summary>
        public void Bind(StimulusObject stimulus, float absoluteRotationX)
        {
            ReleasePresentationRig();
            if (stimulus == null) return;

            _boundStimulus = stimulus;
            _rigParent = stimulus.transform.parent;

            var animationGo = new GameObject("PresentationAnimationY");
            _presentationAnimationRoot = animationGo.transform;
            _presentationAnimationRoot.SetParent(_rigParent, false);
            _presentationAnimationRoot.localPosition = Vector3.zero;
            _presentationAnimationRoot.localRotation = Quaternion.identity;
            _presentationAnimationRoot.localScale = Vector3.one;

            var conditionGo = new GameObject("ConditionRotationX");
            _conditionRotationRoot = conditionGo.transform;
            _conditionRotationRoot.SetParent(_presentationAnimationRoot, false);
            _conditionRotationRoot.localPosition = Vector3.zero;
            _conditionRotationRoot.localRotation = Quaternion.identity;
            _conditionRotationRoot.localScale = Vector3.one;

            stimulus.transform.SetParent(_conditionRotationRoot, false);
            stimulus.transform.localPosition = Vector3.zero;
            stimulus.transform.localRotation = Quaternion.identity;

            ApplyConditionRotation(absoluteRotationX);
            ApplyPresentationSpin(0f, 0f);
        }

        /// <summary>只改变内层实验条件；角度绕局部 X 轴。</summary>
        public void ApplyConditionRotation(float absoluteRotationX)
        {
            if (_conditionRotationRoot == null) return;
            _conditionRotationRoot.localRotation = Quaternion.AngleAxis(
                Mathf.Repeat(absoluteRotationX, 360f), Vector3.right);
        }

        /// <summary>
        /// 只改变外层观看动画。外层围绕世界 Y 轴匀速旋转，所有 trial 使用同一运动规则。
        /// </summary>
        public void ApplyPresentationSpin(float normalizedProgress, float revolutions)
        {
            if (_presentationAnimationRoot == null) return;

            float progress = Mathf.Clamp01(normalizedProgress);
            float turns = Mathf.Max(0f, revolutions);
            float animationAngleY = 360f * turns * progress;
            Quaternion parentWorldRotation = _rigParent != null
                ? _rigParent.rotation : Quaternion.identity;

            _presentationAnimationRoot.rotation =
                Quaternion.AngleAxis(animationAngleY, Vector3.up) * parentWorldRotation;
        }

        /// <summary>按呈现记录绑定 X 条件姿态。</summary>
        public void Bind(StimulusObject stimulus, PresentationRecord record)
        {
            Bind(stimulus, record.comparisonRotationX);
        }

        /// <summary>交互式调试用：沿 X 轴按 pair 规则累加一个角度差。</summary>
        public float ApplyConditionDelta(float previousAngleX, float deltaX)
        {
            float next = Mathf.Repeat(previousAngleX + deltaX, 360f);
            ApplyConditionRotation(next);
            return next;
        }

        /// <summary>销毁本次呈现的两层旋转 Root（以及其下的刺激物体）。</summary>
        public void ReleasePresentationRig()
        {
            if (_presentationAnimationRoot != null)
            {
                GameObject root = _presentationAnimationRoot.gameObject;
                if (Application.isPlaying) Destroy(root);
                else DestroyImmediate(root);
            }

            _presentationAnimationRoot = null;
            _conditionRotationRoot = null;
            _rigParent = null;
            _boundStimulus = null;
        }

        public bool IsBoundTo(StimulusObject stimulus)
        {
            return stimulus != null && _boundStimulus == stimulus && HasPresentationRig;
        }
    }
}
