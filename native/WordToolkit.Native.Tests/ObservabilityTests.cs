using System.Text.Json;
using WordToolkit.Engine.Observability;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Resources;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class ObservabilityTests
{
    [Fact]
    public void EnvironmentConfigurationIsExplicitBoundedAndPathFree()
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WORDTOOLKIT_TELEMETRY_ENABLED"] = "true",
            ["WORDTOOLKIT_AUDIT_MODE"] = "memory",
            ["WORDTOOLKIT_AUDIT_MEMORY_EVENTS"] = "64",
            ["WORDTOOLKIT_AUDIT_RETENTION_DAYS"] = "3",
        };
        using var host = NativeObservabilityHost.Create(name =>
            settings.TryGetValue(name, out var value) ? value : null
        );
        var snapshot = host.Observability.Snapshot();
        Assert.True(snapshot.TelemetryEnabled);
        Assert.True(snapshot.AuditEnabled);
        Assert.Equal(64, snapshot.MemoryCapacity);
        Assert.Equal(TimeSpan.FromDays(3).TotalSeconds, snapshot.RetentionSeconds);
        Assert.False(snapshot.Sink.Durable);
        Assert.False(snapshot.Sink.ReturnsPaths);

        settings["WORDTOOLKIT_AUDIT_DIRECTORY"] = "C:\\private\\audit";
        var exception = Assert.Throws<InvalidOperationException>(() =>
            NativeObservabilityHost.Create(name =>
                settings.TryGetValue(name, out var value) ? value : null
            )
        );
        Assert.DoesNotContain("private", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task McpAuditRecordsActualActionWithoutArgumentsOrDocumentContent()
    {
        var observability = Enabled();
        const string secret = "DOCUMENT-SECRET-9c5c251f";
        var input = string.Join(
            "\n",
            JsonSerializer.Serialize(
                new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "tools/call",
                    @params = new
                    {
                        name = "execute_wordtoolkit_action",
                        arguments = new
                        {
                            action = "apply_live_word_operations",
                            arguments = new
                            {
                                live_document_id = "live_1",
                                operations = new[] { new { type = "text", text = secret } },
                            },
                        },
                    },
                }
            ),
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"execute_wordtoolkit_action","arguments":{"action":"inspect_wordtoolkit_observability","arguments":{"view":"events"}}}}"""
        ) + "\n";
        var output = new StringWriter();
        var server = new McpServer(
            new StringReader(input),
            output,
            ToolCatalog.LoadNativeWordTools(),
            new ObservabilityToolHandler(observability),
            observability: observability
        );

        await server.RunAsync();

        var serialized = output.ToString();
        Assert.DoesNotContain(secret, serialized, StringComparison.Ordinal);
        var responses = serialized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line))
            .ToArray();
        try
        {
            var data = responses[1].RootElement
                .GetProperty("result")
                .GetProperty("structuredContent")
                .GetProperty("data");
            var item = Assert.Single(data.GetProperty("events").EnumerateArray());
            Assert.Equal(
                "apply_live_word_operations",
                item.GetProperty("operation_name").GetString()
            );
            Assert.Equal("succeeded", item.GetProperty("outcome").GetString());
            Assert.False(item.TryGetProperty("correlation_id", out _));
            Assert.False(item.TryGetProperty("record_sha256", out _));
            Assert.False(data.GetProperty("security").GetProperty("returns_arguments").GetBoolean());
            Assert.False(data.GetProperty("security").GetProperty("returns_document_content").GetBoolean());
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task UnknownActionIsCollapsedToSafeFixedAuditDimension()
    {
        var observability = Enabled();
        const string hostileName = "secret\r\nC:\\private.docx";
        var input = string.Join(
            "\n",
            JsonSerializer.Serialize(
                new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "tools/call",
                    @params = new { name = hostileName, arguments = new { } },
                }
            ),
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"execute_wordtoolkit_action","arguments":{"action":"inspect_wordtoolkit_observability","arguments":{"view":"events"}}}}"""
        ) + "\n";
        var output = new StringWriter();
        var server = new McpServer(
            new StringReader(input),
            output,
            ToolCatalog.LoadNativeWordTools(),
            new ObservabilityToolHandler(observability),
            observability: observability
        );

        await server.RunAsync();

        Assert.DoesNotContain("private.docx", output.ToString(), StringComparison.OrdinalIgnoreCase);
        var events = observability.Snapshot(limit: 32).Events;
        Assert.Equal("wordtoolkit_unknown_action", events[0].OperationName);
        Assert.Equal(WordOperationAuditOutcome.Rejected, events[0].Outcome);
        Assert.Equal("INVALID_INPUT", events[0].ErrorCode);
    }

    [Fact]
    public async Task UnknownNestedActionIsCollapsedBeforeGatewayRejection()
    {
        var observability = Enabled();
        const string hostileName = "secret\r\nC:\\private.docx";
        var input = JsonSerializer.Serialize(
            new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/call",
                @params = new
                {
                    name = "execute_wordtoolkit_action",
                    arguments = new { action = hostileName, arguments = new { } },
                },
            }
        ) + "\n";
        var server = new McpServer(
            new StringReader(input),
            new StringWriter(),
            ToolCatalog.LoadNativeWordTools(),
            new ObservabilityToolHandler(observability),
            observability: observability
        );

        await server.RunAsync();

        var auditEvent = Assert.Single(observability.Snapshot(limit: 32).Events);
        Assert.Equal("wordtoolkit_unknown_action", auditEvent.OperationName);
        Assert.Equal(WordOperationAuditOutcome.Rejected, auditEvent.Outcome);
        Assert.Equal("INVALID_INPUT", auditEvent.ErrorCode);
        Assert.DoesNotContain(
            "private.docx",
            JsonSerializer.Serialize(auditEvent),
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void ActionContractIsLazyClosedVersionedAndContentFree()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        Assert.Equal(143, catalog.ActionCount);
        using var document = JsonDocument.Parse(
            catalog.InspectAction("inspect_wordtoolkit_observability").ToJsonString()
        );
        var tool = document.RootElement.GetProperty("tool");
        Assert.Equal("1.0", tool.GetProperty("operationVersion").GetString());
        Assert.False(
            tool.GetProperty("annotations").GetProperty("destructiveHint").GetBoolean()
        );
        Assert.Equal(
            "wordtoolkit.inspect_observability/1.0",
            tool.GetProperty("outputSchema")
                .GetProperty("properties")
                .GetProperty("data")
                .GetProperty("properties")
                .GetProperty("operation_contract")
                .GetProperty("const")
                .GetString()
        );
        Assert.False(
            tool.GetProperty("outputSchema")
                .GetProperty("properties")
                .GetProperty("data")
                .GetProperty("properties")
                .GetProperty("security")
                .GetProperty("properties")
                .GetProperty("returns_document_content")
                .GetProperty("const")
                .GetBoolean()
        );
    }

    [Fact]
    public async Task AuditLogCliVerifiesWithoutReturningThePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wordtoolkit-native-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var sink = new WordAuditJsonLinesSink(directory);
            using var observability = Enabled(sink);
            using (var scope = observability.Begin(Descriptor("inspect_ooxml_package")))
            {
                scope.CompleteSucceeded();
            }
            await observability.FlushAsync();
            var path = Assert.Single(Directory.GetFiles(directory, "*.jsonl"));
            var output = new StringWriter();
            var error = new StringWriter();
            Assert.Equal(
                0,
                AuditLogCli.Run(["verify", path, "--format", "json"], output, error)
            );
            Assert.Empty(error.ToString());
            Assert.DoesNotContain(path, output.ToString(), StringComparison.OrdinalIgnoreCase);
            using var result = JsonDocument.Parse(output.ToString());
            Assert.True(result.RootElement.GetProperty("valid").GetBoolean());
            Assert.Equal(1, result.RootElement.GetProperty("event_count").GetInt32());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RealServiceInspectionDoesNotInvokeWordAndRejectsUnknownArguments()
    {
        var host = new LifecycleFakeHost();
        var observability = Enabled();
        using (var scope = observability.Begin(Descriptor("inspect_ooxml_package")))
        {
            scope.CompleteSucceeded();
        }
        var service = new WordLiveService(
            host,
            () => new WordOperationResourceLease(),
            observability
        );
        using var arguments = JsonDocument.Parse(
            """{"view":"events","include_correlation":true,"include_record_hashes":true}"""
        );

        var result = await service.CallAsync(
            "inspect_wordtoolkit_observability",
            arguments.RootElement,
            CancellationToken.None
        );

        var json = JsonSerializer.Serialize(result, JsonDefaults.Compact);
        Assert.Contains("inspect_ooxml_package", json, StringComparison.Ordinal);
        Assert.Contains("correlation_id", json, StringComparison.Ordinal);
        Assert.Contains("record_sha256", json, StringComparison.Ordinal);
        Assert.False(host.LaunchIfMissing);

        using var malformed = JsonDocument.Parse("""{"payload":"secret"}""");
        var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
            service.CallAsync(
                "inspect_wordtoolkit_observability",
                malformed.RootElement,
                CancellationToken.None
            )
        );
        Assert.Equal("INVALID_INPUT", exception.ErrorCode);
    }

    private static WordOperationObservability Enabled(IWordAuditSink? sink = null) => new(
        new WordOperationObservabilityOptions(
            AuditEnabled: true,
            Sink: sink
        )
    );

    private static WordOperationDescriptor Descriptor(string name) => new(
        name,
        "1.0",
        new WordOperationEffects(true, false, true, false)
    );

    private sealed class ObservabilityToolHandler(
        WordOperationObservability observability
    ) : IToolHandler
    {
        public Task<object> CallAsync(
            string name,
            JsonElement arguments,
            CancellationToken cancellationToken
        )
        {
            if (name == "inspect_wordtoolkit_observability")
            {
                var view = arguments.TryGetProperty("view", out var viewNode)
                    ? viewNode.GetString() ?? "summary"
                    : "summary";
                object result = new InspectObservabilityOperation(observability).Execute(
                    new InspectObservabilityRequest(View: view),
                    cancellationToken
                );
                return Task.FromResult(result);
            }
            return Task.FromResult<object>(new { ok = true });
        }
    }
}
