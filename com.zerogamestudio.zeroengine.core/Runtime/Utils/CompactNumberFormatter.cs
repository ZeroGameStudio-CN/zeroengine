using System;
using System.Globalization;
using System.Numerics;

namespace ZeroEngine
{
    /// <summary>Formats counters and other UI values using K, M, B, T and scientific notation.</summary>
    public static class CompactNumberFormatter
    {
        private const decimal GroupSize = 1000m;
        private const long ScientificThreshold = 1_000_000_000_000_000L;
        private const string ScientificFormat = "0.##E+0";
        private static readonly string[] Suffixes = { "", "K", "M", "B", "T" };

        public static string Format(long value, IFormatProvider formatProvider = null)
        {
            if (value <= -ScientificThreshold || value >= ScientificThreshold)
                return value.ToString(ScientificFormat, formatProvider);

            return FormatCompact(value, NumberFormatInfo.GetInstance(formatProvider));
        }

        public static string Format(BigInteger value, IFormatProvider formatProvider = null)
        {
            if (value <= -ScientificThreshold || value >= ScientificThreshold)
                return value.ToString(ScientificFormat, formatProvider);

            return FormatCompact((decimal)value, NumberFormatInfo.GetInstance(formatProvider));
        }

        public static string Format(double value, IFormatProvider formatProvider = null)
        {
            if (double.IsNaN(value))
                return "—";

            var numberFormat = NumberFormatInfo.GetInstance(formatProvider);
            if (double.IsPositiveInfinity(value))
                return "∞";
            if (double.IsNegativeInfinity(value))
                return numberFormat.NegativeSign + "∞";

            if (Math.Abs(value) >= ScientificThreshold)
                return value.ToString(ScientificFormat, numberFormat);

            return FormatCompact((decimal)value, numberFormat);
        }

        private static string FormatCompact(decimal value, NumberFormatInfo numberFormat)
        {
            decimal magnitude = Math.Abs(value);
            int group = 0;
            while (magnitude >= GroupSize && group < Suffixes.Length - 1)
            {
                magnitude /= GroupSize;
                group++;
            }

            magnitude = Math.Round(magnitude, 2, MidpointRounding.AwayFromZero);
            if (magnitude == GroupSize)
            {
                group++;
                if (group == Suffixes.Length)
                    return value.ToString(ScientificFormat, numberFormat);

                magnitude = 1;
            }

            string sign = value < 0 && magnitude != 0 ? numberFormat.NegativeSign : "";
            return sign + magnitude.ToString("0.##", numberFormat) + Suffixes[group];
        }
    }
}
