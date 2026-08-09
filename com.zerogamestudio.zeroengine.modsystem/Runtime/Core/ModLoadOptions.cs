using System;
using System.Collections.Generic;

namespace ZeroEngine.ModSystem
{
    public sealed class ModLoadOptions
    {
        private static readonly TimeSpan DefaultSourceQueryTimeout = TimeSpan.FromSeconds(30);

        public string ManifestFileName { get; set; } = "manifest.json";
        public TimeSpan SourceQueryTimeout { get; set; } = DefaultSourceQueryTimeout;
        public ISet<string> DisabledModIds { get; set; }

        public string GetManifestFileName()
        {
            return string.IsNullOrWhiteSpace(ManifestFileName) ? "manifest.json" : ManifestFileName;
        }

        public TimeSpan GetSourceQueryTimeout()
        {
            return SourceQueryTimeout > TimeSpan.Zero ? SourceQueryTimeout : DefaultSourceQueryTimeout;
        }
    }
}
