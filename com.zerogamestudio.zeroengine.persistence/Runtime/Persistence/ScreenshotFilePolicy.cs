using System;
using System.IO;

namespace ZeroEngine.Persistence
{
    public readonly struct ScreenshotValidationResult
    {
        private ScreenshotValidationResult(
            bool valid,
            string error,
            int width,
            int height,
            long length)
        {
            IsValid = valid;
            Error = error;
            Width = width;
            Height = height;
            Length = length;
        }

        public bool IsValid { get; }
        public string Error { get; }
        public int Width { get; }
        public int Height { get; }
        public long Length { get; }

        public static ScreenshotValidationResult Valid(int width, int height, long length) =>
            new ScreenshotValidationResult(true, null, width, height, length);

        public static ScreenshotValidationResult Invalid(string error) =>
            new ScreenshotValidationResult(false, error, 0, 0, 0);
    }

    /// <summary>
    /// File and byte-level screenshot policy independent of Unity. It accepts
    /// canonical PNG/JPEG files and checks their dimensions before a backend writes.
    /// </summary>
    public sealed class ScreenshotFilePolicy
    {
        private static readonly byte[] PngSignature =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
        };

        private readonly string _rootDirectory;
        private readonly string _fileNamePrefix;
        private readonly string _extension;

        public ScreenshotFilePolicy(
            string rootDirectory,
            string fileNamePrefix = "slot_",
            string extension = ".png",
            int maxWidth = 1920,
            int maxHeight = 1080,
            long maxPixels = 2073600,
            long maxFileLength = 4 * 1024 * 1024)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("Screenshot root directory cannot be empty.", nameof(rootDirectory));
            }

            if (string.IsNullOrWhiteSpace(fileNamePrefix) ||
                fileNamePrefix.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0)
            {
                throw new ArgumentException("Screenshot file name prefix must be a single path component.", nameof(fileNamePrefix));
            }

            if (maxWidth <= 0 || maxHeight <= 0 || maxPixels <= 0 || maxFileLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxWidth), "Screenshot limits must be positive.");
            }

            var fullRoot = Path.GetFullPath(rootDirectory);
            var root = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            _rootDirectory = root.Length == 0 ? fullRoot : root;
            _fileNamePrefix = fileNamePrefix;
            _extension = NormalizeExtension(extension);
            MaxWidth = maxWidth;
            MaxHeight = maxHeight;
            MaxPixels = maxPixels;
            MaxFileLength = maxFileLength;
        }

        public string RootDirectory => _rootDirectory;
        public string FileNamePrefix => _fileNamePrefix;
        public string Extension => _extension;
        public int MaxWidth { get; }
        public int MaxHeight { get; }
        public long MaxPixels { get; }
        public long MaxFileLength { get; }

        public string GetFileName(string slotId)
        {
            if (!IsValidSlotId(slotId))
            {
                throw new ArgumentException("Slot id contains unsafe path characters.", nameof(slotId));
            }

            return _fileNamePrefix + slotId + _extension;
        }

        public bool TryGetPath(string slotId, out string path, out string error)
        {
            path = null;
            if (!IsValidSlotId(slotId))
            {
                error = "slot-id-invalid";
                return false;
            }

            if (!TryValidateRoot(out error))
            {
                return false;
            }

            var candidate = Path.GetFullPath(Path.Combine(_rootDirectory, GetFileName(slotId)));
            if (!IsWithinRoot(candidate))
            {
                error = "path-outside-root";
                return false;
            }

            if (HasReparsePoint(_rootDirectory) || HasReparsePoint(candidate))
            {
                error = "reparse-point-not-allowed";
                return false;
            }

            path = candidate;
            error = null;
            return true;
        }

        public bool TryValidatePath(string slotId, string path, out string error)
        {
            if (!TryGetPath(slotId, out var canonicalPath, out error))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            {
                error = "path-must-be-absolute";
                return false;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }

            if (!string.Equals(fullPath, canonicalPath, StringComparison.OrdinalIgnoreCase))
            {
                error = "non-canonical-file-name";
                return false;
            }

            if (HasReparsePoint(fullPath))
            {
                error = "reparse-point-not-allowed";
                return false;
            }

            error = null;
            return true;
        }

        public ScreenshotValidationResult Validate(string slotId, SaveScreenshot screenshot)
        {
            if (screenshot == null)
            {
                return ScreenshotValidationResult.Invalid("screenshot-null");
            }

            string expectedFileName;
            try
            {
                expectedFileName = GetFileName(slotId);
            }
            catch (Exception exception)
            {
                return ScreenshotValidationResult.Invalid(exception.Message);
            }

            if (!string.Equals(screenshot.FileName, expectedFileName, StringComparison.Ordinal))
            {
                return ScreenshotValidationResult.Invalid("non-canonical-file-name");
            }

            return ValidateBytes(screenshot.Data, screenshot.Width, screenshot.Height);
        }

        public ScreenshotValidationResult ValidateFile(string slotId, string path)
        {
            if (!TryValidatePath(slotId, path, out var error))
            {
                return ScreenshotValidationResult.Invalid(error);
            }

            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return ScreenshotValidationResult.Invalid("screenshot-file-missing");
                }

                if (info.Length > MaxFileLength)
                {
                    return ScreenshotValidationResult.Invalid("screenshot-file-too-large");
                }

                return ValidateBytes(File.ReadAllBytes(path), 0, 0);
            }
            catch (Exception exception)
            {
                return ScreenshotValidationResult.Invalid(exception.Message);
            }
        }

        public ScreenshotValidationResult ValidateBytes(byte[] data, int suppliedWidth = 0, int suppliedHeight = 0)
        {
            if (data == null)
            {
                return ScreenshotValidationResult.Invalid("screenshot-bytes-null");
            }

            if (data.LongLength > MaxFileLength)
            {
                return ScreenshotValidationResult.Invalid("screenshot-file-too-large");
            }

            if (!TryReadDimensions(data, out var width, out var height))
            {
                return ScreenshotValidationResult.Invalid("unsupported-or-invalid-image");
            }

            if (suppliedWidth > 0 && suppliedWidth != width || suppliedHeight > 0 && suppliedHeight != height)
            {
                return ScreenshotValidationResult.Invalid("screenshot-dimensions-mismatch");
            }

            if (width > MaxWidth || height > MaxHeight)
            {
                return ScreenshotValidationResult.Invalid("screenshot-dimensions-too-large");
            }

            if ((long)width * height > MaxPixels)
            {
                return ScreenshotValidationResult.Invalid("screenshot-pixels-too-large");
            }

            return ScreenshotValidationResult.Valid(width, height, data.LongLength);
        }

        public static bool IsValidSlotId(string slotId)
        {
            if (string.IsNullOrWhiteSpace(slotId))
            {
                return false;
            }

            foreach (var character in slotId)
            {
                if (!(character == '-' || character == '_' ||
                      character >= 'a' && character <= 'z' ||
                      character >= 'A' && character <= 'Z' ||
                      character >= '0' && character <= '9'))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryValidateRoot(out string error)
        {
            if (File.Exists(_rootDirectory))
            {
                error = "root-is-file";
                return false;
            }

            if (HasReparsePoint(_rootDirectory))
            {
                error = "root-reparse-point-not-allowed";
                return false;
            }

            error = null;
            return true;
        }

        private bool IsWithinRoot(string path)
        {
            var root = _rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                       Path.DirectorySeparatorChar;
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasReparsePoint(string path)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                var current = fullPath;
                while (!string.IsNullOrEmpty(current))
                {
                    if (File.Exists(current) || Directory.Exists(current))
                    {
                        var attributes = File.GetAttributes(current);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            return true;
                        }
                    }

                    var parent = Directory.GetParent(current);
                    if (parent == null || string.Equals(parent.FullName, current, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    current = parent.FullName;
                }
            }
            catch
            {
                // An inaccessible path must be rejected by the caller rather
                // than treated as a safe path.
                return true;
            }

            return false;
        }

        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                throw new ArgumentException("Screenshot extension cannot be empty.", nameof(extension));
            }

            var normalized = extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
            if (!string.Equals(normalized, ".png", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalized, ".jpg", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalized, ".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Screenshot policy supports PNG and JPEG files only.", nameof(extension));
            }

            return normalized.ToLowerInvariant();
        }

        private static bool TryReadDimensions(byte[] data, out int width, out int height)
        {
            if (data.Length >= 24 && IsPng(data))
            {
                width = ReadInt32BigEndian(data, 16);
                height = ReadInt32BigEndian(data, 20);
                return width > 0 && height > 0;
            }

            if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
            {
                return TryReadJpegDimensions(data, out width, out height);
            }

            width = 0;
            height = 0;
            return false;
        }

        private static bool IsPng(byte[] data)
        {
            for (var index = 0; index < PngSignature.Length; index++)
            {
                if (data[index] != PngSignature[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static int ReadInt32BigEndian(byte[] data, int offset)
        {
            return (data[offset] << 24) |
                   (data[offset + 1] << 16) |
                   (data[offset + 2] << 8) |
                   data[offset + 3];
        }

        private static bool TryReadJpegDimensions(byte[] data, out int width, out int height)
        {
            var offset = 2;
            while (offset + 3 < data.Length)
            {
                if (data[offset] != 0xFF)
                {
                    offset++;
                    continue;
                }

                while (offset < data.Length && data[offset] == 0xFF)
                {
                    offset++;
                }

                if (offset >= data.Length)
                {
                    break;
                }

                var marker = data[offset++];
                if (marker == 0xD8 || marker == 0xD9 || marker == 0x01 || marker >= 0xD0 && marker <= 0xD7)
                {
                    continue;
                }

                if (offset + 1 >= data.Length)
                {
                    break;
                }

                var segmentLength = (data[offset] << 8) | data[offset + 1];
                if (segmentLength < 2 || offset + segmentLength > data.Length)
                {
                    break;
                }

                var isStartOfFrame = marker >= 0xC0 && marker <= 0xC3 ||
                                     marker >= 0xC5 && marker <= 0xC7 ||
                                     marker >= 0xC9 && marker <= 0xCB ||
                                     marker >= 0xCD && marker <= 0xCF;
                if (isStartOfFrame && segmentLength >= 7)
                {
                    height = (data[offset + 3] << 8) | data[offset + 4];
                    width = (data[offset + 5] << 8) | data[offset + 6];
                    return width > 0 && height > 0;
                }

                offset += segmentLength;
            }

            width = 0;
            height = 0;
            return false;
        }
    }
}
