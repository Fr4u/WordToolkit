using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordFontTableGraphTests
{
    [Fact]
    public void BuildsTypedFontInventoryAndValidatesEmbeddedFaces()
    {
        using var bytes = BuildPackage(FontTableXml());
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordFontTableGraphBuilder().Build(package, semantic);

        Assert.True(graph.HasFontTablePart);
        Assert.Equal("/word/fontTable.xml", graph.FontTablePartUri);
        Assert.Equal(2, graph.Fonts.Count);
        Assert.True(graph.TryGetFont("cambria", out var cambria));
        Assert.NotNull(cambria);
        Assert.Equal("Cambria Alt", cambria!.AlternateName);
        Assert.Equal("roman", cambria.Family);
        Assert.Equal("variable", cambria.Pitch);
        Assert.False(cambria.NotTrueType);
        Assert.Equal("E00002FF", cambria.Signature!.UnicodeSubset0);
        Assert.Equal(2, cambria.EmbeddedFaces.Count);
        Assert.All(cambria.EmbeddedFaces, face => Assert.True(face.IsWordReadable));
        Assert.Contains(
            cambria.EmbeddedFaces,
            face => face.Kind == WordEmbeddedFontFaceKind.Regular
                && face.IsObfuscated
                && !face.HasAllZeroFontKey
                && face.ByteLength > 0
                && face.Sha256 is not null
        );
        Assert.Contains(
            cambria.EmbeddedFaces,
            face => face.Kind == WordEmbeddedFontFaceKind.Bold
                && !face.IsObfuscated
                && face.HasAllZeroFontKey
        );
        Assert.True(graph.TryGetFont("Bitmap Face", out var bitmap));
        Assert.True(bitmap!.NotTrueType);
        Assert.False(Assert.Single(bitmap.EmbeddedFaces).IsWordReadable);
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "FONT_TABLE_BITMAP_FONT_UNSUPPORTED_BY_WORD"
        );
        Assert.Contains("/word/fonts/unused.odttf", graph.UnreferencedEmbeddedFontPartUris);
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "FONT_TABLE_UNREFERENCED_FONT_PART"
        );
        Assert.Contains(
            graph.UnmodeledRootElements,
            element => element.EndsWith("}futureFonts", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void MissingFontTablePartIsAValidEmptyGraph()
    {
        using var bytes = BuildPackage(fontTableXml: null);
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordFontTableGraphBuilder().Build(package, semantic);

        Assert.False(graph.HasFontTablePart);
        Assert.Empty(graph.Fonts);
        Assert.Empty(graph.Issues);
    }

    [Fact]
    public void AcceptsStrictFontTableAndFontRelationships()
    {
        const string strictWord = "http://purl.oclc.org/ooxml/wordprocessingml/main";
        const string strictRelationships =
            "http://purl.oclc.org/ooxml/officeDocument/relationships";
        using var bytes = BuildPackage(
            FontTableXml(strictWord, strictRelationships),
            strictRelationships: true
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordFontTableGraphBuilder().Build(package, semantic);

        Assert.True(graph.TryGetFont("Cambria", out var cambria));
        Assert.All(cambria!.EmbeddedFaces, face => Assert.True(face.IsWordReadable));
    }

    [Fact]
    public void DuplicateNamesAreDiagnosedAndNotReturnedByUniqueLookup()
    {
        var xml = FontTableXml().Replace(
            "<w:futureFonts/>",
            "<w:font w:name=\"CAMBRIA\"><w:family w:val=\"swiss\"/></w:font><w:futureFonts/>",
            StringComparison.Ordinal
        );
        using var bytes = BuildPackage(xml);
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordFontTableGraphBuilder().Build(package, semantic);

        Assert.False(graph.TryGetFont("Cambria", out _));
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "FONT_TABLE_DUPLICATE_NAME"
        );
    }

    [Fact]
    public void NonZeroKeyMakesPlainTtfUnreadableByWord()
    {
        var xml = FontTableXml().Replace(
            "00000000-0000-0000-0000-000000000000",
            "11111111-2222-3333-4444-555555555555",
            StringComparison.Ordinal
        );
        using var bytes = BuildPackage(xml);
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordFontTableGraphBuilder().Build(package, semantic);

        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "FONT_TABLE_TTF_REQUIRES_ZERO_KEY"
        );
        Assert.True(graph.TryGetFont("Cambria", out var cambria));
        Assert.False(
            cambria!.EmbeddedFaces.Single(face =>
                face.Kind == WordEmbeddedFontFaceKind.Bold
            ).IsWordReadable
        );
    }

    [Fact]
    public void RejectsDuplicateFaceElementsAndConfiguredLimits()
    {
        var duplicate = FontTableXml().Replace(
            "<w:embedRegular r:id=\"rIdRegular\" w:fontKey=\"11111111-2222-3333-4444-555555555555\"/>",
            "<w:embedRegular r:id=\"rIdRegular\" w:fontKey=\"11111111-2222-3333-4444-555555555555\"/><w:embedRegular r:id=\"rIdRegular\" w:fontKey=\"11111111-2222-3333-4444-555555555555\"/>",
            StringComparison.Ordinal
        );
        using var duplicateBytes = BuildPackage(duplicate);
        var duplicateSnapshots = ReadSnapshots(duplicateBytes);
        Assert.Throws<WordFontTableProjectionException>(() =>
            new WordFontTableGraphBuilder().Build(
                duplicateSnapshots.Package,
                duplicateSnapshots.Semantic
            )
        );

        using var limitedBytes = BuildPackage(FontTableXml());
        var limitedSnapshots = ReadSnapshots(limitedBytes);
        Assert.Throws<WordFontTableLimitException>(() =>
            new WordFontTableGraphBuilder(
                new WordFontTableGraphOptions { MaxFontTablePartBytes = 128 }
            ).Build(limitedSnapshots.Package, limitedSnapshots.Semantic)
        );
    }

    [Fact]
    public void BuildsGraphsForEveryBundledDocxFontTable()
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
        var fontTables = 0;
        foreach (var path in paths)
        {
            var package = reader.Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            var graph = new WordFontTableGraphBuilder().Build(package, semantic);
            Assert.Equal(package.Fingerprint, graph.PackageFingerprint);
            if (graph.HasFontTablePart)
            {
                fontTables++;
                Assert.NotEmpty(graph.Fonts);
            }
        }

        Assert.True(fontTables >= 40);
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
        string? fontTableXml,
        bool strictRelationships = false
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var fontOverride = fontTableXml is null
                ? string.Empty
                : "<Override PartName=\"/word/fontTable.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml\"/>";
            WriteEntry(
                archive,
                "[Content_Types].xml",
                $"""
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="odttf" ContentType="application/vnd.openxmlformats-officedocument.obfuscatedFont"/>
                  <Default Extension="ttf" ContentType="application/x-font-ttf"/>
                  <Default Extension="fntdata" ContentType="application/x-fontdata"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  {fontOverride}
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
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Fonts</w:t></w:r></w:p></w:body></w:document>
                """
            );
            if (fontTableXml is not null)
            {
                var baseRelationship = strictRelationships
                    ? "http://purl.oclc.org/ooxml/officeDocument/relationships"
                    : "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
                WriteEntry(
                    archive,
                    "word/_rels/document.xml.rels",
                    $"""
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdFonts" Type="{baseRelationship}/fontTable" Target="fontTable.xml"/></Relationships>
                    """
                );
                WriteEntry(archive, "word/fontTable.xml", fontTableXml);
                WriteEntry(
                    archive,
                    "word/_rels/fontTable.xml.rels",
                    $"""
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                      <Relationship Id="rIdRegular" Type="{baseRelationship}/font" Target="fonts/font1.odttf"/>
                      <Relationship Id="rIdBold" Type="{baseRelationship}/font" Target="fonts/font2.ttf"/>
                      <Relationship Id="rIdBitmap" Type="{baseRelationship}/font" Target="fonts/font3.fntdata"/>
                      <Relationship Id="rIdUnused" Type="{baseRelationship}/font" Target="fonts/unused.odttf"/>
                    </Relationships>
                    """
                );
                WriteBytes(archive, "word/fonts/font1.odttf", [1, 2, 3, 4]);
                WriteBytes(archive, "word/fonts/font2.ttf", [5, 6, 7, 8]);
                WriteBytes(archive, "word/fonts/font3.fntdata", [9, 10, 11]);
                WriteBytes(archive, "word/fonts/unused.odttf", [12, 13]);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static string FontTableXml(
        string wordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main",
        string relationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
    ) => $"""
        <w:fonts xmlns:w="{wordNamespace}" xmlns:r="{relationshipNamespace}">
          <w:font w:name="Cambria">
            <w:altName w:val="Cambria Alt"/>
            <w:panose1 w:val="02040503050406030204"/>
            <w:charset w:val="00"/>
            <w:family w:val="roman"/>
            <w:pitch w:val="variable"/>
            <w:sig w:usb0="E00002FF" w:usb1="4000004B" w:usb2="00000000" w:usb3="00000000" w:csb0="0000019F" w:csb1="00000000"/>
            <w:embedRegular r:id="rIdRegular" w:fontKey="11111111-2222-3333-4444-555555555555"/>
            <w:embedBold r:id="rIdBold" w:fontKey="00000000-0000-0000-0000-000000000000"/>
          </w:font>
          <w:font w:name="Bitmap Face">
            <w:notTrueType/>
            <w:charset w:val="00"/>
            <w:family w:val="decorative"/>
            <w:pitch w:val="fixed"/>
            <w:embedItalic r:id="rIdBitmap" w:fontKey="22222222-3333-4444-5555-666666666666"/>
          </w:font>
          <w:futureFonts/>
        </w:fonts>
        """;

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(Encoding.UTF8.GetBytes(content));
    }

    private static void WriteBytes(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(content);
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
