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

        using var invalidNextStream = BuildPackage(
            $"<w:document xmlns:w='{WordNamespace}'><w:body><w:p/></w:body></w:document>",
            $"""
            <w:styles xmlns:w="{WordNamespace}">
              <w:style w:type="paragraph" w:styleId="BrokenNext"><w:name w:val="Broken next"/><w:next w:val="Missing"/></w:style>
            </w:styles>
            """
        );
        var invalidNextPackage = new OpcPackageReader().Read(invalidNextStream);
        var invalidNextSemantic = new WordSemanticProjector().Project(invalidNextPackage);
        var invalidNextError = Assert.Throws<WordSemanticEditException>(() =>
            planner.PlanStyleEdits(
                invalidNextPackage,
                invalidNextSemantic,
                [new WordStyleCloneCommand("BrokenNext", "CopiedBroken", "Copied broken")],
                Array.Empty<WordStyleAssignmentCommand>()
            )
        );
        Assert.Contains("missing next", invalidNextError.Message, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void ConsolidatesEquivalentLinkedStylesAcrossStoriesNumberingAndRevisions()
    {
        var stylesXml = ConsolidationStylesXml();
        var documentXml = $"""
            <w:document xmlns:w="{WordNamespace}"><w:body keep="yes">
              <w:p><w:pPr><w:pStyle w:val="SourcePara"/><w:pPrChange w:id="1" w:author="a" w:date="2024-01-01T00:00:00Z"><w:pPr><w:pStyle w:val="SourcePara"/></w:pPr></w:pPrChange></w:pPr><w:r><w:rPr><w:rStyle w:val="SourceChar"/></w:rPr><w:t>body</w:t></w:r></w:p>
              <w:tbl><w:tblPr><w:tblStyle w:val="KeepTable"/></w:tblPr><w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl>
            </w:body></w:document>
            """;
        var numberingXml = $"""
            <w:numbering xmlns:w="{WordNamespace}">
              <w:abstractNum w:abstractNumId="0"><w:multiLevelType w:val="singleLevel"/><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/><w:pStyle w:val="SourcePara"/><w:rPr><w:rStyle w:val="SourceChar"/></w:rPr></w:lvl></w:abstractNum>
              <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
            </w:numbering>
            """;
        var headerXml = $"""
            <w:hdr xmlns:w="{WordNamespace}"><w:p><w:pPr><w:pStyle w:val="SourcePara"/></w:pPr><w:r><w:t>header</w:t></w:r></w:p></w:hdr>
            """;
        var commentsXml = $"""
            <w:comments xmlns:w="{WordNamespace}"><w:comment w:id="0" w:author="a" w:date="2024-01-01T00:00:00Z"><w:p><w:pPr><w:pStyle w:val="SourcePara"/></w:pPr><w:r><w:t>comment</w:t></w:r></w:p></w:comment></w:comments>
            """;
        var footnotesXml = $"""
            <w:footnotes xmlns:w="{WordNamespace}"><w:footnote w:id="1"><w:p><w:pPr><w:pStyle w:val="SourcePara"/></w:pPr><w:r><w:t>note</w:t></w:r></w:p></w:footnote></w:footnotes>
            """;
        var glossaryXml = $"""
            <w:glossaryDocument xmlns:w="{WordNamespace}"><w:docParts><w:docPart><w:docPartPr><w:name w:val="entry"/><w:style w:val="SourcePara"/></w:docPartPr><w:docPartBody><w:p><w:r><w:t>entry</w:t></w:r></w:p></w:docPartBody></w:docPart></w:docParts></w:glossaryDocument>
            """;
        using var stream = BuildConsolidationPackage(
            documentXml,
            stylesXml,
            numberingXml,
            headerXml,
            commentsXml,
            footnotesXml,
            glossaryXml
        );
        var reader = new OpcPackageReader();
        var package = reader.Read(stream);
        var semantic = new WordSemanticProjector().Project(package);
        var commands = new WordStyleDefinitionCommand[]
        {
            new WordStyleConsolidateCommand("SourcePara", "TargetPara"),
            new WordStyleConsolidateCommand("SourceChar", "TargetChar"),
        };
        var planner = new WordSemanticTransactionPlanner();

        var plan = planner.PlanStyleEdits(
            package,
            semantic,
            commands,
            Array.Empty<WordStyleAssignmentCommand>()
        );
        var repeated = planner.PlanStyleEdits(
            package,
            semantic,
            commands,
            Array.Empty<WordStyleAssignmentCommand>()
        );

        Assert.Equal(plan.PlanId, repeated.PlanId);
        Assert.Equal(2, plan.OperationCount);
        Assert.Equal(7, plan.ChangedPartCount);
        Assert.All(plan.DefinitionOperations, operation =>
            Assert.Equal("consolidate_style", operation.Kind)
        );
        Assert.Equal(8, plan.DefinitionOperations[0].ReferenceUpdateCount);
        Assert.Equal(3, plan.DefinitionOperations[1].ReferenceUpdateCount);

        using var appliedStream = Serialize(plan.CreateMutation(package));
        var applied = reader.Read(appliedStream);
        Assert.Equal(plan.ResultPackageFingerprint, applied.Fingerprint);
        var changedSemantic = new WordSemanticProjector().Project(applied);
        var changedStyles = new WordStyleGraphBuilder().Build(applied, changedSemantic);
        Assert.False(changedStyles.TryGetStyle("SourcePara", out _));
        Assert.False(changedStyles.TryGetStyle("SourceChar", out _));
        Assert.True(changedStyles.TryGetStyle("TargetPara", out _));
        Assert.True(changedStyles.TryGetStyle("TargetChar", out _));
        Assert.All(
            changedSemantic.Nodes.Where(node =>
                node.Properties.TryGetValue("style_id", out _)
            ),
            node => Assert.DoesNotContain(
                node.Properties["style_id"],
                new[] { "SourcePara", "SourceChar" }
            )
        );
        Assert.Equal(
            "TargetPara",
            changedStyles.Styles.Single(style => style.StyleId == "DerivedPara")
                .BasedOnStyleId
        );
        Assert.Equal(
            "TargetChar",
            changedStyles.Styles.Single(style => style.StyleId == "DerivedChar")
                .BasedOnStyleId
        );
        foreach (
            var partUri in new[]
            {
                "/word/document.xml",
                "/word/styles.xml",
                "/word/numbering.xml",
                "/word/header1.xml",
                "/word/comments.xml",
                "/word/footnotes.xml",
                "/word/glossary/document.xml",
            }
        )
        {
            var xml = Encoding.UTF8.GetString(applied.Parts[partUri].Entry.Content.Span);
            Assert.DoesNotContain("SourcePara", xml, StringComparison.Ordinal);
            Assert.DoesNotContain("SourceChar", xml, StringComparison.Ordinal);
        }
        Assert.Equal(
            package.Parts["/custom/opaque.bin"].Entry.Sha256,
            applied.Parts["/custom/opaque.bin"].Entry.Sha256
        );

        using var revertedStream = Serialize(plan.CreateInverseMutation(applied));
        var reverted = reader.Read(revertedStream);
        Assert.Equal(package.Fingerprint, reverted.Fingerprint);
        foreach (var part in package.Parts.Values)
        {
            Assert.Equal(
                part.Entry.Content.ToArray(),
                reverted.Parts[part.Uri].Entry.Content.ToArray()
            );
        }
    }

    [Fact]
    public void ComposesCloneConsolidationAndAssignmentAcrossTheSameParts()
    {
        var stylesXml = $"""
            <w:styles xmlns:w="{WordNamespace}">
              <w:style w:type="paragraph" w:styleId="Base"><w:name w:val="Base"/></w:style>
              <w:style w:type="paragraph" w:styleId="Source" w:customStyle="1"><w:name w:val="Source"/><w:basedOn w:val="Base"/><w:next w:val="Source"/><w:qFormat/><w:rPr><w:b/></w:rPr></w:style>
            </w:styles>
            """;
        var documentXml = $"""
            <w:document xmlns:w="{WordNamespace}"><w:body><w:p><w:pPr><w:pStyle w:val="Source"/></w:pPr><w:r><w:t>old</w:t></w:r></w:p><w:p><w:r><w:t>new</w:t></w:r></w:p></w:body></w:document>
            """;
        using var stream = BuildPackage(documentXml, stylesXml);
        var reader = new OpcPackageReader();
        var package = reader.Read(stream);
        var semantic = new WordSemanticProjector().Project(package);
        var unstyled = semantic.Nodes.Where(node =>
            node.Kind == WordSemanticNodeKind.Paragraph
        ).Last();
        var definitions = new WordStyleDefinitionCommand[]
        {
            new WordStyleCloneCommand("Source", "Target", "Target"),
            new WordStyleConsolidateCommand("Source", "Target"),
        };
        var assignments = new[]
        {
            new WordStyleAssignmentCommand(
                unstyled.Id,
                "Target",
                RequireNoExplicitStyle: true
            ),
        };

        var plan = new WordSemanticTransactionPlanner().PlanStyleEdits(
            package,
            semantic,
            definitions,
            assignments
        );

        Assert.Equal(3, plan.OperationCount);
        Assert.Equal(2, plan.ChangedPartCount);
        Assert.Equal(
            new[] { "clone_style", "consolidate_style" },
            plan.DefinitionOperations.Select(operation => operation.Kind).ToArray()
        );
        using var appliedStream = Serialize(plan.CreateMutation(package));
        var applied = reader.Read(appliedStream);
        var changedSemantic = new WordSemanticProjector().Project(applied);
        Assert.Equal(
            2,
            changedSemantic.Nodes.Count(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
                && node.Properties.GetValueOrDefault("style_id") == "Target"
            )
        );
        var graph = new WordStyleGraphBuilder().Build(applied, changedSemantic);
        Assert.False(graph.TryGetStyle("Source", out _));
        Assert.True(graph.TryGetStyle("Target", out _));

        using var revertedStream = Serialize(plan.CreateInverseMutation(applied));
        var reverted = reader.Read(revertedStream);
        Assert.Equal(package.Fingerprint, reverted.Fingerprint);
    }

    [Fact]
    public void RejectsUnsafeOrNonEquivalentStyleConsolidation()
    {
        var planner = new WordSemanticTransactionPlanner();
        var baseDocument = $"""
            <w:document xmlns:w="{WordNamespace}"><w:body><w:p><w:pPr><w:pStyle w:val="Source"/></w:pPr><w:r><w:t>x</w:t></w:r></w:p></w:body></w:document>
            """;
        var nonEquivalentStyles = $"""
            <w:styles xmlns:w="{WordNamespace}">
              <w:style w:type="paragraph" w:styleId="Source" w:customStyle="1"><w:name w:val="Source"/><w:rPr><w:b/></w:rPr></w:style>
              <w:style w:type="paragraph" w:styleId="Target" w:customStyle="1"><w:name w:val="Target"/><w:rPr><w:i/></w:rPr></w:style>
            </w:styles>
            """;
        using var nonEquivalentStream = BuildPackage(
            baseDocument,
            nonEquivalentStyles
        );
        var nonEquivalentPackage = new OpcPackageReader().Read(nonEquivalentStream);
        var nonEquivalentSemantic = new WordSemanticProjector().Project(
            nonEquivalentPackage
        );
        Assert.Throws<WordSemanticEditException>(() => planner.PlanStyleEdits(
            nonEquivalentPackage,
            nonEquivalentSemantic,
            [new WordStyleConsolidateCommand("Source", "Target")],
            Array.Empty<WordStyleAssignmentCommand>()
        ));

        var builtInStyles = nonEquivalentStyles.Replace(
            " w:customStyle=\"1\"",
            string.Empty,
            StringComparison.Ordinal
        ).Replace("<w:i/>", "<w:b/>", StringComparison.Ordinal);
        using var builtInStream = BuildPackage(baseDocument, builtInStyles);
        var builtInPackage = new OpcPackageReader().Read(builtInStream);
        var builtInSemantic = new WordSemanticProjector().Project(builtInPackage);
        Assert.Throws<WordSemanticEditException>(() => planner.PlanStyleEdits(
            builtInPackage,
            builtInSemantic,
            [new WordStyleConsolidateCommand("Source", "Target")],
            Array.Empty<WordStyleAssignmentCommand>()
        ));

        using var chainStream = BuildPackage(
            baseDocument,
            nonEquivalentStyles.Replace("<w:i/>", "<w:b/>", StringComparison.Ordinal)
                .Replace("</w:styles>", "<w:style w:type=\"paragraph\" w:styleId=\"Third\" w:customStyle=\"1\"><w:name w:val=\"Third\"/><w:rPr><w:b/></w:rPr></w:style></w:styles>", StringComparison.Ordinal)
        );
        var chainPackage = new OpcPackageReader().Read(chainStream);
        var chainSemantic = new WordSemanticProjector().Project(chainPackage);
        Assert.Throws<WordSemanticEditException>(() => planner.PlanStyleEdits(
            chainPackage,
            chainSemantic,
            [
                new WordStyleConsolidateCommand("Source", "Target"),
                new WordStyleConsolidateCommand("Target", "Third"),
            ],
            Array.Empty<WordStyleAssignmentCommand>()
        ));

        var invalidNextStyles = $"""
            <w:styles xmlns:w="{WordNamespace}">
              <w:style w:type="character" w:styleId="SourceChar" w:customStyle="1"><w:name w:val="Source"/><w:next w:val="SourceChar"/><w:rPr><w:b/></w:rPr></w:style>
              <w:style w:type="character" w:styleId="TargetChar" w:customStyle="1"><w:name w:val="Target"/><w:next w:val="TargetChar"/><w:rPr><w:b/></w:rPr></w:style>
            </w:styles>
            """;
        using var invalidNextStream = BuildPackage(
            $"<w:document xmlns:w='{WordNamespace}'><w:body><w:p/></w:body></w:document>",
            invalidNextStyles
        );
        var invalidNextPackage = new OpcPackageReader().Read(invalidNextStream);
        var invalidNextSemantic = new WordSemanticProjector().Project(
            invalidNextPackage
        );
        Assert.Throws<WordSemanticEditException>(() => planner.PlanStyleEdits(
            invalidNextPackage,
            invalidNextSemantic,
            [new WordStyleConsolidateCommand("SourceChar", "TargetChar")],
            Array.Empty<WordStyleAssignmentCommand>()
        ));
    }

    [Fact]
    public void RejectsOpaqueConsumersAndReferenceUpdateOverflow()
    {
        var styles = $"""
            <w:styles xmlns:w="{WordNamespace}">
              <w:style w:type="paragraph" w:styleId="Source" w:customStyle="1"><w:name w:val="Source name"/><w:rPr><w:b/></w:rPr></w:style>
              <w:style w:type="paragraph" w:styleId="Target" w:customStyle="1"><w:name w:val="Target name"/><w:rPr><w:b/></w:rPr></w:style>
            </w:styles>
            """;
        var fieldDocument = $"""
            <w:document xmlns:w="{WordNamespace}"><w:body><w:p><w:fldSimple w:instr=" STYLEREF &quot;Source name&quot; "><w:r><w:t>x</w:t></w:r></w:fldSimple></w:p></w:body></w:document>
            """;
        using var fieldStream = BuildPackage(fieldDocument, styles);
        var fieldPackage = new OpcPackageReader().Read(fieldStream);
        var fieldSemantic = new WordSemanticProjector().Project(fieldPackage);
        Assert.Throws<WordSemanticEditException>(() =>
            new WordSemanticTransactionPlanner().PlanStyleEdits(
                fieldPackage,
                fieldSemantic,
                [new WordStyleConsolidateCommand("Source", "Target")],
                Array.Empty<WordStyleAssignmentCommand>()
            )
        );

        var latentStyles = styles.Replace(
            $"<w:styles xmlns:w=\"{WordNamespace}\">",
            $"<w:styles xmlns:w=\"{WordNamespace}\"><w:latentStyles><w:lsdException w:name=\"Source name\"/></w:latentStyles>",
            StringComparison.Ordinal
        );
        using var latentStream = BuildPackage(
            $"<w:document xmlns:w='{WordNamespace}'><w:body><w:p/></w:body></w:document>",
            latentStyles
        );
        var latentPackage = new OpcPackageReader().Read(latentStream);
        var latentSemantic = new WordSemanticProjector().Project(latentPackage);
        Assert.Throws<WordSemanticEditException>(() =>
            new WordSemanticTransactionPlanner().PlanStyleEdits(
                latentPackage,
                latentSemantic,
                [new WordStyleConsolidateCommand("Source", "Target")],
                Array.Empty<WordStyleAssignmentCommand>()
            )
        );

        var repeatedReferences = $"""
            <w:document xmlns:w="{WordNamespace}"><w:body><w:p><w:pPr><w:pStyle w:val="Source"/></w:pPr></w:p><w:p><w:pPr><w:pStyle w:val="Source"/></w:pPr></w:p></w:body></w:document>
            """;
        using var limitStream = BuildPackage(repeatedReferences, styles);
        var limitPackage = new OpcPackageReader().Read(limitStream);
        var limitSemantic = new WordSemanticProjector().Project(limitPackage);
        Assert.Throws<WordSemanticTransactionLimitException>(() =>
            new WordSemanticTransactionPlanner(
                new WordSemanticTransactionOptions { MaxStyleReferenceUpdates = 1 }
            ).PlanStyleEdits(
                limitPackage,
                limitSemantic,
                [new WordStyleConsolidateCommand("Source", "Target")],
                Array.Empty<WordStyleAssignmentCommand>()
            )
        );

        using var linkedTemplateStream = BuildPackage(
            $"<w:document xmlns:w='{WordNamespace}'><w:body><w:p/></w:body></w:document>",
            styles,
            settingsXml: $"<w:settings xmlns:w='{WordNamespace}'><w:linkStyles/></w:settings>"
        );
        var linkedTemplatePackage = new OpcPackageReader().Read(linkedTemplateStream);
        var linkedTemplateSemantic = new WordSemanticProjector().Project(
            linkedTemplatePackage
        );
        Assert.Throws<WordSemanticEditException>(() =>
            new WordSemanticTransactionPlanner().PlanStyleEdits(
                linkedTemplatePackage,
                linkedTemplateSemantic,
                [new WordStyleConsolidateCommand("Source", "Target")],
                Array.Empty<WordStyleAssignmentCommand>()
            )
        );

        using var macroStream = BuildPackage(
            $"<w:document xmlns:w='{WordNamespace}'><w:body><w:p/></w:body></w:document>",
            styles,
            vbaProject: [1, 2, 3]
        );
        var macroPackage = new OpcPackageReader().Read(macroStream);
        var macroSemantic = new WordSemanticProjector().Project(macroPackage);
        Assert.Throws<WordSemanticEditException>(() =>
            new WordSemanticTransactionPlanner().PlanStyleEdits(
                macroPackage,
                macroSemantic,
                [new WordStyleConsolidateCommand("Source", "Target")],
                Array.Empty<WordStyleAssignmentCommand>()
            )
        );

        using var altChunkStream = BuildPackage(
            $"<w:document xmlns:w='{WordNamespace}'><w:body><w:altChunk/><w:p/></w:body></w:document>",
            styles
        );
        var altChunkPackage = new OpcPackageReader().Read(altChunkStream);
        var altChunkSemantic = new WordSemanticProjector().Project(altChunkPackage);
        Assert.Throws<WordSemanticEditException>(() =>
            new WordSemanticTransactionPlanner().PlanStyleEdits(
                altChunkPackage,
                altChunkSemantic,
                [new WordStyleConsolidateCommand("Source", "Target")],
                Array.Empty<WordStyleAssignmentCommand>()
            )
        );

        using var unmodeledConsumerStream = BuildPackage(
            $"<w:document xmlns:w='{WordNamespace}'><w:body><w:p/></w:body></w:document>",
            styles,
            unmodeledXml: $"<w:root xmlns:w='{WordNamespace}'><w:pStyle w:val='Source'/></w:root>"
        );
        var unmodeledConsumerPackage = new OpcPackageReader().Read(
            unmodeledConsumerStream
        );
        var unmodeledConsumerSemantic = new WordSemanticProjector().Project(
            unmodeledConsumerPackage
        );
        Assert.Throws<WordSemanticEditException>(() =>
            new WordSemanticTransactionPlanner().PlanStyleEdits(
                unmodeledConsumerPackage,
                unmodeledConsumerSemantic,
                [new WordStyleConsolidateCommand("Source", "Target")],
                Array.Empty<WordStyleAssignmentCommand>()
            )
        );
    }

    [Fact]
    public void DeletesAClosedUnusedCustomStyleSubgraphWithAnExactInverse()
    {
        var stylesXml = $"""
            <w:styles xmlns:w="{WordNamespace}" xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml">
              <w:style w:type="paragraph" w:default="1" w:styleId="Base"><w:name w:val="Base"/></w:style>
              <w:style w:type="paragraph" w:styleId="UnusedParent" w:customStyle="1"><w:name w:val="Unused parent"/><w:basedOn w:val="Base"/><w:next w:val="UnusedParent"/><w14:opaque w14:val="keep-until-delete"/><w:rPr><w:b/></w:rPr></w:style>
              <w:style w:type="paragraph" w:styleId="UnusedChild" w:customStyle="1"><w:name w:val="Unused child"/><w:basedOn w:val="UnusedParent"/><w:rPr><w:i/></w:rPr></w:style>
              <w:style w:type="character" w:styleId="KeepChar" w:customStyle="1"><w:name w:val="Keep character"/></w:style>
            </w:styles>
            """;
        var documentXml = $"""
            <w:document xmlns:w="{WordNamespace}"><w:body><w:p><w:pPr><w:pStyle w:val="Base"/></w:pPr><w:r><w:rPr><w:rStyle w:val="KeepChar"/></w:rPr><w:t>kept</w:t></w:r></w:p></w:body></w:document>
            """;
        using var stream = BuildPackage(documentXml, stylesXml, opaque: [4, 3, 2, 1]);
        var reader = new OpcPackageReader();
        var package = reader.Read(stream);
        var semantic = new WordSemanticProjector().Project(package);
        var commands = new WordStyleDefinitionCommand[]
        {
            new WordStyleDeleteUnusedCommand("UnusedChild"),
            new WordStyleDeleteUnusedCommand("UnusedParent"),
        };

        var plan = new WordSemanticTransactionPlanner().PlanStyleEdits(
            package,
            semantic,
            commands,
            Array.Empty<WordStyleAssignmentCommand>()
        );
        var repeated = new WordSemanticTransactionPlanner().PlanStyleEdits(
            package,
            semantic,
            commands,
            Array.Empty<WordStyleAssignmentCommand>()
        );

        Assert.Equal(plan.PlanId, repeated.PlanId);
        Assert.Equal(2, plan.OperationCount);
        Assert.Equal(1, plan.ChangedPartCount);
        Assert.All(plan.DefinitionOperations, operation =>
        {
            Assert.Equal("delete_unused_style", operation.Kind);
            Assert.True(operation.XmlByteDelta < 0);
            Assert.Equal(0, operation.ReferenceUpdateCount);
        });

        using var appliedStream = Serialize(plan.CreateMutation(package));
        var applied = reader.Read(appliedStream);
        Assert.Equal(plan.ResultPackageFingerprint, applied.Fingerprint);
        var changedSemantic = new WordSemanticProjector().Project(applied);
        var graph = new WordStyleGraphBuilder().Build(applied, changedSemantic);
        Assert.False(graph.TryGetStyle("UnusedChild", out _));
        Assert.False(graph.TryGetStyle("UnusedParent", out _));
        Assert.True(graph.TryGetStyle("Base", out _));
        Assert.True(graph.TryGetStyle("KeepChar", out _));
        Assert.Equal(
            package.Parts["/custom/opaque.bin"].Entry.Sha256,
            applied.Parts["/custom/opaque.bin"].Entry.Sha256
        );

        using var revertedStream = Serialize(plan.CreateInverseMutation(applied));
        var reverted = reader.Read(revertedStream);
        Assert.Equal(package.Fingerprint, reverted.Fingerprint);
        foreach (var part in package.Parts.Values)
        {
            Assert.Equal(
                part.Entry.Content.ToArray(),
                reverted.Parts[part.Uri].Entry.Content.ToArray()
            );
        }
    }

    [Fact]
    public void RejectsReferencedBuiltInDuplicateAndCrossStageStyleDeletion()
    {
        var planner = new WordSemanticTransactionPlanner();
        var styles = $"""
            <w:styles xmlns:w="{WordNamespace}">
              <w:style w:type="paragraph" w:default="1" w:styleId="Base"><w:name w:val="Base"/></w:style>
              <w:style w:type="paragraph" w:styleId="Source" w:customStyle="1"><w:name w:val="Source name"/><w:rPr><w:b/></w:rPr></w:style>
              <w:style w:type="paragraph" w:styleId="Target" w:customStyle="1"><w:name w:val="Target name"/><w:rPr><w:b/></w:rPr></w:style>
              <w:style w:type="paragraph" w:styleId="Dependent" w:customStyle="1"><w:name w:val="Dependent"/><w:basedOn w:val="Source"/></w:style>
            </w:styles>
            """;
        var document = $"<w:document xmlns:w='{WordNamespace}'><w:body><w:p/></w:body></w:document>";
        using var referencedStream = BuildPackage(document, styles);
        var referencedPackage = new OpcPackageReader().Read(referencedStream);
        var referencedSemantic = new WordSemanticProjector().Project(referencedPackage);
        Assert.Throws<WordSemanticEditException>(() => planner.PlanStyleEdits(
            referencedPackage,
            referencedSemantic,
            [new WordStyleDeleteUnusedCommand("Source")],
            Array.Empty<WordStyleAssignmentCommand>()
        ));
        Assert.Throws<WordSemanticEditException>(() => planner.PlanStyleEdits(
            referencedPackage,
            referencedSemantic,
            [new WordStyleDeleteUnusedCommand("Base")],
            Array.Empty<WordStyleAssignmentCommand>()
        ));
        Assert.Throws<WordSemanticEditException>(() => planner.PlanStyleEdits(
            referencedPackage,
            referencedSemantic,
            [
                new WordStyleDeleteUnusedCommand("Target"),
                new WordStyleDeleteUnusedCommand("Target"),
            ],
            Array.Empty<WordStyleAssignmentCommand>()
        ));
        Assert.Throws<WordSemanticEditException>(() => planner.PlanStyleEdits(
            referencedPackage,
            referencedSemantic,
            [
                new WordStyleCloneCommand("Target", "Fresh", "Fresh"),
                new WordStyleDeleteUnusedCommand("Fresh"),
            ],
            Array.Empty<WordStyleAssignmentCommand>()
        ));
        Assert.Throws<WordSemanticEditException>(() => planner.PlanStyleEdits(
            referencedPackage,
            referencedSemantic,
            [
                new WordStyleConsolidateCommand("Source", "Target"),
                new WordStyleDeleteUnusedCommand("Target"),
            ],
            Array.Empty<WordStyleAssignmentCommand>()
        ));

        var usedDocument = $"<w:document xmlns:w='{WordNamespace}'><w:body><w:p><w:pPr><w:pStyle w:val='Target'/></w:pPr></w:p></w:body></w:document>";
        using var usedStream = BuildPackage(
            usedDocument,
            styles.Replace(
                "<w:basedOn w:val=\"Source\"/>",
                "<w:basedOn w:val=\"Base\"/>",
                StringComparison.Ordinal
            )
        );
        var usedPackage = new OpcPackageReader().Read(usedStream);
        var usedSemantic = new WordSemanticProjector().Project(usedPackage);
        Assert.Throws<WordSemanticEditException>(() => planner.PlanStyleEdits(
            usedPackage,
            usedSemantic,
            [new WordStyleDeleteUnusedCommand("Target")],
            Array.Empty<WordStyleAssignmentCommand>()
        ));
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

    private static string ConsolidationStylesXml() => $"""
        <w:styles xmlns:w="{WordNamespace}" xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml">
          <w:style w:type="paragraph" w:styleId="BasePara"><w:name w:val="Base paragraph"/></w:style>
          <w:style w:type="character" w:styleId="BaseChar"><w:name w:val="Base character"/></w:style>
          <w:style w:type="paragraph" w:styleId="SourcePara" w:customStyle="1" mc:Ignorable="w14"><w:name w:val="Source paragraph"/><w:aliases w:val="Source alias"/><w:basedOn w:val="BasePara"/><w:next w:val="SourcePara"/><w:link w:val="SourceChar"/><w:qFormat/><w:rsid w:val="11111111"/><w14:opaque w14:val="same"/><w:pPr><w:keepNext/></w:pPr><w:rPr><w:b/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="TargetPara" w:customStyle="1" mc:Ignorable="w14"><w:name w:val="Target paragraph"/><w:aliases w:val="Target alias"/><w:basedOn w:val="BasePara"/><w:next w:val="TargetPara"/><w:link w:val="TargetChar"/><w:qFormat/><w:rsid w:val="22222222"/><w14:opaque w14:val="same"/><w:pPr><w:keepNext/></w:pPr><w:rPr><w:b/></w:rPr></w:style>
          <w:style w:type="character" w:styleId="SourceChar" w:customStyle="1"><w:name w:val="Source character"/><w:basedOn w:val="BaseChar"/><w:link w:val="SourcePara"/><w:rPr><w:i/></w:rPr></w:style>
          <w:style w:type="character" w:styleId="TargetChar" w:customStyle="1"><w:name w:val="Target character"/><w:basedOn w:val="BaseChar"/><w:link w:val="TargetPara"/><w:rPr><w:i/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="DerivedPara" w:customStyle="1"><w:name w:val="Derived paragraph"/><w:basedOn w:val="SourcePara"/></w:style>
          <w:style w:type="character" w:styleId="DerivedChar" w:customStyle="1"><w:name w:val="Derived character"/><w:basedOn w:val="SourceChar"/></w:style>
          <w:style w:type="table" w:styleId="KeepTable"><w:name w:val="Keep table"/></w:style>
        </w:styles>
        """;

    private static MemoryStream BuildConsolidationPackage(
        string documentXml,
        string stylesXml,
        string numberingXml,
        string headerXml,
        string commentsXml,
        string footnotesXml,
        string glossaryXml
    )
    {
        var contentTypes = """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Default Extension="bin" ContentType="application/octet-stream"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
              <Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
              <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
              <Override PartName="/word/comments.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml"/>
              <Override PartName="/word/footnotes.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml"/>
              <Override PartName="/word/glossary/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.glossary+xml"/>
            </Types>
            """;
        var relationships = """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
              <Relationship Id="rIdNumbering" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
              <Relationship Id="rIdHeader" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
              <Relationship Id="rIdComments" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments" Target="comments.xml"/>
              <Relationship Id="rIdFootnotes" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes" Target="footnotes.xml"/>
              <Relationship Id="rIdGlossary" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/glossaryDocument" Target="glossary/document.xml"/>
            </Relationships>
            """;
        var entries = new (string Name, byte[] Content)[]
        {
            ("[Content_Types].xml", Encoding.UTF8.GetBytes(contentTypes)),
            ("_rels/.rels", Encoding.UTF8.GetBytes(RootRelationships())),
            ("word/document.xml", Encoding.UTF8.GetBytes(documentXml)),
            ("word/styles.xml", Encoding.UTF8.GetBytes(stylesXml)),
            ("word/numbering.xml", Encoding.UTF8.GetBytes(numberingXml)),
            ("word/header1.xml", Encoding.UTF8.GetBytes(headerXml)),
            ("word/comments.xml", Encoding.UTF8.GetBytes(commentsXml)),
            ("word/footnotes.xml", Encoding.UTF8.GetBytes(footnotesXml)),
            ("word/glossary/document.xml", Encoding.UTF8.GetBytes(glossaryXml)),
            ("word/_rels/document.xml.rels", Encoding.UTF8.GetBytes(relationships)),
            ("custom/opaque.bin", new byte[] { 9, 7, 5, 3, 1 }),
        };
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

    private static MemoryStream BuildPackage(
        string documentXml,
        string stylesXml,
        byte[]? opaque = null,
        string? stylesWithEffectsXml = null,
        string? settingsXml = null,
        byte[]? vbaProject = null,
        string? unmodeledXml = null
    )
    {
        var entries = new List<(string Name, byte[] Content)>
        {
            ("[Content_Types].xml", Encoding.UTF8.GetBytes(ContentTypes(
                opaque is not null,
                stylesWithEffectsXml is not null,
                settingsXml is not null,
                vbaProject is not null
            ))),
            ("_rels/.rels", Encoding.UTF8.GetBytes(RootRelationships())),
            ("word/document.xml", Encoding.UTF8.GetBytes(documentXml)),
            ("word/styles.xml", Encoding.UTF8.GetBytes(stylesXml)),
            ("word/_rels/document.xml.rels", Encoding.UTF8.GetBytes(DocumentRelationships(
                stylesWithEffectsXml is not null,
                settingsXml is not null,
                vbaProject is not null
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
        if (settingsXml is not null)
        {
            entries.Add(("word/settings.xml", Encoding.UTF8.GetBytes(settingsXml)));
        }
        if (vbaProject is not null)
        {
            entries.Add(("word/vbaProject.bin", vbaProject));
        }
        if (unmodeledXml is not null)
        {
            entries.Add(("custom/unmodeled.xml", Encoding.UTF8.GetBytes(unmodeledXml)));
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

    private static string ContentTypes(
        bool includeOpaque,
        bool includeEffects = false,
        bool includeSettings = false,
        bool includeVbaProject = false
    ) => $"""
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
          <Default Extension="xml" ContentType="application/xml" />
          {(includeOpaque || includeVbaProject ? "<Default Extension=\"bin\" ContentType=\"application/octet-stream\" />" : string.Empty)}
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" />
          <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml" />
          {(includeEffects ? "<Override PartName=\"/word/stylesWithEffects.xml\" ContentType=\"application/vnd.ms-word.stylesWithEffects+xml\" />" : string.Empty)}
          {(includeSettings ? "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\" />" : string.Empty)}
          {(includeVbaProject ? "<Override PartName=\"/word/vbaProject.bin\" ContentType=\"application/vnd.ms-office.vbaProject\" />" : string.Empty)}
        </Types>
        """;

    private static string RootRelationships() => """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml" />
        </Relationships>
        """;

    private static string DocumentRelationships(
        bool includeEffects = false,
        bool includeSettings = false,
        bool includeVbaProject = false
    ) => $"""
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml" />
          {(includeEffects ? "<Relationship Id=\"rIdEffects\" Type=\"http://schemas.microsoft.com/office/2007/relationships/stylesWithEffects\" Target=\"stylesWithEffects.xml\" />" : string.Empty)}
          {(includeSettings ? "<Relationship Id=\"rIdSettings\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\" />" : string.Empty)}
          {(includeVbaProject ? "<Relationship Id=\"rIdVba\" Type=\"http://schemas.microsoft.com/office/2006/relationships/vbaProject\" Target=\"vbaProject.bin\" />" : string.Empty)}
        </Relationships>
        """;
}
