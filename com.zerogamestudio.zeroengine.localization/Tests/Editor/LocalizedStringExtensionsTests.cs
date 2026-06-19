using NUnit.Framework;

namespace ZeroEngine.Localization.Editor.Tests
{
    public sealed class LocalizedStringExtensionsTests
    {
        [Test]
        public void CacheAndDebugSettingsCanBeReset()
        {
            var previousFormat = LocalizedStringExtensions.MissingKeyFormat;
            var previousDebugMode = LocalizedStringExtensions.DebugMode;
            try
            {
                LocalizedStringExtensions.MissingKeyFormat = "<{0}>";
                LocalizedStringExtensions.DebugMode = true;
                LocalizedStringExtensions.ClearCache();

                Assert.AreEqual("<{0}>", LocalizedStringExtensions.MissingKeyFormat);
                Assert.IsTrue(LocalizedStringExtensions.DebugMode);
            }
            finally
            {
                LocalizedStringExtensions.MissingKeyFormat = previousFormat;
                LocalizedStringExtensions.DebugMode = previousDebugMode;
                LocalizedStringExtensions.ClearCache();
            }
        }

#if !UNITY_LOCALIZATION
        [Test]
        public void FallbackApiReturnsNoLocalizationWhenUnityLocalizationSymbolIsAbsent()
        {
            var placeholder = new object();

            Assert.AreEqual("[NO_LOCALIZATION]", placeholder.GetSafe());
            Assert.AreEqual("[NO_LOCALIZATION]", placeholder.GetSafe("arg"));
            Assert.IsFalse(placeholder.IsValid());
            Assert.IsNull(placeholder.GetKey());
            Assert.IsNull(placeholder.GetTableName());
        }
#endif
    }
}
