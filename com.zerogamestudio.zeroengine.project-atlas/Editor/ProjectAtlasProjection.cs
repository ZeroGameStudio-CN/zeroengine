using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ZeroEngine.ProjectAtlas
{
    public static class ProjectAtlasValidator
    {
        public static bool IsProjectionCurrent(ProjectAtlasGraph graph)
        {
            if (graph == null || graph.Project == null)
                return false;
            string path = ProjectAtlasCatalogLoader.ResolveSafeProjectPath(
                graph.ProjectRoot,
                ProjectAtlasCatalogLoader.GeneratedIndexPath,
                false);
            if (!File.Exists(path))
                return false;
            return string.Equals(
                File.ReadAllText(path),
                ProjectAtlasMarkdownProjector.Render(graph),
                StringComparison.Ordinal);
        }

        public static string DescribeProjectionDifference(ProjectAtlasGraph graph)
        {
            if (graph == null || graph.Project == null)
                return "当前没有可生成的项目图谱。";
            string path = ProjectAtlasCatalogLoader.ResolveSafeProjectPath(
                graph.ProjectRoot,
                ProjectAtlasCatalogLoader.GeneratedIndexPath,
                false);
            if (!File.Exists(path))
                return "将新建 " + ProjectAtlasCatalogLoader.GeneratedIndexPath + "。";

            string current = File.ReadAllText(path).Replace("\r\n", "\n");
            string expected = ProjectAtlasMarkdownProjector.Render(graph);
            if (string.Equals(current, expected, StringComparison.Ordinal))
                return "生成索引已经是最新状态。";
            int currentLines = current.Split('\n').Length;
            int expectedLines = expected.Split('\n').Length;
            return "将更新 " + ProjectAtlasCatalogLoader.GeneratedIndexPath +
                   "（当前 " + currentLines + " 行，生成后 " + expectedLines + " 行）。";
        }
    }

    public static class ProjectAtlasMarkdownProjector
    {
        public static string Render(ProjectAtlasGraph graph)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));
            if (graph.Project == null)
                throw new InvalidOperationException("当前图谱没有有效的 project 元数据。");

            var builder = new StringBuilder();
            Line(builder, "<!-- GENERATED: Project Atlas schemaVersion=1; source=docs/architecture/project-atlas.json; DO NOT EDIT -->");
            Line(builder, "# " + EscapeHeading(graph.Project.DisplayName) + " 系统路由索引");
            Line(builder);
            Line(builder, graph.Project.Summary);
            Line(builder);
            Line(builder, "本文件由 Project Atlas 确定性生成。系统语义维护在 `docs/architecture/project-atlas.json` 及其显式碎片中；结构事实仍以各引用的权威源为准。");
            Line(builder);
            RenderSummary(builder, graph);
            RenderTeamView(builder, graph);
            RenderProgramView(builder, graph);
            RenderAgentView(builder, graph);
            RenderDiagnostics(builder, graph);
            return builder.ToString();
        }

        private static void RenderSummary(StringBuilder builder, ProjectAtlasGraph graph)
        {
            Line(builder, "## 快速定位");
            Line(builder);
            Line(builder, "| 分类 | 系统 | 生命周期 | 归属 | 负责岗位 |");
            Line(builder, "| --- | --- | --- | --- | --- |");
            foreach (ProjectAtlasSystem system in graph.Systems)
            {
                Line(builder, "| " + Cell(system.Category) + " | [" + Cell(system.DisplayName) + "](#" + TeamAnchor(system.Id) + ") | " +
                              Cell(system.Lifecycle) + " | " + Cell(system.Ownership) + " | " + Cell(string.Join("、", system.OwnerRoles)) + " |");
            }
            Line(builder);
        }

        private static void RenderTeamView(StringBuilder builder, ProjectAtlasGraph graph)
        {
            Line(builder, "## 项目与功能");
            Line(builder);
            foreach (ProjectAtlasSystem system in graph.Systems)
            {
                Line(builder, "<a id=\"" + TeamAnchor(system.Id) + "\"></a>");
                Line(builder, "### 团队 · " + system.Id);
                Line(builder);
                Line(builder, "**" + system.DisplayName + "** — " + system.Summary);
                Line(builder);
                Bullet(builder, "用途", system.Team.Purpose);
                Bullet(builder, "使用者", JoinOrNone(system.Team.Audiences));
                Bullet(builder, "负责岗位", JoinOrNone(system.OwnerRoles));
                Bullet(builder, "典型工作流", JoinOrNone(system.Team.Workflows));
                if (system.Team.ConfigurationMode == "none")
                    Bullet(builder, "配置", "无人工配置入口。" + system.Team.ConfigurationReason);
                else
                    Bullet(builder, "配置入口", DescribeReferences(graph, system.Team.ConfigurationRefs));
                Bullet(builder, "诊断入口", DescribeReferences(graph, system.Team.DiagnosticRefs));
                Bullet(builder, "影响关系", DescribeRelations(graph, system));
                Line(builder);
            }
        }

        private static void RenderProgramView(StringBuilder builder, ProjectAtlasGraph graph)
        {
            Line(builder, "## 架构与路由");
            Line(builder);
            foreach (ProjectAtlasSystem system in graph.Systems)
            {
                Line(builder, "### 程序 · " + system.Id);
                Line(builder);
                Bullet(builder, "系统", system.DisplayName + "（" + system.Ownership + " / " + system.Lifecycle + "）");
                Bullet(builder, "入口", DescribeReferences(graph, system.Program.EntryRefs));
                Bullet(builder, "结构", DescribeReferences(graph, system.Program.StructureRefs));
                Bullet(builder, "数据流", JoinOrNone(system.Program.DataFlow));
                Bullet(builder, "验证", DescribeReferences(graph, system.Program.VerificationRefs));
                Bullet(builder, "关系", DescribeRelations(graph, system));
                Line(builder);
            }
        }

        private static void RenderAgentView(StringBuilder builder, ProjectAtlasGraph graph)
        {
            Line(builder, "## Agent 改动合同");
            Line(builder);
            Line(builder, "根 Agent 入口：" + DescribeReferences(graph, new[] { graph.Project.RootAgentRule }) + "。图谱不能覆盖更高优先级的安全、Unity、设备、SCM 或发布规则。");
            Line(builder);
            foreach (ProjectAtlasSystem system in graph.Systems)
            {
                Line(builder, "### Agent · " + system.Id);
                Line(builder);
                Bullet(builder, "系统", system.DisplayName);
                Bullet(builder, "开始前阅读", DescribeReferences(graph, system.Agent.ReadFirstRefs));
                Bullet(builder, "改动边界", system.Agent.ChangeBoundary);
                Bullet(builder, "最窄验证", DescribeReferences(graph, system.Agent.VerificationRefs));
                Bullet(builder, "更新触发", JoinOrNone(system.Agent.UpdateTriggers));
                Line(builder);
            }
        }

        private static void RenderDiagnostics(StringBuilder builder, ProjectAtlasGraph graph)
        {
            Line(builder, "## 生成时诊断");
            Line(builder);
            ProjectAtlasDiagnostic[] diagnostics = graph.Diagnostics
                .OrderBy(item => item.Severity)
                .ThenBy(item => item.Code, StringComparer.Ordinal)
                .ThenBy(item => item.SourcePath, StringComparer.Ordinal)
                .ThenBy(item => item.FieldPath, StringComparer.Ordinal)
                .ToArray();
            if (diagnostics.Length == 0)
            {
                Line(builder, "- 无。");
                Line(builder);
                return;
            }
            foreach (ProjectAtlasDiagnostic diagnostic in diagnostics)
            {
                string location = string.IsNullOrEmpty(diagnostic.SourcePath)
                    ? string.Empty
                    : "（" + diagnostic.SourcePath + (string.IsNullOrEmpty(diagnostic.FieldPath) ? string.Empty : ": " + diagnostic.FieldPath) + "）";
                Line(builder, "- " + diagnostic.Severity + " `" + diagnostic.Code + "`：" + diagnostic.Message + location);
            }
            Line(builder);
        }

        private static string DescribeReferences(ProjectAtlasGraph graph, IEnumerable<string> referenceIds)
        {
            string[] values = (referenceIds ?? Array.Empty<string>())
                .Select(id => DescribeReference(graph, id))
                .Where(value => !string.IsNullOrEmpty(value))
                .ToArray();
            return JoinOrNone(values);
        }

        private static string DescribeReference(ProjectAtlasGraph graph, string id)
        {
            ProjectAtlasReference reference = graph.FindReference(id);
            if (reference == null)
                return "`" + id + "`（缺失）";
            string suffix = string.Empty;
            if (graph.Resolutions.TryGetValue(id, out ProjectAtlasReferenceResolution resolution))
            {
                suffix = resolution.Status == ProjectAtlasResolutionStatus.Resolved
                    ? " → " + DescribeProjectionValue(reference, resolution.DisplayValue)
                    : "（" + resolution.Status + "）";
            }
            return reference.DisplayName + " [`" + reference.Kind + ":" + reference.Target + "`]" + suffix;
        }

        private static string DescribeProjectionValue(ProjectAtlasReference reference, string value)
        {
            if (!string.Equals(reference.Kind, "package", StringComparison.Ordinal) ||
                string.IsNullOrEmpty(value) ||
                !value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            string path = value.Substring("file:".Length);
            bool rootedUnixPath = path.StartsWith("/", StringComparison.Ordinal);
            bool rootedWindowsPath = path.Length >= 3 &&
                                     char.IsLetter(path[0]) &&
                                     path[1] == ':' &&
                                     (path[2] == '/' || path[2] == '\\');
            return rootedUnixPath || rootedWindowsPath ? "file:<local>" : value;
        }

        private static string DescribeRelations(ProjectAtlasGraph graph, ProjectAtlasSystem system)
        {
            string[] values = system.Relations.Select(relation =>
            {
                ProjectAtlasSystem target = graph.FindSystem(relation.TargetSystemId);
                return relation.Kind + " → " + (target == null ? relation.TargetSystemId : target.DisplayName + " (`" + target.Id + "`)");
            }).ToArray();
            return JoinOrNone(values);
        }

        private static string JoinOrNone(IEnumerable<string> values)
        {
            string[] array = (values ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
            return array.Length == 0 ? "无" : string.Join("；", array);
        }

        private static void Bullet(StringBuilder builder, string label, string value)
        {
            Line(builder, "- **" + label + "**：" + (string.IsNullOrWhiteSpace(value) ? "无" : value));
        }

        private static string Cell(string value)
        {
            return (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }

        private static string EscapeHeading(string value)
        {
            return (value ?? string.Empty).Replace("#", "\\#").Trim();
        }

        private static string TeamAnchor(string systemId)
        {
            return "team-" + systemId;
        }

        private static void Line(StringBuilder builder, string value = "")
        {
            builder.Append(value).Append('\n');
        }
    }

    public static class ProjectAtlasProjectWriter
    {
        public static string WriteGeneratedIndex(ProjectAtlasGraph graph)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));
            if (graph.HasErrors)
                throw new InvalidOperationException("图谱仍有 error，不能生成索引。");

            string destination = ProjectAtlasCatalogLoader.ResolveSafeProjectPath(
                graph.ProjectRoot,
                ProjectAtlasCatalogLoader.GeneratedIndexPath,
                false);
            string directory = Path.GetDirectoryName(destination);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                throw new DirectoryNotFoundException("生成索引目录不存在：docs/architecture");

            string temporary = Path.Combine(directory, ".system-routing-index." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temporary, ProjectAtlasMarkdownProjector.Render(graph), new UTF8Encoding(false));
                if (File.Exists(destination))
                    File.Replace(temporary, destination, null);
                else
                    File.Move(temporary, destination);
                return destination;
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }
}
