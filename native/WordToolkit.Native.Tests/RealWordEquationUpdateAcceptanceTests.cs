using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Security.Cryptography;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class RealWordEquationUpdateAcceptanceTests
{
    [Fact]
    public async Task VerifiedOfflinePackageOpensAsOneNewLiveWordIdentity()
    {
        if (
            !string.Equals(
                Environment.GetEnvironmentVariable(
                    "WORDTOOLKIT_REAL_WORD_EQUATION_UPDATE_TEST"
                ),
                "1",
                StringComparison.Ordinal
            )
        )
        {
            return;
        }

        var path = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-hybrid-real-{Guid.NewGuid():N}.docx"
        );
        using (var package = WordprocessingDocument.Create(
            path,
            WordprocessingDocumentType.Document
        ))
        {
            var main = package.AddMainDocumentPart();
            main.Document = new Document(
                new Body(
                    new Paragraph(
                        new Run(new Text("Verified offline package"))
                    )
                )
            );
            main.Document.Save();
        }
        var sourceHash = HashFileShared(path);
        await using var host = new WordComHost();
        var service = new WordLiveService(host);
        string? documentId = null;
        try
        {
            var fingerprint = new InspectWordPackageOperation()
                .Execute(new InspectWordPackageRequest(path))
                .PackageFingerprint;
            using var published = await Call(
                service,
                "publish_ooxml_package_to_live_word",
                new
                {
                    local_path = path,
                    expected_package_fingerprint = fingerprint,
                    publication_mode = "open_as_new_document",
                    visible = false,
                    activate = true,
                }
            );
            documentId = published.RootElement
                .GetProperty("live_document_id")
                .GetString();
            Assert.True(
                published.RootElement.GetProperty("opened_as_new_document").GetBoolean()
            );
            Assert.False(
                published.RootElement.GetProperty("connected_document_replaced").GetBoolean()
            );
            Assert.Equal(
                fingerprint,
                published.RootElement.GetProperty("package_fingerprint").GetString()
            );
            Assert.Equal(
                1,
                published.RootElement.GetProperty("document")
                    .GetProperty("paragraph_count")
                    .GetInt32()
            );
            Assert.Equal(
                sourceHash,
                HashFileShared(path)
            );
        }
        finally
        {
            if (documentId is not null)
            {
                using var closed = await Call(
                    service,
                    "close_live_word_document",
                    new
                    {
                        live_document_id = documentId,
                        expected_version = 0,
                        save_changes = "discard",
                    }
                );
            }
            File.Delete(path);
        }
    }

    [Fact]
    public async Task NativePreflightPointUpdateAndSemanticDriftGateWorkTogether()
    {
        if (
            !string.Equals(
                Environment.GetEnvironmentVariable(
                    "WORDTOOLKIT_REAL_WORD_EQUATION_UPDATE_TEST"
                ),
                "1",
                StringComparison.Ordinal
            )
        )
        {
            return;
        }

        await using var host = new WordComHost();
        var service = new WordLiveService(host);
        string? documentId = null;
        long version = 0;
        try
        {
            using var started = await Call(
                service,
                "start_word_application",
                new { visible = false }
            );
            JsonDocument preflight;
            try
            {
                preflight = await Call(
                    service,
                    "preflight_live_word_equations",
                    new
                    {
                        validation_mode = "native",
                        equations = new[]
                        {
                            new
                            {
                                value = @"\binom{n}{k}",
                                input_format = "latex",
                                display = true,
                            },
                            new
                            {
                                value = @"x_1,\dots,x_n",
                                input_format = "latex",
                                display = true,
                            },
                            new
                            {
                                value = @"\sin^4 x+\cos^2(2x)",
                                input_format = "latex",
                                display = true,
                            },
                            new
                            {
                                value = @"E=mc^2,\text{ energia spoczynkowa}",
                                input_format = "latex",
                                display = true,
                            },
                        },
                    }
                );
            }
            catch (NativeToolException exception)
            {
                throw new InvalidOperationException(
                    $"Native preflight failed: {exception.ErrorCode} {JsonSerializer.Serialize(exception.Details, JsonDefaults.Compact)}",
                    exception
                );
            }
            using (preflight)
            {
                Assert.True(preflight.RootElement.GetProperty("valid").GetBoolean());
                Assert.True(
                    preflight.RootElement
                        .GetProperty("native_execution_verified")
                        .GetBoolean()
                );
                Assert.Equal(
                    4,
                    preflight.RootElement.GetProperty("equation_count").GetInt32()
                );
                Assert.All(
                    preflight.RootElement.GetProperty("equations").EnumerateArray(),
                    equation =>
                    {
                        Assert.True(equation.GetProperty("valid").GetBoolean());
                        Assert.True(
                            equation.GetProperty("native_execution_verified").GetBoolean()
                        );
                        Assert.True(
                            equation.GetProperty("native_readback_verified").GetBoolean()
                        );
                    }
                );
            }
            using (var created = await Call(
                service,
                "create_live_word_document",
                new { activate = true }
            ))
            {
                documentId = created.RootElement
                    .GetProperty("live_document_id")
                    .GetString();
                version = created.RootElement.GetProperty("live_version").GetInt64();
            }
            using (var applied = await Call(
                service,
                "apply_live_word_operations",
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    operations = new object[]
                    {
                        new
                        {
                            type = "equation",
                            value = "x+1",
                            input_format = "latex",
                            display = true,
                        },
                    },
                }
            ))
            {
                version = applied.RootElement.GetProperty("live_version").GetInt64();
                Assert.Equal(1, version);
                Assert.Equal(
                    1,
                    applied.RootElement.GetProperty("document")
                        .GetProperty("equation_count")
                        .GetInt32()
                );
            }

            string token;
            using (var inspected = await Call(
                service,
                "inspect_live_word_equations",
                new { live_document_id = documentId }
            ))
            {
                Assert.Equal(1, inspected.RootElement.GetProperty("equation_count").GetInt32());
                var equation = inspected.RootElement.GetProperty("equations")[0];
                Assert.Equal(1, equation.GetProperty("equation_index").GetInt32());
                token = equation.GetProperty("equation_token").GetString()!;
            }

            using (var updated = await Call(
                service,
                "update_live_word_equation",
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    equation_index = 1,
                    equation_token = token,
                    value = @"\frac{a}{b}",
                    input_format = "latex",
                    display = true,
                    verify_readback = true,
                }
            ))
            {
                version = updated.RootElement.GetProperty("live_version").GetInt64();
                Assert.Equal(2, version);
                Assert.True(updated.RootElement.GetProperty("updated").GetBoolean());
                Assert.True(updated.RootElement.GetProperty("native_verified").GetBoolean());
                Assert.True(
                    updated.RootElement.GetProperty("readback_verified").GetBoolean()
                );
                Assert.Equal(
                    1,
                    updated.RootElement.GetProperty("document")
                        .GetProperty("equation_count")
                        .GetInt32()
                );
            }

            using var staleArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    new
                    {
                        live_document_id = documentId,
                        expected_version = version,
                        equation_index = 1,
                        equation_token = token,
                        value = "z",
                    },
                    JsonDefaults.Compact
                )
            );
            var stale = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "update_live_word_equation",
                    staleArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("VERSION_CONFLICT", stale.ErrorCode);
        }
        finally
        {
            if (documentId is not null)
            {
                try
                {
                    using var closed = await Call(
                        service,
                        "close_live_word_document",
                        new
                        {
                            live_document_id = documentId,
                            expected_version = version,
                            save_changes = "discard",
                        }
                    );
                }
                catch
                {
                    using var disconnectedArguments = JsonDocument.Parse(
                        JsonSerializer.Serialize(
                            new { live_document_id = documentId },
                            JsonDefaults.Compact
                        )
                    );
                    _ = await service.CallAsync(
                        "disconnect_live_word_document",
                        disconnectedArguments.RootElement,
                        CancellationToken.None
                    );
                    throw;
                }
            }
        }
    }

    [Fact]
    public async Task RepeatedInspectAndPointUpdatesRemainNativeAcrossSixtyCycles()
    {
        if (
            !string.Equals(
                Environment.GetEnvironmentVariable(
                    "WORDTOOLKIT_REAL_WORD_EQUATION_UPDATE_TEST"
                ),
                "1",
                StringComparison.Ordinal
            )
        )
        {
            return;
        }

        await using var host = new WordComHost();
        var service = new WordLiveService(host);
        string? documentId = null;
        long version = 0;
        var runtimeStartedWord = false;
        ExceptionDispatchInfo? primaryFailure = null;
        Exception? cleanupFailure = null;
        try
        {
            using (var started = await Call(
                service,
                "start_word_application",
                new { visible = false }
            ))
            {
                Assert.True(started.RootElement.ValueKind == JsonValueKind.Object);
                runtimeStartedWord = started.RootElement
                    .GetProperty("application_owned_by_runtime")
                    .GetBoolean();
            }

            using (var created = await Call(
                service,
                "create_live_word_document",
                new { activate = false }
            ))
            {
                documentId = created.RootElement
                    .GetProperty("live_document_id")
                    .GetString();
                version = created.RootElement.GetProperty("live_version").GetInt64();
            }

            using (var applied = await Call(
                service,
                "apply_live_word_operations",
                new
                {
                    live_document_id = documentId,
                    expected_version = version,
                    operations = new object[]
                    {
                        new
                        {
                            type = "equation",
                            value = "x+1",
                            input_format = "latex",
                            display = true,
                        },
                    },
                }
            ))
            {
                version = applied.RootElement.GetProperty("live_version").GetInt64();
                Assert.Equal(1, version);
                Assert.Equal(
                    1,
                    applied.RootElement.GetProperty("document")
                        .GetProperty("equation_count")
                        .GetInt32()
                );
            }

            for (var cycle = 0; cycle < 60; cycle++)
            {
                string token;
                using (var inspected = await Call(
                    service,
                    "inspect_live_word_equations",
                    new { live_document_id = documentId }
                ))
                {
                    Assert.Equal(
                        1,
                        inspected.RootElement.GetProperty("equation_count").GetInt32()
                    );
                    var equation = inspected.RootElement.GetProperty("equations")[0];
                    Assert.Equal(1, equation.GetProperty("equation_index").GetInt32());
                    token = equation.GetProperty("equation_token").GetString()!;
                    Assert.False(string.IsNullOrWhiteSpace(token));
                }

                var value = cycle % 2 == 0 ? "x+1" : @"\frac{a}{b}";
                using var updated = await Call(
                    service,
                    "update_live_word_equation",
                    new
                    {
                        live_document_id = documentId,
                        expected_version = version,
                        equation_index = 1,
                        equation_token = token,
                        value,
                        input_format = "latex",
                        display = true,
                        verify_readback = true,
                    }
                );
                version = updated.RootElement.GetProperty("live_version").GetInt64();
                Assert.Equal(cycle + 2, version);
                Assert.True(updated.RootElement.GetProperty("updated").GetBoolean());
                Assert.True(updated.RootElement.GetProperty("native_verified").GetBoolean());
                Assert.True(updated.RootElement.GetProperty("readback_verified").GetBoolean());
                Assert.Equal(
                    1,
                    updated.RootElement.GetProperty("document")
                        .GetProperty("equation_count")
                        .GetInt32()
                );
            }
        }
        catch (Exception exception)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            if (documentId is not null)
            {
                try
                {
                    using var closed = await Call(
                        service,
                        "close_live_word_document",
                        new
                        {
                            live_document_id = documentId,
                            expected_version = version,
                            save_changes = "discard",
                        }
                    );
                }
                catch (Exception exception)
                {
                    cleanupFailure ??= exception;
                    try
                    {
                        using var disconnectedArguments = JsonDocument.Parse(
                            JsonSerializer.Serialize(
                                new { live_document_id = documentId },
                                JsonDefaults.Compact
                            )
                        );
                        _ = await service.CallAsync(
                            "disconnect_live_word_document",
                            disconnectedArguments.RootElement,
                            CancellationToken.None
                        );
                    }
                    catch (Exception disconnectException)
                    {
                        cleanupFailure ??= disconnectException;
                    }
                }
            }
            if (runtimeStartedWord || host.ApplicationOwnedByRuntime)
            {
                try
                {
                    using var quit = await Call(
                        service,
                        "quit_word_application",
                        new { save_changes = "discard_all", confirm = true }
                    );
                }
                catch (Exception exception)
                {
                    cleanupFailure ??= exception;
                }
            }
        }
        if (primaryFailure is not null)
        {
            if (cleanupFailure is not null)
            {
                primaryFailure.SourceException.Data["WordToolkitCleanupFailure"] =
                    cleanupFailure.ToString();
            }
            primaryFailure.Throw();
        }
        if (cleanupFailure is not null)
        {
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }
    }

    private static async Task<JsonDocument> Call(
        WordLiveService service,
        string action,
        object arguments
    )
    {
        using var request = JsonDocument.Parse(
            JsonSerializer.Serialize(arguments, JsonDefaults.Compact)
        );
        var result = await service.CallAsync(
            action,
            request.RootElement,
            CancellationToken.None
        );
        return JsonDocument.Parse(JsonSerializer.Serialize(result, JsonDefaults.Compact));
    }

    private static string HashFileShared(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
