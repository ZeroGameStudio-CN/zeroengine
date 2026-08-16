namespace ZeroEngine.Editor
{
    internal static class DashboardText
    {
        internal const string WindowTitle = "ZGS 工作台";
        internal const string HeaderSubtitle = "从常用工作流进入任务，按需查找完整工具与资料。";
        internal const string Home = "首页";
        internal const string HomeTooltip = "查看常用工作流，并在左侧进入项目工作区面板。";
        internal const string System = "系统";
        internal const string SystemTooltip = "查看描述符健康状态、已安装包和项目适配器。";
        internal const string RefreshTooltip = "重新扫描模块描述符和项目适配器。";
        internal const string SearchPlaceholder = "搜索工具、资料或诊断…";
        internal const string SearchTooltip = "搜索中文名称、说明、使用方法、资料、旧入口关键词和技术标识。";
        internal const string Clear = "清空";
        internal const string ClearTooltip = "清空当前搜索条件。";
        internal const string Context = "说明";
        internal const string ContextTooltip = "查看当前所选工具或面板的用途、状态、用法和相关资料。";
        internal const string ContextEmpty = "选择一个工作流、工具或面板后，这里会显示用途、状态和相关资料。";
        internal const string CommonWorkflows = "常用工作流";
        internal const string CommonWorkflowsSubtitle = "高频、安全且适合直接进入的工作入口。";
        internal const string SearchResults = "搜索结果";
        internal const string SearchResultsSubtitle = "显示与关键词匹配的工具动作和相关资料。";
        internal const string WorkspaceNavigation = "工作区";
        internal const string WorkspaceNavigationTooltip = "选择当前项目提供的内嵌工作区面板。";
        internal const string LoadingCatalog = "正在载入工作台模块；窗口已可操作。";
        internal const string LoadingPanel = "正在恢复上次面板…";
        internal const string ReorderWorkspaceModules = "调整工作区分组顺序";
        internal const string ReorderWorkspaceModulesTooltip = "拖动此处调整左侧模块分组顺序；关闭工作台后仍会保留。";
        internal const string ReorderWorkspacePanels = "调整分组内面板顺序";
        internal const string ReorderWorkspacePanelsTooltip = "拖动此处调整当前分组内的面板顺序；关闭工作台后仍会保留。";
        internal const string ResizeWorkspaceNavigationTooltip = "拖动分隔线调整左侧工作区宽度；关闭工作台后仍会保留。";
        internal const string ExpandAllGroups = "展";
        internal const string ExpandAllGroupsTooltip = "展开当前工作区的全部分组。";
        internal const string CollapseAllGroups = "收";
        internal const string CollapseAllGroupsTooltip = "折叠当前工作区的全部分组。";
        internal const string ResetOrder = "重置";
        internal const string ResetOrderTooltip = "恢复描述符提供的默认分组和面板顺序；折叠状态保持不变。";
        internal const string More = "更多…";
        internal const string MoreTooltip = "查看该工作流的其他动作。";
        internal const string RelatedResources = "相关资料";
        internal const string OpenReference = "打开资料";
        internal const string OpenReferenceTooltip = "打开该模块提供的说明或参考窗口。";
        internal const string CurrentState = "当前状态";
        internal const string Ready = "当前可用。";
        internal const string SafetyAndImpact = "安全与影响";
        internal const string DeveloperInfo = "开发信息";
        internal const string GoToDiagnostics = "前往系统诊断";
        internal const string GoToDiagnosticsTooltip = "打开系统页查看该问题的完整诊断。";
        internal const string PanelLoadFailed = "面板加载失败；其他工作流仍可使用。";
        internal const string ReferenceResults = "相关资料";
        internal const string ReferenceResultsTooltip = "与搜索词匹配的说明、指南和参考窗口。";
        internal const string LegacyEntry = "旧版入口";
        internal const string LegacyEntryTooltip = "此入口仍使用 schema v1，仅在 Dashboard 4.x 兼容。";
        internal const string Modules = "模块";
        internal const string ModuleSelectorTooltip = "选择要查看的工具模块。";
        internal const string Documentation = "文档";
        internal const string DocumentationTooltip = "在文件管理器中打开该模块的本地文档。";
        internal const string Website = "网页";
        internal const string WebsiteTooltip = "在浏览器中打开该模块的在线文档。";
        internal const string Details = "详情";
        internal const string DetailsTooltip = "显示模块、入口和 provider action 等技术详情。";
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
        internal const string Continue = "继续";
        internal const string Cancel = "取消";
        internal const string SystemSubtitle = "描述符健康状态、已安装包和项目适配器。";
        internal const string Healthy = "健康 · 无诊断";
        internal const string HealthyDescription = "所有已发现的工作台描述符与 provider 均有效。";
        internal const string InstalledDescriptorIssue = "已安装 · 工作台配置异常";
        internal const string InstalledDescriptorIssueTooltip = "包已安装，但工作台描述符存在错误；可在上方诊断中查看详情。";
        internal const string InstalledWithoutWorkspaceEntry = "已安装 · 无工作台入口";
        internal const string InstalledWithoutWorkspaceEntryTooltip = "包已正常安装，但没有提供工作台描述符；这不表示缺失或异常。";
        internal const string InstalledWorkspaceContentTooltip = "包已安装，并已向工作台提供工具、面板或资料入口。";
        internal const string PackageIssuesTooltip = "已安装但工作台描述符需要处理的包。";
        internal const string ConnectedPackagesTooltip = "已安装且已向工作台提供入口的包。";
        internal const string PackagesWithoutWorkspaceEntryTooltip = "已安装但没有工作台入口的包；通常是基础库或仅供其他模块依赖。";
        internal const string ReadOnly = "只读";
        internal const string ReadOnlyTooltip = "只读取项目或编辑器数据，不写入项目文件。";
        internal const string ProjectWrite = "写入项目";
        internal const string ProjectWriteTooltip = "该动作可能修改当前项目；执行前会保留原确认流程。";
        internal const string Destructive = "破坏性操作";
        internal const string DestructiveTooltip = "该动作可能产生难以恢复的修改；执行前必须确认。";
        internal const string Navigation = "导航";
        internal const string NavigationTooltip = "仅打开或切换编辑器窗口。";
        internal const string InstalledPackagesTooltip = "这里只列出当前项目已安装的 ZeroEngine 相关包，并单独说明是否接入工作台；不包含未安装包。";
        internal const string ProjectAdaptersTooltip = "查看由当前项目贡献并挂载到上游模块的适配器。";
        internal const string Error = "错误";
        internal const string Warning = "警告";

        internal static string ModuleCount(int count) => count + " 个模块";
        internal static string ToolCount(int count) => count + " 个工具";
        internal static string PanelCount(int count) => count + " 个面板";
        internal static string IssueCount(int count) => count + " 个问题";
        internal static string IssuesRequireAttention(int count) => count + " 个问题需要处理";
        internal static string InstalledPackages(int count) => "已安装包（" + count + "）";
        internal static string InstalledCount(int count) => count + " 个已安装";
        internal static string ConnectedPackageCount(int count) => count + " 个已接入";
        internal static string PackageWithoutEntryCount(int count) => count + " 个无入口";
        internal static string PackageIssues(int count) => "需要处理（" + count + "）";
        internal static string ConnectedPackages(int count) => "已接入工作台（" + count + "）";
        internal static string PackagesWithoutWorkspaceEntry(int count) => "无工作台入口（" + count + "）";
        internal static string ProjectAdapters(int count) => "项目适配器（" + count + "）";
        internal static string ContributedTools(int count) => "提供工具：" + count;
        internal static string InstalledWorkspaceContent(int toolCount, int panelCount, int referenceCount)
        {
            string summary = string.Empty;
            if (toolCount > 0)
                summary = toolCount + " 个工具";
            if (panelCount > 0)
                summary += (summary.Length == 0 ? string.Empty : " · ") + panelCount + " 个面板";
            if (referenceCount > 0)
                summary += (summary.Length == 0 ? string.Empty : " · ") + referenceCount + " 份资料";
            return summary.Length == 0 ? "已安装 · 已接入工作台" : "已安装 · " + summary;
        }
        internal static string InstalledPackageTooltip(string packageName, string resolvedPath) =>
            "包标识：" + packageName + (string.IsNullOrWhiteSpace(resolvedPath) ? string.Empty : "\n安装路径：" + resolvedPath);
        internal static string PackageVersion(string version) => string.IsNullOrWhiteSpace(version) ? "版本未知" : "v" + version;
        internal static string PackageVersionTooltip(string version) =>
            string.IsNullOrWhiteSpace(version) ? "Unity 未返回包版本。" : "当前安装版本：" + version;
        internal static string PackageSearchExpandedTooltip(string tooltip) => tooltip + "\n搜索期间匹配分组会临时展开。";
        internal static string WorkspaceGroupTooltip(string description, bool searchActive)
        {
            string action = searchActive
                ? "搜索期间匹配分组会临时展开；清空搜索后恢复原折叠状态。"
                : "点击展开或折叠此分组；关闭工作台后仍会保留。";
            return string.IsNullOrWhiteSpace(description) ? action : description + "\n" + action;
        }
        internal static string ActionDisabled(string name, string reason) => name + "：" + reason;
        internal static string EditModeOnly(string name) => name + " 仅在编辑模式可用。";
        internal static string PlayModeOnly(string name) => name + " 仅在运行模式可用。";
        internal static string DocumentationMissing(string path) => "文档路径不存在：" + path;
    }
}
