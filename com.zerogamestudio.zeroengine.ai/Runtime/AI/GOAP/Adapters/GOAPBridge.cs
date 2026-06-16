using System;
using UnityEngine;

namespace ZeroEngine.AI.GOAP
{
    /// <summary>
    /// GOAP 桥接器 - 连接 crashkonijn/GOAP 与 ZeroEngine
    /// 需要安装 com.crashkonijn.goap 包
    /// </summary>
    /// <remarks>
    /// 安装方式: 在 manifest.json 中添加:
    /// "com.crashkonijn.goap": "https://github.com/crashkonijn/GOAP.git?path=Package#3.0.0"
    /// </remarks>
    public static class GOAPBridge
    {
        /// <summary>GOAP 包是否已安装</summary>
        public static bool IsGOAPInstalled
        {
            get
            {
#if CRASHKONIJN_GOAP
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// 检查 GOAP 依赖
        /// </summary>
        public static void ValidateDependency()
        {
            if (!IsGOAPInstalled)
            {
                Debug.LogWarning("[ZeroEngine.AI.GOAP] crashkonijn/GOAP package is not installed. " +
                    "Add to manifest.json: \"com.crashkonijn.goap\": \"https://github.com/crashkonijn/GOAP.git?path=Package#3.0.0\"");
            }
        }
    }

#if !CRASHKONIJN_GOAP
    // 当 GOAP 未安装时的占位符

    /// <summary>
    /// GOAP 未安装时的占位 Agent
    /// </summary>
    public class ZeroGOAPAgent : MonoBehaviour, IAIBrain
    {
        public bool IsActive { get; set; }
        public string CurrentActionName => "GOAP Not Installed";

        public void Initialize(AIContext context)
        {
            GOAPBridge.ValidateDependency();
        }

        public void Tick(float deltaTime) { }
        public void ForceReevaluate() { }
        public void StopCurrentAction() { }
        public void Reset() { }
    }
#endif
}
