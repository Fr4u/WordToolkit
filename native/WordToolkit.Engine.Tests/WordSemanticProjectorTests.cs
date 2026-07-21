using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordSemanticProjectorTests
{
    [Fact]
    public void ProjectsWordStructuresOfficeMathAndUnknownExtensions()
    {
        using var package = BuildPackage(RichDocumentXml());
        var snapshot = new OpcPackageReader().Read(package);

        var semantic = new WordSemanticProjector().Project(snapshot);

        Assert.Equal(WordSemanticNodeKind.Document, semantic.Root.Kind);
        Assert.Equal("/word/document.xml", semantic.MainPartUri);
        Assert.Equal(2, semantic.Nodes.Count(node =>
            node.Kind == WordSemanticNodeKind.Paragraph
        ));
        Assert.Single(semantic.Nodes, node => node.Kind == WordSemanticNodeKind.Table);
        Assert.Single(semantic.Nodes, node => node.Kind == WordSemanticNodeKind.Equation);
        Assert.Contains(
            semantic.Nodes,
            node => node.Kind == WordSemanticNodeKind.EquationComponent
                && node.Properties["math_element"] == "f"
        );
        Assert.Contains(
            semantic.Nodes,
            node => node.Kind == WordSemanticNodeKind.ExtensionIsland
                && node.Properties["namespace"] == "urn:wordtoolkit:test"
        );
        var paragraph = Assert.Single(
            semantic.Nodes,
            node => node.Kind == WordSemanticNodeKind.Paragraph
                && node.Properties.ContainsKey("paragraph_id")
        );
        Assert.Equal("00112233", paragraph.Properties["paragraph_id"]);
        Assert.Equal("Heading1", paragraph.Properties["style_id"]);
        Assert.Contains("Linked equation", paragraph.TextPreview());
        Assert.StartsWith("wdn_", paragraph.Id.Value, StringComparison.Ordinal);
        Assert.Equal(
            semantic.NodeCount,
            semantic.Nodes.Select(node => node.Id).Distinct().Count()
        );
    }

    [Fact]
    public void DurableParagraphIdSurvivesUnrelatedInsertion()
    {
        var originalXml = DocumentWithParagraphs(
            Paragraph("00112233", "stable"),
            Paragraph("44556677", "second")
        );
        var insertedXml = DocumentWithParagraphs(
            Paragraph("8899AABB", "inserted"),
            Paragraph("00112233", "stable"),
            Paragraph("44556677", "second")
        );
        using var originalPackage = BuildPackage(originalXml);
        using var insertedPackage = BuildPackage(insertedXml);
        var reader = new OpcPackageReader();
        var projector = new WordSemanticProjector();

        var original = projector.Project(reader.Read(originalPackage));
        var inserted = projector.Project(reader.Read(insertedPackage));
        var originalParagraph = FindParagraph(original, "00112233");
        var insertedParagraph = FindParagraph(inserted, "00112233");

        Assert.Equal(originalParagraph.Id, insertedParagraph.Id);
        Assert.Equal(
            originalParagraph.Children.Single(node => node.Kind == WordSemanticNodeKind.Run).Id,
            insertedParagraph.Children.Single(node => node.Kind == WordSemanticNodeKind.Run).Id
        );
    }

    [Fact]
    public void IdenticalUnanchoredParagraphsReceiveDistinctIds()
    {
        using var package = BuildPackage(
            DocumentWithParagraphs(
                "<w:p><w:r><w:t>same</w:t></w:r></w:p>",
                "<w:p><w:r><w:t>same</w:t></w:r></w:p>"
            )
        );

        var semantic = new WordSemanticProjector().Project(
            new OpcPackageReader().Read(package)
        );
        var paragraphs = semantic.Nodes
            .Where(node => node.Kind == WordSemanticNodeKind.Paragraph)
            .ToArray();

        Assert.Equal(2, paragraphs.Length);
        Assert.NotEqual(paragraphs[0].Id, paragraphs[1].Id);
    }

    [Fact]
    public void StrictWordprocessingNamespaceIsSupported()
    {
        const string strictDocument = """
            <w:document xmlns:w="http://purl.oclc.org/ooxml/wordprocessingml/main">
              <w:body><w:p><w:r><w:t>strict</w:t></w:r></w:p></w:body>
            </w:document>
            """;
        using var package = BuildPackage(strictDocument);

        var semantic = new WordSemanticProjector().Project(
            new OpcPackageReader().Read(package)
        );

        Assert.Contains(
            semantic.Nodes,
            node => node.Kind == WordSemanticNodeKind.Text && node.Text == "strict"
        );
    }

    [Fact]
    public void MainDocumentDtdIsRejected()
    {
        const string malicious = """
            <!DOCTYPE w:document [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body><w:p><w:r><w:t>&xxe;</w:t></w:r></w:p></w:body>
            </w:document>
            """;
        using var package = BuildPackage(malicious);

        var exception = Assert.Throws<WordSemanticProjectionException>(() =>
            new WordSemanticProjector().Project(new OpcPackageReader().Read(package))
        );

        Assert.Contains("safe", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectionDepthLimitStopsPathologicalNesting()
    {
        var nestedRuns = string.Concat(Enumerable.Repeat("<w:r>", 8))
            + "<w:t>x</w:t>"
            + string.Concat(Enumerable.Repeat("</w:r>", 8));
        using var package = BuildPackage(DocumentWithParagraphs($"<w:p>{nestedRuns}</w:p>"));
        var projector = new WordSemanticProjector(
            new WordSemanticProjectionOptions { MaxXmlDepth = 6 }
        );

        Assert.Throws<WordSemanticLimitException>(() =>
            projector.Project(new OpcPackageReader().Read(package))
        );
    }

    [Fact]
    public void ProjectorCanBeReusedConcurrentlyWithoutIdentityDrift()
    {
        using var package = BuildPackage(
            DocumentWithParagraphs(Paragraph("00112233", "stable"))
        );
        var snapshot = new OpcPackageReader().Read(package);
        var projector = new WordSemanticProjector();
        var ids = new System.Collections.Concurrent.ConcurrentBag<SemanticNodeId>();

        Parallel.For(
            0,
            16,
            _ => ids.Add(FindParagraph(projector.Project(snapshot), "00112233").Id)
        );

        Assert.Equal(16, ids.Count);
        Assert.Single(ids.Distinct());
    }

    [Fact]
    public void ProjectsEveryPrimaryTextStoryAndKeepsMainIdsStable()
    {
        using var originalPackage = BuildStoryPackage("Header A");
        using var changedStoryPackage = BuildStoryPackage("Header B");
        var reader = new OpcPackageReader();
        var projector = new WordSemanticProjector();

        var original = projector.Project(reader.Read(originalPackage));
        var changedStory = projector.Project(reader.Read(changedStoryPackage));

        Assert.Equal(7, original.ProjectedPartCount);
        Assert.Equal(
            new[]
            {
                "/word/document.xml",
                "/word/header1.xml",
                "/word/footer1.xml",
                "/word/footnotes.xml",
                "/word/endnotes.xml",
                "/word/comments.xml",
                "/word/glossary/document.xml",
            },
            original.ProjectedPartUris
        );
        foreach (
            var kind in new[]
            {
                WordSemanticNodeKind.Header,
                WordSemanticNodeKind.Footer,
                WordSemanticNodeKind.Footnotes,
                WordSemanticNodeKind.Footnote,
                WordSemanticNodeKind.Endnotes,
                WordSemanticNodeKind.Endnote,
                WordSemanticNodeKind.Comments,
                WordSemanticNodeKind.Comment,
                WordSemanticNodeKind.GlossaryDocument,
                WordSemanticNodeKind.GlossaryEntry,
            }
        )
        {
            Assert.Contains(original.Nodes, node => node.Kind == kind);
        }

        var header = Assert.Single(
            original.Nodes,
            node => node.Kind == WordSemanticNodeKind.Header
        );
        Assert.Equal(original.Root.Id, header.ParentId);
        Assert.Equal("header", header.Properties["story_kind"]);
        Assert.Equal("rIdHeader", header.Properties["relationship_id"]);
        var comment = Assert.Single(
            original.Nodes,
            node => node.Kind == WordSemanticNodeKind.Comment
        );
        Assert.Equal("4", comment.Properties["id"]);
        Assert.Equal("Ada", comment.Properties["author"]);
        var glossaryEntry = Assert.Single(
            original.Nodes,
            node => node.Kind == WordSemanticNodeKind.GlossaryEntry
        );
        Assert.Equal("Clause", glossaryEntry.Properties["name"]);
        Assert.Equal("{00112233-4455-6677-8899-AABBCCDDEEFF}", glossaryEntry.Properties["guid"]);
        Assert.Contains(
            original.Nodes,
            node => node.Kind == WordSemanticNodeKind.Text
                && node.Text == "Header A"
                && node.SourcePartUri == "/word/header1.xml"
        );

        var originalMainParagraph = FindParagraph(original, "00112233");
        var changedMainParagraph = FindParagraph(changedStory, "00112233");
        Assert.Equal(originalMainParagraph.Id, changedMainParagraph.Id);
        Assert.Equal(
            header.Id,
            Assert.Single(
                changedStory.Nodes,
                node => node.Kind == WordSemanticNodeKind.Header
            ).Id
        );
    }

    [Fact]
    public void LosslessTransactionCanEditAHeaderStoryWithoutTouchingMainXml()
    {
        using var package = BuildStoryPackage("Header A");
        var reader = new OpcPackageReader();
        var before = reader.Read(package);
        var semantic = new WordSemanticProjector().Project(before);
        var headerText = Assert.Single(
            semantic.Nodes,
            node => node.Kind == WordSemanticNodeKind.Text
                && node.Text == "Header A"
        );
        var plan = new WordSemanticTransactionPlanner().PlanTextReplacements(
            before,
            semantic,
            [new WordTextReplacementCommand(headerText.Id, "Header & changed", "Header A")]
        );
        using var changedBytes = new MemoryStream();

        new OpcPackageSerializer().Write(changedBytes, plan.CreateMutation(before));
        changedBytes.Position = 0;
        var after = reader.Read(changedBytes);

        Assert.Equal(plan.ResultPackageFingerprint, after.Fingerprint);
        Assert.Equal(
            before.Parts["/word/document.xml"].Entry.Content.ToArray(),
            after.Parts["/word/document.xml"].Entry.Content.ToArray()
        );
        Assert.Contains(
            new WordSemanticProjector().Project(after).Nodes,
            node => node.Kind == WordSemanticNodeKind.Text
                && node.Text == "Header & changed"
                && node.SourcePartUri == "/word/header1.xml"
        );
    }

    [Fact]
    public void StoryPartLimitStopsRelationshipFanOut()
    {
        using var package = BuildStoryPackage("Header A");
        var projector = new WordSemanticProjector(
            new WordSemanticProjectionOptions { MaxStoryParts = 2 }
        );

        Assert.Throws<WordSemanticLimitException>(() =>
            projector.Project(new OpcPackageReader().Read(package))
        );
    }

    [Fact]
    public void DtdInRelatedStoryIsRejected()
    {
        const string maliciousHeader = """
            <!DOCTYPE w:hdr [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:p><w:r><w:t>&xxe;</w:t></w:r></w:p>
            </w:hdr>
            """;
        using var package = BuildStoryPackage("unused", maliciousHeader);

        var exception = Assert.Throws<WordSemanticProjectionException>(() =>
            new WordSemanticProjector().Project(new OpcPackageReader().Read(package))
        );

        Assert.Contains("/word/header1.xml", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StrictRelationshipNamespaceCanReachAStoryPart()
    {
        using var package = BuildStoryPackage(
            "Strict relationship",
            relationshipNamespace:
                "http://purl.oclc.org/ooxml/officeDocument/relationships"
        );

        var semantic = new WordSemanticProjector().Project(
            new OpcPackageReader().Read(package)
        );

        Assert.Contains(
            semantic.Nodes,
            node => node.Kind == WordSemanticNodeKind.Text
                && node.Text == "Strict relationship"
                && node.SourcePartUri == "/word/header1.xml"
        );
    }

    private static WordSemanticNode FindParagraph(
        WordSemanticDocument document,
        string paragraphId
    ) => Assert.Single(
        document.Nodes,
        node => node.Kind == WordSemanticNodeKind.Paragraph
            && node.Properties.TryGetValue("paragraph_id", out var value)
            && value == paragraphId
    );

    private static MemoryStream BuildPackage(string documentXml)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                "[Content_Types].xml",
                """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
                  <Default Extension="xml" ContentType="application/xml" />
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" />
                </Types>
                """
            );
            WriteEntry(
                archive,
                "_rels/.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml" />
                </Relationships>
                """
            );
            WriteEntry(archive, "word/document.xml", documentXml);
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream BuildStoryPackage(
        string headerText,
        string? headerXml = null,
        string relationshipNamespace =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                "[Content_Types].xml",
                """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
                  <Default Extension="xml" ContentType="application/xml" />
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" />
                  <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml" />
                  <Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml" />
                  <Override PartName="/word/footnotes.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml" />
                  <Override PartName="/word/endnotes.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.endnotes+xml" />
                  <Override PartName="/word/comments.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml" />
                  <Override PartName="/word/glossary/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.glossary+xml" />
                </Types>
                """
            );
            WriteEntry(
                archive,
                "_rels/.rels",
                $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="{relationshipNamespace}/officeDocument" Target="word/document.xml" />
                </Relationships>
                """
            );
            WriteEntry(
                archive,
                "word/document.xml",
                """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                            xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body>
                    <w:p w14:paraId="00112233"><w:r><w:t>Main</w:t><w:footnoteReference w:id="2"/><w:endnoteReference w:id="3"/></w:r></w:p>
                    <w:sectPr>
                      <w:headerReference r:id="rIdHeader" w:type="default"/>
                      <w:footerReference r:id="rIdFooter" w:type="default"/>
                    </w:sectPr>
                  </w:body>
                </w:document>
                """
            );
            WriteEntry(
                archive,
                "word/_rels/document.xml.rels",
                $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdHeader" Type="{relationshipNamespace}/header" Target="header1.xml" />
                  <Relationship Id="rIdFooter" Type="{relationshipNamespace}/footer" Target="footer1.xml" />
                  <Relationship Id="rIdFootnotes" Type="{relationshipNamespace}/footnotes" Target="footnotes.xml" />
                  <Relationship Id="rIdEndnotes" Type="{relationshipNamespace}/endnotes" Target="endnotes.xml" />
                  <Relationship Id="rIdComments" Type="{relationshipNamespace}/comments" Target="comments.xml" />
                  <Relationship Id="rIdGlossary" Type="{relationshipNamespace}/glossaryDocument" Target="glossary/document.xml" />
                </Relationships>
                """
            );
            WriteEntry(
                archive,
                "word/header1.xml",
                headerXml
                    ?? $"""
                    <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:p><w:r><w:t>{headerText}</w:t></w:r></w:p>
                    </w:hdr>
                    """
            );
            WriteEntry(
                archive,
                "word/footer1.xml",
                """
                <w:ftr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:p><w:r><w:t>Footer</w:t></w:r></w:p>
                </w:ftr>
                """
            );
            WriteEntry(
                archive,
                "word/footnotes.xml",
                """
                <w:footnotes xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:footnote w:id="2"><w:p><w:r><w:t>Footnote</w:t></w:r></w:p></w:footnote>
                </w:footnotes>
                """
            );
            WriteEntry(
                archive,
                "word/endnotes.xml",
                """
                <w:endnotes xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:endnote w:id="3"><w:p><w:r><w:t>Endnote</w:t></w:r></w:p></w:endnote>
                </w:endnotes>
                """
            );
            WriteEntry(
                archive,
                "word/comments.xml",
                """
                <w:comments xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:comment w:id="4" w:author="Ada" w:initials="A" w:date="2026-07-21T00:00:00Z"><w:p><w:r><w:t>Comment</w:t></w:r></w:p></w:comment>
                </w:comments>
                """
            );
            WriteEntry(
                archive,
                "word/glossary/document.xml",
                """
                <w:glossaryDocument xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:docParts><w:docPart>
                    <w:docPartPr><w:name w:val="Clause"/><w:guid w:val="{00112233-4455-6677-8899-AABBCCDDEEFF}"/></w:docPartPr>
                    <w:docPartBody><w:p><w:r><w:t>Glossary</w:t></w:r></w:p></w:docPartBody>
                  </w:docPart></w:docParts>
                </w:glossaryDocument>
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

    private static string RichDocumentXml() => """
        <w:document
            xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
            xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml"
            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
            xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math"
            xmlns:custom="urn:wordtoolkit:test">
          <w:body>
            <w:p w14:paraId="00112233">
              <w:pPr><w:pStyle w:val="Heading1" /></w:pPr>
              <w:hyperlink r:id="rId9"><w:r><w:t>Linked equation </w:t></w:r></w:hyperlink>
              <m:oMath><m:f><m:num><m:r><m:t>a</m:t></m:r></m:num><m:den><m:r><m:t>b</m:t></m:r></m:den></m:f></m:oMath>
              <custom:opaque custom:value="preserve"><custom:child /></custom:opaque>
            </w:p>
            <w:tbl><w:tr><w:tc><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
          </w:body>
        </w:document>
        """;

    private static string DocumentWithParagraphs(params string[] paragraphs) => $"""
        <w:document
            xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
            xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml">
          <w:body>{string.Join(string.Empty, paragraphs)}</w:body>
        </w:document>
        """;

    private static string Paragraph(string id, string text) =>
        $"<w:p w14:paraId=\"{id}\"><w:r><w:t>{text}</w:t></w:r></w:p>";
}
