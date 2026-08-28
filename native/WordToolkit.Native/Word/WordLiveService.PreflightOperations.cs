using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private async Task<object> PreflightOperationsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var expectedVersion = arguments.NullableInt64("expected_version")
            ?? throw new NativeToolException(
                "INVALID_INPUT",
                "expected_version is required for target-bound operation preflight"
            );
        var operationInputs = arguments.RequiredArray("operations");
        var record = Record(arguments.String("live_document_id"));
        CheckVersion(record, expectedVersion);
        IReadOnlyList<PreparedOperation> operations;
        try
        {
            operations = PrepareOperations(operationInputs);
        }
        catch (Exception)
        {
            var diagnostic = await DiagnoseAllPreflightEquationsAsync(
                record,
                expectedVersion,
                operationInputs,
                cancellationToken
            ).ConfigureAwait(false);
            if (diagnostic is not null)
            {
                return diagnostic;
            }
            throw;
        }
        try
        {
            return await PreflightPreparedOperationsAsync(
                record,
                operations,
                expectedVersion,
                cancellationToken
            ).ConfigureAwait(false);
        }
        catch (NativeToolException exception) when (
            _host is WordComHost
            && (
            exception.ErrorCode is "EQUATION_INVALID" or "INVALID_INPUT"
            || TryGetFailedOperationIndex(exception) is not null
            )
        )
        {
            var diagnostic = await DiagnoseAllPreflightEquationsAsync(
                record,
                expectedVersion,
                operationInputs,
                cancellationToken
            ).ConfigureAwait(false);
            if (diagnostic is not null)
            {
                return diagnostic;
            }
            throw;
        }
    }

    private async Task<object?> DiagnoseAllPreflightEquationsAsync(
        LiveDocumentRecord record,
        long expectedVersion,
        JsonElement operations,
        CancellationToken cancellationToken
    )
    {
        var equationInputs = new JsonArray();
        var operationIndexes = new List<int>();
        var operationIndex = 0;
        foreach (var operation in operations.EnumerateArray())
        {
            if (
                operation.ValueKind == JsonValueKind.Object
                && operation.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == "equation"
            )
            {
                var equation = new JsonObject();
                foreach (var name in new[]
                {
                    "value",
                    "input_format",
                    "display",
                    "verify_readback",
                    "source_format",
                    "equation_source_format",
                    "format",
                })
                {
                    if (operation.TryGetProperty(name, out var value))
                    {
                        equation[name] = JsonNode.Parse(value.GetRawText());
                    }
                }
                equationInputs.Add(equation);
                operationIndexes.Add(operationIndex);
            }
            operationIndex++;
        }
        if (equationInputs.Count == 0)
        {
            return null;
        }
        using var preflightArguments = JsonDocument.Parse(
            new JsonObject
            {
                ["validation_mode"] = "native",
                ["equations"] = equationInputs,
            }.ToJsonString(JsonDefaults.Compact)
        );
        var result = await PreflightEquationsAsync(
            preflightArguments.RootElement,
            cancellationToken
        ).ConfigureAwait(false);
        var resultNode = JsonSerializer.SerializeToNode(
            result,
            JsonDefaults.Compact
        )?.AsObject() ?? new JsonObject();
        if (resultNode["valid"]?.GetValue<bool>() != false)
        {
            return null;
        }
        CheckVersion(record, expectedVersion);
        await _host.InvokeAsync(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic? document = null;
                try
                {
                    document = ResolveDocument(application, record);
                    return true;
                }
                finally
                {
                    FinalReleaseBatchComObject(document);
                }
            },
            WordComReplaySafety.ReplaySafe,
            cancellationToken
        ).ConfigureAwait(false);
        var failures = new JsonArray();
        if (resultNode["equations"] is JsonArray items)
        {
            foreach (var item in items.OfType<JsonObject>().Where(item =>
                item["valid"]?.GetValue<bool>() == false))
            {
                var equationOrdinal = item["index"]!.GetValue<int>();
                failures.Add(new JsonObject
                {
                    ["operation_index"] = operationIndexes[equationOrdinal],
                    ["equation_index"] = equationOrdinal,
                    ["equation_id"] = item["equation_id"]?.DeepClone(),
                    ["error_code"] = item["error_code"]?.DeepClone(),
                    ["stage"] = item["stage"]?.DeepClone(),
                    ["suggestion_code"] = item["suggestion_code"]?.DeepClone(),
                    ["diagnostic"] = item["diagnostic"]?.DeepClone(),
                });
            }
        }
        return new
        {
            operation_contract = "wordtoolkit.preflight_live_word_operations/1.1",
            live_document_id = record.Id,
            live_version = record.Version,
            expected_version = expectedVersion,
            validation_mode = "target_bound_with_isolated_equation_diagnostics",
            valid = false,
            published = false,
            target_document_mutated = false,
            operation_count = operations.GetArrayLength(),
            text_operation_count = operations.GetArrayLength() - equationInputs.Count,
            equation_operation_count = equationInputs.Count,
            valid_equation_count = resultNode["valid_count"]?.DeepClone(),
            invalid_equation_count = resultNode["invalid_count"]?.DeepClone(),
            equation_failures = failures,
            runtime = "dotnet-native",
            python_used = false,
        };
    }

    private async Task<object> PreflightPreparedOperationsAsync(
        LiveDocumentRecord record,
        IReadOnlyList<PreparedOperation> operations,
        long expectedVersion,
        CancellationToken cancellationToken
    )
    {
        CheckVersion(record, expectedVersion);
        var started = Stopwatch.GetTimestamp();
        var complexity = BatchComplexity.For(operations);
        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                var insertionStart = Math.Max(0, (int)document.Content.End - 1);
                string previous;
                if (insertionStart > 0)
                {
                    dynamic? previousRange = null;
                    try
                    {
                        previousRange = document.Range(insertionStart - 1, insertionStart);
                        previous = (string?)previousRange.Text ?? "";
                    }
                    finally
                    {
                        FinalReleaseBatchComObject(previousRange);
                    }
                }
                else
                {
                    previous = "";
                }

                var payload = BuildPreparedBatchPayload(
                    operations,
                    insertionStart,
                    previous
                );
                var snapshot = CaptureLiveRollbackSnapshot(
                    document,
                    insertionStart,
                    insertionStart,
                    record.Version
                );
                StagedPreparedBatch? staged = null;
                var stagingOpen = false;
                try
                {
                    staged = StagePreparedBatch(
                        application,
                        document,
                        operations,
                        payload.Payload,
                        payload.Segments
                    );
                    stagingOpen = true;
                    EnsureTargetUnchangedBeforePublication(document, snapshot, record);

                    var proofs = staged.OperationRanges
                        .Select((range, index) => new
                        {
                            index,
                            type = operations[index] is PreparedEquationOperation
                                ? "equation"
                                : "text",
                            range_length = checked(range.End - range.Start),
                            content_sha256 = range.TextSha256,
                            native_equation_verified = operations[index]
                                is PreparedEquationOperation,
                        })
                        .ToArray();
                    var equationCount = staged.EquationIndexes.Count;

                    stagingOpen = false;
                    CloseStagedPreparedBatch(staged, targetMutationAttempted: false);
                    staged = null;
                    EnsureTargetUnchangedBeforePublication(document, snapshot, record);
                    CheckVersion(record, expectedVersion);

                    return new
                    {
                        operation_contract =
                            "wordtoolkit.preflight_live_word_operations/1.1",
                        live_document_id = record.Id,
                        live_version = record.Version,
                        expected_version = expectedVersion,
                        validation_mode = "target_bound_exact_batch_staging",
                        valid = true,
                        published = false,
                        target_document_mutated = false,
                        operation_count = operations.Count,
                        text_operation_count = operations.Count - equationCount,
                        equation_operation_count = equationCount,
                        operations = proofs,
                        complexity,
                        runtime = "dotnet-native",
                        python_used = false,
                        performance = Performance(started, complexity),
                    };
                }
                catch (Exception exception)
                {
                    Exception effectiveException = exception;
                    if (staged is not null && stagingOpen)
                    {
                        stagingOpen = false;
                        try
                        {
                            CloseStagedPreparedBatch(
                                staged,
                                targetMutationAttempted: false,
                                originalFailure: exception
                            );
                        }
                        catch (Exception cleanupException)
                        {
                            effectiveException = new NativeToolException(
                                "STAGING_CLEANUP_FAILED",
                                "Target-bound batch preflight failed and scratch cleanup could not be proven",
                                new
                                {
                                    original_error_code = exception
                                        is NativeToolException originalNative
                                            ? originalNative.ErrorCode
                                            : "EXTERNAL_TOOL_FAILED",
                                    original_error_message = exception.Message[..Math.Min(
                                        exception.Message.Length,
                                        256
                                    )],
                                    cleanup_error_code = cleanupException
                                        is NativeToolException cleanupNative
                                            ? cleanupNative.ErrorCode
                                            : "EXTERNAL_TOOL_FAILED",
                                    cleanup_error_message = cleanupException.Message[..Math.Min(
                                        cleanupException.Message.Length,
                                        256
                                    )],
                                    failed_operation_index = TryGetFailedOperationIndex(exception),
                                    target_document_mutated = false,
                                    raw_document_content_returned = false,
                                }
                            );
                        }
                    }
                    EnsureTargetUnchangedBeforePublication(
                        document,
                        snapshot,
                        record,
                        effectiveException
                    );
                    throw effectiveException;
                }
            },
            cancellationToken
        );
    }
}
