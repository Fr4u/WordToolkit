using System.Text.Json;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Operations;

/// <summary>
/// Strict transport-neutral JSON codec for the style-edit operation. The same parser is
/// used by CLI and MCP, so unknown or variant-specific fields cannot drift by adapter.
/// </summary>
public static class StyleEditOperationJson
{
    public static StyleEditPlanRequest ParsePlanRequest(string json)
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
        return new StyleEditPlanRequest(
            RequiredString(root, "local_path"),
            RequiredString(root, "expected_package_fingerprint"),
            Commands(root),
            OptionalBoolean(root, "include_details") ?? false
        );
    }

    public static StyleEditApplyRequest ParseApplyRequest(string json)
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
        return new StyleEditApplyRequest(
            RequiredString(root, "local_path"),
            RequiredString(root, "expected_package_fingerprint"),
            RequiredString(root, "expected_plan_id"),
            Commands(root),
            OptionalBoolean(root, "keep_backup") ?? true
        );
    }

    private static JsonDocument ParseDocument(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw Invalid("Request JSON must be a non-empty object");
        }
        if (json.Length > StyleWordPackageContract.MaximumRequestJsonCharacters)
        {
            throw Invalid(
                $"Request JSON cannot exceed {StyleWordPackageContract.MaximumRequestJsonCharacters} characters"
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
                    MaxDepth = 32,
                }
            );
        }
        catch (JsonException exception)
        {
            throw Invalid("Request JSON is malformed or exceeds the depth limit", exception);
        }
    }

    private static IReadOnlyList<StyleEditCommand> Commands(
        IReadOnlyDictionary<string, JsonElement> root
    )
    {
        var value = Required(root, "commands");
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("commands must be an array");
        }
        if (value.GetArrayLength() is < 1 or > StyleWordPackageContract.MaximumCommands)
        {
            throw Invalid(
                $"commands must contain between 1 and {StyleWordPackageContract.MaximumCommands} semantic edits"
            );
        }

        var result = new List<StyleEditCommand>(value.GetArrayLength());
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            result.Add(ParseCommand(Object(item, $"commands[{index}]"), index));
            index++;
        }
        return result;
    }

    private static StyleEditCommand ParseCommand(
        IReadOnlyDictionary<string, JsonElement> item,
        int index
    )
    {
        var name = $"commands[{index}]";
        return RequiredString(item, "type") switch
        {
            "create_style" => ParseCreateStyle(item, name),
            "clone_style" => ParseCloneStyle(item, name),
            "consolidate_style" => ParseConsolidateStyle(item, name),
            "delete_unused_style" => ParseDeleteStyle(item, name),
            "rename_style" => ParseRenameStyle(item, name),
            "set_style" => ParseSetStyle(item, name),
            "set_style_where" => ParseSetStyleWhere(item, name),
            _ => throw Invalid(
                "Semantic edit command type must be create_style, clone_style, consolidate_style, delete_unused_style, rename_style, set_style, or set_style_where"
            ),
        };
    }

    private static CreateStyleEditCommand ParseCreateStyle(
        IReadOnlyDictionary<string, JsonElement> item,
        string name
    )
    {
        RequireOnly(
            item,
            name,
            "type",
            "style_id",
            "name",
            "style_type",
            "based_on_style_id",
            "next_style_id",
            "quick_format",
            "ui_priority"
        );
        return new CreateStyleEditCommand(
            RequiredString(item, "style_id"),
            RequiredString(item, "name"),
            ParseStyleType(RequiredString(item, "style_type")),
            OptionalString(item, "based_on_style_id"),
            OptionalString(item, "next_style_id"),
            OptionalBoolean(item, "quick_format"),
            OptionalInt32(item, "ui_priority")
        );
    }

    private static CloneStyleEditCommand ParseCloneStyle(
        IReadOnlyDictionary<string, JsonElement> item,
        string name
    )
    {
        RequireOnly(item, name, "type", "source_style_id", "style_id", "name");
        return new CloneStyleEditCommand(
            RequiredString(item, "source_style_id"),
            RequiredString(item, "style_id"),
            RequiredString(item, "name")
        );
    }

    private static ConsolidateStyleEditCommand ParseConsolidateStyle(
        IReadOnlyDictionary<string, JsonElement> item,
        string name
    )
    {
        RequireOnly(item, name, "type", "source_style_id", "target_style_id");
        return new ConsolidateStyleEditCommand(
            RequiredString(item, "source_style_id"),
            RequiredString(item, "target_style_id")
        );
    }

    private static DeleteUnusedStyleEditCommand ParseDeleteStyle(
        IReadOnlyDictionary<string, JsonElement> item,
        string name
    )
    {
        RequireOnly(item, name, "type", "style_id");
        return new DeleteUnusedStyleEditCommand(RequiredString(item, "style_id"));
    }

    private static RenameStyleEditCommand ParseRenameStyle(
        IReadOnlyDictionary<string, JsonElement> item,
        string name
    )
    {
        RequireOnly(item, name, "type", "style_id", "name");
        return new RenameStyleEditCommand(
            RequiredString(item, "style_id"),
            RequiredString(item, "name")
        );
    }

    private static SetStyleEditCommand ParseSetStyle(
        IReadOnlyDictionary<string, JsonElement> item,
        string name
    )
    {
        RequireOnly(
            item,
            name,
            "type",
            "node_id",
            "style_id",
            "expected_style_id",
            "require_no_explicit_style"
        );
        return new SetStyleEditCommand(
            RequiredString(item, "node_id"),
            RequiredString(item, "style_id"),
            OptionalString(item, "expected_style_id"),
            OptionalBoolean(item, "require_no_explicit_style") ?? false
        );
    }

    private static SetStyleWhereEditCommand ParseSetStyleWhere(
        IReadOnlyDictionary<string, JsonElement> item,
        string name
    )
    {
        RequireOnly(
            item,
            name,
            "type",
            "selector",
            "style_id",
            "expected_style_id",
            "require_no_explicit_style",
            "max_matches"
        );
        return new SetStyleWhereEditCommand(
            ParseSelector(Object(Required(item, "selector"), $"{name}.selector")),
            RequiredString(item, "style_id"),
            RequiredInt32(item, "max_matches"),
            OptionalString(item, "expected_style_id"),
            OptionalBoolean(item, "require_no_explicit_style") ?? false
        );
    }

    private static StyleEditSelector ParseSelector(
        IReadOnlyDictionary<string, JsonElement> item
    )
    {
        RequireOnly(
            item,
            "selector",
            "kind",
            "text",
            "text_match",
            "text_scope",
            "case_sensitive",
            "property_equals",
            "ancestor",
            "descendant",
            "within_node_id",
            "source_part_uri"
        );
        return new StyleEditSelector
        {
            Kind = ParseSemanticKind(RequiredString(item, "kind")),
            Text = OptionalString(item, "text"),
            TextMatch = OptionalString(item, "text_match") switch
            {
                null or "contains" => WordSemanticTextMatchMode.Contains,
                "equals" => WordSemanticTextMatchMode.Equals,
                "starts_with" => WordSemanticTextMatchMode.StartsWith,
                "ends_with" => WordSemanticTextMatchMode.EndsWith,
                _ => throw Invalid(
                    "selector.text_match must be contains, equals, starts_with, or ends_with"
                ),
            },
            TextScope = OptionalString(item, "text_scope") switch
            {
                null or "node" => WordSemanticTextScope.Node,
                "subtree" => WordSemanticTextScope.Subtree,
                _ => throw Invalid("selector.text_scope must be node or subtree"),
            },
            CaseSensitive = OptionalBoolean(item, "case_sensitive") ?? false,
            PropertyEquals = OptionalStringMap(item, "property_equals"),
            Ancestor = OptionalRelated(item, "ancestor"),
            Descendant = OptionalRelated(item, "descendant"),
            WithinNodeId = OptionalString(item, "within_node_id"),
            SourcePartUri = OptionalString(item, "source_part_uri"),
        };
    }

    private static StyleEditRelatedPredicate? OptionalRelated(
        IReadOnlyDictionary<string, JsonElement> item,
        string property
    )
    {
        if (!item.TryGetValue(property, out var value))
        {
            return null;
        }
        var related = Object(value, $"selector.{property}");
        RequireOnly(related, $"selector.{property}", "kind", "property_equals");
        return new StyleEditRelatedPredicate
        {
            Kind = OptionalString(related, "kind") is { } kind
                ? ParseSemanticKind(kind)
                : null,
            PropertyEquals = OptionalStringMap(related, "property_equals"),
        };
    }

    private static IReadOnlyDictionary<string, string>? OptionalStringMap(
        IReadOnlyDictionary<string, JsonElement> item,
        string property
    )
    {
        if (!item.TryGetValue(property, out var value))
        {
            return null;
        }
        var source = Object(value, property);
        var result = new Dictionary<string, string>(source.Count, StringComparer.Ordinal);
        foreach (var (key, node) in source)
        {
            if (node.ValueKind != JsonValueKind.String)
            {
                throw Invalid($"{property} values must be strings");
            }
            result.Add(key, node.GetString() ?? string.Empty);
        }
        return result;
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

    private static int RequiredInt32(
        IReadOnlyDictionary<string, JsonElement> item,
        string property
    ) => OptionalInt32(item, property)
        ?? throw Invalid($"Missing required field '{property}'");

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

    private static WordStyleType ParseStyleType(string value) => value switch
    {
        "paragraph" => WordStyleType.Paragraph,
        "character" => WordStyleType.Character,
        "table" => WordStyleType.Table,
        "numbering" => WordStyleType.Numbering,
        _ => throw Invalid(
            "style_type must be paragraph, character, table, or numbering"
        ),
    };

    private static WordSemanticNodeKind ParseSemanticKind(string value)
    {
        foreach (var kind in Enum.GetValues<WordSemanticNodeKind>())
        {
            if (
                string.Equals(
                    SnakeCase(kind.ToString()),
                    value,
                    StringComparison.Ordinal
                )
            )
            {
                return kind;
            }
        }
        throw Invalid($"'{value}' is not a known semantic node kind");
    }

    private static string SnakeCase(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (
                index > 0
                && char.IsUpper(character)
                && (
                    char.IsLower(value[index - 1])
                    || index + 1 < value.Length && char.IsLower(value[index + 1])
                )
            )
            {
                builder.Append('_');
            }
            builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }

    private static WordToolkitOperationException Invalid(
        string message,
        Exception? innerException = null
    ) => new("INVALID_INPUT", message, innerException: innerException);
}
