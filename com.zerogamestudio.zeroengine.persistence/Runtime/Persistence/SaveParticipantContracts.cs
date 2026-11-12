using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.Persistence
{
    /// <summary>
    /// A project-facing adapter for one save participant. The key is part of the
    /// serialized contract and must remain stable across type or assembly moves.
    /// </summary>
    public interface ISaveParticipantAdapter
    {
        string Key { get; }

        object Capture();

        void Restore(object state);
    }

    /// <summary>
    /// Optional asynchronous preparation contract. Preparation for every
    /// participant is awaited before any participant is restored.
    /// </summary>
    public interface IAsyncSaveParticipantAdapter
    {
        Task PrepareRestoreAsync(object state, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Alternate name for the optional preparation contract used by consumers
    /// that keep their adapter and preparer interfaces separate.
    /// </summary>
    public interface ISaveParticipantRestorePreparer
    {
        Task PrepareRestoreAsync(object state, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Convenience base class for adapters. Override only the participant
    /// behavior; preparation is optional and is a completed operation by default.
    /// </summary>
    public abstract class SaveParticipantAdapter : ISaveParticipantAdapter, IAsyncSaveParticipantAdapter
    {
        public abstract string Key { get; }

        public string StableKey => Key;

        public abstract object Capture();

        public virtual Task PrepareRestoreAsync(object state, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public abstract void Restore(object state);
    }

    /// <summary>
    /// Delegate-based adapter useful for composition and tests.
    /// </summary>
    public sealed class DelegateSaveParticipantAdapter : ISaveParticipantAdapter, IAsyncSaveParticipantAdapter
    {
        private readonly Func<object> _capture;
        private readonly Action<object> _restore;
        private readonly Func<object, CancellationToken, Task> _prepare;

        public DelegateSaveParticipantAdapter(
            string key,
            Func<object> capture,
            Action<object> restore,
            Func<object, CancellationToken, Task> prepare = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Participant key cannot be empty.", nameof(key));
            }

            Key = key;
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
            _restore = restore ?? throw new ArgumentNullException(nameof(restore));
            _prepare = prepare;
        }

        public string Key { get; }

        public object Capture() => _capture();

        public Task PrepareRestoreAsync(object state, CancellationToken cancellationToken)
        {
            return _prepare == null
                ? Task.CompletedTask
                : _prepare(state, cancellationToken) ?? Task.CompletedTask;
        }

        public void Restore(object state) => _restore(state);
    }

    /// <summary>
    /// Adapter for the legacy ZeroEngine.Save ISaveable contract. It keeps the
    /// old API available while allowing new orchestration to use stable keys.
    /// </summary>
    public sealed class LegacySaveParticipantAdapter : SaveParticipantAdapter
    {
        private readonly ZeroEngine.Save.ISaveable _saveable;

        public LegacySaveParticipantAdapter(ZeroEngine.Save.ISaveable saveable)
        {
            _saveable = saveable ?? throw new ArgumentNullException(nameof(saveable));
            if (string.IsNullOrWhiteSpace(_saveable.SaveKey))
            {
                throw new ArgumentException("Legacy saveable key cannot be empty.", nameof(saveable));
            }
        }

        public override string Key => _saveable.SaveKey;

        public override object Capture() => _saveable.ExportSaveData();

        public override void Restore(object state) => _saveable.ImportSaveData(state);
    }

    public enum SaveParticipantOperationStatus
    {
        Succeeded,
        Failed,
        Cancelled
    }

    public readonly struct SaveParticipantCaptureResult
    {
        private SaveParticipantCaptureResult(
            SaveParticipantOperationStatus status,
            IReadOnlyDictionary<string, object> states,
            string error,
            Exception exception)
        {
            Status = status;
            States = states;
            Error = error;
            Exception = exception;
        }

        public SaveParticipantOperationStatus Status { get; }
        public bool Success => Status == SaveParticipantOperationStatus.Succeeded;
        public bool IsCancelled => Status == SaveParticipantOperationStatus.Cancelled;
        public IReadOnlyDictionary<string, object> States { get; }
        public string Error { get; }
        public Exception Exception { get; }

        public static SaveParticipantCaptureResult Succeeded(IReadOnlyDictionary<string, object> states)
        {
            return new SaveParticipantCaptureResult(
                SaveParticipantOperationStatus.Succeeded,
                states ?? throw new ArgumentNullException(nameof(states)),
                null,
                null);
        }

        public static SaveParticipantCaptureResult Failed(string error, Exception exception = null)
        {
            return new SaveParticipantCaptureResult(
                SaveParticipantOperationStatus.Failed,
                null,
                error,
                exception);
        }

        public static SaveParticipantCaptureResult Cancelled(string error = "cancelled", Exception exception = null)
        {
            return new SaveParticipantCaptureResult(
                SaveParticipantOperationStatus.Cancelled,
                null,
                error,
                exception);
        }
    }

    public readonly struct SaveParticipantRestoreResult
    {
        private SaveParticipantRestoreResult(
            SaveParticipantOperationStatus status,
            IReadOnlyList<string> restoredKeys,
            string error,
            Exception exception)
        {
            Status = status;
            RestoredKeys = restoredKeys;
            Error = error;
            Exception = exception;
        }

        public SaveParticipantOperationStatus Status { get; }
        public bool Success => Status == SaveParticipantOperationStatus.Succeeded;
        public bool IsCancelled => Status == SaveParticipantOperationStatus.Cancelled;
        public IReadOnlyList<string> RestoredKeys { get; }
        public string Error { get; }
        public Exception Exception { get; }

        public static SaveParticipantRestoreResult Succeeded(IReadOnlyList<string> restoredKeys)
        {
            return new SaveParticipantRestoreResult(
                SaveParticipantOperationStatus.Succeeded,
                restoredKeys ?? throw new ArgumentNullException(nameof(restoredKeys)),
                null,
                null);
        }

        public static SaveParticipantRestoreResult Failed(string error, Exception exception = null)
        {
            return new SaveParticipantRestoreResult(
                SaveParticipantOperationStatus.Failed,
                null,
                error,
                exception);
        }

        public static SaveParticipantRestoreResult Cancelled(string error = "cancelled", Exception exception = null)
        {
            return new SaveParticipantRestoreResult(
                SaveParticipantOperationStatus.Cancelled,
                null,
                error,
                exception);
        }
    }

    /// <summary>
    /// Ordered participant registry. Registration order is the restore order;
    /// duplicate keys are rejected rather than silently replacing state.
    /// </summary>
    public sealed class SaveParticipantRegistry
    {
        private readonly List<ISaveParticipantAdapter> _participants = new List<ISaveParticipantAdapter>();
        private readonly Dictionary<string, ISaveParticipantAdapter> _byKey =
            new Dictionary<string, ISaveParticipantAdapter>(StringComparer.Ordinal);

        public IReadOnlyList<ISaveParticipantAdapter> Participants => _participants.AsReadOnly();

        public int Count => _participants.Count;

        public void Register(ISaveParticipantAdapter participant)
        {
            if (participant == null)
            {
                throw new ArgumentNullException(nameof(participant));
            }

            if (string.IsNullOrWhiteSpace(participant.Key))
            {
                throw new ArgumentException("Participant key cannot be empty.", nameof(participant));
            }

            if (_byKey.ContainsKey(participant.Key))
            {
                throw new ArgumentException(
                    "A participant with key '" + participant.Key + "' is already registered.",
                    nameof(participant));
            }

            _byKey.Add(participant.Key, participant);
            _participants.Add(participant);
        }

        public bool TryRegister(ISaveParticipantAdapter participant, out string error)
        {
            try
            {
                Register(participant);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public bool Unregister(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || !_byKey.TryGetValue(key, out var participant))
            {
                return false;
            }

            _byKey.Remove(key);
            _participants.Remove(participant);
            return true;
        }

        public bool TryGet(string key, out ISaveParticipantAdapter participant)
        {
            return _byKey.TryGetValue(key, out participant);
        }

        public SaveParticipantCaptureResult Capture(CancellationToken cancellationToken = default)
        {
            var states = new Dictionary<string, object>(StringComparer.Ordinal);
            try
            {
                foreach (var participant in _participants)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    states.Add(participant.Key, participant.Capture());
                }

                return SaveParticipantCaptureResult.Succeeded(states);
            }
            catch (OperationCanceledException exception)
            {
                return SaveParticipantCaptureResult.Cancelled(exception.Message, exception);
            }
            catch (Exception exception)
            {
                return SaveParticipantCaptureResult.Failed(exception.Message, exception);
            }
        }

        public Task<SaveParticipantRestoreResult> RestoreAsync(
            IReadOnlyDictionary<string, object> states,
            CancellationToken cancellationToken = default)
        {
            return RestoreCoreAsync(states, cancellationToken);
        }

        public Task<SaveParticipantRestoreResult> PrepareAndRestoreAsync(
            IReadOnlyDictionary<string, object> states,
            CancellationToken cancellationToken = default)
        {
            return RestoreCoreAsync(states, cancellationToken);
        }

        private async Task<SaveParticipantRestoreResult> RestoreCoreAsync(
            IReadOnlyDictionary<string, object> states,
            CancellationToken cancellationToken)
        {
            if (states == null)
            {
                return SaveParticipantRestoreResult.Failed("participant-states-null");
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var participant in _participants)
                {
                    if (!states.TryGetValue(participant.Key, out var state))
                    {
                        continue;
                    }

                    if (participant is IAsyncSaveParticipantAdapter asyncParticipant)
                    {
                        await (asyncParticipant.PrepareRestoreAsync(state, cancellationToken) ?? Task.CompletedTask);
                    }
                    else if (participant is ISaveParticipantRestorePreparer preparer)
                    {
                        await (preparer.PrepareRestoreAsync(state, cancellationToken) ?? Task.CompletedTask);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                var restoredKeys = new List<string>();
                foreach (var participant in _participants)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!states.TryGetValue(participant.Key, out var state))
                    {
                        continue;
                    }

                    participant.Restore(state);
                    restoredKeys.Add(participant.Key);
                }

                return SaveParticipantRestoreResult.Succeeded(restoredKeys.AsReadOnly());
            }
            catch (OperationCanceledException exception)
            {
                return SaveParticipantRestoreResult.Cancelled(exception.Message, exception);
            }
            catch (Exception exception)
            {
                return SaveParticipantRestoreResult.Failed(exception.Message, exception);
            }
        }
    }
}
