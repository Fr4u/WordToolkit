using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WordToolkit.Native.Protocol;

internal sealed class McpServer
{
    private const string ProtocolVersion = "2025-06-18";
    private static readonly string ServerVersion =
        typeof(McpServer).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+', 2)[0]
        ?? "0.0.0";
    internal const int DefaultMaxMessageCharacters = 8 * 1024 * 1024;
    private const int MaxConcurrentRequests = 64;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly ToolCatalog _catalog;
    private readonly IToolHandler _handler;
    private readonly int _maxMessageCharacters;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeRequests =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly SemaphoreSlim _outputGate = new(1, 1);

    public McpServer(
        TextReader input,
        TextWriter output,
        ToolCatalog catalog,
        IToolHandler handler,
        int maxMessageCharacters = DefaultMaxMessageCharacters
    )
    {
        if (maxMessageCharacters < 128)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMessageCharacters));
        }
        _input = input;
        _output = output;
        _catalog = catalog;
        _handler = handler;
        _maxMessageCharacters = maxMessageCharacters;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var reader = new BoundedLineReader(_input, _maxMessageCharacters);
        var pending = new List<Task>();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await reader.ReadAsync(cancellationToken);
                if (read is null)
                {
                    break;
                }
                if (read.LimitExceeded)
                {
                    await WriteResponseAsync(
                        RpcError(null, -32600, "JSON-RPC message exceeds the size limit")
                    );
                    continue;
                }
                if (string.IsNullOrWhiteSpace(read.Line))
                {
                    continue;
                }

                JsonObject request;
                try
                {
                    request = JsonNode.Parse(read.Line) as JsonObject
                        ?? throw new JsonException("Request is not an object");
                }
                catch (JsonException)
                {
                    await WriteResponseAsync(RpcError(null, -32700, "Parse error"));
                    continue;
                }

                var method = StringValue(request["method"]);
                var id = request["id"]?.DeepClone();
                if (id is null)
                {
                    HandleNotification(method, request["params"]);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(method) || !TryRequestKey(id, out var key))
                {
                    await WriteResponseAsync(RpcError(id, -32600, "Invalid Request"));
                    continue;
                }
                if (_activeRequests.Count >= MaxConcurrentRequests)
                {
                    await WriteResponseAsync(
                        RpcError(id, -32000, "Too many active requests")
                    );
                    continue;
                }

                var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken
                );
                if (!_activeRequests.TryAdd(key, requestCancellation))
                {
                    requestCancellation.Dispose();
                    await WriteResponseAsync(
                        RpcError(id, -32600, "A request with this id is already active")
                    );
                    continue;
                }

                pending.Add(
                    ProcessRequestAsync(request, id, key, requestCancellation)
                );
                if (pending.Count >= 256)
                {
                    pending.RemoveAll(task => task.IsCompleted);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal server shutdown.
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
            {
                foreach (var request in _activeRequests.Values)
                {
                    request.Cancel();
                }
            }
            try
            {
                await Task.WhenAll(pending);
            }
            catch (OperationCanceledException)
            {
                // Individual cancellation responses are emitted by ProcessRequestAsync.
            }
        }
    }

    private async Task ProcessRequestAsync(
        JsonObject request,
        JsonNode id,
        string key,
        CancellationTokenSource requestCancellation
    )
    {
        JsonObject response;
        var enteredRequestGate = false;
        try
        {
            await _requestGate.WaitAsync(requestCancellation.Token);
            enteredRequestGate = true;
            response = await HandleRequestAsync(
                request,
                id,
                requestCancellation.Token
            );
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
            response = RpcError(id, -32800, "Request cancelled");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"WordToolkit.Native protocol failure: {exception.GetType().Name}"
            );
            response = RpcError(id, -32603, "Internal JSON-RPC error");
        }
        finally
        {
            if (enteredRequestGate)
            {
                _requestGate.Release();
            }
        }
        try
        {
            await WriteResponseAsync(response);
        }
        finally
        {
            _activeRequests.TryRemove(key, out _);
            requestCancellation.Dispose();
        }
    }

    private async Task<JsonObject> HandleRequestAsync(
        JsonObject request,
        JsonNode id,
        CancellationToken cancellationToken
    )
    {
        var method = StringValue(request["method"]);
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
                        ["version"] = ServerVersion,
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

    private void HandleNotification(string? method, JsonNode? parameters)
    {
        if (method is not ("notifications/cancelled" or "$/cancelRequest"))
        {
            return;
        }
        var requestId = (parameters as JsonObject)?["requestId"];
        if (requestId is null || !TryRequestKey(requestId, out var key))
        {
            return;
        }
        if (_activeRequests.TryGetValue(key, out var requestCancellation))
        {
            requestCancellation.Cancel();
        }
    }

    private async Task WriteResponseAsync(JsonObject response)
    {
        await _outputGate.WaitAsync();
        try
        {
            await _output.WriteLineAsync(response.ToJsonString(JsonDefaults.Compact));
            await _output.FlushAsync(CancellationToken.None);
        }
        finally
        {
            _outputGate.Release();
        }
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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

    private static string? StringValue(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    private static bool TryRequestKey(JsonNode id, out string key)
    {
        if (
            id is JsonValue
            && id.GetValueKind() is JsonValueKind.String or JsonValueKind.Number
        )
        {
            key = id.ToJsonString(JsonDefaults.Compact);
            return true;
        }
        key = "";
        return false;
    }

    private sealed record BoundedLine(string? Line, bool LimitExceeded);

    private sealed class BoundedLineReader
    {
        private readonly TextReader _reader;
        private readonly int _maximumCharacters;
        private readonly char[] _buffer = new char[8 * 1024];
        private int _offset;
        private int _count;

        public BoundedLineReader(TextReader reader, int maximumCharacters)
        {
            _reader = reader;
            _maximumCharacters = maximumCharacters;
        }

        public async Task<BoundedLine?> ReadAsync(CancellationToken cancellationToken)
        {
            var builder = new StringBuilder(Math.Min(_maximumCharacters, 16 * 1024));
            var limitExceeded = false;
            while (true)
            {
                if (_offset >= _count)
                {
                    _count = await _reader.ReadAsync(
                        _buffer.AsMemory(),
                        cancellationToken
                    );
                    _offset = 0;
                    if (_count == 0)
                    {
                        if (builder.Length == 0 && !limitExceeded)
                        {
                            return null;
                        }
                        return Finish(builder, limitExceeded);
                    }
                }

                var newline = Array.IndexOf(_buffer, '\n', _offset, _count - _offset);
                var end = newline >= 0 ? newline : _count;
                var length = end - _offset;
                if (!limitExceeded)
                {
                    if (builder.Length + length > _maximumCharacters)
                    {
                        limitExceeded = true;
                        builder.Clear();
                    }
                    else
                    {
                        builder.Append(_buffer, _offset, length);
                    }
                }
                _offset = newline >= 0 ? newline + 1 : _count;
                if (newline >= 0)
                {
                    return Finish(builder, limitExceeded);
                }
            }
        }

        private static BoundedLine Finish(StringBuilder builder, bool limitExceeded)
        {
            if (limitExceeded)
            {
                return new BoundedLine(null, true);
            }
            if (builder.Length > 0 && builder[^1] == '\r')
            {
                builder.Length--;
            }
            return new BoundedLine(builder.ToString(), false);
        }
    }
}
