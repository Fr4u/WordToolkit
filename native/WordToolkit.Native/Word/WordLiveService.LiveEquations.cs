using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private async Task<object> InspectLiveEquationsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        RequireObject(arguments, "live equation inspection arguments");
        foreach (var property in arguments.EnumerateObject())
        {
            if (
                property.Name
                is not (
                    "live_document_id"
                    or "offset"
                    or "limit"
                    or "include_text_preview"
                )
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Unknown live equation inspection argument",
                    new { argument = property.Name }
                );
            }
        }
        var record = Record(arguments.String("live_document_id"));
        var offset = checked((int)(arguments.NullableInt64("offset") ?? 0));
        var limit = checked((int)(arguments.NullableInt64("limit") ?? 50));
        var includeTextPreview = arguments.Boolean("include_text_preview", false);
        if (offset < 0 || limit is < 1 or > 200)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "offset must be non-negative and limit must be between 1 and 200"
            );
        }
        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                dynamic document = ResolveDocument(application, record);
                dynamic? equations = null;
                try
                {
                    equations = document.OMaths;
                    var total = (int)equations.Count;
                    var returned = Math.Min(limit, Math.Max(0, total - offset));
                    var items = new object[returned];
                    for (var itemIndex = 0; itemIndex < returned; itemIndex++)
                    {
                        dynamic? equation = null;
                        dynamic? range = null;
                        try
                        {
                            var equationIndex = offset + itemIndex + 1;
                            equation = equations.Item(equationIndex);
                            range = equation.Range;
                            var start = (int)range.Start;
                            var end = (int)range.End;
                            var wordOpenXml = (string?)range.WordOpenXML ?? "";
                            var semanticHash = RollbackSemanticSha256(wordOpenXml);
                            var contextHash = SelectionContextHash(document, start, end);
                            var token = Convert.ToHexString(
                                    RandomNumberGenerator.GetBytes(32)
                                )
                                .ToLowerInvariant();
                            _equationGrants[token] = new EquationGrant(
                                token,
                                record.Id,
                                record.Version,
                                equationIndex,
                                start,
                                end,
                                semanticHash,
                                contextHash
                            );
                            var text = ((string?)range.Text ?? "")
                                .Replace("\0", "", StringComparison.Ordinal);
                            var textHash = Convert.ToHexString(
                                    SHA256.HashData(Encoding.UTF8.GetBytes(text))
                                )
                                .ToLowerInvariant()[..16];
                            items[itemIndex] = new
                            {
                                equation_index = equationIndex,
                                display = (int)equation.Type == 0,
                                range = new { start, end },
                                text_characters = text.Length,
                                text_sha256 = textHash,
                                text_preview = includeTextPreview
                                    ? text[..Math.Min(text.Length, 256)]
                                    : null,
                                text_preview_truncated = includeTextPreview
                                    && text.Length > 256
                                        ? true
                                        : (bool?)null,
                                equation_token = token,
                                token_live_version = record.Version,
                                raw_omml_returned = false,
                            };
                        }
                        finally
                        {
                            FinalReleaseBatchComObject(range);
                            FinalReleaseBatchComObject(equation);
                        }
                    }
                    TrimEquationGrants();
                    return new
                    {
                        live_document_id = record.Id,
                        live_version = record.Version,
                        equation_count = total,
                        offset,
                        returned_equation_count = returned,
                        next_offset = offset + returned < total
                            ? offset + returned
                            : (int?)null,
                        equations = items,
                        raw_omml_returned = false,
                        document = DocumentInfo(application, document),
                        performance = Performance(started),
                    };
                }
                finally
                {
                    FinalReleaseBatchComObject(equations);
                }
            },
            cancellationToken
        );
    }

    private async Task<object> UpdateLiveEquationAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        RequireObject(arguments, "live equation update arguments");
        foreach (var property in arguments.EnumerateObject())
        {
            if (
                property.Name
                is not (
                    "live_document_id"
                    or "expected_version"
                    or "equation_index"
                    or "equation_token"
                    or "value"
                    or "input_format"
                    or "display"
                    or "verify_readback"
                    or "activate"
                    or "optimize_screen_updates"
                )
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Unknown live equation update argument",
                    new { argument = property.Name }
                );
            }
        }
        var record = Record(arguments.String("live_document_id"));
        var expectedVersion = arguments.NullableInt64("expected_version");
        if (expectedVersion is null)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "update_live_word_equation requires expected_version"
            );
        }
        var equationIndex = checked(
            (int)(arguments.NullableInt64("equation_index") ?? 0)
        );
        if (equationIndex < 1)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "equation_index must be a one-based positive integer"
            );
        }
        var equationToken = arguments.String("equation_token");
        if (!_equationGrants.TryGetValue(equationToken, out var grant))
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "A fresh equation_token from inspect_live_word_equations is required",
                retryable: true
            );
        }
        var operation = EquationOperationFromArguments(arguments);
        var activate = arguments.Boolean("activate", true);
        var optimizeScreenUpdates = arguments.Boolean(
            "optimize_screen_updates",
            true
        );
        CheckVersion(record, expectedVersion);
        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                if (activate)
                {
                    document.Activate();
                }
                dynamic? currentEquation = null;
                dynamic? currentEquationRange = null;
                dynamic? targetRange = null;
                dynamic? publishedRange = null;
                dynamic? publishedEquation = null;
                dynamic? finalRange = null;
                try
                {
                    currentEquation = ResolveVerifiedEquation(
                        document,
                        record,
                        equationIndex,
                        grant
                    );
                    currentEquationRange = currentEquation.Range;
                    targetRange = currentEquationRange.Duplicate;
                    var targetStart = (int)targetRange.Start;
                    var targetEnd = (int)targetRange.End;
                    var rollbackSnapshot = CaptureLiveRollbackSnapshot(
                        document,
                        targetStart,
                        targetEnd,
                        record.Version
                    );
                    var equationPayload = NormalizeWordText(operation.Value);
                    var stagingPayload = equationPayload + "\r";
                    StagedPreparedBatch? staged = null;
                    var stagingOpen = false;
                    try
                    {
                        staged = StagePreparedBatch(
                            application,
                            document,
                            new PreparedOperation[] { operation },
                            stagingPayload,
                            new (int Start, int End)[] { (0, equationPayload.Length) }
                        );
                        stagingOpen = true;
                        EnsureTargetUnchangedBeforePublication(
                            document,
                            rollbackSnapshot,
                            record
                        );
                    }
                    catch (Exception stagingException)
                    {
                        Exception effectiveException = stagingException;
                        if (staged is not null && stagingOpen)
                        {
                            try
                            {
                                CloseStagedPreparedBatch(
                                    staged,
                                    targetMutationAttempted: false,
                                    originalFailure: stagingException
                                );
                            }
                            catch (Exception cleanupException)
                            {
                                effectiveException = cleanupException;
                            }
                        }
                        EnsureTargetUnchangedBeforePublication(
                            document,
                            rollbackSnapshot,
                            record,
                            effectiveException
                        );
                        throw effectiveException;
                    }

                    var beforeContentEnd = ReadLiveContentEnd(document);
                    var beforeEquationCount = ReadLiveEquationCount(document);
                    var replacedLength = targetEnd - targetStart;
                    dynamic? undoRecord = null;
                    var undoStarted = false;
                    var mutationAttempted = false;
                    bool? originalScreenUpdating = null;
                    try
                    {
                        if (optimizeScreenUpdates)
                        {
                            originalScreenUpdating = (bool)application.ScreenUpdating;
                            application.ScreenUpdating = false;
                        }
                        undoRecord = application.UndoRecord;
                        undoRecord.StartCustomRecord("WordToolkit: update native equation");
                        undoStarted = true;
                        mutationAttempted = true;
                        dynamic? stagedEquationRange = null;
                        dynamic? stagedFormattedText = null;
                        try
                        {
                            stagedEquationRange = ((dynamic)staged!.Equations[0].Equation).Range;
                            stagedFormattedText = stagedEquationRange.FormattedText;
                            targetRange.FormattedText = stagedFormattedText;
                        }
                        finally
                        {
                            FinalReleaseBatchComObject(stagedFormattedText);
                            FinalReleaseBatchComObject(stagedEquationRange);
                        }

                        CloseStagedPreparedBatch(staged, targetMutationAttempted: true);
                        stagingOpen = false;
                        document.Activate();

                        var afterContentEnd = ReadLiveContentEnd(document);
                        var publishedLength = afterContentEnd - beforeContentEnd + replacedLength;
                        publishedRange = document.Range(
                            targetStart,
                            targetStart + publishedLength
                        );
                        var afterEquationCount = ReadLiveEquationCount(document);
                        dynamic? publishedEquations = null;
                        int publishedEquationCount;
                        try
                        {
                            publishedEquations = publishedRange.OMaths;
                            publishedEquationCount = (int)publishedEquations.Count;
                            if (
                                afterEquationCount != beforeEquationCount
                                || publishedEquationCount != 1
                            )
                            {
                                throw new NativeToolException(
                                    "EQUATION_INVALID",
                                    "Microsoft Word did not preserve exactly one equation during point update",
                                    new
                                    {
                                        before = beforeEquationCount,
                                        after = afterEquationCount,
                                        published = publishedEquationCount,
                                    }
                                );
                            }
                            publishedEquation = publishedEquations.Item(1);
                        }
                        finally
                        {
                            FinalReleaseBatchComObject(publishedEquations);
                        }
                        var verified = VerifyPublishedEquation(
                            publishedEquation,
                            staged.Equations[0]
                        );
                        undoRecord.EndCustomRecord();
                        undoStarted = false;
                        record.Version++;
                        InvalidateSelectionGrants(record.Id);
                        InvalidateRangeGrants(record.Id);
                        InvalidateUndoGrants(record.Id);
                        _equationLearning.AddOrUpdate(
                            $"success:{operation.InputFormat}",
                            1,
                            static (_, current) => current + 1
                        );
                        finalRange = ((dynamic)verified.Equation).Range;
                        var finalStart = (int)finalRange.Start;
                        var finalEnd = (int)finalRange.End;
                        return new
                        {
                            live_document_id = record.Id,
                            live_version = record.Version,
                            equation_index = equationIndex,
                            updated = true,
                            native_verified = true,
                            readback_verified = verified.Readback is not null,
                            native_style_verified = verified.StyleVerification is not null,
                            range = new
                            {
                                start = finalStart,
                                end = finalEnd,
                            },
                            stale_equation_token_invalidated = true,
                            raw_omml_returned = false,
                            document = DocumentInfo(application, document),
                            performance = Performance(started),
                        };
                    }
                    catch (Exception exception)
                    {
                        Exception effectiveException = exception;
                        if (stagingOpen)
                        {
                            try
                            {
                                CloseStagedPreparedBatch(
                                    staged!,
                                    targetMutationAttempted: mutationAttempted,
                                    originalFailure: exception
                                );
                            }
                            catch (Exception cleanupException)
                            {
                                effectiveException = cleanupException;
                            }
                        }
                        _equationLearning.AddOrUpdate(
                            $"failure:{operation.InputFormat}",
                            1,
                            static (_, current) => current + 1
                        );
                        RollbackPreparedOperationsOrThrow(
                            document,
                            undoRecord,
                            ref undoStarted,
                            mutationAttempted,
                            rollbackSnapshot,
                            record,
                            effectiveException,
                            independentRestore: (Action)(() =>
                                RestoreLiveMainStoryFromFlatOpc(
                                    application,
                                    document,
                                    staged!.BaselineFlatOpc
                                ))
                        );
                        throw effectiveException;
                    }
                    finally
                    {
                        FinalReleaseBatchComObject(undoRecord);
                        if (originalScreenUpdating is not null)
                        {
                            application.ScreenUpdating = originalScreenUpdating.Value;
                        }
                    }
                }
                finally
                {
                    FinalReleaseBatchComObject(finalRange);
                    FinalReleaseBatchComObject(publishedEquation);
                    FinalReleaseBatchComObject(publishedRange);
                    FinalReleaseBatchComObject(targetRange);
                    FinalReleaseBatchComObject(currentEquationRange);
                    FinalReleaseBatchComObject(currentEquation);
                }
            },
            cancellationToken
        );
    }

    private static dynamic ResolveVerifiedEquation(
        dynamic document,
        LiveDocumentRecord record,
        int equationIndex,
        EquationGrant grant
    )
    {
        dynamic? equations = null;
        dynamic? equation = null;
        dynamic? range = null;
        try
        {
            equations = document.OMaths;
            var count = (int)equations.Count;
            if (
                grant.DocumentId != record.Id
                || grant.Version != record.Version
                || grant.EquationIndex != equationIndex
                || equationIndex > count
            )
            {
                throw new NativeToolException(
                    "VERSION_CONFLICT",
                    "The equation set changed after the token was issued",
                    retryable: true
                );
            }
            equation = equations.Item(equationIndex);
            range = equation.Range;
            var start = (int)range.Start;
            var end = (int)range.End;
            var semanticHash = RollbackSemanticSha256(
                (string?)range.WordOpenXML ?? ""
            );
            var contextHash = SelectionContextHash(document, start, end);
            if (
                grant.Start != start
                || grant.End != end
                || !FixedHashEquals(grant.EquationSemanticHash, semanticHash)
                || !FixedHashEquals(grant.ContextHash, contextHash)
            )
            {
                throw new NativeToolException(
                    "VERSION_CONFLICT",
                    "The native equation or its surrounding context changed after the token was issued",
                    retryable: true
                );
            }
        }
        catch
        {
            FinalReleaseBatchComObject(equation);
            throw;
        }
        finally
        {
            FinalReleaseBatchComObject(range);
            FinalReleaseBatchComObject(equations);
        }
        return equation!;
    }

    private static int ReadLiveContentEnd(dynamic document)
    {
        dynamic? content = null;
        try
        {
            content = document.Content;
            return (int)content.End;
        }
        finally
        {
            FinalReleaseBatchComObject(content);
        }
    }

    private static int ReadLiveEquationCount(dynamic owner)
    {
        dynamic? equations = null;
        try
        {
            equations = owner.OMaths;
            return (int)equations.Count;
        }
        finally
        {
            FinalReleaseBatchComObject(equations);
        }
    }

    private static bool FixedHashEquals(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(left),
                Convert.FromHexString(right)
            );
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
