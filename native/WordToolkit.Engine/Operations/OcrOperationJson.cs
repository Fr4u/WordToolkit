using System.Text.Json;
using WordToolkit.Engine.Extensions;

namespace WordToolkit.Engine.Operations;

public static class OcrOperationJson
{
    public static OcrCandidateInspectionRequest ParseInspectRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement);
        RequireOnly(
            root,
            "local_path",
            "view",
            "expected_package_fingerprint",
            "candidate_id",
            "offset",
            "max_items",
            "include_hashes",
            "include_source"
        );
        return new OcrCandidateInspectionRequest(
            RequiredString(root, "local_path"),
            OptionalString(root, "view") ?? "candidates",
            OptionalString(root, "expected_package_fingerprint"),
            OptionalString(root, "candidate_id"),
            OptionalInt32(root, "offset") ?? 0,
            OptionalInt32(root, "max_items") ?? OcrWordPackageContract.DefaultMaxItems,
            OptionalBoolean(root, "include_hashes") ?? false,
            OptionalBoolean(root, "include_source") ?? false
        );
    }

    public static OcrRecognitionRequest ParseRecognizeRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement);
        RequireOnly(
            root,
            "local_path",
            "expected_package_fingerprint",
            "candidate_ids",
            "select_all_eligible",
            "provider_capability_id",
            "privacy_mode",
            "languages",
            "layout_hint",
            "provider_executable_path",
            "provider_model_directory",
            "timeout_milliseconds",
            "provider_output_characters",
            "detail",
            "include_text",
            "include_hashes",
            "max_returned_text_characters",
            "max_returned_lines",
            "max_returned_words_per_line",
            "minimum_mean_confidence"
        );
        return new OcrRecognitionRequest(
            RequiredString(root, "local_path"),
            RequiredString(root, "expected_package_fingerprint"),
            OptionalStringArray(root, "candidate_ids") ?? [],
            OptionalBoolean(root, "select_all_eligible") ?? false,
            OptionalString(root, "provider_capability_id")
                ?? OcrWordPackageContract.DefaultProviderCapabilityId,
            OptionalString(root, "privacy_mode") ?? "local_only",
            OptionalStringArray(root, "languages") ?? ["eng"],
            ParseLayoutHint(OptionalString(root, "layout_hint") ?? "automatic"),
            OptionalString(root, "provider_executable_path"),
            OptionalString(root, "provider_model_directory"),
            OptionalInt32(root, "timeout_milliseconds")
                ?? OcrWordPackageContract.DefaultTimeoutMilliseconds,
            OptionalInt32(root, "provider_output_characters")
                ?? OcrWordPackageContract.DefaultProviderOutputCharacters,
            OptionalString(root, "detail") ?? "summary",
            OptionalBoolean(root, "include_text") ?? false,
            OptionalBoolean(root, "include_hashes") ?? false,
            OptionalInt32(root, "max_returned_text_characters")
                ?? OcrWordPackageContract.DefaultReturnedTextCharacters,
            OptionalInt32(root, "max_returned_lines")
                ?? OcrWordPackageContract.DefaultReturnedLines,
            OptionalInt32(root, "max_returned_words_per_line")
                ?? OcrWordPackageContract.DefaultReturnedWordsPerLine,
            OptionalDouble(root, "minimum_mean_confidence")
        );
    }

    private static WordOcrLayoutHint ParseLayoutHint(string value) => value switch
    {
        "automatic" => WordOcrLayoutHint.Automatic,
        "single_block" => WordOcrLayoutHint.SingleBlock,
        "sparse_text" => WordOcrLayoutHint.SparseText,
        "single_line" => WordOcrLayoutHint.SingleLine,
        "single_word" => WordOcrLayoutHint.SingleWord,
        _ => throw Invalid("layout_hint is unsupported"),
    };

    private static JsonDocument ParseDocument(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw Invalid("Request JSON must be a non-empty object");
        }
        if (json.Length > OcrWordPackageContract.MaximumRequestJsonCharacters)
        {
            throw Invalid(
                $"Request JSON cannot exceed {OcrWordPackageContract.MaximumRequestJsonCharacters} characters"
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

    private static IReadOnlyList<string>? OptionalStringArray(
        IReadOnlyDictionary<string, JsonElement> item,
        string property
    )
    {
        if (!item.TryGetValue(property, out var value))
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"{property} must be an array of strings");
        }
        var result = new List<string>();
        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                throw Invalid($"{property} must contain only strings");
            }
            result.Add(element.GetString() ?? string.Empty);
        }
        return result;
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

    private static double? OptionalDouble(
        IReadOnlyDictionary<string, JsonElement> item,
        string property
    )
    {
        if (!item.TryGetValue(property, out var value))
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var result)
            || !double.IsFinite(result))
        {
            throw Invalid($"{property} must be a finite number");
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
