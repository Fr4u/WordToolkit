using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WordToolkit.Native.Protocol;

internal sealed class ToolCatalog
{
    private const string InspectActionName = "inspect_wordtoolkit_action";
    private const string ExecuteActionName = "execute_wordtoolkit_action";
    private const string SearchActionsName = "search_wordtoolkit_actions";

    private static readonly HashSet<string> NativeToolNames =
    [
        "list_live_word_documents",
        "start_word_application",
        "create_live_word_document",
        "open_live_word_document",
        "connect_live_word_document",
        "inspect_ooxml_package",
        "inspect_ooxml_semantics",
        "query_ooxml_semantics",
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
        "inspect_ooxml_settings",
        "inspect_ooxml_references",
        "inspect_ooxml_dependencies",
        "lint_ooxml_document",
        "inspect_ooxml_equations",
        "inspect_ooxml_review",
        "inspect_ooxml_fonts",
        "resolve_ooxml_formatting",
        "plan_ooxml_text_edits",
        "apply_ooxml_text_edits",
        "plan_ooxml_review_decisions",
        "apply_ooxml_review_decisions",
        "inspect_live_word_document",
        "map_live_word_structures",
        "inspect_live_word_structure_items",
        "inspect_live_word_equation_learning",
        "inspect_live_word_structure_learning",
        "inspect_live_word_object_model_types",
        "inspect_live_word_object_model_members",
        "inspect_live_word_member_capabilities",
        "preflight_live_word_member_operations",
        "execute_live_word_member_operations",
        "find_live_word_text",
        "replace_live_word_text",
        "inspect_live_word_review",
        "manage_live_word_review",
        "diagnose_live_word_layout",
        "get_live_word_selection",
        "inspect_live_word_undo",
        "undo_live_word_operation",
        "insert_live_word_text",
        "format_live_word_selection",
        "insert_live_word_table",
        "preflight_live_word_table_formulas",
        "insert_live_word_table_formulas",
        "update_live_word_table_fields",
        "insert_live_word_list",
        "preflight_live_word_bookmarks",
        "insert_live_word_bookmarks",
        "preflight_live_word_fields",
        "insert_live_word_fields",
        "insert_live_word_image",
        "insert_live_word_comment",
        "insert_live_word_note",
        "set_live_word_header_footer",
        "insert_live_word_equation",
        "insert_live_word_equations_batch",
        "preflight_live_word_equations",
        "apply_live_word_operations",
        "validate_live_word_document",
        "export_live_word_pdf",
        "save_live_word_document",
        "close_live_word_document",
        "quit_word_application",
        "disconnect_live_word_document",
    ];

    private static readonly HashSet<string> CoreToolNames =
    [
        "list_live_word_documents",
        "start_word_application",
        "create_live_word_document",
        "open_live_word_document",
        "connect_live_word_document",
        "inspect_ooxml_package",
        "inspect_live_word_document",
        "get_live_word_selection",
        "apply_live_word_operations",
        "save_live_word_document",
        "disconnect_live_word_document",
    ];

    private readonly IReadOnlyDictionary<string, JsonObject> _allTools;

    public JsonArray Tools { get; }
    public int ActionCount => _allTools.Count;

    private ToolCatalog(
        JsonArray tools,
        IReadOnlyDictionary<string, JsonObject> allTools
    )
    {
        Tools = tools;
        _allTools = allTools;
    }

    public static ToolCatalog LoadNativeWordTools()
    {
        const string suffix = "Schemas.mcp-tools-local.v1.json";
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .Single(name => name.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Embedded MCP tool schema is missing");
        using var reader = new StreamReader(stream);
        var root = JsonNode.Parse(reader.ReadToEnd())?.AsObject()
            ?? throw new InvalidOperationException("Embedded MCP tool schema is invalid");
        var tools = root["tools"]?.AsArray()
            ?? throw new InvalidOperationException("Embedded MCP tool list is missing");
        var allTools = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var node in tools)
        {
            var tool = node?.AsObject();
            var name = tool?["name"]?.GetValue<string>();
            if (name is not null && NativeToolNames.Contains(name))
            {
                allTools[name] = CompactSchema(tool!);
            }
        }
        if (allTools.Count != NativeToolNames.Count)
        {
            throw new InvalidOperationException(
                $"Native tool schema mismatch: expected {NativeToolNames.Count}, found {allTools.Count}"
            );
        }

        var exposed = new JsonArray();
        foreach (var name in CoreToolNames)
        {
            exposed.Add(allTools[name].DeepClone());
        }
        exposed.Add(SearchActionsTool());
        exposed.Add(InspectActionTool());
        exposed.Add(ExecuteActionTool());
        return new ToolCatalog(exposed, allTools);
    }

