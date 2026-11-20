using System;
using System.Text;
using UnityEngine;
using ZeroEngine.AssetCatalog;

namespace ZeroEngine.ParticleCatalog
{
    public static class ParticleCatalogCredentialStore
    {
        public const string TargetName = "ZeroEngine.ParticleCatalog.DeepSeekApiKey";
        private const string CredentialDescription = "personal DeepSeek API Key";
        private static string _sessionApiKey;

        public static void SetApiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("API Key is required.", nameof(apiKey));
            if (!IsWindowsEditor)
            {
                _sessionApiKey = apiKey;
                return;
            }

            CatalogWindowsCredentials.Write(TargetName, apiKey, Encoding.Unicode, CredentialDescription);
        }

        public static bool TryGetApiKey(out string apiKey)
        {
            if (!IsWindowsEditor)
            {
                apiKey = _sessionApiKey;
                return !string.IsNullOrWhiteSpace(apiKey);
            }

            return CatalogWindowsCredentials.TryRead(TargetName, Encoding.Unicode, CredentialDescription, out apiKey);
        }

        public static void DeleteApiKey()
        {
            _sessionApiKey = null;
            if (IsWindowsEditor) CatalogWindowsCredentials.Delete(TargetName, CredentialDescription);
        }

        private static bool IsWindowsEditor => Application.platform == RuntimePlatform.WindowsEditor;

    }
}
