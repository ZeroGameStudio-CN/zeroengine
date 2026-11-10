using System;
using UnityEngine;

namespace ZeroEngine.Tutorial
{
    /// <summary>
    /// 教程条件基类 (v1.14.0+)
    /// 使用 [SerializeReference] 实现多态序列化
    /// </summary>
    [Serializable]
    public abstract class TutorialCondition
    {
        /// <summary>条件类型名称</summary>
        public abstract string ConditionType { get; }

        /// <summary>
        /// 检查条件是否满足
        /// </summary>
        public abstract bool IsSatisfied(TutorialContext ctx);

        /// <summary>
        /// 获取条件描述
        /// </summary>
        public virtual string GetDescription()
        {
            return ConditionType;
        }
    }

    /// <summary>
    /// 首次进入条件 (v1.14.0+)
    /// </summary>
    [Serializable]
    public class FirstTimeCondition : TutorialCondition
    {
        [Tooltip("检查的键 (如 'scene_entered', 'first_login')")]
        public string Key;

        public override string ConditionType => "FirstTime";

        public override bool IsSatisfied(TutorialContext ctx)
        {
            if (string.IsNullOrEmpty(Key)) return true;

            // 检查是否首次
            return ctx?.Manager && !ctx.Manager.HasTriggered(Key);
        }

        public override string GetDescription()
        {
            return $"First time: {Key}";
        }
    }

    /// <summary>
    /// 等级条件 (v1.14.0+)
    /// </summary>
    [Serializable]
    public class LevelCondition : TutorialCondition
    {
        public string StateKey = "level";
        [Tooltip("最低等级")]
        public int MinLevel = 1;

        [Tooltip("最高等级 (0 表示无限制)")]
        public int MaxLevel = 0;

        public override string ConditionType => "Level";

        public override bool IsSatisfied(TutorialContext ctx)
        {
            if (ctx == null || !ctx.TryReadNumber(StateKey, out var playerLevel)) return false;

            if (playerLevel < MinLevel) return false;
            if (MaxLevel > 0 && playerLevel > MaxLevel) return false;

            return true;
        }

        public override string GetDescription()
        {
            if (MaxLevel > 0)
            {
                return $"Level {MinLevel}-{MaxLevel}";
            }
            return $"Level {MinLevel}+";
        }
    }

    /// <summary>
    /// 教程完成条件 (v1.14.0+)
    /// </summary>
    [Serializable]
    public class TutorialCompletedCondition : TutorialCondition
    {
        [Tooltip("需要完成的教程 ID 列表")]
        public string[] RequiredTutorialIds;

        [Tooltip("需要全部完成 (否则只需完成任一)")]
        public bool RequireAll = true;

        public override string ConditionType => "TutorialCompleted";

