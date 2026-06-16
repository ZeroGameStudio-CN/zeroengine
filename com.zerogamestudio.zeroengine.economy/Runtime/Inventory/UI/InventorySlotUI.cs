using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZeroEngine.Inventory.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public class InventorySlotUI : MonoBehaviour,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IDropHandler,
        IPointerClickHandler,
        IPointerEnterHandler,
        IPointerExitHandler
    {
        public Image IconImage;
        public Text AmountText;
        public Button ClickButton;
        public Image RarityBorder;
        public GameObject SelectedHighlight;
        
        private CanvasGroup _canvasGroup;
        private InventorySlot _slot;
        private int _slotIndex;
        private static InventoryDragData _currentDrag;

        public event Action<int> OnSlotClicked;
        public event Action<int> OnSlotHoverEnter;
        public event Action<int> OnSlotHoverExit;
        public event Action<int, int> OnSlotDropRequested;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            if (ClickButton != null)
            {
                ClickButton.onClick.AddListener(HandleButtonClicked);
            }
        }

        private void OnDestroy()
        {
            if (ClickButton != null)
            {
                ClickButton.onClick.RemoveListener(HandleButtonClicked);
            }
        }

        public void Setup(int index)
        {
            _slotIndex = index;
        }

        public void Bind(InventorySlot slot)
        {
            Refresh(slot);
        }

        public void Refresh(InventorySlot slot)
        {
            _slot = slot;
            if (_slot == null || _slot.IsEmpty)
            {
                if (IconImage != null)
                {
                    IconImage.gameObject.SetActive(false);
                    IconImage.sprite = null;
                }
                if (AmountText != null)
                {
                    AmountText.text = "";
                }
                if (RarityBorder != null)
                {
                    RarityBorder.enabled = false;
                }
                if (ClickButton != null)
                {
                    ClickButton.interactable = false;
                }
            }
            else
            {
                if (IconImage != null)
                {
                    IconImage.gameObject.SetActive(true);
                    IconImage.sprite = _slot.ItemData != null ? _slot.ItemData.Icon : null;
                }
                
                if (AmountText != null)
                {
                    AmountText.text = _slot.Amount > 1 ? _slot.Amount.ToString() : "";
                }
                if (RarityBorder != null && _slot.ItemData != null)
                {
                    RarityBorder.enabled = true;
                    RarityBorder.color = _slot.ItemData.GetRarityColor();
                }
                if (ClickButton != null)
                {
                    ClickButton.interactable = true;
                }
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_slot == null || _slot.IsEmpty) return;

            _currentDrag = new InventoryDragData
            {
                SourceSlotIndex = _slotIndex,
                SourceSlot = _slot,
                SourceUI = this
            };

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0.5f;
                _canvasGroup.blocksRaycasts = false;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = true;
            }
            _currentDrag = null;
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (_currentDrag == null || _currentDrag.SourceSlotIndex == _slotIndex)
            {
                return;
            }

            OnSlotDropRequested?.Invoke(_currentDrag.SourceSlotIndex, _slotIndex);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            HandleButtonClicked();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            OnSlotHoverEnter?.Invoke(_slotIndex);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            OnSlotHoverExit?.Invoke(_slotIndex);
        }

        public void SetSelected(bool selected)
        {
            if (SelectedHighlight != null)
            {
                SelectedHighlight.SetActive(selected);
            }
        }

        public void ConfigureForTests(Image iconImage, Text amountText, Image rarityBorder, GameObject selectedHighlight)
        {
            IconImage = iconImage;
            AmountText = amountText;
            RarityBorder = rarityBorder;
            SelectedHighlight = selectedHighlight;
            _canvasGroup = GetComponent<CanvasGroup>();
        }
        
        private void HandleButtonClicked()
        {
            if (_slot == null || _slot.IsEmpty) return;
            OnSlotClicked?.Invoke(_slotIndex);
        }
    }
}
