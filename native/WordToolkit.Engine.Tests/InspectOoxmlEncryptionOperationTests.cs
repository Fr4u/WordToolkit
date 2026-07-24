using System.Buffers.Binary;
using System.Text;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;

namespace WordToolkit.Engine.Tests;

public sealed class InspectOoxmlEncryptionOperationTests
{
    [Theory]
    [InlineData(2, 2, "standard")]
    [InlineData(3, 2, "standard")]
    [InlineData(4, 2, "standard")]
    [InlineData(3, 3, "extensible")]
    [InlineData(4, 3, "extensible")]
    [InlineData(4, 4, "agile")]
    public void DetectsCompleteEncryptedOoxmlCompoundFile(
        ushort encryptionMajor,
        ushort encryptionMinor,
        string expectedVariant
    )
    {
        using var stream = new MemoryStream(
            CompoundFile(encryptionMajor, encryptionMinor)
        );
        stream.Position = 31;

        var result = new InspectOoxmlEncryptionOperation().Execute(
            stream,
            "protected.docx"
        );

        Assert.Equal(31, stream.Position);
        Assert.Equal(InspectOoxmlEncryptionContract.Contract, result.OperationContract);
        Assert.Equal("encrypted_ooxml_compound_file", result.ContainerKind);
        Assert.Equal("encrypted", result.EncryptionState);
        Assert.True(result.IsEncryptedOoxml);
        Assert.True(result.CompleteEncryptionContainer);
        Assert.True(result.HasEncryptionInfoStream);
        Assert.True(result.HasEncryptedPackageStream);
        Assert.True(result.HasDataSpacesStorage);
        Assert.Equal(expectedVariant, result.EncryptionInfoVariant);
        Assert.Equal(encryptionMajor, result.EncryptionInfoMajor);
        Assert.Equal(encryptionMinor, result.EncryptionInfoMinor);
        Assert.Equal(3, result.CompoundFileMajorVersion);
        Assert.Equal(512, result.SectorSize);
        Assert.Equal(3, result.RootChildCount);
        Assert.Empty(result.IssueCodes);
        Assert.False(result.Security.AcceptsPassword);
        Assert.False(result.Security.DecryptsContent);
        Assert.False(result.Security.ReturnsDocumentContent);
        Assert.False(result.Security.ReturnsStreamNames);
        Assert.False(result.Security.ReturnsPaths);
        Assert.Equal(8, result.Security.EncryptionInfoBytesReadMaximum);

        var json = WordToolkitOperationJson.Serialize(result);
        Assert.DoesNotContain("EncryptionInfo", json, StringComparison.Ordinal);
        Assert.DoesNotContain("EncryptedPackage", json, StringComparison.Ordinal);
        Assert.DoesNotContain("DataSpaces", json, StringComparison.Ordinal);
    }

    [Fact]
    public void FutureEncryptionVersionRemainsEncryptedButIsExplicitlyUnknown()
    {
        using var stream = new MemoryStream(CompoundFile(5, 1));

        var result = new InspectOoxmlEncryptionOperation().Execute(
            stream,
            "future.docx"
        );

        Assert.True(result.IsEncryptedOoxml);
        Assert.True(result.CompleteEncryptionContainer);
        Assert.Equal("encrypted", result.EncryptionState);
        Assert.Equal("unknown", result.EncryptionInfoVariant);
        Assert.Equal(["encryption_info_version_unknown"], result.IssueCodes);
    }

    [Fact]
    public void RecognizedVersionWithInvalidHeaderDoesNotPassAsComplete()
    {
        var bytes = CompoundFile(4, 4);
        WriteUInt32(bytes, SectorOffset(3) + 4, 0);
        using var stream = new MemoryStream(bytes);

        var result = new InspectOoxmlEncryptionOperation().Execute(
            stream,
            "spoofed-agile.docx"
        );

        Assert.False(result.IsEncryptedOoxml);
        Assert.False(result.CompleteEncryptionContainer);
        Assert.Equal("malformed_encryption_container", result.EncryptionState);
        Assert.Equal(["encryption_info_header_invalid"], result.IssueCodes);
    }

