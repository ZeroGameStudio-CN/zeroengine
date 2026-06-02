using NUnit.Framework;

namespace ZeroEngine.Timing.Tests
{
    public sealed class TimeControlServiceTests
    {
        [Test]
        public void GetScale_UnknownDomain_ReturnsOne()
        {
            var service = new TimeControlService();

            Assert.That(service.GetScale(TimeDomainIds.Project("pob", "enemy")), Is.EqualTo(1f));
        }

        [Test]
        public void SetBaseScale_ChangesDomainScaleAndRaisesEvent()
        {
            var service = new TimeControlService();
            var domain = TimeDomainIds.Project("pob", "enemy");
            TimeDomainId receivedDomain = default;
            float receivedScale = -1f;

            service.DomainScaleChanged += (changedDomain, scale) =>
            {
                receivedDomain = changedDomain;
                receivedScale = scale;
            };

            service.SetBaseScale(domain, 0.5f);

            Assert.That(service.GetScale(domain), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(receivedDomain, Is.EqualTo(domain));
            Assert.That(receivedScale, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void SetScaleModifier_MultipleTokens_UsesStrongestSlow()
        {
            var service = new TimeControlService();
            var domain = TimeDomainIds.Project("pob", "enemy");
            var tokenA = new object();
            var tokenB = new object();

            service.SetScaleModifier(tokenA, domain, 0.5f);
            service.SetScaleModifier(tokenB, domain, 0.25f);

            Assert.That(service.GetScale(domain), Is.EqualTo(0.25f).Within(0.0001f));

            service.Release(tokenB, domain);

            Assert.That(service.GetScale(domain), Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void Freeze_MultipleHandles_RequiresEveryHandleToRelease()
        {
            var service = new TimeControlService();
            var domain = TimeDomainIds.Project("pob", "projectile");

            var first = service.Freeze(new object(), domain);
            var second = service.Freeze(new object(), domain);

            Assert.That(service.IsFrozen(domain), Is.True);

            first.Release();
            Assert.That(service.IsFrozen(domain), Is.True);

            second.Release();
            Assert.That(service.IsFrozen(domain), Is.False);
            Assert.That(service.GetScale(domain), Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void TimedModifier_TickAutoReleasesAfterUnscaledDuration()
        {
            var service = new TimeControlService();
            var domain = TimeDomainIds.Presentation;

            service.SetScaleModifier(new object(), domain, 0.2f, durationSeconds: 0.5f);
            service.Tick(0.25f);
            Assert.That(service.GetScale(domain), Is.EqualTo(0.2f).Within(0.0001f));

            service.Tick(0.25f);
            Assert.That(service.GetScale(domain), Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void Release_WithRecovery_RampsUpUsingUnscaledTick()
        {
            var service = new TimeControlService();
            var domain = TimeDomainIds.Gameplay;
            var token = new object();

            service.SetScaleModifier(token, domain, 0f);
            service.Release(token, domain, recoveryDuration: 1f);

            service.Tick(0.5f);
            Assert.That(service.GetScale(domain), Is.EqualTo(0.5f).Within(0.0001f));

            service.Tick(0.5f);
            Assert.That(service.GetScale(domain), Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void SetScaleModifier_SameTokenAndDomain_ReplacesPreviousRequest()
        {
            var service = new TimeControlService();
            var domain = TimeDomainIds.Project("p5", "global");
            var token = new object();

            service.SetScaleModifier(token, domain, 0.6f);
            service.SetScaleModifier(token, domain, 0.3f);

            Assert.That(service.GetScale(domain), Is.EqualTo(0.3f).Within(0.0001f));

            service.Release(token, domain);
            Assert.That(service.GetScale(domain), Is.EqualTo(1f).Within(0.0001f));
        }
    }
}
