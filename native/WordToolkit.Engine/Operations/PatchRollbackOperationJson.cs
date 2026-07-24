using System.Text.Json;

namespace WordToolkit.Engine.Operations;

/// <summary>
/// Strict transport-neutral JSON codec shared by the patch rollback CLI and MCP adapter.
/// </summary>
public static class PatchRollbackOperationJson
{
    public static PatchRollbackPlanRequest ParsePlanRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement, "request");
        RequireOnly(
            root,
            "request",
            "local_path",
            "patch_path",
            "expected_package_fingerprint",
            "expected_patch_id",
            "view",
            "offset",
            "max_items",
            "include_hashes"
        );
        return new PatchRollbackPlanRequest(
            RequiredString(root, "local_path"),
            RequiredString(root, "patch_path"),
            RequiredString(root, "expected_package_fingerprint"),
            RequiredString(root, "expected_patch_id"),
            ParseView(OptionalString(root, "view")),
            OptionalInt32(root, "offset") ?? 0,
            OptionalInt32(root, "max_items") ?? 50,
            OptionalBoolean(root, "include_hashes") ?? false
        );
    }

    public static PatchRollbackApplyRequest ParseApplyRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement, "request");
        RequireOnly(
            root,
            "request",
            "local_path",
            "patch_path",
            "expected_package_fingerprint",
            "expected_patch_id",
            "expected_rollback_plan_id",
            "allow_digital_signature_invalidation",
            "allow_active_content_changes",
            "allow_external_relationship_changes",
            "allow_opaque_binary_changes",
            "allow_new_structural_errors",
            "keep_backup"
        );
        return new PatchRollbackApplyRequest(
            RequiredString(root, "local_path"),
            RequiredString(root, "patch_path"),
            RequiredString(root, "expected_package_fingerprint"),
            RequiredString(root, "expected_patch_id"),
            RequiredString(root, "expected_rollback_plan_id"),
            OptionalBoolean(root, "allow_digital_signature_invalidation") ?? false,
            OptionalBoolean(root, "allow_active_content_changes") ?? false,
            OptionalBoolean(root, "allow_external_relationship_changes") ?? false,
            OptionalBoolean(root, "allow_opaque_binary_changes") ?? false,
            OptionalBoolean(root, "allow_new_structural_errors") ?? false,
            OptionalBoolean(root, "keep_backup") ?? true
        );
    }

    private static PatchRollbackView ParseView(string? value) => value switch
    {
        null or "summary" => PatchRollbackView.Summary,
        "operations" => PatchRollbackView.Operations,
        "risks" => PatchRollbackView.Risks,
        "schema_errors" => PatchRollbackView.SchemaErrors,
        _ => throw Invalid(
            "view must be summary, operations, risks, or schema_errors"
        ),
    };

    private static JsonDocument ParseDocument(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw Invalid("Request JSON must be a non-empty object");
        }
        if (json.Length > PatchRollbackWordPackageContract.MaximumRequestJsonCharacters)
        {
            throw Invalid(
                $"Request JSON cannot exceed {PatchRollbackWordPackageContract.MaximumRequestJsonCharacters} characters"
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
                    MaxDepth = 8,
                }
            );
        }
        catch (JsonException exception)
        {
            throw Invalid("Request JSON is malformed or exceeds the depth limit", exception);
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> Object(
        JsonElement value,
        string name
    )
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{name} must be an object");
        }
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!result.TryAdd(property.Name, property.Value))
            {
                throw Invalid($"{name} contains duplicate property '{property.Name}'");
            }
        }
        return result;
    }

    private static void RequireOnly(
        IReadOnlyDictionary<string, JsonElement> item,
        string name,
        params string[] allowed
    )
    {
        foreach (var property in item.Keys)
        {
            if (!allowed.Contains(property, StringComparer.Ordinal))
            {
                throw Invalid($"{name} contains unsupported field '{property}'");
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
        return value.GetString() ?? string.Empty;
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
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw Invalid($"{property} must be a boolean");
        }
        return value.GetBoolean();
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

    private static WordToolkitOperationException Invalid(
        string message,
        Exception? innerException = null
    ) => new("INVALID_INPUT", message, innerException: innerException);
}
