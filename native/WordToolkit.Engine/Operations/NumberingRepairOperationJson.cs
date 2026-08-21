using System.Text.Json;

namespace WordToolkit.Engine.Operations;

public static class NumberingRepairOperationJson
{
    public static NumberingRepairPlanRequest ParsePlanRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement);
        RequireOnly(
            root,
            "local_path",
            "expected_package_fingerprint",
            "target_paragraph_node_id",
            "expected_number_id",
            "expected_level_index",
            "start_value",
            "include_details"
        );
        return new NumberingRepairPlanRequest(
            RequiredString(root, "local_path"),
            RequiredString(root, "expected_package_fingerprint"),
            RequiredString(root, "target_paragraph_node_id"),
            RequiredInt32(root, "expected_number_id"),
            RequiredInt32(root, "expected_level_index"),
            RequiredInt32(root, "start_value"),
            OptionalBoolean(root, "include_details") ?? false
        );
    }

    public static NumberingRepairApplyRequest ParseApplyRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement);
        RequireOnly(
            root,
            "local_path",
            "expected_package_fingerprint",
            "expected_plan_id",
            "target_paragraph_node_id",
            "expected_number_id",
            "expected_level_index",
            "start_value",
            "keep_backup"
        );
        return new NumberingRepairApplyRequest(
            RequiredString(root, "local_path"),
            RequiredString(root, "expected_package_fingerprint"),
            RequiredString(root, "expected_plan_id"),
            RequiredString(root, "target_paragraph_node_id"),
            RequiredInt32(root, "expected_number_id"),
            RequiredInt32(root, "expected_level_index"),
            RequiredInt32(root, "start_value"),
            OptionalBoolean(root, "keep_backup") ?? true
        );
    }

    private static JsonDocument ParseDocument(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw Invalid("Request JSON must be a non-empty object");
        }
        if (json.Length > NumberingRepairWordPackageContract.MaximumRequestJsonCharacters)
        {
            throw Invalid(
                $"Request JSON cannot exceed {NumberingRepairWordPackageContract.MaximumRequestJsonCharacters} characters"
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

    private static int RequiredInt32(
        IReadOnlyDictionary<string, JsonElement> item,
        string property
    )
    {
        var value = Required(item, property);
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