    [Fact]
    public void ReportsZipCandidateWithoutTryingToOpenIt()
    {
        using var stream = new MemoryStream([0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4]);

        var result = new InspectOoxmlEncryptionOperation().Execute(stream, "plain.docx");

        Assert.Equal("opc_zip_candidate", result.ContainerKind);
        Assert.Equal("not_encrypted", result.EncryptionState);
        Assert.False(result.IsEncryptedOoxml);
        Assert.Empty(result.IssueCodes);
    }

    [Fact]
    public void DetectsFourKilobyteSectorCompoundFiles()
    {
        using var stream = new MemoryStream(CompoundFile(4, 4, cfbMajor: 4));

        var result = new InspectOoxmlEncryptionOperation().Execute(
            stream,
            "protected-v4.docx"
        );

        Assert.True(result.IsEncryptedOoxml);
        Assert.Equal(4, result.CompoundFileMajorVersion);
        Assert.Equal(4096, result.SectorSize);
        Assert.Equal(32, result.DirectoryEntryCount);
        Assert.Empty(result.IssueCodes);
    }

    [Fact]
    public void RejectsMarkerSpoofWhenRequiredRootMarkerIsMissing()
    {
        var bytes = CompoundFile(4, 4);
        WriteDirectoryEntry(
            bytes,
            SectorOffset(1) + 3 * 128,
            "OtherStorage",
            1
        );
        using var stream = new MemoryStream(bytes);

        var result = new InspectOoxmlEncryptionOperation().Execute(stream, "spoof.docx");

        Assert.Equal("compound_file", result.ContainerKind);
        Assert.Equal("malformed_encryption_container", result.EncryptionState);
        Assert.False(result.IsEncryptedOoxml);
        Assert.False(result.CompleteEncryptionContainer);
        Assert.Contains("data_spaces_missing", result.IssueCodes);

        stream.Position = 0;
        var packageError = Assert.Throws<WordToolkitOperationException>(() =>
            new InspectWordPackageOperation().Execute(stream, "spoof.docx")
        );
        Assert.Equal("ENCRYPTION_CONTAINER_INVALID", packageError.Code);
    }

    [Fact]
    public void OrdinaryCompoundFileDoesNotAcquireMissingEncryptionMarkerErrors()
    {
        var bytes = CompoundFile(4, 4);
        var directory = SectorOffset(1);
        WriteDirectoryEntry(bytes, directory + 128, "OrdinaryStreamA", 2, start: 0, size: 8);
        WriteDirectoryEntry(
            bytes,
            directory + 256,
            "OrdinaryStreamB",
            2,
            left: 1,
            right: 3,
            start: 4,
            size: 4096
        );
        WriteDirectoryEntry(bytes, directory + 384, "OrdinaryStorage", 1);
        using var stream = new MemoryStream(bytes);

        var result = new InspectOoxmlEncryptionOperation().Execute(
            stream,
            "ordinary.docx"
        );

        Assert.Equal("compound_file", result.ContainerKind);
        Assert.Equal("not_encrypted", result.EncryptionState);
        Assert.False(result.IsEncryptedOoxml);
        Assert.Empty(result.IssueCodes);
    }

    [Fact]
    public void MalformedCompoundFileFailsClosedWithoutThrowingParserDetails()
    {
        var bytes = CompoundFile(4, 4);
        WriteUInt32(bytes, SectorOffset(0), 0);
        using var stream = new MemoryStream(bytes);

        var result = new InspectOoxmlEncryptionOperation().Execute(stream, "hostile.docx");

        Assert.Equal("malformed_compound_file", result.ContainerKind);
        Assert.Equal("indeterminate", result.EncryptionState);
        Assert.False(result.IsEncryptedOoxml);
        Assert.Equal(["cfb_fat_self_marker_invalid"], result.IssueCodes);
    }

    [Fact]
    public void ImpossibleFatSectorCountIsRejectedBeforeAllocation()
    {
        var bytes = CompoundFile(4, 4);
        WriteUInt32(bytes, 44, 1_000);
        using var stream = new MemoryStream(bytes);

        var result = new InspectOoxmlEncryptionOperation().Execute(
            stream,
            "hostile-count.docx"
        );

        Assert.Equal("malformed_compound_file", result.ContainerKind);
        Assert.Equal(["cfb_fat_count_invalid"], result.IssueCodes);
    }

