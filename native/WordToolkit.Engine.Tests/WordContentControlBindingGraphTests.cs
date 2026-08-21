using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordContentControlBindingGraphTests
{
    private const string W =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string W14 =
        "http://schemas.microsoft.com/office/word/2010/wordml";
    private const string W15 =
        "http://schemas.microsoft.com/office/word/2012/wordml";
    private const string StoreItemId =
        "{A6C895A1-6B29-470C-84D7-6D14B798EAE7}";
    private const string StoreItemIdNormalized =
        "a6c895a1-6b29-470c-84d7-6d14b798eae7";

    [Fact]
    public void ResolvesPhysicalCustomXmlBindingWithoutReturningStoreValues()
    {
        using var bytes = BuildPackage(SingleControlDocument());
        var package = new OpcPackageReader().Read(bytes);

        var graph = new WordContentControlBindingGraphBuilder().Build(package);

        Assert.Equal(package.Fingerprint, graph.PackageFingerprint);
        var store = Assert.Single(graph.Stores);
        Assert.Equal(WordBindingStoreKind.CustomXml, store.Kind);
        Assert.Equal(StoreItemIdNormalized, store.ItemId);
        Assert.Equal("/customXml/item1.xml", store.PartUri);
        Assert.Equal("/customXml/itemProps1.xml", store.PropertiesPartUri);
        Assert.Equal("urn:wordtoolkit:test", store.RootNamespaceUri);
        Assert.Equal("profile", store.RootLocalName);
        Assert.Equal(new[] { "urn:wordtoolkit:test" }, store.SchemaReferences);
        Assert.Equal(1, store.IncomingRelationshipCount);
        Assert.True(store.PropertiesRelationshipResolved);
        Assert.True(store.Parsed);

        var control = Assert.Single(graph.Controls);
        Assert.Equal(WordContentControlType.PlainText, control.Type);
        Assert.True(control.TypeExplicit);
        Assert.Equal(WordContentControlLevel.Block, control.Level);
        Assert.Equal("42", control.NativeId);
        Assert.Equal("Customer", control.Alias);
        Assert.Equal("CustomerName", control.Tag);
        Assert.Equal(
            WordContentControlLock.ControlAndContentLocked,
            control.Lock
        );

        var binding = Assert.Single(graph.Bindings);
        Assert.Equal(control.Id, binding.ControlId);
        Assert.Equal(store.Id, binding.StoreId);
        Assert.Equal(StoreItemIdNormalized, binding.StoreItemId);
        Assert.Equal(WordBindingResolutionStatus.Resolved, binding.Status);
        Assert.Equal("urn:wordtoolkit:test", binding.NamespaceMappings["wt"]);
        var target = Assert.Single(graph.Targets);
        Assert.Equal(binding.Id, target.BindingId);
        Assert.Equal(store.Id, target.StoreId);
        Assert.Equal("urn:wordtoolkit:test", target.NamespaceUri);
        Assert.Equal("name", target.LocalName);
        Assert.Equal(new[] { target.Id }, binding.TargetIds);
        Assert.Empty(graph.RepeatingSections);
        Assert.DoesNotContain(
            graph.Issues,
            issue => issue.Severity == WordContentControlIssueSeverity.Error
        );
    }

    [Fact]
    public void JoinsRepeatingSectionItemsToEverySelectedXmlElement()
    {
        var document = $$"""
            <w:document xmlns:w="{{W}}" xmlns:w15="{{W15}}">
              <w:body>
                <w:sdt>
                  <w:sdtPr>
                    <w:id w:val="500"/>
                    <w15:repeatingSection>
                      <w15:sectionTitle w15:val="Books"/>
                      <w15:doNotAllowInsertDeleteSection/>
                    </w15:repeatingSection>
                    <w15:dataBinding w15:storeItemID="{{StoreItemId}}"
                      w15:xpath="/wt:profile[1]/wt:book"
                      w15:prefixMappings="xmlns:wt='urn:wordtoolkit:test'"/>
                  </w:sdtPr>
                  <w:sdtContent>
                    {{RepeatingItem(501, "One")}}
                    {{RepeatingItem(502, "Two")}}
                  </w:sdtContent>
                </w:sdt>
                <w:sectPr/>
              </w:body>
            </w:document>
            """;
        const string customXml =
            "<wt:profile xmlns:wt='urn:wordtoolkit:test'>"
            + "<wt:book/><wt:book/></wt:profile>";
        using var bytes = BuildPackage(document, customXml);

        var graph = new WordContentControlBindingGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        Assert.Equal(3, graph.Controls.Count);
        var section = Assert.Single(graph.RepeatingSections);
        Assert.Equal(2, section.ItemControlIds.Count);
        Assert.Equal(2, section.BindingTargetCount);
        Assert.True(section.CardinalityMatches);
        Assert.True(section.DoNotAllowInsertDeleteSection);
        var container = graph.Controls.Single(control => control.Id == section.ControlId);
        Assert.Equal(WordContentControlType.RepeatingSection, container.Type);
        Assert.Equal("Books", container.RepeatingSectionTitle);
        Assert.All(
            section.ItemControlIds,
            itemId => Assert.Equal(
                container.Id,
                graph.Controls.Single(control => control.Id == itemId).ParentControlId
            )
        );
        Assert.Equal(2, Assert.Single(graph.Bindings).TargetIds.Count);
        Assert.DoesNotContain(
            graph.Issues,
            issue => issue.Code == "CCB_REPEATING_SECTION_CARDINALITY"
        );
    }

    [Theory]
    [InlineData("lo_sdt_content.docx", WordBindingStoreKind.CoreProperties)]
    [InlineData("lo_groupshape_sdt.docx", WordBindingStoreKind.ExtendedProperties)]
    public void ResolvesWordBuiltInPropertyStoresFromRealProducerFixtures(
        string fileName,
        WordBindingStoreKind expectedKind
    )
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "upstream",
            "fixtures",
            fileName
        );
        var package = new OpcPackageReader().Read(path);

        var graph = new WordContentControlBindingGraphBuilder().Build(package);

        var store = Assert.Single(graph.Stores, item => item.Kind == expectedKind);
        var binding = Assert.Single(graph.Bindings, item => item.StoreId == store.Id);
        Assert.Equal(WordBindingResolutionStatus.Resolved, binding.Status);
        Assert.NotEmpty(binding.TargetIds);
        Assert.All(
            binding.TargetIds,
            targetId => Assert.Equal(
                store.Id,
                graph.Targets.Single(target => target.Id == targetId).StoreId
            )
        );
    }

    [Fact]
    public void ResolvesTheRealAdvancedTortureCustomXmlBinding()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "examples",
            "advanced",
            "WordToolkit-advanced-torture-test.docx"
        );
        var package = new OpcPackageReader().Read(path);

        var graph = new WordContentControlBindingGraphBuilder().Build(package);

        var binding = Assert.Single(graph.Bindings);
        Assert.Equal(WordBindingResolutionStatus.Resolved, binding.Status);
        Assert.Single(binding.TargetIds);
        Assert.Equal(
            WordBindingStoreKind.CustomXml,
            graph.Stores.Single(store => store.Id == binding.StoreId).Kind
        );
    }

    [Theory]
    [InlineData("not-a-guid", "/wt:profile[1]/wt:name[1]", "xmlns:wt='urn:wordtoolkit:test'", WordBindingResolutionStatus.StoreIdInvalid)]
    [InlineData("{11111111-1111-1111-1111-111111111111}", "/wt:profile[1]/wt:name[1]", "xmlns:wt='urn:wordtoolkit:test'", WordBindingResolutionStatus.StoreMissing)]
    [InlineData(StoreItemId, "/wt:profile[1]/wt:name[1]", "xmlns:wt='unterminated", WordBindingResolutionStatus.PrefixMappingsInvalid)]
    [InlineData(StoreItemId, "//wt:name", "xmlns:wt='urn:wordtoolkit:test'", WordBindingResolutionStatus.XPathUnsupported)]
    [InlineData(StoreItemId, "/missing:profile[1]", "xmlns:wt='urn:wordtoolkit:test'", WordBindingResolutionStatus.XPathInvalid)]
    [InlineData(StoreItemId, "/wt:profile[1]/wt:absent[1]", "xmlns:wt='urn:wordtoolkit:test'", WordBindingResolutionStatus.TargetMissing)]
    public void FailsClosedForUnresolvableBindingMetadata(
        string storeItemId,
        string xpath,
        string prefixMappings,
        WordBindingResolutionStatus expectedStatus
    )
    {
        using var bytes = BuildPackage(
            SingleControlDocument(storeItemId, xpath, prefixMappings)
        );

        var graph = new WordContentControlBindingGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        Assert.Equal(expectedStatus, Assert.Single(graph.Bindings).Status);
        Assert.Empty(graph.Targets);
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "CCB_BINDING_" + expectedStatus.ToString().ToUpperInvariant()
        );
    }

    [Fact]
    public void RetainsUnreadableStoreMetadataAndMarksTheBindingUnreadable()
    {
        using var bytes = BuildPackage(
            SingleControlDocument(),
            "<wt:profile xmlns:wt='urn:wordtoolkit:test'><wt:name></wt:profile>"
        );

        var graph = new WordContentControlBindingGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        var store = Assert.Single(graph.Stores);
        Assert.False(store.Parsed);
        Assert.Equal(StoreItemIdNormalized, store.ItemId);
        Assert.Null(store.RootNamespaceUri);
        Assert.Equal(0, store.XmlElementCount);
        Assert.Equal(
            WordBindingResolutionStatus.StoreUnreadable,
            Assert.Single(graph.Bindings).Status
        );
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "CCB_CUSTOM_XML_NOT_WELL_FORMED"
        );
        Assert.Empty(graph.Targets);
    }

    [Fact]
    public void ClassifiesControlMetadataAndReportsIdentityAndTypeConflicts()
    {
        var document = $$"""
            <w:document xmlns:w="{{W}}" xmlns:w14="{{W14}}">
              <w:body>
                <w:sdt>
                  <w:sdtPr>
                    <w:alias w:val="Consent"/><w:tag w:val="ConsentTag"/>
                    <w:id w:val="7"/><w:lock w:val="sdtLocked"/>
                    <w:placeholder><w:docPart w:val="PlaceholderId"/></w:placeholder>
                    <w:showingPlcHdr/><w:temporary/><w14:checkbox/>
                  </w:sdtPr>
                  <w:sdtContent><w:p/></w:sdtContent>
                </w:sdt>
                <w:sdt>
                  <w:sdtPr><w:id w:val="7"/><w:text/><w:date/></w:sdtPr>
                  <w:sdtContent><w:p/></w:sdtContent>
                </w:sdt>
                <w:sectPr/>
              </w:body>
            </w:document>
            """;
        using var bytes = BuildPackage(document, includeCustomXml: false);

        var graph = new WordContentControlBindingGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        var checkBox = graph.Controls.Single(control => control.Type == WordContentControlType.CheckBox);
        Assert.Equal("Consent", checkBox.Alias);
        Assert.Equal("ConsentTag", checkBox.Tag);
        Assert.Equal(WordContentControlLock.ControlLocked, checkBox.Lock);
        Assert.Equal("PlaceholderId", checkBox.PlaceholderBuildingBlock);
        Assert.True(checkBox.ShowingPlaceholder);
        Assert.True(checkBox.Temporary);
        Assert.Contains(graph.Controls, control => control.Type == WordContentControlType.Unknown);
        Assert.Equal(
            2,
            graph.Issues.Count(issue => issue.Code == "CCB_DUPLICATE_CONTROL_ID")
        );
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "CCB_MULTIPLE_CONTROL_TYPES"
        );
    }

    [Fact]
    public void EnforcesLimitsCancellationAndSemanticFingerprintOwnership()
    {
        var document = $$"""
            <w:document xmlns:w="{{W}}"><w:body>
              <w:sdt><w:sdtPr><w:id w:val="1"/></w:sdtPr><w:sdtContent><w:p/></w:sdtContent></w:sdt>
              <w:sdt><w:sdtPr><w:id w:val="2"/></w:sdtPr><w:sdtContent><w:p/></w:sdtContent></w:sdt>
              <w:sectPr/>
            </w:body></w:document>
            """;
        using var bytes = BuildPackage(document, includeCustomXml: false);
        var package = new OpcPackageReader().Read(bytes);
        var limited = new WordContentControlBindingGraphBuilder(
            new WordContentControlBindingGraphOptions { MaxControls = 1 }
        );

        Assert.Throws<WordContentControlLimitException>(() => limited.Build(package));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            new WordContentControlBindingGraphBuilder().Build(
                package,
                cancellation.Token
            )
        );

        using var otherBytes = BuildPackage(
            "<w:document xmlns:w='" + W + "'><w:body><w:p/><w:sectPr/></w:body></w:document>",
            includeCustomXml: false
        );
        var otherPackage = new OpcPackageReader().Read(otherBytes);
        var otherSemantic = new WordSemanticProjector().Project(otherPackage);
        Assert.Throws<WordContentControlProjectionException>(() =>
            new WordContentControlBindingGraphBuilder().Build(
                package,
                otherSemantic
            )
        );
    }

    private static string SingleControlDocument(
        string storeItemId = StoreItemId,
        string xpath = "/wt:profile[1]/wt:name[1]",
        string prefixMappings = "xmlns:wt='urn:wordtoolkit:test'"
    ) => $$"""
        <w:document xmlns:w="{{W}}">
          <w:body>
            <w:sdt>
              <w:sdtPr>
                <w:id w:val="42"/><w:alias w:val="Customer"/>
                <w:tag w:val="CustomerName"/><w:lock w:val="sdtContentLocked"/>
                <w:text/>
                <w:dataBinding w:storeItemID="{{storeItemId}}"
                  w:xpath="{{xpath}}" w:prefixMappings="{{prefixMappings}}"/>
              </w:sdtPr>
              <w:sdtContent><w:p><w:r><w:t>redacted-value</w:t></w:r></w:p></w:sdtContent>
            </w:sdt>
            <w:sectPr/>
          </w:body>
        </w:document>
        """;

    private static string RepeatingItem(int id, string text) => $$"""
        <w:sdt>
          <w:sdtPr><w:id w:val="{{id}}"/><w15:repeatingSectionItem/></w:sdtPr>
          <w:sdtContent><w:p><w:r><w:t>{{text}}</w:t></w:r></w:p></w:sdtContent>
        </w:sdt>
        """;

    private static MemoryStream BuildPackage(
        string documentXml,
        string customXml = "<wt:profile xmlns:wt='urn:wordtoolkit:test'><wt:name>Ada</wt:name></wt:profile>",
        bool includeCustomXml = true
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var customOverride = includeCustomXml
                ? "<Override PartName='/customXml/itemProps1.xml' ContentType='application/vnd.openxmlformats-officedocument.customXmlProperties+xml'/>"
                : string.Empty;
            AddEntry(
                archive,
                "[Content_Types].xml",
                $$"""
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  {{customOverride}}
                </Types>
                """
            );
            AddEntry(
                archive,
                "_rels/.rels",
                "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
                    + "<Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='word/document.xml'/>"
                    + "</Relationships>"
            );
            AddEntry(archive, "word/document.xml", documentXml);
            if (includeCustomXml)
            {
                AddEntry(
                    archive,
                    "word/_rels/document.xml.rels",
                    "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
                        + "<Relationship Id='rIdCustom' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml' Target='../customXml/item1.xml'/>"
                        + "</Relationships>"
                );
                AddEntry(archive, "customXml/item1.xml", customXml);
                AddEntry(
                    archive,
                    "customXml/_rels/item1.xml.rels",
                    "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
                        + "<Relationship Id='rIdProps' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps' Target='itemProps1.xml'/>"
                        + "</Relationships>"
                );
                AddEntry(
                    archive,
                    "customXml/itemProps1.xml",
                    $$"""
                    <ds:datastoreItem ds:itemID="{{StoreItemId}}"
                      xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml">
                      <ds:schemaRefs><ds:schemaRef ds:uri="urn:wordtoolkit:test"/></ds:schemaRefs>
                    </ds:datastoreItem>
                    """
                );
            }
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
        throw new DirectoryNotFoundException(
            "Could not locate the WordToolkit repository root."
        );
    }
}
