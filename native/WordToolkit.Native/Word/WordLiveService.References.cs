using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> InspectPackageReferencesAsync(
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
                and not "stories"
                and not "bookmarks"
                and not "fields"
                and not "dependencies"
                and not "issues"
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "view must be summary, stories, bookmarks, fields, dependencies, or issues"
            );
        }
        var detail = arguments.String("detail", "metadata");
        if (detail is not "metadata" and not "parsed")
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "detail must be metadata or parsed"
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

        var includeSensitive = arguments.Boolean("include_sensitive", false);
        var includeResultText = arguments.Boolean("include_result_text", false);
        if (includeResultText && !includeSensitive)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "include_result_text requires include_sensitive=true"
            );
        }
        var includeSource = arguments.Boolean("include_source", false);
        var includeIssues = arguments.Boolean("include_issues", true);
        var fieldType = BoundedOptionalArgument(arguments, "field_type", 128)
            ?.ToUpperInvariant();
        var bookmarkName = BoundedOptionalArgument(
            arguments,
            "bookmark_name",
            4_096
        );
        var storyId = BoundedOptionalArgument(arguments, "story_id", 128);
        try
        {
            var package = new OpcPackageReader().Read(path, cancellationToken);
            var semantic = new WordSemanticProjector().Project(
                package,
                cancellationToken
            );
            var graph = new WordReferenceGraphBuilder().Build(
                package,
                semantic,
                cancellationToken
            );
            var matching = ReferenceItems(
                graph,
                view,
                detail,
                includeSensitive,
                includeResultText,
                includeSource,
                fieldType,
                bookmarkName,
                storyId
            );
            var page = matching.Skip((int)offset).Take((int)maximum).ToArray();
            var consumed = (long)offset + page.Length;
            var issuePage = includeIssues && view != "issues"
                ? graph.Issues.Take(40).Select(issue => ReferenceIssueItem(
                    issue,
                    includeSource
                )).ToArray()
                : null;
            var duplicateNameCount = graph.Bookmarks
                .Where(bookmark => !string.IsNullOrWhiteSpace(bookmark.Name))
                .GroupBy(bookmark => bookmark.Name!, StringComparer.OrdinalIgnoreCase)
                .Count(group => group.Count() > 1);
            return Task.FromResult<object>(new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = graph.PackageFingerprint,
                main_part_uri = includeSource
                    ? BoundForResponse(graph.MainPartUri, 512)
                    : null,
                story_count = graph.Stories.Count,
                bookmark_count = graph.Bookmarks.Count,
                complete_bookmark_count = graph.Bookmarks.Count(bookmark =>
                    bookmark.IsComplete
                ),
                duplicate_bookmark_name_count = duplicateNameCount,
                field_count = graph.Fields.Count,
                complex_field_count = graph.Fields.Count(field =>
                    field.Kind == WordFieldKind.Complex
                ),
                simple_field_count = graph.Fields.Count(field =>
                    field.Kind == WordFieldKind.Simple
                ),
                incomplete_field_count = graph.Fields.Count(field =>
                    field.Status != WordFieldStatus.Complete
                    || !field.InstructionParseComplete
                ),
                external_field_count = graph.Fields.Count(field =>
                    field.RequiresExternalAccess
                ),
                application_invoking_field_count = graph.Fields.Count(field =>
                    field.MayInvokeApplication
                ),
                dependency_count = graph.Edges.Count,
                unresolved_dependency_count = graph.Edges.Count(edge =>
                    !edge.IsResolved
                ),
                external_dependency_count = graph.Edges.Count(edge =>
                    edge.IsExternal
                ),
                execution_policy = "parse_only_never_execute_or_follow_external_targets",
                word_opened = false,
                external_targets_followed = false,
                view,
                detail,
                sensitive_values_included = includeSensitive,
                result_text_included = includeResultText,
                matched_item_count = matching.Count,
                offset,
                returned_item_count = page.Length,
                next_offset = consumed < matching.Count ? (int)consumed : (int?)null,
                items = page,
                issue_count = graph.Issues.Count,
                issues = issuePage,
                issues_truncated = graph.IssuesTruncated
                    || issuePage is not null && graph.Issues.Count > issuePage.Length,
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            });
        }
        catch (WordReferenceLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "Reference graph exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordReferenceProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a Word reference graph",
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

    private static IReadOnlyList<object> ReferenceItems(
        WordReferenceGraph graph,
        string view,
        string detail,
        bool includeSensitive,
        bool includeResultText,
        bool includeSource,
        string? fieldType,
        string? bookmarkName,
        string? storyId
    )
    {
        var fields = graph.Fields.Where(field =>
                (fieldType is null || string.Equals(
                    field.FieldType,
                    fieldType,
                    StringComparison.OrdinalIgnoreCase
                ))
                && (storyId is null || string.Equals(
                    field.StoryId,
                    storyId,
                    StringComparison.Ordinal
                ))
            )
            .ToArray();
        var fieldIds = fields.Select(field => field.Id)
            .ToHashSet(StringComparer.Ordinal);
        return view switch
        {
            "summary" => fields.GroupBy(field => new
            {
                Type = field.FieldType ?? "(unknown)",
                field.Classification,
            })
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key.Type, StringComparer.Ordinal)
                .Select(group => (object)new
                {
                    field_type = group.Key.Type,
                    classification = ToSnakeCase(
                        group.Key.Classification.ToString()
                    ),
                    count = group.Count(),
                    incomplete_count = group.Count(field =>
                        field.Status != WordFieldStatus.Complete
                        || !field.InstructionParseComplete
                    ),
                    external_count = group.Count(field =>
                        field.RequiresExternalAccess
                    ),
                    application_invoking_count = group.Count(field =>
                        field.MayInvokeApplication
                    ),
                })
                .ToArray(),
            "stories" => graph.Stories.Where(story =>
                    storyId is null || string.Equals(
                        story.Id,
                        storyId,
                        StringComparison.Ordinal
                    )
                )
                .Select(story => (object)new
                {
                    story_id = story.Id,
                    kind = ToSnakeCase(story.Kind.ToString()),
                    ooxml_key = BoundForResponse(story.OoxmlKey, 128),
                    field_count = graph.Fields.Count(field =>
                        field.StoryId == story.Id
                    ),
                    bookmark_count = graph.Bookmarks.Count(bookmark =>
                        bookmark.StoryId == story.Id
                    ),
                    part_uri = includeSource
                        ? BoundForResponse(story.PartUri, 512)
                        : null,
                    root_element_ordinal = includeSource
                        ? story.RootElementOrdinal
                        : (int?)null,
                })
                .ToArray(),
            "bookmarks" => graph.Bookmarks.Where(bookmark =>
                    (storyId is null || string.Equals(
                        bookmark.StoryId,
                        storyId,
                        StringComparison.Ordinal
                    ))
                    && (bookmarkName is null || string.Equals(
                        bookmark.Name,
                        bookmarkName,
                        StringComparison.OrdinalIgnoreCase
                    ))
                )
                .Select(bookmark => BookmarkItem(
                    bookmark,
                    includeSensitive,
                    includeSource
                ))
                .ToArray(),
            "fields" => fields.Select(field => FieldItem(
                    field,
                    detail,
                    includeSensitive,
                    includeResultText,
                    includeSource
                ))
                .ToArray(),
            "dependencies" => graph.Edges.Where(edge =>
                    fieldIds.Contains(edge.SourceFieldId)
                    && (bookmarkName is null || edge.TargetKind ==
                        WordReferenceTargetKind.Bookmark
                        && string.Equals(
                            edge.TargetKey,
                            bookmarkName,
                            StringComparison.OrdinalIgnoreCase
                        ))
                )
                .Select(edge => DependencyItem(edge, includeSensitive))
                .ToArray(),
            _ => graph.Issues.Where(issue =>
                    storyId is null || string.Equals(
                        issue.StoryId,
                        storyId,
                        StringComparison.Ordinal
                    )
                )
                .Select(issue => ReferenceIssueItem(issue, includeSource))
                .ToArray(),
        };
    }

    private static object BookmarkItem(
        WordBookmarkDefinition bookmark,
        bool includeSensitive,
        bool includeSource
    ) => new
    {
        bookmark_id = bookmark.Id,
        story_id = bookmark.StoryId,
        status = ToSnakeCase(bookmark.Status.ToString()),
        effective_by_name = bookmark.IsEffectiveByName,
        name = includeSensitive ? BoundForResponse(bookmark.Name, 4_096) : null,
        name_redacted = includeSensitive || bookmark.Name is null ? (bool?)null : true,
        name_character_count = bookmark.Name?.Length ?? 0,
        name_fingerprint = FingerprintSensitiveValue(bookmark.Name),
        has_table_column_range = bookmark.ColumnFirst is not null
            || bookmark.ColumnLast is not null,
        column_first = bookmark.ColumnFirst,
        column_last = bookmark.ColumnLast,
        ooxml_id = includeSource ? BoundForResponse(bookmark.OoxmlId, 128) : null,
        part_uri = includeSource
            ? BoundForResponse(bookmark.PartUri, 512)
            : null,
        start_element_ordinal = includeSource
            ? bookmark.StartElementOrdinal
            : (int?)null,
        end_element_ordinal = includeSource
            ? bookmark.EndElementOrdinal
            : null,
        start_node_id = includeSource ? bookmark.StartNodeId?.Value : null,
        end_node_id = includeSource ? bookmark.EndNodeId?.Value : null,
    };

    private static object FieldItem(
        WordFieldDefinition field,
        string detail,
        bool includeSensitive,
        bool includeResultText,
        bool includeSource
    )
    {
        var includeParsed = detail == "parsed";
        return new
        {
            field_id = field.Id,
            story_id = field.StoryId,
            kind = ToSnakeCase(field.Kind.ToString()),
            status = ToSnakeCase(field.Status.ToString()),
            field_type = BoundForResponse(field.FieldType, 128),
            implicit_ref = field.IsImplicitReference,
            classification = ToSnakeCase(field.Classification.ToString()),
            parent_field_id = field.ParentFieldId,
            child_field_ids = field.ChildFieldIds.Count == 0
                ? null
                : field.ChildFieldIds.Take(64).ToArray(),
            child_field_ids_truncated = field.ChildFieldIds.Count > 64
                ? true
                : (bool?)null,
            dirty = field.IsDirty,
            locked = field.IsLocked,
            inside_deleted_content = field.IsInDeletedContent,
            has_separator = field.HasSeparator,
            dynamic_instruction = field.HasDynamicInstruction,
            instruction_parse_complete = field.InstructionParseComplete,
            instruction_character_count = field.InstructionCharacterCount,
            instruction_fragment_count = field.InstructionFragmentCount,
            instruction_fingerprint = FingerprintSensitiveValue(field.Instruction),
            instruction = includeParsed && includeSensitive
                ? BoundForResponse(field.Instruction, 4_096)
                : null,
            instruction_response_truncated = includeParsed
                && includeSensitive
                && field.Instruction.Length > 4_096
                    ? true
                    : (bool?)null,
            instruction_redacted = includeSensitive || field.Instruction.Length == 0
                ? (bool?)null
                : true,
            token_count = field.Tokens.Count,
            tokens = includeParsed
                ? field.Tokens.Take(128).Select(token => new
                {
                    kind = ToSnakeCase(token.Kind.ToString()),
                    value = includeSensitive
                        ? BoundForResponse(token.Value, 512)
                        : null,
                    value_fingerprint = FingerprintSensitiveValue(token.Value),
                    character_offset = token.CharacterOffset,
                    character_length = token.CharacterLength,
                }).ToArray()
                : null,
            tokens_truncated = includeParsed && field.Tokens.Count > 128
                ? true
                : (bool?)null,
            result_character_count = field.ResultCharacterCount,
            result_capture_truncated = field.ResultTruncated,
            result_text = includeResultText
                ? BoundForResponse(field.ResultText, 4_096)
                : null,
            result_response_truncated = includeResultText
                && field.ResultText.Length > 4_096
                    ? true
                    : (bool?)null,
            requires_external_access = field.RequiresExternalAccess,
            may_invoke_application = field.MayInvokeApplication,
            part_uri = includeSource ? BoundForResponse(field.PartUri, 512) : null,
            start_element_ordinal = includeSource
                ? field.StartElementOrdinal
                : (int?)null,
            separator_element_ordinal = includeSource
                ? field.SeparatorElementOrdinal
                : null,
            end_element_ordinal = includeSource
                ? field.EndElementOrdinal
                : null,
            start_node_id = includeSource ? field.StartNodeId?.Value : null,
            separator_node_id = includeSource ? field.SeparatorNodeId?.Value : null,
            end_node_id = includeSource ? field.EndNodeId?.Value : null,
        };
    }

    private static object DependencyItem(
        WordReferenceEdge edge,
        bool includeSensitive
    ) => new
    {
        dependency_id = edge.Id,
        source_field_id = edge.SourceFieldId,
        kind = ToSnakeCase(edge.Kind.ToString()),
        target_kind = ToSnakeCase(edge.TargetKind.ToString()),
        target_key = includeSensitive
            ? BoundForResponse(edge.TargetKey, 4_096)
            : null,
        target_key_redacted = includeSensitive ? (bool?)null : true,
        target_key_character_count = edge.TargetKey.Length,
        target_key_fingerprint = FingerprintSensitiveValue(edge.TargetKey),
        resolved = edge.IsResolved,
        external = edge.IsExternal,
        resolved_bookmark_id = edge.ResolvedBookmarkId,
    };

    private static object ReferenceIssueItem(
        WordReferenceIssue issue,
        bool includeSource
    ) => new
    {
        code = BoundForResponse(issue.Code, 128),
        severity = ToSnakeCase(issue.Severity.ToString()),
        message = BoundForResponse(issue.Message, 512),
        subject_id = BoundForResponse(issue.SubjectId, 128),
        story_id = includeSource ? BoundForResponse(issue.StoryId, 128) : null,
        part_uri = includeSource ? BoundForResponse(issue.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? issue.SourceElementOrdinal
            : null,
    };

    private static string? BoundedOptionalArgument(
        JsonElement arguments,
        string name,
        int maximumLength
    )
    {
        var value = OptionalString(arguments, name);
        if (value is not null && (value.Length == 0 || value.Length > maximumLength))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} must contain between 1 and {maximumLength} characters"
            );
        }
        return value;
    }

    private static string? FingerprintSensitiveValue(string? value)
    {
        if (value is null)
        {
            return null;
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant()[..16];
    }
}
