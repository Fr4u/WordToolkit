using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> InspectPackageMarkupCompatibilityAsync(
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
                and not "parts"
                and not "namespaces"
                and not "rules"
                and not "alternate_content"
                and not "affected"
                and not "must_understand"
                and not "issues"
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "view must be summary, parts, namespaces, rules, alternate_content, affected, must_understand, or issues"
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

        var partId = BoundedOptionalArgument(arguments, "part_id", 128);
        if (partId is not null && !partId.StartsWith("wmcp_", StringComparison.Ordinal))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "part_id must be a WordToolkit MCE part ID"
            );
        }
        var understoodNamespaces = OptionalMceStringArray(
            arguments,
            "understood_namespaces",
            64,
            2_048,
            32_768
        );
        var extensionElements = OptionalMceExtensionElements(arguments);
        var includeNamespaceDetails = arguments.Boolean(
            "include_namespace_details",
            false
        );
        var includeSource = arguments.Boolean("include_source", false);
        var includeIssues = arguments.Boolean("include_issues", true);

        try
        {
            var package = new OpcPackageReader().Read(path, cancellationToken);
            var graph = new WordMarkupCompatibilityGraphBuilder().Build(
                package,
                new WordMceApplicationConfiguration
                {
                    UnderstoodNamespaces = understoodNamespaces,
                    ApplicationDefinedExtensionElements = extensionElements,
                },
                cancellationToken
            );
            if (partId is not null && !graph.Parts.Any(part => part.Id == partId))
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "The MCE part ID does not exist in this package fingerprint"
                );
            }

            var page = MceInspectionItems(
                graph,
                view,
                partId,
                includeNamespaceDetails,
                includeSource,
                (int)offset,
                (int)maximum
            );
            var selectedIssues = FilterMceIssues(graph, partId).ToArray();
            var issuePage = includeIssues && view != "issues"
                ? selectedIssues.Take(20)
                    .Select(issue => MceIssueItem(issue, includeSource))
                    .ToArray()
                : null;
            var selectedParts = FilterMceParts(graph, partId).ToArray();
            var selectedPartIds = selectedParts.Select(part => part.Id)
                .ToHashSet(StringComparer.Ordinal);
            var selectedAffected = graph.AffectedElements.Where(item =>
                selectedPartIds.Contains(item.PartId)
            ).ToArray();
            var selectedAlternates = graph.AlternateContent.Where(item =>
                selectedPartIds.Contains(item.PartId)
            ).ToArray();
            var selectedMismatches = graph.MustUnderstandMismatches.Where(item =>
                selectedPartIds.Contains(item.PartId)
            ).ToArray();
            var consumed = (long)offset + page.Items.Length;

            return Task.FromResult<object>(new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = graph.PackageFingerprint,
                application_configuration_fingerprint =
                    graph.ApplicationConfigurationFingerprint,
                understood_namespace_count = understoodNamespaces.Count,
                application_defined_extension_element_count = extensionElements.Count,
                xml_part_count = selectedParts.Length,
                parsed_xml_part_count = selectedParts.Count(part => part.Parsed),
                parsed_xml_bytes = partId is null ? graph.ParsedXmlBytes : selectedParts.Sum(
                    part => graph.Parts.Single(value => value.Id == part.Id).Parsed
                        ? package.Parts[part.PartUri].Entry.Content.Length
                        : 0
                ),
                parsed_element_count = selectedParts.Sum(part => part.ElementCount),
                namespace_count = graph.Namespaces.Count,
                rule_count = selectedParts.Sum(part => part.RuleCount),
                alternate_content_count = selectedAlternates.Length,
                selected_choice_count = selectedAlternates.SelectMany(item => item.Branches)
                    .Count(branch => branch.Selected && branch.Kind == WordMceBranchKind.Choice),
                selected_fallback_count = selectedAlternates.SelectMany(item => item.Branches)
                    .Count(branch => branch.Selected && branch.Kind == WordMceBranchKind.Fallback),
                affected_element_count = selectedAffected.Length,
                output_affecting_element_count = selectedAffected.Count(item => item.AffectsOutput),
                must_understand_mismatch_count = selectedMismatches.Count(item =>
                    item.AffectsOutput
                ),
                issue_count = selectedIssues.Length,
                issues_truncated_at_source = graph.IssuesTruncated,
                current_processing_model = "ecma_376_part_3_fifth_edition",
                legacy_preservation_hints =
                    "inventoried_and_preserved_not_executed_by_current_model",
                execution_policy =
                    "parse_only_lossless_source_never_preprocess_or_rewrite_never_follow_external_targets",
                word_opened = false,
                external_targets_followed = false,
                package_mutated = false,
                namespace_details_included = includeNamespaceDetails,
                source_included = includeSource,
                view,
                part_id = partId,
                matched_item_count = page.MatchedCount,
                offset,
                returned_item_count = page.Items.Length,
                next_offset = consumed < page.MatchedCount ? (int)consumed : (int?)null,
                items = page.Items,
                issues = issuePage,
                issues_truncated = issuePage is not null
                    && selectedIssues.Length > issuePage.Length,
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            });
        }
        catch (WordMceLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The markup-compatibility graph exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordMceProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a markup-compatibility graph",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (ArgumentException exception)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "The MCE application or markup configuration is invalid",
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

    private static MceInspectionPage MceInspectionItems(
        WordMarkupCompatibilityGraph graph,
        string view,
        string? partId,
        bool includeNamespaceDetails,
        bool includeSource,
        int offset,
        int maximum
    )
    {
        var parts = FilterMceParts(graph, partId).ToArray();
        var partIds = parts.Select(part => part.Id).ToHashSet(StringComparer.Ordinal);
        IEnumerable<object> items;
        int matchedCount;
        switch (view)
        {
            case "summary":
                var summary = graph.Rules.Where(rule => partIds.Contains(rule.PartId))
                    .GroupBy(rule => rule.Kind)
                    .OrderBy(group => group.Key)
                    .Select(group => (object)new
                    {
                        rule_kind = ToSnakeCase(group.Key.ToString()),
                        rule_count = group.Count(),
                        invalid_token_count = group.Sum(rule => rule.InvalidTokenCount),
                    }).ToArray();
                items = summary;
                matchedCount = summary.Length;
                break;
            case "parts":
                items = parts.Select(part => McePartItem(part, includeSource));
                matchedCount = parts.Length;
                break;
            case "namespaces":
                items = graph.Namespaces.Select(item => MceNamespaceItem(
                    item,
                    includeNamespaceDetails
                ));
                matchedCount = graph.Namespaces.Count;
                break;
            case "rules":
                var rules = graph.Rules.Where(rule => partIds.Contains(rule.PartId));
                items = rules.Select(rule => MceRuleItem(
                    rule,
                    includeNamespaceDetails,
                    includeSource
                ));
                matchedCount = rules.Count();
                break;
            case "alternate_content":
                var alternates = graph.AlternateContent.Where(item =>
                    partIds.Contains(item.PartId)
                );
                items = alternates.Select(item => MceAlternateItem(item, includeSource));
                matchedCount = alternates.Count();
                break;
            case "affected":
                var affected = graph.AffectedElements.Where(item =>
                    partIds.Contains(item.PartId)
                );
                items = affected.Select(item => MceAffectedItem(
                    item,
                    includeNamespaceDetails,
                    includeSource
                ));
                matchedCount = affected.Count();
                break;
            case "must_understand":
                var mismatches = graph.MustUnderstandMismatches.Where(item =>
                    partIds.Contains(item.PartId)
                );
                items = mismatches.Select(item => MceMismatchItem(
                    item,
                    includeNamespaceDetails,
                    includeSource
                ));
                matchedCount = mismatches.Count();
                break;
            case "issues":
                var issues = FilterMceIssues(graph, partId);
                items = issues.Select(issue => MceIssueItem(issue, includeSource));
                matchedCount = issues.Count;
                break;
            default:
                throw new UnreachableException();
        }
        return new MceInspectionPage(
            items.Skip(offset).Take(maximum).ToArray(),
            matchedCount
        );
    }

    private static IEnumerable<WordMcePartDefinition> FilterMceParts(
        WordMarkupCompatibilityGraph graph,
        string? partId
    ) => graph.Parts.Where(part => partId is null || part.Id == partId);

    private static IReadOnlyList<WordMceIssue> FilterMceIssues(
        WordMarkupCompatibilityGraph graph,
        string? partId
    ) => graph.Issues.Where(issue => partId is null || issue.PartId == partId).ToArray();

    private static object McePartItem(WordMcePartDefinition part, bool includeSource) => new
    {
        part_id = part.Id,
        parsed = part.Parsed,
        element_count = part.ElementCount,
        rule_count = part.RuleCount,
        alternate_content_count = part.AlternateContentCount,
        affected_element_count = part.AffectedElementCount,
        must_understand_mismatch_count = part.MustUnderstandMismatchCount,
        issue_count = part.IssueCount,
        part_uri = includeSource ? BoundForResponse(part.PartUri, 512) : null,
        content_type = includeSource ? BoundForResponse(part.ContentType, 512) : null,
        source_sha256 = includeSource ? part.SourceSha256 : null,
    };

    private static object MceNamespaceItem(
        WordMceNamespaceDefinition item,
        bool includeDetails
    ) => new
    {
        namespace_id = item.Id,
        understood = item.UnderstoodByConfiguration,
        element_occurrence_count = item.ElementOccurrenceCount,
        attribute_occurrence_count = item.AttributeOccurrenceCount,
        ignorable_declaration_count = item.IgnorableDeclarationCount,
        process_content_reference_count = item.ProcessContentReferenceCount,
        must_understand_reference_count = item.MustUnderstandReferenceCount,
        choice_requirement_count = item.ChoiceRequirementCount,
        namespace_uri = includeDetails
            ? BoundForResponse(item.NamespaceUri, 2_048)
            : null,
    };

    private static object MceRuleItem(
        WordMceRuleDefinition rule,
        bool includeNamespaceDetails,
        bool includeSource
    ) => new
    {
        rule_id = rule.Id,
        part_id = rule.PartId,
        kind = ToSnakeCase(rule.Kind.ToString()),
        token_count = rule.TokenCount,
        invalid_token_count = rule.InvalidTokenCount,
        resolved_names = rule.ResolvedNames.Select(name => new
        {
            namespace_id = name.NamespaceId,
            local_name = includeNamespaceDetails ? name.LocalName : null,
            wildcard = name.IsWildcard,
            namespace_uri = includeNamespaceDetails
                ? BoundForResponse(name.NamespaceUri, 2_048)
                : null,
        }).ToArray(),
        part_uri = includeSource ? BoundForResponse(rule.PartUri, 512) : null,
        source_element_ordinal = includeSource ? rule.SourceElementOrdinal : (int?)null,
    };

    private static object MceAlternateItem(
        WordMceAlternateContentDefinition item,
        bool includeSource
    ) => new
    {
        alternate_content_id = item.Id,
        part_id = item.PartId,
        structure_conformant = item.StructureConformant,
        selected_branch_id = item.SelectedBranchId,
        branch_count = item.Branches.Count,
        branches = item.Branches.Select(branch => new
        {
            branch_id = branch.Id,
            kind = ToSnakeCase(branch.Kind.ToString()),
            required_namespace_ids = branch.RequiredNamespaceIds,
            requirements_valid = branch.RequirementsValid,
            selected = branch.Selected,
            source_element_ordinal = includeSource
                ? branch.SourceElementOrdinal
                : (int?)null,
        }).ToArray(),
        part_uri = includeSource ? BoundForResponse(item.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? item.SourceElementOrdinal
            : (int?)null,
    };

    private static object MceAffectedItem(
        WordMceAffectedElement item,
        bool includeNamespaceDetails,
        bool includeSource
    ) => new
    {
        affected_element_id = item.Id,
        part_id = item.PartId,
        namespace_id = item.NamespaceId,
        disposition = ToSnakeCase(item.Disposition.ToString()),
        ignored_attribute_count = item.IgnoredAttributeCount,
        affects_output = item.AffectsOutput,
        namespace_uri = includeNamespaceDetails
            ? BoundForResponse(item.NamespaceUri, 2_048)
            : null,
        local_name = includeNamespaceDetails
            ? BoundForResponse(item.LocalName, 512)
            : null,
        part_uri = includeSource ? BoundForResponse(item.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? item.SourceElementOrdinal
            : (int?)null,
    };

    private static object MceMismatchItem(
        WordMceMustUnderstandMismatch item,
        bool includeNamespaceDetails,
        bool includeSource
    ) => new
    {
        mismatch_id = item.Id,
        part_id = item.PartId,
        namespace_id = item.NamespaceId,
        affects_output = item.AffectsOutput,
        namespace_uri = includeNamespaceDetails
            ? BoundForResponse(item.NamespaceUri, 2_048)
            : null,
        part_uri = includeSource ? BoundForResponse(item.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? item.SourceElementOrdinal
            : (int?)null,
    };

    private static object MceIssueItem(WordMceIssue issue, bool includeSource) => new
    {
        code = BoundForResponse(issue.Code, 128),
        severity = ToSnakeCase(issue.Severity.ToString()),
        message = BoundForResponse(issue.Message, 512),
        part_id = issue.PartId,
        rule_id = issue.RuleId,
        alternate_content_id = issue.AlternateContentId,
        part_uri = includeSource ? BoundForResponse(issue.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? issue.SourceElementOrdinal
            : null,
    };

    private static IReadOnlyList<string> OptionalMceStringArray(
        JsonElement arguments,
        string name,
        int maximumItems,
        int maximumCharactersPerItem,
        int maximumTotalCharacters
    )
    {
        if (!arguments.TryGetProperty(name, out var value))
        {
            return Array.Empty<string>();
        }
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"Argument '{name}' must be an array"
            );
        }
        var elements = value.EnumerateArray().ToArray();
        if (elements.Length > maximumItems)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"Argument '{name}' exceeds {maximumItems} items"
            );
        }
        var result = new List<string>(elements.Length);
        var total = 0;
        foreach (var element in elements)
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"Argument '{name}' must contain only strings"
                );
            }
            var item = element.GetString();
            if (string.IsNullOrWhiteSpace(item) || item.Length > maximumCharactersPerItem)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"Argument '{name}' contains an empty or oversized value"
                );
            }
            total = checked(total + item.Length);
            if (total > maximumTotalCharacters)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"Argument '{name}' exceeds the total character limit"
                );
            }
            result.Add(item);
        }
        return result.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<WordMceExpandedName> OptionalMceExtensionElements(
        JsonElement arguments
    )
    {
        const string name = "application_defined_extension_elements";
        if (!arguments.TryGetProperty(name, out var value))
        {
            return Array.Empty<WordMceExpandedName>();
        }
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"Argument '{name}' must be an array"
            );
        }
        var elements = value.EnumerateArray().ToArray();
        if (elements.Length > 64)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"Argument '{name}' exceeds 64 items"
            );
        }
        var result = new List<WordMceExpandedName>(elements.Length);
        foreach (var element in elements)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"Argument '{name}' must contain only objects"
                );
            }
            var properties = element.EnumerateObject().Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal);
            if (
                properties.Count != 2
                || !properties.Contains("namespace_uri")
                || !properties.Contains("local_name")
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"Each '{name}' item must contain only namespace_uri and local_name"
                );
            }
            var namespaceUri = BoundedMceObjectString(
                element,
                "namespace_uri",
                2_048
            );
            var localName = BoundedMceObjectString(element, "local_name", 256);
            result.Add(new WordMceExpandedName(namespaceUri, localName));
        }
        return result.Distinct().OrderBy(item => item.NamespaceUri, StringComparer.Ordinal)
            .ThenBy(item => item.LocalName, StringComparer.Ordinal)
            .ToArray();
    }

    private static string BoundedMceObjectString(
        JsonElement value,
        string name,
        int maximumCharacters
    )
    {
        if (
            !value.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.String
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"MCE extension property '{name}' must be a string"
            );
        }
        var result = property.GetString();
        if (string.IsNullOrWhiteSpace(result) || result.Length > maximumCharacters)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"MCE extension property '{name}' is empty or oversized"
            );
        }
        return result;
    }

    private sealed record MceInspectionPage(object[] Items, int MatchedCount);
}
