using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.OpenXmlSdk;

namespace WordToolkit.Engine.Tests;

public sealed class WordNumberingRebuildTests
{
    private const string TransitionalWord =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string StrictWord =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";

    [Fact]
    public void CreatesMissingNumberingInfrastructureAndRebuildsAListAtomically()
    {
        using var bytes = BuildPackage(
            includeNumbering: false,
            documentBody: PlainParagraph("one")
                + PlainParagraph("two")
                + PlainParagraph("three")
        );
        var package = ReadPackage(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var paragraphs = Paragraphs(semantic);
        var planner = new WordNumberingRebuildPlanner();
        var inspected = new WordNumberingRebuildCandidateInspector().Inspect(
            package,
            semantic,
            paragraphs.Select(paragraph => paragraph.Id).ToArray()
        );
        var candidates = inspected.ToDictionary(candidate => candidate.ParagraphNodeId);

        var plan = planner.Plan(
            package,
            semantic,
            [new WordNumberingRebuildCommand(
                "fresh-list",
                WordNumberingRebuildMultiLevelKind.SingleLevel,
                RestartAfterSectionBreak: false,
                Levels: [new WordNumberingRebuildLevel(
                    0,
                    1,
                    WordNumberingRebuildFormat.Decimal,
                    "%1."
                )],
                Targets: paragraphs.Select(paragraph => new WordNumberingRebuildTarget(
                    paragraph.Id,
                    candidates[paragraph.Id].Fingerprint,
                    0
                )).ToArray()
            )]
        );

        Assert.True(plan.Validation.Passed);
        Assert.True(plan.NumberingPartCreated);
        Assert.Equal("/word/numbering.xml", plan.NumberingPartUri);
        Assert.Equal(4, plan.ChangedEntries.Count);
        Assert.Equal(1, plan.Commands.Single().NumberId);
        Assert.Equal(0, plan.Commands.Single().AbstractNumberId);
        Assert.All(plan.Commands.Single().Targets, target =>
        {
            Assert.Equal(WordListCounterStatus.Exact, target.CounterStatus);
            Assert.Equal(WordListLabelStatus.Exact, target.LabelStatus);
        });
        using var otherBytes = BuildPackage(
            includeNumbering: false,
            documentBody: PlainParagraph("different")
        );
        var otherPackage = ReadPackage(otherBytes);
        Assert.Throws<WordSemanticPreconditionException>(() =>
            plan.CreateMutation(otherPackage)
        );
        Assert.Throws<WordSemanticPreconditionException>(() =>
            plan.CreateInverseMutation(package)
        );

        var candidate = Apply(package, plan.CreateMutation(package));
        Assert.True(candidate.Parts.ContainsKey("/word/numbering.xml"));
        Assert.Contains(candidate.RelationshipsFrom("/word/document.xml"), relationship =>
            relationship.ResolvedTargetPartUri == "/word/numbering.xml"
        );
        Assert.Equal(
            new string?[] { "1.", "2.", "3." },
            BuildSequence(candidate).Items.Select(item => item.Label).ToArray()
        );
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml",
            candidate.Parts["/word/numbering.xml"].ContentType
        );

        var restored = Apply(candidate, plan.CreateInverseMutation(candidate));
        Assert.Equal(package.Fingerprint, restored.Fingerprint);
    }

