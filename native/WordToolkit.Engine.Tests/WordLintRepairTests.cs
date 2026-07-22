using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordLintRepairTests
{
    [Fact]
    public void PlansValidatedSourceBoundTitleRepairAndExactInverse()
    {
        using var source = BuildPackage("<dc:title/>");
        var reader = new OpcPackageReader();
        var package = reader.Read(source);
        var semantic = new WordSemanticProjector().Project(package);
        var finding = FindTitleFinding(package, semantic);
        const string title = "Private <title> & evidence";
        var planner = new WordLintRepairPlanner();

        var plan = planner.PlanSetDocumentTitle(
            package,
            semantic,
            package.Fingerprint,
            finding.Id,
            title
        );
        var repeated = planner.PlanSetDocumentTitle(
            package,
            semantic,
            package.Fingerprint.ToUpperInvariant(),
            finding.Id,
            title
        );

        Assert.NotNull(finding.Source.ByteSpan);
        Assert.Equal(finding.Source.SourceElementOrdinal, plan.SourceElementOrdinal);
        Assert.StartsWith("wlrplan_", plan.PlanId, StringComparison.Ordinal);
        Assert.Equal(plan.PlanId, repeated.PlanId);
        Assert.Equal(WordLintRepairKind.SetDocumentTitle, plan.RepairKind);
        Assert.Equal(finding.Id, plan.FindingId);
        Assert.Equal(WordLintRepairPlanner.DocumentTitleRuleId, plan.RuleId);
        Assert.Equal(package.Fingerprint, plan.BasePackageFingerprint);
        Assert.NotEqual(package.Fingerprint, plan.ResultPackageFingerprint);
        Assert.True(plan.HasChanges);
        Assert.True(plan.Validation.Passed);
        Assert.True(plan.Validation.TargetFindingResolved);
        Assert.True(plan.Validation.ChangedOnlyExpectedPart);
        Assert.Single(plan.ChangedParts);
        Assert.Equal("/docProps/core.xml", plan.ChangedParts[0].PartUri);
        Assert.Equal(title.Length, plan.AfterCharacters);
        Assert.DoesNotContain(
            title,
            JsonSerializer.Serialize(plan),
            StringComparison.Ordinal
        );

        using var appliedBytes = Serialize(plan.CreateMutation(package));
        var applied = reader.Read(appliedBytes);
        Assert.Equal(plan.ResultPackageFingerprint, applied.Fingerprint);
        var coreXml = Encoding.UTF8.GetString(
            applied.Parts["/docProps/core.xml"].Entry.Content.Span
        );
        Assert.Contains(
            "<dc:title>Private &lt;title&gt; &amp; evidence</dc:title>",
            coreXml,
            StringComparison.Ordinal
        );
        Assert.Equal(
            package.Parts["/word/document.xml"].Entry.Sha256,
            applied.Parts["/word/document.xml"].Entry.Sha256
        );
        var appliedSemantic = new WordSemanticProjector().Project(applied);
        Assert.DoesNotContain(
            new WordDocumentLinter(
                new WordDocumentLinterOptions
                {
                    EnabledRulePacks = [WordLintRulePack.Accessibility],
                }
            ).Analyze(applied, appliedSemantic).Findings,
            item => item.RuleId == WordLintRepairPlanner.DocumentTitleRuleId
        );

        using var revertedBytes = Serialize(plan.CreateInverseMutation(applied));
        var reverted = reader.Read(revertedBytes);
        Assert.Equal(package.Fingerprint, reverted.Fingerprint);
        Assert.Equal(
            package.Parts["/docProps/core.xml"].Entry.Content.ToArray(),
            reverted.Parts["/docProps/core.xml"].Entry.Content.ToArray()
        );
    }

    [Fact]
    public void RejectsMissingAmbiguousAndLexicallyUnsafeTitleSources()
    {
        AssertUnsupportedSource(string.Empty);
        AssertUnsupportedSource("<dc:title/><dc:title/>");
        AssertUnsupportedSource("<dc:title><!--opaque--></dc:title>");
        AssertUnsupportedSource("<cp:wrapper><dc:title/></cp:wrapper>");
    }

    [Fact]
    public void RejectsStaleEvidenceInvalidTitlesAndCancellation()
    {
        using var source = BuildPackage("<dc:title></dc:title>");
        var package = new OpcPackageReader().Read(source);
        var semantic = new WordSemanticProjector().Project(package);
        var finding = FindTitleFinding(package, semantic);
        var planner = new WordLintRepairPlanner(
            new WordLintRepairPlannerOptions { MaxDocumentTitleCharacters = 5 }
        );

        Assert.Throws<ArgumentException>(() => planner.PlanSetDocumentTitle(
            package,
            semantic,
            package.Fingerprint,
            finding.Id,
            " "
        ));
        Assert.Throws<ArgumentException>(() => planner.PlanSetDocumentTitle(
            package,
            semantic,
            package.Fingerprint,
            finding.Id,
            " title"
        ));
        Assert.Throws<WordLintRepairLimitException>(() => planner.PlanSetDocumentTitle(
            package,
            semantic,
            package.Fingerprint,
            finding.Id,
            "longer"
        ));
        Assert.Throws<WordLintRepairPreconditionException>(() => planner.PlanSetDocumentTitle(
            package,
            semantic,
            new string('0', 64),
            finding.Id,
            "title"
        ));
        Assert.Throws<WordLintRepairPreconditionException>(() => planner.PlanSetDocumentTitle(
            package,
            semantic,
            package.Fingerprint,
            "wtlint_000000000000000000000000",
            "title"
        ));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => planner.PlanSetDocumentTitle(
            package,
            semantic,
            package.Fingerprint,
            finding.Id,
            "title",
            cancellation.Token
        ));
    }

    [Fact]
    public void ForwardMutationRejectsPackageFingerprintDrift()
    {
        using var firstBytes = BuildPackage("<dc:title/>");
        using var secondBytes = BuildPackage("<dc:title/>", "changed");
        var reader = new OpcPackageReader();
        var first = reader.Read(firstBytes);
        var second = reader.Read(secondBytes);
        var semantic = new WordSemanticProjector().Project(first);
        var finding = FindTitleFinding(first, semantic);
        var plan = new WordLintRepairPlanner().PlanSetDocumentTitle(
            first,
            semantic,
            first.Fingerprint,
            finding.Id,
            "title"
        );

        Assert.Throws<WordSemanticPreconditionException>(() =>
            plan.CreateMutation(second)
        );
    }

    private static void AssertUnsupportedSource(string titleMarkup)
    {
        using var source = BuildPackage(titleMarkup);
        var package = new OpcPackageReader().Read(source);
        var semantic = new WordSemanticProjector().Project(package);
        var finding = FindTitleFinding(package, semantic);
        Assert.False(finding.Fix.IsImplemented);

        Assert.ThrowsAny<WordLintRepairException>(() =>
            new WordLintRepairPlanner().PlanSetDocumentTitle(
                package,
                semantic,
                package.Fingerprint,
                finding.Id,
                "safe title"
            )
        );
    }

    private static WordLintFinding FindTitleFinding(
        OpcPackageSnapshot package,
        WordSemanticDocument semantic
    ) => new WordDocumentLinter(
        new WordDocumentLinterOptions
        {
            EnabledRulePacks = [WordLintRulePack.Accessibility],
        }
    ).Analyze(package, semantic).Findings.Single(item =>
        item.RuleId == WordLintRepairPlanner.DocumentTitleRuleId
    );

    private static MemoryStream Serialize(OpcPackageMutationBuilder mutation)
    {
        var stream = new MemoryStream();
        new OpcPackageSerializer().Write(stream, mutation);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream BuildPackage(
        string titleMarkup,
        string documentText = "body"
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(
                archive,
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
                </Types>
                """
            );
            AddEntry(
                archive,
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
                </Relationships>
                """
            );
            AddEntry(
                archive,
                "docProps/core.xml",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/">
                  {titleMarkup}
                </cp:coreProperties>
                """
            );
            AddEntry(
                archive,
                "word/document.xml",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>{documentText}</w:t></w:r></w:p><w:sectPr/></w:body></w:document>
                """
            );
        }
        stream.Position = 0;
        return stream;
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var writer = new StreamWriter(
            entry.Open(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
        writer.NewLine = "\n";
        writer.Write(content);
    }
}
