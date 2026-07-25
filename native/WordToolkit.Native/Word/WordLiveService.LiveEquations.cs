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
                var total = (int)document.OMaths.Count;
                var returned = Math.Min(limit, Math.Max(0, total - offset));
                var items = new object[returned];
                for (var itemIndex = 0; itemIndex < returned; itemIndex++)
                {
                    var equationIndex = offset + itemIndex + 1;
                    dynamic equation = document.OMaths.Item(equationIndex);
                    dynamic range = equation.Range;
                    var start = (int)range.Start;
                    var end = (int)range.End;
                    var wordOpenXml = (string?)range.WordOpenXML ?? "";
                    var semanticHash = RollbackSemanticSha256(wordOpenXml);
                    var contextHash = SelectionContextHash(document, start, end);
                    var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
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
                        text_preview_truncated = includeTextPreview && text.Length > 256
                            ? true
                            : (bool?)null,
                        equation_token = token,
                        token_live_version = record.Version,
                        raw_omml_returned = false,
                    };
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
                dynamic currentEquation = ResolveVerifiedEquation(
                    document,
                    record,
                    equationIndex,
                    grant
                );
                dynamic targetRange = currentEquation.Range.Duplicate;
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
                                targetMutationAttempted: false
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

                var beforeContentEnd = (int)document.Content.End;
                var beforeEquationCount = (int)document.OMaths.Count;
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
                    targetRange.FormattedText = ((dynamic)staged!.Equations[0].Equation)
                        .Range.FormattedText;

                    CloseStagedPreparedBatch(staged, targetMutationAttempted: true);
                    stagingOpen = false;
                    document.Activate();

                    var afterContentEnd = (int)document.Content.End;
                    var publishedLength = afterContentEnd - beforeContentEnd + replacedLength;
                    dynamic publishedRange = document.Range(
                        targetStart,
                        targetStart + publishedLength
                    );
                    if (
                        (int)document.OMaths.Count != beforeEquationCount
                        || (int)publishedRange.OMaths.Count != 1
                    )
                    {
                        throw new NativeToolException(
                            "EQUATION_INVALID",
                            "Microsoft Word did not preserve exactly one equation during point update",
                            new
                            {
                                before = beforeEquationCount,
                                after = (int)document.OMaths.Count,
                                published = (int)publishedRange.OMaths.Count,
                            }
                        );
                    }
                    dynamic publishedEquation = publishedRange.OMaths.Item(1);
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
                    dynamic finalRange = ((dynamic)verified.Equation).Range;
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
                            start = (int)finalRange.Start,
                            end = (int)finalRange.End,
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
                                targetMutationAttempted: mutationAttempted
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
                    if (originalScreenUpdating is not null)
                    {
                        application.ScreenUpdating = originalScreenUpdating.Value;
                    }
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
        var count = (int)document.OMaths.Count;
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
        dynamic equation = document.OMaths.Item(equationIndex);
        dynamic range = equation.Range;
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
        return equation;
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
