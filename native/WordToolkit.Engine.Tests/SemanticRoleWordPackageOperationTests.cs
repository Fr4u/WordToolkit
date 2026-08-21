using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class SemanticRoleWordPackageOperationTests
{
    [Fact]
    public void DefaultFindsUsableMainStoryTheoremsWithoutReturningContent()
    {
        var path = TemporaryPath();
        try
        {
            File.WriteAllBytes(path, BuildPackage());

            var result = new SemanticRoleWordPackageOperation().Inspect(
                SemanticRoleInspectionRequest.Default(path)
            );

            Assert.Equal(SemanticRoleWordPackageContract.Contract, result.OperationContract);
            Assert.Equal(["theorem"], result.RequestedRoles);
            Assert.Equal(3, result.MatchedCandidateCount);
            Assert.Equal(3, result.ReturnedItemCount);
            Assert.All(result.Items, item => Assert.Equal("theorem", item.Role));
            Assert.All(result.Items, item => Assert.True(item.UsableAsSemanticRole));
            Assert.All(result.Items, item => Assert.Null(item.Evidence));
            Assert.All(result.Items, item => Assert.Null(item.TextPreview));
            Assert.All(result.Items, item => Assert.Null(item.ParagraphCharacterCount));
            Assert.All(result.Items, item => Assert.Null(item.ParagraphTextFingerprint));
            Assert.All(result.Items, item => Assert.Null(item.SourcePartUri));
            Assert.False(result.SemanticCompletenessClaimed);
            Assert.False(result.SemanticRoleCoverageComplete);
            Assert.Contains("unstated_author_conventions", result.CoverageOmissions);
            Assert.False(result.Disclosure.TextReturned);
            Assert.False(result.Disclosure.EvidenceReturned);
            Assert.False(result.Disclosure.RawXmlReturned);
            Assert.False(result.Disclosure.CustomXmlValuesReturned);
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
    public void EvidenceTextStylesDeclarationsHashesAndSourceAreIndependentOptIns()
    {
        var path = TemporaryPath();
        try
        {
            File.WriteAllBytes(path, BuildPackage());
            var request = SemanticRoleInspectionRequest.Default(path) with
            {
                IncludeEvidence = true,
                IncludeStyles = true,
                IncludeDeclarations = true,
                IncludeHashes = true,
                IncludeSource = true,
                IncludeSensitive = true,
                TextPreviewCharacters = 24,
            };

            var result = new SemanticRoleWordPackageOperation().Inspect(request);

            Assert.Contains(result.Items, item =>
                item.TextPreview == "Theorem 1. Lexical text"
            );
            Assert.All(result.Items, item => Assert.NotNull(item.ParagraphCharacterCount));
            Assert.All(result.Items, item => Assert.NotNull(item.ParagraphTextFingerprint));
            Assert.All(result.Items, item => Assert.NotNull(item.SourcePartUri));
            Assert.Contains(result.Items.SelectMany(item => item.Evidence ?? []), evidence =>
                evidence.StyleId == "Theorem"
            );
            Assert.Contains(result.Items.SelectMany(item => item.Evidence ?? []), evidence =>
                evidence.ContentControlId is not null
            );
            Assert.True(result.Disclosure.TextReturned);
            Assert.True(result.Disclosure.EvidenceReturned);
            Assert.True(result.Disclosure.StylesReturned);
            Assert.True(result.Disclosure.DeclarationsReturned);
            Assert.True(result.Disclosure.HashesReturned);
            Assert.True(result.Disclosure.SourceReturned);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ConflictsAreVisibleOnlyWhenTheCallerDropsUsableFilter()
    {
        var path = TemporaryPath();
        try
        {
            File.WriteAllBytes(path, BuildPackage());
            var operation = new SemanticRoleWordPackageOperation();
            var ordinary = operation.Inspect(SemanticRoleInspectionRequest.Default(path));
            var diagnostic = operation.Inspect(
                SemanticRoleInspectionRequest.Default(path) with
                {
                    UsableOnly = false,
                    IncludeEvidence = true,
                }
            );

            Assert.DoesNotContain(ordinary.Items, item => item.Classification == "conflicting");
            var conflict = Assert.Single(
                diagnostic.Items,
                item => item.Classification == "conflicting"
            );
            Assert.Null(conflict.Role);
            Assert.False(conflict.UsableAsSemanticRole);
            Assert.Contains(conflict.Evidence!, item => item.Role == "theorem");
            Assert.Contains(conflict.Evidence!, item => item.Role == "definition");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PagingRequiresAndHonorsTheExactPackageFingerprint()
    {
        var path = TemporaryPath();
        try
        {
            File.WriteAllBytes(path, BuildPackage());
            var operation = new SemanticRoleWordPackageOperation();
            var first = operation.Inspect(
                SemanticRoleInspectionRequest.Default(path) with { MaxItems = 1 }
            );
            Assert.Equal(1, first.NextOffset);
            var upperFingerprint = first.PackageFingerprint.ToUpperInvariant();
            Assert.NotEqual(first.PackageFingerprint, upperFingerprint);

            var uppercase = operation.Inspect(
                SemanticRoleInspectionRequest.Default(path) with
                {
                    ExpectedPackageFingerprint = upperFingerprint,
                    MaxItems = 1,
                }
            );
            Assert.Equal(first.PackageFingerprint, uppercase.PackageFingerprint);

            var second = operation.Inspect(
                SemanticRoleInspectionRequest.Default(path) with
                {
                    ExpectedPackageFingerprint = first.PackageFingerprint,
                    Offset = 1,
                    MaxItems = 1,
                }
            );

            Assert.Single(second.Items);
            Assert.NotEqual(first.Items[0].CandidateId, second.Items[0].CandidateId);
            var invalid = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Inspect(
                    SemanticRoleInspectionRequest.Default(path) with { Offset = 1 }
                )
            );
            Assert.Equal("INVALID_INPUT", invalid.Code);
            var mismatch = first.PackageFingerprint[..^1]
                + (first.PackageFingerprint[^1] == '0' ? '1' : '0');
            var conflict = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Inspect(
                    SemanticRoleInspectionRequest.Default(path) with
                    {
                        ExpectedPackageFingerprint = mismatch,
                        MaxItems = 1,
                    }
                )
            );
            Assert.Equal("VERSION_CONFLICT", conflict.Code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParserIsClosedAndRequiresExplicitDisclosureGates()
    {
        var unknown = Assert.Throws<WordToolkitOperationException>(() =>
            SemanticRoleOperationJson.ParseInspectRequest(
                "{\"local_path\":\"x.docx\",\"raw_xml\":true}"
            )
        );
        Assert.Equal("INVALID_INPUT", unknown.Code);

        var duplicate = Assert.Throws<WordToolkitOperationException>(() =>
            SemanticRoleOperationJson.ParseInspectRequest(
                "{\"local_path\":\"x.docx\",\"local_path\":\"y.docx\"}"
            )
        );
        Assert.Equal("INVALID_INPUT", duplicate.Code);

        var invalidRole = Assert.Throws<WordToolkitOperationException>(() =>
            SemanticRoleOperationJson.ParseInspectRequest(
                "{\"local_path\":\"x.docx\",\"roles\":[\"magic\"]}"
            )
        );
        Assert.Equal("INVALID_INPUT", invalidRole.Code);

        var preview = SemanticRoleOperationJson.ParseInspectRequest(
            "{\"local_path\":\"x.docx\",\"text_preview_chars\":5}"
        );
        var invalidPreview = Assert.Throws<WordToolkitOperationException>(() =>
            new SemanticRoleWordPackageOperation().Inspect(preview)
        );
        Assert.Equal("INVALID_INPUT", invalidPreview.Code);

        var style = SemanticRoleOperationJson.ParseInspectRequest(
            "{\"local_path\":\"x.docx\",\"include_styles\":true}"
        );
        var invalidStyle = Assert.Throws<WordToolkitOperationException>(() =>
            new SemanticRoleWordPackageOperation().Inspect(style)
        );
        Assert.Equal("INVALID_INPUT", invalidStyle.Code);
    }

    [Fact]
    public void StreamInspectionRestoresOriginalPosition()
    {
        using var stream = new MemoryStream(BuildPackage());
        stream.Position = 9;

        var result = new SemanticRoleWordPackageOperation().Inspect(
            stream,
            "sample.docx",
            SemanticRoleInspectionRequest.Default("sample.docx")
        );

        Assert.Equal(9, stream.Position);
        Assert.Equal(3, result.MatchedCandidateCount);
    }

    private static string TemporaryPath() => Path.Combine(
        Path.GetTempPath(),
        $"wordtoolkit-semantic-roles-{Guid.NewGuid():N}.docx"
    );

    private static byte[] BuildPackage()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(
                archive,
                "[Content_Types].xml",
                """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/></Types>
                """
            );
            Add(
                archive,
                "_rels/.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                """
            );
            Add(
                archive,
                "word/document.xml",
                """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
                <w:p><w:r><w:t>Theorem 1. Lexical text</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Theorem"/></w:pPr><w:r><w:t>Styled theorem</w:t></w:r></w:p>
                <w:sdt><w:sdtPr><w:tag w:val="wordtoolkit:role=theorem"/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>Declared theorem</w:t></w:r></w:p></w:sdtContent></w:sdt>
                <w:p><w:pPr><w:pStyle w:val="Definition"/></w:pPr><w:r><w:t>Theorem 2. conflict</w:t></w:r></w:p>
                <w:p><w:r><w:t>Ordinary body</w:t></w:r></w:p>
                </w:body></w:document>
                """
            );
            Add(
                archive,
                "word/_rels/document.xml.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
                """
            );
            Add(
                archive,
                "word/styles.xml",
                """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style><w:style w:type="paragraph" w:styleId="Theorem"><w:name w:val="Twierdzenie"/></w:style><w:style w:type="paragraph" w:styleId="Definition"><w:name w:val="Definicja"/></w:style></w:styles>
                """
            );
        }
        return stream.ToArray();
    }

    private static void Add(ZipArchive archive, string name, string text)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var output = entry.Open();
        output.Write(Encoding.UTF8.GetBytes(text));
    }
}
