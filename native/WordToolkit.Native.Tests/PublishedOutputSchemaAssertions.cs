using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WordToolkit.Native.Tests;

internal static class PublishedOutputSchemaAssertions
{
    public static void AssertConforms(
        JsonNode? actual,
        JsonObject schema,
        JsonObject rootSchema,
        string path = "$"
    )
    {
        if (schema["$ref"]?.GetValue<string>() is { } reference)
        {
            const string definitionPrefix = "#/$defs/";
            Assert.StartsWith(definitionPrefix, reference, StringComparison.Ordinal);
            var definitionName = reference[definitionPrefix.Length..];
            AssertConforms(
                actual,
                rootSchema["$defs"]![definitionName]!.AsObject(),
                rootSchema,
                path
            );
            return;
        }

        if (schema["const"] is { } constant)
        {
            Assert.True(
                JsonNode.DeepEquals(actual, constant),
                $"{path} does not equal its published const value"
            );
        }
        if (schema["enum"] is JsonArray allowed)
        {
            Assert.Contains(allowed, candidate => JsonNode.DeepEquals(actual, candidate));
        }

        var declaredType = ResolveDeclaredType(actual, schema, path);
        if (declaredType is null)
        {
            return;
        }
        switch (declaredType)
        {
            case "object":
                AssertObject(actual, schema, rootSchema, path);
                break;
            case "array":
                AssertArray(actual, schema, rootSchema, path);
                break;
            case "string":
                AssertString(actual, schema, path);
                break;
            case "integer":
                AssertInteger(actual, schema, path);
                break;
            case "boolean":
                _ = actual!.GetValue<bool>();
                break;
        }
    }

    private static string? ResolveDeclaredType(
        JsonNode? actual,
        JsonObject schema,
        string path
    )
    {
        if (schema["type"] is JsonValue single)
        {
            return single.GetValue<string>();
        }
        if (schema["type"] is not JsonArray alternatives)
        {
            return null;
        }
        var allowed = alternatives.Select(item => item!.GetValue<string>()).ToHashSet();
        if (actual is null)
        {
            Assert.Contains("null", allowed);
            return null;
        }
        var actualType = actual switch
        {
            JsonObject => "object",
            JsonArray => "array",
            JsonValue value when value.TryGetValue<bool>(out _) => "boolean",
            JsonValue value when value.TryGetValue<long>(out _) => "integer",
            JsonValue value when value.TryGetValue<double>(out _) => "number",
            JsonValue value when value.TryGetValue<string>(out _) => "string",
            _ => throw new Xunit.Sdk.XunitException($"{path} has an unknown JSON type"),
        };
        Assert.Contains(actualType, allowed);
        return actualType;
    }

    private static void AssertObject(
        JsonNode? actual,
        JsonObject schema,
        JsonObject rootSchema,
        string path
    )
    {
        var obj = Assert.IsType<JsonObject>(actual);
        var declared = schema["properties"] as JsonObject ?? new JsonObject();
        if (schema["required"] is JsonArray required)
        {
            foreach (var item in required)
            {
                var requiredName = item!.GetValue<string>();
                Assert.True(
                    obj.ContainsKey(requiredName),
                    $"{path} is missing required property '{requiredName}'"
                );
            }
        }
        if (schema["maxProperties"]?.GetValue<int>() is { } maxProperties)
        {
            Assert.True(obj.Count <= maxProperties, $"{path} has too many properties");
        }
        foreach (var property in obj)
        {
            if (declared[property.Key] is JsonObject propertySchema)
            {
                AssertConforms(
                    property.Value,
                    propertySchema,
                    rootSchema,
                    $"{path}.{property.Key}"
                );
                continue;
            }
            if (schema["additionalProperties"] is JsonObject additionalSchema)
            {
                AssertConforms(
                    property.Value,
                    additionalSchema,
                    rootSchema,
                    $"{path}.{property.Key}"
                );
                continue;
            }
            Assert.False(
                schema["additionalProperties"]?.GetValue<bool>() == false,
                $"{path} contains undeclared property '{property.Key}'"
            );
        }
    }

    private static void AssertArray(
        JsonNode? actual,
        JsonObject schema,
        JsonObject rootSchema,
        string path
    )
    {
        var array = Assert.IsType<JsonArray>(actual);
        if (schema["maxItems"]?.GetValue<int>() is { } maxItems)
        {
            Assert.True(array.Count <= maxItems, $"{path} has too many items");
        }
        if (schema["uniqueItems"]?.GetValue<bool>() == true)
        {
            Assert.Equal(
                array.Count,
                array.Select(item => item?.ToJsonString() ?? "null").Distinct().Count()
            );
        }
        if (schema["items"] is not JsonObject itemSchema)
        {
            return;
        }
        for (var index = 0; index < array.Count; index++)
        {
            AssertConforms(array[index], itemSchema, rootSchema, $"{path}[{index}]");
        }
    }

    private static void AssertString(JsonNode? actual, JsonObject schema, string path)
    {
        var value = actual!.GetValue<string>();
        if (schema["maxLength"]?.GetValue<int>() is { } maxLength)
        {
            Assert.True(value.Length <= maxLength, $"{path} is too long");
        }
        if (schema["pattern"]?.GetValue<string>() is { } pattern)
        {
            Assert.Matches(new Regex(pattern, RegexOptions.CultureInvariant), value);
        }
    }

    private static void AssertInteger(JsonNode? actual, JsonObject schema, string path)
    {
        var value = actual!.GetValue<long>();
        if (schema["minimum"]?.GetValue<long>() is { } minimum)
        {
            Assert.True(value >= minimum, $"{path} is below its minimum");
        }
        if (schema["maximum"]?.GetValue<long>() is { } maximum)
        {
            Assert.True(value <= maximum, $"{path} exceeds its maximum");
        }
    }
}
