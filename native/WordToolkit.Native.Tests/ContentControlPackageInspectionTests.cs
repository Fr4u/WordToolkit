using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class ContentControlPackageInspectionTests
{
    private const string SecretNamespace = "urn:secret-company:customer-schema";
    private const string SecretAlias = "SECRET CUSTOMER ALIAS";
    private const string SecretTag = "SECRET-CUSTOMER-TAG";
    private const string SecretDisplayValue = "SECRET-DISPLAY-VALUE";
    private const string SecretXmlValue = "ULTRA-SECRET-XML-VALUE";
    private const string StoreItemId =
        "{A6C895A1-6B29-470C-84D7-6D14B798EAE7}";

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
                "inspect_ooxml_content_controls",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;

            Assert.Equal("summary", root.GetProperty("view").GetString());
            Assert.Equal(1, root.GetProperty("control_count").GetInt32());
            Assert.Equal(1, root.GetProperty("store_count").GetInt32());
            Assert.Equal(1, root.GetProperty("binding_count").GetInt32());
            Assert.Equal(1, root.GetProperty("resolved_binding_count").GetInt32());
            Assert.Equal(1, root.GetProperty("binding_target_count").GetInt32());
            Assert.False(root.GetProperty("word_opened").GetBoolean());
            Assert.False(root.GetProperty("package_mutated").GetBoolean());
            Assert.False(root.GetProperty("custom_xml_values_included").GetBoolean());
            Assert.False(root.GetProperty("raw_xml_included").GetBoolean());
            Assert.False(root.GetProperty("names_included").GetBoolean());
            Assert.False(root.GetProperty("binding_details_included").GetBoolean());
            Assert.False(root.GetProperty("source_included").GetBoolean());
            AssertRedacted(root.GetRawText());
            Assert.True(
                root.GetRawText().Length < 5_000,
                $"Default content-control response is too large: {root.GetRawText().Length} characters"
            );
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task NamesBindingDetailsAndSourceRequireIndependentOptIns()
    {
        var path = CreateTemporaryPackage();
        try
        {
            var host = new NoInvokeHost();
            var service = new WordLiveService(host);
            using var controlArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "controls",
                    include_names = true,
                    include_source = true,
                })
            );
            var controlResult = await service.CallAsync(
                "inspect_ooxml_content_controls",
                controlArguments.RootElement,
                CancellationToken.None
            );
            using var controlJson = JsonDocument.Parse(
                JsonSerializer.Serialize(controlResult)
            );
            var controlRaw = controlJson.RootElement.GetRawText();
            Assert.Contains(SecretAlias, controlRaw);
            Assert.Contains(SecretTag, controlRaw);
            Assert.Contains("/word/document.xml", controlRaw);
            Assert.DoesNotContain(SecretNamespace, controlRaw);
            Assert.DoesNotContain(SecretDisplayValue, controlRaw);
            Assert.DoesNotContain(SecretXmlValue, controlRaw);

            using var bindingArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "bindings",
                    include_binding_details = true,
                    include_source = true,
                })
            );
            var bindingResult = await service.CallAsync(
                "inspect_ooxml_content_controls",
                bindingArguments.RootElement,
                CancellationToken.None
            );
            using var bindingJson = JsonDocument.Parse(
                JsonSerializer.Serialize(bindingResult)
            );
            var bindingRaw = bindingJson.RootElement.GetRawText();
            Assert.Contains("a6c895a1-6b29-470c-84d7-6d14b798eae7", bindingRaw);
            Assert.Contains("/secret:profile[1]/secret:name[1]", bindingRaw);
            Assert.Contains(SecretNamespace, bindingRaw);
            Assert.Contains("/word/document.xml", bindingRaw);
            Assert.DoesNotContain(SecretAlias, bindingRaw);
            Assert.DoesNotContain(SecretTag, bindingRaw);
            Assert.DoesNotContain(SecretDisplayValue, bindingRaw);
            Assert.DoesNotContain(SecretXmlValue, bindingRaw);

            using var targetArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "targets",
                    include_binding_details = true,
                    include_source = true,
                })
            );
            var targetResult = await service.CallAsync(
                "inspect_ooxml_content_controls",
                targetArguments.RootElement,
                CancellationToken.None
            );
            using var targetJson = JsonDocument.Parse(
                JsonSerializer.Serialize(targetResult)
            );
            var targetRaw = targetJson.RootElement.GetRawText();
            Assert.Contains(SecretNamespace, targetRaw);
            Assert.Contains("name", targetRaw);
            Assert.DoesNotContain(SecretDisplayValue, targetRaw);
            Assert.DoesNotContain(SecretXmlValue, targetRaw);
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExactIdsFilterTheBoundObjectsAndUnknownIdsFailClosed()
    {
        var path = CreateTemporaryPackage();
        try
        {
            var service = new WordLiveService(new NoInvokeHost());
            using var discoveryArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "bindings",
                })
            );
            var discovery = await service.CallAsync(
                "inspect_ooxml_content_controls",
                discoveryArguments.RootElement,
                CancellationToken.None
            );
            using var discoveryJson = JsonDocument.Parse(
                JsonSerializer.Serialize(discovery)
            );
            var binding = Assert.Single(
                discoveryJson.RootElement.GetProperty("items").EnumerateArray()
            );
            var bindingId = binding.GetProperty("binding_id").GetString()!;
            var controlId = binding.GetProperty("control_id").GetString()!;
            var storeId = binding.GetProperty("store_id").GetString()!;

            using var filteredArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "targets",
                    binding_id = bindingId,
                    control_id = controlId,
                    store_id = storeId,
                })
            );
            var filtered = await service.CallAsync(
                "inspect_ooxml_content_controls",
                filteredArguments.RootElement,
                CancellationToken.None
            );
            using var filteredJson = JsonDocument.Parse(
                JsonSerializer.Serialize(filtered)
            );
            Assert.Equal(
                1,
                filteredJson.RootElement.GetProperty("matched_item_count").GetInt32()
            );
            Assert.Single(filteredJson.RootElement.GetProperty("items").EnumerateArray());

            using var unknownArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "controls",
                    control_id = "wccc_00000000000000000000000000000000",
                })
            );
            var unknown = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_content_controls",
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
                    "inspect_ooxml_content_controls",
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
                        action = "inspect_ooxml_content_controls",
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
    public async Task NestedBindingTargetIdsHaveAnIndependentResponseLimit()
    {
        var path = CreateTemporaryPackage(targetCount: 150);
        try
        {
            var service = new WordLiveService(new NoInvokeHost());
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "bindings",
                    max_items = 1,
                })
            );

            var result = await service.CallAsync(
                "inspect_ooxml_content_controls",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var item = Assert.Single(
                json.RootElement.GetProperty("items").EnumerateArray()
            );
            Assert.Equal(150, item.GetProperty("target_count").GetInt32());
            Assert.Equal(
                100,
                item.GetProperty("target_ids").GetArrayLength()
            );
            Assert.True(item.GetProperty("target_ids_truncated").GetBoolean());
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
        Assert.DoesNotContain(SecretNamespace, value);
        Assert.DoesNotContain(SecretAlias, value);
        Assert.DoesNotContain(SecretTag, value);
        Assert.DoesNotContain(SecretDisplayValue, value);
        Assert.DoesNotContain(SecretXmlValue, value);
        Assert.DoesNotContain("/secret:profile", value);
        Assert.DoesNotContain("A6C895A1", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/word/document.xml", value);
        Assert.DoesNotContain("/customXml/item1.xml", value);
    }

    private static string CreateTemporaryPackage(int targetCount = 1)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-content-controls-{Guid.NewGuid():N}.docx"
        );
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(
            archive,
            "[Content_Types].xml",
            "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>"
                + "<Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/>"
                + "<Default Extension='xml' ContentType='application/xml'/>"
                + "<Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/>"
                + "<Override PartName='/customXml/itemProps1.xml' ContentType='application/vnd.openxmlformats-officedocument.customXmlProperties+xml'/>"
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
                <w:sdt>
                  <w:sdtPr>
                    <w:id w:val="42"/><w:alias w:val="{{SecretAlias}}"/>
                    <w:tag w:val="{{SecretTag}}"/><w:text/>
                    <w:dataBinding w:storeItemID="{{StoreItemId}}"
                      w:xpath="/secret:profile[1]/secret:name{{(targetCount == 1 ? "[1]" : string.Empty)}}"
                      w:prefixMappings="xmlns:secret='{{SecretNamespace}}'"/>
                  </w:sdtPr>
                  <w:sdtContent><w:p><w:r><w:t>{{SecretDisplayValue}}</w:t></w:r></w:p></w:sdtContent>
                </w:sdt>
                <w:sectPr/>
              </w:body>
            </w:document>
            """
        );
        AddEntry(
            archive,
            "word/_rels/document.xml.rels",
            "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
                + "<Relationship Id='rIdCustom' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml' Target='../customXml/item1.xml'/>"
                + "</Relationships>"
        );
        AddEntry(
            archive,
            "customXml/item1.xml",
            $$"""
            <secret:profile xmlns:secret="{{SecretNamespace}}">{{string.Concat(Enumerable.Range(0, targetCount).Select(index => $"<secret:name>{SecretXmlValue}-{index}</secret:name>"))}}</secret:profile>
            """
        );
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
            <ds:datastoreItem ds:itemID="{{StoreItemId}}" xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml">
              <ds:schemaRefs><ds:schemaRef ds:uri="{{SecretNamespace}}"/></ds:schemaRefs>
            </ds:datastoreItem>
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
