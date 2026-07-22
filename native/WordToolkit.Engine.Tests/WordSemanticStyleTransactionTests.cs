using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordSemanticStyleTransactionTests
{
    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void PlansParagraphRunAndTableStylesAsOneLosslessReversibleTransaction()
    {
        const string documentXml = """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body data-keep="yes"><!--opaque--><w:p custom="p"><w:pPr><w:pStyle w:val='OldPara'/><w:keep/></w:pPr><w:r custom="r"><w:rPr/><w:t>term</w:t></w:r></w:p><w:tbl custom="t"><w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl></w:body></w:document>
            """;
        using var stream = BuildPackage(documentXml, StylesXml(), [1, 3, 3, 7]);
        var reader = new OpcPackageReader();
        var package = reader.Read(stream);
        var semantic = new WordSemanticProjector().Project(package);
        var paragraph = semantic.Nodes.First(node =>
            node.Kind == WordSemanticNodeKind.Paragraph
            && node.Properties.TryGetValue("style_id", out var styleId)
            && styleId == "OldPara"
        );
        var run = semantic.Nodes.First(node => node.Kind == WordSemanticNodeKind.Run);
        var table = semantic.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Table);
        var commands = new[]
        {
            new WordStyleAssignmentCommand(
                paragraph.Id,
                "Definition",
                ExpectedStyleId: "OldPara"
            ),
            new WordStyleAssignmentCommand(
                run.Id,
                "Emphasis",
                RequireNoExplicitStyle: true
            ),
            new WordStyleAssignmentCommand(
                table.Id,
                "Grid",
                RequireNoExplicitStyle: true
            ),
        };
        var planner = new WordSemanticTransactionPlanner();

        var plan = planner.PlanStyleAssignments(package, semantic, commands);
        var repeated = planner.PlanStyleAssignments(package, semantic, commands);

        Assert.StartsWith("wseplan_", plan.PlanId, StringComparison.Ordinal);
        Assert.Equal(plan.PlanId, repeated.PlanId);
        Assert.True(plan.HasChanges);
        Assert.Equal(3, plan.OperationCount);
        Assert.Equal(3, plan.ChangedOperationCount);
        Assert.Equal(1, plan.ChangedPartCount);
        Assert.All(plan.Operations, operation =>
        {
            Assert.Equal("set_style", operation.Kind);
            Assert.Equal("style_id", operation.PropertyName);
            Assert.True(operation.HasChange);
        });

        using var appliedStream = Serialize(plan.CreateMutation(package));
        var applied = reader.Read(appliedStream);
        Assert.Equal(plan.ResultPackageFingerprint, applied.Fingerprint);
        var changedSemantic = new WordSemanticProjector().Project(applied);
        Assert.Contains(changedSemantic.Nodes, node =>
            node.Kind == WordSemanticNodeKind.Paragraph
            && node.Properties.GetValueOrDefault("style_id") == "Definition"
        );
        Assert.Contains(changedSemantic.Nodes, node =>
            node.Kind == WordSemanticNodeKind.Run
            && node.Properties.GetValueOrDefault("style_id") == "Emphasis"
        );
        Assert.Equal(
            "Grid",
            changedSemantic.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Table)
                .Properties["style_id"]
        );
        var changedXml = Encoding.UTF8.GetString(
            applied.Parts["/word/document.xml"].Entry.Content.Span
        );
        Assert.Contains("data-keep=\"yes\"><!--opaque-->", changedXml, StringComparison.Ordinal);
        Assert.Contains("<w:keep/>", changedXml, StringComparison.Ordinal);
        Assert.Contains("custom=\"p\"", changedXml, StringComparison.Ordinal);
        Assert.Equal(
            package.Parts["/word/styles.xml"].Entry.Sha256,
            applied.Parts["/word/styles.xml"].Entry.Sha256
        );
        Assert.Equal(
            package.Parts["/custom/opaque.bin"].Entry.Sha256,
            applied.Parts["/custom/opaque.bin"].Entry.Sha256
        );

        using var revertedStream = Serialize(plan.CreateInverseMutation(applied));
        var reverted = reader.Read(revertedStream);
        Assert.Equal(package.Fingerprint, reverted.Fingerprint);
        Assert.Equal(
            package.Parts["/word/document.xml"].Entry.Content.ToArray(),
            reverted.Parts["/word/document.xml"].Entry.Content.ToArray()
        );
    }

    [Fact]
    public void SupportsDefaultWordNamespaceAndProducesAStableNoOp()
    {
        var documentXml = $"""
            <document xmlns="{WordNamespace}"><body><p><r><t>term</t></r></p></body></document>
            """;
        using var stream = BuildPackage(documentXml, StylesXml());
        var reader = new OpcPackageReader();
        var package = reader.Read(stream);
        var semantic = new WordSemanticProjector().Project(package);
        var paragraph = semantic.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Paragraph);
        var planner = new WordSemanticTransactionPlanner();
        var first = planner.PlanStyleAssignments(
            package,
            semantic,
            [new WordStyleAssignmentCommand(paragraph.Id, "Definition")]
        );
        using var changedStream = Serialize(first.CreateMutation(package));
        var changed = reader.Read(changedStream);
        var changedSemantic = new WordSemanticProjector().Project(changed);
        var changedParagraph = changedSemantic.Nodes.Single(node =>
            node.Kind == WordSemanticNodeKind.Paragraph
        );

        var noOp = planner.PlanStyleAssignments(
            changed,
            changedSemantic,
            [
                new WordStyleAssignmentCommand(
                    changedParagraph.Id,
                    "Definition",
                    ExpectedStyleId: "Definition"
                ),
            ]
        );

        Assert.Equal("Definition", changedParagraph.Properties["style_id"]);
        Assert.False(noOp.HasChanges);
        Assert.Equal(0, noOp.ChangedPartCount);
        Assert.Equal(changed.Fingerprint, noOp.ResultPackageFingerprint);
    }

    [Fact]
    public void RejectsMissingWrongTypeBrokenAndStaleStyleAssignments()
    {
        using var stream = BuildPackage(
            $"<w:document xmlns:w='{WordNamespace}'><w:body><w:p><w:pPr><w:pStyle w:val='OldPara'/></w:pPr></w:p></w:body></w:document>",
            StylesXml(includeBrokenStyle: true)
        );
        var package = new OpcPackageReader().Read(stream);
        var semantic = new WordSemanticProjector().Project(package);
        var paragraph = semantic.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Paragraph);
        var planner = new WordSemanticTransactionPlanner();

        Assert.Throws<WordSemanticEditException>(() =>
            planner.PlanStyleAssignments(
                package,
                semantic,
                [new WordStyleAssignmentCommand(paragraph.Id, "Missing")]
            )
        );
        Assert.Throws<WordSemanticEditException>(() =>
            planner.PlanStyleAssignments(
                package,
                semantic,
                [new WordStyleAssignmentCommand(paragraph.Id, "Emphasis")]
            )
        );
        Assert.Throws<WordSemanticEditException>(() =>
            planner.PlanStyleAssignments(
                package,
                semantic,
                [new WordStyleAssignmentCommand(paragraph.Id, "Broken")]
            )
        );
        Assert.Throws<WordSemanticPreconditionException>(() =>
            planner.PlanStyleAssignments(
                package,
                semantic,
                [
                    new WordStyleAssignmentCommand(
                        paragraph.Id,
                        "Definition",
                        ExpectedStyleId: "Other"
                    ),
                ]
            )
        );
        Assert.Throws<ArgumentException>(() =>
            planner.PlanStyleAssignments(
                package,
                semantic,
                [
                    new WordStyleAssignmentCommand(
                        paragraph.Id,
                        "Definition",
                        ExpectedStyleId: "OldPara",
                        RequireNoExplicitStyle: true
                    ),
                ]
            )
        );
    }

    [Fact]
    public void RejectsDuplicatePropertyContainersAndCancellation()
    {
        using var stream = BuildPackage(
            $"<w:document xmlns:w='{WordNamespace}'><w:body><w:p><w:pPr/><w:pPr/></w:p></w:body></w:document>",
            StylesXml()
        );
        var package = new OpcPackageReader().Read(stream);
        var semantic = new WordSemanticProjector().Project(package);
        var paragraph = semantic.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Paragraph);
        var planner = new WordSemanticTransactionPlanner();

        Assert.Throws<WordSemanticEditException>(() =>
            planner.PlanStyleAssignments(
                package,
                semantic,
                [new WordStyleAssignmentCommand(paragraph.Id, "Definition")]
            )
        );
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            planner.PlanStyleAssignments(
                package,
                semantic,
                [new WordStyleAssignmentCommand(paragraph.Id, "Definition")],
                cancellation.Token
            )
        );
    }

    private static string StylesXml(bool includeBrokenStyle = false) => $"""
        <w:styles xmlns:w="{WordNamespace}">
          <w:style w:type="paragraph" w:styleId="OldPara"><w:name w:val="Old paragraph"/></w:style>
          <w:style w:type="paragraph" w:styleId="Definition"><w:name w:val="Definition"/></w:style>
          <w:style w:type="character" w:styleId="Emphasis"><w:name w:val="Emphasis"/></w:style>
          <w:style w:type="table" w:styleId="Grid"><w:name w:val="Grid"/></w:style>
          {(includeBrokenStyle ? "<w:style w:type=\"paragraph\" w:styleId=\"Broken\"><w:basedOn w:val=\"MissingBase\"/></w:style>" : string.Empty)}
        </w:styles>
        """;

    private static MemoryStream BuildPackage(
        string documentXml,
        string stylesXml,
        byte[]? opaque = null
    )
    {
        var entries = new List<(string Name, byte[] Content)>
        {
            ("[Content_Types].xml", Encoding.UTF8.GetBytes(ContentTypes(opaque is not null))),
            ("_rels/.rels", Encoding.UTF8.GetBytes(RootRelationships())),
            ("word/document.xml", Encoding.UTF8.GetBytes(documentXml)),
            ("word/styles.xml", Encoding.UTF8.GetBytes(stylesXml)),
            ("word/_rels/document.xml.rels", Encoding.UTF8.GetBytes(DocumentRelationships())),
        };
        if (opaque is not null)
        {
            entries.Add(("custom/opaque.bin", opaque));
        }
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(content);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream Serialize(OpcPackageMutationBuilder mutation)
    {
        var stream = new MemoryStream();
        new OpcPackageSerializer().Write(stream, mutation);
        stream.Position = 0;
        return stream;
    }

    private static string ContentTypes(bool includeOpaque) => $"""
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
          <Default Extension="xml" ContentType="application/xml" />
          {(includeOpaque ? "<Default Extension=\"bin\" ContentType=\"application/octet-stream\" />" : string.Empty)}
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" />
          <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml" />
        </Types>
        """;

    private static string RootRelationships() => """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml" />
        </Relationships>
        """;

    private static string DocumentRelationships() => """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml" />
        </Relationships>
        """;
}
