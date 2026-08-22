using System;
using System.Collections.Generic;
using System.IO;

namespace ZeroEngine.Persistence
{
    public enum AtomicPromotionPhase
    {
        BeforeBackup,
        AfterBackup,
        BeforeTempPromotion,
        AfterTempPromotion,
        BeforeCleanup,
        AfterCleanup,
        Rollback
    }

    public interface IAtomicPromotionHook
    {
        void OnPhase(AtomicPromotionPhase phase, AtomicPromotionFile file);
    }

    public sealed class DelegateAtomicPromotionHook : IAtomicPromotionHook
    {
        private readonly Action<AtomicPromotionPhase, AtomicPromotionFile> _callback;

        public DelegateAtomicPromotionHook(Action<AtomicPromotionPhase, AtomicPromotionFile> callback)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public void OnPhase(AtomicPromotionPhase phase, AtomicPromotionFile file)
        {
            _callback(phase, file);
        }
    }

    public sealed class AtomicPromotionFile
    {
        public AtomicPromotionFile(string finalPath, string tempPath, string backupPath)
        {
            FinalPath = finalPath ?? throw new ArgumentNullException(nameof(finalPath));
            TempPath = tempPath ?? throw new ArgumentNullException(nameof(tempPath));
            BackupPath = backupPath ?? throw new ArgumentNullException(nameof(backupPath));
        }

        public string FinalPath { get; }
        public string TempPath { get; }
        public string BackupPath { get; }
    }

    public readonly struct AtomicPromotionResult
    {
        private AtomicPromotionResult(
            bool success,
            bool rolledBack,
            string error,
            Exception exception)
        {
            Success = success;
            RolledBack = rolledBack;
            Error = error;
            Exception = exception;
        }

        public bool Success { get; }
        public bool RolledBack { get; }
        public string Error { get; }
        public Exception Exception { get; }

        internal static AtomicPromotionResult Completed() =>
            new AtomicPromotionResult(true, false, null, null);

        internal static AtomicPromotionResult Failed(string error, bool rolledBack, Exception exception = null) =>
            new AtomicPromotionResult(false, rolledBack, error, exception);
    }

    /// <summary>
    /// Promotes several already-written temporary files as one logical change.
    /// Backends must perform serialization and read-back verification before
    /// calling this utility; this class only owns promotion and recovery.
    /// </summary>
    public static class AtomicFilePromotion
    {
        public static AtomicPromotionResult Promote(
            IReadOnlyList<AtomicPromotionFile> files,
            IAtomicPromotionHook hook = null)
        {
            if (!TryValidate(files, out var validationError))
            {
                return AtomicPromotionResult.Failed(validationError, false);
            }

            var states = new List<PromotionState>(files.Count);
            foreach (var file in files)
            {
                states.Add(new PromotionState(file));
            }

            try
            {
                for (var index = 0; index < states.Count; index++)
                {
                    var state = states[index];
                    state.FinalInitiallyExisted = File.Exists(state.File.FinalPath);
                    state.BackupInitiallyExisted = File.Exists(state.File.BackupPath);
                    state.TempInitiallyExisted = File.Exists(state.File.TempPath);

                    if (!state.TempInitiallyExisted)
                    {
                        throw new FileNotFoundException("Atomic promotion temp file does not exist.", state.File.TempPath);
                    }

                    hook?.OnPhase(AtomicPromotionPhase.BeforeBackup, state.File);

                    // Preserve a previous backup before replacing it. This keeps
                    // the old valid backup available throughout a failed commit.
                    if (state.FinalInitiallyExisted && state.BackupInitiallyExisted)
                    {
                        state.PreservedBackupPath = state.File.BackupPath + ".preserve." + Guid.NewGuid().ToString("N");
                        MoveWithoutOverwrite(state.File.BackupPath, state.PreservedBackupPath);
                        state.BackupPreserved = true;
                    }

                    if (state.FinalInitiallyExisted)
                    {
                        MoveWithoutOverwrite(state.File.FinalPath, state.File.BackupPath);
                        state.FinalMovedToBackup = true;
                    }

                    hook?.OnPhase(AtomicPromotionPhase.AfterBackup, state.File);
                    hook?.OnPhase(AtomicPromotionPhase.BeforeTempPromotion, state.File);
                    MoveWithoutOverwrite(state.File.TempPath, state.File.FinalPath);
                    state.TempPromoted = true;
                    hook?.OnPhase(AtomicPromotionPhase.AfterTempPromotion, state.File);
                }

            }
            catch (Exception exception)
            {
                var rollbackError = Rollback(states, hook);
                if (rollbackError == null)
                {
                    return AtomicPromotionResult.Failed(exception.Message, true, exception);
                }

                return AtomicPromotionResult.Failed(
                    exception.Message + "; rollback failed: " + rollbackError.Message,
                    false,
                    exception);
            }

            // All temporary files are now authoritative. Backup cleanup is
            // intentionally best-effort: a cleanup failure must never roll an
            // already committed multi-file save back into a mixed pair.
            TryInvokeCleanupHook(hook, AtomicPromotionPhase.BeforeCleanup);
            foreach (var state in states)
            {
                TryDelete(state.FinalMovedToBackup ? state.File.BackupPath : null);
                TryDelete(state.BackupPreserved ? state.PreservedBackupPath : null);
            }

            TryInvokeCleanupHook(hook, AtomicPromotionPhase.AfterCleanup);
            return AtomicPromotionResult.Completed();
        }

