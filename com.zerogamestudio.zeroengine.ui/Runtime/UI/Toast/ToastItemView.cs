using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ZeroEngine.UI.Toast
{
    public sealed class ToastItemView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image background;
        [SerializeField] private Image accent;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private Button button;

        private RectTransform rectTransform;
        private Coroutine lifeRoutine;
        private Coroutine moveRoutine;
        private ToastHandle handle;

        private RectTransform RectTransform => rectTransform != null ? rectTransform : rectTransform = (RectTransform)transform;

        public void Configure(CanvasGroup targetCanvasGroup, Image targetBackground, Image targetAccent, Image targetIcon, TMP_Text targetMessageText, Button targetButton)
        {
            canvasGroup = targetCanvasGroup;
            background = targetBackground;
            accent = targetAccent;
            icon = targetIcon;
            messageText = targetMessageText;
            button = targetButton;
        }

        private void Awake()
        {
            if (canvasGroup == null) canvasGroup = gameObject.GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
            if (button != null) button.onClick.AddListener(HandleClick);
        }

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(HandleClick);
        }

        public void Show(ToastHandle toastHandle, string message, ToastStyle style, ToastAnimationTimings timings)
        {
            handle = toastHandle;
            if (lifeRoutine != null) StopCoroutine(lifeRoutine);

            if (messageText != null)
            {
                messageText.text = message;
                messageText.color = style.TextColor;
            }

            if (background != null) background.color = style.BackgroundColor;
            if (accent != null) accent.color = style.AccentColor;
            if (icon != null)
            {
                icon.sprite = toastHandle.Request.Icon != null ? toastHandle.Request.Icon : style.Icon;
                icon.gameObject.SetActive(icon.sprite != null);
            }

            if (canvasGroup != null) canvasGroup.alpha = 1f;
            if (Application.isPlaying)
                lifeRoutine = StartCoroutine(LifeRoutine(timings));
        }

        public void MoveToIndex(int index, float spacing)
        {
            var target = new Vector2(0f, -index * spacing);
            if (!Application.isPlaying)
            {
                RectTransform.anchoredPosition = target;
                return;
            }

            if (moveRoutine != null) StopCoroutine(moveRoutine);
            moveRoutine = StartCoroutine(MoveRoutine(target, 0.16f));
        }

        public void DismissImmediate()
        {
            if (lifeRoutine != null) StopCoroutine(lifeRoutine);
            if (moveRoutine != null) StopCoroutine(moveRoutine);

            if (Application.isPlaying)
                Destroy(gameObject);
            else
                DestroyImmediate(gameObject);
        }

        private void HandleClick()
        {
            if (handle == null) return;
            handle.Request.OnClick?.Invoke(handle);
            if (handle.Request.DismissOnClick)
                handle.Dismiss(ToastDismissReason.Clicked);
        }

        private IEnumerator LifeRoutine(ToastAnimationTimings timings)
        {
            yield return Fade(0f, 1f, timings.fadeInSeconds);
            yield return Wait(timings.holdSeconds, handle != null && handle.Request.PauseWithGameTime);
            yield return Fade(1f, 0f, timings.fadeOutSeconds);
            handle?.Dismiss(ToastDismissReason.Expired);
        }

        private IEnumerator Fade(float from, float to, float duration)
        {
            if (canvasGroup == null) yield break;
            if (duration <= 0f)
            {
                canvasGroup.alpha = to;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            canvasGroup.alpha = to;
        }

        private IEnumerator MoveRoutine(Vector2 target, float duration)
        {
            var start = RectTransform.anchoredPosition;
            if (duration <= 0f)
            {
                RectTransform.anchoredPosition = target;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                RectTransform.anchoredPosition = Vector2.Lerp(start, target, 1f - Mathf.Pow(1f - t, 2f));
                yield return null;
            }

            RectTransform.anchoredPosition = target;
        }

        private static IEnumerator Wait(float seconds, bool scaled)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += scaled ? Time.deltaTime : Time.unscaledDeltaTime;
                yield return null;
            }
        }
    }
}
