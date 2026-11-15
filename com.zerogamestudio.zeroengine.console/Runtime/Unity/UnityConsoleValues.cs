using System;
using System.Globalization;
using UnityEngine;

namespace ZeroEngine.Console.Unity
{
    /// <summary>Statically bound Unity value conversions, independent of UI and vendor SDKs.</summary>
    public static class UnityConsoleValues
    {
        public static bool TryConvert(ConsoleToken token, Type target, out object value, out string error)
        {
            value = null;
            error = null;
            bool color = target == typeof(Color) || target == typeof(Color32);
            bool integer = target == typeof(Vector2Int) || target == typeof(Vector3Int);
            int count = target == typeof(Vector2) || target == typeof(Vector2Int) ? 2 :
                target == typeof(Vector3) || target == typeof(Vector3Int) ? 3 :
                target == typeof(Vector4) || target == typeof(Quaternion) || color ? 4 : 0;
            if (count == 0) return false;
            string text = (token.Text ?? string.Empty).Trim();
            if (color)
            {
                string html = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? "#" + text.Substring(2) : text;
                if (ColorUtility.TryParseHtmlString(html, out var parsed))
                {
                    value = target == typeof(Color32) ? (object)(Color32)parsed : parsed;
                    return true;
                }
            }
            if (text.Length >= 2 && (text[0] == '(' && text[text.Length - 1] == ')' ||
                text[0] == '[' && text[text.Length - 1] == ']')) text = text.Substring(1, text.Length - 2);
            string[] parts = text.Split(',');
            error = color ? "Expected a named/HTML color or R,G,B[,A] components in [0,1]." :
                "Expected " + count + " comma-separated " + (integer ? "integer" : "finite numeric") + " components for " + target.Name + ".";
            if (parts.Length != count && !(color && parts.Length == 3)) return false;
            if (integer)
            {
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
                    !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)) return false;
                if (count == 2) value = new Vector2Int(x, y);
                else
                {
                    if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z)) return false;
                    value = new Vector3Int(x, y, z);
                }
            }
            else
            {
                var numbers = new Vector4(0, 0, 0, color ? 1 : 0);
                for (int i = 0; i < parts.Length; i++)
                {
                    if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float number) ||
                        float.IsNaN(number) || float.IsInfinity(number) || color && (number < 0 || number > 1)) return false;
                    numbers[i] = number;
                }
                if (color)
                {
                    var parsed = new Color(numbers[0], numbers[1], numbers[2], numbers[3]);
                    value = target == typeof(Color32) ? (object)(Color32)parsed : parsed;
                }
                else if (count == 2) value = new Vector2(numbers[0], numbers[1]);
                else if (count == 3) value = new Vector3(numbers[0], numbers[1], numbers[2]);
                else if (target == typeof(Quaternion)) value = new Quaternion(numbers[0], numbers[1], numbers[2], numbers[3]);
                else value = new Vector4(numbers[0], numbers[1], numbers[2], numbers[3]);
            }
            error = null;
            return true;
        }
    }
}
