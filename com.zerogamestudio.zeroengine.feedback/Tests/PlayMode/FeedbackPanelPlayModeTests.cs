using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace ZeroEngine.Feedback.Tests.PlayMode
{
    public sealed class FeedbackPanelPlayModeTests
    {
        [UnityTest]
        public IEnumerator Open_WithExistingEventSystem_CreatesAndClosesDefaultView()
        {
            var eventSystem = new GameObject("Feedback Test EventSystem", typeof(EventSystem));
            FeedbackPanel.Configure(new FeedbackUiConfiguration());

            FeedbackPanel.Open();
            yield return null;

            DefaultFeedbackPanelView view = Object.FindObjectOfType<DefaultFeedbackPanelView>();
            Assert.IsNotNull(view);
            Assert.IsFalse(view.IsSubmitting);
            Assert.IsNotNull(view.GetComponentInParent<Canvas>());

            FeedbackPanel.Close();
            yield return null;

            Assert.IsNull(Object.FindObjectOfType<DefaultFeedbackPanelView>());
            Object.Destroy(eventSystem);
        }
    }
}
