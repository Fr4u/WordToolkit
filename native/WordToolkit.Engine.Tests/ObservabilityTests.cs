using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using WordToolkit.Engine.Observability;
using WordToolkit.Engine.Operations;

namespace WordToolkit.Engine.Tests;

public sealed class ObservabilityTests
{
    private static readonly WordOperationDescriptor ReadDescriptor = new(
        "inspect_ooxml_package",
        "1.0",
        new WordOperationEffects(
            ReadOnly: true,
            Destructive: false,
            Idempotent: true,
            OpenWorld: false
        )
    );

    [Fact]
    public void AuditIsOptInAndDisabledModeRetainsNoEvents()
    {
        var observability = new WordOperationObservability(
            new WordOperationObservabilityOptions()
        );

        using (var scope = observability.Begin(ReadDescriptor))
        {
            scope.CompleteSucceeded();
        }

        var snapshot = observability.Snapshot();
        Assert.False(snapshot.TelemetryEnabled);
        Assert.False(snapshot.AuditEnabled);
        Assert.Equal("wordtoolkit.audit.disabled", snapshot.Sink.SinkId);
        Assert.Empty(snapshot.Events);
        Assert.Equal(1, snapshot.Counters.AttemptedOperationCount);
        Assert.Equal(1, snapshot.Counters.SucceededOperationCount);
        Assert.Null(snapshot.Integrity.LastRecordSha256);
    }

    [Fact]
    public void MemoryAuditReportsItsActualNonDurableSinkMode()
    {
        using var observability = Enabled();

        var snapshot = observability.Snapshot();

        Assert.True(snapshot.AuditEnabled);
        Assert.Equal("wordtoolkit.audit.memory", snapshot.Sink.SinkId);
        Assert.Equal("bounded_memory", snapshot.Sink.Kind);
        Assert.False(snapshot.Sink.Durable);
        Assert.False(snapshot.Sink.ExternalNetwork);
        Assert.False(snapshot.Sink.ReturnsDocumentContent);
        Assert.False(snapshot.Sink.ReturnsPaths);
    }

