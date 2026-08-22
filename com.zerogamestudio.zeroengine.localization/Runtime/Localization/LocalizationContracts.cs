using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.Localization
{
    public enum LocalizationOperationStatus
    {
        Succeeded,
        Failed,
        Cancelled,
        NotReady,
        Missing,
        FormatFailed
    }

    public enum LocalizationProviderStatus
    {
        Succeeded,
        Failed,
        Cancelled,
        Missing
    }

    public readonly struct LocalizationProviderResult
    {
        private LocalizationProviderResult(LocalizationProviderStatus status, string error, Exception exception)
        {
            Status = status;
            Error = error;
            Exception = exception;
        }

        public LocalizationProviderStatus Status { get; }
        public bool Success => Status == LocalizationProviderStatus.Succeeded;
        public bool IsCancelled => Status == LocalizationProviderStatus.Cancelled;
        public string Error { get; }
        public Exception Exception { get; }

        public static LocalizationProviderResult Succeeded() =>
            new LocalizationProviderResult(LocalizationProviderStatus.Succeeded, null, null);

        public static LocalizationProviderResult Failed(string error, Exception exception = null) =>
            new LocalizationProviderResult(LocalizationProviderStatus.Failed, error, exception);

        public static LocalizationProviderResult Cancelled(string error = "cancelled", Exception exception = null) =>
            new LocalizationProviderResult(LocalizationProviderStatus.Cancelled, error, exception);
    }

    public readonly struct LocalizationProviderTextResult
    {
        private LocalizationProviderTextResult(
            LocalizationProviderStatus status,
            string value,
            string error,
            Exception exception)
        {
            Status = status;
            Value = value;
            Error = error;
            Exception = exception;
        }

        public LocalizationProviderStatus Status { get; }
        public bool Success => Status == LocalizationProviderStatus.Succeeded;
        public bool IsMissing => Status == LocalizationProviderStatus.Missing;
        public bool IsCancelled => Status == LocalizationProviderStatus.Cancelled;
        public string Value { get; }
        public string Error { get; }
        public Exception Exception { get; }

        public static LocalizationProviderTextResult Found(string value) =>
            new LocalizationProviderTextResult(LocalizationProviderStatus.Succeeded, value, null, null);

        public static LocalizationProviderTextResult Missing(string error = "missing") =>
            new LocalizationProviderTextResult(LocalizationProviderStatus.Missing, null, error, null);

        public static LocalizationProviderTextResult Failed(string error, Exception exception = null) =>
            new LocalizationProviderTextResult(LocalizationProviderStatus.Failed, null, error, exception);

        public static LocalizationProviderTextResult Cancelled(string error = "cancelled", Exception exception = null) =>
            new LocalizationProviderTextResult(LocalizationProviderStatus.Cancelled, null, error, exception);
    }

    /// <summary>
    /// Provider boundary for Unity Localization, an in-memory table, or any
    /// other backend. Loading/preloading is asynchronous by contract.
    /// </summary>
    public interface ILocalizationProvider
    {
        IReadOnlyList<string> AvailableLocales { get; }

        Task<LocalizationProviderResult> InitializeAsync(CancellationToken cancellationToken);

        Task<LocalizationProviderResult> PreloadAsync(
            string localeCode,
            IReadOnlyList<string> requiredTables,
            CancellationToken cancellationToken);

        Task<LocalizationProviderTextResult> GetTextAsync(
            string localeCode,
            string tableName,
            string key,
            CancellationToken cancellationToken);
    }

    public static class LocalizationProviderExtensions
    {
        public static Task<LocalizationProviderTextResult> GetStringAsync(
            this ILocalizationProvider provider,
            string localeCode,
            string tableName,
            string key,
            CancellationToken cancellationToken = default)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            return provider.GetTextAsync(localeCode, tableName, key, cancellationToken);
        }
    }

    public sealed class DelegateLocalizationProvider : ILocalizationProvider
    {
        private readonly Func<CancellationToken, Task<LocalizationProviderResult>> _initialize;
        private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<LocalizationProviderResult>> _preload;
        private readonly Func<string, string, string, CancellationToken, Task<LocalizationProviderTextResult>> _getText;

        public DelegateLocalizationProvider(
            IReadOnlyList<string> availableLocales,
            Func<CancellationToken, Task<LocalizationProviderResult>> initialize,
            Func<string, IReadOnlyList<string>, CancellationToken, Task<LocalizationProviderResult>> preload,
            Func<string, string, string, CancellationToken, Task<LocalizationProviderTextResult>> getText)
        {
            AvailableLocales = availableLocales ?? Array.Empty<string>();
            _initialize = initialize ?? throw new ArgumentNullException(nameof(initialize));
            _preload = preload ?? throw new ArgumentNullException(nameof(preload));
            _getText = getText ?? throw new ArgumentNullException(nameof(getText));
        }

        public IReadOnlyList<string> AvailableLocales { get; }

        public Task<LocalizationProviderResult> InitializeAsync(CancellationToken cancellationToken)
        {
            return _initialize(cancellationToken) ?? Task.FromResult(LocalizationProviderResult.Succeeded());
        }

        public Task<LocalizationProviderResult> PreloadAsync(
            string localeCode,
            IReadOnlyList<string> requiredTables,
            CancellationToken cancellationToken)
        {
            return _preload(localeCode, requiredTables, cancellationToken) ??
                   Task.FromResult(LocalizationProviderResult.Succeeded());
        }

        public Task<LocalizationProviderTextResult> GetTextAsync(
            string localeCode,
            string tableName,
            string key,
            CancellationToken cancellationToken)
        {
            return _getText(localeCode, tableName, key, cancellationToken) ??
                   Task.FromResult(LocalizationProviderTextResult.Missing());
        }
    }

    public sealed class LocalizationServiceOptions
    {
        public string DefaultLocale { get; set; } = "en";
        public IReadOnlyList<string> FallbackLocales { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> RequiredTables { get; set; } = Array.Empty<string>();
    }

    public readonly struct LocalizationResult
    {
        private LocalizationResult(
            LocalizationOperationStatus status,
            string requestedLocale,
            string effectiveLocale,
            bool usedFallback,
            string error,
            Exception exception)
        {
            Status = status;
            RequestedLocale = requestedLocale;
            EffectiveLocale = effectiveLocale;
            UsedFallback = usedFallback;
            Error = error;
            Exception = exception;
        }

        public LocalizationOperationStatus Status { get; }
        public bool Success => Status == LocalizationOperationStatus.Succeeded;
        public bool IsCancelled => Status == LocalizationOperationStatus.Cancelled;
        public string RequestedLocale { get; }
        public string EffectiveLocale { get; }
        public bool UsedFallback { get; }
        public string Error { get; }
        public Exception Exception { get; }

        public static LocalizationResult Succeeded(
            string requestedLocale,
            string effectiveLocale,
            bool usedFallback) =>
            new LocalizationResult(
                LocalizationOperationStatus.Succeeded,
                requestedLocale,
                effectiveLocale,
                usedFallback,
                null,
                null);

        public static LocalizationResult Failed(
            string requestedLocale,
            string error,
            Exception exception = null) =>
            new LocalizationResult(
                LocalizationOperationStatus.Failed,
                requestedLocale,
                null,
                false,
                error,
                exception);

        public static LocalizationResult Cancelled(
            string requestedLocale,
            string error = "cancelled",
            Exception exception = null) =>
            new LocalizationResult(
                LocalizationOperationStatus.Cancelled,
                requestedLocale,
                null,
                false,
                error,
                exception);

        public static LocalizationResult NotReady(string requestedLocale) =>
            new LocalizationResult(
                LocalizationOperationStatus.NotReady,
                requestedLocale,
                null,
                false,
                "not-ready",
                null);
    }

    public readonly struct LocalizedTextResult
    {
        private LocalizedTextResult(
            LocalizationOperationStatus status,
            string localeCode,
            string tableName,
            string key,
            string value,
            string error,
            LocalizationFormatDiagnostic formatDiagnostic,
            Exception exception)
        {
            Status = status;
            LocaleCode = localeCode;
            TableName = tableName;
            Key = key;
            Value = value;
            Error = error;
            FormatDiagnostic = formatDiagnostic;
            Exception = exception;
        }

        public LocalizationOperationStatus Status { get; }
        public bool Success => Status == LocalizationOperationStatus.Succeeded;
        public bool IsFormatFailed => Status == LocalizationOperationStatus.FormatFailed;
        public bool IsMissing => Status == LocalizationOperationStatus.Missing;
        public bool IsCancelled => Status == LocalizationOperationStatus.Cancelled;
        public string LocaleCode { get; }
        public string TableName { get; }
        public string Key { get; }
        public string Value { get; }
        public string Error { get; }
        public LocalizationFormatDiagnostic FormatDiagnostic { get; }
        public Exception Exception { get; }

        public static LocalizedTextResult Found(
            string localeCode,
            string tableName,
            string key,
            string value) =>
            new LocalizedTextResult(
                LocalizationOperationStatus.Succeeded,
                localeCode,
                tableName,
                key,
                value,
                null,
                default(LocalizationFormatDiagnostic),
                null);

        public static LocalizedTextResult Missing(
            string localeCode,
            string tableName,
            string key,
            string value) =>
            new LocalizedTextResult(
                LocalizationOperationStatus.Missing,
                localeCode,
                tableName,
                key,
                value,
                "missing",
                default(LocalizationFormatDiagnostic),
                null);

        public static LocalizedTextResult Failed(
            string localeCode,
            string tableName,
            string key,
            string value,
            string error,
            Exception exception = null) =>
            new LocalizedTextResult(
                LocalizationOperationStatus.Failed,
                localeCode,
                tableName,
                key,
                value,
                error,
                default(LocalizationFormatDiagnostic),
                exception);

        public static LocalizedTextResult Cancelled(
            string localeCode,
            string tableName,
            string key,
            string value,
            string error = "cancelled",
            Exception exception = null) =>
            new LocalizedTextResult(
                LocalizationOperationStatus.Cancelled,
                localeCode,
                tableName,
                key,
                value,
                error,
                default(LocalizationFormatDiagnostic),
                exception);

        public static LocalizedTextResult NotReady(
            string localeCode,
            string tableName,
            string key,
            string value = null,
            string error = "not-ready") =>
            new LocalizedTextResult(
                LocalizationOperationStatus.NotReady,
                localeCode,
                tableName,
                key,
                value,
                error,
                default(LocalizationFormatDiagnostic),
                null);

        public static LocalizedTextResult FormatFailed(
            string localeCode,
            string tableName,
            string key,
            string value,
            LocalizationFormatDiagnostic diagnostic) =>
            new LocalizedTextResult(
                LocalizationOperationStatus.FormatFailed,
                localeCode,
                tableName,
                key,
                value,
                "format-failed",
                diagnostic,
                null);
    }

    public readonly struct LocalizationFormatDiagnostic
    {
        public LocalizationFormatDiagnostic(
            string code,
            int parameterCount,
            string exceptionType = null)
        {
            Code = code;
            ParameterCount = parameterCount;
            ExceptionType = exceptionType;
        }

        public string Code { get; }
        public int ParameterCount { get; }
        public string ExceptionType { get; }
        public bool IsValid => !string.IsNullOrEmpty(Code);
    }

    public sealed class LocalizationChangedEventArgs
    {
        public LocalizationChangedEventArgs(
            string previousLocale,
            string requestedLocale,
            string effectiveLocale,
            bool usedFallback)
        {
            PreviousLocale = previousLocale;
            RequestedLocale = requestedLocale;
            EffectiveLocale = effectiveLocale;
            UsedFallback = usedFallback;
        }

        public string PreviousLocale { get; }
        public string RequestedLocale { get; }
        public string EffectiveLocale { get; }
        public bool UsedFallback { get; }
    }

    public interface ILocalizedTextService
    {
        bool IsReady { get; }
        string CurrentLocale { get; }
        string CurrentLocaleCode { get; }
        event Action<LocalizationChangedEventArgs> LocaleChanged;
        event Action LanguageChanged;

        Task<LocalizationResult> InitializeAsync(CancellationToken cancellationToken = default);

        Task<LocalizationResult> SetLocaleAsync(
            string localeCode,
            CancellationToken cancellationToken = default);

        Task<LocalizedTextResult> GetStringAsync(
            string tableName,
            string key,
            CancellationToken cancellationToken = default);

        Task<LocalizedTextResult> GetStringAsync(
            string tableName,
            string key,
            IReadOnlyList<object> arguments,
            CancellationToken cancellationToken = default);
    }
}
