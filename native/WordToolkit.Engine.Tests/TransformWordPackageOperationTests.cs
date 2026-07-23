using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class TransformWordPackageOperationTests
{
    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void ReplacesAcrossRunBoundaryAndPreservesOpaqueContent()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "input.docx");
            var output = Path.Combine(directory, "output.docx");
            var opaque = SHA256.HashData(Encoding.UTF8.GetBytes("opaque-sentinel"));
            var extension =
                "<ext:opaque xmlns:ext='urn:wordtoolkit:test' ext:id='sentinel'>"
                + "do-not-touch</ext:opaque>";
            CreatePackage(
                input,
                DocumentXml(
                    "<w:p><w:r><w:t>Alpha-tar</w:t></w:r>"
                        + "<w:r><w:t>get-tail</w:t></w:r>"
                        + "<w:sdt><w:sdtPr>"
                        + extension
                        + "</w:sdtPr><w:sdtContent><w:r><w:t> controlled</w:t>"
                        + "</w:r></w:sdtContent></w:sdt></w:p>"
                ),
                new Dictionary<string, byte[]> { ["custom/opaque.bin"] = opaque }
            );
            var inputHash = SHA256.HashData(File.ReadAllBytes(input));

            var result = new TransformWordPackageOperation().Execute(
                new TransformWordPackageRequest(
                    input,
                    output,
                    WordPackageTransformKind.ReplaceFirstTextOccurrence,
                    "target",
                    "clause"
                )
            );

            Assert.True(result.Changed);
            Assert.Equal(6, result.MatchOffset);
            Assert.Equal(2, result.MatchedTextNodeCount);
            Assert.Equal(["word/document.xml"], result.ChangedEntryNames);
            Assert.Equal(inputHash, SHA256.HashData(File.ReadAllBytes(input)));
            var package = new OpcPackageReader().Read(output);
            var xml = PartXml(package);
            var semantic = new WordSemanticProjector().Project(package);
            var paragraph = semantic.Nodes.Single(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
            );
            Assert.Equal("Alpha-clause-tail controlled", paragraph.TextPreview());
            Assert.Contains(extension, xml, StringComparison.Ordinal);
            Assert.Equal(
                opaque,
                package.Entries.Single(entry => entry.Name == "custom/opaque.bin")
                    .Content.ToArray()
            );
            Assert.Equal(result.ResultPackageFingerprint, package.Fingerprint);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReplacesOnlyTheFirstParagraphOccurrence()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "first.docx");
            var output = Path.Combine(directory, "first-output.docx");
            CreatePackage(
                input,
                DocumentXml(
                    "<w:p><w:r><w:t>thirty</w:t></w:r></w:p>"
                        + "<w:p><w:r><w:t>thirty</w:t></w:r></w:p>"
                )
            );

            _ = new TransformWordPackageOperation().Execute(
                new TransformWordPackageRequest(
                    input,
                    output,
                    WordPackageTransformKind.ReplaceFirstTextOccurrence,
                    "thirty",
                    "sixty"
                )
            );

            var xml = PartXml(new OpcPackageReader().Read(output));
            Assert.Equal(1, Count(xml, ">sixty<"));
            Assert.Equal(1, Count(xml, ">thirty<"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExcludesOfficeMathTextFromPlainTextMatching()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "math.docx");
            var output = Path.Combine(directory, "math-output.docx");
            CreatePackage(
                input,
                DocumentXml(
                    "<w:p><m:oMath xmlns:m='http://schemas.openxmlformats.org/officeDocument/2006/math'>"
                        + "<m:r><m:t>target</m:t></m:r></m:oMath>"
                        + "<w:r><w:t> plain target</w:t></w:r></w:p>"
                )
            );

            _ = new TransformWordPackageOperation().Execute(
                new TransformWordPackageRequest(
                    input,
                    output,
                    WordPackageTransformKind.ReplaceFirstTextOccurrence,
                    "target",
                    "changed"
                )
            );

            var xml = PartXml(new OpcPackageReader().Read(output));
            Assert.Contains("<m:t>target</m:t>", xml, StringComparison.Ordinal);
            Assert.Contains(" plain changed", xml, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DeclinesAmbiguousMarkupCompatibilityParagraph()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "mce.docx");
            var output = Path.Combine(directory, "mce-output.docx");
            CreatePackage(
                input,
                DocumentXml(
                    "<w:p><mc:AlternateContent xmlns:mc='http://schemas.openxmlformats.org/markup-compatibility/2006'>"
                        + "<mc:Choice Requires='w14' xmlns:w14='http://schemas.microsoft.com/office/word/2010/wordml'>"
                        + "<w:r><w:t>target</w:t></w:r></mc:Choice>"
                        + "<mc:Fallback><w:r><w:t>target</w:t></w:r></mc:Fallback>"
                        + "</mc:AlternateContent></w:p>"
                )
            );

            var exception = Assert.Throws<WordToolkitOperationException>(() =>
                new TransformWordPackageOperation().Execute(
                    new TransformWordPackageRequest(
                        input,
                        output,
                        WordPackageTransformKind.ReplaceFirstTextOccurrence,
                        "target",
                        "changed"
                    )
                )
            );

            Assert.Equal("UNSUPPORTED_DOCUMENT", exception.Code);
            Assert.Contains("Markup Compatibility", exception.Reason, StringComparison.Ordinal);
            Assert.False(File.Exists(output));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AcceptsAndRejectsAllModeledRevisionsToSeparateOutputs()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "review.docx");
            var accepted = Path.Combine(directory, "accepted.docx");
            var rejected = Path.Combine(directory, "rejected.docx");
            CreatePackage(
                input,
                DocumentXml(
                    "<w:p>"
                        + "<w:ins w:id='1' w:author='A'><w:r><w:t>new</w:t></w:r></w:ins>"
                        + "<w:del w:id='2' w:author='A'><w:r><w:delText>old</w:delText></w:r></w:del>"
                        + "</w:p>"
                )
            );

            var operation = new TransformWordPackageOperation();
            var acceptResult = operation.Execute(
                new TransformWordPackageRequest(
                    input,
                    accepted,
                    WordPackageTransformKind.AcceptAllTrackedChanges
                )
            );
            var rejectResult = operation.Execute(
                new TransformWordPackageRequest(
                    input,
                    rejected,
                    WordPackageTransformKind.RejectAllTrackedChanges
                )
            );

            var acceptedXml = PartXml(new OpcPackageReader().Read(accepted));
            var rejectedXml = PartXml(new OpcPackageReader().Read(rejected));
            Assert.Contains(">new<", acceptedXml, StringComparison.Ordinal);
            Assert.DoesNotContain(">old<", acceptedXml, StringComparison.Ordinal);
            Assert.Contains(">old<", rejectedXml, StringComparison.Ordinal);
            Assert.DoesNotContain(">new<", rejectedXml, StringComparison.Ordinal);
            Assert.DoesNotContain("<w:ins", acceptedXml, StringComparison.Ordinal);
            Assert.DoesNotContain("<w:del", acceptedXml, StringComparison.Ordinal);
            Assert.DoesNotContain("<w:ins", rejectedXml, StringComparison.Ordinal);
            Assert.DoesNotContain("<w:del", rejectedXml, StringComparison.Ordinal);
            Assert.Equal(0, acceptResult.RemainingRevisionCount);
            Assert.Equal(0, rejectResult.RemainingRevisionCount);
            Assert.Equal(2, acceptResult.SubmittedRevisionCount);
            Assert.Equal(2, rejectResult.SubmittedRevisionCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DeclinesUnsupportedStructuralRevisionWithoutWritingOutput()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "structural.docx");
            var output = Path.Combine(directory, "should-not-exist.docx");
            CreatePackage(
                input,
                DocumentXml(
                    "<w:p><w:pPr><w:rPr><w:del w:id='9' w:author='A'/>"
                        + "</w:rPr></w:pPr><w:r><w:t>x</w:t></w:r></w:p>"
                )
            );

            var exception = Assert.Throws<WordToolkitOperationException>(() =>
                new TransformWordPackageOperation().Execute(
                    new TransformWordPackageRequest(
                        input,
                        output,
                        WordPackageTransformKind.AcceptAllTrackedChanges
                    )
                )
            );

            Assert.Equal("UNSUPPORTED_DOCUMENT", exception.Code);
            Assert.Contains(
                "paragraph_merge_target_missing",
                exception.Reason,
                StringComparison.Ordinal
            );
            Assert.False(File.Exists(output));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BlocksSignedPackageAndExistingOutput()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var signed = Path.Combine(directory, "signed.docx");
            var signedOutput = Path.Combine(directory, "signed-output.docx");
            CreatePackage(
                signed,
                DocumentXml("<w:p><w:r><w:t>x</w:t></w:r></w:p>"),
                new Dictionary<string, byte[]>
                {
                    ["_xmlsignatures/sig1.xml"] = Encoding.UTF8.GetBytes("<Signature/>")
                }
            );
            var signedError = Assert.Throws<WordToolkitOperationException>(() =>
                new TransformWordPackageOperation().Execute(
                    new TransformWordPackageRequest(
                        signed,
                        signedOutput,
                        WordPackageTransformKind.ReplaceFirstTextOccurrence,
                        "x",
                        "y"
                    )
                )
            );
            Assert.Equal("SIGNED_PACKAGE", signedError.Code);
            Assert.False(File.Exists(signedOutput));

            var input = Path.Combine(directory, "input.docx");
            var existing = Path.Combine(directory, "existing.docx");
            CreatePackage(input, DocumentXml("<w:p/>"));
            File.WriteAllText(existing, "sentinel");
            var conflict = Assert.Throws<WordToolkitOperationException>(() =>
                new TransformWordPackageOperation().Execute(
                    new TransformWordPackageRequest(
                        input,
                        existing,
                        WordPackageTransformKind.AcceptAllTrackedChanges
                    )
                )
            );
            Assert.Equal("VERSION_CONFLICT", conflict.Code);
            Assert.Equal("sentinel", File.ReadAllText(existing));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void NoRevisionTransformProducesVerifiedCloneWithoutSemanticChanges()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "clean.docx");
            var output = Path.Combine(directory, "clean-copy.docx");
            CreatePackage(
                input,
                DocumentXml("<w:p><w:r><w:t>clean</w:t></w:r></w:p>"),
                new Dictionary<string, byte[]> { ["word/vbaProject.bin"] = [1, 2, 3] }
            );

            var result = new TransformWordPackageOperation().Execute(
                new TransformWordPackageRequest(
                    input,
                    output,
                    WordPackageTransformKind.AcceptAllTrackedChanges
                )
            );

            Assert.False(result.Changed);
            Assert.Empty(result.ChangedEntryNames);
            Assert.Equal(result.BasePackageFingerprint, result.ResultPackageFingerprint);
            var before = new OpcPackageReader().Read(input);
            var after = new OpcPackageReader().Read(output);
            Assert.Equal(before.Fingerprint, after.Fingerprint);
            Assert.Equal(
                before.Entries.Select(entry => (entry.Name, entry.Sha256)),
                after.Entries.Select(entry => (entry.Name, entry.Sha256))
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CreatePackage(
        string path,
        string documentXml,
        IReadOnlyDictionary<string, byte[]>? extraEntries = null
    )
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "[Content_Types].xml",
            """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
              <Default Extension="xml" ContentType="application/xml" />
              <Default Extension="bin" ContentType="application/octet-stream" />
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" />
            </Types>
            """
        );
        WriteEntry(
            archive,
            "_rels/.rels",
            $"""
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="{WordPackageConformance.TransitionalOfficeDocumentRelationship}" Target="word/document.xml" />
            </Relationships>
            """
        );
        WriteEntry(archive, "word/document.xml", documentXml);
        foreach (var (name, content) in extraEntries ?? new Dictionary<string, byte[]>())
        {
            WriteEntry(archive, name, content);
        }
    }

    private static string DocumentXml(string body) =>
        $"<w:document xmlns:w='{WordNamespace}'><w:body>{body}</w:body></w:document>";

    private static string PartXml(OpcPackageSnapshot package) => Encoding.UTF8.GetString(
        package.Parts["/word/document.xml"].Entry.Content.Span
    );

    private static int Count(string value, string token)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(token, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += token.Length;
        }
        return count;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content) =>
        WriteEntry(archive, name, Encoding.UTF8.GetBytes(content));

    private static void WriteEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var target = entry.Open();
        target.Write(content);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-transform-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }
}
