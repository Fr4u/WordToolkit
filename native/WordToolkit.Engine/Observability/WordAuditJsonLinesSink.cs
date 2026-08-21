using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WordToolkit.Engine.Observability;

public static class WordAuditJsonLinesContract
{
    public const string VerificationContract = "wordtoolkit.audit.verify/1.0";
    public const long MinimumFileBytes = 64 * 1024;
    public const long MaximumFileBytes = 64 * 1024 * 1024;
    public const long MaximumVerificationBytes = 256 * 1024 * 1024;
    public const int MaximumVerificationEvents = 100_000;
    public const int MaximumLineCharacters = 4096;
}

public sealed partial class WordAuditJsonLinesSink : IWordAuditSink, IDisposable
{
    private readonly object _sync = new();
    private readonly string _directoryPath;
    private readonly int _retentionDays;
    private readonly long _maximumFileBytes;
    private readonly string _instanceId;
    private DateOnly _currentDate;
    private int _currentIndex;
    private string? _currentPath;
    private bool _disposed;

    public WordAuditSinkMetadata Metadata { get; } = new(
        "wordtoolkit.audit.json_lines",
        "json_lines_file",
        Durable: true,
        ExternalNetwork: false,
        ReturnsDocumentContent: false,
        ReturnsPaths: false
    );

