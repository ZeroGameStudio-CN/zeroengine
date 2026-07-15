using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;

namespace ZGS.Analytics.Tests.Editor
{
    [TestFixture]
    public sealed class ZipAttachmentUploaderTests
    {
        private const int ExpectedMaxLogBytes = 4 * 1024 * 1024;
        private const long OversizedAttachmentBytes = 46L * 1024L * 1024L;

        [Test]
        public void TryCreateZip_WithSmallPackage_WritesFeedbackFirstFilesAndManifest()
        {
            using var fixture = new ZipFixture();
            File.WriteAllText(Path.Combine(fixture.IncludeRoot, "data.txt"), "payload");
            File.WriteAllText(Path.Combine(fixture.IncludeRoot, "Player.log"), "current log");

            Assert.IsTrue(InvokeCreateZip(fixture, new[] { fixture.IncludeRoot }, Array.Empty<string>()));

            using ZipArchive zip = ZipFile.OpenRead(fixture.ZipPath);
            string[] entryNames = zip.Entries.Select(entry => entry.FullName).ToArray();
            Assert.AreEqual("Report/Feedback.txt", zip.Entries[0].FullName);
            CollectionAssert.Contains(entryNames, "Report/UploadManifest.json");
            CollectionAssert.Contains(entryNames, "Report/data.txt");
            CollectionAssert.Contains(entryNames, "Report/Player.log");

            string manifest = ReadEntryText(zip, "Report/UploadManifest.json");
            string compactManifest = CompactJson(manifest);
            StringAssert.Contains("\"clientPolicyVersion\":\"zgs-analytics-upload-v1\"", compactManifest);
            StringAssert.Contains("\"entryName\":\"Report/Feedback.txt\"", compactManifest);
            StringAssert.Contains("\"entryName\":\"Report/data.txt\"", compactManifest);
            StringAssert.Contains("\"status\":\"included\"", compactManifest);
            Assert.IsFalse(compactManifest.Contains("\"partial\":true"), manifest);
        }

        [Test]
        public void BuildZipEntryPrefix_WithNonAsciiUserName_UsesAsciiSafeEntryFolder()
        {
            string prefix = InvokeBuildZipEntryPrefix(
                "POB_vEA0.7.7.3",
                "木有枝",
                "20260626_004742");

            Assert.AreEqual("POB_vEA0.7.7.3_20260626_004742", prefix);
            Assert.IsTrue(prefix.All(c => c <= 127), prefix);
        }

        [Test]
        public void BuildZipFileName_WithAsciiSafePrefix_UsesAsciiSafeUploadFileName()
        {
            string prefix = InvokeBuildZipEntryPrefix(
                "POB_vEA0.7.7.3",
                "木有枝",
                "20260626_004742");

            string fileName = InvokeBuildZipFileName(prefix);

            Assert.AreEqual("POB_vEA0.7.7.3_20260626_004742.zip", fileName);
            Assert.IsTrue(fileName.All(c => c <= 127), fileName);
        }

        [Test]
        public void BuildUploadVersion_WithConfiguredAppId_UsesAppIdInsteadOfProductName()
        {
            string version = InvokeBuildUploadVersion("LC", "POB", "EA 0.7.7.3");

            Assert.AreEqual("LC_vEA0.7.7.3", version);
        }

        [Test]
        public void BuildUploadVersion_WithBlankAppId_FallsBackToProductName()
        {
            string version = InvokeBuildUploadVersion(" ", "POB", "EA 0.7.7.3");

            Assert.AreEqual("POB_vEA0.7.7.3", version);
        }

        [Test]
        public void TryCreateZip_WithGeneratedNonAsciiUserPrefix_UsesAsciiTopLevelEntries()
        {
            using var fixture = new ZipFixture();
            string prefix = InvokeBuildZipEntryPrefix(
                "POB_vEA0.7.7.3",
                "木有枝",
                "20260626_004742");

            Assert.IsTrue(InvokeCreateZip(
                fixture,
                "木有枝",
                prefix,
                prefix + "/",
                Array.Empty<string>(),
                Array.Empty<string>()));

            using ZipArchive zip = ZipFile.OpenRead(fixture.ZipPath);
            string[] entryNames = zip.Entries.Select(entry => entry.FullName).ToArray();
            Assert.IsTrue(entryNames.All(name => name.All(c => c <= 127)), string.Join("\n", entryNames));
            CollectionAssert.Contains(entryNames, prefix + "/Feedback.txt");
            CollectionAssert.Contains(entryNames, prefix + "/UploadManifest.json");

            string manifest = ReadEntryText(zip, prefix + "/UploadManifest.json");
            string compactManifest = CompactJson(manifest);
            StringAssert.Contains($"\"entryName\":\"{prefix}/Feedback.txt\"", compactManifest);
        }

