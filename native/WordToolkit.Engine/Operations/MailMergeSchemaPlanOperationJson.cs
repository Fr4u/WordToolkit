using System.Text.Json;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Operations;

public static class MailMergeSchemaPlanOperationJson
{
    private static readonly IReadOnlyDictionary<string, WordMailMergeSourceDataKind>
        DataKinds = new Dictionary<string, WordMailMergeSourceDataKind>(StringComparer.Ordinal)
        {
            ["unspecified"] = WordMailMergeSourceDataKind.Unspecified,
            ["text"] = WordMailMergeSourceDataKind.Text,
            ["number"] = WordMailMergeSourceDataKind.Number,
            ["date_time"] = WordMailMergeSourceDataKind.DateTime,
            ["boolean"] = WordMailMergeSourceDataKind.Boolean,
            ["binary"] = WordMailMergeSourceDataKind.Binary,
        };

    public static MailMergeSchemaPlanRequest ParseRequest(string json)
    {
        using var document = ParseDocument(json);
        var root = Object(document.RootElement, "Request");
        RequireOnly(
            root,
            "local_path",
            "expected_package_fingerprint",
            "source_columns"
        );
        var columns = Required(root, "source_columns");
        if (columns.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("source_columns must be an array");
        }
        if (columns.GetArrayLength() > WordMailMergeSchemaPlannerOptions.Default.MaxSourceColumns)
        {
            throw new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                $"source_columns cannot exceed {WordMailMergeSchemaPlannerOptions.Default.MaxSourceColumns} items"
            );
        }
        var parsed = new List<WordMailMergeSourceColumn>(columns.GetArrayLength());
        var ordinal = 0;
        foreach (var item in columns.EnumerateArray())
        {
            var column = Object(item, $"source_columns[{ordinal}]");
            RequireOnly(column, "name", "data_kind");
            var dataKindName = OptionalString(column, "data_kind") ?? "unspecified";
            if (!DataKinds.TryGetValue(dataKindName, out var dataKind))
            {
                throw Invalid(
                    $"source_columns[{ordinal}].data_kind must be unspecified, text, number, date_time, boolean, or binary"
                );
            }
            parsed.Add(new WordMailMergeSourceColumn(
                RequiredString(column, "name"),
                dataKind
            ));
            ordinal++;
        }
        return new MailMergeSchemaPlanRequest(
            RequiredString(root, "local_path"),
            RequiredString(root, "expected_package_fingerprint"),
            parsed
        );
    }

    private static JsonDocument ParseDocument(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw Invalid("Request JSON must be a non-empty object");
        }
        if (json.Length > MailMergeSchemaPlanWordPackageContract.MaximumRequestJsonCharacters)
        {
            throw Invalid(
                $"Request JSON cannot exceed {MailMergeSchemaPlanWordPackageContract.MaximumRequestJsonCharacters} characters"
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
        string location
    )
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{location} must be an object");
        }
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!result.TryAdd(property.Name, property.Value))
            {
                throw Invalid($"{location} contains duplicate field '{property.Name}'");
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

    private static WordToolkitOperationException Invalid(
        string message,
        Exception? innerException = null
    ) => new("INVALID_INPUT", message, innerException: innerException);
}
