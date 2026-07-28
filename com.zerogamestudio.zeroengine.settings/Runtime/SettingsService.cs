using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.PlayerSettings
{
    public readonly struct SettingApplyResult
    {
        private SettingApplyResult(bool success, string error)
        {
            Success = success;
            Error = error;
        }

        public bool Success { get; }
        public string Error { get; }
        public static SettingApplyResult Applied() => new(true, null);
        public static SettingApplyResult Failed(string error) => new(false, error);
    }

    public interface ISettingApplier
    {
        IReadOnlyCollection<SettingId> SettingIds { get; }
        Task<SettingApplyResult> ApplyAsync(SettingsSnapshot snapshot, CancellationToken cancellationToken);
    }

    public readonly struct SettingsInitializeResult
    {
        public SettingsInitializeResult(bool success, SettingsStoreSource source, IReadOnlyList<string> errors)
        {
            Success = success;
            Source = source;
            Errors = errors;
        }

        public bool Success { get; }
        public SettingsStoreSource Source { get; }
        public IReadOnlyList<string> Errors { get; }
    }

    public readonly struct SettingsCommitResult
    {
        private SettingsCommitResult(bool success, string stage, string error)
        {
            Success = success;
            Stage = stage;
            Error = error;
        }

        public bool Success { get; }
        public string Stage { get; }
        public string Error { get; }
        public static SettingsCommitResult Committed() => new(true, null, null);
        public static SettingsCommitResult Failed(string stage, string error) => new(false, stage, error);
    }

    public readonly struct SettingOperationResult
    {
        private SettingOperationResult(bool success, string error)
        {
            Success = success;
            Error = error;
        }

        public bool Success { get; }
        public string Error { get; }
        public static SettingOperationResult Applied() => new(true, null);
        public static SettingOperationResult Failed(string error) => new(false, error);
    }

    public readonly struct SettingsChangedEvent
    {
        public SettingsChangedEvent(SettingsSnapshot snapshot, bool committed)
        {
            Snapshot = snapshot;
            IsCommitted = committed;
        }

        public SettingsSnapshot Snapshot { get; }
        public bool IsCommitted { get; }
    }

    public interface ISettingsService
    {
        bool IsReady { get; }
        SettingsSnapshot Committed { get; }
        Task<SettingsInitializeResult> InitializeAsync(CancellationToken cancellationToken);
        SettingsSession OpenSession();
        Task<SettingsCommitResult> SetAndCommitAsync(SettingId id, SettingValue value, CancellationToken cancellationToken);
        event Action<SettingsChangedEvent> Changed;
        event Action MetadataChanged;
    }

    public sealed class SettingsService : ISettingsService
    {
        private const int CurrentFormatVersion = 1;
        private readonly SettingsCatalog _catalog;
        private readonly ISettingsStore _store;
        private readonly IReadOnlyList<ISettingApplier> _appliers;
        private readonly IReadOnlyList<ISettingsMigration> _migrations;
        private readonly List<SettingsEntry> _unknownEntries = new();
        private SettingsSession _activeSession;

        public SettingsService(
            SettingsCatalog catalog,
            ISettingsStore store,
            IEnumerable<ISettingApplier> appliers,
            IEnumerable<ISettingsMigration> migrations = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _appliers = (appliers ?? Array.Empty<ISettingApplier>()).ToArray();
            _migrations = (migrations ?? Array.Empty<ISettingsMigration>()).OrderBy(x => x.FromVersion).ToArray();
            ValidateAppliers();
        }

        public bool IsReady { get; private set; }
        public SettingsSnapshot Committed { get; private set; }
        public event Action<SettingsChangedEvent> Changed;
        public event Action MetadataChanged;
        internal IReadOnlyCollection<SettingDefinition> CatalogDefinitions => _catalog.Definitions;

        public async Task<SettingsInitializeResult> InitializeAsync(CancellationToken cancellationToken)
        {
            if (IsReady)
            {
                return new SettingsInitializeResult(true, SettingsStoreSource.None, Array.Empty<string>());
            }

            var errors = new List<string>();
            var values = CreateDefaults();
            var load = _store.Load();
            var source = load.Source;
            if (!load.Success)
            {
                errors.Add(load.Error);
            }
            else if (load.Document != null)
            {
                if (!TryMigrate(load.Document, out var document, out var migrationError))
                {
                    errors.Add(migrationError);
                }
                else
                {
                    LoadValues(document, values, errors);
                }
            }

            var snapshot = new SettingsSnapshot(values);
            var apply = await ApplySnapshotAsync(snapshot, cancellationToken);
            if (!apply.Success)
            {
                errors.Add(apply.Error);
                return new SettingsInitializeResult(false, source, errors);
            }

            Committed = snapshot;
            IsReady = true;
            Changed?.Invoke(new SettingsChangedEvent(Committed, true));
            return new SettingsInitializeResult(true, source, errors);
        }

        public SettingsSession OpenSession()
        {
            if (!IsReady)
            {
                throw new InvalidOperationException("Settings are not initialized.");
            }

            if (_activeSession != null)
            {
                throw new InvalidOperationException("A writable settings session is already open.");
            }

            _activeSession = new SettingsSession(this, Committed.CopyValues());
            return _activeSession;
        }

        public async Task<SettingsCommitResult> SetAndCommitAsync(
            SettingId id,
            SettingValue value,
            CancellationToken cancellationToken)
        {
            var session = OpenSession();
            var set = await session.SetAsync(id, value, cancellationToken);
            if (!set.Success)
            {
                await session.CancelAsync(cancellationToken);
                return SettingsCommitResult.Failed("validate", set.Error);
            }

            return await session.CommitAsync(cancellationToken);
        }

        public void NotifyMetadataChanged() => MetadataChanged?.Invoke();

        internal bool TryGetDefinition(SettingId id, out SettingDefinition definition) => _catalog.TryGet(id, out definition);

        internal async Task<SettingOperationResult> PreviewAsync(
            SettingsSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            var result = await ApplySnapshotAsync(snapshot, cancellationToken);
            if (result.Success)
            {
                Changed?.Invoke(new SettingsChangedEvent(snapshot, false));
                MetadataChanged?.Invoke();
                return SettingOperationResult.Applied();
            }

            await ApplySnapshotAsync(Committed, CancellationToken.None);
            return SettingOperationResult.Failed(result.Error);
        }

        internal async Task<SettingsCommitResult> CommitAsync(
            SettingsSession session,
            SettingsSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            EnsureActive(session);
            var apply = await ApplySnapshotAsync(snapshot, cancellationToken);
            if (!apply.Success)
            {
                await ApplySnapshotAsync(Committed, CancellationToken.None);
                return SettingsCommitResult.Failed("apply", apply.Error);
            }

            var save = _store.Save(CreateDocument(snapshot));
            if (!save.Success)
            {
                await ApplySnapshotAsync(Committed, CancellationToken.None);
                return SettingsCommitResult.Failed("save", save.Error);
            }

            Committed = snapshot;
            _activeSession = null;
            session.Close();
            Changed?.Invoke(new SettingsChangedEvent(Committed, true));
            MetadataChanged?.Invoke();
            return SettingsCommitResult.Committed();
        }

        internal async Task CancelAsync(SettingsSession session, CancellationToken cancellationToken)
        {
            EnsureActive(session);
            await ApplySnapshotAsync(Committed, cancellationToken);
            _activeSession = null;
            session.Close();
            Changed?.Invoke(new SettingsChangedEvent(Committed, true));
        }

        private Dictionary<SettingId, SettingValue> CreateDefaults()
        {
            var values = new Dictionary<SettingId, SettingValue>();
            foreach (var definition in _catalog.Definitions)
            {
                values.Add(definition.Id, definition.DefaultValue);
            }

            return values;
        }

        private void LoadValues(SettingsDocument document, IDictionary<SettingId, SettingValue> values, ICollection<string> errors)
        {
            _unknownEntries.Clear();
            foreach (var entry in document.entries)
            {
                var id = new SettingId(entry.id);
                if (!_catalog.TryGet(id, out var definition))
                {
                    _unknownEntries.Add(entry.Clone());
                    continue;
                }

                if (!Enum.TryParse(entry.kind, false, out SettingValueKind kind) ||
                    !SettingValue.TryParse(kind, entry.value, out var value) ||
                    !definition.IsValid(value))
                {
                    errors.Add("invalid-value:" + entry.id);
                    continue;
                }

                values[id] = value;
            }
        }

        private SettingsDocument CreateDocument(SettingsSnapshot snapshot)
        {
            var document = new SettingsDocument { formatVersion = CurrentFormatVersion };
            foreach (var pair in snapshot.Values.OrderBy(x => x.Key.Value, StringComparer.Ordinal))
            {
                document.entries.Add(new SettingsEntry
                {
                    id = pair.Key.Value,
                    kind = pair.Value.Kind.ToString(),
                    value = pair.Value.ToCanonicalString()
                });
            }

            document.entries.AddRange(_unknownEntries.Select(x => x.Clone()));
            return document;
        }

        private bool TryMigrate(SettingsDocument source, out SettingsDocument document, out string error)
        {
            document = source.Clone();
            error = null;
            while (document.formatVersion < CurrentFormatVersion)
            {
                var currentVersion = document.formatVersion;
                var migration = _migrations.FirstOrDefault(x => x.FromVersion == currentVersion);
                if (migration == null || migration.ToVersion != document.formatVersion + 1 ||
                    !migration.TryMigrate(document, out document, out error))
                {
                    error ??= "migration-failed";
                    return false;
                }
            }

            if (document.formatVersion != CurrentFormatVersion)
            {
                error = "unsupported-version";
                return false;
            }

            return true;
        }

        private async Task<SettingApplyResult> ApplySnapshotAsync(
            SettingsSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            foreach (var applier in _appliers)
            {
                var result = await applier.ApplyAsync(snapshot, cancellationToken);
                if (!result.Success)
                {
                    return result;
                }
            }

            return SettingApplyResult.Applied();
        }

        private void ValidateAppliers()
        {
            var covered = new HashSet<SettingId>(_appliers.SelectMany(x => x.SettingIds));
            foreach (var definition in _catalog.Definitions)
            {
                if (definition.ApplyPolicy != SettingApplyPolicy.RestartRequired &&
                    definition.IsAvailable && !covered.Contains(definition.Id))
                {
                    throw new ArgumentException($"No applier registered for {definition.Id}.");
                }
            }
        }

        private void EnsureActive(SettingsSession session)
        {
            if (!ReferenceEquals(_activeSession, session))
            {
                throw new InvalidOperationException("Settings session is not active.");
            }
        }
    }

    public sealed class SettingsSession
    {
        private readonly SettingsService _service;
        private readonly Dictionary<SettingId, SettingValue> _working;

        internal SettingsSession(SettingsService service, Dictionary<SettingId, SettingValue> working)
        {
            _service = service;
            _working = working;
        }

        public bool IsOpen { get; private set; } = true;
        public SettingsSnapshot Working => new(_working);
        public bool IsDirty => _working.Any(x => !_service.Committed.TryGet(x.Key, out var committed) || !committed.Equals(x.Value));

        public async Task<SettingOperationResult> SetAsync(
            SettingId id,
            SettingValue value,
            CancellationToken cancellationToken)
        {
            EnsureOpen();
            if (!_service.TryGetDefinition(id, out var definition) || !definition.IsAvailable)
            {
                return SettingOperationResult.Failed("unavailable");
            }

            if (!definition.IsValid(value))
            {
                return SettingOperationResult.Failed("invalid-value");
            }

            var previous = _working[id];
            _working[id] = value;
            if (definition.ApplyPolicy != SettingApplyPolicy.Preview)
            {
                return SettingOperationResult.Applied();
            }

            var result = await _service.PreviewAsync(Working, cancellationToken);
            if (!result.Success)
            {
                _working[id] = previous;
            }

            return result;
        }

        public async Task<SettingOperationResult> ResetAsync(
            SettingId id,
            CancellationToken cancellationToken)
        {
            return _service.TryGetDefinition(id, out var definition)
                ? await SetAsync(id, definition.DefaultValue, cancellationToken)
                : SettingOperationResult.Failed("unknown-setting");
        }

        public async Task ResetCategoryAsync(string categoryId, CancellationToken cancellationToken)
        {
            foreach (var definition in _service.CatalogDefinitions.Where(x => x.CategoryId == categoryId))
            {
                var result = await SetAsync(definition.Id, definition.DefaultValue, cancellationToken);
                if (!result.Success)
                {
                    throw new InvalidOperationException(result.Error);
                }
            }
        }

        public async Task ResetAllAsync(CancellationToken cancellationToken)
        {
            foreach (var definition in _service.CatalogDefinitions)
            {
                var result = await SetAsync(definition.Id, definition.DefaultValue, cancellationToken);
                if (!result.Success)
                {
                    throw new InvalidOperationException(result.Error);
                }
            }
        }

        public Task<SettingsCommitResult> CommitAsync(CancellationToken cancellationToken)
        {
            EnsureOpen();
            return _service.CommitAsync(this, Working, cancellationToken);
        }

        public Task CancelAsync(CancellationToken cancellationToken)
        {
            EnsureOpen();
            return _service.CancelAsync(this, cancellationToken);
        }

        internal void Close() => IsOpen = false;

        private void EnsureOpen()
        {
            if (!IsOpen)
            {
                throw new InvalidOperationException("Settings session is closed.");
            }
        }
    }
}
