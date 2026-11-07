using System.Collections.Generic;
using NUnit.Framework;
using ZeroEngine.Achievement;

namespace ZeroEngine.Tests.Achievement
{
    [TestFixture]
    public class AchievementConditionTests
    {
        #region CounterCondition Tests

        [Test]
        public void CounterCondition_NotCompleted_WhenBelowTarget()
        {
            var condition = new CounterCondition
            {
                EventId = "Kill",
                TargetCount = 10
            };
            var progress = new AchievementProgress();
            progress.ConditionProgress[0] = 5;

            bool completed = condition.IsCompleted(progress, 0);

            Assert.IsFalse(completed);
        }

        [Test]
        public void CounterCondition_Completed_WhenReachesTarget()
        {
            var condition = new CounterCondition
            {
                EventId = "Kill",
                TargetCount = 10
            };
            var progress = new AchievementProgress();
            progress.ConditionProgress[0] = 10;

            bool completed = condition.IsCompleted(progress, 0);

            Assert.IsTrue(completed);
        }

        [Test]
        public void CounterCondition_Completed_WhenExceedsTarget()
        {
            var condition = new CounterCondition
            {
                EventId = "Kill",
                TargetCount = 10
            };
            var progress = new AchievementProgress();
            progress.ConditionProgress[0] = 15;

            bool completed = condition.IsCompleted(progress, 0);

            Assert.IsTrue(completed);
        }

