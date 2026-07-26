using System.Text.Json;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Operations;

public static class SemanticRoleOperationJson
{
    public static SemanticRoleInspectionRequest ParseInspectRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement);
        RequireOnly(
            root,
            "local_path",
            "view",
            "story_kind",
            "expected_package_fingerprint",
            "roles",
            "minimum_evidence",
            "usable_only",
            "candidate_id",
            "paragraph_node_id",
            "classification",
            "offset",
            "max_items",
            "include_evidence",
            "include_styles",
            "include_declarations",
            "include_hashes",
            "include_source",
            "include_sensitive",
            "text_preview_chars"
        );
        var roles = root.TryGetValue("roles", out var roleValue)
            ? Array(roleValue, "roles").Select((item, index) =>
                ParseRole(String(item, $"roles[{index}]"), $"roles[{index}]")
            ).ToArray()
            : new[] { WordSemanticRoleKind.Theorem };
        return new SemanticRoleInspectionRequest(
            RequiredString(root, "local_path"),
            OptionalString(root, "view") ?? "candidates",
            OptionalString(root, "story_kind") ?? "main",
            OptionalString(root, "expected_package_fingerprint"),
            roles,
            OptionalString(root, "minimum_evidence") ?? "any",
            OptionalBoolean(root, "usable_only") ?? true,
            OptionalString(root, "candidate_id"),
            OptionalString(root, "paragraph_node_id"),
            OptionalString(root, "classification"),
            OptionalInt32(root, "offset") ?? 0,
            OptionalInt32(root, "max_items")
                ?? SemanticRoleWordPackageContract.DefaultMaxItems,
            OptionalBoolean(root, "include_evidence") ?? false,
            OptionalBoolean(root, "include_styles") ?? false,
            OptionalBoolean(root, "include_declarations") ?? false,
            OptionalBoolean(root, "include_hashes") ?? false,
            OptionalBoolean(root, "include_source") ?? false,
            OptionalBoolean(root, "include_sensitive") ?? false,
            OptionalInt32(root, "text_preview_chars") ?? 0
        );
    }

    private static WordSemanticRoleKind ParseRole(string value, string path) =>
        WordSemanticRoleGraphBuilder.TryParseRole(value, out var role)
            ? role
            : throw Invalid($"{path} is not a supported semantic role");

    private static JsonDocument ParseDocument(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw Invalid("Request JSON must be a non-empty object");
        }
        if (json.Length > SemanticRoleWordPackageContract.MaximumRequestJsonCharacters)
        {
            throw Invalid(
                $"Request JSON cannot exceed {SemanticRoleWordPackageContract.MaximumRequestJsonCharacters} characters"
            );
        }
        try
        {
            return JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 16,
                }
            );
        }
        catch (JsonException exception)
        {
            throw Invalid("Request JSON is malformed or exceeds the depth limit", exception);
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> Object(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("Request must be an object");
        }
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!result.TryAdd(property.Name, property.Value))
            {
                throw Invalid($"Request contains duplicate field '{property.Name}'");
            }
        }
        return result;
    }

    private static IReadOnlyList<JsonElement> Array(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"{path} must be an array");
        }
        return value.EnumerateArray().ToArray();
    }

    private static void RequireOnly(
        IReadOnlyDictionary<string, JsonElement> item,
        params string[] allowed
    )
    {
        foreach (var property in item.Keys)
        {
            if (!allowed.Contains(property, StringComparer.Ordinal))
            {
                throw Invalid($"Request contains unsupported field '{property}'");
            }
        }
    }

    private static JsonElement Required(
        IReadOnlyDictionary<string, JsonElement> item,
        string property
    ) => item.TryGetValue(property, out var value)
        ? value
        : throw Invalid($"Missing required field '{property}'");

    private static string RequiredString(
        IReadOnlyDictionary<string, JsonElement> item,
        string property
    ) => String(Required(item, property), property);

    private static string String(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"{path} must be a string");
        }
        return value.GetString() ?? string.Empty;
    }

    private static string? OptionalString(
        IReadOnlyDictionary<string, JsonElement> item,
        string property
    )
    {
        if (!item.TryGetValue(property, out var value))
        {
            return null;
        }
        return String(value, property);
    }

    private static int? OptionalInt32(
        IReadOnlyDictionary<string, JsonElement> item,
        string property
    )
    {
        if (!item.TryGetValue(property, out var value))
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            throw Invalid($"{property} must be a 32-bit integer");
        }
        return result;
    }

    private static bool? OptionalBoolean(
        IReadOnlyDictionary<string, JsonElement> item,
        string property
    )
    {
        if (!item.TryGetValue(property, out var value))
        {
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Invalid($"{property} must be a boolean"),
        };
    }

    private static WordToolkitOperationException Invalid(
        string message,
        Exception? innerException = null
    ) => new("INVALID_INPUT", message, innerException: innerException);
}
