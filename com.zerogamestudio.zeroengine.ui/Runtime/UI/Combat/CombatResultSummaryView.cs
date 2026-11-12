using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ZeroEngine.UI.Combat
{
    public sealed class CombatResultSummaryView : MonoBehaviour
    {
        [Header("Header")]
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _subtitleText;

        [Header("Sections")]
        [SerializeField] private RectTransform _summaryContainer;
        [SerializeField] private RectTransform _rewardContainer;
        [SerializeField] private RectTransform _growthContainer;
        [SerializeField] private RectTransform _rewardSectionRoot;
        [SerializeField] private RectTransform _growthSectionRoot;
        [SerializeField] private RectTransform _tagContainer;
        [SerializeField] private TextMeshProUGUI _emptyRewardText;

        [Header("Prefabs")]
        [SerializeField] private GameObject _linePrefab;
        [SerializeField] private GameObject _tagPrefab;

        [Header("Actions")]
        [SerializeField] private Button _confirmButton;

        public event Action OnConfirm;

        public bool IsVisible => gameObject.activeInHierarchy;

        private void Awake()
        {
            WireConfirmButton();
        }

        private void OnDestroy()
        {
            if (_confirmButton != null)
            {
                _confirmButton.onClick.RemoveListener(Confirm);
            }
        }

        public void ConfigureForRuntime(
            TextMeshProUGUI titleText,
            TextMeshProUGUI subtitleText,
            RectTransform summaryContainer,
            RectTransform rewardContainer,
            RectTransform growthContainer,
            RectTransform tagContainer,
            GameObject linePrefab,
            GameObject tagPrefab,
            TextMeshProUGUI emptyRewardText,
            Button confirmButton,
            RectTransform rewardSectionRoot = null,
            RectTransform growthSectionRoot = null)
        {
            _titleText = titleText;
            _subtitleText = subtitleText;
            _summaryContainer = summaryContainer;
            _rewardContainer = rewardContainer;
            _growthContainer = growthContainer;
            _rewardSectionRoot = rewardSectionRoot;
            _growthSectionRoot = growthSectionRoot;
            _tagContainer = tagContainer;
            _linePrefab = linePrefab;
            _tagPrefab = tagPrefab;
            _emptyRewardText = emptyRewardText;
            _confirmButton = confirmButton;
            WireConfirmButton();
        }

        public void Show(CombatResultReport report)
        {
            if (report == null)
            {
                report = new CombatResultReport
                {
                    Result = CombatResultType.Defeat,
                    Title = string.Empty,
                    Subtitle = string.Empty
                };
            }

            gameObject.SetActive(true);

            SetText(_titleText, report.Title);
            SetText(_subtitleText, report.Subtitle);

            RenderLines(_summaryContainer, report.Summary);
            RenderTags(_tagContainer, report.Tags);
            RenderLines(_rewardContainer, report.ShouldShowRewardArea ? report.Rewards : null);
            RenderLines(_growthContainer, report.ShouldShowRewardArea ? report.Growth : null);

            SetSectionVisible(_rewardSectionRoot, _rewardContainer, report.ShouldShowRewardArea);
            SetSectionVisible(_growthSectionRoot, _growthContainer, report.ShouldShowRewardArea && report.Growth.Count > 0);

            if (_emptyRewardText != null)
            {
                _emptyRewardText.text = report.EmptyRewardText;
                _emptyRewardText.gameObject.SetActive(report.ShouldShowRewardArea && !report.HasRewards);
            }

            if (_confirmButton != null)
            {
                _confirmButton.gameObject.SetActive(true);
            }
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            ClearContainer(_summaryContainer);
            ClearContainer(_rewardContainer);
            ClearContainer(_growthContainer);
            ClearContainer(_tagContainer);
        }

        private void RenderLines(RectTransform container, System.Collections.Generic.IReadOnlyList<CombatResultLine> lines)
        {
            ClearContainer(container);
            if (container == null || lines == null || _linePrefab == null)
            {
                return;
            }

            foreach (var line in lines)
            {
                var row = Instantiate(_linePrefab, container);
                row.SetActive(true);
                var texts = row.GetComponentsInChildren<TextMeshProUGUI>(true);
                if (texts.Length > 0)
                {
                    texts[0].text = line.Label;
                }

                if (texts.Length > 1)
                {
                    texts[1].text = line.Value;
                }
            }
        }

        private void RenderTags(RectTransform container, System.Collections.Generic.IReadOnlyList<string> tags)
        {
            ClearContainer(container);
            if (container == null || tags == null || _tagPrefab == null)
            {
                return;
            }

            foreach (var tag in tags)
            {
                var item = Instantiate(_tagPrefab, container);
                item.SetActive(true);
                var text = item.GetComponentInChildren<TextMeshProUGUI>(true);
                if (text != null)
                {
                    text.text = tag;
                }
            }
        }

        private void WireConfirmButton()
        {
            if (_confirmButton == null)
            {
                return;
            }

            _confirmButton.onClick.RemoveListener(Confirm);
            _confirmButton.onClick.AddListener(Confirm);
        }

        private void Confirm()
        {
            OnConfirm?.Invoke();
        }

        private static void SetText(TextMeshProUGUI text, string value)
        {
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }

        private static void SetSectionVisible(RectTransform sectionRoot, RectTransform container, bool visible)
        {
            if (sectionRoot != null)
            {
                sectionRoot.gameObject.SetActive(visible);
            }

            if (container != null)
            {
                container.gameObject.SetActive(visible);
            }
        }

        private static void ClearContainer(RectTransform container)
        {
            if (container == null)
            {
                return;
            }

            for (int i = container.childCount - 1; i >= 0; i--)
            {
                var child = container.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }
    }
}
