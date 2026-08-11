using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.EditorUI.Tests.Editor
{
    public sealed class EditorToolActionContractTests
    {
        [Test]
        public void ProviderAttribute_PreservesStableId()
        {
            var attribute = new EditorToolActionProviderAttribute("zeroengine.formula");

            Assert.AreEqual("zeroengine.formula", attribute.ProviderId);
        }

        [Test]
        public void State_DisabledWithoutReason_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => new EditorToolActionState(false));
        }

        [Test]
        public void State_PreservesAvailabilityCheckAndReason()
        {
            var state = new EditorToolActionState(false, true, "仅在运行模式可用。");

            Assert.IsFalse(state.Enabled);
            Assert.IsTrue(state.IsChecked);
            Assert.AreEqual("仅在运行模式可用。", state.DisabledReason);
        }

        [TestCase(EditorToolActionStatus.Succeeded)]
        [TestCase(EditorToolActionStatus.Cancelled)]
        [TestCase(EditorToolActionStatus.Failed)]
        public void Result_PreservesStatusAndSummary(EditorToolActionStatus status)
        {
            var result = new EditorToolActionResult(status, "执行结果。");

            Assert.AreEqual(status, result.Status);
            Assert.AreEqual("执行结果。", result.Message);
        }

        [Test]
        public void Result_WithoutSummary_IsRejected()
        {
            Assert.Throws<ArgumentException>(() =>
                new EditorToolActionResult(EditorToolActionStatus.Succeeded, string.Empty));
        }

        [Test]
        public void Context_ExposesStableEntryIdentity()
        {
            EditorWindow owner = ScriptableObject.CreateInstance<EditorWindow>();
            try
            {
                var context = new EditorToolActionContext(owner, "com.zerogamestudio.zeroengine", "ability-editor");

                Assert.AreSame(owner, context.Owner);
                Assert.AreEqual("com.zerogamestudio.zeroengine/ability-editor", context.FullId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }
    }
}
