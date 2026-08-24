using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using ZeroGameStudio.ConfigPipeline.Editor;

namespace ZeroGameStudio.ConfigPipeline.Tests
{
    [Category("ZGS.ConfigPipeline.CoreContract")]
    public sealed class VbaCompoundFileValidatorTests
    {
        private const uint FreeSector = 0xFFFFFFFFU;
        private const uint EndOfChain = 0xFFFFFFFEU;
        private const uint FatSector = 0xFFFFFFFDU;
        private const uint DifatSectorMarker = 0xFFFFFFFCU;
        private const uint NoStream = 0xFFFFFFFFU;

        internal static byte[] CreateValidVbaProjectFixture()
        {
            return Fixture.Create(3).Bytes;
        }

        [TestCase(3)]
        [TestCase(4)]
        public void Validate_AcceptsMinimalRealCompoundFileWithMiniStreams(int majorVersion)
        {
            Fixture fixture = Fixture.Create(majorVersion);

            Assert.DoesNotThrow(() => Validate(fixture));
        }

        [TestCase(3)]
        [TestCase(4)]
        public void Validate_AcceptsStructurallyValidDifat(int majorVersion)
        {
            Fixture fixture = Fixture.Create(majorVersion, 110);

            Assert.DoesNotThrow(() => Validate(fixture));
        }

        [TestCase(3)]
        [TestCase(4)]
        public void Validate_AcceptsOfficeZeroFilledFatCapacityPastEndOfFile(
            int majorVersion)
        {
            Fixture fixture = Fixture.Create(majorVersion);
            int physicalSectorCount = fixture.Bytes.Length / fixture.SectorSize - 1;
            int fatEntryCapacity = fixture.FatSectorCount * (fixture.SectorSize / 4);
            for (int entry = physicalSectorCount; entry < fatEntryCapacity; entry++)
            {
                fixture.WriteFat(entry, 0U);
            }

            Assert.DoesNotThrow(() => Validate(fixture));
        }

        [Test]
        public void Validate_RejectsNonZeroFatPointerPastEndOfFile()
        {
            Fixture fixture = Fixture.Create(3);
            int physicalSectorCount = fixture.Bytes.Length / fixture.SectorSize - 1;
            fixture.WriteFat(physicalSectorCount, 1U);

            AssertInvalid(fixture.Bytes);
        }

        [TestCase(3)]
        [TestCase(4)]
        public void Validate_AcceptsOfficeZeroFilledMiniFatCapacityPastMiniStream(
            int majorVersion)
        {
            Fixture fixture = Fixture.Create(majorVersion);
            int rootMiniSectorCount = 3;
            int miniFatEntryCapacity = fixture.SectorSize / 4;
            for (int entry = rootMiniSectorCount; entry < miniFatEntryCapacity; entry++)
            {
                fixture.WriteMiniFat(entry, 0U);
            }

            Assert.DoesNotThrow(() => Validate(fixture));
        }

        [Test]
        public void Validate_RejectsNonZeroMiniFatPointerPastMiniStream()
        {
            Fixture fixture = Fixture.Create(3);
            fixture.WriteMiniFat(3, 1U);

            AssertInvalid(fixture.Bytes);
        }

        [TestCase(3)]
        [TestCase(4)]
        public void Validate_AcceptsOfficeZeroFilledOrphanEndSector(int majorVersion)
        {
            Fixture fixture = Fixture.Create(majorVersion);
            int orphanSector = fixture.Bytes.Length / fixture.SectorSize - 1;
            Array.Resize(ref fixture.Bytes, fixture.Bytes.Length + fixture.SectorSize);
            fixture.WriteFat(orphanSector, EndOfChain);

            Assert.DoesNotThrow(() => Validate(fixture));
        }

        [Test]
        public void Validate_RejectsNonZeroOrphanEndSector()
        {
            Fixture fixture = Fixture.Create(3);
            int orphanSector = fixture.Bytes.Length / fixture.SectorSize - 1;
            Array.Resize(ref fixture.Bytes, fixture.Bytes.Length + fixture.SectorSize);
            fixture.WriteFat(orphanSector, EndOfChain);
            fixture.Bytes[fixture.Bytes.Length - 1] = 1;

            AssertInvalid(fixture.Bytes);
        }