        [Test]
        public void TryCreateZip_WithNonAsciiNames_UsesAsciiEntryNamesAndPreservesFeedbackText()
        {
            using var fixture = new ZipFixture();
            string nestedDir = Path.Combine(fixture.IncludeRoot, "中文目录");
            Directory.CreateDirectory(nestedDir);
            File.WriteAllText(Path.Combine(nestedDir, "报告.txt"), "nested payload");
            string looseFile = Path.Combine(fixture.Root, "玩家记录.txt");
            File.WriteAllText(looseFile, "loose payload");

            Assert.IsTrue(InvokeCreateZip(
                fixture,
                "木有枝",
                new[] { fixture.IncludeRoot },
                new[] { looseFile }));

            using ZipArchive zip = ZipFile.OpenRead(fixture.ZipPath);
            string[] entryNames = zip.Entries.Select(entry => entry.FullName).ToArray();
            Assert.IsTrue(entryNames.All(name => name.All(c => c <= 127)), string.Join("\n", entryNames));
            CollectionAssert.Contains(entryNames, "Report/entry/entry.txt");
            CollectionAssert.Contains(entryNames, "Report/entry.txt");

            string feedback = ReadEntryText(zip, "Report/Feedback.txt");
            StringAssert.Contains("User: 木有枝", feedback);

            string manifest = ReadEntryText(zip, "Report/UploadManifest.json");
            string compactManifest = CompactJson(manifest);
            StringAssert.Contains("\"entryName\":\"Report/entry/entry.txt\"", compactManifest);
            StringAssert.Contains("\"entryName\":\"Report/entry.txt\"", compactManifest);
        }

