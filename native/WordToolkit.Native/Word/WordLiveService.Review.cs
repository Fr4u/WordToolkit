using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static readonly IReadOnlyDictionary<int, string> WordRevisionTypes =
        new Dictionary<int, string>
        {
            [1] = "insert",
            [2] = "delete",
            [3] = "property",
            [4] = "paragraph_number",
            [5] = "display_field",
            [6] = "reconcile",
            [7] = "conflict",
            [8] = "style",
            [9] = "replace",
            [10] = "section_property",
            [11] = "table_property",
            [12] = "cell_insert",
            [13] = "cell_delete",
            [14] = "cell_merge",
        };

    private readonly ConcurrentDictionary<string, ReviewGrant> _reviewGrants = new();

    private async Task<object> InspectReviewAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var kind = arguments.String("kind");
        if (kind is not ("comments" or "revisions"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "kind must be comments or revisions"
            );
        }
        var offset = (int)(arguments.NullableInt64("offset") ?? 0);
        var limit = (int)(arguments.NullableInt64("limit") ?? 50);
        var includeText = arguments.Boolean("include_text", true);
        var maxTextChars = (int)(arguments.NullableInt64("max_text_chars") ?? 500);
        if (offset is < 0 or > 1_000_000)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "offset must be between 0 and 1,000,000"
            );
        }
        if (limit is < 1 or > 200)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "limit must be between 1 and 200"
            );
        }
        if (maxTextChars is < 1 or > 2_000)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_text_chars must be between 1 and 2,000"
            );
        }

        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                dynamic document = ResolveDocument(application, record);
                dynamic collection = kind == "comments"
                    ? document.Comments
                    : document.Revisions;
                var total = Math.Max(0, (int)collection.Count);
                var first = Math.Min(total, offset) + 1;
                var last = Math.Min(total, offset + limit);
                var items = new List<object>();
                for (var index = first; index <= last; index++)
                {
                    dynamic item = collection.Item(index);
                    dynamic targetRange = kind == "comments" ? item.Scope : item.Range;
                    var signature = ReviewSignature(item, kind);
                    var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
                        .ToLowerInvariant();
                    _reviewGrants[token] = new ReviewGrant(
                        token,
                        record.Id,
                        record.Version,
                        kind,
                        index,
                        signature
                    );

                    var payload = new Dictionary<string, object?>
                    {
                        ["item_index"] = index,
                        ["review_token"] = token,
                        ["range"] = new
                        {
                            start = (int)targetRange.Start,
                            end = (int)targetRange.End,
                        },
                        ["author"] = SafeString(() => (string?)item.Author),
                        ["date"] = SafeString(
                            () => Convert.ToString(item.Date, CultureInfo.InvariantCulture)
                        ),
                    };
                    if (kind == "comments")
                    {
                        var (replyCount, repliesSupported) = CommentReplyCount(
                            (object)item
                        );
                        var (resolved, resolveSupported) = CommentResolvedState(
                            (object)item
                        );
                        payload["resolved"] = resolved;
                        payload["resolve_supported"] = resolveSupported;
                        payload["reply_count"] = replyCount;
                        payload["replies_supported"] = repliesSupported;
                    }
                    else
                    {
                        var type = SafeInt(() => (int)item.Type, 0);
                        payload["type_id"] = type;
                        payload["type"] = WordRevisionTypes.TryGetValue(type, out var name)
                            ? name
                            : $"unknown_{type}";
                    }
                    if (includeText)
                    {
                        var raw = SafeString(() => (string?)item.Range.Text);
                        var cleaned = CleanWordPreview(raw);
                        payload["text_preview"] = cleaned[
                            ..Math.Min(cleaned.Length, maxTextChars)
                        ];
                        payload["text_truncated"] = cleaned.Length > maxTextChars;
                    }
                    items.Add(payload);
                }
                TrimReviewGrants();
                return new
                {
                    live_document_id = record.Id,
                    live_version = record.Version,
                    kind,
                    track_changes = SafeBool(() => (bool)document.TrackRevisions, false),
                    total_count = total,
                    offset,
                    limit,
                    returned_count = items.Count,
                    truncated = offset + items.Count < total,
                    items,
                    token_policy = new
                    {
                        fresh_token_required_for_mutation = true,
                        raw_index_without_token_allowed = false,
                        invalidated_by_live_version_change = true,
                        current_item_fingerprint_verified = true,
                    },
                    document = DocumentInfo(application, document),
                    performance = Performance(started),
                };
            },
            cancellationToken
        );
    }

    private async Task<object> ManageReviewAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var action = arguments.String("action");
        var supported = new HashSet<string>(
            [
                "add_comment",
                "reply_comment",
                "resolve_comment",
                "delete_comment",
                "accept_revision",
                "reject_revision",
                "set_track_changes",
            ],
            StringComparer.Ordinal
        );
        if (!supported.Contains(action))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Unsupported Word review action",
                new { supported = supported.Order(StringComparer.Ordinal).ToArray() }
            );
        }
        var expectedVersion = arguments.NullableInt64("expected_version")
            ?? throw new NativeToolException(
                "INVALID_INPUT",
                "expected_version is required for review mutations"
            );
        var itemIndex = (int)(arguments.NullableInt64("item_index") ?? 0);
        var reviewToken = arguments.String("review_token");
        var selectionToken = arguments.String("selection_token");
        var text = arguments.String("text");
        var resolved = arguments.Boolean("resolved", true);
        var optimize = arguments.Boolean("optimize_screen_updates", true);
        var trackingEnabled = arguments.TryGetProperty(
                "tracking_enabled",
                out var trackingNode
            )
            && trackingNode.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? trackingNode.GetBoolean()
                : (bool?)null;
        if (action is "add_comment" or "reply_comment")
        {
            if (text.Length is < 1 or > 20_000)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Comment text must contain between 1 and 20,000 characters"
                );
            }
        }
        if (action == "set_track_changes" && trackingEnabled is null)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "tracking_enabled must be true or false for set_track_changes"
            );
        }
        if (action is not ("add_comment" or "set_track_changes") && itemIndex < 1)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "item_index must be a positive integer"
            );
        }
        CheckVersion(record, expectedVersion);
        var started = Stopwatch.GetTimestamp();

        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                var originalScreenUpdating = (bool?)null;
                dynamic? undoRecord = null;
                var undoStarted = false;
                var undoable = false;
                var mutated = true;
                object result;
                try
                {
                    if (optimize)
                    {
                        originalScreenUpdating = (bool)application.ScreenUpdating;
                        application.ScreenUpdating = false;
                    }

                    if (action == "set_track_changes")
                    {
                        var previous = (bool)document.TrackRevisions;
                        var desired = trackingEnabled!.Value;
                        if (previous == desired)
                        {
                            mutated = false;
                            result = new
                            {
                                previous_state = previous,
                                track_changes = previous,
                            };
                        }
                        else
                        {
                            try
                            {
                                document.TrackRevisions = desired;
                                if ((bool)document.TrackRevisions != desired)
                                {
                                    throw new NativeToolException(
                                        "EXTERNAL_TOOL_FAILED",
                                        "Word did not apply the requested Track Changes state",
                                        retryable: true
                                    );
                                }
                            }
                            catch
                            {
                                try
                                {
                                    document.TrackRevisions = previous;
                                }
                                catch
                                {
                                    // Preserve the original failure.
                                }
                                throw;
                            }
                            result = new
                            {
                                previous_state = previous,
                                track_changes = desired,
                            };
                        }
                    }
                    else if (action == "add_comment")
                    {
                        RequireActive(application, document);
                        dynamic range = ResolveVerifiedSelectionRange(
                            application,
                            document,
                            record,
                            selectionToken,
                            requireNonEmpty: true
                        );
                        var before = (int)document.Comments.Count;
                        undoRecord = application.UndoRecord;
                        undoRecord.StartCustomRecord("WordToolkit: add live comment");
                        undoStarted = true;
                        dynamic comment = document.Comments.Add(range, text);
                        if ((int)document.Comments.Count != before + 1)
                        {
                            throw new NativeToolException(
                                "EXTERNAL_TOOL_FAILED",
                                "Word did not create exactly one comment",
                                retryable: true
                            );
                        }
                        undoRecord.EndCustomRecord();
                        undoStarted = false;
                        undoable = true;
                        result = new
                        {
                            comment_index = SafeInt(() => (int)comment.Index, before + 1),
                            scope = new { start = (int)range.Start, end = (int)range.End },
                        };
                    }
                    else if (
                        action
                        is "reply_comment"
                            or "resolve_comment"
                            or "delete_comment"
                    )
                    {
                        dynamic comment = RequireReviewItem(
                            document,
                            record,
                            "comments",
                            itemIndex,
                            reviewToken
                        );
                        if (action == "reply_comment")
                        {
                            var (before, repliesSupported) = CommentReplyCount(
                                (object)comment
                            );
                            if (!repliesSupported)
                            {
                                throw new NativeToolException(
                                    "LIVE_WORD_UNAVAILABLE",
                                    "This Word version does not expose threaded comment replies through COM"
                                );
                            }
                            undoRecord = application.UndoRecord;
                            undoRecord.StartCustomRecord(
                                "WordToolkit: reply to live comment"
                            );
                            undoStarted = true;
                            dynamic reply = comment.Replies.Add(comment.Scope, text);
                            if ((int)comment.Replies.Count != before + 1)
                            {
                                throw new NativeToolException(
                                    "EXTERNAL_TOOL_FAILED",
                                    "Word did not create exactly one comment reply",
                                    retryable: true
                                );
                            }
                            undoRecord.EndCustomRecord();
                            undoStarted = false;
                            undoable = true;
                            result = new
                            {
                                comment_index = itemIndex,
                                reply_index = SafeInt(
                                    () => (int)reply.Index,
                                    before + 1
                                ),
                                reply_count = (int)comment.Replies.Count,
                            };
                        }
                        else if (action == "resolve_comment")
                        {
                            var (previous, supportedResolve) =
                                CommentResolvedState((object)comment);
                            if (!supportedResolve)
                            {
                                throw new NativeToolException(
                                    "LIVE_WORD_UNAVAILABLE",
                                    "This Word comment model does not expose resolution state through COM"
                                );
                            }
                            if (previous == resolved)
                            {
                                mutated = false;
                            }
                            else
                            {
                                try
                                {
                                    comment.Done = resolved;
                                    if ((bool)comment.Done != resolved)
                                    {
                                        throw new NativeToolException(
                                            "EXTERNAL_TOOL_FAILED",
                                            "Word did not apply the requested comment state",
                                            retryable: true
                                        );
                                    }
                                }
                                catch
                                {
                                    try
                                    {
                                        comment.Done = previous;
                                    }
                                    catch
                                    {
                                        // Preserve the original failure.
                                    }
                                    throw;
                                }
                            }
                            result = new
                            {
                                comment_index = itemIndex,
                                previous_state = previous,
                                resolved = mutated ? resolved : previous,
                            };
                        }
                        else
                        {
                            var before = (int)document.Comments.Count;
                            undoRecord = application.UndoRecord;
                            undoRecord.StartCustomRecord(
                                "WordToolkit: delete live comment"
                            );
                            undoStarted = true;
                            comment.Delete();
                            if ((int)document.Comments.Count != before - 1)
                            {
                                throw new NativeToolException(
                                    "EXTERNAL_TOOL_FAILED",
                                    "Word did not delete exactly one comment",
                                    retryable: true
                                );
                            }
                            undoRecord.EndCustomRecord();
                            undoStarted = false;
                            undoable = true;
                            result = new
                            {
                                deleted_comment_index = itemIndex,
                                remaining_comments = (int)document.Comments.Count,
                            };
                        }
                    }
                    else
                    {
                        dynamic revision = RequireReviewItem(
                            document,
                            record,
                            "revisions",
                            itemIndex,
                            reviewToken
                        );
                        var before = (int)document.Revisions.Count;
                        var verb = action == "accept_revision" ? "accept" : "reject";
                        undoRecord = application.UndoRecord;
                        undoRecord.StartCustomRecord(
                            $"WordToolkit: {verb} live revision"
                        );
                        undoStarted = true;
                        if (action == "accept_revision")
                        {
                            revision.Accept();
                        }
                        else
                        {
                            revision.Reject();
                        }
                        if ((int)document.Revisions.Count >= before)
                        {
                            throw new NativeToolException(
                                "EXTERNAL_TOOL_FAILED",
                                $"Word did not {verb} the selected revision",
                                retryable: true
                            );
                        }
                        undoRecord.EndCustomRecord();
                        undoStarted = false;
                        undoable = true;
                        result = new
                        {
                            reviewed_revision_index = itemIndex,
                            decision = verb,
                            remaining_revisions = (int)document.Revisions.Count,
                        };
                    }

                    if (mutated)
                    {
                        record.Version++;
                        InvalidateSelectionGrants(record.Id);
                        InvalidateRangeGrants(record.Id);
                        InvalidateUndoGrants(record.Id);
                    }
                    return new
                    {
                        live_document_id = record.Id,
                        live_version = record.Version,
                        action,
                        mutated,
                        result,
                        document = DocumentInfo(application, document),
                        execution = new
                        {
                            com_attachments = 1,
                            single_undo_record = undoable,
                            rollback_on_error = undoable,
                            manual_rollback_on_error = !undoable,
                            raw_index_without_token_allowed =
                                action is "add_comment" or "set_track_changes",
                            screen_updates_suspended = optimize && undoable,
                        },
                        performance = Performance(started),
                    };
                }
                catch
                {
                    Rollback(document, undoRecord, ref undoStarted);
                    throw;
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

    private dynamic RequireReviewItem(
        dynamic document,
        LiveDocumentRecord record,
        string kind,
        int itemIndex,
        string reviewToken
    )
    {
        if (
            reviewToken.Length == 0
            || !_reviewGrants.TryGetValue(reviewToken, out var grant)
        )
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "A fresh review_token is required for this review mutation",
                retryable: true
            );
        }
        dynamic collection = kind == "comments"
            ? document.Comments
            : document.Revisions;
        var total = (int)collection.Count;
        if (itemIndex < 1 || itemIndex > total)
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "The reviewed Word item no longer exists at that position",
                new { item_index = itemIndex, total_count = total },
                retryable: true
            );
        }
        dynamic item = collection.Item(itemIndex);
        var actualSignature = ReviewSignature(item, kind);
        if (
            grant.DocumentId != record.Id
            || grant.Version != record.Version
            || grant.Kind != kind
            || grant.Index != itemIndex
            || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(grant.Signature),
                Convert.FromHexString(actualSignature)
            )
        )
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "The Word comment or revision changed after inspection",
                new { kind, item_index = itemIndex },
                retryable: true
            );
        }
        return item;
    }

    private static string ReviewSignature(dynamic item, string kind)
    {
        dynamic target = kind == "comments" ? item.Scope : item.Range;
        var (replyCount, repliesSupported) = CommentReplyCount((object)item);
        var (resolved, resolveSupported) = CommentResolvedState((object)item);
        var text = SafeString(() => (string?)item.Range.Text);
        var value = string.Join(
            "\0",
            kind,
            kind == "revisions" ? SafeInt(() => (int)item.Type, 0) : 0,
            SafeString(() => (string?)item.Author),
            SafeString(() => Convert.ToString(item.Date, CultureInfo.InvariantCulture)),
            SafeInt(() => (int)target.Start, -1),
            SafeInt(() => (int)target.End, -1),
            replyCount,
            repliesSupported,
            resolved,
            resolveSupported,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
        );
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }

    private static (int Count, bool Supported) CommentReplyCount(object itemObject)
    {
        dynamic item = itemObject;
        try
        {
            return (Math.Max(0, (int)item.Replies.Count), true);
        }
        catch
        {
            return (0, false);
        }
    }

    private static (bool Resolved, bool Supported) CommentResolvedState(
        object itemObject
    )
    {
        dynamic item = itemObject;
        try
        {
            return ((bool)item.Done, true);
        }
        catch
        {
            return (false, false);
        }
    }

    private static bool SafeBool(Func<bool> read, bool fallback)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }

    private void TrimReviewGrants()
    {
        const int maximum = 4_096;
        if (_reviewGrants.Count <= maximum)
        {
            return;
        }
        foreach (var key in _reviewGrants.Keys.Take(_reviewGrants.Count - maximum))
        {
            _reviewGrants.TryRemove(key, out _);
        }
    }

    private void InvalidateReviewGrants(string documentId)
    {
        foreach (
            var pair in _reviewGrants.Where(
                pair => pair.Value.DocumentId == documentId
            )
        )
        {
            _reviewGrants.TryRemove(pair.Key, out _);
        }
    }

    private sealed record ReviewGrant(
        string Token,
        string DocumentId,
        long Version,
        string Kind,
        int Index,
        string Signature
    );
}
