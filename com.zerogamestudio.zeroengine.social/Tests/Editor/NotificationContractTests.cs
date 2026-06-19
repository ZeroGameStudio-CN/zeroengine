using NUnit.Framework;
using ZeroEngine.Notification;

namespace ZeroEngine.Social.Editor.Tests
{
    public sealed class NotificationContractTests
    {
        [Test]
        public void BuilderPopulatesNotificationDataAndCallbacks()
        {
            var clicked = false;
            var closed = false;

            var data = new NotificationBuilder("Server", "Maintenance")
                .SetType(NotificationType.System)
                .SetPriority(NotificationPriority.Critical)
                .SetPosition(NotificationPosition.Center)
                .SetDuration(0f)
                .SetClosable(false)
                .SetCustomData("ops")
                .OnClick(() => clicked = true)
                .OnClose(() => closed = true)
                .Build();

            data.OnClick?.Invoke();
            data.OnClose?.Invoke();

            Assert.AreEqual("Server", data.Title);
            Assert.AreEqual("Maintenance", data.Message);
            Assert.AreEqual(NotificationType.System, data.Type);
            Assert.AreEqual(NotificationPriority.Critical, data.Priority);
            Assert.AreEqual(NotificationPosition.Center, data.Position);
            Assert.IsFalse(data.Closable);
            Assert.IsTrue(data.Clickable);
            Assert.AreEqual("ops", data.CustomData);
            Assert.IsTrue(clicked);
            Assert.IsTrue(closed);
        }

        [Test]
        public void ManualCloseNotificationNeverExpiresByTime()
        {
            var data = new NotificationData("Manual", "Close")
            {
                Duration = 0f,
                ExpireTime = 0f
            };

            Assert.IsFalse(data.IsExpired);
        }

        [Test]
        public void EventFactoriesPreserveNotificationAndType()
        {
            var data = new NotificationData("Quest", "Ready", NotificationType.Quest);

            AssertEvent(NotificationEventArgs.Shown(data), NotificationEventType.Shown, data);
            AssertEvent(NotificationEventArgs.Hidden(data), NotificationEventType.Hidden, data);
            AssertEvent(NotificationEventArgs.Clicked(data), NotificationEventType.Clicked, data);
            AssertEvent(NotificationEventArgs.Expired(data), NotificationEventType.Expired, data);
            AssertEvent(NotificationEventArgs.Queued(data), NotificationEventType.Queued, data);
        }

        private static void AssertEvent(
            NotificationEventArgs args,
            NotificationEventType expectedType,
            NotificationData expectedNotification)
        {
            Assert.AreEqual(expectedType, args.Type);
            Assert.AreSame(expectedNotification, args.Notification);
        }
    }
}
