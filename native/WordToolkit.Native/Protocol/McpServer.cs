using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Observability;

namespace WordToolkit.Native.Protocol;

internal sealed class McpServer
{
    private static readonly string ServerVersion =
        typeof(McpServer).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "0.0.0";
    internal const int DefaultMaxMessageCharacters = 8 * 1024 * 1024;
    internal const int MaxConcurrentRequests = 64;
    private static readonly TimeSpan DefaultProgressInterval = TimeSpan.FromSeconds(5);
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly ToolCatalog _catalog;
    private readonly IToolHandler _handler;
    private readonly WordOperationObservability _observability;
    private readonly int _maxMessageCharacters;
    private readonly TimeSpan _progressInterval;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeRequests =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly SemaphoreSlim _outputGate = new(1, 1);

    public McpServer(
        TextReader input,
        TextWriter output,
        ToolCatalog catalog,
        IToolHandler handler,
        int maxMessageCharacters = DefaultMaxMessageCharacters,
        WordOperationObservability? observability = null,
        TimeSpan? progressInterval = null
    )
    {
        if (maxMessageCharacters < 128)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMessageCharacters));
        }
        var resolvedProgressInterval = progressInterval ?? DefaultProgressInterval;
        if (
            resolvedProgressInterval <= TimeSpan.Zero
            || resolvedProgressInterval > TimeSpan.FromMinutes(1)
        )
        {
            throw new ArgumentOutOfRangeException(nameof(progressInterval));
        }
        _input = input;
        _output = output;
        _catalog = catalog;
        _handler = handler;
        _observability = observability ?? WordOperationObservability.Disabled;
        _maxMessageCharacters = maxMessageCharacters;
        _progressInterval = resolvedProgressInterval;
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
                    ["protocolVersion"] = _catalog.McpProtocolVersion,
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
                        "Token-lean native Word bridge. Use core tools directly; search returns "
                        + "the top advanced action schema by default, then execute it without redundant inspection.",
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
        WordOperationAuditScope? observation = null;
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
            if (ToolCatalog.IsCapabilitiesGateway(name))
            {
                observation = BeginObservation(name);
                var response = RpcResult(
                    id,
                    ToolResult(
                        ok: true,
                        _catalog.GetCapabilities(argumentsDocument.RootElement),
                        error: null
                    )
                );
                observation.CompleteSucceeded();
                return response;
            }
            if (ToolCatalog.IsSearchGateway(name))
            {
                observation = BeginObservation(name);
                var root = argumentsDocument.RootElement;
                foreach (var property in root.EnumerateObject())
                {
                    if (property.Name is not ("query" or "max_results" or "include_top_schema"))
                    {
                        throw new NativeToolException(
                            "INVALID_INPUT",
                            "Unknown search argument",
                            new { argument = property.Name }
                        );
                    }
                }
                var query = root.TryGetProperty("query", out var queryNode)
                    ? queryNode.GetString() ?? ""
                    : "";
                var maxResults =
                    root.TryGetProperty("max_results", out var maxNode)
                    && maxNode.TryGetInt32(out var requestedMaximum)
                        ? requestedMaximum
                        : 3;
                var includeTopSchema = true;
                if (root.TryGetProperty("include_top_schema", out var includeNode))
                {
                    if (
                        includeNode.ValueKind is not (
                            JsonValueKind.True or JsonValueKind.False
                        )
                    )
                    {
                        throw new NativeToolException(
                            "INVALID_INPUT",
                            "include_top_schema must be a boolean"
                        );
                    }
                    includeTopSchema = includeNode.GetBoolean();
                }
                var response = RpcResult(
                    id,
                    ToolResult(
                        ok: true,
                        _catalog.SearchActions(query, maxResults, includeTopSchema),
                        error: null
                    )
                );
                observation.CompleteSucceeded();
                return response;
            }
            if (ToolCatalog.IsInspectGateway(name))
            {
                observation = BeginObservation(name);
                var action = RequiredString(argumentsDocument.RootElement, "action");
                var response = RpcResult(
                    id,
                    ToolResult(
                        ok: true,
                        _catalog.InspectAction(action),
                        error: null
                    )
                );
                observation.CompleteSucceeded();
                return response;
            }

            var actionName = name;
            var actionArguments = argumentsDocument.RootElement.Clone();
            var fullResponse = false;
            if (ToolCatalog.IsExecuteGateway(name))
            {
                actionName = RequiredString(argumentsDocument.RootElement, "action");
                observation = BeginObservation(actionName);
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

            observation ??= BeginObservation(actionName);
            if (!_catalog.IsAction(actionName))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Unknown WordToolkit action"
                );
            }

            var progress = CreateProgressState(parameterObject);
            CancellationTokenSource? progressCancellation = null;
            Task progressLoop = Task.CompletedTask;
            if (progress is not null)
            {
                await WriteProgressAsync(
                    progress,
                    DescribeProgressStart(actionName, actionArguments)
                );
                progressCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken
                );
                progressLoop = RunProgressLoopAsync(
                    progress,
                    actionName,
                    progressCancellation.Token
                );
            }

            object data;
            using var progressScope = progress is null
                ? null
                : ToolProgressContext.Push(message => WriteProgressAsync(progress, message));
            try
            {
                data = await _handler.CallAsync(
                    actionName,
                    actionArguments,
                    cancellationToken
                );
            }
            finally
            {
                if (progressCancellation is not null)
                {
                    progressCancellation.Cancel();
                    try
                    {
                        await progressLoop;
                    }
                    catch (OperationCanceledException)
                    {
                        // The request response remains authoritative.
                    }
                    progressCancellation.Dispose();
                }
            }
            if (progress is not null)
            {
                await WriteProgressAsync(progress, $"{actionName} completed");
            }
            var responseData = fullResponse
                ? JsonSerializer.SerializeToNode(data, JsonDefaults.Compact)
                : ToolResponseCompactor.Compact(actionName, data);
            var success = RpcResult(id, ToolResult(ok: true, responseData, error: null));
            observation.CompleteSucceeded();
            return success;
        }
        catch (NativeToolException exception)
        {
            observation ??= BeginObservation(name);
            observation.CompleteRejected(exception.ErrorCode);
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
            observation ??= BeginObservation(name);
            observation.CompleteCancelled();
            throw;
        }
        catch (Exception exception)
        {
            observation ??= BeginObservation(name);
            observation.CompleteFailed();
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
        finally
        {
            observation?.Dispose();
        }
    }

    private WordOperationAuditScope BeginObservation(string? name)
    {
        WordOperationDescriptor descriptor;
        if (
            name is not null
            && (
                _catalog.IsAction(name)
                || ToolCatalog.IsCapabilitiesGateway(name)
                || ToolCatalog.IsSearchGateway(name)
                || ToolCatalog.IsInspectGateway(name)
                || ToolCatalog.IsExecuteGateway(name)
            )
        )
        {
            descriptor = _catalog.GetObservationDescriptor(name);
        }
        else
        {
            descriptor = new WordOperationDescriptor(
                "wordtoolkit_unknown_action",
                "1.0",
                new WordOperationEffects(
                    ReadOnly: false,
                    Destructive: false,
                    Idempotent: false,
                    OpenWorld: false
                )
            );
        }
        return _observability.Begin(descriptor);
    }

    private ProgressState? CreateProgressState(
        JsonObject? parameters
    )
    {
        var token = (parameters?["_meta"] as JsonObject)?["progressToken"];
        if (
            token is not JsonValue
            || token.GetValueKind() is not (JsonValueKind.String or JsonValueKind.Number)
        )
        {
            return null;
        }
        return new ProgressState(token.DeepClone());
    }

    private async Task RunProgressLoopAsync(
        ProgressState state,
        string actionName,
        CancellationToken cancellationToken
    )
    {
        using var timer = new PeriodicTimer(_progressInterval);
        var started = Stopwatch.GetTimestamp();
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var elapsed = Math.Max(0, (int)Stopwatch.GetElapsedTime(started).TotalSeconds);
            await WriteProgressAsync(
                state,
                $"{actionName} is still running ({elapsed}s elapsed)"
            );
        }
    }

    private async Task WriteProgressAsync(ProgressState state, string message)
    {
        await state.Gate.WaitAsync();
        try
        {
            var value = ++state.Value;
            await WriteResponseAsync(
                new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["method"] = "notifications/progress",
                    ["params"] = new JsonObject
                    {
                        ["progressToken"] = state.Token.DeepClone(),
                        ["progress"] = value,
                        ["message"] = message[..Math.Min(message.Length, 256)],
                    },
                }
            );
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private static string DescribeProgressStart(string actionName, JsonElement arguments)
    {
        if (
            arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty("operations", out var operations)
            && operations.ValueKind == JsonValueKind.Array
        )
        {
            var equations = operations.EnumerateArray().Count(item =>
                item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && string.Equals(type.GetString(), "equation", StringComparison.Ordinal)
            );
            return $"Starting {actionName}: {operations.GetArrayLength()} operations, {equations} equations";
        }
        if (
            arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty("equations", out var equationItems)
            && equationItems.ValueKind == JsonValueKind.Array
        )
        {
            return $"Starting {actionName}: {equationItems.GetArrayLength()} equations";
        }
        return $"Starting {actionName}";
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

    private sealed class ProgressState(JsonNode token)
    {
        public JsonNode Token { get; } = token;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public long Value;
    }

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
