using NUnit.Framework;
using ZeroEngine.Dlc;

namespace ZeroEngine.Tests.Dlc
{
    [TestFixture]
    public sealed class ContentAvailabilityServiceTests
    {
        [Test]
        public void CanUseContent_BaseGamePack_ReturnsAvailable()
        {
            var catalog = ContentPackCatalog.CreateInMemory(new[]
            {
                new ContentPackDefinition("base.story", true, null, "Base Story")
            });
            var entitlements = new LocalDlcEntitlementService();
            var service = new ContentAvailabilityService(catalog, entitlements);

            var result = service.CanUseContent("base.story");

            Assert.True(result.Available);
            Assert.That(result.Status, Is.EqualTo(ContentAvailabilityStatus.Available));
            Assert.That(result.ContentPackId, Is.EqualTo("base.story"));
        }

        [Test]
        public void CanUseContent_DlcPackWithoutOwnership_ReturnsDlcNotOwned()
        {
            var catalog = ContentPackCatalog.CreateInMemory(new[]
            {
                new ContentPackDefinition("chapter.afterfall", false, "dlc.afterfall", "Afterfall")
            });
            var entitlements = new LocalDlcEntitlementService();
            var service = new ContentAvailabilityService(catalog, entitlements);

            var result = service.CanUseContent("chapter.afterfall");

            Assert.False(result.Available);
            Assert.That(result.Status, Is.EqualTo(ContentAvailabilityStatus.DlcNotOwned));
            Assert.That(result.RequiredDlcId, Is.EqualTo("dlc.afterfall"));
        }

        [Test]
        public void CanUseContent_OwnedButNotInstalledDlc_ReturnsDlcNotInstalled()
        {
            var catalog = ContentPackCatalog.CreateInMemory(new[]
            {
                new ContentPackDefinition("chapter.afterfall", false, "dlc.afterfall", "Afterfall")
            });
            var entitlements = new LocalDlcEntitlementService();
            entitlements.SetEntitlement("dlc.afterfall", new DlcEntitlement(true, false));
            var service = new ContentAvailabilityService(catalog, entitlements);

            var result = service.CanUseContent("chapter.afterfall");

            Assert.False(result.Available);
            Assert.That(result.Status, Is.EqualTo(ContentAvailabilityStatus.DlcNotInstalled));
            Assert.That(result.RequiredDlcId, Is.EqualTo("dlc.afterfall"));
        }

        [Test]
        public void CanUseContent_OwnedAndInstalledDlc_ReturnsAvailable()
        {
            var catalog = ContentPackCatalog.CreateInMemory(new[]
            {
                new ContentPackDefinition("chapter.afterfall", false, "dlc.afterfall", "Afterfall")
            });
            var entitlements = new LocalDlcEntitlementService();
            entitlements.SetEntitlement("dlc.afterfall", DlcEntitlement.OwnedInstalled);
            var service = new ContentAvailabilityService(catalog, entitlements);

            var result = service.CanUseContent("chapter.afterfall");

            Assert.True(result.Available);
            Assert.That(result.Status, Is.EqualTo(ContentAvailabilityStatus.Available));
            Assert.That(result.RequiredDlcId, Is.EqualTo("dlc.afterfall"));
        }

        [Test]
        public void CanUseContent_UnknownPack_ReturnsMissingContentPack()
        {
            var catalog = ContentPackCatalog.CreateInMemory(new ContentPackDefinition[0]);
            var service = new ContentAvailabilityService(catalog, new LocalDlcEntitlementService());

            var result = service.CanUseContent("missing.pack");

            Assert.False(result.Available);
            Assert.That(result.Status, Is.EqualTo(ContentAvailabilityStatus.MissingContentPack));
            Assert.That(result.ContentPackId, Is.EqualTo("missing.pack"));
        }

        [Test]
        public void CanUseContent_DlcPackWithoutRequiredDlcId_ReturnsInvalidContentPack()
        {
            var catalog = ContentPackCatalog.CreateInMemory(new[]
            {
                new ContentPackDefinition("chapter.invalid", false, null, "Invalid Chapter")
            });
            var service = new ContentAvailabilityService(catalog, new LocalDlcEntitlementService());

            var result = service.CanUseContent("chapter.invalid");

            Assert.False(result.Available);
            Assert.That(result.Status, Is.EqualTo(ContentAvailabilityStatus.InvalidContentPack));
            Assert.That(result.ContentPackId, Is.EqualTo("chapter.invalid"));
        }
    }
}
