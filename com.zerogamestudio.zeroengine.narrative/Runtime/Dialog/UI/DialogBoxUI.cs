using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

#if TEXTMESHPRO_ENABLED
using TMPro;
#endif

namespace ZeroEngine.Dialog.UI
{
    /// <summary>
    /// Complete dialog box UI component with portrait support.
    /// Features:
    /// - Speaker name display
    /// - Text with typewriter effect
    /// - Portrait/expression display
    /// - Choice buttons
    /// - Continue indicator
    /// - Narration mode (no speaker box)
    /// </summary>
    public class DialogBoxUI : MonoBehaviour
    {
        #region References

        [Header("Panel References")]
        [SerializeField] private GameObject _dialogPanel;
        [SerializeField] private GameObject _speakerNamePanel;
        [SerializeField] private GameObject _portraitPanel;

        [Header("Text References")]
#if TEXTMESHPRO_ENABLED
        [SerializeField] private TextMeshProUGUI _speakerText;
        [SerializeField] private TextMeshProUGUI _dialogText;
#else
        [SerializeField] private Text _speakerText;
        [SerializeField] private Text _dialogText;
#endif

        [Header("Portrait References")]
        [SerializeField] private Image _portraitImage;
        [SerializeField] private Image _portraitLeftImage;
        [SerializeField] private Image _portraitRightImage;

        [Header("Choice References")]
        [SerializeField] private Transform _choicesContainer;
        [SerializeField] private Button _choiceButtonPrefab;

        [Header("Indicators")]
        [SerializeField] private GameObject _continueIndicator;

        #endregion

        #region Settings

        [Header("Portrait Settings")]
        [SerializeField] private PortraitDisplayMode _portraitMode = PortraitDisplayMode.Single;
        [SerializeField] private List<PortraitBinding> _portraitBindings = new List<PortraitBinding>();
        [SerializeField] private bool _cachePortraits = true;

        [Header("Narration Settings")]
        [Tooltip("Hide speaker panel when speaker is empty")]
        [SerializeField] private bool _supportNarration = true;

        #endregion

        #region Events

        /// <summary>Fired when continue is clicked.</summary>
        public event Action OnContinueClicked;

        /// <summary>Fired when a choice is selected.</summary>
        public event Action<int> OnChoiceSelected;

        #endregion

        #region State

        private readonly List<Button> _activeChoiceButtons = new List<Button>();
        private readonly Dictionary<string, Sprite> _portraitCache = new Dictionary<string, Sprite>();
        private readonly Dictionary<string, Sprite> _registeredPortraits = new Dictionary<string, Sprite>();
        private string _currentSpeaker;
        private bool _isShowingChoices;

        #endregion

        #region Public API

        /// <summary>
        /// Show the dialog panel.
        /// </summary>
        public void Show()
        {
            if (_dialogPanel != null)
                _dialogPanel.SetActive(true);
        }

        /// <summary>
        /// Hide the dialog panel.
        /// </summary>
        public void Hide()
        {
            if (_dialogPanel != null)
                _dialogPanel.SetActive(false);

            ClearChoices();
            HidePortrait();
        }

        /// <summary>
        /// Display a dialog line.
        /// </summary>
        public void ShowLine(DialogLine line)
        {
            _isShowingChoices = false;
            ClearChoices();

            // Speaker
            UpdateSpeaker(line.Speaker);

            // Portrait
            if (!string.IsNullOrEmpty(line.PortraitKey))
            {
                ShowPortrait(line.PortraitKey, line.Speaker);
            }
            else
            {
                HidePortrait();
            }

            // Continue indicator
            if (_continueIndicator != null)
                _continueIndicator.SetActive(true);
        }

        /// <summary>
        /// Update displayed text (used by typewriter).
        /// </summary>
        public void UpdateText(string text)
        {
            if (_dialogText != null)
                _dialogText.text = text;
        }

        /// <summary>
        /// Show choices.
        /// </summary>
        public void ShowChoices(List<DialogChoice> choices)
        {
            _isShowingChoices = true;
            ClearChoices();

            if (_continueIndicator != null)
                _continueIndicator.SetActive(false);

            if (_choicesContainer == null || _choiceButtonPrefab == null) return;

            for (int i = 0; i < choices.Count; i++)
            {
                var choice = choices[i];
                var btn = Instantiate(_choiceButtonPrefab, _choicesContainer);
                btn.gameObject.SetActive(true);

#if TEXTMESHPRO_ENABLED
                var txt = btn.GetComponentInChildren<TextMeshProUGUI>();
#else
                var txt = btn.GetComponentInChildren<Text>();
#endif
                if (txt != null) txt.text = choice.Text;

                btn.interactable = choice.IsEnabled;

                int index = i;
                btn.onClick.AddListener(() => SelectChoice(index));

                _activeChoiceButtons.Add(btn);
            }
        }

