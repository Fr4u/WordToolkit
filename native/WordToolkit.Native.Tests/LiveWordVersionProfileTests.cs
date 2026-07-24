using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class LiveWordVersionProfileTests
{
    [Fact]
    public void PublishesAClosedReadOnlyVersionedContract()
    {
        var tool = ToolCatalog
            .LoadNativeWordTools()
            .InspectAction("inspect_live_word_version_profile")["tool"]!
            .AsObject();

        Assert.Equal("1.0", tool["operationVersion"]!.GetValue<string>());
        Assert.Equal(
            "read_connected_word_environment_without_content",
            tool["permissions"]!["microsoft_word"]!.GetValue<string>()
        );
        Assert.False(tool["inputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.NotNull(tool["outputSchema"]);
        Assert.True(tool["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.True(tool["annotations"]!["idempotentHint"]!.GetValue<bool>());
        Assert.True(tool["annotations"]!["openWorldHint"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ReportsRawWordBuildCompatibilityAndBoundedRuntimeProbes()
    {
        await using var host = new DrawingLayoutFakeHost();
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new { live_document_id = documentId })
        );

        var result = await service.CallAsync(
            "inspect_live_word_version_profile",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonDefaults.Compact));
        var data = json.RootElement;

        Assert.Equal(
            "wordtoolkit.inspect_live_word_version_profile/1.0",
            data.GetProperty("operation_contract").GetString()
        );
        Assert.Equal("microsoft_word_com", data.GetProperty("backend").GetString());
        Assert.Equal("16.0", data.GetProperty("application").GetProperty("version").GetString());
        Assert.Equal(
            "16.0.19426.20186",
            data.GetProperty("application").GetProperty("build").GetString()
        );
        Assert.Equal(16, data.GetProperty("application").GetProperty("major_version").GetInt32());
        Assert.Equal(
            "word_16_generation",
            data.GetProperty("application").GetProperty("version_family").GetString()
        );
        Assert.False(
            data.GetProperty("application").GetProperty("product_edition_inferred").GetBoolean()
        );
        Assert.Equal(15, data.GetProperty("document").GetProperty("compatibility_mode").GetInt32());
        Assert.Equal(
            "word_2013",
            data.GetProperty("document").GetProperty("compatibility_profile").GetString()
        );
        Assert.Equal(16, data.GetProperty("document").GetProperty("save_format").GetInt32());
        Assert.All(
            data.GetProperty("probes").EnumerateObject(),
            probe => Assert.Equal("available", probe.Value.GetProperty("status").GetString())
        );
        Assert.Empty(data.GetProperty("issues").EnumerateArray());
        Assert.False(data.GetProperty("security").GetProperty("reads_document_content").GetBoolean());
        Assert.False(data.GetProperty("security").GetProperty("returns_paths").GetBoolean());
        Assert.Equal(0, host.Application.SensitiveReads.Count);
    }

    [Fact]
    public async Task ContainsProbeFailuresWithoutGuessingOrLeakingExceptionText()
    {
        await using var host = new DrawingLayoutFakeHost();
        host.Application.FailVersionProbe = true;
        host.Application.FailBuildProbe = true;
        host.Application.FailOMathProbe = true;
        host.Application.NullSmartArtProbe = true;
        host.Application.FailUndoRecordProbe = true;
        host.Application.ActiveDocument.FailCompatibilityModeProbe = true;
        host.Application.ActiveDocument.FailSaveFormatProbe = true;
        host.Application.ActiveDocument.FailContentControlsProbe = true;
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new { live_document_id = documentId })
        );

        var result = await service.CallAsync(
            "inspect_live_word_version_profile",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonDefaults.Compact));
        var data = json.RootElement;

        Assert.Equal(JsonValueKind.Null, data.GetProperty("application").GetProperty("version").ValueKind);
        Assert.Equal("unknown", data.GetProperty("application").GetProperty("version_family").GetString());
        Assert.Equal(
            "unknown",
            data.GetProperty("document").GetProperty("compatibility_profile").GetString()
        );
        Assert.Equal(
            "probe_failed",
            data.GetProperty("probes").GetProperty("undo_record").GetProperty("status").GetString()
        );
        Assert.Equal(
            "probe_failed",
            data.GetProperty("probes").GetProperty("native_omath").GetProperty("status").GetString()
        );
        Assert.Equal(
            "unavailable",
            data.GetProperty("probes").GetProperty("smartart").GetProperty("status").GetString()
        );
        var issues = data.GetProperty("issues").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Equal(7, issues.Length);
        Assert.Contains("APPLICATION_VERSION_PROBE_FAILED", issues);
        Assert.Contains("DOCUMENT_COMPATIBILITY_MODE_PROBE_FAILED", issues);
        Assert.Contains("CONTENT_CONTROLS_PROBE_FAILED", issues);
        Assert.DoesNotContain("InvalidOperationException", json.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(0, host.Application.SensitiveReads.Count);
    }

    [Fact]
    public async Task MapsOnlyExplicitVersionAndCompatibilityFamilies()
    {
        var cases = new[]
        {
            new VersionCase("11.0", 11, "word_2003", "word_2003", true),
            new VersionCase("12.0", 12, "word_2007", "word_2007", true),
            new VersionCase("14.0", 14, "word_2010", "word_2010", true),
            new VersionCase("15.0", 15, "word_2013", "word_2013", false),
            new VersionCase("16.0", 65535, "word_16_generation", "current", false),
            new VersionCase("future", 4242, "unknown", "unknown", null),
        };

        foreach (var item in cases)
        {
            await using var host = new DrawingLayoutFakeHost();
            host.Application.VersionValue = item.ApplicationVersion;
            host.Application.ActiveDocument.CompatibilityModeValue = item.CompatibilityMode;
            var service = new WordLiveService(host);
            var documentId = await ConnectAsync(service);
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { live_document_id = documentId })
            );

            var result = await service.CallAsync(
                "inspect_live_word_version_profile",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(
                JsonSerializer.Serialize(result, JsonDefaults.Compact)
            );
            var application = json.RootElement.GetProperty("application");
            var document = json.RootElement.GetProperty("document");

            Assert.Equal(item.VersionFamily, application.GetProperty("version_family").GetString());
            Assert.Equal(
                item.CompatibilityProfile,
                document.GetProperty("compatibility_profile").GetString()
            );
            if (item.LegacyRestrictions is null)
            {
                Assert.Equal(
                    JsonValueKind.Null,
                    document.GetProperty("legacy_feature_restrictions_documented").ValueKind
                );
            }
            else
            {
                Assert.Equal(
                    item.LegacyRestrictions,
                    document
                        .GetProperty("legacy_feature_restrictions_documented")
                        .GetBoolean()
                );
            }
        }
    }

    [Fact]
    public async Task RejectsUnknownArgumentsBeforeCallingWord()
    {
        await using var host = new DrawingLayoutFakeHost();
        var service = new WordLiveService(host);
        using var arguments = JsonDocument.Parse(
            """{"live_document_id":"unused","include_license_identity":true}"""
        );

        var exception = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "inspect_live_word_version_profile",
                    arguments.RootElement,
                    CancellationToken.None
                )
        );

        Assert.Equal("INVALID_INPUT", exception.ErrorCode);
        Assert.Equal(0, host.CallCount);
    }

    private static async Task<string> ConnectAsync(WordLiveService service)
    {
        using var arguments = JsonDocument.Parse("""{"use_active":true,"activate":true}""");
        var connected = await service.CallAsync(
            "connect_live_word_document",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(connected, JsonDefaults.Compact));
        return json.RootElement.GetProperty("live_document_id").GetString()!;
    }

    private sealed record VersionCase(
        string ApplicationVersion,
        int CompatibilityMode,
        string VersionFamily,
        string CompatibilityProfile,
        bool? LegacyRestrictions
    );
}
