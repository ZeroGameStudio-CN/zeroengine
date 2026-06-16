using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ZeroEngine.Tutorial
{
    /// <summary>
    /// 教程对话视图 (v1.14.0+)
    /// 显示教程对话文本，支持打字机效果
    /// </summary>
    public class TutorialDialogueView : MonoBehaviour
    {
        #region UI References

        [Header("UI References")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Text _dialogueText;
        [SerializeField] private Text _speakerNameText;
        [SerializeField] private Image _speakerAvatarImage;
        [SerializeField] private GameObject _continueIndicator;
        [SerializeField] private Button _continueButton;
        [SerializeField] private Button _skipButton;
        [SerializeField] private Transform _choicesContainer;
        [SerializeField] private GameObject _choiceButtonPrefab;

        [Header("Layout")]
        [SerializeField] private RectTransform _dialogueBox;
        [SerializeField] private DialoguePosition _defaultPosition = DialoguePosition.Bottom;

        #endregion

        #region Runtime

        private TutorialUIManager _manager;
        private Coroutine _typewriterCoroutine;
        private string _fullText;
        private bool _isTyping;
        private bool _waitingForInput;

        // 选项按钮池
        private readonly System.Collections.Generic.List<Button> _choiceButtons = new();

        #endregion

        #region Initialization

        public void Initialize(TutorialUIManager manager)
        {
            _manager = manager;

            // 绑定按钮事件
            if (_continueButton != null)
            {
                _continueButton.onClick.AddListener(OnContinueClicked);
            }

            if (_skipButton != null)
            {
                _skipButton.onClick.AddListener(OnSkipClicked);
            }

            // 初始状态
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
            }

            SetPosition(_defaultPosition);
            HideChoices();
        }

        #endregion

        #region Public API

        /// <summary>
        /// 显示对话
        /// </summary>
        public void ShowDialogue(string text, string speakerName = null, Sprite speakerAvatar = null)
        {
            gameObject.SetActive(true);
            HideChoices();

            // 设置说话者信息
            SetSpeaker(speakerName, speakerAvatar);

            // 开始打字机效果
            _fullText = text;
            StartTypewriter(text);

            // 淡入
            StartCoroutine(FadeIn());
        }

        /// <summary>
        /// 显示带选项的对话
        /// </summary>
        public void ShowDialogueWithChoices(string text, string[] choices, string speakerName = null)
        {
            gameObject.SetActive(true);

            // 设置说话者
            SetSpeaker(speakerName, null);

            // 显示文本
            _fullText = text;

            // 先显示完整文本再显示选项
            StopTypewriter();
            if (_dialogueText != null)
            {
                _dialogueText.text = text;
            }

            // 显示选项
            ShowChoices(choices);

            // 淡入
            StartCoroutine(FadeIn());
        }

        /// <summary>
        /// 隐藏对话
        /// </summary>
        public void Hide()
        {
            StopTypewriter();
            StartCoroutine(FadeOutAndDeactivate());
        }

        /// <summary>
        /// 跳过打字机效果
        /// </summary>
        public void SkipTypewriter()
        {
            if (_isTyping)
            {
                StopTypewriter();
                if (_dialogueText != null)
                {
                    _dialogueText.text = _fullText;
                }
                ShowContinueIndicator(true);
                _waitingForInput = true;
            }
            else if (_waitingForInput)
            {
                OnContinueClicked();
            }
        }

        /// <summary>
        /// 设置对话框位置
        /// </summary>
        public void SetPosition(DialoguePosition position)
        {
            if (_dialogueBox == null) return;

            switch (position)
            {
                case DialoguePosition.Top:
                    _dialogueBox.anchorMin = new Vector2(0.5f, 1f);
                    _dialogueBox.anchorMax = new Vector2(0.5f, 1f);
                    _dialogueBox.pivot = new Vector2(0.5f, 1f);
                    _dialogueBox.anchoredPosition = new Vector2(0, -50);
                    break;

                case DialoguePosition.Center:
                    _dialogueBox.anchorMin = new Vector2(0.5f, 0.5f);
                    _dialogueBox.anchorMax = new Vector2(0.5f, 0.5f);
                    _dialogueBox.pivot = new Vector2(0.5f, 0.5f);
                    _dialogueBox.anchoredPosition = Vector2.zero;
                    break;

                case DialoguePosition.Bottom:
                default:
                    _dialogueBox.anchorMin = new Vector2(0.5f, 0f);
                    _dialogueBox.anchorMax = new Vector2(0.5f, 0f);
                    _dialogueBox.pivot = new Vector2(0.5f, 0f);
                    _dialogueBox.anchoredPosition = new Vector2(0, 50);
                    break;
            }
        }

        #endregion

        #region Private Methods

        private void SetSpeaker(string name, Sprite avatar)
        {
            if (_speakerNameText != null)
            {
                _speakerNameText.text = name ?? "";
                _speakerNameText.gameObject.SetActive(!string.IsNullOrEmpty(name));
            }

            if (_speakerAvatarImage != null)
            {
                _speakerAvatarImage.sprite = avatar;
                _speakerAvatarImage.gameObject.SetActive(avatar != null);
            }
        }

        private void StartTypewriter(string text)
        {
            StopTypewriter();
            _typewriterCoroutine = StartCoroutine(TypewriterCoroutine(text));
        }

        private void StopTypewriter()
        {
            if (_typewriterCoroutine != null)
            {
                StopCoroutine(_typewriterCoroutine);
                _typewriterCoroutine = null;
            }
            _isTyping = false;
        }

        private IEnumerator TypewriterCoroutine(string text)
        {
            _isTyping = true;
            _waitingForInput = false;
            ShowContinueIndicator(false);

            if (_dialogueText != null)
            {
                _dialogueText.text = "";
            }

            float charDelay = 1f / (_manager?.Config?.DefaultTypewriterSpeed ?? 30f);
            int charIndex = 0;

            while (charIndex < text.Length)
            {
                // 处理富文本标签
                if (text[charIndex] == '<')
                {
                    int closeIndex = text.IndexOf('>', charIndex);
                    if (closeIndex > charIndex)
                    {
                        // 添加完整标签
                        string tag = text.Substring(charIndex, closeIndex - charIndex + 1);
                        if (_dialogueText != null)
                        {
                            _dialogueText.text += tag;
                        }
                        charIndex = closeIndex + 1;
                        continue;
                    }
                }

                // 添加单个字符
                if (_dialogueText != null)
                {
                    _dialogueText.text += text[charIndex];
                }

                // 播放打字音效
                if (charIndex % 3 == 0) // 每 3 个字符播放一次
                {
                    _manager?.PlayDialogueSound();
                }

                charIndex++;
                yield return new WaitForSecondsRealtime(charDelay);
            }

            _isTyping = false;
            _waitingForInput = true;
            ShowContinueIndicator(true);
        }

        private void ShowContinueIndicator(bool show)
        {
            if (_continueIndicator != null)
            {
                _continueIndicator.SetActive(show);
            }
        }

        private void ShowChoices(string[] choices)
        {
            if (_choicesContainer == null || _choiceButtonPrefab == null)
            {
                Debug.LogWarning("[Tutorial] Choices container or prefab not set");
                return;
            }

            // 清除旧按钮
            HideChoices();

            // 创建新按钮
            for (int i = 0; i < choices.Length; i++)
            {
                var buttonGo = Instantiate(_choiceButtonPrefab, _choicesContainer);
                var button = buttonGo.GetComponent<Button>();

                if (button != null)
                {
                    var text = buttonGo.GetComponentInChildren<Text>();
                    if (text != null)
                    {
                        text.text = choices[i];
                    }

                    int choiceIndex = i;
                    button.onClick.AddListener(() => OnChoiceClicked(choiceIndex));
                    _choiceButtons.Add(button);
                }

                buttonGo.SetActive(true);
            }

            _choicesContainer.gameObject.SetActive(true);
            ShowContinueIndicator(false);
        }

        private void HideChoices()
        {
            foreach (var button in _choiceButtons)
            {
                if (button != null)
                {
                    Destroy(button.gameObject);
                }
            }
            _choiceButtons.Clear();

            if (_choicesContainer != null)
            {
                _choicesContainer.gameObject.SetActive(false);
            }
        }

        private IEnumerator FadeIn()
        {
            if (_canvasGroup == null)
            {
                yield break;
            }

            float duration = _manager?.Config?.UIFadeInDuration ?? 0.3f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / duration);
                yield return null;
            }

            _canvasGroup.alpha = 1f;
        }

        private IEnumerator FadeOutAndDeactivate()
        {
            if (_canvasGroup == null)
            {
                gameObject.SetActive(false);
                yield break;
            }

            float duration = _manager?.Config?.UIFadeOutDuration ?? 0.2f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / duration);
                yield return null;
            }

            _canvasGroup.alpha = 0f;
            gameObject.SetActive(false);
        }

        #endregion

        #region Event Handlers

        private void OnContinueClicked()
        {
            if (_isTyping)
            {
                // 跳过打字机
                SkipTypewriter();
            }
            else
            {
                // 继续下一步
                _manager?.PlayConfirmSound();
                _manager?.TriggerDialogueContinue();
            }
        }

        private void OnSkipClicked()
        {
            _manager?.TriggerSkipClicked();
        }

        private void OnChoiceClicked(int index)
        {
            _manager?.PlayConfirmSound();
            _manager?.TriggerChoiceSelected(index);
        }

        #endregion

        #region Input Handling

        private void Update()
        {
            // 键盘输入处理
            var config = _manager?.Config;
            if (config == null) return;

            if (Input.GetKeyDown(config.DialogueConfirmKey))
            {
                if (_isTyping)
                {
                    SkipTypewriter();
                }
                else if (_waitingForInput && _choiceButtons.Count == 0)
                {
                    OnContinueClicked();
                }
            }

            if (Input.GetKeyDown(config.SkipKey))
            {
                OnSkipClicked();
            }
        }

        #endregion

        private void OnDestroy()
        {
            StopTypewriter();
            HideChoices();
        }
    }

}
