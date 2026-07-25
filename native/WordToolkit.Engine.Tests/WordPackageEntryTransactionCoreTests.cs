using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordPackageEntryTransactionCoreTests
{
    [Fact]
    public void AddReplaceDeleteAndInverseRoundTripThePackageFingerprint()
    {
        using var bytes = BuildPackage();
        var reader = new OpcPackageReader();
        var serializer = new OpcPackageSerializer();
        var baseline = reader.Read(bytes);
        var replacement = Encoding.UTF8.GetBytes("replacement");
        var addition = Encoding.UTF8.GetBytes("addition");
        var changes = new Dictionary<string, WordPackageEntryPayload>(StringComparer.Ordinal)
        {
            ["word/document.xml"] = new(
                "word/document.xml",
                "/word/document.xml",
                baseline.Entries.Single(item => item.Name == "word/document.xml")
                    .Content.ToArray(),
                replacement
            ),
            ["word/deleted.bin"] = new(
                "word/deleted.bin",
                "/word/deleted.bin",
                baseline.Entries.Single(item => item.Name == "word/deleted.bin")
                    .Content.ToArray(),
                null
            ),
            ["word/added.bin"] = new(
                "word/added.bin",
                "/word/added.bin",
                null,
                addition
            ),
        };
        var provisional = new WordPackageEntryTransactionCore(
            baseline.Fingerprint,
            new string('0', 64),
            changes
        );
        using var candidateBytes = new MemoryStream();
        serializer.Write(candidateBytes, provisional.CreateMutation(baseline));
        candidateBytes.Position = 0;
        var candidate = reader.Read(candidateBytes);
        var transaction = new WordPackageEntryTransactionCore(
            baseline.Fingerprint,
            candidate.Fingerprint,
            changes
        );

        using var appliedBytes = new MemoryStream();
        serializer.Write(appliedBytes, transaction.CreateMutation(baseline));
        appliedBytes.Position = 0;
        var applied = reader.Read(appliedBytes);
        Assert.Equal(candidate.Fingerprint, applied.Fingerprint);
        Assert.DoesNotContain(applied.Entries, item => item.Name == "word/deleted.bin");
        Assert.Contains(applied.Entries, item => item.Name == "word/added.bin");

        using var restoredBytes = new MemoryStream();
        serializer.Write(restoredBytes, transaction.CreateInverseMutation(applied));
        restoredBytes.Position = 0;
        var restored = reader.Read(restoredBytes);
        Assert.Equal(baseline.Fingerprint, restored.Fingerprint);
    }

    [Fact]
    public void RejectsFingerprintAndEntryDriftInBothDirections()
    {
        using var bytes = BuildPackage();
        var reader = new OpcPackageReader();
        var baseline = reader.Read(bytes);
        var original = baseline.Entries.Single(item => item.Name == "word/document.xml")
            .Content.ToArray();
        var changes = new Dictionary<string, WordPackageEntryPayload>(StringComparer.Ordinal)
        {
            ["word/document.xml"] = new(
                "word/document.xml",
                "/word/document.xml",
                original,
                Encoding.UTF8.GetBytes("changed")
            ),
        };
        var transaction = new WordPackageEntryTransactionCore(
            baseline.Fingerprint,
            new string('a', 64),
            changes
        );

        using var otherBytes = BuildPackage("other");
        var other = reader.Read(otherBytes);
        Assert.Throws<WordSemanticPreconditionException>(() =>
            transaction.CreateMutation(other)
        );
        Assert.Throws<WordSemanticPreconditionException>(() =>
            transaction.CreateInverseMutation(baseline)
        );
    }

    private static MemoryStream BuildPackage(string text = "baseline")
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Default Extension="bin" ContentType="application/octet-stream"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
                """);
            Write(archive, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                """);
            Write(archive, "word/document.xml", text);
            Write(archive, "word/deleted.bin", "delete me");
        }
        stream.Position = 0;
        return stream;
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var target = entry.Open();
        target.Write(Encoding.UTF8.GetBytes(content));
    }
}
