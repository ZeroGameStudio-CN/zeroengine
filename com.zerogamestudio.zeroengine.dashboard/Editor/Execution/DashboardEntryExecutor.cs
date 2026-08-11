using System;
using UnityEditor;
using UnityEngine;
using ZeroEngine.EditorUI;

namespace ZeroEngine.Editor.Dashboard
{
    internal enum DashboardExecutionStatus
    {
        Succeeded,
        Unavailable,
        Cancelled,
        MenuMissing,
        Failed
    }

    internal sealed class DashboardExecutionResult
    {
        internal DashboardExecutionResult(
            DashboardExecutionStatus status,
            string message,
            Exception exception = null,
            DashboardDiagnostic diagnostic = null)
        {
            Status = status;
            Message = message ?? string.Empty;
            Exception = exception;
            Diagnostic = diagnostic;
        }

        internal DashboardExecutionStatus Status { get; }
        internal string Message { get; }
        internal Exception Exception { get; }
        internal DashboardDiagnostic Diagnostic { get; }
    }

    internal interface IDashboardExecutionHost
    {
        bool IsPlaying { get; }
        bool Confirm(DashboardEntry entry);
        bool ExecuteMenuItem(string menuPath);
        void LogException(Exception exception);
    }

    internal static class DashboardEntryExecutor
    {
        internal static DashboardExecutionResult Execute(DashboardEntry entry)
        {
            return Execute(entry, UnityDashboardExecutionHost.Instance);
        }

        internal static DashboardExecutionResult Execute(
            DashboardEntry entry,
            DashboardActionRegistry registry,
            EditorWindow owner)
        {
            return Execute(entry, registry, owner, UnityDashboardExecutionHost.Instance);
        }

        internal static DashboardExecutionResult Execute(DashboardEntry entry, IDashboardExecutionHost host)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (host == null)
                throw new ArgumentNullException(nameof(host));

            if (entry.Availability == DashboardEntryAvailability.EditMode && host.IsPlaying)
            {
                return new DashboardExecutionResult(
                    DashboardExecutionStatus.Unavailable,
                    "该工具仅在编辑模式可用。");
            }
            if (entry.Availability == DashboardEntryAvailability.PlayMode && !host.IsPlaying)
            {
                return new DashboardExecutionResult(
                    DashboardExecutionStatus.Unavailable,
                    "该工具仅在运行模式可用。");
            }

            if (entry.ExecutionKind != DashboardEntryExecutionKind.LegacyMenu)
            {
                return new DashboardExecutionResult(
                    DashboardExecutionStatus.Failed,
                    "Provider action 必须通过工作台动作注册表执行。");
            }

            if ((entry.Safety == DashboardEntrySafety.ProjectWrite ||
                 entry.Safety == DashboardEntrySafety.Destructive) &&
                !host.Confirm(entry))
            {
                return new DashboardExecutionResult(DashboardExecutionStatus.Cancelled, "已取消执行。");
            }

            try
            {
                if (!host.ExecuteMenuItem(entry.MenuPath))
                {
                    return new DashboardExecutionResult(
                        DashboardExecutionStatus.MenuMissing,
                        "Unity 无法执行旧版菜单路径 '" + entry.MenuPath + "'。");
                }
                return new DashboardExecutionResult(DashboardExecutionStatus.Succeeded, "执行成功。");
            }
            catch (Exception exception)
            {
                host.LogException(exception);
                return new DashboardExecutionResult(
                    DashboardExecutionStatus.Failed,
                    "旧版菜单执行异常 " + exception.GetType().Name + "：" + exception.Message,
                    exception);
            }
        }

        internal static DashboardExecutionResult Execute(
            DashboardEntry entry,
            DashboardActionRegistry registry,
            EditorWindow owner,
            IDashboardExecutionHost host)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            if (!owner)
                throw new ArgumentNullException(nameof(owner));
            if (host == null)
                throw new ArgumentNullException(nameof(host));
            if (entry.ExecutionKind == DashboardEntryExecutionKind.LegacyMenu)
                return Execute(entry, host);

            DashboardExecutionResult availability = CheckAvailability(entry, host.IsPlaying);
            if (availability != null)
                return availability;

            if (!registry.TryGetState(entry, out EditorToolActionState state, out DashboardDiagnostic diagnostic))
            {
                return new DashboardExecutionResult(
                    DashboardExecutionStatus.Failed,
                    diagnostic.Message,
                    diagnostic: diagnostic);
            }
            if (!state.Enabled)
                return new DashboardExecutionResult(DashboardExecutionStatus.Unavailable, state.DisabledReason);

            if ((entry.Safety == DashboardEntrySafety.ProjectWrite ||
                 entry.Safety == DashboardEntrySafety.Destructive) &&
                !host.Confirm(entry))
            {
                return new DashboardExecutionResult(DashboardExecutionStatus.Cancelled, "已取消执行。");
            }

            if (!registry.TryGetState(entry, out state, out diagnostic))
            {
                return new DashboardExecutionResult(
                    DashboardExecutionStatus.Failed,
                    diagnostic.Message,
                    diagnostic: diagnostic);
            }
            if (!state.Enabled)
                return new DashboardExecutionResult(DashboardExecutionStatus.Unavailable, state.DisabledReason);

            var context = new EditorToolActionContext(owner, entry.ModuleId, entry.Id);
            if (!registry.TryExecute(entry, context, out EditorToolActionResult actionResult, out diagnostic))
            {
                return new DashboardExecutionResult(
                    DashboardExecutionStatus.Failed,
                    diagnostic.Message,
                    diagnostic: diagnostic);
            }

            switch (actionResult.Status)
            {
                case EditorToolActionStatus.Succeeded:
                    return new DashboardExecutionResult(DashboardExecutionStatus.Succeeded, actionResult.Message);
                case EditorToolActionStatus.Cancelled:
                    return new DashboardExecutionResult(DashboardExecutionStatus.Cancelled, actionResult.Message);
                default:
                    return new DashboardExecutionResult(DashboardExecutionStatus.Failed, actionResult.Message);
            }
        }

        private static DashboardExecutionResult CheckAvailability(DashboardEntry entry, bool isPlaying)
        {
            if (entry.Availability == DashboardEntryAvailability.EditMode && isPlaying)
                return new DashboardExecutionResult(DashboardExecutionStatus.Unavailable, "该工具仅在编辑模式可用。");
            if (entry.Availability == DashboardEntryAvailability.PlayMode && !isPlaying)
                return new DashboardExecutionResult(DashboardExecutionStatus.Unavailable, "该工具仅在运行模式可用。");
            return null;
        }

        internal static bool InterpretConfirmationDialogResult(bool continueSelected)
        {
            return continueSelected;
        }

        private sealed class UnityDashboardExecutionHost : IDashboardExecutionHost
        {
            internal static readonly UnityDashboardExecutionHost Instance = new UnityDashboardExecutionHost();

            public bool IsPlaying => EditorApplication.isPlaying;

            public bool Confirm(DashboardEntry entry)
            {
                string title = entry.Safety == DashboardEntrySafety.Destructive
                    ? "确认破坏性操作"
                    : "确认项目写入";
                bool continueSelected = EditorUtility.DisplayDialog(
                    title,
                    entry.Confirmation,
                    "继续",
                    "取消");
                return InterpretConfirmationDialogResult(continueSelected);
            }

            public bool ExecuteMenuItem(string menuPath)
            {
                return EditorApplication.ExecuteMenuItem(menuPath);
            }

            public void LogException(Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
