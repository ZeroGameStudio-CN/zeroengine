using NUnit.Framework;
using ZeroEngine.TCE.EditorTesting;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceAdapterContractTests
    {
        [Test]
        public void TceAdapterContractAssertions_ValidFixture_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => TceAdapterContractAssertions.AssertCoreAdapterContract(new ValidFixture()));
        }

        [Test]
        public void TceAdapterContractAssertions_DeadActorFixture_ThrowsLivenessAssertion()
        {
            Assert.Throws<AssertionException>(() => TceAdapterContractAssertions.AssertCoreAdapterContract(new DeadActorFixture()));
        }

        [Test]
        public void TceAdapterContractAssertions_ClockFixture_ThrowsClockAssertion()
        {
            Assert.Throws<AssertionException>(() => TceAdapterContractAssertions.AssertCoreAdapterContract(new ClockFixture()));
        }

        [Test]
        public void TceAdapterContractAssertions_RuntimeLifecycleFixture_VerifiesUninstall()
        {
            Assert.DoesNotThrow(() => TceAdapterContractAssertions.AssertCoreAdapterContract(new ValidFixture()));
        }

        private class ValidFixture : ITceAdapterContractFixture
        {
            public virtual ITceActor CreateAliveActor()
            {
                return new TestActor { IsAlive = true };
            }

            public virtual ITceActor CreateDeadActor()
            {
                return new TestActor { IsAlive = false };
            }

            public virtual ITceClock CreateClock(float initialTime)
            {
                return new TestClock { Now = initialTime };
            }

            public virtual void SetClockTime(ITceClock clock, float time)
            {
                ((TestClock)clock).Now = time;
            }
        }

        private sealed class DeadActorFixture : ValidFixture
        {
            public override ITceActor CreateDeadActor()
            {
                return new TestActor { IsAlive = true };
            }
        }

        private sealed class ClockFixture : ValidFixture
        {
            public override void SetClockTime(ITceClock clock, float time)
            {
            }
        }

        private sealed class TestActor : ITceActor
        {
            public bool IsAlive { get; set; }
            public float DomainTime => 0f;
            public object NativeObject => this;
        }

        private sealed class TestClock : ITceClock
        {
            public float Now { get; set; }
        }
    }
}
