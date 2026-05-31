using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Dialog;

namespace ZeroEngine.Narrative.Tests.Dialog
{
    [TestFixture]
    public sealed class DialogCommandTests
    {
        [Test]
        public void TryParse_KnownQuestCallback_ReturnsStructuredCommand()
        {
            var parsed = DialogCommandParser.TryParse("quest.accept", "quest.demo.longleji.herb_and_bandit", out var command);

            Assert.IsTrue(parsed);
            Assert.AreEqual("quest.accept", command.CommandId);
            Assert.AreEqual(DialogCommandKind.QuestAccept, command.Kind);
            Assert.AreEqual("quest.demo.longleji.herb_and_bandit", command.TargetId);
            Assert.AreEqual(1, command.Amount);
            Assert.AreEqual("quest.demo.longleji.herb_and_bandit", command.RawParameter);
        }

        [Test]
        public void TryParse_FactAssignment_SplitsFactIdAndValue()
        {
            var parsed = DialogCommandParser.TryParse("fact.set", "world.visible_bandit_defeated=true", out var command);

            Assert.IsTrue(parsed);
            Assert.AreEqual(DialogCommandKind.FactSet, command.Kind);
            Assert.AreEqual("world.visible_bandit_defeated", command.FactId);
            Assert.AreEqual("true", command.Value);
        }

        [Test]
        public void TryParse_UnknownCallback_ReturnsFalse()
        {
            Assert.IsFalse(DialogCommandParser.TryParse("legacy.unparsed", "payload", out _));
        }

        [Test]
        public void DialogRunner_CommandCallback_InvokesExactlyOneDispatchPath()
        {
            var gameObject = new GameObject("DialogRunnerTest");
            try
            {
                var runner = gameObject.AddComponent<DialogRunner>();
                var callbackCount = 0;
                var commandCount = 0;

                runner.OnCallback += (_, _) => callbackCount++;
                runner.OnCommand += _ => commandCount++;

                var handle = typeof(DialogRunner).GetMethod(
                    "HandleCallback",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(handle);

                handle.Invoke(runner, new object[] { "quest.accept", "quest.demo.longleji.herb_and_bandit" });

                Assert.AreEqual(0, callbackCount);
                Assert.AreEqual(1, commandCount);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
