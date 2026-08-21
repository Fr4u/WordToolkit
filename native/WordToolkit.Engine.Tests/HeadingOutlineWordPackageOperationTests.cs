using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Operations;

namespace WordToolkit.Engine.Tests;

public sealed class HeadingOutlineWordPackageOperationTests
{
    [Fact]
    public void DefaultInspectionIsMainHierarchyOnlyAndReturnsNoDocumentText()
    {
        var path = TemporaryPath();
        try
        {
            CreatePackage(path);

            var result = new HeadingOutlineWordPackageOperation().Inspect(
                new HeadingOutlineInspectionRequest(path)
            );

            Assert.Equal(HeadingOutlineWordPackageContract.Contract, result.OperationContract);
            Assert.Equal("headings", result.View);
            Assert.Equal("main", result.StoryKind);
            Assert.Equal(2, result.MatchedHeadingCount);
            Assert.Equal(2, result.ReturnedItemCount);
            Assert.All(result.Items, item => Assert.Equal("main", item.StoryKind));
            Assert.All(result.Items, item => Assert.Null(item.TextPreview));
            Assert.All(result.Items, item => Assert.Null(item.TitleCharacterCount));
            Assert.All(result.Items, item => Assert.Null(item.ParagraphStyleId));
            Assert.All(result.Items, item => Assert.Null(item.SourcePartUri));
            Assert.False(result.Disclosure.TextReturned);
            Assert.False(result.Disclosure.StylesReturned);
            Assert.False(result.Disclosure.SourceReturned);
            Assert.False(result.Disclosure.RawXmlReturned);
            Assert.False(result.Disclosure.ExternalRelationshipsFollowed);
            Assert.False(result.Disclosure.MutationPerformed);
            Assert.False(result.Disclosure.WordOpened);
            Assert.True(result.Disclosure.DocumentContentIsUntrusted);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SensitivePreviewAndStyleAndSourceAreIndependentExplicitOptIns()
    {
        var path = TemporaryPath();
        try
        {
            CreatePackage(path);

            var result = new HeadingOutlineWordPackageOperation().Inspect(
                new HeadingOutlineInspectionRequest(
                    path,
                    IncludeStyles: true,
                    IncludeSource: true,
                    IncludeSensitive: true,
                    TextPreviewCharacters: 4
                )
            );

            Assert.Equal("Root", result.Items[0].TextPreview);
            Assert.False(result.Items[0].TextPreviewTruncated);
            Assert.Equal("Child", result.Items[0].ParagraphStyleId);
            Assert.Equal("Base", result.Items[0].LevelSourceStyleId);
            Assert.Equal("/word/document.xml", result.Items[0].SourcePartUri);
            Assert.NotNull(result.Items[0].TitleCharacterCount);
            Assert.True(result.Disclosure.TextReturned);
            Assert.True(result.Disclosure.StylesReturned);
            Assert.True(result.Disclosure.SourceReturned);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PagesOnlyAgainstAnExactPackageFingerprint()
    {
        var path = TemporaryPath();
        try
        {
            CreatePackage(path);
            var operation = new HeadingOutlineWordPackageOperation();
            var first = operation.Inspect(new HeadingOutlineInspectionRequest(
                path,
                MaxItems: 1
            ));
            Assert.Equal(1, first.NextOffset);
            var upperFingerprint = first.PackageFingerprint.ToUpperInvariant();
            Assert.NotEqual(first.PackageFingerprint, upperFingerprint);

            var uppercase = operation.Inspect(new HeadingOutlineInspectionRequest(
                path,
                ExpectedPackageFingerprint: upperFingerprint,
                MaxItems: 1
            ));
            Assert.Equal(first.PackageFingerprint, uppercase.PackageFingerprint);

            var second = operation.Inspect(new HeadingOutlineInspectionRequest(
                path,
                ExpectedPackageFingerprint: first.PackageFingerprint,
                Offset: first.NextOffset!.Value,
                MaxItems: 1
            ));

            Assert.Single(second.Items);
            Assert.Null(second.NextOffset);
            Assert.NotEqual(
                first.Items[0].ParagraphNodeId,
                second.Items[0].ParagraphNodeId
            );
            var invalid = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Inspect(new HeadingOutlineInspectionRequest(path, Offset: 1))
            );
            Assert.Equal("INVALID_INPUT", invalid.Code);
            var mismatch = first.PackageFingerprint[..^1]
                + (first.PackageFingerprint[^1] == '0' ? '1' : '0');
            var conflict = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Inspect(new HeadingOutlineInspectionRequest(
                    path,
                    ExpectedPackageFingerprint: mismatch,
                    MaxItems: 1
                ))
            );
            Assert.Equal("VERSION_CONFLICT", conflict.Code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IssueViewSurfacesBrokenStylesWithoutLeakingSourceByDefault()
    {
        var path = TemporaryPath();
        try
        {
            CreatePackage(
                path,
                body:
                    """
                    <w:p><w:pPr><w:pStyle w:val="Missing"/></w:pPr><w:r><w:t>Secret</w:t></w:r></w:p>
                    """
            );

            var result = new HeadingOutlineWordPackageOperation().Inspect(
                new HeadingOutlineInspectionRequest(path, View: "issues")
            );

            var issue = Assert.Single(result.Issues);
            Assert.Equal("OUTLINE_LEVEL_UNRESOLVED", issue.Code);
            Assert.Null(issue.SourcePartUri);
            Assert.DoesNotContain("Secret", HeadingOutlineJson(result), StringComparison.Ordinal);
            Assert.DoesNotContain("Missing", HeadingOutlineJson(result), StringComparison.Ordinal);
            Assert.False(result.Disclosure.StylesReturned);
            Assert.Equal(0, result.ReturnedItemCount);
            Assert.Equal(1, result.UnresolvedParagraphCount);
            Assert.False(result.OutlineCoverageComplete);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParserIsClosedAndRequiresTheSensitivePreviewGate()
    {
        var unknown = Assert.Throws<WordToolkitOperationException>(() =>
            HeadingOutlineOperationJson.ParseInspectRequest(
                "{\"local_path\":\"x.docx\",\"raw_xml\":true}"
            )
        );
        Assert.Equal("INVALID_INPUT", unknown.Code);

        var duplicate = Assert.Throws<WordToolkitOperationException>(() =>
            HeadingOutlineOperationJson.ParseInspectRequest(
                "{\"local_path\":\"x.docx\",\"local_path\":\"y.docx\"}"
            )
        );
        Assert.Equal("INVALID_INPUT", duplicate.Code);

        var request = HeadingOutlineOperationJson.ParseInspectRequest(
            "{\"local_path\":\"x.docx\",\"text_preview_chars\":5}"
        );
        var invalidGate = Assert.Throws<WordToolkitOperationException>(() =>
            new HeadingOutlineWordPackageOperation().Inspect(request)
        );
        Assert.Equal("INVALID_INPUT", invalidGate.Code);
    }

    [Fact]
    public void StreamInspectionRestoresTheOriginalPosition()
    {
        using var stream = PackageBytes();
        stream.Position = 7;
        var request = new HeadingOutlineInspectionRequest("sample.docx");

        var result = new HeadingOutlineWordPackageOperation().Inspect(
            stream,
            "sample.docx",
            request
        );

        Assert.Equal(7, stream.Position);
        Assert.Equal(2, result.HeadingCount);
    }

    private static string HeadingOutlineJson(HeadingOutlineInspectionResult result) =>
        WordToolkitOperationJson.Serialize(result);

    private static string TemporaryPath() => Path.Combine(
        Path.GetTempPath(),
        $"wordtoolkit-outline-{Guid.NewGuid():N}.docx"
    );

    private static void CreatePackage(string path, string? body = null)
    {
        using var source = PackageBytes(body);
        File.WriteAllBytes(path, source.ToArray());
    }

    private static MemoryStream PackageBytes(string? body = null)
    {
        body ??=
            """
            <w:p><w:pPr><w:pStyle w:val="Child"/></w:pPr><w:r><w:t>Root</w:t></w:r></w:p>
            <w:p><w:pPr><w:outlineLvl w:val="1"/></w:pPr><w:r><w:t>Sensitive child heading</w:t></w:r></w:p>
            <w:p><w:r><w:t>Body text</w:t></w:r></w:p>
            """;
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                "[Content_Types].xml",
                """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/></Types>
                """
            );
            WriteEntry(
                archive,
                "_rels/.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                """
            );
            WriteEntry(
                archive,
                "word/document.xml",
                $"<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>{body}</w:body></w:document>"
            );
            WriteEntry(
                archive,
                "word/_rels/document.xml.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
                """
            );
            WriteEntry(
                archive,
                "word/styles.xml",
                """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:style w:type="paragraph" w:default="1" w:styleId="Normal"/><w:style w:type="paragraph" w:styleId="Base"><w:pPr><w:outlineLvl w:val="0"/></w:pPr></w:style><w:style w:type="paragraph" w:styleId="Child"><w:basedOn w:val="Base"/></w:style></w:styles>
                """
            );
        }
        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }
}
