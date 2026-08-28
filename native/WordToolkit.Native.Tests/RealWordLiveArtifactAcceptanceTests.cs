using System.Runtime.ExceptionServices;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

[Collection("RealWordAcceptance")]
public sealed class RealWordLiveArtifactAcceptanceTests
{
    [Fact]
    public async Task UnsavedConnectedDocumentExportsPdfAndManifestWithoutSaveOrReopen()
    {
        if (
            !string.Equals(
                Environment.GetEnvironmentVariable(
                    "WORDTOOLKIT_REAL_WORD_LIVE_ARTIFACT_TEST"
                ),
                "1",
                StringComparison.Ordinal
            )
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
                var wordType = Type.GetTypeFromProgID("Word.Application", throwOnError: true)
                    ?? throw new InvalidOperationException("Microsoft Word is unavailable.");
                ownedApplication = Activator.CreateInstance(wordType)
                    ?? throw new InvalidOperationException("Could not create Microsoft Word.");
            }
            return ownedApplication;
        }

        ownedApplication = CreateApplication(true);
        await using var host = new WordComHost(
            CreateApplication,
            shutdownTimeout: TimeSpan.FromSeconds(15)
        );
        var service = new WordLiveService(host);
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-real-live-render-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(outputDirectory);
        string? documentId = null;
        ExceptionDispatchInfo? primaryFailure = null;
        Exception? cleanupFailure = null;
        try
        {
            string documentName = await host.InvokeAsync(
                application =>
                {
                    dynamic document = application.Documents.Add(Visible: false);
                    document.Content.Text = "Unsaved live artifact acceptance\r";
                    return (string)document.Name;
                },
                launchIfMissing: false
            );
            using var connected = await Call(
                service,
                "connect_live_word_document",
                new { document_name = documentName, activate = false }
            );
            documentId = connected.RootElement.GetProperty("live_document_id").GetString();
            var version = connected.RootElement.GetProperty("live_version").GetInt64();

            using var exported = await Call(
                service,
                "export_live_word_artifacts",
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    output_directory = outputDirectory,
                    artifact_stem = "live-proof",
                    output = "pdf",
                }
            );

            Assert.Equal(version, exported.RootElement.GetProperty("live_version").GetInt64());
            Assert.False(exported.RootElement.GetProperty("source_saved").GetBoolean());
            Assert.True(
                exported.RootElement
                    .GetProperty("source_included_unsaved_changes")
                    .GetBoolean()
            );
            Assert.False(exported.RootElement.GetProperty("source_mutated").GetBoolean());
            Assert.False(exported.RootElement.GetProperty("document_reopened").GetBoolean());
            Assert.False(exported.RootElement.GetProperty("document_saved").GetBoolean());
            Assert.True(File.Exists(Path.Combine(outputDirectory, "live-proof.pdf")));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "live-proof.render.json")));
            Assert.Equal(
                1,
                await host.InvokeAsync(
                    application => (int)application.Documents.Count,
                    launchIfMissing: false
                )
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
            try
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
            catch (Exception exception)
            {
                cleanupFailure ??= exception;
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
