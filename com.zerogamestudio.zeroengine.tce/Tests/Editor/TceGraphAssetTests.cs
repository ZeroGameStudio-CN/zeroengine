using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceGraphAssetTests
    {
        [Test]
        public void TceGraphAsset_DefaultGraph_IsNonNull()
        {
            var asset = ScriptableObject.CreateInstance<TceGraphAsset>();

            Assert.NotNull(asset.Graph);
        }

        [Test]
        public void TceGraphAsset_RuntimeCanInstallAssetGraph()
        {
            var asset = ScriptableObject.CreateInstance<TceGraphAsset>();
            asset.Graph.AddTrigger(new OnInstallTriggerData());
            asset.Graph.AddEffect(new DebugLogEffectData { Message = "asset-run" });

            string logged = null;
            TceLog.Handler = message => logged = message;
            try
            {
                new TceRuntime().Install(null, new TestActor(), asset.Graph);
            }
            finally
            {
                TceLog.Handler = Debug.Log;
            }

            Assert.AreEqual("asset-run", logged);
        }

        private sealed class TestActor : ITceActor
        {
            public bool IsAlive => true;
            public float DomainTime => 0f;
            public object NativeObject => this;
        }
    }
}
