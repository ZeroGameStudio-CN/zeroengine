using System;
using UnityEngine;

namespace ZeroEngine.Core
{
    public enum ZeroLogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }

    public readonly struct ZeroLogEntry
    {
        public ZeroLogEntry(ZeroLogLevel level, string category, string message, UnityEngine.Object context = null)
        {
            Level = level;
            Category = category ?? string.Empty;
            Message = message ?? string.Empty;
            Context = context;
        }

        public ZeroLogLevel Level { get; }
        public string Category { get; }
        public string Message { get; }
        public UnityEngine.Object Context { get; }
    }

    public interface IZeroLogSink
    {
        void Write(in ZeroLogEntry entry);
    }

    public sealed class UnityZeroLogSink : IZeroLogSink
    {
        public void Write(in ZeroLogEntry entry)
        {
            var formatted = $"[{entry.Category}] {entry.Message}";
            switch (entry.Level)
            {
                case ZeroLogLevel.Debug:
                case ZeroLogLevel.Info:
                    Debug.Log(formatted, entry.Context);
                    break;
                case ZeroLogLevel.Warning:
                    Debug.LogWarning(formatted, entry.Context);
                    break;
                case ZeroLogLevel.Error:
                    Debug.LogError(formatted, entry.Context);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(entry), entry.Level, "Unknown log level.");
            }
        }
    }

    public sealed class ZeroLogChannel
    {
        private readonly string _categoryPrefix;
        private readonly IZeroLogSink _sink;

        public ZeroLogChannel(
            string categoryPrefix = "ZeroEngine.",
            ZeroLogLevel minimumLevel = ZeroLogLevel.Info,
            IZeroLogSink sink = null)
        {
            _categoryPrefix = categoryPrefix ?? string.Empty;
            MinimumLevel = minimumLevel;
            _sink = sink ?? new UnityZeroLogSink();
        }

        public ZeroLogLevel MinimumLevel { get; }

        public void Write(
            ZeroLogLevel level,
            string category,
            string message,
            UnityEngine.Object context = null)
        {
            if (level < MinimumLevel)
            {
                return;
            }

            var entry = new ZeroLogEntry(level, _categoryPrefix + (category ?? string.Empty), message, context);
            _sink.Write(in entry);
        }
    }
}
