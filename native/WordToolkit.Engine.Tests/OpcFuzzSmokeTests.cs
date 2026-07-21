using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;

namespace WordToolkit.Engine.Tests;

public sealed class OpcFuzzSmokeTests
{
    [Fact]
    public void ArbitrarySmallInputsStayInsideDocumentedFailureBoundary()
    {
        var reader = new OpcPackageReader(
            new OpcPackageLimits
            {
                MaxEntries = 256,
                MaxEntryUncompressedBytes = 2 * 1024 * 1024,
                MaxTotalUncompressedBytes = 8 * 1024 * 1024,
                MaxCompressionRatio = 100,
                MaxMetadataXmlCharacters = 2 * 1024 * 1024,
            }
        );
        for (var seed = 0; seed < 200; seed++)
        {
            var random = new Random(seed);
            var bytes = new byte[random.Next(0, 4096)];
            random.NextBytes(bytes);
            using var input = new MemoryStream(bytes, writable: false);

            try
            {
                _ = reader.Read(input);
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException
            )
            {
                // Malformed ZIP data and explicit package limits are the public,
                // bounded failure boundary for arbitrary bytes.
            }
        }
    }

    [Fact]
    public void RandomOpaquePartsRemainByteIdenticalAcrossNoOpRoundTrip()
    {
        for (var seed = 0; seed < 25; seed++)
        {
            using var package = BuildPackageWithOpaqueParts(seed);
            var reader = new OpcPackageReader();
            var before = reader.Read(package);
            using var output = new MemoryStream();

            new OpcPackageSerializer().Write(
                output,
                new OpcPackageMutationBuilder(before),
                OpcSerializationMode.Deterministic
            );
            output.Position = 0;
            var after = reader.Read(output);

            Assert.Equal(before.Fingerprint, after.Fingerprint);
            Assert.Equal(
                before.Entries.ToDictionary(entry => entry.Name, entry => entry.Sha256),
                after.Entries.ToDictionary(entry => entry.Name, entry => entry.Sha256)
            );
        }
    }

    private static MemoryStream BuildPackageWithOpaqueParts(int seed)
    {
        var random = new Random(seed);
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                "[Content_Types].xml",
                Encoding.UTF8.GetBytes(
                    """
                    <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                      <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
                      <Default Extension="xml" ContentType="application/xml" />
                      <Default Extension="bin" ContentType="application/octet-stream" />
                      <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" />
                    </Types>
                    """
                )
            );
            WriteEntry(
                archive,
                "_rels/.rels",
                Encoding.UTF8.GetBytes(
                    """
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                      <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml" />
                    </Relationships>
                    """
                )
            );
            WriteEntry(
                archive,
                "word/document.xml",
                Encoding.UTF8.GetBytes(
                    """
                    <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:body><w:p /></w:body>
                    </w:document>
                    """
                )
            );
            for (var index = 0; index < 12; index++)
            {
                var content = new byte[random.Next(0, 8192)];
                random.NextBytes(content);
                WriteEntry(archive, $"custom/blob-{index:D2}.bin", content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content);
    }
}