    public bool IsAction(string name) => _allTools.ContainsKey(name);

    public JsonObject InspectAction(string name)
    {
        if (!_allTools.TryGetValue(name, out var tool))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Unknown WordToolkit action",
                new { action = name }
            );
        }
        return new JsonObject
        {
            ["action"] = name,
            ["tool"] = tool.DeepClone(),
        };
    }

    public JsonObject SearchActions(string query, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "query must be a non-empty string"
            );
        }
        if (maxResults is < 1 or > 12)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_results must be between 1 and 12"
            );
        }
        var terms = query.Split(
            [' ', '-', '_', '/'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        var matches = _allTools
            .Select(
                pair =>
                {
                    var description =
                        pair.Value["description"]?.GetValue<string>() ?? "";
                    var haystack = pair.Key + " " + description;
                    var score = terms.Count(
                        term =>
                            haystack.Contains(term, StringComparison.OrdinalIgnoreCase)
                    );
                    return new
                    {
                        pair.Key,
                        Description = description,
                        Score = score,
                    };
                }
            )
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Take(maxResults)
            .Select(
                item =>
                    new
                    {
                        action = item.Key,
                        description = FirstSentence(item.Description),
                    }
            )
            .ToArray();
        return new JsonObject
        {
            ["query"] = query,
            ["match_count"] = matches.Length,
            ["actions"] = JsonSerializer.SerializeToNode(matches, JsonDefaults.Compact),
        };
    }

    public static bool IsSearchGateway(string name) =>
        string.Equals(name, SearchActionsName, StringComparison.Ordinal);

    public static bool IsInspectGateway(string name) =>
        string.Equals(name, InspectActionName, StringComparison.Ordinal);

    public static bool IsExecuteGateway(string name) =>
        string.Equals(name, ExecuteActionName, StringComparison.Ordinal);

    private static JsonObject CompactSchema(JsonObject source)
    {
        var compact = source.DeepClone().AsObject();
        RemovePresentationMetadata(compact);
        return compact;
    }

    private static void RemovePresentationMetadata(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove("title");
            foreach (var child in obj.ToArray())
            {
                RemovePresentationMetadata(child.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                RemovePresentationMetadata(child);
            }
        }
    }

    private static JsonObject InspectActionTool()
    {
        return new JsonObject
        {
            ["name"] = InspectActionName,
            ["description"] =
                "Return the input schema for one WordToolkit action. Use only when the small core catalog does not cover the task.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["action"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Exact WordToolkit action name.",
                    },
                },
                ["required"] = new JsonArray("action"),
                ["additionalProperties"] = false,
            },
            ["annotations"] = new JsonObject
            {
                ["readOnlyHint"] = true,
                ["destructiveHint"] = false,
                ["idempotentHint"] = true,
                ["openWorldHint"] = false,
            },
        };
    }

    private static JsonObject SearchActionsTool()
    {
        return new JsonObject
        {
            ["name"] = SearchActionsName,
            ["description"] =
                "Find a rare WordToolkit action by a short capability query, then inspect only the chosen action.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["query"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Capability keywords, for example image or review.",
                    },
                    ["max_results"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["minimum"] = 1,
                        ["maximum"] = 12,
                        ["default"] = 8,
                    },
                },
                ["required"] = new JsonArray("query"),
                ["additionalProperties"] = false,
            },
            ["annotations"] = new JsonObject
            {
                ["readOnlyHint"] = true,
                ["destructiveHint"] = false,
                ["idempotentHint"] = true,
                ["openWorldHint"] = false,
            },
        };
    }

    private static JsonObject ExecuteActionTool()
    {
        return new JsonObject
        {
            ["name"] = ExecuteActionName,
            ["description"] =
                "Execute one inspected WordToolkit action. Compact responses omit echoed input and diagnostics; request full only when exact detail is required.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["action"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Exact action previously inspected.",
                    },
                    ["arguments"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["description"] = "Arguments matching the inspected schema.",
                    },
                    ["response_mode"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray("compact", "full"),
                        ["default"] = "compact",
                    },
                },
                ["required"] = new JsonArray("action", "arguments"),
                ["additionalProperties"] = false,
            },
            ["annotations"] = new JsonObject
            {
                ["readOnlyHint"] = false,
                ["destructiveHint"] = true,
                ["idempotentHint"] = false,
                ["openWorldHint"] = false,
            },
        };
    }

    private static string FirstSentence(string value)
    {
        var index = value.IndexOf(". ", StringComparison.Ordinal);
        return index < 0 ? value : value[..(index + 1)];
    }
}
