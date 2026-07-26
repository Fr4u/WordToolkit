using System.Text.Json;

namespace WordToolkit.Engine.Operations;

/// <summary>
/// Strict transport-neutral JSON codec shared by direct CLI and MCP adapters.
/// </summary>
public static class EquationParagraphRewriteOperationJson
{
    public static EquationParagraphRewriteInspectRequest ParseInspectRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement, "request");
        RequireOnly(
            root,
            "request",
            "local_path",
            "expected_package_fingerprint",
            "paragraph_node_id",
            "offset",
            "max_items",
            "include_text"
        );
        return new EquationParagraphRewriteInspectRequest(
            RequiredString(root, "local_path"),
            RequiredString(root, "expected_package_fingerprint"),
            OptionalString(root, "paragraph_node_id"),
            OptionalInt32(root, "offset") ?? 0,
            OptionalInt32(root, "max_items") ?? 25,
            OptionalBoolean(root, "include_text") ?? false
        );
    }

    public static EquationParagraphRewritePlanRequest ParsePlanRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement, "request");
        RequireOnly(
            root,
            "request",
            "local_path",
            "expected_package_fingerprint",
            "commands",
            "include_details"
        );
        return new EquationParagraphRewritePlanRequest(
            RequiredString(root, "local_path"),
            RequiredString(root, "expected_package_fingerprint"),
            Commands(root),
            OptionalBoolean(root, "include_details") ?? false
        );
    }

    public static EquationParagraphRewriteApplyRequest ParseApplyRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement, "request");
        RequireOnly(
            root,
            "request",
            "local_path",
            "expected_package_fingerprint",
            "expected_plan_id",
            "commands",
            "keep_backup"
        );
        return new EquationParagraphRewriteApplyRequest(
            RequiredString(root, "local_path"),
            RequiredString(root, "expected_package_fingerprint"),
            RequiredString(root, "expected_plan_id"),
            Commands(root),
            OptionalBoolean(root, "keep_backup") ?? true
        );
    }

    private static IReadOnlyList<RewriteEquationParagraphTextCommand> Commands(
        IReadOnlyDictionary<string, JsonElement> root
    )
    {
        var value = Required(root, "commands");
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("commands must be an array");
        }
        if (value.GetArrayLength() is < 1
            or > EquationParagraphRewriteWordPackageContract.MaximumCommands)
        {
            throw Invalid(
                $"commands must contain between 1 and {EquationParagraphRewriteWordPackageContract.MaximumCommands} paragraph rewrites"
            );
        }
        var result = new List<RewriteEquationParagraphTextCommand>(value.GetArrayLength());
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var name = $"commands[{index}]";
            var command = Object(item, name);
            RequireOnly(
                command,
                name,
                "type",
                "candidate_id",
                "expected_candidate_fingerprint",
                "replacement_text_slots"
            );
            if (!string.Equals(
                    RequiredString(command, "type"),
                    "rewrite_equation_paragraph_text",
                    StringComparison.Ordinal
                ))
            {
                throw Invalid(
                    "Equation paragraph command type must be rewrite_equation_paragraph_text"
                );
            }
            result.Add(new RewriteEquationParagraphTextCommand(
                RequiredString(command, "candidate_id"),
                RequiredString(command, "expected_candidate_fingerprint"),
                StringArray(command, "replacement_text_slots", name)
            ));
            index++;
        }
        return result;
    }

    private static IReadOnlyList<string> StringArray(
        IReadOnlyDictionary<string, JsonElement> item,
        string property,
        string name
    )
    {
        var value = Required(item, property);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"{name}.{property} must be an array of strings");
        }
        if (value.GetArrayLength()
            > EquationParagraphRewriteWordPackageContract.MaximumTextSlotsPerCommand)
        {
            throw Invalid(
                $"{name}.{property} cannot contain more than {EquationParagraphRewriteWordPackageContract.MaximumTextSlotsPerCommand} items"
            );
        }
        var result = new List<string>(value.GetArrayLength());
        foreach (var child in value.EnumerateArray())
        {
            if (child.ValueKind != JsonValueKind.String)
            {
                throw Invalid($"{name}.{property} must contain only strings");
            }
            result.Add(child.GetString() ?? string.Empty);
        }
        return result;
    }

    private static JsonDocument ParseDocument(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw Invalid("Request JSON must be a non-empty object");
        }
        if (json.Length
            > EquationParagraphRewriteWordPackageContract.MaximumRequestJsonCharacters)
        {
            throw Invalid(
                $"Request JSON cannot exceed {EquationParagraphRewriteWordPackageContract.MaximumRequestJsonCharacters} characters"
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
