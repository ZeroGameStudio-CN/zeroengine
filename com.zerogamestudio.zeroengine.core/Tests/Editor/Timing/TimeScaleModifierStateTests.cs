using NUnit.Framework;

namespace ZeroEngine.Timing.Tests
{
    public sealed class TimeScaleModifierStateTests
    {
        [Test]
        public void Set_MultipleTokens_UsesLowestScale()
        {
            var state = new TimeScaleModifierState();
            float observed = 1f;

            state.Set(new object(), 0.75f, value => observed = value);
            state.Set(new object(), 0.25f, value => observed = value);

            Assert.That(state.Scale, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(observed, Is.EqualTo(0.25f).Within(0.0001f));
        }

        [Test]
        public void Clear_WithRecovery_RequiresTickToRecover()
        {
            var state = new TimeScaleModifierState();
            var token = new object();
            float observed = 1f;

            state.Set(token, 0f, value => observed = value);
            state.Clear(token, recoveryDuration: 1f, onChanged: value => observed = value);

            state.Tick(0.5f);
            Assert.That(state.Scale, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(observed, Is.EqualTo(0.5f).Within(0.0001f));

            state.Tick(0.5f);
            Assert.That(state.Scale, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void Reset_ClearsModifiersAndScale()
        {
            var state = new TimeScaleModifierState();
            state.Set(new object(), 0.2f, null);

            state.Reset();

            Assert.That(state.Scale, Is.EqualTo(1f).Within(0.0001f));
        }
    }
}
