using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Resources;
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
            Assert.False(summary.GetProperty("active_content_executed").GetBoolean());
            Assert.False(summary.GetProperty("binary_payloads_decoded").GetBoolean());
            Assert.False(summary.GetProperty("embedded_packages_opened").GetBoolean());
            Assert.False(
                summary.GetProperty("cryptographic_signature_validation_performed")
                    .GetBoolean()
            );
            Assert.False(summary.GetProperty("keys_included").GetBoolean());
            Assert.True(summary.GetProperty("node_count").GetInt32() > 0);
            Assert.True(summary.GetProperty("edge_count").GetInt32() > 0);
            var byteBudget = summary.GetProperty("byte_budget");
            Assert.Equal("wdg1", byteBudget.GetProperty("model").GetString());
            var accountedBytes = byteBudget.GetProperty("used").GetInt64();
            var maximumAccountedBytes = byteBudget.GetProperty("maximum").GetInt64();
            Assert.InRange(accountedBytes, 1, maximumAccountedBytes);
            var operationBudget = summary.GetProperty("operation_budget");
            Assert.Equal("wop1", operationBudget.GetProperty("model").GetString());
            var operationAccountedBytes = operationBudget.GetProperty("used").GetInt64();
            var operationMaximumAccountedBytes = operationBudget
                .GetProperty("maximum")
                .GetInt64();
            Assert.InRange(
                operationAccountedBytes,
                accountedBytes,
                operationMaximumAccountedBytes
            );
            Assert.Equal(1, summary.GetProperty("external_edge_count").GetInt32());
            var summaryItems = summary.GetProperty("items").EnumerateArray().ToArray();
            Assert.Contains(
                summaryItems,
                item =>
                    item.GetProperty("external_count").ValueKind == JsonValueKind.Number
                    && item.GetProperty("external_count").GetInt32() > 0
            );
            Assert.Contains(
                summaryItems,
                item => item.GetProperty("external_count").ValueKind == JsonValueKind.Null
            );
            Assert.True(
                summary.GetProperty("coverage")
                    .GetProperty("package_relationships")
                    .GetBoolean()
            );
            Assert.True(
                summary.GetProperty("coverage")
                    .GetProperty("active_content")
                    .GetBoolean()
            );
            Assert.True(
                summary.GetProperty("coverage")
                    .GetProperty("smartart_diagrams")
                    .GetBoolean()
            );
            Assert.True(
                summary.GetProperty("coverage")
                    .GetProperty("headings_and_outline")
                    .GetBoolean()
            );
            Assert.True(
                summary.GetProperty("source_diagnostics")
                    .TryGetProperty("headings_and_outline", out _)
            );
            Assert.DoesNotContain(
                "smartart_diagrams",
                summary.GetProperty("coverage")
                    .GetProperty("explicitly_unmodeled_domains")
                    .EnumerateArray()
                    .Select(value => value.GetString())
            );
            Assert.DoesNotContain("secret.example", summary.GetRawText());
            Assert.Equal(JsonValueKind.Null, summary.GetProperty("issues").ValueKind);
            Assert.True(
                summary.GetRawText().Length < 5_000,
                $"Default dependency response is too large: {summary.GetRawText().Length} characters"
            );
            Assert.Equal(0, host.InvocationCount);

            using var issueArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path, include_issues = true })
            );
            var issueObject = await service.CallAsync(
                "inspect_ooxml_dependencies",
                issueArguments.RootElement,
                CancellationToken.None
            );
            using var issueJson = JsonDocument.Parse(JsonSerializer.Serialize(issueObject));
            Assert.Equal(
                JsonValueKind.Array,
                issueJson.RootElement.GetProperty("issues").ValueKind
            );

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
    public async Task OperationBudgetFailureIsTypedBoundedAndNeverStartsWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-dependency-budget-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "budget.docx");
            CreatePackage(path);
            var host = new NoInvokeHost();
            var service = new WordLiveService(
                host,
                () => new WordOperationResourceLease(4_096)
            );
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path })
            );

            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_dependencies",
                    arguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("PACKAGE_LIMIT", exception.ErrorCode);
            Assert.Equal(
                "The dependency inspection exceeded its operation resource budget",
                exception.Message
            );
            Assert.False(exception.Retryable);
            var detailsText = JsonSerializer.Serialize(exception.Details);
            using var detailsJson = JsonDocument.Parse(detailsText);
            var budget = detailsJson.RootElement.GetProperty("operation_budget");
            Assert.Equal("wop1", budget.GetProperty("model").GetString());
            Assert.Equal(4_096, budget.GetProperty("used").GetInt64());
            Assert.Equal(4_096, budget.GetProperty("maximum").GetInt64());
            Assert.Equal("opc_package", budget.GetProperty("stage").GetString());
            Assert.True(budget.GetProperty("attempted").GetInt64() > 0);
            Assert.DoesNotContain(path, detailsText, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidChartPartIsClassifiedAsInvalidWordPackageWithoutWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-dependency-chart-errors",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "invalid-chart.docx");
            CreateInvalidChartPackage(path);
            var host = new NoInvokeHost();
            var service = new WordLiveService(host);
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path })
            );

            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_dependencies",
                    arguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("INVALID_WORD_PACKAGE", exception.ErrorCode);
            Assert.Equal(
                "A typed Word dependency source graph could not be resolved",
                exception.Message
            );
            Assert.False(exception.Retryable);
            Assert.DoesNotContain(path, JsonSerializer.Serialize(exception.Details));
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
        Assert.Contains(
            data.GetProperty("items").EnumerateArray(),
            item =>
                !item.TryGetProperty("unresolved_count", out _)
                && !item.TryGetProperty("external_count", out _)
        );
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

    private static void CreateInvalidChartPackage(string path)
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
              <Override PartName="/word/charts/chart1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.chart+xml"/>
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
              <w:body><w:p/><w:sectPr/></w:body>
            </w:document>
            """
        );
        AddEntry(
            archive,
            "word/charts/chart1.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <c:notChartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"/>
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
