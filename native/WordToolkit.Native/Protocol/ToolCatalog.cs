using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Observability;

namespace WordToolkit.Native.Protocol;

internal sealed partial class ToolCatalog
{
    private const string CapabilitiesName = "get_wordtoolkit_capabilities";
    private const string InspectActionName = "inspect_wordtoolkit_action";
    private const string ExecuteActionName = "execute_wordtoolkit_action";
    private const string SearchActionsName = "search_wordtoolkit_actions";

    private readonly IReadOnlyDictionary<string, JsonObject> _allTools;
    private readonly IReadOnlySet<string> _coreToolNames;
    private readonly string _capabilitySchemaJson;

    public JsonArray Tools { get; }
    public int ActionCount => _allTools.Count;
    public string SchemaVersion { get; }
    public string McpProtocolVersion { get; }
    public string CompatibilityPolicy { get; }
    public string Transport { get; }
    public string SourceSchemaSha256 { get; }
    public string CapabilitySchemaSha256 { get; }

    private ToolCatalog(
        JsonArray tools,
        IReadOnlyDictionary<string, JsonObject> allTools,
        IReadOnlySet<string> coreToolNames,
        string schemaVersion,
        string mcpProtocolVersion,
        string compatibilityPolicy,
        string transport,
        string sourceSchemaSha256,
        string capabilitySchemaSha256,
        string capabilitySchemaJson
    )
    {
        Tools = tools;
        _allTools = allTools;
        _coreToolNames = coreToolNames;
        SchemaVersion = schemaVersion;
        McpProtocolVersion = mcpProtocolVersion;
        CompatibilityPolicy = compatibilityPolicy;
        Transport = transport;
        SourceSchemaSha256 = sourceSchemaSha256;
        CapabilitySchemaSha256 = capabilitySchemaSha256;
        _capabilitySchemaJson = capabilitySchemaJson;
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
        var schemaJson = reader.ReadToEnd();
        var capabilitySchemaJson = ReadEmbeddedResource(
            assembly,
            "Schemas.wordtoolkit-capabilities.v1.schema.json",
            "Embedded capability manifest schema is missing"
        );
        return LoadNativeWordTools(schemaJson, capabilitySchemaJson);
    }

    internal static ToolCatalog LoadNativeWordTools(
        string schemaJson,
        string capabilitySchemaJson
    )
    {
        var root = JsonNode.Parse(schemaJson)?.AsObject()
            ?? throw new InvalidOperationException("Embedded MCP tool schema is invalid");
        var schemaVersion = RequiredContractString(root, "schema_version");
        var mcpProtocolVersion = RequiredContractString(root, "mcp_protocol");
        var compatibilityPolicy = RequiredContractString(root, "compatibility_policy");
        var transport = RequiredContractString(root, "transport");
        var nativeRuntime = root["native_runtime"]?.AsObject()
            ?? throw new InvalidOperationException(
                "Embedded MCP schema is missing the native runtime registry"
            );
        var nativeToolOrder = ReadActionList(nativeRuntime, "actions");
        var coreToolOrder = ReadActionList(nativeRuntime, "core_actions");
        var nativeToolNames = nativeToolOrder.ToHashSet(StringComparer.Ordinal);
        var coreToolNames = coreToolOrder.ToHashSet(StringComparer.Ordinal);
        if (!coreToolNames.IsSubsetOf(nativeToolNames))
        {
            throw new InvalidOperationException(
                "Every native core action must also be present in the native action registry"
            );
        }
        _ = JsonNode.Parse(capabilitySchemaJson)?.AsObject()
            ?? throw new InvalidOperationException(
                "Embedded capability manifest schema is invalid"
            );
        var tools = root["tools"]?.AsArray()
            ?? throw new InvalidOperationException("Embedded MCP tool list is missing");
        var allTools = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var node in tools)
        {
            var tool = node?.AsObject();
            var name = tool?["name"]?.GetValue<string>();
            if (name is not null && nativeToolNames.Contains(name))
            {
                if (!allTools.TryAdd(name, CompactSchema(tool!)))
                {
                    throw new InvalidOperationException(
                        $"Embedded MCP tool list contains duplicate native action '{name}'"
                    );
                }
            }
        }
        if (allTools.Count != nativeToolNames.Count)
        {
            throw new InvalidOperationException(
                $"Native tool schema mismatch: expected {nativeToolNames.Count}, found {allTools.Count}"
            );
        }

