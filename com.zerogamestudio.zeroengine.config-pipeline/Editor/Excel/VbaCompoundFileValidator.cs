using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    /// <summary>
    /// Performs structural validation of the OLE Compound File Binary payload
    /// carried by an XLSM VBA project part. This validator never interprets or
    /// executes VBA source or p-code.
    /// </summary>
    internal static class VbaCompoundFileValidator
    {
        private const string ErrorCode = "XLSX_VBA_PACKAGE_INVALID";
        private const long MaximumCompoundFileBytes =
            XlsxWorkbookLimits.DefaultExpandedBytes;

        public static void Validate(Stream stream, string workbookName)
        {
            long originalPosition = 0;
            bool restorePosition = false;
            MemoryStream snapshot = null;
            Exception failure = null;

            try
            {
                if (stream == null)
                {
                    throw new InvalidDataException("The VBA project stream is missing.");
                }

                if (!stream.CanRead)
                {
                    throw new InvalidDataException(
                        "The VBA project stream must be readable.");
                }

                Stream validationStream = stream;
                if (stream.CanSeek)
                {
                    originalPosition = stream.Position;
                    restorePosition = true;
                    if (stream.Length > MaximumCompoundFileBytes)
                    {
                        throw new InvalidDataException(
                            "The VBA project stream exceeds the expanded-size limit.");
                    }
                }
                else
                {
                    snapshot = Snapshot(stream);
                    validationStream = snapshot;
                }

                new Validator(validationStream).Validate();
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (restorePosition)
            {
                try
                {
                    stream.Position = originalPosition;
                }
                catch (Exception exception)
                {
                    if (failure == null)
                    {
                        failure = exception;
                    }
                }
            }

            if (snapshot != null)
            {
                try
                {
                    snapshot.Dispose();
                }
                catch (Exception exception)
                {
                    if (failure == null)
                    {
                        failure = exception;
                    }
                }
            }

            if (failure != null)
            {
                throw new XlsxConfigException(
                    ErrorCode,
                    "The VBA project compound file is invalid: " + failure.Message,
                    workbookName,
                    null,
                    null,
                    null);
            }
        }

        private static MemoryStream Snapshot(Stream source)
        {
            var snapshot = new MemoryStream();
            byte[] buffer = new byte[81920];
            long total = 0;
            try
            {
                while (true)
                {
                    int read = source.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                    {
                        break;
                    }

                    total = checked(total + read);
                    if (total > MaximumCompoundFileBytes)
                    {
                        throw new InvalidDataException(
                            "The VBA project stream exceeds the expanded-size limit.");
                    }

                    snapshot.Write(buffer, 0, read);
                }

                snapshot.Position = 0;
                return snapshot;
            }
            catch
            {
                snapshot.Dispose();
                throw;
            }
        }

        private sealed class Validator
        {
            private const uint FreeSector = 0xFFFFFFFFU;
            private const uint EndOfChain = 0xFFFFFFFEU;
            private const uint FatSector = 0xFFFFFFFDU;
            private const uint DifatSector = 0xFFFFFFFCU;
            private const uint MaxRegularSector = 0xFFFFFFFAU;
            private const uint NoStream = 0xFFFFFFFFU;

            private const int HeaderLength = 512;
            private const int HeaderDifatEntries = 109;
            private const int DirectoryEntryLength = 128;
            private const int MiniSectorSize = 64;
            private const int MiniStreamCutoff = 4096;
            private const int MaximumSectorCount = 4 * 1024 * 1024;
            private const int MaximumDirectoryEntryCount = 1024 * 1024;
            private const int MaximumCompressedDirectoryBytes = 64 * 1024 * 1024;

            private static readonly byte[] Signature =
            {
                0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1
            };

            private readonly Stream stream;
            private readonly HashSet<int> fatSectorIds = new HashSet<int>();
            private readonly HashSet<int> difatSectorIds = new HashSet<int>();
            private readonly Dictionary<int, string> regularSectorOwners =
                new Dictionary<int, string>();
            private readonly Dictionary<int, string> miniSectorOwners =
                new Dictionary<int, string>();

            private byte[] sectorBuffer;
            private uint[] fat;
            private uint[] miniFat;
            private DirectoryEntry[] directoryEntries;
            private int[] parentIds;
            private List<int> rootMiniStreamChain;
            private int rootMiniSectorCount;
            private int sectorSize;
            private int sectorCount;
            private int majorVersion;
            private int fatSectorCount;
            private int directorySectorCount;
            private int miniFatSectorCount;
            private int difatSectorCount;
            private uint firstDirectorySector;
            private uint firstMiniFatSector;
            private uint firstDifatSector;
            private readonly uint[] headerDifat = new uint[HeaderDifatEntries];

            public Validator(Stream stream)
            {
                this.stream = stream;
            }

            public void Validate()
            {
                ReadAndValidateHeader();
                List<int> fatSectors = ReadDifat();
                ReadAndValidateFat(fatSectors);
                ReadAndValidateDirectory();
                ReadAndValidateMiniFat();
                ValidateDirectoryHierarchy();

                int rootId = 0;
                int projectId = FindRequiredChild(rootId, "PROJECT", 2);
                int vbaStorageId = FindRequiredChild(rootId, "VBA", 1);
                int dirId = FindRequiredChild(vbaStorageId, "dir", 2);
                int vbaProjectId = FindRequiredChild(vbaStorageId, "_VBA_PROJECT", 2);

                ValidateAllStreamChains(dirId, out List<int> dirRegularChain,
                    out List<int> dirMiniChain);
                ValidateAllocationOwnership();

                DirectoryEntry project = directoryEntries[projectId];
                DirectoryEntry directory = directoryEntries[dirId];
                DirectoryEntry vbaProject = directoryEntries[vbaProjectId];
                if (project.StreamSize == 0)
                {
                    Fail("The root PROJECT stream is empty.");
                }

                if (directory.StreamSize == 0)
                {
                    Fail("The VBA/dir stream is empty.");
                }

                if (vbaProject.StreamSize < 7)
                {
                    Fail("The VBA/_VBA_PROJECT stream is shorter than seven bytes.");
                }

                byte[] compressedDirectory = ReadDirectoryStream(
                    directory,
                    dirRegularChain,
                    dirMiniChain);
                ValidateCompressedDirectory(compressedDirectory);
            }

            private void ReadAndValidateHeader()
            {
                if (stream.Length < HeaderLength)
                {
                    Fail("The compound file header is truncated.");
                }

                byte[] header = new byte[HeaderLength];
                ReadExact(0, header, 0, header.Length);
                for (int index = 0; index < Signature.Length; index++)
                {
                    if (header[index] != Signature[index])
                    {
                        Fail("The compound file signature is invalid.");
                    }
                }

                for (int index = 8; index < 24; index++)
                {
                    if (header[index] != 0)
                    {
                        Fail("The compound file header CLSID must be zero.");
                    }
                }

                if (ReadUInt16(header, 24) != 0x003E)
                {
                    Fail("The compound file minor version is invalid.");
                }

                majorVersion = ReadUInt16(header, 26);
                if (majorVersion != 3 && majorVersion != 4)
                {
                    Fail("Only compound file major versions 3 and 4 are supported.");
                }

                if (ReadUInt16(header, 28) != 0xFFFE)
                {
                    Fail("The compound file byte order is invalid.");
                }

                int sectorShift = ReadUInt16(header, 30);
                int expectedSectorShift = majorVersion == 3 ? 9 : 12;
                if (sectorShift != expectedSectorShift)
                {
                    Fail("The compound file sector shift does not match its version.");
                }

                if (ReadUInt16(header, 32) != 6)
                {
                    Fail("The compound file mini-sector shift is invalid.");
                }

                for (int index = 34; index < 40; index++)
                {
                    if (header[index] != 0)
                    {
                        Fail("The compound file reserved header bytes must be zero.");
                    }
                }

                sectorSize = 1 << sectorShift;
                if (stream.Length < sectorSize * 2L || stream.Length % sectorSize != 0)
                {
                    Fail("The compound file length is truncated or not sector-aligned.");
                }

                long sectorCountValue = checked(stream.Length / sectorSize - 1L);
                if (sectorCountValue <= 0 || sectorCountValue > MaximumSectorCount)
                {
                    Fail("The compound file sector count exceeds the validation limit.");
                }

                sectorCount = checked((int)sectorCountValue);
                sectorBuffer = new byte[sectorSize];

                if (majorVersion == 4)
                {
                    byte[] padding = new byte[sectorSize - HeaderLength];
                    ReadExact(HeaderLength, padding, 0, padding.Length);
                    for (int index = 0; index < padding.Length; index++)
                    {
                        if (padding[index] != 0)
                        {
                            Fail("The version 4 header padding must be zero.");
                        }
                    }
                }

                directorySectorCount = ToBoundedCount(
                    ReadUInt32(header, 40),
                    "directory sector count");
                fatSectorCount = ToBoundedCount(
                    ReadUInt32(header, 44),
                    "FAT sector count");
                firstDirectorySector = ReadUInt32(header, 48);
                if (ReadUInt32(header, 56) != MiniStreamCutoff)
                {
                    Fail("The compound file mini-stream cutoff is invalid.");
                }

                firstMiniFatSector = ReadUInt32(header, 60);
                miniFatSectorCount = ToBoundedCount(
                    ReadUInt32(header, 64),
                    "miniFAT sector count");
                firstDifatSector = ReadUInt32(header, 68);
                difatSectorCount = ToBoundedCount(
                    ReadUInt32(header, 72),
                    "DIFAT sector count");

                if (majorVersion == 3 && directorySectorCount != 0)
                {
                    Fail("Version 3 compound files must report zero directory sectors.");
                }

                if (majorVersion == 4 && directorySectorCount == 0)
                {
                    Fail("Version 4 compound files must report their directory sectors.");
                }

                if (fatSectorCount == 0)
                {
                    Fail("The compound file does not contain a FAT.");
                }

                for (int index = 0; index < HeaderDifatEntries; index++)
                {
                    headerDifat[index] = ReadUInt32(header, 76 + index * 4);
                }
            }

            private List<int> ReadDifat()
            {
                int entriesPerSector = sectorSize / 4;
                int expectedDifatCount = fatSectorCount <= HeaderDifatEntries
                    ? 0
                    : CeilingDivide(fatSectorCount - HeaderDifatEntries,
                        entriesPerSector - 1);
                if (difatSectorCount != expectedDifatCount)
                {
                    Fail("The DIFAT sector count does not match the FAT count.");
                }

                if (expectedDifatCount == 0)
                {
                    if (firstDifatSector != EndOfChain)
                    {
                        Fail("The first DIFAT sector must be end-of-chain when no DIFAT exists.");
                    }
                }
                else
                {
                    ValidatePhysicalSector(firstDifatSector, "first DIFAT sector");
                }

                var fatSectors = new List<int>(fatSectorCount);
                for (int index = 0; index < HeaderDifatEntries; index++)
                {
                    AddFatSectorReference(headerDifat[index], fatSectors,
                        "header DIFAT");
                }

                uint currentDifat = firstDifatSector;
                for (int difatIndex = 0; difatIndex < expectedDifatCount; difatIndex++)
                {
                    int sectorId = ValidatePhysicalSector(currentDifat, "DIFAT sector");
                    if (!difatSectorIds.Add(sectorId))
                    {
                        Fail("The DIFAT chain contains a duplicate or cycle.");
                    }

                    if (fatSectorIds.Contains(sectorId))
                    {
                        Fail("A sector is shared by the DIFAT and FAT.");
                    }

                    ReadSector(sectorId, sectorBuffer);
                    for (int entry = 0; entry < entriesPerSector - 1; entry++)
                    {
                        AddFatSectorReference(
                            ReadUInt32(sectorBuffer, entry * 4),
                            fatSectors,
                            "DIFAT sector");
                    }

                    uint nextDifat = ReadUInt32(
                        sectorBuffer,
                        (entriesPerSector - 1) * 4);
                    bool isLast = difatIndex == expectedDifatCount - 1;
                    if (isLast)
                    {
                        if (nextDifat != EndOfChain)
                        {
                            Fail("The DIFAT chain does not terminate at its declared count.");
                        }
                    }
                    else
                    {
                        ValidatePhysicalSector(nextDifat, "next DIFAT sector");
                    }

                    currentDifat = nextDifat;
                }

                if (fatSectors.Count != fatSectorCount)
                {
                    Fail("The DIFAT does not contain the declared number of FAT sectors.");
                }

                long fatEntryCapacity = checked((long)fatSectorCount * entriesPerSector);
                if (fatEntryCapacity < sectorCount)
                {
                    Fail("The FAT does not cover every physical sector.");
                }

                return fatSectors;
            }

            private void AddFatSectorReference(
                uint value,
                List<int> fatSectors,
                string source)
            {
                if (fatSectors.Count >= fatSectorCount)
                {
                    if (value != FreeSector)
                    {
                        Fail(source + " contains entries after the declared FAT count.");
                    }

                    return;
                }

                if (value == FreeSector)
                {
                    Fail(source + " ends before the declared FAT count.");
                }

                int sectorId = ValidatePhysicalSector(value, source + " FAT reference");
                if (!fatSectorIds.Add(sectorId))
                {
                    Fail("The DIFAT contains a duplicate FAT sector.");
                }

                if (difatSectorIds.Contains(sectorId))
                {
                    Fail("A sector is shared by the DIFAT and FAT.");
                }

                fatSectors.Add(sectorId);
            }

            private void ReadAndValidateFat(List<int> fatSectors)
            {
                fat = new uint[sectorCount];
                int entriesPerSector = sectorSize / 4;
                long logicalEntry = 0;
                foreach (int fatSectorId in fatSectors)
                {
                    ReadSector(fatSectorId, sectorBuffer);
                    for (int entry = 0; entry < entriesPerSector; entry++)
                    {
                        uint value = ReadUInt32(sectorBuffer, entry * 4);
                        if (logicalEntry < sectorCount)
                        {
                            ValidateFatValue(value);
                            fat[(int)logicalEntry] = value;
                        }
                        // Office VBA payloads can zero-fill unused FAT capacity.
                        else if (value != FreeSector && value != 0U)
                        {
                            Fail("FAT entries beyond the physical file must be free.");
                        }

                        logicalEntry++;
                    }
                }

                for (int sectorId = 0; sectorId < sectorCount; sectorId++)
                {
                    uint marker = fat[sectorId];
                    bool isFat = fatSectorIds.Contains(sectorId);
                    bool isDifat = difatSectorIds.Contains(sectorId);
                    if (isFat != (marker == FatSector))
                    {
                        Fail("The FAT sector markers do not match the DIFAT.");
                    }

                    if (isDifat != (marker == DifatSector))
                    {
                        Fail("The DIFAT sector markers do not match the FAT.");
                    }

                    if (isFat || isDifat)
                    {
                        regularSectorOwners.Add(
                            sectorId,
                            isFat ? "FAT" : "DIFAT");
                    }
                }
            }

            private void ReadAndValidateDirectory()
            {
                int? expectedCount = majorVersion == 4
                    ? directorySectorCount
                    : (int?)null;
                List<int> chain = ReadFatChain(
                    firstDirectorySector,
                    expectedCount,
                    "directory");
                if (chain.Count == 0)
                {
                    Fail("The compound file directory is missing.");
                }

                long entryCountValue = checked(
                    (long)chain.Count * (sectorSize / DirectoryEntryLength));
                if (entryCountValue <= 0 ||
                    entryCountValue > MaximumDirectoryEntryCount)
                {
                    Fail("The compound file directory exceeds the validation limit.");
                }

                directoryEntries = new DirectoryEntry[(int)entryCountValue];
                int directoryId = 0;
                foreach (int sectorId in chain)
                {
                    ReadSector(sectorId, sectorBuffer);
                    for (int offset = 0;
                        offset < sectorSize;
                        offset += DirectoryEntryLength)
                    {
                        directoryEntries[directoryId] = ParseDirectoryEntry(
                            sectorBuffer,
                            offset,
                            directoryId);
                        directoryId++;
                    }
                }

                if (directoryEntries[0].ObjectType != 5)
                {
                    Fail("Directory entry 0 must be the root storage.");
                }

                for (int index = 1; index < directoryEntries.Length; index++)
                {
                    if (directoryEntries[index].ObjectType == 5)
                    {
                        Fail("Only directory entry 0 may be the root storage.");
                    }
                }
            }

            private DirectoryEntry ParseDirectoryEntry(
                byte[] bytes,
                int offset,
                int directoryId)
            {
                int nameLength = ReadUInt16(bytes, offset + 64);
                int objectType = bytes[offset + 66];
                if (objectType != 0 && objectType != 1 &&
                    objectType != 2 && objectType != 5)
                {
                    Fail("Directory entry " + directoryId + " has an invalid object type.");
                }

                string name = string.Empty;
                if (objectType != 0)
                {
                    if (nameLength < 2 || nameLength > 64 || (nameLength & 1) != 0)
                    {
                        Fail("Directory entry " + directoryId + " has an invalid name length.");
                    }

                    if (ReadUInt16(bytes, offset + nameLength - 2) != 0)
                    {
                        Fail("Directory entry " + directoryId + " name is not null-terminated.");
                    }

                    name = Encoding.Unicode.GetString(bytes, offset, nameLength - 2);
                    if (name.IndexOf('\0') >= 0)
                    {
                        Fail("Directory entry " + directoryId + " name contains an embedded null.");
                    }

                    int color = bytes[offset + 67];
                    if (color != 0 && color != 1)
                    {
                        Fail("Directory entry " + directoryId + " has an invalid color flag.");
                    }
                }
                else if (nameLength != 0)
                {
                    Fail("An unused directory entry has a non-zero name length.");
                }

                uint leftSibling = ReadUInt32(bytes, offset + 68);
                uint rightSibling = ReadUInt32(bytes, offset + 72);
                uint child = ReadUInt32(bytes, offset + 76);
                uint startSector = ReadUInt32(bytes, offset + 116);
                ulong streamSize = ReadUInt64(bytes, offset + 120);

                if (majorVersion == 3 && (streamSize >> 32) != 0)
                {
                    Fail("Version 3 directory stream sizes must fit in 32 bits.");
                }

                if (objectType == 2 && child != NoStream)
                {
                    Fail("A stream directory entry cannot own child entries.");
                }

                if (objectType == 1 && streamSize != 0)
                {
                    Fail("A storage directory entry must have a zero stream size.");
                }

                return new DirectoryEntry(
                    directoryId,
                    name,
                    objectType,
                    leftSibling,
                    rightSibling,
                    child,
                    startSector,
                    streamSize);
            }

            private void ReadAndValidateMiniFat()
            {
                if (miniFatSectorCount == 0)
                {
                    if (firstMiniFatSector != EndOfChain)
                    {
                        Fail("The first miniFAT sector must be end-of-chain when no miniFAT exists.");
                    }

                    miniFat = Array.Empty<uint>();
                    return;
                }

                List<int> chain = ReadFatChain(
                    firstMiniFatSector,
                    miniFatSectorCount,
                    "miniFAT");
                int entriesPerSector = sectorSize / 4;
                int entryCount = checked(chain.Count * entriesPerSector);
                miniFat = new uint[entryCount];
                int destination = 0;
                foreach (int sectorId in chain)
                {
                    ReadSector(sectorId, sectorBuffer);
                    for (int entry = 0; entry < entriesPerSector; entry++)
                    {
                        uint value = ReadUInt32(sectorBuffer, entry * 4);
                        if (value != FreeSector && value != EndOfChain &&
                            value >= entryCount)
                        {
                            Fail("The miniFAT contains an out-of-range or reserved sector value.");
                        }

                        miniFat[destination++] = value;
                    }
                }
            }

            private void ValidateDirectoryHierarchy()
            {
                parentIds = new int[directoryEntries.Length];
                for (int index = 0; index < parentIds.Length; index++)
                {
                    parentIds[index] = -2;
                }

                parentIds[0] = -1;
                DirectoryEntry root = directoryEntries[0];
                if (root.LeftSibling != NoStream || root.RightSibling != NoStream)
                {
                    Fail("The root directory entry cannot have siblings.");
                }

                for (int index = 0; index < directoryEntries.Length; index++)
                {
                    DirectoryEntry entry = directoryEntries[index];
                    if (!entry.IsActive)
                    {
                        continue;
                    }

                    ValidateDirectoryReference(entry.LeftSibling, "left sibling");
                    ValidateDirectoryReference(entry.RightSibling, "right sibling");
                    if (entry.ObjectType == 1 || entry.ObjectType == 5)
                    {
                        ValidateDirectoryReference(entry.Child, "child");
                    }
                }

                for (int parentId = 0;
                    parentId < directoryEntries.Length;
                    parentId++)
                {
                    DirectoryEntry parent = directoryEntries[parentId];
                    if (!parent.IsStorage || parent.Child == NoStream)
                    {
                        continue;
                    }

                    var stack = new Stack<int>();
                    AssignDirectoryOwner(parent.Child, parentId, stack);
                    while (stack.Count > 0)
                    {
                        int childId = stack.Pop();
                        DirectoryEntry child = directoryEntries[childId];
                        AssignDirectoryOwner(child.LeftSibling, parentId, stack);
                        AssignDirectoryOwner(child.RightSibling, parentId, stack);
                    }
                }

                for (int index = 1; index < directoryEntries.Length; index++)
                {
                    if (directoryEntries[index].IsActive && parentIds[index] == -2)
                    {
                        Fail("Directory entry " + index + " is not owned by a storage.");
                    }
                }
            }

            private void AssignDirectoryOwner(
                uint reference,
                int parentId,
                Stack<int> stack)
            {
                if (reference == NoStream)
                {
                    return;
                }

                int childId = ValidateDirectoryReference(reference, "sibling tree");
                if (childId == 0)
                {
                    Fail("The root directory entry cannot appear in a sibling tree.");
                }

                if (parentIds[childId] != -2)
                {
                    Fail("A directory sibling tree contains a cycle or duplicate ownership.");
                }

                parentIds[childId] = parentId;
                stack.Push(childId);
            }

            private int FindRequiredChild(int parentId, string name, int objectType)
            {
                int found = -1;
                for (int index = 1; index < directoryEntries.Length; index++)
                {
                    DirectoryEntry entry = directoryEntries[index];
                    if (!entry.IsActive || parentIds[index] != parentId ||
                        !string.Equals(entry.Name, name,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (found >= 0)
                    {
                        Fail("The required " + name + " entry is duplicated.");
                    }

                    if (entry.ObjectType != objectType)
                    {
                        Fail("The required " + name + " entry has the wrong object type.");
                    }

                    found = index;
                }

                if (found < 0)
                {
                    Fail("The required " + name + " entry is missing from its storage.");
                }

                return found;
            }

            private void ValidateAllStreamChains(
                int dirId,
                out List<int> dirRegularChain,
                out List<int> dirMiniChain)
            {
                DirectoryEntry root = directoryEntries[0];
                if (root.StreamSize % MiniSectorSize != 0)
                {
                    Fail("The root mini stream size must be mini-sector aligned.");
                }

                int rootSectorLength = StreamSectorLength(root.StreamSize, sectorSize);
                rootMiniStreamChain = ReadFatChain(
                    root.StartSector,
                    rootSectorLength,
                    "root mini stream");
                rootMiniSectorCount = StreamSectorLength(
                    root.StreamSize,
                    MiniSectorSize);
                if (rootMiniSectorCount > miniFat.Length)
                {
                    Fail("The miniFAT does not cover the root mini stream.");
                }

                for (int index = rootMiniSectorCount; index < miniFat.Length; index++)
                {
                    // Office VBA payloads can zero-fill unused miniFAT capacity.
                    if (miniFat[index] != FreeSector && miniFat[index] != 0U)
                    {
                        Fail("miniFAT entries outside the root mini stream must be free.");
                    }
                }

                dirRegularChain = null;
                dirMiniChain = null;
                for (int index = 1; index < directoryEntries.Length; index++)
                {
                    DirectoryEntry entry = directoryEntries[index];
                    if (entry.ObjectType != 2)
                    {
                        continue;
                    }

                    if (entry.StreamSize == 0)
                    {
                        if (entry.StartSector != EndOfChain)
                        {
                            Fail("A zero-length stream must start at end-of-chain.");
                        }

                        if (index == dirId)
                        {
                            dirMiniChain = new List<int>();
                        }

                        continue;
                    }

                    if (entry.StreamSize < MiniStreamCutoff)
                    {
                        List<int> chain = ReadMiniFatChain(
                            entry.StartSector,
                            StreamSectorLength(entry.StreamSize, MiniSectorSize),
                            "mini stream " + entry.Name);
                        if (index == dirId)
                        {
                            dirMiniChain = chain;
                        }
                    }
                    else
                    {
                        List<int> chain = ReadFatChain(
                            entry.StartSector,
                            StreamSectorLength(entry.StreamSize, sectorSize),
                            "stream " + entry.Name);
                        if (index == dirId)
                        {
                            dirRegularChain = chain;
                        }
                    }
                }

                if (dirRegularChain == null && dirMiniChain == null)
                {
                    Fail("The VBA/dir stream chain was not validated.");
                }
            }

            private List<int> ReadFatChain(
                uint startSector,
                int? expectedSectorCount,
                string owner)
            {
                int expected = expectedSectorCount ?? -1;
                if (expected == 0)
                {
                    if (startSector != EndOfChain)
                    {
                        Fail(owner + " has a start sector despite having zero length.");
                    }

                    return new List<int>();
                }

                var chain = expected > 0
                    ? new List<int>(expected)
                    : new List<int>();
                var visited = new HashSet<int>();
                uint current = startSector;
                while (current != EndOfChain)
                {
                    if (chain.Count >= sectorCount)
                    {
                        Fail(owner + " chain exceeds the physical sector count.");
                    }

                    int sectorId = ValidatePhysicalSector(current, owner + " chain");
                    if (!visited.Add(sectorId))
                    {
                        Fail(owner + " chain contains a cycle.");
                    }

                    ClaimRegularSector(sectorId, owner);
                    chain.Add(sectorId);
                    current = fat[sectorId];
                    if (current != EndOfChain && current >= MaxRegularSector)
                    {
                        Fail(owner + " chain contains a free or reserved marker.");
                    }
                }

                if (expected >= 0 && chain.Count != expected)
                {
                    Fail(owner + " chain length does not match its declared size.");
                }

                return chain;
            }

            private List<int> ReadMiniFatChain(
                uint startSector,
                int expectedSectorCount,
                string owner)
            {
                if (expectedSectorCount <= 0)
                {
                    Fail(owner + " has an invalid expected chain length.");
                }

                if (expectedSectorCount > miniFat.Length ||
                    expectedSectorCount > rootMiniSectorCount)
                {
                    Fail(owner + " chain length exceeds the available mini stream.");
                }

                var chain = new List<int>(expectedSectorCount);
                var visited = new HashSet<int>();
                uint current = startSector;
                while (current != EndOfChain)
                {
                    if (chain.Count >= miniFat.Length)
                    {
                        Fail(owner + " chain exceeds the miniFAT.");
                    }

                    if (current >= miniFat.Length || current >= rootMiniSectorCount)
                    {
                        Fail(owner + " chain references a mini sector outside the root mini stream.");
                    }

                    int miniSectorId = (int)current;
                    if (!visited.Add(miniSectorId))
                    {
                        Fail(owner + " chain contains a cycle.");
                    }

                    if (miniSectorOwners.TryGetValue(miniSectorId, out string existingOwner))
                    {
                        Fail(owner + " shares a mini sector with " + existingOwner + ".");
                    }

                    miniSectorOwners.Add(miniSectorId, owner);
                    chain.Add(miniSectorId);
                    current = miniFat[miniSectorId];
                    if (current != EndOfChain && current != FreeSector &&
                        current >= miniFat.Length)
                    {
                        Fail(owner + " chain contains a reserved or out-of-range marker.");
                    }

                    if (current == FreeSector)
                    {
                        Fail(owner + " chain terminates in a free mini sector.");
                    }
                }

                if (chain.Count != expectedSectorCount)
                {
                    Fail(owner + " chain length does not match its declared size.");
                }

                return chain;
            }

            private void ValidateAllocationOwnership()
            {
                for (int sectorId = 0; sectorId < sectorCount; sectorId++)
                {
                    if (fat[sectorId] != FreeSector &&
                        !regularSectorOwners.ContainsKey(sectorId))
                    {
                        // Office can retain an empty terminal sector after a stream shrinks.
                        if (fat[sectorId] == EndOfChain && IsZeroFilledSector(sectorId))
                        {
                            continue;
                        }

                        Fail("The FAT contains an allocated sector that is not owned by a stream or system chain.");
                    }
                }

                for (int miniSectorId = 0;
                    miniSectorId < rootMiniSectorCount;
                    miniSectorId++)
                {
                    if (miniFat[miniSectorId] != FreeSector &&
                        !miniSectorOwners.ContainsKey(miniSectorId))
                    {
                        Fail("The miniFAT contains an allocated mini sector that is not owned by a stream.");
                    }
                }
            }

            private bool IsZeroFilledSector(int sectorId)
            {
                ReadSector(sectorId, sectorBuffer);
                for (int index = 0; index < sectorBuffer.Length; index++)
                {
                    if (sectorBuffer[index] != 0)
                    {
                        return false;
                    }
                }

                return true;
            }

            private byte[] ReadDirectoryStream(
                DirectoryEntry entry,
                List<int> regularChain,
                List<int> miniChain)
            {
                if (entry.StreamSize > MaximumCompressedDirectoryBytes)
                {
                    Fail("The VBA/dir stream exceeds the validation limit.");
                }

                int length = checked((int)entry.StreamSize);
                byte[] result = new byte[length];
                int written = 0;
                if (entry.StreamSize < MiniStreamCutoff)
                {
                    foreach (int miniSectorId in miniChain)
                    {
                        int logicalOffset = checked(miniSectorId * MiniSectorSize);
                        int rootSectorIndex = logicalOffset / sectorSize;
                        int rootSectorOffset = logicalOffset % sectorSize;
                        if (rootSectorIndex < 0 ||
                            rootSectorIndex >= rootMiniStreamChain.Count ||
                            rootSectorOffset + MiniSectorSize > sectorSize)
                        {
                            Fail("The VBA/dir mini sector lies outside the root mini stream chain.");
                        }

                        ReadSector(rootMiniStreamChain[rootSectorIndex], sectorBuffer);
                        int copyLength = Math.Min(MiniSectorSize, result.Length - written);
                        Buffer.BlockCopy(
                            sectorBuffer,
                            rootSectorOffset,
                            result,
                            written,
                            copyLength);
                        written += copyLength;
                    }
                }
                else
                {
                    foreach (int sectorId in regularChain)
                    {
                        ReadSector(sectorId, sectorBuffer);
                        int copyLength = Math.Min(sectorSize, result.Length - written);
                        Buffer.BlockCopy(sectorBuffer, 0, result, written, copyLength);
                        written += copyLength;
                    }
                }

                if (written != result.Length)
                {
                    Fail("The VBA/dir stream is truncated.");
                }

                return result;
            }

            private static void ValidateCompressedDirectory(byte[] bytes)
            {
                if (bytes.Length < 4 || bytes[0] != 0x01)
                {
                    Fail("The VBA/dir compressed container signature is invalid.");
                }

                int position = 1;
                int chunkCount = 0;
                while (position < bytes.Length)
                {
                    if (bytes.Length - position < 2)
                    {
                        Fail("The VBA/dir compressed chunk header is truncated.");
                    }

                    int header = ReadUInt16(bytes, position);
                    int chunkLength = (header & 0x0FFF) + 3;
                    int signature = (header >> 12) & 0x07;
                    bool compressed = (header & 0x8000) != 0;
                    if (signature != 0x03)
                    {
                        Fail("The VBA/dir compressed chunk signature is invalid.");
                    }

                    int chunkEnd = checked(position + chunkLength);
                    if (chunkEnd > bytes.Length || chunkEnd <= position + 2)
                    {
                        Fail("The VBA/dir compressed chunk length is invalid.");
                    }

                    int dataPosition = position + 2;
                    if (!compressed)
                    {
                        if (header != 0x3FFF || chunkLength != 4098)
                        {
                            Fail("An uncompressed VBA/dir chunk has an invalid length.");
                        }
                    }
                    else
                    {
                        ValidateCompressedChunk(bytes, dataPosition, chunkEnd);
                    }

                    position = chunkEnd;
                    chunkCount++;
                }

                if (chunkCount == 0 || position != bytes.Length)
                {
                    Fail("The VBA/dir compressed container is incomplete.");
                }
            }

            private static void ValidateCompressedChunk(
                byte[] bytes,
                int position,
                int chunkEnd)
            {
                int decompressedLength = 0;
                while (position < chunkEnd)
                {
                    int flags = bytes[position++];
                    for (int tokenIndex = 0;
                        tokenIndex < 8 && position < chunkEnd;
                        tokenIndex++)
                    {
                        bool isCopyToken = (flags & (1 << tokenIndex)) != 0;
                        if (!isCopyToken)
                        {
                            decompressedLength++;
                            position++;
                        }
                        else
                        {
                            if (chunkEnd - position < 2 || decompressedLength == 0)
                            {
                                Fail("The VBA/dir compressed chunk contains a truncated or invalid copy token.");
                            }

                            int token = ReadUInt16(bytes, position);
                            position += 2;
                            int bitCount = CopyTokenBitCount(decompressedLength);
                            int lengthMask = 0xFFFF >> bitCount;
                            int offset = (token >> (16 - bitCount)) + 1;
                            int length = (token & lengthMask) + 3;
                            if (offset > decompressedLength ||
                                decompressedLength + length > 4096)
                            {
                                Fail("The VBA/dir compressed chunk contains an out-of-range copy token.");
                            }

                            decompressedLength += length;
                        }

                        if (decompressedLength > 4096)
                        {
                            Fail("The VBA/dir compressed chunk expands beyond 4096 bytes.");
                        }
                    }
                }

                if (position != chunkEnd)
                {
                    Fail("The VBA/dir compressed chunk was not fully consumed.");
                }
            }

            private static int CopyTokenBitCount(int decompressedLength)
            {
                int value = decompressedLength - 1;
                int bitCount = 0;
                while (value > 0)
                {
                    bitCount++;
                    value >>= 1;
                }

                if (bitCount < 4)
                {
                    return 4;
                }

                return bitCount > 12 ? 12 : bitCount;
            }

            private int ValidateDirectoryReference(uint reference, string role)
            {
                if (reference == NoStream)
                {
                    return -1;
                }

                if (reference >= directoryEntries.Length)
                {
                    Fail("A directory " + role + " reference is out of range.");
                }

                int directoryId = (int)reference;
                if (!directoryEntries[directoryId].IsActive)
                {
                    Fail("A directory " + role + " reference targets an unused entry.");
                }

                return directoryId;
            }

            private int ValidatePhysicalSector(uint sector, string role)
            {
                if (sector >= MaxRegularSector || sector >= sectorCount)
                {
                    Fail("The " + role + " is reserved or outside the physical file.");
                }

                return (int)sector;
            }

            private void ClaimRegularSector(int sectorId, string owner)
            {
                if (regularSectorOwners.TryGetValue(sectorId, out string existingOwner))
                {
                    Fail(owner + " shares a physical sector with " + existingOwner + ".");
                }

                regularSectorOwners.Add(sectorId, owner);
            }

            private void ValidateFatValue(uint value)
            {
                if (value < sectorCount || value == FreeSector ||
                    value == EndOfChain || value == FatSector || value == DifatSector)
                {
                    return;
                }

                Fail("The FAT contains an out-of-range or reserved sector value.");
            }

            private int ToBoundedCount(uint value, string role)
            {
                if (value > sectorCount || value > int.MaxValue)
                {
                    Fail("The " + role + " exceeds the physical sector count.");
                }

                return (int)value;
            }

            private int StreamSectorLength(ulong streamLength, int unitSize)
            {
                ulong maximumLength = checked((ulong)sectorCount * (ulong)sectorSize);
                if (streamLength > maximumLength)
                {
                    Fail("A directory stream size exceeds the physical file capacity.");
                }

                ulong result = streamLength == 0
                    ? 0
                    : checked((streamLength - 1) / (ulong)unitSize + 1);
                if (result > int.MaxValue)
                {
                    Fail("A directory stream chain exceeds the validation limit.");
                }

                return (int)result;
            }

            private void ReadSector(int sectorId, byte[] destination)
            {
                long offset = checked(((long)sectorId + 1L) * sectorSize);
                ReadExact(offset, destination, 0, sectorSize);
            }

            private void ReadExact(long offset, byte[] destination, int index, int count)
            {
                stream.Position = offset;
                int remaining = count;
                while (remaining > 0)
                {
                    int read = stream.Read(destination, index, remaining);
                    if (read <= 0)
                    {
                        Fail("The compound file is truncated.");
                    }

                    index += read;
                    remaining -= read;
                }
            }

            private static int CeilingDivide(int value, int divisor)
            {
                return checked((value + divisor - 1) / divisor);
            }

            private static ushort ReadUInt16(byte[] bytes, int offset)
            {
                return (ushort)(bytes[offset] | bytes[offset + 1] << 8);
            }

            private static uint ReadUInt32(byte[] bytes, int offset)
            {
                return bytes[offset] |
                    (uint)bytes[offset + 1] << 8 |
                    (uint)bytes[offset + 2] << 16 |
                    (uint)bytes[offset + 3] << 24;
            }

            private static ulong ReadUInt64(byte[] bytes, int offset)
            {
                uint low = ReadUInt32(bytes, offset);
                uint high = ReadUInt32(bytes, offset + 4);
                return low | (ulong)high << 32;
            }

            private static void Fail(string message)
            {
                throw new InvalidDataException(message);
            }

            private sealed class DirectoryEntry
            {
                public DirectoryEntry(
                    int id,
                    string name,
                    int objectType,
                    uint leftSibling,
                    uint rightSibling,
                    uint child,
                    uint startSector,
                    ulong streamSize)
                {
                    Id = id;
                    Name = name;
                    ObjectType = objectType;
                    LeftSibling = leftSibling;
                    RightSibling = rightSibling;
                    Child = child;
                    StartSector = startSector;
                    StreamSize = streamSize;
                }

                public int Id { get; }

                public string Name { get; }

                public int ObjectType { get; }

                public uint LeftSibling { get; }

                public uint RightSibling { get; }

                public uint Child { get; }

                public uint StartSector { get; }

                public ulong StreamSize { get; }

                public bool IsActive => ObjectType != 0;

                public bool IsStorage => ObjectType == 1 || ObjectType == 5;
            }
        }
    }
}