    [Fact]
    public void AppendsAnIndependentMultilevelDefinitionAndPreservesExistingSequence()
    {
        var existingNumbering = NumberingXml(
            TransitionalWord,
            """
            <w:abstractNum w:abstractNumId="1"><w:lvl w:ilvl="0"><w:start w:val="4"/><w:numFmt w:val="decimal"/><w:lvlText w:val="old-%1"/></w:lvl></w:abstractNum>
            <w:num w:numId="5"><w:abstractNumId w:val="1"/></w:num>
            <w:numIdMacAtCleanup w:val="5"/>
            """
        );
        using var bytes = BuildPackage(
            includeNumbering: true,
            numberingXml: existingNumbering,
            documentBody: NumberedParagraph(5, 0, "untouched")
                + PlainParagraph("chapter")
                + PlainParagraph("child-a")
                + PlainParagraph("child-b")
                + PlainParagraph("next")
        );
        var package = ReadPackage(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var paragraphs = Paragraphs(semantic);
        var targets = paragraphs.Skip(1).ToArray();
        var inspected = new WordNumberingRebuildCandidateInspector().Inspect(
            package,
            semantic,
            targets.Select(paragraph => paragraph.Id).ToArray()
        ).ToDictionary(candidate => candidate.ParagraphNodeId);
        var requestedLevels = new[] { 0, 1, 1, 0 };

        var plan = new WordNumberingRebuildPlanner().Plan(
            package,
            semantic,
            [new WordNumberingRebuildCommand(
                "outline",
                WordNumberingRebuildMultiLevelKind.Multilevel,
                RestartAfterSectionBreak: true,
                Levels:
                [
                    new WordNumberingRebuildLevel(
                        0,
                        1,
                        WordNumberingRebuildFormat.Decimal,
                        "%1."
                    ),
                    new WordNumberingRebuildLevel(
                        1,
                        1,
                        WordNumberingRebuildFormat.LowerLetter,
                        "%1.%2)"
                    ),
                ],
                Targets: targets.Select((paragraph, index) =>
                    new WordNumberingRebuildTarget(
                        paragraph.Id,
                        inspected[paragraph.Id].Fingerprint,
                        requestedLevels[index]
                    )
                ).ToArray()
            )]
        );

        Assert.True(plan.Validation.Passed);
        Assert.False(plan.NumberingPartCreated);
        Assert.Equal(2, plan.Commands.Single().AbstractNumberId);
        Assert.Equal(6, plan.Commands.Single().NumberId);
        Assert.True(plan.Validation.UnselectedNumberingPreserved);
        Assert.True(plan.Validation.UnaffectedSequencesPreserved);

        var candidate = Apply(package, plan.CreateMutation(package));
        Assert.Equal(
            new[]
            {
                (5, "old-4"),
                (6, "1."),
                (6, "1.a)"),
                (6, "1.b)"),
                (6, "2."),
            },
            BuildSequence(candidate).Items.Select(item => (item.NumberId, item.Label!)).ToArray()
        );
        var numbering = XDocument.Parse(Encoding.UTF8.GetString(
            candidate.Parts["/word/numbering.xml"].Entry.Content.Span
        ));
        XNamespace w = TransitionalWord;
        Assert.Equal(2, numbering.Root!.Elements(w + "abstractNum").Count());
        Assert.Equal(2, numbering.Root.Elements(w + "num").Count());
        Assert.Equal("5", numbering.Root.Element(w + "numIdMacAtCleanup")?
            .Attribute(w + "val")?.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreatesAValidMissingNumberingPartForTransitionalAndStrict(bool strict)
    {
        var wordNamespace = strict ? StrictWord : TransitionalWord;
        using var bytes = BuildPackage(
            includeNumbering: false,
            strict: strict,
            documentBody: PlainParagraph("strict-or-transitional")
        );
        var package = ReadPackage(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var paragraph = Paragraphs(semantic).Single();
        var inspected = new WordNumberingRebuildCandidateInspector().Inspect(
            package,
            semantic,
            [paragraph.Id]
        ).Single();
        var plan = new WordNumberingRebuildPlanner().Plan(
            package,
            semantic,
            [new WordNumberingRebuildCommand(
                "dialect",
                WordNumberingRebuildMultiLevelKind.SingleLevel,
                true,
                [new WordNumberingRebuildLevel(
                    0,
                    3,
                    WordNumberingRebuildFormat.UpperRoman,
                    "%1)"
                )],
                [new WordNumberingRebuildTarget(paragraph.Id, inspected.Fingerprint, 0)]
            )]
        );
        var candidate = Apply(package, plan.CreateMutation(package));
        var numbering = XDocument.Parse(Encoding.UTF8.GetString(
            candidate.Parts["/word/numbering.xml"].Entry.Content.Span
        ));

        Assert.True(plan.Validation.Passed);
        Assert.Equal(wordNamespace, numbering.Root!.Name.NamespaceName);
        Assert.Equal("III)", BuildSequence(candidate).Items.Single().Label);
        Assert.Contains(candidate.RelationshipsFrom("/word/document.xml"), relationship =>
            relationship.Type == (strict
                ? "http://purl.oclc.org/ooxml/officeDocument/relationships/numbering"
                : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering")
        );
        using var baselineStream = Serialize(package);
        using var candidateStream = Serialize(candidate);
        var validation = new MicrosoftOpenXmlPackageValidator().Validate(
            baselineStream,
            candidateStream
        );
        Assert.True(validation.CandidateValid);
        Assert.True(validation.NoNewErrors);
    }

    [Fact]
    public void RejectsStaleFingerprintsRevisionAncestryAndInvalidBlueprints()
    {
        using var bytes = BuildPackage(
            includeNumbering: false,
            documentBody: PlainParagraph("safe")
                + "<w:ins w:id=\"1\" w:author=\"A\">"
                + PlainParagraph("tracked")
                + "</w:ins>"
        );
        var package = ReadPackage(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var paragraphs = Paragraphs(semantic);
        var inspector = new WordNumberingRebuildCandidateInspector();
        var candidates = inspector.Inspect(
            package,
            semantic,
            paragraphs.Select(paragraph => paragraph.Id).ToArray()
        );
        Assert.True(candidates[0].CanRebuild);
        Assert.False(candidates[1].CanRebuild);

        var stale = new WordNumberingRebuildCommand(
            "stale",
            WordNumberingRebuildMultiLevelKind.SingleLevel,
            false,
            [new WordNumberingRebuildLevel(
                0,
                1,
                WordNumberingRebuildFormat.Decimal,
                "%1."
            )],
            [new WordNumberingRebuildTarget(paragraphs[0].Id, "wnrb_stale", 0)]
        );
        Assert.Throws<WordSemanticPreconditionException>(() =>
            new WordNumberingRebuildPlanner().Plan(package, semantic, [stale])
        );

        var tracked = stale with
        {
            CommandId = "tracked",
            Targets = [new WordNumberingRebuildTarget(
                paragraphs[1].Id,
                candidates[1].Fingerprint,
                0
            )],
        };
        Assert.Throws<WordSemanticEditException>(() =>
            new WordNumberingRebuildPlanner().Plan(package, semantic, [tracked])
        );

        Assert.Throws<ArgumentException>(() => new WordNumberingRebuildPlanner().Plan(
            package,
            semantic,
            [stale with
            {
                CommandId = "invalid",
                Levels = [new WordNumberingRebuildLevel(
                    0,
                    1,
                    WordNumberingRebuildFormat.Decimal,
                    "%2."
                )],
                Targets = [new WordNumberingRebuildTarget(
                    paragraphs[0].Id,
                    candidates[0].Fingerprint,
                    0
                )],
            }]
        ));
    }

    [Fact]
    public void RebuildsOneDefinitionAcrossMainAndHeaderStoriesWithIndependentCounters()
    {
        using var bytes = BuildMultiStoryPackage();
        var package = ReadPackage(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var paragraphs = Paragraphs(semantic);
        Assert.Equal(2, paragraphs.Length);
        var candidates = new WordNumberingRebuildCandidateInspector().Inspect(
            package,
            semantic,
            paragraphs.Select(item => item.Id).ToArray()
        ).ToDictionary(item => item.ParagraphNodeId);
        var plan = new WordNumberingRebuildPlanner().Plan(
            package,
            semantic,
            [new WordNumberingRebuildCommand(
                "cross-story",
                WordNumberingRebuildMultiLevelKind.SingleLevel,
                false,
                [new WordNumberingRebuildLevel(
                    0,
                    1,
                    WordNumberingRebuildFormat.Decimal,
                    "%1."
                )],
                paragraphs.Select(item => new WordNumberingRebuildTarget(
                    item.Id,
                    candidates[item.Id].Fingerprint,
                    0
                )).ToArray()
            )]
        );
        var candidate = Apply(package, plan.CreateMutation(package));
        var sequence = BuildSequence(candidate);

        Assert.True(plan.Validation.Passed);
        Assert.Equal(5, plan.ChangedEntries.Count);
        Assert.Equal(
            new[] { WordStoryKind.Main, WordStoryKind.Header },
            plan.Commands.Single().Targets.Select(item => item.StoryKind).ToArray()
        );
        Assert.Equal(new string?[] { "1.", "1." }, sequence.Items.Select(item => item.Label).ToArray());
        Assert.Equal(
            new[] { WordStoryKind.Main, WordStoryKind.Header },
            sequence.Items.Select(item => item.StoryKind).ToArray()
        );
        var restored = Apply(candidate, plan.CreateInverseMutation(candidate));
        Assert.Equal(package.Fingerprint, restored.Fingerprint);
    }

    [Fact]
    public void RebuildsMoreThanOneInspectionPageWithoutWeakeningCandidateBinding()
    {
        var body = string.Concat(Enumerable.Range(1, 205).Select(index =>
            PlainParagraph("p" + index.ToString(System.Globalization.CultureInfo.InvariantCulture))
        ));
        using var bytes = BuildPackage(includeNumbering: false, documentBody: body);
        var package = ReadPackage(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var paragraphs = Paragraphs(semantic);
        var inspector = new WordNumberingRebuildCandidateInspector();
        var candidates = paragraphs.Chunk(100).SelectMany(batch => inspector.Inspect(
            package,
            semantic,
            batch.Select(item => item.Id).ToArray()
        )).ToDictionary(item => item.ParagraphNodeId);
        var plan = new WordNumberingRebuildPlanner().Plan(
            package,
            semantic,
            [new WordNumberingRebuildCommand(
                "paged-list",
                WordNumberingRebuildMultiLevelKind.SingleLevel,
                false,
                [new WordNumberingRebuildLevel(
                    0,
                    1,
                    WordNumberingRebuildFormat.Decimal,
                    "%1."
                )],
                paragraphs.Select(item => new WordNumberingRebuildTarget(
                    item.Id,
                    candidates[item.Id].Fingerprint,
                    0
                )).ToArray()
            )]
        );
        var candidate = Apply(package, plan.CreateMutation(package));
        var items = BuildSequence(candidate).Items;

        Assert.True(plan.Validation.Passed);
        Assert.Equal(205, plan.TargetCount);
        Assert.Equal(4, plan.ChangedEntries.Count);
        Assert.Equal("1.", items[0].Label);
        Assert.Equal("205.", items[^1].Label);
    }

    [Fact]
    public void BlocksMarkupCompatibilityTrackedPropertiesAndSignedPackages()
    {
        var body =
            "<mc:AlternateContent xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\"><mc:Choice Requires=\"w14\">"
            + PlainParagraph("choice")
            + "</mc:Choice><mc:Fallback>"
            + PlainParagraph("fallback")
            + "</mc:Fallback></mc:AlternateContent>"
            + "<w:p><w:pPr><w:pPrChange w:id=\"1\" w:author=\"A\"><w:pPr/></w:pPrChange></w:pPr><w:r><w:t>tracked-properties</w:t></w:r></w:p>"
            + "<w:p><w:pPr><w:numPr><w:numberingChange w:id=\"2\" w:author=\"A\"/></w:numPr></w:pPr><w:r><w:t>tracked-numbering</w:t></w:r></w:p>";
        using var bytes = BuildPackage(includeNumbering: false, documentBody: body);
        var package = ReadPackage(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var paragraphs = Paragraphs(semantic);
        var candidates = new WordNumberingRebuildCandidateInspector().Inspect(
            package,
            semantic,
            paragraphs.Select(item => item.Id).ToArray()
        );

        Assert.All(candidates, candidate => Assert.False(candidate.CanRebuild));
        Assert.Contains(candidates, candidate => candidate.BlockedReasons.Contains(
            "revision_or_markup_compatibility_ancestry"
        ));
        Assert.Contains(candidates, candidate => candidate.BlockedReasons.Contains(
            "tracked_paragraph_properties"
        ));
        Assert.Contains(candidates, candidate => candidate.BlockedReasons.Contains(
            "tracked_or_unmodeled_numbering_properties"
        ));

        using var signedBytes = BuildPackage(
            includeNumbering: false,
            documentBody: PlainParagraph("signed")
        );
        using (var archive = new ZipArchive(signedBytes, ZipArchiveMode.Update, leaveOpen: true))
        {
            WriteEntry(archive, "_xmlsignatures/sig1.xml", "<Signature/>");
        }
        var signed = ReadPackage(signedBytes);
        var signedSemantic = new WordSemanticProjector().Project(signed);
        var signedParagraph = Paragraphs(signedSemantic).Single();
        var signedCandidate = new WordNumberingRebuildCandidateInspector().Inspect(
            signed,
            signedSemantic,
            [signedParagraph.Id]
        ).Single();
        Assert.Throws<WordSemanticEditException>(() =>
            new WordNumberingRebuildPlanner().Plan(
                signed,
                signedSemantic,
                [new WordNumberingRebuildCommand(
                    "signed",
                    WordNumberingRebuildMultiLevelKind.SingleLevel,
                    false,
                    [new WordNumberingRebuildLevel(
                        0,
                        1,
                        WordNumberingRebuildFormat.Decimal,
                        "%1."
                    )],
                    [new WordNumberingRebuildTarget(
                        signedParagraph.Id,
                        signedCandidate.Fingerprint,
                        0
                    )]
                )]
            )
        );
    }

    [Theory]
    [InlineData(WordNumberingRebuildFormat.Decimal, "%1 <&", "1 <&")]
    [InlineData(WordNumberingRebuildFormat.DecimalZero, "%1", "01")]
    [InlineData(WordNumberingRebuildFormat.UpperRoman, "%1)", "I)")]
    [InlineData(WordNumberingRebuildFormat.LowerRoman, "%1)", "i)")]
    [InlineData(WordNumberingRebuildFormat.UpperLetter, "%1)", "A)")]
    [InlineData(WordNumberingRebuildFormat.LowerLetter, "%1)", "a)")]
    [InlineData(WordNumberingRebuildFormat.Bullet, "•", "•")]
    [InlineData(WordNumberingRebuildFormat.None, "", "")]
    public void SupportedFormatsAreDeterministicEscapedAndExactlyInvertible(
        WordNumberingRebuildFormat format,
        string levelText,
        string? expectedLabel
    )
    {
        using var bytes = BuildPackage(
            includeNumbering: false,
            documentBody: PlainParagraph("format")
        );
        var package = ReadPackage(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var paragraph = Paragraphs(semantic).Single();
        var inspected = new WordNumberingRebuildCandidateInspector().Inspect(
            package,
            semantic,
            [paragraph.Id]
        ).Single();
        var command = new WordNumberingRebuildCommand(
            "format-" + format,
            WordNumberingRebuildMultiLevelKind.SingleLevel,
            false,
            [new WordNumberingRebuildLevel(0, 1, format, levelText)],
            [new WordNumberingRebuildTarget(paragraph.Id, inspected.Fingerprint, 0)]
        );
        var planner = new WordNumberingRebuildPlanner();
        var first = planner.Plan(package, semantic, [command]);
        var second = planner.Plan(package, semantic, [command]);
        var candidate = Apply(package, first.CreateMutation(package));
        var item = BuildSequence(candidate).Items.Single();
        var restored = Apply(candidate, first.CreateInverseMutation(candidate));

        Assert.Equal(first.PlanId, second.PlanId);
        Assert.Equal(first.ResultPackageFingerprint, second.ResultPackageFingerprint);
        Assert.Equal(expectedLabel, item.Label);
        Assert.Equal(WordListLabelStatus.Exact, item.LabelStatus);
        Assert.Equal(package.Fingerprint, restored.Fingerprint);
    }

    [Fact]
    public void MaterializesOnlyTheTargetWhenNumberingWasInheritedFromAStyle()
    {
        var numbering = NumberingXml(
            TransitionalWord,
            "<w:abstractNum w:abstractNumId=\"1\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"2\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"style-%1\"/></w:lvl></w:abstractNum><w:num w:numId=\"5\"><w:abstractNumId w:val=\"1\"/></w:num>"
        );
        var styles =
            "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:style w:type=\"paragraph\" w:styleId=\"ListStyle\"><w:name w:val=\"List Style\"/><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"5\"/></w:numPr></w:pPr></w:style></w:styles>";
        using var bytes = BuildPackage(
            includeNumbering: true,
            documentBody: StyledParagraph("ListStyle", "untouched")
                + StyledParagraph("ListStyle", "target"),
            numberingXml: numbering,
            stylesXml: styles
        );
        var package = ReadPackage(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var target = Paragraphs(semantic)[1];
        var inspected = new WordNumberingRebuildCandidateInspector().Inspect(
            package,
            semantic,
            [target.Id]
        ).Single();
        var plan = new WordNumberingRebuildPlanner().Plan(
            package,
            semantic,
            [new WordNumberingRebuildCommand(
                "style-override",
                WordNumberingRebuildMultiLevelKind.SingleLevel,
                false,
                [new WordNumberingRebuildLevel(
                    0,
                    1,
                    WordNumberingRebuildFormat.Decimal,
                    "new-%1"
                )],
                [new WordNumberingRebuildTarget(target.Id, inspected.Fingerprint, 0)]
            )]
        );
        var candidate = Apply(package, plan.CreateMutation(package));
        var items = BuildSequence(candidate).Items;

        Assert.True(plan.Validation.Passed);
        Assert.Null(plan.Commands.Single().Targets.Single().PreviousNumberId);
        Assert.True(plan.Commands.Single().Targets.Single().DirectNumberingMaterialized);
        Assert.Equal(new[] { (5, "style-2"), (6, "new-1") }, items.Select(item =>
            (item.NumberId, item.Label!)
        ).ToArray());
        Assert.True(plan.Validation.UnselectedNumberingPreserved);
        Assert.True(plan.Validation.UnaffectedSequencesPreserved);
    }

    private static WordSemanticNode[] Paragraphs(WordSemanticDocument semantic) =>
        semantic.Nodes.Where(node => node.Kind == WordSemanticNodeKind.Paragraph).ToArray();

    private static WordListSequenceGraph BuildSequence(OpcPackageSnapshot package)
    {
        var semantic = new WordSemanticProjector().Project(package);
        var styles = new WordStyleGraphBuilder().Build(package, semantic);
        var numbering = new WordNumberingGraphBuilder().Build(package, semantic, styles);
        return new WordListSequenceGraphBuilder().Build(package, semantic, styles, numbering);
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

    private static MemoryStream Serialize(OpcPackageSnapshot package)
    {
        var output = new MemoryStream();
        new OpcPackageSerializer().Write(output, new OpcPackageMutationBuilder(package));
        output.Position = 0;
        return output;
    }

    private static OpcPackageSnapshot ReadPackage(Stream stream)
    {
        stream.Position = 0;
        return new OpcPackageReader().Read(stream);
    }

    private static MemoryStream BuildPackage(
        bool includeNumbering,
        string documentBody,
        string? numberingXml = null,
        bool strict = false,
        string? stylesXml = null
    )
    {
        var word = strict ? StrictWord : TransitionalWord;
        var officeRelationship = strict
            ? "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument"
            : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
        var numberingRelationship = strict
            ? "http://purl.oclc.org/ooxml/officeDocument/relationships/numbering"
            : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering";
        var numberingOverride = includeNumbering
            ? "<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/>"
            : string.Empty;
        var stylesOverride = stylesXml is null
            ? string.Empty
            : "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>";
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                "[Content_Types].xml",
                $"<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>{numberingOverride}{stylesOverride}</Types>"
            );
            WriteEntry(
                archive,
                "_rels/.rels",
                $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"{officeRelationship}\" Target=\"word/document.xml\"/></Relationships>"
            );
            WriteEntry(
                archive,
                "word/document.xml",
                $"<w:document xmlns:w=\"{word}\"><w:body>{documentBody}</w:body></w:document>"
            );
            var numberingRelation = includeNumbering
                ? $"<Relationship Id=\"rIdNumbering\" Type=\"{numberingRelationship}\" Target=\"numbering.xml\"/>"
                : string.Empty;
            var stylesRelation = stylesXml is null
                ? string.Empty
                : $"<Relationship Id=\"rIdStyles\" Type=\"{(strict ? "http://purl.oclc.org/ooxml/officeDocument/relationships/styles" : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles")}\" Target=\"styles.xml\"/>";
            WriteEntry(
                archive,
                "word/_rels/document.xml.rels",
                $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{numberingRelation}{stylesRelation}</Relationships>"
            );
            if (includeNumbering)
            {
                WriteEntry(
                    archive,
                    "word/numbering.xml",
                    numberingXml ?? NumberingXml(word, string.Empty)
                );
            }
            if (stylesXml is not null)
            {
                WriteEntry(archive, "word/styles.xml", stylesXml);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream BuildMultiStoryPackage()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml",
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/><Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/></Types>");
            WriteEntry(archive, "_rels/.rels",
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            WriteEntry(archive, "word/document.xml",
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><w:body><w:p><w:r><w:t>main</w:t></w:r></w:p><w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdHeader\"/></w:sectPr></w:body></w:document>");
            WriteEntry(archive, "word/header1.xml",
                "<w:hdr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:r><w:t>header</w:t></w:r></w:p></w:hdr>");
            WriteEntry(archive, "word/_rels/document.xml.rels",
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rIdHeader\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header1.xml\"/></Relationships>");
        }
        stream.Position = 0;
        return stream;
    }

    private static string NumberingXml(string wordNamespace, string content) =>
        $"<w:numbering xmlns:w=\"{wordNamespace}\">{content}</w:numbering>";

    private static string PlainParagraph(string text) =>
        $"<w:p><w:r><w:t>{text}</w:t></w:r></w:p>";

    private static string NumberedParagraph(int numberId, int levelIndex, string text) =>
        $"<w:p><w:pPr><w:numPr><w:ilvl w:val=\"{levelIndex}\"/><w:numId w:val=\"{numberId}\"/></w:numPr></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>";

    private static string StyledParagraph(string styleId, string text) =>
        $"<w:p><w:pPr><w:pStyle w:val=\"{styleId}\"/></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>";

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }
}
