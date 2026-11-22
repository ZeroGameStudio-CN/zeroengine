using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ZeroEngine.UI.Pad
{
    /// <summary>Named slots and controls for a shared PAD prefab.</summary>
    [DisallowMultipleComponent]
    public sealed class PadPanelView : MonoBehaviour
    {
        [SerializeField] private Graphic _backdrop;
        [SerializeField] private TMP_Text _title;
        [SerializeField] private Button _closeButton;
        [SerializeField] private RectTransform _content;
        [SerializeField] private RectTransform _navigation;
        [SerializeField] private RectTransform _actions;

        public Graphic Backdrop => _backdrop;
        public Button CloseButton => _closeButton;
        public RectTransform Content => _content;
        public RectTransform Navigation => _navigation;
        public RectTransform Actions => _actions;

        public bool HasRequiredReferences => Owns(_title) && Owns(_closeButton) && Owns(_content) &&
            (!_backdrop || Owns(_backdrop)) && (!_navigation || Owns(_navigation)) && (!_actions || Owns(_actions));

        public void ConfigureForRuntime(TMP_Text title, Button closeButton, RectTransform content,
            Graphic backdrop = null, RectTransform navigation = null, RectTransform actions = null)
        {
            if (!Owns(title) || !Owns(closeButton) || !Owns(content) ||
                (backdrop && !Owns(backdrop)) || (navigation && !Owns(navigation)) || (actions && !Owns(actions)))
            {
                throw new ArgumentException("PAD title, close button and layout slots must belong to the panel.");
            }

            _title = title;
            _closeButton = closeButton;
            _content = content;
            _backdrop = backdrop;
            _navigation = navigation;
            _actions = actions;
        }

        public void SetTitle(string title)
        {
            if (!HasRequiredReferences)
                throw new InvalidOperationException("Configure the PAD panel before setting its title.");

            _title.text = title ?? string.Empty;
        }

        private bool Owns(Component part) => part && part.transform.IsChildOf(transform);
    }
}
