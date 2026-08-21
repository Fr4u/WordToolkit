using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Operations;

namespace WordToolkit.Engine.Tests;

public sealed class DocumentAnalysisWordPackageOperationTests
{
    [Fact]
    public void ProducesOneHighLevelContentFreeAnalysisWithExplicitCoverageGaps()
    {
        var path = TemporaryPath("docx");
        try
        {
            File.WriteAllBytes(path, BuildPackage(includeActiveContent: false));

            var result = new DocumentAnalysisWordPackageOperation().Analyze(
                new DocumentAnalysisRequest(path)
            );

            Assert.Equal(DocumentAnalysisWordPackageContract.Contract, result.OperationContract);
            Assert.True(result.Package.StructurallyValid);
            Assert.True(result.Semantic.SemanticNodeCount > 0);
            Assert.Contains(
                result.Semantic.ObjectCounts,
                item => item.Kind == "paragraph" && item.Count >= 2
            );
            Assert.Equal(1, result.Safety.ExternalRelationshipCount);
            Assert.True(result.Quality.FindingCount > 0);
            Assert.Contains(
                result.Quality.Opportunities,
                item => item.RepairKind == "set_document_title"
                    && item.Implemented
            );
            Assert.Contains(
                result.Signals,
                signal => signal.Code == "EXTERNAL_RELATIONSHIPS_PRESENT"
            );
            Assert.DoesNotContain(
                result.Signals,
                signal => signal.Code == "STRUCTURAL_PACKAGE_INVALID"
            );
            Assert.False(result.Coverage.DocumentCoverageComplete);
            Assert.False(result.Coverage.SemanticCompletenessClaimed);
            Assert.False(result.Coverage.OperationBudgetCoverageComplete);
            Assert.NotEmpty(result.Coverage.ExplicitlyUnmodeledDomains);
            Assert.False(result.Disclosure.DocumentTextReturned);
            Assert.False(result.Disclosure.RawXmlReturned);
            Assert.False(result.Disclosure.SourceLocationsReturned);
            Assert.False(result.Disclosure.ExternalRelationshipTargetsReturned);
            Assert.False(result.Disclosure.ExternalRelationshipsFollowed);
            Assert.False(result.Disclosure.ActiveContentExecuted);
            Assert.False(result.Disclosure.MutationPerformed);
            Assert.False(result.Disclosure.WordOpened);
            Assert.True(result.Disclosure.DocumentContentIsUntrusted);
            Assert.True(result.OperationBudget.XmlParseCache.Requests > 0);
            Assert.True(result.OperationBudget.XmlParseCache.CacheHits > 0);
            Assert.True(result.OperationBudget.XmlParseCache.UniqueParses > 0);
            Assert.Equal(
                result.OperationBudget.XmlParseCache.Requests,
                result.OperationBudget.XmlParseCache.UniqueParses
                    + result.OperationBudget.XmlParseCache.CacheHits
            );
            Assert.True(
                result.OperationBudget.XmlParseCache.AvoidedAccountedBytes > 0
            );
            Assert.DoesNotContain(
                "Sensitive analysis body",
                WordToolkitOperationJson.Serialize(result),
                StringComparison.Ordinal
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ClassifiesTypedActiveContentWithoutOpeningOrDecodingIt()
    {
        var path = TemporaryPath("docm");
        try
        {
            File.WriteAllBytes(path, BuildPackage(includeActiveContent: true));

            var result = new DocumentAnalysisWordPackageOperation().Analyze(
                new DocumentAnalysisRequest(path)
            );

            Assert.True(result.Safety.ActiveContentPresent);
            Assert.True(result.Safety.ActiveContentPayloadCount > 0);
            Assert.False(result.Safety.BinaryPayloadsDecoded);
            Assert.False(result.Safety.EmbeddedPackagesOpened);
            Assert.False(result.Safety.CryptographicSignatureValidationPerformed);
            Assert.Contains(
                result.Signals,
                signal => signal.Code == "ACTIVE_CONTENT_PRESENT"
                    && signal.BlocksAutomaticMutation
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParserAndOperationRejectUnknownFieldsAndStaleFingerprints()
    {
        var unknown = Assert.Throws<WordToolkitOperationException>(() =>
            DocumentAnalysisOperationJson.ParseRequest(
                "{\"local_path\":\"x.docx\",\"raw_xml\":true}"
            )
        );
        Assert.Equal("INVALID_INPUT", unknown.Code);

        var duplicate = Assert.Throws<WordToolkitOperationException>(() =>
            DocumentAnalysisOperationJson.ParseRequest(
                "{\"local_path\":\"x.docx\",\"local_path\":\"y.docx\"}"
            )
        );
        Assert.Equal("INVALID_INPUT", duplicate.Code);

        var path = TemporaryPath("docx");
        try
        {
            File.WriteAllBytes(path, BuildPackage(includeActiveContent: false));
            var operation = new DocumentAnalysisWordPackageOperation();
            var first = operation.Analyze(new DocumentAnalysisRequest(path));
            var upperFingerprint = first.PackageFingerprint.ToUpperInvariant();
            Assert.NotEqual(first.PackageFingerprint, upperFingerprint);
            var uppercase = operation.Analyze(
                new DocumentAnalysisRequest(path, upperFingerprint)
            );
            Assert.Equal(first.PackageFingerprint, uppercase.PackageFingerprint);
            var stale = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Analyze(
                    new DocumentAnalysisRequest(path, new string('0', 64))
                )
            );
            Assert.Equal("VERSION_CONFLICT", stale.Code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SignalPageIsBoundedAndStreamPositionIsRestored()
    {
        using var stream = new MemoryStream(BuildPackage(includeActiveContent: true));
        stream.Position = 9;

        var result = new DocumentAnalysisWordPackageOperation().Analyze(
            stream,
            "sample.docm",
            new DocumentAnalysisRequest("sample.docm", MaxSignals: 1)
        );

        Assert.Equal(9, stream.Position);
        Assert.Equal(1, result.ReturnedSignalCount);
        Assert.True(result.SignalCount > result.ReturnedSignalCount);
        Assert.True(result.SignalsTruncated);
    }

    [Fact]
    public void RepeatedAnalysisIsDeterministic()
    {
        using var firstStream = new MemoryStream(BuildPackage(includeActiveContent: false));
        using var secondStream = new MemoryStream(BuildPackage(includeActiveContent: false));
        var operation = new DocumentAnalysisWordPackageOperation();

        var first = operation.Analyze(
            firstStream,
            "sample.docx",
            new DocumentAnalysisRequest("sample.docx")
        );
        var second = operation.Analyze(
            secondStream,
            "sample.docx",
            new DocumentAnalysisRequest("sample.docx")
        );

        Assert.Equal(
            WordToolkitOperationJson.Serialize(first),
            WordToolkitOperationJson.Serialize(second)
        );
    }

    private static string TemporaryPath(string extension) => Path.Combine(
        Path.GetTempPath(),
        $"wordtoolkit-analysis-{Guid.NewGuid():N}.{extension}"
    );

    private static byte[] BuildPackage(bool includeActiveContent)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var mainContentType = includeActiveContent
                ? "application/vnd.ms-word.document.macroEnabled.main+xml"
                : "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
            var activeContentTypes = includeActiveContent
                ? "<Override PartName=\"/word/vbaProject.bin\" ContentType=\"application/vnd.ms-office.vbaProject\"/>"
                : string.Empty;
            Add(
                archive,
                "[Content_Types].xml",
                $"<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"{mainContentType}\"/><Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/><Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>{activeContentTypes}</Types>"
            );
            Add(
                archive,
                "_rels/.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdRoot" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/><Relationship Id="rIdCore" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/></Relationships>
                """
            );
            Add(
                archive,
                "word/document.xml",
                """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml" mc:Ignorable="w14"><w:body><w:p><w:pPr><w:pStyle w:val="HeadingOne"/></w:pPr><w:r><w:t>Sensitive analysis body</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>Header</w:t></w:r></w:p></w:tc></w:tr><w:tr><w:tc><w:p><w:r><w:t>Value</w:t></w:r></w:p></w:tc></w:tr></w:tbl><mc:AlternateContent><mc:Choice Requires="w14"><w:p/></mc:Choice><mc:Fallback><w:p/></mc:Fallback></mc:AlternateContent></w:body></w:document>
                """
            );
            var activeRelationship = includeActiveContent
                ? "<Relationship Id=\"rVba\" Type=\"http://schemas.microsoft.com/office/2006/relationships/vbaProject\" Target=\"vbaProject.bin\"/>"
                : string.Empty;
            Add(
                archive,
                "word/_rels/document.xml.rels",
                $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/><Relationship Id=\"rExternal\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink\" Target=\"https://example.invalid/secret\" TargetMode=\"External\"/>{activeRelationship}</Relationships>"
            );
            Add(
                archive,
                "word/styles.xml",
                """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:style w:type="paragraph" w:default="1" w:styleId="Normal"/><w:style w:type="paragraph" w:styleId="HeadingOne"><w:pPr><w:outlineLvl w:val="0"/></w:pPr></w:style></w:styles>
                """
            );
            Add(
                archive,
                "docProps/core.xml",
                """
                <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title/></cp:coreProperties>
                """
            );
            if (includeActiveContent)
            {
                AddBytes(archive, "word/vbaProject.bin", [0x01, 0x02, 0x03, 0x04]);
            }
        }
        return stream.ToArray();
    }

    private static void Add(ZipArchive archive, string name, string content) =>
        AddBytes(archive, name, Encoding.UTF8.GetBytes(content));

    private static void AddBytes(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var output = entry.Open();
        output.Write(content);
    }
}
