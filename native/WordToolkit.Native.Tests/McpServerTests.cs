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
            "0.27.0",
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
                "compare_ooxml_semantics",
                "inspect_ooxml_sections",
                "inspect_ooxml_styles",
                "inspect_ooxml_numbering",
                "inspect_ooxml_theme",
                "inspect_ooxml_references",
                "inspect_ooxml_equations",
                "inspect_ooxml_review",
                "resolve_ooxml_formatting",
                "plan_ooxml_text_edits",
                "apply_ooxml_text_edits",
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
            """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"execute_wordtoolkit_action","arguments":{"action":"apply_live_word_operations","arguments":{"live_document_id":"live_1","operations":[{"type":"text","text":"x"}]},"response_mode":"full"}}}"""
        ) + "\n";
        var output = new StringWriter();
        var catalog = ToolCatalog.LoadNativeWordTools();
        Assert.Equal(66, catalog.ActionCount);
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
        Assert.Equal(4, responses.Length);

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
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public void SemanticQuerySchemaCoversEveryEngineNodeKind()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        using var document = JsonDocument.Parse(
            catalog.InspectAction("query_ooxml_semantics").ToJsonString()
        );
        var kinds = document.RootElement
            .GetProperty("tool")
            .GetProperty("inputSchema")
            .GetProperty("properties")
            .GetProperty("kinds")
            .GetProperty("items")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expected = Enum.GetNames<WordSemanticNodeKind>()
            .Select(ToSnakeCase)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, kinds);
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
}