        [Test]
        public void Validate_PreservesWorkbookNameOnEveryFailure()
        {
            Fixture fixture = Fixture.Create(3);
            fixture.Bytes[0] = 0;

            XlsxConfigException exception = AssertInvalid(
                fixture.Bytes,
                "designer-macros.xlsm");

            Assert.That(exception.Code, Is.EqualTo("XLSX_VBA_PACKAGE_INVALID"));
            Assert.That(exception.Workbook, Is.EqualTo("designer-macros.xlsm"));
            Assert.That(exception.Sheet, Is.Null);
            Assert.That(exception.Row, Is.Null);
            Assert.That(exception.Column, Is.Null);
        }

        [Test]
        public void Validate_RejectsNullAndUnreadableStreamsAsStructuredErrors()
        {
            XlsxConfigException missing = Assert.Throws<XlsxConfigException>(() =>
                VbaCompoundFileValidator.Validate(null, "missing.xlsm"));
            Assert.That(missing.Code, Is.EqualTo("XLSX_VBA_PACKAGE_INVALID"));

            using (var stream = new UnreadableStream())
            {
                XlsxConfigException unreadable = Assert.Throws<XlsxConfigException>(() =>
                    VbaCompoundFileValidator.Validate(stream, "stream.xlsm"));
                Assert.That(unreadable.Code, Is.EqualTo("XLSX_VBA_PACKAGE_INVALID"));
            }
        }

        [TestCase(3)]
        [TestCase(4)]
        public void Validate_AcceptsReadableNonSeekableCompoundFile(int majorVersion)
        {
            using (var stream = new NonSeekableStream(
                       Fixture.Create(majorVersion).Bytes))
            {
                Assert.DoesNotThrow(() =>
                    VbaCompoundFileValidator.Validate(stream, "stream.xlsm"));
            }
        }

        [Test]
        public void Validate_RejectsDamagedReadableNonSeekableCompoundFile()
        {
            Fixture fixture = Fixture.Create(3);
            fixture.Bytes[0] = 0;
            using (var stream = new NonSeekableStream(fixture.Bytes))
            {
                XlsxConfigException exception = Assert.Throws<XlsxConfigException>(() =>
                    VbaCompoundFileValidator.Validate(stream, "damaged-stream.xlsm"));

                Assert.That(exception.Code, Is.EqualTo("XLSX_VBA_PACKAGE_INVALID"));
                Assert.That(exception.Workbook, Is.EqualTo("damaged-stream.xlsm"));
            }
        }

        [Test]
        public void Validate_RestoresStreamPositionAfterSuccessAndFailure()
        {
            Fixture valid = Fixture.Create(3);
            using (var stream = new MemoryStream(valid.Bytes, false))
            {
                stream.Position = 17;
                VbaCompoundFileValidator.Validate(stream, "valid.xlsm");
                Assert.That(stream.Position, Is.EqualTo(17));
            }

            Fixture invalid = Fixture.Create(3);
            invalid.Bytes[0] = 0;
            using (var stream = new MemoryStream(invalid.Bytes, false))
            {
                stream.Position = 23;
                Assert.Throws<XlsxConfigException>(() =>
                    VbaCompoundFileValidator.Validate(stream, "invalid.xlsm"));
                Assert.That(stream.Position, Is.EqualTo(23));
            }
        }

        [Test]
        public void Validate_RejectsLegacySignatureOnlyFakeHeader()
        {
            byte[] fakeHeader = new byte[512];
            Buffer.BlockCopy(
                new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 },
                0,
                fakeHeader,
                0,
                8);

            AssertInvalid(fakeHeader);
        }

