using System.Runtime.ExceptionServices;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

[Collection("RealWordAcceptance")]
public sealed class RealWordDirectOmmlAcceptanceTests
{
    [Fact]
    public async Task DirectOmmlSurvivesStagingPublicationAndReadback()
    {
        if (
            Environment.GetEnvironmentVariable(
                "WORDTOOLKIT_REAL_WORD_DIRECT_OMML_TEST"
            ) != "1"
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
                var type = Type.GetTypeFromProgID("Word.Application", throwOnError: true)!;
                ownedApplication = Activator.CreateInstance(type)!;
            }
            return ownedApplication;
        }

        await using var host = new WordComHost(
            CreateApplication,
            shutdownTimeout: TimeSpan.FromSeconds(15)
        );
        var service = new WordLiveService(host);
        ExceptionDispatchInfo? primaryFailure = null;
        Exception? cleanupFailure = null;
        try
        {
            using var started = await Call(
                service,
                "start_word_application",
                new { visible = false }
            );
            using var created = await Call(
                service,
                "create_live_word_document",
                new { lifecycle = "scratch", visible = false, activate = false }
            );
            var id = created.RootElement.GetProperty("live_document_id").GetString()!;
            const string omml =
                "<m:oMathPara xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\">"
                + "<m:oMathParaPr><m:jc m:val=\"center\"/></m:oMathParaPr>"
                + "<m:oMath><m:f><m:num><m:r><m:t>a</m:t></m:r></m:num>"
                + "<m:den><m:r><m:t>b</m:t></m:r></m:den></m:f></m:oMath>"
                + "</m:oMathPara>";
            using var applied = await Call(
                service,
                "apply_live_word_operations",
                new
                {
                    live_document_id = id,
                    expected_version = 0,
                    idempotency_key = "direct-omml-real-word",
                    operations = new[]
                    {
                        new
                        {
                            type = "equation",
                            value = omml,
                            input_format = "omml",
                            verify_readback = true,
                            display = true,
                        },
                    },
                }
            );
            var root = applied.RootElement;
            Assert.Equal("succeeded", root.GetProperty("operation_status").GetString());
            Assert.Equal(1, root.GetProperty("live_version").GetInt32());
            var direct = root.GetProperty("operations")[0]
                .GetProperty("equation")
                .GetProperty("direct_omml");
            Assert.True(direct.GetProperty("source_validated").GetBoolean());
            Assert.True(direct.GetProperty("native_semantic_verified").GetBoolean());
            Assert.Equal(64, direct.GetProperty("expected_semantic_sha256").GetString()!.Length);
            Assert.False(direct.TryGetProperty("actual_semantic_sha256", out _));
            Assert.Equal(
                direct.GetProperty("expected_equation_semantic_sha256").GetString(),
                direct.GetProperty("actual_equation_semantic_sha256").GetString()
            );
            Assert.Equal(
                direct.GetProperty("expected_paragraph_properties_sha256").GetString(),
                direct.GetProperty("actual_paragraph_properties_sha256").GetString()
            );
            Assert.Equal("center", direct.GetProperty("actual_paragraph_justification").GetString());
            using var disconnected = await Call(
                service,
                "disconnect_live_word_document",
                new { live_document_id = id }
            );
            Assert.True(
                disconnected.RootElement.GetProperty("scratch_document_closed").GetBoolean()
            );
        }
        catch (NativeToolException exception)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(
                new InvalidOperationException(
                    $"{exception.ErrorCode}: {exception.Message}; details="
                    + JsonSerializer.Serialize(exception.Details, JsonDefaults.Compact),
                    exception
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
                if (ownedApplication is not null)
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
    ) =>
        JsonDocument.Parse(
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
