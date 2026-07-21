using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordThemeGraphTests
{
    [Fact]
    public void BuildsTypedThemeAndResolvesWordFontsAndColors()
    {
        using var bytes = BuildPackage(ThemeXml());
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordThemeGraphBuilder().Build(package, semantic);

        Assert.True(graph.HasThemePart);
        Assert.Equal("/word/theme/theme1.xml", graph.ThemePartUri);
        Assert.Equal("Office", graph.Name);
        Assert.Equal("Office", graph.ColorScheme!.Name);
        Assert.Equal(12, graph.ColorScheme.Colors.Count);
        Assert.Equal("Office", graph.FontScheme!.Name);
        Assert.Equal("Cambria", graph.FontScheme.Major.Latin.Typeface);
        Assert.Equal("Calibri", graph.FontScheme.Minor.Latin.Typeface);
        Assert.Equal("ＭＳ ゴシック", Assert.Single(graph.FontScheme.Major.SupplementalFonts).Typeface);
        Assert.Equal(3, graph.FormatScheme!.FillStyleCount);
        Assert.Equal(2, graph.FormatScheme.LineStyleCount);
        Assert.Equal(1, graph.FormatScheme.EffectStyleCount);
        Assert.Equal(3, graph.FormatScheme.BackgroundFillStyleCount);
        Assert.Empty(graph.Issues);

        var shaded = graph.ResolveColor("accent2", themeShade: "BF");
        Assert.Equal("C0504D", shaded.BaseRgb);
        Assert.Equal("943734", shaded.EffectiveRgb);
        var tinted = graph.ResolveColor("accent1", themeTint: "99");
        Assert.Equal("95B3D7", tinted.EffectiveRgb);
        var tintWins = graph.ResolveColor(
            "accent1",
            themeTint: "99",
            themeShade: "00"
        );
        Assert.Equal("95B3D7", tintWins.EffectiveRgb);
        Assert.Equal("000000", graph.ResolveColor("text1").EffectiveRgb);
        Assert.Equal("FFFFFF", graph.ResolveColor("background1").EffectiveRgb);

        var major = graph.ResolveFont("majorHAnsi");
        Assert.Equal(WordThemeFontCollectionKind.Major, major.CollectionKind);
        Assert.Equal(WordThemeFontRole.Latin, major.Role);
        Assert.Equal("Cambria", major.Typeface);
        Assert.Equal("Calibri", graph.ResolveFont("minorAscii").Typeface);
        Assert.Throws<WordThemeResolutionException>(() =>
            graph.ResolveFont("minorEastAsia")
        );
    }

    [Fact]
    public void MissingThemePartIsAValidEmptyGraph()
    {
        using var bytes = BuildPackage(themeXml: null);
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordThemeGraphBuilder().Build(package, semantic);

        Assert.False(graph.HasThemePart);
        Assert.Null(graph.ColorScheme);
        Assert.Null(graph.FontScheme);
        Assert.Empty(graph.Issues);
        Assert.Throws<WordThemeResolutionException>(() =>
            graph.ResolveColor("accent1")
        );
    }

    [Fact]
    public void AcceptsStrictThemeRelationshipAndDrawingNamespace()
    {
        using var bytes = BuildPackage(
            ThemeXml("http://purl.oclc.org/ooxml/drawingml/main"),
            strictThemeRelationship: true
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordThemeGraphBuilder().Build(package, semantic);

        Assert.Equal("4F81BD", graph.ResolveColor("accent1").EffectiveRgb);
        Assert.Equal("Calibri", graph.ResolveFont("minorHAnsi").Typeface);
    }

    [Fact]
    public void ReportsMissingAndEnvironmentalColorsWithoutInventingRgb()
    {
        var theme = ThemeXml()
            .Replace(
                "<a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1>",
                "<a:dk1><a:sysClr val=\"windowText\"/></a:dk1>",
                StringComparison.Ordinal
            )
            .Replace(
                "<a:folHlink><a:srgbClr val=\"800080\"/></a:folHlink>",
                string.Empty,
                StringComparison.Ordinal
            );
        using var bytes = BuildPackage(theme);
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordThemeGraphBuilder().Build(package, semantic);

        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "THEME_COLOR_SOURCE_ENVIRONMENTAL"
                && issue.ColorSlot == "dk1"
        );
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "THEME_COLOR_SLOT_MISSING"
                && issue.ColorSlot == "folHlink"
        );
        Assert.Throws<WordThemeResolutionException>(() => graph.ResolveColor("text1"));
        Assert.Throws<WordThemeResolutionException>(() =>
            graph.ResolveColor("followedHyperlink")
        );
    }

    [Fact]
    public void RejectsDuplicateThemeRelationshipsAndConfiguredLimits()
    {
        using var duplicateBytes = BuildPackage(
            ThemeXml(),
            duplicateThemeRelationship: true
        );
        var duplicateSnapshots = ReadSnapshots(duplicateBytes);
        Assert.Throws<WordThemeProjectionException>(() =>
            new WordThemeGraphBuilder().Build(
                duplicateSnapshots.Package,
                duplicateSnapshots.Semantic
            )
        );

        using var limitedBytes = BuildPackage(ThemeXml());
        var limitedSnapshots = ReadSnapshots(limitedBytes);
        Assert.Throws<WordThemeLimitException>(() =>
            new WordThemeGraphBuilder(
                new WordThemeGraphOptions { MaxThemePartBytes = 128 }
            ).Build(limitedSnapshots.Package, limitedSnapshots.Semantic)
        );
    }

    [Fact]
    public void BuildsGraphsForEveryBundledDocxThemePart()
    {
        var fixtureDirectory = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "upstream",
            "fixtures"
        );
        var paths = Directory.EnumerateFiles(fixtureDirectory, "*.docx").ToArray();
        Assert.NotEmpty(paths);
        var reader = new OpcPackageReader();
        var themeParts = 0;
        foreach (var path in paths)
        {
            var package = reader.Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            var graph = new WordThemeGraphBuilder().Build(package, semantic);
            Assert.Equal(package.Fingerprint, graph.PackageFingerprint);
            if (graph.HasThemePart)
            {
                themeParts++;
                Assert.Equal(12, graph.ColorScheme!.Colors.Count);
                Assert.NotEmpty(graph.FontScheme!.Major.SupplementalFonts);
            }
        }

        Assert.True(themeParts >= 40);
    }

    private static (
        OpcPackageSnapshot Package,
        WordSemanticDocument Semantic
    ) ReadSnapshots(Stream bytes)
    {
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        return (package, semantic);
    }

    private static MemoryStream BuildPackage(
        string? themeXml,
        bool strictThemeRelationship = false,
        bool duplicateThemeRelationship = false
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var themeOverride = themeXml is null
                ? string.Empty
                : "<Override PartName=\"/word/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>";
            WriteEntry(
                archive,
                "[Content_Types].xml",
                $"""
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  {themeOverride}
                </Types>
                """
            );
            WriteEntry(
                archive,
                "_rels/.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                """
            );
            WriteEntry(
                archive,
                "word/document.xml",
                """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Theme</w:t></w:r></w:p></w:body></w:document>
                """
            );
            if (themeXml is not null)
            {
                var relationshipType = strictThemeRelationship
                    ? "http://purl.oclc.org/ooxml/officeDocument/relationships/theme"
                    : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";
                var duplicate = duplicateThemeRelationship
                    ? $"<Relationship Id=\"rIdTheme2\" Type=\"{relationshipType}\" Target=\"theme/theme1.xml\"/>"
                    : string.Empty;
                WriteEntry(
                    archive,
                    "word/_rels/document.xml.rels",
                    $"""
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdTheme" Type="{relationshipType}" Target="theme/theme1.xml"/>{duplicate}</Relationships>
                    """
                );
                WriteEntry(archive, "word/theme/theme1.xml", themeXml);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static string ThemeXml(
        string drawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main"
    ) => $"""
        <a:theme xmlns:a="{drawingNamespace}" name="Office">
          <a:themeElements>
            <a:clrScheme name="Office">
              <a:dk1><a:sysClr val="windowText" lastClr="000000"/></a:dk1>
              <a:lt1><a:sysClr val="window" lastClr="FFFFFF"/></a:lt1>
              <a:dk2><a:srgbClr val="1F497D"/></a:dk2>
              <a:lt2><a:srgbClr val="EEECE1"/></a:lt2>
              <a:accent1><a:srgbClr val="4F81BD"/></a:accent1>
              <a:accent2><a:srgbClr val="C0504D"/></a:accent2>
              <a:accent3><a:srgbClr val="9BBB59"/></a:accent3>
              <a:accent4><a:srgbClr val="8064A2"/></a:accent4>
              <a:accent5><a:srgbClr val="4BACC6"/></a:accent5>
              <a:accent6><a:srgbClr val="F79646"/></a:accent6>
              <a:hlink><a:srgbClr val="0000FF"/></a:hlink>
              <a:folHlink><a:srgbClr val="800080"/></a:folHlink>
            </a:clrScheme>
            <a:fontScheme name="Office">
              <a:majorFont><a:latin typeface="Cambria"/><a:ea typeface=""/><a:cs typeface=""/><a:font script="Jpan" typeface="ＭＳ ゴシック"/></a:majorFont>
              <a:minorFont><a:latin typeface="Calibri"/><a:ea typeface=""/><a:cs typeface=""/><a:font script="Jpan" typeface="ＭＳ 明朝"/></a:minorFont>
            </a:fontScheme>
            <a:fmtScheme name="Office">
              <a:fillStyleLst><a:solidFill/><a:gradFill/><a:solidFill/></a:fillStyleLst>
              <a:lnStyleLst><a:ln/><a:ln/></a:lnStyleLst>
              <a:effectStyleLst><a:effectStyle/></a:effectStyleLst>
              <a:bgFillStyleLst><a:solidFill/><a:solidFill/><a:gradFill/></a:bgFillStyleLst>
            </a:fmtScheme>
          </a:themeElements>
        </a:theme>
        """;

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(Encoding.UTF8.GetBytes(content));
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
}
