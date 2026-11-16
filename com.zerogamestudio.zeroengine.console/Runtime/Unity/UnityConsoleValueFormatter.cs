using System.Globalization;
using UnityEngine;

namespace ZeroEngine.Console.Unity
{
    public sealed class UnityConsoleValueFormatter : ConsoleValueFormatter
    {
        protected override bool TryFormatAtom(object value, out string text)
        {
            var culture = CultureInfo.InvariantCulture;
            switch (value)
            {
                case Vector2 item: text = item.ToString("G9", culture); return true;
                case Vector3 item: text = item.ToString("G9", culture); return true;
                case Vector4 item: text = item.ToString("G9", culture); return true;
                case Vector2Int item: text = item.ToString(null, culture); return true;
                case Vector3Int item: text = item.ToString(null, culture); return true;
                case Quaternion item: text = item.ToString("G9", culture); return true;
                case Color item: text = item.ToString("G9", culture); return true;
                case Color32 item: text = "RGBA(" + item.r + ", " + item.g + ", " + item.b + ", " + item.a + ")"; return true;
                default: return base.TryFormatAtom(value, out text);
            }
        }
    }
}
