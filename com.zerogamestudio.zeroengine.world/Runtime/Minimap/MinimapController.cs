using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;

namespace ZeroEngine.Minimap
{
    /// <summary>
    /// 小地图控制器
    /// 管理小地图相机、缩放、旋转模式
    /// </summary>
    public class MinimapController : MonoSingleton<MinimapController>
    {
        [Header("Camera Setup")]
        [SerializeField] private Camera _minimapCamera;
        [SerializeField] private float _cameraHeight = 50f;
        [SerializeField] private int _renderTextureSize = 256;
        [SerializeField] private LayerMask _cullingMask;

        [Header("Target")]
        [SerializeField] private Transform _followTarget;
        [SerializeField] private bool _smoothFollow = true;
        [SerializeField, Range(1f, 20f)] private float _followSpeed = 10f;

        [Header("Zoom")]
        [SerializeField] private MinimapZoomMode _zoomMode = MinimapZoomMode.Manual;
        [SerializeField, Range(10f, 200f)] private float _orthographicSize = 30f;
        [SerializeField] private float _minZoom = 15f;
        [SerializeField] private float _maxZoom = 100f;
        [SerializeField] private float _zoomSpeed = 5f;

        [Header("Rotation")]
        [SerializeField] private MinimapRotationMode _rotationMode = MinimapRotationMode.NorthUp;

        [Header("Marker Icons")]
        [SerializeField] private List<MarkerIconConfig> _markerIconConfigs = new List<MarkerIconConfig>();

        [Header("Debug")]
        [SerializeField] private bool _debugMode;

        // 事件
        public event Action<MinimapEventArgs> OnMinimapEvent;

        // 运行时
        private RenderTexture _renderTexture;
        private float _targetZoom;
        private Vector3 _targetPosition;
        private bool _listeningForMarkerEvents;
        private readonly Dictionary<MinimapMarkerType, MarkerIconConfig> _iconLookup = new Dictionary<MinimapMarkerType, MarkerIconConfig>();

        #region Properties

        public Camera MinimapCamera => _minimapCamera;
        public RenderTexture RenderTexture => _renderTexture;
        public float OrthographicSize => _orthographicSize;
        public float CurrentZoom => _orthographicSize;

        public Transform FollowTarget
        {
            get => _followTarget;
            set
            {
                _followTarget = value;
                OnMinimapEvent?.Invoke(MinimapEventArgs.TargetChanged(value));
            }
        }

        public MinimapZoomMode ZoomMode
        {
            get => _zoomMode;
            set => _zoomMode = value;
        }

        public MinimapRotationMode RotationMode
        {
            get => _rotationMode;
            set => _rotationMode = value;
        }

        #endregion

        #region Unity Lifecycle

        protected override void Awake()
        {
            base.Awake();
            BuildIconLookup();
            SetupCamera();
        }

        private void Start()
        {
            if (_followTarget == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                    _followTarget = player.transform;
            }

            _targetZoom = _orthographicSize;

            if (_followTarget != null)
                _targetPosition = GetCameraPosition(_followTarget.position);
        }

        private void OnEnable()
        {
            BindMarkerEvents();
        }

        private void OnDisable()
        {
            UnbindMarkerEvents();
        }

        private void LateUpdate()
        {
            if (_minimapCamera == null) return;

            UpdateCameraPosition();
            UpdateCameraRotation();
            UpdateZoom();
        }

        protected override void OnDestroy()
        {
            UnbindMarkerEvents();

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }
            base.OnDestroy();
        }

        #endregion

        #region Public API

        /// <summary>设置缩放级别</summary>
        public void SetZoom(float zoom)
        {
            _targetZoom = Mathf.Clamp(zoom, _minZoom, _maxZoom);
            OnMinimapEvent?.Invoke(MinimapEventArgs.ZoomChanged(_targetZoom));
            Log($"缩放设置: {_targetZoom}");
        }

        /// <summary>放大</summary>
        public void ZoomIn(float amount = 5f)
        {
            SetZoom(_targetZoom - amount);
        }

        /// <summary>缩小</summary>
        public void ZoomOut(float amount = 5f)
        {
            SetZoom(_targetZoom + amount);
        }

        /// <summary>获取标记图标配置</summary>
        public MarkerIconConfig GetIconConfig(MinimapMarkerType type)
        {
            _iconLookup.TryGetValue(type, out var config);
            return config;
        }

        /// <summary>世界坐标转小地图坐标 (归一化 0-1)</summary>
        public Vector2 WorldToMinimapNormalized(Vector3 worldPosition)
        {
            if (_minimapCamera == null || _followTarget == null) return Vector2.zero;

            Vector3 cameraPos = _minimapCamera.transform.position;
            float size = _orthographicSize;

            float x = (worldPosition.x - cameraPos.x) / (size * 2f) + 0.5f;
            float y = (worldPosition.z - cameraPos.z) / (size * 2f) + 0.5f;

            return new Vector2(x, y);
        }

