using System;
using UnityEngine;

namespace ZeroEngine.UI.Combat
{
    [Serializable]
    public readonly struct CombatFactionStyle
    {
        public readonly string Label;
        public readonly Color Color;

        public CombatFactionStyle(string label, Color color)
        {
            Label = label;
            Color = color;
        }
    }
}