    public WordAuditJsonLinesSink(
        string directoryPath,
        int retentionDays = 7,
        long maximumFileBytes = 4 * 1024 * 1024
    )
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException("Audit directory is required.", nameof(directoryPath));
        }
        if (retentionDays is < 1 or > 365)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionDays));
        }
        if (
            maximumFileBytes
                is < WordAuditJsonLinesContract.MinimumFileBytes
                or > WordAuditJsonLinesContract.MaximumFileBytes
        )
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFileBytes));
        }

        _directoryPath = Path.GetFullPath(directoryPath);
        _retentionDays = retentionDays;
        _maximumFileBytes = maximumFileBytes;
        _instanceId = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        Directory.CreateDirectory(_directoryPath);
        PruneExpiredFiles(DateOnly.FromDateTime(DateTime.UtcNow));
    }

    public void Write(WordAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        var line = WordAuditJsonCodec.Serialize(auditEvent);
        if (line.Length > WordAuditJsonLinesContract.MaximumLineCharacters)
        {
            throw new InvalidOperationException("Audit event exceeds the line limit.");
        }
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        if (bytes.LongLength > _maximumFileBytes)
        {
            throw new InvalidOperationException("Audit event exceeds the file limit.");
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var currentDate = DateOnly.FromDateTime(DateTime.UtcNow);
            if (currentDate != _currentDate)
            {
                _currentDate = currentDate;
                _currentIndex = 0;
                _currentPath = null;
                PruneExpiredFiles(currentDate);
            }
            EnsureCurrentPath(bytes.LongLength);
            using var stream = new FileStream(
                _currentPath!,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.WriteThrough
            );
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
        }
    }

    private void EnsureCurrentPath(long nextBytes)
    {
        while (true)
        {
            _currentPath ??= Path.Combine(
                _directoryPath,
                $"wordtoolkit-audit-{_currentDate:yyyyMMdd}-{_instanceId}-{_currentIndex:D4}.jsonl"
            );
            var existingLength = File.Exists(_currentPath)
                ? new FileInfo(_currentPath).Length
                : 0;
            if (existingLength + nextBytes <= _maximumFileBytes)
            {
                return;
            }
            _currentIndex = checked(_currentIndex + 1);
            _currentPath = null;
        }
    }

    private void PruneExpiredFiles(DateOnly currentDate)
    {
        var minimumDate = currentDate.AddDays(-_retentionDays + 1);
        foreach (var path in Directory.EnumerateFiles(_directoryPath, "wordtoolkit-audit-*.jsonl"))
        {
            var name = Path.GetFileName(path);
            var match = FileNamePattern().Match(name);
            if (
                !match.Success
                || !DateOnly.TryParseExact(
                    match.Groups[1].Value,
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date
                )
                || date >= minimumDate
            )
            {
                continue;
            }
            File.Delete(path);
        }
    }

    [GeneratedRegex(
        "^wordtoolkit-audit-([0-9]{8})-[a-f0-9]{8}-[0-9]{4}\\.jsonl$",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex FileNamePattern();
}

public sealed record WordAuditLogVerificationResult(
    string Contract,
    bool Valid,
    long FileBytes,
    int EventCount,
    long? FirstSequence,
    long? LastSequence,
    string? FirstPreviousRecordSha256,
    string? LastRecordSha256,
    bool StartsAtGenesis,
    string? FailureCode,
    int? FailureLine,
    bool ReturnsDocumentContent,
    bool ReturnsPaths
);

public static class WordAuditJsonLinesVerifier
{
    public static WordAuditLogVerificationResult Verify(
        string path,
        long maximumBytes = WordAuditJsonLinesContract.MaximumVerificationBytes,
        int maximumEvents = WordAuditJsonLinesContract.MaximumVerificationEvents,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Audit log path is required.", nameof(path));
        }
        if (maximumBytes is < 1 or > WordAuditJsonLinesContract.MaximumVerificationBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }
        if (maximumEvents is < 1 or > WordAuditJsonLinesContract.MaximumVerificationEvents)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEvents));
        }
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Audit log does not exist.", fullPath);
        }
        var fileBytes = new FileInfo(fullPath).Length;
        if (fileBytes > maximumBytes)
        {
            return Failure(fileBytes, 0, "AUDIT_LOG_LIMIT", null, null, null, null, null);
        }

        var events = new List<WordAuditEvent>();
        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 4096,
            FileOptions.SequentialScan
        );
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: false
        );
        string? line;
        var lineNumber = 0;
        try
        {
            while ((line = reader.ReadLine()) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lineNumber++;
                if (
                    line.Length == 0
                    || line.Length > WordAuditJsonLinesContract.MaximumLineCharacters
                )
                {
                    return Failure(
                        fileBytes,
                        events.Count,
                        "AUDIT_LOG_INVALID",
                        lineNumber,
                        events.FirstOrDefault()?.Sequence,
                        events.LastOrDefault()?.Sequence,
                        events.FirstOrDefault()?.PreviousRecordSha256,
                        events.LastOrDefault()?.RecordSha256
                    );
                }
                if (events.Count >= maximumEvents)
                {
                    return Failure(
                        fileBytes,
                        events.Count,
                        "AUDIT_LOG_LIMIT",
                        lineNumber,
                        events.FirstOrDefault()?.Sequence,
                        events.LastOrDefault()?.Sequence,
                        events.FirstOrDefault()?.PreviousRecordSha256,
                        events.LastOrDefault()?.RecordSha256
                    );
                }
                events.Add(WordAuditJsonCodec.Deserialize(line));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or DecoderFallbackException or FormatException
        )
        {
            return Failure(
                fileBytes,
                events.Count,
                "AUDIT_LOG_INVALID",
                lineNumber,
                events.FirstOrDefault()?.Sequence,
                events.LastOrDefault()?.Sequence,
                events.FirstOrDefault()?.PreviousRecordSha256,
                events.LastOrDefault()?.RecordSha256
            );
        }

        for (var index = 1; index < events.Count; index++)
        {
            if (events[index].Sequence != events[index - 1].Sequence + 1)
            {
                return Failure(
                    fileBytes,
                    events.Count,
                    "AUDIT_SEQUENCE_INVALID",
                    index + 1,
                    events[0].Sequence,
                    events[^1].Sequence,
                    events[0].PreviousRecordSha256,
                    events[^1].RecordSha256
                );
            }
        }
        if (!WordAuditIntegrity.VerifyChain(events, out var invalidIndex))
        {
            return Failure(
                fileBytes,
                events.Count,
                "AUDIT_CHAIN_INVALID",
                invalidIndex + 1,
                events.FirstOrDefault()?.Sequence,
                events.LastOrDefault()?.Sequence,
                events.FirstOrDefault()?.PreviousRecordSha256,
                events.LastOrDefault()?.RecordSha256
            );
        }
        return new WordAuditLogVerificationResult(
            WordAuditJsonLinesContract.VerificationContract,
            Valid: true,
            fileBytes,
            events.Count,
            events.FirstOrDefault()?.Sequence,
            events.LastOrDefault()?.Sequence,
            events.FirstOrDefault()?.PreviousRecordSha256,
            events.LastOrDefault()?.RecordSha256,
            events.Count > 0
                && events[0].Sequence == 1
                && events[0].PreviousRecordSha256 == new string('0', 64),
            FailureCode: null,
            FailureLine: null,
            ReturnsDocumentContent: false,
            ReturnsPaths: false
        );
    }

    private static WordAuditLogVerificationResult Failure(
        long fileBytes,
        int eventCount,
        string failureCode,
        int? failureLine,
        long? firstSequence,
        long? lastSequence,
        string? firstPreviousHash,
        string? lastHash
    ) => new(
        WordAuditJsonLinesContract.VerificationContract,
        Valid: false,
        fileBytes,
        eventCount,
        firstSequence,
        lastSequence,
        firstPreviousHash,
        lastHash,
        StartsAtGenesis: false,
        failureCode,
        failureLine,
        ReturnsDocumentContent: false,
        ReturnsPaths: false
    );
}

internal static partial class WordAuditJsonCodec
{
    private static readonly string[] EventProperties =
    [
        "contract",
        "sequence",
        "occurred_utc",
        "duration_microseconds",
        "correlation_id",
        "operation_name",
        "operation_version",
        "effects",
        "outcome",
        "error_code",
        "previous_record_sha256",
        "record_sha256",
    ];
    private static readonly string[] EffectProperties =
    [
        "read_only",
        "destructive",
        "idempotent",
        "open_world",
    ];

