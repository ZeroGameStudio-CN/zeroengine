using System;
using ZeroEngine.EditorUI;

namespace ZeroEngine.Editor
{
    [EditorToolActionProvider("zeroengine.core-tools")]
    public sealed class ZeroEngineCoreToolActionProvider : IEditorToolActionProvider
    {
        public IEditorToolAction CreateAction(string actionId)
        {
            switch (actionId)
            {
                case "ability-editor": return Open(AbilitySystem.AbilityEditorWindow.OpenWindow, "已打开能力编辑器。");
                case "achievement-editor": return Open(Achievement.AchievementEditorWindow.ShowWindow, "已打开成就编辑器。");
                case "behavior-tree-editor": return Open(BehaviorTree.BTGraphEditorWindow.Open, "已打开行为树图编辑器。");
                case "behavior-tree-viewer": return Open(BehaviorTree.BehaviorTreeViewerWindow.ShowWindow, "已打开行为树查看器。");
                case "buff-editor": return Open(Buff.BuffEditorWindow.OpenWindow, "已打开 Buff 编辑器。");
                case "calendar-editor": return Open(Calendar.CalendarEditorWindow.ShowWindow, "已打开日历编辑器。");
                case "crafting-editor": return Open(Crafting.CraftingEditorWindow.ShowWindow, "已打开配方编辑器。");
                case "dialog-export": return Open(Dialog.DialogExportWindow.ShowWindow, "已打开对话导出工具。");
                case "dialog-graph-editor": return Open(Dialog.DialogGraphEditorWindow.Open, "已打开对话图编辑器。");
                case "dialog-node-inspector": return Open(Dialog.DialogNodeInspector.Open, "已打开对话节点检查器。");
                case "equipment-editor": return Open(Equipment.EquipmentEditorWindow.Open, "已打开装备编辑器。");
                case "global-search": return Open(GlobalSearchWindow.OpenWindow, "已打开全局搜索。");
                case "inventory-editor": return Open(Inventory.InventoryEditorWindow.OpenWindow, "已打开背包编辑器。");
                case "loot-table-editor": return Open(Loot.LootTableEditorWindow.ShowWindow, "已打开掉落表编辑器。");
                case "notification-editor": return Open(Notification.NotificationEditorWindow.ShowWindow, "已打开通知编辑器。");
                case "package-export": return Open(PackageExporter.Export, "已完成包导出流程。");
                case "quest-editor": return Open(Quest.QuestEditorWindow.OpenWindow, "已打开任务编辑器。");
                case "relationship-editor": return Open(Relationship.RelationshipEditorWindow.ShowWindow, "已打开关系编辑器。");
                case "settings-editor": return Open(Settings.SettingsEditorWindow.ShowWindow, "已打开设置编辑器。");
                case "shop-editor": return Open(Shop.ShopEditorWindow.ShowWindow, "已打开商店编辑器。");
                case "talent-tree-editor": return Open(TalentTree.TalentTreeEditorWindow.Open, "已打开天赋树编辑器。");
                case "translation-checker": return Open(Localization.TranslationCheckerWindow.ShowWindow, "已打开本地化检查器。");
                case "tutorial-editor": return Open(Tutorial.TutorialEditorWindow.ShowWindow, "已打开教程编辑器。");
                case "tutorial-graph-editor": return Open(Tutorial.TutorialGraphEditorWindow.Open, "已打开教程图编辑器。");
                default: return null;
            }
        }

        private static IEditorToolAction Open(Action action, string successMessage)
        {
            return new DelegateEditorToolAction(context =>
            {
                action();
                return new EditorToolActionResult(EditorToolActionStatus.Succeeded, successMessage);
            });
        }
    }
}
