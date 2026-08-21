using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordSettingsGraphTests
{
    [Fact]
    public void BuildsTypedSettingsWithoutExposingProtectionSecretsAsFields()
    {
        using var bytes = BuildPackage(SettingsXml());
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordSettingsGraphBuilder().Build(package, semantic);

        Assert.True(graph.HasSettingsPart);
        Assert.Equal("/word/settings.xml", graph.SettingsPartUri);
        Assert.True(graph.TrackRevisions);
        Assert.True(graph.EvenAndOddHeaders);
        Assert.True(graph.EmbedTrueTypeFonts);
        Assert.True(graph.SaveSubsetFonts);
        Assert.Equal("ja-JP", graph.ThemeFontLanguages!.Latin);
        Assert.Equal("zh-TW", graph.ThemeFontLanguages.EastAsian);
        Assert.Equal("ar-SA", graph.ThemeFontLanguages.ComplexScript);
        Assert.Equal(15, graph.Compatibility!.CompatibilityMode);
        Assert.Contains(
            graph.Compatibility.LegacyOptions,
            option => option.Name == "useWord97LineBreakRules" && option.Value
        );
        Assert.True(graph.DocumentProtection!.IsEnforced);
        Assert.False(graph.DocumentProtection.FormattingRestricted);
        Assert.True(graph.DocumentProtection.HasHash);
        Assert.True(graph.DocumentProtection.HasSalt);
        Assert.Equal(100_000, graph.DocumentProtection.SpinCount);
        Assert.Equal("SHA-512", graph.DocumentProtection.AlgorithmName);
        Assert.DoesNotContain(
            graph.DocumentProtection.GetType().GetProperties(),
            property => property.Name.Contains("HashValue", StringComparison.Ordinal)
                || property.Name.Contains("SaltValue", StringComparison.Ordinal)
        );
        var variable = Assert.Single(graph.DocumentVariables);
        Assert.Equal("CustomerId", variable.Name);
        Assert.Equal("secret-value", variable.Value);
        Assert.True(graph.AttachedTemplate!.Relationship.IsResolved);
        Assert.Equal(OpcRelationshipTargetMode.External, graph.AttachedTemplate.Relationship.TargetMode);
        Assert.Equal("letters", graph.MailMerge!.MainDocumentType);
        Assert.True(graph.MailMerge.LinkToQuery);
        Assert.True(graph.MailMerge.DataSource!.IsResolved);
        Assert.Equal("print", graph.View.View);
        Assert.Equal(125, graph.View.ZoomPercent);
        Assert.Equal(720, graph.View.DefaultTabStopTwips);
        Assert.Equal(300, graph.View.DefaultImageDpi);
        Assert.Equal(",", graph.DecimalSymbol);
        Assert.Equal(";", graph.ListSeparator);
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "SETTINGS_DOCUMENT_PROTECTION_NOT_SECURITY_BOUNDARY"
        );
        Assert.Contains(
            graph.UnmodeledRootElements,
            element => element.EndsWith("}mathPr", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void MissingSettingsPartIsAValidEmptyGraph()
    {
        using var bytes = BuildPackage(settingsXml: null);
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordSettingsGraphBuilder().Build(package, semantic);

        Assert.False(graph.HasSettingsPart);
        Assert.False(graph.TrackRevisions);
        Assert.Null(graph.ThemeFontLanguages);
        Assert.Empty(graph.DocumentVariables);
        Assert.Empty(graph.Inventory);
    }

    [Fact]
    public void AcceptsStrictSettingsRelationshipAndNamespace()
    {
        const string strict = "http://purl.oclc.org/ooxml/wordprocessingml/main";
        using var bytes = BuildPackage(
            SettingsXml(strict),
            strictSettingsRelationship: true
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordSettingsGraphBuilder().Build(package, semantic);

        Assert.True(graph.TrackRevisions);
        Assert.Equal(15, graph.Compatibility!.CompatibilityMode);
    }

    [Fact]
    public void ReportsConflictingCompatibilityModesAndInactiveSubsetting()
    {
        var xml = SettingsXml()
            .Replace(
                "<w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/>",
                "<w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"14\"/>",
                StringComparison.Ordinal
            )
            .Replace(
                "<w:embedTrueTypeFonts/>",
                "<w:embedTrueTypeFonts w:val=\"false\"/>",
                StringComparison.Ordinal
            );
        using var bytes = BuildPackage(xml);
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordSettingsGraphBuilder().Build(package, semantic);

        Assert.Null(graph.Compatibility!.CompatibilityMode);
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "SETTINGS_COMPATIBILITY_MODE_CONFLICT"
        );
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "SETTINGS_FONT_SUBSETTING_INACTIVE"
        );
    }

    [Fact]
    public void RejectsDuplicateSingletonsAndConfiguredLimits()
    {
        var duplicate = SettingsXml().Replace(
            "<w:trackRevisions/>",
            "<w:trackRevisions/><w:trackRevisions w:val=\"false\"/>",
            StringComparison.Ordinal
        );
        using var duplicateBytes = BuildPackage(duplicate);
        var duplicateSnapshots = ReadSnapshots(duplicateBytes);
        Assert.Throws<WordSettingsProjectionException>(() =>
            new WordSettingsGraphBuilder().Build(
                duplicateSnapshots.Package,
                duplicateSnapshots.Semantic
            )
        );

        using var limitedBytes = BuildPackage(SettingsXml());
        var limitedSnapshots = ReadSnapshots(limitedBytes);
        Assert.Throws<WordSettingsLimitException>(() =>
            new WordSettingsGraphBuilder(
                new WordSettingsGraphOptions { MaxSettingsPartBytes = 128 }
            ).Build(limitedSnapshots.Package, limitedSnapshots.Semantic)
        );
    }

    [Fact]
    public void BuildsGraphsForEveryBundledDocxSettingsPart()
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
        var settingsParts = 0;
        foreach (var path in paths)
        {
            var package = reader.Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            var graph = new WordSettingsGraphBuilder().Build(package, semantic);
            Assert.Equal(package.Fingerprint, graph.PackageFingerprint);
            if (graph.HasSettingsPart)
            {
                settingsParts++;
                Assert.NotEmpty(graph.Inventory);
            }
        }

        Assert.True(settingsParts >= 40);
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
        string? settingsXml,
        bool strictSettingsRelationship = false
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var settingsOverride = settingsXml is null
                ? string.Empty
                : "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>";
            WriteEntry(
                archive,
                "[Content_Types].xml",
                $"""
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  {settingsOverride}
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
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Settings</w:t></w:r></w:p></w:body></w:document>
                """
            );
            if (settingsXml is not null)
            {
                var relationshipType = strictSettingsRelationship
                    ? "http://purl.oclc.org/ooxml/officeDocument/relationships/settings"
                    : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings";
                WriteEntry(
                    archive,
                    "word/_rels/document.xml.rels",
                    $"""
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdSettings" Type="{relationshipType}" Target="settings.xml"/></Relationships>
                    """
                );
                WriteEntry(archive, "word/settings.xml", settingsXml);
                WriteEntry(
                    archive,
                    "word/_rels/settings.xml.rels",
                    """
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                      <Relationship Id="rIdTemplate" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/attachedTemplate" Target="https://example.invalid/template.dotx" TargetMode="External"/>
                      <Relationship Id="rIdData" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/mailMergeSource" Target="https://example.invalid/data.csv" TargetMode="External"/>
                    </Relationships>
                    """
                );
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static string SettingsXml(
        string wordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
    )
    {
        var relationshipNamespace = wordNamespace.Contains("purl.oclc.org", StringComparison.Ordinal)
            ? "http://purl.oclc.org/ooxml/officeDocument/relationships"
            : "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        return $"""
            <w:settings xmlns:w="{wordNamespace}" xmlns:r="{relationshipNamespace}" xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml">
              <w:view w:val="print"/>
              <w:zoom w:val="bestFit" w:percent="125"/>
              <w:trackRevisions/>
              <w:evenAndOddHeaders w:val="on"/>
              <w:embedTrueTypeFonts/>
              <w:saveSubsetFonts w:val="1"/>
              <w:themeFontLang w:val="ja-JP" w:eastAsia="zh-TW" w:bidi="ar-SA"/>
              <w:compat>
                <w:useWord97LineBreakRules/>
                <w:compatSetting w:name="compatibilityMode" w:uri="http://schemas.microsoft.com/office/word" w:val="15"/>
              </w:compat>
              <w:documentProtection w:edit="comments" w:enforcement="1" w:formatting="0" w14:algorithmName="SHA-512" w14:spinCount="100000" w14:hashValue="secret-hash" w14:saltValue="secret-salt" w:future="x"/>
              <w:writeProtection w:recommended="true" w:hash="legacy-hash" w:salt="legacy-salt"/>
              <w:docVars><w:docVar w:name="CustomerId" w:val="secret-value"/></w:docVars>
              <w:attachedTemplate r:id="rIdTemplate"/>
              <w:mailMerge>
                <w:mainDocumentType w:val="letters"/>
                <w:dataType w:val="native"/>
                <w:linkToQuery/>
                <w:query w:val="SELECT * FROM Customers"/>
                <w:connectString w:val="Server=secret"/>
                <w:dataSource r:id="rIdData"/>
                <w:odso/>
              </w:mailMerge>
              <w:defaultTabStop w:val="720"/>
              <w14:defaultImageDpi w14:val="300"/>
              <w:decimalSymbol w:val=","/>
              <w:listSeparator w:val=";"/>
              <w:mathPr/>
            </w:settings>
            """;
    }

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
