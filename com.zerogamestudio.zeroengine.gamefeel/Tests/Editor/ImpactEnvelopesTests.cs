using NUnit.Framework;

namespace ZeroEngine.GameFeel.Tests
{
    public sealed class ImpactEnvelopesTests
    {
        [Test]
        public void RecoilStartsAndEndsAtRestWithAShorterReturnSwing()
        {
            Assert.That(ImpactEnvelopes.DirectionalRecoil(0f), Is.Zero);
            Assert.That(ImpactEnvelopes.DirectionalRecoil(.16f), Is.EqualTo(1f).Within(.00001f));
            Assert.That(ImpactEnvelopes.DirectionalRecoil(.44f), Is.Zero.Within(.00001f));
            Assert.That(ImpactEnvelopes.DirectionalRecoil(.62f), Is.EqualTo(-.21f).Within(.00001f));
            Assert.That(ImpactEnvelopes.DirectionalRecoil(1f), Is.Zero);
            Assert.That(ImpactEnvelopes.DirectionalRecoil(float.NaN), Is.Zero);
        }

        [Test]
        public void FlashAndCameraImpulseReturnToNeutral()
        {
            Assert.That(ImpactEnvelopes.FlashRemaining(0f), Is.EqualTo(1f));
            Assert.That(ImpactEnvelopes.FlashRemaining(.25f), Is.EqualTo(.421875f));
            Assert.That(ImpactEnvelopes.FlashRemaining(1f), Is.Zero);
            Assert.That(ImpactEnvelopes.CameraImpulse(0f, 1.6f), Is.Zero);
            Assert.That(ImpactEnvelopes.CameraImpulse(.18f, 1.6f), Is.GreaterThan(0f));
            Assert.That(ImpactEnvelopes.CameraImpulse(.45f, 1.6f), Is.LessThan(0f));
            Assert.That(ImpactEnvelopes.CameraImpulse(1f, 1.6f), Is.Zero);
        }
    }
}