    [Fact]
    public void AcceptsADeclaredSurplusFatSectorWhenItsIdentityIsValid()
    {
        using var stream = new MemoryStream(
            CompoundFile(4, 4, surplusFatSectors: 1)
        );

        var result = new InspectOoxmlEncryptionOperation().Execute(
            stream,
            "word-preallocated.docx"
        );

        Assert.True(result.IsEncryptedOoxml);
        Assert.True(result.CompleteEncryptionContainer);
        Assert.Equal("agile", result.EncryptionInfoVariant);
        Assert.Empty(result.IssueCodes);
    }

    [Fact]
    public void PathContractReturnsLeafNameAndNeverReturnsThePath()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-encryption-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "secret.docx");
            File.WriteAllBytes(path, CompoundFile(4, 4));

            var result = new InspectOoxmlEncryptionOperation().Execute(
                new InspectOoxmlEncryptionRequest(path)
            );
            var json = WordToolkitOperationJson.Serialize(result);

            Assert.Equal("secret.docx", result.FileName);
            Assert.DoesNotContain(directory, json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EnforcesInputAndResourceBoundsWithStableCodes()
    {
        var missing = Assert.Throws<WordToolkitOperationException>(() =>
            new InspectOoxmlEncryptionOperation().Execute(
                new InspectOoxmlEncryptionRequest(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".docx"))
            )
        );
        Assert.Equal("NOT_FOUND", missing.Code);

        using var package = new MemoryStream(CompoundFile(4, 4));
        var extension = Assert.Throws<WordToolkitOperationException>(() =>
            new InspectOoxmlEncryptionOperation().Execute(package, "legacy.doc")
        );
        Assert.Equal("INVALID_INPUT", extension.Code);

        using var oversized = new MemoryStream(new byte[1024]);
        var limit = Assert.Throws<WordToolkitOperationException>(() =>
            new InspectOoxmlEncryptionOperation(
                new OoxmlEncryptionInspectionLimits { MaxFileBytes = 512 }
            ).Execute(oversized, "large.docx")
        );
        Assert.Equal("ENCRYPTION_INSPECTION_LIMIT", limit.Code);
    }

    [Theory]
    [InlineData("folder/input.docx")]
    [InlineData("folder\\input.docx")]
    [InlineData("C:secret.docx")]
    public void StreamFileNameMustBeABoundedLeafOnEveryPlatform(string fileName)
    {
        using var package = new MemoryStream(CompoundFile(4, 4));

        var exception = Assert.Throws<WordToolkitOperationException>(() =>
            new InspectOoxmlEncryptionOperation().Execute(package, fileName)
        );

        Assert.Equal("INVALID_INPUT", exception.Code);
    }

    internal static byte[] CompoundFile(
        ushort encryptionMajor,
        ushort encryptionMinor,
        ushort cfbMajor = 3,
        int surplusFatSectors = 0
    )
    {
        const uint freeSector = 0xFFFFFFFF;
        const uint endOfChain = 0xFFFFFFFE;
        const uint fatSector = 0xFFFFFFFD;
        if (surplusFatSectors is < 0 or > 108)
        {
            throw new ArgumentOutOfRangeException(nameof(surplusFatSectors));
        }
        var sectorSize = cfbMajor == 3 ? 512 : 4096;
        var packageSectorCount = 4096 / sectorSize;
        var firstSurplusFatSector = 4 + packageSectorCount;
        var sectorCount = firstSurplusFatSector + surplusFatSectors;
        var bytes = new byte[(sectorCount + 1) * sectorSize];
        int Offset(int sector) => checked((sector + 1) * sectorSize);

        new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }
            .CopyTo(bytes, 0);
        WriteUInt16(bytes, 24, 0x003E);
        WriteUInt16(bytes, 26, cfbMajor);
        WriteUInt16(bytes, 28, 0xFFFE);
        WriteUInt16(bytes, 30, cfbMajor == 3 ? (ushort)9 : (ushort)12);
        WriteUInt16(bytes, 32, 6);
        WriteUInt32(bytes, 40, cfbMajor == 3 ? 0U : 1U);
        WriteUInt32(bytes, 44, checked((uint)(1 + surplusFatSectors)));
        WriteUInt32(bytes, 48, 1);
        WriteUInt32(bytes, 56, 4096);
        WriteUInt32(bytes, 60, 2);
        WriteUInt32(bytes, 64, 1);
        WriteUInt32(bytes, 68, endOfChain);
        WriteUInt32(bytes, 72, 0);
        for (var index = 0; index < 109; index++)
        {
            WriteUInt32(bytes, 76 + index * 4, freeSector);
        }
        WriteUInt32(bytes, 76, 0);
        for (var index = 0; index < surplusFatSectors; index++)
        {
            WriteUInt32(
                bytes,
                80 + index * 4,
                checked((uint)(firstSurplusFatSector + index))
            );
        }

        var fatOffset = Offset(0);
        for (var index = 0; index < sectorSize / 4; index++)
        {
            WriteUInt32(bytes, fatOffset + index * 4, freeSector);
        }
        WriteUInt32(bytes, fatOffset, fatSector);
        WriteUInt32(bytes, fatOffset + 4, endOfChain);
        WriteUInt32(bytes, fatOffset + 8, endOfChain);
        WriteUInt32(bytes, fatOffset + 12, endOfChain);
        for (var sector = 4; sector < 4 + packageSectorCount - 1; sector++)
        {
            WriteUInt32(bytes, fatOffset + sector * 4, checked((uint)(sector + 1)));
        }
        WriteUInt32(
            bytes,
            fatOffset + (4 + packageSectorCount - 1) * 4,
            endOfChain
        );
        for (var index = 0; index < surplusFatSectors; index++)
        {
            var sector = firstSurplusFatSector + index;
            WriteUInt32(bytes, fatOffset + sector * 4, fatSector);
            var surplusOffset = Offset(sector);
            for (var item = 0; item < sectorSize / 4; item++)
            {
                WriteUInt32(bytes, surplusOffset + item * 4, freeSector);
            }
        }

        var directoryOffset = Offset(1);
        WriteDirectoryEntry(bytes, directoryOffset, "Root Entry", 5, child: 2, start: 3, size: 64);
        WriteDirectoryEntry(bytes, directoryOffset + 128, "EncryptionInfo", 2, start: 0, size: 8);
        WriteDirectoryEntry(
            bytes,
            directoryOffset + 256,
            "EncryptedPackage",
            2,
            left: 1,
            right: 3,
            start: 4,
            size: 4096
        );
        WriteDirectoryEntry(bytes, directoryOffset + 384, "\u0006DataSpaces", 1);

        var miniFatOffset = Offset(2);
        for (var index = 0; index < sectorSize / 4; index++)
        {
            WriteUInt32(bytes, miniFatOffset + index * 4, freeSector);
        }
        WriteUInt32(bytes, miniFatOffset, endOfChain);

        var miniStreamOffset = Offset(3);
        WriteUInt16(bytes, miniStreamOffset, encryptionMajor);
        WriteUInt16(bytes, miniStreamOffset + 2, encryptionMinor);
        WriteUInt32(
            bytes,
            miniStreamOffset + 4,
            (encryptionMajor, encryptionMinor) switch
            {
                (2 or 3 or 4, 2) => 0x24,
                (3 or 4, 3) => 0x10,
                (4, 4) => 0x40,
                _ => 0,
            }
        );
        WriteUInt64(bytes, Offset(4), 1234);
        return bytes;
    }

    private static void WriteDirectoryEntry(
        byte[] bytes,
        int offset,
        string name,
        byte type,
        uint left = 0xFFFFFFFF,
        uint right = 0xFFFFFFFF,
        uint child = 0xFFFFFFFF,
        uint start = 0xFFFFFFFE,
        ulong size = 0
    )
    {
        var nameBytes = Encoding.Unicode.GetBytes(name + "\0");
        nameBytes.CopyTo(bytes, offset);
        WriteUInt16(bytes, offset + 64, checked((ushort)nameBytes.Length));
        bytes[offset + 66] = type;
        bytes[offset + 67] = 1;
        WriteUInt32(bytes, offset + 68, left);
        WriteUInt32(bytes, offset + 72, right);
        WriteUInt32(bytes, offset + 76, child);
        WriteUInt32(bytes, offset + 116, start);
        WriteUInt64(bytes, offset + 120, size);
    }

    private static int SectorOffset(int sector) => checked((sector + 1) * 512);

    private static void WriteUInt16(byte[] bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), value);

    private static void WriteUInt32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);

    private static void WriteUInt64(byte[] bytes, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset, 8), value);
}
