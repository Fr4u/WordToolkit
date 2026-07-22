using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordDocumentLinterTests
{
    [Fact]
    public void ProducesDeterministicSourceLinkedFindingsAcrossRulePacks()
    {
        using var bytes = BuildLintPackage();
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var linter = new WordDocumentLinter();

        var first = linter.Analyze(package, semantic);
        var second = linter.Analyze(package, semantic);

        Assert.Equal(
            first.Findings.Select(item => item.Id),
            second.Findings.Select(item => item.Id)
        );
        Assert.Equal(package.Fingerprint, first.PackageFingerprint);
        Assert.Equal(semantic.NodeCount, first.Coverage.SemanticNodeCount);
        Assert.True(first.Coverage.SemanticNodesScanned > 0);
        Assert.True(first.Coverage.ExecutionComplete);
        Assert.False(first.Coverage.DocumentCoverageComplete);
        Assert.NotEmpty(first.Coverage.ExplicitlyUnmodeledDomains);
        Assert.Contains(first.Findings, item => item.RuleId == "WTL_STYLE_UNUSED");
        Assert.Contains(
            first.Findings,
            item => item.RuleId == "WTL_STYLE_EQUIVALENT_FORMATTING"
                && item.EvidenceCount >= 2
        );
        Assert.Contains(
            first.Findings,
            item => item.RuleId == "WTL_FORMATTING_DIRECT_OVERRIDE"
                && item.Source.ByteSpan is { ByteLength: > 0 }
        );
        Assert.Contains(first.Findings, item => item.RuleId == "WTL_SECURITY_HIDDEN_TEXT");
        Assert.Contains(
            first.Findings,
            item => item.RuleId == "WTL_SECURITY_EXTERNAL_RELATIONSHIP"
                && item.Source.RelationshipId == "rIdExternal"
        );
        Assert.Equal(
            2,
            first.Findings.Count(item => item.RuleId == "WTL_ACCESSIBILITY_HEADING_ORDER")
        );
        Assert.Contains(
            first.Findings,
            item => item.RuleId == "WTL_ACCESSIBILITY_DRAWING_ALT_TEXT"
        );
        Assert.Contains(
            first.Findings,
            item => item.RuleId == "WTL_ACCESSIBILITY_TABLE_HEADER"
        );
        Assert.Contains(
            first.Findings,
            item => item.RuleId == "WTL_ACCESSIBILITY_DOCUMENT_TITLE"
        );
        Assert.All(
            first.Findings,
            item => Assert.False(item.Fix.IsImplemented)
        );
        Assert.DoesNotContain(
            "secret.example",
            JsonSerializer.Serialize(first),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.DoesNotContain(
            "SecretMissingStyle",
            JsonSerializer.Serialize(first),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void SuppressionsSeverityPacksAndFindingLimitsAreExplicit()
    {
        using var bytes = BuildLintPackage();
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var baseline = new WordDocumentLinter().Analyze(package, semantic);
        var direct = baseline.Findings.First(item =>
            item.RuleId == "WTL_FORMATTING_DIRECT_OVERRIDE"
        );

        var suppressed = new WordDocumentLinter(
            new WordDocumentLinterOptions
            {
                SuppressedRuleIds = ["WTL_STYLE_UNUSED"],
                SuppressedFindingIds = [direct.Id],
            }
        ).Analyze(package, semantic);

        Assert.DoesNotContain(
            suppressed.Findings,
            item => item.RuleId == "WTL_STYLE_UNUSED" || item.Id == direct.Id
        );
        Assert.True(suppressed.SuppressedFindingCount >= 2);

        var securityOnly = new WordDocumentLinter(
            new WordDocumentLinterOptions
            {
                EnabledRulePacks = [WordLintRulePack.Security],
            }
        ).Analyze(package, semantic);
        Assert.NotEmpty(securityOnly.Findings);
        Assert.All(
            securityOnly.Findings,
            item => Assert.Equal(WordLintRulePack.Security, item.RulePack)
        );
        Assert.All(
            securityOnly.EvaluatedRules,
            item => Assert.Equal(WordLintRulePack.Security, item.Pack)
        );

        var errorsOnly = new WordDocumentLinter(
            new WordDocumentLinterOptions
            {
                MinimumSeverity = WordLintSeverity.Error,
            }
        ).Analyze(package, semantic);
        Assert.All(
            errorsOnly.Findings,
            item => Assert.True(item.Severity >= WordLintSeverity.Error)
        );
        Assert.True(errorsOnly.SeverityFilteredFindingCount > 0);

        var bounded = new WordDocumentLinter(
            new WordDocumentLinterOptions { MaxFindings = 2 }
        ).Analyze(package, semantic);
        Assert.Equal(2, bounded.Findings.Count);
        Assert.True(bounded.VisibleFindingCount > bounded.Findings.Count);
        Assert.True(bounded.FindingsTruncated);
        Assert.False(bounded.Complete);
    }

    [Fact]
    public void SourceAndSemanticBudgetsFailOpenOnlyWithVisibleCoverageOmissions()
    {
        using var bytes = BuildLintPackage();
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);

        var report = new WordDocumentLinter(
            new WordDocumentLinterOptions
            {
                MaxSemanticNodes = 2,
                MaxSourceXmlPartBytes = 64,
            }
        ).Analyze(package, semantic);

        Assert.Equal(2, report.Coverage.SemanticNodesScanned);
        Assert.Contains("semantic_node_scan_truncated", report.Coverage.Omissions);
        Assert.Contains("source_xml_part_limit", report.Coverage.Omissions);
        Assert.False(report.Coverage.ExecutionComplete);
        Assert.False(report.Coverage.Complete);
        Assert.False(report.Complete);

        var coreOnly = new WordDocumentLinter(
            new WordDocumentLinterOptions
            {
                EnabledRulePacks = [WordLintRulePack.Core],
                MaxSourceXmlPartBytes = 64,
            }
        ).Analyze(package, semantic);
        Assert.Equal(0, coreOnly.Coverage.FormattingNodesScanned);
        Assert.Equal(0, coreOnly.Coverage.HeadingCount);
    }

    [Fact]
    public void RejectsUnknownSuppressionsFingerprintDriftAndCancellation()
    {
        Assert.Throws<ArgumentException>(() => new WordDocumentLinter(
            new WordDocumentLinterOptions
            {
                SuppressedRuleIds = ["WTL_DOES_NOT_EXIST"],
            }
        ));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WordDocumentLinter(
            new WordDocumentLinterOptions { MaxDependencyNodes = 0 }
        ));
        Assert.Throws<ArgumentException>(() => new WordDocumentLinter(
            new WordDocumentLinterOptions
            {
                EnabledRulePacks = Array.Empty<WordLintRulePack>(),
            }
        ));
        Assert.Throws<ArgumentException>(() => new WordDocumentLinter(
            new WordDocumentLinterOptions
            {
                SuppressedRuleIds = ["WTL_STYLE_UNUSED", "WTL_STYLE_UNUSED"],
            }
        ));

        using var firstBytes = BuildLintPackage();
        var first = new OpcPackageReader().Read(firstBytes);
        using var secondBytes = BuildLintPackage("Different");
        var second = new OpcPackageReader().Read(secondBytes);
        var secondSemantic = new WordSemanticProjector().Project(second);
        Assert.Throws<WordLintProjectionException>(() =>
            new WordDocumentLinter().Analyze(first, secondSemantic)
        );
        var firstReport = new WordDocumentLinter().Analyze(
            first,
            new WordSemanticProjector().Project(first)
        );
        var secondReport = new WordDocumentLinter().Analyze(second, secondSemantic);
        Assert.Empty(
            firstReport.Findings.Select(item => item.Id)
                .Intersect(secondReport.Findings.Select(item => item.Id))
        );

        var firstSemantic = new WordSemanticProjector().Project(first);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            new WordDocumentLinter().Analyze(
                first,
                firstSemantic,
                cancellation.Token
            )
        );
    }

    [Fact]
    public void RuleCatalogIsUniqueAndStable()
    {
        var rules = WordDocumentLinter.RuleCatalog;
        Assert.Equal(rules.Count, rules.Select(item => item.Id).Distinct().Count());
        Assert.All(rules, item => Assert.StartsWith("WTL_", item.Id));
        Assert.Contains(rules, item => item.Pack == WordLintRulePack.Core);
        Assert.Contains(rules, item => item.Pack == WordLintRulePack.Styles);
        Assert.Contains(rules, item => item.Pack == WordLintRulePack.Accessibility);
        Assert.Contains(rules, item => item.Pack == WordLintRulePack.Security);
    }

    private static MemoryStream BuildLintPackage(string firstHeadingText = "Heading")
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
                  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
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
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/">
                  <dc:title></dc:title>
                </cp:coreProperties>
                """
            );
            AddEntry(
                archive,
                "word/document.xml",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <w:body>
                    <w:p>
                      <w:pPr><w:pStyle w:val="Heading2"/><w:jc w:val="center"/></w:pPr>
                      <w:r><w:rPr><w:b/><w:vanish/></w:rPr><w:t>{firstHeadingText}</w:t></w:r>
                    </w:p>
                    <w:p>
                      <w:pPr><w:pStyle w:val="Heading4"/></w:pPr>
                      <w:r><w:t>Lower heading</w:t></w:r>
                    </w:p>
                    <w:tbl>
                      <w:tblPr/>
                      <w:tr><w:tc><w:p><w:r><w:t>Column</w:t></w:r></w:p></w:tc></w:tr>
                      <w:tr><w:tc><w:p><w:r><w:t>Value</w:t></w:r></w:p></w:tc></w:tr>
                    </w:tbl>
                    <w:p><w:r><w:drawing><wp:inline><wp:docPr id="1" name="Figure"/><a:graphic/></wp:inline></w:drawing></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """
            );
            AddEntry(
                archive,
                "word/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
                  <w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="Heading 2"/><w:basedOn w:val="Normal"/><w:pPr><w:outlineLvl w:val="1"/></w:pPr></w:style>
                  <w:style w:type="paragraph" w:styleId="Heading4"><w:name w:val="Heading 4"/><w:basedOn w:val="Normal"/><w:pPr><w:outlineLvl w:val="3"/></w:pPr></w:style>
                  <w:style w:type="paragraph" w:customStyle="1" w:styleId="Unused"><w:name w:val="Unused"/><w:basedOn w:val="Normal"/><w:pPr><w:spacing w:after="120"/></w:pPr></w:style>
                  <w:style w:type="paragraph" w:customStyle="1" w:styleId="EquivalentA"><w:name w:val="Equivalent A"/><w:basedOn w:val="Normal"/><w:pPr><w:keepNext/></w:pPr></w:style>
                  <w:style w:type="paragraph" w:customStyle="1" w:styleId="EquivalentB"><w:name w:val="Equivalent B"/><w:basedOn w:val="Normal"/><w:pPr><w:keepNext/></w:pPr></w:style>
                  <w:style w:type="paragraph" w:customStyle="1" w:styleId="LeakyStyle"><w:name w:val="Private"/><w:basedOn w:val="SecretMissingStyle"/></w:style>
                </w:styles>
                """
            );
            AddEntry(
                archive,
                "word/_rels/document.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                  <Relationship Id="rIdExternal" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://secret.example/private" TargetMode="External"/>
                </Relationships>
                """
            );
        }
        stream.Position = 0;
        return stream;
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var output = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        output.Write(bytes);
    }
}
