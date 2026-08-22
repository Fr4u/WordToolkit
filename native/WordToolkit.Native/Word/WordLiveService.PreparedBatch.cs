using System.Globalization;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.Json;
using WordToolkit.Native.Equations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static StagedPreparedBatch StagePreparedBatch(
        dynamic application,
        dynamic targetDocument,
        IReadOnlyList<PreparedOperation> operations,
        string payload,
        IReadOnlyList<(int Start, int End)> segments
    )
    {
        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-live-stage-{Guid.NewGuid():N}.xml"
        );
        dynamic? stagingDocument = null;
        dynamic? publicationRange = null;
        Exception? failure = null;
        var ownershipTransferred = false;
        var textRanges = new Dictionary<int, object>();
        var stagedEquations = new Dictionary<int, BuiltEquationResult>();
        try
        {
            var flatOpc = (string?)targetDocument.WordOpenXML ?? "";
            if (flatOpc.Length == 0)
            {
                throw new NativeToolException(
                    "STAGING_FAILED",
                    "Microsoft Word returned an empty Flat OPC snapshot for isolated batch staging"
                );
            }
            File.WriteAllText(temporaryPath, flatOpc, new UTF8Encoding(false));

            var originalAutomationSecurity = (int)application.AutomationSecurity;
            var originalUpdateLinksAtOpen = (bool)application.Options.UpdateLinksAtOpen;
            try
            {
                application.AutomationSecurity = OfficeAutomationSecurityForceDisable;
                application.Options.UpdateLinksAtOpen = false;
                stagingDocument = application.Documents.Open(
                    FileName: temporaryPath,
                    ConfirmConversions: false,
                    ReadOnly: false,
                    AddToRecentFiles: false,
                    Revert: false,
                    Visible: false,
                    OpenAndRepair: false,
                    NoEncodingDialog: true
                );
            }
            finally
            {
                application.Options.UpdateLinksAtOpen = originalUpdateLinksAtOpen;
                application.AutomationSecurity = originalAutomationSecurity;
            }

            stagingDocument.TrackRevisions = false;
            dynamic? stagingContent = null;
            int stagingStart;
            try
            {
                stagingContent = stagingDocument.Content;
                stagingContent.Text = "";
                stagingStart = (int)stagingContent.Start;
            }
            finally
            {
                FinalReleaseBatchComObject(stagingContent);
            }
            publicationRange = stagingDocument.Range(stagingStart, stagingStart);
            publicationRange.Text = payload;

            for (var index = 0; index < operations.Count; index++)
            {
                if (operations[index] is not PreparedTextOperation textOperation)
                {
                    continue;
                }
                try
                {
                    var segment = segments[index];
                    dynamic stagedTextRange = stagingDocument.Range(
                        stagingStart + segment.Start,
                        stagingStart + segment.End
                    );
                    if (textOperation.Style.Length > 0)
                    {
                        stagedTextRange.Style = textOperation.Style;
                    }
                    if (textOperation.Formatting is not null)
                    {
                        ApplyFormatting(stagedTextRange, textOperation.Formatting.Value);
                    }
                    if (textOperation.Runs.Count > 0)
                    {
                        var runOffset = 0;
                        foreach (var run in textOperation.Runs)
                        {
                            if (run.Formatting is not null)
                            {
                                dynamic runRange = stagingDocument.Range(
                                    stagedTextRange.Start + runOffset,
                                    stagedTextRange.Start + runOffset + run.Text.Length
                                );
                                try
                                {
                                    ApplyFormatting(runRange, run.Formatting.Value);
                                }
                                finally
                                {
                                    if (Marshal.IsComObject(runRange))
                                    {
                                        Marshal.FinalReleaseComObject(runRange);
                                    }
                                }
                            }
                            runOffset += run.Text.Length;
                        }
                    }
                    textRanges[index] = (object)stagedTextRange;
                }
                catch (Exception exception)
                {
                    throw WithFailedOperationIndex(exception, index);
                }
            }

            var equationIndexes = Enumerable.Range(0, operations.Count)
                .Where(index => operations[index] is PreparedEquationOperation)
                .ToArray();
            var perOperationEquationCounts = new List<int>();
            var beforeEquations = (int)publicationRange.OMaths.Count;
            for (var reverse = equationIndexes.Length - 1; reverse >= 0; reverse--)
            {
                var index = equationIndexes[reverse];
                var segment = segments[index];
                try
                {
                    stagedEquations[index] = BuildVerifiedNativeEquation(
                        stagingDocument,
                        stagingStart + segment.Start,
                        stagingStart + segment.End,
                        (PreparedEquationOperation)operations[index]
                    );
                }
                catch (Exception exception)
                {
                    throw WithFailedOperationIndex(exception, index);
                }
                var perOperationEquationCount = (int)stagingDocument.OMaths.Count;
                perOperationEquationCounts.Add(perOperationEquationCount);
                var expectedPerOperationEquationCount = beforeEquations + (equationIndexes.Length - reverse);
                if (perOperationEquationCount != expectedPerOperationEquationCount)
                {
                    throw new NativeToolException(
                        "EQUATION_INVALID",
                        "Microsoft Word changed the native equation count during isolated batch construction",
                        new { operation_index = index, before = beforeEquations, after = perOperationEquationCount, expected = expectedPerOperationEquationCount }
                    );
                }
            }
            // Reacquire after Word may expand the original range while building OMaths.
            var refreshedContentEnd = Math.Max(stagingStart, (int)stagingDocument.Content.End - 1);
            if (Marshal.IsComObject(publicationRange)) Marshal.FinalReleaseComObject(publicationRange);
            publicationRange = stagingDocument.Range(stagingStart, refreshedContentEnd);
            var afterEquations = (int)publicationRange.OMaths.Count;
            if (afterEquations != beforeEquations + equationIndexes.Length)
            {
                throw new NativeToolException(
                    "EQUATION_INVALID",
                    "Microsoft Word did not create the expected native equations in the isolated batch",
                    new
                    {
                        before = beforeEquations,
                        after = afterEquations,
                        expected = equationIndexes.Length,
                        per_operation_counts = perOperationEquationCounts,
                        target_document_mutated = false,
                    }
                );
            }

            var publicationStart = (int)publicationRange.Start;
            var publicationEnd = (int)publicationRange.End;
            var operationRanges = new StagedOperationRange[operations.Count];
            for (var index = 0; index < operations.Count; index++)
            {
                var textOperation = operations[index] as PreparedTextOperation;
                var ownsStagedRange = textOperation is null;
                object stagedRangeObject = ownsStagedRange
                    ? (object)((dynamic)stagedEquations[index].Equation).Range
                    : textRanges[index];
                dynamic stagedRange = stagedRangeObject;
                try
                {
                    var relativeStart = (int)stagedRange.Start - publicationStart;
                    var relativeEnd = (int)stagedRange.End - publicationStart;
                    if (
                        relativeStart < 0
                        || relativeEnd < relativeStart
                        || publicationStart + relativeEnd > publicationEnd
                    )
                    {
                        throw new NativeToolException(
                            "STAGING_FAILED",
                            "A staged operation escaped the isolated publication range",
                            new { operation_index = index, target_document_mutated = false }
                        );
                    }
                    var runFormatting = new List<StagedRunFormatting>();
                    if (textOperation is not null && textOperation.Runs.Count > 0)
                    {
                        var runOffset = 0;
                        foreach (var run in textOperation.Runs)
                        {
                            if (run.Formatting is not null)
                            {
                                dynamic runRange = stagingDocument.Range(
                                    stagedRange.Start + runOffset,
                                    stagedRange.Start + runOffset + run.Text.Length
                                );
                                try
                                {
                                    runFormatting.Add(
                                        new StagedRunFormatting(
                                            runOffset,
                                            runOffset + run.Text.Length,
                                            run.Formatting.Value,
                                            CaptureRequestedFormatting(
                                                (object)runRange,
                                                run.Formatting.Value
                                            )
                                        )
                                    );
                                }
                                finally
                                {
                                    FinalReleaseBatchComObject(runRange);
                                }
                            }
                            runOffset += run.Text.Length;
                        }
                    }
                    operationRanges[index] = new StagedOperationRange(
                        relativeStart,
                        relativeEnd,
                        RollbackSha256((string?)stagedRange.Text ?? ""),
                        textOperation is null || textOperation.Style.Length == 0
                            ? ""
                            : ReadStyleIdentity(stagedRange),
                        textOperation is null
                            ? new Dictionary<string, string>(StringComparer.Ordinal)
                            : CaptureRequestedFormatting(stagedRange, textOperation),
                        runFormatting
                    );
                }
                finally
                {
                    if (ownsStagedRange)
                    {
                        FinalReleaseBatchComObject(stagedRangeObject);
                    }
                }
            }

            var stagedText = (string?)publicationRange.Text ?? "";
            var staged = new StagedPreparedBatch(
                (object)stagingDocument,
                (object)publicationRange,
                temporaryPath,
                flatOpc,
                publicationEnd - publicationStart,
                RollbackSha256(stagedText),
                operationRanges,
                stagedEquations,
                equationIndexes
            );
            ownershipTransferred = true;
            stagingDocument = null;
            targetDocument.Activate();
            return staged;
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            foreach (var range in textRanges.Values)
            {
                FinalReleaseBatchComObject(range);
            }
            textRanges.Clear();
            if (!ownershipTransferred)
            {
                foreach (var stagedEquation in stagedEquations.Values)
                {
                    FinalReleaseBatchComObject(stagedEquation.Equation);
                }
            }
            Exception? cleanupFailure = null;
            if (!ownershipTransferred)
            {
                try
                {
                    CloseStagingArtifacts(
                        stagingDocument,
                        temporaryPath,
                        failure,
                        targetMutationAttempted: false,
                        publicationRange: publicationRange
                    );
                }
                catch (Exception exception)
                {
                    cleanupFailure = exception;
                }
            }
            try
            {
                targetDocument.Activate();
            }
            catch when (failure is not null || cleanupFailure is not null)
            {
                // The staging failure remains authoritative; target integrity is checked by the caller.
            }
            if (cleanupFailure is not null)
            {
                throw cleanupFailure;
            }
        }
    }

    internal static Exception WithFailedOperationIndex(Exception exception, int index)
    {
        if (exception is NativeToolException native)
        {
            var details = new Dictionary<string, object?>
            {
                ["failed_operation_index"] = index,
                ["raw_document_content_returned"] = false,
            };
            if (native.Details is not null)
            {
                using var document = JsonDocument.Parse(JsonSerializer.Serialize(native.Details));
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (property.Name.Contains("fingerprint", StringComparison.OrdinalIgnoreCase))
                        {
                            details[property.Name] = property.Value.Clone();
                        }
                    }
                }
            }
            return new NativeToolException(native.ErrorCode, native.Message, details, native.Retryable);
        }
        return new NativeToolException(
            "EXTERNAL_TOOL_FAILED",
            "Microsoft Word rejected one operation in the batch",
            new { failed_operation_index = index, raw_document_content_returned = false },
            retryable: true
        );
    }

    private static void CloseStagedPreparedBatch(
        StagedPreparedBatch staged,
        bool targetMutationAttempted,
        Exception? originalFailure = null
    )
    {
        try
        {
            foreach (var stagedEquation in staged.Equations.Values)
            {
                FinalReleaseBatchComObject(stagedEquation.Equation);
            }
        }
        finally
        {
            CloseStagingArtifacts(
                staged.Document,
                staged.TemporaryPath,
                originalFailure,
                targetMutationAttempted,
                staged.PublicationRange
            );
        }
    }

    internal static void RestoreLiveMainStoryFromFlatOpc(
        dynamic application,
        dynamic targetDocument,
        string flatOpc
    )
    {
        if (flatOpc.Length == 0)
        {
            throw new NativeToolException(
                "ROLLBACK_FAILED",
                "The independent live recovery snapshot is empty"
            );
        }
        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-live-recovery-{Guid.NewGuid():N}.xml"
        );
        dynamic? recoveryDocument = null;
        dynamic? sourceContent = null;
        dynamic? targetContent = null;
        dynamic? sourceRange = null;
        dynamic? targetRange = null;
        dynamic? sourceFormattedText = null;
        Exception? failure = null;
        var trackRevisionsRead = false;
        var originalTrackRevisions = false;
        try
        {
            File.WriteAllText(temporaryPath, flatOpc, new UTF8Encoding(false));
            var originalAutomationSecurity = (int)application.AutomationSecurity;
            var originalUpdateLinksAtOpen = (bool)application.Options.UpdateLinksAtOpen;
            try
            {
                application.AutomationSecurity = OfficeAutomationSecurityForceDisable;
                application.Options.UpdateLinksAtOpen = false;
                recoveryDocument = application.Documents.Open(
                    FileName: temporaryPath,
                    ConfirmConversions: false,
                    ReadOnly: true,
                    AddToRecentFiles: false,
                    Revert: false,
                    Visible: false,
                    OpenAndRepair: false,
                    NoEncodingDialog: true
                );
            }
            finally
            {
                application.Options.UpdateLinksAtOpen = originalUpdateLinksAtOpen;
                application.AutomationSecurity = originalAutomationSecurity;
            }

            originalTrackRevisions = (bool)targetDocument.TrackRevisions;
            trackRevisionsRead = true;
            if (originalTrackRevisions)
            {
                targetDocument.TrackRevisions = false;
            }
            sourceContent = recoveryDocument.Content;
            targetContent = targetDocument.Content;
            var sourceStart = (int)sourceContent.Start;
            var sourceEnd = Math.Max(sourceStart, (int)sourceContent.End - 1);
            var targetStart = (int)targetContent.Start;
            var targetEnd = Math.Max(targetStart, (int)targetContent.End - 1);
            sourceRange = recoveryDocument.Range(sourceStart, sourceEnd);
            targetRange = targetDocument.Range(targetStart, targetEnd);
            sourceFormattedText = sourceRange.FormattedText;
            targetRange.FormattedText = sourceFormattedText;
            if (originalTrackRevisions)
            {
                targetDocument.TrackRevisions = true;
            }
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            if (trackRevisionsRead)
            {
                try
                {
                    targetDocument.TrackRevisions = originalTrackRevisions;
                }
                catch when (failure is not null)
                {
                    // The recovery failure remains authoritative and will quarantine the handle.
                }
            }
            FinalReleaseBatchComObject(sourceFormattedText);
            FinalReleaseBatchComObject(targetRange);
            FinalReleaseBatchComObject(sourceRange);
            FinalReleaseBatchComObject(sourceContent);
            FinalReleaseBatchComObject(targetContent);
            CloseStagingArtifacts(
                recoveryDocument,
                temporaryPath,
                failure,
                targetMutationAttempted: true
            );
            try
            {
                targetDocument.Activate();
            }
            catch when (failure is not null)
            {
                // The rollback verifier will report the unavailable live state.
            }
        }
    }

    private static void CloseStagingArtifacts(
        object? stagingDocument,
        string temporaryPath,
        Exception? originalFailure,
        bool targetMutationAttempted,
        object? publicationRange = null
    )
    {
        Exception? closeFailure = null;
        Exception? deleteFailure = null;
        if (stagingDocument is not null)
        {
            try
            {
                ((dynamic)stagingDocument).Close(WordDoNotSaveChanges);
            }
            catch (Exception exception)
            {
                closeFailure = exception;
            }
        }
        FinalReleaseBatchComObject(publicationRange);
        FinalReleaseBatchComObject(stagingDocument);
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (Exception exception)
        {
            deleteFailure = exception;
        }
        if (closeFailure is null && deleteFailure is null)
        {
            return;
        }
        var originalErrorCode = originalFailure is NativeToolException nativeFailure
            ? nativeFailure.ErrorCode
            : originalFailure is null
                ? null
                : "EXTERNAL_TOOL_FAILED";
        var details = new Dictionary<string, object?>
        {
            ["original_error_code"] = originalErrorCode,
            ["close_failed"] = closeFailure is not null,
            ["delete_failed"] = deleteFailure is not null,
            ["target_mutation_attempted"] = targetMutationAttempted,
            ["raw_document_content_returned"] = false,
        };
        if (TryGetFailedOperationIndex(originalFailure) is int failedOperationIndex)
        {
            details["failed_operation_index"] = failedOperationIndex;
        }
        throw new NativeToolException(
            "STAGING_CLEANUP_FAILED",
            "WordToolkit could not prove deletion of the isolated live-batch staging artifact",
            details
        );
    }

    internal static int? TryGetFailedOperationIndex(Exception? exception)
    {
        if (exception is not NativeToolException { Details: not null } native)
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(native.Details));
            if (
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(
                    "failed_operation_index",
                    out var failedOperationIndex
                )
                && failedOperationIndex.TryGetInt32(out var index)
                && index >= 0
            )
            {
                return index;
            }
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        return null;
    }

    private void EnsureTargetUnchangedBeforePublication(
        dynamic document,
        LiveRollbackSnapshot baseline,
        LiveDocumentRecord record,
        Exception? originalFailure = null
    )
    {
        LiveRollbackSnapshot? observed = null;
        Exception? verificationFailure = null;
        try
        {
            observed = CaptureLiveRollbackSnapshot(
                document,
                baseline.TargetStart,
                baseline.TargetEnd,
                baseline.LiveVersion
            );
        }
        catch (Exception exception)
        {
            verificationFailure = exception;
        }
        var differences = observed is null
            ? new[] { "prepublication_snapshot_unavailable" }
            : PrePublicationDifferences(baseline, observed);
        if (verificationFailure is null && differences.Length == 0)
        {
            return;
        }

        QuarantineLiveDocument(record, "STAGING_TARGET_DRIFT");
        var originalErrorCode = originalFailure is NativeToolException nativeFailure
            ? nativeFailure.ErrorCode
            : originalFailure is null
                ? "STAGING_TARGET_DRIFT"
                : "EXTERNAL_TOOL_FAILED";
        throw new NativeToolException(
            "STAGING_TARGET_DRIFT",
            "The semantic content or structure of the target Word document changed during isolated staging; publication was refused and the live handle was quarantined",
            new
            {
                live_document_id = record.Id,
                live_version_before = baseline.LiveVersion,
                stage = "prepublication",
                original_error_code = originalErrorCode,
                undo_attempted = false,
                rollback_verification_failed = verificationFailure is not null,
                differences,
                baseline = baseline.StructuralSummary(),
                observed = observed?.StructuralSummary(),
                handle_invalidated = true,
                document_quarantined = true,
                requires_explicit_disconnect = true,
                raw_document_content_returned = false,
            }
        );
    }

    internal static string[] PrePublicationDifferences(
        LiveRollbackSnapshot baseline,
        LiveRollbackSnapshot observed
    )
    {
        // Word may rewrite volatile package/session XML while an isolated staging
        // document is opened and closed. Raw Flat OPC and range hashes therefore
        // cannot decide whether the user's document changed. Publication is gated
        // by the stable semantic package hash, visible text, exact boundaries and
        // object counts instead. Rollback verification remains stricter after an
        // actual target mutation.
        return baseline.RecoveryDifferences(observed).ToArray();
    }

    private static BuiltEquationResult VerifyPublishedEquation(
        dynamic equation,
        BuiltEquationResult stagedEquation
    )
    {
        var operation = stagedEquation.Operation;
        var expectedType = operation.Display ? 0 : 1;
        if ((int)equation.Type != expectedType)
        {
            throw new NativeToolException(
                "EQUATION_INVALID",
                "Microsoft Word changed the native equation layout during staged publication",
                new { expected_type = expectedType, actual_type = (int)equation.Type }
            );
        }

        dynamic? equationRange = null;
        try
        {
            equationRange = equation.Range;
            string readbackXml = "";
            EquationStyleVerification? styleVerification = null;
            if (stagedEquation.StyleRewrite is not null)
            {
                readbackXml = (string?)equationRange.WordOpenXML ?? "";
                styleVerification = EquationStyleRewriter.Verify(
                    readbackXml,
                    stagedEquation.StyleRewrite
                );
            }
            EquationReadbackVerification? readback = null;
            if (operation.VerifyReadback)
            {
                if (readbackXml.Length == 0)
                {
                    readbackXml = (string?)equationRange.WordOpenXML ?? "";
                }
                readback = EquationReadbackVerifier.Verify(readbackXml, operation.Linear);
            }
            return new BuiltEquationResult(
                (object)equation,
                operation,
                readback,
                stagedEquation.StyleRewrite,
                styleVerification
            );
        }
        finally
        {
            FinalReleaseBatchComObject(equationRange);
        }
    }

    private static void VerifyPublishedTextOperation(
        dynamic publishedRange,
        PreparedTextOperation operation,
        StagedOperationRange expected,
        int operationIndex
    )
    {
        if (
            !string.Equals(
                expected.TextSha256,
                RollbackSha256((string?)publishedRange.Text ?? ""),
                StringComparison.Ordinal
            )
        )
        {
            throw new NativeToolException(
                "PUBLICATION_INVALID",
                "Microsoft Word changed staged text during publication",
                new { operation_index = operationIndex, raw_text_returned = false }
            );
        }
        if (
            operation.Style.Length > 0
            && !string.Equals(
                expected.StyleIdentity,
                ReadStyleIdentity(publishedRange),
                StringComparison.Ordinal
            )
        )
        {
            throw new NativeToolException(
                "PUBLICATION_INVALID",
                "Microsoft Word changed the staged paragraph style during publication",
                new { operation_index = operationIndex, style = operation.Style }
            );
        }
        var actualFormatting = CaptureRequestedFormatting(
            (object)publishedRange,
            operation
        );
        foreach (var pair in expected.Formatting)
        {
            if (
                !actualFormatting.TryGetValue(pair.Key, out var actual)
                || !string.Equals(pair.Value, actual, StringComparison.Ordinal)
            )
            {
                throw new NativeToolException(
                    "PUBLICATION_INVALID",
                    "Microsoft Word changed staged formatting during publication",
                new { operation_index = operationIndex, field = pair.Key }
                );
            }
        }
        foreach (var run in expected.RunFormatting)
        {
            dynamic runRange = publishedRange.Document.Range(
                publishedRange.Start + run.Start,
                publishedRange.Start + run.End
            );
            Dictionary<string, string> actualRunFormatting;
            try
            {
                actualRunFormatting = CaptureRequestedFormatting(
                    (object)runRange,
                    run.Requested
                );
            }
            finally
            {
                if (Marshal.IsComObject(runRange))
                {
                    Marshal.FinalReleaseComObject(runRange);
                }
            }
            foreach (var pair in run.Expected)
            {
                if (
                    !actualRunFormatting.TryGetValue(pair.Key, out var actual)
                    || !string.Equals(pair.Value, actual, StringComparison.Ordinal)
                )
                {
                    throw new NativeToolException(
                        "PUBLICATION_INVALID",
                        "Microsoft Word changed staged run formatting during publication",
                        new
                        {
                            operation_index = operationIndex,
                            field = pair.Key,
                            run_start = run.Start,
                            run_end = run.End,
                        }
                    );
                }
            }
        }
    }

    private static Dictionary<string, string> CaptureRequestedFormatting(
        dynamic range,
        PreparedTextOperation operation
    ) => operation.Formatting is null
        ? new Dictionary<string, string>(StringComparer.Ordinal)
        : CaptureRequestedFormatting(range, operation.Formatting.Value);

    private static Dictionary<string, string> CaptureRequestedFormatting(
        dynamic range,
        JsonElement formatting
    )
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        dynamic font = range.Font;
        dynamic paragraph = range.ParagraphFormat;
        foreach (var property in formatting.EnumerateObject())
        {
            values[property.Name] = property.Name switch
            {
                "font_name" => Convert.ToString(font.Name, CultureInfo.InvariantCulture) ?? "",
                "font_size_pt" => FormatFloating(font.Size),
                "font_color_rgb" => FormatInteger(font.Color),
                "bold" => FormatInteger(font.Bold),
                "italic" => FormatInteger(font.Italic),
                "underline" => FormatInteger(font.Underline),
                "strike" => FormatInteger(font.StrikeThrough),
                "all_caps" => FormatInteger(font.AllCaps),
                "small_caps" => FormatInteger(font.SmallCaps),
                "hidden" => FormatInteger(font.Hidden),
                "paragraph_alignment" => FormatInteger(paragraph.Alignment),
                "space_before_pt" => FormatFloating(paragraph.SpaceBefore),
                "space_after_pt" => FormatFloating(paragraph.SpaceAfter),
                "left_indent_pt" => FormatFloating(paragraph.LeftIndent),
                "right_indent_pt" => FormatFloating(paragraph.RightIndent),
                "first_line_indent_pt" => FormatFloating(paragraph.FirstLineIndent),
                "keep_with_next" => FormatInteger(paragraph.KeepWithNext),
                "keep_together" => FormatInteger(paragraph.KeepTogether),
                "page_break_before" => FormatInteger(paragraph.PageBreakBefore),
                "widow_control" => FormatInteger(paragraph.WidowControl),
                _ => throw new NativeToolException(
                    "INVALID_INPUT",
                    $"Unsupported formatting field: {property.Name}"
                ),
            };
        }
        return values;
    }

    private static string ReadStyleIdentity(dynamic range)
    {
        dynamic style = range.Style;
        try
        {
            return (string?)style.NameLocal ?? "";
        }
        catch
        {
            try
            {
                return (string?)style.Name ?? "";
            }
            catch
            {
                return Convert.ToString((object?)style, CultureInfo.InvariantCulture) ?? "";
            }
        }
    }

    private static string FormatInteger(dynamic value) =>
        Convert.ToInt32(value, CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture);

    private static string FormatFloating(dynamic value) =>
        Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture), 3)
            .ToString("0.###", CultureInfo.InvariantCulture);

    private sealed record StagedPreparedBatch(
        object Document,
        object PublicationRange,
        string TemporaryPath,
        string BaselineFlatOpc,
        int PublicationLength,
        string TextSha256,
        IReadOnlyList<StagedOperationRange> OperationRanges,
        IReadOnlyDictionary<int, BuiltEquationResult> Equations,
        IReadOnlyList<int> EquationIndexes
    );

    private sealed record StagedOperationRange(
        int Start,
        int End,
        string TextSha256,
        string StyleIdentity,
        IReadOnlyDictionary<string, string> Formatting,
        IReadOnlyList<StagedRunFormatting> RunFormatting
    );

    private sealed record StagedRunFormatting(
        int Start,
        int End,
        JsonElement Requested,
        IReadOnlyDictionary<string, string> Expected
    );
}
