using System;
using NUnit.Framework;
using ZeroEngine.Save;
using ZeroEngine.Settings;

namespace ZeroEngine.Persistence.Editor.Tests
{
    public sealed class PersistenceContractTests
    {
        [Test]
        public void SaveSystemConfigBuildsSlotAndAutoSaveFileNames()
        {
            var config = new SaveSystemConfig
            {
                SaveFilePrefix = "Profile",
                AutoSaveFileName = "Auto.es3"
            };

            Assert.AreEqual("Profile_3.es3", config.GetSlotFileName(3));
            Assert.AreEqual("Auto.es3", config.GetSlotFileName(-1));
        }

        [Test]
        public void SaveSlotMetaFormatsShortAndLongPlayTime()
        {
            var meta = new SaveSlotMeta
            {
                PlayTimeSeconds = 65f
            };

            Assert.AreEqual("01:05", meta.FormattedPlayTime);

            meta.PlayTimeSeconds = 3661f;

            Assert.AreEqual("1:01:01", meta.FormattedPlayTime);
        }

        [Test]
        public void SaveSlotMetaRoundTripsTimestampTicks()
        {
            var timestamp = new DateTime(2026, 6, 18, 10, 30, 0, DateTimeKind.Utc);
            var meta = new SaveSlotMeta
            {
                Timestamp = timestamp
            };

            Assert.AreEqual(timestamp.Ticks, meta.TimestampTicks);
            Assert.AreEqual(timestamp, meta.Timestamp);
        }

        [Test]
        public void SettingValueConvertsPrimitiveValues()
        {
            var value = new SettingValue();

            value.SetBool(true);
            Assert.IsTrue(value.GetBool());

            value.SetInt(12);
            Assert.AreEqual(12, value.GetInt());

            value.SetFloat(1.235f);
            Assert.AreEqual("1.24", value.StringValue);
            Assert.AreEqual(1.24f, value.GetFloat());

            value.SetString("custom");
            Assert.AreEqual("custom", value.StringValue);
        }

        [Test]
        public void SettingsEventFactoriesPreservePayload()
        {
            var changed = SettingsEventArgs.ValueChanged("volume", "0.5", "0.8", SettingCategory.Audio);
            var reset = SettingsEventArgs.Reset(SettingCategory.Graphics);

            Assert.AreEqual(SettingsEventType.ValueChanged, changed.Type);
            Assert.AreEqual("volume", changed.Key);
            Assert.AreEqual("0.5", changed.OldValue);
            Assert.AreEqual("0.8", changed.NewValue);
            Assert.AreEqual(SettingCategory.Audio, changed.Category);
            Assert.AreEqual(SettingsEventType.Reset, reset.Type);
            Assert.AreEqual(SettingCategory.Graphics, reset.Category);
        }
    }
}
