using System;
using System.Collections.Generic;
using NUnit.Framework;
using ZeroEngine.Save;

namespace ZeroEngine.Persistence.Tests.Editor
{
    public class JsonSaveProviderTests
    {
        [Test]
        public void ObjectDictionary_RoundTripsConcretePayloadTypes()
        {
            var provider = new JsonSaveProvider();
            string fileName = $"JsonSaveProviderTests_{Guid.NewGuid():N}.json";

            try
            {
                var source = new Dictionary<string, object>
                {
                    ["GameData"] = new SampleSavePayload
                    {
                        Gold = 620,
                        BuildVersion = "0.1.0-demo1",
                        BattleSpeed = 2f
                    },
                    ["Score"] = 42,
                    ["PlayerName"] = "Tester"
                };

                provider.Save("SystemData", source, fileName);

                var loaded = provider.Load<Dictionary<string, object>>("SystemData", null, fileName);

                Assert.IsNotNull(loaded);
                Assert.IsInstanceOf<SampleSavePayload>(loaded["GameData"]);
                var payload = (SampleSavePayload)loaded["GameData"];
                Assert.AreEqual(620, payload.Gold);
                Assert.AreEqual("0.1.0-demo1", payload.BuildVersion);
                Assert.AreEqual(2f, payload.BattleSpeed);
                Assert.AreEqual(42, loaded["Score"]);
                Assert.AreEqual("Tester", loaded["PlayerName"]);
            }
            finally
            {
                provider.DeleteFile(fileName);
            }
        }

        [Serializable]
        private class SampleSavePayload
        {
            public int Gold;
            public string BuildVersion;
            public float BattleSpeed;
        }
    }
}
