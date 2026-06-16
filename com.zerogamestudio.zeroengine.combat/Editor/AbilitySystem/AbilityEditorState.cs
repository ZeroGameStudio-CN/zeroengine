using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.AbilitySystem.Editor
{
    public sealed class AbilityEditorState
    {
        private static readonly Dictionary<string, AbilityEditorState> States = new();

        public string TriggerSearch = string.Empty;
        public string ConditionSearch = string.Empty;
        public string EffectSearch = string.Empty;
        public Vector2 TriggerScroll;
        public Vector2 ConditionScroll;
        public Vector2 EffectScroll;
        public bool ShowDebugRawAbility;
        public readonly HashSet<string> ExpandedDocs = new();
        public readonly Dictionary<string, bool> Foldouts = new();

        public static AbilityEditorState Get(int targetInstanceId, string propertyPath)
        {
            var key = $"{targetInstanceId}:{propertyPath}";
            if (!States.TryGetValue(key, out var state))
            {
                state = new AbilityEditorState();
                States[key] = state;
            }

            return state;
        }
    }
}
