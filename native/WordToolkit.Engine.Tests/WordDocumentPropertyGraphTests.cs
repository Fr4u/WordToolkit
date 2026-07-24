using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordDocumentPropertyGraphTests
{
    [Fact]
    public void BuildsTypedCoreExtendedAndCustomPropertiesWithFieldAliases()
    {
        var package = ReadPackage(CreateCompletePackage());
        var graph = new WordDocumentPropertyGraphBuilder().Build(package);

        Assert.Equal(package.Fingerprint, graph.PackageFingerprint);
        Assert.Equal(3, graph.Parts.Count);
        Assert.All(graph.Parts, part => Assert.True(part.IsPackageReachable));
        Assert.Equal(10, graph.Properties.Count);
        Assert.Empty(graph.Issues);

        Assert.True(graph.TryResolveFieldProperty("Title", out var title));
        Assert.Equal(WordDocumentPropertyFamily.Core, title!.Family);
        Assert.Equal("Plan Alpha", title.Value);
        Assert.Equal(WordDocumentPropertyValueKind.Text, title.ValueKind);

        Assert.True(graph.TryResolveFieldProperty("Author", out var author));
        Assert.Equal("Ada", author!.Value);
        Assert.True(graph.TryResolveFieldProperty("Company", out var company));
        Assert.Equal("Acme", company!.Value);
        Assert.True(graph.TryResolveFieldProperty("Project Code", out var project));
        Assert.Equal(2, project!.PropertyId);
        Assert.Equal("ZX-9", project.Value);
        Assert.True(project.IsStructurallyValid);

        var vector = Assert.Single(
            graph.Properties,
            item => item.CanonicalName == "OpaqueVector"
        );
        Assert.Equal(WordDocumentPropertyValueKind.Vector, vector.ValueKind);
        Assert.False(vector.HasScalarValue);
        Assert.Null(vector.Value);
        Assert.False(graph.TryResolveFieldProperty("OpaqueVector", out _));

        var headingPairs = Assert.Single(
            graph.Properties,
            item => item.CanonicalName == "HeadingPairs"
        );
        Assert.Equal(WordDocumentPropertyValueKind.Vector, headingPairs.ValueKind);
        Assert.False(headingPairs.HasScalarValue);
    }

    [Fact]
    public void DuplicateNamesAndPropertyIdsFailClosedForFieldResolution()
    {
        var custom =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <op:Properties xmlns:op="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties"
                           xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
              <op:property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="2" name="SecretName"><vt:lpwstr>one</vt:lpwstr></op:property>
              <op:property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="2" name="secretname"><vt:lpwstr>two</vt:lpwstr></op:property>
              <op:property fmtid="{00000000-0000-0000-0000-000000000000}" pid="1" name="Broken"><vt:lpwstr>three</vt:lpwstr></op:property>
              <op:property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="5" name="Multiple"><vt:lpwstr>a</vt:lpwstr><vt:i4>1</vt:i4></op:property>
            </op:Properties>
            """;
        var graph = new WordDocumentPropertyGraphBuilder().Build(
            ReadPackage(CreatePackage(custom: custom))
        );

        Assert.False(graph.TryResolveFieldProperty("SecretName", out _));
        Assert.False(graph.TryResolveFieldProperty("Broken", out _));
        Assert.False(graph.TryResolveFieldProperty("Multiple", out _));
        Assert.Contains(graph.Issues, item => item.Code == "WDP032");
        Assert.Contains(graph.Issues, item => item.Code == "WDP033");
        Assert.Contains(graph.Issues, item => item.Code == "WDP034");
        Assert.Equal(2, graph.Issues.Count(item => item.Code == "WDP040"));
        Assert.Equal(2, graph.Issues.Count(item => item.Code == "WDP042"));
    }

    [Fact]
    public void RelationshipSuffixSpoofDoesNotMakePropertyPartReachable()
    {
        var packageBytes = CreatePackage(
            rootRelationships:
                """
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                <Relationship Id="rId2" Type="https://attacker.invalid/custom-properties" Target="docProps/custom.xml"/>
                <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties/evil" Target="docProps/custom.xml"/>
                """
        );
        var graph = new WordDocumentPropertyGraphBuilder().Build(
            ReadPackage(packageBytes)
        );

        var part = Assert.Single(graph.Parts);
        Assert.False(part.IsPackageReachable);
        Assert.Contains(graph.Issues, item => item.Code == "WDP003");
    }

    [Fact]
    public void SupportsStrictCustomPropertyNamespacesAndRelationships()
    {
        var custom =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <op:Properties xmlns:op="http://purl.oclc.org/ooxml/officeDocument/customProperties"
                           xmlns:vt="http://purl.oclc.org/ooxml/officeDocument/docPropsVTypes">
              <op:property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="2" name="StrictValue"><vt:i4>42</vt:i4></op:property>
            </op:Properties>
            """;
        var package = ReadPackage(
            CreatePackage(
                custom: custom,
                rootRelationships:
                    """
                    <Relationship Id="rId1" Type="http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument" Target="word/document.xml"/>
                    <Relationship Id="rId2" Type="http://purl.oclc.org/ooxml/officeDocument/relationships/custom-properties" Target="docProps/custom.xml"/>
                    """
            )
        );
        var graph = new WordDocumentPropertyGraphBuilder().Build(package);

        Assert.Empty(graph.Issues);
        Assert.True(graph.TryResolveFieldProperty("StrictValue", out var property));
        Assert.Equal(WordDocumentPropertyValueKind.Integer, property!.ValueKind);
        Assert.Equal("42", property.Value);
    }

    [Fact]
    public void InvalidScalarLexemeCannotEnterTheFieldIndex()
    {
        var custom =
            """
            <op:Properties xmlns:op="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties"
                           xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
              <op:property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="2" name="BrokenInteger"><vt:i4>not-an-integer</vt:i4></op:property>
            </op:Properties>
            """;
        var graph = new WordDocumentPropertyGraphBuilder().Build(
            ReadPackage(CreatePackage(custom: custom))
        );

        var property = Assert.Single(graph.Properties);
        Assert.False(property.IsStructurallyValid);
        Assert.False(graph.TryResolveFieldProperty("BrokenInteger", out _));
        Assert.Contains(graph.Issues, item => item.Code == "WDP036");
    }

    [Fact]
    public void InvalidCoreAndExtendedScalarLexemesAreDiagnosedAndFailClosed()
    {
        var core =
            """
            <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                               xmlns:dcterms="http://purl.org/dc/terms/"
                               xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <dcterms:created xsi:type="dcterms:W3CDTF">not-a-date</dcterms:created>
            </cp:coreProperties>
            """;
        var extended =
            """
            <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">
              <Pages>not-an-integer</Pages>
            </Properties>
            """;
        var graph = new WordDocumentPropertyGraphBuilder().Build(
            ReadPackage(CreatePackage(core: core, extended: extended))
        );

        var created = Assert.Single(
            graph.Properties,
            item => item.Family == WordDocumentPropertyFamily.Core
        );
        var pages = Assert.Single(
            graph.Properties,
            item => item.Family == WordDocumentPropertyFamily.Extended
        );
        Assert.False(created.IsStructurallyValid);
        Assert.False(pages.IsStructurallyValid);
        Assert.False(graph.TryResolveFieldProperty("created", out _));
        Assert.False(graph.TryResolveFieldProperty("Pages", out _));
        Assert.Contains(graph.Issues, item => item.Code == "WDP012");
        Assert.Contains(graph.Issues, item => item.Code == "WDP022");
        Assert.DoesNotContain(graph.Issues, item => item.Code == "WDP013");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" xsi:type=\"dcterms:dateTime\"")]
    [InlineData(" xsi:type=\"wrong:W3CDTF\" xmlns:wrong=\"urn:not-dcterms\"")]
    public void MissingOrWrongCoreDateTypeAnnotationFailsClosed(string annotation)
    {
        var core =
            $$"""
            <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                               xmlns:dcterms="http://purl.org/dc/terms/"
                               xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <dcterms:created{{annotation}}>2026-07-24T00:00:00Z</dcterms:created>
            </cp:coreProperties>
            """;
        var graph = new WordDocumentPropertyGraphBuilder().Build(
            ReadPackage(CreatePackage(core: core))
        );

        var created = Assert.Single(
            graph.Properties,
            item => item.Family == WordDocumentPropertyFamily.Core
        );
        Assert.False(created.IsStructurallyValid);
        Assert.False(graph.TryResolveFieldProperty("created", out _));
        Assert.Contains(graph.Issues, item => item.Code == "WDP013");
        Assert.DoesNotContain(graph.Issues, item => item.Code == "WDP012");
    }

    [Fact]
    public void CoreDateTypeAnnotationResolvesByNamespaceNotPrefixSpelling()
    {
        var core =
            """
            <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                               xmlns="http://purl.org/dc/terms/"
                               xmlns:terms="http://purl.org/dc/terms/"
                               xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <terms:created xsi:type="terms:W3CDTF">2026-07-24T00:00:00Z</terms:created>
              <modified xsi:type="W3CDTF">2026-07-24T00:01:00Z</modified>
            </cp:coreProperties>
            """;
        var graph = new WordDocumentPropertyGraphBuilder().Build(
            ReadPackage(CreatePackage(core: core))
        );

        var created = Assert.Single(
            graph.Properties,
            item => item.CanonicalName == "created"
        );
        var modified = Assert.Single(
            graph.Properties,
            item => item.CanonicalName == "modified"
        );
        Assert.True(created.IsStructurallyValid);
        Assert.True(modified.IsStructurallyValid);
        Assert.True(graph.TryResolveFieldProperty("CreateTime", out var resolved));
        Assert.Equal(created.Id, resolved?.Id);
        Assert.True(graph.TryResolveFieldProperty("SaveTime", out resolved));
        Assert.Equal(modified.Id, resolved?.Id);
        Assert.DoesNotContain(graph.Issues, item => item.Code is "WDP012" or "WDP013");
    }

    [Fact]
    public void ClipboardDataIsBinaryAndLocalizedDateTextFailsClosed()
    {
        var custom =
            """
            <op:Properties xmlns:op="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties"
                           xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
              <op:property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="2" name="Clipboard"><vt:cf>opaque</vt:cf></op:property>
              <op:property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="3" name="LocalizedDate"><vt:filetime>07/24/2026 12:00:00</vt:filetime></op:property>
            </op:Properties>
            """;
        var graph = new WordDocumentPropertyGraphBuilder().Build(
            ReadPackage(CreatePackage(custom: custom))
        );

        var clipboard = Assert.Single(
            graph.Properties,
            item => item.CanonicalName == "Clipboard"
        );
        var localizedDate = Assert.Single(
            graph.Properties,
            item => item.CanonicalName == "LocalizedDate"
        );
        Assert.Equal(WordDocumentPropertyValueKind.Binary, clipboard.ValueKind);
        Assert.False(clipboard.HasScalarValue);
        Assert.Null(clipboard.Value);
        Assert.Equal(WordDocumentPropertyValueKind.DateTime, localizedDate.ValueKind);
        Assert.False(localizedDate.IsStructurallyValid);
        Assert.False(graph.TryResolveFieldProperty("LocalizedDate", out _));
        Assert.Contains(graph.Issues, item => item.Code == "WDP036");
    }

    [Fact]
    public void WrongContentTypeAndDuplicateRelationshipsRemainExplicit()
    {
        var wrongType = new WordDocumentPropertyGraphBuilder().Build(
            ReadPackage(CreatePackage(customContentType: "application/xml"))
        );
        Assert.Empty(wrongType.Parts);
        Assert.Contains(wrongType.Issues, item => item.Code == "WDP005");

        var duplicate = new WordDocumentPropertyGraphBuilder().Build(
            ReadPackage(
                CreatePackage(
                    rootRelationships:
                        """
                        <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                        <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties" Target="docProps/custom.xml"/>
                        <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties" Target="docProps/custom.xml"/>
                        """
                )
            )
        );
        Assert.Single(duplicate.Parts);
        Assert.Contains(duplicate.Issues, item => item.Code == "WDP006");
    }

    [Fact]
    public void UnsafeXmlAndGraphLimitsFailDeterministically()
    {
        var unsafeCustom =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE Properties [<!ENTITY xxe SYSTEM "file:///secret">]>
            <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties"
                        xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
              <property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="2" name="Unsafe"><vt:lpwstr>&xxe;</vt:lpwstr></property>
            </Properties>
            """;
        Assert.Throws<WordDocumentPropertyProjectionException>(() =>
            new WordDocumentPropertyGraphBuilder().Build(
                ReadPackage(CreatePackage(custom: unsafeCustom))
            )
        );

        var packageBytes = CreateCompletePackage();
        var firstLease = new WordOperationResourceLease();
        var firstPackage = new OpcPackageReader(null, firstLease).Read(
            new MemoryStream(packageBytes, writable: false)
        );
        _ = new WordDocumentPropertyGraphBuilder(null, firstLease).Build(firstPackage);
        var used = firstLease.AccountedBytes;
        Assert.True(used > 1);

        var limitedLease = new WordOperationResourceLease(used - 1);
        Assert.Throws<WordOperationResourceLimitException>(() =>
        {
            var limitedPackage = new OpcPackageReader(null, limitedLease).Read(
                new MemoryStream(packageBytes, writable: false)
            );
            _ = new WordDocumentPropertyGraphBuilder(null, limitedLease).Build(
                limitedPackage
            );
        });

        var exactLease = new WordOperationResourceLease(used);
        var exactPackage = new OpcPackageReader(null, exactLease).Read(
            new MemoryStream(packageBytes, writable: false)
        );
        var exactGraph = new WordDocumentPropertyGraphBuilder(null, exactLease).Build(
            exactPackage
        );
        Assert.Equal(10, exactGraph.Properties.Count);
        Assert.Equal(used, exactLease.AccountedBytes);
    }

    [Fact]
    public void DependencyGraphResolvesDocPropertyAndPersistentDocVariableFields()
    {
        var document =
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
              <w:p><w:fldSimple w:instr="DOCPROPERTY Value"><w:r><w:t>text</w:t></w:r></w:fldSimple></w:p>
              <w:p><w:fldSimple w:instr="DOCVARIABLE CustomerId"><w:r><w:t>text</w:t></w:r></w:fldSimple></w:p>
              <w:p><w:fldSimple w:instr="DOCPROPERTY Missing"><w:r><w:t>text</w:t></w:r></w:fldSimple></w:p>
              <w:p><w:fldSimple w:instr="SET CustomerId next"><w:r><w:t>text</w:t></w:r></w:fldSimple></w:p>
            </w:body></w:document>
            """;
        var settings =
            """
            <w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:docVars><w:docVar w:name="CustomerId" w:val="sensitive-value"/></w:docVars>
            </w:settings>
            """;
        var package = ReadPackage(
            CreatePackage(document: document, settings: settings)
        );
        var semantic = new WordSemanticProjector().Project(package);
        var references = new WordReferenceGraphBuilder().Build(package, semantic);
        var graph = new WordDependencyGraphBuilder().Build(package, semantic);

        Assert.True(graph.Coverage.DocumentPropertiesAndVariables);
        var propertyNode = Assert.Single(
            graph.Nodes,
            item => item.Kind == WordDependencyNodeKind.DocumentProperty
        );
        var variableNode = Assert.Single(
            graph.Nodes,
            item => item.Kind == WordDependencyNodeKind.DocumentVariable
        );
        Assert.Contains(
            graph.Edges,
            item => item.Kind == WordDependencyEdgeKind.DefinesDocumentProperty
                && item.TargetNodeId == propertyNode.Id
        );
        Assert.Contains(
            graph.Edges,
            item => item.Kind == WordDependencyEdgeKind.DefinesDocumentVariable
                && item.TargetNodeId == variableNode.Id
        );

        var propertyField = Assert.Single(
            references.Fields,
            item => item.FieldType == "DOCPROPERTY" && item.Instruction.Contains("Value")
        );
        var variableField = Assert.Single(
            references.Fields,
            item => item.FieldType == "DOCVARIABLE"
        );
        var setField = Assert.Single(
            references.Fields,
            item => item.FieldType == "SET"
        );
        var fieldNodes = graph.Nodes
            .Where(item => item.Kind == WordDependencyNodeKind.Field)
            .ToDictionary(item => item.Key, item => item.Id, StringComparer.Ordinal);
        Assert.Contains(
            graph.Edges,
            item => item.Kind == WordDependencyEdgeKind.FieldReference
                && item.SourceNodeId == fieldNodes[propertyField.Id]
                && item.TargetNodeId == propertyNode.Id
                && item.IsResolved
        );
        Assert.Contains(
            graph.Edges,
            item => item.Kind == WordDependencyEdgeKind.FieldReference
                && item.SourceNodeId == fieldNodes[variableField.Id]
                && item.TargetNodeId == variableNode.Id
                && item.IsResolved
        );
        Assert.Contains(
            graph.Edges,
            item => item.Kind == WordDependencyEdgeKind.FieldReference
                && item.SourceNodeId == fieldNodes[setField.Id]
                && item.TargetNodeId != variableNode.Id
                && !item.IsResolved
        );
        Assert.Contains(graph.Issues, item => item.Code == "WDG030");
    }

    private static byte[] CreateCompletePackage() => CreatePackage(
        core:
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                               xmlns:dc="http://purl.org/dc/elements/1.1/"
                               xmlns:dcterms="http://purl.org/dc/terms/"
                               xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <dc:title>Plan Alpha</dc:title>
              <dc:creator>Ada</dc:creator>
              <dcterms:created xsi:type="dcterms:W3CDTF">2026-07-24T00:00:00Z</dcterms:created>
            </cp:coreProperties>
            """,
        extended:
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"
                        xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
              <Company>Acme</Company>
              <Pages>12</Pages>
              <HeadingPairs><vt:vector size="0" baseType="variant"/></HeadingPairs>
            </Properties>
            """,
        custom:
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <op:Properties xmlns:op="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties"
                           xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
              <op:property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="2" name="Project Code"><vt:lpwstr>ZX-9</vt:lpwstr></op:property>
              <op:property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="3" name="Approved"><vt:bool>true</vt:bool></op:property>
              <op:property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="4" name="Budget"><vt:r8>19.5</vt:r8></op:property>
              <op:property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="5" name="OpaqueVector"><vt:vector size="1" baseType="lpwstr"><vt:lpwstr>x</vt:lpwstr></vt:vector></op:property>
            </op:Properties>
            """
    );

    private static byte[] CreatePackage(
        string? core = null,
        string? extended = null,
        string? custom = null,
        string? rootRelationships = null,
        string? document = null,
        string? settings = null,
        string customContentType =
            "application/vnd.openxmlformats-officedocument.custom-properties+xml"
    )
    {
        custom ??=
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <op:Properties xmlns:op="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties"
                           xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
              <op:property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="2" name="Value"><vt:lpwstr>text</vt:lpwstr></op:property>
            </op:Properties>
            """;
        rootRelationships ??=
            """
            <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties" Target="docProps/custom.xml"/>
            """
            + (core is null
                ? string.Empty
                : "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>")
            + (extended is null
                ? string.Empty
                : "<Relationship Id=\"rId4\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/>");

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var overrides = new StringBuilder(
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>"
            );
            if (core is not null)
            {
                overrides.Append(
                    "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>"
                );
            }
            if (extended is not null)
            {
                overrides.Append(
                    "<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>"
                );
            }
            if (custom is not null)
            {
                overrides.Append(
                    $"<Override PartName=\"/docProps/custom.xml\" ContentType=\"{customContentType}\"/>"
                );
            }
            if (settings is not null)
            {
                overrides.Append(
                    "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>"
                );
            }
            AddEntry(
                archive,
                "[Content_Types].xml",
                $"<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>{overrides}</Types>"
            );
            AddEntry(
                archive,
                "_rels/.rels",
                $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{rootRelationships}</Relationships>"
            );
            AddEntry(
                archive,
                "word/document.xml",
                document
                    ?? "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p/></w:body></w:document>"
            );
            if (settings is not null)
            {
                AddEntry(
                    archive,
                    "word/_rels/document.xml.rels",
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rSettings\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/></Relationships>"
                );
                AddEntry(archive, "word/settings.xml", settings);
            }
            if (core is not null)
            {
                AddEntry(archive, "docProps/core.xml", core);
            }
            if (extended is not null)
            {
                AddEntry(archive, "docProps/app.xml", extended);
            }
            if (custom is not null)
            {
                AddEntry(archive, "docProps/custom.xml", custom);
            }
        }
        return stream.ToArray();
    }

    private static OpcPackageSnapshot ReadPackage(byte[] bytes) =>
        new OpcPackageReader().Read(new MemoryStream(bytes, writable: false));

    private static void AddEntry(ZipArchive archive, string name, string text)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var writer = new StreamWriter(
            entry.Open(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
        writer.Write(text);
    }
}
