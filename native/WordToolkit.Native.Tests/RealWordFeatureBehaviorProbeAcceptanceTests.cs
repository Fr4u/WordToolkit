using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class RealWordFeatureBehaviorProbeAcceptanceTests
{
    [Fact]
    public async Task ScratchBehaviorProbesLeaveTheRealConnectedDocumentUntouched()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("WORDTOOLKIT_REAL_WORD_FEATURE_BEHAVIOR_TEST"),
            "1",
            StringComparison.Ordinal
        ))
        {
            return;
        }

        await using var host = new WordComHost();
        var service = new WordLiveService(host);
        object? targetObject = null;
        LiveRollbackSnapshot? baselineSnapshot = null;
        var baselineDocumentCount = 0;
        var baselineActiveWindowHwnd = 0;
        await host.InvokeAsync(
            application =>
            {
                dynamic target = application.Documents.Add(Visible: false);
                targetObject = (object)target;
                target.Content.Text = "Connected target must remain untouched.\r";
                target.Saved = true;
                target.Activate();
                baselineDocumentCount = (int)application.Documents.Count;
                baselineActiveWindowHwnd = (int)application.ActiveWindow.Hwnd;
                baselineSnapshot = WordLiveService.CaptureLiveRollbackSnapshot(target, 0);
                return true;
            },
            launchIfMissing: true
        );

        try
        {
            using var connectArguments = JsonDocument.Parse(
                """{"use_active":true,"activate":true}"""
            );
            var connected = await service.CallAsync(
                "connect_live_word_document",
                connectArguments.RootElement,
                CancellationToken.None
            );
            using var connectedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(connected, JsonDefaults.Compact)
            );
            var documentId = connectedJson.RootElement
                .GetProperty("live_document_id")
                .GetString()!;
            using var probeArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    new
                    {
                        live_document_id = documentId,
                        confirm_scratch_documents = true,
                    }
                )
            );

            var result = await service.CallAsync(
                "probe_live_word_feature_behaviors",
                probeArguments.RootElement,
                CancellationToken.None
            );
            using var resultJson = JsonDocument.Parse(
                JsonSerializer.Serialize(result, JsonDefaults.Compact)
            );
            var data = resultJson.RootElement;

            Assert.Equal(0, data.GetProperty("live_version").GetInt64());
            Assert.Equal(0, data.GetProperty("summary").GetProperty("failed").GetInt32());
            Assert.Equal(
                "passed",
                data.GetProperty("probes").GetProperty("native_omath")
                    .GetProperty("status").GetString()
            );
            Assert.Equal(
                "passed",
                data.GetProperty("probes").GetProperty("content_controls")
                    .GetProperty("status").GetString()
            );
            Assert.Equal(
                "passed",
                data.GetProperty("probes").GetProperty("undo_record")
                    .GetProperty("status").GetString()
            );
            Assert.Contains(
                data.GetProperty("probes").GetProperty("smartart")
                    .GetProperty("status").GetString(),
                new[] { "passed", "unavailable" }
            );

            await host.InvokeAsync(
                application =>
                {
                    dynamic target = targetObject!;
                    Assert.Equal(
                        baselineDocumentCount,
                        (int)application.Documents.Count
                    );
                    Assert.Equal(
                        baselineActiveWindowHwnd,
                        (int)application.ActiveWindow.Hwnd
                    );
                    LiveRollbackSnapshot observed =
                        WordLiveService.CaptureLiveRollbackSnapshot(target, 0);
                    var differences = baselineSnapshot!
                        .RecoveryDifferences(observed)
                        .ToArray();
                    Assert.All(
                        differences,
                        difference =>
                            Assert.Equal(
                                "document_semantic_word_open_xml_sha256",
                                difference
                            )
                    );
                    Assert.True(differences.Length <= 1);
                    return true;
                }
            );
        }
        finally
        {
            if (targetObject is not null)
            {
                await host.InvokeAsync(
                    _ =>
                    {
                        ((dynamic)targetObject).Close(0);
                        return true;
                    }
                );
            }
        }
    }

}
