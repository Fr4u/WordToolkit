using System.Text.Json;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal static class JsonArguments
{
    public static string String(
        this JsonElement arguments,
        string name,
        string defaultValue = ""
    )
    {
        if (!arguments.TryGetProperty(name, out var value))
        {
            return defaultValue;
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid(name, "string");
        }
        return value.GetString() ?? defaultValue;
    }

    public static bool Boolean(
        this JsonElement arguments,
        string name,
        bool defaultValue
    )
    {
        if (!arguments.TryGetProperty(name, out var value))
        {
            return defaultValue;
        }
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw Invalid(name, "boolean");
        }
        return value.GetBoolean();
    }

    public static long? NullableInt64(this JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result))
        {
            throw Invalid(name, "integer or null");
        }
        return result;
    }

    public static double? NullableDouble(this JsonElement arguments, string name)
    {
        if (
            !arguments.TryGetProperty(name, out var value)
            || value.ValueKind == JsonValueKind.Null
        )
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var result))
        {
            throw Invalid(name, "number or null");
        }
        if (!double.IsFinite(result))
        {
            throw Invalid(name, "finite number or null");
        }
        return result;
    }

    public static JsonElement RequiredArray(this JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(name, "array");
        }
        return value;
    }

    public static JsonElement Required(this JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"Missing required argument: {name}"
            );
        }
        return value;
    }

    private static NativeToolException Invalid(string name, string expected)
    {
        return new NativeToolException(
            "INVALID_INPUT",
            $"Argument '{name}' must be {expected}"
        );
    }
}
