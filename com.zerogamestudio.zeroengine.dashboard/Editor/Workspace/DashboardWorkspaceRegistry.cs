using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using ZeroEngine.EditorUI;

namespace ZeroEngine.Editor.Dashboard
{
    internal sealed class DashboardWorkspaceRegistry
    {
        private readonly IReadOnlyDictionary<string, Type> _providers;

        private DashboardWorkspaceRegistry(
            IReadOnlyDictionary<string, Type> providers,
            IReadOnlyList<DashboardDiagnostic> diagnostics)
        {
            _providers = providers;
            Diagnostics = diagnostics;
        }

        internal IReadOnlyList<DashboardDiagnostic> Diagnostics { get; }

        internal static DashboardWorkspaceRegistry Build(DashboardCatalog catalog)
        {
            return Build(catalog, TypeCache.GetTypesDerivedFrom<IEditorWorkspacePanelProvider>());
        }

        internal static DashboardWorkspaceRegistry Build(
            DashboardCatalog catalog,
            IEnumerable<Type> providerTypes)
        {
            var diagnostics = new List<DashboardDiagnostic>();
            var candidates = new List<KeyValuePair<string, Type>>();
            foreach (Type type in (providerTypes ?? Array.Empty<Type>())
                         .Where(type => type != null && !type.IsAbstract && !type.IsInterface)
                         .Where(type => typeof(IEditorWorkspacePanelProvider).IsAssignableFrom(type))
                         .OrderBy(type => type.AssemblyQualifiedName, StringComparer.Ordinal))
            {
                var attribute = (EditorWorkspacePanelProviderAttribute)Attribute.GetCustomAttribute(
                    type,
                    typeof(EditorWorkspacePanelProviderAttribute),
                    false);
                if (attribute == null || string.IsNullOrWhiteSpace(attribute.ProviderId))
                    continue;
                candidates.Add(new KeyValuePair<string, Type>(attribute.ProviderId.Trim(), type));
            }

            var providers = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (IGrouping<string, KeyValuePair<string, Type>> group in candidates.GroupBy(item => item.Key, StringComparer.Ordinal))
            {
                KeyValuePair<string, Type>[] matches = group.ToArray();
                if (matches.Length == 1)
                {
                    providers[group.Key] = matches[0].Value;
                    continue;
                }

                foreach (DashboardPanel panel in catalog.Modules.SelectMany(module => module.Panels)
                             .Where(panel => string.Equals(panel.ProviderId, group.Key, StringComparison.Ordinal)))
                {
                    diagnostics.Add(PanelError(
                        "workspace-provider-duplicate",
                        "Workspace provider ID '" + group.Key + "' is declared by multiple types; the panel is hidden.",
                        panel));
                }
            }

            foreach (DashboardPanel panel in catalog.Modules.SelectMany(module => module.Panels))
            {
                if (providers.ContainsKey(panel.ProviderId))
                    continue;
                if (diagnostics.Any(item => item.EntryId == panel.Id && item.ModuleId == panel.ModuleId))
                    continue;
                diagnostics.Add(PanelError(
                    "workspace-provider-missing",
                    "Workspace provider '" + panel.ProviderId + "' is not available; the panel is hidden.",
                    panel));
            }

            return new DashboardWorkspaceRegistry(providers, diagnostics);
        }

        internal bool IsAvailable(DashboardPanel panel)
        {
            return panel != null && _providers.ContainsKey(panel.ProviderId);
        }

        internal bool TryCreate(
            DashboardPanel descriptor,
            out IEditorWorkspacePanel panel,
            out DashboardDiagnostic diagnostic)
        {
            panel = null;
            diagnostic = null;
            if (descriptor == null || !_providers.TryGetValue(descriptor.ProviderId, out Type providerType))
            {
                diagnostic = PanelError(
                    "workspace-provider-missing",
                    "Workspace provider is unavailable.",
                    descriptor);
                return false;
            }

            try
            {
                var provider = (IEditorWorkspacePanelProvider)Activator.CreateInstance(providerType);
                panel = provider.CreatePanel(descriptor.Id);
                if (panel != null)
                    return true;
                diagnostic = PanelError(
                    "workspace-panel-not-created",
                    "Provider '" + descriptor.ProviderId + "' did not create panel '" + descriptor.Id + "'.",
                    descriptor);
                return false;
            }
            catch (Exception exception)
            {
                diagnostic = PanelError(
                    "workspace-provider-failed",
                    exception.GetBaseException().Message,
                    descriptor);
                return false;
            }
        }

        private static DashboardDiagnostic PanelError(string code, string message, DashboardPanel panel)
        {
            return new DashboardDiagnostic(
                DashboardDiagnosticSeverity.Error,
                code,
                message,
                panel?.SourcePath ?? string.Empty,
                panel?.ModuleId,
                panel?.Id);
        }
    }
}
