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

        [Test]
        public void BuildGroups_GroupsItemsByLane()
        {
            var groups = TceComponentPalette.BuildGroups();

            Assert.That(groups.Any(group => group.Lane == TceGraphLane.Trigger && group.Items.Any(item => item.DataType == typeof(OnInstallTriggerData))));
            Assert.That(groups.Any(group => group.Lane == TceGraphLane.Condition && group.Items.Any(item => item.DataType == typeof(NumericSourceConditionData))));
            Assert.That(groups.Any(group => group.Lane == TceGraphLane.Effect && group.Items.Any(item => item.DataType == typeof(DebugLogEffectData))));
        }

        [Test]
        public void Search_EmptyQuery_ReturnsDeterministicLaneThenNameOrder()
        {
            var items = TceComponentPalette.Search(string.Empty);

            Assert.That(items, Is.Ordered.By(nameof(TceComponentPaletteItem.Lane)).Then.By(nameof(TceComponentPaletteItem.DisplayName)));
        }

        [Test]
        public void PaletteItem_LabelIncludesDisplayNameAndLane()
        {
            var item = TceComponentPalette.BuildItems().Single(entry => entry.DataType == typeof(DebugLogEffectData));

            Assert.That(item.Label, Is.EqualTo("Debug Log (Effect)"));
        }
    }
}
