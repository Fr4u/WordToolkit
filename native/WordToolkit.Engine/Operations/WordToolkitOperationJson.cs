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
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = indented,
        };
        options.Converters.Add(new StrictSnakeCaseEnumConverterFactory());
        return options;
    }

    private sealed class StrictSnakeCaseEnumConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

        public override JsonConverter CreateConverter(
            Type typeToConvert,
            JsonSerializerOptions options
        ) =>
            (JsonConverter)
                Activator.CreateInstance(
                    typeof(StrictSnakeCaseEnumConverter<>).MakeGenericType(typeToConvert)
                )!;
    }

    private sealed class StrictSnakeCaseEnumConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        private static readonly IReadOnlyDictionary<string, TEnum> ValuesByName =
            Enum.GetValues<TEnum>()
                .ToDictionary(
                    value => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()),
                    value => value,
                    StringComparer.Ordinal
                );

        private static readonly IReadOnlyDictionary<TEnum, string> NamesByValue =
            ValuesByName.ToDictionary(pair => pair.Value, pair => pair.Key);

        public override TEnum Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (
                reader.TokenType != JsonTokenType.String
                || !ValuesByName.TryGetValue(reader.GetString()!, out var value)
            )
            {
                throw new JsonException(
                    $"Expected an exact snake_case {typeof(TEnum).Name} value."
                );
            }
            return value;
        }

        public override void Write(
            Utf8JsonWriter writer,
            TEnum value,
            JsonSerializerOptions options
        )
        {
            if (!NamesByValue.TryGetValue(value, out var name))
            {
                throw new JsonException(
                    $"The {typeof(TEnum).Name} value is not defined."
                );
            }
            writer.WriteStringValue(name);
        }
    }
}
