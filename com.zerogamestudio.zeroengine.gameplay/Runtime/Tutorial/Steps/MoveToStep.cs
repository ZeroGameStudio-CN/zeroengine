using System;
using UnityEngine;

namespace ZeroEngine.Tutorial
{
    /// <summary>
    /// 移动引导步骤 (v1.14.0+)
    /// 引导玩家移动到指定位置
    /// </summary>
    [Serializable]
    public class MoveToStep : TutorialStep
    {
        [Header("Target")]
        [Tooltip("目标位置 (World Space)")]
        public Vector3 TargetPosition;

        [Tooltip("目标对象注册 ID (优先于 TargetPosition)")]
        public string TargetObjectPath;

        [Tooltip("到达距离")]
        public float ArrivalDistance = 2f;

        [Header("Visual")]
        [Tooltip("显示箭头指示")]
        public bool ShowArrow = true;

        [Tooltip("显示路径线")]
        public bool ShowPathLine = false;

        [Tooltip("在小地图上显示目标")]
        public bool ShowOnMinimap = true;

        [Header("Text")]
        [Tooltip("提示文本")]
        [TextArea(1, 2)]
        public string PromptText;

        [Tooltip("到达时的提示")]
        public string ArrivalText;

        [Header("Settings")]
        [Tooltip("超时时间 (秒，0 无限等待)")]
        public float Timeout = 0f;

        [Tooltip("自动完成 (到达后等待时间)")]
        public float ArrivalDelay = 0.5f;

        // 运行时状态
        [NonSerialized]
        private bool _hasArrived;
        [NonSerialized]
        private float _arrivalTime;
        [NonSerialized]
        private Transform _targetTransform;
        [NonSerialized]
        private Vector3 _cachedTargetPosition;

        public override string StepType => "MoveTo";

        public override void OnEnter(TutorialContext ctx)
        {
            _hasArrived = false;
            _arrivalTime = 0;

            // 查找目标
            _targetTransform = null;
            if (!string.IsNullOrEmpty(TargetObjectPath))
            {
                var obj = ctx.GetTarget(TargetObjectPath);
                if (obj != null)
                {
                    _targetTransform = obj.transform;
                }
            }

            _cachedTargetPosition = GetCurrentTargetPosition();

            // 显示提示
            if (!string.IsNullOrEmpty(PromptText))
            {
                TutorialUIManager.Instance?.ShowPrompt(PromptText, null);
            }

            // 显示箭头
            if (ShowArrow)
            {
                TutorialUIManager.Instance?.ShowWorldArrow(_cachedTargetPosition);
            }

            // 小地图标记
            if (ShowOnMinimap)
            {
                // TODO: 与 Minimap 系统集成
            }
        }

        public override void OnUpdate(TutorialContext ctx)
        {
            // 超时检查
            if (Timeout > 0 && ctx.StepElapsedTime >= Timeout)
            {
                _hasArrived = true;
                return;
            }

            // 更新目标位置 (如果使用 Transform)
            if (_targetTransform != null)
            {
                _cachedTargetPosition = _targetTransform.position;
            }

            // 更新箭头
            if (ShowArrow)
            {
                TutorialUIManager.Instance?.UpdateWorldArrow(_cachedTargetPosition);
            }

            // 检查玩家是否到达
            if (ctx.Player != null)
            {
                float distance = Vector3.Distance(ctx.Player.transform.position, _cachedTargetPosition);

                if (distance <= ArrivalDistance)
                {
                    if (_arrivalTime == 0)
                    {
                        _arrivalTime = Time.time;

                        // 显示到达提示
                        if (!string.IsNullOrEmpty(ArrivalText))
                        {
                            TutorialUIManager.Instance?.ShowPrompt(ArrivalText, null);
                        }
                    }

                    // 等待到达延迟
                    if (Time.time - _arrivalTime >= ArrivalDelay)
                    {
                        _hasArrived = true;
                    }
                }
                else
                {
                    // 玩家离开了到达区域
                    _arrivalTime = 0;
                }
            }
        }

        public override void OnExit(TutorialContext ctx)
        {
            TutorialUIManager.Instance?.HidePrompt();
            TutorialUIManager.Instance?.HideWorldArrow();

            // 移除小地图标记
            if (ShowOnMinimap)
            {
                // TODO: 与 Minimap 系统集成
            }
        }

        public override bool IsCompleted(TutorialContext ctx)
        {
            return _hasArrived;
        }

        private Vector3 GetCurrentTargetPosition()
        {
            if (_targetTransform != null)
            {
                return _targetTransform.position;
            }
            return TargetPosition;
        }

        public override string GetDisplayText()
        {
            if (!string.IsNullOrEmpty(PromptText))
            {
                return PromptText;
            }

            return "Move to the target location";
        }

        public override bool Validate(out string error)
        {
            if (TargetPosition == Vector3.zero && string.IsNullOrEmpty(TargetObjectPath))
            {
                error = "Either TargetPosition or TargetObjectPath registered in TutorialContext is required";
                return false;
            }
            if (ArrivalDistance <= 0)
            {
                error = "ArrivalDistance must be positive";
                return false;
            }
            error = null;
            return true;
        }
    }
}
