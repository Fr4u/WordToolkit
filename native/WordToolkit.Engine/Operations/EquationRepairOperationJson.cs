using System.Text.Json;

namespace WordToolkit.Engine.Operations;

public static class EquationRepairOperationJson
{
    public static EquationRepairInspectionRequest ParseInspectRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement, "request");
        RequireOnly(
            root,
            "local_path",
            "expected_package_fingerprint",
            "include_source",
            "include_issues",
            "max_items"
        );
        return new EquationRepairInspectionRequest(
            RequiredString(root, "local_path"),
            RequiredString(root, "expected_package_fingerprint"),
            OptionalBoolean(root, "include_source") ?? false,
            OptionalBoolean(root, "include_issues") ?? true,
            OptionalInt32(root, "max_items") ?? 50
        );
    }

    public static EquationRepairPlanRequest ParsePlanRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement, "request");
        RequireOnly(
            root,
            "local_path",
            "expected_package_fingerprint",
            "commands",
            "include_details"
        );
        return new EquationRepairPlanRequest(
            RequiredString(root, "local_path"),
            RequiredString(root, "expected_package_fingerprint"),
            Commands(root),
            OptionalBoolean(root, "include_details") ?? false
        );
    }

    public static EquationRepairApplyRequest ParseApplyRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement, "request");
        RequireOnly(
            root,
            "local_path",
            "expected_package_fingerprint",
            "expected_plan_id",
            "commands",
            "keep_backup",
            "protected_edit_authorization"
        );
        return new EquationRepairApplyRequest(
            RequiredString(root, "local_path"),
            RequiredString(root, "expected_package_fingerprint"),
            RequiredString(root, "expected_plan_id"),
            Commands(root),
            OptionalBoolean(root, "keep_backup") ?? true,
            OptionalString(root, "protected_edit_authorization")
        );
    }

    private static IReadOnlyList<EquationRepairCommandRequest> Commands(
        IReadOnlyDictionary<string, JsonElement> root
    )
    {
        var value = Required(root, "commands");
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("commands must be an array");
        }
        var result = new List<EquationRepairCommandRequest>();
        foreach (var item in value.EnumerateArray())
        {
            if (result.Count >= EquationRepairWordPackageContract.MaximumCommands)
            {
                throw Invalid(
                    $"commands cannot contain more than {EquationRepairWordPackageContract.MaximumCommands} items"
                );
            }
            var command = Object(item, "command");
            RequireOnly(
                command,
                "repair_kind",
                "candidate_id",
                "expected_candidate_fingerprint"
            );
            result.Add(new EquationRepairCommandRequest(
                RequiredString(command, "repair_kind"),
                RequiredString(command, "candidate_id"),
                RequiredString(command, "expected_candidate_fingerprint")
            ));
        }
        if (result.Count == 0)
        {
            throw Invalid("commands must contain at least one item");
        }
        return result;
    }

    private static JsonDocument ParseDocument(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw Invalid("Request JSON must be a non-empty object");
        }
        if (json.Length > EquationRepairWordPackageContract.MaximumRequestJsonCharacters)
        {
            throw Invalid(
                $"Request JSON cannot exceed {EquationRepairWordPackageContract.MaximumRequestJsonCharacters} characters"
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
                    MaxDepth = 24,
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
        string description
    )
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{description} must be an object");
        }
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!result.TryAdd(property.Name, property.Value))
            {
                throw Invalid($"{description} contains duplicate field '{property.Name}'");
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

    private static string? OptionalString(
        IReadOnlyDictionary<string, JsonElement> item,
        string property
    )
    {
        if (!item.TryGetValue(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String)
            throw Invalid($"{property} must be a string or null");
        return value.GetString();
    }

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
