using System;

namespace ZeroEngine.AbilitySystem.Editor
{
    public sealed class AbilityEditorOptions
    {
        public AbilityEditorLabels Labels { get; set; } = AbilityEditorLabels.English();
        public bool DrawSummary { get; set; } = true;
        public bool DrawValidation { get; set; } = true;
        public bool DrawDebugRawAbility { get; set; }
        public bool AllowDuplicateComponentTypes { get; set; }
        public bool CompactComponentRows { get; set; }
        public bool CollapseAddSectionsByDefault { get; set; }
        public bool ShowComponentActionsInMenu { get; set; }
        public Func<Type, bool> ComponentFilter { get; set; }

        public bool AllowsComponent(Type type)
        {
            return type != null && (ComponentFilter == null || ComponentFilter(type));
        }

        public static AbilityEditorOptions Default()
        {
            return new AbilityEditorOptions();
        }
    }
}
