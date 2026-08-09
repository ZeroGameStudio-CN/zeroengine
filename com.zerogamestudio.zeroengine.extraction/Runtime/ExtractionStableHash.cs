using System;
using System.Security.Cryptography;
using System.Text;

namespace POB.Extraction
{
    /// <summary>
    /// Cross-process deterministic hashing for persisted gameplay decisions.
    /// Fields are UTF-8 length-prefixed so null, empty and field boundaries remain distinct.
    /// </summary>
    public static class ExtractionStableHash
    {
        private const string CanonicalPrefix = "zeroengine.extraction.stable-hash:v1;";

        public static string ComputeSha256(string domain, params string[] values)
        {
            byte[] digest = ComputeDigest(domain, values);
            var builder = new StringBuilder("sha256:", 7 + digest.Length * 2);
            foreach (byte value in digest)
                builder.Append(value.ToString("x2"));
            return builder.ToString();
        }

        public static int ComputeInt32(string domain, params string[] values)
        {
            byte[] digest = ComputeDigest(domain, values);
            uint value = ((uint)digest[0] << 24)
                         | ((uint)digest[1] << 16)
                         | ((uint)digest[2] << 8)
                         | digest[3];
            return unchecked((int)value);
        }

        private static byte[] ComputeDigest(string domain, string[] values)
        {
            if (string.IsNullOrEmpty(domain))
                throw new ArgumentException("A stable-hash domain is required.", nameof(domain));

            values ??= Array.Empty<string>();
            var canonical = new StringBuilder(CanonicalPrefix);
            canonical.Append(values.Length + 1).Append(';');
            AppendField(canonical, domain);
            foreach (string value in values)
                AppendField(canonical, value);

            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
        }

        private static void AppendField(StringBuilder builder, string value)
        {
            if (value == null)
            {
                builder.Append("-1:");
                return;
            }

            builder.Append(Encoding.UTF8.GetByteCount(value));
            builder.Append(':');
            builder.Append(value);
        }
    }

    public static class ExtractionOperationId
    {
        private const string HashDomain = "zeroengine.extraction.operation-id:v1";
        private const string Prefix = "operation:v1:";

        public static string Create(string operationType, params string[] identityParts)
        {
            if (string.IsNullOrEmpty(operationType))
                throw new ArgumentException("An operation type is required.", nameof(operationType));

            identityParts ??= Array.Empty<string>();
            var hashParts = new string[identityParts.Length + 1];
            hashParts[0] = operationType;
            Array.Copy(identityParts, 0, hashParts, 1, identityParts.Length);
            string hash = ExtractionStableHash.ComputeSha256(HashDomain, hashParts);
            return Prefix + hash.Substring("sha256:".Length);
        }
    }

    public static class ExtractionReceiptId
    {
        private const string HashDomain = "zeroengine.extraction.receipt-id:v1";
        private const string Prefix = "receipt:v1:";

        public static string Create(string operationId, string receiptType)
        {
            if (string.IsNullOrEmpty(operationId))
                throw new ArgumentException("An operation id is required.", nameof(operationId));
            if (string.IsNullOrEmpty(receiptType))
                throw new ArgumentException("A receipt type is required.", nameof(receiptType));

            string hash = ExtractionStableHash.ComputeSha256(HashDomain, operationId, receiptType);
            return Prefix + hash.Substring("sha256:".Length);
        }
    }
}
