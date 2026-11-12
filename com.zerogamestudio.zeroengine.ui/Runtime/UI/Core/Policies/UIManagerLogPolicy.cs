namespace ZeroEngine.UI
{
    public enum UIManagerLogLevel
    {
        None = 0,
        Warning = 1,
        Error = 2,
        Info = 3
    }

    public readonly struct UIManagerLogDecision
    {
        private readonly string _message;

        public UIManagerLogDecision(UIManagerLogLevel level, string message = null)
        {
            Level = level;
            _message = message ?? string.Empty;
        }

        public UIManagerLogLevel Level { get; }
        public string Message => _message ?? string.Empty;
        public bool ShouldLog => Level != UIManagerLogLevel.None;
    }

    public static class UIManagerLogPolicy
    {
        public static UIManagerLogDecision ViewNameEmpty() => Error("View name is empty!");
        public static UIManagerLogDecision ViewAlreadyRegistered(string viewName) =>
            Warning($"View '{viewName}' already registered, overwriting...");
        public static UIManagerLogDecision ViewConfigNotFound(string viewName) =>
            Error($"View config not found: {viewName}");
        public static UIManagerLogDecision SingletonViewAlreadyOpen(string viewName) =>
            Warning($"Singleton view '{viewName}' is already open");
        public static UIManagerLogDecision ViewNotFound(string viewName) =>
            Warning($"View not found: {viewName}");
        public static UIManagerLogDecision ViewPrefabLoadFailed(string viewName) =>
            Error($"Failed to load view prefab for: {viewName}");
        public static UIManagerLogDecision ViewComponentNotFound(string viewName) =>
            Error($"View component not found on: {viewName}");
        public static UIManagerLogDecision AddressablesLoadFailed(string runtimeKey, string exceptionMessage) =>
            Error($"Addressables load failed for: {runtimeKey}, {exceptionMessage}");
        public static UIManagerLogDecision AddressablesLoadFailed(string runtimeKey) =>
            Error($"Addressables load failed for: {runtimeKey}");
        public static UIManagerLogDecision Info(string message) =>
            new UIManagerLogDecision(UIManagerLogLevel.Info, message);

        private static UIManagerLogDecision Warning(string message) =>
            new UIManagerLogDecision(UIManagerLogLevel.Warning, message);
        private static UIManagerLogDecision Error(string message) =>
            new UIManagerLogDecision(UIManagerLogLevel.Error, message);
    }
}
