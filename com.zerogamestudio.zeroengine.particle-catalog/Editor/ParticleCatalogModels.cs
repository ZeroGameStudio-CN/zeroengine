using System;
using System.Collections.Generic;

namespace ZeroEngine.ParticleCatalog
{
    [Serializable]
    public sealed class ParticleCatalogManifest
    {
        public const int CurrentSchemaVersion = 2;
        public const string CurrentClassifierVersion = "particle-catalog-v2";

        public int schemaVersion = CurrentSchemaVersion;
        public string generatedAtUtc;
        public string classifierVersion = CurrentClassifierVersion;
        public List<ParticleCatalogEntry> entries = new List<ParticleCatalogEntry>();
    }

    [Serializable]
    public sealed class ParticleCatalogEntry
    {
        public string guid;
        public string path;
        public string dependencyHash;
        public int particleSystemCount;
        public int rendererCount;
        public int maxParticles;
        public float maxDuration;
        public bool looping;
        public bool hasTrails;
        public bool usesCollision;
        public bool usesLights;
        public string[] purposes = Array.Empty<string>();
        public string[] elements = Array.Empty<string>();
        public string[] shapes = Array.Empty<string>();
        public string[] motions = Array.Empty<string>();
        public string[] colors = Array.Empty<string>();
        public string[] timings = Array.Empty<string>();
        public string[] styles = Array.Empty<string>();
        public string[] performance = Array.Empty<string>();
        public string summaryZh;
        public string summaryEn;
        public float confidence;
        public string classifiedBy;
        public string classifierModel;
        public string classifierModelDigest;
        public string classifierVersion;
        public string classifiedAtUtc;

        public bool HasAiClassification => string.Equals(classifiedBy, "ollama", StringComparison.OrdinalIgnoreCase) ||
                                           (!string.IsNullOrWhiteSpace(classifiedBy) && classifiedBy.StartsWith("ai:", StringComparison.Ordinal));
        public bool NeedsBilingualBackfill => string.IsNullOrWhiteSpace(summaryZh) || string.IsNullOrWhiteSpace(summaryEn);
        public string DisplaySummary => !string.IsNullOrWhiteSpace(summaryZh) ? summaryZh :
            !string.IsNullOrWhiteSpace(summaryEn) ? summaryEn : PathName;

        private string PathName => string.IsNullOrWhiteSpace(path) ? string.Empty : System.IO.Path.GetFileNameWithoutExtension(path);
    }

    [Serializable]
    public sealed class ParticleAiClassification
    {
        public string[] purposes = Array.Empty<string>();
        public string[] elements = Array.Empty<string>();
        public string[] shapes = Array.Empty<string>();
        public string[] motions = Array.Empty<string>();
        public string[] colors = Array.Empty<string>();
        public string[] timings = Array.Empty<string>();
        public string[] styles = Array.Empty<string>();
        public string[] performance = Array.Empty<string>();
        public string summaryZh;
        public string summaryEn;
        public float confidence;
        [NonSerialized] public string modelDigest;
    }

    [Serializable]
    public sealed class ParticleCatalogAiAnswer
    {
        public string answer;
        public ParticleCatalogRecommendation[] recommendations = Array.Empty<ParticleCatalogRecommendation>();
        public string[] warnings = Array.Empty<string>();
    }

    [Serializable]
    public sealed class ParticleCatalogRecommendation
    {
        public string guid;
        public string path;
        public string role;
        public string reason;
        public int order;
    }

    [Serializable]
    public sealed class ParticleCatalogCandidate
    {
        public string guid;
        public string path;
        public string summaryZh;
        public string summaryEn;
        public string[] purposes;
        public string[] elements;
        public string[] shapes;
        public string[] motions;
        public string[] colors;
        public string[] timings;
        public string[] styles;
        public string[] performance;
        public int particleSystemCount;
        public int rendererCount;
        public int maxParticles;
        public float maxDuration;
        public bool looping;
        public bool hasTrails;
        public bool usesCollision;
        public bool usesLights;
    }
}
