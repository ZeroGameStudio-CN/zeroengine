using System;
using UnityEngine;
#if ZEROENGINE_ADDRESSABLES
using UnityEngine.AddressableAssets;
#endif

namespace ZeroEngine.UI
{
    /// <summary>
    /// UI层级定义
    /// 数值越大，渲染越靠前
    /// </summary>
    public enum UILayer
    {
        Background = 0,   // 背景层（全屏背景、大地图等）
        Main = 100,       // 主界面层（HUD、主菜单）
        Screen = 200,     // 全屏面板层（背包、角色面板等）
        Popup = 300,      // 弹窗层（确认框、奖励弹窗）
        Overlay = 400,    // 叠加层（Loading、过场）
        Top = 500,        // 顶层（调试控制台、错误提示）
        System = 600      // 系统层（断网提示、强制更新）
    }

    /// <summary>
    /// 面板显示模式
    /// </summary>
    public enum UIShowMode
    {
        Normal,      // 普通模式：可与其他面板共存
        HideOthers,  // 隐藏同层其他面板
        Stack,       // 入栈模式：关闭时恢复上一个
        Singleton    // 单例模式：全局只能存在一个
    }

    /// <summary>
    /// 面板关闭模式
    /// </summary>
    public enum UICloseMode
    {
        Hide,     // 隐藏（保留实例）
        Destroy,  // 销毁（释放内存）
        Pool      // 回池（对象池复用）
    }

    /// <summary>
    /// 面板动画类型
    /// </summary>
    public enum UIAnimationType
    {
        None,         // 无动画
        Fade,         // 淡入淡出
        Scale,        // 缩放
        SlideLeft,    // 从左滑入
        SlideRight,   // 从右滑入
        SlideTop,     // 从上滑入
        SlideBottom,  // 从下滑入
        Custom        // 自定义动画
    }

    /// <summary>
    /// 视图跨会话的生命周期。
    /// Session 视图由 ReleaseSessionViews 清理，Resident 视图保留，
    /// Evictable 视图在关闭时立即清理。
    /// </summary>
    public enum UIViewLifetime
    {
        Session,
        Resident,
        Evictable
    }

    /// <summary>
    /// 视图状态
    /// </summary>
    public enum UIViewState
    {
        None,
        Created,
        Opening,
        Opened,
        Paused,
        Closing,
        Closed
    }

    /// <summary>
    /// 面板配置（可序列化，用于配置表或ScriptableObject）
    /// </summary>
    [Serializable]
    public class UIViewConfig
    {
        [Header("基础配置")]
        [Tooltip("面板名称（唯一标识）")]
        public string viewName;

#if ZEROENGINE_ADDRESSABLES
        [Tooltip("Prefab 资源引用 (Addressables)")]
        public AssetReferenceGameObject prefabReference;
#endif

        [Tooltip("Resources 路径（备选）")]
        public string resourcePath;

        [Tooltip("直接引用 Prefab（编辑器/小项目用）")]
        public GameObject prefab;

        public UILayer layer = UILayer.Screen;
        public UIShowMode showMode = UIShowMode.Normal;
        public UICloseMode closeMode = UICloseMode.Hide;

        [Header("显示设置")]
        public bool fullScreen = false;
        public bool showMask = false;
        public bool maskClickClose = false;
        public Color maskColor = new Color(0, 0, 0, 0.6f);

        [Header("交互设置")]
        public bool blockInput = true;
        public bool pauseGame = false;
        public bool allowESCClose = true;

        [Header("动画设置")]
        public UIAnimationType openAnimation = UIAnimationType.Fade;
        public UIAnimationType closeAnimation = UIAnimationType.Fade;
        public float animationDuration = 0.25f;

        [Header("其他")]
        public bool preload = false;
        public bool cache = true;
        public UIViewLifetime lifetime = UIViewLifetime.Session;
    }

    /// <summary>
    /// 面板打开参数
    /// </summary>
    public class UIOpenArgs
    {
        /// <summary>传递给面板的数据</summary>
        public object Data { get; set; }

        /// <summary>打开完成回调</summary>
        public Action OnOpened { get; set; }

        /// <summary>关闭完成回调</summary>
        public Action OnClosed { get; set; }

        /// <summary>是否立即显示（跳过动画）</summary>
        public bool Immediate { get; set; } = false;

        public static UIOpenArgs Create(object data = null)
        {
            return new UIOpenArgs { Data = data };
        }

        public UIOpenArgs WithCallback(Action onOpened = null, Action onClosed = null)
        {
            OnOpened = onOpened;
            OnClosed = onClosed;
            return this;
        }

        public UIOpenArgs SetImmediate(bool immediate = true)
        {
            Immediate = immediate;
            return this;
        }
    }

    /// <summary>
    /// 面板关闭参数
    /// </summary>
    public class UICloseArgs
    {
        /// <summary>关闭时的返回值</summary>
        public object Result { get; set; }

        /// <summary>是否立即关闭（跳过动画）</summary>
        public bool Immediate { get; set; } = false;

        /// <summary>是否强制关闭</summary>
        public bool Force { get; set; } = false;

        public static UICloseArgs Create(object result = null)
        {
            return new UICloseArgs { Result = result };
        }
    }
}
