using NUnit.Framework;
using ZeroEngine.Formula.Editor;

namespace ZeroEngine.Formula.Tests.Editor
{
    public sealed class FormulaReferenceAssetDatabaseTests
    {
        [Test]
        public void IsSupportedTextAssetPath_AllowsUnityTextAssets()
        {
            Assert.IsTrue(FormulaReferenceAssetDatabase.IsSupportedTextAssetPath("Assets/Data/Formula.asset"));
            Assert.IsTrue(FormulaReferenceAssetDatabase.IsSupportedTextAssetPath("Assets/Data/Scene.unity"));
            Assert.IsTrue(FormulaReferenceAssetDatabase.IsSupportedTextAssetPath("Assets/AddressableAssetsData/Groups.json"));
        }

        [Test]
        public void IsSupportedTextAssetPath_RejectsBinaryAssets()
        {
            Assert.IsFalse(FormulaReferenceAssetDatabase.IsSupportedTextAssetPath("Assets/Art/Icon.png"));
            Assert.IsFalse(FormulaReferenceAssetDatabase.IsSupportedTextAssetPath("Assets/Audio/Hit.wav"));
        }
    }
}
