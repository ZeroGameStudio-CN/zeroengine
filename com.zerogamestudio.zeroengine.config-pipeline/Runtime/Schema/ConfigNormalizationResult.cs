using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ZeroGameStudio.ConfigPipeline
{
    public sealed class ConfigNormalizationResult
    {
        private readonly ReadOnlyCollection<ConfigDiagnostic> diagnostics;

        internal ConfigNormalizationResult(
            ConfigDocument document,
            IEnumerable<ConfigDiagnostic> diagnostics)
        {
            Document = document;
            this.diagnostics = new List<ConfigDiagnostic>(
                diagnostics ?? Array.Empty<ConfigDiagnostic>()).AsReadOnly();
        }

        public ConfigDocument Document { get; }

        public IReadOnlyList<ConfigDiagnostic> Diagnostics => diagnostics;

        public bool IsValid => Document != null &&
                               diagnostics.All(
                                   diagnostic => diagnostic.Severity != ConfigDiagnosticSeverity.Error);
    }
}
