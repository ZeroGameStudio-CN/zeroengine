using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;

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
            MethodInfo method = typeof(ZipAttachmentUploader)
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .Single(methodInfo =>
                    methodInfo.Name == "TryCreateZip" &&
                    methodInfo.GetParameters().Length == 10);

            return (bool)method.Invoke(
                null,
                new object[]
                {
                    "Tester",
                    "message",
                    "{}",
                    directoriesToInclude,
                    filesToInclude,
                    fixture.ZipPath,
                    "Report",
                    "Report/",
                    fixture.TempRoot,
                    fixture.FeedbackDirectory
                });
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
