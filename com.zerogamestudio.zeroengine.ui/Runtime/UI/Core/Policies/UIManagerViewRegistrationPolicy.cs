namespace ZeroEngine.UI
{
    public readonly struct UIManagerViewRegistrationDecision
    {
        public UIManagerViewRegistrationDecision(
            bool logViewNameEmpty,
            bool logViewAlreadyRegistered,
            bool storeConfig,
            bool returnAfterEmptyName)
        {
            LogViewNameEmpty = logViewNameEmpty;
            LogViewAlreadyRegistered = logViewAlreadyRegistered;
            StoreConfig = storeConfig;
            ReturnAfterEmptyName = returnAfterEmptyName;
        }

        public bool LogViewNameEmpty { get; }
        public bool LogViewAlreadyRegistered { get; }
        public bool StoreConfig { get; }
        public bool ReturnAfterEmptyName { get; }
    }

    public static class UIManagerViewRegistrationPolicy
    {
        public static UIManagerViewRegistrationDecision Resolve(bool viewNameIsEmpty, bool alreadyRegistered)
        {
            if (viewNameIsEmpty)
            {
                return new UIManagerViewRegistrationDecision(true, false, false, true);
            }

            return new UIManagerViewRegistrationDecision(false, alreadyRegistered, true, false);
        }
    }
}
