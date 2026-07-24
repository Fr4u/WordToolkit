using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Tests;

public sealed class CapabilityManifestTests
{
    [Fact]
    public void ManifestRetainsCanonicalHeaderAndHasDeterministicDigests()
    {
        var firstCatalog = ToolCatalog.LoadNativeWordTools();
        var secondCatalog = ToolCatalog.LoadNativeWordTools();
        var first = firstCatalog.GetCapabilities(null, 0, 12);
        var second = secondCatalog.GetCapabilities(null, 0, 12);

        Assert.Equal("1.0.0", firstCatalog.SchemaVersion);
        Assert.Equal("2025-06-18", firstCatalog.McpProtocolVersion);
        Assert.Equal("local_stdio", firstCatalog.Transport);
        Assert.Contains("Additive changes within v1", firstCatalog.CompatibilityPolicy);
        Assert.Equal(64, firstCatalog.SourceSchemaSha256.Length);
        Assert.Equal(64, firstCatalog.CapabilitySchemaSha256.Length);
        Assert.Equal(
            first["source"]!["schema_sha256"]!.GetValue<string>(),
            second["source"]!["schema_sha256"]!.GetValue<string>()
        );
        Assert.Equal(
            first["source"]!["native_action_contract_sha256"]!.GetValue<string>(),
            second["source"]!["native_action_contract_sha256"]!.GetValue<string>()
        );
        Assert.Matches(
            new Regex(
                "^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$",
                RegexOptions.CultureInvariant
            ),
            first["toolkit_version"]!.GetValue<string>()
        );
    }

    [Fact]
    public void ManifestIsPagedTokenLeanHonestAndOperationSpecific()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        var manifest = catalog.GetCapabilities(null, 0, 12);

        Assert.Equal("wordtoolkit.capabilities/1.0", manifest["contract_schema"]!.GetValue<string>());
        Assert.Equal("1.0.0", manifest["contract_schema_version"]!.GetValue<string>());
        Assert.Equal(94, manifest["operation_count"]!.GetValue<int>());
        Assert.Equal(15, manifest["exposed_mcp_tool_count"]!.GetValue<int>());
        Assert.Equal(12, manifest["operations"]!.AsArray().Count);
        Assert.Equal(12, manifest["paging"]!["next_offset"]!.GetValue<int>());
        Assert.Equal(94, manifest["metadata_coverage"]!["input_schema"]!.GetValue<int>());
        Assert.Equal(94, manifest["metadata_coverage"]!["mcp_effect_annotations"]!.GetValue<int>());
        Assert.Equal(7, manifest["metadata_coverage"]!["explicit_output_schema"]!.GetValue<int>());
        Assert.Equal(7, manifest["metadata_coverage"]!["explicit_permissions"]!.GetValue<int>());
        Assert.Equal(7, manifest["metadata_coverage"]!["explicit_reversibility"]!.GetValue<int>());
        Assert.Equal(7, manifest["metadata_coverage"]!["explicit_operation_version"]!.GetValue<int>());
        Assert.Equal(
            "operation-specific",
            manifest["format_support"]!["scope"]!.GetValue<string>()
        );
        Assert.False(manifest["security"]!["opens_word"]!.GetValue<bool>());
        Assert.False(manifest["security"]!["reads_document"]!.GetValue<bool>());
        Assert.False(manifest["security"]!["returns_document_content"]!.GetValue<bool>());
        Assert.False(manifest["security"]!["external_network"]!.GetValue<bool>());
        Assert.True(
            manifest.ToJsonString(JsonDefaults.Compact).Length < 10_000,
            $"Default capability page is too large: {manifest.ToJsonString(JsonDefaults.Compact).Length} characters"
        );

