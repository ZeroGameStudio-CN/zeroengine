using NUnit.Framework;
using ZeroEngine.Dlc;

namespace ZeroEngine.Tests.Dlc
{
    [TestFixture]
    public sealed class LocalDlcEntitlementServiceTests
    {
        [Test]
        public void GetEntitlement_UnknownDlc_ReturnsUnavailable()
        {
            var service = new LocalDlcEntitlementService();

            var entitlement = service.GetEntitlement("dlc.unknown");

            Assert.False(entitlement.Owned);
            Assert.False(entitlement.Installed);
            Assert.False(entitlement.CanUse);
        }

        [Test]
        public void SetEntitlement_StoresOwnershipAndInstallState()
        {
            var service = new LocalDlcEntitlementService();

            service.SetEntitlement("dlc.afterfall", new DlcEntitlement(true, false));
            var entitlement = service.GetEntitlement("dlc.afterfall");

            Assert.True(entitlement.Owned);
            Assert.False(entitlement.Installed);
            Assert.False(entitlement.CanUse);
        }

        [Test]
        public void Clear_RemovesStoredEntitlements()
        {
            var service = new LocalDlcEntitlementService();
            service.SetEntitlement("dlc.afterfall", DlcEntitlement.OwnedInstalled);

            service.Clear();
            var entitlement = service.GetEntitlement("dlc.afterfall");

            Assert.False(entitlement.Owned);
            Assert.False(entitlement.Installed);
        }
    }
}
