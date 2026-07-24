using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private const int RollbackContextCharacters = 256;
    private const int RollbackStoryRangeLimit = 4_096;

    internal static LiveRollbackSnapshot CaptureLiveRollbackSnapshot(
        dynamic document,
        long liveVersion
    )
    {
        dynamic content = document.Content;
        return CaptureLiveRollbackSnapshot(
            document,
            (int)content.Start,
            (int)content.End,
            liveVersion
        );
    }

    private static LiveRollbackSnapshot CaptureLiveRollbackSnapshot(
        dynamic document,
        int requestedTargetStart,
        int requestedTargetEnd,
        long liveVersion
    )
    {
        var strictComReadback = Marshal.IsComObject((object)document);
        dynamic content = document.Content;
        var contentStart = (int)content.Start;
        var contentEnd = (int)content.End;
        var targetStart = Math.Clamp(requestedTargetStart, contentStart, contentEnd);
        var targetEnd = Math.Clamp(requestedTargetEnd, targetStart, contentEnd);
        var contextStart = Math.Max(contentStart, targetStart - RollbackContextCharacters);
        var contextEnd = Math.Min(contentEnd, targetEnd + RollbackContextCharacters);
        dynamic targetRange = document.Range(targetStart, targetEnd);
        dynamic contextRange = document.Range(contextStart, contextEnd);

        return new LiveRollbackSnapshot(
            liveVersion,
            RollbackSaved(document, strictComReadback),
            contentStart,
            contentEnd,
            targetStart,
            targetEnd,
            contextStart,
            contextEnd,
            RollbackCount((object)document, strictComReadback, static value => (int)value.Paragraphs.Count, DocumentParagraphCount),
            RollbackCount((object)document, strictComReadback, static value => (int)value.OMaths.Count, DocumentEquationCount),
            RollbackCount((object)document, strictComReadback, static value => (int)value.Tables.Count, DocumentTableCount),
            RollbackCount((object)document, strictComReadback, static value => (int)value.Fields.Count, DocumentFieldCount),
            RollbackCount((object)document, strictComReadback, static value => (int)value.Bookmarks.Count, DocumentBookmarkCount),
            RollbackCount((object)document, strictComReadback, static value => (int)value.InlineShapes.Count, DocumentInlineShapeCount),
            RollbackCount((object)document, strictComReadback, static value => (int)value.Shapes.Count, DocumentShapeCount),
            RollbackCount((object)document, strictComReadback, static value => (int)value.Comments.Count, DocumentCommentCount),
            RollbackCount((object)document, strictComReadback, static value => (int)value.Footnotes.Count, DocumentFootnoteCount),
            RollbackCount((object)document, strictComReadback, static value => (int)value.Endnotes.Count, DocumentEndnoteCount),
            RollbackCount((object)document, strictComReadback, static value => (int)value.Sections.Count, DocumentSectionCount),
            RollbackSha256(RollbackDocumentWordOpenXml(document, content, strictComReadback)),
            RollbackSha256((string?)content.Text ?? ""),
            RollbackSha256(RollbackWordOpenXml(content, strictComReadback)),
            RollbackSha256((string?)targetRange.Text ?? ""),
            RollbackSha256(RollbackWordOpenXml(targetRange, strictComReadback)),
            RollbackSha256((string?)contextRange.Text ?? ""),
            RollbackSha256(RollbackWordOpenXml(contextRange, strictComReadback)),
            CaptureRollbackStoryDigest(document, strictComReadback)
        );
    }

    private static string RollbackDocumentWordOpenXml(
        dynamic document,
        dynamic content,
        bool strictComReadback
    )
    {
        try
        {
            return (string?)document.WordOpenXML ?? "";
        }
        catch when (!strictComReadback)
        {
            return "test-double-document:" + RollbackWordOpenXml(content, false);
        }
    }

    private static bool RollbackSaved(dynamic document, bool strictComReadback)
    {
        try
        {
            return (bool)document.Saved;
        }
        catch when (!strictComReadback)
        {
            return DocumentSaved(document);
        }
    }

    private static int RollbackCount(
        object document,
        bool strictComReadback,
        Func<dynamic, int> strictReader,
        Func<dynamic, int> testDoubleFallback
    )
    {
        try
        {
            return strictReader((dynamic)document);
        }
        catch when (!strictComReadback)
        {
            return testDoubleFallback((dynamic)document);
        }
    }

    private static string RollbackWordOpenXml(dynamic range, bool strictComReadback)
    {
        try
        {
            return (string?)range.WordOpenXML ?? "";
        }
        catch when (!strictComReadback)
        {
            return "test-double-text:" + ((string?)range.Text ?? "");
        }
    }

    private static RollbackStoryDigest CaptureRollbackStoryDigest(
        dynamic document,
        bool strictComReadback
    )
    {
        if (!strictComReadback)
        {
            return new RollbackStoryDigest(0, RollbackSha256("test-double-stories"));
        }

        var records = new List<RollbackStoryRecord>();
        foreach (dynamic firstRange in document.StoryRanges)
        {
            dynamic? current = firstRange;
            var linkIndex = 0;
            while (current is not null)
            {
                if (records.Count >= RollbackStoryRangeLimit)
                {
                    throw new NativeToolException(
                        "LIMIT_EXCEEDED",
                        "The Word story graph exceeds the verified rollback range limit",
                        new
                        {
                            limit = RollbackStoryRangeLimit,
                            stage = "rollback_checkpoint",
                        }
                    );
                }
                records.Add(
                    new RollbackStoryRecord(
                        (int)current.StoryType,
                        linkIndex,
                        (int)current.Start,
                        (int)current.End,
                        RollbackSha256((string?)current.Text ?? ""),
                        RollbackSha256(RollbackWordOpenXml(current, strictComReadback))
                    )
                );
                current = current.NextStoryRange;
                linkIndex++;
            }
        }

        var digest = new StringBuilder(records.Count * 192);
        foreach (
            var record in records.OrderBy(static value => value.StoryType)
                .ThenBy(static value => value.LinkIndex)
        )
        {
            digest.Append(record.StoryType).Append(':')
                .Append(record.LinkIndex).Append(':')
                .Append(record.Start).Append(':')
                .Append(record.End).Append(':')
                .Append(record.TextSha256).Append(':')
                .Append(record.WordOpenXmlSha256).Append('\n');
        }
        return new RollbackStoryDigest(records.Count, RollbackSha256(digest.ToString()));
    }

    internal void RollbackPreparedOperationsOrThrow(
        dynamic document,
        dynamic? undoRecord,
        ref bool undoStarted,
        bool mutationAttempted,
        LiveRollbackSnapshot baseline,
        LiveDocumentRecord record,
        Exception originalException,
        string? supplementalBaseline = null,
        Func<string>? supplementalStateReader = null,
        string supplementalDifferenceName = "supplemental_state_sha256"
    )
    {
        if (!mutationAttempted)
        {
            return;
        }

        LiveRollbackSnapshot? beforeUndo = null;
        Exception? beforeUndoVerificationError = null;
        string? beforeUndoSupplemental = null;
        Exception? beforeUndoSupplementalError = null;
        try
        {
            beforeUndo = CaptureLiveRollbackSnapshot(
                document,
                baseline.TargetStart,
                baseline.TargetEnd,
                baseline.LiveVersion
            );
        }
        catch (Exception exception)
        {
            beforeUndoVerificationError = exception;
        }
        if (supplementalStateReader is not null)
        {
            try
            {
                beforeUndoSupplemental = supplementalStateReader();
            }
            catch (Exception exception)
            {
                beforeUndoSupplementalError = exception;
            }
        }

        Exception? endRecordError = null;
        if (undoStarted)
        {
            try
            {
                if (undoRecord is null)
                {
                    throw new InvalidOperationException(
                        "The custom Word Undo record is unavailable"
                    );
                }
                undoRecord.EndCustomRecord();
            }
            catch (Exception exception)
            {
                endRecordError = exception;
            }
            finally
            {
                undoStarted = false;
            }
        }

        var changedBeforeUndo =
            beforeUndo is null
            || baseline.Differences(beforeUndo).Count > 0
            || beforeUndoSupplementalError is not null
            || !string.Equals(
                supplementalBaseline,
                beforeUndoSupplemental,
                StringComparison.Ordinal
            );
        if (endRecordError is null && !changedBeforeUndo)
        {
            record.Version = baseline.LiveVersion;
            return;
        }

        var undoAttempted = false;
        bool? undoReturned = null;
        Exception? undoError = null;
        if (changedBeforeUndo && endRecordError is null)
        {
            undoAttempted = true;
            try
            {
                undoReturned = (bool)document.Undo(1);
            }
            catch (Exception exception)
            {
                undoError = exception;
            }
        }

        LiveRollbackSnapshot? afterUndo = changedBeforeUndo ? null : beforeUndo;
        Exception? afterUndoVerificationError = null;
        string? afterUndoSupplemental = changedBeforeUndo ? null : beforeUndoSupplemental;
        Exception? afterUndoSupplementalError = null;
        if (changedBeforeUndo && undoAttempted)
        {
            try
            {
                afterUndo = CaptureLiveRollbackSnapshot(
                    document,
                    baseline.TargetStart,
                    baseline.TargetEnd,
                    baseline.LiveVersion
                );
            }
            catch (Exception exception)
            {
                afterUndoVerificationError = exception;
            }
            if (supplementalStateReader is not null)
            {
                try
                {
                    afterUndoSupplemental = supplementalStateReader();
                }
                catch (Exception exception)
                {
                    afterUndoSupplementalError = exception;
                }
            }
        }

        var differences = (afterUndo is null
            ? new[] { "rollback_snapshot_unavailable" }
            : baseline.Differences(afterUndo).ToArray()).ToList();
        if (
            supplementalStateReader is not null
            && (
                afterUndoSupplementalError is not null
                || !string.Equals(
                    supplementalBaseline,
                    afterUndoSupplemental,
                    StringComparison.Ordinal
                )
            )
        )
        {
            differences.Add(supplementalDifferenceName);
        }
        if (
            endRecordError is null
            && undoAttempted
            && undoError is null
            && undoReturned is true
            && afterUndo is not null
            && differences.Count == 0
        )
        {
            record.Version = baseline.LiveVersion;
            return;
        }

        QuarantineLiveDocument(record);
        var originalErrorCode = originalException is NativeToolException nativeException
            ? nativeException.ErrorCode
            : "EXTERNAL_TOOL_FAILED";
        throw new NativeToolException(
            "ROLLBACK_FAILED",
            "Microsoft Word did not prove restoration of the exact pre-transaction state; the live document handle was quarantined",
            new
            {
                live_document_id = record.Id,
                live_version_before = baseline.LiveVersion,
                original_error_code = originalErrorCode,
                original_exception_type = originalException.GetType().Name,
                undo_record_end_failed = endRecordError is not null,
                undo_attempted = undoAttempted,
                undo_returned = undoReturned,
                undo_failed = undoError is not null,
                rollback_verification_failed = afterUndoVerificationError is not null
                    || afterUndoSupplementalError is not null,
                pre_undo_verification_failed = beforeUndoVerificationError is not null
                    || beforeUndoSupplementalError is not null,
                differences,
                baseline = baseline.StructuralSummary(),
                observed = afterUndo?.StructuralSummary(),
                handle_invalidated = true,
                document_quarantined = true,
                requires_explicit_disconnect = true,
                raw_document_content_returned = false,
            }
        );
    }

    private void QuarantineLiveDocument(LiveDocumentRecord record)
    {
        _records.TryRemove(record.Id, out _);
        _quarantinedRecords[record.Id] = new QuarantinedLiveDocumentRecord(
            record.Id,
            record.Name,
            record.FullName
        );
        InvalidateSelectionGrants(record.Id);
        InvalidateRangeGrants(record.Id);
        InvalidateUndoGrants(record.Id);
    }

    private static string RollbackSha256(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }
}

