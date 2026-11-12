using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroEngine.Editor.Dashboard
{
    internal sealed class DashboardKnownPackage
    {
        internal DashboardKnownPackage(
            string name,
            string displayName,
            string description,
            bool automaticallyConnects,
            bool recommended,
            params string[] dependencies)
        {
            Name = name ?? string.Empty;
            DisplayName = displayName ?? Name;
            Description = description ?? string.Empty;
            AutomaticallyConnects = automaticallyConnects;
            Recommended = recommended;
            Dependencies = dependencies ?? Array.Empty<string>();
        }

        internal string Name { get; }
        internal string DisplayName { get; }
        internal string Description { get; }
        internal bool AutomaticallyConnects { get; }
        internal bool Recommended { get; }
        internal IReadOnlyList<string> Dependencies { get; }
    }

    internal sealed class DashboardPackageInstallPlan
    {
        internal DashboardPackageInstallPlan(string targetName, IReadOnlyList<string> packageUrls)
        {
            TargetName = targetName ?? string.Empty;
            PackageUrls = packageUrls ?? Array.Empty<string>();
        }

        internal string TargetName { get; }
        internal IReadOnlyList<string> PackageUrls { get; }
    }

    internal static class DashboardPackageCatalog
    {
        private const string DashboardPackageName = "com.zerogamestudio.zeroengine.dashboard";
        private const string EditorUiPackageName = "com.zerogamestudio.zeroengine.editor-ui";
        private const string GitPathMarker = "?path=";

        private static readonly DashboardKnownPackage[] KnownPackageArray =
        {
            new DashboardKnownPackage("com.zerogamestudio.analytics", "分析与反馈", "玩家反馈、问题追踪与项目分析。", true, true, EditorUiPackageName),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.ai", "AI 决策", "行为树、GOAP 与 Utility AI 决策能力。", false, true, "com.zerogamestudio.zeroengine.core"),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.asset-catalog", "资产目录", "资产目录契约、离线快照与分类交换。", false, true),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.audio", "音频", "音频事件、音效池、音乐与混音支持。", false, true, "com.zerogamestudio.zeroengine.core", "com.zerogamestudio.zeroengine.persistence"),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.autobattle", "自动战斗", "无引擎依赖的格子可达性与确定性自动战斗决策内核。", false, true),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.character", "角色成长", "装备、天赋、队伍与职业成长能力。", false, true, "com.zerogamestudio.zeroengine.core", "com.zerogamestudio.zeroengine.data", "com.zerogamestudio.zeroengine.persistence"),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.combat", "战斗", "伤害、目标选择与战斗关系基础。", false, true, "com.zerogamestudio.zeroengine.core", "com.zerogamestudio.zeroengine.data"),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.config-pipeline", "配置管线", "Schema 优先的配置校验、生成与加载。", true, true, EditorUiPackageName),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.core", "核心基础", "对象池、事件、日志与通用基础设施。", false, true),
            new DashboardKnownPackage(DashboardPackageName, "工作台", "声明式发现与嵌入式编辑器工作台框架。", false, true, EditorUiPackageName),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.data", "数据与属性", "属性、修正器、约束与 Buff 数据能力。", false, true, "com.zerogamestudio.zeroengine.core", "com.zerogamestudio.zeroengine.formula"),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.data-toolkit", "数据工具库", "数据资产发现、浏览、编辑与校验。", true, true, EditorUiPackageName),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.dlc", "DLC 内容包", "平台无关的 DLC 与内容权益基础。", false, true),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.dungeon", "地牢探索", "地牢节点地图、事件与奖励计算。", false, true, "com.zerogamestudio.zeroengine.core", "com.zerogamestudio.zeroengine.data"),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.economy", "经济与物品", "背包、战利品、商店与制作系统。", false, true, "com.zerogamestudio.zeroengine.core", "com.zerogamestudio.zeroengine.persistence"),
            new DashboardKnownPackage(EditorUiPackageName, "编辑器界面基础", "ZeroEngine 编辑器工具的统一视觉与交互契约。", false, true),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.extraction", "搜打撤核心", "突袭、撤离、战利品与结算规则。", false, true),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.feedback", "玩家反馈", "队列化玩家反馈表单与状态展示。", true, true, "com.zerogamestudio.analytics", EditorUiPackageName, "com.zerogamestudio.zeroengine.ui"),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.formula", "公式", "分步公式求值、诊断与公式工作台。", true, true, EditorUiPackageName),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.gameplay", "通用玩法", "交互、教程与命令模式等通用玩法能力。", false, true, "com.zerogamestudio.zeroengine.core", "com.zerogamestudio.zeroengine.persistence"),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.input", "输入", "设备检测、按键重绑与输入上下文。", false, true, "com.zerogamestudio.zeroengine.core"),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.localization", "本地化", "多语言文本与字体切换支持。", false, true, "com.zerogamestudio.zeroengine.core"),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.modsystem", "模组与创意工坊", "项目无关的 Mod、Steam Workshop 与导入编排。", false, true, EditorUiPackageName),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.narrative", "剧情与任务", "对话、任务、目标、奖励与成就内容系统。", false, true, "com.zerogamestudio.zeroengine.core", "com.zerogamestudio.zeroengine.persistence"),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.network", "网络同步", "基于 Netcode 的联网与重连支持。", false, true, "com.zerogamestudio.zeroengine.core"),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.particle-catalog", "粒子库", "粒子目录、检索、分类与推荐契约。", false, true),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.pathfinding2d", "二维寻路", "平台图、跳跃连接与 A* 路线计算。", false, true, "com.zerogamestudio.zeroengine.core"),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.persistence", "存档与持久化", "多存档、截图、设置与保存接口。", false, true, "com.zerogamestudio.zeroengine.core"),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.project-atlas", "项目功能", "按消费项目的功能语言导航到对应说明和配置入口。", true, true, EditorUiPackageName),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.pvp", "异步 PvP", "队伍快照、匹配与排行框架。", false, true, "com.zerogamestudio.zeroengine.core"),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.rpg", "回合 RPG", "ATB、行动点、破防与回合战斗系统。", false, true, "com.zerogamestudio.zeroengine.core", "com.zerogamestudio.zeroengine.data", "com.zerogamestudio.zeroengine.combat"),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.settings", "玩家设置", "事务化、版本化的游戏设置。", false, true, "com.zerogamestudio.zeroengine.core", "com.zerogamestudio.zeroengine.persistence", "com.zerogamestudio.zeroengine.audio", "com.zerogamestudio.zeroengine.input", "com.zerogamestudio.zeroengine.localization"),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.social", "社交与通知", "关系、礼物、等级与通知队列。", false, true, "com.zerogamestudio.zeroengine.core", "com.zerogamestudio.zeroengine.persistence"),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.tce", "TCE 规则", "通用触发-条件-效果运行时与可复用组件。", true, true, EditorUiPackageName),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.tce.presentation", "TCE 表现", "残影、灵魂与视觉快照等表现层。", false, true, "com.zerogamestudio.zeroengine.tce"),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.ui", "用户界面", "界面层级、栈、遮罩与 MVVM 基础。", true, true, "com.zerogamestudio.zeroengine.core", EditorUiPackageName),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine.world", "世界环境", "天气、昼夜、日历与小地图能力。", false, true, "com.zerogamestudio.zeroengine.core", "com.zerogamestudio.zeroengine.persistence"),
            new DashboardKnownPackage("com.zerogamestudio.zeroengine", "旧版 ZeroEngine 合集", "历史聚合包，已由按需模块包替代。", true, false, EditorUiPackageName)
        };

        private static readonly Dictionary<string, DashboardKnownPackage> KnownPackagesByName =
            KnownPackageArray.ToDictionary(package => package.Name, StringComparer.Ordinal);

        internal static IReadOnlyList<DashboardKnownPackage> KnownPackages => KnownPackageArray;

        internal static bool TryGet(string packageName, out DashboardKnownPackage package)
        {
            return KnownPackagesByName.TryGetValue(packageName ?? string.Empty, out package);
        }

        internal static bool TryCreateInstallPlan(
            string dashboardPackageId,
            string targetPackageName,
            IEnumerable<DashboardInstalledPackage> installedPackages,
            out DashboardPackageInstallPlan plan,
            out string reason)
        {
            plan = null;
            reason = string.Empty;
            if (!TryGet(targetPackageName, out DashboardKnownPackage target))
            {
                reason = "该包不在官方 ZeroEngine 目录中。";
                return false;
            }
            if (!target.Recommended)
            {
                reason = "该历史聚合包已退役，请按需安装当前模块包。";
                return false;
            }
            if (!TryParseGitAnchor(dashboardPackageId, out string repositoryUrl, out string commit))
            {
                reason = "当前工作台不是可验证的 ZeroEngine Git pin，不能安全生成安装计划。";
                return false;
            }

            DashboardInstalledPackage[] installed = (installedPackages ?? Array.Empty<DashboardInstalledPackage>()).ToArray();
            foreach (DashboardInstalledPackage package in installed.Where(package => package.IsDirectDependency))
            {
                if (!TryGet(package.Name, out _) || string.IsNullOrWhiteSpace(package.PackageId))
                    continue;
                if (!MatchesGitAnchor(package.PackageId, repositoryUrl, commit))
                {
                    reason = "当前项目的 ZeroEngine 包没有统一到工作台使用的 Git 提交，请先统一 pin。";
                    return false;
                }
            }

            var orderedPackageNames = new List<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            CollectDependencyClosure(target.Name, visited, orderedPackageNames);
            var installedByName = installed.ToDictionary(package => package.Name, StringComparer.Ordinal);
            string[] packageUrls = orderedPackageNames
                .Where(name => !installedByName.TryGetValue(name, out DashboardInstalledPackage package) ||
                               !package.IsDirectDependency ||
                               !MatchesGitAnchor(package.PackageId, repositoryUrl, commit))
                .Select(name => repositoryUrl + GitPathMarker + name + "#" + commit)
                .ToArray();
            if (packageUrls.Length == 0)
            {
                reason = "该包及其 ZeroEngine 依赖已经以当前 Git pin 直接安装。";
                return false;
            }

            plan = new DashboardPackageInstallPlan(target.Name, packageUrls);
            return true;
        }

        internal static bool CanRemove(
            DashboardInstalledPackage package,
            IEnumerable<DashboardInstalledPackage> installedPackages,
            out string reason)
        {
            reason = string.Empty;
            if (package == null)
            {
                reason = "未找到当前包。";
                return false;
            }
            if (string.Equals(package.Name, DashboardPackageName, StringComparison.Ordinal) ||
                string.Equals(package.Name, EditorUiPackageName, StringComparison.Ordinal))
            {
                reason = "工作台基础依赖不能在工作台自身中卸载。";
                return false;
            }
            if (!package.IsDirectDependency)
            {
                reason = "该包由其他依赖间接引入，不能单独卸载。";
                return false;
            }

            string[] dependents = (installedPackages ?? Array.Empty<DashboardInstalledPackage>())
                .Where(candidate => candidate != null &&
                                    !string.Equals(candidate.Name, package.Name, StringComparison.Ordinal) &&
                                    candidate.Dependencies.Contains(package.Name, StringComparer.Ordinal))
                .Select(candidate => candidate.DisplayName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (dependents.Length > 0)
            {
                reason = "仍被以下已安装包依赖：" + string.Join("、", dependents) + "。";
                return false;
            }

            return true;
        }

        internal static bool TryParseGitAnchor(string packageId, out string repositoryUrl, out string commit)
        {
            repositoryUrl = string.Empty;
            commit = string.Empty;
            if (string.IsNullOrWhiteSpace(packageId))
                return false;

            int pathIndex = packageId.IndexOf(GitPathMarker, StringComparison.Ordinal);
            int hashIndex = packageId.LastIndexOf('#');
            if (pathIndex <= 0 || hashIndex <= pathIndex + GitPathMarker.Length || hashIndex >= packageId.Length - 1)
                return false;

            repositoryUrl = packageId.Substring(0, pathIndex);
            commit = packageId.Substring(hashIndex + 1);
            return repositoryUrl.EndsWith("/zeroengine.git", StringComparison.OrdinalIgnoreCase) &&
                   commit.All(character => Uri.IsHexDigit(character));
        }

        private static bool MatchesGitAnchor(string packageId, string repositoryUrl, string commit)
        {
            return TryParseGitAnchor(packageId, out string candidateRepository, out string candidateCommit) &&
                   string.Equals(candidateRepository, repositoryUrl, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(candidateCommit, commit, StringComparison.OrdinalIgnoreCase);
        }

        private static void CollectDependencyClosure(
            string packageName,
            ISet<string> visited,
            ICollection<string> orderedPackageNames)
        {
            if (!visited.Add(packageName))
                return;
            if (!TryGet(packageName, out DashboardKnownPackage package))
                throw new InvalidOperationException("官方包目录缺少依赖项：" + packageName);

            orderedPackageNames.Add(package.Name);
            foreach (string dependency in package.Dependencies)
                CollectDependencyClosure(dependency, visited, orderedPackageNames);
        }
    }
}