        [TestCase(HeaderFault.Signature)]
        [TestCase(HeaderFault.Clsid)]
        [TestCase(HeaderFault.MinorVersion)]
        [TestCase(HeaderFault.MajorVersion)]
        [TestCase(HeaderFault.ByteOrder)]
        [TestCase(HeaderFault.SectorShift)]
        [TestCase(HeaderFault.MiniSectorShift)]
        [TestCase(HeaderFault.Reserved)]
        [TestCase(HeaderFault.MiniStreamCutoff)]
        [TestCase(HeaderFault.Version4Padding)]
        public void Validate_RejectsInvalidHeaderFields(HeaderFault fault)
        {
            Fixture fixture = Fixture.Create(4);
            switch (fault)
            {
                case HeaderFault.Signature:
                    fixture.Bytes[0] ^= 0x01;
                    break;
                case HeaderFault.Clsid:
                    fixture.Bytes[8] = 1;
                    break;
                case HeaderFault.MinorVersion:
                    WriteUInt16(fixture.Bytes, 24, 0);
                    break;
                case HeaderFault.MajorVersion:
                    WriteUInt16(fixture.Bytes, 26, 5);
                    break;
                case HeaderFault.ByteOrder:
                    WriteUInt16(fixture.Bytes, 28, 0xFEFF);
                    break;
                case HeaderFault.SectorShift:
                    WriteUInt16(fixture.Bytes, 30, 9);
                    break;
                case HeaderFault.MiniSectorShift:
                    WriteUInt16(fixture.Bytes, 32, 7);
                    break;
                case HeaderFault.Reserved:
                    fixture.Bytes[34] = 1;
                    break;
                case HeaderFault.MiniStreamCutoff:
                    WriteUInt32(fixture.Bytes, 56, 2048);
                    break;
                case HeaderFault.Version4Padding:
                    fixture.Bytes[513] = 1;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }

            AssertInvalid(fixture.Bytes);
        }

        [TestCase(1)]
        [TestCase(257)]
        public void Validate_RejectsTruncatedOrUnalignedFile(int removedBytes)
        {
            Fixture fixture = Fixture.Create(3);
            Array.Resize(ref fixture.Bytes, fixture.Bytes.Length - removedBytes);

            AssertInvalid(fixture.Bytes);
        }

        [TestCase(FatFault.DeclaredCount)]
        [TestCase(FatFault.Marker)]
        [TestCase(FatFault.OutOfRangeNext)]
        [TestCase(FatFault.DirectoryCycle)]
        [TestCase(FatFault.SharedRegularSector)]
        public void Validate_RejectsInvalidFatAndRegularChains(FatFault fault)
        {
            Fixture fixture = Fixture.Create(3);
            switch (fault)
            {
                case FatFault.DeclaredCount:
                    WriteUInt32(fixture.Bytes, 44, 2);
                    break;
                case FatFault.Marker:
                    fixture.WriteFat(0, EndOfChain);
                    break;
                case FatFault.OutOfRangeNext:
                    fixture.WriteFat(fixture.FirstDirectorySector, 9999);
                    break;
                case FatFault.DirectoryCycle:
                    fixture.WriteFat(fixture.LastDirectorySector,
                        (uint)fixture.FirstDirectorySector);
                    break;
                case FatFault.SharedRegularSector:
                    fixture.SetDirectoryStream(
                        3,
                        (uint)fixture.RootMiniStreamSector,
                        4096);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }

            AssertInvalid(fixture.Bytes);
        }

        [TestCase(DifatFault.Count)]
        [TestCase(DifatFault.OutOfRange)]
        [TestCase(DifatFault.DuplicateFatReference)]
        [TestCase(DifatFault.Cycle)]
        [TestCase(DifatFault.Marker)]
        public void Validate_RejectsInvalidDifat(DifatFault fault)
        {
            Fixture fixture = Fixture.Create(3, 110);
            switch (fault)
            {
                case DifatFault.Count:
                    WriteUInt32(fixture.Bytes, 72, 0);
                    break;
                case DifatFault.OutOfRange:
                    WriteUInt32(fixture.Bytes, 68, 9999);
                    break;
                case DifatFault.DuplicateFatReference:
                    fixture.WriteSectorUInt32(fixture.DifatSector, 0, 0);
                    break;
                case DifatFault.Cycle:
                    fixture.WriteSectorUInt32(
                        fixture.DifatSector,
                        fixture.SectorSize - 4,
                        (uint)fixture.DifatSector);
                    break;
                case DifatFault.Marker:
                    fixture.WriteFat(fixture.DifatSector, EndOfChain);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }

            AssertInvalid(fixture.Bytes);
        }

