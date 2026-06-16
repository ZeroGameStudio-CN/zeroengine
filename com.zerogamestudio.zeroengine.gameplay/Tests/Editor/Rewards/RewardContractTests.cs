using NUnit.Framework;
using ZeroEngine.Gameplay.Rewards;

namespace ZeroEngine.Gameplay.Tests.Rewards
{
    [TestFixture]
    public sealed class RewardContractTests
    {
        [Test]
        public void RewardRequest_CapturesStableRewardPayload()
        {
            var request = new RewardRequest("reward.demo.first_martial_art", "MartialArt", "quest.demo", "demo_first_martial_art", 1);

            Assert.AreEqual("reward.demo.first_martial_art", request.RewardId);
            Assert.AreEqual("MartialArt", request.RewardType);
            Assert.AreEqual("quest.demo", request.SourceId);
            Assert.AreEqual("demo_first_martial_art", request.PayloadId);
            Assert.AreEqual(1, request.Quantity);
        }

        [Test]
        public void RewardResult_RepresentsSuccessSkipAndFailure()
        {
            var success = RewardResult.Succeeded("reward.item", "Herb x2", "item.herb");
            var skipped = RewardResult.Skip("reward.item", "already granted");
            var failed = RewardResult.Failed("reward.item", "missing handler");

            Assert.IsTrue(success.Success);
            Assert.IsFalse(success.Skipped);
            Assert.That(success.SummaryLines, Has.Member("Herb x2"));
            Assert.That(success.AppliedPayloadIds, Has.Member("item.herb"));

            Assert.IsTrue(skipped.Success);
            Assert.IsTrue(skipped.Skipped);
            Assert.AreEqual("already granted", skipped.FailureReason);

            Assert.IsFalse(failed.Success);
            Assert.IsFalse(failed.Skipped);
            Assert.AreEqual("missing handler", failed.FailureReason);
        }

        [Test]
        public void RewardHandler_InterfaceHandlesRequestsWithoutConcreteSystems()
        {
            IRewardHandler handler = new RecordingRewardHandler();
            var request = new RewardRequest("reward.currency", "Currency", "quest.demo", "gold", 25);

            Assert.IsTrue(handler.CanHandle(request));
            Assert.IsTrue(handler.Grant(request).Success);
        }

        private sealed class RecordingRewardHandler : IRewardHandler
        {
            public bool CanHandle(RewardRequest request)
            {
                return request.RewardType == "Currency";
            }

            public RewardResult Grant(RewardRequest request)
            {
                return RewardResult.Succeeded(request.RewardId, $"{request.PayloadId} x{request.Quantity}", request.PayloadId);
            }
        }
    }
}
