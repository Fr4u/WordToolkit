using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
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

    [Fact]
    public void CreatesAndAssignsInheritedStyleAsOneLosslessReversibleTransaction()
    {
        const string documentXml = """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body keep="exact"><w:p><w:pPr><w:pStyle w:val="OldPara"/></w:pPr><w:r><w:t>definition</w:t></w:r></w:p></w:body></w:document>
            """;
        var stylesXml = StylesXml();
        using var stream = BuildPackage(documentXml, stylesXml, [9, 8, 7, 6]);
        var reader = new OpcPackageReader();
        var package = reader.Read(stream);
        var semantic = new WordSemanticProjector().Project(package);
        var paragraph = semantic.Nodes.Single(node =>
            node.Kind == WordSemanticNodeKind.Paragraph
        );
        var definitions = new WordStyleDefinitionCommand[]
        {
            new WordStyleCreateCommand(
                "DefinitionDerived",
                "Definition derived",
                WordStyleType.Paragraph,
                BasedOnStyleId: "OldPara",
                NextStyleId: "DefinitionDerived",
                QuickFormat: true,
                UiPriority: 17
            ),
        };
        var assignments = new[]
        {
            new WordStyleAssignmentCommand(
                paragraph.Id,
                "DefinitionDerived",
                ExpectedStyleId: "OldPara"
            ),
        };
        var planner = new WordSemanticTransactionPlanner();

        var plan = planner.PlanStyleEdits(
            package,
            semantic,
            definitions,
            assignments
        );
        var repeated = planner.PlanStyleEdits(
            package,
            semantic,
            definitions,
            assignments
        );

        Assert.Equal(plan.PlanId, repeated.PlanId);
        Assert.Equal(2, plan.OperationCount);
        Assert.Equal(2, plan.ChangedOperationCount);
        Assert.Equal(2, plan.ChangedPartCount);
        var definitionOperation = Assert.Single(plan.DefinitionOperations);
        Assert.Equal("create_style", definitionOperation.Kind);
        Assert.Equal("DefinitionDerived", definitionOperation.StyleId);
        Assert.Equal(WordStyleType.Paragraph, definitionOperation.StyleType);
        Assert.Single(plan.Operations);

        using var appliedStream = Serialize(plan.CreateMutation(package));
        var applied = reader.Read(appliedStream);
        Assert.Equal(plan.ResultPackageFingerprint, applied.Fingerprint);
        Assert.Equal(
            package.Parts["/custom/opaque.bin"].Entry.Sha256,
            applied.Parts["/custom/opaque.bin"].Entry.Sha256
        );
        var changedSemantic = new WordSemanticProjector().Project(applied);
        var changedStyles = new WordStyleGraphBuilder().Build(applied, changedSemantic);
        Assert.True(changedStyles.TryGetStyle("DefinitionDerived", out var created));
        Assert.NotNull(created);
        Assert.Equal("Definition derived", created.Name);
        Assert.Equal("OldPara", created.BasedOnStyleId);
        Assert.Equal("DefinitionDerived", created.NextStyleId);
        Assert.True(created.IsCustom);
        Assert.True(created.QuickFormat);
        Assert.Equal(17, created.UiPriority);
        Assert.True(created.InheritanceResolvable);
        Assert.Equal(
            "DefinitionDerived",
            changedSemantic.Nodes.Single(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
            ).Properties["style_id"]
        );
        var changedStylesXml = Encoding.UTF8.GetString(
            applied.Parts["/word/styles.xml"].Entry.Content.Span
        );
        Assert.Contains(stylesXml[..stylesXml.LastIndexOf("</w:styles>", StringComparison.Ordinal)], changedStylesXml, StringComparison.Ordinal);

        using var revertedStream = Serialize(plan.CreateInverseMutation(applied));
        var reverted = reader.Read(revertedStream);
        Assert.Equal(package.Fingerprint, reverted.Fingerprint);
        Assert.Equal(
            package.Parts["/word/styles.xml"].Entry.Content.ToArray(),
            reverted.Parts["/word/styles.xml"].Entry.Content.ToArray()
        );
        Assert.Equal(
            package.Parts["/word/document.xml"].Entry.Content.ToArray(),
            reverted.Parts["/word/document.xml"].Entry.Content.ToArray()
        );
    }

    [Fact]
    public void ClonesOpaqueStyleMarkupWithoutCopyingDefaultOrLinkedIdentity()
    {
        var stylesXml = $"""
            <w:styles xmlns:w="{WordNamespace}" xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml">
              <w:style w:type="paragraph" w:styleId="OldPara" w:default="1" mc:Ignorable="w14"><w:name w:val="Old paragraph"/><w:next w:val="OldPara"/><w:link w:val="CharLink"/><w14:opaque w14:val="keep"/><w:rPr><w:b/></w:rPr></w:style>
              <w:style w:type="character" w:styleId="CharLink"><w:name w:val="Character link"/><w:link w:val="OldPara"/></w:style>
            </w:styles>
            """;
        using var stream = BuildPackage(
            $"<w:document xmlns:w='{WordNamespace}'><w:body><w:p/></w:body></w:document>",
            stylesXml
        );
        var reader = new OpcPackageReader();
        var package = reader.Read(stream);
        var semantic = new WordSemanticProjector().Project(package);
        var planner = new WordSemanticTransactionPlanner();

        var plan = planner.PlanStyleEdits(
            package,
            semantic,
            [new WordStyleCloneCommand("OldPara", "CopiedPara", "Copied paragraph")],
            Array.Empty<WordStyleAssignmentCommand>()
        );
        using var appliedStream = Serialize(plan.CreateMutation(package));
        var applied = reader.Read(appliedStream);
        var appliedSemantic = new WordSemanticProjector().Project(applied);
        var graph = new WordStyleGraphBuilder().Build(applied, appliedSemantic);

        Assert.True(graph.TryGetStyle("CopiedPara", out var copied));
        Assert.NotNull(copied);
        Assert.Equal("Copied paragraph", copied.Name);
        Assert.False(copied.IsDefault);
        Assert.True(copied.IsCustom);
        Assert.Null(copied.LinkedStyleId);
        Assert.Equal("CopiedPara", copied.NextStyleId);
        Assert.Equal("true", copied.RunProperties.Values["bold"]);
        Assert.Single(graph.DefaultStyleIds);
        Assert.Equal("OldPara", graph.DefaultStyleIds[WordStyleType.Paragraph]);
        var xml = Encoding.UTF8.GetString(
            applied.Parts["/word/styles.xml"].Entry.Content.Span
        );
        var parsed = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        XNamespace word = WordNamespace;
        XNamespace w14 = "http://schemas.microsoft.com/office/word/2010/wordml";
        var copiedElement = parsed.Root!.Elements(word + "style").Single(element =>
            (string?)element.Attribute(word + "styleId") == "CopiedPara"
        );
        Assert.NotNull(copiedElement.Element(w14 + "opaque"));
        Assert.Equal(
            "w14",
            (string?)copiedElement.Attribute(
                XName.Get(
                    "Ignorable",
                    "http://schemas.openxmlformats.org/markup-compatibility/2006"
                )
            )
        );
        Assert.NotNull(copiedElement.GetNamespaceOfPrefix("w14"));
    }

    [Fact]
    public void RejectsInvalidStyleDefinitionGraphsAndEffectsMirrorDrift()
    {
        using var stream = BuildPackage(
            $"<w:document xmlns:w='{WordNamespace}'><w:body><w:p/></w:body></w:document>",
            StylesXml()
        );
        var package = new OpcPackageReader().Read(stream);
        var semantic = new WordSemanticProjector().Project(package);
        var planner = new WordSemanticTransactionPlanner();

        Assert.Throws<WordSemanticEditException>(() => planner.PlanStyleEdits(
            package,
            semantic,
            [new WordStyleCloneCommand("Missing", "Copy", "Copy")],
            Array.Empty<WordStyleAssignmentCommand>()
        ));
        Assert.Throws<WordSemanticEditException>(() => planner.PlanStyleEdits(
            package,
            semantic,
            [new WordStyleCreateCommand(
                "OldPara",
                "Collision",
                WordStyleType.Paragraph
            )],
            Array.Empty<WordStyleAssignmentCommand>()
        ));
        Assert.Throws<WordSemanticEditException>(() => planner.PlanStyleEdits(
            package,
            semantic,
            [new WordStyleCreateCommand(
                "WrongBase",
                "Wrong base",
                WordStyleType.Paragraph,
                BasedOnStyleId: "Emphasis"
            )],
            Array.Empty<WordStyleAssignmentCommand>()
        ));
        Assert.Throws<WordSemanticEditException>(() => planner.PlanStyleEdits(
            package,
            semantic,
            [
                new WordStyleCreateCommand(
                    "CycleA",
                    "Cycle A",
                    WordStyleType.Paragraph,
                    BasedOnStyleId: "CycleB"
                ),
                new WordStyleCreateCommand(
                    "CycleB",
                    "Cycle B",
                    WordStyleType.Paragraph,
                    BasedOnStyleId: "CycleA"
                ),
            ],
            Array.Empty<WordStyleAssignmentCommand>()
        ));

        using var effectsStream = BuildPackage(
            $"<w:document xmlns:w='{WordNamespace}'><w:body><w:p/></w:body></w:document>",
            StylesXml(),
            stylesWithEffectsXml: StylesXml()
        );
        var effectsPackage = new OpcPackageReader().Read(effectsStream);
        var effectsSemantic = new WordSemanticProjector().Project(effectsPackage);
        var effectsError = Assert.Throws<WordSemanticEditException>(() =>
            planner.PlanStyleEdits(
                effectsPackage,
                effectsSemantic,
                [new WordStyleCreateCommand(
                    "SafeOnlyWhenMirrored",
                    "Safe only when mirrored",
                    WordStyleType.Paragraph
                )],
                Array.Empty<WordStyleAssignmentCommand>()
            )
        );
        Assert.Contains("stylesWithEffects", effectsError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CancelsStyleDefinitionPlanningBeforeMutation()
    {
        using var stream = BuildPackage(
            $"<w:document xmlns:w='{WordNamespace}'><w:body><w:p/></w:body></w:document>",
            StylesXml()
        );
        var package = new OpcPackageReader().Read(stream);
        var semantic = new WordSemanticProjector().Project(package);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            new WordSemanticTransactionPlanner().PlanStyleEdits(
                package,
                semantic,
                [new WordStyleCreateCommand(
                    "NeverWritten",
                    "Never written",
                    WordStyleType.Paragraph
                )],
                Array.Empty<WordStyleAssignmentCommand>(),
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
        byte[]? opaque = null,
        string? stylesWithEffectsXml = null
    )
    {
        var entries = new List<(string Name, byte[] Content)>
        {
            ("[Content_Types].xml", Encoding.UTF8.GetBytes(ContentTypes(
                opaque is not null,
                stylesWithEffectsXml is not null
            ))),
            ("_rels/.rels", Encoding.UTF8.GetBytes(RootRelationships())),
            ("word/document.xml", Encoding.UTF8.GetBytes(documentXml)),
            ("word/styles.xml", Encoding.UTF8.GetBytes(stylesXml)),
            ("word/_rels/document.xml.rels", Encoding.UTF8.GetBytes(DocumentRelationships(
                stylesWithEffectsXml is not null
            ))),
        };
        if (opaque is not null)
        {
            entries.Add(("custom/opaque.bin", opaque));
        }
        if (stylesWithEffectsXml is not null)
        {
            entries.Add((
                "word/stylesWithEffects.xml",
                Encoding.UTF8.GetBytes(stylesWithEffectsXml)
            ));
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

    private static string ContentTypes(bool includeOpaque, bool includeEffects = false) => $"""
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
          <Default Extension="xml" ContentType="application/xml" />
          {(includeOpaque ? "<Default Extension=\"bin\" ContentType=\"application/octet-stream\" />" : string.Empty)}
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" />
          <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml" />
          {(includeEffects ? "<Override PartName=\"/word/stylesWithEffects.xml\" ContentType=\"application/vnd.ms-word.stylesWithEffects+xml\" />" : string.Empty)}
        </Types>
        """;

    private static string RootRelationships() => """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml" />
        </Relationships>
        """;

    private static string DocumentRelationships(bool includeEffects = false) => $"""
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml" />
          {(includeEffects ? "<Relationship Id=\"rIdEffects\" Type=\"http://schemas.microsoft.com/office/2007/relationships/stylesWithEffects\" Target=\"stylesWithEffects.xml\" />" : string.Empty)}
        </Relationships>
        """;
}
