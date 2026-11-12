using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace ZeroEngine.ParticleCatalog
{
    public static class ParticleCatalogCredentialStore
    {
        public const string TargetName = "ZeroEngine.ParticleCatalog.DeepSeekApiKey";
        private const int CredentialTypeGeneric = 1;
        private const int CredentialPersistLocalMachine = 2;
        private const int ErrorNotFound = 1168;
        private static string _sessionApiKey;

        public static void SetApiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("API Key is required.", nameof(apiKey));
            if (!IsWindowsEditor)
            {
                _sessionApiKey = apiKey;
                return;
            }

            IntPtr blob = Marshal.StringToCoTaskMemUni(apiKey);
            try
            {
                NativeCredential credential = new NativeCredential
                {
                    Type = CredentialTypeGeneric,
                    TargetName = TargetName,
                    CredentialBlobSize = (uint)Encoding.Unicode.GetByteCount(apiKey),
                    CredentialBlob = blob,
                    Persist = CredentialPersistLocalMachine,
                    UserName = Environment.UserName
                };
                if (!CredWrite(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not save the personal DeepSeek API Key.");
            }
            finally
            {
                Marshal.ZeroFreeCoTaskMemUnicode(blob);
            }
        }

        public static bool TryGetApiKey(out string apiKey)
        {
            if (!IsWindowsEditor)
            {
                apiKey = _sessionApiKey;
                return !string.IsNullOrWhiteSpace(apiKey);
            }

            apiKey = null;
            if (!CredRead(TargetName, CredentialTypeGeneric, 0, out IntPtr pointer))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ErrorNotFound) return false;
                throw new Win32Exception(error, "Could not read the personal DeepSeek API Key.");
            }
            try
            {
                NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(pointer);
                apiKey = Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
                return !string.IsNullOrWhiteSpace(apiKey);
            }
            finally
            {
                CredFree(pointer);
            }
        }

        public static void DeleteApiKey()
        {
            _sessionApiKey = null;
            if (!IsWindowsEditor) return;
            if (CredDelete(TargetName, CredentialTypeGeneric, 0)) return;
            int error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound) throw new Win32Exception(error, "Could not delete the personal DeepSeek API Key.");
        }

        private static bool IsWindowsEditor => Application.platform == RuntimePlatform.WindowsEditor;

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
    }
}
