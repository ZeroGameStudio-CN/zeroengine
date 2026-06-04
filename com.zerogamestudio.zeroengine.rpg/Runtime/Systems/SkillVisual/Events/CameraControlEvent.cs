// ============================================================================
// ZeroEngine v2.7.0 - Skill Visual System
// 相机控制事件
// ============================================================================

using System;
using UnityEngine;
using ZeroEngine.Timing;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

#if ZEROENGINE_DOTWEEN
using DG.Tweening;
#endif

namespace ZeroEngine.RPG.SkillVisual
{
    /// <summary>
    /// 相机控制类型
    /// </summary>
    public enum CameraAction
    {
        /// <summary>震动</summary>
        Shake,
        /// <summary>缩放</summary>
        Zoom,
        /// <summary>聚焦目标</summary>
        FocusTarget,
        /// <summary>慢动作</summary>
        SlowMotion
    }

    /// <summary>
    /// 相机控制事件 - 震动、缩放、聚焦等
    /// </summary>
    [Serializable]
#if ODIN_INSPECTOR
    [LabelText("Camera Control")]
#endif
    public class CameraControlEvent : VisualEvent
    {
        private static readonly object SlowMotionToken = new object();

#if ODIN_INSPECTOR
        [FoldoutGroup("Settings")]
#endif
        [Tooltip("相机动作类型")]
        public CameraAction Action = CameraAction.Shake;

        // ========================================
        // Shake 参数
        // ========================================

#if ODIN_INSPECTOR
        [FoldoutGroup("Settings"), ShowIf("Action", CameraAction.Shake)]
#endif
        [Tooltip("震动时长")]
        public float ShakeDuration = 0.2f;

#if ODIN_INSPECTOR
        [FoldoutGroup("Settings"), ShowIf("Action", CameraAction.Shake)]
#endif
        [Tooltip("震动强度")]
        public float ShakeStrength = 1f;

#if ODIN_INSPECTOR
        [FoldoutGroup("Settings"), ShowIf("Action", CameraAction.Shake)]
#endif
        [Tooltip("震动频率")]
        public int ShakeVibrato = 10;

        // ========================================
        // Zoom 参数
        // ========================================

#if ODIN_INSPECTOR
        [FoldoutGroup("Settings"), ShowIf("Action", CameraAction.Zoom)]
#endif
        [Tooltip("目标 FOV 或正交尺寸")]
        public float ZoomValue = 10f;

#if ODIN_INSPECTOR
        [FoldoutGroup("Settings"), ShowIf("Action", CameraAction.Zoom)]
#endif
        [Tooltip("缩放时长")]
        public float ZoomDuration = 0.3f;

#if ODIN_INSPECTOR
        [FoldoutGroup("Settings"), ShowIf("Action", CameraAction.Zoom)]
#endif
        [Tooltip("缩放后自动恢复")]
        public bool ZoomAutoRestore = true;

        // ========================================
        // SlowMotion 参数
        // ========================================

#if ODIN_INSPECTOR
        [FoldoutGroup("Settings"), ShowIf("Action", CameraAction.SlowMotion)]
#endif
        [Tooltip("时间缩放 (0.1 = 10% 速度)")]
        [Range(0.01f, 1f)]
        public float TimeScale = 0.2f;

#if ODIN_INSPECTOR
        [FoldoutGroup("Settings"), ShowIf("Action", CameraAction.SlowMotion)]
#endif
        [Tooltip("慢动作时长 (真实时间)")]
        public float SlowMotionDuration = 0.5f;

#if ZEROENGINE_DOTWEEN
        public override void AddToSequence(Sequence sequence, VisualContext context)
        {
            if (!Enabled) return;

            sequence.InsertCallback(Delay, () => ExecuteAction(context));
        }
#endif

        public override void Execute(VisualContext context)
        {
            if (!Enabled) return;
            ExecuteAction(context);
        }

        private void ExecuteAction(VisualContext context)
        {
            switch (Action)
            {
                case CameraAction.Shake:
                    DoShake();
                    break;
                case CameraAction.Zoom:
                    DoZoom();
                    break;
                case CameraAction.FocusTarget:
                    DoFocus(context);
                    break;
                case CameraAction.SlowMotion:
                    DoSlowMotion();
                    break;
            }
        }

        private void DoShake()
        {
            var cam = Camera.main;
            if (cam == null) return;

#if ZEROENGINE_DOTWEEN
            cam.DOShakePosition(ShakeDuration, ShakeStrength, ShakeVibrato);
#else
            Debug.Log($"[CameraControl] Shake: duration={ShakeDuration}, strength={ShakeStrength}");
#endif
        }

        private void DoZoom()
        {
            var cam = Camera.main;
            if (cam == null) return;

#if ZEROENGINE_DOTWEEN
            float originalValue = cam.orthographic ? cam.orthographicSize : cam.fieldOfView;

            if (cam.orthographic)
            {
                var tween = cam.DOOrthoSize(ZoomValue, ZoomDuration);
                if (ZoomAutoRestore)
                {
                    tween.OnComplete(() => cam.DOOrthoSize(originalValue, ZoomDuration));
                }
            }
            else
            {
                var tween = cam.DOFieldOfView(ZoomValue, ZoomDuration);
                if (ZoomAutoRestore)
                {
                    tween.OnComplete(() => cam.DOFieldOfView(originalValue, ZoomDuration));
                }
            }
#else
            Debug.Log($"[CameraControl] Zoom: value={ZoomValue}, duration={ZoomDuration}");
#endif
        }

        private void DoFocus(VisualContext context)
        {
            // 聚焦到目标位置
            var targetPos = context.GetTargetPosition();
            Debug.Log($"[CameraControl] Focus: target={targetPos}");

            // 需要外部相机系统集成
            // CameraController.Instance?.FocusOn(targetPos);
        }

        private void DoSlowMotion()
        {
            TimeControlLocator.Service.SetScaleModifier(
                SlowMotionToken,
                TimeDomainIds.Presentation,
                Mathf.Clamp(TimeScale, 0f, 10f),
                durationSeconds: Mathf.Max(0f, SlowMotionDuration),
                reason: "ZE.RPG.CameraControlEvent.SlowMotion");
        }
    }
}
