namespace ZeroEngine.TCE
{
    public static class TceValidationCodes
    {
        public const string NullGraph = "TCE_GRAPH_NULL";
        public const string MissingTrigger = "TCE_GRAPH_MISSING_TRIGGER";
        public const string MissingEffect = "TCE_GRAPH_MISSING_EFFECT";
        public const string NullComponent = "TCE_COMPONENT_NULL";
        public const string RuntimeTypeMissing = "TCE_COMPONENT_RUNTIME_TYPE_MISSING";
        public const string RuntimeTypeMismatch = "TCE_COMPONENT_RUNTIME_TYPE_MISMATCH";
        public const string InvalidField = "TCE_COMPONENT_INVALID_FIELD";
        public const string InvalidEnumValue = "TCE_COMPONENT_INVALID_ENUM_VALUE";
        public const string GraphMigrationRequired = "TCE_GRAPH_MIGRATION_REQUIRED";
        public const string GraphVersionUnsupported = "TCE_GRAPH_VERSION_UNSUPPORTED";
        public const string GraphMigrationFailed = "TCE_GRAPH_MIGRATION_FAILED";
        public const string GraphFormatUnsupported = "TCE_GRAPH_FORMAT_UNSUPPORTED";
        public const string UnsupportedComponent = "TCE_COMPONENT_UNSUPPORTED";
        public const string DuplicateGraphId = "TCE_GRAPH_DUPLICATE_ID";
    }
}
