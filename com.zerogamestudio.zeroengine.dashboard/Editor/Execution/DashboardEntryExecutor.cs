using System;
using UnityEditor;
using UnityEngine;

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
        internal DashboardExecutionResult(DashboardExecutionStatus status, string message, Exception exception = null)
        {
            Status = status;
            Message = message ?? string.Empty;
            Exception = exception;
        }

        internal DashboardExecutionStatus Status { get; }
        internal string Message { get; }
        internal Exception Exception { get; }
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
                    "This entry is available only in Edit Mode.");
            }
            if (entry.Availability == DashboardEntryAvailability.PlayMode && !host.IsPlaying)
            {
                return new DashboardExecutionResult(
                    DashboardExecutionStatus.Unavailable,
                    "This entry is available only in Play Mode.");
            }

            if ((entry.Safety == DashboardEntrySafety.ProjectWrite ||
                 entry.Safety == DashboardEntrySafety.Destructive) &&
                !host.Confirm(entry))
            {
                return new DashboardExecutionResult(DashboardExecutionStatus.Cancelled, "Execution was cancelled.");
            }

            try
            {
                if (!host.ExecuteMenuItem(entry.MenuPath))
                {
                    return new DashboardExecutionResult(
                        DashboardExecutionStatus.MenuMissing,
                        "Unity could not execute menu path '" + entry.MenuPath + "'.");
                }
                return new DashboardExecutionResult(DashboardExecutionStatus.Succeeded, "Executed successfully.");
            }
            catch (Exception exception)
            {
                host.LogException(exception);
                return new DashboardExecutionResult(
                    DashboardExecutionStatus.Failed,
                    "Menu execution threw " + exception.GetType().Name + ": " + exception.Message,
                    exception);
            }
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
                    ? "Confirm destructive command"
                    : "Confirm project change";
                bool continueSelected = EditorUtility.DisplayDialog(
                    title,
                    entry.Confirmation,
                    "Continue",
                    "Cancel");
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