        [Test]
        public void ZipAttachmentUploader_AllowsMultipleReportsInOneRun()
        {
            FieldInfo field = typeof(ZipAttachmentUploader).GetField(
                "_hasUploaded",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNull(field, "Default analytics uploader should not keep process-wide one-shot upload state.");
        }

        [Test]
        public void FeedbackUploadQueue_StartBackgroundProcessing_IsPublicPackageEntrypoint()
        {
            MethodInfo method = typeof(FeedbackUploadQueue).GetMethod(
                "StartBackgroundProcessing",
                BindingFlags.Public | BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);

            Assert.IsNotNull(method, "Feedback upload recovery must start from package bootstrap without game-specific code.");
        }

        [Test]
        public void FeedbackUploadQueue_BackgroundRetryDelay_UsesCappedProgressiveSchedule()
        {
            MethodInfo method = typeof(FeedbackUploadQueue).GetMethod(
                "GetBackgroundRetryDelaySeconds",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method);

            Assert.AreEqual(60f, (float)method.Invoke(null, new object[] { 0 }));
            Assert.AreEqual(300f, (float)method.Invoke(null, new object[] { 1 }));
            Assert.AreEqual(900f, (float)method.Invoke(null, new object[] { 2 }));
            Assert.AreEqual(1800f, (float)method.Invoke(null, new object[] { 3 }));
            Assert.AreEqual(1800f, (float)method.Invoke(null, new object[] { 99 }));
        }

        [Test]
        public void FeedbackUploadQueue_UploadWithRetry_UsesRealtimeRetryDelay()
        {
            string previousServerUrl = AnalyticsConfig.ServerUrl;
            string previousSecret = AnalyticsConfig.Secret;

            try
            {
                AnalyticsConfig.ServerUrl = "";
                AnalyticsConfig.Secret = "";
                using var fixture = new ZipFixture();
                File.WriteAllText(fixture.ZipPath, "zip");

                IEnumerator coroutine = FeedbackUploadQueue.UploadWithRetry(
                    fixture.ZipPath,
                    "POB_vEA0.7.7.3",
                    "Tester",
                    _ => { });

                Assert.IsTrue(coroutine.MoveNext());
                Assert.IsInstanceOf<IEnumerator>(coroutine.Current);
                ExhaustCoroutine((IEnumerator)coroutine.Current);

                Assert.IsTrue(coroutine.MoveNext());
                Assert.IsInstanceOf<WaitForSecondsRealtime>(coroutine.Current);
            }
            finally
            {
                AnalyticsConfig.ServerUrl = previousServerUrl;
                AnalyticsConfig.Secret = previousSecret;
            }
        }

        [Test]
        public void AnalyticsConfig_UploadSecret_WhenUnset_FallsBackToEventSecret()
        {
            PropertyInfo uploadSecretProperty = typeof(AnalyticsConfig).GetProperty(
                "UploadSecret",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(uploadSecretProperty);

            string previousEventSecret = AnalyticsConfig.Secret;
            string previousUploadSecret = (string)uploadSecretProperty.GetValue(null);

            try
            {
                AnalyticsConfig.Secret = "event-secret";
                uploadSecretProperty.SetValue(null, string.Empty);

                Assert.AreEqual("event-secret", uploadSecretProperty.GetValue(null));
            }
            finally
            {
                AnalyticsConfig.Secret = previousEventSecret;
                uploadSecretProperty.SetValue(null, previousUploadSecret);
            }
        }

        [Test]
        public void FeedbackUploadQueue_CreateUploadRequest_UsesHeaderAndOmitsFormSecret()
        {
            PropertyInfo uploadSecretProperty = typeof(AnalyticsConfig).GetProperty(
                "UploadSecret",
                BindingFlags.Public | BindingFlags.Static);
            MethodInfo createRequestMethod = typeof(FeedbackUploadQueue).GetMethod(
                "CreateUploadRequest",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(uploadSecretProperty);
            Assert.IsNotNull(createRequestMethod);

            string previousEventSecret = AnalyticsConfig.Secret;
            string previousUploadSecret = (string)uploadSecretProperty.GetValue(null);

            try
            {
                AnalyticsConfig.Secret = "event-secret";
                uploadSecretProperty.SetValue(null, "upload-secret");

                using var request = (UnityWebRequest)createRequestMethod.Invoke(
                    null,
                    new object[]
                    {
                        "https://example.invalid/upload",
                        "POB_vEA0.7.7.4",
                        "20260714_215146",
                        "feedback.zip",
                        new byte[] { 1, 2, 3 }
                    });

                Assert.AreEqual("upload-secret", request.GetRequestHeader("X-Upload-Secret"));
                string formBody = Encoding.UTF8.GetString(request.uploadHandler.data);
                StringAssert.DoesNotContain("upload-secret", formBody);
                StringAssert.DoesNotContain("name=\"secret\"", formBody);
            }
            finally
            {
                AnalyticsConfig.Secret = previousEventSecret;
                uploadSecretProperty.SetValue(null, previousUploadSecret);
            }
        }

        [Test]
        public void TryCreateZip_WithHugePlayerLogs_TailsLogsAndMarksManifestTruncated()
        {
            using var fixture = new ZipFixture();
            byte[] logBytes = new byte[ExpectedMaxLogBytes + 1234];
            for (int i = 0; i < logBytes.Length; i++)
                logBytes[i] = (byte)(i % 251);
            File.WriteAllBytes(Path.Combine(fixture.IncludeRoot, "Player.log"), logBytes);
            File.WriteAllBytes(Path.Combine(fixture.IncludeRoot, "Player-prev.log"), logBytes);

            Assert.IsTrue(InvokeCreateZip(fixture, new[] { fixture.IncludeRoot }, Array.Empty<string>()));

            using ZipArchive zip = ZipFile.OpenRead(fixture.ZipPath);
            byte[] expectedTail = logBytes.Skip(logBytes.Length - ExpectedMaxLogBytes).ToArray();
            CollectionAssert.AreEqual(expectedTail, ReadEntryBytes(zip, "Report/Player.log"));
            CollectionAssert.AreEqual(expectedTail, ReadEntryBytes(zip, "Report/Player-prev.log"));

            string manifest = ReadEntryText(zip, "Report/UploadManifest.json");
            string compactManifest = CompactJson(manifest);
            StringAssert.Contains("\"status\":\"truncated\"", compactManifest);
            StringAssert.Contains("tail_truncated_to_4194304_bytes", manifest);
            StringAssert.Contains("\"truncated\":true", compactManifest);
        }

        [Test]
        public void TryCreateZip_WhenAttachmentsExceedBudgets_StillCreatesBoundedZipWithManifest()
        {
            using var fixture = new ZipFixture();
            var files = new List<string>();
            string hugePath = Path.Combine(fixture.Root, "huge.bin");
            using (FileStream stream = File.Create(hugePath))
                stream.SetLength(OversizedAttachmentBytes);
            files.Add(hugePath);

            for (int i = 0; i < 95; i++)
            {
                string path = Path.Combine(fixture.Root, $"extra_{i:00}.txt");
                File.WriteAllText(path, "x");
                files.Add(path);
            }

            Assert.IsTrue(InvokeCreateZip(fixture, Array.Empty<string>(), files));

            using ZipArchive zip = ZipFile.OpenRead(fixture.ZipPath);
            Assert.LessOrEqual(GetUncompressedBytes(zip), 45L * 1024L * 1024L);
            Assert.LessOrEqual(zip.Entries.Count, 90);
            Assert.IsNotNull(zip.GetEntry("Report/Feedback.txt"));
            Assert.IsNotNull(zip.GetEntry("Report/UploadManifest.json"));
            Assert.IsNull(zip.GetEntry("Report/huge.bin"));

            string manifest = ReadEntryText(zip, "Report/UploadManifest.json");
            StringAssert.Contains("uncompressed_budget_exceeded", manifest);
            StringAssert.Contains("entry_budget_exceeded", manifest);
            string compactManifest = CompactJson(manifest);
            StringAssert.Contains("\"partial\":true", compactManifest);
            StringAssert.Contains("\"skipped\":true", compactManifest);
        }

        [Test]
        public void TryCreateZip_WhenDirectoryContainsFeedbackQueue_SkipsQueuedZips()
        {
            using var fixture = new ZipFixture();
            string queuedZip = Path.Combine(fixture.FeedbackDirectory, "old.zip");
            File.WriteAllText(queuedZip, "old upload");
            File.WriteAllText(Path.Combine(fixture.IncludeRoot, "data.txt"), "payload");

            Assert.IsTrue(InvokeCreateZip(fixture, new[] { fixture.Root }, Array.Empty<string>()));

            using ZipArchive zip = ZipFile.OpenRead(fixture.ZipPath);
            string[] entryNames = zip.Entries.Select(entry => entry.FullName).ToArray();
            CollectionAssert.Contains(entryNames, "Report/Include/data.txt");
            CollectionAssert.DoesNotContain(entryNames, "Report/PendingFeedback/old.zip");

            string manifest = ReadEntryText(zip, "Report/UploadManifest.json");
            StringAssert.Contains("feedback_queue_directory", manifest);
            StringAssert.Contains("old.zip", manifest);
        }

        [Test]
        public void BuildManifestBytesWithinBudget_WhenManifestRecordsExceedBudget_OmitsRecordsAndKeepsFeedback()
        {
            object manifest = CreateManifest();
            IList attachments = GetManifestAttachments(manifest);
            Type attachmentType = typeof(ZipAttachmentUploader)
                .GetNestedType("UploadManifestAttachment", BindingFlags.NonPublic);
            Assert.IsNotNull(attachmentType);

            attachments.Add(CreateManifestAttachment(
                attachmentType,
                "Feedback.txt",
                "Report/Feedback.txt",
                128,
                128,
                "included",
                "required"));

            for (int i = 0; i < 80; i++)
            {
                string longPath = Path.Combine(
                    "/very/long/path/for/manifest/budget",
                    new string('x', 180) + i.ToString("000") + ".txt");
                attachments.Add(CreateManifestAttachment(
                    attachmentType,
                    longPath,
                    "Report/" + Path.GetFileName(longPath),
                    1024,
                    0,
                    "skipped",
                    "entry_budget_exceeded"));
            }

            MethodInfo method = typeof(ZipAttachmentUploader)
                .GetMethod("BuildManifestBytesWithinBudget", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method);

            var bytes = (byte[])method.Invoke(null, new[] { manifest, (object)2048L });
            string manifestText = Encoding.UTF8.GetString(bytes);
            string compactManifest = CompactJson(manifestText);

            Assert.LessOrEqual(bytes.Length, 2048);
            StringAssert.Contains("\"entryName\":\"Report/Feedback.txt\"", compactManifest);
            StringAssert.Contains("\"manifestTruncated\":true", compactManifest);
            StringAssert.Contains("\"manifestTruncationReason\":\"manifest_budget_exceeded\"", compactManifest);
            StringAssert.Contains("\"omittedAttachmentRecords\":", compactManifest);
        }

        private static bool InvokeCreateZip(
            ZipFixture fixture,
            IEnumerable<string> directoriesToInclude,
            IEnumerable<string> filesToInclude)
        {
            return InvokeCreateZip(fixture, "Tester", directoriesToInclude, filesToInclude);
        }

        private static bool InvokeCreateZip(
            ZipFixture fixture,
            string userName,
            IEnumerable<string> directoriesToInclude,
            IEnumerable<string> filesToInclude)
        {
            return InvokeCreateZip(
                fixture,
                userName,
                "Report",
                "Report/",
                directoriesToInclude,
                filesToInclude);
        }

        private static bool InvokeCreateZip(
            ZipFixture fixture,
            string userName,
            string prefix,
            string prefixDir,
            IEnumerable<string> directoriesToInclude,
            IEnumerable<string> filesToInclude)
        {
            MethodInfo method = typeof(ZipAttachmentUploader)
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .Single(methodInfo =>
                    methodInfo.Name == "TryCreateZip" &&
                    methodInfo.GetParameters().Length == 10);

            return (bool)method.Invoke(
                null,
                new object[]
                {
                    userName,
                    "message",
                    "{}",
                    directoriesToInclude,
                    filesToInclude,
                    fixture.ZipPath,
                    prefix,
                    prefixDir,
                    fixture.TempRoot,
                    fixture.FeedbackDirectory
                });
        }

        private static string InvokeBuildZipEntryPrefix(string version, string userName, string timestamp)
        {
            MethodInfo method = typeof(ZipAttachmentUploader).GetMethod(
                "BuildZipEntryPrefix",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method);

            return (string)method.Invoke(null, new object[] { version, userName, timestamp });
        }

        private static string InvokeBuildZipFileName(string zipEntryPrefix)
        {
            MethodInfo method = typeof(ZipAttachmentUploader).GetMethod(
                "BuildZipFileName",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method);

            return (string)method.Invoke(null, new object[] { zipEntryPrefix });
        }

        private static string InvokeBuildUploadVersion(string appId, string productName, string appVersion)
        {
            MethodInfo method = typeof(ZipAttachmentUploader).GetMethod(
                "BuildUploadVersion",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method);

            return (string)method.Invoke(null, new object[] { appId, productName, appVersion });
        }

        private static void ExhaustCoroutine(IEnumerator coroutine)
        {
            while (coroutine.MoveNext())
            {
                if (coroutine.Current is IEnumerator nested)
                    ExhaustCoroutine(nested);
            }
        }

        private static string ReadEntryText(ZipArchive zip, string entryName)
        {
            ZipArchiveEntry entry = zip.GetEntry(entryName);
            Assert.IsNotNull(entry, entryName);
            using Stream stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static byte[] ReadEntryBytes(ZipArchive zip, string entryName)
        {
            ZipArchiveEntry entry = zip.GetEntry(entryName);
            Assert.IsNotNull(entry, entryName);
            using Stream stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        private static string CompactJson(string value)
        {
            return value
                .Replace(" ", string.Empty)
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty)
                .Replace("\t", string.Empty);
        }

        private static long GetUncompressedBytes(ZipArchive zip)
        {
            long total = 0;
            foreach (ZipArchiveEntry entry in zip.Entries)
                total += entry.Length;
            return total;
        }

        private static object CreateManifest()
        {
            Type manifestType = typeof(ZipAttachmentUploader)
                .GetNestedType("UploadManifest", BindingFlags.NonPublic);
            Assert.IsNotNull(manifestType);
            return Activator.CreateInstance(manifestType, true);
        }

        private static IList GetManifestAttachments(object manifest)
        {
            FieldInfo field = manifest.GetType().GetField(
                "attachments",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            return (IList)field.GetValue(manifest);
        }

        private static object CreateManifestAttachment(
            Type attachmentType,
            string path,
            string entryName,
            long originalBytes,
            long includedBytes,
            string status,
            string reason)
        {
            object attachment = Activator.CreateInstance(attachmentType, true);
            SetField(attachment, "path", path);
            SetField(attachment, "entryName", entryName);
            SetField(attachment, "originalBytes", originalBytes);
            SetField(attachment, "includedBytes", includedBytes);
            SetField(attachment, "status", status);
            SetField(attachment, "reason", reason);
            return attachment;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(target, value);
        }

        private sealed class ZipFixture : IDisposable
        {
            public ZipFixture()
            {
                Root = Path.Combine(Path.GetTempPath(), "ZGS_AnalyticsZip_" + Guid.NewGuid().ToString("N"));
                IncludeRoot = Path.Combine(Root, "Include");
                TempRoot = Path.Combine(Root, "Temp");
                FeedbackDirectory = Path.Combine(Root, "PendingFeedback");
                ZipPath = Path.Combine(FeedbackDirectory, "report.zip");
                Directory.CreateDirectory(IncludeRoot);
                Directory.CreateDirectory(TempRoot);
                Directory.CreateDirectory(FeedbackDirectory);
            }

            public string Root { get; }
            public string IncludeRoot { get; }
            public string TempRoot { get; }
            public string FeedbackDirectory { get; }
            public string ZipPath { get; }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(Root, true);
                }
                catch
                {
                    // Ignore cleanup failures from transient file locks.
                }
            }
        }
    }
}
