using System;
using System.Collections.Generic;

namespace ZeroEngine.Multiplayer
{
    public readonly struct OperationResult
    {
        private static readonly IReadOnlyList<string> EmptyArguments = Array.Empty<string>();
        private readonly string _messageKey;
        private readonly IReadOnlyList<string> _messageArguments;

        private OperationResult(
            bool succeeded,
            MultiplayerErrorCode errorCode,
            string messageKey,
            IReadOnlyList<string> messageArguments)
        {
            Succeeded = succeeded;
            ErrorCode = errorCode;
            _messageKey = messageKey ?? string.Empty;
            _messageArguments = messageArguments ?? EmptyArguments;
        }

        public bool Succeeded { get; }
        public MultiplayerErrorCode ErrorCode { get; }
        public string MessageKey => _messageKey ?? string.Empty;
        public IReadOnlyList<string> MessageArguments => _messageArguments ?? EmptyArguments;

        public static OperationResult Success()
        {
            return new OperationResult(true, MultiplayerErrorCode.None, string.Empty, EmptyArguments);
        }

        public static OperationResult Failure(
            MultiplayerErrorCode errorCode,
            string messageKey,
            params string[] messageArguments)
        {
            if (errorCode == MultiplayerErrorCode.None)
            {
                throw new ArgumentException("A failed operation must have an error code.", nameof(errorCode));
            }

            string[] arguments = messageArguments == null
                ? Array.Empty<string>()
                : (string[])messageArguments.Clone();
            return new OperationResult(false, errorCode, messageKey, Array.AsReadOnly(arguments));
        }
    }

    public readonly struct OperationResult<T>
    {
        private OperationResult(OperationResult result, T value)
        {
            Result = result;
            Value = value;
        }

        public OperationResult Result { get; }
        public T Value { get; }
        public bool Succeeded => Result.Succeeded;
        public MultiplayerErrorCode ErrorCode => Result.ErrorCode;
        public string MessageKey => Result.MessageKey;
        public IReadOnlyList<string> MessageArguments => Result.MessageArguments;

        public static OperationResult<T> Success(T value)
        {
            return new OperationResult<T>(OperationResult.Success(), value);
        }

        public static OperationResult<T> Failure(
            MultiplayerErrorCode errorCode,
            string messageKey,
            params string[] messageArguments)
        {
            return new OperationResult<T>(
                OperationResult.Failure(errorCode, messageKey, messageArguments),
                default(T));
        }

        public static OperationResult<T> FromFailure(OperationResult result)
        {
            if (result.Succeeded)
            {
                throw new ArgumentException("A generic failure cannot be created from a successful result.", nameof(result));
            }

            return new OperationResult<T>(result, default(T));
        }
    }
}
