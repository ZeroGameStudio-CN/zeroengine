using System;
using System.IO;
using NUnit.Framework;

namespace ZGS.DataToolkit.Editor.Tests
{
    [TestFixture]
    public sealed class DataToolkitInspectorSelectionTests
    {
        private const string CompositeInspectorPath =
            "Packages/com.zerogamestudio.zeroengine.data-toolkit/Editor/Inspection/CompositeAssetInspector.cs";

        [Test]
        public void FullInspectorFallback_PrefersUnityNativeEditorBeforeOdinReflection()
        {
            var source = File.ReadAllText(CompositeInspectorPath);

            StringAssert.Contains("private readonly IAssetInspector nativeInspector = new UnityFallbackAssetInspector();", source);
            StringAssert.Contains("private readonly IAssetInspector odinInspector = new OdinReflectionAssetInspector();", source);
            StringAssert.Contains("nativeInspector.CanInspect(asset)", source);
            StringAssert.Contains("odinInspector.CanInspect(asset)", source);

            var nativeIndex = source.IndexOf("nativeInspector.CanInspect(asset)", StringComparison.Ordinal);
            var odinIndex = source.IndexOf("odinInspector.CanInspect(asset)", StringComparison.Ordinal);

            Assert.GreaterOrEqual(nativeIndex, 0);
            Assert.GreaterOrEqual(odinIndex, 0);
            Assert.Less(nativeIndex, odinIndex, "Full inspector fallback must use Unity native Editor.CreateEditor before Odin reflection so project [CustomEditor] implementations are respected.");
        }
    }
}
