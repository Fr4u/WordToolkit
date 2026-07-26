using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordNoteGraphTests
{
    private const string TransitionalWord =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string TransitionalRelationships =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string StrictWord =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string StrictRelationships =
        "http://purl.oclc.org/ooxml/officeDocument/relationships";

    [Fact]
    public void BuildsHealthyGraphWithoutAssumingMagicSpecialNoteIds()
    {
        using var package = BuildPackage(
            TransitionalWord,
            TransitionalRelationships,
            """
            <w:p><w:r><w:t>Main</w:t><w:footnoteReference w:id="2"/></w:r></w:p>
            <w:p><w:r><w:endnoteReference w:id="3"/></w:r></w:p>
            <w:sectPr>
              <w:footnotePr><w:pos w:val="beneathText"/><w:numRestart w:val="eachSect"/></w:footnotePr>
              <w:endnotePr><w:pos w:val="docEnd"/><w:numStart w:val="4"/></w:endnotePr>
            </w:sectPr>
            """,
            """
            <w:footnote w:type="separator" w:id="-1"><w:p><w:r><w:separator/></w:r></w:p></w:footnote>
            <w:footnote w:type="continuationSeparator" w:id="0"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:footnote>
            <w:footnote w:id="2"><w:p><w:r><w:footnoteRef/><w:t>Footnote</w:t></w:r></w:p></w:footnote>
            """,
            """
            <w:endnote w:type="separator" w:id="-1"><w:p><w:r><w:separator/></w:r></w:p></w:endnote>
            <w:endnote w:type="continuationSeparator" w:id="0"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:endnote>
            <w:endnote w:id="3"><w:p><w:r><w:endnoteRef/><w:t>Endnote</w:t></w:r></w:p></w:endnote>
            """,
            """
            <w:footnotePr>
              <w:pos w:val="pageBottom"/><w:numFmt w:val="decimal"/><w:numStart w:val="1"/><w:numRestart w:val="continuous"/>
              <w:footnote w:id="-1"/><w:footnote w:id="0"/>
            </w:footnotePr>
            <w:endnotePr>
              <w:numFmt w:val="lowerRoman"/><w:endnote w:id="-1"/><w:endnote w:id="0"/>
            </w:endnotePr>
            """
        );

        var graph = BuildGraph(package);

        Assert.True(graph.AnalysisExecutionComplete);
        Assert.True(graph.DocumentCoverageComplete);
        Assert.False(graph.IssuesTruncated);
        Assert.Equal(6, graph.Definitions.Count);
        Assert.Equal(2, graph.References.Count);
        Assert.Equal(4, graph.SpecialReferences.Count);
        Assert.Equal(4, graph.NumberingPolicies.Count);
        Assert.DoesNotContain(graph.Issues, issue => issue.Severity == WordNoteIssueSeverity.Error);
        Assert.All(graph.References, reference => Assert.Equal("resolved", reference.ResolutionStatus));
        Assert.All(graph.SpecialReferences, reference => Assert.Equal("resolved", reference.ResolutionStatus));
        Assert.Contains(
            graph.Definitions,
            definition => definition.OoxmlId == -1
                && definition.DefinitionType == WordNoteDefinitionType.Separator
        );
        Assert.Contains(
            graph.Definitions,
            definition => definition.OoxmlId == 0
                && definition.DefinitionType == WordNoteDefinitionType.ContinuationSeparator
        );
    }

    [Fact]
    public void ReportsBrokenGraphAndOnlyMarksConservativeRemovalCandidates()
    {
        using var package = BuildPackage(
            TransitionalWord,
            TransitionalRelationships,
            """
            <w:p><w:r><w:footnoteReference w:id="2" w:customMarkFollows="banana"/></w:r></w:p>
            <w:p><w:r><w:footnoteReference w:id="9"/></w:r></w:p>
            <w:p><w:r><w:footnoteReference w:id="bad"/></w:r></w:p>
            <w:p><w:r><w:footnoteReference w:id="8"/></w:r></w:p>
            """,
            """
            <w:footnote w:id="2"><w:p><w:r><w:footnoteRef/></w:r></w:p></w:footnote>
            <w:footnote w:id="2"><w:p><w:r><w:footnoteRef/></w:r></w:p></w:footnote>
            <w:footnote w:id="4"><w:p><w:r><w:footnoteRef/></w:r></w:p></w:footnote>
            <w:footnote w:id="5"><w:p><w:r><w:footnoteRef/><w:t>Keep me</w:t></w:r></w:p></w:footnote>
            <w:footnote w:id="bad"><w:p/></w:footnote>
            <w:footnote w:id="6"><w:p><w:r><w:footnoteReference w:id="2"/></w:r></w:p></w:footnote>
            <w:footnote w:type="separator" w:id="8"><w:p><w:r><w:separator/></w:r></w:p></w:footnote>
            <w:footnote w:type="futureType" w:id="7"><w:p/></w:footnote>
            """,
            endnotes: null,
            settings: null
        );

        var graph = BuildGraph(package);

        Assert.Contains(graph.Issues, issue => issue.Code == "NOTE_DEFINITION_ID_DUPLICATE");
        Assert.Contains(graph.Issues, issue => issue.Code == "NOTE_REFERENCE_DEFINITION_MISSING");
        Assert.Contains(graph.Issues, issue => issue.Code == "NOTE_REFERENCE_ID_INVALID");
        Assert.Contains(graph.Issues, issue => issue.Code == "NOTE_DEFINITION_ID_INVALID");
        Assert.Contains(graph.Issues, issue => issue.Code == "NOTE_REFERENCE_CUSTOM_MARK_VALUE_INVALID");
        Assert.Contains(graph.Issues, issue => issue.Code == "NOTE_REFERENCE_NESTED_IN_NOTE");
        Assert.Contains(graph.Issues, issue => issue.Code == "NOTE_REFERENCE_TARGET_SPECIAL");
        Assert.Contains(graph.Issues, issue => issue.Code == "NOTE_DEFINITION_TYPE_UNKNOWN");
        Assert.Contains(graph.Issues, issue => issue.Code == "NOTE_SPECIAL_DEFINITION_UNREFERENCED");

        var emptyOrphan = Assert.Single(graph.Definitions, definition => definition.OoxmlId == 4);
        Assert.True(emptyOrphan.IsOrphan);
        Assert.True(emptyOrphan.EmptyOrphanRemovalCandidate);

        var contentfulOrphan = Assert.Single(graph.Definitions, definition => definition.OoxmlId == 5);
        Assert.True(contentfulOrphan.IsOrphan);
        Assert.False(contentfulOrphan.EmptyOrphanRemovalCandidate);

        var duplicates = graph.Definitions.Where(definition => definition.OoxmlId == 2).ToArray();
        Assert.Equal(2, duplicates.Length);
        Assert.False(duplicates[0].RedundantDuplicateRemovalCandidate);
        Assert.True(duplicates[1].RedundantDuplicateRemovalCandidate);
    }

    [Fact]
    public void RefusesAutomaticDuplicateRepairWhenDefinitionsDiffer()
    {
        using var package = BuildPackage(
            TransitionalWord,
            TransitionalRelationships,
            "<w:p><w:r><w:footnoteReference w:id=\"2\"/></w:r></w:p>",
            """
            <w:footnote w:id="2"><w:p><w:r><w:footnoteRef/><w:t>One</w:t></w:r></w:p></w:footnote>
            <w:footnote w:id="2"><w:p><w:r><w:footnoteRef/><w:t>Two</w:t></w:r></w:p></w:footnote>
            """,
            endnotes: null,
            settings: null
        );

        var graph = BuildGraph(package);

        Assert.All(
            graph.Definitions.Where(definition => definition.OoxmlId == 2),
            definition => Assert.False(definition.RedundantDuplicateRemovalCandidate)
        );
        var issue = Assert.Single(
            graph.Issues,
            issue => issue.Code == "NOTE_DEFINITION_ID_DUPLICATE"
        );
        Assert.False(issue.RepairCandidate);
    }

    [Fact]
    public void ReportsDuplicateSpecialReferences()
    {
        using var package = BuildPackage(
            TransitionalWord,
            TransitionalRelationships,
            "<w:p/>",
            "<w:footnote w:type=\"separator\" w:id=\"-1\"><w:p><w:r><w:separator/></w:r></w:p></w:footnote>",
            endnotes: null,
            settings: "<w:footnotePr><w:footnote w:id=\"-1\"/><w:footnote w:id=\"-1\"/></w:footnotePr>"
        );

        var graph = BuildGraph(package);

        Assert.Equal(2, graph.SpecialReferences.Count);
        Assert.All(graph.SpecialReferences, reference => Assert.Equal("resolved", reference.ResolutionStatus));
        Assert.Contains(graph.Issues, issue => issue.Code == "NOTE_SPECIAL_REFERENCE_DUPLICATE");
    }

    [Fact]
    public void SupportsStrictNamespacesAndRelationships()
    {
        using var package = BuildPackage(
            StrictWord,
            StrictRelationships,
            "<w:p><w:r><w:footnoteReference w:id=\"7\"/></w:r></w:p>",
            "<w:footnote w:id=\"7\"><w:p><w:r><w:footnoteRef/><w:t>Strict</w:t></w:r></w:p></w:footnote>",
            endnotes: null,
            settings: null
        );

        var graph = BuildGraph(package);

        Assert.True(graph.DocumentCoverageComplete);
        Assert.Equal("resolved", Assert.Single(graph.References).ResolutionStatus);
        Assert.DoesNotContain(graph.Issues, issue => issue.Severity == WordNoteIssueSeverity.Error);
    }

    [Fact]
    public void MarksCoverageIncompleteForAmbiguousNoteRelationships()
    {
        using var package = BuildPackage(
            TransitionalWord,
            TransitionalRelationships,
            "<w:p/>",
            "<w:footnote w:id=\"2\"><w:p/></w:footnote>",
            endnotes: null,
            settings: null,
            extraDocumentRelationships:
                $"<Relationship Id=\"rIdFootnotes2\" Type=\"{TransitionalRelationships}/footnotes\" Target=\"footnotes.xml\"/>"
        );

        var graph = BuildGraph(package);

        Assert.False(graph.DocumentCoverageComplete);
        Assert.Contains(graph.Issues, issue => issue.Code == "NOTE_PART_RELATIONSHIP_AMBIGUOUS");
    }

    [Fact]
    public void RejectsSectionScopedSpecialReferencesInsteadOfMisclassifyingThem()
    {
        using var package = BuildPackage(
            TransitionalWord,
            TransitionalRelationships,
            """
            <w:sectPr><w:footnotePr><w:footnote w:id="-1"/></w:footnotePr></w:sectPr>
            """,
            "<w:footnote w:type=\"separator\" w:id=\"-1\"><w:p/></w:footnote>",
            endnotes: null,
            settings: null
        );

        var graph = BuildGraph(package);

        Assert.Empty(graph.SpecialReferences);
        Assert.Contains(graph.Issues, issue => issue.Code == "NOTE_SECTION_SPECIAL_REFERENCE_INVALID");
    }

    [Fact]
    public void MarksDuplicatePropertyScopesAsIncompleteCoverage()
    {
        using var package = BuildPackage(
            TransitionalWord,
            TransitionalRelationships,
            "<w:sectPr><w:endnotePr/><w:endnotePr/></w:sectPr>",
            footnotes: null,
            endnotes: null,
            settings: "<w:footnotePr/><w:footnotePr/>"
        );

        var graph = BuildGraph(package);

        Assert.False(graph.DocumentCoverageComplete);
        Assert.Contains(graph.Issues, issue => issue.Code == "NOTE_DOCUMENT_PROPERTIES_DUPLICATE");
        Assert.Contains(graph.Issues, issue => issue.Code == "NOTE_SECTION_PROPERTIES_DUPLICATE");
    }

    [Fact]
    public void RejectsPageRestartForEndnotes()
    {
        using var package = BuildPackage(
            TransitionalWord,
            TransitionalRelationships,
            "<w:sectPr><w:endnotePr><w:numRestart w:val=\"eachPage\"/></w:endnotePr></w:sectPr>",
            footnotes: null,
            endnotes: null,
            settings: null
        );

        var graph = BuildGraph(package);

        Assert.False(Assert.Single(graph.NumberingPolicies).ValuesValid);
        Assert.Contains(graph.Issues, issue => issue.Code == "NOTE_NUMBERING_PROPERTIES_INVALID");
    }

    [Fact]
    public void EnforcesDefinitionAndIssueLimits()
    {
        using var package = BuildPackage(
            TransitionalWord,
            TransitionalRelationships,
            "<w:p/>",
            "<w:footnote w:id=\"1\"><w:p/></w:footnote><w:footnote w:id=\"2\"><w:p/></w:footnote>",
            endnotes: null,
            settings: null
        );
        var snapshot = new OpcPackageReader().Read(package);

        var exception = Assert.Throws<WordNoteLimitException>(() =>
            new WordNoteGraphBuilder(WordNoteGraphOptions.Default with { MaxDefinitions = 1 })
                .Build(snapshot)
        );

        Assert.Contains("more than 1 note definitions", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnforcesSpecialReferenceLimit()
    {
        using var package = BuildPackage(
            TransitionalWord,
            TransitionalRelationships,
            "<w:p/>",
            "<w:footnote w:type=\"separator\" w:id=\"-1\"><w:p/></w:footnote><w:footnote w:type=\"continuationSeparator\" w:id=\"0\"><w:p/></w:footnote>",
            endnotes: null,
            settings: "<w:footnotePr><w:footnote w:id=\"-1\"/><w:footnote w:id=\"0\"/></w:footnotePr>"
        );
        var snapshot = new OpcPackageReader().Read(package);

        var exception = Assert.Throws<WordNoteLimitException>(() =>
            new WordNoteGraphBuilder(WordNoteGraphOptions.Default with
            {
                MaxSpecialReferences = 1,
            }).Build(snapshot)
        );

        Assert.Contains("more than 1 special-note references", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PlansEmptyOrphanRemovalWithExactInverse()
    {
        using var package = BuildPackage(
            TransitionalWord,
            TransitionalRelationships,
            "<w:p><w:r><w:t>Main</w:t></w:r></w:p>",
            "<w:footnote w:id=\"4\"><w:p><w:r><w:footnoteRef/></w:r></w:p></w:footnote>",
            endnotes: null,
            settings: null
        );
        var reader = new OpcPackageReader();
        var snapshot = reader.Read(package);
        var graph = new WordNoteGraphBuilder().Build(snapshot);
        var target = Assert.Single(graph.Definitions);

        var plan = new WordNoteRepairPlanner().Plan(
            snapshot,
            new WordNoteRepairCommand(
                WordNoteRepairKind.RemoveEmptyOrphanDefinition,
                target.Id,
                target.Fingerprint.ToUpperInvariant()
            )
        );
        var lowercasePlan = new WordNoteRepairPlanner().Plan(
            snapshot,
            new WordNoteRepairCommand(
                WordNoteRepairKind.RemoveEmptyOrphanDefinition,
                target.Id,
                target.Fingerprint
            )
        );

        Assert.True(plan.HasChanges);
        Assert.True(plan.Validation.Passed);
        Assert.Equal(plan.PlanId, lowercasePlan.PlanId);
        Assert.StartsWith("wnrplan_", plan.PlanId, StringComparison.Ordinal);
        Assert.Equal("/word/footnotes.xml", Assert.Single(plan.ChangedParts).PartUri);
        var candidate = Materialize(snapshot, plan.CreateMutation(snapshot));
        Assert.Empty(new WordNoteGraphBuilder().Build(candidate).Definitions);
        var restored = Materialize(candidate, plan.CreateInverseMutation(candidate));
        Assert.Equal(snapshot.Fingerprint, restored.Fingerprint);
    }

    [Fact]
    public void PlansOnlyTheRedundantCanonicalDuplicate()
    {
        using var package = BuildPackage(
            TransitionalWord,
            TransitionalRelationships,
            "<w:p><w:r><w:footnoteReference w:id=\"2\"/></w:r></w:p>",
            """
            <w:footnote w:id="2"><w:p><w:r><w:footnoteRef/><w:t>Same</w:t></w:r></w:p></w:footnote>
            <w:footnote w:id="2"><w:p><w:r><w:footnoteRef/><w:t>Same</w:t></w:r></w:p></w:footnote>
            """,
            endnotes: null,
            settings: null
        );
        var snapshot = new OpcPackageReader().Read(package);
        var before = new WordNoteGraphBuilder().Build(snapshot);
        var target = Assert.Single(
            before.Definitions,
            definition => definition.RedundantDuplicateRemovalCandidate
        );

        var plan = new WordNoteRepairPlanner().Plan(
            snapshot,
            new WordNoteRepairCommand(
                WordNoteRepairKind.RemoveRedundantDuplicateDefinition,
                target.Id,
                target.Fingerprint
            )
        );
        var candidate = Materialize(snapshot, plan.CreateMutation(snapshot));
        var after = new WordNoteGraphBuilder().Build(candidate);

        Assert.True(plan.Validation.Passed);
        Assert.Single(after.Definitions);
        Assert.Equal("resolved", Assert.Single(after.References).ResolutionStatus);
        Assert.DoesNotContain(after.Issues, issue => issue.Code == "NOTE_DEFINITION_ID_DUPLICATE");
    }

    [Fact]
    public void RejectsContentfulOrphanAndStaleDefinitionFingerprint()
    {
        using var package = BuildPackage(
            TransitionalWord,
            TransitionalRelationships,
            "<w:p/>",
            "<w:footnote w:id=\"4\"><w:p><w:r><w:footnoteRef/><w:t>Do not delete</w:t></w:r></w:p></w:footnote>",
            endnotes: null,
            settings: null
        );
        var snapshot = new OpcPackageReader().Read(package);
        var target = Assert.Single(new WordNoteGraphBuilder().Build(snapshot).Definitions);
        var planner = new WordNoteRepairPlanner();

        Assert.Throws<WordSemanticEditException>(() => planner.Plan(
            snapshot,
            new WordNoteRepairCommand(
                WordNoteRepairKind.RemoveEmptyOrphanDefinition,
                target.Id,
                target.Fingerprint
            )
        ));
        Assert.Throws<WordSemanticPreconditionException>(() => planner.Plan(
            snapshot,
            new WordNoteRepairCommand(
                WordNoteRepairKind.RemoveRedundantDuplicateDefinition,
                target.Id,
                new string('0', 64)
            )
        ));
    }

    private static WordNoteGraph BuildGraph(Stream package) =>
        new WordNoteGraphBuilder().Build(new OpcPackageReader().Read(package));

    private static OpcPackageSnapshot Materialize(
        OpcPackageSnapshot package,
        OpcPackageMutationBuilder mutation
    )
    {
        using var stream = new MemoryStream();
        new OpcPackageSerializer().Write(stream, mutation);
        stream.Position = 0;
        return new OpcPackageReader().Read(stream);
    }

    private static MemoryStream BuildPackage(
        string wordNamespace,
        string relationshipNamespace,
        string body,
        string? footnotes,
        string? endnotes,
        string? settings,
        string extraDocumentRelationships = ""
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var overrides = new StringBuilder(
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>"
            );
            if (footnotes is not null)
            {
                overrides.Append("<Override PartName=\"/word/footnotes.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml\"/>");
            }
            if (endnotes is not null)
            {
                overrides.Append("<Override PartName=\"/word/endnotes.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.endnotes+xml\"/>");
            }
            if (settings is not null)
            {
                overrides.Append("<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>");
            }
            Add(
                archive,
                "[Content_Types].xml",
                $"<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>{overrides}</Types>"
            );
            Add(
                archive,
                "_rels/.rels",
                $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"{relationshipNamespace}/officeDocument\" Target=\"word/document.xml\"/></Relationships>"
            );
            Add(
                archive,
                "word/document.xml",
                $"<w:document xmlns:w=\"{wordNamespace}\"><w:body>{body}</w:body></w:document>"
            );

            var relationships = new StringBuilder();
            if (footnotes is not null)
            {
                relationships.Append($"<Relationship Id=\"rIdFootnotes\" Type=\"{relationshipNamespace}/footnotes\" Target=\"footnotes.xml\"/>");
                Add(
                    archive,
                    "word/footnotes.xml",
                    $"<w:footnotes xmlns:w=\"{wordNamespace}\">{footnotes}</w:footnotes>"
                );
            }
            if (endnotes is not null)
            {
                relationships.Append($"<Relationship Id=\"rIdEndnotes\" Type=\"{relationshipNamespace}/endnotes\" Target=\"endnotes.xml\"/>");
                Add(
                    archive,
                    "word/endnotes.xml",
                    $"<w:endnotes xmlns:w=\"{wordNamespace}\">{endnotes}</w:endnotes>"
                );
            }
            if (settings is not null)
            {
                relationships.Append($"<Relationship Id=\"rIdSettings\" Type=\"{relationshipNamespace}/settings\" Target=\"settings.xml\"/>");
                Add(
                    archive,
                    "word/settings.xml",
                    $"<w:settings xmlns:w=\"{wordNamespace}\">{settings}</w:settings>"
                );
            }
            relationships.Append(extraDocumentRelationships);
            Add(
                archive,
                "word/_rels/document.xml.rels",
                $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{relationships}</Relationships>"
            );
        }
        stream.Position = 0;
        return stream;
    }

    private static void Add(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var output = entry.Open();
        output.Write(Encoding.UTF8.GetBytes(content));
    }
}
