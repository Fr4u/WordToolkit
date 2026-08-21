using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordNumberingSequenceRepairTests
{
    [Fact]
    public void RestartsOnlyTheSelectedTailThroughAClonedInstance()
    {
        using var bytes = BuildPackage(
            NumberingXml(
                """
                <w:abstractNum w:abstractNumId="1">
                  <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl>
                  <w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="lowerLetter"/><w:lvlText w:val="%1.%2"/></w:lvl>
                </w:abstractNum>
                <w:num w:numId="5"><w:abstractNumId w:val="1"/></w:num>
                <w:numIdMacAtCleanup w:val="5"/>
                """
            ),
            stylesXml:
                """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:style w:type="paragraph" w:styleId="TailList"><w:name w:val="Tail list"/><w:pPr><w:numPr><w:numId w:val="5"/></w:numPr></w:pPr></w:style>
                </w:styles>
                """,
            documentBody: string.Concat(
                Paragraph(5, 0, "before"),
                Paragraph(5, 0, "target"),
                StyledParagraph("TailList", "inherited"),
                Paragraph(5, 1, "nested")
            )
        );
        var package = ReadPackage(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var paragraphs = semantic.Nodes
            .Where(node => node.Kind == WordSemanticNodeKind.Paragraph)
            .ToArray();

        var plan = new WordNumberingSequenceRepairPlanner().PlanRestart(
            package,
            semantic,
            new WordNumberingSequenceRestartCommand(
                paragraphs[1].Id,
                ExpectedNumberId: 5,
                ExpectedLevelIndex: 0,
                StartValue: 7
            )
        );

        Assert.True(plan.Validation.Passed);
        Assert.Equal(5, plan.SourceNumberId);
        Assert.Equal(6, plan.NewNumberId);
        Assert.Equal(3, plan.AffectedParagraphs.Count);
        Assert.Equal(1, plan.DirectNumberingMaterializedCount);
        Assert.Equal(2, plan.ChangedParts.Count);
        Assert.Equal(7, plan.TargetCounterAfter);
        Assert.Equal(WordListCounterStatus.Exact, plan.TargetCounterStatusAfter);
        Assert.StartsWith("wnrplan_", plan.PlanId, StringComparison.Ordinal);
        Assert.DoesNotContain("target", JsonSerializer.Serialize(plan), StringComparison.Ordinal);
        Assert.DoesNotContain("inherited", JsonSerializer.Serialize(plan), StringComparison.Ordinal);

        var candidate = Apply(package, plan.CreateMutation(package));
        var graph = BuildSequence(candidate);
        Assert.Equal(
            new[] { (5, "1."), (6, "7."), (6, "8."), (6, "8.a") },
            graph.Items.Select(item => (item.NumberId, item.Label!)).ToArray()
        );

        var numberingPart = candidate.Parts["/word/numbering.xml"];
        var numbering = XDocument.Parse(Encoding.UTF8.GetString(numberingPart.Entry.Content.Span));
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var instances = numbering.Root!.Elements(w + "num").ToArray();
        Assert.Equal(2, instances.Length);
        Assert.Equal("1", instances.Single(item =>
            item.Attribute(w + "numId")?.Value == "5"
        ).Element(w + "abstractNumId")?.Attribute(w + "val")?.Value);
        Assert.Equal("6", numbering.Root.Element(w + "numIdMacAtCleanup")?
            .Attribute(w + "val")?.Value);

        var inverse = plan.CreateInverseMutation(candidate);
        var restored = Apply(candidate, inverse);
        Assert.Equal(package.Fingerprint, restored.Fingerprint);
    }

    [Fact]
    public void SynchronizesReplacementLevelStartForStandardAndQualifiedWordBehavior()
    {
        using var bytes = BuildPackage(
            NumberingXml(
                """
                <w:abstractNum w:abstractNumId="1"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl></w:abstractNum>
                <w:num w:numId="5"><w:abstractNumId w:val="1"/><w:lvlOverride w:ilvl="0"><w:startOverride w:val="3"/><w:lvl w:ilvl="0"><w:start w:val="9"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl></w:lvlOverride></w:num>
                """
            ),
            documentBody: Paragraph(5, 0, "before") + Paragraph(5, 0, "target")
        );
        var package = ReadPackage(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var target = semantic.Nodes.Where(node =>
            node.Kind == WordSemanticNodeKind.Paragraph
        ).ElementAt(1);

        var plan = new WordNumberingSequenceRepairPlanner().PlanRestart(
            package,
            semantic,
            new WordNumberingSequenceRestartCommand(target.Id, 5, 0, 4)
        );
        var candidate = Apply(package, plan.CreateMutation(package));
        var numberingXml = XDocument.Parse(
            Encoding.UTF8.GetString(
                candidate.Parts["/word/numbering.xml"].Entry.Content.Span
            )
        );
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var clone = numberingXml.Root!.Elements(w + "num").Single(item =>
            item.Attribute(w + "numId")?.Value == "6"
        );
        var levelOverride = clone.Elements(w + "lvlOverride").Single();

        Assert.Equal("4", levelOverride.Element(w + "startOverride")?
            .Attribute(w + "val")?.Value);
        Assert.Equal("4", levelOverride.Element(w + "lvl")?
            .Element(w + "start")?.Attribute(w + "val")?.Value);
        Assert.Contains(
            "replacement_level_start_synchronized_for_qualified_word_build",
            plan.CompatibilityRules
        );
        Assert.Equal(4, plan.TargetCounterAfter);
        Assert.Equal(
            new string?[] { "9.", "4." },
            BuildSequence(candidate).Items.Select(item => item.Label).ToArray()
        );
    }

    [Fact]
    public void RejectsStaleAmbiguousAndOversizedRepairs()
    {
        using var bytes = BuildPackage(
            NumberingXml(
                """
                <w:abstractNum w:abstractNumId="1"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl></w:abstractNum>
                <w:num w:numId="5"><w:abstractNumId w:val="1"/></w:num>
                """
            ),
            documentBody: Paragraph(5, 0, "target")
                + "<w:ins w:id=\"1\" w:author=\"A\">"
                + Paragraph(5, 0, "ambiguous")
                + "</w:ins>"
        );
        var package = ReadPackage(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var target = semantic.Nodes.First(node =>
            node.Kind == WordSemanticNodeKind.Paragraph
        );
        var planner = new WordNumberingSequenceRepairPlanner();

        Assert.Throws<WordSemanticPreconditionException>(() =>
            planner.PlanRestart(
                package,
                semantic,
                new WordNumberingSequenceRestartCommand(target.Id, 4, 0, 1)
            )
        );
        Assert.Throws<WordSemanticEditException>(() =>
            planner.PlanRestart(
                package,
                semantic,
                new WordNumberingSequenceRestartCommand(target.Id, 5, 0, 1)
            )
        );

        using var safeBytes = BuildPackage(
            NumberingXml(
                """
                <w:abstractNum w:abstractNumId="1"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl></w:abstractNum>
                <w:num w:numId="5"><w:abstractNumId w:val="1"/></w:num>
                """
            ),
            documentBody: Paragraph(5, 0, "one") + Paragraph(5, 0, "two")
        );
        var safePackage = ReadPackage(safeBytes);
        var safeSemantic = new WordSemanticProjector().Project(safePackage);
        var safeTarget = safeSemantic.Nodes.First(node =>
            node.Kind == WordSemanticNodeKind.Paragraph
        );
        Assert.Throws<WordSemanticTransactionLimitException>(() =>
            new WordNumberingSequenceRepairPlanner(
                new WordNumberingSequenceRepairOptions { MaxAffectedParagraphs = 1 }
            ).PlanRestart(
                safePackage,
                safeSemantic,
                new WordNumberingSequenceRestartCommand(safeTarget.Id, 5, 0, 1)
            )
        );

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            planner.PlanRestart(
                safePackage,
                safeSemantic,
                new WordNumberingSequenceRestartCommand(safeTarget.Id, 5, 0, 1),
                cancellation.Token
            )
        );
    }

    private static WordListSequenceGraph BuildSequence(OpcPackageSnapshot package)
    {
        var semantic = new WordSemanticProjector().Project(package);
        var styles = new WordStyleGraphBuilder().Build(package, semantic);
        var numbering = new WordNumberingGraphBuilder().Build(package, semantic, styles);
        return new WordListSequenceGraphBuilder().Build(
            package,
            semantic,
            styles,
            numbering
        );
    }

    private static OpcPackageSnapshot Apply(
        OpcPackageSnapshot package,
        OpcPackageMutationBuilder mutation
    )
    {
        using var output = new MemoryStream();
        new OpcPackageSerializer().Write(output, mutation);
        output.Position = 0;
        return new OpcPackageReader().Read(output);
    }

    private static OpcPackageSnapshot ReadPackage(Stream stream)
    {
        stream.Position = 0;
        return new OpcPackageReader().Read(stream);
    }

    private static MemoryStream BuildPackage(
        string numberingXml,
        string? stylesXml = null,
        string? documentBody = null
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var stylesOverride = stylesXml is null
                ? string.Empty
                : "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>";
            WriteEntry(
                archive,
                "[Content_Types].xml",
                $"""
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>{stylesOverride}</Types>
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
                $"""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>{documentBody ?? "<w:p/>"}</w:body></w:document>
                """
            );
            var styleRelationship = stylesXml is null
                ? string.Empty
                : "<Relationship Id=\"rIdStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>";
            WriteEntry(
                archive,
                "word/_rels/document.xml.rels",
                $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdNumbering" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>{styleRelationship}</Relationships>
                """
            );
            WriteEntry(archive, "word/numbering.xml", numberingXml);
            if (stylesXml is not null)
            {
                WriteEntry(archive, "word/styles.xml", stylesXml);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static string NumberingXml(string content) => $"""
        <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">{content}</w:numbering>
        """;

    private static string Paragraph(int numberId, int levelIndex, string text) => $"""
        <w:p><w:pPr><w:numPr><w:ilvl w:val="{levelIndex}"/><w:numId w:val="{numberId}"/></w:numPr></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>
        """;

    private static string StyledParagraph(string styleId, string text) => $"""
        <w:p><w:pPr><w:pStyle w:val="{styleId}"/></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>
        """;

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }
}
