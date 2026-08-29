using System.Globalization;
using System.Security.Cryptography;
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
                        (PreparedEquationOperation)operations[index],
                        expectedEquationCollectionIndex: 1
                    );
                }
                catch (Exception exception)
                {
                    throw WithFailedOperationIndex(exception, index);
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
                        failed_operation_index_available = false,
                        failure_scope = "batch",
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
                catch (Exception exception)
                {
                    throw WithFailedOperationIndex(exception, index);
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
                ["failed_operation_index"] = TryGetFailedOperationIndex(native) ?? index,
                ["raw_document_content_returned"] = false,
            };
            if (native.Details is not null)
            {
                using var document = JsonDocument.Parse(
                    JsonSerializer.Serialize(native.Details, JsonDefaults.Compact)
                );
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (
                            property.Name == "diagnostic"
                            && TryProjectEquationDiagnostic(
                                property.Value,
                                out var projectedDiagnostic
                            )
                        )
                        {
                            details[property.Name] = projectedDiagnostic;
                        }
                        else if (property.Name.Contains("fingerprint", StringComparison.OrdinalIgnoreCase)
                            || property.Name is "expected_differential_count"
                                or "actual_differential_count" or "expected_integral_differential_count"
                                or "actual_integral_differential_count" or "differential_placement_verified"
                                or "nary_count"
                                or "expected_semantic_sha256"
                                or "actual_semantic_sha256"
                                or "expected_paragraph_properties_sha256"
                                or "actual_paragraph_properties_sha256")
                        {
                            details[property.Name] = property.Value.Clone();
                        }
                        else if (
                            property.Name
                                is "expected_paragraph_justification"
                                    or "actual_paragraph_justification"
                            && property.Value.ValueKind == JsonValueKind.String
                            && property.Value.GetString()
                                is "left" or "right" or "center" or "centerGroup"
                        )
                        {
                            details[property.Name] = property.Value.GetString();
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

    private static bool TryProjectEquationDiagnostic(
        JsonElement value,
        out object? projected
    )
    {
        projected = null;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        var mismatchKind = value.TryGetProperty("mismatch_kind", out var mismatch)
            && mismatch.ValueKind == JsonValueKind.String
            ? mismatch.GetString() ?? ""
            : "";
        if (
            mismatchKind is not (
                "equation_count"
                or "canonical_structure"
                or "differential_count"
                or "differential_placement"
                or "equation_structure"
            )
        )
        {
            return false;
        }
        var result = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["mismatch_kind"] = mismatchKind,
        };
        foreach (
            var name in new[]
            {
                "expected_count",
                "actual_count",
                "first_difference_index",
                "expected_code_point",
                "actual_code_point",
            }
        )
        {
            if (
                value.TryGetProperty(name, out var count)
                && count.ValueKind == JsonValueKind.Number
                && count.TryGetInt32(out var parsed)
                && parsed is >= 0 and <= 100_000
            )
            {
                result[name] = parsed;
            }
        }
        foreach (var name in new[] { "expected_token_kind", "actual_token_kind" })
        {
            if (
                value.TryGetProperty(name, out var token)
                && token.ValueKind == JsonValueKind.String
                && token.GetString()
                    is "end" or "letter" or "digit" or "space" or "operator"
                        or "differential" or "nary" or "matrix" or "radical"
            )
            {
                result[name] = token.GetString();
            }
        }
        foreach (
            var name in new[]
            {
                "expected_code_point_window",
                "actual_code_point_window",
            }
        )
        {
            if (
                !value.TryGetProperty(name, out var window)
                || window.ValueKind != JsonValueKind.Array
                || window.GetArrayLength() > 24
            )
            {
                continue;
            }
            var projectedWindow = new List<int>();
            var valid = true;
            foreach (var item in window.EnumerateArray())
            {
                if (
                    item.ValueKind != JsonValueKind.Number
                    || !item.TryGetInt32(out var codePoint)
                    || codePoint is < 0 or > 0x10FFFF
                )
                {
                    valid = false;
                    break;
                }
                projectedWindow.Add(codePoint);
            }
            if (valid)
            {
                result[name] = projectedWindow;
            }
        }
        if (
            value.TryGetProperty("node_path", out var path)
            && path.ValueKind == JsonValueKind.String
            && path.GetString() is { Length: > 0 and <= 64 } pathText
            && pathText.StartsWith("equation", StringComparison.Ordinal)
            && pathText.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '/' or '_'
            )
        )
        {
            result["node_path"] = pathText;
        }
        foreach (var name in new[] { "expected_families", "actual_families" })
        {
            if (
                !value.TryGetProperty(name, out var families)
                || families.ValueKind != JsonValueKind.Object
            )
            {
                continue;
            }
            var projectedFamilies = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (
                var family in new[]
                {
                    "nary",
                    "fraction",
                    "superscript",
                    "subscript",
                    "radical",
                    "matrix",
                }
            )
            {
                if (
                    families.TryGetProperty(family, out var count)
                    && count.ValueKind == JsonValueKind.Number
                    && count.TryGetInt32(out var parsed)
                    && parsed is >= 0 and <= 100_000
                )
                {
                    projectedFamilies[family] = parsed;
                }
            }
            result[name] = projectedFamilies;
        }
        projected = result;
        return true;
    }

    internal static object? ProjectSafeErrorDetails(Exception exception)
    {
        if (exception is not NativeToolException { Details: not null } native)
        {
            return null;
        }
        var projected = new Dictionary<string, object?>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(native.Details, JsonDefaults.Compact)
        );
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (
                property.Name == "diagnostic"
                && TryProjectEquationDiagnostic(property.Value, out var diagnostic)
            )
            {
                projected[property.Name] = diagnostic;
            }
            else if (
                property.Name == "original_details"
                && property.Value.ValueKind == JsonValueKind.Object
            )
            {
                foreach (var nested in property.Value.EnumerateObject())
                {
                    if (
                        nested.Name == "diagnostic"
                        && TryProjectEquationDiagnostic(
                            nested.Value,
                            out var nestedDiagnostic
                        )
                    )
                    {
                        projected["diagnostic"] = nestedDiagnostic;
                    }
                    else if (
                        nested.Name.Contains(
                            "fingerprint",
                            StringComparison.OrdinalIgnoreCase
                        )
                        || nested.Name is "expected_differential_count"
                            or "actual_differential_count"
                            or "expected_integral_differential_count"
                            or "actual_integral_differential_count"
                            or "differential_placement_verified"
                            or "nary_count"
                            or "expected_semantic_sha256"
                            or "actual_semantic_sha256"
                            or "expected_paragraph_properties_sha256"
                            or "actual_paragraph_properties_sha256"
                            or "hresult"
                            or "exception_type"
                            or "stage"
                    )
                    {
                        projected[nested.Name] = nested.Value.Clone();
                    }
                }
            }
            else if (
                property.Name.Contains("fingerprint", StringComparison.OrdinalIgnoreCase)
                || property.Name
                    is "failed_operation_index"
                        or "equation_index"
                        or "equation_id"
                        or "stage"
                        or "elapsed_milliseconds"
                        or "reported_first_error_index"
                        or "reported_last_error_index"
                        or "expected_differential_count"
                        or "actual_differential_count"
                        or "expected_integral_differential_count"
                        or "actual_integral_differential_count"
                        or "differential_placement_verified"
                        or "nary_count"
                        or "operation_phase"
                        or "hresult"
                        or "exception_type"
                        or "native_error_code"
                        or "expected_semantic_sha256"
                        or "actual_semantic_sha256"
                        or "expected_paragraph_properties_sha256"
                        or "actual_paragraph_properties_sha256"
            )
            {
                projected[property.Name] = property.Value.Clone();
            }
            else if (
                property.Name
                    is "expected_paragraph_justification"
                        or "actual_paragraph_justification"
                && property.Value.ValueKind == JsonValueKind.String
                && property.Value.GetString()
                    is "left" or "right" or "center" or "centerGroup"
            )
            {
                projected[property.Name] = property.Value.GetString();
            }
        }
        projected["raw_document_content_returned"] = false;
        return projected;
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
            DirectOmmlVerification? directVerification = null;
            if (operation.DirectPlan is not null)
            {
                var actualParagraphJustification = VerifyDirectOmmlParagraphProperties(
                    equation,
                    operation.DirectPlan,
                    applyRequestedValue: false
                );
                if (readbackXml.Length == 0)
                {
                    readbackXml = ReadDirectOmmlWordOpenXml(
                        equationRange,
                        includeParagraphProperties: operation.DirectPlan.ParagraphPropertiesOmml
                            is not null
                    );
                }
                var parsed = DirectOmmlEquationParser.ParseWordReadback(readbackXml);
                if (
                    !string.Equals(
                        parsed.EquationSemanticSha256,
                        operation.DirectPlan.EquationSemanticSha256,
                        StringComparison.Ordinal
                    )
                )
                {
                    throw new NativeToolException(
                        "PUBLICATION_INVALID",
                        "Microsoft Word changed direct OMML during staged publication",
                        new
                        {
                            expected_semantic_sha256 =
                                operation.DirectPlan.EquationSemanticSha256,
                            actual_semantic_sha256 = parsed.EquationSemanticSha256,
                            expected_paragraph_properties_sha256 =
                                operation.DirectPlan.ParagraphPropertiesSemanticSha256,
                            actual_paragraph_properties_sha256 =
                                actualParagraphJustification is null
                                    ? null
                                    : operation.DirectPlan.ParagraphPropertiesSemanticSha256,
                            expected_paragraph_justification =
                                operation.DirectPlan.ParagraphJustification,
                            actual_paragraph_justification = actualParagraphJustification,
                        }
                    );
                }
                directVerification = new DirectOmmlVerification(
                    operation.DirectPlan.NamespaceIdentity,
                    operation.DirectPlan.SemanticSha256,
                    ActualCombinedSemanticSha256: null,
                    operation.DirectPlan.EquationSemanticSha256,
                    parsed.EquationSemanticSha256,
                    operation.DirectPlan.ParagraphPropertiesSemanticSha256,
                    actualParagraphJustification is null
                        ? null
                        : operation.DirectPlan.ParagraphPropertiesSemanticSha256,
                    operation.DirectPlan.ParagraphJustification,
                    actualParagraphJustification,
                    parsed.ElementCount
                );
            }
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
                styleVerification,
                directVerification
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
                "font_name_ascii" => Convert.ToString(font.NameAscii, CultureInfo.InvariantCulture) ?? "",
                "font_name_bidi" => Convert.ToString(font.NameBi, CultureInfo.InvariantCulture) ?? "",
                "font_name_far_east" => Convert.ToString(font.NameFarEast, CultureInfo.InvariantCulture) ?? "",
                "font_name_other" => Convert.ToString(font.NameOther, CultureInfo.InvariantCulture) ?? "",
                "font_size_pt" => FormatFloating(font.Size),
                "font_size_bidi_pt" => FormatFloating(font.SizeBi),
                "font_color_rgb" => FormatWordRgb(font.Color),
                "font_color_index" => FormatInteger(font.ColorIndex),
                "font_color_bidi_index" => FormatInteger(font.ColorIndexBi),
                "diacritic_color" => FormatWordColor(font.DiacriticColor),
                "bold" => FormatWordBoolean(font.Bold),
                "italic" => FormatWordBoolean(font.Italic),
                "bold_bidi" => FormatWordBoolean(font.BoldBi),
                "italic_bidi" => FormatWordBoolean(font.ItalicBi),
                "underline" => FormatUnderlineBoolean(font.Underline),
                "underline_style" => FormatEnumReadback((object)font.Underline, UnderlineStyleName),
                "underline_color" => FormatWordColor(font.UnderlineColor),
                "strike" => FormatWordBoolean(font.StrikeThrough),
                "double_strike" => FormatWordBoolean(font.DoubleStrikeThrough),
                "subscript" => FormatWordBoolean(font.Subscript),
                "superscript" => FormatWordBoolean(font.Superscript),
                "all_caps" => FormatWordBoolean(font.AllCaps),
                "small_caps" => FormatWordBoolean(font.SmallCaps),
                "hidden" => FormatWordBoolean(font.Hidden),
                "shadow" => FormatWordBoolean(font.Shadow),
                "outline" => FormatWordBoolean(font.Outline),
                "emboss" => FormatWordBoolean(font.Emboss),
                "engrave" => FormatWordBoolean(font.Engrave),
                "scaling_percent" => FormatInteger(font.Scaling),
                "spacing_pt" => FormatFloating(font.Spacing),
                "position_pt" => FormatInteger(font.Position),
                "kerning_pt" => FormatFloating(font.Kerning),
                "disable_character_space_grid" => FormatWordBoolean(font.DisableCharacterSpaceGrid),
                "emphasis_mark" => FormatEnumReadback((object)font.EmphasisMark, EmphasisMarkName),
                "ligatures" => FormatEnumReadback((object)font.Ligatures, LigaturesName),
                "number_form" => FormatEnumReadback((object)font.NumberForm, NumberFormName),
                "number_spacing" => FormatEnumReadback((object)font.NumberSpacing, NumberSpacingName),
                "stylistic_sets" => FormatStylisticSets(font.StylisticSet),
                "contextual_alternates" => FormatWordBoolean(font.ContextualAlternates),
                "clear_character_formatting" => property.Value.GetBoolean()
                    ? CaptureCharacterFormattingFingerprint(range)
                    : "0",
                "highlight_color_index" => FormatInteger(range.HighlightColorIndex),
                "paragraph_alignment" => FormatEnumReadback(
                    (object)paragraph.Alignment,
                    ParagraphAlignmentName
                ),
                "space_before_pt" => FormatFloating(paragraph.SpaceBefore),
                "space_after_pt" => FormatFloating(paragraph.SpaceAfter),
                "left_indent_pt" => FormatFloating(paragraph.LeftIndent),
                "right_indent_pt" => FormatFloating(paragraph.RightIndent),
                "first_line_indent_pt" => FormatFloating(paragraph.FirstLineIndent),
                "keep_with_next" => FormatWordBoolean(paragraph.KeepWithNext),
                "keep_together" => FormatWordBoolean(paragraph.KeepTogether),
                "page_break_before" => FormatWordBoolean(paragraph.PageBreakBefore),
                "widow_control" => FormatWordBoolean(paragraph.WidowControl),
                _ => throw new NativeToolException(
                    "INVALID_INPUT",
                    $"Unsupported formatting field: {property.Name}"
                ),
            };
        }
        return values;
    }

    private static void VerifyRequestedFormatting(dynamic range, JsonElement formatting)
    {
        Dictionary<string, string> actual = CaptureRequestedFormatting(
            (object)range,
            formatting
        );
        foreach (var property in formatting.EnumerateObject())
        {
            if (property.Name == "clear_character_formatting")
            {
                continue;
            }
            var expected = ExpectedRequestedFormatting(property);
            if (
                !actual.TryGetValue(property.Name, out var actualValue)
                || !string.Equals(expected, actualValue, StringComparison.Ordinal)
            )
            {
                throw new NativeToolException(
                    "FORMATTING_INVALID",
                    "Microsoft Word did not retain the requested character or paragraph formatting",
                    new
                    {
                        field = property.Name,
                        expected,
                        actual = actualValue ?? "missing",
                    }
                );
            }
        }
    }

    private static string ExpectedRequestedFormatting(JsonProperty property)
    {
        var value = property.Value;
        return property.Name switch
        {
            "font_name"
            or "font_name_ascii"
            or "font_name_bidi"
            or "font_name_far_east"
            or "font_name_other" => value.GetString() ?? "",
            "font_size_pt"
            or "font_size_bidi_pt"
            or "spacing_pt"
            or "kerning_pt"
            or "space_before_pt"
            or "space_after_pt"
            or "left_indent_pt"
            or "right_indent_pt"
            or "first_line_indent_pt" => FormatFloating(value.GetDouble()),
            "font_color_rgb" => FormatWordRgb(ParseWordColor(value.GetString() ?? "")),
            "font_color_index"
            or "font_color_bidi_index"
            or "highlight_color_index"
            or "scaling_percent"
            or "position_pt" => value.GetInt32().ToString(CultureInfo.InvariantCulture),
            "diacritic_color" or "underline_color" => string.Equals(
                value.GetString(),
                "automatic",
                StringComparison.Ordinal
            )
                ? "automatic"
                : (value.GetString() ?? "").ToUpperInvariant(),
            "underline" => value.GetBoolean() ? "true" : "false",
            "bold"
            or "italic"
            or "bold_bidi"
            or "italic_bidi"
            or "strike"
            or "double_strike"
            or "subscript"
            or "superscript"
            or "all_caps"
            or "small_caps"
            or "hidden"
            or "shadow"
            or "outline"
            or "emboss"
            or "engrave"
            or "disable_character_space_grid"
            or "contextual_alternates"
            or "keep_with_next"
            or "keep_together"
            or "page_break_before"
            or "widow_control" => value.GetBoolean() ? "true" : "false",
            "underline_style"
            or "emphasis_mark"
            or "ligatures"
            or "number_form"
            or "number_spacing" => value.GetString() ?? "",
            "stylistic_sets" => FormatStylisticSets(StylisticSetsMask(value)),
            "paragraph_alignment" => value.GetString() ?? "",
            _ => throw new NativeToolException(
                "INVALID_INPUT",
                $"Unsupported formatting field: {property.Name}"
            ),
        };
    }

    private static int StylisticSetsMask(JsonElement value)
    {
        var mask = 0;
        foreach (var item in value.EnumerateArray())
        {
            mask |= 1 << (item.GetInt32() - 1);
        }
        return mask;
    }

    private static string FormatWordColor(dynamic value)
    {
        var color = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        return color == unchecked((int)0xFF000000) ? "automatic" : FormatWordRgb(color);
    }

    private static string FormatWordRgb(dynamic value)
    {
        var color = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        var red = color & 0xFF;
        var green = (color >> 8) & 0xFF;
        var blue = (color >> 16) & 0xFF;
        return $"#{red:X2}{green:X2}{blue:X2}";
    }

    private static string FormatWordBoolean(dynamic value)
    {
        var number = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        return number switch
        {
            -1 => "true",
            0 => "false",
            _ => number.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static string FormatUnderlineBoolean(dynamic value)
    {
        var number = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        return number switch
        {
            1 => "true",
            0 => "false",
            _ => number.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static string FormatEnumReadback(
        object value,
        Func<int, string?> nameResolver
    )
    {
        var number = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        return nameResolver(number) ?? number.ToString(CultureInfo.InvariantCulture);
    }

    private static string? UnderlineStyleName(int value) => value switch
    {
        0 => "none",
        1 => "single",
        2 => "words",
        3 => "double",
        4 => "dotted",
        6 => "thick",
        7 => "dash",
        9 => "dot_dash",
        10 => "dot_dot_dash",
        11 => "wavy",
        20 => "dotted_heavy",
        23 => "dash_heavy",
        25 => "dot_dash_heavy",
        26 => "dot_dot_dash_heavy",
        27 => "wavy_heavy",
        39 => "dash_long",
        43 => "wavy_double",
        55 => "dash_long_heavy",
        _ => null,
    };

    private static string? EmphasisMarkName(int value) => value switch
    {
        0 => "none",
        1 => "over_solid_circle",
        2 => "over_comma",
        3 => "over_white_circle",
        4 => "under_solid_circle",
        _ => null,
    };

    private static string? LigaturesName(int value) => value switch
    {
        0 => "none",
        1 => "standard",
        2 => "contextual",
        3 => "standard_contextual",
        4 => "historical",
        5 => "standard_historical",
        6 => "contextual_historical",
        7 => "standard_contextual_historical",
        8 => "discretionary",
        9 => "standard_discretionary",
        10 => "contextual_discretionary",
        11 => "standard_contextual_discretionary",
        12 => "historical_discretionary",
        13 => "standard_historical_discretionary",
        14 => "contextual_historical_discretionary",
        15 => "all",
        _ => null,
    };

    private static string? NumberFormName(int value) => value switch
    {
        0 => "default",
        1 => "lining",
        2 => "old_style",
        _ => null,
    };

    private static string? NumberSpacingName(int value) => value switch
    {
        0 => "default",
        1 => "proportional",
        2 => "tabular",
        _ => null,
    };

    private static string? ParagraphAlignmentName(int value) => value switch
    {
        0 => "left",
        1 => "center",
        2 => "right",
        3 => "justify",
        4 => "distribute",
        _ => null,
    };

    private static string FormatStylisticSets(int mask)
    {
        var sets = Enumerable.Range(1, 20)
            .Where(index => (mask & (1 << (index - 1))) != 0)
            .ToArray();
        return JsonSerializer.Serialize(sets, JsonDefaults.Compact);
    }

    private static IReadOnlyDictionary<string, object?> PublicFormattingReadback(
        IReadOnlyDictionary<string, string> captured,
        JsonElement formatting
    )
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in formatting.EnumerateObject())
        {
            if (property.Name == "clear_character_formatting")
            {
                result[property.Name] = property.Value.GetBoolean();
                continue;
            }
            var value = captured[property.Name];
            result[property.Name] = property.Name switch
            {
                "bold"
                or "italic"
                or "bold_bidi"
                or "italic_bidi"
                or "underline"
                or "strike"
                or "double_strike"
                or "subscript"
                or "superscript"
                or "all_caps"
                or "small_caps"
                or "hidden"
                or "shadow"
                or "outline"
                or "emboss"
                or "engrave"
                or "disable_character_space_grid"
                or "contextual_alternates"
                or "keep_with_next"
                or "keep_together"
                or "page_break_before"
                or "widow_control" => bool.Parse(value),
                "font_color_index"
                or "font_color_bidi_index"
                or "highlight_color_index"
                or "scaling_percent"
                or "position_pt" => int.Parse(value, CultureInfo.InvariantCulture),
                "font_size_pt"
                or "font_size_bidi_pt"
                or "spacing_pt"
                or "kerning_pt"
                or "space_before_pt"
                or "space_after_pt"
                or "left_indent_pt"
                or "right_indent_pt"
                or "first_line_indent_pt" => double.Parse(
                    value,
                    CultureInfo.InvariantCulture
                ),
                "stylistic_sets" => JsonSerializer.Deserialize<int[]>(
                    value,
                    JsonDefaults.Compact
                ) ?? [],
                _ => value,
            };
        }
        return result;
    }

    private static string CaptureCharacterFormattingFingerprint(dynamic range)
    {
        dynamic font = range.Font;
        var components = new[]
        {
            Convert.ToString(font.Name, CultureInfo.InvariantCulture) ?? "",
            Convert.ToString(font.NameAscii, CultureInfo.InvariantCulture) ?? "",
            Convert.ToString(font.NameBi, CultureInfo.InvariantCulture) ?? "",
            Convert.ToString(font.NameFarEast, CultureInfo.InvariantCulture) ?? "",
            Convert.ToString(font.NameOther, CultureInfo.InvariantCulture) ?? "",
            FormatFloating(font.Size),
            FormatFloating(font.SizeBi),
            FormatInteger(font.Color),
            FormatInteger(font.ColorIndex),
            FormatInteger(font.ColorIndexBi),
            FormatInteger(font.DiacriticColor),
            FormatInteger(font.Bold),
            FormatInteger(font.BoldBi),
            FormatInteger(font.Italic),
            FormatInteger(font.ItalicBi),
            FormatInteger(font.Underline),
            FormatInteger(font.UnderlineColor),
            FormatInteger(font.StrikeThrough),
            FormatInteger(font.DoubleStrikeThrough),
            FormatInteger(font.Subscript),
            FormatInteger(font.Superscript),
            FormatInteger(font.AllCaps),
            FormatInteger(font.SmallCaps),
            FormatInteger(font.Hidden),
            FormatInteger(font.Shadow),
            FormatInteger(font.Outline),
            FormatInteger(font.Emboss),
            FormatInteger(font.Engrave),
            FormatInteger(font.Scaling),
            FormatFloating(font.Spacing),
            FormatInteger(font.Position),
            FormatFloating(font.Kerning),
            FormatInteger(font.DisableCharacterSpaceGrid),
            FormatInteger(font.EmphasisMark),
            FormatInteger(font.Ligatures),
            FormatInteger(font.NumberForm),
            FormatInteger(font.NumberSpacing),
            FormatInteger(font.StylisticSet),
            FormatInteger(font.ContextualAlternates),
            FormatInteger(range.HighlightColorIndex),
        };
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', components)))
            )
            .ToLowerInvariant();
    }

    internal static string ReadStyleIdentity(object rangeObject)
    {
        object? styleObject = null;
        try
        {
            dynamic range = rangeObject;
            styleObject = range.Style;
            return ReadStyleValueIdentity(styleObject);
        }
        catch
        {
            return "";
        }
        finally
        {
            FinalReleaseBatchComObject(styleObject);
        }
    }

    internal static string ReadStyleValueIdentity(object? styleObject)
    {
        if (styleObject is null)
        {
            return "";
        }
        if (styleObject is string scalarText)
        {
            return string.Equals(
                scalarText,
                "System.__ComObject",
                StringComparison.Ordinal
            )
                ? ""
                : scalarText;
        }

        dynamic style = styleObject;
        try
        {
            var nameLocal = Convert.ToString(style.NameLocal, CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(nameLocal))
            {
                return nameLocal;
            }
        }
        catch
        {
            // Older/fake Word surfaces may omit NameLocal; try Name next.
        }

        try
        {
            var name = Convert.ToString(style.Name, CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }
        }
        catch
        {
            // A scalar style value has neither NameLocal nor Name.
        }

        if (Marshal.IsComObject(styleObject))
        {
            return "";
        }

        var scalar = Convert.ToString(styleObject, CultureInfo.InvariantCulture) ?? "";
        return string.Equals(scalar, "System.__ComObject", StringComparison.Ordinal)
            ? ""
            : scalar;
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
