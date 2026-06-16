using NUnit.Framework;
using ZeroEngine.TCE.Editor;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceValidationPanelTests
    {
        [Test]
        public void TryGetFocus_TriggerPath_ReturnsTriggerSelection()
        {
            var issue = new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.InvalidField, "triggers[2].Duration", "bad");

            bool focused = TceValidationPanel.TryGetFocus(issue, out TceGraphLane lane, out int index, out string fieldPath);

            Assert.IsTrue(focused);
            Assert.AreEqual(TceGraphLane.Trigger, lane);
            Assert.AreEqual(2, index);
            Assert.AreEqual("Duration", fieldPath);
        }

        [Test]
        public void TryGetFocus_EffectPathWithoutField_ReturnsEffectSelection()
        {
            var issue = new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.NullComponent, "effects[1]", "bad");

            bool focused = TceValidationPanel.TryGetFocus(issue, out TceGraphLane lane, out int index, out string fieldPath);

            Assert.IsTrue(focused);
            Assert.AreEqual(TceGraphLane.Effect, lane);
            Assert.AreEqual(1, index);
            Assert.AreEqual(string.Empty, fieldPath);
        }

        [Test]
        public void TryGetFocus_GraphLevelPath_ReturnsFalse()
        {
            var issue = new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.MissingTrigger, "triggers", "missing");

            bool focused = TceValidationPanel.TryGetFocus(issue, out _, out _, out _);

            Assert.IsFalse(focused);
        }

        [Test]
        public void TryGetFocus_NegativeIndex_ReturnsFalse()
        {
            var issue = new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.InvalidField, "conditions[-1].Duration", "bad");

            bool focused = TceValidationPanel.TryGetFocus(issue, out _, out _, out _);

            Assert.IsFalse(focused);
        }

        [Test]
        public void TryGetFocus_InvalidSuffix_ReturnsFalse()
        {
            var issue = new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.InvalidField, "effects[0]Message", "bad");

            bool focused = TceValidationPanel.TryGetFocus(issue, out _, out _, out _);

            Assert.IsFalse(focused);
        }
    }
}
