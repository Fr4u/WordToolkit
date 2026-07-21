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
        if (node is JsonObject obj && BatchMutationTools.Contains(toolName))
        {
            return CompactBatchMutation(obj);
        }
        StripNoise(node);
        return node;
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
        if (obj["rules"] is JsonArray)
        {
            obj.Remove("rules");
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
