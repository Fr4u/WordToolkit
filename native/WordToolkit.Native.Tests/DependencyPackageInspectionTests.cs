using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class DependencyPackageInspectionTests
{
    [Fact]
    public async Task InspectDependenciesIsCompactRedactedPagedAndNeverStartsWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-dependency-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "dependencies.docx");
            CreatePackage(path);
            var host = new NoInvokeHost();
            var service = new WordLiveService(host);
            using var summaryArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path })
            );

            var summaryObject = await service.CallAsync(
                "inspect_ooxml_dependencies",
                summaryArguments.RootElement,
                CancellationToken.None
            );
            using var summaryJson = JsonDocument.Parse(
                JsonSerializer.Serialize(summaryObject)
            );
            var summary = summaryJson.RootElement;
            Assert.Equal("summary", summary.GetProperty("view").GetString());
            Assert.False(summary.GetProperty("word_opened").GetBoolean());
            Assert.False(
                summary.GetProperty("external_targets_followed").GetBoolean()
            );
            Assert.False(summary.GetProperty("keys_included").GetBoolean());
            Assert.True(summary.GetProperty("node_count").GetInt32() > 0);
            Assert.True(summary.GetProperty("edge_count").GetInt32() > 0);
            Assert.Equal(1, summary.GetProperty("external_edge_count").GetInt32());
            Assert.True(
                summary.GetProperty("coverage")
                    .GetProperty("package_relationships")
                    .GetBoolean()
            );
            Assert.Contains(
                "charts_smartart_diagrams",
                summary.GetProperty("coverage")
                    .GetProperty("explicitly_unmodeled_domains")
                    .EnumerateArray()
                    .Select(value => value.GetString())
            );
            Assert.DoesNotContain("secret.example", summary.GetRawText());
            Assert.True(
                summary.GetRawText().Length < 5_000,
                $"Default dependency response is too large: {summary.GetRawText().Length} characters"
            );
            Assert.Equal(0, host.InvocationCount);

            using var externalArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "nodes",
                    node_kind = "external_target",
                })
            );
            var externalObject = await service.CallAsync(
                "inspect_ooxml_dependencies",
                externalArguments.RootElement,
                CancellationToken.None
            );
            using var externalJson = JsonDocument.Parse(
                JsonSerializer.Serialize(externalObject)
            );
            var externalNode = Assert.Single(
                externalJson.RootElement.GetProperty("items").EnumerateArray()
            );
            Assert.Equal(JsonValueKind.Null, externalNode.GetProperty("key").ValueKind);
            Assert.True(externalNode.GetProperty("key_redacted").GetBoolean());
            Assert.Equal(
                16,
                externalNode.GetProperty("key_fingerprint").GetString()!.Length
            );
            Assert.DoesNotContain("secret.example", externalJson.RootElement.GetRawText());

            using var styleArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "nodes",
                    node_kind = "style",
                    include_keys = true,
                    max_items = 1,
                })
            );
            var styleObject = await service.CallAsync(
                "inspect_ooxml_dependencies",
                styleArguments.RootElement,
                CancellationToken.None
            );
            using var styleJson = JsonDocument.Parse(JsonSerializer.Serialize(styleObject));
            var styleNode = Assert.Single(
                styleJson.RootElement.GetProperty("items").EnumerateArray()
            );
            Assert.NotNull(styleNode.GetProperty("key").GetString());
            var styleNodeId = styleNode.GetProperty("node_id").GetString();

            using var impactArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "impact",
                    node_id = styleNodeId,
                    direction = "both",
                    max_depth = 2,
                    max_items = 100,
                })
            );
            var impactObject = await service.CallAsync(
                "inspect_ooxml_dependencies",
                impactArguments.RootElement,
                CancellationToken.None
            );
            using var impactJson = JsonDocument.Parse(
                JsonSerializer.Serialize(impactObject)
            );
            Assert.Equal("impact", impactJson.RootElement.GetProperty("view").GetString());
            Assert.True(
                impactJson.RootElement.GetProperty("returned_item_count").GetInt32() > 0
            );
            Assert.All(
                impactJson.RootElement.GetProperty("items").EnumerateArray(),
                edge => Assert.StartsWith(
                    "wdde_",
                    edge.GetProperty("edge_id").GetString(),
                    StringComparison.Ordinal
                )
            );
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectDependenciesRejectsInvalidViewAndUnknownImpactNode()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-dependency-errors",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "dependencies.docx");
            CreatePackage(path);
            var service = new WordLiveService(new NoInvokeHost());
            using var invalidViewArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path, view = "everything" })
            );
            var invalidView = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_dependencies",
                    invalidViewArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_INPUT", invalidView.ErrorCode);

            using var missingNodeArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "impact",
                    node_id = "wddn_AAAAAAAAAAAAAAAAAAAA",
                })
            );
            var missingNode = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_dependencies",
                    missingNodeArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("NOT_FOUND", missingNode.ErrorCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DefaultDependencySummaryStaysCompactOnFieldHeavyCorpusDocument()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "upstream",
            "fixtures",
            "lo_toc_preserve.docx"
        );
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new { local_path = path })
        );
        var host = new NoInvokeHost();
        var service = new WordLiveService(host);

        var result = await service.CallAsync(
            "inspect_ooxml_dependencies",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));

        Assert.Equal("summary", json.RootElement.GetProperty("view").GetString());
        Assert.True(json.RootElement.GetProperty("edge_count").GetInt32() > 0);
        Assert.True(
            json.RootElement.GetRawText().Length < 5_000,
            $"Default dependency response is too large: {json.RootElement.GetRawText().Length} characters"
        );
        Assert.False(json.RootElement.GetProperty("keys_included").GetBoolean());
        Assert.Equal(0, host.InvocationCount);
    }

    [Fact]
    public async Task DependencyGatewayKeepsTheCompleteJsonRpcEnvelopeBoundedWithoutWord()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "upstream",
            "fixtures",
            "lo_toc_preserve.docx"
        );
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
                        action = "inspect_ooxml_dependencies",
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
        var contentText = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        var data = result.GetProperty("structuredContent").GetProperty("data");
        Assert.True(
            data.GetRawText().Length < 5_000,
            $"Dependency result data is too large: {data.GetRawText().Length} characters"
        );
        Assert.True(
            contentText.Length < 5_000,
            $"Dependency text content is too large: {contentText.Length} characters"
        );
        Assert.True(
            responseLine.Length < 8_000,
            $"Complete dependency JSON-RPC response is too large: {responseLine.Length} characters"
        );
        Assert.Equal("summary", data.GetProperty("view").GetString());
        Assert.False(data.GetProperty("word_opened").GetBoolean());
        Assert.False(data.GetProperty("external_targets_followed").GetBoolean());
        Assert.Equal(0, host.InvocationCount);
    }

    private static void CreatePackage(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
            </Types>
            """
        );
        AddEntry(
            archive,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """
        );
        AddEntry(
            archive,
            "word/document.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p><w:pPr><w:pStyle w:val="Definition"/></w:pPr><w:r><w:t>Definition body</w:t></w:r></w:p>
                <w:sectPr/>
              </w:body>
            </w:document>
            """
        );
        AddEntry(
            archive,
            "word/styles.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
              <w:style w:type="paragraph" w:styleId="Definition"><w:basedOn w:val="Normal"/></w:style>
            </w:styles>
            """
        );
        AddEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://secret.example/private" TargetMode="External"/>
            </Relationships>
            """
        );
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var writer = new StreamWriter(
            entry.Open(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
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
        throw new DirectoryNotFoundException("Repository root was not found");
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