        public override bool IsSatisfied(TutorialContext ctx)
        {
            if (RequiredTutorialIds == null || RequiredTutorialIds.Length == 0)
            {
                return true;
            }

            var manager = ctx?.Manager;
            if (manager == null) return false;

            if (RequireAll)
            {
                foreach (var id in RequiredTutorialIds)
                {
                    if (!manager.IsSequenceCompleted(id))
                    {
                        return false;
                    }
                }
                return true;
            }
            else
            {
                foreach (var id in RequiredTutorialIds)
                {
                    if (manager.IsSequenceCompleted(id))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public override string GetDescription()
        {
            return RequireAll
                ? $"All completed: {string.Join(", ", RequiredTutorialIds)}"
                : $"Any completed: {string.Join(", ", RequiredTutorialIds)}";
        }
    }

    /// <summary>
    /// 场景条件 (v1.14.0+)
    /// </summary>
    [Serializable]
    public class SceneCondition : TutorialCondition
    {
        [Tooltip("允许的场景名称列表")]
        public string[] AllowedScenes;

        [Tooltip("排除的场景名称列表")]
        public string[] ExcludedScenes;

        public override string ConditionType => "Scene";

        public override bool IsSatisfied(TutorialContext ctx)
        {
            string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            // 检查排除列表
            if (ExcludedScenes != null && ExcludedScenes.Length > 0)
            {
                foreach (var scene in ExcludedScenes)
                {
                    if (currentScene == scene)
                    {
                        return false;
                    }
                }
            }

            // 检查允许列表
            if (AllowedScenes != null && AllowedScenes.Length > 0)
            {
                foreach (var scene in AllowedScenes)
                {
                    if (currentScene == scene)
                    {
                        return true;
                    }
                }
                return false;
            }

            return true;
        }

        public override string GetDescription()
        {
            if (AllowedScenes != null && AllowedScenes.Length > 0)
            {
                return $"Scene in: {string.Join(", ", AllowedScenes)}";
            }
            return "Any scene";
        }
    }

    /// <summary>
    /// 任务条件 (v1.14.0+)
    /// </summary>
    [Serializable]
    public class QuestCondition : TutorialCondition
    {
        [Tooltip("任务 ID")]
        public string QuestId;

        [Tooltip("所需状态")]
        public QuestRequiredState RequiredState = QuestRequiredState.Completed;

        public override string ConditionType => "Quest";

        public override bool IsSatisfied(TutorialContext ctx)
        {
            return ctx?.QueryQuestState != null && ctx.QueryQuestState(QuestId) == RequiredState;
        }

        public override string GetDescription()
        {
            return $"Quest {QuestId}: {RequiredState}";
        }
    }

    public enum QuestRequiredState
    {
        NotStarted,
        InProgress,
        Completed
    }

    /// <summary>
    /// 变量条件 (v1.14.0+)
    /// </summary>
    [Serializable]
    public class VariableCondition : TutorialCondition
    {
        [Tooltip("变量键")]
        public string VariableKey;

        [Tooltip("比较运算符")]
        public ComparisonOperator Operator = ComparisonOperator.Equals;

        [Tooltip("比较值")]
        public string CompareValue;

        public override string ConditionType => "Variable";

        public override bool IsSatisfied(TutorialContext ctx)
        {
            if (ctx == null || string.IsNullOrEmpty(VariableKey))
            {
                return false;
            }

            if (!ctx.HasVariable(VariableKey)) return false;

            var value = ctx.GetVariable<string>(VariableKey, "");
            return Compare(value, CompareValue);
        }

        private bool Compare(string value, string target)
        {
            // 尝试数值比较
            if (float.TryParse(value, out float numValue) && float.TryParse(target, out float numTarget))
            {
                return Operator switch
                {
                    ComparisonOperator.Equals => Mathf.Approximately(numValue, numTarget),
                    ComparisonOperator.NotEquals => !Mathf.Approximately(numValue, numTarget),
                    ComparisonOperator.Greater => numValue > numTarget,
                    ComparisonOperator.GreaterOrEqual => numValue >= numTarget,
                    ComparisonOperator.Less => numValue < numTarget,
                    ComparisonOperator.LessOrEqual => numValue <= numTarget,
                    _ => false
                };
            }

            // 字符串比较
            return Operator switch
            {
                ComparisonOperator.Equals => value == target,
                ComparisonOperator.NotEquals => value != target,
                _ => false
            };
        }

        public override string GetDescription()
        {
            string op = Operator switch
            {
                ComparisonOperator.Equals => "==",
                ComparisonOperator.NotEquals => "!=",
                ComparisonOperator.Greater => ">",
                ComparisonOperator.GreaterOrEqual => ">=",
                ComparisonOperator.Less => "<",
                ComparisonOperator.LessOrEqual => "<=",
                _ => "?"
            };
            return $"{VariableKey} {op} {CompareValue}";
        }
    }

    public enum ComparisonOperator
    {
        Equals,
        NotEquals,
        Greater,
        GreaterOrEqual,
        Less,
        LessOrEqual
    }
}
