using System;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.PlayerSettings.Tests
{
    public sealed class DisplayPreviewTests
    {
        [Test]
        public void Timeout_RestoresCapturedDisplayState()
        {
            var original = new DisplayState(FullScreenMode.Windowed, 1280, 720, 60, 0, 120, "Low");
            var driver = new FakeDriver(original);
            var confirmation = new DisplayPreviewConfirmation(
                new DisplaySettingsApplier(driver), TimeSpan.FromSeconds(15));
            var now = DateTimeOffset.UtcNow;

            confirmation.Begin(now);
            driver.State = new DisplayState(FullScreenMode.FullScreenWindow, 1920, 1080, 0, 1, -1, "High");

            Assert.That(confirmation.Update(now.AddSeconds(14)), Is.False);
            Assert.That(confirmation.Update(now.AddSeconds(15)), Is.True);
            Assert.That(driver.State.Width, Is.EqualTo(1280));
            Assert.That(driver.State.QualityName, Is.EqualTo("Low"));
        }

        private sealed class FakeDriver : IDisplaySettingsDriver
        {
            public FakeDriver(DisplayState state) => State = state;
            public DisplayState State { get; set; }
            public DisplayState Capture() => State;

            public SettingApplyResult Apply(DisplayState state)
            {
                State = state;
                return SettingApplyResult.Applied();
            }
        }
    }
}