        [TestCase(DirectoryFault.RootType)]
        [TestCase(DirectoryFault.ChildOutOfRange)]
        [TestCase(DirectoryFault.SiblingCycle)]
        [TestCase(DirectoryFault.DuplicateOwnership)]
        [TestCase(DirectoryFault.Orphan)]
        [TestCase(DirectoryFault.MissingProject)]
        [TestCase(DirectoryFault.WrongProjectType)]
        [TestCase(DirectoryFault.DirUnderWrongParent)]
        public void Validate_RejectsInvalidDirectoryTree(DirectoryFault fault)
        {
            Fixture fixture = Fixture.Create(3);
            switch (fault)
            {
                case DirectoryFault.RootType:
                    fixture.SetDirectoryType(0, 1);
                    break;
                case DirectoryFault.ChildOutOfRange:
                    fixture.SetDirectoryReference(0, 76, 9999);
                    break;
                case DirectoryFault.SiblingCycle:
                    fixture.SetDirectoryReference(2, 72, 1);
                    break;
                case DirectoryFault.DuplicateOwnership:
                    fixture.SetDirectoryReference(2, 76, 1);
                    break;
                case DirectoryFault.Orphan:
                    fixture.SetDirectoryReference(0, 76, NoStream);
                    break;
                case DirectoryFault.MissingProject:
                    fixture.SetDirectoryName(1, "NOT_PROJECT");
                    break;
                case DirectoryFault.WrongProjectType:
                    fixture.SetDirectoryType(1, 1);
                    fixture.SetDirectoryStream(1, EndOfChain, 0);
                    break;
                case DirectoryFault.DirUnderWrongParent:
                    fixture.SetDirectoryReference(2, 72, 3);
                    fixture.SetDirectoryReference(2, 76, 4);
                    fixture.SetDirectoryReference(3, 72, NoStream);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }

            AssertInvalid(fixture.Bytes);
        }

        [TestCase(MiniFatFault.Marker)]
        [TestCase(MiniFatFault.Cycle)]
        [TestCase(MiniFatFault.OutOfRange)]
        [TestCase(MiniFatFault.LengthMismatch)]
        [TestCase(MiniFatFault.SharedMiniSector)]
        public void Validate_RejectsInvalidMiniFatAndMiniStreamChains(MiniFatFault fault)
        {
            Fixture fixture = Fixture.Create(3);
            switch (fault)
            {
                case MiniFatFault.Marker:
                    fixture.WriteFat(fixture.MiniFatSector, FreeSector);
                    break;
                case MiniFatFault.Cycle:
                    fixture.WriteMiniFat(1, 1);
                    break;
                case MiniFatFault.OutOfRange:
                    fixture.SetDirectoryStream(3, 127, 5);
                    break;
                case MiniFatFault.LengthMismatch:
                    fixture.SetDirectoryStream(3, 1, 65);
                    break;
                case MiniFatFault.SharedMiniSector:
                    fixture.SetDirectoryStream(3, 0, 5);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }

            AssertInvalid(fixture.Bytes);
        }

        [TestCase(CoreStreamFault.EmptyProject)]
        [TestCase(CoreStreamFault.EmptyDirectory)]
        [TestCase(CoreStreamFault.ShortVbaProject)]
        [TestCase(CoreStreamFault.MissingVbaStorage)]
        [TestCase(CoreStreamFault.MissingDirectory)]
        [TestCase(CoreStreamFault.MissingVbaProject)]
        public void Validate_RejectsMissingOrEmptyCoreEntries(CoreStreamFault fault)
        {
            Fixture fixture = Fixture.Create(3);
            switch (fault)
            {
                case CoreStreamFault.EmptyProject:
                    fixture.SetDirectoryStream(1, EndOfChain, 0);
                    break;
                case CoreStreamFault.EmptyDirectory:
                    fixture.SetDirectoryStream(3, EndOfChain, 0);
                    break;
                case CoreStreamFault.ShortVbaProject:
                    fixture.SetDirectoryStream(4, 2, 6);
                    break;
                case CoreStreamFault.MissingVbaStorage:
                    fixture.SetDirectoryName(2, "NOT_VBA");
                    break;
                case CoreStreamFault.MissingDirectory:
                    fixture.SetDirectoryName(3, "NOT_DIR");
                    break;
                case CoreStreamFault.MissingVbaProject:
                    fixture.SetDirectoryName(4, "NOT_VBA_PROJECT");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }

            AssertInvalid(fixture.Bytes);
        }

