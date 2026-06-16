using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZeroEngine.UI.Combat;

namespace ZeroEngine.UI.Tests.Editor.Combat
{
    public sealed class CombatResultSummaryViewTests
    {
        [Test]
        public void Show_RendersSummaryRewardsGrowthAndConfirm()
        {
            var root = new GameObject("CombatResult", typeof(RectTransform));
            try
            {
                var view = root.AddComponent<CombatResultSummaryView>();
                var title = CreateText(root.transform, "Title");
                var subtitle = CreateText(root.transform, "Subtitle");
                var summary = CreateContainer(root.transform, "Summary");
                var rewards = CreateContainer(root.transform, "Rewards");
                var growth = CreateContainer(root.transform, "Growth");
                var tags = CreateContainer(root.transform, "Tags");
                var emptyReward = CreateText(root.transform, "EmptyReward");
                var confirm = CreateButton(root.transform, "Confirm");
                var rowPrefab = CreateRowPrefab("Row");
                var tagPrefab = CreateTextPrefab("Tag");

                view.ConfigureForRuntime(title, subtitle, summary, rewards, growth, tags, rowPrefab, tagPrefab, emptyReward, confirm);

                var report = new CombatResultReport
                {
                    Result = CombatResultType.Victory,
                    Title = "战斗胜利",
                    Subtitle = "战斗总结"
                };
                report.Summary.Add(new CombatResultLine("回合", "3"));
                report.Summary.Add(new CombatResultLine("击败", "2"));
                report.Tags.Add("无伤");
                report.Rewards.Add(new CombatResultLine("经验", "+120"));
                report.Growth.Add(new CombatResultLine("Test Hero", "+120 EXP"));

                var confirmed = false;
                view.OnConfirm += () => confirmed = true;
                view.Show(report);
                confirm.onClick.Invoke();

                Assert.That(title.text, Is.EqualTo("战斗胜利"));
                Assert.That(subtitle.text, Is.EqualTo("战斗总结"));
                Assert.That(summary.childCount, Is.EqualTo(2));
                Assert.That(rewards.childCount, Is.EqualTo(1));
                Assert.That(growth.childCount, Is.EqualTo(1));
                Assert.That(tags.childCount, Is.EqualTo(1));
                Assert.False(emptyReward.gameObject.activeSelf);
                Assert.True(confirmed);
                Assert.True(view.IsVisible);

                Object.DestroyImmediate(rowPrefab);
                Object.DestroyImmediate(tagPrefab);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Show_EmptyVictoryRewardsDisplaysStableEmptyState()
        {
            var root = new GameObject("CombatResult", typeof(RectTransform));
            try
            {
                var view = root.AddComponent<CombatResultSummaryView>();
                var title = CreateText(root.transform, "Title");
                var subtitle = CreateText(root.transform, "Subtitle");
                var summary = CreateContainer(root.transform, "Summary");
                var rewards = CreateContainer(root.transform, "Rewards");
                var growth = CreateContainer(root.transform, "Growth");
                var tags = CreateContainer(root.transform, "Tags");
                var emptyReward = CreateText(root.transform, "EmptyReward");
                var confirm = CreateButton(root.transform, "Confirm");
                var rowPrefab = CreateRowPrefab("Row");
                var tagPrefab = CreateTextPrefab("Tag");

                view.ConfigureForRuntime(title, subtitle, summary, rewards, growth, tags, rowPrefab, tagPrefab, emptyReward, confirm);

                view.Show(new CombatResultReport
                {
                    Result = CombatResultType.Victory,
                    Title = "战斗胜利",
                    Subtitle = "战斗总结",
                    EmptyRewardText = "无奖励"
                });

                Assert.True(emptyReward.gameObject.activeSelf);
                Assert.That(emptyReward.text, Is.EqualTo("无奖励"));
                Assert.That(rewards.childCount, Is.EqualTo(0));
                Assert.That(growth.childCount, Is.EqualTo(0));

                Object.DestroyImmediate(rowPrefab);
                Object.DestroyImmediate(tagPrefab);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Show_NonVictoryDoesNotHideSharedParentWhenNoSectionRootIsConfigured()
        {
            var root = new GameObject("CombatResult", typeof(RectTransform));
            try
            {
                var view = root.AddComponent<CombatResultSummaryView>();
                var title = CreateText(root.transform, "Title");
                var subtitle = CreateText(root.transform, "Subtitle");
                var sharedParent = CreateContainer(root.transform, "SharedParent");
                var summary = CreateContainer(sharedParent, "Summary");
                var rewards = CreateContainer(sharedParent, "Rewards");
                var growth = CreateContainer(sharedParent, "Growth");
                var tags = CreateContainer(root.transform, "Tags");
                var emptyReward = CreateText(root.transform, "EmptyReward");
                var confirm = CreateButton(root.transform, "Confirm");
                var rowPrefab = CreateRowPrefab("Row");
                var tagPrefab = CreateTextPrefab("Tag");

                view.ConfigureForRuntime(title, subtitle, summary, rewards, growth, tags, rowPrefab, tagPrefab, emptyReward, confirm);
                view.Show(new CombatResultReport
                {
                    Result = CombatResultType.Defeat,
                    Title = "战斗失败",
                    Subtitle = "战斗总结"
                });

                Assert.True(sharedParent.gameObject.activeSelf, "A generic view must not infer and hide a shared parent.");
                Assert.False(rewards.gameObject.activeSelf);
                Assert.False(growth.gameObject.activeSelf);

                Object.DestroyImmediate(rowPrefab);
                Object.DestroyImmediate(tagPrefab);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static RectTransform CreateContainer(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<TextMeshProUGUI>();
        }

        private static Button CreateButton(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            return go.GetComponent<Button>();
        }

        private static GameObject CreateRowPrefab(string name)
        {
            var row = new GameObject(name, typeof(RectTransform));
            CreateText(row.transform, "Label");
            CreateText(row.transform, "Value");
            return row;
        }

        private static GameObject CreateTextPrefab(string name)
        {
            var tag = new GameObject(name, typeof(RectTransform));
            tag.AddComponent<TextMeshProUGUI>();
            return tag;
        }
    }
}
