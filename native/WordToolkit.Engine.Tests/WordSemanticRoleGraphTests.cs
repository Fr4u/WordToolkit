using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordSemanticRoleGraphTests
{
    [Fact]
    public void SeparatesDeclaredStyleLexicalAndConflictingEvidence()
    {
        using var stream = PackageBytes(
            """
            <w:sdt><w:sdtPr><w:tag w:val="wordtoolkit:role=theorem"/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>Declared body</w:t></w:r></w:p></w:sdtContent></w:sdt>
            <w:p><w:pPr><w:pStyle w:val="Twierdzenie"/></w:pPr><w:r><w:t>Styled body</w:t></w:r></w:p>
            <w:p><w:r><w:t>Twierdzenie 2. Lexical body</w:t></w:r></w:p>
            <w:p><w:pPr><w:pStyle w:val="Definicja"/></w:pPr><w:r><w:t>Theorem 3. Conflict</w:t></w:r></w:p>
            <w:p><w:r><w:t>Theorematic prose is not a label.</w:t></w:r></w:p>
            """
        );

        var graph = Build(stream);

        Assert.Equal(5, graph.ExaminedParagraphCount);
        Assert.Equal(4, graph.Candidates.Count);
        var declared = graph.Candidates[0];
        Assert.Equal(WordSemanticRoleKind.Theorem, declared.Role);
        Assert.Equal(WordSemanticRoleClassification.Declared, declared.Classification);
        Assert.True(declared.UsableAsSemanticRole);
        Assert.Contains(declared.Evidence, item =>
            item.Kind == WordSemanticRoleEvidenceKind.ContentControlTag
            && item.AuthorDeclared
        );

        var styled = graph.Candidates[1];
        Assert.Equal(WordSemanticRoleKind.Theorem, styled.Role);
        Assert.Equal(
            WordSemanticRoleClassification.StyleConvention,
            styled.Classification
        );
        Assert.All(styled.Evidence, item => Assert.False(item.AuthorDeclared));

        var lexical = graph.Candidates[2];
        Assert.Equal(WordSemanticRoleKind.Theorem, lexical.Role);
        Assert.Equal(
            WordSemanticRoleClassification.LexicalCandidate,
            lexical.Classification
        );
        Assert.Single(lexical.Evidence);
        Assert.Equal(
            WordSemanticRoleEvidenceKind.LexicalLabel,
            lexical.Evidence[0].Kind
        );

        var conflict = graph.Candidates[3];
        Assert.Null(conflict.Role);
        Assert.Equal(WordSemanticRoleClassification.Conflicting, conflict.Classification);
        Assert.False(conflict.UsableAsSemanticRole);
        Assert.Contains(graph.Issues, issue =>
            issue.Code == "SEMANTIC_ROLE_CONFLICT"
            && issue.CandidateId == conflict.Id
        );
        Assert.False(graph.ModeledEvidenceCoverageComplete);
        Assert.DoesNotContain(graph.Candidates, item => item.SourceOrder == 21);
    }

    [Fact]
    public void RevisionAncestryKeepsCandidateButMakesItsViewAmbiguous()
    {
        using var stream = PackageBytes(
            """
            <w:ins w:id="1" w:author="A"><w:p><w:r><w:t>Theorem 1. Changed</w:t></w:r></w:p></w:ins>
            """
        );

        var graph = Build(stream);

        var candidate = Assert.Single(graph.Candidates);
        Assert.True(candidate.ViewAmbiguous);
        Assert.False(candidate.UsableAsSemanticRole);
        Assert.Equal(1, graph.AmbiguousParagraphCount);
        Assert.Contains(graph.Issues, issue =>
            issue.Code == "SEMANTIC_ROLE_VIEW_AMBIGUOUS"
        );
        Assert.False(graph.ModeledEvidenceCoverageComplete);
    }

    [Fact]
    public void InlineContentControlDoesNotDeclareTheRoleOfItsContainingParagraph()
    {
        using var stream = PackageBytes(
            """
            <w:p><w:r><w:t>Ordinary prefix </w:t></w:r><w:sdt><w:sdtPr><w:tag w:val="wordtoolkit:role=theorem"/></w:sdtPr><w:sdtContent><w:r><w:t>inline fragment</w:t></w:r></w:sdtContent></w:sdt></w:p>
            """
        );

        var graph = Build(stream);

        Assert.Empty(graph.Candidates);
        Assert.True(graph.ModeledEvidenceCoverageComplete);
    }

    [Fact]
    public void InheritedRoleStyleIdIsEvidenceForAnExplicitDerivedStyle()
    {
        using var stream = PackageBytes(
            """
            <w:p><w:pPr><w:pStyle w:val="DerivedTheorem"/></w:pPr><w:r><w:t>Body without a lexical label</w:t></w:r></w:p>
            """
        );

        var candidate = Assert.Single(Build(stream).Candidates);

        Assert.Equal(WordSemanticRoleKind.Theorem, candidate.Role);
        Assert.Equal(
            WordSemanticRoleClassification.StyleConvention,
            candidate.Classification
        );
        Assert.Contains(candidate.Evidence, item =>
            item.Kind == WordSemanticRoleEvidenceKind.InheritedStyleId
            && item.StyleId == "Theorem"
        );
    }

    [Fact]
    public void DefaultParagraphStyleIsNotSemanticRoleEvidence()
    {
        using var stream = PackageBytes(
            "<w:p><w:r><w:t>Ordinary body without a role label</w:t></w:r></w:p>",
            """
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:style w:type="paragraph" w:default="1" w:styleId="Theorem"><w:name w:val="Twierdzenie"/></w:style></w:styles>
            """
        );

        var graph = Build(stream);

        Assert.Empty(graph.Candidates);
        Assert.True(graph.ModeledEvidenceCoverageComplete);
    }

    [Fact]
    public void UnresolvedStylesMakeCoverageIncompleteAndRespectTheIssueLimit()
    {
        using var oneStream = PackageBytes(
            "<w:p><w:pPr><w:pStyle w:val=\"MissingOne\"/></w:pPr><w:r><w:t>Body</w:t></w:r></w:p>"
        );
        var onePackage = new OpcPackageReader().Read(oneStream);
        var oneSemantic = new WordSemanticProjector().Project(onePackage);
        var oneStyles = new WordStyleGraphBuilder().Build(onePackage, oneSemantic);
        var oneControls = new WordContentControlBindingGraphBuilder().Build(
            onePackage,
            oneSemantic
        );

        var graph = new WordSemanticRoleGraphBuilder().Build(
            onePackage,
            oneSemantic,
            oneStyles,
            oneControls
        );

        Assert.Empty(graph.Candidates);
        Assert.False(graph.ModeledEvidenceCoverageComplete);
        Assert.Contains(graph.Issues, issue =>
            issue.Code == "SEMANTIC_ROLE_STYLE_UNRESOLVED"
        );

        using var twoStream = PackageBytes(
            """
            <w:p><w:pPr><w:pStyle w:val="MissingOne"/></w:pPr><w:r><w:t>First</w:t></w:r></w:p>
            <w:p><w:pPr><w:pStyle w:val="MissingTwo"/></w:pPr><w:r><w:t>Second</w:t></w:r></w:p>
            """
        );
        var twoPackage = new OpcPackageReader().Read(twoStream);
        var twoSemantic = new WordSemanticProjector().Project(twoPackage);
        var twoStyles = new WordStyleGraphBuilder().Build(twoPackage, twoSemantic);
        var twoControls = new WordContentControlBindingGraphBuilder().Build(
            twoPackage,
            twoSemantic
        );

        Assert.Throws<WordSemanticRoleLimitException>(() =>
            new WordSemanticRoleGraphBuilder(
                new WordSemanticRoleGraphOptions { MaxIssues = 1 }
            ).Build(twoPackage, twoSemantic, twoStyles, twoControls)
        );
    }

    [Theory]
    [InlineData("Theorem 1.", WordSemanticRoleKind.Theorem)]
    [InlineData("Lemat 1.", WordSemanticRoleKind.Lemma)]
    [InlineData("Definition: value", WordSemanticRoleKind.Definition)]
    [InlineData("Dowód. text", WordSemanticRoleKind.Proof)]
    [InlineData("Przykład 2", WordSemanticRoleKind.Example)]
    [InlineData("Aksjomat", WordSemanticRoleKind.Axiom)]
    public void ConservativeProfileRecognizesClosedPolishEnglishLabels(
        string text,
        WordSemanticRoleKind role
    )
    {
        using var stream = PackageBytes(
            $"<w:p><w:r><w:t>{text}</w:t></w:r></w:p>"
        );

        var candidate = Assert.Single(Build(stream).Candidates);

        Assert.Equal(role, candidate.Role);
        Assert.Equal(
            WordSemanticRoleClassification.LexicalCandidate,
            candidate.Classification
        );
    }

    [Fact]
    public void CandidateIdentitiesAreDeterministicAndContentBound()
    {
        using var firstStream = PackageBytes(
            "<w:p><w:r><w:t>Theorem 1. First</w:t></w:r></w:p>"
        );
        using var secondStream = PackageBytes(
            "<w:p><w:r><w:t>Theorem 1. First</w:t></w:r></w:p>"
        );
        using var changedStream = PackageBytes(
            "<w:p><w:r><w:t>Theorem 1. Changed</w:t></w:r></w:p>"
        );

        var first = Assert.Single(Build(firstStream).Candidates);
        var second = Assert.Single(Build(secondStream).Candidates);
        var changed = Assert.Single(Build(changedStream).Candidates);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.NotEqual(first.Id, changed.Id);
        Assert.NotEqual(first.Fingerprint, changed.Fingerprint);
        Assert.NotEqual(first.ParagraphTextFingerprint, changed.ParagraphTextFingerprint);
    }

    [Fact]
    public void ParagraphTextLimitFailsClosedInsteadOfClassifyingAPrefix()
    {
        using var stream = PackageBytes(
            "<w:p><w:r><w:t>Theorem 1. Long content</w:t></w:r></w:p>"
        );
        var package = new OpcPackageReader().Read(stream);
        var semantic = new WordSemanticProjector().Project(package);
        var styles = new WordStyleGraphBuilder().Build(package, semantic);
        var controls = new WordContentControlBindingGraphBuilder().Build(package, semantic);

        var graph = new WordSemanticRoleGraphBuilder(
            new WordSemanticRoleGraphOptions { MaxParagraphTextCharacters = 8 }
        ).Build(package, semantic, styles, controls);

        Assert.Empty(graph.Candidates);
        Assert.Contains(graph.Issues, issue => issue.Code == "SEMANTIC_ROLE_TEXT_LIMIT");
        Assert.False(graph.ModeledEvidenceCoverageComplete);
    }

    private static WordSemanticRoleGraph Build(MemoryStream stream)
    {
        stream.Position = 0;
        var package = new OpcPackageReader().Read(stream);
        var semantic = new WordSemanticProjector().Project(package);
        var styles = new WordStyleGraphBuilder().Build(package, semantic);
        var controls = new WordContentControlBindingGraphBuilder().Build(package, semantic);
        return new WordSemanticRoleGraphBuilder().Build(
            package,
            semantic,
            styles,
            controls
        );
    }

    private static MemoryStream PackageBytes(string body, string? stylesXml = null)
    {
        var stream = new MemoryStream();
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
                $"<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>{body}</w:body></w:document>"
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
                stylesXml ?? """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style><w:style w:type="paragraph" w:styleId="Twierdzenie"><w:name w:val="Theorem"/></w:style><w:style w:type="paragraph" w:styleId="Theorem"><w:name w:val="RoleBase"/></w:style><w:style w:type="paragraph" w:styleId="DerivedTheorem"><w:name w:val="Derived Role"/><w:basedOn w:val="Theorem"/></w:style><w:style w:type="paragraph" w:styleId="Definicja"><w:name w:val="Definition"/></w:style></w:styles>
                """
            );
        }
        stream.Position = 0;
        return stream;
    }

    private static void Add(ZipArchive archive, string name, string text)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var output = entry.Open();
        output.Write(Encoding.UTF8.GetBytes(text));
    }
}
