using System.Runtime.ExceptionServices;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

[Collection("RealWordAcceptance")]
public sealed class RealWordTargetBoundMultiPreflightAcceptanceTests
{
    [Fact]
    public async Task TargetBoundPreflightReturnsAllEquationFailuresAndLeavesTargetEmpty()
    {
        if (
            Environment.GetEnvironmentVariable(
                "WORDTOOLKIT_REAL_WORD_ISOLATED_PREFLIGHT_TEST"
            ) != "1"
        ) return;

        object? ownedApplication = null;
        object CreateApplication(bool launch)
        {
            if (ownedApplication is null)
            {
                if (!launch) throw new InvalidOperationException("Word not created");
                var type = Type.GetTypeFromProgID("Word.Application", true)!;
                ownedApplication = Activator.CreateInstance(type)!;
            }
            return ownedApplication;
        }
        ownedApplication = CreateApplication(true);
        await using var host = new WordComHost(CreateApplication, shutdownTimeout: TimeSpan.FromSeconds(15));
        var service = new WordLiveService(host);
        ExceptionDispatchInfo? primary = null;
        Exception? cleanup = null;
        try
        {
            using var created = await Call(service, "create_live_word_document", new
            {
                lifecycle = "scratch",
                activate = true,
            });
            var id = created.RootElement.GetProperty("live_document_id").GetString()!;
            using var result = await Call(service, "preflight_live_word_operations", new
            {
                live_document_id = id,
                expected_version = 0,
                operations = new[]
                {
                    new { type = "equation", value = "x+1", input_format = "latex" },
                    new { type = "equation", value = "\\badcommand{x}", input_format = "latex" },
                    new { type = "equation", value = "y+1", input_format = "latex" },
                    new { type = "equation", value = "\\anotherbad{y}", input_format = "latex" },
                },
            });
            var root = result.RootElement;
            Assert.False(root.GetProperty("valid").GetBoolean());
            Assert.Equal(2, root.GetProperty("invalid_equation_count").GetInt32());
            Assert.Equal(
                new[] { 1, 3 },
                root.GetProperty("equation_failures").EnumerateArray()
                    .Select(item => item.GetProperty("operation_index").GetInt32())
                    .ToArray()
            );
            Assert.All(
                root.GetProperty("equation_failures").EnumerateArray(),
                item => Assert.StartsWith("weq_", item.GetProperty("equation_id").GetString())
            );
            Assert.Equal(0, await host.InvokeAsync(
                app => (int)app.ActiveDocument.OMaths.Count,
                launchIfMissing: false
            ));
            Assert.Equal(1, await host.InvokeAsync(
                app => (int)app.ActiveDocument.Paragraphs.Count,
                launchIfMissing: false
            ));
            using var disconnected = await Call(service, "disconnect_live_word_document", new
            {
                live_document_id = id,
            });
            Assert.True(disconnected.RootElement.GetProperty("scratch_document_closed").GetBoolean());
        }
        catch (Exception exception) { primary = ExceptionDispatchInfo.Capture(exception); }
        finally
        {
            try
            {
                if (ownedApplication is not null)
                {
                    await host.InvokeAsync(app =>
                    {
                        while ((int)app.Documents.Count > 0) app.Documents.Item(1).Close(0);
                        app.Quit(0);
                        return true;
                    }, launchIfMissing: false);
                }
            }
            catch (Exception exception) { cleanup = exception; }
        }
        primary?.Throw();
        if (cleanup is not null) ExceptionDispatchInfo.Capture(cleanup).Throw();
    }

    private static async Task<JsonDocument> Call(
        WordLiveService service,
        string action,
        object arguments
    ) => JsonDocument.Parse(JsonSerializer.Serialize(
        await service.CallAsync(
            action,
            JsonSerializer.SerializeToElement(arguments),
            CancellationToken.None
        ),
        JsonDefaults.Compact
    ));
}