        var names = manifest["operations"]!
            .AsArray()
            .Select(item => item!["name"]!.GetValue<string>())
            .ToArray();
        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        Assert.All(
            manifest["operations"]!.AsArray(),
            item => Assert.Equal(64, item!["input_schema_sha256"]!.GetValue<string>().Length)
        );
    }

    [Fact]
    public void FilteredManifestRoundTripsWithoutLosingContractData()
    {
        var manifest = ToolCatalog
            .LoadNativeWordTools()
            .GetCapabilities("inspect_ooxml_equations", 0, 8);
        var serialized = manifest.ToJsonString(JsonDefaults.Compact);
        var roundTripped = JsonNode.Parse(serialized)!.ToJsonString(JsonDefaults.Compact);

        Assert.Equal(serialized, roundTripped);
        Assert.Single(manifest["operations"]!.AsArray());
        Assert.Equal(
            "inspect_ooxml_equations",
            manifest["operations"]![0]!["name"]!.GetValue<string>()
        );
    }

    [Fact]
    public void CapabilityV1OperationSummaryKeepsItsClosedBackwardCompatibleShape()
    {
        var manifest = ToolCatalog
            .LoadNativeWordTools()
            .GetCapabilities("query_ooxml_semantics", 0, 1);
        var operation = Assert.Single(manifest["operations"]!.AsArray())!.AsObject();

        Assert.Equal(
            ["description", "effects", "exposure", "input_schema_sha256", "name"],
            operation.Select(property => property.Key).Order(StringComparer.Ordinal)
        );

        var inspected = ToolCatalog
            .LoadNativeWordTools()
            .InspectAction("query_ooxml_semantics")["tool"]!
            .AsObject();
        Assert.Equal("1.0", inspected["operationVersion"]!.GetValue<string>());
        Assert.NotNull(inspected["outputSchema"]);
        Assert.NotNull(inspected["permissions"]);
        Assert.NotNull(inspected["reversibility"]);
    }

    [Fact]
    public void ManifestRejectsUnboundedMalformedAndUnknownInput()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        using var tooLong = JsonDocument.Parse(
            JsonSerializer.Serialize(new { query = new string('x', 129) })
        );
        using var badType = JsonDocument.Parse("""{"limit":"12"}""");
        using var unknown = JsonDocument.Parse("""{"document_path":"secret.docx"}""");
        using var mixedSchemaView = JsonDocument.Parse(
            """{"view":"schema","limit":1}"""
        );

        Assert.Equal(
            "INVALID_INPUT",
            Assert.Throws<NativeToolException>(() => catalog.GetCapabilities(tooLong.RootElement)).ErrorCode
        );
        Assert.Equal(
            "INVALID_INPUT",
            Assert.Throws<NativeToolException>(() => catalog.GetCapabilities(badType.RootElement)).ErrorCode
        );
        Assert.Equal(
            "INVALID_INPUT",
            Assert.Throws<NativeToolException>(() => catalog.GetCapabilities(unknown.RootElement)).ErrorCode
        );
        Assert.Equal(
            "INVALID_INPUT",
            Assert.Throws<NativeToolException>(() => catalog.GetCapabilities(mixedSchemaView.RootElement)).ErrorCode
        );
        Assert.Throws<NativeToolException>(() => catalog.GetCapabilities(null, -1, 12));
        Assert.Throws<NativeToolException>(() => catalog.GetCapabilities(null, 0, 33));
    }

    [Fact]
    public void CliReturnsTheSameContractAndStableUsageExitWithoutStartingWord()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CapabilityCli.Run(
            ["--query", "table", "--limit", "3", "--format", "json"],
            output,
            error
        );

        Assert.Equal(0, exitCode);
        Assert.Equal("", error.ToString());
        using var cli = JsonDocument.Parse(output.ToString());
        var direct = ToolCatalog.LoadNativeWordTools().GetCapabilities("table", 0, 3);
        var canonicalCli = JsonNode
            .Parse(cli.RootElement.GetRawText())!
            .ToJsonString(JsonDefaults.Compact);
        Assert.Equal(
            direct.ToJsonString(JsonDefaults.Compact),
            canonicalCli
        );

        output.GetStringBuilder().Clear();
        error.GetStringBuilder().Clear();
        exitCode = CapabilityCli.Run(["--format", "xml"], output, error);
        Assert.Equal(64, exitCode);
        Assert.Equal("", output.ToString());
        Assert.Contains("usage:", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaViewReturnsTheExactEmbeddedNormativeSchemaAndHash()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        var result = catalog.GetCapabilitySchema();
        var expected = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "schemas",
                "wordtoolkit-capabilities.v1.schema.json"
            )
        );
        var schemaJson = result["schema_json"]!.GetValue<string>();
        var sha256 = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(schemaJson))
            )
            .ToLowerInvariant();

        Assert.Equal("application/schema+json", result["media_type"]!.GetValue<string>());
        Assert.Equal(expected, schemaJson);
        Assert.Equal(result["schema_sha256"]!.GetValue<string>(), sha256);
        using var schema = JsonDocument.Parse(schemaJson);
        Assert.Equal(
            "https://json-schema.org/draft/2020-12/schema",
            schema.RootElement.GetProperty("$schema").GetString()
        );

        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(0, CapabilityCli.Run(["--schema"], output, error));
        Assert.Equal("", error.ToString());
        using var cli = JsonDocument.Parse(output.ToString());
        Assert.Equal(schemaJson, cli.RootElement.GetProperty("schema_json").GetString());
        Assert.Equal(sha256, cli.RootElement.GetProperty("schema_sha256").GetString());
    }

    [Fact]
    public void CompactionPreservesRealPropertiesNamedTitleAndHashesTheExactSchema()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        var inspected = catalog.InspectAction("insert_live_word_image");
        var inputSchema = inspected["tool"]!["inputSchema"]!.AsObject();
        var properties = inputSchema["properties"]!.AsObject();
        Assert.True(properties.ContainsKey("title"));
        Assert.True(properties.ContainsKey("alternative_text"));

        var manifest = catalog.GetCapabilities("insert_live_word_image", 0, 1);
        var expectedHash = Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        inputSchema.ToJsonString(JsonDefaults.Compact)
                    )
                )
            )
            .ToLowerInvariant();
        Assert.Equal(
            expectedHash,
            manifest["operations"]![0]!["input_schema_sha256"]!.GetValue<string>()
        );
    }

    [Fact]
    public void ContractLoaderRejectsRegistrySchemaAndAnnotationDrift()
    {
        var repositoryRoot = FindRepositoryRoot();
        var schemaJson = File.ReadAllText(
            Path.Combine(repositoryRoot, "schemas", "mcp-tools-local.v1.json")
        );
        var capabilitySchemaJson = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "schemas",
                "wordtoolkit-capabilities.v1.schema.json"
            )
        );

        var duplicateRegistry = JsonNode.Parse(schemaJson)!.AsObject();
        var actions = duplicateRegistry["native_runtime"]!["actions"]!.AsArray();
        actions.Add(actions[0]!.DeepClone());
        Assert.Throws<InvalidOperationException>(() =>
            ToolCatalog.LoadNativeWordTools(
                duplicateRegistry.ToJsonString(),
                capabilitySchemaJson
            )
        );

        var unknownCore = JsonNode.Parse(schemaJson)!.AsObject();
        unknownCore["native_runtime"]!["core_actions"]!.AsArray().Add("not_an_action");
        Assert.Throws<InvalidOperationException>(() =>
            ToolCatalog.LoadNativeWordTools(
                unknownCore.ToJsonString(),
                capabilitySchemaJson
            )
        );

        var duplicateTool = JsonNode.Parse(schemaJson)!.AsObject();
        var toolArray = duplicateTool["tools"]!.AsArray();
        var nativeTool = toolArray.Single(node =>
            node!["name"]!.GetValue<string>() == "insert_live_word_image"
        );
        toolArray.Add(nativeTool!.DeepClone());
        Assert.Throws<InvalidOperationException>(() =>
            ToolCatalog.LoadNativeWordTools(
                duplicateTool.ToJsonString(),
                capabilitySchemaJson
            )
        );

        var missingAnnotation = JsonNode.Parse(schemaJson)!.AsObject();
        var affectedTool = missingAnnotation["tools"]!
            .AsArray()
            .Single(node =>
                node!["name"]!.GetValue<string>() == "insert_live_word_image"
            );
        affectedTool!["annotations"]!.AsObject().Remove("readOnlyHint");
        var catalog = ToolCatalog.LoadNativeWordTools(
            missingAnnotation.ToJsonString(),
            capabilitySchemaJson
        );
        Assert.Throws<InvalidOperationException>(() =>
            catalog.GetCapabilities("insert_live_word_image", 0, 1)
        );
    }

    [Fact]
    public async Task McpCapabilityGatewayNeverInvokesTheDocumentHandler()
    {
        const string input =
            """{"jsonrpc":"2.0","id":41,"method":"tools/call","params":{"name":"get_wordtoolkit_capabilities","arguments":{"query":"patch","limit":4}}}"""
            + "\n";
        var output = new StringWriter();
        var server = new McpServer(
            new StringReader(input),
            output,
            ToolCatalog.LoadNativeWordTools(),
            new RejectingHandler()
        );

        await server.RunAsync();

        using var response = JsonDocument.Parse(output.ToString());
        var result = response.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent");
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal(
            "wordtoolkit.capabilities/1.0",
            result.GetProperty("data").GetProperty("contract_schema").GetString()
        );
    }

    [Fact]
    public async Task McpSchemaViewReturnsVerifiableBytesWithoutDocumentHandler()
    {
        const string input =
            """{"jsonrpc":"2.0","id":42,"method":"tools/call","params":{"name":"get_wordtoolkit_capabilities","arguments":{"view":"schema"}}}"""
            + "\n";
        var output = new StringWriter();
        var server = new McpServer(
            new StringReader(input),
            output,
            ToolCatalog.LoadNativeWordTools(),
            new RejectingHandler()
        );

        await server.RunAsync();

        using var response = JsonDocument.Parse(output.ToString());
        var data = response.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent")
            .GetProperty("data");
        var schemaJson = data.GetProperty("schema_json").GetString()!;
        var sha256 = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(schemaJson))
            )
            .ToLowerInvariant();
        Assert.Equal(data.GetProperty("schema_sha256").GetString(), sha256);
    }

    [Fact]
    public void CheckedInJsonSchemaCoversEveryTopLevelManifestField()
    {
        var schemaPath = Path.Combine(
            FindRepositoryRoot(),
            "schemas",
            "wordtoolkit-capabilities.v1.schema.json"
        );
        using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var manifest = ToolCatalog.LoadNativeWordTools().GetCapabilities(null, 0, 1);
        var root = schema.RootElement;
        var properties = root.GetProperty("properties");
        var required = root.GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(required, manifest.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal));
        Assert.All(manifest, pair => Assert.True(properties.TryGetProperty(pair.Key, out _)));
    }

    [Fact]
    public void PublicCapabilityDocumentationUsesCurrentActionCount()
    {
        var repositoryRoot = FindRepositoryRoot();
        var count = ToolCatalog.LoadNativeWordTools()
            .GetCapabilities(null, 0, 1)["operation_count"]!
            .GetValue<int>();
        var readme = File.ReadAllText(Path.Combine(repositoryRoot, "README.md"));
        var architecture = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "docs",
            "DOCUMENT-ENGINE-ARCHITECTURE.md"
        ));
        var audit = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "docs",
            "DOCUMENT-ENGINE-GOAL-AUDIT.md"
        ));
        var interoperability = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "docs",
            "AI-INTEROPERABILITY.md"
        ));

        Assert.Contains($"{count}-action schema set", readme);
        Assert.Contains($"all {count} actions", architecture);
        Assert.Contains($"for {count} actions", audit);
        Assert.Contains($"remaining {count - 7} actions", audit);
        Assert.Contains($"native {count}-action subset", interoperability);
        Assert.Contains($"all {count} schemas", interoperability);
        Assert.Contains($"remaining {count - 7} are still uncovered", interoperability);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "global.json")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("Repository root was not found");
    }

    private sealed class RejectingHandler : IToolHandler
    {
        public Task<object> CallAsync(
            string name,
            JsonElement arguments,
            CancellationToken cancellationToken
        )
        {
            throw new InvalidOperationException("Document handler must not be called");
        }
    }
}
