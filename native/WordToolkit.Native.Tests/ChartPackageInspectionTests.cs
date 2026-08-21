using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class ChartPackageInspectionTests
{
    [Fact]
    public async Task DefaultChartInspectionIsCompactRedactedAndNeverOpensContent()
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
                "inspect_ooxml_charts",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;

            Assert.Equal("summary", root.GetProperty("view").GetString());
            Assert.Equal(1, root.GetProperty("chart_count").GetInt32());
            Assert.Equal(1, root.GetProperty("series_count").GetInt32());
            Assert.Equal(2, root.GetProperty("axis_count").GetInt32());
            Assert.Equal(5, root.GetProperty("cached_point_count").GetInt64());
            Assert.Equal(1, root.GetProperty("external_relationship_count").GetInt32());
            Assert.False(root.GetProperty("word_opened").GetBoolean());
            Assert.False(root.GetProperty("embedded_packages_opened").GetBoolean());
            Assert.False(root.GetProperty("external_targets_followed").GetBoolean());
            Assert.False(root.GetProperty("sensitive_values_included").GetBoolean());
            Assert.False(root.GetProperty("source_included").GetBoolean());
            Assert.DoesNotContain("PRIVATE CHART TITLE", root.GetRawText());
            Assert.DoesNotContain("Sheet1!", root.GetRawText());
            Assert.DoesNotContain("SECRET_CATEGORY_ALPHA", root.GetRawText());
            Assert.DoesNotContain("991337", root.GetRawText());
            Assert.DoesNotContain("SECRET FORMAT", root.GetRawText());
            Assert.DoesNotContain("secret.example", root.GetRawText());
            Assert.True(
                root.GetRawText().Length < 5_000,
                $"Default chart response is too large: {root.GetRawText().Length} characters"
            );
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SensitiveOptInReturnsTitlesAndFormulasButNeverCachedPointValues()
    {
        var path = CreateTemporaryPackage();
        try
        {
            var service = new WordLiveService(new NoInvokeHost());
            using var redactedArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "series",
                    detail = "declared",
                })
            );
            var redactedResult = await service.CallAsync(
                "inspect_ooxml_charts",
                redactedArguments.RootElement,
                CancellationToken.None
            );
            using var redactedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(redactedResult)
            );
            var redactedRaw = redactedJson.RootElement.GetRawText();
            Assert.DoesNotContain("Sheet1!", redactedRaw);
            Assert.DoesNotContain("SECRET FORMAT", redactedRaw);
            Assert.DoesNotContain("SECRET_CATEGORY_ALPHA", redactedRaw);
            Assert.DoesNotContain("991337", redactedRaw);

            using var chartArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "charts",
                    include_sensitive = true,
                })
            );
            var chartResult = await service.CallAsync(
                "inspect_ooxml_charts",
                chartArguments.RootElement,
                CancellationToken.None
            );
            using var chartJson = JsonDocument.Parse(JsonSerializer.Serialize(chartResult));
            var chart = Assert.Single(
                chartJson.RootElement.GetProperty("items").EnumerateArray()
            );
            Assert.Equal(
                "PRIVATE CHART TITLE",
                chart.GetProperty("title_text").GetString()
            );

            using var seriesArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "series",
                    detail = "declared",
                    include_sensitive = true,
                })
            );
            var seriesResult = await service.CallAsync(
                "inspect_ooxml_charts",
                seriesArguments.RootElement,
                CancellationToken.None
            );
            using var seriesJson = JsonDocument.Parse(
                JsonSerializer.Serialize(seriesResult)
            );
            var raw = seriesJson.RootElement.GetRawText();
            Assert.Contains("Sheet1!$B$2:$B$3", raw);
            Assert.Contains("SECRET FORMAT", raw);
            Assert.DoesNotContain("SECRET_CATEGORY_ALPHA", raw);
            Assert.DoesNotContain("991337", raw);
            Assert.DoesNotContain("point_values", raw);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SourceOptInExposesExternalTargetMetadataButDoesNotFollowIt()
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
                    view = "relationships",
                    detail = "declared",
                    include_source = true,
                })
            );

            var result = await service.CallAsync(
                "inspect_ooxml_charts",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var external = Assert.Single(
                json.RootElement.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("target_mode").GetString() == "external"
            );
            Assert.Equal(
                "https://secret.example/private-workbook.xlsx",
                external.GetProperty("target").GetString()
            );
            Assert.True(external.GetProperty("used_by_external_data").GetBoolean());
            Assert.False(json.RootElement.GetProperty("external_targets_followed").GetBoolean());
            Assert.False(json.RootElement.GetProperty("embedded_packages_opened").GetBoolean());
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RejectsInvalidViewAndUnknownChartId()
    {
        var path = CreateTemporaryPackage();
        try
        {
            var service = new WordLiveService(new NoInvokeHost());
            using var invalidArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path, view = "values" })
            );
            var invalid = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_charts",
                    invalidArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_INPUT", invalid.ErrorCode);

            using var unknownArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "charts",
                    chart_id = "wdc_missing",
                })
            );
            var unknown = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_charts",
                    unknownArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("NOT_FOUND", unknown.ErrorCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ChartGatewayKeepsTheCompleteDefaultEnvelopeBounded()
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
                        action = "inspect_ooxml_charts",
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
            var contentText = result.GetProperty("content")[0].GetProperty("text").GetString()!;
            var data = result.GetProperty("structuredContent").GetProperty("data");
            Assert.True(data.GetRawText().Length < 5_000);
            Assert.True(contentText.Length < 5_000);
            Assert.True(responseLine.Length < 8_000);
            Assert.DoesNotContain("PRIVATE CHART TITLE", responseLine);
            Assert.DoesNotContain("Sheet1!", responseLine);
            Assert.DoesNotContain("SECRET_CATEGORY_ALPHA", responseLine);
            Assert.DoesNotContain("secret.example", responseLine);
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTemporaryPackage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wordtoolkit-chart-{Guid.NewGuid():N}.docx");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(
            archive,
            "[Content_Types].xml",
            """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/charts/chart1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.chart+xml"/>
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
        AddEntry(
            archive,
            "word/document.xml",
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p/></w:body></w:document>
            """
        );
        AddEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdChart" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart" Target="charts/chart1.xml"/>
            </Relationships>
            """
        );
        AddEntry(
            archive,
            "word/charts/chart1.xml",
            """
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <c:chart>
                <c:title><c:tx><c:rich><a:p><a:r><a:t>PRIVATE CHART TITLE</a:t></a:r></a:p></c:rich></c:tx></c:title>
                <c:plotArea>
                  <c:lineChart>
                    <c:ser>
                      <c:idx val="0"/><c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f><c:strCache><c:ptCount val="1"/><c:pt idx="0"><c:v>PRIVATE SERIES</c:v></c:pt></c:strCache></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$3</c:f><c:strCache><c:ptCount val="2"/><c:pt idx="0"><c:v>SECRET_CATEGORY_ALPHA</c:v></c:pt><c:pt idx="1"><c:v>SECRET_CATEGORY_BETA</c:v></c:pt></c:strCache></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$3</c:f><c:numCache><c:formatCode>SECRET FORMAT</c:formatCode><c:ptCount val="2"/><c:pt idx="0"><c:v>991337</c:v></c:pt><c:pt idx="1"><c:v>881337</c:v></c:pt></c:numCache></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="10"/><c:axId val="20"/>
                  </c:lineChart>
                  <c:catAx><c:axId val="10"/><c:axPos val="b"/><c:crossAx val="20"/></c:catAx>
                  <c:valAx><c:axId val="20"/><c:axPos val="l"/><c:crossAx val="10"/></c:valAx>
                </c:plotArea>
              </c:chart>
              <c:externalData r:id="rIdWorkbook"><c:autoUpdate val="0"/></c:externalData>
            </c:chartSpace>
            """
        );
        AddEntry(
            archive,
            "word/charts/_rels/chart1.xml.rels",
            """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdWorkbook" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/package" Target="https://secret.example/private-workbook.xlsx" TargetMode="External"/>
            </Relationships>
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
