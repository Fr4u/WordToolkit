using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WordToolkit.Engine.Operations;

public sealed record WordToolkitOperationError(
    string Code,
    string Message,
    string? Reason,
    bool Retryable
)
{
    public static WordToolkitOperationError FromException(
        WordToolkitOperationException exception
    )
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new WordToolkitOperationError(
            exception.Code,
            exception.Message,
            exception.Reason,
            exception.Retryable
        );
    }
}

public sealed record WordToolkitOperationErrorEnvelope(
    bool Ok,
    WordToolkitOperationError Error
);

public static class WordToolkitOperationJson
{
    private static readonly JsonSerializerOptions Compact = Create(indented: false);
    private static readonly JsonSerializerOptions Indented = Create(indented: true);

    public static string Serialize<T>(T value, bool indented = false)
    {
        return JsonSerializer.Serialize(value, indented ? Indented : Compact);
    }

    public static JsonNode? SerializeToNode<T>(T value)
    {
        return JsonSerializer.SerializeToNode(value, Compact);
    }

    public static T Deserialize<T>(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<T>(json, Compact)
            ?? throw new JsonException("The operation JSON payload is null.");
    }

    private static JsonSerializerOptions Create(bool indented)
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = indented,
        };
    }
}
