using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Resources;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class DocumentPropertyPackageInspectionTests
{
    private const string SecretCustomName = "Customer Secret Code";
    private const string SecretCustomValue = "SENSITIVE-CUSTOM-VALUE";
    private const string SecretTitle = "SENSITIVE-TITLE";
    private const string SecretCompany = "SENSITIVE-COMPANY";

    [Fact]
    public async Task DefaultInspectionIsCompactRedactedAndNeverStartsWord()
    {
        var path = CreateTemporaryPackage();
        try
        {
            var host = new NoInvokeHost();
            var service = new WordLiveService(host);
            var summary = await Inspect(service, new { local_path = path });
            var summaryText = summary.GetRawText();

            Assert.Equal("summary", summary.GetProperty("view").GetString());
            Assert.Equal(3, summary.GetProperty("part_count").GetInt32());
            Assert.Equal(5, summary.GetProperty("property_count").GetInt32());
            Assert.False(summary.GetProperty("word_opened").GetBoolean());
            Assert.False(summary.GetProperty("package_mutated").GetBoolean());
            Assert.False(summary.GetProperty("fields_evaluated").GetBoolean());
            Assert.False(summary.GetProperty("complex_values_decoded").GetBoolean());
            Assert.False(summary.GetProperty("raw_xml_included").GetBoolean());
            Assert.Equal(0, host.InvocationCount);
            AssertRedacted(summaryText);
            Assert.True(
                summaryText.Length < 5_000,
                $"Default property summary is too large: {summaryText.Length} characters"
            );

            var properties = await Inspect(
                service,
                new { local_path = path, view = "properties" }
            );
            var propertyText = properties.GetRawText();
            Assert.Equal(5, properties.GetProperty("matched_item_count").GetInt32());
            AssertRedacted(propertyText);
            var custom = Assert.Single(
                properties.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("property_family").GetString() == "custom"
            );
            Assert.Equal(JsonValueKind.Null, custom.GetProperty("name").ValueKind);
            Assert.True(custom.GetProperty("name_redacted").GetBoolean());
            Assert.Equal(JsonValueKind.Null, custom.GetProperty("value").ValueKind);
            Assert.True(custom.GetProperty("value_redacted").GetBoolean());
            Assert.Equal(JsonValueKind.Null, custom.GetProperty("part_uri").ValueKind);
            Assert.Equal(JsonValueKind.Null, custom.GetProperty("numeric_property_id").ValueKind);
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task NamesValuesHashesAndSourceAreIndependentOptIns()
    {
        var path = CreateTemporaryPackage();
        try
        {
            var service = new WordLiveService(new NoInvokeHost());
            var names = await Inspect(
                service,
                new
                {
                    local_path = path,
                    view = "properties",
                    property_family = "custom",
                    include_names = true,
                }
            );
            var named = Assert.Single(names.GetProperty("items").EnumerateArray());
            Assert.Equal(SecretCustomName, named.GetProperty("name").GetString());
            Assert.Equal(JsonValueKind.Null, named.GetProperty("value").ValueKind);
            Assert.DoesNotContain(SecretCustomValue, names.GetRawText());

            var values = await Inspect(
                service,
                new
                {
                    local_path = path,
                    view = "properties",
                    property_family = "custom",
                    include_values = true,
                }
            );
            var valued = Assert.Single(values.GetProperty("items").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, valued.GetProperty("name").ValueKind);
            Assert.Equal(SecretCustomValue, valued.GetProperty("value").GetString());
            Assert.DoesNotContain(SecretCustomName, values.GetRawText());

            var hashes = await Inspect(
                service,
                new
                {
                    local_path = path,
                    view = "properties",
                    property_family = "custom",
                    include_hashes = true,
                }
            );
            var hashed = Assert.Single(hashes.GetProperty("items").EnumerateArray());
            Assert.Equal(16, hashed.GetProperty("name_fingerprint").GetString()!.Length);
            Assert.Equal(16, hashed.GetProperty("value_fingerprint").GetString()!.Length);
            AssertRedacted(hashes.GetRawText());

            var source = await Inspect(
                service,
                new
                {
                    local_path = path,
                    view = "properties",
                    property_family = "custom",
                    include_source = true,
                }
            );
            var sourced = Assert.Single(source.GetProperty("items").EnumerateArray());
            Assert.Equal(2, sourced.GetProperty("numeric_property_id").GetInt32());
            Assert.Equal("/docProps/custom.xml", sourced.GetProperty("part_uri").GetString());
            AssertSensitiveValuesRedacted(source.GetRawText());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExactFiltersAndSharedOperationBudgetFailClosed()
    {
        var path = CreateTemporaryPackage();
        try
        {
            var host = new NoInvokeHost();
            var service = new WordLiveService(host);
            var properties = await Inspect(
                service,
                new { local_path = path, view = "properties" }
            );
            var custom = Assert.Single(
                properties.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("property_family").GetString() == "custom"
            );
            var propertyId = custom.GetProperty("property_id").GetString()!;
            var used = properties.GetProperty("operation_budget").GetProperty("used")
                .GetInt64();

            var filtered = await Inspect(
                service,
                new
                {
                    local_path = path,
                    view = "properties",
                    property_id = propertyId,
                    value_kind = "text",
                }
            );
            Assert.Single(filtered.GetProperty("items").EnumerateArray());

            await Assert.ThrowsAsync<NativeToolException>(() =>
                Inspect(
                    service,
                    new
                    {
                        local_path = path,
                        view = "properties",
                        property_id = "wdp_aaaaaaaaaaaaaaaaaaaaaaaa",
                    }
                )
            );

            var limited = new WordLiveService(
                new NoInvokeHost(),
                () => new WordOperationResourceLease(used - 1)
            );
            var limit = await Assert.ThrowsAsync<NativeToolException>(() =>
                Inspect(limited, new { local_path = path, view = "properties" })
            );
            Assert.Equal("PACKAGE_LIMIT", limit.ErrorCode);

            var exact = new WordLiveService(
                new NoInvokeHost(),
                () => new WordOperationResourceLease(used)
            );
            var exactResult = await Inspect(
                exact,
                new { local_path = path, view = "properties" }
            );
            Assert.Equal(
                used,
                exactResult.GetProperty("operation_budget").GetProperty("used")
                    .GetInt64()
            );
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CompleteGatewayEnvelopeStaysBoundedAndRedacted()
    {
        var path = CreateTemporaryPackage();
        try
        {
            var request = JsonSerializer.Serialize(
                new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "tools/call",
                    @params = new
                    {
                        name = "execute_wordtoolkit_action",
                        arguments = new
                        {
                            action = "inspect_ooxml_properties",
                            arguments = new { local_path = path },
                        },
                    },
                }
            );
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
                .GetProperty("text")
                .GetString()!;
            var data = result.GetProperty("structuredContent").GetProperty("data");
            Assert.True(data.GetRawText().Length < 5_000);
            Assert.True(contentText.Length < 5_000);
            Assert.True(
                responseLine.Length < 8_000,
                $"Complete property JSON-RPC response is too large: {responseLine.Length} characters"
            );
            AssertRedacted(responseLine);
            Assert.False(data.GetProperty("word_opened").GetBoolean());
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<JsonElement> Inspect(
        WordLiveService service,
        object arguments
    )
    {
        using var argumentJson = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        var result = await service.CallAsync(
            "inspect_ooxml_properties",
            argumentJson.RootElement,
            CancellationToken.None
        );
        using var resultJson = JsonDocument.Parse(JsonSerializer.Serialize(result));
        return resultJson.RootElement.Clone();
    }

    private static void AssertRedacted(string text)
    {
        AssertSensitiveValuesRedacted(text);
        Assert.DoesNotContain("/docProps/", text);
    }

    private static void AssertSensitiveValuesRedacted(string text)
    {
        Assert.DoesNotContain(SecretCustomName, text);
        Assert.DoesNotContain(SecretCustomValue, text);
        Assert.DoesNotContain(SecretTitle, text);
        Assert.DoesNotContain(SecretCompany, text);
    }

    private static string CreateTemporaryPackage()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-document-properties-{Guid.NewGuid():N}.docx"
        );
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(
            archive,
            "[Content_Types].xml",
            "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>"
                + "<Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/>"
                + "<Default Extension='xml' ContentType='application/xml'/>"
                + "<Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/>"
                + "<Override PartName='/docProps/core.xml' ContentType='application/vnd.openxmlformats-package.core-properties+xml'/>"
                + "<Override PartName='/docProps/app.xml' ContentType='application/vnd.openxmlformats-officedocument.extended-properties+xml'/>"
                + "<Override PartName='/docProps/custom.xml' ContentType='application/vnd.openxmlformats-officedocument.custom-properties+xml'/>"
                + "</Types>"
        );
        AddEntry(
            archive,
            "_rels/.rels",
            "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
                + "<Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='word/document.xml'/>"
                + "<Relationship Id='rId2' Type='http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties' Target='docProps/core.xml'/>"
                + "<Relationship Id='rId3' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties' Target='docProps/app.xml'/>"
                + "<Relationship Id='rId4' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties' Target='docProps/custom.xml'/>"
                + "</Relationships>"
        );
        AddEntry(
            archive,
            "word/document.xml",
            "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p/></w:body></w:document>"
        );
        AddEntry(
            archive,
            "docProps/core.xml",
            $"<cp:coreProperties xmlns:cp='http://schemas.openxmlformats.org/package/2006/metadata/core-properties' xmlns:dc='http://purl.org/dc/elements/1.1/'><dc:title>{SecretTitle}</dc:title><dc:creator>Ada</dc:creator></cp:coreProperties>"
        );
        AddEntry(
            archive,
            "docProps/app.xml",
            $"<Properties xmlns='http://schemas.openxmlformats.org/officeDocument/2006/extended-properties'><Company>{SecretCompany}</Company><Pages>3</Pages></Properties>"
        );
        AddEntry(
            archive,
            "docProps/custom.xml",
            $"<op:Properties xmlns:op='http://schemas.openxmlformats.org/officeDocument/2006/custom-properties' xmlns:vt='http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes'><op:property fmtid='{{D5CDD505-2E9C-101B-9397-08002B2CF9AE}}' pid='2' name='{SecretCustomName}'><vt:lpwstr>{SecretCustomValue}</vt:lpwstr></op:property></op:Properties>"
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
