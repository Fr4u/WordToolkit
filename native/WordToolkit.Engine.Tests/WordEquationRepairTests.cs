using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordEquationRepairTests
{
    private const string TransitionalWord =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string TransitionalMath =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string StrictWord =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string StrictMath =
        "http://purl.oclc.org/ooxml/officeDocument/math";

    [Fact]
    public void DiscoversEverySupportedExactDuplicateGroupWithoutReturningMathText()
    {
        using var bytes = BuildPackage(
            DuplicateDocument(TransitionalWord, TransitionalMath),
            DuplicateSettings(TransitionalWord, TransitionalMath)
        );
        var package = new OpcPackageReader().Read(bytes);

        var catalog = new WordEquationRepairPlanner().Inspect(package);

        Assert.True(catalog.AnalysisExecutionComplete);
        Assert.True(catalog.RepairCoverageComplete);
        Assert.Equal(5, catalog.Candidates.Count);
        Assert.Equal(
            4,
            catalog.Candidates.Count(candidate =>
                candidate.Kind
                    == WordEquationRepairKind.RemoveRedundantDuplicatePropertyContainer
            )
        );
        Assert.Single(catalog.Candidates, candidate =>
            candidate.Kind == WordEquationRepairKind.RemoveRedundantDuplicateProperty
        );
        Assert.Contains(catalog.Candidates, candidate =>
            candidate.IssueCode == "MATH_PARAGRAPH_PROPERTIES_DUPLICATE"
        );
        Assert.Contains(catalog.Candidates, candidate =>
            candidate.IssueCode == "MATH_PROPERTIES_DUPLICATE"
        );
        Assert.Contains(catalog.Candidates, candidate =>
            candidate.IssueCode == "MATH_RUN_PROPERTIES_DUPLICATE"
        );
        Assert.Contains(catalog.Candidates, candidate =>
            candidate.IssueCode == "MATH_SETTINGS_DUPLICATE"
        );
        Assert.All(catalog.Candidates, candidate =>
        {
            Assert.StartsWith("wder_", candidate.Id, StringComparison.Ordinal);
            Assert.Equal(29, candidate.Id.Length);
            Assert.Equal(64, candidate.Fingerprint.Length);
            Assert.NotEmpty(candidate.RemovedElementOrdinals);
            Assert.DoesNotContain("secret", candidate.ToString(), StringComparison.Ordinal);
        });
        Assert.All(
            catalog.Candidates.Where(candidate => candidate.IssueCode != "MATH_SETTINGS_DUPLICATE"),
            candidate => Assert.NotNull(candidate.EquationId)
        );
    }

    [Fact]
    public void RejectsNonEquivalentDuplicatesInsteadOfGuessingWhichPropertyWins()
    {
        using var bytes = BuildPackage(
            $"""
            <w:document xmlns:w="{TransitionalWord}" xmlns:m="{TransitionalMath}">
              <w:body><w:p><m:oMath><m:f>
                <m:fPr><m:type m:val="bar"/></m:fPr>
                <m:fPr><m:type m:val="noBar"/></m:fPr>
                <m:num>{Run("secret")}</m:num><m:den>{Run("2")}</m:den>
              </m:f></m:oMath></w:p></w:body>
            </w:document>
            """
        );
        var package = new OpcPackageReader().Read(bytes);

        var catalog = new WordEquationRepairPlanner().Inspect(package);

        Assert.Empty(catalog.Candidates);
        Assert.Contains(catalog.EquationGraph.Issues, issue =>
            issue.Code == "MATH_PROPERTIES_DUPLICATE"
        );
    }

    [Fact]
    public void DoesNotPublishARepairCandidateWithoutMatchingGraphEvidence()
    {
        using var bytes = BuildPackage(
            $"""
            <w:document xmlns:w="{TransitionalWord}" xmlns:m="{TransitionalMath}">
              <w:body><w:p><m:rPr><m:sty m:val="b"/><m:sty m:val="b"/></m:rPr></w:p></w:body>
            </w:document>
            """
        );
        var package = new OpcPackageReader().Read(bytes);

        var catalog = new WordEquationRepairPlanner().Inspect(package);

        Assert.Empty(catalog.Candidates);
    }

    [Fact]
    public void PlansBatchRemovalWithNormalizedSemanticAndExactInverseProof()
    {
        using var bytes = BuildPackage(
            DuplicateDocument(TransitionalWord, TransitionalMath),
            DuplicateSettings(TransitionalWord, TransitionalMath)
        );
        var reader = new OpcPackageReader();
        var package = reader.Read(bytes);
        var planner = new WordEquationRepairPlanner();
        var catalog = planner.Inspect(package);
        var commands = catalog.Candidates.Select(candidate =>
            new WordEquationRepairCommand(
                candidate.Kind,
                candidate.Id,
                candidate.Fingerprint.ToUpperInvariant()
            )
        ).ToArray();

        var plan = planner.Plan(package, commands);

        Assert.True(plan.HasChanges);
        Assert.StartsWith("werplan_", plan.PlanId, StringComparison.Ordinal);
        Assert.Equal(5, plan.Candidates.Count);
        Assert.Equal(2, plan.ChangedParts.Count);
        Assert.True(plan.Validation.Passed);
        Assert.True(plan.Validation.SelectedDuplicateGroupsRemoved);
        Assert.True(plan.Validation.NormalizedMathSemanticsPreserved);
        Assert.True(plan.Validation.NoNewEquationIssues);
        Assert.True(plan.Validation.ExactInverseVerified);
        Assert.Equal(
            plan.Candidates.Sum(candidate => candidate.RemovedXmlElementCount),
            plan.Validation.RemovedElementCount
        );
        Assert.True(
            plan.Validation.AfterEquationErrorCount
                < plan.Validation.BeforeEquationErrorCount
        );

        var candidate = Materialize(package, plan.CreateMutation(package));
        var after = planner.Inspect(candidate);
        Assert.Empty(after.Candidates);
        Assert.DoesNotContain(after.EquationGraph.Issues, issue =>
            issue.Code is "MATH_PARAGRAPH_PROPERTIES_DUPLICATE"
                or "MATH_PROPERTIES_DUPLICATE"
                or "MATH_RUN_PROPERTIES_DUPLICATE"
                or "MATH_SETTINGS_DUPLICATE"
                or "MATH_PROPERTY_DUPLICATE"
        );
        var inverse = Materialize(candidate, plan.CreateInverseMutation(candidate));
        Assert.Equal(package.Fingerprint, inverse.Fingerprint);
    }

    [Fact]
    public void SupportsStrictNamespacesAndRemovesAllLaterMembersOfOneGroup()
    {
        using var bytes = BuildPackage(
            $"""
            <w:document xmlns:w="{StrictWord}" xmlns:m="{StrictMath}">
              <w:body><w:p><m:oMath><m:f>
                <m:fPr><m:type m:val="bar"/></m:fPr>
                <m:fPr><m:type m:val="bar"/></m:fPr>
                <m:fPr><m:type m:val="bar"/></m:fPr>
                <m:num>{Run("x")}</m:num><m:den>{Run("y")}</m:den>
              </m:f></m:oMath></w:p></w:body>
            </w:document>
            """,
            strictRelationships: true
        );
        var package = new OpcPackageReader().Read(bytes);
        var planner = new WordEquationRepairPlanner();
        var catalog = planner.Inspect(package);
        var target = Assert.Single(catalog.Candidates);
        Assert.Equal(2, target.RemovedElementCount);

        var plan = planner.Plan(
            package,
            [new WordEquationRepairCommand(target.Kind, target.Id, target.Fingerprint)]
        );

        Assert.Equal(4, plan.Validation.RemovedElementCount);
        Assert.True(plan.Validation.Passed);
        Assert.Empty(planner.Inspect(Materialize(package, plan.CreateMutation(package))).Candidates);
    }

    [Fact]
    public void CandidateAndPlanIdentityArePackageBoundAndOrderIndependent()
    {
        using var bytes = BuildPackage(DuplicateDocument(TransitionalWord, TransitionalMath));
        var package = new OpcPackageReader().Read(bytes);
        var planner = new WordEquationRepairPlanner();
        var candidates = planner.Inspect(package).Candidates;
        Assert.True(candidates.Count > 1);
        var commands = candidates.Select(candidate => new WordEquationRepairCommand(
            candidate.Kind,
            candidate.Id,
            candidate.Fingerprint
        )).ToArray();

        var first = planner.Plan(package, commands);
        var second = planner.Plan(package, commands.Reverse().ToArray());

        Assert.Equal(first.PlanId, second.PlanId);
        var stale = commands.ToArray();
        stale[0] = stale[0] with
        {
            ExpectedCandidateFingerprint = new string('a', 64),
        };
        Assert.Throws<WordSemanticPreconditionException>(() =>
            planner.Plan(package, stale)
        );
    }

    [Fact]
    public void EnforcesCandidateAndCommandBounds()
    {
        using var bytes = BuildPackage(DuplicateDocument(TransitionalWord, TransitionalMath));
        var package = new OpcPackageReader().Read(bytes);
        Assert.Throws<WordEquationLimitException>(() =>
            new WordEquationRepairPlanner(
                new WordEquationRepairOptions { MaxCandidates = 1 }
            ).Inspect(package)
        );
        var planner = new WordEquationRepairPlanner(
            new WordEquationRepairOptions { MaxCommands = 1 }
        );
        var candidates = planner.Inspect(package).Candidates;
        Assert.Throws<ArgumentException>(() => planner.Plan(
            package,
            candidates.Take(2).Select(candidate => new WordEquationRepairCommand(
                candidate.Kind,
                candidate.Id,
                candidate.Fingerprint
            )).ToArray()
        ));
    }

    private static string DuplicateDocument(string wordNamespace, string mathNamespace) =>
        $"""
        <w:document xmlns:w="{wordNamespace}" xmlns:m="{mathNamespace}">
          <w:body><w:p><m:oMathPara>
            <m:oMathParaPr><m:jc m:val="center"/></m:oMathParaPr>
            <m:oMathParaPr><m:jc m:val="center"/></m:oMathParaPr>
            <m:oMath><m:f>
              <m:fPr><m:type m:val="bar"/></m:fPr>
              <m:fPr><m:type m:val="bar"/></m:fPr>
              <m:num><m:r>
                <m:rPr><m:sty m:val="b"/><m:sty m:val="b"/></m:rPr>
                <m:rPr><m:sty m:val="b"/><m:sty m:val="b"/></m:rPr>
                <m:t>secret</m:t>
              </m:r></m:num>
              <m:den>{Run("2")}</m:den>
            </m:f></m:oMath>
          </m:oMathPara></w:p></w:body>
        </w:document>
        """;

    private static string DuplicateSettings(string wordNamespace, string mathNamespace) =>
        $"""
        <w:settings xmlns:w="{wordNamespace}" xmlns:m="{mathNamespace}">
          <m:mathPr><m:mathFont m:val="Cambria Math"/></m:mathPr>
          <m:mathPr><m:mathFont m:val="Cambria Math"/></m:mathPr>
        </w:settings>
        """;

    private static string Run(string text) =>
        $"<m:r><m:t>{System.Security.SecurityElement.Escape(text)}</m:t></m:r>";

    private static OpcPackageSnapshot Materialize(
        OpcPackageSnapshot source,
        OpcPackageMutationBuilder mutation
    )
    {
        using var stream = new MemoryStream();
        new OpcPackageSerializer().Write(stream, mutation);
        stream.Position = 0;
        return new OpcPackageReader().Read(stream);
    }

    private static MemoryStream BuildPackage(
        string documentXml,
        string? settingsXml = null,
        bool strictRelationships = false
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
                  {(settingsXml is null ? string.Empty : "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>")}
                </Types>
                """
            );
            var relationshipBase = strictRelationships
                ? "http://purl.oclc.org/ooxml/officeDocument/relationships/"
                : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
            WriteEntry(
                archive,
                "_rels/.rels",
                $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"{relationshipBase}officeDocument\" Target=\"word/document.xml\"/></Relationships>"
            );
            WriteEntry(archive, "word/document.xml", documentXml);
            if (settingsXml is not null)
            {
                WriteEntry(
                    archive,
                    "word/_rels/document.xml.rels",
                    $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rIdSettings\" Type=\"{relationshipBase}settings\" Target=\"settings.xml\"/></Relationships>"
                );
                WriteEntry(archive, "word/settings.xml", settingsXml);
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
}
