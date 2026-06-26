using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZGS.Analytics
{
    /// <summary>
    /// 通用 ZIP 附件上传器 - SDK 内置实现
    /// 支持指数退避重试和离线队列
    /// </summary>
    public class ZipAttachmentUploader : IAttachmentUploader
    {
        private const int MaxPixels = 1920 * 1080;
        private const int JpgQuality = 70;
        private const int MaxFileReadAttempts = 3;
        private const int FileReadRetryDelayMs = 40;
        private const int MaxZipEntries = 90;
        private const int ReservedManifestEntries = 1;
        private const long MaxLogBytes = 4L * 1024L * 1024L;
        private const long MaxZipUncompressedBytes = 45L * 1024L * 1024L;
        private const long ManifestReserveBytes = 1024L * 1024L;
        private const long AttachmentBytesBudget = MaxZipUncompressedBytes - ManifestReserveBytes;
        private const string ClientPolicyVersion = "zgs-analytics-upload-v1";
        private const string FeedbackFileName = "Feedback.txt";
        private const string ManifestFileName = "UploadManifest.json";
        private const string CurrentPlayerLogFileName = "Player.log";
        private const string PreviousPlayerLogFileName = "Player-prev.log";
        private static bool _hasUploaded;

        private enum AttachmentKind
        {
            Generic,
            Screenshot,
            CurrentPlayerLog,
            PreviousPlayerLog
        }

        private sealed class AttachmentCandidate
        {
            public string SourcePath;
            public string EntryName;
            public AttachmentKind Kind;
            public int Priority;
            public int Order;
            public string PreSkippedReason;

            public bool IsLog => Kind == AttachmentKind.CurrentPlayerLog || Kind == AttachmentKind.PreviousPlayerLog;
        }

        [Serializable]
        private sealed class UploadManifest
        {
            public string clientPolicyVersion = ClientPolicyVersion;
            public bool partial;
            public bool truncated;
            public bool skipped;
            public int maxEntries = MaxZipEntries;
            public long maxUncompressedBytes = MaxZipUncompressedBytes;
            public long maxLogBytes = MaxLogBytes;
            public bool manifestTruncated;
            public int omittedAttachmentRecords;
            public long omittedAttachmentOriginalBytes;
            public long omittedAttachmentIncludedBytes;
            public string manifestTruncationReason = string.Empty;
            public List<UploadManifestAttachment> attachments = new();
        }

        [Serializable]
        private sealed class UploadManifestAttachment
        {
            public string path;
            public string entryName;
            public long originalBytes;
            public long includedBytes;
            public string status;
            public string reason;
        }

        public IEnumerator Upload(AttachmentUploadRequest request)
        {
            if (!AnalyticsConfig.IsConfigured)
            {
                AnalyticsLog.LogWarning("[ZipAttachmentUploader] 未配置服务器 URL 或 Secret，跳过上传");
                yield break;
            }

            if (_hasUploaded)
            {
                AnalyticsLog.LogWarning("[ZipAttachmentUploader] 已上传过反馈，本次忽略");
                yield break;
            }
#if !UNITY_EDITOR
            _hasUploaded = true;
#endif

            string safeUserName = SanitizeFileName(request.UserName ?? "Unknown");
            string version = $"{Application.productName}_v{Application.version}".Replace(" ", "");
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string zipEntryPrefix = BuildZipEntryPrefix(version, safeUserName, timestamp);
            string prefixDir = zipEntryPrefix + "/";
            string zipName = zipEntryPrefix + ".zip";

            // 使用持久化目录存储 ZIP（不会被系统清理）
            string feedbackDir = FeedbackUploadQueue.FeedbackDirectory;
            string zipPath = Path.Combine(feedbackDir, zipName);
            if (File.Exists(zipPath)) File.Delete(zipPath);

            // 临时目录用于处理截图等中间文件
            string tmpDir = Application.temporaryCachePath;

            if (!TryCreateZip(request, zipPath, zipEntryPrefix, prefixDir, tmpDir, feedbackDir))
            {
                AnalyticsLog.LogWarning("[ZipAttachmentUploader] 创建反馈 ZIP 失败，跳过上传");
                yield break;
            }

            // 使用带重试的上传（失败后自动入队列）
            bool uploadSuccess = false;
            yield return FeedbackUploadQueue.UploadWithRetry(
                zipPath,
                version,
                safeUserName,
                success =>
                {
                    uploadSuccess = success;
                    if (success)
                    {
                        // 上传成功，删除 ZIP 文件
                        try
                        {
                            if (File.Exists(zipPath))
                                File.Delete(zipPath);
                        }
                        catch { }
                    }
                });

            AnalyticsLog.Log(uploadSuccess
                ? $"[ZipAttachmentUploader] 上传成功"
                : $"[ZipAttachmentUploader] 上传失败，已加入离线队列");
        }

        private static bool TryCreateZip(
            AttachmentUploadRequest request,
            string zipPath,
            string prefix,
            string prefixDir,
            string temporaryDirectory,
            string feedbackDirectory)
        {
            return TryCreateZip(
                request.UserName,
                request.UserMessage,
                request.TimelineJson,
                request.DirectoriesToInclude,
                request.FilesToInclude,
                zipPath,
                prefix,
                prefixDir,
                temporaryDirectory,
                feedbackDirectory);
        }

        private static bool TryCreateZip(
            string userName,
            string userMessage,
            string timelineJson,
            IEnumerable<string> directoriesToInclude,
            IEnumerable<string> filesToInclude,
            string zipPath,
            string prefix,
            string prefixDir,
            string temporaryDirectory,
            string feedbackDirectory)
        {
            try
            {
                string feedbackContent = $"User: {userName}\nMessage: {userMessage}\n\n---TIMELINE---\n{timelineJson}";
                byte[] feedbackBytes = Encoding.UTF8.GetBytes(feedbackContent);
                var manifest = new UploadManifest();
                int entryCount = 0;
                long uncompressedBytes = 0;

                using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    string feedbackEntryName = prefixDir + FeedbackFileName;
                    AddBytesToZip(zip, feedbackBytes, feedbackEntryName);
                    entryCount++;
                    uncompressedBytes += feedbackBytes.Length;
                    AddManifestAttachment(
                        manifest,
                        FeedbackFileName,
                        feedbackEntryName,
                        feedbackBytes.Length,
                        feedbackBytes.Length,
                        "included",
                        "required");

                    List<AttachmentCandidate> candidates = BuildAttachmentCandidates(
                        directoriesToInclude,
                        filesToInclude,
                        zipPath,
                        feedbackDirectory,
                        prefix,
                        prefixDir);

                    for (int i = 0; i < candidates.Count; i++)
                    {
                        AddCandidateToZip(
                            zip,
                            candidates[i],
                            temporaryDirectory,
                            manifest,
                            ref entryCount,
                            ref uncompressedBytes);
                    }

                    long maxManifestBytes = Math.Min(
                        ManifestReserveBytes,
                        Math.Max(256, MaxZipUncompressedBytes - uncompressedBytes));
                    byte[] manifestBytes = BuildManifestBytesWithinBudget(manifest, maxManifestBytes);
                    AddBytesToZip(zip, manifestBytes, prefixDir + ManifestFileName);
                }

                return true;
            }
            catch (Exception e)
            {
                AnalyticsLog.LogWarning($"[ZipAttachmentUploader] 创建反馈 ZIP 失败: {e.Message}");
                DeleteIfExists(zipPath);
                return false;
            }
        }

        private static List<AttachmentCandidate> BuildAttachmentCandidates(
            IEnumerable<string> directoriesToInclude,
            IEnumerable<string> filesToInclude,
            string zipPath,
            string feedbackDirectory,
            string prefix,
            string prefixDir)
        {
            var result = new List<AttachmentCandidate>();
            var usedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int order = 0;

            if (directoriesToInclude != null)
            {
                foreach (string directory in directoriesToInclude)
                    AddDirectoryCandidates(result, usedSourcePaths, usedEntryNames, directory, zipPath, feedbackDirectory, prefixDir, ref order);
            }

            if (filesToInclude != null)
            {
                int screenshotIndex = 0;
                foreach (string path in filesToInclude)
                {
                    if (string.IsNullOrEmpty(path) || !File.Exists(path) || !usedSourcePaths.Add(NormalizePath(path)))
                        continue;

                    AttachmentKind kind = GetAttachmentKind(path);
                    string entryName;
                    int priority;
                    if (IsImageFile(path) && !IsPlayerLog(kind))
                    {
                        screenshotIndex++;
                        kind = AttachmentKind.Screenshot;
                        entryName = prefixDir + $"{prefix}_Screenshot{screenshotIndex}.jpg";
                        priority = 20;
                    }
                    else
                    {
                        entryName = prefixDir + Path.GetFileName(path);
                        priority = IsPlayerLog(kind) ? 10 : 30;
                    }

                    result.Add(new AttachmentCandidate
                    {
                        SourcePath = path,
                        EntryName = MakeUniqueEntryName(entryName, usedEntryNames),
                        Kind = kind,
                        Priority = priority,
                        Order = order++
                    });
                }
            }

            result.Sort(CompareCandidatePriority);
            return result;
        }

        private static void AddDirectoryCandidates(
            List<AttachmentCandidate> result,
            HashSet<string> usedSourcePaths,
            HashSet<string> usedEntryNames,
            string directory,
            string zipPath,
            string feedbackDirectory,
            string prefixDir,
            ref int order)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return;

            try
            {
                foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    if (string.IsNullOrEmpty(file) || !File.Exists(file) || !usedSourcePaths.Add(NormalizePath(file)))
                        continue;

                    string rel = BuildRelativeEntryName(directory, file);
                    AttachmentKind kind = GetAttachmentKind(file);
                    string skippedReason = string.Empty;
                    if (PathsEqual(file, zipPath))
                        skippedReason = "current_zip_file";
                    else if (IsPathInsideDirectory(file, feedbackDirectory))
                        skippedReason = "feedback_queue_directory";

                    result.Add(new AttachmentCandidate
                    {
                        SourcePath = file,
                        EntryName = MakeUniqueEntryName(prefixDir + rel, usedEntryNames),
                        Kind = kind,
                        Priority = IsPlayerLog(kind) ? 10 : 40,
                        Order = order++,
                        PreSkippedReason = skippedReason
                    });
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                AnalyticsLog.LogWarning($"[ZipAttachmentUploader] 扫描附件目录失败: {directory} - {e.Message}");
            }
        }

        private static void AddCandidateToZip(
            ZipArchive zip,
            AttachmentCandidate candidate,
            string temporaryDirectory,
            UploadManifest manifest,
            ref int entryCount,
            ref long uncompressedBytes)
        {
            long originalBytes = GetFileLength(candidate.SourcePath);

            if (!string.IsNullOrEmpty(candidate.PreSkippedReason))
            {
                AddManifestAttachment(
                    manifest,
                    candidate.SourcePath,
                    candidate.EntryName,
                    originalBytes,
                    0,
                    "skipped",
                    candidate.PreSkippedReason);
                return;
            }

            if (entryCount + 1 + ReservedManifestEntries > MaxZipEntries)
            {
                AddManifestAttachment(
                    manifest,
                    candidate.SourcePath,
                    candidate.EntryName,
                    originalBytes,
                    0,
                    "skipped",
                    "entry_budget_exceeded");
                return;
            }

            long budgetBytes = candidate.IsLog ? Math.Min(originalBytes, MaxLogBytes) : originalBytes;
            if (candidate.Kind != AttachmentKind.Screenshot &&
                uncompressedBytes + budgetBytes > AttachmentBytesBudget)
            {
                AddManifestAttachment(
                    manifest,
                    candidate.SourcePath,
                    candidate.EntryName,
                    originalBytes,
                    0,
                    "skipped",
                    "uncompressed_budget_exceeded");
                return;
            }

            if (!TryPrepareAttachmentBytes(candidate, temporaryDirectory, out byte[] bytes, out Exception lastException))
            {
                string message = lastException == null ? "unknown" : lastException.Message;
                AddManifestAttachment(
                    manifest,
                    candidate.SourcePath,
                    candidate.EntryName,
                    originalBytes,
                    0,
                    "skipped",
                    $"read_failed: {message}");
                AnalyticsLog.LogWarning($"[ZipAttachmentUploader] 跳过附件: {candidate.SourcePath} - {message}");
                return;
            }

            if (uncompressedBytes + bytes.Length > AttachmentBytesBudget)
            {
                AddManifestAttachment(
                    manifest,
                    candidate.SourcePath,
                    candidate.EntryName,
                    originalBytes,
                    0,
                    "skipped",
                    "uncompressed_budget_exceeded");
                return;
            }

            AddBytesToZip(zip, bytes, candidate.EntryName);
            entryCount++;
            uncompressedBytes += bytes.Length;

            bool truncated = candidate.IsLog && originalBytes > bytes.Length;
            AddManifestAttachment(
                manifest,
                candidate.SourcePath,
                candidate.EntryName,
                originalBytes,
                bytes.Length,
                truncated ? "truncated" : "included",
                truncated ? $"tail_truncated_to_{MaxLogBytes}_bytes" : "within_budget");
        }

        private static bool TryPrepareAttachmentBytes(
            AttachmentCandidate candidate,
            string temporaryDirectory,
            out byte[] bytes,
            out Exception lastException)
        {
            if (candidate.IsLog)
                return TryReadTailFileBytes(candidate.SourcePath, MaxLogBytes, out bytes, out lastException);

            if (candidate.Kind == AttachmentKind.Screenshot)
            {
                string screenshotPrefix = Path.GetFileNameWithoutExtension(candidate.EntryName);
                if (!TryPrepareScreenshot(candidate.SourcePath, temporaryDirectory, screenshotPrefix, out string jpgPath))
                {
                    bytes = Array.Empty<byte>();
                    lastException = new InvalidDataException("Selected screenshot could not be prepared.");
                    return false;
                }

                bool read = TryReadFileBytes(jpgPath, out bytes, out lastException);
                DeleteIfExists(jpgPath);
                return read;
            }

            return TryReadFileBytes(candidate.SourcePath, out bytes, out lastException);
        }

        private static void AddBytesToZip(ZipArchive zip, byte[] bytes, string entryName)
        {
            ZipArchiveEntry entry = zip.CreateEntry(entryName);
            using Stream entryStream = entry.Open();
            entryStream.Write(bytes, 0, bytes.Length);
        }

        private static byte[] BuildManifestBytesWithinBudget(UploadManifest manifest, long maxManifestBytes)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(manifest, true));
            if (bytes.Length <= maxManifestBytes)
                return bytes;

            manifest.partial = true;
            manifest.skipped = true;
            manifest.manifestTruncated = true;
            manifest.manifestTruncationReason = "manifest_budget_exceeded";

            while (bytes.Length > maxManifestBytes && TryOmitLastManifestAttachment(manifest))
                bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(manifest, true));

            return bytes;
        }

        private static bool TryOmitLastManifestAttachment(UploadManifest manifest)
        {
            for (int i = manifest.attachments.Count - 1; i >= 0; i--)
            {
                UploadManifestAttachment attachment = manifest.attachments[i];
                if ((attachment.entryName != null &&
                     attachment.entryName.EndsWith("/" + FeedbackFileName, StringComparison.Ordinal)) ||
                    string.Equals(attachment.path, FeedbackFileName, StringComparison.Ordinal))
                {
                    continue;
                }

                manifest.omittedAttachmentRecords++;
                manifest.omittedAttachmentOriginalBytes += attachment.originalBytes;
                manifest.omittedAttachmentIncludedBytes += attachment.includedBytes;
                manifest.attachments.RemoveAt(i);
                return true;
            }

            return false;
        }

        private static void AddManifestAttachment(
            UploadManifest manifest,
            string path,
            string entryName,
            long originalBytes,
            long includedBytes,
            string status,
            string reason)
        {
            manifest.attachments.Add(new UploadManifestAttachment
            {
                path = path,
                entryName = entryName,
                originalBytes = originalBytes,
                includedBytes = includedBytes,
                status = status,
                reason = reason
            });

            if (status == "truncated")
            {
                manifest.partial = true;
                manifest.truncated = true;
            }
            else if (status == "skipped")
            {
                manifest.partial = true;
                manifest.skipped = true;
            }
        }

        private static bool TryReadFileBytes(string path, out byte[] bytes, out Exception lastException)
        {
            bytes = Array.Empty<byte>();
            lastException = null;

            for (int attempt = 1; attempt <= MaxFileReadAttempts; attempt++)
            {
                try
                {
                    using var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var memory = new MemoryStream();
                    stream.CopyTo(memory);
                    bytes = memory.ToArray();
                    return true;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    lastException = e;
                    if (attempt < MaxFileReadAttempts)
                        Thread.Sleep(FileReadRetryDelayMs);
                }
            }

            return false;
        }

        private static bool TryReadTailFileBytes(
            string path,
            long maxBytes,
            out byte[] bytes,
            out Exception lastException)
        {
            bytes = Array.Empty<byte>();
            lastException = null;

            for (int attempt = 1; attempt <= MaxFileReadAttempts; attempt++)
            {
                try
                {
                    using var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    int bytesToRead = (int)Math.Min(stream.Length, maxBytes);
                    bytes = new byte[bytesToRead];
                    stream.Seek(Math.Max(0, stream.Length - bytesToRead), SeekOrigin.Begin);

                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read == 0)
                            break;

                        offset += read;
                    }

                    if (offset != bytes.Length)
                        Array.Resize(ref bytes, offset);

                    return true;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    lastException = e;
                    if (attempt < MaxFileReadAttempts)
                        Thread.Sleep(FileReadRetryDelayMs);
                }
            }

            return false;
        }

        private static long GetFileLength(string path)
        {
            try
            {
                return string.IsNullOrEmpty(path) ? 0 : new FileInfo(path).Length;
            }
            catch
            {
                return 0;
            }
        }

        private static AttachmentKind GetAttachmentKind(string path)
        {
            string fileName = Path.GetFileName(path);
            if (string.Equals(fileName, CurrentPlayerLogFileName, StringComparison.OrdinalIgnoreCase))
                return AttachmentKind.CurrentPlayerLog;
            if (string.Equals(fileName, PreviousPlayerLogFileName, StringComparison.OrdinalIgnoreCase))
                return AttachmentKind.PreviousPlayerLog;
            return AttachmentKind.Generic;
        }

        private static bool IsPlayerLog(AttachmentKind kind)
        {
            return kind == AttachmentKind.CurrentPlayerLog || kind == AttachmentKind.PreviousPlayerLog;
        }

        private static int CompareCandidatePriority(AttachmentCandidate left, AttachmentCandidate right)
        {
            int priorityCompare = left.Priority.CompareTo(right.Priority);
            return priorityCompare != 0 ? priorityCompare : left.Order.CompareTo(right.Order);
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private static bool TryPrepareScreenshot(string srcPath, string tmpDir, string prefix, out string outPath)
        {
            outPath = Path.Combine(tmpDir, $"{prefix}.jpg");
            Texture2D texIn = null;
            Texture2D scaled = null;

            try
            {
                byte[] bytes = File.ReadAllBytes(srcPath);
                texIn = new Texture2D(2, 2);
                if (!texIn.LoadImage(bytes, false))
                    return false;

                scaled = DownscaleIfNeeded(texIn);
                File.WriteAllBytes(outPath, scaled.EncodeToJPG(JpgQuality));
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException)
            {
                AnalyticsLog.LogWarning($"[ZipAttachmentUploader] 截图处理失败: {srcPath} - {e.Message}");
                return false;
            }
            finally
            {
                if (texIn != null)
                    Object.Destroy(texIn);
                if (scaled != null && scaled != texIn)
                    Object.Destroy(scaled);
            }
        }

        private static Texture2D DownscaleIfNeeded(Texture2D src)
        {
            int pixels = src.width * src.height;
            if (pixels <= MaxPixels) return src;

            float ratio = Mathf.Sqrt(MaxPixels / (float)pixels);
            int w = Mathf.RoundToInt(src.width * ratio);
            int h = Mathf.RoundToInt(src.height * ratio);

            var dst = new Texture2D(w, h, TextureFormat.RGB24, false);
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0);
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            dst.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }

        private static bool IsImageFile(string path)
        {
            string ext = Path.GetExtension(path);
            return string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildRelativeEntryName(string root, string file)
        {
            try
            {
                string fullRoot = Path.GetFullPath(root)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                  Path.DirectorySeparatorChar;
                string fullFile = Path.GetFullPath(file);
                if (fullFile.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                    return NormalizeEntryName(fullFile.Substring(fullRoot.Length));
            }
            catch
            {
                // Fall back to the file name below.
            }

            return NormalizeEntryName(Path.GetFileName(file));
        }

        private static string MakeUniqueEntryName(string entryName, HashSet<string> usedEntryNames)
        {
            entryName = NormalizeEntryName(entryName);
            if (usedEntryNames.Add(entryName))
                return entryName;

            string directory = NormalizeEntryName(Path.GetDirectoryName(entryName) ?? string.Empty);
            string prefix = string.IsNullOrEmpty(directory) ? string.Empty : directory + "/";
            string fileName = Path.GetFileNameWithoutExtension(entryName);
            string extension = Path.GetExtension(entryName);
            int suffix = 2;

            while (true)
            {
                string candidate = $"{prefix}{fileName}_{suffix}{extension}";
                if (usedEntryNames.Add(candidate))
                    return candidate;
                suffix++;
            }
        }

        private static string NormalizeEntryName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var safeSegments = new List<string>();
            string normalized = value.Replace('\\', '/');
            foreach (string rawPart in normalized.Split('/'))
            {
                if (string.IsNullOrWhiteSpace(rawPart))
                    continue;

                string safePart = SanitizeZipEntrySegment(rawPart);
                safeSegments.Add(string.IsNullOrEmpty(safePart) ? "Entry" : safePart);
            }

            return string.Join("/", safeSegments);
        }

        private static string BuildZipEntryPrefix(string version, string safeUserName, string timestamp)
        {
            string rawPrefix = $"{version}_{safeUserName}_{timestamp}";
            string safePrefix = SanitizeZipEntrySegment(rawPrefix);
            return string.IsNullOrEmpty(safePrefix) ? "Report" : safePrefix;
        }

        private static string SanitizeZipEntrySegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length);
            bool previousWasSeparator = false;

            foreach (char c in value)
            {
                bool allowed =
                    (c >= 'A' && c <= 'Z') ||
                    (c >= 'a' && c <= 'z') ||
                    (c >= '0' && c <= '9') ||
                    c == '.' ||
                    c == '-' ||
                    c == '_';

                if (allowed)
                {
                    if ((c == '.' || c == '-' || c == '_') && previousWasSeparator)
                        continue;

                    sb.Append(c);
                    previousWasSeparator = c == '.' || c == '-' || c == '_';
                }
                else
                {
                    sb.Append('u');
                    sb.Append(((int)c).ToString("X4"));
                    previousWasSeparator = false;
                }
            }

            return sb.ToString().Trim('_', '.', '-');
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path ?? string.Empty;
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
                return false;

            return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPathInsideDirectory(string path, string directory)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(directory))
                return false;

            try
            {
                string fullDirectory = Path.GetFullPath(directory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                       Path.DirectorySeparatorChar;
                string fullPath = Path.GetFullPath(path);
                return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void DeleteIfExists(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }
}
