using System.Runtime.ExceptionServices;
using System.Text.Json;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

[Collection("RealWordAcceptance")]
public sealed class RealWordInlineRunAcceptanceTests
{
    [Fact]
    public async Task DedicatedWordBatchPublishesOneParagraphWithDistinctInlineFormatting()
    {
        if (
            !string.Equals(
                Environment.GetEnvironmentVariable("WORDTOOLKIT_REAL_WORD_INLINE_RUN_TEST"),
                "1",
                StringComparison.Ordinal
            )
        )
        {
            return;
        }

        foreach (
            var stalePath in Directory.EnumerateFiles(
                Path.GetTempPath(),
                "wordtoolkit-real-open-inspection-*.docx",
                SearchOption.TopDirectoryOnly
            )
        )
        {
            File.Delete(stalePath);
        }

        object? ownedApplication = null;
        object CreateApplication(bool launchIfMissing)
        {
            if (ownedApplication is null)
            {
                if (!launchIfMissing)
                {
                    throw new InvalidOperationException("Dedicated Word application was not created.");
                }
                var wordType = Type.GetTypeFromProgID("Word.Application", throwOnError: true)
                    ?? throw new InvalidOperationException("Microsoft Word ProgID is unavailable.");
                ownedApplication = Activator.CreateInstance(wordType)
                    ?? throw new InvalidOperationException(
                        "Could not create dedicated Word application."
                    );
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
        string? documentName = null;
        string? savedPath = null;
        ExceptionDispatchInfo? primary = null;
        Exception? cleanupFailure = null;
        try
        {
            documentName = await host.InvokeAsync(
                application => (string)application.Documents.Add(Visible: false).Name,
                launchIfMissing: false
            );
            using var connectArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { document_name = documentName, activate = false })
            );
            using var connected = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    await service.CallAsync(
                        "connect_live_word_document",
                        connectArguments.RootElement,
                        CancellationToken.None
                    ),
                    JsonDefaults.Compact
                )
            );
            documentId = connected.RootElement.GetProperty("live_document_id").GetString();
            var version = connected.RootElement.GetProperty("live_version").GetInt64();
            using var applyArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    new
                    {
                        live_document_id = documentId,
                        expected_version = version,
                        operations = new object[]
                        {
                            new
                            {
                                type = "text",
                                runs = new object[]
                                {
                                    new { text = "Normal " },
                                    new { text = "bold", formatting = new { bold = true } },
                                    new
                                    {
                                        text = " italic",
                                        formatting = new { italic = true },
                                    },
                                    new
                                    {
                                        text = " double",
                                        formatting = new
                                        {
                                            double_strike = true,
                                            highlight_color_index = 7,
                                        },
                                    },
                                },
                                as_new_paragraph = true,
                                formatting = new
                                {
                                    font_name = "Times New Roman",
                                    font_size = 12,
                                    alignment = "distribute",
                                },
                            },
                            new
                            {
                                type = "equation",
                                value = "\\boxed{x+1}",
                                input_format = "latex",
                                display = true,
                                verify_readback = true,
                            },
                        },
                    }
                )
            );
            using var applied = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    await service.CallAsync(
                        "apply_live_word_operations",
                        applyArguments.RootElement,
                        CancellationToken.None
                    ),
                    JsonDefaults.Compact
                )
            );
            var operation = applied.RootElement.GetProperty("operations")[0];
            Assert.Equal(4, operation.GetProperty("run_count").GetInt32());
            Assert.True(
                applied.RootElement
                    .GetProperty("operations")[1]
                    .GetProperty("equation")
                    .GetProperty("native_verified")
                    .GetBoolean()
            );
            var start = operation.GetProperty("range").GetProperty("start").GetInt32();
            await host.InvokeAsync(
                application =>
                {
                    dynamic document = application.Documents.Item(documentName);
                    Assert.Equal(
                        "Normal bold italic double",
                        (string)document.Range(start, start + 25).Text
                    );
                    Assert.Equal(
                        "Times New Roman",
                        (string)document.Range(start, start + 25).Font.Name
                    );
                    Assert.Equal(12f, (float)document.Range(start, start + 25).Font.Size);
                    Assert.Equal(4, (int)document.Range(start, start + 25).ParagraphFormat.Alignment);
                    Assert.Equal(-1, (int)document.Range(start + 7, start + 11).Font.Bold);
                    Assert.Equal(-1, (int)document.Range(start + 11, start + 18).Font.Italic);
                    Assert.Equal(
                        -1,
                        (int)document.Range(start + 18, start + 25).Font.DoubleStrikeThrough
                    );
                    Assert.Equal(
                        7,
                        (int)document.Range(start + 18, start + 25).HighlightColorIndex
                    );
                    dynamic strikeRange = document.Range(start + 18, start + 25);
                    strikeRange.Font.StrikeThrough = -1;
                    Assert.Equal(-1, (int)strikeRange.Font.StrikeThrough);
                    Assert.Equal(0, (int)strikeRange.Font.DoubleStrikeThrough);
                    strikeRange.Font.DoubleStrikeThrough = -1;
                    Assert.Equal(0, (int)strikeRange.Font.StrikeThrough);
                    Assert.Equal(-1, (int)strikeRange.Font.DoubleStrikeThrough);
                    Assert.Equal(1, (int)document.OMaths.Count);
                    savedPath = Path.Combine(
                        Path.GetTempPath(),
                        $"wordtoolkit-real-open-inspection-{Guid.NewGuid():N}.docx"
                    );
                    document.SaveAs2(FileName: savedPath, FileFormat: 16, AddToRecentFiles: false);
                    documentName = (string)document.Name;
                    return true;
                },
                launchIfMissing: false
            );
            var inspection = new InspectWordPackageOperation().Execute(
                new InspectWordPackageRequest(savedPath!, IncludeDetails: true, MaxItems: 20)
            );
            Assert.True(inspection.ValidWordPackage);
            Assert.DoesNotContain(
                inspection.Diagnostics.Items,
                diagnostic => diagnostic.Code == "IO_ERROR"
            );
        }
        catch (Exception exception)
        {
            primary = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            try
            {
                if (documentName is not null)
                {
                    await host.InvokeAsync(
                        application =>
                        {
                            foreach (dynamic document in application.Documents)
                            {
                                if ((string)document.Name == documentName)
                                {
                                    document.Close(0);
                                    break;
                                }
                            }
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
            try
            {
                await host.InvokeAsync(
                    application =>
                    {
                        application.Quit(0);
                        return true;
                    },
                    launchIfMissing: false
                );
            }
            catch (Exception exception)
            {
                cleanupFailure ??= exception;
            }
            try
            {
                if (savedPath is not null && File.Exists(savedPath))
                {
                    File.Delete(savedPath);
                }
            }
            catch (Exception exception)
            {
                cleanupFailure ??= exception;
            }
            if (primary is not null)
            {
                primary.Throw();
            }
            if (cleanupFailure is not null)
            {
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            }
        }
    }
}
