using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Resources;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class DiagramPackageInspectionTests
{
    private const string RootModelId = "SECRET_ROOT_MODEL";
    private const string ChildModelId = "SECRET_CHILD_MODEL";
    private const string PrivateText = "PRIVATE SMARTART TEXT";

    [Fact]
    public async Task DefaultInspectionIsCompactRedactedAndNeverStartsWord()
    {
        var path = CreateTemporaryPackage();
        try
        {
            var host = new NoInvokeHost();
            var service = new WordLiveService(host);

            var summary = await Inspect(service, new { local_path = path });
            var raw = summary.GetRawText();

            Assert.Equal("summary", summary.GetProperty("view").GetString());
            Assert.Equal(1, summary.GetProperty("diagram_count").GetInt32());
            Assert.Equal(2, summary.GetProperty("point_count").GetInt32());
            Assert.Equal(1, summary.GetProperty("connection_count").GetInt32());
            Assert.Equal(5, summary.GetProperty("part_reference_count").GetInt32());
            Assert.False(summary.GetProperty("word_opened").GetBoolean());
            Assert.False(summary.GetProperty("package_mutated").GetBoolean());
            Assert.False(summary.GetProperty("layout_executed").GetBoolean());
            Assert.False(summary.GetProperty("text_values_returned").GetBoolean());
            Assert.False(summary.GetProperty("raw_xml_included").GetBoolean());
            Assert.False(summary.GetProperty("keys_included").GetBoolean());
            Assert.False(summary.GetProperty("hashes_included").GetBoolean());
            Assert.False(summary.GetProperty("source_included").GetBoolean());
            Assert.Equal(0, host.InvocationCount);
            Assert.DoesNotContain(RootModelId, raw);
            Assert.DoesNotContain(ChildModelId, raw);
            Assert.DoesNotContain(PrivateText, raw);
            Assert.DoesNotContain("urn:private:layout", raw);
            Assert.DoesNotContain("/word/diagrams/data1.xml", raw);
            Assert.True(raw.Length < 5_000, $"Default SmartArt response is too large: {raw.Length}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ViewsFiltersAndIndependentOptInsExposeOnlyRequestedMetadata()
    {
        var path = CreateTemporaryPackage();
        try
        {
            var service = new WordLiveService(new NoInvokeHost());
            var diagrams = await Inspect(
                service,
                new
                {
                    local_path = path,
                    view = "diagrams",
                    include_keys = true,
                    include_hashes = true,
                    include_source = true,
                }
            );
            var diagram = Assert.Single(diagrams.GetProperty("items").EnumerateArray());
            var diagramId = diagram.GetProperty("diagram_id").GetString()!;
            Assert.Equal("urn:private:layout", diagram.GetProperty("layout_unique_id").GetString());
            Assert.Equal(
                16,
                diagram.GetProperty("layout_unique_id_fingerprint").GetString()!.Length
            );
            Assert.Equal("/word/document.xml", diagram.GetProperty("source_part_uri").GetString());

            var points = await Inspect(
                service,
                new
                {
                    local_path = path,
                    view = "points",
                    diagram_id = diagramId,
                    point_type = "doc",
                    include_keys = true,
                    include_hashes = true,
                    include_source = true,
                }
            );
            var point = Assert.Single(points.GetProperty("items").EnumerateArray());
            Assert.Equal(RootModelId, point.GetProperty("model_id").GetString());
            Assert.Equal(16, point.GetProperty("model_id_fingerprint").GetString()!.Length);
            Assert.Equal(PrivateText.Length, point.GetProperty("text_character_count").GetInt32());
            Assert.DoesNotContain(PrivateText, points.GetRawText());
            Assert.Equal("/word/diagrams/data1.xml", point.GetProperty("part_uri").GetString());

            var connections = await Inspect(
                service,
                new
                {
                    local_path = path,
                    view = "connections",
                    diagram_id = diagramId,
                    include_keys = true,
                }
            );
            var connection = Assert.Single(
                connections.GetProperty("items").EnumerateArray()
            );
            Assert.Equal(RootModelId, connection.GetProperty("source_model_id").GetString());
            Assert.Equal(ChildModelId, connection.GetProperty("destination_model_id").GetString());

            var parts = await Inspect(
                service,
                new
                {
                    local_path = path,
                    view = "parts",
                    diagram_id = diagramId,
                    include_hashes = true,
                    include_source = true,
                }
            );
            Assert.Equal(5, parts.GetProperty("items").GetArrayLength());
            Assert.Contains(
                parts.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("part_kind").GetString() == "persisted_drawing"
                    && item.GetProperty("source_sha256").GetString()!.Length == 64
            );
            Assert.DoesNotContain(PrivateText, parts.GetRawText());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExactBudgetPassesAndOneByteLessFailsClosed()
    {
        var path = CreateTemporaryPackage();
        try
        {
            var baseline = await Inspect(
                new WordLiveService(new NoInvokeHost()),
                new { local_path = path, view = "diagrams" }
            );
            var used = baseline.GetProperty("operation_budget").GetProperty("used")
                .GetInt64();

            var exact = new WordLiveService(
                new NoInvokeHost(),
                () => new WordOperationResourceLease(used)
            );
            var exactResult = await Inspect(
                exact,
                new { local_path = path, view = "diagrams" }
            );
            Assert.Equal(
                used,
                exactResult.GetProperty("operation_budget").GetProperty("used").GetInt64()
            );

            var limited = new WordLiveService(
                new NoInvokeHost(),
                () => new WordOperationResourceLease(used - 1)
            );
            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                Inspect(limited, new { local_path = path, view = "diagrams" })
            );
            Assert.Equal("PACKAGE_LIMIT", exception.ErrorCode);

            var notFound = await Assert.ThrowsAsync<NativeToolException>(() =>
                Inspect(
                    new WordLiveService(new NoInvokeHost()),
                    new
                    {
                        local_path = path,
                        view = "diagrams",
                        diagram_id = "wdd_aaaaaaaaaaaaaaaaaaaaaaaa",
                    }
                )
            );
            Assert.Equal("NOT_FOUND", notFound.ErrorCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LargeKeyProjectionIsPagedBelowTheResponseCeiling()
    {
        var path = CreateTemporaryPackage(pointCount: 300);
        try
        {
            var result = await Inspect(
                new WordLiveService(new NoInvokeHost()),
                new
                {
                    local_path = path,
                    view = "points",
                    max_items = 50,
                    include_keys = true,
                    include_hashes = true,
                    include_source = true,
                }
            );
            var raw = result.GetRawText();

            Assert.True(result.GetProperty("response_truncated").GetBoolean());
            Assert.True(result.GetProperty("next_offset").GetInt32() > 0);
            Assert.True(result.GetProperty("returned_item_count").GetInt32() < 50);
            Assert.True(raw.Length < 32 * 1_024, $"SmartArt response exceeded 32 KiB: {raw.Length}");
            Assert.DoesNotContain(PrivateText, raw);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SchemaIsLazyReadOnlyAndTokenBounded()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        var serialized = catalog.InspectAction("inspect_ooxml_diagrams").ToJsonString();
        using var document = JsonDocument.Parse(serialized);
        var tool = document.RootElement.GetProperty("tool");

        Assert.True(serialized.Length < 7_000);
        Assert.True(tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
        Assert.False(tool.GetProperty("annotations").GetProperty("openWorldHint").GetBoolean());
        Assert.Equal(
            50,
            tool.GetProperty("inputSchema")
                .GetProperty("properties")
                .GetProperty("max_items")
                .GetProperty("maximum")
                .GetInt32()
        );
        Assert.Equal(
            "wop1",
            tool.GetProperty("resourceAccounting")
                .GetProperty("operation")
                .GetProperty("mcpModel")
                .GetString()
        );
    }

    private static async Task<JsonElement> Inspect(WordLiveService service, object arguments)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        var result = await service.CallAsync(
            "inspect_ooxml_diagrams",
            document.RootElement,
            CancellationToken.None
        );
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(result));
        return serialized.RootElement.Clone();
    }

    private static string CreateTemporaryPackage(int pointCount = 2)
    {
        var path = Path.Combine(Path.GetTempPath(), $"wordtoolkit-diagrams-{Guid.NewGuid():N}.docx");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(archive, "[Content_Types].xml", ContentTypes());
        AddEntry(
            archive,
            "_rels/.rels",
            """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """
        );
        AddEntry(
            archive,
            "word/document.xml",
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                        xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                        xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                        xmlns:dgm="http://schemas.openxmlformats.org/drawingml/2006/diagram"
                        xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body><w:p><w:r><w:drawing><wp:inline>
                <wp:extent cx="914400" cy="914400"/><wp:docPr id="1" name="SmartArt 1"/><wp:cNvGraphicFramePr/>
                <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/diagram">
                  <dgm:relIds r:dm="rId1" r:lo="rId2" r:qs="rId3" r:cs="rId4"/>
                </a:graphicData></a:graphic>
              </wp:inline></w:drawing></w:r></w:p></w:body>
            </w:document>
            """
        );
        AddEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramData" Target="diagrams/data1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramLayout" Target="diagrams/layout1.xml"/>
              <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramQuickStyle" Target="diagrams/quickStyle1.xml"/>
              <Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramColors" Target="diagrams/colors1.xml"/>
              <Relationship Id="rId5" Type="http://schemas.microsoft.com/office/2007/relationships/diagramDrawing" Target="diagrams/drawing1.xml"/>
            </Relationships>
            """
        );
        AddEntry(archive, "word/diagrams/data1.xml", DataXml(pointCount));
        AddEntry(
            archive,
            "word/diagrams/layout1.xml",
            """
            <dgm:layoutDef xmlns:dgm="http://schemas.openxmlformats.org/drawingml/2006/diagram" uniqueId="urn:private:layout" minVer="12.0"><dgm:layoutNode name="root"/></dgm:layoutDef>
            """
        );
        AddEntry(
            archive,
            "word/diagrams/quickStyle1.xml",
            """
            <dgm:styleDef xmlns:dgm="http://schemas.openxmlformats.org/drawingml/2006/diagram" uniqueId="urn:private:style" minVer="12.0"><dgm:styleLbl name="node"/></dgm:styleDef>
            """
        );
        AddEntry(
            archive,
            "word/diagrams/colors1.xml",
            """
            <dgm:colorsDef xmlns:dgm="http://schemas.openxmlformats.org/drawingml/2006/diagram" uniqueId="urn:private:colors" minVer="12.0"/>
            """
        );
        AddEntry(
            archive,
            "word/diagrams/drawing1.xml",
            """
            <dsp:drawing xmlns:dsp="http://schemas.microsoft.com/office/drawing/2008/diagram"><dsp:spTree/></dsp:drawing>
            """
        );
        return path;
    }

    private static string DataXml(int pointCount)
    {
        var points = new StringBuilder();
        for (var index = 0; index < pointCount; index++)
        {
            var modelId = index switch
            {
                0 => RootModelId,
                1 => ChildModelId,
                _ => $"SECRET_MODEL_{index:D4}_{new string('x', 96)}",
            };
            var type = index == 0 ? " type=\"doc\"" : string.Empty;
            var properties = index == 0
                ? "<dgm:prSet loTypeId=\"urn:private:layout\" qsTypeId=\"urn:private:style\" csTypeId=\"urn:private:colors\"/>"
                : string.Empty;
            var text = index == 0 ? PrivateText : $"PRIVATE POINT {index}";
            points.Append($"<dgm:pt modelId=\"{modelId}\"{type}>{properties}<dgm:t><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>{text}</a:t></a:r></a:p></dgm:t></dgm:pt>");
        }
        var connection = pointCount > 1
            ? $"<dgm:cxn modelId=\"SECRET_CONNECTION\" srcId=\"{RootModelId}\" destId=\"{ChildModelId}\" srcOrd=\"0\" destOrd=\"0\"/>"
            : string.Empty;
        return $"""
            <dgm:dataModel xmlns:dgm="http://schemas.openxmlformats.org/drawingml/2006/diagram"
                           xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                           xmlns:dsp="http://schemas.microsoft.com/office/drawing/2008/diagram">
              <dgm:ptLst>{points}</dgm:ptLst><dgm:cxnLst>{connection}</dgm:cxnLst><dgm:bg/><dgm:whole/>
              <dgm:extLst><a:ext uri="http://schemas.microsoft.com/office/drawing/2008/diagram"><dsp:dataModelExt relId="rId5" minVer="http://schemas.openxmlformats.org/drawingml/2006/diagram"/></a:ext></dgm:extLst>
            </dgm:dataModel>
            """;
    }

    private static string ContentTypes() =>
        """
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
          <Override PartName="/word/diagrams/data1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml"/>
          <Override PartName="/word/diagrams/layout1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml"/>
          <Override PartName="/word/diagrams/quickStyle1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml"/>
          <Override PartName="/word/diagrams/colors1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml"/>
          <Override PartName="/word/diagrams/drawing1.xml" ContentType="application/vnd.ms-office.drawingml.diagramDrawing+xml"/>
        </Types>
        """;

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private sealed class NoInvokeHost : IWordComHost
    {
        public int InvocationCount { get; private set; }

        public Task<T> InvokeAsync<T>(
            Func<dynamic, T> operation,
            CancellationToken cancellationToken = default,
            bool launchIfMissing = false
        )
        {
            InvocationCount++;
            throw new InvalidOperationException("COM must not be invoked");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
