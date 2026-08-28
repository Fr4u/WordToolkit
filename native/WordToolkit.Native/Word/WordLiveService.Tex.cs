using System.Security.Cryptography;
using System.Text.Json;
using WordToolkit.Engine.Publishing;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Tex;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private async Task<object> CompileTexDocumentAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        _ = arguments.Required("tectonic_path");
        _ = arguments.Required("source");
        _ = arguments.Required("output_path");
        var executablePath = arguments.String("tectonic_path");
        var source = arguments.String("source");
        var outputPath = ValidatePdfOutputPath(arguments.String("output_path"));
        _ = ResolveLibreOfficeRenderPath(
            Path.GetDirectoryName(outputPath)!,
            "output_path parent directory",
            file: false
        );
        if (File.Exists(outputPath))
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "The TeX PDF output file already exists; it was not overwritten"
            );
        }
        var expectedSha256 = arguments.String("expected_tectonic_sha256", "");
        var allowNetworkResourceFetch = arguments.Boolean(
            "allow_network_resource_fetch",
            false
        );
        var timeoutSeconds = 60;
        if (arguments.TryGetProperty("timeout_seconds", out var timeoutNode))
        {
            if (timeoutNode.ValueKind != JsonValueKind.Number || !timeoutNode.TryGetInt32(out timeoutSeconds))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Argument 'timeout_seconds' must be an integer"
                );
            }
        }

        TectonicCompilationResult result;
        try
        {
            result = await new TectonicCompiler().CompileAsync(
                executablePath,
                source,
                TimeSpan.FromSeconds(timeoutSeconds),
                expectedSha256.Length == 0 ? null : expectedSha256,
                allowNetworkResourceFetch,
                cancellationToken
            );
        }
        catch (ArgumentException exception)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                exception.Message,
                new { argument = exception.ParamName }
            );
        }
        catch (InvalidOperationException exception)
        {
            throw new NativeToolException(
                "PROVIDER_UNAVAILABLE",
                exception.Message
            );
        }
        catch (TimeoutException exception)
        {
            throw new NativeToolException(
                "BACKEND_TIMEOUT",
                exception.Message,
                retryable: true
            );
        }
        catch (TectonicCleanupException exception)
        {
            throw new NativeToolException(
                "CLEANUP_FAILED",
                exception.Message
            );
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            throw new NativeToolException(
                "IO_ERROR",
                "Tectonic provider I/O failed before publication",
                new { exception = exception.GetType().Name }
            );
        }

        if (!result.Succeeded || result.PdfBytes is null)
        {
            return TexCompilationResponse(result, source, outputPath, published: false);
        }

        var outputDirectory = Path.GetDirectoryName(outputPath)!;
        var stagedPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileName(outputPath)}.wordtoolkit-{Guid.NewGuid():N}.tmp"
        );
        try
        {
            await using (var stream = new FileStream(
                stagedPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough
            ))
            {
                await stream.WriteAsync(result.PdfBytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            BeforeCreateNewPublication?.Invoke(stagedPath, outputPath);
            AtomicFilePublisher.PublishCreateNew(stagedPath, outputPath);
            await VerifyPublishedTexPdfAsync(
                outputPath,
                result.PdfBytesLength,
                result.PdfSha256!,
                cancellationToken
            );
        }
        catch (IOException exception) when (AtomicFilePublisher.IsAlreadyExists(exception))
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "The TeX PDF output file was created concurrently; it was not overwritten"
            );
        }
        finally
        {
            try
            {
                if (File.Exists(stagedPath))
                    File.Delete(stagedPath);
            }
            catch
            {
                // The public result remains independently hash-verifiable. A private
                // cleanup failure must not trigger a second publication attempt.
            }
        }

        return TexCompilationResponse(result, source, outputPath, published: true);
    }

    private static object TexCompilationResponse(
        TectonicCompilationResult result,
        string source,
        string outputPath,
        bool published
    ) => new
    {
        operation_contract = "wordtoolkit.compile_tex_document/1.0",
        succeeded = result.Succeeded,
        published,
        output_path = published ? outputPath : null,
        diagnostics = result.Diagnostics.Select(diagnostic => new
        {
            severity = diagnostic.Severity,
            message = diagnostic.Message,
            line = diagnostic.Line,
        }),
        pdf_sha256 = result.PdfSha256,
        pdf_bytes = result.PdfBytesLength,
        provider = new
        {
            name = "tectonic",
            version = result.ProviderVersion,
            executable_sha256 = result.ProviderSha256,
            invocation_untrusted = true,
            only_cached_resources = result.OnlyCachedResources,
            network_requested = result.NetworkRequested,
            network_isolation_proven = false,
            provider_cache_mutation_possible = result.NetworkRequested,
            resource_bundle_hash_bound = false,
        },
        source_sha256 = Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source))
        ).ToLowerInvariant(),
        source_returned = false,
        temporary_files_retained = false,
        word_opened = false,
        document_mutated = false,
        editable_office_math = false,
        tex_to_omml_conversion_performed = false,
        representability = new
        {
            arbitrary_tex_is_not_bijective_with_office_math = true,
            use_native_equation_actions_for_supported_latex_math = true,
            use_input_format_omml_for_exact_office_math = true,
        },
    };

    private static async Task VerifyPublishedTexPdfAsync(
        string path,
        long expectedLength,
        string expectedSha256,
        CancellationToken cancellationToken
    )
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        if (stream.Length != expectedLength)
        {
            throw new NativeToolException(
                "OUTPUT_VERIFICATION_FAILED",
                "Published TeX PDF length does not match the staged result"
            );
        }
        var actualSha256 = Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken)
        ).ToLowerInvariant();
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.Ordinal))
        {
            throw new NativeToolException(
                "OUTPUT_VERIFICATION_FAILED",
                "Published TeX PDF hash does not match the staged result"
            );
        }
    }
}
