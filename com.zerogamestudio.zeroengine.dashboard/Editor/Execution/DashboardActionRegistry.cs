using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using ZeroEngine.EditorUI;

namespace ZeroEngine.Editor.Dashboard
{
    internal sealed class DashboardActionRegistry
    {
        private readonly IReadOnlyDictionary<string, Type> _providerTypes;
        private readonly Dictionary<string, IEditorToolActionProvider> _providers =
            new Dictionary<string, IEditorToolActionProvider>(StringComparer.Ordinal);
        private readonly Dictionary<string, IEditorToolAction> _actions =
            new Dictionary<string, IEditorToolAction>(StringComparer.Ordinal);

        private DashboardActionRegistry(
            IReadOnlyDictionary<string, Type> providerTypes,
            IReadOnlyList<DashboardDiagnostic> diagnostics)
        {
            _providerTypes = providerTypes;
            Diagnostics = diagnostics;
        }

        internal IReadOnlyList<DashboardDiagnostic> Diagnostics { get; }

        internal static DashboardActionRegistry Build(DashboardCatalog catalog)
        {
            return Build(catalog, TypeCache.GetTypesDerivedFrom<IEditorToolActionProvider>());
        }

        internal static DashboardActionRegistry Build(DashboardCatalog catalog, IEnumerable<Type> providerTypes)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            var diagnostics = new List<DashboardDiagnostic>();
            var candidates = new List<KeyValuePair<string, Type>>();
            foreach (Type type in (providerTypes ?? Array.Empty<Type>())
                         .Where(type => type != null && !type.IsAbstract && !type.IsInterface)
                         .Where(type => typeof(IEditorToolActionProvider).IsAssignableFrom(type))
                         .OrderBy(type => type.AssemblyQualifiedName, StringComparer.Ordinal))
            {
                var attribute = (EditorToolActionProviderAttribute)Attribute.GetCustomAttribute(
                    type,
                    typeof(EditorToolActionProviderAttribute),
                    false);
                if (attribute == null || string.IsNullOrWhiteSpace(attribute.ProviderId))
                    continue;
                candidates.Add(new KeyValuePair<string, Type>(attribute.ProviderId.Trim(), type));
            }

            var providerMap = new Dictionary<string, Type>(StringComparer.Ordinal);
            var duplicateIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (IGrouping<string, KeyValuePair<string, Type>> group in candidates.GroupBy(item => item.Key, StringComparer.Ordinal))
            {
                KeyValuePair<string, Type>[] matches = group.ToArray();
                if (matches.Length == 1)
                    providerMap[group.Key] = matches[0].Value;
                else
                    duplicateIds.Add(group.Key);
            }

            foreach (DashboardEntry entry in catalog.Modules.SelectMany(module => module.Entries)
                         .Where(entry => entry.ExecutionKind == DashboardEntryExecutionKind.Provider))
            {
                if (duplicateIds.Contains(entry.ProviderId))
                {
                    diagnostics.Add(EntryError(
                        "action-provider-duplicate",
                        "Action provider ID '" + entry.ProviderId + "' is declared by multiple types; the action is unavailable.",
                        entry));
                }
                else if (!providerMap.ContainsKey(entry.ProviderId))
                {
                    diagnostics.Add(EntryError(
                        "action-provider-missing",
                        "Action provider '" + entry.ProviderId + "' is not available.",
                        entry));
                }
            }

            return new DashboardActionRegistry(providerMap, diagnostics);
        }

        internal bool TryGetState(
            DashboardEntry entry,
            out EditorToolActionState state,
            out DashboardDiagnostic diagnostic)
        {
            state = null;
            if (!TryGetAction(entry, out IEditorToolAction action, out diagnostic))
                return false;

            try
            {
                state = action.GetState();
                if (state != null)
                    return true;
                diagnostic = EntryError(
                    "action-state-missing",
                    "Action '" + entry.ProviderId + "/" + entry.ActionId + "' returned no state.",
                    entry);
                return false;
            }
            catch (Exception exception)
            {
                diagnostic = EntryError(
                    "action-state-failed",
                    exception.GetBaseException().Message,
                    entry);
                return false;
            }
        }

        internal bool TryExecute(
            DashboardEntry entry,
            EditorToolActionContext context,
            out EditorToolActionResult result,
            out DashboardDiagnostic diagnostic)
        {
            result = null;
            if (!TryGetAction(entry, out IEditorToolAction action, out diagnostic))
                return false;

            try
            {
                result = action.Execute(context);
                if (result != null)
                    return true;
                diagnostic = EntryError(
                    "action-result-missing",
                    "Action '" + entry.ProviderId + "/" + entry.ActionId + "' returned no result.",
                    entry);
                return false;
            }
            catch (Exception exception)
            {
                diagnostic = EntryError(
                    "action-execution-failed",
                    exception.GetBaseException().Message,
                    entry);
                return false;
            }
        }

        private bool TryGetAction(
            DashboardEntry entry,
            out IEditorToolAction action,
            out DashboardDiagnostic diagnostic)
        {
            action = null;
            diagnostic = null;
            if (entry == null || entry.ExecutionKind != DashboardEntryExecutionKind.Provider)
            {
                diagnostic = EntryError("action-binding-invalid", "The entry is not bound to a provider action.", entry);
                return false;
            }

            string bindingId = entry.ProviderId + "/" + entry.ActionId;
            if (_actions.TryGetValue(bindingId, out action))
                return true;
            if (!_providerTypes.TryGetValue(entry.ProviderId, out Type providerType))
            {
                diagnostic = EntryError(
                    "action-provider-missing",
                    "Action provider '" + entry.ProviderId + "' is not available.",
                    entry);
                return false;
            }

            if (!_providers.TryGetValue(entry.ProviderId, out IEditorToolActionProvider provider))
            {
                try
                {
                    provider = (IEditorToolActionProvider)Activator.CreateInstance(providerType);
                    _providers[entry.ProviderId] = provider;
                }
                catch (Exception exception)
                {
                    diagnostic = EntryError(
                        "action-provider-failed",
                        exception.GetBaseException().Message,
                        entry);
                    return false;
                }
            }

            try
            {
                action = provider.CreateAction(entry.ActionId);
                if (action == null)
                {
                    diagnostic = EntryError(
                        "action-not-created",
                        "Provider '" + entry.ProviderId + "' did not create action '" + entry.ActionId + "'.",
                        entry);
                    return false;
                }
                _actions[bindingId] = action;
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = EntryError(
                    "action-creation-failed",
                    exception.GetBaseException().Message,
                    entry);
                return false;
            }
        }

        private static DashboardDiagnostic EntryError(string code, string message, DashboardEntry entry)
        {
            return new DashboardDiagnostic(
                DashboardDiagnosticSeverity.Error,
                code,
                message,
                entry?.SourcePath ?? string.Empty,
                entry?.ModuleId,
                entry?.Id,
                entry?.MenuPath);
        }
    }
}
