using System.Text.Json;
using System.Text;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Tests;

public sealed class McpServerTests
{
    [Fact]
    public async Task InitializeListAndCallUseValidLineDelimitedJsonRpc()
    {
        var input = string.Join(
            "\n",
            """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
            """,
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""",
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_live_word_documents","arguments":{}}}"""
        ) + "\n";
        var output = new StringWriter();
        var server = new McpServer(
            new StringReader(input),
            output,
            ToolCatalog.LoadNativeWordTools(),
            new FakeToolHandler()
        );

        await server.RunAsync();

        var responses = output
            .ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line))
            .ToArray();
        Assert.Equal(3, responses.Length);
        Assert.Equal(
            "WordToolkit Native",
            responses[0].RootElement
                .GetProperty("result")
                .GetProperty("serverInfo")
                .GetProperty("name")
                .GetString()
        );
        Assert.Equal(
            "0.35.0",
            responses[0].RootElement
                .GetProperty("result")
                .GetProperty("serverInfo")
                .GetProperty("version")
                .GetString()
        );
        var tools = responses[1].RootElement
            .GetProperty("result")
            .GetProperty("tools");
        Assert.Equal(14, tools.GetArrayLength());
        Assert.Contains(
            tools.EnumerateArray(),
            tool => tool.GetProperty("name").GetString() == "apply_live_word_operations"
        );
        Assert.Contains(
            tools.EnumerateArray(),
            tool => tool.GetProperty("name").GetString() == "inspect_ooxml_package"
        );
        Assert.Contains(
            tools.EnumerateArray(),
            tool => tool.GetProperty("name").GetString() == "search_wordtoolkit_actions"
        );
        Assert.Contains(
            tools.EnumerateArray(),
            tool => tool.GetProperty("name").GetString() == "inspect_wordtoolkit_action"
        );
        Assert.Contains(
            tools.EnumerateArray(),
            tool => tool.GetProperty("name").GetString() == "execute_wordtoolkit_action"
        );
        foreach (
            var required in new[]
            {
                "start_word_application",
                "open_live_word_document",
                "create_live_word_document",
                "connect_live_word_document",
                "inspect_live_word_document",
                "get_live_word_selection",
                "save_live_word_document",
                "disconnect_live_word_document",
            }
        )
        {
            Assert.Contains(
                tools.EnumerateArray(),
                tool => tool.GetProperty("name").GetString() == required
            );
        }
        Assert.DoesNotContain(
            tools.EnumerateArray(),
            tool => tool.GetProperty("name").GetString() == "insert_live_word_image"
        );
        foreach (
            var lazyAction in new[]
            {
                "query_ooxml_semantics",
                "manage_ooxml_semantic_index",
                "compare_ooxml_semantics",
                "plan_ooxml_patch",
                "create_ooxml_patch",
                "inspect_ooxml_patch",
                "plan_ooxml_patch_apply",
                "apply_ooxml_patch",
                "plan_ooxml_merge",
                "apply_ooxml_merge",
                "inspect_ooxml_sections",
                "inspect_ooxml_styles",
                "inspect_ooxml_numbering",
                "inspect_ooxml_theme",
                "inspect_ooxml_references",
                "inspect_ooxml_dependencies",
                "inspect_ooxml_charts",
                "inspect_ooxml_markup_compatibility",
                "lint_ooxml_document",
                "plan_ooxml_lint_repair",
                "apply_ooxml_lint_repair",
                "inspect_ooxml_equations",
                "inspect_ooxml_review",
                "resolve_ooxml_formatting",
                "plan_ooxml_text_edits",
                "apply_ooxml_text_edits",
                "plan_ooxml_semantic_edits",
                "apply_ooxml_semantic_edits",
                "plan_ooxml_review_decisions",
                "apply_ooxml_review_decisions",
            }
        )
        {
            Assert.DoesNotContain(
                tools.EnumerateArray(),
                tool => tool.GetProperty("name").GetString() == lazyAction
            );
        }
        Assert.True(
            tools.GetRawText().Length < 10_000,
            $"Core catalog is too large: {tools.GetRawText().Length} characters"
        );
        var toolResult = responses[2].RootElement.GetProperty("result");
        Assert.False(toolResult.GetProperty("isError").GetBoolean());
        Assert.True(
            toolResult
                .GetProperty("structuredContent")
                .GetProperty("ok")
                .GetBoolean()
        );
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task LazyActionsPreserveAllCapabilitiesAndCompactLargeResponses()
    {
        var input = string.Join(
            "\n",
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"search_wordtoolkit_actions","arguments":{"query":"image"}}}""",
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"inspect_wordtoolkit_action","arguments":{"action":"insert_live_word_image"}}}""",
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"execute_wordtoolkit_action","arguments":{"action":"apply_live_word_operations","arguments":{"live_document_id":"live_1","operations":[{"type":"text","text":"x"}]}}}}""",
            """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"execute_wordtoolkit_action","arguments":{"action":"apply_live_word_operations","arguments":{"live_document_id":"live_1","operations":[{"type":"text","text":"x"}]},"response_mode":"full"}}}""",
            """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"execute_wordtoolkit_action","arguments":{"action":"preflight_live_word_equations","arguments":{"equations":[{"value":"x"}]}}}}""",
            """{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"execute_wordtoolkit_action","arguments":{"action":"preflight_live_word_equations","arguments":{"equations":[{"value":"x"}]},"response_mode":"full"}}}"""
        ) + "\n";
        var output = new StringWriter();
        var catalog = ToolCatalog.LoadNativeWordTools();
        Assert.Equal(82, catalog.ActionCount);
        var server = new McpServer(
            new StringReader(input),
            output,
            catalog,
            new FakeToolHandler()
        );

        await server.RunAsync();

        var responses = output
            .ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line))
            .ToArray();
        Assert.Equal(6, responses.Length);

        var searched = responses[0].RootElement
            .GetProperty("result")
            .GetProperty("structuredContent")
            .GetProperty("data");
        Assert.Contains(
            searched.GetProperty("actions").EnumerateArray(),
            action => action.GetProperty("action").GetString() == "insert_live_word_image"
        );

        var inspected = responses[1].RootElement
            .GetProperty("result")
            .GetProperty("structuredContent")
            .GetProperty("data");
        Assert.Equal("insert_live_word_image", inspected.GetProperty("action").GetString());
        Assert.Equal(
            "insert_live_word_image",
            inspected.GetProperty("tool").GetProperty("name").GetString()
        );

        var compact = responses[2].RootElement
            .GetProperty("result")
            .GetProperty("structuredContent")
            .GetProperty("data");
        Assert.True(compact.GetProperty("native_verified").GetBoolean());
        Assert.False(compact.TryGetProperty("operations", out _));
        Assert.False(compact.TryGetProperty("performance", out _));
        Assert.True(compact.GetRawText().Length < 500);

        var full = responses[3].RootElement
            .GetProperty("result")
            .GetProperty("structuredContent")
            .GetProperty("data");
        Assert.True(full.TryGetProperty("operations", out _));
        Assert.True(full.TryGetProperty("performance", out _));
        Assert.True(
            full.GetRawText().Length > compact.GetRawText().Length * 50,
            "Compact mode should remove at least 98% of a large echoed batch response"
        );

        var compactPreflight = responses[4].RootElement
            .GetProperty("result")
            .GetProperty("structuredContent")
            .GetProperty("data");
        var compactEquation = compactPreflight
            .GetProperty("equations")
            .EnumerateArray()
            .Single();
        Assert.False(compactEquation.TryGetProperty("word_linear", out _));
        Assert.Equal(
            20_000,
            compactEquation.GetProperty("word_linear_characters").GetInt32()
        );
        Assert.Equal(
            16,
            compactEquation.GetProperty("word_linear_sha256").GetString()!.Length
        );
        Assert.False(compactPreflight.GetProperty("word_linear_returned").GetBoolean());
        Assert.True(compactPreflight.GetRawText().Length < 600);

        var fullPreflight = responses[5].RootElement
            .GetProperty("result")
            .GetProperty("structuredContent")
            .GetProperty("data");
        Assert.Equal(
            20_000,
            fullPreflight
                .GetProperty("equations")[0]
                .GetProperty("word_linear")
                .GetString()!
                .Length
        );
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public void SemanticEditActionsStayTypedBoundedLazyAndTokenLean()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        using var plan = JsonDocument.Parse(
            catalog.InspectAction("plan_ooxml_semantic_edits").ToJsonString()
        );
        using var apply = JsonDocument.Parse(
            catalog.InspectAction("apply_ooxml_semantic_edits").ToJsonString()
        );

        var planTool = plan.RootElement.GetProperty("tool");
        var planSchema = planTool.GetProperty("inputSchema");
        var planCommands = planSchema.GetProperty("properties").GetProperty("commands");
        Assert.Equal(200, planCommands.GetProperty("maxItems").GetInt32());
        var planDefinitions = planSchema.GetProperty("$defs");
        Assert.Equal(
            "set_style",
            planDefinitions
                .GetProperty("exact_style")
                .GetProperty("properties")
                .GetProperty("type")
                .GetProperty("const")
                .GetString()
        );
        Assert.Equal(
            "set_style_where",
            planDefinitions
                .GetProperty("selected_style")
                .GetProperty("properties")
                .GetProperty("type")
                .GetProperty("const")
                .GetString()
        );
        Assert.Equal(
            "create_style",
            planDefinitions
                .GetProperty("create_style")
                .GetProperty("properties")
                .GetProperty("type")
                .GetProperty("const")
                .GetString()
        );
        Assert.Equal(
            ["paragraph", "character", "table", "numbering"],
            planDefinitions
                .GetProperty("create_style")
                .GetProperty("properties")
                .GetProperty("style_type")
                .GetProperty("enum")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray()
        );
        Assert.Equal(
            "clone_style",
            planDefinitions
                .GetProperty("clone_style")
                .GetProperty("properties")
                .GetProperty("type")
                .GetProperty("const")
                .GetString()
        );
        Assert.Equal(
            "consolidate_style",
            planDefinitions
                .GetProperty("consolidate_style")
                .GetProperty("properties")
                .GetProperty("type")
                .GetProperty("const")
                .GetString()
        );
        Assert.Equal(
            ["type", "source_style_id", "target_style_id"],
            planDefinitions
                .GetProperty("consolidate_style")
                .GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray()
        );
        Assert.Equal(
            "delete_unused_style",
            planDefinitions
                .GetProperty("delete_unused_style")
                .GetProperty("properties")
                .GetProperty("type")
                .GetProperty("const")
                .GetString()
        );
        Assert.Equal(
            ["type", "style_id"],
            planDefinitions
                .GetProperty("delete_unused_style")
                .GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray()
        );
        Assert.Equal(
            "rename_style",
            planDefinitions
                .GetProperty("rename_style")
                .GetProperty("properties")
                .GetProperty("type")
                .GetProperty("const")
                .GetString()
        );
        Assert.Equal(
            ["type", "style_id", "name"],
            planDefinitions
                .GetProperty("rename_style")
                .GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray()
        );
        Assert.Equal(
            200,
            planDefinitions
                .GetProperty("selected_style")
                .GetProperty("properties")
                .GetProperty("max_matches")
                .GetProperty("maximum")
                .GetInt32()
        );
        Assert.Equal(
            ["paragraph", "run", "table"],
            planDefinitions
                .GetProperty("selector")
                .GetProperty("properties")
                .GetProperty("kind")
                .GetProperty("enum")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray()
        );
        Assert.Equal(
            [
                "#/$defs/create_style",
                "#/$defs/clone_style",
                "#/$defs/consolidate_style",
                "#/$defs/delete_unused_style",
                "#/$defs/rename_style",
                "#/$defs/exact_style",
                "#/$defs/selected_style",
            ],
            planCommands
                .GetProperty("items")
                .GetProperty("oneOf")
                .EnumerateArray()
                .Select(item => item.GetProperty("$ref").GetString()!)
                .ToArray()
        );
        Assert.True(
            planTool
                .GetProperty("annotations")
                .GetProperty("readOnlyHint")
                .GetBoolean()
        );
        Assert.False(
            planTool
                .GetProperty("annotations")
                .GetProperty("destructiveHint")
                .GetBoolean()
        );

        var applyTool = apply.RootElement.GetProperty("tool");
        var applySchema = applyTool.GetProperty("inputSchema");
        Assert.Equal(
            "^wseplan_[A-Za-z0-9_-]+$",
            applySchema
                .GetProperty("properties")
                .GetProperty("expected_plan_id")
                .GetProperty("pattern")
                .GetString()
        );
        Assert.Equal(
            "consolidate_style",
            applySchema
                .GetProperty("$defs")
                .GetProperty("consolidate_style")
                .GetProperty("properties")
                .GetProperty("type")
                .GetProperty("const")
                .GetString()
        );
        Assert.Equal(
            "rename_style",
            applySchema
                .GetProperty("$defs")
                .GetProperty("rename_style")
                .GetProperty("properties")
                .GetProperty("type")
                .GetProperty("const")
                .GetString()
        );
        Assert.Equal(
            planCommands
                .GetProperty("items")
                .GetProperty("oneOf")
                .EnumerateArray()
                .Select(item => item.GetProperty("$ref").GetString()!)
                .ToArray(),
            applySchema
                .GetProperty("properties")
                .GetProperty("commands")
                .GetProperty("items")
                .GetProperty("oneOf")
                .EnumerateArray()
                .Select(item => item.GetProperty("$ref").GetString()!)
                .ToArray()
        );
        Assert.False(
            applyTool
                .GetProperty("annotations")
                .GetProperty("readOnlyHint")
                .GetBoolean()
        );
        Assert.True(
            applyTool
                .GetProperty("annotations")
                .GetProperty("destructiveHint")
                .GetBoolean()
        );
        Assert.DoesNotContain(
            catalog.Tools,
            node => node?["name"]?.GetValue<string>() == "plan_ooxml_semantic_edits"
        );
        Assert.DoesNotContain(
            catalog.Tools,
            node => node?["name"]?.GetValue<string>() == "apply_ooxml_semantic_edits"
        );
        Assert.True(
            planTool.GetRawText().Length < 4_500,
            $"Plan semantic edit action is too large: {planTool.GetRawText().Length} characters"
        );
        Assert.True(
            applyTool.GetRawText().Length < 4_500,
            $"Apply semantic edit action is too large: {applyTool.GetRawText().Length} characters"
        );

        var exactBulkInput = JsonSerializer.Serialize(new
        {
            commands = Enumerable.Range(0, 200).Select(index => new
            {
                type = "set_style",
                node_id = $"wdn_{index:D3}",
                style_id = "Definition",
                expected_style_id = "OldPara",
            }),
        });
        var selectedBulkInput = JsonSerializer.Serialize(new
        {
            commands = new[]
            {
                new
                {
                    type = "set_style_where",
                    selector = new
                    {
                        kind = "paragraph",
                        property_equals = new { style_id = "OldPara" },
                    },
                    style_id = "Definition",
                    expected_style_id = "OldPara",
                    max_matches = 200,
                },
            },
        });
        Assert.True(
            selectedBulkInput.Length * 20 < exactBulkInput.Length,
            $"Selector request {selectedBulkInput.Length} characters; exact-node request {exactBulkInput.Length} characters"
        );
    }

    [Fact]
    public void SemanticQuerySchemaCoversEveryEngineNodeKind()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        var serialized = catalog
            .InspectAction("query_ooxml_semantics")
            .ToJsonString();
        Assert.True(
            serialized.Length < 6_000,
            $"Semantic query schema is too large: {serialized.Length} characters"
        );
        using var document = JsonDocument.Parse(serialized);
        var properties = document.RootElement
            .GetProperty("tool")
            .GetProperty("inputSchema")
            .GetProperty("properties");
        var inputSchema = document.RootElement
            .GetProperty("tool")
            .GetProperty("inputSchema");
        var kinds = inputSchema
            .GetProperty("$defs")
            .GetProperty("semantic_node_kind")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expectedKinds = Enum.GetNames<WordSemanticNodeKind>()
            .Select(ToSnakeCase)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedKinds, kinds);
        foreach (var propertyName in new[] { "kinds", "ancestor", "descendant" })
        {
            var kindsSchema = properties.GetProperty(propertyName);
            if (propertyName is not "kinds")
            {
                kindsSchema = kindsSchema.GetProperty("properties").GetProperty("kinds");
            }
            Assert.Equal(
                "#/$defs/semantic_node_kind",
                kindsSchema.GetProperty("items").GetProperty("$ref").GetString()
            );
            Assert.Equal(
                expectedKinds.Length,
                kindsSchema.GetProperty("maxItems").GetInt32()
            );
            Assert.True(kindsSchema.GetProperty("uniqueItems").GetBoolean());
        }
    }

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (
                index > 0
                && char.IsUpper(character)
                && (
                    char.IsLower(value[index - 1])
                    || (
                        index + 1 < value.Length
                        && char.IsLower(value[index + 1])
                    )
                )
            )
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    [Fact]
    public async Task NativeToolErrorBecomesStructuredMcpError()
    {
        const string input =
            """{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"fail","arguments":{}}}"""
            + "\n";
        var output = new StringWriter();
        var server = new McpServer(
            new StringReader(input),
            output,
            ToolCatalog.LoadNativeWordTools(),
            new FakeToolHandler()
        );

        await server.RunAsync();

        using var response = JsonDocument.Parse(output.ToString());
        var result = response.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Equal(
            "INVALID_INPUT",
            result
                .GetProperty("structuredContent")
                .GetProperty("error")
                .GetProperty("code")
                .GetString()
        );
    }

    [Fact]
    public async Task OversizedMessageIsDrainedAndTheNextRequestStillRuns()
    {
        var input = new string('x', 256)
            + "\n"
            + """{"jsonrpc":"2.0","id":9,"method":"ping"}"""
            + "\n";
        var output = new StringWriter();
        var server = new McpServer(
            new StringReader(input),
            output,
            ToolCatalog.LoadNativeWordTools(),
            new FakeToolHandler(),
            maxMessageCharacters: 128
        );

        await server.RunAsync();

        var responses = output
            .ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line))
            .ToArray();
        Assert.Equal(2, responses.Length);
        Assert.Equal(
            -32600,
            responses.Single(response => response.RootElement.GetProperty("id").ValueKind == JsonValueKind.Null)
                .RootElement.GetProperty("error").GetProperty("code").GetInt32()
        );
        Assert.Equal(
            JsonValueKind.Object,
            responses.Single(response =>
                response.RootElement.GetProperty("id").ValueKind == JsonValueKind.Number
                && response.RootElement.GetProperty("id").GetInt32() == 9
            )
                .RootElement.GetProperty("result").ValueKind
        );
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task CancellationNotificationCancelsOnlyItsActiveRequest()
    {
        var input = string.Join(
            "\n",
            """{"jsonrpc":"2.0","id":"slow","method":"tools/call","params":{"name":"list_live_word_documents","arguments":{}}}""",
            """{"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":"slow","reason":"client timeout"}}""",
            """{"jsonrpc":"2.0","id":"alive","method":"ping"}"""
        ) + "\n";
        var output = new StringWriter();
        var server = new McpServer(
            new StringReader(input),
            output,
            ToolCatalog.LoadNativeWordTools(),
            new CancellationAwareToolHandler()
        );

        await server.RunAsync();

        var responses = output
            .ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line))
            .ToArray();
        Assert.Equal(2, responses.Length);
        var cancelled = responses.Single(response =>
            response.RootElement.GetProperty("id").GetString() == "slow"
        );
        Assert.Equal(
            -32800,
            cancelled.RootElement.GetProperty("error").GetProperty("code").GetInt32()
        );
        Assert.Contains(
            responses,
            response => response.RootElement.GetProperty("id").GetString() == "alive"
                && response.RootElement.TryGetProperty("result", out _)
        );
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    private sealed class FakeToolHandler : IToolHandler
    {
        public Task<object> CallAsync(
            string name,
            JsonElement arguments,
            CancellationToken cancellationToken
        )
        {
            if (name == "fail")
            {
                throw new NativeToolException("INVALID_INPUT", "expected failure");
            }
            if (name == "apply_live_word_operations")
            {
                return Task.FromResult<object>(
                    new
                    {
                        live_document_id = "live_1",
                        live_version = 7,
                        operation_count = 100,
                        text_operation_count = 100,
                        equation_operation_count = 0,
                        operations = Enumerable.Range(0, 100)
                            .Select(index => new
                            {
                                type = "text",
                                text = new string('x', 200),
                                index,
                            })
                            .ToArray(),
                        document = new
                        {
                            name = "Document1",
                            full_name = "Document1",
                            saved_to_disk = false,
                            active = true,
                            saved = false,
                            read_only = false,
                            paragraph_count = 101,
                            equation_count = 0,
                            table_count = 0,
                            window_hwnd = 123,
                        },
                        performance = new { total_ms = 12.3 },
                        runtime = "dotnet-native",
                        python_used = false,
                    }
                );
            }
            if (name == "preflight_live_word_equations")
            {
                return Task.FromResult<object>(
                    new
                    {
                        valid = true,
                        equation_count = 1,
                        equations = new[]
                        {
                            new
                            {
                                index = 0,
                                valid = true,
                                input_format = "latex",
                                word_linear = new string('x', 20_000),
                                word_linear_characters = 20_000,
                                display = true,
                                native_readback_required = true,
                                native_readback_enabled = true,
                                rules = new[] { "large_echo" },
                                warnings = Array.Empty<string>(),
                            },
                        },
                        mutated_word = false,
                        runtime = "dotnet-native",
                        python_used = false,
                    }
                );
            }
            return Task.FromResult<object>(
                new
                {
                    runtime = "dotnet-native",
                    python_used = false,
                    name,
                }
            );
        }
    }

    private sealed class CancellationAwareToolHandler : IToolHandler
    {
        public async Task<object> CallAsync(
            string name,
            JsonElement arguments,
            CancellationToken cancellationToken
        )
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new { name };
        }
    }
}
