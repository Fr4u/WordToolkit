using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Operations;

namespace WordToolkit.Engine.Tests;

/// <summary>
/// Small deterministic corpus for the public OPC inspection boundary. This is
/// intentionally bounded: it is a regression gate, not an unbounded fuzzer.
/// </summary>
public sealed class OpcStructuredMutationTests
{
    [Fact]
    public void StructuredMutationsStayWithinThePublicFailureBoundary()
    {
        var baseline = MinimalDocx();
        // Keep one valid control package in the bounded corpus. This prevents a
        // regression that maps every input, including a known-good package, to IO_ERROR.
        var mutations = new List<byte[]>
        {
            baseline,
            baseline[..^1],
            baseline[..(baseline.Length / 2)],
        };

        for (var seed = 0; seed < 16; seed++)
        {
            var mutated = baseline.ToArray();
            var offset = 32 + ((seed * 7919) % (mutated.Length - 32));
            mutated[offset] ^= (byte)(1 << (seed % 8));
            mutations.Add(mutated);
        }

        mutations.Add(PackageWithMetadata("<Types xmlns=\"urn:broken\"><Default"));
        mutations.Add(PackageWithMetadata("<Relationships><Relationship Id=\"rId1\""));
        mutations.Add(PackageWithDuplicateNames());
        mutations.Add(PackageWithCaseCollision());

        foreach (var bytes in mutations)
        {
            using var stream = new MemoryStream(bytes, writable: false);
            stream.Position = Math.Min(7, stream.Length);
            var originalPosition = stream.Position;

            try
            {
                var result = new InspectWordPackageOperation().Execute(
                    stream,
                    "mutation.docx",
                    includeDetails: true,
                    maxItems: 40
                );

                Assert.DoesNotContain(
                    result.Diagnostics.Items,
                    item => item.Code == "INTERNAL_ERROR"
                );
                Assert.False(result.ValidWordPackage && !result.StructurallyValid);
                if (result.ValidWordPackage)
                {
                    Assert.Equal(0, result.Diagnostics.Errors);
                }
                Assert.InRange(result.EntryCount, 0, 8);
                Assert.InRange(result.PartCount, 0, 8);
                Assert.InRange(result.RelationshipCount, 0, 8);
                Assert.InRange(result.Diagnostics.Errors, 0, 40);
                Assert.NotNull(result.Details);
                Assert.InRange(result.Details!.Parts.Count, 0, 40);
                Assert.InRange(result.Details.Relationships.Count, 0, 40);
                Assert.True(result.Diagnostics.Items.Count <= 40);
            }
            catch (WordToolkitOperationException exception)
            {
                Assert.NotEqual("INTERNAL_ERROR", exception.Code);
                Assert.Contains(
                    exception.Code,
                    new[] { "INVALID_PACKAGE", "PACKAGE_LIMIT", "INVALID_INPUT" }
                );
            }

            Assert.Equal(originalPosition, stream.Position);
        }
    }

    private static byte[] MinimalDocx() => Package(
        ("[Content_Types].xml", """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
              <Default Extension="xml" ContentType="application/xml" />
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" />
            </Types>
            """),
        ("_rels/.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml" />
            </Relationships>
            """),
        ("word/document.xml", """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p /></w:body></w:document>
            """)
    );

    private static byte[] PackageWithMetadata(string brokenXml) => Package(
        ("[Content_Types].xml", brokenXml),
        ("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\" />"),
        ("word/document.xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body /></w:document>")
    );

    private static byte[] PackageWithDuplicateNames() => Package(
        ("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\" />"),
        ("word/document.xml", "one"),
        ("word/document.xml", "two")
    );

    private static byte[] PackageWithCaseCollision() => Package(
        ("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\" />"),
        ("word/custom.xml", "one"),
        ("WORD/CUSTOM.XML", "two")
    );

    private static byte[] Package(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }
}