    [Fact]
    public void AuditRecordsOnlyValidatedContentFreeDimensions()
    {
        var observability = Enabled();
        using (var scope = observability.Begin(ReadDescriptor))
        {
            scope.CompleteRejected("INVALID_INPUT");
        }
        using (var scope = observability.Begin(ReadDescriptor))
        {
            scope.CompleteFailed("secret\r\npath=C:\\Users\\Admin");
        }

        var events = observability.Snapshot().Events;
        Assert.Equal(2, events.Count);
        Assert.Equal("INVALID_INPUT", events[0].ErrorCode);
        Assert.Equal("UNCLASSIFIED_ERROR", events[1].ErrorCode);
        Assert.All(events, item =>
        {
            Assert.Equal("inspect_ooxml_package", item.OperationName);
            Assert.Equal(32, item.CorrelationId.Length);
            Assert.DoesNotContain("Users", item.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", item.ToString(), StringComparison.OrdinalIgnoreCase);
        });
        Assert.Throws<ArgumentException>(() =>
            observability.Begin(ReadDescriptor with { OperationName = "C:\\private.docx" })
        );
    }

    [Fact]
    public void AuditChainDetectsMutationAndSurvivesCapacityTrimming()
    {
        var observability = Enabled(capacity: 16);
        for (var index = 0; index < 20; index++)
        {
            using var scope = observability.Begin(ReadDescriptor);
            scope.CompleteSucceeded();
        }

        var snapshot = observability.Snapshot();
        Assert.Equal(16, snapshot.Events.Count);
        Assert.Equal(4, snapshot.Counters.DroppedByCapacityCount);
        Assert.Equal(5, snapshot.Events[0].Sequence);
        Assert.True(WordAuditIntegrity.VerifyChain(snapshot.Events, out var invalidIndex));
        Assert.Equal(-1, invalidIndex);
        var changed = snapshot.Events.ToArray();
        changed[7] = changed[7] with { DurationMicroseconds = changed[7].DurationMicroseconds + 1 };
        Assert.False(WordAuditIntegrity.VerifyChain(changed, out invalidIndex));
        Assert.Equal(7, invalidIndex);
    }

    [Fact]
    public void RetentionPrunesWithoutResettingTheAppendChain()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 24, 10, 0, 0, TimeSpan.Zero));
        var observability = Enabled(
            timeProvider: time,
            retention: TimeSpan.FromMinutes(1)
        );
        using (var scope = observability.Begin(ReadDescriptor))
        {
            scope.CompleteSucceeded();
        }
        time.Advance(TimeSpan.FromMinutes(2));
        using (var scope = observability.Begin(ReadDescriptor))
        {
            scope.CompleteSucceeded();
        }

        var snapshot = observability.Snapshot();
        var retained = Assert.Single(snapshot.Events);
        Assert.Equal(2, retained.Sequence);
        Assert.NotEqual(new string('0', 64), retained.PreviousRecordSha256);
        Assert.Equal(1, snapshot.Counters.DroppedByRetentionCount);
    }

    [Fact]
    public async Task SinkFailuresAreCountedAndNeverReplaceOperationOutcome()
    {
        using var observability = Enabled(sink: new ThrowingSink());
        using (var scope = observability.Begin(ReadDescriptor))
        {
            scope.CompleteSucceeded();
        }
        await observability.FlushAsync();

        var snapshot = observability.Snapshot();
        Assert.Single(snapshot.Events);
        Assert.Equal(1, snapshot.Counters.SucceededOperationCount);
        Assert.Equal(1, snapshot.Counters.SinkWriteFailureCount);
        Assert.Equal(0, snapshot.Counters.SinkWriteSuccessCount);
        Assert.NotNull(snapshot.Counters.LastSinkFailureUtc);
    }

    [Fact]
    public void ConcurrentCompletionProducesOneOrderedVerifiedChain()
    {
        var observability = Enabled(capacity: 256);
        Parallel.For(0, 200, _ =>
        {
            using var scope = observability.Begin(ReadDescriptor);
            scope.CompleteSucceeded();
        });

        var snapshot = observability.Snapshot(limit: 32);
        Assert.Equal(200, snapshot.Counters.AttemptedOperationCount);
        Assert.Equal(200, snapshot.Counters.RetainedAuditEventCount);
        var all = observability.Snapshot(limit: 32).Events;
        Assert.Equal(32, all.Count);
        Assert.True(WordAuditIntegrity.VerifyChain(all, out _));
        Assert.Equal(32, all.Select(item => item.Sequence).Distinct().Count());
    }

    [Fact]
    public async Task JsonLinesSinkRoundTripsAndVerifierDetectsTampering()
    {
        var directory = TemporaryDirectory();
        try
        {
            using var sink = new WordAuditJsonLinesSink(directory);
            using var observability = Enabled(sink: sink);
            for (var index = 0; index < 3; index++)
            {
                using var scope = observability.Begin(ReadDescriptor);
                scope.CompleteSucceeded();
            }
            await observability.FlushAsync();
            var path = Assert.Single(Directory.GetFiles(directory, "*.jsonl"));
            var verified = WordAuditJsonLinesVerifier.Verify(path);
            Assert.True(verified.Valid);
            Assert.Equal(3, verified.EventCount);
            Assert.False(verified.ReturnsDocumentContent);
            Assert.False(verified.ReturnsPaths);

            var lines = File.ReadAllLines(path);
            lines[1] = lines[1].Replace(
                "\"duration_microseconds\":",
                "\"duration_microseconds\":9",
                StringComparison.Ordinal
            );
            File.WriteAllLines(path, lines);
            var tampered = WordAuditJsonLinesVerifier.Verify(path);
            Assert.False(tampered.Valid);
            Assert.Equal("AUDIT_CHAIN_INVALID", tampered.FailureCode);
            Assert.Equal(2, tampered.FailureLine);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task JsonLinesVerifierRejectsUnknownFieldsAndBounds()
    {
        var directory = TemporaryDirectory();
        try
        {
            using var sink = new WordAuditJsonLinesSink(directory);
            using var observability = Enabled(sink: sink);
            using (var scope = observability.Begin(ReadDescriptor))
            {
                scope.CompleteSucceeded();
            }
            await observability.FlushAsync();
            var path = Assert.Single(Directory.GetFiles(directory, "*.jsonl"));
            var text = File.ReadAllText(path).TrimEnd();
            File.WriteAllText(path, text[..^1] + ",\"payload\":\"secret\"}\n");
            var invalid = WordAuditJsonLinesVerifier.Verify(path);
            Assert.False(invalid.Valid);
            Assert.Equal("AUDIT_LOG_INVALID", invalid.FailureCode);

            Assert.Equal(
                "AUDIT_LOG_LIMIT",
                WordAuditJsonLinesVerifier.Verify(path, maximumBytes: 1).FailureCode
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ActivityAndMetricsUseOnlyFixedLowCardinalityDimensions()
    {
        Activity? stoppedActivity = null;
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == WordOperationObservabilityContract.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStopped = activity => stoppedActivity = activity,
        };
        ActivitySource.AddActivityListener(activityListener);
        var measurements = new ConcurrentBag<(
            string Instrument,
            IReadOnlyDictionary<string, object?> Tags
        )>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == WordOperationObservabilityContract.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            measurements.Add((instrument.Name, tags.ToArray().ToDictionary(item => item.Key, item => item.Value)))
        );
        meterListener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
            measurements.Add((instrument.Name, tags.ToArray().ToDictionary(item => item.Key, item => item.Value)))
        );
        meterListener.Start();
        var observability = new WordOperationObservability(
            new WordOperationObservabilityOptions(TelemetryEnabled: true)
        );

        using (var scope = observability.Begin(ReadDescriptor))
        {
            scope.CompleteSucceeded();
        }

        Assert.NotNull(stoppedActivity);
        Assert.Equal("inspect_ooxml_package", stoppedActivity!.OperationName);
        Assert.Equal(
            "inspect_ooxml_package",
            stoppedActivity.GetTagItem("wordtoolkit.operation.name")
        );
        Assert.Null(stoppedActivity.GetTagItem("wordtoolkit.document.path"));
        Assert.Contains(measurements, item => item.Instrument == "wordtoolkit.operation.count");
        Assert.Contains(measurements, item => item.Instrument == "wordtoolkit.operation.duration");
        Assert.All(measurements, item =>
        {
            Assert.Equal(
                ["wordtoolkit.operation.name", "wordtoolkit.operation.outcome"],
                item.Tags.Keys.Order(StringComparer.Ordinal).ToArray()
            );
            Assert.Equal(
                "inspect_ooxml_package",
                item.Tags["wordtoolkit.operation.name"]
            );
        });
    }

    [Fact]
    public void ThrowingTelemetryListenerCannotReplaceTheOperationOutcome()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == WordOperationObservabilityContract.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStopped = _ => throw new InvalidOperationException("listener failed"),
        };
        ActivitySource.AddActivityListener(listener);
        using var observability = new WordOperationObservability(
            new WordOperationObservabilityOptions(
                TelemetryEnabled: true,
                AuditEnabled: true
            )
        );

        using (var scope = observability.Begin(ReadDescriptor))
        {
            scope.CompleteSucceeded();
        }

        var snapshot = observability.Snapshot();
        Assert.Equal(1, snapshot.Counters.SucceededOperationCount);
        Assert.Single(snapshot.Events);
        Assert.True(snapshot.Counters.TelemetryEmissionFailureCount > 0);
        Assert.NotNull(snapshot.Counters.LastTelemetryFailureUtc);
    }

    [Fact]
    public void PublicInspectionKeepsEventsAndCorrelationBehindIndependentOptIns()
    {
        var observability = Enabled();
        using (var scope = observability.Begin(ReadDescriptor))
        {
            scope.CompleteSucceeded();
        }
        var operation = new InspectObservabilityOperation(observability);

        var summary = operation.Execute(new InspectObservabilityRequest());
        Assert.Empty(summary.Events);
        Assert.Null(summary.Integrity.LastRecordSha256);
        Assert.False(summary.Security.ReturnsDocumentContent);
        Assert.False(summary.Security.ReturnsArguments);
        Assert.False(summary.Security.ReturnsPaths);

        var safeEvents = operation.Execute(
            new InspectObservabilityRequest(View: "events")
        );
        var safeEvent = Assert.Single(safeEvents.Events);
        Assert.Null(safeEvent.CorrelationId);
        Assert.Null(safeEvent.RecordSha256);
        Assert.Null(safeEvents.Integrity.LastRecordSha256);

        var detailed = operation.Execute(
            new InspectObservabilityRequest(
                View: "events",
                IncludeCorrelation: true,
                IncludeRecordHashes: true
            )
        );
        var detailedEvent = Assert.Single(detailed.Events);
        Assert.Equal(32, detailedEvent.CorrelationId!.Length);
        Assert.Equal(64, detailedEvent.RecordSha256!.Length);
        Assert.Equal(64, detailed.Integrity.LastRecordSha256!.Length);
    }

    [Fact]
    public void SinkMetadataCannotSmugglePathsContentOrNetworkExport()
    {
        Assert.Throws<ArgumentException>(() => Enabled(sink: new UnsafeSink(
            new WordAuditSinkMetadata(
                "C:\\private",
                "file",
                false,
                false,
                false,
                false
            )
        )));
        Assert.Throws<ArgumentException>(() => Enabled(sink: new UnsafeSink(
            new WordAuditSinkMetadata(
                "wordtoolkit.test.network",
                "network",
                false,
                true,
                false,
                false
            )
        )));
        Assert.Throws<ArgumentException>(() => Enabled(sink: new UnsafeSink(
            new WordAuditSinkMetadata(
                "wordtoolkit.test.content",
                "memory",
                false,
                false,
                true,
                false
            )
        )));
    }

    [Fact]
    public async Task BlockingSinkCannotBlockOperationsAndQueueOverflowIsExplicit()
    {
        var sink = new BlockingSink();
        using var observability = Enabled(sink: sink, sinkQueueCapacity: 16);
        var started = Stopwatch.GetTimestamp();
        for (var index = 0; index < 128; index++)
        {
            using var scope = observability.Begin(ReadDescriptor);
            scope.CompleteSucceeded();
        }
        var elapsed = Stopwatch.GetElapsedTime(started);
        var saturated = observability.Snapshot();
        sink.Release();
        await observability.FlushAsync();
        var drained = observability.Snapshot();

        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"Operation path blocked for {elapsed}.");
        Assert.True(saturated.Counters.SinkQueueDropCount > 0);
        Assert.Equal(128, saturated.Counters.SucceededOperationCount);
        Assert.True(drained.Counters.SinkWriteSuccessCount > 0);
    }

    private static WordOperationObservability Enabled(
        int capacity = 256,
        TimeProvider? timeProvider = null,
        TimeSpan? retention = null,
        IWordAuditSink? sink = null,
        int sinkQueueCapacity = 256
    ) => new(
        new WordOperationObservabilityOptions(
            AuditEnabled: true,
            MemoryCapacity: capacity,
            SinkQueueCapacity: sinkQueueCapacity,
            Retention: retention,
            Sink: sink,
            TimeProvider: timeProvider
        )
    );

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wordtoolkit-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class ThrowingSink : IWordAuditSink
    {
        public WordAuditSinkMetadata Metadata { get; } = new(
            "wordtoolkit.test.throwing",
            "test",
            Durable: false,
            ExternalNetwork: false,
            ReturnsDocumentContent: false,
            ReturnsPaths: false
        );

        public void Write(WordAuditEvent auditEvent) => throw new IOException("injected");
    }

    private sealed class UnsafeSink(WordAuditSinkMetadata metadata) : IWordAuditSink
    {
        public WordAuditSinkMetadata Metadata { get; } = metadata;

        public void Write(WordAuditEvent auditEvent) { }
    }

    private sealed class BlockingSink : IWordAuditSink
    {
        private readonly ManualResetEventSlim _release = new(initialState: false);

        public WordAuditSinkMetadata Metadata { get; } = new(
            "wordtoolkit.test.blocking",
            "test",
            Durable: false,
            ExternalNetwork: false,
            ReturnsDocumentContent: false,
            ReturnsPaths: false
        );

        public void Write(WordAuditEvent auditEvent) => _release.Wait();

        public void Release() => _release.Set();
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan value)
        {
            _utcNow += value;
            _timestamp += value.Ticks;
        }
    }
}
