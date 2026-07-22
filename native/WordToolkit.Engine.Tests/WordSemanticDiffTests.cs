using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordSemanticDiffTests
{
    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Word2010Namespace =
        "http://schemas.microsoft.com/office/word/2010/wordml";

    [Fact]
    public void IdenticalPackagesProduceDeterministicEmptyDiff()
    {
        var xml = DocumentXml(Paragraph("00112233", "unchanged"));
        using var beforeStream = BuildPackage(xml);
        using var afterStream = BuildPackage(xml);
        var (beforePackage, beforeDocument) = Read(beforeStream);
        var (afterPackage, afterDocument) = Read(afterStream);
        var engine = new WordSemanticDiffEngine();

        var first = engine.Compare(
            beforePackage,
            beforeDocument,
            afterPackage,
            afterDocument
        );
        var second = engine.Compare(
            beforePackage,
            beforeDocument,
            afterPackage,
            afterDocument
        );

        Assert.Equal(first.DiffId, second.DiffId);
        Assert.True(first.PackageEquivalent);
        Assert.True(first.SemanticallyEquivalent);
        Assert.True(first.MatchingComplete);
        Assert.Empty(first.EntryDifferences);
        Assert.Empty(first.SemanticDifferences);
        Assert.Equal(beforeDocument.NodeCount, first.MatchedNodeCount);
    }

    [Fact]
    public void DurableParagraphReportsTextAndDeclaredStyleChanges()
    {
        var beforeXml = DocumentXml(
            "<w:p w14:paraId='00112233'><w:pPr><w:pStyle w:val='BodyText'/></w:pPr>"
                + "<w:r><w:t>Alpha beta gamma.</w:t></w:r></w:p>"
        );
        var afterXml = DocumentXml(
            "<w:p w14:paraId='00112233'><w:pPr><w:pStyle w:val='Heading1'/></w:pPr>"
                + "<w:r><w:t>Alpha beta gamma delta.</w:t></w:r></w:p>"
        );

        var result = Compare(beforeXml, afterXml);
        var paragraph = Assert.Single(result.SemanticDifferences, difference =>
            difference.NodeKind == WordSemanticNodeKind.Paragraph
            && difference.Kinds.Contains(WordSemanticDifferenceKind.TextChanged)
            && difference.Kinds.Contains(WordSemanticDifferenceKind.PropertiesChanged)
        );

        Assert.Equal(WordSemanticMatchBasis.ExactNodeId, paragraph.MatchBasis);
        Assert.Equal(WordSemanticMatchConfidence.Exact, paragraph.MatchConfidence);
        Assert.Equal("Alpha beta gamma.", paragraph.Text!.Before!.CapturedText);
        Assert.Equal("Alpha beta gamma delta.", paragraph.Text.After!.CapturedText);
        var style = Assert.Single(paragraph.Properties);
        Assert.Equal("style_id", style.Name);
        Assert.Equal("BodyText", style.BeforeValue);
        Assert.Equal("Heading1", style.AfterValue);
    }

    [Fact]
    public void PureUnmodeledRunFormattingChangeIsNeverReportedAsEqual()
    {
        var beforeXml = DocumentXml(
            "<w:p w14:paraId='00112233'><w:r><w:rPr><w:b/></w:rPr>"
                + "<w:t>same text</w:t></w:r></w:p>"
        );
        var afterXml = DocumentXml(
            "<w:p w14:paraId='00112233'><w:r><w:rPr><w:i/></w:rPr>"
                + "<w:t>same text</w:t></w:r></w:p>"
        );

        var result = Compare(beforeXml, afterXml);

        Assert.False(result.SemanticallyEquivalent);
        Assert.Contains(result.SemanticDifferences, difference =>
            difference.NodeKind == WordSemanticNodeKind.Run
            && difference.Kinds.Contains(
                WordSemanticDifferenceKind.UnmodeledMarkupChanged
            )
        );
        Assert.Equal(0, result.TextChangedNodeCount);
    }

    [Fact]
    public void IgnoresOnlyWordRevisionSessionIdsNotExtensionAttributesWithSimilarNames()
    {
        var wordRsidBefore = DocumentXml(
            "<w:p w14:paraId='00112233' w:rsidR='00000001'><w:r><w:t>same</w:t></w:r></w:p>"
        );
        var wordRsidAfter = DocumentXml(
            "<w:p w14:paraId='00112233' w:rsidR='00000002'><w:r><w:t>same</w:t></w:r></w:p>"
        );
        var extensionBefore = DocumentXml(
            "<w:p w14:paraId='00112233'><w:r xmlns:x='urn:wordtoolkit:test' x:rsidOpaque='one'>"
                + "<w:t>same</w:t></w:r></w:p>"
        );
        var extensionAfter = DocumentXml(
            "<w:p w14:paraId='00112233'><w:r xmlns:x='urn:wordtoolkit:test' x:rsidOpaque='two'>"
                + "<w:t>same</w:t></w:r></w:p>"
        );

        var ignoredNoise = Compare(wordRsidBefore, wordRsidAfter);
        var preservedEvidence = Compare(extensionBefore, extensionAfter);

        Assert.True(ignoredNoise.SemanticallyEquivalent);
        Assert.False(preservedEvidence.SemanticallyEquivalent);
        Assert.Contains(preservedEvidence.SemanticDifferences, difference =>
            difference.NodeKind == WordSemanticNodeKind.Run
            && difference.Kinds.Contains(
                WordSemanticDifferenceKind.UnmodeledMarkupChanged
            )
        );
    }

    [Fact]
    public void DetectsOneTopLevelMoveWithoutTreatingIndexShiftAsMovement()
    {
        var beforeXml = DocumentXml(
            Paragraph("00000001", "A"),
            Paragraph("00000002", "B"),
            Paragraph("00000003", "C")
        );
        var afterXml = DocumentXml(
            Paragraph("00000003", "C"),
            Paragraph("00000001", "A"),
            Paragraph("00000002", "B")
        );

        var result = Compare(beforeXml, afterXml);
        var moved = Assert.Single(result.SemanticDifferences, difference =>
            difference.Kinds.Contains(WordSemanticDifferenceKind.Moved)
        );

        Assert.Equal(WordSemanticNodeKind.Paragraph, moved.NodeKind);
        Assert.Equal(2, moved.Before!.SiblingIndex);
        Assert.Equal(0, moved.After!.SiblingIndex);
        Assert.Equal(0, result.AddedNodeCount);
        Assert.Equal(0, result.RemovedNodeCount);
    }

    [Fact]
    public void ContextualAlignmentSeparatesEditedParagraphFromInsertion()
    {
        var beforeXml = DocumentXml(
            Paragraph("00000001", "anchor one"),
            "<w:p><w:r><w:t>The quick brown fox jumps over the tired dog.</w:t></w:r></w:p>",
            Paragraph("00000002", "anchor two")
        );
        var afterXml = DocumentXml(
            Paragraph("00000001", "anchor one"),
            "<w:p><w:r><w:t>Completely new preface.</w:t></w:r></w:p>",
            "<w:p><w:r><w:t>The quick brown fox jumps over the very tired dog.</w:t></w:r></w:p>",
            Paragraph("00000002", "anchor two")
        );

        var result = Compare(beforeXml, afterXml);
        var edited = Assert.Single(result.SemanticDifferences, difference =>
            difference.NodeKind == WordSemanticNodeKind.Paragraph
            && difference.Kinds.Contains(WordSemanticDifferenceKind.TextChanged)
        );

        Assert.Equal(WordSemanticMatchBasis.ContextualSimilarity, edited.MatchBasis);
        Assert.True(edited.MatchScore >= 0.7);
        Assert.Contains(result.SemanticDifferences, difference =>
            difference.NodeKind == WordSemanticNodeKind.Paragraph
            && difference.Kinds.SequenceEqual([WordSemanticDifferenceKind.Added])
            && difference.Text?.After?.CapturedText == "Completely new preface."
        );
    }

    [Fact]
    public void ComparesHeaderStoryWithoutInventingMainBodyChanges()
    {
        var document = DocumentXml(Paragraph("00000001", "main"), includeHeader: true);
        using var beforeStream = BuildPackage(
            document,
            HeaderXml(Paragraph("10000001", "header before"))
        );
        using var afterStream = BuildPackage(
            document,
            HeaderXml(Paragraph("10000001", "header after"))
        );
        var (beforePackage, beforeDocument) = Read(beforeStream);
        var (afterPackage, afterDocument) = Read(afterStream);

        var result = new WordSemanticDiffEngine().Compare(
            beforePackage,
            beforeDocument,
            afterPackage,
            afterDocument
        );

        var changed = Assert.Single(result.SemanticDifferences, difference =>
            difference.NodeKind == WordSemanticNodeKind.Paragraph
            && difference.Kinds.Contains(WordSemanticDifferenceKind.TextChanged)
        );
        Assert.Equal("Header", changed.Before!.ScopeFamily);
        Assert.Equal("/word/header1.xml", changed.Before.SourcePartUri);
        Assert.DoesNotContain(result.SemanticDifferences, difference =>
            difference.Before?.SourcePartUri == "/word/document.xml"
            && difference.Kinds.Contains(WordSemanticDifferenceKind.TextChanged)
        );
    }

    [Fact]
    public void SeparatesOpaquePackageChangeFromSemanticEquality()
    {
        var xml = DocumentXml(Paragraph("00112233", "same"));
        using var beforeStream = BuildPackage(xml, opaque: [1, 2, 3]);
        using var afterStream = BuildPackage(xml, opaque: [1, 2, 4]);
        var (beforePackage, beforeDocument) = Read(beforeStream);
        var (afterPackage, afterDocument) = Read(afterStream);

        var result = new WordSemanticDiffEngine().Compare(
            beforePackage,
            beforeDocument,
            afterPackage,
            afterDocument
        );

        Assert.False(result.PackageEquivalent);
        Assert.True(result.SemanticallyEquivalent);
        var entry = Assert.Single(result.EntryDifferences);
        Assert.Equal("custom/opaque.bin", entry.EntryName);
        Assert.False(entry.IsProjectedSemanticPart);
        Assert.Equal(0, result.UnclassifiedProjectedEntryCount);
    }

    [Fact]
    public void CountsChangedProjectedMarkupOutsideCurrentSemanticVocabulary()
    {
        var beforeXml = DocumentXml(
            Paragraph("00112233", "same"),
            documentAttributes: "w:conformance='strict'"
        );
        var afterXml = DocumentXml(
            Paragraph("00112233", "same"),
            documentAttributes: "w:conformance='transitional'"
        );

        var result = Compare(beforeXml, afterXml);

        Assert.False(result.PackageEquivalent);
        Assert.True(result.SemanticallyEquivalent);
        Assert.Equal(1, result.UnclassifiedProjectedEntryCount);
    }

    [Fact]
    public void IgnoredCaseAndWhitespaceDoNotReappearAsOpaqueMarkupChanges()
    {
        var beforeXml = DocumentXml(Paragraph("00112233", "Alpha   Beta"));
        var afterXml = DocumentXml(Paragraph("00112233", "alpha beta"));
        using var beforeStream = BuildPackage(beforeXml);
        using var afterStream = BuildPackage(afterXml);
        var (beforePackage, beforeDocument) = Read(beforeStream);
        var (afterPackage, afterDocument) = Read(afterStream);

        var exact = new WordSemanticDiffEngine().Compare(
            beforePackage,
            beforeDocument,
            afterPackage,
            afterDocument
        );
        var ignored = new WordSemanticDiffEngine(
            new WordSemanticDiffOptions
            {
                CaseSensitive = false,
                CompareWhitespace = false,
            }
        ).Compare(beforePackage, beforeDocument, afterPackage, afterDocument);

        Assert.True(exact.TextChangedNodeCount > 0);
        Assert.True(ignored.SemanticallyEquivalent);
        Assert.Equal(0, ignored.UnmodeledMarkupChangedNodeCount);
        Assert.False(ignored.PackageEquivalent);
    }

    [Fact]
    public void IgnoredDeclaredPropertiesDoNotReappearAsUnmodeledMarkup()
    {
        var beforeXml = DocumentXml(
            "<w:p w14:paraId='00112233'><w:pPr><w:pStyle w:val='BodyText'/></w:pPr>"
                + "<w:r><w:t>same text</w:t></w:r></w:p>"
        );
        var afterXml = DocumentXml(
            "<w:p w14:paraId='00112233'><w:pPr><w:pStyle w:val='Heading1'/></w:pPr>"
                + "<w:r><w:t>same text</w:t></w:r></w:p>"
        );
        using var beforeStream = BuildPackage(beforeXml);
        using var afterStream = BuildPackage(afterXml);
        var (beforePackage, beforeDocument) = Read(beforeStream);
        var (afterPackage, afterDocument) = Read(afterStream);

        var result = new WordSemanticDiffEngine(
            new WordSemanticDiffOptions { CompareProperties = false }
        ).Compare(beforePackage, beforeDocument, afterPackage, afterDocument);

        Assert.True(result.SemanticallyEquivalent);
        Assert.Equal(0, result.PropertiesChangedNodeCount);
        Assert.Equal(0, result.UnmodeledMarkupChangedNodeCount);
        Assert.False(result.PackageEquivalent);
    }

    [Fact]
    public void DisabledTextComparisonAlignsStructurallyEqualRepeatedParagraphsByOrder()
    {
        var beforeXml = DocumentXml(
            "<w:p><w:r><w:t>alpha one</w:t></w:r></w:p>",
            "<w:p><w:r><w:t>beta two</w:t></w:r></w:p>"
        );
        var afterXml = DocumentXml(
            "<w:p><w:r><w:t>unrelated first replacement</w:t></w:r></w:p>",
            "<w:p><w:r><w:t>unrelated second replacement</w:t></w:r></w:p>"
        );
        using var beforeStream = BuildPackage(beforeXml);
        using var afterStream = BuildPackage(afterXml);
        var (beforePackage, beforeDocument) = Read(beforeStream);
        var (afterPackage, afterDocument) = Read(afterStream);

        var result = new WordSemanticDiffEngine(
            new WordSemanticDiffOptions { CompareText = false }
        ).Compare(beforePackage, beforeDocument, afterPackage, afterDocument);

        Assert.True(result.SemanticallyEquivalent);
        Assert.True(result.MatchingComplete);
        Assert.Equal(0, result.AddedNodeCount);
        Assert.Equal(0, result.RemovedNodeCount);
        Assert.Equal(0, result.TextChangedNodeCount);
    }

    [Fact]
    public void StopsWhenReportableChangeLimitIsExceeded()
    {
        using var beforeStream = BuildPackage(DocumentXml());
        using var afterStream = BuildPackage(DocumentXml(
            Paragraph("00000001", "one"),
            Paragraph("00000002", "two"),
            Paragraph("00000003", "three")
        ));
        var (beforePackage, beforeDocument) = Read(beforeStream);
        var (afterPackage, afterDocument) = Read(afterStream);

        var exception = Assert.Throws<WordSemanticDiffLimitException>(() =>
            new WordSemanticDiffEngine(
                new WordSemanticDiffOptions { MaxChanges = 1 }
            ).Compare(beforePackage, beforeDocument, afterPackage, afterDocument)
        );

        Assert.Contains("more than 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StopsWhenAggregateTextProcessingBudgetIsExceeded()
    {
        using var beforeStream = BuildPackage(
            DocumentXml(Paragraph("00000001", "five"))
        );
        using var afterStream = BuildPackage(
            DocumentXml(Paragraph("00000001", "five"))
        );
        var (beforePackage, beforeDocument) = Read(beforeStream);
        var (afterPackage, afterDocument) = Read(afterStream);

        var exception = Assert.Throws<WordSemanticDiffLimitException>(() =>
            new WordSemanticDiffEngine(
                new WordSemanticDiffOptions
                {
                    MaxTotalTextCharactersProcessedPerDocument = 3,
                }
            ).Compare(beforePackage, beforeDocument, afterPackage, afterDocument)
        );

        Assert.Contains("text-processing budget", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsProjectionFromAnotherPackage()
    {
        using var firstStream = BuildPackage(DocumentXml(Paragraph("1", "first")));
        using var secondStream = BuildPackage(DocumentXml(Paragraph("2", "second")));
        var (firstPackage, firstDocument) = Read(firstStream);
        var (secondPackage, secondDocument) = Read(secondStream);

        var exception = Assert.Throws<WordSemanticDiffPreconditionException>(() =>
            new WordSemanticDiffEngine().Compare(
                firstPackage,
                secondDocument,
                secondPackage,
                secondDocument
            )
        );

        Assert.Contains("before", exception.Message, StringComparison.Ordinal);
        Assert.NotEqual(firstDocument.PackageFingerprint, secondDocument.PackageFingerprint);
    }

    [Fact]
    public void FallsBackExplicitlyWhenAlignmentBudgetIsExhausted()
    {
        var beforeParagraphs = Enumerable.Range(0, 12)
            .Select(index => $"<w:p><w:r><w:t>before sentence {index} alpha beta</w:t></w:r></w:p>")
            .ToArray();
        var afterParagraphs = Enumerable.Range(0, 12)
            .Select(index => $"<w:p><w:r><w:t>after sentence {index} alpha beta</w:t></w:r></w:p>")
            .ToArray();
        using var beforeStream = BuildPackage(DocumentXml(beforeParagraphs));
        using var afterStream = BuildPackage(DocumentXml(afterParagraphs));
        var (beforePackage, beforeDocument) = Read(beforeStream);
        var (afterPackage, afterDocument) = Read(afterStream);

        var result = new WordSemanticDiffEngine(
            new WordSemanticDiffOptions { MaxAlignmentCells = 20 }
        ).Compare(beforePackage, beforeDocument, afterPackage, afterDocument);

        Assert.False(result.MatchingComplete);
        Assert.True(result.AlignmentFallbackCount > 0);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "alignment_budget_fallback"
        );
    }

    [Fact]
    public void LeavesNearEqualContextualCandidatesUnmatchedInsteadOfGuessing()
    {
        var beforeXml = DocumentXml(
            "<w:p><w:r><w:t>Clause alpha baseline text.</w:t></w:r></w:p>",
            "<w:p><w:r><w:t>Clause alpha baseline text.</w:t></w:r></w:p>"
        );
        var afterXml = DocumentXml(
            "<w:p><w:r><w:t>Clause alpha revised text.</w:t></w:r></w:p>",
            "<w:p><w:r><w:t>Clause alpha revised text.</w:t></w:r></w:p>"
        );

        var result = Compare(beforeXml, afterXml);

        Assert.False(result.MatchingComplete);
        Assert.True(result.AmbiguousContextualMatchCount > 0);
        Assert.Equal(0, result.ContextualMatchCount);
        Assert.Equal(2, result.RemovedNodeCount);
        Assert.Equal(2, result.AddedNodeCount);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "ambiguous_contextual_match"
        );
    }

    [Fact]
    public void HonorsCancellationBeforeComparison()
    {
        using var stream = BuildPackage(DocumentXml(Paragraph("1", "text")));
        var (package, document) = Read(stream);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            new WordSemanticDiffEngine().Compare(
                package,
                document,
                package,
                document,
                cancellation.Token
            )
        );
    }

    [Fact]
    public void ProducesEmptyNoOpDiffForEveryBundledMultiProducerDocument()
    {
        var root = FindRepositoryRoot();
        var paths = new[]
        {
            Path.Combine(root, "examples"),
            Path.Combine(root, "tests", "upstream", "fixtures"),
            Path.Combine(root, "tests", "upstream", "fuzz", "corpus"),
        }
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(
                path,
                "*.docx",
                SearchOption.AllDirectories
            ))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.True(paths.Length >= 40, $"Expected a real corpus, found {paths.Length} files.");
        var reader = new OpcPackageReader();
        var projector = new WordSemanticProjector();
        var engine = new WordSemanticDiffEngine();

        foreach (var path in paths)
        {
            var package = reader.Read(path);
            var document = projector.Project(package);
            var result = engine.Compare(package, document, package, document);

            Assert.True(result.PackageEquivalent, path);
            Assert.True(result.SemanticallyEquivalent, path);
            Assert.Equal(document.NodeCount, result.MatchedNodeCount);
            Assert.Equal(0, result.AlignmentFallbackCount);
        }
    }

    [Fact]
    public void ComparesDifferentRealProducerDocumentsWithoutFlatteningThem()
    {
        var root = FindRepositoryRoot();
        var beforePath = Path.Combine(
            root,
            "tests",
            "upstream",
            "fixtures",
            "real_tracked_changes.docx"
        );
        var afterPath = Path.Combine(
            root,
            "tests",
            "upstream",
            "fixtures",
            "pandoc_track_move.docx"
        );
        var reader = new OpcPackageReader();
        var projector = new WordSemanticProjector();
        var beforePackage = reader.Read(beforePath);
        var afterPackage = reader.Read(afterPath);
        var beforeDocument = projector.Project(beforePackage);
        var afterDocument = projector.Project(afterPackage);

        var result = new WordSemanticDiffEngine().Compare(
            beforePackage,
            beforeDocument,
            afterPackage,
            afterDocument
        );

        Assert.False(result.PackageEquivalent);
        Assert.False(result.SemanticallyEquivalent);
        Assert.NotEmpty(result.EntryDifferences);
        Assert.NotEmpty(result.SemanticDifferences);
        Assert.StartsWith("wddiff_", result.DiffId, StringComparison.Ordinal);
        Assert.All(result.SemanticDifferences, difference =>
            Assert.DoesNotContain("<w:", difference.DifferenceId, StringComparison.Ordinal)
        );
    }

    private static WordSemanticDiffResult Compare(string beforeXml, string afterXml)
    {
        using var beforeStream = BuildPackage(beforeXml);
        using var afterStream = BuildPackage(afterXml);
        var (beforePackage, beforeDocument) = Read(beforeStream);
        var (afterPackage, afterDocument) = Read(afterStream);
        return new WordSemanticDiffEngine().Compare(
            beforePackage,
            beforeDocument,
            afterPackage,
            afterDocument
        );
    }

    private static (OpcPackageSnapshot Package, WordSemanticDocument Document) Read(
        MemoryStream stream
    )
    {
        var package = new OpcPackageReader().Read(stream);
        return (package, new WordSemanticProjector().Project(package));
    }

    private static string Paragraph(string paragraphId, string text) =>
        $"<w:p w14:paraId='{paragraphId}'><w:r><w:t>{text}</w:t></w:r></w:p>";

    private static string DocumentXml(
        params string[] body
    ) => DocumentXml(body, includeHeader: false, documentAttributes: null);

    private static string DocumentXml(
        string body,
        bool includeHeader = false,
        string? documentAttributes = null
    ) => DocumentXml([body], includeHeader, documentAttributes);

    private static string DocumentXml(
        IReadOnlyList<string> body,
        bool includeHeader = false,
        string? documentAttributes = null
    )
    {
        var section = includeHeader
            ? "<w:sectPr><w:headerReference w:type='default' r:id='rIdHeader'/></w:sectPr>"
            : string.Empty;
        return $"<w:document xmlns:w='{WordNamespace}' xmlns:w14='{Word2010Namespace}' "
            + $"xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships' "
            + $"{documentAttributes ?? string.Empty}><w:body>{string.Concat(body)}{section}"
            + "</w:body></w:document>";
    }

    private static string HeaderXml(string body) =>
        $"<w:hdr xmlns:w='{WordNamespace}' xmlns:w14='{Word2010Namespace}'>{body}</w:hdr>";

    private static MemoryStream BuildPackage(
        string documentXml,
        string? headerXml = null,
        byte[]? opaque = null
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml", ContentTypes(headerXml is not null, opaque is not null));
            Write(archive, "_rels/.rels", RootRelationships());
            Write(archive, "word/document.xml", documentXml);
            if (headerXml is not null)
            {
                Write(archive, "word/_rels/document.xml.rels", DocumentRelationships());
                Write(archive, "word/header1.xml", headerXml);
            }
            if (opaque is not null)
            {
                Write(archive, "custom/opaque.bin", opaque);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static void Write(ZipArchive archive, string name, string value) =>
        Write(archive, name, Encoding.UTF8.GetBytes(value));

    private static void Write(ZipArchive archive, string name, byte[] value)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var target = entry.Open();
        target.Write(value);
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
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static string ContentTypes(bool header, bool opaque) =>
        "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>"
        + "<Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/>"
        + "<Default Extension='xml' ContentType='application/xml'/>"
        + (opaque ? "<Default Extension='bin' ContentType='application/octet-stream'/>" : string.Empty)
        + "<Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/>"
        + (header ? "<Override PartName='/word/header1.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml'/>" : string.Empty)
        + "</Types>";

    private static string RootRelationships() =>
        "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
        + "<Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='word/document.xml'/>"
        + "</Relationships>";

    private static string DocumentRelationships() =>
        "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
        + "<Relationship Id='rIdHeader' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/header' Target='header1.xml'/>"
        + "</Relationships>";
}
