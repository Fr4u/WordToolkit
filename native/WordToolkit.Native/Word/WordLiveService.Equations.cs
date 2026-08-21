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
    private static Task<object> InspectPackageEquationsAsync(
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
                and not "equations"
                and not "nodes"
                and not "paragraphs"
                and not "settings"
                and not "issues"
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "view must be summary, equations, nodes, paragraphs, settings, or issues"
            );
        }
        var detail = arguments.String("detail", "metadata");
        if (detail is not "metadata" and not "properties")
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "detail must be metadata or properties"
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
        var equationId = BoundedOptionalArgument(arguments, "equation_id", 128);
        var nodeKind = BoundedOptionalArgument(arguments, "node_kind", 128)
            ?.ToLowerInvariant();
        var storyKind = BoundedOptionalArgument(arguments, "story_kind", 128)
            ?.ToLowerInvariant();
        try
        {
            var package = new OpcPackageReader().Read(path, cancellationToken);
            var semantic = new WordSemanticProjector().Project(
                package,
                cancellationToken
            );
            var graph = new WordEquationGraphBuilder().Build(
                package,
                semantic,
                cancellationToken
            );
            if (
                equationId is not null
                && !graph.TryGetEquation(equationId, out _)
            )
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "equation_id does not exist in this package fingerprint"
                );
            }
            if (
                nodeKind is not null
                && !Enum.GetValues<WordMathNodeKind>()
                    .Any(kind => ToSnakeCase(kind.ToString()) == nodeKind)
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "node_kind is not a recognized canonical OfficeMath node kind"
                );
            }
            if (
                storyKind is not null
                && !Enum.GetValues<WordStoryKind>()
                    .Any(kind => ToSnakeCase(kind.ToString()) == storyKind)
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "story_kind is not a recognized Word story kind"
                );
            }
            if (nodeKind is not null && view != "nodes")
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "node_kind is valid only when view=nodes"
                );
            }
            if (
                view == "settings"
                && (equationId is not null || storyKind is not null)
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "equation_id and story_kind do not apply when view=settings"
                );
            }

            var page = EquationItems(
                graph,
                view,
                detail,
                includeSensitive,
                includeSource,
                (int)previewCharacters,
                equationId,
                nodeKind,
                storyKind,
                (int)offset,
                (int)maximum
            );
            var consumed = (long)offset + page.Items.Count;
            var relatedIssues = RelatedEquationIssues(
                graph,
                equationId,
                storyKind
            );
            var issuePage = includeIssues && view != "issues"
                ? relatedIssues.Take(40)
                    .Select(issue => EquationIssueItem(issue, includeSource))
                    .ToArray()
                : null;
            return Task.FromResult<object>(new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = graph.PackageFingerprint,
                main_part_uri = includeSource
                    ? BoundForResponse(graph.MainPartUri, 512)
                    : null,
                equation_count = graph.Equations.Count,
                display_equation_count = graph.DisplayEquationCount,
                inline_equation_count = graph.InlineEquationCount,
                canonical_equation_count = graph.Equations.Count(equation =>
                    equation.IsCanonical
                ),
                malformed_equation_count = graph.MalformedEquationCount,
                unsupported_equation_count = graph.UnsupportedEquationCount,
                math_paragraph_count = graph.MathParagraphs.Count,
                math_node_count = graph.NodeCount,
                maximum_equation_depth = graph.Equations.Count == 0
                    ? 0
                    : graph.Equations.Max(equation => equation.MaximumDepth),
                equation_text_character_count = graph.Equations.Sum(equation =>
                    (long)equation.TextCharacterCount
                ),
                math_settings_present = graph.Settings is not null,
                execution_policy = "parse_only_no_word_no_conversion_no_external_access",
                word_opened = false,
                conversion_performed = false,
                raw_omml_returned = false,
                external_content_followed = false,
                view,
                detail,
                sensitive_text_included = includeSensitive
                    && previewCharacters > 0,
                equation_id_filter = equationId,
                node_kind_filter = nodeKind,
                story_kind_filter = storyKind,
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
        catch (WordEquationLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "Equation graph exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordEquationProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a Word equation graph",
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

    private static EquationResponsePage EquationItems(
        WordEquationGraph graph,
        string view,
        string detail,
        bool includeSensitive,
        bool includeSource,
        int previewCharacters,
        string? equationId,
        string? nodeKind,
        string? storyKind,
        int offset,
        int maximum
    )
    {
        var equations = graph.Equations.Where(equation =>
                (equationId is null || equation.Id == equationId)
                && (storyKind is null
                    || ToSnakeCase(equation.StoryKind.ToString()) == storyKind)
            )
            .ToArray();
        return view switch
        {
            "summary" => PageItems(
                equations.GroupBy(equation => new
                {
                    equation.IsDisplay,
                    equation.Status,
                    equation.StoryKind,
                })
                    .OrderBy(group => group.Key.StoryKind)
                    .ThenByDescending(group => group.Key.IsDisplay)
                    .ThenBy(group => group.Key.Status),
                offset,
                maximum,
                group => new
                {
                    story_kind = ToSnakeCase(group.Key.StoryKind.ToString()),
                    display = group.Key.IsDisplay,
                    status = ToSnakeCase(group.Key.Status.ToString()),
                    count = group.Count(),
                    node_count = group.Sum(equation => equation.NodeCount),
                    text_character_count = group.Sum(equation =>
                        (long)equation.TextCharacterCount
                    ),
                }
            ),
            "equations" => PageItems(
                equations,
                offset,
                maximum,
                equation => EquationItem(
                    equation,
                    detail,
                    includeSensitive,
                    includeSource,
                    previewCharacters
                )
            ),
            "nodes" => PageItems(
                equations.SelectMany(equation =>
                    equation.Root.DescendantsAndSelf().Select(node =>
                        (Equation: equation, Node: node)
                    )
                ).Where(item =>
                    nodeKind is null
                        || ToSnakeCase(item.Node.Kind.ToString()) == nodeKind
                ),
                offset,
                maximum,
                item => MathNodeItem(
                    item.Equation,
                    item.Node,
                    detail,
                    includeSensitive,
                    includeSource,
                    previewCharacters
                )
            ),
            "paragraphs" => PageItems(
                graph.MathParagraphs.Where(paragraph =>
                    (storyKind is null
                        || ToSnakeCase(paragraph.StoryKind.ToString()) == storyKind)
                    && (equationId is null
                        || paragraph.EquationIds.Contains(
                            equationId,
                            StringComparer.Ordinal
                        ))
                ),
                offset,
                maximum,
                paragraph => new
                {
                    math_paragraph_id = paragraph.Id,
                    story_kind = ToSnakeCase(paragraph.StoryKind.ToString()),
                    justification = BoundForResponse(paragraph.Justification, 64),
                    equation_count = paragraph.EquationIds.Count,
                    equation_ids = paragraph.EquationIds.Take(64).ToArray(),
                    equation_ids_truncated = paragraph.EquationIds.Count > 64
                        ? true
                        : (bool?)null,
                    part_uri = includeSource
                        ? BoundForResponse(paragraph.PartUri, 512)
                        : null,
                    source_element_ordinal = includeSource
                        ? paragraph.SourceElementOrdinal
                        : (int?)null,
                    source_path = includeSource
                        ? BoundForResponse(paragraph.SourcePath, 1024)
                        : null,
                    semantic_node_id = includeSource
                        ? paragraph.SemanticNodeId?.Value
                        : null,
                    paragraph_node_id = includeSource
                        ? paragraph.ParagraphNodeId?.Value
                        : null,
                    story_node_id = includeSource
                        ? paragraph.StoryNodeId?.Value
                        : null,
                }
            ),
            "settings" => PageItems(
                graph.Settings is null
                    ? Array.Empty<WordMathSettingsDefinition>()
                    : new[] { graph.Settings },
                offset,
                maximum,
                settings => new
                {
                    properties = BoundProperties(settings.Properties, 128),
                    property_count = settings.Properties.Count,
                    part_uri = includeSource
                        ? BoundForResponse(settings.PartUri, 512)
                        : null,
                    source_element_ordinal = includeSource
                        ? settings.SourceElementOrdinal
                        : (int?)null,
                }
            ),
            _ => PageItems(
                RelatedEquationIssues(graph, equationId, storyKind),
                offset,
                maximum,
                issue => EquationIssueItem(issue, includeSource)
            ),
        };
    }

    private static IReadOnlyList<WordEquationIssue> RelatedEquationIssues(
        WordEquationGraph graph,
        string? equationId,
        string? storyKind
    )
    {
        if (equationId is null && storyKind is null)
        {
            return graph.Issues;
        }
        var matchingIds = graph.Equations.Where(equation =>
                (equationId is null || equation.Id == equationId)
                && (storyKind is null
                    || ToSnakeCase(equation.StoryKind.ToString()) == storyKind)
            )
            .Select(equation => equation.Id)
            .ToHashSet(StringComparer.Ordinal);
        return graph.Issues.Where(issue =>
                issue.EquationId is not null
                && matchingIds.Contains(issue.EquationId)
            )
            .ToArray();
    }

    private static EquationResponsePage PageItems<T>(
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
            {
                items.Add(project(item));
            }
            matched++;
        }
        return new EquationResponsePage(matched, items.ToArray());
    }

    private static object EquationItem(
        WordEquationDefinition equation,
        string detail,
        bool includeSensitive,
        bool includeSource,
        int previewCharacters
    )
    {
        IReadOnlyDictionary<string, int>? objectKinds = null;
        if (detail == "properties")
        {
            objectKinds = equation.Root.DescendantsAndSelf()
                .Where(node => node.Kind is not WordMathNodeKind.Sequence
                    and not WordMathNodeKind.Text)
                .GroupBy(
                    node => ToSnakeCase(node.Kind.ToString()),
                    StringComparer.Ordinal
                )
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Count(),
                    StringComparer.Ordinal
                );
        }
        var preview = SensitivePreview(
            equation.Text,
            includeSensitive,
            previewCharacters
        );
        return new
        {
            equation_id = equation.Id,
            status = ToSnakeCase(equation.Status.ToString()),
            display = equation.IsDisplay,
            story_kind = ToSnakeCase(equation.StoryKind.ToString()),
            inside_deleted_content = equation.IsInDeletedContent,
            index_in_math_paragraph = equation.IndexInMathParagraph,
            math_paragraph_id = equation.MathParagraphId,
            node_count = equation.NodeCount,
            maximum_depth = equation.MaximumDepth,
            unsupported_node_count = equation.UnsupportedNodeCount,
            text_character_count = equation.TextCharacterCount,
            text_capture_truncated = equation.TextTruncated,
            text_fingerprint = TextFingerprint(equation.Text),
            text_preview = preview.Value,
            text_preview_truncated = preview.Truncated,
            object_kinds = objectKinds,
            root_node_id = equation.Root.Id,
            top_level_node_ids = detail == "properties"
                ? equation.Root.Children.Take(64).Select(node => node.Id).ToArray()
                : null,
            top_level_nodes_truncated = detail == "properties"
                && equation.Root.Children.Count > 64
                    ? true
                    : (bool?)null,
            part_uri = includeSource
                ? BoundForResponse(equation.PartUri, 512)
                : null,
            source_element_ordinal = includeSource
                ? equation.SourceElementOrdinal
                : (int?)null,
            source_path = includeSource
                ? BoundForResponse(equation.SourcePath, 1024)
                : null,
            semantic_node_id = includeSource ? equation.SemanticNodeId?.Value : null,
            paragraph_node_id = includeSource ? equation.ParagraphNodeId?.Value : null,
            story_node_id = includeSource ? equation.StoryNodeId?.Value : null,
        };
    }

    private static object MathNodeItem(
        WordEquationDefinition equation,
        WordMathNode node,
        string detail,
        bool includeSensitive,
        bool includeSource,
        int previewCharacters
    )
    {
        var preview = SensitivePreview(
            node.Text,
            includeSensitive,
            previewCharacters
        );
        return new
        {
            equation_id = equation.Id,
            node_id = node.Id,
            parent_node_id = node.ParentId,
            kind = ToSnakeCase(node.Kind.ToString()),
            source_name = BoundForResponse(node.SourceName, 128),
            role = BoundForResponse(node.Role, 128),
            depth = node.Depth,
            child_count = node.Children.Count,
            child_node_ids = detail == "properties"
                ? node.Children.Take(64).Select(child => child.Id).ToArray()
                : null,
            children_truncated = detail == "properties" && node.Children.Count > 64
                ? true
                : (bool?)null,
            properties = detail == "properties"
                ? BoundProperties(node.Properties, 128)
                : null,
            property_count = node.Properties.Count,
            text_character_count = node.Text?.Length ?? 0,
            text_fingerprint = TextFingerprint(node.Text),
            text_preview = preview.Value,
            text_preview_truncated = preview.Truncated,
            part_uri = includeSource ? BoundForResponse(node.PartUri, 512) : null,
            source_element_ordinal = includeSource
                ? node.SourceElementOrdinal
                : (int?)null,
            semantic_node_id = includeSource ? node.SemanticNodeId?.Value : null,
        };
    }

    private static object EquationIssueItem(
        WordEquationIssue issue,
        bool includeSource
    ) => new
    {
        code = BoundForResponse(issue.Code, 128),
        severity = ToSnakeCase(issue.Severity.ToString()),
        message = BoundForResponse(issue.Message, 512),
        equation_id = issue.EquationId,
        node_id = issue.NodeId,
        part_uri = includeSource ? BoundForResponse(issue.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? issue.SourceElementOrdinal
            : null,
    };

    private static (string? Value, bool? Truncated) SensitivePreview(
        string? value,
        bool includeSensitive,
        int maximumCharacters
    )
    {
        if (!includeSensitive || maximumCharacters == 0 || value is null)
        {
            return (null, null);
        }
        return value.Length <= maximumCharacters
            ? (value, false)
            : (value[..maximumCharacters], true);
    }

    private static string? TextFingerprint(string? value)
    {
        if (value is null)
        {
            return null;
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant()[..16];
    }

    private sealed record EquationResponsePage(
        int MatchedItemCount,
        IReadOnlyList<object> Items
    );
}
