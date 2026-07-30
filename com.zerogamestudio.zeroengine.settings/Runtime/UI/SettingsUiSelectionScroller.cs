using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZeroEngine.PlayerSettings.UI
{
    [DisallowMultipleComponent]
    public sealed class SettingsUiSelectionScroller : MonoBehaviour
    {
        private ScrollRect _scrollRect;
        private GameObject _lastSelection;

        public void Initialize(ScrollRect scrollRect)
        {
            _scrollRect = scrollRect;
            _lastSelection = null;
        }

        public void EnsureVisible(RectTransform target)
        {
            if (_scrollRect == null
                || _scrollRect.content == null
                || _scrollRect.viewport == null
                || target == null
                || !target.IsChildOf(_scrollRect.content))
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            Bounds targetBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                _scrollRect.viewport,
                target);
            Rect viewportRect = _scrollRect.viewport.rect;
            float offset = 0f;
            if (targetBounds.min.y < viewportRect.yMin)
            {
                offset = viewportRect.yMin - targetBounds.min.y;
            }
            else if (targetBounds.max.y > viewportRect.yMax)
            {
                offset = viewportRect.yMax - targetBounds.max.y;
            }

            if (Mathf.Approximately(offset, 0f))
            {
                return;
            }

            _scrollRect.StopMovement();
            Vector2 position = _scrollRect.content.anchoredPosition;
            position.y += offset;
            _scrollRect.content.anchoredPosition = position;
            _scrollRect.verticalNormalizedPosition = Mathf.Clamp01(
                _scrollRect.verticalNormalizedPosition);
            Canvas.ForceUpdateCanvases();
        }

        private void LateUpdate()
        {
            GameObject selection = EventSystem.current?.currentSelectedGameObject;
            if (selection == null || selection == _lastSelection)
            {
                return;
            }

            _lastSelection = selection;
            if (selection.transform is RectTransform rect)
            {
                EnsureVisible(rect);
            }
        }
    }
}
