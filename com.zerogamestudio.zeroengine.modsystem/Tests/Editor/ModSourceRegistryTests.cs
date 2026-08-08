using System;
using NUnit.Framework;

namespace ZeroEngine.ModSystem.Tests.Editor
{
    public sealed class ModSourceRegistryTests
    {
        [TearDown]
        public void TearDown()
        {
            ModSourceRegistry.Clear();
        }

        [Test]
        public void Register_WhenSameSourceIdRegisteredTwice_KeepsFirstSource()
        {
            var first = new SampleSource("local");
            var second = new SampleSource("local");

            ModSourceRegistry.Register(first);
            ModSourceRegistry.Register(second);

            Assert.That(ModSourceRegistry.RegisteredSources, Has.Count.EqualTo(1));
            Assert.That(ModSourceRegistry.RegisteredSources[0], Is.SameAs(first));
        }

        [Test]
        public void Clear_RemovesRegisteredSources()
        {
            ModSourceRegistry.Register(new SampleSource("local"));

            ModSourceRegistry.Clear();

            Assert.That(ModSourceRegistry.RegisteredSources, Is.Empty);
        }

        private sealed class SampleSource : IModSource
        {
            public SampleSource(string sourceId)
            {
                SourceId = sourceId;
            }

            public string SourceId { get; }
            public bool IsAvailable => true;

            public void QueryInstalledModFolders(Action<ModSourceQueryResult> onCompleted)
            {
                onCompleted?.Invoke(ModSourceQueryResult.Success(SourceId, Array.Empty<string>()));
            }
        }
    }
}
