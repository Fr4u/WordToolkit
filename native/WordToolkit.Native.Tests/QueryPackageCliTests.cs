using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class QueryPackageCliTests
{
    [Fact]
    public async Task EngineCliAndMcpReturnTheSameVersionedSemanticObjectsWithoutWord()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "parity.docx");
            CreatePackage(path);
            var before = SHA256.HashData(File.ReadAllBytes(path));
            var query = new WordSemanticQuery
            {
                Kinds = [WordSemanticNodeKind.Bookmark, WordSemanticNodeKind.Field],
                IncludeProperties = true,
                IncludeSource = true,
            };
            var engineResult = new QueryWordPackageOperation().Execute(
                new QueryWordPackageRequest(path, query)
            );
            var engineJson = WordToolkitOperationJson.Serialize(engineResult);
            var requestJson = JsonSerializer.Serialize(new
            {
                local_path = path,
                kinds = new[] { "bookmark", "field" },
                include_properties = true,
                include_source = true,
            });

            var cliOutput = new StringWriter();
            var cliError = new StringWriter();
            var cliExit = QueryPackageCli.Run(
                ["--request", "-", "--format", "json"],
                new StringReader(requestJson),
                cliOutput,
                cliError
            );
            var cliJson = JsonNode.Parse(cliOutput.ToString())!
                .ToJsonString(JsonDefaults.Compact);

            var host = new NoInvokeHost();
            var mcpResponse = await CallMcpAsync(host, JsonNode.Parse(requestJson)!.AsObject());
            var structured = mcpResponse
                .GetProperty("result")
                .GetProperty("structuredContent");
            var mcpJson = JsonNode.Parse(
                    structured.GetProperty("data").GetRawText()
                )!
                .ToJsonString(JsonDefaults.Compact);

            Assert.Equal(0, cliExit);
            Assert.Equal(string.Empty, cliError.ToString());
            Assert.Equal(engineJson, cliJson);
            Assert.Equal(engineJson, mcpJson);
            Assert.True(structured.GetProperty("ok").GetBoolean());
            var outputSchema = ToolCatalog
                .LoadNativeWordTools()
                .InspectAction("query_ooxml_semantics")["tool"]!["outputSchema"]!
                .AsObject();
            AssertConformsToPublishedOutputSchema(
                JsonNode.Parse(structured.GetRawText()),
                outputSchema,
                outputSchema,
                "$"
            );
            Assert.Equal(0, host.InvocationCount);
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.DoesNotContain("SecretAnchor", engineJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("redacted_property_names", engineJson, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LocalPathFingerprintConflictHasParityAcrossEveryAdapter()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "stale.docx");
            CreatePackage(path);
            var stale = new string('0', 64);
            var engineError = Assert.Throws<WordToolkitOperationException>(() =>
                new QueryWordPackageOperation().Execute(
                    new QueryWordPackageRequest(
                        path,
                        new WordSemanticQuery(),
                        stale
                    )
                )
            );
            var request = new JsonObject
            {
                ["local_path"] = path,
                ["expected_package_fingerprint"] = stale,
            };

            var cliOutput = new StringWriter();
            var cliError = new StringWriter();
            var cliExit = QueryPackageCli.Run(
                ["--request", "-", "--format", "json"],
                new StringReader(request.ToJsonString()),
                cliOutput,
                cliError
            );
            using var cliJson = JsonDocument.Parse(cliError.ToString());
            var host = new NoInvokeHost();
            var mcpResponse = await CallMcpAsync(host, request);
            var mcpError = mcpResponse
                .GetProperty("result")
                .GetProperty("structuredContent")
                .GetProperty("error");

            Assert.Equal("VERSION_CONFLICT", engineError.Code);
            Assert.Equal(75, cliExit);
            Assert.Equal(string.Empty, cliOutput.ToString());
            Assert.Equal(
                "VERSION_CONFLICT",
                cliJson.RootElement.GetProperty("error").GetProperty("code").GetString()
            );
            Assert.Equal("VERSION_CONFLICT", mcpError.GetProperty("code").GetString());
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task McpSensitivePropertiesRequireAnExplicitTwoFlagOptIn()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "sensitive.docx");
            CreatePackage(path);
            var host = new NoInvokeHost();
            var rejected = await CallMcpAsync(
                host,
                new JsonObject
                {
                    ["local_path"] = path,
                    ["kinds"] = new JsonArray("bookmark"),
                    ["include_sensitive_properties"] = true,
                }
            );
            Assert.Equal(
                "INVALID_INPUT",
                rejected
                    .GetProperty("result")
                    .GetProperty("structuredContent")
                    .GetProperty("error")
                    .GetProperty("code")
                    .GetString()
            );

            var accepted = await CallMcpAsync(
                host,
                new JsonObject
                {
                    ["local_path"] = path,
                    ["kinds"] = new JsonArray("bookmark"),
                    ["include_properties"] = true,
                    ["include_sensitive_properties"] = true,
                }
            );
            var data = accepted
                .GetProperty("result")
                .GetProperty("structuredContent")
                .GetProperty("data");
            var match = Assert.Single(data.GetProperty("matches").EnumerateArray());

            Assert.Equal(
                "SecretAnchor",
                match.GetProperty("properties").GetProperty("name").GetString()
            );
            Assert.True(
                data
                    .GetProperty("disclosure")
                    .GetProperty("sensitive_properties_returned")
                    .GetBoolean()
            );
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("{\"local_path\":\"sample.docx\",\"execute_magic\":true}")]
    [InlineData("{\"local_path\":\"sample.docx\",\"ancestor\":{\"execute_magic\":true}}")]
    [InlineData("{\"local_path\":\"sample.docx\",\"kinds\":[\"paragraph\",\"paragraph\"]}")]
    [InlineData("{\"local_path\":\"sample.docx\",\"kinds\":[\"Paragraph\"]}")]
    public void CliRejectsUnknownOrDuplicateJsonMembersAndKeepsStdoutClean(
        string requestJson
    )
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryPackageCli.Run(
            ["--request", "-", "--format", "json"],
            new StringReader(requestJson),
            output,
            error
        );

        Assert.Equal(64, exit);
        Assert.Equal(string.Empty, output.ToString());
        using var json = JsonDocument.Parse(error.ToString());
        Assert.Equal(
            "INVALID_INPUT",
            json.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task McpRejectsUnknownJsonMembersWithoutInvokingWord()
    {
        var host = new NoInvokeHost();
        var response = await CallMcpAsync(
            host,
            new JsonObject
            {
                ["local_path"] = "sample.docx",
                ["execute_magic"] = true,
            }
        );
        var result = response.GetProperty("result");

        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Equal(
            "INVALID_INPUT",
            result
                .GetProperty("structuredContent")
                .GetProperty("error")
                .GetProperty("code")
                .GetString()
        );
        Assert.Equal(0, host.InvocationCount);
    }

    [Fact]
    public void LazyActionPublishesClosedInputAndOutputContracts()
    {
        var action = ToolCatalog.LoadNativeWordTools().InspectAction(
            "query_ooxml_semantics"
        );
        var tool = action["tool"]!.AsObject();
        var input = tool["inputSchema"]!.AsObject();
        var output = tool["outputSchema"]!.AsObject();

        Assert.Equal("1.0", tool["operationVersion"]!.GetValue<string>());
        Assert.False(input["additionalProperties"]!.GetValue<bool>());
        Assert.NotNull(input["properties"]!["include_sensitive_properties"]);
        Assert.False(output["additionalProperties"]!.GetValue<bool>());
        Assert.Contains(
            output["required"]!.AsArray(),
            item => item!.GetValue<string>() == "data"
        );
        Assert.True(output["properties"]!["ok"]!["const"]!.GetValue<bool>());
        Assert.Equal(
            137,
            output["properties"]!["data"]!["properties"]!["candidate_seed"]![
                "maxLength"
            ]!.GetValue<int>()
        );
        Assert.Equal(
            "read_input_package",
            tool["permissions"]!["filesystem"]!.GetValue<string>()
        );
        Assert.False(tool["reversibility"]!["applicable"]!.GetValue<bool>());
    }

    private static async Task<JsonElement> CallMcpAsync(
        NoInvokeHost host,
        JsonObject arguments
    )
    {
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = "query_ooxml_semantics",
                ["arguments"] = arguments.DeepClone(),
            },
        };
        var output = new StringWriter();
        var server = new McpServer(
            new StringReader(request.ToJsonString(JsonDefaults.Compact) + Environment.NewLine),
            output,
            ToolCatalog.LoadNativeWordTools(),
            new WordLiveService(host)
        );
        await server.RunAsync();
        using var document = JsonDocument.Parse(output.ToString().Trim());
        return document.RootElement.Clone();
    }

    private static void AssertConformsToPublishedOutputSchema(
        JsonNode? actual,
        JsonObject schema,
        JsonObject rootSchema,
        string path
    )
    {
        if (schema["$ref"]?.GetValue<string>() is { } reference)
        {
            const string definitionPrefix = "#/$defs/";
            Assert.StartsWith(definitionPrefix, reference, StringComparison.Ordinal);
            var definitionName = reference[definitionPrefix.Length..];
            AssertConformsToPublishedOutputSchema(
                actual,
                rootSchema["$defs"]![definitionName]!.AsObject(),
                rootSchema,
                path
            );
            return;
        }

        if (schema["const"] is { } constant)
        {
            Assert.True(
                JsonNode.DeepEquals(actual, constant),
                $"{path} does not equal its published const value"
            );
        }
        if (schema["enum"] is JsonArray allowed)
        {
            Assert.Contains(
                allowed,
                candidate => JsonNode.DeepEquals(actual, candidate)
            );
        }

        switch (schema["type"]?.GetValue<string>())
        {
            case "object":
                {
                    var obj = Assert.IsType<JsonObject>(actual);
                    var declared = schema["properties"] as JsonObject ?? new JsonObject();
                    if (schema["required"] is JsonArray required)
                    {
                        foreach (var item in required)
                        {
                            var requiredName = item!.GetValue<string>();
                            Assert.True(
                                obj.ContainsKey(requiredName),
                                $"{path} is missing required property '{requiredName}'"
                            );
                        }
                    }
                    if (schema["maxProperties"]?.GetValue<int>() is { } maxProperties)
                    {
                        Assert.True(obj.Count <= maxProperties, $"{path} has too many properties");
                    }
                    foreach (var property in obj)
                    {
                        if (declared[property.Key] is JsonObject propertySchema)
                        {
                            AssertConformsToPublishedOutputSchema(
                                property.Value,
                                propertySchema,
                                rootSchema,
                                $"{path}.{property.Key}"
                            );
                            continue;
                        }
                        if (schema["additionalProperties"] is JsonObject additionalSchema)
                        {
                            AssertConformsToPublishedOutputSchema(
                                property.Value,
                                additionalSchema,
                                rootSchema,
                                $"{path}.{property.Key}"
                            );
                            continue;
                        }
                        Assert.False(
                            schema["additionalProperties"]?.GetValue<bool>() == false,
                            $"{path} contains undeclared property '{property.Key}'"
                        );
                    }
                    break;
                }
            case "array":
                {
                    var array = Assert.IsType<JsonArray>(actual);
                    if (schema["maxItems"]?.GetValue<int>() is { } maxItems)
                    {
                        Assert.True(array.Count <= maxItems, $"{path} has too many items");
                    }
                    if (schema["uniqueItems"]?.GetValue<bool>() == true)
                    {
                        Assert.Equal(
                            array.Count,
                            array.Select(item => item?.ToJsonString() ?? "null").Distinct().Count()
                        );
                    }
                    if (schema["items"] is JsonObject itemSchema)
                    {
                        for (var index = 0; index < array.Count; index++)
                        {
                            AssertConformsToPublishedOutputSchema(
                                array[index],
                                itemSchema,
                                rootSchema,
                                $"{path}[{index}]"
                            );
                        }
                    }
                    break;
                }
            case "string":
                {
                    var value = actual!.GetValue<string>();
                    if (schema["maxLength"]?.GetValue<int>() is { } maxLength)
                    {
                        Assert.True(value.Length <= maxLength, $"{path} is too long");
                    }
                    if (schema["pattern"]?.GetValue<string>() is { } pattern)
                    {
                        Assert.Matches(new Regex(pattern, RegexOptions.CultureInvariant), value);
                    }
                    break;
                }
            case "integer":
                {
                    var value = actual!.GetValue<long>();
                    if (schema["minimum"]?.GetValue<long>() is { } minimum)
                    {
                        Assert.True(value >= minimum, $"{path} is below its minimum");
                    }
                    if (schema["maximum"]?.GetValue<long>() is { } maximum)
                    {
                        Assert.True(value <= maximum, $"{path} exceeds its maximum");
                    }
                    break;
                }
            case "boolean":
                _ = actual!.GetValue<bool>();
                break;
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-query-cli-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CreatePackage(string path)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "[Content_Types].xml",
            """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
              <Default Extension="xml" ContentType="application/xml" />
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" />
            </Types>
            """
        );
        WriteEntry(
            archive,
            "_rels/.rels",
            """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml" />
            </Relationships>
            """
        );
        WriteEntry(
            archive,
            "word/document.xml",
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p>
                  <w:bookmarkStart w:id="9" w:name="SecretAnchor" />
                  <w:fldSimple w:instr=" REF SecretAnchor "><w:r><w:t>Visible result</w:t></w:r></w:fldSimple>
                  <w:bookmarkEnd w:id="9" />
                </w:p>
              </w:body>
            </w:document>
            """
        );
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var target = entry.Open();
        target.Write(Encoding.UTF8.GetBytes(content));
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
            throw new Xunit.Sdk.XunitException(
                "Saved-package semantic queries must not invoke the Word COM host."
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
