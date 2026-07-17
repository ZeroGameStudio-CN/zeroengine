using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace POB.Extraction
{
    public enum ExtractionProfileLoadFailure
    {
        None = 0,
        IoFailure = 1,
        CorruptData = 2,
        UnsupportedSchema = 3
    }

    public enum ExtractionProfileCommitFailure
    {
        None = 0,
        InvalidDraft = 1,
        ReadOnly = 2,
        PrepareFailed = 3,
        WriteFailed = 4,
        ReadbackFailed = 5,
        ReplaceFailed = 6,
        RevisionConflict = 7
    }

    public enum ExtractionProfileInMemoryCommitFault
    {
        None = 0,
        Prepare = 1,
        Commit = 2,
        Readback = 3
    }

    public enum ExtractionProfileBlobCommitStage
    {
        Prepare = 0,
        Write = 1,
        Readback = 2,
        Replace = 3
    }

    public sealed class ExtractionProfileLoadResult
    {
        private readonly ExtractionProfileSaveData profile;

        private ExtractionProfileLoadResult(
            ExtractionProfileSaveData profile,
            string revision,
            bool isMissing,
            bool isReadOnly,
            ExtractionProfileLoadFailure failure,
            string message)
        {
            this.profile = ExtractionProfileCloneUtility.Clone(profile);
            Revision = string.IsNullOrEmpty(revision) ? ExtractionProfileRevision.Missing : revision;
            IsMissing = isMissing;
            IsReadOnly = isReadOnly;
            Failure = failure;
            Message = message ?? string.Empty;
        }

        public ExtractionProfileSaveData Profile => ExtractionProfileCloneUtility.Clone(profile);
        public string Revision { get; }
        public bool IsMissing { get; }
        public bool IsReadOnly { get; }
        public ExtractionProfileLoadFailure Failure { get; }
        public string Message { get; }
        public bool Success => Failure == ExtractionProfileLoadFailure.None;
        public bool CanCommit => Success && !IsReadOnly;

        public bool TryCreateDraft(out ExtractionProfileDraft draft)
        {
            return TryCreateDraft(profile, out draft);
        }

        public bool TryCreateDraft(ExtractionProfileSaveData source, out ExtractionProfileDraft draft)
        {
            if (!CanCommit)
            {
                draft = null;
                return false;
            }

            draft = new ExtractionProfileDraft(Revision, source);
            return true;
        }

        public static ExtractionProfileLoadResult Loaded(
            ExtractionProfileSaveData profile,
            string revision,
            bool isMissing = false)
        {
            return new ExtractionProfileLoadResult(
                profile,
                revision,
                isMissing,
                false,
                ExtractionProfileLoadFailure.None,
                string.Empty);
        }

        public static ExtractionProfileLoadResult Failed(
            ExtractionProfileSaveData profile,
            string revision,
            ExtractionProfileLoadFailure failure,
            string message,
            bool isReadOnly = true)
        {
            if (failure == ExtractionProfileLoadFailure.None)
                throw new ArgumentException("A failed load requires a failure type.", nameof(failure));

            return new ExtractionProfileLoadResult(
                profile,
                revision,
                false,
                isReadOnly,
                failure,
                message);
        }
    }

    public sealed class ExtractionProfileDraft
    {
        internal ExtractionProfileDraft(string baseRevision, ExtractionProfileSaveData profile)
        {
            BaseRevision = string.IsNullOrEmpty(baseRevision)
                ? ExtractionProfileRevision.Missing
                : baseRevision;
            Profile = ExtractionProfileCloneUtility.Clone(profile);
        }

        public string BaseRevision { get; }
        public ExtractionProfileSaveData Profile { get; }
    }

    public sealed class ExtractionProfileCommitResult
    {
        private ExtractionProfileCommitResult(
            bool success,
            ExtractionProfileCommitFailure failure,
            string message,
            ExtractionProfileLoadResult snapshot)
        {
            Success = success;
            Failure = failure;
            Message = message ?? string.Empty;
            Snapshot = snapshot;
        }

        public bool Success { get; }
        public ExtractionProfileCommitFailure Failure { get; }
        public string Message { get; }
        public ExtractionProfileLoadResult Snapshot { get; }
        public string Revision => Snapshot?.Revision ?? string.Empty;

        public static ExtractionProfileCommitResult Succeeded(ExtractionProfileLoadResult snapshot)
        {
            if (snapshot == null || !snapshot.Success)
                throw new ArgumentException("A successful commit requires a successful snapshot.", nameof(snapshot));

            return new ExtractionProfileCommitResult(
                true,
                ExtractionProfileCommitFailure.None,
                string.Empty,
                snapshot);
        }

        public static ExtractionProfileCommitResult Failed(
            ExtractionProfileCommitFailure failure,
            string message,
            ExtractionProfileLoadResult currentSnapshot = null)
        {
            if (failure == ExtractionProfileCommitFailure.None)
                throw new ArgumentException("A failed commit requires a failure type.", nameof(failure));

            return new ExtractionProfileCommitResult(
                false,
                failure,
                message,
                currentSnapshot);
        }
    }

    public sealed class ExtractionProfileBlobLoadResult
    {
        private ExtractionProfileBlobLoadResult(
            bool success,
            bool found,
            string json,
            string revision,
            ExtractionProfileLoadFailure failure,
            string message)
        {
            Success = success;
            Found = found;
            Json = json;
            Revision = string.IsNullOrEmpty(revision) ? ExtractionProfileRevision.Missing : revision;
            Failure = failure;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public bool Found { get; }
        public string Json { get; }
        public string Revision { get; }
        public ExtractionProfileLoadFailure Failure { get; }
        public string Message { get; }

        public static ExtractionProfileBlobLoadResult Missing()
        {
            return new ExtractionProfileBlobLoadResult(
                true,
                false,
                null,
                ExtractionProfileRevision.Missing,
                ExtractionProfileLoadFailure.None,
                string.Empty);
        }

        public static ExtractionProfileBlobLoadResult Loaded(string json)
        {
            return new ExtractionProfileBlobLoadResult(
                true,
                true,
                json,
                ExtractionProfileRevision.FromJson(json),
                ExtractionProfileLoadFailure.None,
                string.Empty);
        }

        public static ExtractionProfileBlobLoadResult Failed(
            ExtractionProfileLoadFailure failure,
            string message)
        {
            return new ExtractionProfileBlobLoadResult(
                false,
                false,
                null,
                ExtractionProfileRevision.Missing,
                failure,
                message);
        }
    }

    public sealed class ExtractionProfileBlobCommitResult
    {
        private ExtractionProfileBlobCommitResult(
            bool success,
            ExtractionProfileCommitFailure failure,
            string revision,
            string message)
        {
            Success = success;
            Failure = failure;
            Revision = revision ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public ExtractionProfileCommitFailure Failure { get; }
        public string Revision { get; }
        public string Message { get; }

        public static ExtractionProfileBlobCommitResult Succeeded(string revision)
        {
            return new ExtractionProfileBlobCommitResult(
                true,
                ExtractionProfileCommitFailure.None,
                revision,
                string.Empty);
        }

        public static ExtractionProfileBlobCommitResult Failed(
            ExtractionProfileCommitFailure failure,
            string message)
        {
            return new ExtractionProfileBlobCommitResult(
                false,
                failure,
                string.Empty,
                message);
        }
    }

    public static class ExtractionProfileRevision
    {
        public const string Missing = "missing";

        public static string FromJson(string json)
        {
            if (json == null)
                return Missing;

            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
            var builder = new StringBuilder("sha256:", 7 + hash.Length * 2);
            foreach (byte value in hash)
                builder.Append(value.ToString("x2"));
            return builder.ToString();
        }
    }

    internal static class ExtractionProfileCloneUtility
    {
        public static ExtractionProfileSaveData Clone(ExtractionProfileSaveData profile)
        {
            profile ??= ExtractionProfileSaveData.CreateEmpty();
            string json = JsonUtility.ToJson(profile);
            var clone = JsonUtility.FromJson<ExtractionProfileSaveData>(json)
                        ?? ExtractionProfileSaveData.CreateEmpty();
            clone.EnsureInitialized();
            return clone;
        }
    }
}