        private static void TryInvokeCleanupHook(IAtomicPromotionHook hook, AtomicPromotionPhase phase)
        {
            try
            {
                hook?.OnPhase(phase, null);
            }
            catch
            {
                // Cleanup hooks are diagnostics only after commit.
            }
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // A stale backup is recoverable; the committed final is not.
            }
        }

        private static Exception Rollback(
            IReadOnlyList<PromotionState> states,
            IAtomicPromotionHook hook)
        {
            Exception firstError = null;
            try
            {
                hook?.OnPhase(AtomicPromotionPhase.Rollback, null);
            }
            catch (Exception exception)
            {
                firstError = exception;
            }

            for (var index = states.Count - 1; index >= 0; index--)
            {
                var state = states[index];
                try
                {
                    if (state.TempPromoted && File.Exists(state.File.FinalPath))
                    {
                        File.Delete(state.File.FinalPath);
                    }

                    if (state.FinalMovedToBackup && File.Exists(state.File.BackupPath))
                    {
                        MoveWithoutOverwrite(state.File.BackupPath, state.File.FinalPath);
                    }

                    if (state.BackupPreserved && File.Exists(state.PreservedBackupPath))
                    {
                        if (File.Exists(state.File.BackupPath))
                        {
                            throw new IOException("Cannot restore preserved backup because the backup path is occupied.");
                        }

                        MoveWithoutOverwrite(state.PreservedBackupPath, state.File.BackupPath);
                    }

                    // A temp file not yet promoted is no longer part of a valid
                    // transaction and must not be mistaken for a future save.
                    if (!state.TempPromoted && File.Exists(state.File.TempPath))
                    {
                        File.Delete(state.File.TempPath);
                    }
                }
                catch (Exception exception)
                {
                    firstError ??= exception;
                }
            }

            return firstError;
        }

        private static void MoveWithoutOverwrite(string source, string destination)
        {
            if (File.Exists(destination))
            {
                throw new IOException("Atomic promotion destination already exists: " + destination);
            }

            File.Move(source, destination);
        }

        private static bool TryValidate(
            IReadOnlyList<AtomicPromotionFile> files,
            out string error)
        {
            if (files == null || files.Count == 0)
            {
                error = "atomic-files-empty";
                return false;
            }

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                if (file == null || string.IsNullOrWhiteSpace(file.FinalPath) ||
                    string.IsNullOrWhiteSpace(file.TempPath) || string.IsNullOrWhiteSpace(file.BackupPath))
                {
                    error = "atomic-file-path-empty";
                    return false;
                }

                string finalPath;
                string tempPath;
                string backupPath;
                try
                {
                    finalPath = Path.GetFullPath(file.FinalPath);
                    tempPath = Path.GetFullPath(file.TempPath);
                    backupPath = Path.GetFullPath(file.BackupPath);
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                    return false;
                }

                if (!paths.Add(finalPath) || !paths.Add(tempPath) || !paths.Add(backupPath))
                {
                    error = "atomic-file-path-duplicate";
                    return false;
                }
            }

            error = null;
            return true;
        }

        private sealed class PromotionState
        {
            public PromotionState(AtomicPromotionFile file)
            {
                File = file;
            }

            public AtomicPromotionFile File { get; }
            public bool FinalInitiallyExisted { get; set; }
            public bool BackupInitiallyExisted { get; set; }
            public bool TempInitiallyExisted { get; set; }
            public bool BackupPreserved { get; set; }
            public bool FinalMovedToBackup { get; set; }
            public bool TempPromoted { get; set; }
            public string PreservedBackupPath { get; set; }
        }
    }
}
