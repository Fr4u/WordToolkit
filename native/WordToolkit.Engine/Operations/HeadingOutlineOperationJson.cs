using System.Text.Json;

namespace WordToolkit.Engine.Operations;

public static class HeadingOutlineOperationJson
{
    public static HeadingOutlineInspectionRequest ParseInspectRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement);
        RequireOnly(
            root,
            "local_path",
            "view",
            "story_kind",
            "expected_package_fingerprint",
            "paragraph_node_id",
            "minimum_level",
            "maximum_level",
            "hierarchy_only",
            "offset",
            "max_items",
            "include_styles",
            "include_source",
            "include_sensitive",
            "text_preview_chars"
        );
        return new HeadingOutlineInspectionRequest(
            RequiredString(root, "local_path"),
            OptionalString(root, "view") ?? "headings",
            OptionalString(root, "story_kind") ?? "main",
            OptionalString(root, "expected_package_fingerprint"),
            OptionalString(root, "paragraph_node_id"),
            OptionalInt32(root, "minimum_level"),
            OptionalInt32(root, "maximum_level"),
            OptionalBoolean(root, "hierarchy_only") ?? true,
            OptionalInt32(root, "offset") ?? 0,
            OptionalInt32(root, "max_items")
                ?? HeadingOutlineWordPackageContract.DefaultMaxItems,
            OptionalBoolean(root, "include_styles") ?? false,
            OptionalBoolean(root, "include_source") ?? false,
            OptionalBoolean(root, "include_sensitive") ?? false,
            OptionalInt32(root, "text_preview_chars") ?? 0
        );
    }

    private static JsonDocument ParseDocument(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw Invalid("Request JSON must be a non-empty object");
        }
        if (json.Length > HeadingOutlineWordPackageContract.MaximumRequestJsonCharacters)
        {
            throw Invalid(
                $"Request JSON cannot exceed {HeadingOutlineWordPackageContract.MaximumRequestJsonCharacters} characters"
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
    )
    {
        var value = Required(item, property);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"{property} must be a string");
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
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"{property} must be a string");
        }
        return value.GetString();
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
