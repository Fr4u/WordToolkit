using System.Runtime.ExceptionServices;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

[Collection("RealWordAcceptance")]
public sealed class RealWordDropdownAcceptanceTests
{
    [Fact]
    public async Task ScratchDocumentCreatesVerifiedDropdownsAndClosesOnDisconnect()
    {
        if (
            Environment.GetEnvironmentVariable("WORDTOOLKIT_REAL_WORD_DROPDOWN_TEST")
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
        string? documentId = null;
        ExceptionDispatchInfo? primaryFailure = null;
        Exception? cleanupFailure = null;
        try
        {
            using var created = await Call(
                service,
                "create_live_word_document",
                new { lifecycle = "scratch", activate = true }
            );
            documentId = created.RootElement.GetProperty("live_document_id").GetString();
            var version = created.RootElement.GetProperty("live_version").GetInt64();
            Assert.True(created.RootElement.GetProperty("auto_close_on_disconnect").GetBoolean());

            using (var applied = await Call(
                service,
                "apply_live_word_operations",
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    operations = new[]
                    {
                        new { type = "text", text = "Month Year", as_new_paragraph = true },
                    },
                }
            ))
            {
                version = applied.RootElement.GetProperty("live_version").GetInt64();
            }
            var monthToken = await FindToken(service, documentId!, "Month");
            var yearToken = await FindToken(service, documentId!, "Year");
            using var dropdowns = await Call(
                service,
                "insert_live_word_dropdowns",
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    controls = new object[]
                    {
                        new
                        {
                            range_token = monthToken,
                            title = "Month",
                            tag = "tax_month",
                            items = new[] { "January", "February", "March" },
                            selected_item = "February",
                        },
                        new
                        {
                            range_token = yearToken,
                            title = "Year",
                            tag = "tax_year",
                            items = new[] { "2025", "2026", "2027" },
                            selected_item = "2026",
                        },
                    },
                }
            );
            Assert.Equal(2, dropdowns.RootElement.GetProperty("created_count").GetInt32());
            Assert.True(dropdowns.RootElement.GetProperty("native_verified").GetBoolean());
            Assert.Equal(
                2,
                await host.InvokeAsync(
                    application => (int)application.ActiveDocument.ContentControls.Count,
                    launchIfMissing: false
                )
            );

            using var disconnected = await Call(
                service,
                "disconnect_live_word_document",
                new { live_document_id = documentId }
            );
            documentId = null;
            Assert.True(
                disconnected.RootElement.GetProperty("scratch_document_closed").GetBoolean()
            );
            Assert.Equal(
                0,
                await host.InvokeAsync(
                    application => (int)application.Documents.Count,
                    launchIfMissing: false
                )
            );
        }
        catch (Exception exception)
        {
            if (exception is NativeToolException native)
            {
                Console.Error.WriteLine(
                    JsonSerializer.Serialize(native.Details, JsonDefaults.Compact)
                );
            }
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

    private static async Task<string> FindToken(
        WordLiveService service,
        string documentId,
        string text
    )
    {
        using var found = await Call(
            service,
            "find_live_word_text",
            new
            {
                live_document_id = documentId,
                search_text = text,
                match_case = true,
                whole_word = true,
                max_results = 1,
            }
        );
        return found.RootElement.GetProperty("matches")[0]
            .GetProperty("range_token")
            .GetString()!;
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