        [TestCase(CompressedDirectoryFault.ContainerSignature)]
        [TestCase(CompressedDirectoryFault.ChunkSignature)]
        [TestCase(CompressedDirectoryFault.ChunkLength)]
        [TestCase(CompressedDirectoryFault.CopyTokenBeforeOutput)]
        [TestCase(CompressedDirectoryFault.TruncatedCopyToken)]
        [TestCase(CompressedDirectoryFault.InvalidRawChunk)]
        [TestCase(CompressedDirectoryFault.TrailingPartialHeader)]
        public void Validate_RejectsInvalidCompressedDirectory(
            CompressedDirectoryFault fault)
        {
            Fixture fixture = Fixture.Create(3);
            switch (fault)
            {
                case CompressedDirectoryFault.ContainerSignature:
                    fixture.SetDirPayload(new byte[] { 0, 1, 0xB0, 0, 0 });
                    break;
                case CompressedDirectoryFault.ChunkSignature:
                    fixture.SetDirPayload(new byte[] { 1, 1, 0xA0, 0, 0 });
                    break;
                case CompressedDirectoryFault.ChunkLength:
                    fixture.SetDirPayload(new byte[] { 1, 0xFF, 0xBF, 0, 0 });
                    break;
                case CompressedDirectoryFault.CopyTokenBeforeOutput:
                    fixture.SetDirPayload(new byte[] { 1, 2, 0xB0, 1, 0, 0 });
                    break;
                case CompressedDirectoryFault.TruncatedCopyToken:
                    fixture.SetDirPayload(new byte[] { 1, 1, 0xB0, 1, 0 });
                    break;
                case CompressedDirectoryFault.InvalidRawChunk:
                    fixture.SetDirPayload(new byte[] { 1, 1, 0x30, 0, 0 });
                    break;
                case CompressedDirectoryFault.TrailingPartialHeader:
                    fixture.SetDirPayload(new byte[] { 1, 1, 0xB0, 0, 0, 0 });
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }

            AssertInvalid(fixture.Bytes);
        }

        private static void Validate(Fixture fixture)
        {
            using (var stream = new MemoryStream(fixture.Bytes, false))
            {
                VbaCompoundFileValidator.Validate(stream, "fixture.xlsm");
            }
        }

        private static XlsxConfigException AssertInvalid(
            byte[] bytes,
            string workbookName = "fixture.xlsm")
        {
            using (var stream = new MemoryStream(bytes, false))
            {
                XlsxConfigException exception = Assert.Throws<XlsxConfigException>(() =>
                    VbaCompoundFileValidator.Validate(stream, workbookName));
                Assert.That(exception.Code, Is.EqualTo("XLSX_VBA_PACKAGE_INVALID"));
                return exception;
            }
        }

