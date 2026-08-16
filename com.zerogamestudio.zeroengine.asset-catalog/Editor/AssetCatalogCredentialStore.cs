using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace ZeroEngine.AssetCatalog
{
    [Serializable]
    public sealed class AssetCatalogPersonalAiSettings
    {
        public string provider = "DeepSeek";
        public string endpoint = "https://api.deepseek.com";
        public string model = "deepseek-v4-flash";
    }

    public static class AssetCatalogCredentialStore
    {
        private const int CredentialTypeGeneric = 1;
        private const int CredentialPersistLocalMachine = 2;
        private const int ErrorNotFound = 1168;
        private const int SecItemNotFound = -25300;
        private const int SecDuplicateItem = -25299;
        private static readonly Dictionary<string, string> SessionFallback = new Dictionary<string, string>(StringComparer.Ordinal);

        public static string CatalogTokenTarget(string endpoint, string projectId)
        {
            if (!AssetCatalogContracts.IsEndpointAllowed(endpoint)) throw new ArgumentException("Catalog endpoint must use HTTPS unless it is loopback HTTP.", nameof(endpoint));
            if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentException("projectId is required.", nameof(projectId));
            return "ZeroEngine.AssetCatalog.AccessToken." + EndpointHash(endpoint) + "." + projectId.Trim();
        }

        public static string PersonalAiKeyTarget(string provider, string endpoint)
        {
            if (!AssetCatalogContracts.IsEndpointAllowed(endpoint)) throw new ArgumentException("AI endpoint must use HTTPS unless it is loopback HTTP.", nameof(endpoint));
            if (string.IsNullOrWhiteSpace(provider)) throw new ArgumentException("provider is required.", nameof(provider));
            return "ZeroEngine.AssetCatalog.Ai." + NormalizeName(provider) + "." + EndpointHash(endpoint);
        }

        public static void SetCatalogToken(string endpoint, string projectId, string token) => SetSecret(CatalogTokenTarget(endpoint, projectId), token);
        public static bool TryGetCatalogToken(string endpoint, string projectId, out string token) => TryGetSecret(CatalogTokenTarget(endpoint, projectId), out token);
        public static void DeleteCatalogToken(string endpoint, string projectId) => DeleteSecret(CatalogTokenTarget(endpoint, projectId));
        public static void SetPersonalAiKey(string provider, string endpoint, string apiKey) => SetSecret(PersonalAiKeyTarget(provider, endpoint), apiKey);
        public static bool TryGetPersonalAiKey(string provider, string endpoint, out string apiKey) => TryGetSecret(PersonalAiKeyTarget(provider, endpoint), out apiKey);
        public static void DeletePersonalAiKey(string provider, string endpoint) => DeleteSecret(PersonalAiKeyTarget(provider, endpoint));

        public static void SetSecret(string targetName, string secret)
        {
            if (string.IsNullOrWhiteSpace(targetName)) throw new ArgumentException("targetName is required.", nameof(targetName));
            if (string.IsNullOrWhiteSpace(secret)) throw new ArgumentException("secret is required.", nameof(secret));
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                SetWindowsSecret(targetName, secret);
                return;
            }
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                SetMacSecret(targetName, secret);
                return;
            }
            SessionFallback[targetName] = secret;
        }

        public static bool TryGetSecret(string targetName, out string secret)
        {
            if (string.IsNullOrWhiteSpace(targetName)) throw new ArgumentException("targetName is required.", nameof(targetName));
            if (Application.platform == RuntimePlatform.WindowsEditor) return TryGetWindowsSecret(targetName, out secret);
            if (Application.platform == RuntimePlatform.OSXEditor) return TryGetMacSecret(targetName, out secret);
            return SessionFallback.TryGetValue(targetName, out secret) && !string.IsNullOrWhiteSpace(secret);
        }

        public static void DeleteSecret(string targetName)
        {
            if (string.IsNullOrWhiteSpace(targetName)) throw new ArgumentException("targetName is required.", nameof(targetName));
            SessionFallback.Remove(targetName);
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                if (!CredDelete(targetName, CredentialTypeGeneric, 0))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != ErrorNotFound) throw new Win32Exception(error, "Could not delete the personal catalog credential.");
                }
            }
            else if (Application.platform == RuntimePlatform.OSXEditor)
            {
                DeleteMacSecret(targetName);
            }
        }

        private static string EndpointHash(string endpoint)
        {
            string normalized = endpoint.Trim().TrimEnd('/').ToLowerInvariant();
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] bytes = algorithm.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                StringBuilder builder = new StringBuilder(bytes.Length * 2);
                foreach (byte value in bytes) builder.Append(value.ToString("x2"));
                return builder.ToString();
            }
        }

        private static string NormalizeName(string value)
        {
            StringBuilder builder = new StringBuilder();
            foreach (char character in value.Trim().ToLowerInvariant()) builder.Append(char.IsLetterOrDigit(character) ? character : '-');
            return builder.ToString().Trim('-');
        }

        private static void SetWindowsSecret(string targetName, string secret)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(secret);
            IntPtr blob = Marshal.AllocCoTaskMem(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                NativeCredential credential = new NativeCredential
                {
                    Type = CredentialTypeGeneric,
                    TargetName = targetName,
                    CredentialBlobSize = (uint)bytes.Length,
                    CredentialBlob = blob,
                    Persist = CredentialPersistLocalMachine,
                    UserName = Environment.UserName
                };
                if (!CredWrite(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not save the personal catalog credential.");
            }
            finally
            {
                ZeroAndFree(blob, bytes.Length);
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        private static bool TryGetWindowsSecret(string targetName, out string secret)
        {
            secret = null;
            if (!CredRead(targetName, CredentialTypeGeneric, 0, out IntPtr pointer))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ErrorNotFound) return false;
                throw new Win32Exception(error, "Could not read the personal catalog credential.");
            }
            try
            {
                NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(pointer);
                byte[] bytes = new byte[credential.CredentialBlobSize];
                if (bytes.Length > 0) Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                secret = Encoding.UTF8.GetString(bytes);
                Array.Clear(bytes, 0, bytes.Length);
                return !string.IsNullOrWhiteSpace(secret);
            }
            finally
            {
                CredFree(pointer);
            }
        }

        private static void SetMacSecret(string targetName, string secret)
        {
            byte[] service = Encoding.UTF8.GetBytes(targetName);
            byte[] account = Encoding.UTF8.GetBytes(Environment.UserName);
            byte[] password = Encoding.UTF8.GetBytes(secret);
            try
            {
                int result = SecKeychainAddGenericPassword(IntPtr.Zero, (uint)service.Length, service, (uint)account.Length, account, (uint)password.Length, password, out IntPtr item);
                if (result == SecDuplicateItem)
                {
                    result = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)service.Length, service, (uint)account.Length, account, out _, out _, out item);
                    if (result == 0) result = SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)password.Length, password);
                }
                if (item != IntPtr.Zero) CFRelease(item);
                if (result != 0) throw new InvalidOperationException("Could not save the personal catalog credential in macOS Keychain.");
            }
            finally
            {
                Array.Clear(service, 0, service.Length);
                Array.Clear(account, 0, account.Length);
                Array.Clear(password, 0, password.Length);
            }
        }

        private static bool TryGetMacSecret(string targetName, out string secret)
        {
            secret = null;
            byte[] service = Encoding.UTF8.GetBytes(targetName);
            byte[] account = Encoding.UTF8.GetBytes(Environment.UserName);
            IntPtr passwordData = IntPtr.Zero;
            IntPtr item = IntPtr.Zero;
            try
            {
                int result = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)service.Length, service, (uint)account.Length, account, out uint length, out passwordData, out item);
                if (result == SecItemNotFound) return false;
                if (result != 0) throw new InvalidOperationException("Could not read the personal catalog credential from macOS Keychain.");
                byte[] password = new byte[length];
                if (password.Length > 0) Marshal.Copy(passwordData, password, 0, password.Length);
                secret = Encoding.UTF8.GetString(password);
                Array.Clear(password, 0, password.Length);
                return !string.IsNullOrWhiteSpace(secret);
            }
            finally
            {
                if (passwordData != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
                if (item != IntPtr.Zero) CFRelease(item);
                Array.Clear(service, 0, service.Length);
                Array.Clear(account, 0, account.Length);
            }
        }

        private static void DeleteMacSecret(string targetName)
        {
            byte[] service = Encoding.UTF8.GetBytes(targetName);
            byte[] account = Encoding.UTF8.GetBytes(Environment.UserName);
            try
            {
                int result = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)service.Length, service, (uint)account.Length, account, out _, out IntPtr passwordData, out IntPtr item);
                if (passwordData != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
                if (result == SecItemNotFound) return;
                if (result != 0 || item == IntPtr.Zero) throw new InvalidOperationException("Could not locate the personal catalog credential in macOS Keychain.");
                try
                {
                    if (SecKeychainItemDelete(item) != 0) throw new InvalidOperationException("Could not delete the personal catalog credential from macOS Keychain.");
                }
                finally
                {
                    CFRelease(item);
                }
            }
            finally
            {
                Array.Clear(service, 0, service.Length);
                Array.Clear(account, 0, account.Length);
            }
        }

        private static void ZeroAndFree(IntPtr pointer, int length)
        {
            if (pointer == IntPtr.Zero) return;
            for (int index = 0; index < length; index++) Marshal.WriteByte(pointer, index, 0);
            Marshal.FreeCoTaskMem(pointer);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public uint Flags;
            public uint Type;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
            [MarshalAs(UnmanagedType.LPWStr)] public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetAlias;
            [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite([In] ref NativeCredential credential, uint flags);
        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, uint type, uint flags);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern void CredFree(IntPtr credential);

        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName, uint passwordLength, byte[] passwordData, out IntPtr itemRef);
        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        private static extern int SecKeychainFindGenericPassword(IntPtr keychainOrArray, uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName, out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);
        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        private static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length, byte[] data);
        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);
        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        private static extern int SecKeychainItemDelete(IntPtr itemRef);
        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern void CFRelease(IntPtr cf);
    }
}
