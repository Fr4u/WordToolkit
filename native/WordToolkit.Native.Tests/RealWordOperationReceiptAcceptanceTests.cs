using System.Runtime.ExceptionServices;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

[Collection("RealWordAcceptance")]
public sealed class RealWordOperationReceiptAcceptanceTests
{
    [Fact]
    public async Task ClientCancellationKeepsOneTransactionAndExactRetryReplaysReceipt()
    {
        if (
            Environment.GetEnvironmentVariable("WORDTOOLKIT_REAL_WORD_RECEIPT_TEST")
            != "1"
        )
        {
            return;
        }
        object? ownedApplication = null;
        object CreateApplication(bool launchIfMissing)
        {
            if (ownedApplication is null)
            {
                if (!launchIfMissing)
                {
                    throw new InvalidOperationException("Dedicated Word was not created.");
                }
                var type = Type.GetTypeFromProgID("Word.Application", throwOnError: true)
                    ?? throw new InvalidOperationException("Microsoft Word is unavailable.");
                ownedApplication = Activator.CreateInstance(type)
                    ?? throw new InvalidOperationException("Could not create Word.");
            }
            return ownedApplication;
        }

        ownedApplication = CreateApplication(true);
        await using var host = new WordComHost(
            CreateApplication,
            shutdownTimeout: TimeSpan.FromSeconds(15)
        );
        var service = new WordLiveService(host);
        ExceptionDispatchInfo? primaryFailure = null;
        Exception? cleanupFailure = null;
        try
        {
            using var created = await Call(
                service,
                "create_live_word_document",
                new { lifecycle = "scratch", activate = true }
            );
            var documentId = created.RootElement.GetProperty("live_document_id").GetString()!;
            var operations = Enumerable.Range(1, 100)
                .Select(index => new
                {
                    type = "text",
                    text = $"Receipt paragraph {index}",
                    as_new_paragraph = true,
                })
                .ToArray();
            var request = new
            {
                live_document_id = documentId,
                expected_version = 0,
                idempotency_key = "cancelled-batch",
                operations,
            };
            using var cancellation = new CancellationTokenSource();
            var pendingCall = service.CallAsync(
                "apply_live_word_operations",
                JsonSerializer.SerializeToElement(request),
                cancellation.Token
            );
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingCall);

            JsonDocument? status = null;
            for (var attempt = 0; attempt < 120; attempt++)
            {
                status?.Dispose();
                status = await Call(
                    service,
                    "get_live_word_operation_status",
                    new { operation_id = "wlop_cancelled-batch" }
                );
                if (
                    status.RootElement.GetProperty("operation_status").GetString()
                    is "succeeded" or "failed"
                )
                {
                    break;
                }
                await Task.Delay(250);
            }
            using (status)
            {
                Assert.NotNull(status);
                Assert.Equal(
                    "succeeded",
                    status!.RootElement.GetProperty("operation_status").GetString()
                );
                Assert.Equal(
                    100,
                    status.RootElement.GetProperty("result")
                        .GetProperty("live_version")
                        .GetInt64()
                );
            }

            using var replay = await Call(
                service,
                "apply_live_word_operations",
                request
            );
            Assert.Equal(
                "wlop_cancelled-batch",
                replay.RootElement.GetProperty("operation_id").GetString()
            );
            Assert.True(replay.RootElement.GetProperty("receipt_replayed").GetBoolean());
            Assert.Equal(100, replay.RootElement.GetProperty("live_version").GetInt64());
            Assert.Equal(
                101,
                await host.InvokeAsync(
                    application => (int)application.ActiveDocument.Paragraphs.Count,
                    launchIfMissing: false
                )
            );

            using var disconnected = await Call(
                service,
                "disconnect_live_word_document",
                new { live_document_id = documentId }
            );
            Assert.True(
                disconnected.RootElement.GetProperty("scratch_document_closed").GetBoolean()
            );
        }
        catch (Exception exception)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            try
            {
                await host.InvokeAsync(
                    application =>
                    {
                        while ((int)application.Documents.Count > 0)
                        {
                            application.Documents.Item(1).Close(0);
                        }
                        application.Quit(0);
                        return true;
                    },
                    launchIfMissing: false
                );
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }
        }
        primaryFailure?.Throw();
        if (cleanupFailure is not null)
        {
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }
    }

    private static async Task<JsonDocument> Call(
        WordLiveService service,
        string action,
        object arguments
    ) => JsonDocument.Parse(
        JsonSerializer.Serialize(
            await service.CallAsync(
                action,
                JsonSerializer.SerializeToElement(arguments),
                CancellationToken.None
            ),
            JsonDefaults.Compact
        )
    );
}
