using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordReferenceGraphTests
{
    [Fact]
    public void PairsCrossParagraphBookmarkAndComplexRefWithSourceLinks()
    {
        using var bytes = BuildPackage(
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p>
                  <w:bookmarkStart w:id="7" w:name="Target"/>
                  <w:r><w:t>Alpha</w:t></w:r>
                </w:p>
                <w:p>
                  <w:bookmarkEnd w:id="7"/>
                  <w:r><w:fldChar w:fldCharType="begin" w:dirty="1"/></w:r>
                  <w:r><w:instrText xml:space="preserve"> REF target \h </w:instrText></w:r>
                </w:p>
                <w:p>
                  <w:r><w:fldChar w:fldCharType="separate"/></w:r>
                  <w:r><w:t>Alpha</w:t></w:r>
                  <w:r><w:fldChar w:fldCharType="end"/></w:r>
                </w:p>
              </w:body>
            </w:document>
            """
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordReferenceGraphBuilder().Build(package, semantic);

        var bookmark = Assert.Single(graph.Bookmarks);
        Assert.True(bookmark.IsComplete);
        Assert.True(bookmark.IsEffectiveByName);
        Assert.NotNull(bookmark.StartNodeId);
        Assert.NotNull(bookmark.EndNodeId);
        Assert.True(graph.TryGetEffectiveBookmark("TARGET", out var found));
        Assert.Equal(bookmark.Id, found!.Id);

        var field = Assert.Single(graph.Fields);
        Assert.Equal(WordFieldKind.Complex, field.Kind);
        Assert.Equal(WordFieldStatus.Complete, field.Status);
        Assert.Equal("REF", field.FieldType);
        Assert.Equal(WordFieldClassification.DocumentReference, field.Classification);
        Assert.True(field.HasSeparator);
        Assert.True(field.IsDirty);
        Assert.NotNull(field.StartNodeId);
        Assert.NotNull(field.SeparatorNodeId);
        Assert.NotNull(field.EndNodeId);
        Assert.Contains("Alpha", field.ResultText, StringComparison.Ordinal);

        var edge = Assert.Single(graph.Edges);
        Assert.Equal(WordReferenceTargetKind.Bookmark, edge.TargetKind);
        Assert.True(edge.IsResolved);
        Assert.Equal(bookmark.Id, edge.ResolvedBookmarkId);
    }

    [Fact]
    public void KeepsSimpleAndNestedComplexFieldsAsAParentChildGraph()
    {
        using var bytes = BuildPackage(
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p>
                  <w:bookmarkStart w:id="1" w:name="Dest"/>
                  <w:r><w:t>Destination</w:t></w:r>
                  <w:bookmarkEnd w:id="1"/>
                </w:p>
                <w:p>
                  <w:fldSimple w:instr=" HYPERLINK \l &quot;dest&quot; " w:dirty="true">
                    <w:r><w:fldChar w:fldCharType="begin"/></w:r>
                    <w:r><w:instrText xml:space="preserve"> REF Dest </w:instrText></w:r>
                    <w:r><w:fldChar w:fldCharType="separate"/></w:r>
                    <w:r><w:t>Destination</w:t></w:r>
                    <w:r><w:fldChar w:fldCharType="end"/></w:r>
                  </w:fldSimple>
                </w:p>
              </w:body>
            </w:document>
            """
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordReferenceGraphBuilder().Build(package, semantic);

        Assert.Equal(2, graph.Fields.Count);
        var simple = graph.Fields.Single(field => field.Kind == WordFieldKind.Simple);
        var complex = graph.Fields.Single(field => field.Kind == WordFieldKind.Complex);
        Assert.Equal(simple.Id, complex.ParentFieldId);
        Assert.Equal(new[] { complex.Id }, simple.ChildFieldIds);
        Assert.Equal("HYPERLINK", simple.FieldType);
        Assert.Equal("REF", complex.FieldType);
        Assert.Equal("Destination", simple.ResultText);
        Assert.Equal("Destination", complex.ResultText);
        Assert.Equal(2, graph.Edges.Count);
        Assert.All(graph.Edges, edge => Assert.True(edge.IsResolved));
    }

    [Fact]
    public void DoesNotPairFieldsAcrossStoryBoundaries()
    {
        using var bytes = BuildPackage(
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body><w:p><w:r><w:fldChar w:fldCharType="end"/></w:r></w:p></w:body>
            </w:document>
            """,
            """
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:p>
                <w:r><w:fldChar w:fldCharType="begin"/></w:r>
                <w:r><w:instrText> PAGE </w:instrText></w:r>
              </w:p>
            </w:hdr>
            """
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordReferenceGraphBuilder().Build(package, semantic);

        var field = Assert.Single(graph.Fields);
        Assert.Equal(WordFieldStatus.MissingEnd, field.Status);
        Assert.Equal("/word/header1.xml", field.PartUri);
        Assert.Contains(graph.Issues, issue => issue.Code == "FIELD_END_ORPHAN");
        Assert.Contains(graph.Issues, issue => issue.Code == "FIELD_END_MISSING");
        Assert.Equal(2, graph.Stories.Count);
    }

    [Fact]
    public void ReportsDuplicateNamesMalformedRangesAndInstructionsDeterministically()
    {
        using var bytes = BuildPackage(
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body><w:p>
                <w:bookmarkStart w:id="1" w:name="Same"/>
                <w:bookmarkEnd w:id="1"/>
                <w:bookmarkStart w:id="2" w:name="same"/>
                <w:bookmarkEnd w:id="99"/>
                <w:r><w:fldChar w:fldCharType="begin"/></w:r>
                <w:r><w:instrText> REF &quot;unterminated </w:instrText></w:r>
              </w:p></w:body>
            </w:document>
            """
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordReferenceGraphBuilder().Build(package, semantic);

        Assert.Contains(graph.Issues, issue => issue.Code == "BOOKMARK_DUPLICATE_NAME");
        Assert.Contains(graph.Issues, issue => issue.Code == "BOOKMARK_END_ORPHAN");
        Assert.Contains(graph.Issues, issue => issue.Code == "BOOKMARK_END_MISSING");
        Assert.Contains(graph.Issues, issue => issue.Code == "FIELD_END_MISSING");
        Assert.Contains(graph.Issues, issue => issue.Code == "FIELD_UNTERMINATED_QUOTE");
        var field = Assert.Single(graph.Fields);
        Assert.Equal(WordFieldStatus.MissingEnd, field.Status);
        Assert.False(field.InstructionParseComplete);
        Assert.True(graph.Bookmarks.Single(bookmark => bookmark.Name == "same").IsEffectiveByName);
    }

    [Fact]
    public void ClassifiesExternalFieldsWithoutExecutingOrResolvingTheirTargets()
    {
        using var bytes = BuildPackage(
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body><w:p>
                <w:r><w:fldChar w:fldCharType="begin"/></w:r>
                <w:r><w:instrText xml:space="preserve"> DDEAUTO calc.exe &quot;private-file&quot; topic </w:instrText></w:r>
                <w:r><w:fldChar w:fldCharType="end"/></w:r>
              </w:p></w:body>
            </w:document>
            """
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordReferenceGraphBuilder().Build(package, semantic);

        var field = Assert.Single(graph.Fields);
        Assert.Equal(WordFieldClassification.ExternalContent, field.Classification);
        Assert.True(field.RequiresExternalAccess);
        Assert.True(field.MayInvokeApplication);
        var edge = Assert.Single(graph.Edges);
        Assert.True(edge.IsExternal);
        Assert.False(edge.IsResolved);
        Assert.Equal(WordReferenceTargetKind.ExternalResource, edge.TargetKind);
    }

    [Fact]
    public void ReadsWordGeneratedTableOfAuthoritiesEntryFromItsLongCitationSwitch()
    {
        using var bytes = BuildPackage(
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body><w:p>
                <w:fldSimple w:instr=" TA \l &quot;Alpha przeciwko Beta&quot; \s &quot;Alpha v. Beta&quot; \c 1 ">
                  <w:r><w:t/></w:r>
                </w:fldSimple>
              </w:p></w:body>
            </w:document>
            """
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordReferenceGraphBuilder().Build(package, semantic);

        var field = Assert.Single(graph.Fields);
        Assert.Equal("TA", field.FieldType);
        Assert.Equal(WordFieldClassification.Index, field.Classification);
        var edge = Assert.Single(graph.Edges);
        Assert.Equal(WordReferenceEdgeKind.Generates, edge.Kind);
        Assert.Equal(WordReferenceTargetKind.IndexEntry, edge.TargetKind);
        Assert.Equal("Alpha przeciwko Beta", edge.TargetKey);
        Assert.DoesNotContain(graph.Issues, issue => issue.Code == "FIELD_TARGET_MISSING");
    }

    [Fact]
    public void ResolvesAuthorityEntryAgainstMatchingTableOfAuthoritiesCategory()
    {
        using var bytes = BuildPackage(
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p><w:fldSimple w:instr=" TA \l &quot;Alpha przeciwko Beta&quot; \c 3 "><w:r><w:t/></w:r></w:fldSimple></w:p>
                <w:p><w:fldSimple w:instr=" TOA \c &quot;3&quot; "><w:r><w:t>Alpha przeciwko Beta 1</w:t></w:r></w:fldSimple></w:p>
              </w:body>
            </w:document>
            """
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordReferenceGraphBuilder().Build(package, semantic);

        var edge = Assert.Single(graph.Edges);
        Assert.True(edge.IsResolved);
        Assert.Equal(WordReferenceTargetKind.IndexEntry, edge.TargetKind);
        Assert.Equal("Alpha przeciwko Beta", edge.TargetKey);
    }

    [Fact]
    public void ResolvesNativeIndexEntryAgainstCompleteIndexField()
    {
        using var bytes = BuildPackage(
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p><w:fldSimple w:instr=" XE &quot;Analiza:Całki&quot; "><w:r><w:t/></w:r></w:fldSimple></w:p>
                <w:p><w:fldSimple w:instr=" INDEX \\h &quot;A&quot; "><w:r><w:t>Analiza, 1</w:t></w:r></w:fldSimple></w:p>
              </w:body>
            </w:document>
            """
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordReferenceGraphBuilder().Build(package, semantic);

        Assert.Equal(2, graph.Fields.Count);
        var edge = Assert.Single(graph.Edges);
        Assert.True(edge.IsResolved);
        Assert.Equal(WordReferenceEdgeKind.Generates, edge.Kind);
        Assert.Equal(WordReferenceTargetKind.IndexEntry, edge.TargetKind);
        Assert.Equal("Analiza:Całki", edge.TargetKey);
    }

    [Fact]
    public void ResolvesIndexEntryOnlyAgainstMatchingIndexType()
    {
        using var bytes = BuildPackage(
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p><w:fldSimple w:instr=" XE &quot;Analiza&quot; \f &quot;A&quot; "><w:r><w:t/></w:r></w:fldSimple></w:p>
                <w:p><w:fldSimple w:instr=" INDEX \f &quot;B&quot; "><w:r><w:t>Inny indeks</w:t></w:r></w:fldSimple></w:p>
              </w:body>
            </w:document>
            """
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordReferenceGraphBuilder().Build(package, semantic);

        var edge = Assert.Single(graph.Edges);
        Assert.False(edge.IsResolved);
        Assert.Equal(WordReferenceTargetKind.IndexEntry, edge.TargetKind);
    }

    [Fact]
    public void DoesNotResolveIndexEntryAgainstDeletedIndexField()
    {
        using var bytes = BuildPackage(
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p><w:fldSimple w:instr=" XE &quot;Analiza&quot; "><w:r><w:t/></w:r></w:fldSimple></w:p>
                <w:del w:id="1" w:author="x"><w:p><w:fldSimple w:instr=" INDEX "><w:r><w:delText>Analiza, 1</w:delText></w:r></w:fldSimple></w:p></w:del>
              </w:body>
            </w:document>
            """
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordReferenceGraphBuilder().Build(package, semantic);

        var edge = Assert.Single(graph.Edges);
        Assert.False(edge.IsResolved);
    }

    [Fact]
    public void AcceptsStrictBookmarkAndSimpleRefMarkup()
    {
        using var bytes = BuildPackage(
            """
            <w:document xmlns:w="http://purl.oclc.org/ooxml/wordprocessingml/main">
              <w:body><w:p>
                <w:bookmarkStart w:id="4" w:name="StrictName"/>
                <w:r><w:t>Strict</w:t></w:r>
                <w:bookmarkEnd w:id="4"/>
                <w:fldSimple w:instr=" REF strictname "><w:r><w:t>Strict</w:t></w:r></w:fldSimple>
              </w:p></w:body>
            </w:document>
            """
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordReferenceGraphBuilder().Build(package, semantic);

        Assert.True(Assert.Single(graph.Bookmarks).IsComplete);
        Assert.Equal("REF", Assert.Single(graph.Fields).FieldType);
        Assert.True(Assert.Single(graph.Edges).IsResolved);
    }

    [Fact]
    public void EnforcesConfiguredFieldAndInstructionLimits()
    {
        using var bytes = BuildPackage(
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body><w:p>
                <w:fldSimple w:instr=" PAGE "><w:r><w:t>1</w:t></w:r></w:fldSimple>
                <w:fldSimple w:instr=" NUMPAGES "><w:r><w:t>2</w:t></w:r></w:fldSimple>
              </w:p></w:body>
            </w:document>
            """
        );
        var (package, semantic) = ReadSnapshots(bytes);

        Assert.Throws<WordReferenceLimitException>(() =>
            new WordReferenceGraphBuilder(
                new WordReferenceGraphOptions { MaxFields = 1 }
            ).Build(package, semantic)
        );
        Assert.Throws<WordReferenceLimitException>(() =>
            new WordReferenceGraphBuilder(
                new WordReferenceGraphOptions
                {
                    MaxInstructionCharactersPerField = 4,
                }
            ).Build(package, semantic)
        );
    }

    [Fact]
    public void CapsDiagnosticFloodWithoutDiscardingTheGraph()
    {
        using var bytes = BuildPackage(
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body><w:p>
                <w:bookmarkEnd w:id="99"/>
                <w:r><w:instrText>orphan one</w:instrText></w:r>
                <w:r><w:fldChar w:fldCharType="separate"/></w:r>
              </w:p></w:body>
            </w:document>
            """
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordReferenceGraphBuilder(
            new WordReferenceGraphOptions { MaxIssues = 1 }
        ).Build(package, semantic);

        Assert.Single(graph.Issues);
        Assert.True(graph.IssuesTruncated);
        Assert.Empty(graph.Fields);
    }

    [Fact]
    public void BuildsReferenceGraphsForEveryBundledDocxFixture()
    {
        var fixtureDirectory = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "upstream",
            "fixtures"
        );
        var paths = Directory.EnumerateFiles(fixtureDirectory, "*.docx").ToArray();
        Assert.NotEmpty(paths);
        var reader = new OpcPackageReader();
        var fieldCount = 0;
        var bookmarkCount = 0;
        foreach (var path in paths)
        {
            var package = reader.Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            var graph = new WordReferenceGraphBuilder().Build(package, semantic);
            Assert.Equal(package.Fingerprint, graph.PackageFingerprint);
            fieldCount += graph.Fields.Count;
            bookmarkCount += graph.Bookmarks.Count;
        }

        Assert.True(fieldCount >= 60, $"Expected at least 60 fields, found {fieldCount}.");
        Assert.True(
            bookmarkCount >= 60,
            $"Expected at least 60 bookmarks, found {bookmarkCount}."
        );
    }

    private static (
        OpcPackageSnapshot Package,
        WordSemanticDocument Semantic
    ) ReadSnapshots(Stream bytes)
    {
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        return (package, semantic);
    }

    private static MemoryStream BuildPackage(
        string documentXml,
        string? headerXml = null
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                "[Content_Types].xml",
                $"""
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  {(headerXml is null ? string.Empty : "<Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/>")}
                </Types>
                """
            );
            WriteEntry(
                archive,
                "_rels/.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """
            );
            WriteEntry(archive, "word/document.xml", documentXml);
            if (headerXml is not null)
            {
                WriteEntry(
                    archive,
                    "word/_rels/document.xml.rels",
                    """
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                      <Relationship Id="rIdHeader" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
                    </Relationships>
                    """
                );
                WriteEntry(archive, "word/header1.xml", headerXml);
            }
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

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "pyproject.toml")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate the WordToolkit repository root."
        );
    }
}
