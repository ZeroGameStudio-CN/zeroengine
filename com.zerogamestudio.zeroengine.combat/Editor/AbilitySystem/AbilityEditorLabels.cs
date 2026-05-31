namespace ZeroEngine.AbilitySystem.Editor
{
    public sealed class AbilityEditorLabels
    {
        public string SummaryTitle = "Ability Summary";
        public string LogicTitle = "Ability Logic";
        public string TriggerTitle = "Trigger";
        public string ConditionTitle = "Condition";
        public string EffectTitle = "Effect";
        public string AddTrigger = "Add Trigger";
        public string AddCondition = "Add Condition";
        public string AddEffect = "Add Effect";
        public string Search = "Search";
        public string Clear = "Clear";
        public string Configured = "Configured";
        public string NoMatchingComponents = "No matching components";
        public string EmptyConfigured = "<none>";
        public string Duplicate = "Duplicate";
        public string MoveUp = "Up";
        public string MoveDown = "Down";
        public string Remove = "Remove";
        public string Info = "Info";
        public string Actions = "...";
        public string MissingAbilityProperty = "Missing AbilityDefinition property.";
        public string DebugRawAbility = "Debug Raw Ability";
        public string ValidationPassed = "Ability definition is valid.";

        public static AbilityEditorLabels English()
        {
            return new AbilityEditorLabels();
        }

        public static AbilityEditorLabels Chinese()
        {
            return new AbilityEditorLabels
            {
                SummaryTitle = "Ability 摘要",
                LogicTitle = "Ability 逻辑",
                TriggerTitle = "触发器",
                ConditionTitle = "条件",
                EffectTitle = "效果",
                AddTrigger = "添加触发器",
                AddCondition = "添加条件",
                AddEffect = "添加效果",
                Search = "搜索",
                Clear = "清空",
                Configured = "已配置",
                NoMatchingComponents = "没有匹配组件",
                EmptyConfigured = "暂无已配置组件",
                Duplicate = "复制",
                MoveUp = "上移",
                MoveDown = "下移",
                Remove = "删除",
                Info = "说明",
                Actions = "...",
                MissingAbilityProperty = "缺少 AbilityDefinition 序列化字段。",
                DebugRawAbility = "调试原始 Ability",
                ValidationPassed = "Ability 配置有效。"
            };
        }
    }
}
