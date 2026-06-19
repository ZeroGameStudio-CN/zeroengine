using System;
using System.Globalization;
using UnityEditor;

namespace ZGS.DataToolkit.Editor
{
    internal static class DataAssetSerializedPropertyUtility
    {
        public static bool TryReadValue(SerializedProperty property, out string value)
        {
            value = string.Empty;
            if (property == null)
            {
                return false;
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.String:
                    value = property.stringValue ?? string.Empty;
                    return true;
                case SerializedPropertyType.Integer:
                    value = property.intValue.ToString(CultureInfo.InvariantCulture);
                    return true;
                case SerializedPropertyType.Boolean:
                    value = property.boolValue ? "true" : "false";
                    return true;
                case SerializedPropertyType.Float:
                    value = property.floatValue.ToString(CultureInfo.InvariantCulture);
                    return true;
                case SerializedPropertyType.Enum:
                    value = property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length
                        ? property.enumDisplayNames[property.enumValueIndex]
                        : string.Empty;
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryValidateValue(SerializedProperty property, string value, out string error)
        {
            error = string.Empty;
            if (property == null)
            {
                error = "Field is missing.";
                return false;
            }

            value ??= string.Empty;
            switch (property.propertyType)
            {
                case SerializedPropertyType.String:
                    return true;
                case SerializedPropertyType.Integer:
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    {
                        return true;
                    }

                    error = $"'{value}' is not a valid integer.";
                    return false;
                case SerializedPropertyType.Boolean:
                    if (bool.TryParse(value, out _) || value == "1" || value == "0")
                    {
                        return true;
                    }

                    error = $"'{value}' is not a valid boolean.";
                    return false;
                case SerializedPropertyType.Float:
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    {
                        return true;
                    }

                    error = $"'{value}' is not a valid number.";
                    return false;
                case SerializedPropertyType.Enum:
                    return TryValidateEnumValue(property, value, out error);
                default:
                    error = $"Field type '{property.propertyType}' is not supported by generic field import.";
                    return false;
            }
        }

        public static bool TrySetValue(SerializedProperty property, string value, out string error)
        {
            error = string.Empty;
            if (property == null)
            {
                error = "Field is missing.";
                return false;
            }

            value ??= string.Empty;
            switch (property.propertyType)
            {
                case SerializedPropertyType.String:
                    property.stringValue = value;
                    return true;
                case SerializedPropertyType.Integer:
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                    {
                        property.intValue = intValue;
                        return true;
                    }

                    error = $"'{value}' is not a valid integer.";
                    return false;
                case SerializedPropertyType.Boolean:
                    if (bool.TryParse(value, out var boolValue))
                    {
                        property.boolValue = boolValue;
                        return true;
                    }

                    if (value == "1" || value == "0")
                    {
                        property.boolValue = value == "1";
                        return true;
                    }

                    error = $"'{value}' is not a valid boolean.";
                    return false;
                case SerializedPropertyType.Float:
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
                    {
                        property.floatValue = floatValue;
                        return true;
                    }

                    error = $"'{value}' is not a valid number.";
                    return false;
                case SerializedPropertyType.Enum:
                    return TrySetEnumValue(property, value, out error);
                default:
                    error = $"Field type '{property.propertyType}' is not supported by generic field import.";
                    return false;
            }
        }

        private static bool TrySetEnumValue(SerializedProperty property, string value, out string error)
        {
            error = string.Empty;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var enumIndex) &&
                enumIndex >= 0 &&
                enumIndex < property.enumDisplayNames.Length)
            {
                property.enumValueIndex = enumIndex;
                return true;
            }

            for (var index = 0; index < property.enumDisplayNames.Length; index++)
            {
                if (string.Equals(property.enumDisplayNames[index], value, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(property.enumNames[index], value, StringComparison.OrdinalIgnoreCase))
                {
                    property.enumValueIndex = index;
                    return true;
                }
            }

            error = $"'{value}' is not a valid enum value.";
            return false;
        }

        private static bool TryValidateEnumValue(SerializedProperty property, string value, out string error)
        {
            error = string.Empty;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var enumIndex) &&
                enumIndex >= 0 &&
                enumIndex < property.enumDisplayNames.Length)
            {
                return true;
            }

            for (var index = 0; index < property.enumDisplayNames.Length; index++)
            {
                if (string.Equals(property.enumDisplayNames[index], value, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(property.enumNames[index], value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            error = $"'{value}' is not a valid enum value.";
            return false;
        }
    }
}
