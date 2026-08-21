using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordMarkupCompatibilityGraphTests
{
    private const string W =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string W14 =
        "http://schemas.microsoft.com/office/word/2010/wordml";
    private const string Mc =
        "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string X = "urn:wordtoolkit:test-extension";

    [Fact]
    public void EvaluatesIgnorableProcessContentMustUnderstandAndAlternateContent()
    {
        using var bytes = BuildPackage($$"""
            <w:document xmlns:w="{{W}}" xmlns:w14="{{W14}}" xmlns:mc="{{Mc}}" xmlns:x="{{X}}"
                mc:Ignorable="w14 x" mc:ProcessContent="x:unwrap" mc:MustUnderstand="x">
              <w:body>
                <w:p x:flag="discarded-attribute"/>
                <x:drop><w:p/></x:drop>
                <x:unwrap><w:p/></x:unwrap>
                <mc:AlternateContent>
                  <mc:Choice Requires="w14"><w:p w14:paraId="00112233"/></mc:Choice>
                  <mc:Choice Requires="x"><w:p/></mc:Choice>
                  <mc:Fallback><w:p/></mc:Fallback>
                </mc:AlternateContent>
              </w:body>
            </w:document>
            """);
        var package = new OpcPackageReader().Read(bytes);
        var graph = new WordMarkupCompatibilityGraphBuilder().Build(
            package,
            new WordMceApplicationConfiguration
            {
                UnderstoodNamespaces = new[] { W14 },
            }
        );

        Assert.Equal(package.Fingerprint, graph.PackageFingerprint);
        Assert.Single(graph.Parts);
        Assert.Equal(3, graph.Rules.Count);
        var alternate = Assert.Single(graph.AlternateContent);
        Assert.True(alternate.StructureConformant);
        Assert.Equal(3, alternate.Branches.Count);
        Assert.Equal(
            WordMceBranchKind.Choice,
            alternate.Branches.Single(branch => branch.Selected).Kind
        );
        Assert.Equal(
            new[] { WordMceElementDisposition.RetainedWithIgnoredAttributes,
                WordMceElementDisposition.Ignored,
                WordMceElementDisposition.Unwrapped },
            graph.AffectedElements.Select(element => element.Disposition)
        );
        Assert.All(graph.AffectedElements, element => Assert.True(element.AffectsOutput));
        Assert.Equal(
            1,
            graph.AffectedElements.Single(element =>
                element.Disposition == WordMceElementDisposition.RetainedWithIgnoredAttributes
            ).IgnoredAttributeCount
        );
        var mismatch = Assert.Single(graph.MustUnderstandMismatches);
        Assert.Equal(X, mismatch.NamespaceUri);
        Assert.True(mismatch.AffectsOutput);
        Assert.True(
            graph.Namespaces.Single(item => item.NamespaceUri == W14)
                .UnderstoodByConfiguration
        );
        Assert.False(
            graph.Namespaces.Single(item => item.NamespaceUri == X)
                .UnderstoodByConfiguration
        );
        Assert.DoesNotContain(graph.Issues, issue => issue.Severity == WordMceIssueSeverity.Error);
    }

    [Fact]
    public void UsesFallbackAndMarksEffectsInsideUnselectedBranchesAsNonOutput()
    {
        using var bytes = BuildPackage($$"""
            <w:document xmlns:w="{{W}}" xmlns:w14="{{W14}}" xmlns:mc="{{Mc}}" mc:Ignorable="w14">
              <w:body>
                <mc:AlternateContent>
                  <mc:Choice Requires="w14"><w14:future/></mc:Choice>
                  <mc:Fallback><w:p/></mc:Fallback>
                </mc:AlternateContent>
              </w:body>
            </w:document>
            """);
        var graph = Build(bytes);

        var alternate = Assert.Single(graph.AlternateContent);
        Assert.Equal(
            WordMceBranchKind.Fallback,
            alternate.Branches.Single(branch => branch.Selected).Kind
        );
        var future = Assert.Single(graph.AffectedElements);
        Assert.Equal(WordMceElementDisposition.Ignored, future.Disposition);
        Assert.False(future.AffectsOutput);
        Assert.Empty(graph.MustUnderstandMismatches);
    }

    [Fact]
    public void PreservesApplicationDefinedExtensionIslandsFromMceInterpretation()
    {
        using var bytes = BuildPackage($$"""
            <w:document xmlns:w="{{W}}" xmlns:mc="{{Mc}}" xmlns:x="{{X}}" xmlns:e="urn:island"
                mc:Ignorable="x">
              <w:body>
                <x:outside/>
                <e:island>
                  <x:inside mc:MustUnderstand="x"/>
                  <mc:AlternateContent><mc:Fallback><x:opaque/></mc:Fallback></mc:AlternateContent>
                </e:island>
              </w:body>
            </w:document>
            """);
        var package = new OpcPackageReader().Read(bytes);
        var graph = new WordMarkupCompatibilityGraphBuilder().Build(
            package,
            new WordMceApplicationConfiguration
            {
                ApplicationDefinedExtensionElements = new[]
                {
                    new WordMceExpandedName("urn:island", "island"),
                },
            }
        );

        var affected = Assert.Single(graph.AffectedElements);
        Assert.Equal("outside", affected.LocalName);
        Assert.Single(graph.Rules);
        Assert.Empty(graph.AlternateContent);
        Assert.Empty(graph.MustUnderstandMismatches);
    }

    [Fact]
    public void ReportsLegacyHintsAndNonConformantRulesWithoutDiscardingThePart()
    {
        using var bytes = BuildPackage($$"""
            <w:document xmlns:w="{{W}}" xmlns:mc="{{Mc}}" xmlns:x="{{X}}"
                mc:Ignorable="x missing" mc:ProcessContent="x:* missing:name"
                mc:PreserveElements="x:*" mc:PreserveAttributes="x:flag">
              <mc:AlternateContent bad="1">
                <mc:Fallback/>
                <mc:Choice Requires="missing"/>
                <mc:Fallback/>
              </mc:AlternateContent>
            </w:document>
            """);
        var graph = Build(bytes);

        Assert.True(Assert.Single(graph.Parts).Parsed);
        Assert.Equal(4, graph.Rules.Count);
        Assert.Equal(
            2,
            graph.Rules.Count(rule =>
                rule.Kind is WordMceRuleKind.LegacyPreserveElements
                    or WordMceRuleKind.LegacyPreserveAttributes
            )
        );
        Assert.False(Assert.Single(graph.AlternateContent).StructureConformant);
        Assert.Contains(graph.Issues, issue => issue.Code == "MCE_PREFIX_UNBOUND");
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "MCE_LEGACY_PRESERVATION_HINT"
        );
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "MCE_ALTERNATE_CONTENT_CHOICE_MISSING"
                || issue.Code == "MCE_CHOICE_AFTER_FALLBACK"
        );
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "MCE_ALTERNATE_CONTENT_MULTIPLE_FALLBACKS"
        );
    }

    [Fact]
    public void ReportsMalformedXmlTypedPartsAndKeepsTheGraphBounded()
    {
        using var bytes = BuildPackage("<w:document xmlns:w=\"" + W + "\"><w:body></w:document>");
        var graph = Build(bytes);

        var part = Assert.Single(graph.Parts);
        Assert.False(part.Parsed);
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "MCE_XML_PART_NOT_WELL_FORMED"
        );
    }

    [Fact]
    public void EnforcesResourceAndCancellationLimitsBeforeUnboundedAnalysis()
    {
        using var bytes = BuildPackage($$"""
            <w:document xmlns:w="{{W}}"><w:body><w:p/></w:body></w:document>
            """);
        var package = new OpcPackageReader().Read(bytes);
        var limited = new WordMarkupCompatibilityGraphBuilder(
            new WordMarkupCompatibilityGraphOptions { MaxTotalElements = 2 }
        );

        Assert.Throws<WordMceLimitException>(() => limited.Build(package));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            new WordMarkupCompatibilityGraphBuilder().Build(
                package,
                cancellationToken: cancellation.Token
            )
        );
    }

    [Fact]
    public void InspectsRealLibreOfficeChartPackageWithoutOpeningRelatedContent()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "upstream",
            "fixtures",
            "lo_chart.docx"
        );
        var package = new OpcPackageReader().Read(path);

        var graph = new WordMarkupCompatibilityGraphBuilder().Build(package);

        Assert.True(graph.Parts.Count > 5);
        Assert.True(graph.ParsedElementCount > 100);
        Assert.True(graph.ParsedXmlBytes > 0);
        Assert.DoesNotContain(graph.Parts, part => !part.Parsed);
    }

    private static WordMarkupCompatibilityGraph Build(MemoryStream bytes)
    {
        var package = new OpcPackageReader().Read(bytes);
        return new WordMarkupCompatibilityGraphBuilder().Build(package);
    }

    private static MemoryStream BuildPackage(string documentXml)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(
                archive,
                "[Content_Types].xml",
                """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """
            );
            AddEntry(
                archive,
                "_rels/.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """
            );
            AddEntry(archive, "word/document.xml", documentXml);
        }
        stream.Position = 0;
        return stream;
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "pyproject.toml")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the WordToolkit repository root.");
    }
}
