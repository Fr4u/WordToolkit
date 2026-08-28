using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static readonly Regex IdempotencyKeyPattern = new(
        "^[A-Za-z0-9._:-]{1,64}$",
        RegexOptions.CultureInvariant
    );

    private static LiveOperationReceiptIntent PrepareLiveOperationReceiptIntent(
        JsonElement arguments
    )
    {
        var suppliedKey = arguments.TryGetProperty("idempotency_key", out var keyNode)
            ? keyNode.ValueKind == JsonValueKind.String
                ? keyNode.GetString() ?? ""
                : throw new NativeToolException(
                    "INVALID_INPUT",
                    "idempotency_key must be a string"
                )
            : "";
        if (suppliedKey.Length > 0 && !IdempotencyKeyPattern.IsMatch(suppliedKey))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "idempotency_key must contain 1 to 64 ASCII letters, digits, dots, underscores, colons, or hyphens"
            );
        }
        var canonicalIntent = CanonicalReceiptIntent(arguments);
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalIntent))
        ).ToLowerInvariant();
        var operationId = suppliedKey.Length > 0
            ? $"wlop_{suppliedKey}"
            : $"wlop_{fingerprint[..32]}";
        return new LiveOperationReceiptIntent(operationId, fingerprint);
    }

    private static string CanonicalReceiptIntent(JsonElement value)
    {
        var normalized = NormalizeReceiptNode(value, skipIdempotencyKey: true);
        return normalized?.ToJsonString(JsonDefaults.Compact) ?? "null";
    }

    private static JsonNode? NormalizeReceiptNode(
        JsonElement value,
        bool skipIdempotencyKey = false
    )
    {
        return value.ValueKind switch
        {
            JsonValueKind.Object => new JsonObject(
                value.EnumerateObject()
                    .Where(property => !skipIdempotencyKey
                        || property.Name != "idempotency_key")
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property => KeyValuePair.Create<string, JsonNode?>(
                        property.Name,
                        NormalizeReceiptNode(property.Value)
                    ))
            ),
            JsonValueKind.Array => new JsonArray(
                value.EnumerateArray()
                    .Select(item => NormalizeReceiptNode(item))
                    .ToArray()
            ),
            JsonValueKind.String => JsonValue.Create(value.GetString()),
            JsonValueKind.Number => JsonNode.Parse(value.GetRawText()),
            JsonValueKind.True => JsonValue.Create(true),
            JsonValueKind.False => JsonValue.Create(false),
            JsonValueKind.Null => null,
            _ => throw new NativeToolException(
                "INVALID_INPUT",
                "The live operation request contains an unsupported JSON value"
            ),
        };
    }

    private static object AddLiveOperationReceiptMetadata(
        object result,
        string operationId,
        bool replayed
    )
    {
        var node = JsonSerializer.SerializeToNode(result, JsonDefaults.Compact)
            as JsonObject ?? throw new InvalidOperationException(
                "Live operation result was not an object"
            );
        node["operation_id"] = operationId;
        node["operation_status"] = "succeeded";
        node["receipt_replayed"] = replayed;
        node["outcome_known"] = true;
        return node;
    }

    private sealed record LiveOperationReceiptIntent(
        string OperationId,
        string Fingerprint
    );
}

internal sealed class LiveOperationReceiptStore
{
    internal const int MaximumEntries = 256;
    internal static readonly TimeSpan EntryTimeToLive = TimeSpan.FromHours(1);
    private readonly object _gate = new();
    private readonly Dictionary<string, LiveOperationReceipt> _entries = new(
        StringComparer.Ordinal
    );
    private readonly Func<DateTimeOffset> _clock;

    internal LiveOperationReceiptStore(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    internal LiveOperationReceiptHandle GetOrCreate(
        string operationId,
        string fingerprint,
        Func<Task<object>> executionFactory
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentNullException.ThrowIfNull(executionFactory);
        LiveOperationReceipt receipt;
        var created = false;
        lock (_gate)
        {
            PruneLocked();
            if (_entries.TryGetValue(operationId, out var existing))
            {
                if (!string.Equals(
                    existing.Fingerprint,
                    fingerprint,
                    StringComparison.Ordinal
                ))
                {
                    throw new NativeToolException(
                        "IDEMPOTENCY_CONFLICT",
                        "The idempotency key is already bound to a different live Word request",
                        new
                        {
                            operation_id = operationId,
                            existing_status = existing.Status,
                            intent_bound = true,
                            raw_document_content_returned = false,
                        }
                    );
                }
                receipt = existing;
            }
            else
            {
                EnsureCapacityLocked();
                var now = _clock();
                receipt = new LiveOperationReceipt(
                    operationId,
                    fingerprint,
                    now,
                    executionFactory,
                    RunAndRecordAsync
                );
                _entries.Add(operationId, receipt);
                created = true;
            }
        }
        var execution = receipt.Execution.Value;
        _ = execution.ContinueWith(
            task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted
                | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
        return new LiveOperationReceiptHandle(receipt, created);
    }

    internal object Status(string operationId)
    {
        if (
            operationId.Length is < 6 or > 133
            || !operationId.StartsWith("wlop_", StringComparison.Ordinal)
            || !operationId[5..].All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '.' or '_' or ':' or '-'
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "operation_id is not a valid live Word operation receipt identifier"
            );
        }
        lock (_gate)
        {
            PruneLocked();
            if (!_entries.TryGetValue(operationId, out var receipt))
            {
                return new
                {
                    operation_id = operationId,
                    operation_status = "unknown",
                    outcome_known = false,
                    receipt_scope = "current_wordtoolkit_runtime",
                    raw_document_content_returned = false,
                };
            }
            return receipt.StatusPayload();
        }
    }

    internal int Count
    {
        get
        {
            lock (_gate)
            {
                PruneLocked();
                return _entries.Count;
            }
        }
    }

    private async Task<object> RunAndRecordAsync(
        LiveOperationReceipt receipt,
        Func<Task<object>> executionFactory
    )
    {
        try
        {
            var result = await executionFactory().ConfigureAwait(false);
            lock (_gate)
            {
                receipt.CompleteSucceeded(result, _clock());
                PruneLocked();
            }
            return result;
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                receipt.CompleteFailed(
                    exception is NativeToolException native
                        ? native.ErrorCode
                        : "INTERNAL_ERROR",
                    exception.GetType().Name,
                    exception is NativeToolException retryable && retryable.Retryable,
                    _clock()
                );
                PruneLocked();
            }
            throw;
        }
    }

