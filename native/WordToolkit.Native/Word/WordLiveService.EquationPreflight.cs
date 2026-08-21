using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private async Task<object> PreflightEquationsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        RequireObject(arguments, "equation preflight arguments");
        foreach (var property in arguments.EnumerateObject())
        {
            if (property.Name is not ("equations" or "validation_mode"))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Unknown equation preflight argument",
                    new { argument = property.Name }
                );
            }
        }

        var equations = arguments.RequiredArray("equations");
        if (equations.GetArrayLength() is < 1 or > 200)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "equations must contain between 1 and 200 items"
            );
        }
        var validationMode = arguments.String("validation_mode", "native");
        if (validationMode is not ("native" or "conversion_only"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "validation_mode must be 'native' or 'conversion_only'"
            );
        }

        var prepared = equations.EnumerateArray()
            .Select(EquationOperationFromArguments)
            .ToArray();
        if (validationMode == "conversion_only")
        {
            var conversionResults = prepared
                .Select(
                    (operation, index) => EquationPreflightItem(
                        index,
                        operation,
                        built: null
                    )
                )
                .ToArray();
            return new
            {
                valid = (bool?)null,
                conversion_valid = true,
                native_execution_verified = false,
                validation_mode = validationMode,
                equation_count = conversionResults.Length,
                equations = conversionResults,
                mutated_connected_document = false,
                warnings = new[]
                {
                    "Conversion-only mode does not prove Microsoft Word OMath BuildUp or native OMML readback.",
                },
                runtime = "dotnet-native",
                python_used = false,
            };
        }

        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application => ExecuteNativeEquationPreflight(
                application,
                prepared,
                started
            ),
            cancellationToken
        );
    }

    private static object ExecuteNativeEquationPreflight(
        dynamic application,
        IReadOnlyList<PreparedEquationOperation> operations,
        long started
    )
    {
        var initialDocumentCount = (int)application.Documents.Count;
        object? previousActiveDocument = null;
        object? previousActiveWindow = null;
        if (initialDocumentCount > 0)
        {
            previousActiveDocument = (object)application.ActiveDocument;
            previousActiveWindow = (object)application.ActiveWindow;
        }

        object? scratchDocument = null;
        BuiltEquationResult?[] built = new BuiltEquationResult?[operations.Count];
        Exception? executionFailure = null;
        Exception? closeFailure = null;
        Exception? restoreFailure = null;
        Exception? verificationFailure = null;
        var scratchClosed = false;
        try
        {
            scratchDocument = (object)application.Documents.Add(Visible: false);
            dynamic document = scratchDocument;
            document.Activate();

            dynamic content = document.Content;
            var insertionStart = (int)content.Start;
            var pieces = new List<string>(operations.Count);
            var segments = new (int Start, int End)[operations.Count];
            var offset = 0;
            for (var index = 0; index < operations.Count; index++)
            {
                var value = NormalizeWordText(operations[index].Value);
                var prefix = index == 0 ? "" : "\r";
                pieces.Add(prefix + value);
                segments[index] = (
                    offset + prefix.Length,
                    offset + prefix.Length + value.Length
                );
                offset += prefix.Length + value.Length;
            }
            dynamic insertion = document.Range(insertionStart, insertionStart);
            insertion.Text = string.Concat(pieces);

            var before = (int)document.OMaths.Count;
            for (var index = operations.Count - 1; index >= 0; index--)
            {
                var segment = segments[index];
                try
                {
                    built[index] = BuildVerifiedNativeEquation(
                        document,
                        insertionStart + segment.Start,
                        insertionStart + segment.End,
                        operations[index]
                    );
                }
                catch (Exception exception)
                {
                    throw EquationPreflightFailure(index, exception);
                }
            }
            var after = (int)document.OMaths.Count;
            if (after != before + operations.Count)
            {
                throw new NativeToolException(
                    "EQUATION_INVALID",
                    "Microsoft Word did not create the expected native equations during isolated preflight",
                    new
                    {
                        before,
                        after,
                        expected = operations.Count,
                        target_document_mutated = false,
                    }
                );
            }
        }
        catch (Exception exception)
        {
            executionFailure = exception;
        }
        finally
        {
            if (scratchDocument is not null)
            {
                try
                {
                    ((dynamic)scratchDocument).Close(WordDoNotSaveChanges);
                    scratchClosed = true;
                }
                catch (Exception exception)
                {
                    closeFailure = exception;
                }
            }
            if (previousActiveDocument is not null)
            {
                try
                {
                    ((dynamic)previousActiveDocument).Activate();
                    ((dynamic)previousActiveWindow!).Activate();
                }
                catch (Exception exception)
                {
                    restoreFailure = exception;
                }
            }
            try
            {
                var finalDocumentCount = (int)application.Documents.Count;
                var activeDocumentRestored = previousActiveDocument is null
                    || SameWordComIdentity(
                        (object)application.ActiveDocument,
                        previousActiveDocument
                    );
                var activeWindowRestored = previousActiveWindow is null
                    || SameWordComIdentity(
                        (object)application.ActiveWindow,
                        previousActiveWindow
                    );
                if (
                    finalDocumentCount != initialDocumentCount
                    || !activeDocumentRestored
                    || !activeWindowRestored
                )
                {
                    verificationFailure = new InvalidOperationException(
                        "Word state was not restored after native equation preflight"
                    );
                }
            }
            catch (Exception exception)
            {
                verificationFailure = exception;
            }
        }

        if (
            closeFailure is not null
            || restoreFailure is not null
            || verificationFailure is not null
            || scratchDocument is not null && !scratchClosed
        )
        {
            throw new NativeToolException(
                "TEMPORARY_DOCUMENT_CLEANUP_FAILED",
                "WordToolkit could not prove cleanup of the isolated native equation preflight document",
                new
                {
                    original_error_code = executionFailure is NativeToolException nativeFailure
                        ? nativeFailure.ErrorCode
                        : executionFailure is null
                            ? null
                            : "EXTERNAL_TOOL_FAILED",
                    scratch_document_created = scratchDocument is not null,
                    scratch_document_closed = scratchClosed,
                    close_failed = closeFailure is not null,
                    active_state_restore_failed = restoreFailure is not null,
                    final_state_verification_failed = verificationFailure is not null,
                    connected_document_mutated = false,
                    raw_document_content_returned = false,
                }
            );
        }
        if (executionFailure is not null)
        {
            ExceptionDispatchInfo.Capture(executionFailure).Throw();
        }

        var results = operations
            .Select(
                (operation, index) => EquationPreflightItem(
                    index,
                    operation,
                    built[index]!
                )
            )
            .ToArray();
        return new
        {
            valid = true,
            conversion_valid = true,
            native_execution_verified = true,
            validation_mode = "native",
            equation_count = results.Length,
            equations = results,
            mutated_connected_document = false,
            isolation = new
            {
                scratch_document_created = true,
                scratch_document_closed = true,
                document_count_restored = true,
                active_document_restored = true,
                active_window_restored = true,
            },
            runtime = "dotnet-native",
            python_used = false,
            performance = Performance(started),
        };
    }

    private static NativeToolException EquationPreflightFailure(
        int index,
        Exception exception
    )
    {
        var errorCode = exception is NativeToolException nativeFailure
            ? nativeFailure.ErrorCode
            : "EQUATION_INVALID";
        return new NativeToolException(
            errorCode,
            "Microsoft Word rejected an equation during isolated native preflight",
            new
            {
                equation_index = index,
                original_error_code = exception is NativeToolException original
                    ? original.ErrorCode
                    : "EXTERNAL_TOOL_FAILED",
                original_details = exception is NativeToolException detailed
                    ? detailed.Details
                    : null,
                target_document_mutated = false,
                raw_document_content_returned = false,
            }
        );
    }

    private static object EquationPreflightItem(
        int index,
        PreparedEquationOperation prepared,
        BuiltEquationResult? built
    )
    {
        var readback = built?.Readback;
        return new
        {
            index,
            valid = built is null ? (bool?)null : true,
            conversion_valid = true,
            native_execution_verified = built is not null,
            input_format = prepared.InputFormat,
            word_linear = prepared.Linear,
            word_linear_characters = prepared.Linear.Length,
            display = prepared.Display,
            native_readback_required = prepared.ReadbackRequired,
            native_readback_enabled = prepared.VerifyReadback,
            native_readback_verified = readback is not null,
            native_style_rewrite_required = prepared.HasFormatting,
            native_style_verified = built?.StyleVerification is not null,
            formatting_region_count = prepared.StyleCounts.Total,
            formatting_regions = new
            {
                plain = prepared.StyleCounts.Plain,
                bold = prepared.StyleCounts.Bold,
                italic = prepared.StyleCounts.Italic,
                bold_italic = prepared.StyleCounts.BoldItalic,
                runs_and_controls = prepared.StyleCounts.RunsAndControls,
                runs_only = prepared.StyleCounts.RunsOnly,
                first_control = prepared.StyleCounts.FirstControl,
            },
            readback = readback is null
                ? null
                : new
                {
                    expected_contract_sha256 = readback.ExpectedContractSha256,
                    actual_contract_sha256 = readback.ActualContractSha256,
                    math_element_count = readback.MathElementCount,
                    nary_count = readback.NaryCount,
                    differential_count = readback.DifferentialCount,
                    differential_placement_verified = readback.DifferentialPlacementVerified,
                    raw_omml_returned = false,
                },
            rules = new[]
                {
                    prepared.InputFormat switch
                    {
                        "latex" => "native_latex_to_unicodemath",
                        "mathml" => "secure_mathml_to_unicodemath",
                        "omml" => "secure_omml_to_unicodemath",
                        _ => "native_unicodemath",
                    },
                    "single_com_omath_build_up",
                }
                .Concat(
                    prepared.VerifyReadback
                        ? new[] { "bounded_native_omml_readback" }
                        : Array.Empty<string>()
                )
                .Concat(
                    prepared.HasFormatting
                        ? new[] { "verified_native_omml_style_rewrite" }
                        : Array.Empty<string>()
                )
                .ToArray(),
            warnings = built is null
                ? new[] { "Native Microsoft Word execution was not requested." }
                : Array.Empty<string>(),
        };
    }
}
