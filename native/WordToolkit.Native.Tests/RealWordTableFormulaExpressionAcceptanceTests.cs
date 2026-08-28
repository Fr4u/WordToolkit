using System.Runtime.ExceptionServices;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

[Collection("RealWordAcceptance")]
public sealed class RealWordTableFormulaExpressionAcceptanceTests
{
    [Fact]
    public async Task NativeExpressionsCalculateAndRejectReferencesOutsideTheLiveTable()
    {
        if (
            Environment.GetEnvironmentVariable("WORDTOOLKIT_REAL_WORD_FORMULA_EXPRESSION_TEST")
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
            var version = created.RootElement.GetProperty("live_version").GetInt64();

            using (var table = await Call(
                service,
                "insert_live_word_table",
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    rows = new[]
                    {
                        new[] { "A", "B", "C", "D" },
                        new[] { "0.25", "0.75", "", "" },
                        new[] { "1", "15", "", "" },
                        new[] { "130000", "", "", "" },
                        new[] { "1000", "200", "0.10", "" },
                    },
                    header_row = true,
                    activate = true,
                }
            ))
            {
                version = table.RootElement.GetProperty("live_version").GetInt64();
            }

            using var inserted = await Call(
                service,
                "insert_live_word_table_formulas",
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    table_index = 1,
                    formulas = new object[]
                    {
                        new { row = 2, column = 3, function = "expression", expression = "(B2-A2)*24", numeric_format = "0.00" },
                        new { row = 3, column = 3, function = "expression", expression = "INT(B3/7)-INT((A3-1)/7)" },
                        new { row = 4, column = 4, function = "expression", expression = "IF(A4>120000,(A4-120000)*0.32+A4*0.12,A4*0.12)", numeric_format = "0.00" },
                        new { row = 5, column = 4, function = "expression", expression = "MAX(0,A5-B5)*C5", numeric_format = "0.00" },
                    },
                }
            );
            version = inserted.RootElement.GetProperty("live_version").GetInt64();
            Assert.Equal(4, inserted.RootElement.GetProperty("formula_count").GetInt32());
            Assert.All(
                inserted.RootElement.GetProperty("formulas").EnumerateArray(),
                formula =>
                {
                    Assert.Equal("expression", formula.GetProperty("source").GetString());
                    Assert.Equal(34, formula.GetProperty("field_type").GetInt32());
                }
            );
            Assert.Equal(
                4,
                await host.InvokeAsync(
                    application => (int)application.ActiveDocument.Fields.Count,
                    launchIfMissing: false
                )
            );

            var outside = await Assert.ThrowsAsync<NativeToolException>(
                async () =>
                {
                    using var ignored = await Call(
                        service,
                        "insert_live_word_table_formulas",
                        new
                        {
                            live_document_id = documentId,
                            expected_version = version,
                            table_index = 1,
                            formulas = new[]
                            {
                                new { row = 3, column = 4, function = "expression", expression = "D6+1" },
                            },
                        }
                    );
                }
            );
            Assert.Equal("INVALID_INPUT", outside.ErrorCode);
            Assert.Equal(
                4,
                await host.InvokeAsync(
                    application => (int)application.ActiveDocument.Fields.Count,
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
