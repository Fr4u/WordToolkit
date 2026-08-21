namespace WordToolkit.Engine.Observability;

public static class WordOperationObservabilityContract
{
    public const string EventContract = "wordtoolkit.audit.event/1.0";
    public const string SnapshotContract = "wordtoolkit.observability.snapshot/1.0";
    public const string ActivitySourceName = "WordToolkit.Engine.Operations";
    public const string MeterName = "WordToolkit.Engine.Operations";
    public const int MinimumMemoryCapacity = 16;
    public const int MaximumMemoryCapacity = 4096;
    public const int MaximumOperationNameCharacters = 128;
    public const int MaximumOperationVersionCharacters = 32;
    public const int MaximumErrorCodeCharacters = 64;
}

public enum WordOperationAuditOutcome
{
    Succeeded,
    Rejected,
    Cancelled,
    Failed,
    Abandoned,
}

public sealed record WordOperationEffects(
    bool ReadOnly,
    bool Destructive,
    bool Idempotent,
    bool OpenWorld
);

public sealed record WordOperationDescriptor(
    string OperationName,
    string OperationVersion,
    WordOperationEffects Effects
);

public sealed record WordAuditSinkMetadata(
    string SinkId,
    string Kind,
    bool Durable,
    bool ExternalNetwork,
    bool ReturnsDocumentContent,
    bool ReturnsPaths
);

public interface IWordAuditSink
{
    WordAuditSinkMetadata Metadata { get; }

    void Write(WordAuditEvent auditEvent);
}

public sealed record WordOperationObservabilityOptions(
    bool TelemetryEnabled = false,
    bool AuditEnabled = false,
    int MemoryCapacity = 256,
    int SinkQueueCapacity = 256,
    TimeSpan? Retention = null,
    IWordAuditSink? Sink = null,
    TimeProvider? TimeProvider = null
);

public sealed record WordAuditEvent(
    string Contract,
    long Sequence,
    DateTimeOffset OccurredUtc,
    long DurationMicroseconds,
    string CorrelationId,
    string OperationName,
    string OperationVersion,
    WordOperationEffects Effects,
    WordOperationAuditOutcome Outcome,
    string? ErrorCode,
    string PreviousRecordSha256,
    string RecordSha256
);

public sealed record WordAuditIntegritySummary(
    string Algorithm,
    bool AppendChain,
    bool Authenticated,
    string? FirstRetainedPreviousSha256,
    string? LastRecordSha256
);

public sealed record WordOperationObservabilityCounters(
    long AttemptedOperationCount,
    long SucceededOperationCount,
    long RejectedOperationCount,
    long CancelledOperationCount,
    long FailedOperationCount,
    long AbandonedOperationCount,
    long RetainedAuditEventCount,
    long DroppedByCapacityCount,
    long DroppedByRetentionCount,
    long TelemetryEmissionFailureCount,
    long SinkWriteSuccessCount,
    long SinkWriteFailureCount,
    long SinkQueueDropCount,
    DateTimeOffset? LastTelemetryFailureUtc,
    DateTimeOffset? LastSinkFailureUtc
);

public sealed record WordOperationObservabilitySnapshot(
    string Contract,
    bool TelemetryEnabled,
    bool AuditEnabled,
    int MemoryCapacity,
    int SinkQueueCapacity,
    long RetentionSeconds,
    WordAuditSinkMetadata Sink,
    WordOperationObservabilityCounters Counters,
    WordAuditIntegritySummary Integrity,
    IReadOnlyList<WordAuditEvent> Events
);