        /// <summary>
        /// Set whether continue indicator is visible.
        /// </summary>
        public void SetContinueIndicatorVisible(bool visible)
        {
            if (_continueIndicator != null && !_isShowingChoices)
                _continueIndicator.SetActive(visible);
        }

        /// <summary>
        /// Trigger continue action.
        /// </summary>
        public void Continue()
        {
            if (_isShowingChoices) return;
            OnContinueClicked?.Invoke();
        }

        #endregion

        #region Portrait Management

        private void UpdateSpeaker(string speaker)
        {
            _currentSpeaker = speaker;
            bool isNarration = string.IsNullOrEmpty(speaker);

            if (_speakerText != null)
                _speakerText.text = speaker ?? "";

            if (_supportNarration && _speakerNamePanel != null)
                _speakerNamePanel.SetActive(!isNarration);
        }

        private void ShowPortrait(string portraitKey, string speaker = null)
        {
            Sprite sprite = LoadPortrait(portraitKey);
            if (sprite == null) return;

            switch (_portraitMode)
            {
                case PortraitDisplayMode.Single:
                    if (_portraitImage != null)
                    {
                        _portraitImage.sprite = sprite;
                        _portraitImage.gameObject.SetActive(true);
                    }
                    if (_portraitPanel != null)
                        _portraitPanel.SetActive(true);
                    break;

                case PortraitDisplayMode.LeftRight:
                    // Determine side based on speaker or key convention
                    bool isLeft = portraitKey.Contains("_L") || portraitKey.Contains("_left");
                    Image targetImage = isLeft ? _portraitLeftImage : _portraitRightImage;

                    if (targetImage != null)
                    {
                        targetImage.sprite = sprite;
                        targetImage.gameObject.SetActive(true);
                    }
                    break;
            }
        }

        private void HidePortrait()
        {
            if (_portraitImage != null)
                _portraitImage.gameObject.SetActive(false);
            if (_portraitLeftImage != null)
                _portraitLeftImage.gameObject.SetActive(false);
            if (_portraitRightImage != null)
                _portraitRightImage.gameObject.SetActive(false);
            if (_portraitPanel != null)
                _portraitPanel.SetActive(false);
        }

        private Sprite LoadPortrait(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            if (_cachePortraits && _portraitCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            if (_registeredPortraits.TryGetValue(key, out var registered))
            {
                CachePortrait(key, registered);
                return registered;
            }

            foreach (var binding in _portraitBindings)
            {
                if (binding == null || string.IsNullOrEmpty(binding.Key) || binding.Sprite == null)
                {
                    continue;
                }

                if (string.Equals(binding.Key, key, StringComparison.Ordinal))
                {
                    CachePortrait(key, binding.Sprite);
                    return binding.Sprite;
                }
            }

            return null;
        }

        public void RegisterPortrait(string key, Sprite sprite)
        {
            if (string.IsNullOrEmpty(key) || sprite == null)
            {
                return;
            }

            _registeredPortraits[key] = sprite;
            CachePortrait(key, sprite);
        }

        private void CachePortrait(string key, Sprite sprite)
        {
            if (_cachePortraits && sprite != null)
            {
                _portraitCache[key] = sprite;
            }
        }

        /// <summary>
        /// Clear portrait cache (call on scene unload).
        /// </summary>
        public void ClearPortraitCache()
        {
            _portraitCache.Clear();
        }

        #endregion

        #region Choice Management

        private void ClearChoices()
        {
            foreach (var btn in _activeChoiceButtons)
            {
                if (btn != null)
                    Destroy(btn.gameObject);
            }
            _activeChoiceButtons.Clear();
        }

        private void SelectChoice(int index)
        {
            _isShowingChoices = false;
            OnChoiceSelected?.Invoke(index);
        }

        #endregion

        #region Input Handling

        private void Update()
        {
            // Optional: Handle keyboard/gamepad input
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                Continue();
            }
        }

        #endregion

        private void OnDestroy()
        {
            ClearChoices();
            ClearPortraitCache();
        }
    }

    /// <summary>
    /// Portrait display mode.
    /// </summary>
    public enum PortraitDisplayMode
    {
        /// <summary>Single portrait slot.</summary>
        Single,

        /// <summary>Left and right portrait slots for conversations.</summary>
        LeftRight
    }

    [Serializable]
    public sealed class PortraitBinding
    {
        public string Key;
        public Sprite Sprite;
    }
}
