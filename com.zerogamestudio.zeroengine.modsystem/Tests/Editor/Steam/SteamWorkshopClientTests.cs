using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace ZeroEngine.ModSystem.Steam.Tests.Editor
{
    public sealed class SteamWorkshopClientTests
    {
        private readonly List<string> temporaryDirectories = new();

        [TearDown]
        public void TearDown()
        {
            foreach (string path in temporaryDirectories)
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            temporaryDirectories.Clear();
        }

        [Test]
        public void Query_SuccessfulEmptySet_IsDistinctFromFailure()
        {
            var api = new FakeApi();
            api.QueryCompletion = new SteamWorkshopApiQueryResult(true, Array.Empty<WorkshopItemInfo>(), string.Empty);
            var client = new SteamWorkshopClient(api, new AllowPolicy());

            WorkshopQueryResult result = client.QuerySubscribedItemsAsync(
                    TimeSpan.FromSeconds(1),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(result.Status, Is.EqualTo(WorkshopOperationStatus.Succeeded));
            Assert.That(result.Items, Is.Empty);
            Assert.That(result.ReasonCode, Is.Empty);
            Assert.That(api.Operation.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void Query_StartFailure_ReturnsStableReason()
        {
            var api = new FakeApi { StartQuery = false, StartReason = "query_create_failed" };
            var client = new SteamWorkshopClient(api, new AllowPolicy());

            WorkshopQueryResult result = client.QuerySubscribedItemsAsync(
                    TimeSpan.FromSeconds(1),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(result.Status, Is.EqualTo(WorkshopOperationStatus.Failed));
            Assert.That(result.ReasonCode, Is.EqualTo("query_create_failed"));
        }

        [Test]
        public void Query_CompletionFailure_ReturnsApiReasonWithoutItems()
        {
            var api = new FakeApi
            {
                QueryCompletion = new SteamWorkshopApiQueryResult(
                    false,
                    new[] { new WorkshopItemInfo() },
                    "k_EResultFail")
            };
            var client = new SteamWorkshopClient(api, new AllowPolicy());

            WorkshopQueryResult result = client.QuerySubscribedItemsAsync(
                    TimeSpan.FromSeconds(1),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(result.Status, Is.EqualTo(WorkshopOperationStatus.Failed));
            Assert.That(result.Items, Is.Empty);
            Assert.That(result.ReasonCode, Is.EqualTo("k_EResultFail"));
        }

        [UnityTest]
        public IEnumerator Query_TimeoutDisposesOperationAndIgnoresLateCallback()
        {
            var api = new FakeApi { CompleteQuerySynchronously = false };
            var client = new SteamWorkshopClient(api, new AllowPolicy());

            Task<WorkshopQueryResult> task = client.QuerySubscribedItemsAsync(
                TimeSpan.FromMilliseconds(20),
                CancellationToken.None);
            while (!task.IsCompleted)
                yield return null;
            WorkshopQueryResult result = task.GetAwaiter().GetResult();
            api.CompletePendingQuery(new SteamWorkshopApiQueryResult(
                true,
                new[] { new WorkshopItemInfo() },
                string.Empty));

            Assert.That(result.Status, Is.EqualTo(WorkshopOperationStatus.TimedOut));
            Assert.That(api.Operation.DisposeCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Query_CancelDisposesOperationAndReturnsCancelled()
        {
            var api = new FakeApi { CompleteQuerySynchronously = false };
            var client = new SteamWorkshopClient(api, new AllowPolicy());
            using var cancellation = new CancellationTokenSource();

            Task<WorkshopQueryResult> task = client.QuerySubscribedItemsAsync(
                TimeSpan.FromSeconds(1),
                cancellation.Token);
            cancellation.Cancel();
            while (!task.IsCompleted)
                yield return null;
            WorkshopQueryResult result = task.GetAwaiter().GetResult();

            Assert.That(result.Status, Is.EqualTo(WorkshopOperationStatus.Cancelled));
            Assert.That(api.Operation.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void Query_PreCancelled_DoesNotStartSteamOperation()
        {
            var api = new FakeApi();
            var client = new SteamWorkshopClient(api, new AllowPolicy());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            WorkshopQueryResult result = client.QuerySubscribedItemsAsync(
                    TimeSpan.FromSeconds(1),
                    cancellation.Token)
                .GetAwaiter()
                .GetResult();

            Assert.That(result.Status, Is.EqualTo(WorkshopOperationStatus.Cancelled));
            Assert.That(api.QueryStartCount, Is.Zero);
        }

        [Test]
        public void Source_FiltersInstallationsAndSignalsSnapshotChanges()
        {
            string valid = CreateDirectory(withManifest: true);
            string missingManifest = CreateDirectory(withManifest: false);
            var api = new FakeApi
            {
                QueryCompletion = QueryItems(valid, missingManifest)
            };
            var client = new SteamWorkshopClient(api, new AllowPolicy());
            var source = new SteamWorkshopModSource(
                client,
                "steam-workshop",
                "mod.json",
                TimeSpan.FromSeconds(1));

            ModSourceQueryResult first = source.QueryInstalledModFoldersAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            api.QueryCompletion = new SteamWorkshopApiQueryResult(true, Array.Empty<WorkshopItemInfo>(), string.Empty);
            ModSourceQueryResult second = source.QueryInstalledModFoldersAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(first.Succeeded, Is.True);
            Assert.That(first.ModFolders, Is.EqualTo(new[] { valid }));
            Assert.That(second.Succeeded, Is.True);
            Assert.That(source.RestartRequired, Is.True);
        }

        [TestCase(WorkshopVisibility.Public)]
        [TestCase(WorkshopVisibility.FriendsOnly)]
        [TestCase(WorkshopVisibility.Private)]
        [TestCase(WorkshopVisibility.Unlisted)]
        public void Publish_AlwaysPassesExplicitVisibilityToPolicy(WorkshopVisibility visibility)
        {
            var api = new FakeApi();
            var policy = new AllowPolicy();
            var client = new SteamWorkshopClient(api, policy);

            WorkshopPublishResult result = client.CreateItemAsync(
                    visibility,
                    TimeSpan.FromSeconds(1),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(result.Succeeded, Is.True);
            Assert.That(policy.LastVisibility, Is.EqualTo(visibility));
        }

        [Test]
        public void Publish_MissingPolicyAndPublicDeny_DoNotStartSteamOperation()
        {
            var api = new FakeApi();
            var missingPolicyClient = new SteamWorkshopClient(api, null);
            var deniedClient = new SteamWorkshopClient(api, new DenyPublicPolicy());

            WorkshopPublishResult missing = missingPolicyClient.CreateItemAsync(
                    WorkshopVisibility.Private,
                    TimeSpan.FromSeconds(1),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            WorkshopPublishResult denied = deniedClient.CreateItemAsync(
                    WorkshopVisibility.Public,
                    TimeSpan.FromSeconds(1),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(missing.ReasonCode, Is.EqualTo(SteamWorkshopReasonCodes.PublishPolicyMissing));
            Assert.That(denied.ReasonCode, Is.EqualTo("public_publishing_disabled"));
            Assert.That(api.CreateStartCount, Is.Zero);
        }

        [Test]
        public void Download_StartFailureReturnsStableReason()
        {
            var api = new FakeApi { StartDownload = false, StartReason = "download_start_failed" };
            var client = new SteamWorkshopClient(api, new AllowPolicy());

            WorkshopActionResult result = client.DownloadItem(42);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo(SteamWorkshopReasonCodes.DownloadStartFailed));
        }

        [Test]
        public void Publish_LegalAgreementAndStartFailureRemainDistinct()
        {
            var legalApi = new FakeApi
            {
                PublishCompletion = new SteamWorkshopApiPublishResult(false, 42, true, string.Empty)
            };
            var failedApi = new FakeApi { StartCreate = false, StartReason = "create_start_failed" };

            WorkshopPublishResult legal = new SteamWorkshopClient(legalApi, new AllowPolicy())
                .CreateItemAsync(WorkshopVisibility.Private, TimeSpan.FromSeconds(1), CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            WorkshopPublishResult failed = new SteamWorkshopClient(failedApi, new AllowPolicy())
                .CreateItemAsync(WorkshopVisibility.Private, TimeSpan.FromSeconds(1), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(legal.PublishedFileId, Is.EqualTo(42));
            Assert.That(legal.ReasonCode, Is.EqualTo(SteamWorkshopReasonCodes.LegalAgreementRequired));
            Assert.That(failed.ReasonCode, Is.EqualTo(SteamWorkshopReasonCodes.CreateStartFailed));
        }

        [UnityTest]
        public IEnumerator Publish_CancelDisposesOperationAndIgnoresLateCallback()
        {
            var api = new FakeApi { CompletePublishSynchronously = false };
            var client = new SteamWorkshopClient(api, new AllowPolicy());
            using var cancellation = new CancellationTokenSource();

            Task<WorkshopPublishResult> task = client.CreateItemAsync(
                WorkshopVisibility.Private,
                TimeSpan.FromSeconds(1),
                cancellation.Token);
            cancellation.Cancel();
            while (!task.IsCompleted)
                yield return null;
            WorkshopPublishResult result = task.GetAwaiter().GetResult();
            api.CompletePendingPublish(new SteamWorkshopApiPublishResult(true, 99, false, string.Empty));

            Assert.That(result.Status, Is.EqualTo(WorkshopOperationStatus.Cancelled));
            Assert.That(api.Operation.DisposeCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Publish_TimeoutDisposesOperationAndReturnsStableReason()
        {
            var api = new FakeApi { CompletePublishSynchronously = false };
            var client = new SteamWorkshopClient(api, new AllowPolicy());

            Task<WorkshopPublishResult> task = client.CreateItemAsync(
                WorkshopVisibility.Private,
                TimeSpan.FromMilliseconds(20),
                CancellationToken.None);
            while (!task.IsCompleted)
                yield return null;
            WorkshopPublishResult result = task.GetAwaiter().GetResult();

            Assert.That(result.Status, Is.EqualTo(WorkshopOperationStatus.TimedOut));
            Assert.That(result.ReasonCode, Is.EqualTo(SteamWorkshopReasonCodes.PublishTimeout));
            Assert.That(api.Operation.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void Update_PassesExplicitVisibilityAndPreservesApiFailureReason()
        {
            string content = CreateDirectory(withManifest: false);
            var api = new FakeApi
            {
                PublishCompletion = new SteamWorkshopApiPublishResult(false, 77, false, "k_EResultFail")
            };
            var client = new SteamWorkshopClient(api, new AllowPolicy());

            WorkshopPublishResult result = client.UpdateItemAsync(
                    new WorkshopPublishRequest
                    {
                        PublishedFileId = 77,
                        ContentFolder = content,
                        Title = "Title",
                        Visibility = WorkshopVisibility.Unlisted
                    },
                    TimeSpan.FromSeconds(1),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(api.LastUpdateRequest.Visibility, Is.EqualTo(WorkshopVisibility.Unlisted));
            Assert.That(result.Status, Is.EqualTo(WorkshopOperationStatus.Failed));
            Assert.That(result.ReasonCode, Is.EqualTo("k_EResultFail"));
        }

        private string CreateDirectory(bool withManifest)
        {
            string path = Path.Combine(Path.GetTempPath(), "ze-workshop-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            temporaryDirectories.Add(path);
            if (withManifest)
                File.WriteAllText(Path.Combine(path, "mod.json"), "{}");
            return path;
        }

        private static SteamWorkshopApiQueryResult QueryItems(string valid, string missingManifest)
        {
            return new SteamWorkshopApiQueryResult(
                true,
                new[]
                {
                    new WorkshopItemInfo { IsInstalled = true, InstallPath = valid },
                    new WorkshopItemInfo { IsInstalled = true, InstallPath = missingManifest },
                    new WorkshopItemInfo { IsInstalled = false, InstallPath = valid }
                },
                string.Empty);
        }

        private sealed class FakeApi : ISteamWorkshopApi
        {
            private Action<SteamWorkshopApiQueryResult> pendingQuery;
            private Action<SteamWorkshopApiPublishResult> pendingPublish;

            public bool IsAvailable { get; set; } = true;
            public uint AppId { get; set; } = 123;
            public bool StartQuery { get; set; } = true;
            public bool StartCreate { get; set; } = true;
            public bool StartDownload { get; set; } = true;
            public string StartReason { get; set; } = string.Empty;
            public bool CompleteQuerySynchronously { get; set; } = true;
            public bool CompletePublishSynchronously { get; set; } = true;
            public SteamWorkshopApiQueryResult QueryCompletion { get; set; } =
                new(true, Array.Empty<WorkshopItemInfo>(), string.Empty);
            public SteamWorkshopApiPublishResult PublishCompletion { get; set; } =
                new(true, 1, false, string.Empty);
            public CountingOperation Operation { get; private set; }
            public int CreateStartCount { get; private set; }
            public int QueryStartCount { get; private set; }
            public WorkshopPublishRequest LastUpdateRequest { get; private set; }

            public bool TryStartQuery(Action<SteamWorkshopApiQueryResult> onCompleted, out IDisposable operation, out string reasonCode)
            {
                QueryStartCount++;
                reasonCode = StartReason;
                Operation = new CountingOperation();
                operation = Operation;
                if (!StartQuery)
                    return false;
                if (CompleteQuerySynchronously)
                    onCompleted(QueryCompletion);
                else
                    pendingQuery = onCompleted;
                return true;
            }

            public bool TryStartCreate(Action<SteamWorkshopApiPublishResult> onCompleted, out IDisposable operation, out string reasonCode)
            {
                CreateStartCount++;
                reasonCode = StartReason;
                Operation = new CountingOperation();
                operation = Operation;
                if (!StartCreate)
                    return false;
                if (CompletePublishSynchronously)
                    onCompleted(PublishCompletion);
                else
                    pendingPublish = onCompleted;
                return true;
            }

            public bool TryStartUpdate(WorkshopPublishRequest request, Action<SteamWorkshopApiPublishResult> onCompleted, out IDisposable operation, out string reasonCode)
            {
                LastUpdateRequest = request;
                return TryStartCreate(onCompleted, out operation, out reasonCode);
            }

            public bool TryStartDownload(ulong publishedFileId, bool highPriority, out string reasonCode)
            {
                reasonCode = StartReason;
                return StartDownload;
            }

            public bool TryOpenCatalog(out string reasonCode) { reasonCode = string.Empty; return true; }
            public bool TryOpenItemPage(ulong publishedFileId, out string reasonCode) { reasonCode = string.Empty; return true; }
            public bool TryOpenLegalAgreementPage(out string reasonCode) { reasonCode = string.Empty; return true; }

            public void CompletePendingQuery(SteamWorkshopApiQueryResult result) => pendingQuery?.Invoke(result);
            public void CompletePendingPublish(SteamWorkshopApiPublishResult result) => pendingPublish?.Invoke(result);
        }

        private sealed class CountingOperation : IDisposable
        {
            public int DisposeCount { get; private set; }
            public void Dispose() => DisposeCount++;
        }

        private sealed class AllowPolicy : IModPublishPolicy
        {
            public WorkshopVisibility LastVisibility { get; private set; }
            public bool CanPublish(WorkshopVisibility visibility, out string reasonCode)
            {
                LastVisibility = visibility;
                reasonCode = string.Empty;
                return true;
            }
        }

        private sealed class DenyPublicPolicy : IModPublishPolicy
        {
            public bool CanPublish(WorkshopVisibility visibility, out string reasonCode)
            {
                reasonCode = visibility == WorkshopVisibility.Public
                    ? "public_publishing_disabled"
                    : string.Empty;
                return visibility != WorkshopVisibility.Public;
            }
        }
    }
}
