namespace ZeroGameStudio.ConfigPipeline.Editor
{
    public sealed class XlsxWorkbookLimits
    {
        public const long DefaultCompressedBytes = 32L * 1024L * 1024L;
        public const long DefaultExpandedBytes = 256L * 1024L * 1024L;
        public const int DefaultWorksheetCount = 128;
        public const int DefaultRowsPerSheet = 100000;
        public const int DefaultColumnsPerSheet = 512;

        public XlsxWorkbookLimits(
            long maximumCompressedBytes = DefaultCompressedBytes,
            long maximumExpandedBytes = DefaultExpandedBytes,
            int maximumWorksheetCount = DefaultWorksheetCount,
            int maximumRowsPerSheet = DefaultRowsPerSheet,
            int maximumColumnsPerSheet = DefaultColumnsPerSheet)
        {
            if (maximumCompressedBytes <= 0 || maximumExpandedBytes <= 0 ||
                maximumWorksheetCount <= 0 || maximumRowsPerSheet < 0 ||
                maximumColumnsPerSheet <= 0)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(maximumCompressedBytes),
                    "Workbook limits must be positive; row count may be zero.");
            }

            MaximumCompressedBytes = maximumCompressedBytes;
            MaximumExpandedBytes = maximumExpandedBytes;
            MaximumWorksheetCount = maximumWorksheetCount;
            MaximumRowsPerSheet = maximumRowsPerSheet;
            MaximumColumnsPerSheet = maximumColumnsPerSheet;
        }

        public long MaximumCompressedBytes { get; }

        public long MaximumExpandedBytes { get; }

        public int MaximumWorksheetCount { get; }

        public int MaximumRowsPerSheet { get; }

        public int MaximumColumnsPerSheet { get; }
    }
}
