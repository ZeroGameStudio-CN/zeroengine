using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZeroEngine.Feedback
{
    public static class FeedbackPanel
    {
        private static FeedbackUiConfiguration _configuration = new FeedbackUiConfiguration();
        private static DefaultFeedbackPanelView _view;
        private static GameObject _ownedCanvas;

        internal static FeedbackUiConfiguration CurrentConfiguration => _configuration;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            _configuration = new FeedbackUiConfiguration();
            _view = null;
            _ownedCanvas = null;
        }

        public static void Configure(FeedbackUiConfiguration configuration)
        {
            _configuration = configuration ?? new FeedbackUiConfiguration();
            FeedbackStatusCoordinator.Configure(_configuration);
        }

        public static void Open()
        {
            if (_view != null)
            {
                _view.gameObject.SetActive(true);
                _view.FocusDescription();
                return;
            }

            FeedbackStatusCoordinator.Configure(_configuration);
            if (EventSystem.current == null)
            {
                Debug.LogWarning(
                    "[ZeroEngine.Feedback] No EventSystem found. Add one using the input module selected by the project.");
            }

            RectTransform parent = _configuration.Parent;
            if (parent == null)
            {
                _ownedCanvas = new GameObject(
                    "ZeroEngine Feedback Canvas",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster));
                Object.DontDestroyOnLoad(_ownedCanvas);

                var canvas = _ownedCanvas.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32000;

                var scaler = _ownedCanvas.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                parent = _ownedCanvas.GetComponent<RectTransform>();
            }

            if (_configuration.PanelPrefab == null)
            {
                _view = DefaultFeedbackPanelView.Create(parent, _configuration);
            }
            else
            {
                _view = Object.Instantiate(_configuration.PanelPrefab, parent, false);
                _view.Initialize(_configuration);
            }
        }

        public static void Close()
        {
            if (_view != null && _view.IsSubmitting)
                return;

            DestroyView();
        }

        internal static void CloseAfterAccepted()
        {
            DestroyView();
        }

        private static void DestroyView()
        {
            if (_view != null)
                Object.Destroy(_view.gameObject);
            if (_ownedCanvas != null)
                Object.Destroy(_ownedCanvas);

            _view = null;
            _ownedCanvas = null;
        }
    }
}
