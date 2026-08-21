using System.Text.Json;

namespace WordToolkit.Engine.Operations;

public static class TemplateStyleAlignmentOperationJson
{
    public static TemplateStyleAlignmentInspectRequest ParseInspectRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement, "request");
        RequireOnly(root, "target_path", "template_path",
            "expected_target_package_fingerprint",
            "expected_template_package_fingerprint", "max_items", "include_issues",
            "include_dependencies");
        return new TemplateStyleAlignmentInspectRequest(
            RequiredString(root, "target_path", "request"),
            RequiredString(root, "template_path", "request"),
            RequiredString(root, "expected_target_package_fingerprint", "request"),
            RequiredString(root, "expected_template_package_fingerprint", "request"),
            OptionalInt32(root, "max_items") ?? 50,
            OptionalBoolean(root, "include_issues") ?? true,
            OptionalBoolean(root, "include_dependencies") ?? false
        );
    }

    public static TemplateStyleAlignmentPlanRequest ParsePlanRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement, "request");
        RequireOnly(root, "target_path", "template_path",
            "expected_target_package_fingerprint",
            "expected_template_package_fingerprint", "commands", "include_details");
        return new TemplateStyleAlignmentPlanRequest(
            RequiredString(root, "target_path", "request"),
            RequiredString(root, "template_path", "request"),
            RequiredString(root, "expected_target_package_fingerprint", "request"),
            RequiredString(root, "expected_template_package_fingerprint", "request"),
            Commands(Required(root, "commands", "request")),
            OptionalBoolean(root, "include_details") ?? false
        );
    }

    public static TemplateStyleAlignmentApplyRequest ParseApplyRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement, "request");
        RequireOnly(root, "target_path", "template_path",
            "expected_target_package_fingerprint",
            "expected_template_package_fingerprint", "expected_plan_id", "commands",
            "keep_backup");
        return new TemplateStyleAlignmentApplyRequest(
            RequiredString(root, "target_path", "request"),
            RequiredString(root, "template_path", "request"),
            RequiredString(root, "expected_target_package_fingerprint", "request"),
            RequiredString(root, "expected_template_package_fingerprint", "request"),
            RequiredString(root, "expected_plan_id", "request"),
            Commands(Required(root, "commands", "request")),
            OptionalBoolean(root, "keep_backup") ?? true
        );
    }

    private static IReadOnlyList<TemplateStyleAlignmentCommandRequest> Commands(
        JsonElement element
    )
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("commands must be an array");
        }
        var items = element.EnumerateArray().ToArray();
        if (items.Length is < 1 or > TemplateStyleAlignmentWordPackageContract.MaximumCommands)
        {
            throw Invalid($"commands must contain between 1 and {TemplateStyleAlignmentWordPackageContract.MaximumCommands} items");
        }
        return items.Select((item, index) =>
        {
            var value = Object(item, $"commands[{index}]");
            RequireOnly(value, "candidate_id", "expected_candidate_fingerprint");
            return new TemplateStyleAlignmentCommandRequest(
                RequiredString(value, "candidate_id", $"commands[{index}]"),
                RequiredString(value, "expected_candidate_fingerprint", $"commands[{index}]")
            );
        }).ToArray();
    }

    private static JsonDocument ParseDocument(string json)
    {
        if (string.IsNullOrWhiteSpace(json)
            || json.Length > TemplateStyleAlignmentWordPackageContract.MaximumRequestJsonCharacters)
        {
            throw Invalid("request JSON must be non-empty and bounded");
        }
        try
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 24,
            });
        }
        catch (JsonException exception)
        {
            throw Invalid("request is not strict JSON", exception);
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> Object(
        JsonElement element,
        string name
    )
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{name} must be an object");
        }
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!result.TryAdd(property.Name, property.Value))
            {
                throw Invalid($"{name} contains duplicate field '{property.Name}'");
            }
        }
        return result;
    }

    private static void RequireOnly(
        IReadOnlyDictionary<string, JsonElement> value,
        params string[] allowed
    )
    {
        var set = allowed.ToHashSet(StringComparer.Ordinal);
        var unknown = value.Keys.FirstOrDefault(key => !set.Contains(key));
        if (unknown is not null)
        {
            throw Invalid($"unknown field '{unknown}'");
        }
    }

    private static JsonElement Required(
        IReadOnlyDictionary<string, JsonElement> value,
        string field,
        string owner
    ) => value.TryGetValue(field, out var element)
        ? element
        : throw Invalid($"{owner}.{field} is required");

    private static string RequiredString(
        IReadOnlyDictionary<string, JsonElement> value,
        string field,
        string owner
    )
    {
        var element = Required(value, field, owner);
        if (element.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"{owner}.{field} must be a string");
        }
        return element.GetString()!;
    }

    private static int? OptionalInt32(
        IReadOnlyDictionary<string, JsonElement> value,
        string field
    )
    {
        if (!value.TryGetValue(field, out var element))
        {
            return null;
        }
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var result))
        {
            throw Invalid($"{field} must be a 32-bit integer");
        }
        return result;
    }

    private static bool? OptionalBoolean(
        IReadOnlyDictionary<string, JsonElement> value,
        string field
    )
    {
        if (!value.TryGetValue(field, out var element))
        {
            return null;
        }
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Invalid($"{field} must be a boolean"),
        };
    }

    private static WordToolkitOperationException Invalid(
        string message,
        Exception? exception = null
    ) => new("INVALID_INPUT", message, innerException: exception);
}
