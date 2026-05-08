using UnityEngine;
using ZeroEngine.Quest;
using ZeroEngine.Quest.Conditions;

namespace ZeroEngine.Samples
{
    /// <summary>
    /// Quest 系统示例
    /// 演示任务接受、进度更新、条件系统和奖励
    /// </summary>
    public class QuestSystemExample : MonoBehaviour
    {
        [Header("Quest Config")]
        [SerializeField] private QuestConfigSO testQuest;

        private void Start()
        {
            if (QuestManager.Instance == null)
            {
                Debug.LogError("[QuestExample] QuestManager not found!");
                return;
            }

            // 监听事件
            QuestManager.Instance.OnConditionProgress += OnConditionProgress;
            QuestManager.Instance.OnConditionCompleted += OnConditionCompleted;

            if (testQuest != null)
            {
                TestQuestOperations();
            }
            else
            {
                Debug.Log("[QuestExample] No test quest assigned.");
            }
        }

        private void TestQuestOperations()
        {
            Debug.Log($"[QuestExample] Testing quest: {testQuest.questId}");

            // 接受任务
            bool accepted = QuestManager.Instance.AcceptQuest(testQuest.questId);
            Debug.Log($"[QuestExample] Quest accepted: {accepted}");

            // 查询状态
            var state = QuestManager.Instance.GetQuestState(testQuest.questId);
            Debug.Log($"[QuestExample] Quest state: {state}");

            TestConditionSystem();
        }

        private void TestConditionSystem()
        {
            // 模拟击杀事件
            QuestManager.Instance.ProcessConditionEvent(QuestEvents.EntityKilled, new ConditionEventData
            {
                TargetId = "slime",
                Amount = 1
            });

            // 查询条件进度
            var progressList = QuestManager.Instance.GetConditionProgress(testQuest.questId);
            foreach (var (condition, current, target, completed) in progressList)
            {
                Debug.Log($"[QuestExample] Condition: {condition.Description}, Progress: {current}/{target}, Completed: {completed}");
            }
        }

        private void OnConditionProgress(string questId, QuestCondition condition)
        {
            Debug.Log($"[QuestExample] Quest {questId} condition progress: {condition.Description}");
        }

        private void OnConditionCompleted(string questId, QuestCondition condition)
        {
            Debug.Log($"[QuestExample] Quest {questId} condition completed: {condition.Description}");
        }

        private void Update()
        {
            if (testQuest == null) return;

            // 按 K 键模拟击杀
            if (Input.GetKeyDown(KeyCode.K))
            {
                QuestManager.Instance.ProcessConditionEvent(QuestEvents.EntityKilled, new ConditionEventData
                {
                    TargetId = "slime",
                    Amount = 1
                });
                Debug.Log("[QuestExample] Simulated kill event");
            }

            // 按 I 键模拟收集
            if (Input.GetKeyDown(KeyCode.I))
            {
                QuestManager.Instance.ProcessConditionEvent(QuestEvents.ItemObtained, new ConditionEventData
                {
                    TargetId = "herb",
                    Amount = 1
                });
                Debug.Log("[QuestExample] Simulated collect event");
            }

            // 按 T 键提交任务
            if (Input.GetKeyDown(KeyCode.T))
            {
                QuestManager.Instance.SubmitQuest(testQuest.questId);
            }

            // 按 X 键放弃任务
            if (Input.GetKeyDown(KeyCode.X))
            {
                QuestManager.Instance.AbandonQuest(testQuest.questId);
            }
        }

        private void OnDestroy()
        {
            if (QuestManager.Instance != null)
            {
                QuestManager.Instance.OnConditionProgress -= OnConditionProgress;
                QuestManager.Instance.OnConditionCompleted -= OnConditionCompleted;
            }
        }
    }
}
