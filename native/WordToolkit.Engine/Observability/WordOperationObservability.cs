using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace WordToolkit.Engine.Observability;

public sealed partial class WordOperationObservability : IDisposable
{
    private static readonly ActivitySource ActivitySource = new(
        WordOperationObservabilityContract.ActivitySourceName,
        "1.0.0"
    );
    private static readonly Meter Meter = new(
        WordOperationObservabilityContract.MeterName,
        "1.0.0"
    );
    private static readonly Counter<long> OperationCounter = Meter.CreateCounter<long>(
        "wordtoolkit.operation.count",
        description: "Number of completed WordToolkit operations."
    );
    private static readonly Histogram<double> DurationHistogram =
        Meter.CreateHistogram<double>(
            "wordtoolkit.operation.duration",
            unit: "ms",
            description: "Duration of completed WordToolkit operations."
        );
    private static readonly WordAuditSinkMetadata DisabledSink = new(
        "wordtoolkit.audit.disabled",
        "none",
        Durable: false,
        ExternalNetwork: false,
        ReturnsDocumentContent: false,
        ReturnsPaths: false
    );
    private static readonly WordAuditSinkMetadata MemorySink = new(
        "wordtoolkit.audit.memory",
        "bounded_memory",
        Durable: false,
        ExternalNetwork: false,
        ReturnsDocumentContent: false,
        ReturnsPaths: false
    );
    private static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(7);
    private static readonly TimeSpan MaximumRetention = TimeSpan.FromDays(365);
    private static readonly TimeSpan MinimumRetention = TimeSpan.FromMinutes(1);
    private static readonly string ZeroHash = new('0', 64);

    private readonly object _sync = new();
    private readonly bool _telemetryEnabled;
    private readonly bool _auditEnabled;
    private readonly int _memoryCapacity;
    private readonly int _sinkQueueCapacity;
    private readonly TimeSpan _retention;
    private readonly IWordAuditSink? _sink;
    private readonly TimeProvider _timeProvider;
    private readonly Channel<WordAuditEvent>? _sinkQueue;
    private readonly CancellationTokenSource? _sinkCancellation;
    private readonly Task? _sinkWorker;
    private readonly List<WordAuditEvent> _events = [];
    private long _sequence;
    private string _lastRecordSha256 = ZeroHash;
    private long _attempted;
    private long _succeeded;
    private long _rejected;
    private long _cancelled;
    private long _failed;
    private long _abandoned;
    private long _droppedByCapacity;
    private long _droppedByRetention;
    private long _telemetryFailure;
    private long _sinkSuccess;
    private long _sinkFailure;
    private long _sinkQueueDrop;
    private long _sinkPending;
    private DateTimeOffset? _lastTelemetryFailureUtc;
    private DateTimeOffset? _lastSinkFailureUtc;
    private int _disposed;

    public static WordOperationObservability Disabled { get; } = new(
        new WordOperationObservabilityOptions()
    );

