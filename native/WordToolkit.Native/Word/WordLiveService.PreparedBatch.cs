using System.Globalization;
using System.Text;
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
        Exception? failure = null;
        var ownershipTransferred = false;
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
            dynamic stagingContent = stagingDocument.Content;
            stagingContent.Text = "";
            var stagingStart = (int)stagingContent.Start;
            dynamic publicationRange = stagingDocument.Range(stagingStart, stagingStart);
            publicationRange.Text = payload;

            var textRanges = new Dictionary<int, object>();
            for (var index = 0; index < operations.Count; index++)
            {
                if (operations[index] is not PreparedTextOperation textOperation)
                {
                    continue;
                }
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
                textRanges[index] = (object)stagedTextRange;
            }

            var equationIndexes = Enumerable.Range(0, operations.Count)
                .Where(index => operations[index] is PreparedEquationOperation)
                .ToArray();
            var stagedEquations = new Dictionary<int, BuiltEquationResult>();
            var beforeEquations = (int)publicationRange.OMaths.Count;
            for (var reverse = equationIndexes.Length - 1; reverse >= 0; reverse--)
            {
                var index = equationIndexes[reverse];
                var segment = segments[index];
                stagedEquations[index] = BuildVerifiedNativeEquation(
                    stagingDocument,
                    stagingStart + segment.Start,
                    stagingStart + segment.End,
                    (PreparedEquationOperation)operations[index]
                );
            }
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
                        target_document_mutated = false,
                    }
                );
            }

            var publicationStart = (int)publicationRange.Start;
            var publicationEnd = (int)publicationRange.End;
            var operationRanges = new StagedOperationRange[operations.Count];
            for (var index = 0; index < operations.Count; index++)
            {
                dynamic stagedRange = operations[index] is PreparedTextOperation
                    ? textRanges[index]
                    : ((dynamic)stagedEquations[index].Equation).Range;
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
                var textOperation = operations[index] as PreparedTextOperation;
                operationRanges[index] = new StagedOperationRange(
                    relativeStart,
                    relativeEnd,
                    RollbackSha256((string?)stagedRange.Text ?? ""),
                    textOperation is null || textOperation.Style.Length == 0
                        ? ""
                        : ReadStyleIdentity(stagedRange),
                    textOperation is null
                        ? new Dictionary<string, string>(StringComparer.Ordinal)
                        : CaptureRequestedFormatting(stagedRange, textOperation)
                );
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
            Exception? cleanupFailure = null;
            if (!ownershipTransferred)
            {
                try
                {
                    CloseStagingArtifacts(
                        stagingDocument,
                        temporaryPath,
                        failure,
                        targetMutationAttempted: false
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

    private static void CloseStagedPreparedBatch(
        StagedPreparedBatch staged,
        bool targetMutationAttempted
    )
    {
        CloseStagingArtifacts(
            staged.Document,
            staged.TemporaryPath,
            null,
            targetMutationAttempted
        );
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
            dynamic sourceContent = recoveryDocument.Content;
            dynamic targetContent = targetDocument.Content;
            var sourceStart = (int)sourceContent.Start;
            var sourceEnd = Math.Max(sourceStart, (int)sourceContent.End - 1);
            var targetStart = (int)targetContent.Start;
            var targetEnd = Math.Max(targetStart, (int)targetContent.End - 1);
            dynamic sourceRange = recoveryDocument.Range(sourceStart, sourceEnd);
            dynamic targetRange = targetDocument.Range(targetStart, targetEnd);
            targetRange.FormattedText = sourceRange.FormattedText;
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
        bool targetMutationAttempted
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
        throw new NativeToolException(
            "STAGING_CLEANUP_FAILED",
            "WordToolkit could not prove deletion of the isolated live-batch staging artifact",
            new
            {
                original_error_code = originalErrorCode,
                close_failed = closeFailure is not null,
                delete_failed = deleteFailure is not null,
                target_mutation_attempted = targetMutationAttempted,
                raw_document_content_returned = false,
            }
        );
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

        string readbackXml = "";
        EquationStyleVerification? styleVerification = null;
        if (stagedEquation.StyleRewrite is not null)
        {
            readbackXml = (string?)equation.Range.WordOpenXML ?? "";
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
                readbackXml = (string?)equation.Range.WordOpenXML ?? "";
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
    }

    private static Dictionary<string, string> CaptureRequestedFormatting(
        dynamic range,
        PreparedTextOperation operation
    )
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (operation.Formatting is null)
        {
            return values;
        }
        dynamic font = range.Font;
        dynamic paragraph = range.ParagraphFormat;
        foreach (var property in operation.Formatting.Value.EnumerateObject())
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
        IReadOnlyDictionary<string, string> Formatting
    );
}
