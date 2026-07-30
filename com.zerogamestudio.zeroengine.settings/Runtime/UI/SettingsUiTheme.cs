using UnityEngine;

namespace ZeroEngine.PlayerSettings.UI
{
    [CreateAssetMenu(fileName = "SettingsUiTheme", menuName = "ZeroEngine/Settings/UI Theme")]
    public sealed class SettingsUiTheme : ScriptableObject
    {
        [SerializeField] private Font font;
        [SerializeField] private Sprite panelSprite;
        [SerializeField] private Sprite rowSprite;
        [SerializeField] private Sprite buttonSprite;
        [SerializeField] private Sprite sliderTrackSprite;
        [SerializeField] private Sprite sliderFillSprite;
        [SerializeField] private Sprite sliderHandleSprite;
        [SerializeField] private Sprite toggleBackgroundSprite;
        [SerializeField] private Sprite toggleCheckmarkSprite;
        [SerializeField] private Color panelColor = new(0.055f, 0.045f, 0.04f, 0.98f);
        [SerializeField] private Color rowColor = new(0.12f, 0.095f, 0.075f, 0.82f);
        [SerializeField] private Color buttonColor = new(0.14f, 0.105f, 0.085f, 1f);
        [SerializeField] private Color primaryButtonColor = new(0.36f, 0.20f, 0.065f, 1f);
        [SerializeField] private Color textColor = new(0.95f, 0.92f, 0.86f, 1f);
        [SerializeField] private Color mutedTextColor = new(0.67f, 0.62f, 0.56f, 1f);
        [SerializeField] private Color accentColor = new(0.96f, 0.68f, 0.24f, 1f);
        [SerializeField] private Color sliderTrackColor = new(0.24f, 0.18f, 0.13f, 1f);

        public SettingsUiStyle Resolve(Font fallbackFont = null)
        {
            return new SettingsUiStyle
            {
                Font = font != null ? font : SettingsUiStyle.ResolveFont(fallbackFont),
                PanelSprite = panelSprite,
                RowSprite = rowSprite,
                ButtonSprite = buttonSprite,
                SliderTrackSprite = sliderTrackSprite,
                SliderFillSprite = sliderFillSprite,
                SliderHandleSprite = sliderHandleSprite,
                ToggleBackgroundSprite = toggleBackgroundSprite,
                ToggleCheckmarkSprite = toggleCheckmarkSprite,
                PanelColor = panelColor,
                RowColor = rowColor,
                ButtonColor = buttonColor,
                PrimaryButtonColor = primaryButtonColor,
                TextColor = textColor,
                MutedTextColor = mutedTextColor,
                AccentColor = accentColor,
                SliderTrackColor = sliderTrackColor
            };
        }
    }

    public sealed class SettingsUiStyle
    {
        public Font Font { get; set; }
        public Sprite PanelSprite { get; set; }
        public Sprite RowSprite { get; set; }
        public Sprite ButtonSprite { get; set; }
        public Sprite SliderTrackSprite { get; set; }
        public Sprite SliderFillSprite { get; set; }
        public Sprite SliderHandleSprite { get; set; }
        public Sprite ToggleBackgroundSprite { get; set; }
        public Sprite ToggleCheckmarkSprite { get; set; }
        public Color PanelColor { get; set; }
        public Color RowColor { get; set; }
        public Color ButtonColor { get; set; }
        public Color PrimaryButtonColor { get; set; }
        public Color TextColor { get; set; }
        public Color MutedTextColor { get; set; }
        public Color AccentColor { get; set; }
        public Color SliderTrackColor { get; set; }

        public static SettingsUiStyle CreateFallback(Font preferredFont = null)
        {
            return new SettingsUiStyle
            {
                Font = ResolveFont(preferredFont),
                PanelColor = new Color(0.055f, 0.045f, 0.04f, 0.98f),
                RowColor = new Color(0.12f, 0.095f, 0.075f, 0.82f),
                ButtonColor = new Color(0.14f, 0.105f, 0.085f, 1f),
                PrimaryButtonColor = new Color(0.36f, 0.20f, 0.065f, 1f),
                TextColor = new Color(0.95f, 0.92f, 0.86f, 1f),
                MutedTextColor = new Color(0.67f, 0.62f, 0.56f, 1f),
                AccentColor = new Color(0.96f, 0.68f, 0.24f, 1f),
                SliderTrackColor = new Color(0.24f, 0.18f, 0.13f, 1f)
            };
        }

        internal static Font ResolveFont(Font preferredFont)
        {
            if (preferredFont != null)
            {
                return preferredFont;
            }

            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }
}
