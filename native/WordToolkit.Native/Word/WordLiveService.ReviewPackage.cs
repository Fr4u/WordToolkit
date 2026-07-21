using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> InspectPackageReviewAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveInspectablePackagePath(arguments);
        var view = arguments.String("view", "summary");
        if (
            view is not "summary"
                and not "comments"
                and not "anchors"
                and not "threads"
                and not "revisions"
                and not "move_ranges"
                and not "moves"
                and not "permissions"
                and not "people"
                and not "settings"
                and not "issues"
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "view must be summary, comments, anchors, threads, revisions, move_ranges, moves, permissions, people, settings, or issues"
            );
        }
        var detail = arguments.String("detail", "metadata");
        if (detail is not "metadata" and not "links")
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "detail must be metadata or links"
            );
        }
        var offset = arguments.NullableInt64("offset") ?? 0;
        var maximum = arguments.NullableInt64("max_items") ?? 30;
        if (offset is < 0 or > int.MaxValue)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "offset must be between 0 and 2147483647"
            );
        }
        if (maximum is < 1 or > 100)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_items must be between 1 and 100"
            );
        }
        var previewCharacters = arguments.NullableInt64("text_preview_chars") ?? 0;
        if (previewCharacters is < 0 or > 400)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "text_preview_chars must be between 0 and 400"
            );
        }
        var includeSensitive = arguments.Boolean("include_sensitive", false);
        if (previewCharacters > 0 && !includeSensitive)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "text_preview_chars greater than zero requires include_sensitive=true"
            );
        }
        var includeSource = arguments.Boolean("include_source", false);
        var includeIssues = arguments.Boolean("include_issues", true);
        var commentId = BoundedOptionalArgument(arguments, "comment_id", 128);
        var revisionId = BoundedOptionalArgument(arguments, "revision_id", 128);
        var storyKind = BoundedOptionalArgument(arguments, "story_kind", 128)
            ?.ToLowerInvariant();
        var revisionKind = BoundedOptionalArgument(arguments, "revision_kind", 128)
            ?.ToLowerInvariant();
        var authorFingerprint = BoundedOptionalArgument(
            arguments,
            "author_fingerprint",
            64
        )?.ToLowerInvariant();
        if (commentId is not null && view is not "comments" and not "anchors" and not "threads")
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "comment_id applies only to comments, anchors, or threads views"
            );
        }
        if (revisionId is not null && view != "revisions")
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "revision_id applies only to the revisions view"
            );
        }
        if (revisionKind is not null && view is not "summary" and not "revisions")
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "revision_kind applies only to summary or revisions views"
            );
        }
        try
        {
            var package = new OpcPackageReader().Read(path, cancellationToken);
            var semantic = new WordSemanticProjector().Project(
                package,
                cancellationToken
            );
            var graph = new WordReviewGraphBuilder().Build(
                package,
                semantic,
                cancellationToken
            );
            if (commentId is not null && !graph.TryGetComment(commentId, out _))
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "comment_id does not exist in this package fingerprint"
                );
            }
            if (revisionId is not null && !graph.TryGetRevision(revisionId, out _))
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "revision_id does not exist in this package fingerprint"
                );
            }
            if (
                storyKind is not null
                && !Enum.GetValues<WordStoryKind>().Any(kind =>
                    ToSnakeCase(kind.ToString()) == storyKind
                )
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "story_kind is not a recognized Word story kind"
                );
            }
            if (
                revisionKind is not null
                && !Enum.GetValues<WordRevisionKind>().Any(kind =>
                    ToSnakeCase(kind.ToString()) == revisionKind
                )
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "revision_kind is not a recognized revision kind"
                );
            }
            if (
                authorFingerprint is not null
                && (authorFingerprint.Length != 16
                    || authorFingerprint.Any(character =>
                        !char.IsAsciiHexDigit(character)
                    ))
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "author_fingerprint must be a 16-character lowercase or uppercase hexadecimal fingerprint"
                );
            }

            var page = ReviewItems(
                graph,
                view,
                detail,
                includeSensitive,
                includeSource,
                (int)previewCharacters,
                commentId,
                revisionId,
                storyKind,
                revisionKind,
                authorFingerprint,
                (int)offset,
                (int)maximum
            );
            var consumed = (long)offset + page.Items.Count;
            var relatedIssues = RelatedReviewIssues(
                graph,
                commentId,
                revisionId,
                storyKind
            );
            var issuePage = includeIssues && view != "issues"
                ? relatedIssues.Take(40)
                    .Select(issue => ReviewIssueItem(issue, includeSource))
                    .ToArray()
                : null;
            return Task.FromResult<object>(new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = graph.PackageFingerprint,
                main_part_uri = includeSource
                    ? BoundForResponse(graph.MainPartUri, 512)
                    : null,
                comment_count = graph.Comments.Count,
                anchored_comment_count = graph.Comments.Count(comment =>
                    comment.AnchorIds.Count > 0
                ),
                reply_count = graph.ReplyCount,
                thread_count = graph.ThreadCount,
                resolved_comment_count = graph.ResolvedCommentCount,
                reaction_comment_count = graph.Comments.Count(comment =>
                    comment.HasReactions
                ),
                comment_anchor_count = graph.Anchors.Count,
                incomplete_comment_anchor_count = graph.Anchors.Count(anchor =>
                    anchor.Status is not WordCommentAnchorStatus.Complete
                        and not WordCommentAnchorStatus.PointReference
                ),
                person_count = graph.People.Count,
                revision_count = graph.Revisions.Count,
                insertion_count = graph.Revisions.Count(revision =>
                    revision.Kind == WordRevisionKind.Insertion
                ),
                deletion_count = graph.Revisions.Count(revision =>
                    revision.Kind == WordRevisionKind.Deletion
                ),
                property_revision_count = graph.Revisions.Count(revision =>
                    revision.Kind.ToString().EndsWith(
                        "Change",
                        StringComparison.Ordinal
                    )
                ),
                tracked_text_character_count = graph.TrackedTextCharacterCount,
                move_range_count = graph.MoveRanges.Count,
                move_count = graph.Moves.Count,
                incomplete_move_count = graph.Moves.Count(move =>
                    move.Status != WordMovePairStatus.Complete
                ),
                permission_range_count = graph.Permissions.Count,
                incomplete_permission_range_count = graph.Permissions.Count(permission =>
                    permission.Status != WordReviewRangeStatus.Complete
                ),
                track_revisions_enabled = graph.Settings?.TrackRevisions,
                do_not_track_moves = graph.Settings?.DoNotTrackMoves,
                do_not_track_formatting = graph.Settings?.DoNotTrackFormatting,
                execution_policy = "parse_only_no_word_no_mutation_no_external_access",
                word_opened = false,
                mutation_performed = false,
                raw_xml_returned = false,
                external_content_followed = false,
                view,
                detail,
                sensitive_values_included = includeSensitive,
                text_preview_characters = previewCharacters,
                comment_id_filter = commentId,
                revision_id_filter = revisionId,
                story_kind_filter = storyKind,
                revision_kind_filter = revisionKind,
                author_fingerprint_filter = authorFingerprint,
                matched_item_count = page.MatchedItemCount,
                offset,
                returned_item_count = page.Items.Count,
                next_offset = consumed < page.MatchedItemCount
                    ? (int)consumed
                    : (int?)null,
                items = page.Items,
                issue_count = graph.Issues.Count,
                matched_issue_count = relatedIssues.Count,
                issues = issuePage,
                issues_truncated = graph.IssuesTruncated
                    || issuePage is not null
                        && relatedIssues.Count > issuePage.Length,
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            });
        }
        catch (WordReviewLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "Review graph exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordReviewProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a Word review graph",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordSemanticLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "Semantic projection exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordSemanticProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be projected as a Word semantic document",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (OpcPackageLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The package exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (InvalidDataException exception)
        {
            throw new NativeToolException(
                "INVALID_PACKAGE",
                "The file is not a readable OPC ZIP package",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (UnauthorizedAccessException)
        {
            throw new NativeToolException(
                "ACCESS_DENIED",
                "The Word package cannot be read with current permissions"
            );
        }
        catch (IOException exception)
        {
            throw new NativeToolException(
                "IO_ERROR",
                "The Word package could not be read",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
    }

    private static ReviewResponsePage ReviewItems(
        WordReviewGraph graph,
        string view,
        string detail,
        bool includeSensitive,
        bool includeSource,
        int previewCharacters,
        string? commentId,
        string? revisionId,
        string? storyKind,
        string? revisionKind,
        string? authorFingerprint,
        int offset,
        int maximum
    )
    {
        var comments = graph.Comments.Where(comment =>
            (commentId is null || comment.Id == commentId)
            && AuthorMatches(comment.Author, authorFingerprint)
        );
        var commentIds = comments.Select(comment => comment.Id)
            .ToHashSet(StringComparer.Ordinal);
        var revisions = graph.Revisions.Where(revision =>
            (revisionId is null || revision.Id == revisionId)
            && (storyKind is null
                || ToSnakeCase(revision.StoryKind.ToString()) == storyKind)
            && (revisionKind is null
                || ToSnakeCase(revision.Kind.ToString()) == revisionKind)
            && AuthorMatches(revision.Author, authorFingerprint)
        );
        return view switch
        {
            "summary" => ReviewPageItems(
                SummaryItems(graph, revisions, authorFingerprint),
                offset,
                maximum,
                item => item
            ),
            "comments" => ReviewPageItems(
                comments,
                offset,
                maximum,
                comment => CommentItem(
                    comment,
                    detail,
                    includeSensitive,
                    includeSource,
                    previewCharacters
                )
            ),
            "anchors" => ReviewPageItems(
                graph.Anchors.Where(anchor =>
                    (commentId is null
                        || anchor.CommentId == commentId)
                    && (storyKind is null
                        || ToSnakeCase(anchor.StoryKind.ToString()) == storyKind)
                    && (authorFingerprint is null
                        || anchor.CommentId is not null
                            && commentIds.Contains(anchor.CommentId))
                ),
                offset,
                maximum,
                anchor => AnchorItem(
                    anchor,
                    includeSensitive,
                    includeSource,
                    previewCharacters
                )
            ),
            "threads" => ReviewPageItems(
                comments.GroupBy(
                    comment => comment.ThreadRootCommentId,
                    StringComparer.Ordinal
                ).Where(group => commentId is null || group.Any(comment =>
                    comment.Id == commentId
                )),
                offset,
                maximum,
                group => ThreadItem(group, detail)
            ),
            "revisions" => ReviewPageItems(
                revisions,
                offset,
                maximum,
                revision => RevisionItem(
                    revision,
                    detail,
                    includeSensitive,
                    includeSource,
                    previewCharacters
                )
            ),
            "move_ranges" => ReviewPageItems(
                graph.MoveRanges.Where(range =>
                    (storyKind is null
                        || ToSnakeCase(range.StoryKind.ToString()) == storyKind)
                    && AuthorMatches(range.Author, authorFingerprint)
                ),
                offset,
                maximum,
                range => MoveRangeItem(
                    range,
                    detail,
                    includeSensitive,
                    includeSource
                )
            ),
            "moves" => ReviewPageItems(
                graph.Moves,
                offset,
                maximum,
                move => MoveItem(move, includeSensitive)
            ),
            "permissions" => ReviewPageItems(
                graph.Permissions.Where(permission =>
                    (storyKind is null
                        || ToSnakeCase(permission.StoryKind.ToString()) == storyKind)
                    && AuthorMatches(permission.Editor, authorFingerprint)
                ),
                offset,
                maximum,
                permission => PermissionItem(
                    permission,
                    includeSensitive,
                    includeSource
                )
            ),
            "people" => ReviewPageItems(
                graph.People.Where(person => AuthorMatches(
                    person.Author,
                    authorFingerprint
                )),
                offset,
                maximum,
                person => PersonItem(person, includeSensitive, includeSource)
            ),
            "settings" => ReviewPageItems(
                graph.Settings is null
                    ? Array.Empty<WordReviewSettingsDefinition>()
                    : new[] { graph.Settings },
                offset,
                maximum,
                settings => new
                {
                    track_revisions = settings.TrackRevisions,
                    do_not_track_moves = settings.DoNotTrackMoves,
                    do_not_track_formatting = settings.DoNotTrackFormatting,
                    part_uri = includeSource
                        ? BoundForResponse(settings.PartUri, 512)
                        : null,
                }
            ),
            _ => ReviewPageItems(
                RelatedReviewIssues(graph, commentId, revisionId, storyKind),
                offset,
                maximum,
                issue => ReviewIssueItem(issue, includeSource)
            ),
        };
    }

    private static IEnumerable<object> SummaryItems(
        WordReviewGraph graph,
        IEnumerable<WordRevisionDefinition> revisions,
        string? authorFingerprint
    )
    {
        if (authorFingerprint is null)
        {
            yield return new
            {
                category = "comments",
                count = graph.Comments.Count,
                reply_count = graph.ReplyCount,
                thread_count = graph.ThreadCount,
                incomplete_count = graph.Anchors.Count(anchor =>
                    anchor.Status is not WordCommentAnchorStatus.Complete
                        and not WordCommentAnchorStatus.PointReference
                ),
            };
        }
        foreach (
            var group in revisions.GroupBy(revision => revision.Kind)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
        )
        {
            yield return new
            {
                category = "revisions",
                revision_kind = ToSnakeCase(group.Key.ToString()),
                count = group.Count(),
                text_character_count = group.Sum(revision =>
                    (long)revision.TextCharacterCount
                ),
                incomplete_count = group.Count(revision =>
                    revision.Status != WordRevisionStatus.Complete
                ),
            };
        }
        if (authorFingerprint is null)
        {
            yield return new
            {
                category = "moves",
                count = graph.Moves.Count,
                incomplete_count = graph.Moves.Count(move =>
                    move.Status != WordMovePairStatus.Complete
                ),
            };
            yield return new
            {
                category = "permissions",
                count = graph.Permissions.Count,
                incomplete_count = graph.Permissions.Count(permission =>
                    permission.Status != WordReviewRangeStatus.Complete
                ),
            };
        }
    }

    private static object CommentItem(
        WordCommentDefinition comment,
        string detail,
        bool includeSensitive,
        bool includeSource,
        int previewCharacters
    )
    {
        var preview = SensitivePreview(
            comment.Text,
            includeSensitive,
            previewCharacters
        );
        return new
        {
            comment_id = comment.Id,
            effective_by_ooxml_id = comment.IsEffectiveByOoxmlId,
            reply = comment.IsReply,
            done = comment.IsDone,
            thread_depth = comment.ThreadDepth,
            anchor_count = comment.AnchorIds.Count,
            has_extended_metadata = comment.HasExtendedMetadata,
            has_durable_id = comment.DurableId is not null,
            intelligent_placeholder = comment.IsIntelligentPlaceholder,
            has_reactions = comment.HasReactions,
            extension_count = comment.ExtensionCount,
            author_fingerprint = FingerprintSensitiveValue(comment.Author),
            author = includeSensitive
                ? BoundForResponse(comment.Author, 512)
                : null,
            initials = includeSensitive
                ? BoundForResponse(comment.Initials, 64)
                : null,
            date = BoundForResponse(comment.Date, 128),
            date_utc = BoundForResponse(
                comment.ExtensibleDateUtc ?? comment.DateUtc,
                128
            ),
            text_character_count = comment.TextCharacterCount,
            text_capture_truncated = comment.TextTruncated,
            text_fingerprint = TextFingerprint(comment.Text),
            text_preview = preview.Value,
            text_preview_truncated = preview.Truncated,
            parent_comment_id = detail == "links"
                ? comment.ParentCommentId
                : null,
            thread_root_comment_id = detail == "links"
                ? comment.ThreadRootCommentId
                : null,
            person_id = detail == "links" ? comment.PersonId : null,
            anchor_ids = detail == "links"
                ? comment.AnchorIds.Take(64).ToArray()
                : null,
            anchor_ids_truncated = detail == "links" && comment.AnchorIds.Count > 64
                ? true
                : (bool?)null,
            ooxml_id = includeSource
                ? BoundForResponse(comment.OoxmlId, 128)
                : null,
            durable_id = includeSource
                ? BoundForResponse(comment.DurableId, 128)
                : null,
            paragraph_count = comment.ParagraphIds.Count,
            last_paragraph_id = includeSource
                ? BoundForResponse(comment.LastParagraphId, 128)
                : null,
            part_uri = includeSource
                ? BoundForResponse(comment.PartUri, 512)
                : null,
            source_element_ordinal = includeSource
                ? comment.SourceElementOrdinal
                : (int?)null,
            semantic_node_id = includeSource ? comment.SemanticNodeId?.Value : null,
        };
    }

    private static object AnchorItem(
        WordCommentAnchor anchor,
        bool includeSensitive,
        bool includeSource,
        int previewCharacters
    )
    {
        var preview = SensitivePreview(
            anchor.Text,
            includeSensitive,
            previewCharacters
        );
        return new
        {
            anchor_id = anchor.Id,
            comment_id = anchor.CommentId,
            story_id = anchor.StoryId,
            story_kind = ToSnakeCase(anchor.StoryKind.ToString()),
            status = ToSnakeCase(anchor.Status.ToString()),
            has_definition = anchor.HasDefinition,
            start_count = anchor.StartCount,
            end_count = anchor.EndCount,
            reference_count = anchor.ReferenceCount,
            text_character_count = anchor.TextCharacterCount,
            text_capture_truncated = anchor.TextTruncated,
            text_fingerprint = TextFingerprint(anchor.Text),
            text_preview = preview.Value,
            text_preview_truncated = preview.Truncated,
            ooxml_id = includeSource
                ? BoundForResponse(anchor.OoxmlId, 128)
                : null,
            part_uri = includeSource
                ? BoundForResponse(anchor.PartUri, 512)
                : null,
            start_element_ordinal = includeSource
                ? anchor.StartElementOrdinal
                : null,
            end_element_ordinal = includeSource
                ? anchor.EndElementOrdinal
                : null,
            reference_element_ordinal = includeSource
                ? anchor.ReferenceElementOrdinal
                : null,
            start_node_id = includeSource ? anchor.StartNodeId?.Value : null,
            end_node_id = includeSource ? anchor.EndNodeId?.Value : null,
            reference_node_id = includeSource ? anchor.ReferenceNodeId?.Value : null,
            story_node_id = includeSource ? anchor.StoryNodeId?.Value : null,
        };
    }

    private static object ThreadItem(
        IGrouping<string, WordCommentDefinition> group,
        string detail
    )
    {
        var comments = group.OrderBy(comment => comment.ThreadDepth)
            .ThenBy(comment => comment.SourceElementOrdinal)
            .ToArray();
        return new
        {
            thread_root_comment_id = group.Key,
            comment_count = comments.Length,
            reply_count = comments.Count(comment => comment.IsReply),
            resolved_count = comments.Count(comment => comment.IsDone),
            reaction_comment_count = comments.Count(comment => comment.HasReactions),
            maximum_depth = comments.Max(comment => comment.ThreadDepth),
            author_count = comments.Select(comment =>
                    FingerprintSensitiveValue(comment.Author)
                )
                .Where(value => value is not null)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            comment_ids = detail == "links"
                ? comments.Take(64).Select(comment => comment.Id).ToArray()
                : null,
            comment_ids_truncated = detail == "links" && comments.Length > 64
                ? true
                : (bool?)null,
        };
    }

    private static object RevisionItem(
        WordRevisionDefinition revision,
        string detail,
        bool includeSensitive,
        bool includeSource,
        int previewCharacters
    )
    {
        var preview = SensitivePreview(
            revision.Text,
            includeSensitive,
            previewCharacters
        );
        return new
        {
            revision_id = revision.Id,
            kind = ToSnakeCase(revision.Kind.ToString()),
            status = ToSnakeCase(revision.Status.ToString()),
            story_id = revision.StoryId,
            story_kind = ToSnakeCase(revision.StoryKind.ToString()),
            source_name = revision.SourceName,
            inside_deleted_content = revision.IsInDeletedContent,
            contains_math = revision.ContainsMath,
            content_element_count = revision.ContentElementCount,
            author_fingerprint = FingerprintSensitiveValue(revision.Author),
            author = includeSensitive
                ? BoundForResponse(revision.Author, 512)
                : null,
            date = BoundForResponse(revision.Date, 128),
            date_utc = BoundForResponse(revision.DateUtc, 128),
            text_character_count = revision.TextCharacterCount,
            text_capture_truncated = revision.TextTruncated,
            text_fingerprint = TextFingerprint(revision.Text),
            text_preview = preview.Value,
            text_preview_truncated = preview.Truncated,
            parent_revision_id = detail == "links"
                ? revision.ParentRevisionId
                : null,
            person_id = detail == "links" ? revision.PersonId : null,
            ooxml_id = includeSource
                ? BoundForResponse(revision.OoxmlId, 128)
                : null,
            part_uri = includeSource
                ? BoundForResponse(revision.PartUri, 512)
                : null,
            source_element_ordinal = includeSource
                ? revision.SourceElementOrdinal
                : (int?)null,
            semantic_node_id = includeSource ? revision.SemanticNodeId?.Value : null,
            paragraph_node_id = includeSource ? revision.ParagraphNodeId?.Value : null,
            story_node_id = includeSource ? revision.StoryNodeId?.Value : null,
        };
    }

    private static object MoveRangeItem(
        WordMoveRangeDefinition range,
        string detail,
        bool includeSensitive,
        bool includeSource
    ) => new
    {
        move_range_id = range.Id,
        kind = ToSnakeCase(range.Kind.ToString()),
        status = ToSnakeCase(range.Status.ToString()),
        story_id = range.StoryId,
        story_kind = ToSnakeCase(range.StoryKind.ToString()),
        name_fingerprint = FingerprintSensitiveValue(range.Name),
        name = includeSensitive ? BoundForResponse(range.Name, 512) : null,
        author_fingerprint = FingerprintSensitiveValue(range.Author),
        author = includeSensitive ? BoundForResponse(range.Author, 512) : null,
        date = BoundForResponse(range.Date, 128),
        start_count = range.StartCount,
        end_count = range.EndCount,
        revision_count = range.RevisionIds.Count,
        revision_ids = detail == "links"
            ? range.RevisionIds.Take(64).ToArray()
            : null,
        revision_ids_truncated = detail == "links" && range.RevisionIds.Count > 64
            ? true
            : (bool?)null,
        ooxml_id = includeSource
            ? BoundForResponse(range.OoxmlId, 128)
            : null,
        part_uri = includeSource ? BoundForResponse(range.PartUri, 512) : null,
        start_element_ordinal = includeSource
            ? range.StartElementOrdinal
            : null,
        end_element_ordinal = includeSource ? range.EndElementOrdinal : null,
        story_node_id = includeSource ? range.StoryNodeId?.Value : null,
    };

    private static object MoveItem(
        WordMovePairDefinition move,
        bool includeSensitive
    ) => new
    {
        move_id = move.Id,
        status = ToSnakeCase(move.Status.ToString()),
        name_fingerprint = FingerprintSensitiveValue(move.Name),
        name = includeSensitive ? BoundForResponse(move.Name, 512) : null,
        source_range_id = move.SourceRangeId,
        destination_range_id = move.DestinationRangeId,
    };

    private static object PermissionItem(
        WordPermissionRangeDefinition permission,
        bool includeSensitive,
        bool includeSource
    ) => new
    {
        permission_range_id = permission.Id,
        status = ToSnakeCase(permission.Status.ToString()),
        story_id = permission.StoryId,
        story_kind = ToSnakeCase(permission.StoryKind.ToString()),
        editor_fingerprint = FingerprintSensitiveValue(permission.Editor),
        editor = includeSensitive
            ? BoundForResponse(permission.Editor, 512)
            : null,
        editor_group = BoundForResponse(permission.EditorGroup, 128),
        column_first = permission.ColumnFirst,
        column_last = permission.ColumnLast,
        start_count = permission.StartCount,
        end_count = permission.EndCount,
        ooxml_id = includeSource
            ? BoundForResponse(permission.OoxmlId, 128)
            : null,
        part_uri = includeSource
            ? BoundForResponse(permission.PartUri, 512)
            : null,
        start_element_ordinal = includeSource
            ? permission.StartElementOrdinal
            : null,
        end_element_ordinal = includeSource
            ? permission.EndElementOrdinal
            : null,
        story_node_id = includeSource ? permission.StoryNodeId?.Value : null,
    };

    private static object PersonItem(
        WordReviewPersonDefinition person,
        bool includeSensitive,
        bool includeSource
    ) => new
    {
        person_id = person.Id,
        author_fingerprint = FingerprintSensitiveValue(person.Author),
        author = includeSensitive ? BoundForResponse(person.Author, 512) : null,
        provider_fingerprint = FingerprintSensitiveValue(person.ProviderId),
        provider_id = includeSensitive
            ? BoundForResponse(person.ProviderId, 512)
            : null,
        user_id_fingerprint = FingerprintSensitiveValue(person.UserId),
        user_id = includeSensitive ? BoundForResponse(person.UserId, 512) : null,
        comment_count = person.CommentCount,
        revision_count = person.RevisionCount,
        part_uri = includeSource ? BoundForResponse(person.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? person.SourceElementOrdinal
            : (int?)null,
    };

    private static IReadOnlyList<WordReviewIssue> RelatedReviewIssues(
        WordReviewGraph graph,
        string? commentId,
        string? revisionId,
        string? storyKind
    )
    {
        if (commentId is null && revisionId is null && storyKind is null)
            return graph.Issues;
        var storyIds = graph.Anchors.Where(anchor =>
                storyKind is null
                    || ToSnakeCase(anchor.StoryKind.ToString()) == storyKind
            )
            .Select(anchor => anchor.StoryId)
            .Concat(graph.Revisions.Where(revision =>
                    storyKind is null
                        || ToSnakeCase(revision.StoryKind.ToString()) == storyKind
                ).Select(revision => revision.StoryId))
            .ToHashSet(StringComparer.Ordinal);
        var commentSubjects = commentId is null
            ? null
            : graph.Comments.Where(comment => comment.Id == commentId)
                .SelectMany(comment => comment.AnchorIds.Append(comment.Id))
                .ToHashSet(StringComparer.Ordinal);
        return graph.Issues.Where(issue =>
                (commentSubjects is null
                    || issue.SubjectId is not null
                        && commentSubjects.Contains(issue.SubjectId))
                && (revisionId is null || issue.SubjectId == revisionId)
                && (storyKind is null
                    || issue.StoryId is not null && storyIds.Contains(issue.StoryId))
            )
            .ToArray();
    }

    private static object ReviewIssueItem(
        WordReviewIssue issue,
        bool includeSource
    ) => new
    {
        code = BoundForResponse(issue.Code, 128),
        severity = ToSnakeCase(issue.Severity.ToString()),
        message = BoundForResponse(issue.Message, 512),
        subject_id = issue.SubjectId,
        story_id = includeSource ? issue.StoryId : null,
        part_uri = includeSource ? BoundForResponse(issue.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? issue.SourceElementOrdinal
            : null,
    };

    private static bool AuthorMatches(string? author, string? fingerprint) =>
        fingerprint is null
        || string.Equals(
            FingerprintSensitiveValue(author),
            fingerprint,
            StringComparison.OrdinalIgnoreCase
        );

    private static ReviewResponsePage ReviewPageItems<T>(
        IEnumerable<T> source,
        int offset,
        int maximum,
        Func<T, object> project
    )
    {
        var matched = 0;
        var items = new List<object>(maximum);
        foreach (var item in source)
        {
            if (matched >= offset && items.Count < maximum)
                items.Add(project(item));
            matched++;
        }
        return new ReviewResponsePage(matched, items.ToArray());
    }

    private sealed record ReviewResponsePage(
        int MatchedItemCount,
        IReadOnlyList<object> Items
    );
}
