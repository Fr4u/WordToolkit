using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class LintPackageInspectionTests
{
    [Fact]
    public async Task LintSummaryIsCompactHonestAndNeverStartsWord()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "lint.docx");
            CreatePackage(path);
            var host = new NoInvokeHost();
            var service = new WordLiveService(host);
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path })
            );

            var result = await service.CallAsync(
                "lint_ooxml_document",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;

            Assert.Equal("summary", root.GetProperty("view").GetString());
            Assert.True(root.GetProperty("visible_finding_count").GetInt32() > 0);
            Assert.Equal(23, root.GetProperty("evaluated_rule_count").GetInt32());
            Assert.True(root.GetProperty("analysis_execution_complete").GetBoolean());
            Assert.False(root.GetProperty("document_coverage_complete").GetBoolean());
            Assert.False(root.GetProperty("report_complete").GetBoolean());
            Assert.NotEmpty(
                root.GetProperty("coverage")
                    .GetProperty("explicitly_unmodeled_domains")
                    .EnumerateArray()
            );
            Assert.False(root.GetProperty("word_opened").GetBoolean());
            Assert.False(root.GetProperty("document_modified").GetBoolean());
            Assert.False(root.GetProperty("external_targets_followed").GetBoolean());
            Assert.Equal(0, host.InvocationCount);
            Assert.DoesNotContain("secret.example", root.GetRawText());
            Assert.True(
                root.GetRawText().Length < 5_000,
                $"Default lint summary is too large: {root.GetRawText().Length} characters"
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SecurityFindingsArePagedSourceLinkedRedactedAndSuppressible()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "lint.docx");
            CreatePackage(path);
            var host = new NoInvokeHost();
            var service = new WordLiveService(host);
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "findings",
                    rule_pack = "security",
                    include_source = true,
                    include_fix = true,
                    max_items = 100,
                })
            );

            var result = await service.CallAsync(
                "lint_ooxml_document",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var findings = json.RootElement.GetProperty("items")
                .EnumerateArray()
                .ToArray();

            Assert.NotEmpty(findings);
            Assert.All(findings, finding =>
            {
                Assert.Equal("security", finding.GetProperty("rule_pack").GetString());
                Assert.StartsWith(
                    "wtlint_",
                    finding.GetProperty("finding_id").GetString(),
                    StringComparison.Ordinal
                );
                Assert.Equal(
                    JsonValueKind.Object,
                    finding.GetProperty("source").ValueKind
                );
                Assert.False(
                    finding.GetProperty("fix").GetProperty("implemented").GetBoolean()
                );
            });
            Assert.Contains(
                findings,
                finding => finding.GetProperty("rule_id").GetString()
                    == "WTL_SECURITY_EXTERNAL_RELATIONSHIP"
            );
            var external = findings.Single(finding =>
                finding.GetProperty("rule_id").GetString()
                    == "WTL_SECURITY_EXTERNAL_RELATIONSHIP"
            );
            Assert.True(
                external.GetProperty("source").GetProperty("byte_length").GetInt32() > 0
            );
            Assert.DoesNotContain("secret.example", json.RootElement.GetRawText());

            var findingId = findings[0].GetProperty("finding_id").GetString();
            using var suppressedArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "findings",
                    rule_pack = "security",
                    suppress_finding_ids = new[] { findingId },
                    max_items = 100,
                })
            );
            var suppressed = await service.CallAsync(
                "lint_ooxml_document",
                suppressedArguments.RootElement,
                CancellationToken.None
            );
            using var suppressedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(suppressed)
            );
            Assert.True(
                suppressedJson.RootElement
                    .GetProperty("suppressed_finding_count")
                    .GetInt32() >= 1
            );
            Assert.DoesNotContain(
                suppressedJson.RootElement.GetProperty("items").EnumerateArray(),
                finding => finding.GetProperty("finding_id").GetString() == findingId
            );
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RuleCatalogIsFilterableAndInvalidSuppressionsAreRejected()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "lint.docx");
            CreatePackage(path);
            var service = new WordLiveService(new NoInvokeHost());
            using var ruleArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "rules",
                    rule_pack = "accessibility",
                    offset = 1,
                    max_items = 2,
                })
            );

            var result = await service.CallAsync(
                "lint_ooxml_document",
                ruleArguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.Equal(2, json.RootElement.GetProperty("returned_item_count").GetInt32());
            Assert.All(
                json.RootElement.GetProperty("items").EnumerateArray(),
                item => Assert.Equal(
                    "accessibility",
                    item.GetProperty("rule_pack").GetString()
                )
            );

            using var invalidArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    suppress_rule_ids = new[] { "WTL_DOES_NOT_EXIST" },
                })
            );
            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "lint_ooxml_document",
                    invalidArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_INPUT", exception.ErrorCode);

            using var unknownRuleArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "findings",
                    rule_id = "WTL_DOES_NOT_EXIST",
                })
            );
            var unknownRule = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "lint_ooxml_document",
                    unknownRuleArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_INPUT", unknownRule.ErrorCode);

            using var categoryArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "findings",
                    category = "accessibility",
                    max_items = 100,
                })
            );
            var categoryResult = await service.CallAsync(
                "lint_ooxml_document",
                categoryArguments.RootElement,
                CancellationToken.None
            );
            using var categoryJson = JsonDocument.Parse(
                JsonSerializer.Serialize(categoryResult)
            );
            var categoryRoot = categoryJson.RootElement;
            Assert.Equal(
                categoryRoot.GetProperty("matched_finding_count").GetInt32(),
                categoryRoot.GetProperty("category_counts")
                    .GetProperty("accessibility")
                    .GetInt32()
            );
            Assert.All(
                categoryRoot.GetProperty("items").EnumerateArray(),
                item => Assert.Equal(
                    "accessibility",
                    item.GetProperty("category").GetString()
                )
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LintGatewayKeepsCompleteJsonRpcEnvelopeBoundedWithoutWord()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "lint.docx");
            CreatePackage(path);
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
                        action = "lint_ooxml_document",
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
            var data = result.GetProperty("structuredContent").GetProperty("data");
            Assert.Equal("summary", data.GetProperty("view").GetString());
            Assert.False(data.GetProperty("word_opened").GetBoolean());
            Assert.True(
                responseLine.Length < 10_000,
                $"Complete lint JSON-RPC response is too large: {responseLine.Length} characters"
            );
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-lint-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        return directory;
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
              <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
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
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
            </Relationships>
            """
        );
        AddEntry(
            archive,
            "docProps/core.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title/></cp:coreProperties>
            """
        );
        AddEntry(
            archive,
            "word/document.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <w:body>
                <w:p><w:pPr><w:pStyle w:val="Heading2"/><w:jc w:val="center"/></w:pPr><w:r><w:rPr><w:b/><w:vanish/></w:rPr><w:t>Hidden heading</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading4"/></w:pPr><w:r><w:t>Skipped heading</w:t></w:r></w:p>
                <w:tbl><w:tblPr/><w:tr><w:tc><w:p><w:r><w:t>Column</w:t></w:r></w:p></w:tc></w:tr><w:tr><w:tc><w:p><w:r><w:t>Value</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
                <w:p><w:r><w:drawing><wp:inline><wp:docPr id="1" name="Figure"/><a:graphic/></wp:inline></w:drawing></w:r></w:p>
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
              <w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="Heading 2"/><w:basedOn w:val="Normal"/><w:pPr><w:outlineLvl w:val="1"/></w:pPr></w:style>
              <w:style w:type="paragraph" w:styleId="Heading4"><w:name w:val="Heading 4"/><w:basedOn w:val="Normal"/><w:pPr><w:outlineLvl w:val="3"/></w:pPr></w:style>
              <w:style w:type="paragraph" w:customStyle="1" w:styleId="Unused"><w:name w:val="Unused"/></w:style>
            </w:styles>
            """
        );
        AddEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
              <Relationship Id="rIdExternal" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://secret.example/private" TargetMode="External"/>
            </Relationships>
            """
        );
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var output = entry.Open();
        output.Write(Encoding.UTF8.GetBytes(content));
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