        [Test]
        public void CounterCondition_GetProgress_ReturnsCorrectRatio()
        {
            var condition = new CounterCondition
            {
                EventId = "Kill",
                TargetCount = 10
            };
            var progress = new AchievementProgress();
            progress.ConditionProgress[0] = 5;

            float progressValue = condition.GetProgress(progress, 0);

            Assert.That(progressValue, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void CounterCondition_GetProgress_ClampsToOne()
        {
            var condition = new CounterCondition
            {
                EventId = "Kill",
                TargetCount = 10
            };
            var progress = new AchievementProgress();
            progress.ConditionProgress[0] = 20; // Exceeds target

            float progressValue = condition.GetProgress(progress, 0);

            Assert.That(progressValue, Is.EqualTo(1.0f).Within(0.0001f));
        }

        [Test]
        public void CounterCondition_GetProgressText_FormatsCorrectly()
        {
            var condition = new CounterCondition
            {
                EventId = "Kill",
                TargetCount = 10
            };
            var progress = new AchievementProgress();
            progress.ConditionProgress[0] = 5;

            string text = condition.GetProgressText(progress, 0);

            Assert.AreEqual("5/10", text);
        }

        [Test]
        public void CounterCondition_ProcessEvent_IncrementsCount()
        {
            var condition = new CounterCondition
            {
                EventId = "Kill",
                TargetCount = 10
            };
            var progress = new AchievementProgress();

            condition.ProcessEvent("Kill", null, progress, 0);

            Assert.AreEqual(1, progress.ConditionProgress[0]);
        }

        [Test]
        public void CounterCondition_ProcessEvent_IgnoresWrongEvent()
        {
            var condition = new CounterCondition
            {
                EventId = "Kill",
                TargetCount = 10
            };
            var progress = new AchievementProgress();

            condition.ProcessEvent("Collect", null, progress, 0);

            Assert.IsFalse(progress.ConditionProgress.ContainsKey(0));
        }

        [Test]
        public void CounterCondition_ProcessEvent_AcceptsIntData()
        {
            var condition = new CounterCondition
            {
                EventId = "Kill",
                TargetCount = 10
            };
            var progress = new AchievementProgress();

            condition.ProcessEvent("Kill", 5, progress, 0);

            Assert.AreEqual(5, progress.ConditionProgress[0]);
        }

        [Test]
        public void CounterCondition_ProcessEvent_CapsAtTarget()
        {
            var condition = new CounterCondition
            {
                EventId = "Kill",
                TargetCount = 10
            };
            var progress = new AchievementProgress();

            condition.ProcessEvent("Kill", 100, progress, 0);

            Assert.AreEqual(10, progress.ConditionProgress[0]);
        }

        #endregion

        #region EventCondition Tests

        [Test]
        public void EventCondition_NotCompleted_Initially()
        {
            var condition = new EventCondition
            {
                EventId = "BossDefeated"
            };
            var progress = new AchievementProgress();

            bool completed = condition.IsCompleted(progress, 0);

            Assert.IsFalse(completed);
        }

        [Test]
        public void EventCondition_Completed_AfterEvent()
        {
            var condition = new EventCondition
            {
                EventId = "BossDefeated"
            };
            var progress = new AchievementProgress();

            condition.ProcessEvent("BossDefeated", null, progress, 0);

            Assert.IsTrue(condition.IsCompleted(progress, 0));
        }

        [Test]
        public void EventCondition_GetProgress_Returns0Or1()
        {
            var condition = new EventCondition
            {
                EventId = "BossDefeated"
            };
            var progress = new AchievementProgress();

            Assert.AreEqual(0f, condition.GetProgress(progress, 0));

            condition.ProcessEvent("BossDefeated", null, progress, 0);

            Assert.AreEqual(1f, condition.GetProgress(progress, 0));
        }

        [Test]
        public void EventCondition_WithRequiredData_MatchesExactly()
        {
            var condition = new EventCondition
            {
                EventId = "AreaReached",
                RequireData = true,
                RequiredDataValue = "Dungeon1"
            };
            var progress = new AchievementProgress();

            condition.ProcessEvent("AreaReached", "Dungeon2", progress, 0);
            Assert.IsFalse(condition.IsCompleted(progress, 0));

            condition.ProcessEvent("AreaReached", "Dungeon1", progress, 0);
            Assert.IsTrue(condition.IsCompleted(progress, 0));
        }

        #endregion

        #region CompositeAchievementCondition Tests

        [Test]
        public void CompositeCondition_And_AllCompleted_ReturnsTrue()
        {
            var condition = new CompositeAchievementCondition
            {
                Operator = CompositeAchievementCondition.LogicalOperator.And,
                SubConditions = new AchievementCondition[]
                {
                    new CounterCondition { EventId = "Kill", TargetCount = 5 },
                    new CounterCondition { EventId = "Collect", TargetCount = 3 }
                }
            };
            var progress = new AchievementProgress();
            progress.ConditionProgress[0] = 5;  // Sub condition 0 at index 0*100+0
            progress.ConditionProgress[1] = 3;  // Sub condition 1 at index 0*100+1

            bool completed = condition.IsCompleted(progress, 0);

            Assert.IsTrue(completed);
        }

        [Test]
        public void CompositeCondition_And_OneIncomplete_ReturnsFalse()
        {
            var condition = new CompositeAchievementCondition
            {
                Operator = CompositeAchievementCondition.LogicalOperator.And,
                SubConditions = new AchievementCondition[]
                {
                    new CounterCondition { EventId = "Kill", TargetCount = 5 },
                    new CounterCondition { EventId = "Collect", TargetCount = 3 }
                }
            };
            var progress = new AchievementProgress();
            progress.ConditionProgress[0] = 5;  // Kill completed
            progress.ConditionProgress[1] = 1;  // Collect not completed

            bool completed = condition.IsCompleted(progress, 0);

            Assert.IsFalse(completed);
        }

        [Test]
        public void CompositeCondition_Or_OneCompleted_ReturnsTrue()
        {
            var condition = new CompositeAchievementCondition
            {
                Operator = CompositeAchievementCondition.LogicalOperator.Or,
                SubConditions = new AchievementCondition[]
                {
                    new CounterCondition { EventId = "Kill", TargetCount = 5 },
                    new CounterCondition { EventId = "Collect", TargetCount = 3 }
                }
            };
            var progress = new AchievementProgress();
            progress.ConditionProgress[0] = 5;  // Kill completed
            progress.ConditionProgress[1] = 0;  // Collect not completed

            bool completed = condition.IsCompleted(progress, 0);

            Assert.IsTrue(completed);
        }

        [Test]
        public void CompositeCondition_Or_NoneCompleted_ReturnsFalse()
        {
            var condition = new CompositeAchievementCondition
            {
                Operator = CompositeAchievementCondition.LogicalOperator.Or,
                SubConditions = new AchievementCondition[]
                {
                    new CounterCondition { EventId = "Kill", TargetCount = 5 },
                    new CounterCondition { EventId = "Collect", TargetCount = 3 }
                }
            };
            var progress = new AchievementProgress();

            bool completed = condition.IsCompleted(progress, 0);

            Assert.IsFalse(completed);
        }

        [Test]
        public void CompositeCondition_GetProgressText_ShowsCompletedCount()
        {
            var condition = new CompositeAchievementCondition
            {
                Operator = CompositeAchievementCondition.LogicalOperator.And,
                SubConditions = new AchievementCondition[]
                {
                    new CounterCondition { EventId = "Kill", TargetCount = 5 },
                    new CounterCondition { EventId = "Collect", TargetCount = 3 }
                }
            };
            var progress = new AchievementProgress();
            progress.ConditionProgress[0] = 5;  // Kill completed

            string text = condition.GetProgressText(progress, 0);

            Assert.AreEqual("1/2", text);
        }

        #endregion

        #region AchievementProgress Tests

        [Test]
        public void AchievementProgress_DefaultState_IsLocked()
        {
            var progress = new AchievementProgress();

            Assert.AreEqual(AchievementState.Locked, progress.State);
        }

        [Test]
        public void AchievementProgress_ConditionProgress_IsInitialized()
        {
            var progress = new AchievementProgress();

            Assert.IsNotNull(progress.ConditionProgress);
        }

        [Test]
        public void AchievementProgress_CanStoreMultipleConditions()
        {
            var progress = new AchievementProgress();
            progress.ConditionProgress[0] = 10;
            progress.ConditionProgress[1] = 20;
            progress.ConditionProgress[2] = 30;

            Assert.AreEqual(10, progress.ConditionProgress[0]);
            Assert.AreEqual(20, progress.ConditionProgress[1]);
            Assert.AreEqual(30, progress.ConditionProgress[2]);
        }

        #endregion

        #region AchievementState Tests

        [Test]
        public void AchievementState_HasCorrectValues()
        {
            Assert.AreEqual(0, (int)AchievementState.Locked);
            Assert.AreEqual(1, (int)AchievementState.InProgress);
            Assert.AreEqual(2, (int)AchievementState.Completed);
            Assert.AreEqual(3, (int)AchievementState.Claimed);
        }

        #endregion

        #region AchievementCategory Tests

        [Test]
        public void AchievementCategory_HasExpectedValues()
        {
            Assert.IsTrue(System.Enum.IsDefined(typeof(AchievementCategory), AchievementCategory.Combat));
            Assert.IsTrue(System.Enum.IsDefined(typeof(AchievementCategory), AchievementCategory.Collection));
            Assert.IsTrue(System.Enum.IsDefined(typeof(AchievementCategory), AchievementCategory.Exploration));
            Assert.IsTrue(System.Enum.IsDefined(typeof(AchievementCategory), AchievementCategory.Social));
            Assert.IsTrue(System.Enum.IsDefined(typeof(AchievementCategory), AchievementCategory.Story));
            Assert.IsTrue(System.Enum.IsDefined(typeof(AchievementCategory), AchievementCategory.Crafting));
            Assert.IsTrue(System.Enum.IsDefined(typeof(AchievementCategory), AchievementCategory.Hidden));
        }

        #endregion
    }

    [TestFixture]
    public class AchievementRewardTests
    {
        #region AchievementPointReward Tests

        [Test]
        public void AchievementPointReward_Description_FormatsCorrectly()
        {
            var reward = new AchievementPointReward { Points = 100 };

            Assert.AreEqual("成就点数 +100", reward.Description);
        }

        [Test]
        public void AchievementPointReward_ZeroPoints_DoesNotGrant()
        {
            var reward = new AchievementPointReward { Points = 0 };

            // Grant should not throw, just do nothing
            Assert.DoesNotThrow(() => reward.Grant());
        }

        #endregion

        #region CompositeReward Tests

        [Test]
        public void CompositeReward_EmptySubRewards_DescriptionIsEmpty()
        {
            var reward = new CompositeReward { SubRewards = null };

            Assert.AreEqual("无奖励", reward.Description);
        }

        [Test]
        public void CompositeReward_Description_CombinesAll()
        {
            var reward = new CompositeReward
            {
                SubRewards = new AchievementReward[]
                {
                    new AchievementPointReward { Points = 10 },
                    new AchievementPointReward { Points = 20 }
                }
            };

            string desc = reward.Description;

            Assert.IsTrue(desc.Contains("成就点数 +10"));
            Assert.IsTrue(desc.Contains("成就点数 +20"));
        }

        #endregion

        #region UnlockReward Tests

        [Test]
        public void UnlockReward_CallsRegisteredCallback()
        {
            bool callbackCalled = false;
            UnlockReward.RegisterUnlockCallback("test_unlock", () => callbackCalled = true);

            var reward = new UnlockReward
            {
                Type = UnlockType.Feature,
                UnlockId = "test_unlock"
            };

            reward.Grant();

            Assert.IsTrue(callbackCalled);

            // Cleanup
            UnlockReward.UnregisterUnlockCallback("test_unlock");
        }

        [Test]
        public void UnlockReward_NoCallback_DoesNotThrow()
        {
            var reward = new UnlockReward
            {
                UnlockId = "nonexistent_unlock"
            };

            Assert.DoesNotThrow(() => reward.Grant());
        }

        [Test]
        public void UnlockReward_Description_UsesCustomDescription()
        {
            var reward = new UnlockReward
            {
                UnlockId = "feature_1",
                UnlockDescription = "解锁新功能"
            };

            Assert.AreEqual("解锁新功能", reward.Description);
        }

        [Test]
        public void UnlockReward_Description_FallbackToId()
        {
            var reward = new UnlockReward
            {
                UnlockId = "feature_1",
                UnlockDescription = ""
            };

            Assert.AreEqual("解锁: feature_1", reward.Description);
        }

        #endregion

        #region CustomAchievementReward Tests

        [Test]
        public void CustomReward_CallsRegisteredHandler()
        {
            string receivedData = null;
            CustomAchievementReward.RegisterCustomHandler("test_custom",
                data => receivedData = data);

            var reward = new CustomAchievementReward
            {
                RewardId = "test_custom",
                CustomData = "test_data_123"
            };

            reward.Grant();

            Assert.AreEqual("test_data_123", receivedData);

            // Cleanup
            CustomAchievementReward.UnregisterCustomHandler("test_custom");
        }

        #endregion
    }
}
