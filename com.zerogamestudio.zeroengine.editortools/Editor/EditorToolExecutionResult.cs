using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroEngine.EditorTools
{
    public enum EditorToolExecutionStatus
    {
        Success,
        Warning,
        Error
    }

    public sealed class EditorToolExecutionResult
    {
        private static readonly string[] EmptyDetails = Array.Empty<string>();

        public EditorToolExecutionResult(EditorToolExecutionStatus status, string message, IEnumerable<string> details = null)
        {
            Status = status;
            Message = message ?? string.Empty;
            Details = details?.Where(detail => !string.IsNullOrWhiteSpace(detail)).ToArray() ?? EmptyDetails;
        }

        public EditorToolExecutionStatus Status { get; }

        public string Message { get; }

        public IReadOnlyList<string> Details { get; }

        public bool Succeeded => Status != EditorToolExecutionStatus.Error;

        public static EditorToolExecutionResult Success(string message = "Done.", IEnumerable<string> details = null)
        {
            return new EditorToolExecutionResult(EditorToolExecutionStatus.Success, message, details);
        }

        public static EditorToolExecutionResult Warning(string message, IEnumerable<string> details = null)
        {
            return new EditorToolExecutionResult(EditorToolExecutionStatus.Warning, message, details);
        }

        public static EditorToolExecutionResult Error(string message, IEnumerable<string> details = null)
        {
            return new EditorToolExecutionResult(EditorToolExecutionStatus.Error, message, details);
        }
    }
}
