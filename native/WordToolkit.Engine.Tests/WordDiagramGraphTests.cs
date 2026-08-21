using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordDiagramGraphTests
{
    [Fact]
    public void ReferenceSmartArtFixtureIsMicrosoftOpenXmlValid()
    {
        using var candidate = BuildPackage();
        using var document = WordprocessingDocument.Open(candidate, false);
        var errors = new OpenXmlValidator(FileFormatVersions.Microsoft365)
            .Validate(document)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            string.Join(
                Environment.NewLine,
                errors.Select(item =>
                    $"{item.Id}: {item.Description} {item.Part?.Uri} {item.Path?.XPath}"
                )
            )
        );
    }

    [Fact]
    public void ProjectsSmartArtPartsPointsConnectionsAndDefinitionIdentity()
    {
        using var bytes = BuildPackage();
        var package = new OpcPackageReader().Read(bytes);

        var graph = new WordDiagramGraphBuilder().Build(package);

        Assert.Equal(package.Fingerprint, graph.PackageFingerprint);
        Assert.Equal(5, graph.Parts.Count);
        var diagram = Assert.Single(graph.Diagrams);
        Assert.True(diagram.RequiredPartsResolved);
        Assert.True(diagram.IsPackageReachable);
        Assert.Equal(5, diagram.PartReferences.Count);
        Assert.All(diagram.PartReferences, item => Assert.True(item.IsResolved));
        var persistedDrawing = Assert.Single(
            diagram.PartReferences,
            item => item.Kind == WordDiagramPartKind.PersistedDrawing
        );
        Assert.Equal("rId5", persistedDrawing.RelationshipId);
        Assert.Equal("/word/diagrams/drawing1.xml", persistedDrawing.TargetPartUri);
        Assert.Equal("urn:test:layout", diagram.LayoutUniqueId);
        Assert.Equal("12.0", diagram.LayoutMinimumVersion);
        Assert.Equal("urn:test:style", diagram.QuickStyleUniqueId);
        Assert.Equal("urn:test:colors", diagram.ColorsUniqueId);
        Assert.Equal(1, diagram.PersistedDrawingPartCount);
        Assert.Equal(3, diagram.Points.Count);
        Assert.Equal(2, diagram.Connections.Count);
        Assert.Equal(6, diagram.Points.Sum(item => item.TextCharacterCount));
        var root = diagram.Points.Single(item => item.ModelId == "0");
        Assert.Equal("doc", root.PointType);
        Assert.Equal("urn:test:layout", root.LayoutTypeId);
        Assert.Equal("urn:test:style", root.QuickStyleTypeId);
        Assert.Equal("urn:test:colors", root.ColorStyleTypeId);
        Assert.True(root.HasText);
        Assert.All(diagram.Points, item =>
        {
            Assert.True(item.IsModelIdUnique);
            Assert.True(item.IsStructurallyValid);
        });
        Assert.All(diagram.Connections, item =>
        {
            Assert.True(item.SourceResolved);
            Assert.True(item.DestinationResolved);
            Assert.True(item.IsStructurallyValid);
        });
        Assert.Empty(graph.Issues);
    }

    [Fact]
    public void ProjectsStrictDiagramNamespaceRelationshipsAndRelationshipAttributes()
    {
        using var bytes = BuildPackage(strict: true, includePersistedDrawing: false);
        var graph = new WordDiagramGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        var diagram = Assert.Single(graph.Diagrams);
        Assert.True(diagram.RequiredPartsResolved);
        Assert.Equal(4, diagram.PartReferences.Count);
        Assert.All(diagram.PartReferences, item => Assert.StartsWith(
            "http://purl.oclc.org/ooxml/officeDocument/relationships/diagram",
            item.RelationshipType,
            StringComparison.Ordinal
        ));
        Assert.Equal(0, diagram.PersistedDrawingPartCount);
        Assert.Empty(graph.Issues);
    }

    [Fact]
    public void DetectsUtf16AndUtf32DiagramSourceParts()
    {
        using var utf16Bytes = BuildPackage(
            documentEncoding: new UnicodeEncoding(
                bigEndian: false,
                byteOrderMark: true
            )
        );
        using var utf32Bytes = BuildPackage(
            documentEncoding: new UTF32Encoding(
                bigEndian: true,
                byteOrderMark: true
            )
        );

        var utf16 = new WordDiagramGraphBuilder().Build(
            new OpcPackageReader().Read(utf16Bytes)
        );
        var utf32 = new WordDiagramGraphBuilder().Build(
            new OpcPackageReader().Read(utf32Bytes)
        );

        Assert.Single(utf16.Diagrams);
        Assert.Single(utf32.Diagrams);
    }

    [Fact]
    public void MissingRequiredAndWrongTypedRelationshipsFailClosed()
    {
        var document = DocumentXml(strict: false)
            .Replace(" r:lo=\"rId2\"", string.Empty, StringComparison.Ordinal);
        var relationships = DocumentRelationships(strict: false)
            .Replace(
                "/diagramData\" Target=\"diagrams/data1.xml\"",
                "/diagramDataSpoof\" Target=\"diagrams/data1.xml\"",
                StringComparison.Ordinal
            );
        using var bytes = BuildPackage(
            documentXml: document,
            documentRelationships: relationships
        );

        var graph = new WordDiagramGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        var diagram = Assert.Single(graph.Diagrams);
        Assert.False(diagram.RequiredPartsResolved);
        Assert.Empty(diagram.Points);
        Assert.Contains(
            graph.Issues,
            item => item.Code == "DGM_REQUIRED_RELATIONSHIP_MISSING"
        );
        Assert.Contains(
            graph.Issues,
            item => item.Code == "DGM_RELATIONSHIP_UNRESOLVED"
        );
        Assert.DoesNotContain(
            diagram.PartReferences,
            item => item.RelationshipType.EndsWith(
                "diagramDataSpoof",
                StringComparison.Ordinal
            ) && item.IsResolved
        );
    }

    [Fact]
    public void DuplicatePointIdsAndMissingConnectionEndpointsAreDiagnosed()
    {
        var data = DataXml(strict: false)
            .Replace(
                "modelId=\"2\"",
                "modelId=\"1\"",
                StringComparison.Ordinal
            )
            .Replace(
                "destId=\"2\"",
                "destId=\"missing\"",
                StringComparison.Ordinal
            );
        using var bytes = BuildPackage(dataXml: data);

        var graph = new WordDiagramGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        var diagram = Assert.Single(graph.Diagrams);
        Assert.Equal(2, diagram.Points.Count(item => !item.IsStructurallyValid));
        Assert.Contains(
            graph.Issues,
            item => item.Code == "DGM_POINT_MODEL_ID_INVALID"
        );
        Assert.Contains(
            graph.Issues,
            item => item.Code == "DGM_CONNECTION_ENDPOINT_UNRESOLVED"
        );
        Assert.Contains(
            diagram.Connections,
            item => !item.DestinationResolved && !item.IsStructurallyValid
        );
    }

    [Fact]
    public void MalformedPointPropertiesAndConnectionOrdersAreDiagnosed()
    {
        var data = DataXml(strict: false)
            .Replace(
                "<dgm:prSet loTypeId=\"urn:test:layout\" qsTypeId=\"urn:test:style\" csTypeId=\"urn:test:colors\"/>",
                "<dgm:prSet phldr=\"maybe\"/><dgm:prSet/>",
                StringComparison.Ordinal
            )
            .Replace("srcOrd=\"0\"", "srcOrd=\"-1\"", StringComparison.Ordinal);
        using var bytes = BuildPackage(dataXml: data);

        var graph = new WordDiagramGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        var diagram = Assert.Single(graph.Diagrams);
        Assert.Contains(diagram.Points, item => !item.IsStructurallyValid);
        Assert.Contains(diagram.Connections, item => !item.IsStructurallyValid);
        Assert.Contains(
            graph.Issues,
            item => item.Code == "DGM_POINT_PROPERTY_SET_CARDINALITY"
        );
        Assert.Contains(
            graph.Issues,
            item => item.Code == "DGM_POINT_PLACEHOLDER_INVALID"
        );
        Assert.Contains(
            graph.Issues,
            item => item.Code == "DGM_CONNECTION_ORDER_INVALID"
        );
    }

    [Fact]
    public void MissingDataModelContainersAndPersistedDrawingTargetsAreDiagnosed()
    {
        var data = DataXml(strict: false)
            .Replace("<dgm:bg/>", string.Empty, StringComparison.Ordinal)
            .Replace("<dgm:whole/>", string.Empty, StringComparison.Ordinal)
            .Replace("<dgm:cxnLst>", "<dgm:cxnLst/><dgm:cxnLst>", StringComparison.Ordinal);
        var relationships = DocumentRelationships(strict: false)
            .Replace(
                "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing",
                "http://schemas.microsoft.com/office/2007/relationships/diagramDrawingSpoof",
                StringComparison.Ordinal
            );
        using var bytes = BuildPackage(
            dataXml: data,
            documentRelationships: relationships
        );

        var graph = new WordDiagramGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        var diagram = Assert.Single(graph.Diagrams);
        Assert.Equal(0, diagram.PersistedDrawingPartCount);
        Assert.Contains(
            graph.Issues,
            item => item.Code == "DGM_CONNECTION_LIST_CARDINALITY"
        );
        Assert.Contains(
            graph.Issues,
            item => item.Code == "DGM_BACKGROUND_CARDINALITY"
        );
        Assert.Contains(
            graph.Issues,
            item => item.Code == "DGM_WHOLE_CARDINALITY"
        );
        Assert.Contains(
            graph.Issues,
            item => item.Code == "DGM_RELATIONSHIP_UNRESOLVED"
                && item.RelationshipId == "rId5"
        );
        Assert.Contains(
            graph.Issues,
            item => item.Code == "DGM_PART_UNREFERENCED"
                && item.PartUri == "/word/diagrams/drawing1.xml"
        );
    }

    [Fact]
    public void OptionalStyleAndColorPartsMayBeAbsentWithoutInventingErrors()
    {
        var document = DocumentXml(strict: false)
            .Replace(" r:qs=\"rId3\"", string.Empty, StringComparison.Ordinal)
            .Replace(" r:cs=\"rId4\"", string.Empty, StringComparison.Ordinal);
        var relationships = DocumentRelationships(strict: false)
            .Replace(
                "  <Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramQuickStyle\" Target=\"diagrams/quickStyle1.xml\"/>\n",
                string.Empty,
                StringComparison.Ordinal
            )
            .Replace(
                "  <Relationship Id=\"rId4\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramColors\" Target=\"diagrams/colors1.xml\"/>\n",
                string.Empty,
                StringComparison.Ordinal
            );
        using var bytes = BuildPackage(
            documentXml: document,
            documentRelationships: relationships,
            includeOptionalParts: false
        );

        var graph = new WordDiagramGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        var diagram = Assert.Single(graph.Diagrams);
        Assert.True(diagram.RequiredPartsResolved);
        Assert.Equal(3, diagram.PartReferences.Count);
        Assert.Null(diagram.QuickStyleUniqueId);
        Assert.Null(diagram.ColorsUniqueId);
        Assert.Empty(graph.Issues);
    }

    [Fact]
    public void UnsafeDiagramXmlAndIdentifierLimitsAreRefused()
    {
        var unsafeData =
            """
            <!DOCTYPE dataModel [<!ENTITY x "unsafe">]>
            <dgm:dataModel xmlns:dgm="http://schemas.openxmlformats.org/drawingml/2006/diagram"><dgm:ptLst><dgm:pt modelId="&x;"/></dgm:ptLst></dgm:dataModel>
            """;
        using var unsafeBytes = BuildPackage(dataXml: unsafeData);
        Assert.Throws<WordDiagramProjectionException>(() =>
            new WordDiagramGraphBuilder().Build(
                new OpcPackageReader().Read(unsafeBytes)
            )
        );

        using var longIdBytes = BuildPackage(dataXml: DataXml(strict: false).Replace(
            "modelId=\"0\"",
            $"modelId=\"{new string('x', 33)}\"",
            StringComparison.Ordinal
        ));
        Assert.Throws<WordDiagramLimitException>(() =>
            new WordDiagramGraphBuilder(new WordDiagramGraphOptions
            {
                MaxIdentifierCharacters = 32,
            }).Build(new OpcPackageReader().Read(longIdBytes))
        );
    }

    [Fact]
    public void OperationResourceBudgetIsExactAndChargedToDiagramStage()
    {
        using var baselineBytes = BuildPackage();
        var baselinePackage = new OpcPackageReader().Read(baselineBytes);
        var baselineLease = new WordOperationResourceLease();
        _ = new WordDiagramGraphBuilder(null, baselineLease).Build(baselinePackage);
        var usage = baselineLease.Snapshot();
        Assert.Contains(
            usage.Stages,
            item => item.Stage == WordOperationResourceStage.Diagrams
        );

        using var exactBytes = BuildPackage();
        var exactLease = new WordOperationResourceLease(usage.AccountedBytes);
        _ = new WordDiagramGraphBuilder(null, exactLease).Build(
            new OpcPackageReader().Read(exactBytes)
        );
        Assert.Equal(usage.AccountedBytes, exactLease.AccountedBytes);

        using var rejectedBytes = BuildPackage();
        var rejectedLease = new WordOperationResourceLease(usage.AccountedBytes - 1);
        var exception = Assert.Throws<WordOperationResourceLimitException>(() =>
            new WordDiagramGraphBuilder(null, rejectedLease).Build(
                new OpcPackageReader().Read(rejectedBytes)
            )
        );
        Assert.Equal(WordOperationResourceStage.Diagrams, exception.Stage);
    }

    internal static MemoryStream BuildPackage(
        bool strict = false,
        string? documentXml = null,
        string? documentRelationships = null,
        string? dataXml = null,
        bool includeOptionalParts = true,
        bool includePersistedDrawing = true,
        Encoding? documentEncoding = null
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                "[Content_Types].xml",
                ContentTypes(includeOptionalParts, includePersistedDrawing)
            );
            WriteEntry(
                archive,
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """
            );
            WriteEntry(
                archive,
                "word/document.xml",
                documentXml ?? DocumentXml(strict),
                documentEncoding
            );
            WriteEntry(
                archive,
                "word/_rels/document.xml.rels",
                documentRelationships
                    ?? DocumentRelationships(strict, includePersistedDrawing)
            );
            WriteEntry(
                archive,
                "word/diagrams/data1.xml",
                dataXml ?? DataXml(strict, includePersistedDrawing)
            );
            WriteEntry(
                archive,
                "word/diagrams/layout1.xml",
                DefinitionXml(strict, "layoutDef", "urn:test:layout")
            );
            if (includeOptionalParts)
            {
                WriteEntry(
                    archive,
                    "word/diagrams/quickStyle1.xml",
                    DefinitionXml(strict, "styleDef", "urn:test:style")
                );
                WriteEntry(
                    archive,
                    "word/diagrams/colors1.xml",
                    DefinitionXml(strict, "colorsDef", "urn:test:colors")
                );
            }
            if (includePersistedDrawing)
            {
                WriteEntry(
                    archive,
                    "word/diagrams/drawing1.xml",
                    """
                    <dsp:drawing xmlns:dsp="http://schemas.microsoft.com/office/drawing/2008/diagram"
                                 xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                      <dsp:spTree>
                        <dsp:nvGrpSpPr>
                          <dsp:cNvPr id="1" name="SmartArt 1"/>
                          <dsp:cNvGrpSpPr/>
                        </dsp:nvGrpSpPr>
                        <dsp:grpSpPr/>
                      </dsp:spTree>
                    </dsp:drawing>
                    """
                );
            }
        }
        stream.Position = 0;
        return stream;
    }

    internal static string DocumentXml(bool strict)
    {
        var dgm = strict
            ? "http://purl.oclc.org/ooxml/drawingml/diagram"
            : "http://schemas.openxmlformats.org/drawingml/2006/diagram";
        var relationships = strict
            ? "http://purl.oclc.org/ooxml/officeDocument/relationships"
            : "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        return $$"""
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                        xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                        xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                        xmlns:dgm="{{dgm}}"
                        xmlns:r="{{relationships}}">
              <w:body><w:p><w:r><w:drawing><wp:inline>
                <wp:extent cx="914400" cy="914400"/>
                <wp:docPr id="1" name="SmartArt 1"/>
                <wp:cNvGraphicFramePr/>
                <a:graphic><a:graphicData uri="{{dgm}}">
                  <dgm:relIds r:dm="rId1" r:lo="rId2" r:qs="rId3" r:cs="rId4"/>
                </a:graphicData></a:graphic>
              </wp:inline></w:drawing></w:r></w:p></w:body>
            </w:document>
            """;
    }

    internal static string DocumentRelationships(
        bool strict,
        bool includePersistedDrawing = true
    )
    {
        var root = strict
            ? "http://purl.oclc.org/ooxml/officeDocument/relationships/diagram"
            : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagram";
        return $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="{{root}}Data" Target="diagrams/data1.xml"/>
              <Relationship Id="rId2" Type="{{root}}Layout" Target="diagrams/layout1.xml"/>
              <Relationship Id="rId3" Type="{{root}}QuickStyle" Target="diagrams/quickStyle1.xml"/>
              <Relationship Id="rId4" Type="{{root}}Colors" Target="diagrams/colors1.xml"/>
            {{(includePersistedDrawing ? "  <Relationship Id=\"rId5\" Type=\"http://schemas.microsoft.com/office/2007/relationships/diagramDrawing\" Target=\"diagrams/drawing1.xml\"/>" : string.Empty)}}
            </Relationships>
            """;
    }

    internal static string DataXml(
        bool strict,
        bool includePersistedDrawing = true
    )
    {
        var dgm = strict
            ? "http://purl.oclc.org/ooxml/drawingml/diagram"
            : "http://schemas.openxmlformats.org/drawingml/2006/diagram";
        var drawing = strict
            ? "http://purl.oclc.org/ooxml/drawingml/main"
            : "http://schemas.openxmlformats.org/drawingml/2006/main";
        return $$"""
            <dgm:dataModel xmlns:dgm="{{dgm}}" xmlns:a="{{drawing}}"
                           xmlns:dsp="http://schemas.microsoft.com/office/drawing/2008/diagram">
              <dgm:ptLst>
                <dgm:pt modelId="0" type="doc"><dgm:prSet loTypeId="urn:test:layout" qsTypeId="urn:test:style" csTypeId="urn:test:colors"/><dgm:t><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Root</a:t></a:r><a:endParaRPr lang="en-US"/></a:p></dgm:t></dgm:pt>
                <dgm:pt modelId="1"><dgm:t><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>A</a:t></a:r><a:endParaRPr lang="en-US"/></a:p></dgm:t></dgm:pt>
                <dgm:pt modelId="2"><dgm:t><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>B</a:t></a:r><a:endParaRPr lang="en-US"/></a:p></dgm:t></dgm:pt>
              </dgm:ptLst>
              <dgm:cxnLst>
                <dgm:cxn modelId="3" srcId="0" destId="1" srcOrd="0" destOrd="0"/>
                <dgm:cxn modelId="4" srcId="0" destId="2" srcOrd="1" destOrd="0"/>
              </dgm:cxnLst>
              <dgm:bg/><dgm:whole/>
              {{(includePersistedDrawing ? "<dgm:extLst><a:ext uri=\"http://schemas.microsoft.com/office/drawing/2008/diagram\"><dsp:dataModelExt relId=\"rId5\" minVer=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\"/></a:ext></dgm:extLst>" : string.Empty)}}
            </dgm:dataModel>
            """;
    }

    private static string DefinitionXml(
        bool strict,
        string rootName,
        string uniqueId
    )
    {
        var dgm = strict
            ? "http://purl.oclc.org/ooxml/drawingml/diagram"
            : "http://schemas.openxmlformats.org/drawingml/2006/diagram";
        var child = rootName switch
        {
            "layoutDef" => "<dgm:layoutNode name=\"root\"/>",
            "styleDef" => "<dgm:styleLbl name=\"node\"/>",
            _ => string.Empty,
        };
        return $"<dgm:{rootName} xmlns:dgm=\"{dgm}\" uniqueId=\"{uniqueId}\" minVer=\"12.0\">{child}</dgm:{rootName}>";
    }

    private static string ContentTypes(
        bool includeOptionalParts,
        bool includePersistedDrawing
    ) =>
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
          <Override PartName="/word/diagrams/data1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml"/>
          <Override PartName="/word/diagrams/layout1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml"/>
        """
        + (
            includeOptionalParts
                ? """
                  <Override PartName="/word/diagrams/quickStyle1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml"/>
                  <Override PartName="/word/diagrams/colors1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml"/>
                  """
                : string.Empty
        )
        + (
            includePersistedDrawing
                ? """
                  <Override PartName="/word/diagrams/drawing1.xml" ContentType="application/vnd.ms-office.drawingml.diagramDrawing+xml"/>
                  """
                : string.Empty
        )
        + "</Types>";

    private static void WriteEntry(
        ZipArchive archive,
        string name,
        string content,
        Encoding? encoding = null
    )
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        using var writer = new StreamWriter(
            stream,
            encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
        writer.Write(content);
    }
}
