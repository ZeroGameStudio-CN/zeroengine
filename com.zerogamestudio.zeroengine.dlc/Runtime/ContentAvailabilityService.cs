namespace ZeroEngine.Dlc
{
    public sealed class ContentAvailabilityService
    {
        private readonly ContentPackCatalog _catalog;
        private readonly IDlcEntitlementService _entitlementService;

        public ContentAvailabilityService(ContentPackCatalog catalog, IDlcEntitlementService entitlementService)
        {
            _catalog = catalog;
            _entitlementService = entitlementService;
        }

        public ContentAvailabilityResult CanUseContent(string contentPackId)
        {
            if (_catalog == null || !_catalog.TryGetContentPack(contentPackId, out var definition))
            {
                return new ContentAvailabilityResult(
                    contentPackId,
                    null,
                    ContentAvailabilityStatus.MissingContentPack);
            }

            if (!definition.RequiresDlc)
            {
                if (!definition.IncludedInBaseGame)
                {
                    return new ContentAvailabilityResult(
                        definition.ContentPackId,
                        definition.RequiredDlcId,
                        ContentAvailabilityStatus.InvalidContentPack);
                }

                return new ContentAvailabilityResult(
                    definition.ContentPackId,
                    definition.RequiredDlcId,
                    ContentAvailabilityStatus.Available);
            }

            var entitlement = _entitlementService?.GetEntitlement(definition.RequiredDlcId) ?? DlcEntitlement.Unavailable;
            if (!entitlement.Owned)
            {
                return new ContentAvailabilityResult(
                    definition.ContentPackId,
                    definition.RequiredDlcId,
                    ContentAvailabilityStatus.DlcNotOwned);
            }

            if (!entitlement.Installed)
            {
                return new ContentAvailabilityResult(
                    definition.ContentPackId,
                    definition.RequiredDlcId,
                    ContentAvailabilityStatus.DlcNotInstalled);
            }

            return new ContentAvailabilityResult(
                definition.ContentPackId,
                definition.RequiredDlcId,
                ContentAvailabilityStatus.Available);
        }
    }
}
