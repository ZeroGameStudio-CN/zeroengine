using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;

namespace ZeroEngine.TCE.Editor
{
    public static class TceComponentCatalogWriter
    {
        public const string CatalogPath = "Packages/com.zerogamestudio.zeroengine.tce/Documentation~/component-catalog.md";

        public static string WriteMarkdown(IReadOnlyList<TceComponentCatalogEntry> entries)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# ZeroEngine TCE Component Catalog");
            builder.AppendLine();

            foreach (IGrouping<TceComponentDocCategory, TceComponentCatalogEntry> group in entries.GroupBy(entry => entry.Category).OrderBy(group => group.Key))
            {
                builder.AppendLine($"## {group.Key}");
                builder.AppendLine();

                foreach (TceComponentCatalogEntry entry in group)
                {
                    builder.AppendLine($"### {entry.DisplayName}");
                    builder.AppendLine();
                    builder.AppendLine($"- Data type: `{entry.DataTypeFullName}`");
                    builder.AppendLine($"- Runtime type: `{entry.RuntimeTypeFullName}`");
                    builder.AppendLine($"- Summary: {entry.ShortDescription}");
                    builder.AppendLine($"- Description: {entry.ExpandedDescription}");

                    if (entry.Fields.Count == 0)
                    {
                        builder.AppendLine("- Fields: none");
                    }
                    else
                    {
                        builder.AppendLine("- Fields:");
                        foreach (TceComponentCatalogField field in entry.Fields)
                            builder.AppendLine($"  - `{field.Name}` (`{field.TypeName}`, default `{field.DefaultValue}`)");
                    }

                    builder.AppendLine();
                }
            }

            return builder.ToString().Replace("\r\n", "\n");
        }

        [MenuItem("ZGS/ZeroEngine/TCE/Regenerate Component Catalog")]
        public static void RegenerateComponentCatalog()
        {
            string markdown = WriteMarkdown(TceComponentCatalogBuilder.Build());
            string directory = Path.GetDirectoryName(CatalogPath);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(CatalogPath, markdown, Encoding.UTF8);
            AssetDatabase.Refresh();
        }
    }
}
