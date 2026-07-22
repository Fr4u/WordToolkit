using System.Text.Json;
using System.Text.Json.Nodes;

namespace WordToolkit.Native.Protocol;

internal sealed class McpServer
{
    private const string ProtocolVersion = "2025-06-18";
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly ToolCatalog _catalog;
    private readonly IToolHandler _handler;

    public McpServer(
        TextReader input,
        TextWriter output,
        ToolCatalog catalog,
        IToolHandler handler
    )
    {
        _input = input;
        _output = output;
        _catalog = catalog;
        _handler = handler;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await _input.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            JsonObject? response;
            try
            {
                response = await HandleMessageAsync(line, cancellationToken);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(
                    $"WordToolkit.Native protocol failure: {exception.GetType().Name}"
                );
                response = RpcError(null, -32603, "Internal JSON-RPC error");
            }
            if (response is null)
            {
                continue;
            }
            await _output.WriteLineAsync(response.ToJsonString(JsonDefaults.Compact));
            await _output.FlushAsync(cancellationToken);
        }
    }

    private async Task<JsonObject?> HandleMessageAsync(
        string line,
        CancellationToken cancellationToken
    )
    {
        JsonObject request;
        try
        {
            request = JsonNode.Parse(line)?.AsObject()
                ?? throw new JsonException("Request is not an object");
        }
        catch (JsonException)
        {
            return RpcError(null, -32700, "Parse error");
        }

        var id = request["id"]?.DeepClone();
        var method = request["method"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(method))
        {
            return id is null ? null : RpcError(id, -32600, "Invalid Request");
        }
        if (id is null)
        {
            return null;
        }

        return method switch
        {
            "initialize" => RpcResult(
                id,
                new JsonObject
                {
                    ["protocolVersion"] = ProtocolVersion,
                    ["capabilities"] = new JsonObject
                    {
                        ["tools"] = new JsonObject { ["listChanged"] = false },
                    },
                    ["serverInfo"] = new JsonObject
                    {
                        ["name"] = "WordToolkit Native",
                        ["version"] = "0.29.0",
                    },
                    ["instructions"] =
                        "Token-lean native Word bridge. Use core tools directly; inspect and "
                        + "execute one advanced action lazily when needed.",
                }
            ),
            "ping" => RpcResult(id, new JsonObject()),
            "tools/list" => RpcResult(
                id,
                new JsonObject { ["tools"] = _catalog.Tools.DeepClone() }
            ),
            "tools/call" => await HandleToolCallAsync(id, request["params"], cancellationToken),
            _ => RpcError(id, -32601, "Method not found"),
        };
    }

    private async Task<JsonObject> HandleToolCallAsync(
        JsonNode id,
        JsonNode? parameters,
        CancellationToken cancellationToken
    )
    {
        var parameterObject = parameters as JsonObject;
        var name = parameterObject?["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            return RpcError(id, -32602, "Tool name is required");
        }

        var argumentsNode = parameterObject?["arguments"] ?? new JsonObject();
        using var argumentsDocument = JsonDocument.Parse(
            argumentsNode.ToJsonString(JsonDefaults.Compact)
        );
        try
        {
            if (ToolCatalog.IsSearchGateway(name))
            {
                var root = argumentsDocument.RootElement;
                var query = root.TryGetProperty("query", out var queryNode)
                    ? queryNode.GetString() ?? ""
                    : "";
                var maxResults =
                    root.TryGetProperty("max_results", out var maxNode)
                    && maxNode.TryGetInt32(out var requestedMaximum)
                        ? requestedMaximum
                        : 8;
                return RpcResult(
                    id,
                    ToolResult(
                        ok: true,
                        _catalog.SearchActions(query, maxResults),
                        error: null
                    )
                );
            }
            if (ToolCatalog.IsInspectGateway(name))
            {
                var action = RequiredString(argumentsDocument.RootElement, "action");
                return RpcResult(
                    id,
                    ToolResult(
                        ok: true,
                        _catalog.InspectAction(action),
                        error: null
                    )
                );
            }

            var actionName = name;
            var actionArguments = argumentsDocument.RootElement.Clone();
            var fullResponse = false;
            if (ToolCatalog.IsExecuteGateway(name))
            {
                actionName = RequiredString(argumentsDocument.RootElement, "action");
                if (!_catalog.IsAction(actionName))
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "Unknown WordToolkit action",
                        new { action = actionName }
                    );
                }
                if (
                    !argumentsDocument.RootElement.TryGetProperty(
                        "arguments",
                        out var nestedArguments
                    )
                    || nestedArguments.ValueKind != JsonValueKind.Object
                )
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "arguments must be an object"
                    );
                }
                actionArguments = nestedArguments.Clone();
                if (
                    argumentsDocument.RootElement.TryGetProperty(
                        "response_mode",
                        out var responseMode
                    )
                )
                {
                    var mode = responseMode.GetString();
                    if (mode is not ("compact" or "full"))
                    {
                        throw new NativeToolException(
                            "INVALID_INPUT",
                            "response_mode must be 'compact' or 'full'"
                        );
                    }
                    fullResponse = mode == "full";
                }
            }

            var data = await _handler.CallAsync(
                actionName,
                actionArguments,
                cancellationToken
            );
            var responseData = fullResponse
                ? JsonSerializer.SerializeToNode(data, JsonDefaults.Compact)
                : ToolResponseCompactor.Compact(actionName, data);
            return RpcResult(id, ToolResult(ok: true, responseData, error: null));
        }
        catch (NativeToolException exception)
        {
            var error = new
            {
                code = exception.ErrorCode,
                message = exception.Message,
                details = exception.Details,
                retryable = exception.Retryable,
            };
            return RpcResult(id, ToolResult(ok: false, data: null, error));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"WordToolkit.Native tool failure ({name}): {exception.GetType().Name}"
            );
            var error = new
            {
                code = "INTERNAL_ERROR",
                message = "The native Word operation failed",
                details = new { exception = exception.GetType().Name },
                retryable = false,
            };
            return RpcResult(id, ToolResult(ok: false, data: null, error));
        }
    }

    private static JsonObject ToolResult(bool ok, object? data, object? error)
    {
        object payload = ok
            ? new { ok = true, data }
            : new { ok = false, error };
        var structured = JsonSerializer.SerializeToNode(payload, JsonDefaults.Compact)
            ?? new JsonObject();
        return new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = structured.ToJsonString(JsonDefaults.Compact),
                },
            },
            ["structuredContent"] = structured,
            ["isError"] = !ok,
        };
    }

    private static JsonObject RpcResult(JsonNode id, JsonNode result)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["result"] = result,
        };
    }

    private static JsonObject RpcError(JsonNode? id, int code, string message)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
            },
        };
    }

    private static string RequiredString(JsonElement root, string name)
    {
        if (
            !root.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString())
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} must be a non-empty string"
            );
        }
        return value.GetString()!;
    }
}
