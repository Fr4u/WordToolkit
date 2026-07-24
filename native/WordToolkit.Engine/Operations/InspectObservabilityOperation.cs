using WordToolkit.Engine.Observability;

namespace WordToolkit.Engine.Operations;

public static class InspectObservabilityContract
{
    public const string OperationName = "inspect_wordtoolkit_observability";
    public const string Contract = "wordtoolkit.inspect_observability/1.0";
    public const int DefaultPageSize = 12;
    public const int MaximumPageSize = 32;
}

public sealed record InspectObservabilityRequest(
    string View = "summary",
    int Offset = 0,
    int Limit = InspectObservabilityContract.DefaultPageSize,
    bool IncludeCorrelation = false,
    bool IncludeRecordHashes = false
);

public sealed record ObservabilityAuditEventItem(
    long Sequence,
    DateTimeOffset OccurredUtc,
    long DurationMicroseconds,
    string OperationName,
    string OperationVersion,
    WordOperationEffects Effects,
    string Outcome,
    string? ErrorCode,
    string? CorrelationId,
    string? PreviousRecordSha256,
    string? RecordSha256
);

public sealed record ObservabilityPaging(
    int Offset,
    int Limit,
    int Returned,
    int? NextOffset
);

public sealed record ObservabilitySecuritySummary(
    bool ReadsDocument,
    bool ReturnsDocumentContent,
    bool ReturnsArguments,
    bool ReturnsPaths,
    bool ReturnsRelationshipTargets,
    bool OpensWord,
    bool UsesNetwork
);

public sealed record InspectObservabilityResult(
    string OperationContract,
    string SnapshotContract,
    string View,
    bool TelemetryEnabled,
    bool AuditEnabled,
    int MemoryCapacity,
    int SinkQueueCapacity,
    long RetentionSeconds,
    WordAuditSinkMetadata Sink,
    WordOperationObservabilityCounters Counters,
    WordAuditIntegritySummary Integrity,
    IReadOnlyList<ObservabilityAuditEventItem> Events,
    ObservabilityPaging Paging,
    ObservabilitySecuritySummary Security
);

/// <summary>
/// Returns bounded, content-free runtime telemetry and audit health. It never reads a
/// document or exposes operation arguments, paths, document hashes, text, XML or binaries.
/// </summary>
public sealed class InspectObservabilityOperation
{
    private readonly WordOperationObservability _observability;

    public InspectObservabilityOperation(WordOperationObservability observability)
    {
        ArgumentNullException.ThrowIfNull(observability);
        _observability = observability;
    }

    public InspectObservabilityResult Execute(
        InspectObservabilityRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.View is not ("summary" or "events"))
        {
            throw InvalidInput("view must be 'summary' or 'events'");
        }
        if (request.Offset < 0)
        {
            throw InvalidInput("offset must be zero or greater");
        }
        if (request.Limit is < 1 or > InspectObservabilityContract.MaximumPageSize)
        {
            throw InvalidInput(
                $"limit must be between 1 and {InspectObservabilityContract.MaximumPageSize}"
            );
        }
        cancellationToken.ThrowIfCancellationRequested();
        var raw = _observability.Snapshot(
            request.View == "events" ? request.Offset : 0,
            request.View == "events" ? request.Limit : 0,
            cancellationToken
        );
        var events = request.View == "events"
            ? raw.Events.Select(item => new ObservabilityAuditEventItem(
                item.Sequence,
                item.OccurredUtc,
                item.DurationMicroseconds,
                item.OperationName,
                item.OperationVersion,
                item.Effects,
                SnakeCase(item.Outcome.ToString()),
                item.ErrorCode,
                request.IncludeCorrelation ? item.CorrelationId : null,
                request.IncludeRecordHashes ? item.PreviousRecordSha256 : null,
                request.IncludeRecordHashes ? item.RecordSha256 : null
            )).ToArray()
            : [];
        var total = checked((int)raw.Counters.RetainedAuditEventCount);
        var nextOffset =
            request.View == "events" && request.Offset + events.Length < total
                ? request.Offset + events.Length
                : (int?)null;
        var integrity = request.IncludeRecordHashes
            ? raw.Integrity
            : raw.Integrity with
            {
                FirstRetainedPreviousSha256 = null,
                LastRecordSha256 = null,
            };
        return new InspectObservabilityResult(
            InspectObservabilityContract.Contract,
            raw.Contract,
            request.View,
            raw.TelemetryEnabled,
            raw.AuditEnabled,
            raw.MemoryCapacity,
            raw.SinkQueueCapacity,
            raw.RetentionSeconds,
            raw.Sink,
            raw.Counters,
            integrity,
            Array.AsReadOnly(events),
            new ObservabilityPaging(
                request.View == "events" ? request.Offset : 0,
                request.View == "events" ? request.Limit : 0,
                events.Length,
                nextOffset
            ),
            new ObservabilitySecuritySummary(
                ReadsDocument: false,
                ReturnsDocumentContent: false,
                ReturnsArguments: false,
                ReturnsPaths: false,
                ReturnsRelationshipTargets: false,
                OpensWord: false,
                UsesNetwork: false
            )
        );
    }

    private static string SnakeCase(string value) => string.Concat(
        value.SelectMany(
            (character, index) =>
                char.IsUpper(character) && index > 0
                    ? new[] { '_', char.ToLowerInvariant(character) }
                    : new[] { char.ToLowerInvariant(character) }
        )
    );

    private static WordToolkitOperationException InvalidInput(string message) => new(
        "INVALID_INPUT",
        message
    );
}