    public static string Serialize(WordAuditEvent auditEvent)
    {
        using var stream = new MemoryStream(1024);
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("contract", auditEvent.Contract);
            writer.WriteNumber("sequence", auditEvent.Sequence);
            writer.WriteString("occurred_utc", auditEvent.OccurredUtc.ToUniversalTime());
            writer.WriteNumber("duration_microseconds", auditEvent.DurationMicroseconds);
            writer.WriteString("correlation_id", auditEvent.CorrelationId);
            writer.WriteString("operation_name", auditEvent.OperationName);
            writer.WriteString("operation_version", auditEvent.OperationVersion);
            writer.WriteStartObject("effects");
            writer.WriteBoolean("read_only", auditEvent.Effects.ReadOnly);
            writer.WriteBoolean("destructive", auditEvent.Effects.Destructive);
            writer.WriteBoolean("idempotent", auditEvent.Effects.Idempotent);
            writer.WriteBoolean("open_world", auditEvent.Effects.OpenWorld);
            writer.WriteEndObject();
            writer.WriteString("outcome", SnakeCase(auditEvent.Outcome.ToString()));
            if (auditEvent.ErrorCode is null)
            {
                writer.WriteNull("error_code");
            }
            else
            {
                writer.WriteString("error_code", auditEvent.ErrorCode);
            }
            writer.WriteString("previous_record_sha256", auditEvent.PreviousRecordSha256);
            writer.WriteString("record_sha256", auditEvent.RecordSha256);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static WordAuditEvent Deserialize(string json)
    {
        using var document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            }
        );
        var root = document.RootElement;
        RequireExactProperties(root, EventProperties);
        var effectsNode = root.GetProperty("effects");
        RequireExactProperties(effectsNode, EffectProperties);
        var contract = RequiredString(root, "contract");
        if (contract != WordOperationObservabilityContract.EventContract)
        {
            throw new FormatException("Unknown audit event contract.");
        }
        var sequence = root.GetProperty("sequence").GetInt64();
        var duration = root.GetProperty("duration_microseconds").GetInt64();
        if (sequence < 1 || duration < 0)
        {
            throw new FormatException("Audit numeric field is invalid.");
        }
        var occurred = root.GetProperty("occurred_utc").GetDateTimeOffset();
        var correlation = RequiredString(root, "correlation_id");
        var operation = RequiredString(root, "operation_name");
        var version = RequiredString(root, "operation_version");
        var previousHash = RequiredString(root, "previous_record_sha256");
        var recordHash = RequiredString(root, "record_sha256");
        if (
            !LowerHex32Pattern().IsMatch(correlation)
            || !OperationNamePattern().IsMatch(operation)
            || !VersionPattern().IsMatch(version)
            || !LowerHex64Pattern().IsMatch(previousHash)
            || !LowerHex64Pattern().IsMatch(recordHash)
        )
        {
            throw new FormatException("Audit string field is invalid.");
        }
        var outcome = RequiredString(root, "outcome") switch
        {
            "succeeded" => WordOperationAuditOutcome.Succeeded,
            "rejected" => WordOperationAuditOutcome.Rejected,
            "cancelled" => WordOperationAuditOutcome.Cancelled,
            "failed" => WordOperationAuditOutcome.Failed,
            "abandoned" => WordOperationAuditOutcome.Abandoned,
            _ => throw new FormatException("Audit outcome is invalid."),
        };
        var errorNode = root.GetProperty("error_code");
        string? errorCode = errorNode.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => errorNode.GetString(),
            _ => throw new FormatException("Audit error code is invalid."),
        };
        if (
            errorCode is not null
            && !ErrorCodePattern().IsMatch(errorCode)
        )
        {
            throw new FormatException("Audit error code is invalid.");
        }
        if (
            outcome == WordOperationAuditOutcome.Succeeded != (errorCode is null)
        )
        {
            throw new FormatException("Audit outcome and error code disagree.");
        }
        return new WordAuditEvent(
            contract,
            sequence,
            occurred,
            duration,
            correlation,
            operation,
            version,
            new WordOperationEffects(
                effectsNode.GetProperty("read_only").GetBoolean(),
                effectsNode.GetProperty("destructive").GetBoolean(),
                effectsNode.GetProperty("idempotent").GetBoolean(),
                effectsNode.GetProperty("open_world").GetBoolean()
            ),
            outcome,
            errorCode,
            previousHash,
            recordHash
        );
    }

    private static void RequireExactProperties(JsonElement element, string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Audit value is not an object.");
        }
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!actual.Add(property.Name))
            {
                throw new FormatException("Audit object contains a duplicate field.");
            }
        }
        if (
            actual.Count != expected.Length
            || expected.Any(name => !actual.Contains(name))
        )
        {
            throw new FormatException("Audit object contains unknown or missing fields.");
        }
    }

    private static string RequiredString(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString()))
        {
            throw new FormatException($"Audit field '{name}' is invalid.");
        }
        return value.GetString()!;
    }

    private static string SnakeCase(string value) => string.Concat(
        value.SelectMany(
            (character, index) =>
                char.IsUpper(character) && index > 0
                    ? new[] { '_', char.ToLowerInvariant(character) }
                    : new[] { char.ToLowerInvariant(character) }
        )
    );

    [GeneratedRegex("^[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex LowerHex32Pattern();

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex LowerHex64Pattern();

    [GeneratedRegex("^[a-z][a-z0-9_]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex OperationNamePattern();

    [GeneratedRegex("^[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorCodePattern();
}
