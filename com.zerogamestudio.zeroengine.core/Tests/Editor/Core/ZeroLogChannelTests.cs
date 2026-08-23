using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZeroEngine.Core.Tests
{
    [TestFixture]
    public sealed class ZeroLogChannelTests
    {
        [Test]
        public void Write_ForwardsEveryEntryField()
        {
            var sink = new RecordingSink();
            var context = new GameObject("LogContext");
            try
            {
                var channel = new ZeroLogChannel("P5.", ZeroLogLevel.Debug, sink);

                channel.Write(ZeroLogLevel.Warning, "Battle", "low health", context);

                Assert.That(sink.WriteCount, Is.EqualTo(1));
                Assert.That(sink.Entry.Level, Is.EqualTo(ZeroLogLevel.Warning));
                Assert.That(sink.Entry.Category, Is.EqualTo("P5.Battle"));
                Assert.That(sink.Entry.Message, Is.EqualTo("low health"));
                Assert.That(sink.Entry.Context, Is.SameAs(context));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(context);
            }
        }

        [Test]
        public void Write_BelowMinimumLevel_DoesNotInvokeSink()
        {
            var sink = new RecordingSink();
            var channel = new ZeroLogChannel(string.Empty, ZeroLogLevel.Warning, sink);

            channel.Write(ZeroLogLevel.Info, "Boot", "hidden");

            Assert.That(sink.WriteCount, Is.Zero);
        }

        [Test]
        public void Write_NullCategoryAndMessage_NormalizesToEmptyStrings()
        {
            var sink = new RecordingSink();
            var channel = new ZeroLogChannel(null, ZeroLogLevel.Debug, sink);

            channel.Write(ZeroLogLevel.Info, null, null);

            Assert.That(sink.Entry.Category, Is.Empty);
            Assert.That(sink.Entry.Message, Is.Empty);
        }

        [Test]
        public void Write_WhenCustomSinkThrows_PropagatesTheFailure()
        {
            var channel = new ZeroLogChannel(string.Empty, ZeroLogLevel.Debug, new ThrowingSink());

            Assert.Throws<InvalidOperationException>(() =>
                channel.Write(ZeroLogLevel.Error, "Test", "boom"));
        }

        [Test]
        public void UnitySink_FormatsCategoryAndDispatchesLevel()
        {
            var channel = new ZeroLogChannel("P5.", ZeroLogLevel.Debug, new UnityZeroLogSink());
            LogAssert.Expect(LogType.Warning, "[P5.Settings] recovered");

            channel.Write(ZeroLogLevel.Warning, "Settings", "recovered");
        }

        private sealed class RecordingSink : IZeroLogSink
        {
            public int WriteCount { get; private set; }
            public ZeroLogEntry Entry { get; private set; }

            public void Write(in ZeroLogEntry entry)
            {
                WriteCount++;
                Entry = entry;
            }
        }

        private sealed class ThrowingSink : IZeroLogSink
        {
            public void Write(in ZeroLogEntry entry)
            {
                throw new InvalidOperationException("sink-failed");
            }
        }
    }
}
