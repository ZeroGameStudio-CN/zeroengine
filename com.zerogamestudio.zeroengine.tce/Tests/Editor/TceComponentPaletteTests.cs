using System.Linq;
using NUnit.Framework;
using ZeroEngine.TCE.Editor;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceComponentPaletteTests
    {
        [Test]
        public void Build_IncludesCatalogComponents()
        {
            var items = TceComponentPalette.BuildItems();

            Assert.That(items.Any(item => item.DataType == typeof(OnInstallTriggerData)));
            Assert.That(items.Any(item => item.DataType == typeof(NumericSourceConditionData)));
            Assert.That(items.Any(item => item.DataType == typeof(DebugLogEffectData)));
        }

        [Test]
        public void CreateData_ReturnsSelectedComponentData()
        {
            var item = TceComponentPalette.BuildItems().Single(entry => entry.DataType == typeof(DebugLogEffectData));

            TceComponentData data = TceComponentPalette.CreateData(item);

            Assert.IsInstanceOf<DebugLogEffectData>(data);
        }
    }
}
