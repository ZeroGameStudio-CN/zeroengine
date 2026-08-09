using System;
using System.IO;
using System.Security.Cryptography;

namespace ZeroGameStudio.ConfigPipeline
{
    public static class ConfigHash
    {
        public static string Sha256(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            using (SHA256 sha256 = SHA256.Create())
            {
                return ToLowerHex(sha256.ComputeHash(bytes));
            }
        }

        public static string Sha256(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using (SHA256 sha256 = SHA256.Create())
            {
                return ToLowerHex(sha256.ComputeHash(stream));
            }
        }

        private static string ToLowerHex(byte[] bytes)
        {
            char[] characters = new char[bytes.Length * 2];
            const string alphabet = "0123456789abcdef";
            for (int index = 0; index < bytes.Length; index++)
            {
                characters[index * 2] = alphabet[bytes[index] >> 4];
                characters[index * 2 + 1] = alphabet[bytes[index] & 0x0f];
            }

            return new string(characters);
        }
    }
}
