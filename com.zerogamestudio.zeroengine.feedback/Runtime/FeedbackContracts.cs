using System;
using TMPro;
using UnityEngine;
using ZGS.Analytics;

namespace ZeroEngine.Feedback
{
    public enum FeedbackTextId
    {
        Title,
        Description,
        ContactOptional,
        AttachmentOptional,
        Send,
        Cancel,
        Uploading,
        Uploaded,
        UploadFailed
    }

    public sealed class FeedbackFormData
    {
        public string Description;
        public string Contact;
        public string[] Attachments = Array.Empty<string>();
    }

    public interface IFeedbackTextResolver
    {
        string Resolve(FeedbackTextId textId);
    }

    public interface IFeedbackRequestDecorator
    {
        void Decorate(FeedbackSubmissionRequest request);
    }

    public interface IFeedbackAttachmentPicker
    {
        void PickAttachments(int remainingSlots, Action<string[]> completed);
    }

    public interface IFeedbackStatusPresenter
    {
        void Show(FeedbackTextId status, string text);
    }

    [Serializable]
    public sealed class FeedbackUiConfiguration
    {
        public FeedbackUiTheme Theme;
        public IFeedbackTextResolver TextResolver;
        public IFeedbackRequestDecorator RequestDecorator;
        public IFeedbackAttachmentPicker AttachmentPicker;
        public IFeedbackStatusPresenter StatusPresenter;
        public RectTransform Parent;
        public DefaultFeedbackPanelView PanelPrefab;
        [Range(1, 3)] public int MaxAttachments = 3;

        internal int AttachmentLimit => Mathf.Clamp(MaxAttachments, 1, 3);
    }

    [CreateAssetMenu(fileName = "FeedbackUiTheme", menuName = "ZeroEngine/Feedback UI Theme")]
    public sealed class FeedbackUiTheme : ScriptableObject
    {
        public TMP_FontAsset Font;
        public Sprite PanelSprite;
        public Sprite ButtonSprite;
        public Color OverlayColor = new Color(0f, 0f, 0f, 0.72f);
        public Color PanelColor = new Color(0.10f, 0.11f, 0.14f, 0.98f);
        public Color InputColor = new Color(0.16f, 0.17f, 0.21f, 1f);
        public Color PrimaryColor = new Color(0.20f, 0.52f, 0.96f, 1f);
        public Color SecondaryColor = new Color(0.24f, 0.25f, 0.30f, 1f);
        public Color TextColor = Color.white;
        [Min(8f)] public float Spacing = 16f;
        [Min(8f)] public float Padding = 24f;

        internal static FeedbackUiTheme CreateRuntimeDefault()
        {
            var theme = CreateInstance<FeedbackUiTheme>();
            theme.hideFlags = HideFlags.HideAndDontSave;
            return theme;
        }
    }
}