    private void PruneLocked()
    {
        var threshold = _clock() - EntryTimeToLive;
        foreach (
            var id in _entries.Values
                .Where(receipt => receipt.Status != "pending"
                    && receipt.UpdatedAt <= threshold)
                .Select(receipt => receipt.OperationId)
                .ToArray()
        )
        {
            _entries.Remove(id);
        }
        while (_entries.Count > MaximumEntries)
        {
            var oldestTerminal = _entries.Values
                .Where(receipt => receipt.Status != "pending")
                .OrderBy(receipt => receipt.UpdatedAt)
                .FirstOrDefault();
            if (oldestTerminal is null)
            {
                break;
            }
            _entries.Remove(oldestTerminal.OperationId);
        }
    }

    private void EnsureCapacityLocked()
    {
        if (_entries.Count < MaximumEntries)
        {
            return;
        }
        var oldestTerminal = _entries.Values
            .Where(receipt => receipt.Status != "pending")
            .OrderBy(receipt => receipt.UpdatedAt)
            .FirstOrDefault();
        if (oldestTerminal is not null)
        {
            _entries.Remove(oldestTerminal.OperationId);
            return;
        }
        throw new NativeToolException(
            "LIVE_OPERATION_RECEIPT_LIMIT",
            "All bounded live Word operation receipt slots are still pending",
            new
            {
                receipt_count = _entries.Count,
                maximum_receipts = MaximumEntries,
                retryable_after_pending_completion = true,
            },
            retryable: true
        );
    }
}

internal sealed class LiveOperationReceipt
{
    internal LiveOperationReceipt(
        string operationId,
        string fingerprint,
        DateTimeOffset createdAt,
        Func<Task<object>> executionFactory,
        Func<LiveOperationReceipt, Func<Task<object>>, Task<object>> runner
    )
    {
        OperationId = operationId;
        Fingerprint = fingerprint;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Execution = new Lazy<Task<object>>(
            () => runner(this, executionFactory),
            LazyThreadSafetyMode.ExecutionAndPublication
        );
    }

    internal string OperationId { get; }
    internal string Fingerprint { get; }
    internal DateTimeOffset CreatedAt { get; }
    internal DateTimeOffset UpdatedAt { get; private set; }
    internal string Status { get; private set; } = "pending";
    internal Lazy<Task<object>> Execution { get; }
    private object? ResultSummary { get; set; }
    private string? ErrorCode { get; set; }
    private string? ErrorType { get; set; }
    private bool ErrorRetryable { get; set; }

    internal void CompleteSucceeded(object result, DateTimeOffset now)
    {
        Status = "succeeded";
        UpdatedAt = now;
        ResultSummary = CreateResultSummary(result);
    }

    internal void CompleteFailed(
        string errorCode,
        string errorType,
        bool retryable,
        DateTimeOffset now
    )
    {
        Status = "failed";
        UpdatedAt = now;
        ErrorCode = errorCode;
        ErrorType = errorType;
        ErrorRetryable = retryable;
    }

    internal object StatusPayload() => new
    {
        operation_id = OperationId,
        operation_status = Status,
        outcome_known = Status is "succeeded" or "failed",
        created_at_utc = CreatedAt.ToString("O"),
        updated_at_utc = UpdatedAt.ToString("O"),
        terminal_receipt_ttl_seconds = (int)LiveOperationReceiptStore.EntryTimeToLive.TotalSeconds,
        receipt_scope = "current_wordtoolkit_runtime",
        result = Status == "succeeded" ? ResultSummary : null,
        error = Status == "failed"
            ? new
            {
                code = ErrorCode,
                exception_type = ErrorType,
                retryable = ErrorRetryable,
            }
            : null,
        raw_document_content_returned = false,
    };

    private static object CreateResultSummary(object result)
    {
        var node = JsonSerializer.SerializeToNode(result, JsonDefaults.Compact)
            as JsonObject ?? new JsonObject();
        var document = node["document"] as JsonObject;
        return new
        {
            live_document_id = node["live_document_id"]?.DeepClone(),
            live_version = node["live_version"]?.DeepClone(),
            operation_count = node["operation_count"]?.DeepClone(),
            text_operation_count = node["text_operation_count"]?.DeepClone(),
            equation_operation_count = node["equation_operation_count"]?.DeepClone(),
            paragraph_count = document?["paragraph_count"]?.DeepClone(),
            equation_count = document?["equation_count"]?.DeepClone(),
            table_count = document?["table_count"]?.DeepClone(),
            native_verified = true,
        };
    }
}

internal sealed record LiveOperationReceiptHandle(
    LiveOperationReceipt Receipt,
    bool WasCreatedForCaller
)
{
    internal string OperationId => Receipt.OperationId;
    internal Lazy<Task<object>> Execution => Receipt.Execution;
}
