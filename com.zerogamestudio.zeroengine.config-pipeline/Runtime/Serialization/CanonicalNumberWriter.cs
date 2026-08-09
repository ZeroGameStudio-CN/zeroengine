using System;
using System.Collections.Generic;
using System.Globalization;

namespace ZeroGameStudio.ConfigPipeline
{
    public static class CanonicalNumberWriter
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static string Write(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string Write(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Config numbers must be finite.");
            }

            if (value == 0f)
            {
                return "0";
            }

            return FindShortest(
                9,
                precision => value.ToString("G" + precision, Invariant),
                candidate => float.TryParse(
                    candidate,
                    NumberStyles.Float,
                    Invariant,
                    out float parsed) && HaveSameBits(value, parsed));
        }

        public static string Write(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Config numbers must be finite.");
            }

            if (value == 0d)
            {
                return "0";
            }

            return FindShortest(
                17,
                precision => value.ToString("G" + precision, Invariant),
                candidate => double.TryParse(
                    candidate,
                    NumberStyles.Float,
                    Invariant,
                    out double parsed) && HaveSameBits(value, parsed));
        }

        private static string FindShortest(
            int maximumPrecision,
            Func<int, string> format,
            Func<string, bool> roundTrips)
        {
            var candidates = new List<string>();
            for (int precision = 1; precision <= maximumPrecision; precision++)
            {
                string candidate = Normalize(format(precision));
                if (roundTrips(candidate))
                {
                    candidates.Add(candidate);
                }
            }

            if (candidates.Count == 0)
            {
                throw new InvalidOperationException("Could not produce a round-trip decimal representation.");
            }

            candidates.Sort(CompareCandidates);
            return candidates[0];
        }

        private static int CompareCandidates(string left, string right)
        {
            int lengthComparison = left.Length.CompareTo(right.Length);
            return lengthComparison != 0
                ? lengthComparison
                : string.CompareOrdinal(left, right);
        }

        private static string Normalize(string value)
        {
            string normalized = value.Replace('E', 'e');
            int exponentIndex = normalized.IndexOf('e');
            string mantissa = exponentIndex >= 0
                ? normalized.Substring(0, exponentIndex)
                : normalized;
            string exponent = exponentIndex >= 0
                ? normalized.Substring(exponentIndex + 1)
                : null;

            int decimalIndex = mantissa.IndexOf('.');
            if (decimalIndex >= 0)
            {
                mantissa = mantissa.TrimEnd('0').TrimEnd('.');
            }

            if (mantissa == "-0")
            {
                mantissa = "0";
            }

            if (exponent == null)
            {
                return mantissa;
            }

            int parsedExponent = int.Parse(
                exponent,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture);
            return mantissa + "e" + parsedExponent.ToString(CultureInfo.InvariantCulture);
        }

        private static bool HaveSameBits(float left, float right)
        {
            byte[] leftBytes = BitConverter.GetBytes(left);
            byte[] rightBytes = BitConverter.GetBytes(right);
            for (int index = 0; index < leftBytes.Length; index++)
            {
                if (leftBytes[index] != rightBytes[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HaveSameBits(double left, double right)
        {
            byte[] leftBytes = BitConverter.GetBytes(left);
            byte[] rightBytes = BitConverter.GetBytes(right);
            for (int index = 0; index < leftBytes.Length; index++)
            {
                if (leftBytes[index] != rightBytes[index])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