        var exposed = new JsonArray();
        foreach (var name in coreToolOrder)
        {
            exposed.Add(allTools[name].DeepClone());
        }
        exposed.Add(SearchActionsTool());
        exposed.Add(InspectActionTool());
        exposed.Add(ExecuteActionTool());
        exposed.Add(CapabilitiesTool());
        return new ToolCatalog(
            exposed,
            allTools,
            coreToolNames,
            schemaVersion,
            mcpProtocolVersion,
            compatibilityPolicy,
            transport,
            Sha256Hex(schemaJson),
            Sha256Hex(capabilitySchemaJson),
            capabilitySchemaJson
        );
    }

    public bool IsAction(string name) => _allTools.ContainsKey(name);

    public WordOperationDescriptor GetObservationDescriptor(string name)
    {
        JsonObject tool = _allTools.TryGetValue(name, out var registered)
            ? registered
            : name switch
            {
                SearchActionsName => SearchActionsTool(),
                InspectActionName => InspectActionTool(),
                ExecuteActionName => ExecuteActionTool(),
                CapabilitiesName => CapabilitiesTool(),
                _ => throw new ArgumentOutOfRangeException(nameof(name)),
            };
        var annotations = tool["annotations"]?.AsObject()
            ?? throw new InvalidOperationException(
                $"Native action '{name}' is missing effect annotations"
            );
        return new WordOperationDescriptor(
            name,
            tool["operationVersion"]?.GetValue<string>() ?? "1.0",
            new WordOperationEffects(
                RequiredAnnotation(annotations, "readOnlyHint"),
                RequiredAnnotation(annotations, "destructiveHint"),
                RequiredAnnotation(annotations, "idempotentHint"),
                RequiredAnnotation(annotations, "openWorldHint")
            )
        );
    }

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

    public static bool IsCapabilitiesGateway(string name) =>
        string.Equals(name, CapabilitiesName, StringComparison.Ordinal);

    private static bool RequiredAnnotation(JsonObject annotations, string name)
    {
        var value = annotations[name];
        if (value?.GetValueKind() is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new InvalidOperationException(
                $"Native action is missing required Boolean MCP annotation '{name}'"
            );
        }
        return value.GetValue<bool>();
    }

    private static JsonObject CompactSchema(JsonObject source)
    {
        var compact = source.DeepClone().AsObject();
        RemovePresentationMetadata(compact);
        return compact;
    }

    private static void RemovePresentationMetadata(JsonNode? node)
    {
        RemovePresentationMetadata(node, preserveMapKeys: false);
    }

    private static void RemovePresentationMetadata(
        JsonNode? node,
        bool preserveMapKeys
    )
    {
        if (node is JsonObject obj)
        {
            if (!preserveMapKeys)
            {
                obj.Remove("title");
            }
            foreach (var child in obj.ToArray())
            {
                RemovePresentationMetadata(
                    child.Value,
                    child.Key is "properties" or "patternProperties" or "$defs" or "dependentSchemas"
                );
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
                    ["view"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray("manifest", "schema"),
                        ["default"] = "manifest",
                        ["description"] =
                            "Return a manifest page or the exact embedded capability JSON Schema text.",
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

    private static JsonObject CapabilitiesTool()
    {
        return new JsonObject
        {
            ["name"] = CapabilitiesName,
            ["description"] =
                "Negotiate the versioned WordToolkit capability contract without opening Word or reading a document. Returns bounded operation summaries or the exact embedded normative JSON Schema; inspect one action separately for its full input schema.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["query"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["maxLength"] = CapabilityManifest.MaxQueryCharacters,
                        ["description"] = "Optional case-insensitive operation-name or description filter.",
                    },
                    ["offset"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["minimum"] = 0,
                        ["default"] = 0,
                    },
                    ["limit"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["minimum"] = 1,
                        ["maximum"] = CapabilityManifest.MaxPageSize,
                        ["default"] = CapabilityManifest.DefaultPageSize,
                    },
                },
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

    private static string RequiredContractString(JsonObject root, string name)
    {
        var value = root[name]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Embedded MCP tool schema is missing required contract field '{name}'"
            );
        }
        return value;
    }

    private static IReadOnlyList<string> ReadActionList(JsonObject root, string name)
    {
        var array = root[name]?.AsArray()
            ?? throw new InvalidOperationException(
                $"Embedded MCP schema is missing native runtime field '{name}'"
            );
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(array.Count);
        foreach (var node in array)
        {
            var value = node?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
            {
                throw new InvalidOperationException(
                    $"Embedded MCP native runtime field '{name}' contains an invalid or duplicate action"
                );
            }
            result.Add(value);
        }
        if (result.Count == 0)
        {
            throw new InvalidOperationException(
                $"Embedded MCP native runtime field '{name}' must not be empty"
            );
        }
        return result;
    }

    private static string ReadEmbeddedResource(
        Assembly assembly,
        string suffix,
        string missingMessage
    )
    {
        var resourceName = assembly
            .GetManifestResourceNames()
            .Single(name => name.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(missingMessage);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string FirstSentence(string value)
    {
        var index = value.IndexOf(". ", StringComparison.Ordinal);
        return index < 0 ? value : value[..(index + 1)];
    }
}
