using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WordToolkit.Native.Protocol;

internal sealed partial class ToolCatalog
{
    internal const int DefaultCapabilityPageSize = CapabilityManifest.DefaultPageSize;

    public JsonObject GetCapabilities(string? query, int offset, int limit)
    {
        return CapabilityManifest.Create(this, query, offset, limit);
    }

    public JsonObject GetCapabilities(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "capability arguments must be an object"
            );
        }
        foreach (var property in arguments.EnumerateObject())
        {
            if (property.Name is not ("view" or "query" or "offset" or "limit"))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Unknown capability argument",
                    new { argument = property.Name }
                );
            }
        }

        var view = "manifest";
        if (arguments.TryGetProperty("view", out var viewNode))
        {
            if (viewNode.ValueKind != JsonValueKind.String)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "view must be a string"
                );
            }
            view = viewNode.GetString() ?? "";
            if (view is not ("manifest" or "schema"))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "view must be 'manifest' or 'schema'"
                );
            }
        }
        if (view == "schema")
        {
            if (
                arguments.TryGetProperty("query", out _)
                || arguments.TryGetProperty("offset", out _)
                || arguments.TryGetProperty("limit", out _)
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "query, offset and limit are not valid for the schema view"
                );
            }
            return GetCapabilitySchema();
        }

        string? query = null;
        if (arguments.TryGetProperty("query", out var queryNode))
        {
            if (queryNode.ValueKind != JsonValueKind.String)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "query must be a string"
                );
            }
            query = queryNode.GetString();
        }
        var offset = 0;
        if (
            arguments.TryGetProperty("offset", out var offsetNode)
            && (
                offsetNode.ValueKind != JsonValueKind.Number
                || !offsetNode.TryGetInt32(out offset)
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "offset must be an integer"
            );
        }
        var limit = DefaultCapabilityPageSize;
        if (
            arguments.TryGetProperty("limit", out var limitNode)
            && (
                limitNode.ValueKind != JsonValueKind.Number
                || !limitNode.TryGetInt32(out limit)
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "limit must be an integer"
            );
        }
        return GetCapabilities(query, offset, limit);
    }

    public JsonObject GetCapabilitySchema()
    {
        return new JsonObject
        {
            ["contract_schema"] = CapabilityManifest.ContractSchema,
            ["media_type"] = "application/schema+json",
            ["schema_sha256"] = CapabilitySchemaSha256,
            ["schema_json"] = _capabilitySchemaJson,
        };
    }

    private static string Sha256Hex(string value)
    {
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value))
            )
            .ToLowerInvariant();
    }

    private static string RuntimeVersion =>
        typeof(ToolCatalog).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "0.0.0";

    private static class CapabilityManifest
    {
        internal const int DefaultPageSize = 12;
        internal const int MaxPageSize = 32;
        internal const int MaxQueryCharacters = 128;
        internal const string ContractSchema = "wordtoolkit.capabilities/1.0";

        public static JsonObject Create(
            ToolCatalog catalog,
            string? query,
            int offset,
            int limit
        )
        {
            query = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
            if (query?.Length > MaxQueryCharacters)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"query must not exceed {MaxQueryCharacters} characters"
                );
            }
            if (offset < 0)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "offset must be zero or greater"
                );
            }
            if (limit is < 1 or > MaxPageSize)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"limit must be between 1 and {MaxPageSize}"
                );
            }

            var ordered = catalog._allTools
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToArray();
            var matches = ordered
                .Where(pair => Matches(pair, query))
                .ToArray();
            var page = matches
                .Skip(offset)
                .Take(limit)
                .Select(pair => OperationSummary(catalog, pair))
                .ToArray();
            var operations = new JsonArray();
            foreach (var item in page)
            {
                operations.Add(item);
            }

            var nextOffset = offset + page.Length < matches.Length
                ? JsonValue.Create(offset + page.Length)
                : null;
            return new JsonObject
            {
                ["contract_schema"] = ContractSchema,
                ["contract_schema_version"] = catalog.SchemaVersion,
                ["toolkit_version"] = RuntimeVersion,
                ["protocols"] = new JsonObject
                {
                    ["mcp"] = catalog.McpProtocolVersion,
                },
                ["compatibility_policy"] = catalog.CompatibilityPolicy,
                ["source"] = new JsonObject
                {
                    ["transport"] = catalog.Transport,
                    ["schema_sha256"] = catalog.SourceSchemaSha256,
                    ["capability_schema_sha256"] = catalog.CapabilitySchemaSha256,
                    ["native_action_contract_sha256"] = ActionContractSha256(catalog),
                },
                ["operation_count"] = ordered.Length,
                ["exposed_mcp_tool_count"] = catalog.Tools.Count,
                ["metadata_coverage"] = MetadataCoverage(ordered),
                ["limits"] = new JsonObject
                {
                    ["request_characters"] = McpServer.DefaultMaxMessageCharacters,
                    ["active_request_limit"] = McpServer.MaxConcurrentRequests,
                    ["capability_query_characters"] = MaxQueryCharacters,
                    ["capability_page_size"] = MaxPageSize,
                    ["action_search_results"] = 12,
                },
                ["format_support"] = new JsonObject
                {
                    ["scope"] = "operation-specific",
                    ["saved_openxml_package_extensions"] = new JsonArray(
                        ".docx",
                        ".docm",
                        ".dotx",
                        ".dotm"
                    ),
                    ["live_word_formats"] = "delegated-to-installed-word",
                },
                ["security"] = new JsonObject
                {
                    ["opens_word"] = false,
                    ["reads_document"] = false,
                    ["returns_document_content"] = false,
                    ["external_network"] = false,
                },
                ["paging"] = new JsonObject
                {
                    ["query"] = query,
                    ["offset"] = offset,
                    ["limit"] = limit,
                    ["matched_operation_count"] = matches.Length,
                    ["returned_operation_count"] = page.Length,
                    ["next_offset"] = nextOffset,
                },
                ["operations"] = operations,
            };
        }

        private static bool Matches(
            KeyValuePair<string, JsonObject> pair,
            string? query
        )
        {
            if (query is null)
            {
                return true;
            }
            var description = pair.Value["description"]?.GetValue<string>() ?? "";
            return pair.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
                || description.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private static JsonObject OperationSummary(
            ToolCatalog catalog,
            KeyValuePair<string, JsonObject> pair
        )
        {
            var annotations = pair.Value["annotations"] as JsonObject;
            var inputSchema = pair.Value["inputSchema"]?.ToJsonString(JsonDefaults.Compact)
                ?? "{}";
            return new JsonObject
            {
                ["name"] = pair.Key,
                ["exposure"] = catalog._coreToolNames.Contains(pair.Key)
                    ? "core"
                    : "lazy",
                ["description"] = FirstSentence(
                    pair.Value["description"]?.GetValue<string>() ?? ""
                ),
                ["input_schema_sha256"] = Sha256Hex(inputSchema),
                ["effects"] = new JsonObject
                {
                    ["read_only"] = Annotation(annotations, "readOnlyHint"),
                    ["destructive"] = Annotation(annotations, "destructiveHint"),
                    ["idempotent"] = Annotation(annotations, "idempotentHint"),
                    ["open_world"] = Annotation(annotations, "openWorldHint"),
                },
            };
        }

        private static bool Annotation(JsonObject? annotations, string name)
        {
            var value = annotations?[name];
            if (value is null || value.GetValueKind() is not JsonValueKind.True and not JsonValueKind.False)
            {
                throw new InvalidOperationException(
                    $"Native action is missing required Boolean MCP annotation '{name}'"
                );
            }
            return value.GetValue<bool>();
        }

        private static JsonObject MetadataCoverage(
            IReadOnlyCollection<KeyValuePair<string, JsonObject>> tools
        )
        {
            return new JsonObject
            {
                ["total_operations"] = tools.Count,
                ["input_schema"] = tools.Count(pair => pair.Value["inputSchema"] is not null),
                ["mcp_effect_annotations"] = tools.Count(pair =>
                    HasAllAnnotations(pair.Value["annotations"] as JsonObject)
                ),
                ["explicit_output_schema"] = tools.Count(pair =>
                    pair.Value["outputSchema"] is not null
                ),
                ["explicit_permissions"] = tools.Count(pair =>
                    pair.Value["permissions"] is not null
                ),
                ["explicit_reversibility"] = tools.Count(pair =>
                    pair.Value["reversibility"] is not null
                ),
                ["explicit_operation_version"] = tools.Count(pair =>
                    pair.Value["operationVersion"] is not null
                ),
            };
        }

        private static bool HasAllAnnotations(JsonObject? annotations)
        {
            return annotations?["readOnlyHint"] is not null
                && annotations["destructiveHint"] is not null
                && annotations["idempotentHint"] is not null
                && annotations["openWorldHint"] is not null;
        }

        private static string ActionContractSha256(ToolCatalog catalog)
        {
            var canonical = new JsonObject
            {
                ["schema_version"] = catalog.SchemaVersion,
                ["mcp_protocol"] = catalog.McpProtocolVersion,
                ["compatibility_policy"] = catalog.CompatibilityPolicy,
                ["transport"] = catalog.Transport,
            };
            canonical["core_actions"] = new JsonArray(
                catalog._coreToolNames
                    .Order(StringComparer.Ordinal)
                    .Select(name => (JsonNode?)JsonValue.Create(name))
                    .ToArray()
            );
            var tools = new JsonArray();
            foreach (
                var pair in catalog._allTools.OrderBy(
                    pair => pair.Key,
                    StringComparer.Ordinal
                )
            )
            {
                tools.Add(pair.Value.DeepClone());
            }
            canonical["tools"] = tools;
            return Sha256Hex(canonical.ToJsonString(JsonDefaults.Compact));
        }
    }
}
