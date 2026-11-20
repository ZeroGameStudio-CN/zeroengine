using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ZeroEngine.AssetCatalog
{
    internal static class CatalogWindowsCredentials
    {
        private const uint CredentialTypeGeneric = 1;
        private const uint CredentialPersistLocalMachine = 2;
        private const int ErrorNotFound = 1168;

        internal static void Write(string targetName, string secret, Encoding encoding, string description)
        {
            byte[] bytes = encoding.GetBytes(secret);
            IntPtr blob = IntPtr.Zero;
            try
            {
                blob = Marshal.AllocCoTaskMem(bytes.Length);
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
                if (!CredWrite(ref credential, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not save the {description}.");
            }
            finally
            {
                if (blob != IntPtr.Zero)
                {
                    for (int index = 0; index < bytes.Length; index++) Marshal.WriteByte(blob, index, 0);
                    Marshal.FreeCoTaskMem(blob);
                }
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        internal static bool TryRead(string targetName, Encoding encoding, string description, out string secret)
        {
            secret = null;
            if (!CredRead(targetName, CredentialTypeGeneric, 0, out IntPtr pointer))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ErrorNotFound) return false;
                throw new Win32Exception(error, $"Could not read the {description}.");
            }

            byte[] bytes = Array.Empty<byte>();
            try
            {
                NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(pointer);
                bytes = new byte[credential.CredentialBlobSize];
                if (bytes.Length > 0) Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                secret = encoding.GetString(bytes);
                return !string.IsNullOrWhiteSpace(secret);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
                CredFree(pointer);
            }
        }

        internal static void Delete(string targetName, string description)
        {
            if (CredDelete(targetName, CredentialTypeGeneric, 0)) return;
            int error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound) throw new Win32Exception(error, $"Could not delete the {description}.");
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
    }
}
