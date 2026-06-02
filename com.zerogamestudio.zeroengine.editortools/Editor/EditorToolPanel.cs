using System;

namespace ZeroEngine.EditorTools
{
    public sealed class EditorToolPanel : IEditorToolPanel
    {
        private readonly Action _draw;

        public EditorToolPanel(string id, string displayName, string group, int order, Action draw, string tooltip = null, string groupDisplayName = null)
        {
            Id = EditorToolCommand.RequireText(id, nameof(id));
            DisplayName = EditorToolCommand.RequireText(displayName, nameof(displayName));
            Tooltip = string.IsNullOrWhiteSpace(tooltip) ? DisplayName : tooltip;
            Group = string.IsNullOrWhiteSpace(group) ? "General" : group;
            GroupDisplayName = string.IsNullOrWhiteSpace(groupDisplayName) ? Group : groupDisplayName;
            Order = order;
            _draw = draw ?? throw new ArgumentNullException(nameof(draw));
        }

        public string Id { get; }

        public string DisplayName { get; }

        public string Tooltip { get; }

        public string Group { get; }

        public string GroupDisplayName { get; }

        public int Order { get; }

        public void Draw()
        {
            _draw.Invoke();
        }
    }
}