internal sealed record LiveRollbackSnapshot(
    long LiveVersion,
    bool Saved,
    int ContentStart,
    int ContentEnd,
    int TargetStart,
    int TargetEnd,
    int ContextStart,
    int ContextEnd,
    int ParagraphCount,
    int EquationCount,
    int TableCount,
    int FieldCount,
    int BookmarkCount,
    int InlineShapeCount,
    int ShapeCount,
    int CommentCount,
    int FootnoteCount,
    int EndnoteCount,
    int SectionCount,
    string DocumentWordOpenXmlSha256,
    string ContentTextSha256,
    string ContentWordOpenXmlSha256,
    string TargetTextSha256,
    string TargetWordOpenXmlSha256,
    string ContextTextSha256,
    string ContextWordOpenXmlSha256,
    RollbackStoryDigest StoryDigest
)
{
    public IReadOnlyList<string> Differences(LiveRollbackSnapshot observed)
    {
        var differences = new List<string>();
        AddDifference(differences, "saved", Saved, observed.Saved);
        AddDifference(differences, "content_start", ContentStart, observed.ContentStart);
        AddDifference(differences, "content_end", ContentEnd, observed.ContentEnd);
        AddDifference(differences, "target_start", TargetStart, observed.TargetStart);
        AddDifference(differences, "target_end", TargetEnd, observed.TargetEnd);
        AddDifference(differences, "context_start", ContextStart, observed.ContextStart);
        AddDifference(differences, "context_end", ContextEnd, observed.ContextEnd);
        AddDifference(differences, "paragraph_count", ParagraphCount, observed.ParagraphCount);
        AddDifference(differences, "equation_count", EquationCount, observed.EquationCount);
        AddDifference(differences, "table_count", TableCount, observed.TableCount);
        AddDifference(differences, "field_count", FieldCount, observed.FieldCount);
        AddDifference(differences, "bookmark_count", BookmarkCount, observed.BookmarkCount);
        AddDifference(
            differences,
            "inline_shape_count",
            InlineShapeCount,
            observed.InlineShapeCount
        );
        AddDifference(differences, "shape_count", ShapeCount, observed.ShapeCount);
        AddDifference(differences, "comment_count", CommentCount, observed.CommentCount);
        AddDifference(differences, "footnote_count", FootnoteCount, observed.FootnoteCount);
        AddDifference(differences, "endnote_count", EndnoteCount, observed.EndnoteCount);
        AddDifference(differences, "section_count", SectionCount, observed.SectionCount);
        AddDifference(
            differences,
            "document_word_open_xml_sha256",
            DocumentWordOpenXmlSha256,
            observed.DocumentWordOpenXmlSha256
        );
        AddDifference(
            differences,
            "content_text_sha256",
            ContentTextSha256,
            observed.ContentTextSha256
        );
        AddDifference(
            differences,
            "content_word_open_xml_sha256",
            ContentWordOpenXmlSha256,
            observed.ContentWordOpenXmlSha256
        );
        AddDifference(
            differences,
            "target_text_sha256",
            TargetTextSha256,
            observed.TargetTextSha256
        );
        AddDifference(
            differences,
            "target_word_open_xml_sha256",
            TargetWordOpenXmlSha256,
            observed.TargetWordOpenXmlSha256
        );
        AddDifference(
            differences,
            "context_text_sha256",
            ContextTextSha256,
            observed.ContextTextSha256
        );
        AddDifference(
            differences,
            "context_word_open_xml_sha256",
            ContextWordOpenXmlSha256,
            observed.ContextWordOpenXmlSha256
        );
        AddDifference(
            differences,
            "story_range_count",
            StoryDigest.RangeCount,
            observed.StoryDigest.RangeCount
        );
        AddDifference(
            differences,
            "story_graph_sha256",
            StoryDigest.Sha256,
            observed.StoryDigest.Sha256
        );
        return differences;
    }

    public object StructuralSummary()
    {
        return new
        {
            saved = Saved,
            content_range = new { start = ContentStart, end = ContentEnd },
            target_range = new { start = TargetStart, end = TargetEnd },
            context_range = new { start = ContextStart, end = ContextEnd },
            paragraph_count = ParagraphCount,
            equation_count = EquationCount,
            table_count = TableCount,
            field_count = FieldCount,
            bookmark_count = BookmarkCount,
            inline_shape_count = InlineShapeCount,
            shape_count = ShapeCount,
            comment_count = CommentCount,
            footnote_count = FootnoteCount,
            endnote_count = EndnoteCount,
            section_count = SectionCount,
            document_word_open_xml_verified_by_hash = true,
            content_text_verified_by_hash = true,
            content_word_open_xml_verified_by_hash = true,
            target_word_open_xml_verified_by_hash = true,
            context_word_open_xml_verified_by_hash = true,
            story_range_count = StoryDigest.RangeCount,
            story_graph_verified_by_hash = true,
            hashes_returned = false,
        };
    }

    private static void AddDifference<T>(
        ICollection<string> differences,
        string name,
        T expected,
        T observed
    )
        where T : IEquatable<T>
    {
        if (!expected.Equals(observed))
        {
            differences.Add(name);
        }
    }
}

internal sealed record RollbackStoryDigest(int RangeCount, string Sha256);

internal sealed record RollbackStoryRecord(
    int StoryType,
    int LinkIndex,
    int Start,
    int End,
    string TextSha256,
    string WordOpenXmlSha256
);
