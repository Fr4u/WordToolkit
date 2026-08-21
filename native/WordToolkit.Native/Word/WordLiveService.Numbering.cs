using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> InspectPackageNumberingAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        RequireObject(arguments, "OOXML numbering inspection arguments");
        var allowedArguments = new Dictionary<string, JsonValueKind>(StringComparer.Ordinal)
        {
            ["local_path"] = JsonValueKind.String,
            ["view"] = JsonValueKind.String,
            ["number_id"] = JsonValueKind.Number,
            ["abstract_number_id"] = JsonValueKind.Number,
            ["level_index"] = JsonValueKind.Number,
            ["story_kind"] = JsonValueKind.String,
            ["paragraph_node_id"] = JsonValueKind.String,
            ["offset"] = JsonValueKind.Number,
            ["max_items"] = JsonValueKind.Number,
            ["detail"] = JsonValueKind.String,
            ["include_issues"] = JsonValueKind.True,
            ["include_source"] = JsonValueKind.True,
        };
        foreach (var property in arguments.EnumerateObject())
        {
            if (!allowedArguments.TryGetValue(property.Name, out var expectedKind))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "inspect_ooxml_numbering received an unknown argument",
                    new { argument = property.Name }
                );
            }
            var validKind = expectedKind == JsonValueKind.True
                ? property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False
                : property.Value.ValueKind == expectedKind;
            if (!validKind)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"{property.Name} has the wrong JSON type"
                );
            }
        }
        var path = ResolveInspectablePackagePath(arguments);
        var view = arguments.String("view", "instances");
        if (
            view is not "instances"
                and not "abstracts"
                and not "resolved_level"
                and not "sequences"
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "view must be instances, abstracts, resolved_level, or sequences"
            );
        }

        var detail = arguments.String("detail", "metadata");
        if (detail is not "metadata" and not "levels" and not "declared")
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "detail must be metadata, levels, or declared"
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

        var numberId = OptionalNonNegativeInt(arguments, "number_id");
        var abstractNumberId = OptionalNonNegativeInt(
            arguments,
            "abstract_number_id"
        );
        var levelIndex = OptionalNonNegativeInt(arguments, "level_index");
        if (
            view == "resolved_level"
            && (numberId is null or 0 || levelIndex is null or > 8)
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "resolved_level requires a positive number_id and level_index from 0 through 8"
            );
        }

        var storyKind = BoundedOptionalArgument(arguments, "story_kind", 32);
        if (
            storyKind is not null
            && !Enum.GetValues<WordStoryKind>().Any(kind =>
                string.Equals(
                    ToSnakeCase(kind.ToString()),
                    storyKind,
                    StringComparison.Ordinal
                )
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "story_kind is not a supported Word story kind"
            );
        }
        var paragraphNodeId = BoundedOptionalArgument(
            arguments,
            "paragraph_node_id",
            SemanticNodeId.MaximumCharacters
        );
        if (
            paragraphNodeId is not null
            && !SemanticNodeId.HasValidSyntax(paragraphNodeId)
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "paragraph_node_id is not a valid semantic node ID"
            );
        }
        if (
            view != "sequences"
            && (storyKind is not null || paragraphNodeId is not null)
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "story_kind and paragraph_node_id are valid only for sequences"
            );
        }

        var includeIssues = arguments.Boolean("include_issues", true);
        var includeSource = arguments.Boolean("include_source", false);
        try
        {
            var package = new OpcPackageReader().Read(path, cancellationToken);
            var semantic = new WordSemanticProjector().Project(
                package,
                cancellationToken
            );
            var styles = new WordStyleGraphBuilder().Build(
                package,
                semantic,
                cancellationToken
            );
            var graph = new WordNumberingGraphBuilder().Build(
                package,
                semantic,
                styles,
                cancellationToken
            );
            var sequenceGraph = view == "sequences"
                ? new WordListSequenceGraphBuilder().Build(
                    package,
                    semantic,
                    styles,
                    graph,
                    cancellationToken
                )
                : null;
            var items = view switch
            {
                "instances" => NumberingInstances(
                    graph,
                    numberId,
                    (int)offset,
                    (int)maximum,
                    detail,
                    includeSource
                ),
                "abstracts" => NumberingAbstracts(
                    graph,
                    abstractNumberId,
                    (int)offset,
                    (int)maximum,
                    detail,
                    includeSource
                ),
                "sequences" => NumberingSequences(
                    sequenceGraph!,
                    numberId,
                    levelIndex,
                    storyKind,
                    paragraphNodeId,
                    (int)offset,
                    (int)maximum,
                    detail,
                    includeSource
                ),
                _ => Array.Empty<object>(),
            };
            var matchedCount = view switch
            {
                "instances" => numberId is null
                    ? graph.Instances.Count
                    : graph.Instances.Count(item => item.NumberId == numberId),
                "abstracts" => abstractNumberId is null
                    ? graph.AbstractDefinitions.Count
                    : graph.AbstractDefinitions.Count(item =>
                        item.AbstractNumberId == abstractNumberId
                    ),
                "sequences" => FilterNumberingSequences(
                    sequenceGraph!,
                    numberId,
                    levelIndex,
                    storyKind,
                    paragraphNodeId
                ).Count(),
                _ => 1,
            };
            var consumed = (long)offset + items.Length;
            var returnedIssues = includeIssues
                ? graph.Issues.Take(40).Select(issue => new
                {
                    code = BoundForResponse(issue.Code, 128),
                    severity = ToSnakeCase(issue.Severity.ToString()),
                    abstract_number_id = issue.AbstractNumberId,
                    number_id = issue.NumberId,
                    level_index = issue.LevelIndex,
                    message = BoundForResponse(issue.Message, 512),
                }).ToArray()
                : null;
            var returnedSequenceIssues = includeIssues && sequenceGraph is not null
                ? sequenceGraph.Issues.Take(40).Select(issue => new
                {
                    code = BoundForResponse(issue.Code, 128),
                    severity = ToSnakeCase(issue.Severity.ToString()),
                    paragraph_node_id = issue.ParagraphNodeId?.Value,
                    story_id = includeSource
                        ? BoundForResponse(issue.StoryId, 128)
                        : null,
                    number_id = issue.NumberId,
                    level_index = issue.LevelIndex,
                    message = BoundForResponse(issue.Message, 512),
                }).ToArray()
                : null;
            return Task.FromResult<object>(new
            {
                operation_contract = "wordtoolkit.inspect_ooxml_numbering/1.0",
                file_name = Path.GetFileName(path),
                package_fingerprint = graph.PackageFingerprint,
                main_part_uri = graph.MainPartUri,
                numbering_part_uri = graph.NumberingPartUri,
                has_numbering_part = graph.HasNumberingPart,
                abstract_definition_count = graph.AbstractDefinitions.Count,
                instance_count = graph.Instances.Count,
                picture_bullet_count = graph.PictureBullets.Count,
                last_assigned_number_id = graph.LastAssignedNumberId,
                view,
                detail,
                matched_item_count = matchedCount,
                offset = view == "resolved_level" ? null : (long?)offset,
                returned_item_count = view == "resolved_level"
                    ? (int?)null
                    : items.Length,
                next_offset = view != "resolved_level" && consumed < matchedCount
                    ? (int)consumed
                    : (int?)null,
                items = view == "resolved_level" ? null : items,
                resolved_level = view == "resolved_level"
                    ? ResolvedNumberingLevel(
                        graph.ResolveLevel(numberId!.Value, levelIndex!.Value),
                        detail,
                        includeSource
                    )
                    : null,
                sequence_analysis = sequenceGraph is null
                    ? null
                    : new
                    {
                        execution_profile = "microsoft_word_compatibility",
                        examined_paragraph_count = sequenceGraph.ExaminedParagraphCount,
                        numbered_paragraph_count = sequenceGraph.NumberedParagraphCount,
                        sequence_item_count = sequenceGraph.Items.Count,
                        skipped_numbered_paragraph_count = sequenceGraph.SkippedNumberedParagraphCount,
                        exact_counter_count = sequenceGraph.ExactCounterCount,
                        exact_label_count = sequenceGraph.ExactLabelCount,
                        analysis_execution_complete = sequenceGraph.AnalysisExecutionComplete,
                        counter_coverage_complete = sequenceGraph.CounterCoverageComplete,
                        label_coverage_complete = sequenceGraph.LabelCoverageComplete,
                        word_specific_rules = new[]
                        {
                            "higher_level_restart_cascade",
                            "override_level_start_precedes_start_override_on_qualified_word_build",
                            "override_level_restart_ignored",
                            "restart_numbering_after_section_break_extension",
                            "invalid_higher_level_placeholder_rejects_entire_label",
                            "legal_numbering_uses_decimal_placeholders",
                        },
                    },
                issue_count = graph.Issues.Count,
                issues = returnedIssues,
                issues_truncated = returnedIssues is not null
                    && graph.Issues.Count > returnedIssues.Length
                        ? true
                        : (bool?)null,
                sequence_issue_count = sequenceGraph?.Issues.Count,
                sequence_issues = returnedSequenceIssues,
                sequence_issues_truncated = returnedSequenceIssues is not null
                    && sequenceGraph!.Issues.Count > returnedSequenceIssues.Length
                        ? true
                        : (bool?)null,
                unmodeled_root_elements = graph.UnmodeledRootElements.Count == 0
                    ? null
                    : graph.UnmodeledRootElements.Take(40)
                        .Select(value => BoundForResponse(value, 256))
                        .ToArray(),
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            });
        }
        catch (WordNumberingLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "Numbering graph exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordListSequenceLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "List-sequence analysis exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordListSequenceProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into Word-compatible list sequences",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordNumberingResolutionException exception)
        {
            throw new NativeToolException(
                "NUMBERING_UNRESOLVED",
                "The requested numbering level cannot be resolved safely",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordNumberingProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a Word numbering graph",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordStyleLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "Style graph exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordStyleProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a Word style graph",
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

    private static object[] NumberingInstances(
        WordNumberingGraph graph,
        int? numberId,
        int offset,
        int maximum,
        string detail,
        bool includeSource
    ) => graph.Instances
        .Where(item => numberId is null || item.NumberId == numberId)
        .Skip(offset)
        .Take(maximum)
        .Select(item =>
        {
            graph.TryGetAbstractResolution(item.AbstractNumberId, out var resolution);
            return (object)new
            {
                number_id = item.NumberId,
                abstract_number_id = item.AbstractNumberId,
                effective_abstract_number_id = resolution?.EffectiveAbstractNumberId,
                resolvable = resolution?.Resolvable ?? false,
                resolution_failure = resolution?.Resolvable == false
                    ? BoundForResponse(resolution.Failure, 512)
                    : null,
                override_count = item.LevelOverrides.Count,
                overrides = detail is "levels" or "declared"
                    ? item.LevelOverrides.Take(9).Select(levelOverride => new
                    {
                        level_index = levelOverride.LevelIndex,
                        start_override = levelOverride.StartOverride,
                        replaces_level = levelOverride.Level is not null,
                        level = levelOverride.Level is null
                            ? null
                            : NumberingLevel(levelOverride.Level, detail, includeSource),
                        source_element_ordinal = includeSource
                            ? levelOverride.SourceElementOrdinal
                            : (int?)null,
                    }).ToArray()
                    : null,
                abstract_number_chain = detail != "metadata"
                    && resolution?.AbstractNumberChain.Count > 1
                        ? resolution.AbstractNumberChain.ToArray()
                        : null,
                numbering_style_chain = detail != "metadata"
                    && resolution?.NumberingStyleChain.Count > 0
                        ? resolution.NumberingStyleChain
                            .Select(value => BoundForResponse(value, 253))
                            .ToArray()
                        : null,
                unmodeled_elements = detail == "declared"
                    && item.UnmodeledElements.Count != 0
                        ? item.UnmodeledElements.Take(40)
                            .Select(value => BoundForResponse(value, 256))
                            .ToArray()
                        : null,
                source_element_ordinal = includeSource
                    ? item.SourceElementOrdinal
                    : (int?)null,
            };
        })
        .ToArray();

    private static object[] NumberingAbstracts(
        WordNumberingGraph graph,
        int? abstractNumberId,
        int offset,
        int maximum,
        string detail,
        bool includeSource
    ) => graph.AbstractDefinitions
        .Where(item =>
            abstractNumberId is null || item.AbstractNumberId == abstractNumberId
        )
        .Skip(offset)
        .Take(maximum)
        .Select(item =>
        {
            graph.TryGetAbstractResolution(item.AbstractNumberId, out var resolution);
            return (object)new
            {
                abstract_number_id = item.AbstractNumberId,
                effective_abstract_number_id = resolution?.EffectiveAbstractNumberId,
                resolvable = resolution?.Resolvable ?? false,
                resolution_failure = resolution?.Resolvable == false
                    ? BoundForResponse(resolution.Failure, 512)
                    : null,
                namespace_id = BoundForResponse(item.NamespaceId, 64),
                multi_level_type = BoundForResponse(item.MultiLevelType, 64),
                name = BoundForResponse(item.Name, 512),
                template_code = BoundForResponse(item.TemplateCode, 64),
                numbering_style_link_id = BoundForResponse(
                    item.NumberingStyleLinkId,
                    253
                ),
                style_link_id = BoundForResponse(item.StyleLinkId, 253),
                level_count = item.Levels.Count,
                levels = detail is "levels" or "declared"
                    ? item.Levels.Take(9)
                        .Select(level => NumberingLevel(level, detail, includeSource))
                        .ToArray()
                    : null,
                abstract_number_chain = detail != "metadata"
                    && resolution?.AbstractNumberChain.Count > 1
                        ? resolution.AbstractNumberChain.ToArray()
                        : null,
                numbering_style_chain = detail != "metadata"
                    && resolution?.NumberingStyleChain.Count > 0
                        ? resolution.NumberingStyleChain
                            .Select(value => BoundForResponse(value, 253))
                            .ToArray()
                        : null,
                unmodeled_elements = detail == "declared"
                    && item.UnmodeledElements.Count != 0
                        ? item.UnmodeledElements.Take(40)
                            .Select(value => BoundForResponse(value, 256))
                            .ToArray()
                        : null,
                source_element_ordinal = includeSource
                    ? item.SourceElementOrdinal
                    : (int?)null,
            };
        })
        .ToArray();

    private static IEnumerable<WordListSequenceItem> FilterNumberingSequences(
        WordListSequenceGraph graph,
        int? numberId,
        int? levelIndex,
        string? storyKind,
        string? paragraphNodeId
    ) => graph.Items.Where(item =>
        (numberId is null || item.NumberId == numberId)
        && (levelIndex is null || item.LevelIndex == levelIndex)
        && (
            storyKind is null
            || string.Equals(
                ToSnakeCase(item.StoryKind.ToString()),
                storyKind,
                StringComparison.Ordinal
            )
        )
        && (
            paragraphNodeId is null
            || string.Equals(
                item.ParagraphNodeId.Value,
                paragraphNodeId,
                StringComparison.Ordinal
            )
        )
    );

    private static object[] NumberingSequences(
        WordListSequenceGraph graph,
        int? numberId,
        int? levelIndex,
        string? storyKind,
        string? paragraphNodeId,
        int offset,
        int maximum,
        string detail,
        bool includeSource
    ) => FilterNumberingSequences(
        graph,
        numberId,
        levelIndex,
        storyKind,
        paragraphNodeId
    )
        .Skip(offset)
        .Take(maximum)
        .Select(item => (object)new
        {
            item_id = item.Id,
            sequence_id = item.SequenceId,
            sequence_index = item.SequenceIndex,
            paragraph_node_id = item.ParagraphNodeId.Value,
            story_kind = ToSnakeCase(item.StoryKind.ToString()),
            number_id = item.NumberId,
            requested_abstract_number_id = item.RequestedAbstractNumberId,
            effective_abstract_number_id = item.EffectiveAbstractNumberId,
            level_index = item.LevelIndex,
            counter_value = item.CounterValue,
            counter_status = ToSnakeCase(item.CounterStatus.ToString()),
            counter_exact = item.CounterExact,
            continuation = ToSnakeCase(item.ContinuationKind.ToString()),
            restart_trigger_paragraph_node_id = item.RestartTriggerParagraphNodeId?.Value,
            label = BoundForResponse(item.Label, 64),
            label_status = ToSnakeCase(item.LabelStatus.ToString()),
            label_exact = item.LabelExact,
            suffix = BoundForResponse(item.Suffix, 32),
            legal_numbering = item.LegalNumbering,
            picture_bullet_id = item.PictureBulletId,
            components = detail is "levels" or "declared"
                ? item.Components.Select(component => new
                {
                    level_index = component.LevelIndex,
                    value = component.Value,
                    number_format = BoundForResponse(component.NumberFormat, 128),
                    formatted_value = BoundForResponse(component.FormattedValue, 64),
                    exact = component.Exact,
                }).ToArray()
                : null,
            compatibility_warnings = item.CompatibilityWarnings.Count == 0
                ? null
                : item.CompatibilityWarnings.Take(16)
                    .Select(value => BoundForResponse(value, 128))
                    .ToArray(),
            story_id = includeSource ? BoundForResponse(item.StoryId, 128) : null,
            source_part_uri = includeSource
                ? BoundForResponse(item.SourcePartUri, 512)
                : null,
            source_order = includeSource ? item.SourceOrder : (int?)null,
            source_element_ordinal = includeSource
                ? item.SourceElementOrdinal
                : (int?)null,
        })
        .ToArray();

    private static object ResolvedNumberingLevel(
        WordResolvedNumberingLevel resolved,
        string detail,
        bool includeSource
    ) => new
    {
        number_id = resolved.NumberId,
        requested_abstract_number_id = resolved.RequestedAbstractNumberId,
        effective_abstract_number_id = resolved.EffectiveAbstractNumberId,
        level_index = resolved.LevelIndex,
        level_source = ToSnakeCase(resolved.LevelSourceKind.ToString()),
        effective_start = resolved.EffectiveStart,
        start_source = ToSnakeCase(resolved.StartSourceKind.ToString()),
        abstract_number_chain = resolved.AbstractNumberChain.Count > 1
            ? resolved.AbstractNumberChain.ToArray()
            : null,
        numbering_style_chain = resolved.NumberingStyleChain.Count == 0
            ? null
            : resolved.NumberingStyleChain
                .Select(value => BoundForResponse(value, 253))
                .ToArray(),
        level = NumberingLevel(resolved.Level, detail, includeSource),
        source_element_ordinal = includeSource
            ? resolved.SourceElementOrdinal
            : (int?)null,
        start_source_element_ordinal = includeSource
            ? resolved.StartSourceElementOrdinal
            : null,
    };

    private static object NumberingLevel(
        WordNumberingLevelDefinition level,
        string detail,
        bool includeSource
    ) => new
    {
        level_index = level.LevelIndex,
        start = level.Start,
        number_format = BoundForResponse(level.NumberFormat, 128),
        custom_number_format = BoundForResponse(level.CustomNumberFormat, 256),
        restart_after_level = level.RestartAfterLevel,
        paragraph_style_id = BoundForResponse(level.ParagraphStyleId, 253),
        legal_numbering = level.IsLegal,
        suffix = BoundForResponse(level.Suffix, 64),
        level_text = BoundForResponse(level.LevelText, 512),
        level_text_is_null = level.LevelTextIsNull,
        picture_bullet_id = level.PictureBulletId,
        justification = BoundForResponse(level.Justification, 64),
        template_code = BoundForResponse(level.TemplateCode, 64),
        tentative = level.Tentative,
        fully_modeled = level.IsFullyModeled,
        declared_properties = detail == "declared"
            ? new
            {
                paragraph = FormattingBlock(level.ParagraphProperties),
                run = FormattingBlock(level.RunProperties),
            }
            : null,
        unmodeled_elements = detail == "declared"
            && level.UnmodeledElements.Count != 0
                ? level.UnmodeledElements.Take(40)
                    .Select(value => BoundForResponse(value, 256))
                    .ToArray()
                : null,
        source_element_ordinal = includeSource
            ? level.SourceElementOrdinal
            : (int?)null,
    };

    private static int? OptionalNonNegativeInt(
        JsonElement arguments,
        string propertyName
    )
    {
        var value = arguments.NullableInt64(propertyName);
        if (value is null)
        {
            return null;
        }

        if (value is < 0 or > int.MaxValue)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{propertyName} must be between 0 and 2147483647"
            );
        }

        return (int)value.Value;
    }
}
