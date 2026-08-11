namespace ZeroEngine.Editor
{
    internal static class DashboardText
    {
        internal const string WindowTitle = "ZeroEngine 仪表盘";
        internal const string HeaderSubtitle = "在一个编辑器工作区中管理已安装模块和项目适配器。";
        internal const string Tools = "工具";
        internal const string ToolsTooltip = "浏览并打开已安装模块提供的工具。";
        internal const string Workspace = "工作区";
        internal const string WorkspaceTooltip = "使用已安装模块和项目适配器提供的内嵌面板。";
        internal const string System = "系统";
        internal const string SystemTooltip = "查看描述符健康状态、已安装包和项目适配器。";
        internal const string RefreshTooltip = "重新扫描模块描述符和项目适配器。";
        internal const string SearchPlaceholder = "搜索当前页…";
        internal const string SearchTooltip = "搜索中文名称、说明、包名、菜单路径和技术标识。";
        internal const string Clear = "清空";
        internal const string ClearTooltip = "清空当前搜索条件。";
        internal const string Modules = "模块";
        internal const string AllTools = "全部工具";
        internal const string ModuleSelectorTooltip = "选择要查看的工具模块。";
        internal const string Documentation = "文档";
        internal const string DocumentationTooltip = "在文件管理器中打开该模块的本地文档。";
        internal const string Website = "网页";
        internal const string WebsiteTooltip = "在浏览器中打开该模块的在线文档。";
        internal const string Details = "详情";
        internal const string DetailsTooltip = "显示模块 ID、入口 ID 和菜单路径等技术详情。";
        internal const string Help = "帮助";
        internal const string HelpTooltip = "查看当前模块、工具或面板的用途、用法和技术详情。";
        internal const string Close = "关闭";
        internal const string CloseHelpTooltip = "关闭帮助抽屉。";
        internal const string Open = "打开";
        internal const string Run = "运行";
        internal const string NoDeclaredTools = "当前没有已声明的模块工具。请安装带有效描述符的包，或添加项目适配器。";
        internal const string NoSearchResults = "没有工具符合当前搜索条件。";
        internal const string NoWorkspacePanels = "当前没有可用的工作区面板。安装带 panels 描述符和 provider 的模块后会自动显示。";
        internal const string NoWorkspaceSearchResults = "没有工作区面板符合当前搜索条件。";
        internal const string Purpose = "用途";
        internal const string Usage = "使用方法";
        internal const string TechnicalDetails = "技术详情";
        internal const string Retry = "重试加载";
        internal const string RetryTooltip = "重新创建当前工作区面板。";
        internal const string ConfirmAction = "确认操作";
        internal const string SystemSubtitle = "描述符健康状态、已安装包和项目适配器。";
        internal const string Healthy = "健康 · 无诊断";
        internal const string HealthyDescription = "所有已发现的 Dashboard 描述符均有效。";
        internal const string DescriptorIssue = "描述符异常";
        internal const string ConnectedNoTools = "已连接 · 无直接工具";
        internal const string NoToolsDeclared = "未声明工具";
        internal const string ReadOnly = "只读";
        internal const string ReadOnlyTooltip = "只读取项目或编辑器数据，不写入项目文件。";
        internal const string ProjectWrite = "写入项目";
        internal const string ProjectWriteTooltip = "该动作可能修改当前项目；执行前会保留原确认流程。";
        internal const string Destructive = "破坏性操作";
        internal const string DestructiveTooltip = "该动作可能产生难以恢复的修改；执行前必须确认。";
        internal const string Navigation = "导航";
        internal const string NavigationTooltip = "仅打开或切换编辑器窗口。";

        internal static string ModuleCount(int count) => count + " 个模块";
        internal static string ToolCount(int count) => count + " 个工具";
        internal static string PanelCount(int count) => count + " 个面板";
        internal static string IssueCount(int count) => count + " 个问题";
        internal static string IssuesRequireAttention(int count) => count + " 个问题需要处理";
        internal static string InstalledPackages(int count) => "已安装包（" + count + "）";
        internal static string ProjectAdapters(int count) => "项目适配器（" + count + "）";
        internal static string ContributedTools(int count) => "提供工具：" + count;
        internal static string ConnectedTools(int count) => "已连接 · " + count + " 个工具";
        internal static string EditModeOnly(string name) => name + " 仅在编辑模式可用。";
        internal static string PlayModeOnly(string name) => name + " 仅在运行模式可用。";
        internal static string DocumentationMissing(string path) => "文档路径不存在：" + path;
    }
}
