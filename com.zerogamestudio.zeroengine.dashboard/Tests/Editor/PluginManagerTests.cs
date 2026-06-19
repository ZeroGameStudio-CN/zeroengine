using NUnit.Framework;
using ZeroEngine.Editor;

namespace ZeroEngine.Dashboard.Editor.Tests
{
    public sealed class PluginManagerTests
    {
        [Test]
        public void CheckPluginsCanRunWithoutHardOptionalDependencies()
        {
            Assert.DoesNotThrow(PluginManager.CheckPlugins);
        }
    }
}
