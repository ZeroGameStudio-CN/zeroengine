namespace ZGS.DataToolkit.Editor
{
    public static class DataAssetCsvUtility
    {
        public static string Escape(string value)
        {
            value ??= string.Empty;
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }
    }
}
