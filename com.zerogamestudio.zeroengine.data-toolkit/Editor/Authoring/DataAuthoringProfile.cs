using System;
using System.Collections.Generic;
using System.Linq;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataAuthoringProfile
    {
        public DataAuthoringProfile(
            string profileId,
            string title,
            IEnumerable<IDataAuthoringAssetAdapter> adapters,
            string description = null,
            IEnumerable<IDataAuthoringImportAdapter> importAdapters = null,
            IEnumerable<IDataAuthoringPreviewProvider> previewProviders = null,
            IEnumerable<IDataAuthoringDetailSection> detailSections = null,
            DataAuthoringWindowLabels labels = null,
            DataAuthoringWindowActions actions = null)
        {
            ProfileId = RequireText(profileId, nameof(profileId));
            Title = RequireText(title, nameof(title));
            Description = string.IsNullOrWhiteSpace(description) ? Title : description;
            Labels = labels ?? DataAuthoringWindowLabels.Default;
            Actions = actions ?? DataAuthoringWindowActions.Empty;
            Adapters = (adapters ?? Array.Empty<IDataAuthoringAssetAdapter>())
                .Where(adapter => adapter != null)
                .OrderBy(adapter => adapter.Order)
                .ThenBy(adapter => adapter.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            ImportAdapters = (importAdapters ?? Array.Empty<IDataAuthoringImportAdapter>())
                .Where(adapter => adapter != null)
                .OrderBy(adapter => adapter.AdapterId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            PreviewProviders = (previewProviders ?? Array.Empty<IDataAuthoringPreviewProvider>())
                .Where(provider => provider != null)
                .OrderBy(provider => provider.Order)
                .ThenBy(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            DetailSections = (detailSections ?? Array.Empty<IDataAuthoringDetailSection>())
                .Where(section => section != null)
                .OrderBy(section => section.Order)
                .ThenBy(section => section.SectionId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public string ProfileId { get; }
        public string Title { get; }
        public string Description { get; }
        public IReadOnlyList<IDataAuthoringAssetAdapter> Adapters { get; }
        public IReadOnlyList<IDataAuthoringImportAdapter> ImportAdapters { get; }
        public IReadOnlyList<IDataAuthoringPreviewProvider> PreviewProviders { get; }
        public IReadOnlyList<IDataAuthoringDetailSection> DetailSections { get; }
        public DataAuthoringWindowLabels Labels { get; }
        public DataAuthoringWindowActions Actions { get; }

        internal static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Value cannot be empty.", parameterName);
            }

            return value.Trim();
        }
    }
}
