using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordSectionGraphTests
{
    [Fact]
    public void ResolvesExplicitInheritedBlankAndFallbackBindings()
    {
        using var packageBytes = BuildMultiSectionPackage(evenAndOddHeaders: true);
        var package = new OpcPackageReader().Read(packageBytes);
        var semantic = new WordSemanticProjector().Project(package);

        var graph = new WordSectionGraphBuilder().Build(package, semantic);

        Assert.True(graph.EvenAndOddHeaders);
        Assert.Equal(2, graph.Sections.Count);
        Assert.Equal(
            new[]
            {
                "/word/footer-default-1.xml",
                "/word/footer-default-2.xml",
                "/word/header-default.xml",
                "/word/header-even-2.xml",
                "/word/header-first.xml",
            },
            graph.ReferencedStoryPartUris
        );
        Assert.Equal(
            new[] { "/word/header-unused.xml" },
            graph.UnboundStoryPartUris
        );

        var first = graph.Sections[0];
        Assert.False(first.IsImplicit);
        Assert.True(first.TitlePage);
        Assert.Equal("continuous", first.BreakType);
        Assert.Equal("16840", first.Properties["page_width_twips"]);
        Assert.Equal("landscape", first.Properties["page_orientation"]);
        Assert.NotNull(first.EndsAtParagraphId);
        Assert.Null(first.StartsAfterParagraphId);
        AssertBinding(
            first.Binding(
                WordHeaderFooterKind.Header,
                WordHeaderFooterVariant.Default
            ),
            enabled: true,
            WordHeaderFooterBindingOrigin.Explicit,
            definitionSection: 1,
            partUri: "/word/header-default.xml",
            effectivePartUri: "/word/header-default.xml"
        );
        AssertBinding(
            first.Binding(
                WordHeaderFooterKind.Header,
                WordHeaderFooterVariant.First
            ),
            enabled: true,
            WordHeaderFooterBindingOrigin.Explicit,
            definitionSection: 1,
            partUri: "/word/header-first.xml",
            effectivePartUri: "/word/header-first.xml"
        );
        AssertBinding(
            first.Binding(
                WordHeaderFooterKind.Header,
                WordHeaderFooterVariant.Even
            ),
            enabled: true,
            WordHeaderFooterBindingOrigin.Blank,
            definitionSection: null,
            partUri: null,
            effectivePartUri: null
        );

        var second = graph.Sections[1];
        Assert.False(second.TitlePage);
        Assert.Equal(first.EndsAtParagraphId, second.StartsAfterParagraphId);
        Assert.Null(second.EndsAtParagraphId);
        AssertBinding(
            second.Binding(
                WordHeaderFooterKind.Header,
                WordHeaderFooterVariant.Default
            ),
            enabled: true,
            WordHeaderFooterBindingOrigin.Inherited,
            definitionSection: 1,
            partUri: "/word/header-default.xml",
            effectivePartUri: "/word/header-default.xml"
        );
        var inheritedFirst = second.Binding(
            WordHeaderFooterKind.Header,
            WordHeaderFooterVariant.First
        );
        AssertBinding(
            inheritedFirst,
            enabled: false,
            WordHeaderFooterBindingOrigin.Inherited,
            definitionSection: 1,
            partUri: "/word/header-first.xml",
            effectivePartUri: "/word/header-default.xml"
        );
        Assert.Equal(
            WordHeaderFooterVariant.Default,
            inheritedFirst.DisplayFallbackVariant
        );
        AssertBinding(
            second.Binding(
                WordHeaderFooterKind.Header,
                WordHeaderFooterVariant.Even
            ),
            enabled: true,
            WordHeaderFooterBindingOrigin.Explicit,
            definitionSection: 2,
            partUri: "/word/header-even-2.xml",
            effectivePartUri: "/word/header-even-2.xml"
        );
        AssertBinding(
            second.Binding(
                WordHeaderFooterKind.Footer,
                WordHeaderFooterVariant.Default
            ),
            enabled: true,
            WordHeaderFooterBindingOrigin.Explicit,
            definitionSection: 2,
            partUri: "/word/footer-default-2.xml",
            effectivePartUri: "/word/footer-default-2.xml"
        );
    }

    [Fact]
    public void DisabledEvenVariantUsesTheDefaultStoryForDisplay()
    {
        using var packageBytes = BuildMultiSectionPackage(evenAndOddHeaders: false);
        var package = new OpcPackageReader().Read(packageBytes);
        var semantic = new WordSemanticProjector().Project(package);

        var graph = new WordSectionGraphBuilder().Build(package, semantic);
        var even = graph.Sections[1].Binding(
            WordHeaderFooterKind.Header,
            WordHeaderFooterVariant.Even
        );

        Assert.False(graph.EvenAndOddHeaders);
        Assert.False(even.IsVariantEnabled);
        Assert.Equal(WordHeaderFooterBindingOrigin.Explicit, even.Origin);
        Assert.Equal("/word/header-even-2.xml", even.PartUri);
        Assert.Equal("/word/header-default.xml", even.EffectiveDisplayPartUri);
        Assert.Equal(
            WordHeaderFooterVariant.Default,
            even.DisplayFallbackVariant
        );
    }

    [Fact]
    public void DocumentWithoutSectionPropertiesHasOneImplicitBlankSection()
    {
        using var packageBytes = BuildMinimalPackage();
        var package = new OpcPackageReader().Read(packageBytes);
        var semantic = new WordSemanticProjector().Project(package);

        var graph = new WordSectionGraphBuilder().Build(package, semantic);

        var section = Assert.Single(graph.Sections);
        Assert.True(section.IsImplicit);
        Assert.Null(section.NodeId);
        Assert.False(section.TitlePage);
        Assert.Equal("nextPage", section.BreakType);
        Assert.All(section.Bindings, binding =>
        {
            Assert.Equal(WordHeaderFooterBindingOrigin.Blank, binding.Origin);
            Assert.Null(binding.PartUri);
            Assert.Null(binding.EffectiveDisplayPartUri);
        });
    }

    [Fact]
    public void RejectsDuplicateVariantAndSectionLimitOverflow()
    {
        using var duplicateBytes = BuildMultiSectionPackage(
            evenAndOddHeaders: true,
            duplicateDefaultHeader: true
        );
        var reader = new OpcPackageReader();
        var duplicatePackage = reader.Read(duplicateBytes);
        var duplicateSemantic = new WordSemanticProjector().Project(duplicatePackage);
        Assert.Throws<WordSectionProjectionException>(() =>
            new WordSectionGraphBuilder().Build(
                duplicatePackage,
                duplicateSemantic
            )
        );

        using var limitedBytes = BuildMultiSectionPackage(evenAndOddHeaders: true);
        var limitedPackage = reader.Read(limitedBytes);
        var limitedSemantic = new WordSemanticProjector().Project(limitedPackage);
        Assert.Throws<WordSectionLimitException>(() =>
            new WordSectionGraphBuilder(
                new WordSectionGraphOptions { MaxSections = 1 }
            ).Build(limitedPackage, limitedSemantic)
        );
    }

    [Fact]
    public void ResolvesBundledPoiHeaderFooterFixture()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "upstream",
            "fixtures",
            "poi_diff_header_footer.docx"
        );
        var package = new OpcPackageReader().Read(path);
        var semantic = new WordSemanticProjector().Project(package);

        var graph = new WordSectionGraphBuilder().Build(package, semantic);

        var section = Assert.Single(graph.Sections);
        Assert.True(section.TitlePage);
        Assert.False(graph.EvenAndOddHeaders);
        Assert.Equal(
            "/word/header1.xml",
            section.Binding(
                WordHeaderFooterKind.Header,
                WordHeaderFooterVariant.Default
            ).EffectiveDisplayPartUri
        );
        Assert.Equal(
            "/word/header2.xml",
            section.Binding(
                WordHeaderFooterKind.Header,
                WordHeaderFooterVariant.First
            ).EffectiveDisplayPartUri
        );
        Assert.Empty(graph.UnboundStoryPartUris);
    }

    private static void AssertBinding(
        WordHeaderFooterBinding binding,
        bool enabled,
        WordHeaderFooterBindingOrigin origin,
        int? definitionSection,
        string? partUri,
        string? effectivePartUri
    )
    {
        Assert.Equal(enabled, binding.IsVariantEnabled);
        Assert.Equal(origin, binding.Origin);
        Assert.Equal(definitionSection, binding.DefinitionSectionOrdinal);
        Assert.Equal(partUri, binding.PartUri);
        Assert.Equal(effectivePartUri, binding.EffectiveDisplayPartUri);
    }

    private static MemoryStream BuildMultiSectionPackage(
        bool evenAndOddHeaders,
        bool duplicateDefaultHeader = false
    )
    {
        var duplicateReference = duplicateDefaultHeader
            ? "<w:headerReference w:type=\"default\" r:id=\"rIdHeaderFirst\"/>"
            : string.Empty;
        var documentXml = $"""
            <w:document
                xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml"
                xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body>
                <w:p w14:paraId="11111111">
                  <w:pPr><w:sectPr>
                    <w:headerReference w:type="default" r:id="rIdHeaderDefault"/>
                    {duplicateReference}
                    <w:headerReference w:type="first" r:id="rIdHeaderFirst"/>
                    <w:footerReference w:type="default" r:id="rIdFooter1"/>
                    <w:type w:val="continuous"/>
                    <w:pgSz w:w="16840" w:h="11900" w:orient="landscape"/>
                    <w:pgMar w:top="720" w:right="800" w:bottom="720" w:left="800" w:header="360" w:footer="360" w:gutter="0"/>
                    <w:cols w:num="2" w:space="360" w:equalWidth="1"/>
                    <w:titlePg/>
                  </w:sectPr></w:pPr>
                  <w:r><w:t>First section</w:t></w:r>
                </w:p>
                <w:p w14:paraId="22222222"><w:r><w:t>Second section</w:t></w:r></w:p>
                <w:sectPr>
                  <w:headerReference w:type="even" r:id="rIdHeaderEven2"/>
                  <w:footerReference w:type="default" r:id="rIdFooter2"/>
                  <w:titlePg w:val="0"/>
                </w:sectPr>
              </w:body>
            </w:document>
            """;
        var settingsXml = $"""
            <w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:evenAndOddHeaders w:val="{(evenAndOddHeaders ? "true" : "false")}"/>
            </w:settings>
            """;
        var storyParts = new Dictionary<string, (string ContentType, string Xml)>(
            StringComparer.Ordinal
        )
        {
            ["word/header-default.xml"] = (
                HeaderContentType,
                HeaderXml("Default header")
            ),
            ["word/header-first.xml"] = (
                HeaderContentType,
                HeaderXml("First header")
            ),
            ["word/header-even-2.xml"] = (
                HeaderContentType,
                HeaderXml("Second even header")
            ),
            ["word/header-unused.xml"] = (
                HeaderContentType,
                HeaderXml("Unbound header")
            ),
            ["word/footer-default-1.xml"] = (
                FooterContentType,
                FooterXml("First footer")
            ),
            ["word/footer-default-2.xml"] = (
                FooterContentType,
                FooterXml("Second footer")
            ),
        };
        var relationships = """
            <Relationship Id="rIdHeaderDefault" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header-default.xml"/>
            <Relationship Id="rIdHeaderFirst" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header-first.xml"/>
            <Relationship Id="rIdHeaderEven2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header-even-2.xml"/>
            <Relationship Id="rIdHeaderUnused" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header-unused.xml"/>
            <Relationship Id="rIdFooter1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer-default-1.xml"/>
            <Relationship Id="rIdFooter2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer-default-2.xml"/>
            <Relationship Id="rIdSettings" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings" Target="settings.xml"/>
            """;
        return BuildPackage(documentXml, relationships, storyParts, settingsXml);
    }

    private static MemoryStream BuildMinimalPackage() => BuildPackage(
        """
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body><w:p><w:r><w:t>Only section</w:t></w:r></w:p></w:body>
        </w:document>
        """,
        string.Empty,
        new Dictionary<string, (string ContentType, string Xml)>(StringComparer.Ordinal),
        settingsXml: null
    );

    private static MemoryStream BuildPackage(
        string documentXml,
        string documentRelationships,
        IReadOnlyDictionary<string, (string ContentType, string Xml)> storyParts,
        string? settingsXml
    )
    {
        var overrides = string.Join(
            string.Empty,
            storyParts.Select(part =>
                $"<Override PartName=\"/{part.Key}\" ContentType=\"{part.Value.ContentType}\"/>"
            )
        );
        if (settingsXml is not null)
        {
            overrides += $"<Override PartName=\"/word/settings.xml\" ContentType=\"{SettingsContentType}\"/>";
        }

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
                  {overrides}
                </Types>
                """
            );
            WriteEntry(
                archive,
                "_rels/.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """
            );
            WriteEntry(archive, "word/document.xml", documentXml);
            if (!string.IsNullOrEmpty(documentRelationships))
            {
                WriteEntry(
                    archive,
                    "word/_rels/document.xml.rels",
                    $"""
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                      {documentRelationships}
                    </Relationships>
                    """
                );
            }

            foreach (var part in storyParts)
            {
                WriteEntry(archive, part.Key, part.Value.Xml);
            }

            if (settingsXml is not null)
            {
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

        throw new DirectoryNotFoundException(
            "Could not locate the WordToolkit repository root."
        );
    }

    private static string HeaderXml(string text) => $"""
        <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:p><w:r><w:t>{text}</w:t></w:r></w:p>
        </w:hdr>
        """;

    private static string FooterXml(string text) => $"""
        <w:ftr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:p><w:r><w:t>{text}</w:t></w:r></w:p>
        </w:ftr>
        """;

    private const string HeaderContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml";
    private const string FooterContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml";
    private const string SettingsContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml";
}
