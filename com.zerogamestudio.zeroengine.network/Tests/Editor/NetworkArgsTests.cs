using NUnit.Framework;
using ZeroEngine.Network.Core;

namespace ZeroEngine.Network.Editor.Tests
{
    public sealed class NetworkArgsTests
    {
        [Test]
        public void ParseCommandLineArgsHandlesValuesFlagsDuplicatesAndCaseInsensitiveKeys()
        {
            var parsed = NetworkArgs.ParseCommandLineArgs(new[]
            {
                "Game.exe",
                "-host",
                "-port",
                "7777",
                "-mode",
                "client",
                "-PORT",
                "8888"
            });

            Assert.AreEqual("true", parsed["host"]);
            Assert.AreEqual("8888", parsed["port"]);
            Assert.AreEqual("client", parsed["mode"]);
        }

        [Test]
        public void ParseCommandLineArgsReturnsEmptyDictionaryForNullInput()
        {
            var parsed = NetworkArgs.ParseCommandLineArgs(null);

            Assert.AreEqual(0, parsed.Count);
        }
    }
}
