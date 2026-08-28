using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal static class EquationPreflightIdentity
{
    internal static string FromInput(JsonElement equation)
    {
        var canonical = new JsonObject
        {
            ["value"] = equation.TryGetProperty("value", out var value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null,
            ["input_format"] = equation.TryGetProperty("input_format", out var format)
                && format.ValueKind == JsonValueKind.String
                    ? format.GetString()
                    : "latex",
            ["display"] = !equation.TryGetProperty("display", out var display)
                || display.ValueKind == JsonValueKind.True,
            ["verify_readback"] = equation.TryGetProperty(
                "verify_readback",
                out var verify
            ) && verify.ValueKind == JsonValueKind.True,
        };
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(canonical.ToJsonString(JsonDefaults.Compact))
        );
        return "weq_" + Convert.ToHexString(bytes.AsSpan(0, 12)).ToLowerInvariant();
    }
}