    public WordOperationObservability(WordOperationObservabilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (
            options.MemoryCapacity
                is < WordOperationObservabilityContract.MinimumMemoryCapacity
                or > WordOperationObservabilityContract.MaximumMemoryCapacity
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"Memory capacity must be between {WordOperationObservabilityContract.MinimumMemoryCapacity} and {WordOperationObservabilityContract.MaximumMemoryCapacity}."
            );
        }
        if (
            options.SinkQueueCapacity
                is < WordOperationObservabilityContract.MinimumMemoryCapacity
                or > WordOperationObservabilityContract.MaximumMemoryCapacity
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"Sink queue capacity must be between {WordOperationObservabilityContract.MinimumMemoryCapacity} and {WordOperationObservabilityContract.MaximumMemoryCapacity}."
            );
        }
        var retention = options.Retention ?? DefaultRetention;
        if (retention < MinimumRetention || retention > MaximumRetention)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Retention must be between one minute and 365 days."
            );
        }
        if (!options.AuditEnabled && options.Sink is not null)
        {
            throw new ArgumentException(
                "An audit sink cannot be configured while audit recording is disabled.",
                nameof(options)
            );
        }
        if (options.Sink?.Metadata.ReturnsDocumentContent == true)
        {
            throw new ArgumentException(
                "An audit sink must not return document content.",
                nameof(options)
            );
        }
        if (options.Sink?.Metadata.ReturnsPaths == true)
        {
            throw new ArgumentException(
                "An audit sink must not return paths.",
                nameof(options)
            );
        }
        if (options.Sink?.Metadata.ExternalNetwork == true)
        {
            throw new ArgumentException(
                "A network audit sink requires a future explicit export policy.",
                nameof(options)
            );
        }
        if (
            options.Sink is not null
            && (
                !SafeDimensionPattern().IsMatch(options.Sink.Metadata.SinkId)
                || !SafeDimensionPattern().IsMatch(options.Sink.Metadata.Kind)
            )
        )
        {
            throw new ArgumentException(
                "Audit sink metadata is not a safe telemetry dimension.",
                nameof(options)
            );
        }

        _telemetryEnabled = options.TelemetryEnabled;
        _auditEnabled = options.AuditEnabled;
        _memoryCapacity = options.MemoryCapacity;
        _sinkQueueCapacity = options.SinkQueueCapacity;
        _retention = retention;
        _sink = options.Sink;
        _timeProvider = options.TimeProvider ?? TimeProvider.System;
        if (_sink is not null)
        {
            _sinkQueue = Channel.CreateBounded<WordAuditEvent>(
                new BoundedChannelOptions(_sinkQueueCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait,
                    AllowSynchronousContinuations = false,
                }
            );
            _sinkCancellation = new CancellationTokenSource();
            _sinkWorker = Task.Run(() => ProcessSinkQueueAsync(_sinkCancellation.Token));
        }
    }

    public WordOperationAuditScope Begin(WordOperationDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ValidateDescriptor(descriptor);
        var startedUtc = _timeProvider.GetUtcNow();
        var startedTimestamp = _timeProvider.GetTimestamp();
        var correlationId = ActivityTraceId.CreateRandom().ToHexString();
        Activity? activity = null;
        if (_telemetryEnabled)
        {
            try
            {
                activity = ActivitySource.StartActivity(descriptor.OperationName);
                if (activity is not null)
                {
                    activity.SetTag("wordtoolkit.operation.name", descriptor.OperationName);
                    activity.SetTag("wordtoolkit.operation.version", descriptor.OperationVersion);
                    activity.SetTag("wordtoolkit.operation.read_only", descriptor.Effects.ReadOnly);
                    activity.SetTag("wordtoolkit.operation.destructive", descriptor.Effects.Destructive);
                    correlationId = activity.TraceId.ToHexString();
                }
            }
            catch
            {
                activity = null;
                RecordTelemetryFailure();
            }
        }
        return new WordOperationAuditScope(
            this,
            descriptor,
            startedUtc,
            startedTimestamp,
            correlationId,
            activity
        );
    }

    public WordOperationObservabilitySnapshot Snapshot(
        int offset = 0,
        int limit = 32,
        CancellationToken cancellationToken = default
    )
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        if (limit is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PruneExpired(_timeProvider.GetUtcNow());
            var events = _events.Skip(offset).Take(limit).ToArray();
            return new WordOperationObservabilitySnapshot(
                WordOperationObservabilityContract.SnapshotContract,
                _telemetryEnabled,
                _auditEnabled,
                _memoryCapacity,
                _sinkQueueCapacity,
                checked((long)_retention.TotalSeconds),
                _sink?.Metadata ?? (_auditEnabled ? MemorySink : DisabledSink),
                new WordOperationObservabilityCounters(
                    _attempted,
                    _succeeded,
                    _rejected,
                    _cancelled,
                    _failed,
                    _abandoned,
                    _events.Count,
                    _droppedByCapacity,
                    _droppedByRetention,
                    _telemetryFailure,
                    _sinkSuccess,
                    _sinkFailure,
                    _sinkQueueDrop,
                    _lastTelemetryFailureUtc,
                    _lastSinkFailureUtc
                ),
                new WordAuditIntegritySummary(
                    "SHA-256",
                    AppendChain: true,
                    Authenticated: false,
                    _events.Count == 0 ? null : _events[0].PreviousRecordSha256,
                    _sequence == 0 ? null : _lastRecordSha256
                ),
                Array.AsReadOnly(events)
            );
        }
    }

    internal void Complete(
        WordOperationDescriptor descriptor,
        DateTimeOffset startedUtc,
        long startedTimestamp,
        string correlationId,
        Activity? activity,
        WordOperationAuditOutcome outcome,
        string? errorCode
    )
    {
        var elapsed = _timeProvider.GetElapsedTime(startedTimestamp);
        var durationMicroseconds = Math.Max(
            0,
            checked((long)Math.Round(elapsed.TotalMilliseconds * 1000, MidpointRounding.AwayFromZero))
        );
        var normalizedError = NormalizeErrorCode(outcome, errorCode);
        if (_telemetryEnabled)
        {
            try
            {
                var tags = new TagList
                {
                    { "wordtoolkit.operation.name", descriptor.OperationName },
                    { "wordtoolkit.operation.outcome", SnakeCase(outcome.ToString()) },
                };
                OperationCounter.Add(1, tags);
                DurationHistogram.Record(elapsed.TotalMilliseconds, tags);
                if (activity is not null)
                {
                    activity.SetTag("wordtoolkit.operation.outcome", SnakeCase(outcome.ToString()));
                    if (normalizedError is not null)
                    {
                        activity.SetTag("wordtoolkit.error.code", normalizedError);
                    }
                    activity.SetStatus(
                        outcome == WordOperationAuditOutcome.Succeeded
                            ? ActivityStatusCode.Ok
                            : ActivityStatusCode.Error
                    );
                }
            }
            catch
            {
                RecordTelemetryFailure();
            }
        }

        lock (_sync)
        {
            _attempted++;
            IncrementOutcome(outcome);
            if (!_auditEnabled)
            {
                return;
            }
            var now = _timeProvider.GetUtcNow();
            PruneExpired(now);
            var sequence = checked(++_sequence);
            var previousHash = _lastRecordSha256;
            var provisional = new WordAuditEvent(
                WordOperationObservabilityContract.EventContract,
                sequence,
                startedUtc,
                durationMicroseconds,
                correlationId,
                descriptor.OperationName,
                descriptor.OperationVersion,
                descriptor.Effects,
                outcome,
                normalizedError,
                previousHash,
                ""
            );
            var auditEvent = provisional with
            {
                RecordSha256 = WordAuditIntegrity.ComputeRecordSha256(provisional),
            };
            _lastRecordSha256 = auditEvent.RecordSha256;
            _events.Add(auditEvent);
            while (_events.Count > _memoryCapacity)
            {
                _events.RemoveAt(0);
                _droppedByCapacity++;
            }
            if (_sinkQueue is not null)
            {
                Interlocked.Increment(ref _sinkPending);
                if (_sinkQueue.Writer.TryWrite(auditEvent))
                {
                    // The single background consumer owns sink I/O.
                }
                else
                {
                    Interlocked.Decrement(ref _sinkPending);
                    _sinkQueueDrop++;
                }
            }
        }
    }

    public async Task FlushAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        var maximumWait = timeout ?? TimeSpan.FromSeconds(5);
        if (maximumWait <= TimeSpan.Zero || maximumWait > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
        var started = Stopwatch.GetTimestamp();
        while (Interlocked.Read(ref _sinkPending) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(started) >= maximumWait)
            {
                throw new TimeoutException("Audit sink flush exceeded the bounded wait.");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        if (_sinkQueue is null || _sinkWorker is null || _sinkCancellation is null)
        {
            return;
        }
        _sinkQueue.Writer.TryComplete();
        try
        {
            if (!_sinkWorker.Wait(TimeSpan.FromSeconds(2)))
            {
                _sinkCancellation.Cancel();
            }
        }
        catch (AggregateException)
        {
            _sinkCancellation.Cancel();
        }
        finally
        {
            _sinkCancellation.Dispose();
        }
    }

    private async Task ProcessSinkQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var auditEvent in _sinkQueue!.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    _sink!.Write(auditEvent);
                    lock (_sync)
                    {
                        _sinkSuccess++;
                    }
                }
                catch
                {
                    lock (_sync)
                    {
                        _sinkFailure++;
                        _lastSinkFailureUtc = _timeProvider.GetUtcNow();
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref _sinkPending);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Bounded shutdown abandons only sink delivery, never the document operation.
        }
    }

    private void PruneExpired(DateTimeOffset now)
    {
        var threshold = now - _retention;
        var count = 0;
        while (count < _events.Count && _events[count].OccurredUtc < threshold)
        {
            count++;
        }
        if (count == 0)
        {
            return;
        }
        _events.RemoveRange(0, count);
        _droppedByRetention += count;
    }

    private void IncrementOutcome(WordOperationAuditOutcome outcome)
    {
        switch (outcome)
        {
            case WordOperationAuditOutcome.Succeeded:
                _succeeded++;
                break;
            case WordOperationAuditOutcome.Rejected:
                _rejected++;
                break;
            case WordOperationAuditOutcome.Cancelled:
                _cancelled++;
                break;
            case WordOperationAuditOutcome.Failed:
                _failed++;
                break;
            case WordOperationAuditOutcome.Abandoned:
                _abandoned++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome));
        }
    }

    internal void DisposeActivity(Activity? activity)
    {
        if (activity is null)
        {
            return;
        }
        try
        {
            activity.Dispose();
        }
        catch
        {
            RecordTelemetryFailure();
        }
    }

    private void RecordTelemetryFailure()
    {
        lock (_sync)
        {
            _telemetryFailure++;
            _lastTelemetryFailureUtc = _timeProvider.GetUtcNow();
        }
    }

    private static string? NormalizeErrorCode(
        WordOperationAuditOutcome outcome,
        string? errorCode
    )
    {
        if (outcome == WordOperationAuditOutcome.Succeeded)
        {
            return null;
        }
        if (
            !string.IsNullOrWhiteSpace(errorCode)
            && errorCode.Length <= WordOperationObservabilityContract.MaximumErrorCodeCharacters
            && ErrorCodePattern().IsMatch(errorCode)
        )
        {
            return errorCode;
        }
        return outcome switch
        {
            WordOperationAuditOutcome.Cancelled => "CANCELLED",
            WordOperationAuditOutcome.Abandoned => "ABANDONED",
            _ => "UNCLASSIFIED_ERROR",
        };
    }

    private static void ValidateDescriptor(WordOperationDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (
            string.IsNullOrWhiteSpace(descriptor.OperationName)
            || descriptor.OperationName.Length
                > WordOperationObservabilityContract.MaximumOperationNameCharacters
            || !OperationNamePattern().IsMatch(descriptor.OperationName)
        )
        {
            throw new ArgumentException("Operation name is not a safe telemetry dimension.", nameof(descriptor));
        }
        if (
            string.IsNullOrWhiteSpace(descriptor.OperationVersion)
            || descriptor.OperationVersion.Length
                > WordOperationObservabilityContract.MaximumOperationVersionCharacters
            || !OperationVersionPattern().IsMatch(descriptor.OperationVersion)
        )
        {
            throw new ArgumentException("Operation version is invalid.", nameof(descriptor));
        }
        ArgumentNullException.ThrowIfNull(descriptor.Effects);
    }

    private static string SnakeCase(string value) => string.Concat(
        value.SelectMany(
            (character, index) =>
                char.IsUpper(character) && index > 0
                    ? new[] { '_', char.ToLowerInvariant(character) }
                    : new[] { char.ToLowerInvariant(character) }
        )
    );

    [GeneratedRegex("^[a-z][a-z0-9_]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex OperationNamePattern();

    [GeneratedRegex("^[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex OperationVersionPattern();

    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorCodePattern();

    [GeneratedRegex("^[a-z][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeDimensionPattern();
}

public sealed class WordOperationAuditScope : IDisposable
{
    private readonly WordOperationObservability _owner;
    private readonly WordOperationDescriptor _descriptor;
    private readonly DateTimeOffset _startedUtc;
    private readonly long _startedTimestamp;
    private readonly string _correlationId;
    private readonly Activity? _activity;
    private int _completed;

    internal WordOperationAuditScope(
        WordOperationObservability owner,
        WordOperationDescriptor descriptor,
        DateTimeOffset startedUtc,
        long startedTimestamp,
        string correlationId,
        Activity? activity
    )
    {
        _owner = owner;
        _descriptor = descriptor;
        _startedUtc = startedUtc;
        _startedTimestamp = startedTimestamp;
        _correlationId = correlationId;
        _activity = activity;
    }

    public void CompleteSucceeded() => Complete(WordOperationAuditOutcome.Succeeded, null);

    public void CompleteRejected(string errorCode) =>
        Complete(WordOperationAuditOutcome.Rejected, errorCode);

    public void CompleteCancelled() =>
        Complete(WordOperationAuditOutcome.Cancelled, "CANCELLED");

    public void CompleteFailed(string errorCode = "INTERNAL_ERROR") =>
        Complete(WordOperationAuditOutcome.Failed, errorCode);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }
        _owner.Complete(
            _descriptor,
            _startedUtc,
            _startedTimestamp,
            _correlationId,
            _activity,
            WordOperationAuditOutcome.Abandoned,
            "ABANDONED"
        );
        _owner.DisposeActivity(_activity);
    }

    private void Complete(WordOperationAuditOutcome outcome, string? errorCode)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            throw new InvalidOperationException("The operation observation is already complete.");
        }
        try
        {
            _owner.Complete(
                _descriptor,
                _startedUtc,
                _startedTimestamp,
                _correlationId,
                _activity,
                outcome,
                errorCode
            );
        }
        finally
        {
            _owner.DisposeActivity(_activity);
        }
    }
}

public static class WordAuditIntegrity
{
    public static string ComputeRecordSha256(WordAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        var canonical = string.Join(
            "\n",
            auditEvent.Contract,
            auditEvent.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            auditEvent.OccurredUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            auditEvent.DurationMicroseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            auditEvent.CorrelationId,
            auditEvent.OperationName,
            auditEvent.OperationVersion,
            auditEvent.Effects.ReadOnly ? "1" : "0",
            auditEvent.Effects.Destructive ? "1" : "0",
            auditEvent.Effects.Idempotent ? "1" : "0",
            auditEvent.Effects.OpenWorld ? "1" : "0",
            auditEvent.Outcome.ToString(),
            auditEvent.ErrorCode ?? "",
            auditEvent.PreviousRecordSha256
        );
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical))
        ).ToLowerInvariant();
    }

    public static bool VerifyChain(
        IReadOnlyList<WordAuditEvent> events,
        out int invalidIndex
    )
    {
        ArgumentNullException.ThrowIfNull(events);
        invalidIndex = -1;
        for (var index = 0; index < events.Count; index++)
        {
            var current = events[index];
            if (
                index > 0
                && !string.Equals(
                    current.PreviousRecordSha256,
                    events[index - 1].RecordSha256,
                    StringComparison.Ordinal
                )
            )
            {
                invalidIndex = index;
                return false;
            }
            if (
                !string.Equals(
                    current.RecordSha256,
                    ComputeRecordSha256(current),
                    StringComparison.Ordinal
                )
            )
            {
                invalidIndex = index;
                return false;
            }
        }
        return true;
    }
}
