using System.Text.Json;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Operations;

public static class NumberingRebuildOperationJson
{
    public static NumberingRebuildInspectRequest ParseInspectRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement);
        RequireOnly(
            root,
            "local_path",
            "expected_package_fingerprint",
            "paragraph_node_ids"
        );
        return new NumberingRebuildInspectRequest(
            RequiredString(root, "local_path"),
            RequiredString(root, "expected_package_fingerprint"),
            RequiredArray(root, "paragraph_node_ids").Select((item, index) =>
                String(item, $"paragraph_node_ids[{index}]")
            ).ToArray()
        );
    }

    public static NumberingRebuildPlanRequest ParsePlanRequest(string json)
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
        return new NumberingRebuildPlanRequest(
            RequiredString(root, "local_path"),
            RequiredString(root, "expected_package_fingerprint"),
            ParseCommands(RequiredArray(root, "commands")),
            OptionalBoolean(root, "include_details") ?? false
        );
    }

    public static NumberingRebuildApplyRequest ParseApplyRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement);
        RequireOnly(
            root,
            "local_path",
            "expected_package_fingerprint",
            "expected_plan_id",
            "commands",
            "keep_backup"
        );
        return new NumberingRebuildApplyRequest(
            RequiredString(root, "local_path"),
            RequiredString(root, "expected_package_fingerprint"),
            RequiredString(root, "expected_plan_id"),
            ParseCommands(RequiredArray(root, "commands")),
            OptionalBoolean(root, "keep_backup") ?? true
        );
    }

    private static IReadOnlyList<WordNumberingRebuildCommand> ParseCommands(
        IReadOnlyList<JsonElement> items
    ) => items.Select((item, index) => ParseCommand(item, index)).ToArray();

    private static WordNumberingRebuildCommand ParseCommand(JsonElement value, int index)
    {
        var path = $"commands[{index}]";
        var item = Object(value, path);
        RequireOnly(
            item,
            "command_id",
            "multi_level_kind",
            "restart_after_section_break",
            "levels",
            "targets"
        );
        return new WordNumberingRebuildCommand(
            RequiredString(item, "command_id", path),
            ParseMultiLevelKind(RequiredString(item, "multi_level_kind", path), path),
            RequiredBoolean(item, "restart_after_section_break", path),
            RequiredArray(item, "levels", path).Select((level, levelIndex) =>
                ParseLevel(level, index, levelIndex)
            ).ToArray(),
            RequiredArray(item, "targets", path).Select((target, targetIndex) =>
                ParseTarget(target, index, targetIndex)
            ).ToArray()
        );
    }

    private static WordNumberingRebuildLevel ParseLevel(
        JsonElement value,
        int commandIndex,
        int index
    )
    {
        var path = $"commands[{commandIndex}].levels[{index}]";
        var item = Object(value, path);
        RequireOnly(
            item,
            "level_index",
            "start_value",
            "number_format",
            "level_text",
            "restart_mode",
            "restart_trigger_level",
            "is_legal",
            "suffix",
            "justification",
            "left_indent_twips",
            "hanging_indent_twips",
            "tab_stop_twips"
        );
        return new WordNumberingRebuildLevel(
            RequiredInt32(item, "level_index", path),
            RequiredInt32(item, "start_value", path),
            ParseNumberFormat(RequiredString(item, "number_format", path), path),
            RequiredString(item, "level_text", path),
            ParseRestartMode(OptionalString(item, "restart_mode", path)
                ?? "default_previous_level", path),
            OptionalInt32(item, "restart_trigger_level", path),
            OptionalBoolean(item, "is_legal", path) ?? false,
            ParseSuffix(OptionalString(item, "suffix", path) ?? "tab", path),
            ParseJustification(
                OptionalString(item, "justification", path) ?? "left",
                path
            ),
            OptionalInt32(item, "left_indent_twips", path),
            OptionalInt32(item, "hanging_indent_twips", path),
            OptionalInt32(item, "tab_stop_twips", path)
        );
    }

    private static WordNumberingRebuildTarget ParseTarget(
        JsonElement value,
        int commandIndex,
        int index
    )
    {
        var path = $"commands[{commandIndex}].targets[{index}]";
        var item = Object(value, path);
        RequireOnly(
            item,
            "paragraph_node_id",
            "expected_candidate_fingerprint",
            "level_index"
        );
        return new WordNumberingRebuildTarget(
            new SemanticNodeId(RequiredString(item, "paragraph_node_id", path)),
            RequiredString(item, "expected_candidate_fingerprint", path),
            RequiredInt32(item, "level_index", path)
        );
    }

    private static WordNumberingRebuildMultiLevelKind ParseMultiLevelKind(
        string value,
        string path
    ) => value switch
    {
        "single_level" => WordNumberingRebuildMultiLevelKind.SingleLevel,
        "multilevel" => WordNumberingRebuildMultiLevelKind.Multilevel,
        "hybrid_multilevel" => WordNumberingRebuildMultiLevelKind.HybridMultilevel,
        _ => throw Invalid($"{path}.multi_level_kind is unsupported"),
    };

    private static WordNumberingRebuildFormat ParseNumberFormat(
        string value,
        string path
    ) => value switch
    {
        "decimal" => WordNumberingRebuildFormat.Decimal,
        "decimal_zero" => WordNumberingRebuildFormat.DecimalZero,
        "upper_roman" => WordNumberingRebuildFormat.UpperRoman,
        "lower_roman" => WordNumberingRebuildFormat.LowerRoman,
        "upper_letter" => WordNumberingRebuildFormat.UpperLetter,
        "lower_letter" => WordNumberingRebuildFormat.LowerLetter,
        "bullet" => WordNumberingRebuildFormat.Bullet,
        "none" => WordNumberingRebuildFormat.None,
        _ => throw Invalid($"{path}.number_format is unsupported"),
    };

    private static WordNumberingRebuildRestartMode ParseRestartMode(
        string value,
        string path
    ) => value switch
    {
        "default_previous_level" => WordNumberingRebuildRestartMode.DefaultPreviousLevel,
        "never" => WordNumberingRebuildRestartMode.Never,
        "after_level" => WordNumberingRebuildRestartMode.AfterLevel,
        _ => throw Invalid($"{path}.restart_mode is unsupported"),
    };

    private static WordNumberingRebuildSuffix ParseSuffix(string value, string path) =>
        value switch
        {
            "tab" => WordNumberingRebuildSuffix.Tab,
            "space" => WordNumberingRebuildSuffix.Space,
            "nothing" => WordNumberingRebuildSuffix.Nothing,
            _ => throw Invalid($"{path}.suffix is unsupported"),
        };

    private static WordNumberingRebuildJustification ParseJustification(
        string value,
        string path
    ) => value switch
    {
        "left" => WordNumberingRebuildJustification.Left,
        "center" => WordNumberingRebuildJustification.Center,
        "right" => WordNumberingRebuildJustification.Right,
        _ => throw Invalid($"{path}.justification is unsupported"),
    };

    private static JsonDocument ParseDocument(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw Invalid("Request JSON must be a non-empty object");
        }
        if (json.Length > NumberingRebuildWordPackageContract.MaximumRequestJsonCharacters)
        {
            throw Invalid(
                $"Request JSON cannot exceed {NumberingRebuildWordPackageContract.MaximumRequestJsonCharacters} characters"
            );
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
            throw Invalid("Request JSON is malformed or exceeds the depth limit", exception);
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> Object(
        JsonElement value,
        string path = "request"
    )
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{path} must be an object");
        }
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!result.TryAdd(property.Name, property.Value))
            {
                throw Invalid($"{path} contains duplicate field '{property.Name}'");
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
        string property,
        string path = "request"
    ) => item.TryGetValue(property, out var value)
        ? value
        : throw Invalid($"Missing required field '{path}.{property}'");

    private static string RequiredString(
        IReadOnlyDictionary<string, JsonElement> item,
        string property,
        string path = "request"
    ) => String(Required(item, property, path), $"{path}.{property}");

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
        string property,
        string path
    ) => item.TryGetValue(property, out var value)
        ? String(value, $"{path}.{property}")
        : null;

    private static int RequiredInt32(
        IReadOnlyDictionary<string, JsonElement> item,
        string property,
        string path
    ) => Int32(Required(item, property, path), $"{path}.{property}");

    private static int? OptionalInt32(
        IReadOnlyDictionary<string, JsonElement> item,
        string property,
        string path
    ) => item.TryGetValue(property, out var value)
        ? Int32(value, $"{path}.{property}")
        : null;

    private static int Int32(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            throw Invalid($"{path} must be a 32-bit integer");
        }
        return result;
    }

    private static bool RequiredBoolean(
        IReadOnlyDictionary<string, JsonElement> item,
        string property,
        string path
    ) => Boolean(Required(item, property, path), $"{path}.{property}");

    private static bool? OptionalBoolean(
        IReadOnlyDictionary<string, JsonElement> item,
        string property,
        string path = "request"
    ) => item.TryGetValue(property, out var value)
        ? Boolean(value, $"{path}.{property}")
        : null;

    private static bool Boolean(JsonElement value, string path) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw Invalid($"{path} must be a boolean"),
    };

    private static IReadOnlyList<JsonElement> RequiredArray(
        IReadOnlyDictionary<string, JsonElement> item,
        string property,
        string path = "request"
    )
    {
        var value = Required(item, property, path);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"{path}.{property} must be an array");
        }
        return value.EnumerateArray().ToArray();
    }

    private static WordToolkitOperationException Invalid(
        string message,
        Exception? innerException = null
    ) => new("INVALID_INPUT", message, innerException: innerException);
}
