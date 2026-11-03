using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZGS.Analytics;

namespace ZeroEngine.Feedback
{
    public static class FeedbackPanel
    {
        private static FeedbackUiConfiguration _configuration = new FeedbackUiConfiguration();
        private static DefaultFeedbackPanelView _view;
        private static GameObject _ownedCanvas;
        private static GameObject _detailsCanvas;
        private static FeedbackSubmissionHistoryView _details;

        internal static FeedbackUiConfiguration CurrentConfiguration => _configuration;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            _configuration = new FeedbackUiConfiguration();
            _view = null;
            _ownedCanvas = null;
            _detailsCanvas = null;
            _details = null;
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

        public static FeedbackSubmissionHistoryView OpenSubmissionDetails(FeedbackSubmissionResult? localFailure = null)
        {
            CloseSubmissionDetails();
            RectTransform parent = _configuration.Parent;
            if (parent == null)
            {
                _detailsCanvas = new GameObject("Feedback Details Canvas", typeof(RectTransform),
                    typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
                Object.DontDestroyOnLoad(_detailsCanvas);
                var canvas = _detailsCanvas.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32001;
                var scaler = _detailsCanvas.GetComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;
                parent = (RectTransform)_detailsCanvas.transform;
            }
            _details = FeedbackSubmissionHistoryView.Create(parent, _configuration, localFailure, CloseSubmissionDetails);
            return _details;
        }

        public static void CloseSubmissionDetails(FeedbackSubmissionHistoryView owner)
        {
            if (owner != null && _details == owner) CloseSubmissionDetails();
        }

        public static void CloseSubmissionDetails()
        {
            if (_details != null) _details.ReleaseSelection();
            if (_details != null) Object.Destroy(_details.gameObject);
            if (_detailsCanvas != null) Object.Destroy(_detailsCanvas);
            _details = null;
            _detailsCanvas = null;
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
