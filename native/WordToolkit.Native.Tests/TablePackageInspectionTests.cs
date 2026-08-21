using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class TablePackageInspectionTests
{
    private const string SecretCellText = "ULTRA-SECRET-CELL-TEXT";
    private const string SecretCaption = "SECRET-TABLE-CAPTION";
    private const string SecretDescription = "SECRET-TABLE-DESCRIPTION";
    private const string SecretStyle = "SecretCorporateTableStyle";

    [Fact]
    public async Task DefaultInspectionIsCompactRedactedAndNeverInvokesWord()
    {
        var path = CreateTemporaryPackage();
        try
        {
            var host = new NoInvokeHost();
            var service = new WordLiveService(host);
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path })
            );

            var result = await service.CallAsync(
                "inspect_ooxml_tables",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;

            Assert.Equal("summary", root.GetProperty("view").GetString());
            Assert.Equal(2, root.GetProperty("table_count").GetInt32());
            Assert.Equal(1, root.GetProperty("nested_table_count").GetInt32());
            Assert.Equal(3, root.GetProperty("row_count").GetInt32());
            Assert.Equal(5, root.GetProperty("cell_count").GetInt32());
            Assert.Equal(1, root.GetProperty("vertical_merge_count").GetInt32());
            Assert.False(root.GetProperty("word_opened").GetBoolean());
            Assert.False(root.GetProperty("package_mutated").GetBoolean());
            Assert.False(root.GetProperty("cell_text_included").GetBoolean());
            Assert.False(root.GetProperty("raw_xml_included").GetBoolean());
            Assert.False(root.GetProperty("layout_included").GetBoolean());
            Assert.False(root.GetProperty("names_included").GetBoolean());
            Assert.False(root.GetProperty("source_included").GetBoolean());
            AssertRedacted(root.GetRawText());
            Assert.True(
                root.GetRawText().Length < 5_000,
                $"Default table response is too large: {root.GetRawText().Length} characters"
            );
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task NamesLayoutAndSourceRequireExplicitOptInsButCellTextNeverLeaks()
    {
        var path = CreateTemporaryPackage();
        try
        {
            var host = new NoInvokeHost();
            var service = new WordLiveService(host);
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "tables",
                    include_names = true,
                    include_layout = true,
                    include_source = true,
                })
            );

            var result = await service.CallAsync(
                "inspect_ooxml_tables",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var raw = json.RootElement.GetRawText();

            Assert.Contains(SecretCaption, raw);
            Assert.Contains(SecretDescription, raw);
            Assert.Contains(SecretStyle, raw);
            Assert.Contains("/word/document.xml", raw);
            Assert.Contains("fixed", raw);
            Assert.Contains("percent", raw);
            Assert.Contains("horizontal_position_twips", raw);
            Assert.DoesNotContain(SecretCellText, raw);
            Assert.DoesNotContain("<w:tbl", raw);
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExactIdsFilterTopologyAndUnknownIdsFailClosed()
    {
        var path = CreateTemporaryPackage();
        try
        {
            var service = new WordLiveService(new NoInvokeHost());
            using var discoveryArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "cells",
                    max_items = 100,
                })
            );
            var discovery = await service.CallAsync(
                "inspect_ooxml_tables",
                discoveryArguments.RootElement,
                CancellationToken.None
            );
            using var discoveryJson = JsonDocument.Parse(
                JsonSerializer.Serialize(discovery)
            );
            var cells = discoveryJson.RootElement.GetProperty("items")
                .EnumerateArray().ToArray();
            var mergedContinuation = cells.Single(cell =>
                cell.GetProperty("vertical_merge").GetString() == "continue"
            );
            var cellId = mergedContinuation.GetProperty("cell_id").GetString()!;
            var rowId = mergedContinuation.GetProperty("row_id").GetString()!;
            var tableId = mergedContinuation.GetProperty("table_id").GetString()!;

            using var filteredArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "merges",
                    table_id = tableId,
                    row_id = rowId,
                    cell_id = cellId,
                })
            );
            var filtered = await service.CallAsync(
                "inspect_ooxml_tables",
                filteredArguments.RootElement,
                CancellationToken.None
            );
            using var filteredJson = JsonDocument.Parse(
                JsonSerializer.Serialize(filtered)
            );
            var merge = Assert.Single(
                filteredJson.RootElement.GetProperty("items").EnumerateArray()
            );
            Assert.Equal(2, merge.GetProperty("row_span").GetInt32());
            Assert.Equal(2, merge.GetProperty("grid_span").GetInt32());

            using var unknownArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    table_id = "wdt_00000000000000000000",
                })
            );
            var unknown = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_tables",
                    unknownArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("NOT_FOUND", unknown.ErrorCode);

            using var invalidArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path, view = "xml" })
            );
            var invalid = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_tables",
                    invalidArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_INPUT", invalid.ErrorCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CompleteDefaultGatewayEnvelopeStaysBoundedAndRedacted()
    {
        var path = CreateTemporaryPackage();
        try
        {
            var request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/call",
                @params = new
                {
                    name = "execute_wordtoolkit_action",
                    arguments = new
                    {
                        action = "inspect_ooxml_tables",
                        arguments = new { local_path = path },
                    },
                },
            });
            var output = new StringWriter();
            var host = new NoInvokeHost();
            var server = new McpServer(
                new StringReader(request + "\n"),
                output,
                ToolCatalog.LoadNativeWordTools(),
                new WordLiveService(host)
            );

            await server.RunAsync();

            var responseLine = output.ToString().TrimEnd('\r', '\n');
            using var response = JsonDocument.Parse(responseLine);
            var result = response.RootElement.GetProperty("result");
            var contentText = result.GetProperty("content")[0]
                .GetProperty("text").GetString()!;
            var data = result.GetProperty("structuredContent").GetProperty("data");
            Assert.True(data.GetRawText().Length < 5_000);
            Assert.True(contentText.Length < 5_000);
            Assert.True(responseLine.Length < 8_000);
            AssertRedacted(responseLine);
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GridWidthsHaveAnIndependentNestedResponseLimit()
    {
        var path = CreateTemporaryPackage(columnCount: 150);
        try
        {
            var service = new WordLiveService(new NoInvokeHost());
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "tables",
                    include_layout = true,
                    max_items = 1,
                })
            );

            var result = await service.CallAsync(
                "inspect_ooxml_tables",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var table = Assert.Single(
                json.RootElement.GetProperty("items").EnumerateArray()
            );
            Assert.Equal(150, table.GetProperty("declared_grid_column_count").GetInt32());
            Assert.Equal(100, table.GetProperty("grid_columns").GetArrayLength());
            Assert.True(table.GetProperty("grid_columns_truncated").GetBoolean());
            Assert.True(json.RootElement.GetRawText().Length < 20_000);
            AssertRedacted(json.RootElement.GetRawText());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AssertRedacted(string value)
    {
        Assert.DoesNotContain(SecretCellText, value);
        Assert.DoesNotContain(SecretCaption, value);
        Assert.DoesNotContain(SecretDescription, value);
        Assert.DoesNotContain(SecretStyle, value);
        Assert.DoesNotContain("/word/document.xml", value);
        Assert.DoesNotContain("<w:tbl", value);
    }

    private static string CreateTemporaryPackage(int columnCount = 3)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-tables-{Guid.NewGuid():N}.docx"
        );
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(
            archive,
            "[Content_Types].xml",
            "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>"
                + "<Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/>"
                + "<Default Extension='xml' ContentType='application/xml'/>"
                + "<Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/>"
                + "</Types>"
        );
        AddEntry(
            archive,
            "_rels/.rels",
            "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
                + "<Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='word/document.xml'/>"
                + "</Relationships>"
        );
        AddEntry(
            archive,
            "word/document.xml",
            $$"""
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:tbl>
                  <w:tblPr>
                    <w:tblStyle w:val="{{SecretStyle}}"/>
                    <w:tblW w:w="5000" w:type="pct"/>
                    <w:tblLayout w:type="fixed"/>
                    <w:tblpPr w:tblpX="24" w:tblpY="48"/>
                    <w:tblCaption w:val="{{SecretCaption}}"/>
                    <w:tblDescription w:val="{{SecretDescription}}"/>
                  </w:tblPr>
                  <w:tblGrid>{{string.Concat(Enumerable.Range(0, columnCount).Select(index => $"<w:gridCol w:w='{1200 + index}'/>"))}}</w:tblGrid>
                  <w:tr>
                    <w:trPr><w:tblHeader/></w:trPr>
                    <w:tc><w:tcPr><w:gridSpan w:val="2"/><w:vMerge w:val="restart"/></w:tcPr><w:p><w:r><w:t>{{SecretCellText}}</w:t></w:r></w:p></w:tc>
                    <w:tc><w:p/></w:tc>
                  </w:tr>
                  <w:tr>
                    <w:tc><w:tcPr><w:gridSpan w:val="2"/><w:vMerge/></w:tcPr><w:p/></w:tc>
                    <w:tc>
                      <w:p/>
                      <w:tbl><w:tblPr/><w:tblGrid><w:gridCol/></w:tblGrid><w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl>
                    </w:tc>
                  </w:tr>
                </w:tbl>
                <w:sectPr/>
              </w:body>
            </w:document>
            """
        );
        return path;
    }

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
