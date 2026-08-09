using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    public sealed class XlsxSourceMapEntry
    {
        public XlsxSourceMapEntry(
            string jsonPath,
            string workbook,
            string sheet,
            int row,
            int column)
        {
            JsonPath = jsonPath;
            Workbook = workbook;
            Sheet = sheet;
            Row = row;
            Column = column;
        }

        public string JsonPath { get; }

        public string Workbook { get; }

        public string Sheet { get; }

        public int Row { get; }

        public int Column { get; }
    }

    public sealed class XlsxReadResult
    {
        private readonly ReadOnlyCollection<XlsxSourceMapEntry> sourceMap;

        public XlsxReadResult(
            ConfigDocument document,
            string workbookBaseHash,
            IEnumerable<XlsxSourceMapEntry> sourceMap)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            WorkbookBaseHash = workbookBaseHash;
            this.sourceMap = new List<XlsxSourceMapEntry>(
                sourceMap ?? Array.Empty<XlsxSourceMapEntry>()).AsReadOnly();
        }

        public ConfigDocument Document { get; }

        public string WorkbookBaseHash { get; }

        public IReadOnlyList<XlsxSourceMapEntry> SourceMap => sourceMap;
    }
}
