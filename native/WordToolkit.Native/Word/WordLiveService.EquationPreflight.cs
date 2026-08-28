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
    ) => await PreflightEquationsCoreAsync(
        arguments,
        cancellationToken,
        forceInProcess: false
    );

    internal async Task<object> PreflightEquationsInProcessAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => await PreflightEquationsCoreAsync(
        arguments,
        cancellationToken,
        forceInProcess: true
    );

    private async Task<object> PreflightEquationsCoreAsync(
        JsonElement arguments,
        CancellationToken cancellationToken,
        bool forceInProcess
    )
    {
        RequireObject(arguments, "equation preflight arguments");
        foreach (var property in arguments.EnumerateObject())
        {
            if (
                property.Name is not (
                    "equations"
                    or "validation_mode"
                    or "per_equation_timeout_seconds"
                    or "total_timeout_seconds"
                )
            )
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
        var requestedPerEquationTimeout = arguments.NullableInt64(
            "per_equation_timeout_seconds"
        ) ?? EquationPreflightProcessRunner.DefaultPerEquationTimeoutSeconds;
        var requestedTotalTimeout = arguments.NullableInt64(
            "total_timeout_seconds"
        ) ?? EquationPreflightProcessRunner.DefaultTotalTimeoutSeconds;
        if (requestedPerEquationTimeout is < 5 or > 60)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "per_equation_timeout_seconds must be between 5 and 60"
            );
        }
        if (
            requestedTotalTimeout is < 10 or > 150
            || requestedTotalTimeout < requestedPerEquationTimeout
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "total_timeout_seconds must be between 10 and 150 and not less than the per-equation timeout"
            );
        }
        var perEquationTimeoutSeconds = (int)requestedPerEquationTimeout;
        var totalTimeoutSeconds = (int)requestedTotalTimeout;

        var equationInputs = equations.EnumerateArray()
            .Select(equation => equation.Clone())
            .ToArray();
        var equationIds = equationInputs
            .Select(EquationPreflightIdentity.FromInput)
            .ToArray();
        if (
            validationMode == "native"
            && !forceInProcess
            && _host is WordComHost
        )
        {
            return await EquationPreflightProcessRunner.RunAsync(
                arguments,
                equationInputs.Length,
                perEquationTimeoutSeconds,
                totalTimeoutSeconds,
                cancellationToken
            );
        }
        if (validationMode == "conversion_only")
        {
            return ConversionOnlyEquationPreflight(equationInputs, equationIds);
        }
        var prepared = equationInputs
            .Select((equation, index) =>
            {
                var conversionStarted = Stopwatch.GetTimestamp();
                try
                {
                    return EquationOperationFromArguments(equation);
                }
                catch (Exception exception)
                {
                    throw EquationPreflightFailure(
                        index,
                        exception,
                        Stopwatch.GetElapsedTime(conversionStarted),
                        "conversion",
                        equationIds[index]
                    );
                }
            })
            .ToArray();
        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application => ExecuteNativeEquationPreflight(
                application,
                prepared,
                equationIds,
                started
            ),
            cancellationToken
        );
    }

    private static object ConversionOnlyEquationPreflight(
        IReadOnlyList<JsonElement> equations,
        IReadOnlyList<string> equationIds
    )
    {
        var results = new object[equations.Count];
        var validCount = 0;
        var invalidCount = 0;
        for (var index = 0; index < equations.Count; index++)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                var prepared = EquationOperationFromArguments(equations[index]);
                results[index] = EquationPreflightItem(
                    index,
                    prepared,
                    built: null,
                    equationIds[index]
                );
                validCount++;
            }
            catch (Exception exception)
            {
                invalidCount++;
                var failure = EquationPreflightFailure(
                    index,
                    exception,
                    Stopwatch.GetElapsedTime(started),
                    "conversion",
                    equationIds[index]
                );
                results[index] = new
                {
                    index,
                    equation_id = equationIds[index],
                    valid = false,
                    conversion_valid = false,
                    native_execution_verified = false,
                    error_code = failure.ErrorCode,
                    stage = "conversion",
                    suggestion_code = failure.Message.Contains(
                        "unsupported",
                        StringComparison.OrdinalIgnoreCase
                    )
                        ? "USE_SUPPORTED_LATEX_OR_UNICODEMATH"
                        : "FIX_EQUATION_INPUT",
                    raw_document_content_returned = false,
                };
            }
        }
        return new
        {
            valid = (bool?)null,
            valid_count = validCount,
            invalid_count = invalidCount,
            conversion_valid = invalidCount == 0,
            native_execution_verified = false,
            validation_mode = "conversion_only",
            equation_count = equations.Count,
            equations = results,
            mutated_connected_document = false,
            warnings = new[]
            {
                "Conversion-only mode does not prove Microsoft Word OMath BuildUp or native OMML readback.",
            },
            runtime = "dotnet-native",
            python_used = false,
        };
    }

    private static object ExecuteNativeEquationPreflight(
        dynamic application,
        IReadOnlyList<PreparedEquationOperation> operations,
        IReadOnlyList<string> equationIds,
        long started
    )
    {
        var initialDocumentCount = ReadPreflightDocumentCount(application);
        object? previousActiveDocument = null;
        object? previousActiveWindow = null;
        if (initialDocumentCount > 0)
        {
            previousActiveDocument = (object)application.ActiveDocument;
            previousActiveWindow = (object)application.ActiveWindow;
        }

        object? scratchDocument = null;
        object? scratchContent = null;
        object? insertionRange = null;
        BuiltEquationResult?[] built = new BuiltEquationResult?[operations.Count];
        Exception? executionFailure = null;
        Exception? closeFailure = null;
        Exception? restoreFailure = null;
        Exception? verificationFailure = null;
        var scratchClosed = false;
        var scratchCreated = false;
        try
        {
            scratchDocument = (object)application.Documents.Add(Visible: false);
            scratchCreated = true;
            dynamic document = scratchDocument;
            document.Activate();

            dynamic content = document.Content;
            scratchContent = (object)content;
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
            insertionRange = (object)insertion;
            insertion.Text = string.Concat(pieces);

            dynamic? beforeEquations = null;
            int before;
            try
            {
                beforeEquations = document.OMaths;
                before = (int)beforeEquations.Count;
            }
            finally
            {
                FinalReleaseBatchComObject(beforeEquations);
            }
            for (var index = operations.Count - 1; index >= 0; index--)
            {
                var segment = segments[index];
                var equationStarted = Stopwatch.GetTimestamp();
                try
                {
                    built[index] = BuildVerifiedNativeEquation(
                        document,
                        insertionStart + segment.Start,
                        insertionStart + segment.End,
                        operations[index],
                        expectedEquationCollectionIndex: 1
                    );
                }
                catch (Exception exception)
                {
                    throw EquationPreflightFailure(
                        index,
                        exception,
                        Stopwatch.GetElapsedTime(equationStarted),
                        "build_verified_native_equation",
                        equationIds[index]
                    );
                }
            }
            dynamic? afterEquations = null;
            int after;
            try
            {
                afterEquations = document.OMaths;
                after = (int)afterEquations.Count;
            }
            finally
            {
                FinalReleaseBatchComObject(afterEquations);
            }
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
            FinalReleaseBatchComObject(insertionRange);
            insertionRange = null;
            FinalReleaseBatchComObject(scratchContent);
            scratchContent = null;
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
                var finalDocumentCount = ReadPreflightDocumentCount(application);
                object? observedActiveDocument = null;
                object? observedActiveWindow = null;
                bool activeDocumentRestored;
                bool activeWindowRestored;
                try
                {
                    if (previousActiveDocument is not null)
                    {
                        observedActiveDocument = (object)application.ActiveDocument;
                    }
                    if (previousActiveWindow is not null)
                    {
                        observedActiveWindow = (object)application.ActiveWindow;
                    }
                    activeDocumentRestored = previousActiveDocument is null
                        || SameWordComIdentity(
                            observedActiveDocument!,
                            previousActiveDocument
                        );
                    activeWindowRestored = previousActiveWindow is null
                        || SameWordComIdentity(
                            observedActiveWindow!,
                            previousActiveWindow
                        );
                }
                finally
                {
                    FinalReleaseBatchComObject(observedActiveWindow);
                    FinalReleaseBatchComObject(observedActiveDocument);
                }
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
            foreach (var result in built)
            {
                if (result is not null)
                {
                    FinalReleaseBatchComObject(result.Equation);
                }
            }
            FinalReleaseBatchComObject(scratchDocument);
            scratchDocument = null;
            FinalReleaseBatchComObject(previousActiveWindow);
            previousActiveWindow = null;
            FinalReleaseBatchComObject(previousActiveDocument);
            previousActiveDocument = null;
        }

        if (
            closeFailure is not null
            || restoreFailure is not null
            || verificationFailure is not null
            || scratchCreated && !scratchClosed
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
                    scratch_document_created = scratchCreated,
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
                    built[index]!,
                    equationIds[index]
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
        Exception exception,
        TimeSpan elapsed,
        string stage,
        string equationId
    )
    {
        var errorCode = exception is NativeToolException nativeFailure
            ? nativeFailure.ErrorCode
            : "EQUATION_INVALID";
        return new NativeToolException(
            errorCode,
            stage == "conversion"
                ? exception.Message
                : "Microsoft Word rejected an equation during isolated native preflight",
            new
            {
                equation_index = index,
                equation_id = equationId,
                stage,
                elapsed_milliseconds = Math.Max(0L, (long)elapsed.TotalMilliseconds),
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

    private static int ReadPreflightDocumentCount(dynamic application)
    {
        dynamic? documents = null;
        try
        {
            documents = application.Documents;
            return (int)documents.Count;
        }
        finally
        {
            FinalReleaseBatchComObject(documents);
        }
    }

    private static object EquationPreflightItem(
        int index,
        PreparedEquationOperation prepared,
        BuiltEquationResult? built,
        string equationId
    )
    {
        var readback = built?.Readback;
        var directOmml = built?.DirectOmml;
        return new
        {
            index,
            equation_id = equationId,
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
            direct_omml = prepared.DirectPlan is null
                ? null
                : new
                {
                    source_validated = true,
                    native_semantic_verified = directOmml is not null,
                    namespace_identity = prepared.DirectPlan.NamespaceIdentity,
                    expected_semantic_sha256 = prepared.DirectPlan.SemanticSha256,
                    actual_semantic_sha256 = directOmml?.ActualCombinedSemanticSha256,
                    expected_equation_semantic_sha256 =
                        directOmml?.ExpectedEquationSemanticSha256,
                    actual_equation_semantic_sha256 =
                        directOmml?.ActualEquationSemanticSha256,
                    expected_paragraph_properties_sha256 =
                        directOmml?.ExpectedParagraphPropertiesSha256,
                    actual_paragraph_properties_sha256 =
                        directOmml?.ActualParagraphPropertiesSha256,
                    expected_paragraph_justification =
                        directOmml?.ExpectedParagraphJustification,
                    actual_paragraph_justification =
                        directOmml?.ActualParagraphJustification,
                    element_count = directOmml?.ElementCount,
                    raw_omml_returned = false,
                },
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