        /// <summary>检查世界坐标是否在小地图范围内</summary>
        public bool IsInMinimapBounds(Vector3 worldPosition)
        {
            Vector2 normalized = WorldToMinimapNormalized(worldPosition);
            return normalized.x >= 0f && normalized.x <= 1f &&
                   normalized.y >= 0f && normalized.y <= 1f;
        }

        /// <summary>获取可见范围内的所有标记</summary>
        public void GetVisibleMarkers(List<MinimapMarker> results)
        {
            results.Clear();
            foreach (var marker in MinimapMarkerManager.Markers)
            {
                if (marker == null) continue;
                if (IsInMinimapBounds(marker.transform.position))
                    results.Add(marker);
            }
        }

        /// <summary>强制刷新相机设置</summary>
        public void RefreshCamera()
        {
            SetupCamera();
        }

        #endregion

        #region Internal

        private void SetupCamera()
        {
            if (_minimapCamera == null)
            {
                var camGO = new GameObject("MinimapCamera");
                camGO.transform.SetParent(transform);
                _minimapCamera = camGO.AddComponent<Camera>();
            }

            // 配置相机
            _minimapCamera.orthographic = true;
            _minimapCamera.orthographicSize = _orthographicSize;
            _minimapCamera.cullingMask = _cullingMask;
            _minimapCamera.clearFlags = CameraClearFlags.SolidColor;
            _minimapCamera.backgroundColor = Color.black;

            // 创建 RenderTexture
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }

            _renderTexture = new RenderTexture(_renderTextureSize, _renderTextureSize, 16);
            _renderTexture.filterMode = FilterMode.Bilinear;
            _minimapCamera.targetTexture = _renderTexture;

            // 初始旋转 (俯视)
            _minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            Log($"相机初始化: Size={_renderTextureSize}, Ortho={_orthographicSize}");
        }

        private void BindMarkerEvents()
        {
            if (_listeningForMarkerEvents)
            {
                return;
            }

            MinimapMarkerManager.OnMarkerRegistered += HandleMarkerRegistered;
            MinimapMarkerManager.OnMarkerUnregistered += HandleMarkerUnregistered;
            _listeningForMarkerEvents = true;
        }

        private void UnbindMarkerEvents()
        {
            if (!_listeningForMarkerEvents)
            {
                return;
            }

            MinimapMarkerManager.OnMarkerRegistered -= HandleMarkerRegistered;
            MinimapMarkerManager.OnMarkerUnregistered -= HandleMarkerUnregistered;
            _listeningForMarkerEvents = false;
        }

        private void HandleMarkerRegistered(MinimapMarker marker)
        {
            OnMinimapEvent?.Invoke(MinimapEventArgs.MarkerAdded(marker));
        }

        private void HandleMarkerUnregistered(MinimapMarker marker)
        {
            OnMinimapEvent?.Invoke(MinimapEventArgs.MarkerRemoved(marker));
        }

        private void UpdateCameraPosition()
        {
            if (_followTarget == null) return;

            _targetPosition = GetCameraPosition(_followTarget.position);

            if (_smoothFollow)
            {
                _minimapCamera.transform.position = Vector3.Lerp(
                    _minimapCamera.transform.position,
                    _targetPosition,
                    Time.deltaTime * _followSpeed);
            }
            else
            {
                _minimapCamera.transform.position = _targetPosition;
            }
        }

        private void UpdateCameraRotation()
        {
            float yRotation = 0f;

            switch (_rotationMode)
            {
                case MinimapRotationMode.NorthUp:
                    yRotation = 0f;
                    break;

                case MinimapRotationMode.PlayerUp:
                    if (_followTarget != null)
                        yRotation = _followTarget.eulerAngles.y;
                    break;
            }

            _minimapCamera.transform.rotation = Quaternion.Euler(90f, yRotation, 0f);
        }

        private void UpdateZoom()
        {
            if (_zoomMode == MinimapZoomMode.Fixed) return;

            if (Mathf.Abs(_orthographicSize - _targetZoom) > 0.01f)
            {
                _orthographicSize = Mathf.Lerp(_orthographicSize, _targetZoom, Time.deltaTime * _zoomSpeed);
                _minimapCamera.orthographicSize = _orthographicSize;
            }
        }

        private Vector3 GetCameraPosition(Vector3 targetPos)
        {
            return new Vector3(targetPos.x, targetPos.y + _cameraHeight, targetPos.z);
        }

        private void BuildIconLookup()
        {
            _iconLookup.Clear();
            foreach (var config in _markerIconConfigs)
            {
                if (config != null)
                    _iconLookup[config.MarkerType] = config;
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("ZEROENGINE_DEBUG")]
        private void Log(string message)
        {
            if (_debugMode) Debug.Log($"[Minimap] {message}");
        }

        #endregion

        #region Editor

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_followTarget == null) return;

            Vector3 center = GetCameraPosition(_followTarget.position);
            float size = _orthographicSize;

            Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
            Gizmos.DrawWireCube(center, new Vector3(size * 2f, 1f, size * 2f));
        }
#endif

        #endregion
    }
}
