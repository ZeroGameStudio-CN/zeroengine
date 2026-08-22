using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.Localization
{
    /// <summary>
    /// Provider-agnostic asynchronous localization orchestration. Provider
    /// failures become results, while cancellation is never presented as a
    /// successful locale change.
    /// </summary>
    public sealed class LocalizedTextService : ILocalizedTextService, IDisposable
    {
        private readonly ILocalizationProvider _provider;
        private readonly LocaleFallbackPolicy _fallbackPolicy;
        private readonly string[] _requiredTables;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private bool _isReady;
        private bool _disposed;
        private string _currentLocale;

        public LocalizedTextService(
            ILocalizationProvider provider,
            LocalizationServiceOptions options = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            options = options ?? new LocalizationServiceOptions();
            _fallbackPolicy = new LocaleFallbackPolicy(options.DefaultLocale, options.FallbackLocales);
            _requiredTables = Copy(options.RequiredTables);
        }

        public bool IsReady => _isReady;

        public string CurrentLocale => _currentLocale;

        public string CurrentLocaleCode => _currentLocale;

        public LocaleFallbackPolicy FallbackPolicy => _fallbackPolicy;

        public event Action<LocalizationChangedEventArgs> LocaleChanged;

        /// <summary>
        /// Compatibility event for consumers that only need an invalidation
        /// signal. LocaleChanged carries the structured transition details.
        /// </summary>
        public event Action LanguageChanged;

        public async Task<LocalizationResult> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            const string requestedLocale = "";
            if (_disposed)
            {
                return LocalizationResult.Failed(requestedLocale, "disposed");
            }

            bool entered = false;
            try
            {
                await _gate.WaitAsync(cancellationToken);
                entered = true;
                if (_isReady)
                {
                    return LocalizationResult.Succeeded(_currentLocale, _currentLocale, false);
                }

                LocalizationProviderResult providerResult = await InitializeProviderAsync(cancellationToken)
                    ;
                cancellationToken.ThrowIfCancellationRequested();
                if (providerResult.IsCancelled)
                {
                    return LocalizationResult.Cancelled(requestedLocale, providerResult.Error, providerResult.Exception);
                }

                if (!providerResult.Success)
                {
                    return LocalizationResult.Failed(
                        requestedLocale,
                        providerResult.Error ?? "provider-initialize-failed",
                        providerResult.Exception);
                }

                LocalizationResult result = await LoadLocaleAsync(
                    _fallbackPolicy.DefaultLocale,
                    cancellationToken);
                if (result.Success)
                {
                    _currentLocale = result.EffectiveLocale;
                    _isReady = true;
                }

                return result;
            }
            catch (OperationCanceledException exception)
            {
                return LocalizationResult.Cancelled(requestedLocale, "cancelled", exception);
            }
            catch (Exception exception)
            {
                return LocalizationResult.Failed(requestedLocale, "initialize-failed", exception);
            }
            finally
            {
                if (entered)
                {
                    _gate.Release();
                }
            }
        }

        public async Task<LocalizationResult> SetLocaleAsync(
            string localeCode,
            CancellationToken cancellationToken = default)
        {
            string requestedLocale = LocaleCode.Normalize(localeCode);
            if (_disposed)
            {
                return LocalizationResult.Failed(requestedLocale, "disposed");
            }

            bool entered = false;
            try
            {
                await _gate.WaitAsync(cancellationToken);
                entered = true;
                if (!_isReady)
                {
                    return LocalizationResult.NotReady(requestedLocale);
                }

                string previousLocale = _currentLocale;
                LocalizationResult result = await LoadLocaleAsync(localeCode, cancellationToken)
                    ;
                if (!result.Success)
                {
                    return result;
                }

                _currentLocale = result.EffectiveLocale;
                if (!LocaleCode.Equals(previousLocale, _currentLocale))
                {
                    PublishLocaleChanged(new LocalizationChangedEventArgs(
                        previousLocale,
                        requestedLocale,
                        _currentLocale,
                        result.UsedFallback));
                }

                return result;
            }
            catch (OperationCanceledException exception)
            {
                return LocalizationResult.Cancelled(requestedLocale, "cancelled", exception);
            }
            catch (Exception exception)
            {
                return LocalizationResult.Failed(requestedLocale, "locale-switch-failed", exception);
            }
            finally
            {
                if (entered)
                {
                    _gate.Release();
                }
            }
        }

        public async Task<LocalizedTextResult> GetStringAsync(
            string tableName,
            string key,
            CancellationToken cancellationToken = default)
        {
            return await GetStringCoreAsync(
                tableName,
                key,
                null,
                cancellationToken);
        }

        public async Task<LocalizedTextResult> GetStringAsync(
            string tableName,
            string key,
            IReadOnlyList<object> arguments,
            CancellationToken cancellationToken = default)
        {
            return await GetStringCoreAsync(
                tableName,
                key,
                arguments,
                cancellationToken);
        }

        public Task<LocalizedTextResult> GetFormattedStringAsync(
            string tableName,
            string key,
            IReadOnlyList<object> arguments,
            CancellationToken cancellationToken = default)
        {
            return GetStringAsync(tableName, key, arguments, cancellationToken);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _gate.Dispose();
            LocaleChanged = null;
            LanguageChanged = null;
        }

        private async Task<LocalizedTextResult> GetStringCoreAsync(
            string tableName,
            string key,
            IReadOnlyList<object> arguments,
            CancellationToken cancellationToken)
        {
            string locale = _currentLocale;
            string placeholder = MissingKeyFormatter.Format(key);
            if (_disposed)
            {
                return LocalizedTextResult.Failed(locale, tableName, key, placeholder, "disposed");
            }

            if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(key))
            {
                return LocalizedTextResult.Missing(locale, tableName, key, placeholder);
            }

            bool entered = false;
            try
            {
                await _gate.WaitAsync(cancellationToken);
                entered = true;
                locale = _currentLocale;
                placeholder = MissingKeyFormatter.Format(key);
                if (!_isReady)
                {
                    return LocalizedTextResult.NotReady(locale, tableName, key, placeholder);
                }

                LocalizationProviderTextResult providerResult = await GetProviderTextAsync(
                    locale,
                    tableName,
                    key,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (providerResult.IsCancelled)
                {
                    return LocalizedTextResult.Cancelled(
                        locale,
                        tableName,
                        key,
                        placeholder,
                        providerResult.Error,
                        providerResult.Exception);
                }

                if (providerResult.IsMissing || !providerResult.Success ||
                    string.IsNullOrEmpty(providerResult.Value))
                {
                    return providerResult.IsMissing
                        ? LocalizedTextResult.Missing(locale, tableName, key, placeholder)
                        : LocalizedTextResult.Failed(
                            locale,
                            tableName,
                            key,
                            placeholder,
                            providerResult.Error ?? "provider-get-failed",
                            providerResult.Exception);
                }

                string value = providerResult.Value;
                LocalizationFormatDiagnostic diagnostic;
                if (!LocalizationFormatter.TryFormat(value, arguments, out value, out diagnostic))
                {
                    return LocalizedTextResult.FormatFailed(
                        locale,
                        tableName,
                        key,
                        placeholder,
                        diagnostic);
                }

                return LocalizedTextResult.Found(locale, tableName, key, value);
            }
            catch (OperationCanceledException exception)
            {
                return LocalizedTextResult.Cancelled(locale, tableName, key, placeholder, "cancelled", exception);
            }
            catch (Exception exception)
            {
                return LocalizedTextResult.Failed(locale, tableName, key, placeholder, "get-string-failed", exception);
            }
            finally
            {
                if (entered)
                {
                    _gate.Release();
                }
            }
        }

        private async Task<LocalizationResult> LoadLocaleAsync(
            string requestedLocale,
            CancellationToken cancellationToken)
        {
            LocaleResolution resolution = _fallbackPolicy.Resolve(
                requestedLocale,
                _provider.AvailableLocales);
            if (!resolution.IsResolved)
            {
                return LocalizationResult.Failed(
                    resolution.RequestedLocale,
                    "locale-not-found");
            }

            string lastError = "locale-preload-failed";
            Exception lastException = null;
            for (int i = 0; i < resolution.Candidates.Count; i++)
            {
                string candidate = resolution.Candidates[i];
                LocalizationProviderResult preload = await PreloadProviderAsync(
                    candidate,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (preload.IsCancelled)
                {
                    return LocalizationResult.Cancelled(
                        resolution.RequestedLocale,
                        preload.Error,
                        preload.Exception);
                }

                if (preload.Success)
                {
                    return LocalizationResult.Succeeded(
                        resolution.RequestedLocale,
                        candidate,
                        !LocaleCode.Equals(resolution.RequestedLocale, candidate));
                }

                lastError = preload.Error ?? lastError;
                lastException = preload.Exception;
            }

            return LocalizationResult.Failed(
                resolution.RequestedLocale,
                lastError,
                lastException);
        }

        private async Task<LocalizationProviderResult> InitializeProviderAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                Task<LocalizationProviderResult> task = _provider.InitializeAsync(cancellationToken);
                return task == null
                    ? LocalizationProviderResult.Failed("provider-null-task")
                    : await task;
            }
            catch (OperationCanceledException exception)
            {
                return LocalizationProviderResult.Cancelled("cancelled", exception);
            }
            catch (Exception exception)
            {
                return LocalizationProviderResult.Failed("provider-initialize-exception", exception);
            }
        }

        private async Task<LocalizationProviderResult> PreloadProviderAsync(
            string localeCode,
            CancellationToken cancellationToken)
        {
            try
            {
                Task<LocalizationProviderResult> task = _provider.PreloadAsync(
                    localeCode,
                    _requiredTables,
                    cancellationToken);
                return task == null
                    ? LocalizationProviderResult.Failed("provider-null-task")
                    : await task;
            }
            catch (OperationCanceledException exception)
            {
                return LocalizationProviderResult.Cancelled("cancelled", exception);
            }
            catch (Exception exception)
            {
                return LocalizationProviderResult.Failed("provider-preload-exception", exception);
            }
        }

        private async Task<LocalizationProviderTextResult> GetProviderTextAsync(
            string localeCode,
            string tableName,
            string key,
            CancellationToken cancellationToken)
        {
            try
            {
                Task<LocalizationProviderTextResult> task = _provider.GetTextAsync(
                    localeCode,
                    tableName,
                    key,
                    cancellationToken);
                return task == null
                    ? LocalizationProviderTextResult.Failed("provider-null-task")
                    : await task;
            }
            catch (OperationCanceledException exception)
            {
                return LocalizationProviderTextResult.Cancelled("cancelled", exception);
            }
            catch (Exception exception)
            {
                return LocalizationProviderTextResult.Failed("provider-get-exception", exception);
            }
        }

        private void PublishLocaleChanged(LocalizationChangedEventArgs eventArgs)
        {
            Action<LocalizationChangedEventArgs> handlers = LocaleChanged;
            if (handlers != null)
            {
                Delegate[] invocationList = handlers.GetInvocationList();
                for (int i = 0; i < invocationList.Length; i++)
                {
                    try
                    {
                        ((Action<LocalizationChangedEventArgs>)invocationList[i])(eventArgs);
                    }
                    catch
                    {
                        // Subscriber failures must not turn a committed locale
                        // switch into a reported failure or a duplicate event.
                    }
                }
            }

            Action languageChanged = LanguageChanged;
            if (languageChanged == null)
            {
                return;
            }

            Delegate[] languageInvocationList = languageChanged.GetInvocationList();
            for (int i = 0; i < languageInvocationList.Length; i++)
            {
                try
                {
                    ((Action)languageInvocationList[i])();
                }
                catch
                {
                    // Keep notification failures isolated from the committed
                    // locale state and from other subscribers.
                }
            }
        }

        private static string[] Copy(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return Array.Empty<string>();
            }

            var copy = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                copy[i] = values[i];
            }

            return copy;
        }
    }
}