        private static void WriteUInt16(byte[] bytes, int offset, ushort value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteUInt64(byte[] bytes, int offset, ulong value)
        {
            WriteUInt32(bytes, offset, (uint)value);
            WriteUInt32(bytes, offset + 4, (uint)(value >> 32));
        }

        private sealed class Fixture
        {
            private const int RootId = 0;
            private const int ProjectId = 1;
            private const int VbaStorageId = 2;
            private const int DirId = 3;
            private const int VbaProjectId = 4;

            private Fixture()
            {
            }

            public byte[] Bytes;

            public int SectorSize;

            public int FatSectorCount;

            public int DifatSector;

            public int FirstDirectorySector;

            public int LastDirectorySector;

            public int MiniFatSector;

            public int RootMiniStreamSector;

            public static Fixture Create(int majorVersion, int fatSectorCount = 1)
            {
                if (majorVersion != 3 && majorVersion != 4)
                {
                    throw new ArgumentOutOfRangeException(nameof(majorVersion));
                }

                if (fatSectorCount != 1 && fatSectorCount != 110)
                {
                    throw new ArgumentOutOfRangeException(nameof(fatSectorCount));
                }

                int sectorSize = majorVersion == 3 ? 512 : 4096;
                int directorySectors = majorVersion == 3 ? 2 : 1;
                int difatSectors = fatSectorCount > 109 ? 1 : 0;
                int difatSector = difatSectors == 0 ? -1 : fatSectorCount;
                int firstDirectorySector = fatSectorCount + difatSectors;
                int miniFatSector = firstDirectorySector + directorySectors;
                int rootMiniStreamSector = miniFatSector + 1;
                int sectorCount = rootMiniStreamSector + 1;
                var fixture = new Fixture
                {
                    Bytes = new byte[checked((sectorCount + 1) * sectorSize)],
                    SectorSize = sectorSize,
                    FatSectorCount = fatSectorCount,
                    DifatSector = difatSector,
                    FirstDirectorySector = firstDirectorySector,
                    LastDirectorySector = firstDirectorySector + directorySectors - 1,
                    MiniFatSector = miniFatSector,
                    RootMiniStreamSector = rootMiniStreamSector
                };

                fixture.WriteHeader(
                    majorVersion,
                    directorySectors,
                    fatSectorCount,
                    difatSectors,
                    firstDirectorySector,
                    miniFatSector,
                    difatSector);
                fixture.WriteFatSectors(sectorCount, directorySectors);
                fixture.WriteDifatSector();
                fixture.WriteDirectory();
                fixture.WriteMiniFatSector();
                fixture.WriteRootMiniStream();
                return fixture;
            }

            public void WriteFat(int sectorId, uint value)
            {
                int entriesPerSector = SectorSize / 4;
                int fatSectorOrdinal = sectorId / entriesPerSector;
                int entryOrdinal = sectorId % entriesPerSector;
                if (fatSectorOrdinal >= FatSectorCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(sectorId));
                }

                WriteSectorUInt32(fatSectorOrdinal, entryOrdinal * 4, value);
            }

            public void WriteMiniFat(int miniSectorId, uint value)
            {
                WriteSectorUInt32(MiniFatSector, miniSectorId * 4, value);
            }

            public void WriteSectorUInt32(int sectorId, int sectorOffset, uint value)
            {
                WriteUInt32(Bytes, SectorOffset(sectorId) + sectorOffset, value);
            }

            public void SetDirectoryType(int directoryId, byte objectType)
            {
                Bytes[DirectoryOffset(directoryId) + 66] = objectType;
            }

            public void SetDirectoryReference(int directoryId, int fieldOffset, uint value)
            {
                WriteUInt32(Bytes, DirectoryOffset(directoryId) + fieldOffset, value);
            }

            public void SetDirectoryName(int directoryId, string name)
            {
                int offset = DirectoryOffset(directoryId);
                Array.Clear(Bytes, offset, 64);
                byte[] encoded = Encoding.Unicode.GetBytes(name + "\0");
                if (encoded.Length > 64)
                {
                    throw new ArgumentOutOfRangeException(nameof(name));
                }

                Buffer.BlockCopy(encoded, 0, Bytes, offset, encoded.Length);
                WriteUInt16(Bytes, offset + 64, (ushort)encoded.Length);
            }

            public void SetDirectoryStream(int directoryId, uint startSector, ulong size)
            {
                int offset = DirectoryOffset(directoryId);
                WriteUInt32(Bytes, offset + 116, startSector);
                WriteUInt64(Bytes, offset + 120, size);
            }

            public void SetDirPayload(byte[] payload)
            {
                if (payload.Length > 64)
                {
                    throw new ArgumentOutOfRangeException(nameof(payload));
                }

                int offset = SectorOffset(RootMiniStreamSector) + 64;
                Array.Clear(Bytes, offset, 64);
                Buffer.BlockCopy(payload, 0, Bytes, offset, payload.Length);
                SetDirectoryStream(DirId, 1, (ulong)payload.Length);
            }

            private void WriteHeader(
                int majorVersion,
                int directorySectors,
                int fatSectorCount,
                int difatSectors,
                int firstDirectorySector,
                int miniFatSector,
                int difatSector)
            {
                byte[] signature =
                {
                    0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1
                };
                Buffer.BlockCopy(signature, 0, Bytes, 0, signature.Length);
                WriteUInt16(Bytes, 24, 0x003E);
                WriteUInt16(Bytes, 26, (ushort)majorVersion);
                WriteUInt16(Bytes, 28, 0xFFFE);
                WriteUInt16(Bytes, 30, (ushort)(majorVersion == 3 ? 9 : 12));
                WriteUInt16(Bytes, 32, 6);
                WriteUInt32(Bytes, 40, (uint)(majorVersion == 3 ? 0 : directorySectors));
                WriteUInt32(Bytes, 44, (uint)fatSectorCount);
                WriteUInt32(Bytes, 48, (uint)firstDirectorySector);
                WriteUInt32(Bytes, 56, 4096);
                WriteUInt32(Bytes, 60, (uint)miniFatSector);
                WriteUInt32(Bytes, 64, 1);
                WriteUInt32(Bytes, 68,
                    difatSectors == 0 ? EndOfChain : (uint)difatSector);
                WriteUInt32(Bytes, 72, (uint)difatSectors);
                for (int index = 0; index < 109; index++)
                {
                    uint value = index < Math.Min(fatSectorCount, 109)
                        ? (uint)index
                        : FreeSector;
                    WriteUInt32(Bytes, 76 + index * 4, value);
                }
            }

            private void WriteFatSectors(int sectorCount, int directorySectors)
            {
                int entriesPerSector = SectorSize / 4;
                for (int fatOrdinal = 0; fatOrdinal < FatSectorCount; fatOrdinal++)
                {
                    int baseOffset = SectorOffset(fatOrdinal);
                    for (int entry = 0; entry < entriesPerSector; entry++)
                    {
                        WriteUInt32(Bytes, baseOffset + entry * 4, FreeSector);
                    }
                }

                for (int sectorId = 0; sectorId < FatSectorCount; sectorId++)
                {
                    WriteFat(sectorId, FatSector);
                }

                if (DifatSector >= 0)
                {
                    WriteFat(DifatSector, DifatSectorMarker);
                }

                for (int index = 0; index < directorySectors; index++)
                {
                    uint next = index == directorySectors - 1
                        ? EndOfChain
                        : (uint)(FirstDirectorySector + index + 1);
                    WriteFat(FirstDirectorySector + index, next);
                }

                WriteFat(MiniFatSector, EndOfChain);
                WriteFat(RootMiniStreamSector, EndOfChain);
            }

            private void WriteDifatSector()
            {
                if (DifatSector < 0)
                {
                    return;
                }

                int offset = SectorOffset(DifatSector);
                int entriesPerSector = SectorSize / 4;
                for (int index = 0; index < entriesPerSector - 1; index++)
                {
                    uint value = 109 + index < FatSectorCount
                        ? (uint)(109 + index)
                        : FreeSector;
                    WriteUInt32(Bytes, offset + index * 4, value);
                }

                WriteUInt32(Bytes, offset + SectorSize - 4, EndOfChain);
            }

            private void WriteDirectory()
            {
                WriteDirectoryEntry(
                    RootId,
                    "Designer Root",
                    5,
                    NoStream,
                    NoStream,
                    ProjectId,
                    (uint)RootMiniStreamSector,
                    192);
                WriteDirectoryEntry(
                    ProjectId,
                    "PROJECT",
                    2,
                    NoStream,
                    VbaStorageId,
                    NoStream,
                    0,
                    1);
                WriteDirectoryEntry(
                    VbaStorageId,
                    "VBA",
                    1,
                    NoStream,
                    NoStream,
                    DirId,
                    0,
                    0);
                WriteDirectoryEntry(
                    DirId,
                    "dir",
                    2,
                    NoStream,
                    VbaProjectId,
                    NoStream,
                    1,
                    5);
                WriteDirectoryEntry(
                    VbaProjectId,
                    "_VBA_PROJECT",
                    2,
                    NoStream,
                    NoStream,
                    NoStream,
                    2,
                    7);
            }

            private void WriteDirectoryEntry(
                int directoryId,
                string name,
                byte objectType,
                uint leftSibling,
                uint rightSibling,
                uint child,
                uint startSector,
                ulong streamSize)
            {
                int offset = DirectoryOffset(directoryId);
                byte[] nameBytes = Encoding.Unicode.GetBytes(name + "\0");
                Buffer.BlockCopy(nameBytes, 0, Bytes, offset, nameBytes.Length);
                WriteUInt16(Bytes, offset + 64, (ushort)nameBytes.Length);
                Bytes[offset + 66] = objectType;
                Bytes[offset + 67] = 1;
                WriteUInt32(Bytes, offset + 68, leftSibling);
                WriteUInt32(Bytes, offset + 72, rightSibling);
                WriteUInt32(Bytes, offset + 76, child);
                WriteUInt32(Bytes, offset + 116, startSector);
                WriteUInt64(Bytes, offset + 120, streamSize);
            }

            private void WriteMiniFatSector()
            {
                int offset = SectorOffset(MiniFatSector);
                int entryCount = SectorSize / 4;
                for (int index = 0; index < entryCount; index++)
                {
                    WriteUInt32(Bytes, offset + index * 4,
                        index < 3 ? EndOfChain : FreeSector);
                }
            }

            private void WriteRootMiniStream()
            {
                int offset = SectorOffset(RootMiniStreamSector);
                Bytes[offset] = (byte)'I';
                byte[] compressedDirectory = { 0x01, 0x01, 0xB0, 0x00, 0x00 };
                Buffer.BlockCopy(
                    compressedDirectory,
                    0,
                    Bytes,
                    offset + 64,
                    compressedDirectory.Length);
                byte[] vbaProject = { 0xCC, 0x61, 0x00, 0x00, 0x00, 0x00, 0x00 };
                Buffer.BlockCopy(
                    vbaProject,
                    0,
                    Bytes,
                    offset + 128,
                    vbaProject.Length);
            }

            private int DirectoryOffset(int directoryId)
            {
                int entriesPerSector = SectorSize / 128;
                int sectorOrdinal = directoryId / entriesPerSector;
                int entryOrdinal = directoryId % entriesPerSector;
                return SectorOffset(FirstDirectorySector + sectorOrdinal) +
                    entryOrdinal * 128;
            }

            private int SectorOffset(int sectorId)
            {
                return checked((sectorId + 1) * SectorSize);
            }
        }

