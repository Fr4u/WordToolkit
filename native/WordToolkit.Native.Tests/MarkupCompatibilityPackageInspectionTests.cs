using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class MarkupCompatibilityPackageInspectionTests
{
    private const string SecretNamespace = "urn:secret-company:contract-schema";

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
                "inspect_ooxml_markup_compatibility",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;

            Assert.Equal("summary", root.GetProperty("view").GetString());
            Assert.True(root.GetProperty("xml_part_count").GetInt32() >= 2);
            Assert.Equal(4, root.GetProperty("rule_count").GetInt32());
            Assert.Equal(1, root.GetProperty("alternate_content_count").GetInt32());
            Assert.Equal(1, root.GetProperty("selected_fallback_count").GetInt32());
            Assert.Equal(0, root.GetProperty("selected_choice_count").GetInt32());
            Assert.False(root.GetProperty("word_opened").GetBoolean());
            Assert.False(root.GetProperty("external_targets_followed").GetBoolean());
            Assert.False(root.GetProperty("package_mutated").GetBoolean());
            Assert.False(root.GetProperty("namespace_details_included").GetBoolean());
            Assert.False(root.GetProperty("source_included").GetBoolean());
            var raw = root.GetRawText();
            Assert.DoesNotContain(SecretNamespace, raw);
            Assert.DoesNotContain("privateClause", raw);
            Assert.DoesNotContain("private.xml", raw);
            Assert.DoesNotContain("SECRET-DOCUMENT-CONTENT", raw);
            Assert.True(
                raw.Length < 5_000,
                $"Default MCE response is too large: {raw.Length} characters"
            );
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExplicitApplicationConfigurationSelectsChoiceAndDetailsAreOptIn()
    {
        var path = CreateTemporaryPackage();
        try
        {
            var service = new WordLiveService(new NoInvokeHost());
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "alternate_content",
                    understood_namespaces = new[] { SecretNamespace },
                    include_namespace_details = true,
                })
            );

            var result = await service.CallAsync(
                "inspect_ooxml_markup_compatibility",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;
            Assert.Equal(1, root.GetProperty("selected_choice_count").GetInt32());
            Assert.Equal(0, root.GetProperty("selected_fallback_count").GetInt32());
            var alternate = Assert.Single(root.GetProperty("items").EnumerateArray());
            Assert.Equal(
                "choice",
                alternate.GetProperty("branches").EnumerateArray()
                    .Single(branch => branch.GetProperty("selected").GetBoolean())
                    .GetProperty("kind").GetString()
            );

            using var namespaceArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "namespaces",
                    understood_namespaces = new[] { SecretNamespace },
                    include_namespace_details = true,
                })
            );
            var namespaceResult = await service.CallAsync(
                "inspect_ooxml_markup_compatibility",
                namespaceArguments.RootElement,
                CancellationToken.None
            );
            using var namespaceJson = JsonDocument.Parse(
                JsonSerializer.Serialize(namespaceResult)
            );
            Assert.Contains(
                namespaceJson.RootElement.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("namespace_uri").GetString() == SecretNamespace
                    && item.GetProperty("understood").GetBoolean()
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SourceOptInReturnsPartMetadataButNeverDocumentText()
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
                    view = "parts",
                    include_source = true,
                })
            );

            var result = await service.CallAsync(
                "inspect_ooxml_markup_compatibility",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var raw = json.RootElement.GetRawText();
            Assert.Contains("/customXml/private.xml", raw);
            Assert.Contains("source_sha256", raw);
            Assert.DoesNotContain("SECRET-DOCUMENT-CONTENT", raw);
            Assert.False(json.RootElement.GetProperty("word_opened").GetBoolean());
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RejectsInvalidConfigurationAndUnknownPartId()
    {
        var path = CreateTemporaryPackage();
        try
        {
            var service = new WordLiveService(new NoInvokeHost());
            using var invalidArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    understood_namespaces = new[] { "" },
                })
            );
            var invalid = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_markup_compatibility",
                    invalidArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_INPUT", invalid.ErrorCode);

            using var unknownArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "parts",
                    part_id = "wmcp_00000000000000000000000000000000",
                })
            );
            var unknown = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_markup_compatibility",
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
                        action = "inspect_ooxml_markup_compatibility",
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
            Assert.DoesNotContain(SecretNamespace, responseLine);
            Assert.DoesNotContain("privateClause", responseLine);
            Assert.DoesNotContain("private.xml", responseLine);
            Assert.DoesNotContain("SECRET-DOCUMENT-CONTENT", responseLine);
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTemporaryPackage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wordtoolkit-mce-{Guid.NewGuid():N}.docx");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(
            archive,
            "[Content_Types].xml",
            """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
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
            $$"""
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                xmlns:secret="{{SecretNamespace}}"
                mc:Ignorable="secret" mc:ProcessContent="secret:unwrap"
                mc:MustUnderstand="secret" mc:PreserveElements="secret:*">
              <w:body>
                <secret:privateClause>SECRET-DOCUMENT-CONTENT</secret:privateClause>
                <secret:unwrap><w:p/></secret:unwrap>
                <mc:AlternateContent>
                  <mc:Choice Requires="secret"><w:p/></mc:Choice>
                  <mc:Fallback><w:p/></mc:Fallback>
                </mc:AlternateContent>
              </w:body>
            </w:document>
            """
        );
        AddEntry(
            archive,
            "customXml/private.xml",
            $$"""
            <secret:record xmlns:secret="{{SecretNamespace}}"><secret:value>SECRET-DOCUMENT-CONTENT</secret:value></secret:record>
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
