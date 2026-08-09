using System;
using UnityEditor;

namespace ZeroEngine.TCE.Editor
{
    public static class TceGraphSerializedAccess
    {
        public const string DisplayNameProperty = "displayName";
        public const string CategoryProperty = "category";
        public const string DescriptionProperty = "description";
        public const string GraphSchemaVersionProperty = "graphSchemaVersion";
        public const string GraphProperty = "graph";
        public const string TriggersProperty = "graph.triggers";
        public const string ConditionsProperty = "graph.conditions";
        public const string EffectsProperty = "graph.effects";

        public static SerializedProperty GetLane(SerializedObject serializedObject, TceGraphLane lane)
        {
            return serializedObject.FindProperty(GetLanePropertyPath(lane));
        }

        public static string GetLanePropertyPath(TceGraphLane lane)
        {
            return lane switch
            {
                TceGraphLane.Trigger => TriggersProperty,
                TceGraphLane.Condition => ConditionsProperty,
                TceGraphLane.Effect => EffectsProperty,
                _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, null)
            };
        }
    }
}
