namespace ZeroEngine.Dlc
{
    public readonly struct DlcEntitlement
    {
        public DlcEntitlement(bool owned, bool installed)
        {
            Owned = owned;
            Installed = installed;
        }

        public bool Owned { get; }
        public bool Installed { get; }
        public bool CanUse => Owned && Installed;

        public static DlcEntitlement Unavailable => new(false, false);
        public static DlcEntitlement OwnedInstalled => new(true, true);
        public static DlcEntitlement OwnedNotInstalled => new(true, false);
    }
}
