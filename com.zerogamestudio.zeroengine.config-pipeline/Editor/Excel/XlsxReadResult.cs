using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    public enum ConfigValueSourceKind
    {
        Schema,
        Preset,
        Instance
    }

    public sealed class XlsxSourceMapEntry
    {
        public XlsxSourceMapEntry(
            string jsonPath,
            string workbook,
            string sheet,
            int row,
            int column)
            : this(
                jsonPath,
                workbook,
                sheet,
                row,
                column,
                ConfigValueSourceKind.Instance,
                jsonPath,
                null)
        {
        }

        public XlsxSourceMapEntry(
            string jsonPath,
            string workbook,
            string sheet,
            int row,
            int column,
            ConfigValueSourceKind sourceKind,
            string sourceJsonPath,
            string schemaPath)
        {
            JsonPath = jsonPath;
            Workbook = workbook;
            Sheet = sheet;
            Row = row;
            Column = column;
            SourceKind = sourceKind;
            SourceJsonPath = sourceJsonPath;
            SchemaPath = schemaPath;
        }

        public string JsonPath { get; }

        public string Workbook { get; }

        public string Sheet { get; }

        public int Row { get; }

        public int Column { get; }

        public ConfigValueSourceKind SourceKind { get; }

        public string SourceJsonPath { get; }

        public string SchemaPath { get; }
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
