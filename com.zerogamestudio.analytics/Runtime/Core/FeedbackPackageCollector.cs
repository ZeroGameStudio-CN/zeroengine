using System;
using System.Collections.Generic;

namespace ZGS.Analytics
{
    public sealed class FeedbackPackageCollector
    {
        internal sealed class Item
        {
            public string SourcePath;
            public string ArchiveRelativePath;
            public byte[] GeneratedBytes;
            public FeedbackAttachmentKind Kind;
            public FeedbackAttachmentPriority Priority;
            public int Order;
        }

        internal sealed class MetadataItem
        {
            public string Key;
            public string Value;
        }

        private readonly List<Item> _items = new List<Item>();
        private readonly List<MetadataItem> _metadata = new List<MetadataItem>();
        private int _order;

        internal IReadOnlyList<Item> Items => _items;
        internal IReadOnlyList<MetadataItem> Metadata => _metadata;

        public void AddFile(
            string sourcePath,
            string archiveRelativePath,
            FeedbackAttachmentKind kind,
            FeedbackAttachmentPriority priority)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                return;

            _items.Add(new Item
            {
                SourcePath = sourcePath,
                ArchiveRelativePath = string.IsNullOrWhiteSpace(archiveRelativePath)
                    ? System.IO.Path.GetFileName(sourcePath)
                    : archiveRelativePath,
                Kind = kind,
                Priority = priority,
                Order = _order++
            });
        }

        public void AddText(
            string archiveRelativePath,
            string content,
            FeedbackAttachmentPriority priority)
        {
            if (string.IsNullOrWhiteSpace(archiveRelativePath))
                return;

            _items.Add(new Item
            {
                ArchiveRelativePath = archiveRelativePath,
                GeneratedBytes = System.Text.Encoding.UTF8.GetBytes(content ?? string.Empty),
                Kind = FeedbackAttachmentKind.Generic,
                Priority = priority,
                Order = _order++
            });
        }

        public void AddMetadata(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            string safeKey = FeedbackTimelineSerializer.TruncateValue(key.Trim());
            string safeValue = FeedbackTimelineSerializer.IsSensitiveKey(safeKey)
                ? "<redacted>"
                : FeedbackTimelineSerializer.TruncateValue(value == null ? "<null>" : value.ToString());

            MetadataItem existing = _metadata.Find(item =>
                string.Equals(item.Key, safeKey, StringComparison.Ordinal));
            if (existing != null)
            {
                existing.Value = safeValue;
                return;
            }

            _metadata.Add(new MetadataItem { Key = safeKey, Value = safeValue });
        }
    }
}