        private sealed class NonSeekableStream : Stream
        {
            private readonly MemoryStream inner;

            public NonSeekableStream(byte[] bytes)
            {
                inner = new MemoryStream(bytes, false);
            }

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return inner.Read(buffer, offset, count);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    inner.Dispose();
                }

                base.Dispose(disposing);
            }
        }

        private sealed class UnreadableStream : Stream
        {
            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }
        }

        public enum HeaderFault
        {
            Signature,
            Clsid,
            MinorVersion,
            MajorVersion,
            ByteOrder,
            SectorShift,
            MiniSectorShift,
            Reserved,
            MiniStreamCutoff,
            Version4Padding
        }

        public enum FatFault
        {
            DeclaredCount,
            Marker,
            OutOfRangeNext,
            DirectoryCycle,
            SharedRegularSector
        }

        public enum DifatFault
        {
            Count,
            OutOfRange,
            DuplicateFatReference,
            Cycle,
            Marker
        }

        public enum DirectoryFault
        {
            RootType,
            ChildOutOfRange,
            SiblingCycle,
            DuplicateOwnership,
            Orphan,
            MissingProject,
            WrongProjectType,
            DirUnderWrongParent
        }

        public enum MiniFatFault
        {
            Marker,
            Cycle,
            OutOfRange,
            LengthMismatch,
            SharedMiniSector
        }

        public enum CoreStreamFault
        {
            EmptyProject,
            EmptyDirectory,
            ShortVbaProject,
            MissingVbaStorage,
            MissingDirectory,
            MissingVbaProject
        }

        public enum CompressedDirectoryFault
        {
            ContainerSignature,
            ChunkSignature,
            ChunkLength,
            CopyTokenBeforeOutput,
            TruncatedCopyToken,
            InvalidRawChunk,
            TrailingPartialHeader
        }
    }
}
