using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.Persistence
{
    public enum SaveSlotOperation
    {
        Save,
        Load,
        Delete
    }

    public enum SaveSlotOperationStatus
    {
        Saved,
        Loaded,
        Deleted,
        Blocked,
        Failed,
        Cancelled,
        Missing
    }

    public readonly struct SaveSlotGateDecision
    {
        private SaveSlotGateDecision(bool allowed, string reason)
        {
            Allowed = allowed;
            Reason = reason;
        }

        public bool Allowed { get; }
        public string Reason { get; }

        public static SaveSlotGateDecision Allow() => new SaveSlotGateDecision(true, null);

        public static SaveSlotGateDecision Block(string reason)
        {
            return new SaveSlotGateDecision(false, string.IsNullOrWhiteSpace(reason) ? "blocked" : reason);
        }
    }

    /// <summary>Project policy hook used before capture or backend access.</summary>
    public interface ISaveSlotOperationGate
    {
        SaveSlotGateDecision Evaluate(string slotId, SaveSlotOperation operation);
    }

    /// <summary>Project metadata source used when creating a slot payload.</summary>
    public interface ISaveSlotMetadataProvider<TMetadata>
    {
        Task<TMetadata> CaptureAsync(string slotId, CancellationToken cancellationToken);
    }

    /// <summary>Optional project screenshot source used when creating a slot payload.</summary>
    public interface ISaveSlotScreenshotProvider
    {
        Task<SaveScreenshot> CaptureAsync(string slotId, CancellationToken cancellationToken);
    }

    public sealed class DelegateSaveSlotOperationGate : ISaveSlotOperationGate
    {
        private readonly Func<string, SaveSlotOperation, SaveSlotGateDecision> _evaluate;

        public DelegateSaveSlotOperationGate(Func<string, SaveSlotOperation, SaveSlotGateDecision> evaluate)
        {
            _evaluate = evaluate ?? throw new ArgumentNullException(nameof(evaluate));
        }

        public SaveSlotGateDecision Evaluate(string slotId, SaveSlotOperation operation)
        {
            return _evaluate(slotId, operation);
        }
    }

    public sealed class DelegateSaveSlotMetadataProvider<TMetadata> : ISaveSlotMetadataProvider<TMetadata>
    {
        private readonly Func<string, CancellationToken, Task<TMetadata>> _capture;

        public DelegateSaveSlotMetadataProvider(Func<string, CancellationToken, Task<TMetadata>> capture)
        {
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        }

        public Task<TMetadata> CaptureAsync(string slotId, CancellationToken cancellationToken)
        {
            return _capture(slotId, cancellationToken) ?? Task.FromResult(default(TMetadata));
        }
    }

    public sealed class DelegateSaveSlotScreenshotProvider : ISaveSlotScreenshotProvider
    {
        private readonly Func<string, CancellationToken, Task<SaveScreenshot>> _capture;

        public DelegateSaveSlotScreenshotProvider(Func<string, CancellationToken, Task<SaveScreenshot>> capture)
        {
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        }

        public Task<SaveScreenshot> CaptureAsync(string slotId, CancellationToken cancellationToken)
        {
            return _capture(slotId, cancellationToken) ?? Task.FromResult<SaveScreenshot>(null);
        }
    }

    /// <summary>
    /// Screenshot bytes captured by a project adapter. Validation and path
    /// policy are deliberately separate so ZE does not depend on Unity.
    /// </summary>
    public sealed class SaveScreenshot
    {
        public SaveScreenshot(string fileName, byte[] data, int width = 0, int height = 0)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("Screenshot file name cannot be empty.", nameof(fileName));
            }

            FileName = fileName;
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Width = width;
            Height = height;
        }

        public string FileName { get; }
        public byte[] Data { get; }
        public int Width { get; }
        public int Height { get; }
    }

    /// <summary>Payload exchanged between the pipeline and a slot backend.</summary>
    public sealed class SaveSlotPayload<TMetadata>
    {
        public SaveSlotPayload(
            string slotId,
            TMetadata metadata,
            IReadOnlyDictionary<string, object> participantStates,
            SaveScreenshot screenshot = null)
        {
            if (string.IsNullOrWhiteSpace(slotId))
            {
                throw new ArgumentException("Slot id cannot be empty.", nameof(slotId));
            }

            SlotId = slotId;
            Metadata = metadata;
            ParticipantStates = participantStates ?? throw new ArgumentNullException(nameof(participantStates));
            Screenshot = screenshot;
        }

        public string SlotId { get; }
        public TMetadata Metadata { get; }
        public IReadOnlyDictionary<string, object> ParticipantStates { get; }
        public SaveScreenshot Screenshot { get; }
    }

    public readonly struct SaveSlotBackendWriteResult
    {
        private SaveSlotBackendWriteResult(SaveSlotOperationStatus status, string error, Exception exception)
        {
            Status = status;
            Error = error;
            Exception = exception;
        }

        public SaveSlotOperationStatus Status { get; }
        public bool Success => Status == SaveSlotOperationStatus.Saved;
        public bool IsCancelled => Status == SaveSlotOperationStatus.Cancelled;
        public string Error { get; }
        public Exception Exception { get; }

        public static SaveSlotBackendWriteResult Saved() =>
            new SaveSlotBackendWriteResult(SaveSlotOperationStatus.Saved, null, null);

        public static SaveSlotBackendWriteResult Failed(string error, Exception exception = null) =>
            new SaveSlotBackendWriteResult(SaveSlotOperationStatus.Failed, error, exception);

        public static SaveSlotBackendWriteResult Cancelled(string error = "cancelled", Exception exception = null) =>
            new SaveSlotBackendWriteResult(SaveSlotOperationStatus.Cancelled, error, exception);
    }

    public readonly struct SaveSlotBackendDeleteResult
    {
        private SaveSlotBackendDeleteResult(SaveSlotOperationStatus status, string error, Exception exception)
        {
            Status = status;
            Error = error;
            Exception = exception;
        }

        public SaveSlotOperationStatus Status { get; }
        public bool Success => Status == SaveSlotOperationStatus.Deleted;
        public bool IsCancelled => Status == SaveSlotOperationStatus.Cancelled;
        public string Error { get; }
        public Exception Exception { get; }

        public static SaveSlotBackendDeleteResult Deleted() =>
            new SaveSlotBackendDeleteResult(SaveSlotOperationStatus.Deleted, null, null);

        public static SaveSlotBackendDeleteResult Failed(string error, Exception exception = null) =>
            new SaveSlotBackendDeleteResult(SaveSlotOperationStatus.Failed, error, exception);

        public static SaveSlotBackendDeleteResult Cancelled(string error = "cancelled", Exception exception = null) =>
            new SaveSlotBackendDeleteResult(SaveSlotOperationStatus.Cancelled, error, exception);
    }

    public readonly struct SaveSlotReadResult<TMetadata>
    {
        private SaveSlotReadResult(
            SaveSlotOperationStatus status,
            SaveSlotPayload<TMetadata> payload,
            string error,
            Exception exception)
        {
            Status = status;
            Payload = payload;
            Error = error;
            Exception = exception;
        }

        public SaveSlotOperationStatus Status { get; }
        public bool Success => Status == SaveSlotOperationStatus.Loaded;
        public bool IsMissing => Status == SaveSlotOperationStatus.Missing;
        public bool IsCancelled => Status == SaveSlotOperationStatus.Cancelled;
        public SaveSlotPayload<TMetadata> Payload { get; }
        public string Error { get; }
        public Exception Exception { get; }

        public static SaveSlotReadResult<TMetadata> Loaded(SaveSlotPayload<TMetadata> payload)
        {
            return new SaveSlotReadResult<TMetadata>(
                SaveSlotOperationStatus.Loaded,
                payload ?? throw new ArgumentNullException(nameof(payload)),
                null,
                null);
        }

        public static SaveSlotReadResult<TMetadata> Missing(string error = "missing") =>
            new SaveSlotReadResult<TMetadata>(SaveSlotOperationStatus.Missing, null, error, null);

        public static SaveSlotReadResult<TMetadata> Failed(string error, Exception exception = null) =>
            new SaveSlotReadResult<TMetadata>(SaveSlotOperationStatus.Failed, null, error, exception);

        public static SaveSlotReadResult<TMetadata> Cancelled(string error = "cancelled", Exception exception = null) =>
            new SaveSlotReadResult<TMetadata>(SaveSlotOperationStatus.Cancelled, null, error, exception);
    }

    /// <summary>
    /// Backend contract. Serialization, temporary files and read-back validation
    /// belong to the project backend; the pipeline owns orchestration only.
    /// </summary>
    public interface ISaveSlotBackend<TMetadata>
    {
        Task<SaveSlotBackendWriteResult> SaveAsync(
            string slotId,
            SaveSlotPayload<TMetadata> payload,
            CancellationToken cancellationToken);

        Task<SaveSlotReadResult<TMetadata>> LoadAsync(
            string slotId,
            CancellationToken cancellationToken);

        Task<SaveSlotBackendDeleteResult> DeleteAsync(
            string slotId,
            CancellationToken cancellationToken);
    }

    public static class SaveSlotBackendExtensions
    {
        public static Task<SaveSlotBackendWriteResult> WriteAsync<TMetadata>(
            this ISaveSlotBackend<TMetadata> backend,
            string slotId,
            SaveSlotPayload<TMetadata> payload,
            CancellationToken cancellationToken = default)
        {
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            return backend.SaveAsync(slotId, payload, cancellationToken);
        }

        public static Task<SaveSlotReadResult<TMetadata>> ReadAsync<TMetadata>(
            this ISaveSlotBackend<TMetadata> backend,
            string slotId,
            CancellationToken cancellationToken = default)
        {
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            return backend.LoadAsync(slotId, cancellationToken);
        }
    }

    public readonly struct SaveSlotResult
    {
        private SaveSlotResult(
            SaveSlotOperationStatus status,
            string slotId,
            string error,
            Exception exception)
        {
            Status = status;
            SlotId = slotId;
            Error = error;
            Exception = exception;
        }

        public SaveSlotOperationStatus Status { get; }
        public string SlotId { get; }
        public bool Success => Status == SaveSlotOperationStatus.Saved || Status == SaveSlotOperationStatus.Deleted;
        public bool IsBlocked => Status == SaveSlotOperationStatus.Blocked;
        public bool IsCancelled => Status == SaveSlotOperationStatus.Cancelled;
        public string Error { get; }
        public Exception Exception { get; }

        public static SaveSlotResult Saved(string slotId) =>
            new SaveSlotResult(SaveSlotOperationStatus.Saved, slotId, null, null);

        public static SaveSlotResult Deleted(string slotId) =>
            new SaveSlotResult(SaveSlotOperationStatus.Deleted, slotId, null, null);

        public static SaveSlotResult Blocked(string slotId, string reason) =>
            new SaveSlotResult(SaveSlotOperationStatus.Blocked, slotId, reason, null);

        public static SaveSlotResult Failed(string slotId, string error, Exception exception = null) =>
            new SaveSlotResult(SaveSlotOperationStatus.Failed, slotId, error, exception);

        public static SaveSlotResult Cancelled(string slotId, string error = "cancelled", Exception exception = null) =>
            new SaveSlotResult(SaveSlotOperationStatus.Cancelled, slotId, error, exception);
    }

    public readonly struct SaveSlotLoadResult<TMetadata>
    {
        private SaveSlotLoadResult(
            SaveSlotOperationStatus status,
            string slotId,
            SaveSlotPayload<TMetadata> payload,
            string error,
            Exception exception)
        {
            Status = status;
            SlotId = slotId;
            Payload = payload;
            Error = error;
            Exception = exception;
        }

        public SaveSlotOperationStatus Status { get; }
        public string SlotId { get; }
        public bool Success => Status == SaveSlotOperationStatus.Loaded;
        public bool IsBlocked => Status == SaveSlotOperationStatus.Blocked;
        public bool IsMissing => Status == SaveSlotOperationStatus.Missing;
        public bool IsCancelled => Status == SaveSlotOperationStatus.Cancelled;
        public SaveSlotPayload<TMetadata> Payload { get; }
        public string Error { get; }
        public Exception Exception { get; }

        public static SaveSlotLoadResult<TMetadata> Loaded(string slotId, SaveSlotPayload<TMetadata> payload) =>
            new SaveSlotLoadResult<TMetadata>(
                SaveSlotOperationStatus.Loaded,
                slotId,
                payload ?? throw new ArgumentNullException(nameof(payload)),
                null,
                null);

        public static SaveSlotLoadResult<TMetadata> Missing(string slotId, string error = "missing") =>
            new SaveSlotLoadResult<TMetadata>(SaveSlotOperationStatus.Missing, slotId, null, error, null);

        public static SaveSlotLoadResult<TMetadata> Blocked(string slotId, string reason) =>
            new SaveSlotLoadResult<TMetadata>(SaveSlotOperationStatus.Blocked, slotId, null, reason, null);

        public static SaveSlotLoadResult<TMetadata> Failed(string slotId, string error, Exception exception = null) =>
            new SaveSlotLoadResult<TMetadata>(SaveSlotOperationStatus.Failed, slotId, null, error, exception);

        public static SaveSlotLoadResult<TMetadata> Cancelled(string slotId, string error = "cancelled", Exception exception = null) =>
            new SaveSlotLoadResult<TMetadata>(SaveSlotOperationStatus.Cancelled, slotId, null, error, exception);
    }
}
