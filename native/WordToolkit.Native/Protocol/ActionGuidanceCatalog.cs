using System.Reflection;
using System.Text.Json.Nodes;

namespace WordToolkit.Native.Protocol;

internal sealed class ActionGuidanceCatalog
{
    private readonly IReadOnlyDictionary<string, JsonObject> _items;
    private ActionGuidanceCatalog(IReadOnlyDictionary<string, JsonObject> items) => _items = items;
    public static ActionGuidanceCatalog Load(IEnumerable<string> actions)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resource = asm.GetManifestResourceNames().Single(x => x.EndsWith("Schemas.action-guidance.v1.json", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        JsonObject root;
        try { root = JsonNode.Parse(reader.ReadToEnd())?.AsObject() ?? throw new InvalidDataException("Guidance root must be an object"); }
        catch (Exception ex) when (ex is not InvalidDataException)
        { throw new InvalidDataException("Embedded action guidance JSON is invalid", ex); }
        if (root["schema_version"]?.GetValueKind() != System.Text.Json.JsonValueKind.String ||
            root["schema_version"]!.GetValue<string>() != "1.0.0")
            throw new InvalidDataException("Unsupported action guidance schema_version; expected 1.0.0");
        if (root["actions"] is not JsonArray actionArray)
            throw new InvalidDataException("Action guidance root.actions must be an array");
        var expected = actions.ToHashSet(StringComparer.Ordinal);
        var items = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var node in actionArray)
        {
            if (node is not JsonObject item || item["name"]?.GetValueKind() != System.Text.Json.JsonValueKind.String)
                throw new InvalidDataException("Each action guidance entry must be an object with a string name");
            var name = item["name"]!.GetValue<string>();
            if (!expected.Contains(name) || !items.TryAdd(name, item)) throw new InvalidDataException($"Invalid action guidance '{name}'");
            foreach (var field in new[]{"prerequisites","acquisition_steps","recipe_ids"})
                if (item[field] is not JsonArray) throw new InvalidDataException($"Action guidance '{name}' field '{field}' must be an array");
            foreach (var field in new[]{"example","success","recovery"})
                if (item[field] is not JsonObject) throw new InvalidDataException($"Action guidance '{name}' field '{field}' must be an object");
        }
        if (items.Count != expected.Count) throw new InvalidDataException("Action guidance action set mismatch");
        return new ActionGuidanceCatalog(items);
    }
    public JsonObject Get(string name) => _items.TryGetValue(name, out var x) ? x.DeepClone().AsObject() : throw new KeyNotFoundException(name);
    public bool TryGet(string name, out JsonObject guidance) { if (_items.TryGetValue(name, out var x)) { guidance=x.DeepClone().AsObject(); return true; } guidance=null!; return false; }
}
