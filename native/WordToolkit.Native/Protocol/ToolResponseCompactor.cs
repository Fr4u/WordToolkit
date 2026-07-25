using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WordToolkit.Native.Protocol;

internal static class ToolResponseCompactor
{
    private static readonly HashSet<string> BatchMutationTools =
    [
        "apply_live_word_operations",
        "insert_live_word_equation",
        "insert_live_word_equations_batch",
    ];

    public static JsonNode Compact(string toolName, object value)
    {
        var node = JsonSerializer.SerializeToNode(value, JsonDefaults.Compact)
            ?? new JsonObject();
        if (
            node is JsonObject preflight
            && toolName == "preflight_live_word_equations"
        )
        {
            return CompactEquationPreflight(preflight);
        }
        if (node is JsonObject obj && BatchMutationTools.Contains(toolName))
        {
            return CompactBatchMutation(obj);
        }
        StripNoise(node);
        return node;
    }

    private static JsonObject CompactEquationPreflight(JsonObject source)
    {
        var result = new JsonObject();
        Copy(source, result, "valid");
        Copy(source, result, "conversion_valid");
        Copy(source, result, "native_execution_verified");
        Copy(source, result, "validation_mode");
        Copy(source, result, "equation_count");
        var items = new JsonArray();
        var required = 0;
        var enabled = 0;
        var styleRequired = 0;
        if (source["equations"] is JsonArray equations)
        {
            foreach (var node in equations)
            {
                if (node is not JsonObject equation)
                {
                    continue;
                }
                var item = new JsonObject();
                Copy(equation, item, "index");
                Copy(equation, item, "valid");
                Copy(equation, item, "conversion_valid");
                Copy(equation, item, "native_execution_verified");
                Copy(equation, item, "input_format");
                Copy(equation, item, "display");
                Copy(equation, item, "native_readback_required");
                Copy(equation, item, "native_readback_enabled");
                Copy(equation, item, "native_readback_verified");
                var linear = equation["word_linear"]?.GetValue<string>() ?? "";
                item["word_linear_characters"] = equation["word_linear_characters"]
                    ?.DeepClone()
                    ?? JsonValue.Create(linear.Length);
                item["word_linear_sha256"] = Convert.ToHexString(
                        SHA256.HashData(Encoding.UTF8.GetBytes(linear))
                    )
                    .ToLowerInvariant()[..16];
                if (
                    equation["native_readback_required"]?.GetValue<bool>() == true
                )
                {
                    required++;
                }
                if (
                    equation["native_readback_enabled"]?.GetValue<bool>() == true
                )
                {
                    enabled++;
                }
                if (
                    equation["native_style_rewrite_required"]?.GetValue<bool>() == true
                )
                {
                    styleRequired++;
                    item["native_style_rewrite_required"] = true;
                    Copy(equation, item, "formatting_region_count");
                }
                items.Add(item);
            }
        }
        result["native_readback_required_count"] = required;
        result["native_readback_enabled_count"] = enabled;
        result["word_linear_returned"] = false;
        if (styleRequired > 0)
        {
            result["native_style_rewrite_required_count"] = styleRequired;
        }
        result["equations"] = items;
        return result;
    }

    private static JsonObject CompactBatchMutation(JsonObject source)
    {
        var result = new JsonObject();
        Copy(source, result, "live_document_id");
        Copy(source, result, "live_version");
        Copy(source, result, "operation_count");
        Copy(source, result, "text_operation_count");
        Copy(source, result, "equation_operation_count");
        result["native_verified"] = true;
        if (source["operations"] is JsonArray operations)
        {
            var styleVerified = 0;
            var formattingRegions = 0;
            foreach (var operation in operations.OfType<JsonObject>())
            {
                if (
                    operation["equation"] is not JsonObject equation
                    || equation["native_style_verified"]?.GetValue<bool>() != true
                )
                {
                    continue;
                }
                styleVerified++;
                if (equation["formatting"] is JsonObject formatting)
                {
                    formattingRegions +=
                        formatting["region_count"]?.GetValue<int>() ?? 0;
                }
            }
            if (styleVerified > 0)
            {
                result["native_style_verified"] = true;
                result["native_style_verified_count"] = styleVerified;
                result["formatting_region_count"] = formattingRegions;
            }
        }
        if (source["document"] is JsonObject document)
        {
            result["document"] = CompactDocument(document);
        }
        return result;
    }

    private static void StripNoise(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                StripNoise(item);
            }
            return;
        }
        if (node is not JsonObject obj)
        {
            return;
        }

        obj.Remove("performance");
        obj.Remove("runtime");
        obj.Remove("python_used");
        obj.Remove("content_returned");
        if (obj["warnings"] is JsonArray warnings && warnings.Count == 0)
        {
            obj.Remove("warnings");
        }
        if (obj["issues_truncated"]?.GetValue<bool>() == false)
        {
            obj.Remove("issues_truncated");
        }
        if (obj["rules"] is JsonArray)
        {
            obj.Remove("rules");
        }
        if (obj["source_diagnostics"] is JsonObject diagnostics)
        {
            foreach (var item in diagnostics.ToArray())
            {
                if (
                    item.Value is JsonValue value
                    && value.TryGetValue<int>(out var count)
                    && count == 0
                )
                {
                    diagnostics.Remove(item.Key);
                }
            }
            if (diagnostics.Count == 0)
            {
                obj.Remove("source_diagnostics");
            }
        }
        if (obj["native_readback_required"]?.GetValue<bool>() == false)
        {
            obj.Remove("native_readback_required");
        }
        if (obj["mutated_word"]?.GetValue<bool>() == false)
        {
            obj.Remove("mutated_word");
        }
        if (obj["document"] is JsonObject document)
        {
            obj["document"] = CompactDocument(document);
        }
        foreach (var child in obj.ToArray())
        {
            StripNoise(child.Value);
        }
    }

    private static JsonObject CompactDocument(JsonObject source)
    {
        var result = new JsonObject();
        foreach (
            var key in new[]
            {
                "name",
                "full_name",
                "saved_to_disk",
                "active",
                "saved",
                "read_only",
                "paragraph_count",
                "equation_count",
                "table_count",
            }
        )
        {
            Copy(source, result, key);
        }
        if (
            result["full_name"]?.GetValue<string>()
            == result["name"]?.GetValue<string>()
        )
        {
            result.Remove("full_name");
        }
        return result;
    }

    private static void Copy(JsonObject source, JsonObject target, string name)
    {
        if (source[name] is JsonNode value)
        {
            target[name] = value.DeepClone();
        }
    }
}
