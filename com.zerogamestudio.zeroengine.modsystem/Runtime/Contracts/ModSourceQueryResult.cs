using System;

namespace ZeroEngine.ModSystem
{
    public sealed class ModSourceQueryResult
    {
        private static readonly string[] EmptyFolders = Array.Empty<string>();

        private ModSourceQueryResult(string sourceId, bool succeeded, string[] modFolders, string error)
        {
            SourceId = sourceId ?? string.Empty;
            Succeeded = succeeded;
            ModFolders = modFolders ?? EmptyFolders;
            Error = error ?? string.Empty;
        }

        public string SourceId { get; }
        public bool Succeeded { get; }
        public string[] ModFolders { get; }
        public string Error { get; }

        public static ModSourceQueryResult Success(string sourceId, string[] modFolders)
        {
            return new ModSourceQueryResult(sourceId, true, modFolders, string.Empty);
        }

        public static ModSourceQueryResult Failed(string sourceId, string error)
        {
            return new ModSourceQueryResult(sourceId, false, EmptyFolders, error);
        }
    }
}
