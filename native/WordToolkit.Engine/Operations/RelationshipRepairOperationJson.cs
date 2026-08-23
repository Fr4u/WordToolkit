using System.Text.Json;

namespace WordToolkit.Engine.Operations;

public static class RelationshipRepairOperationJson
{
    public static RelationshipInspectionRequest ParseInspectRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement);
        RequireOnly(
            root,
            "local_path",
            "expected_package_fingerprint",
            "include_all",
            "include_details",
            "max_items"
        );
        return new RelationshipInspectionRequest(
            RequiredString(root, "local_path"),
            RequiredString(root, "expected_package_fingerprint"),
            OptionalBoolean(root, "include_all") ?? false,
            OptionalBoolean(root, "include_details") ?? false,
            OptionalInt32(root, "max_items") ?? 50
        );
    }

    public static RelationshipRepairPlanRequest ParsePlanRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement);
        RequireOnly(
            root,
            "local_path",
            "expected_package_fingerprint",
            "commands",
            "include_details"
        );
        return new RelationshipRepairPlanRequest(
            RequiredString(root, "local_path"),
            RequiredString(root, "expected_package_fingerprint"),
            Commands(root),
            OptionalBoolean(root, "include_details") ?? false
        );
    }

    public static RelationshipRepairApplyRequest ParseApplyRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement);
        RequireOnly(
            root,
            "local_path",
            "expected_package_fingerprint",
            "expected_plan_id",
            "commands",
            "allow_external_relationship_removal",
            "keep_backup",
            "protected_edit_authorization"
        );
        return new RelationshipRepairApplyRequest(
            RequiredString(root, "local_path"),
            RequiredString(root, "expected_package_fingerprint"),
            RequiredString(root, "expected_plan_id"),
            Commands(root),
            OptionalBoolean(root, "allow_external_relationship_removal") ?? false,
            OptionalBoolean(root, "keep_backup") ?? true,
            OptionalString(root, "protected_edit_authorization")
        );
    }

    private static IReadOnlyList<RelationshipRepairCommandRequest> Commands(
        IReadOnlyDictionary<string, JsonElement> root
    )
    {
        var value = Required(root, "commands");
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("commands must be an array");
        }
        var commands = new List<RelationshipRepairCommandRequest>();
        foreach (var element in value.EnumerateArray())
        {
            if (commands.Count == RelationshipRepairWordPackageContract.MaximumCommands)
            {
                throw Invalid(
                    $"commands cannot contain more than {RelationshipRepairWordPackageContract.MaximumCommands} items"
                );
            }
            var item = Object(element);
            RequireOnly(
                item,
                "kind",
                "source_part_uri",
                "relationship_id",
                "expected_relationship_fingerprint",
                "relationship_part_uri",
                "expected_entry_sha256"
            );
            commands.Add(new RelationshipRepairCommandRequest(
                RequiredString(item, "kind"),
                OptionalString(item, "source_part_uri"),
                OptionalString(item, "relationship_id"),
                OptionalString(item, "expected_relationship_fingerprint"),
                OptionalString(item, "relationship_part_uri"),
                OptionalString(item, "expected_entry_sha256")
            ));
        }
        if (commands.Count == 0)
        {
            throw Invalid("commands must contain at least one item");
        }
        return commands;
    }

    private static JsonDocument ParseDocument(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw Invalid("Request JSON must be a non-empty object");
        }
        if (json.Length > RelationshipRepairWordPackageContract.MaximumRequestJsonCharacters)
        {
            throw Invalid(
                $"Request JSON cannot exceed {RelationshipRepairWordPackageContract.MaximumRequestJsonCharacters} characters"
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

    private static IReadOnlyDictionary<string, JsonElement> Object(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("Request and command items must be objects");
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
