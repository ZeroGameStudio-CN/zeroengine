using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.Persistence
{
    public sealed class SaveSlotPipelineOptions<TMetadata>
    {
        public ISaveSlotOperationGate Gate { get; set; }
        public ISaveSlotMetadataProvider<TMetadata> MetadataProvider { get; set; }
        public ISaveSlotScreenshotProvider ScreenshotProvider { get; set; }
        public ScreenshotFilePolicy ScreenshotPolicy { get; set; }
    }

    /// <summary>
    /// Backend-independent save/load/delete orchestration. It deliberately does
    /// not know about serialization, files, ES3, Unity objects, or project data.
    /// </summary>
    public sealed class SaveSlotPipeline<TMetadata>
    {
        private readonly ISaveSlotBackend<TMetadata> _backend;
        private readonly SaveParticipantRegistry _participants;
        private readonly SaveSlotPipelineOptions<TMetadata> _options;

        public SaveSlotPipeline(
            ISaveSlotBackend<TMetadata> backend,
            SaveParticipantRegistry participants,
            SaveSlotPipelineOptions<TMetadata> options = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _participants = participants ?? throw new ArgumentNullException(nameof(participants));
            _options = options ?? new SaveSlotPipelineOptions<TMetadata>();
        }

        public Task<SaveSlotResult> SaveAsync(string slotId, CancellationToken cancellationToken = default)
        {
            return SaveCoreAsync(slotId, cancellationToken);
        }

        public Task<SaveSlotResult> SaveSlotAsync(string slotId, CancellationToken cancellationToken = default)
        {
            return SaveCoreAsync(slotId, cancellationToken);
        }

        public Task<SaveSlotLoadResult<TMetadata>> LoadAsync(
            string slotId,
            CancellationToken cancellationToken = default)
        {
            return LoadCoreAsync(slotId, cancellationToken);
        }

        public Task<SaveSlotLoadResult<TMetadata>> LoadSlotAsync(
            string slotId,
            CancellationToken cancellationToken = default)
        {
            return LoadCoreAsync(slotId, cancellationToken);
        }

        public Task<SaveSlotResult> DeleteAsync(string slotId, CancellationToken cancellationToken = default)
        {
            return DeleteCoreAsync(slotId, cancellationToken);
        }

        public Task<SaveSlotResult> DeleteSlotAsync(string slotId, CancellationToken cancellationToken = default)
        {
            return DeleteCoreAsync(slotId, cancellationToken);
        }

        private async Task<SaveSlotResult> SaveCoreAsync(string slotId, CancellationToken cancellationToken)
        {
            if (!TryValidateSlotId(slotId, out var validationError))
            {
                return SaveSlotResult.Failed(slotId, validationError);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryEvaluateGate(slotId, SaveSlotOperation.Save, out var blocked))
                {
                    return blocked;
                }

                var capture = _participants.Capture(cancellationToken);
                if (capture.IsCancelled)
                {
                    return SaveSlotResult.Cancelled(slotId, capture.Error, capture.Exception);
                }

                if (!capture.Success)
                {
                    return SaveSlotResult.Failed(slotId, capture.Error, capture.Exception);
                }

                var metadata = default(TMetadata);
                if (_options.MetadataProvider != null)
                {
                    metadata = await _options.MetadataProvider
                        .CaptureAsync(slotId, cancellationToken);
                }

                SaveScreenshot screenshot = null;
                if (_options.ScreenshotProvider != null)
                {
                    screenshot = await _options.ScreenshotProvider
                        .CaptureAsync(slotId, cancellationToken);

                    if (_options.ScreenshotPolicy != null && screenshot != null)
                    {
                        if (!_options.ScreenshotPolicy.TryGetPath(slotId, out _, out var pathError))
                        {
                            return SaveSlotResult.Failed(slotId, pathError);
                        }

                        var screenshotValidation = _options.ScreenshotPolicy.Validate(slotId, screenshot);
                        if (!screenshotValidation.IsValid)
                        {
                            return SaveSlotResult.Failed(slotId, screenshotValidation.Error);
                        }
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                var payload = new SaveSlotPayload<TMetadata>(slotId, metadata, capture.States, screenshot);
                var backendResult = await _backend.SaveAsync(slotId, payload, cancellationToken);
                if (backendResult.Status == SaveSlotOperationStatus.Saved)
                {
                    return SaveSlotResult.Saved(slotId);
                }

                return backendResult.Status == SaveSlotOperationStatus.Cancelled
                    ? SaveSlotResult.Cancelled(slotId, backendResult.Error, backendResult.Exception)
                    : SaveSlotResult.Failed(slotId, backendResult.Error ?? "backend-save-failed", backendResult.Exception);
            }
            catch (OperationCanceledException exception)
            {
                return SaveSlotResult.Cancelled(slotId, exception.Message, exception);
            }
            catch (Exception exception)
            {
                return SaveSlotResult.Failed(slotId, exception.Message, exception);
            }
        }

        private async Task<SaveSlotLoadResult<TMetadata>> LoadCoreAsync(
            string slotId,
            CancellationToken cancellationToken)
        {
            if (!TryValidateSlotId(slotId, out var validationError))
            {
                return SaveSlotLoadResult<TMetadata>.Failed(slotId, validationError);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryEvaluateGate(slotId, SaveSlotOperation.Load, out var blocked))
                {
                    return SaveSlotLoadResult<TMetadata>.Blocked(slotId, blocked.Error);
                }

                var backendResult = await _backend.LoadAsync(slotId, cancellationToken);
                switch (backendResult.Status)
                {
                    case SaveSlotOperationStatus.Missing:
                        return SaveSlotLoadResult<TMetadata>.Missing(slotId, backendResult.Error);
                    case SaveSlotOperationStatus.Cancelled:
                        return SaveSlotLoadResult<TMetadata>.Cancelled(
                            slotId,
                            backendResult.Error,
                            backendResult.Exception);
                    case SaveSlotOperationStatus.Failed:
                        return SaveSlotLoadResult<TMetadata>.Failed(
                            slotId,
                            backendResult.Error ?? "backend-load-failed",
                            backendResult.Exception);
                    case SaveSlotOperationStatus.Loaded:
                        break;
                    default:
                        return SaveSlotLoadResult<TMetadata>.Failed(slotId, "backend-load-invalid-status");
                }

                if (backendResult.Payload == null)
                {
                    return SaveSlotLoadResult<TMetadata>.Failed(slotId, "backend-payload-null");
                }

                var restore = await _participants
                    .PrepareAndRestoreAsync(backendResult.Payload.ParticipantStates, cancellationToken);
                if (restore.IsCancelled)
                {
                    return SaveSlotLoadResult<TMetadata>.Cancelled(slotId, restore.Error, restore.Exception);
                }

                if (!restore.Success)
                {
                    return SaveSlotLoadResult<TMetadata>.Failed(slotId, restore.Error, restore.Exception);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return SaveSlotLoadResult<TMetadata>.Loaded(slotId, backendResult.Payload);
            }
            catch (OperationCanceledException exception)
            {
                return SaveSlotLoadResult<TMetadata>.Cancelled(slotId, exception.Message, exception);
            }
            catch (Exception exception)
            {
                return SaveSlotLoadResult<TMetadata>.Failed(slotId, exception.Message, exception);
            }
        }

        private async Task<SaveSlotResult> DeleteCoreAsync(
            string slotId,
            CancellationToken cancellationToken)
        {
            if (!TryValidateSlotId(slotId, out var validationError))
            {
                return SaveSlotResult.Failed(slotId, validationError);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryEvaluateGate(slotId, SaveSlotOperation.Delete, out var blocked))
                {
                    return blocked;
                }

                var backendResult = await _backend.DeleteAsync(slotId, cancellationToken);
                if (backendResult.Status == SaveSlotOperationStatus.Deleted)
                {
                    return SaveSlotResult.Deleted(slotId);
                }

                return backendResult.Status == SaveSlotOperationStatus.Cancelled
                    ? SaveSlotResult.Cancelled(slotId, backendResult.Error, backendResult.Exception)
                    : SaveSlotResult.Failed(slotId, backendResult.Error ?? "backend-delete-failed", backendResult.Exception);
            }
            catch (OperationCanceledException exception)
            {
                return SaveSlotResult.Cancelled(slotId, exception.Message, exception);
            }
            catch (Exception exception)
            {
                return SaveSlotResult.Failed(slotId, exception.Message, exception);
            }
        }

        private bool TryEvaluateGate(string slotId, SaveSlotOperation operation, out SaveSlotResult result)
        {
            result = default(SaveSlotResult);
            if (_options.Gate == null)
            {
                return true;
            }

            var decision = _options.Gate.Evaluate(slotId, operation);
            if (decision.Allowed)
            {
                return true;
            }

            result = SaveSlotResult.Blocked(slotId, decision.Reason);
            return false;
        }

        private static bool TryValidateSlotId(string slotId, out string error)
        {
            if (string.IsNullOrWhiteSpace(slotId))
            {
                error = "slot-id-empty";
                return false;
            }

            if (slotId.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0 || slotId == "." || slotId == "..")
            {
                error = "slot-id-invalid";
                return false;
            }

            error = null;
            return true;
        }
    }
}
