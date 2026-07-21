using System.Text.Json;

namespace WordToolkit.Native.Protocol;

internal interface IToolHandler
{
    Task<object> CallAsync(string name, JsonElement arguments, CancellationToken cancellationToken);
}
