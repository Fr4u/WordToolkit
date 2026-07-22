using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> LintPackageAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveInspectablePackagePath(arguments);
        var view = arguments.String("view", "summary");
        if (view is not "summary" and not "findings" and not "rules")
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "view must be summary, findings, or rules"
            );
        }
        var offset = arguments.NullableInt64("offset") ?? 0;
        var maximum = arguments.NullableInt64("max_items") ?? 30;
        if (offset is < 0 or > 9_999)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "offset must be between 0 and 9999"
            );
        }
        if (maximum is < 1 or > 100)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_items must be between 1 and 100"
            );
        }
        var includeSource = arguments.Boolean("include_source", false);
        var includeMessages = arguments.Boolean("include_messages", true);
        var includeFix = arguments.Boolean("include_fix", false);
        var pack = ParseLintRulePack(arguments.String("rule_pack", "all"));
        var minimumSeverity = ParseLintSeverity(
            arguments.String("minimum_severity", "info")
        );
        var ruleFilter = BoundedOptionalArgument(arguments, "rule_id", 128);
        if (
            ruleFilter is not null
            && !WordDocumentLinter.RuleCatalog.Any(rule =>
                string.Equals(rule.Id, ruleFilter, StringComparison.Ordinal)
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "rule_id is not a registered Word lint rule"
            );
        }
        var categoryFilter = ParseLintCategory(
            BoundedOptionalArgument(arguments, "category", 128)
        );
        var suppressedRules = OptionalBoundedStringArray(
            arguments,
            "suppress_rule_ids",
            64,
            128
        );
        var suppressedFindings = OptionalBoundedStringArray(
            arguments,
            "suppress_finding_ids",
            256,
            64
        );

        try
        {
            var package = new OpcPackageReader().Read(path, cancellationToken);
            var semantic = new WordSemanticProjector().Project(
                package,
                cancellationToken
            );
            var options = new WordDocumentLinterOptions
            {
                MinimumSeverity = minimumSeverity,
                EnabledRulePacks = pack is null ? null : [pack.Value],
                SuppressedRuleIds = suppressedRules,
                SuppressedFindingIds = suppressedFindings,
            };
            var report = new WordDocumentLinter(options).Analyze(
                package,
                semantic,
                cancellationToken
            );
            IEnumerable<WordLintFinding> matchingFindings = report.Findings;
            if (ruleFilter is not null)
            {
                matchingFindings = matchingFindings.Where(item =>
                    string.Equals(item.RuleId, ruleFilter, StringComparison.Ordinal)
                );
            }
            if (categoryFilter is not null)
            {
                matchingFindings = matchingFindings.Where(item =>
                    item.Category == categoryFilter
                );
            }
            var filteredFindings = matchingFindings.ToArray();
            var filteredSeverityCounts = Enum.GetValues<WordLintSeverity>()
                .ToDictionary(
                    value => ToSnakeCase(value.ToString()),
                    value => filteredFindings.Count(item => item.Severity == value),
                    StringComparer.Ordinal
                );
            var filteredCategoryCounts = Enum.GetValues<WordLintCategory>()
                .ToDictionary(
                    value => ToSnakeCase(value.ToString()),
                    value => filteredFindings.Count(item => item.Category == value),
                    StringComparer.Ordinal
                );
            var filteredRuleCounts = filteredFindings
                .GroupBy(item => item.RuleId, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Count(),
                    StringComparer.Ordinal
                );
            var findingsPage = view == "findings"
                ? filteredFindings.Skip((int)offset).Take((int)maximum).ToArray()
                : Array.Empty<WordLintFinding>();
            var rules = WordDocumentLinter.RuleCatalog
                .Where(item => pack is null || item.Pack == pack)
                .Where(item => ruleFilter is null || item.Id == ruleFilter)
                .Where(item => categoryFilter is null || item.Category == categoryFilter)
                .ToArray();
            var rulesPage = view == "rules"
                ? rules.Skip((int)offset).Take((int)maximum).ToArray()
                : Array.Empty<WordLintRuleDescriptor>();
            var matchedItemCount = view switch
            {
                "findings" => filteredFindings.Length,
                "rules" => rules.Length,
                _ => 0,
            };
            var returnedItemCount = view switch
            {
                "findings" => findingsPage.Length,
                "rules" => rulesPage.Length,
                _ => 0,
            };
            var consumed = (long)offset + returnedItemCount;

            return Task.FromResult<object>(new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = report.PackageFingerprint,
                main_part_uri = includeSource
                    ? BoundForResponse(report.MainPartUri, 512)
                    : null,
                view,
                rule_pack = pack is null ? "all" : ToSnakeCase(pack.Value.ToString()),
                minimum_severity = ToSnakeCase(minimumSeverity.ToString()),
                rule_id = ruleFilter,
                category = categoryFilter is null
                    ? null
                    : ToSnakeCase(categoryFilter.Value.ToString()),
                analyzed_finding_count = report.MatchedFindingCount,
                analyzed_visible_finding_count = report.VisibleFindingCount,
                matched_finding_count = filteredFindings.Length,
                visible_finding_count = filteredFindings.Length,
                materialized_finding_count = report.Findings.Count,
                suppressed_finding_count = report.SuppressedFindingCount,
                severity_filtered_finding_count = report.SeverityFilteredFindingCount,
                findings_truncated = report.FindingsTruncated,
                implemented_fix_count = 0,
                analysis_execution_complete = report.Coverage.ExecutionComplete,
                document_coverage_complete = report.Coverage.DocumentCoverageComplete,
                report_complete = report.Complete,
                severity_counts = filteredSeverityCounts,
                category_counts = filteredCategoryCounts,
                rule_counts = filteredRuleCounts,
                evaluated_rule_count = report.EvaluatedRules.Count,
                coverage = new
                {
                    semantic_node_count = report.Coverage.SemanticNodeCount,
                    semantic_nodes_scanned = report.Coverage.SemanticNodesScanned,
                    formatting_nodes_scanned = report.Coverage.FormattingNodesScanned,
                    heading_count = report.Coverage.HeadingCount,
                    drawing_count = report.Coverage.DrawingCount,
                    table_count = report.Coverage.TableCount,
                    explicitly_unmodeled_domains = report.Coverage
                        .ExplicitlyUnmodeledDomains,
                    omissions = report.Coverage.Omissions,
                },
                offset = view == "summary" ? null : (long?)offset,
                returned_item_count = returnedItemCount,
                next_offset = view != "summary" && consumed < matchedItemCount
                    ? (int)consumed
                    : (int?)null,
                items = view switch
                {
                    "findings" => findingsPage.Select(item => LintFindingItem(
                        item,
                        includeSource,
                        includeMessages,
                        includeFix
                    )).ToArray(),
                    "rules" => rulesPage.Select(LintRuleItem).ToArray(),
                    _ => null,
                },
                word_opened = false,
                document_modified = false,
                external_targets_followed = false,
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            });
        }
        catch (ArgumentException exception)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "The lint configuration is invalid",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (Exception exception) when (IsLintLimit(exception))
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The package exceeds a bounded lint or typed-graph safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (Exception exception) when (IsLintProjectionFailure(exception))
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a bounded Word lint report",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (OpcPackageLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The package exceeds a bounded OPC safety limit",
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
                "The Word lint report could not be read",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
    }

    private static object LintFindingItem(
        WordLintFinding finding,
        bool includeSource,
        bool includeMessages,
        bool includeFix
    ) => new
    {
        finding_id = finding.Id,
        rule_id = finding.RuleId,
        rule_pack = ToSnakeCase(finding.RulePack.ToString()),
        category = ToSnakeCase(finding.Category.ToString()),
        severity = ToSnakeCase(finding.Severity.ToString()),
        confidence = ToSnakeCase(finding.Confidence.ToString()),
        message = includeMessages ? BoundForResponse(finding.Message, 512) : null,
        related_code = finding.RelatedCode,
        subject_kind = finding.SubjectKind,
        subject_fingerprint = finding.SubjectFingerprint,
        evidence_count = finding.EvidenceCount,
        source = includeSource
            ? new
            {
                part_uri = BoundForResponse(finding.Source.PartUri, 512),
                source_element_ordinal = finding.Source.SourceElementOrdinal,
                source_path = BoundForResponse(finding.Source.SourcePath, 1024),
                byte_offset = finding.Source.ByteSpan?.ByteOffset,
                byte_length = finding.Source.ByteSpan?.ByteLength,
                semantic_node_id = finding.Source.SemanticNodeId?.Value,
                relationship_id = finding.Source.RelationshipId,
            }
            : null,
        fix = includeFix
            ? new
            {
                kind = finding.Fix.Kind,
                safety = ToSnakeCase(finding.Fix.Safety.ToString()),
                implemented = finding.Fix.IsImplemented,
                requires_preview = finding.Fix.RequiresPreview,
                blocking_reason = BoundForResponse(finding.Fix.BlockingReason, 256),
            }
            : null,
    };

    private static object LintRuleItem(WordLintRuleDescriptor rule) => new
    {
        rule_id = rule.Id,
        rule_pack = ToSnakeCase(rule.Pack.ToString()),
        category = ToSnakeCase(rule.Category.ToString()),
        description = BoundForResponse(rule.Description, 384),
    };

    private static WordLintRulePack? ParseLintRulePack(string value) => value switch
    {
        "all" => null,
        "core" => WordLintRulePack.Core,
        "styles" => WordLintRulePack.Styles,
        "accessibility" => WordLintRulePack.Accessibility,
        "security" => WordLintRulePack.Security,
        _ => throw new NativeToolException(
            "INVALID_INPUT",
            "rule_pack must be all, core, styles, accessibility, or security"
        ),
    };

    private static WordLintSeverity ParseLintSeverity(string value) => value switch
    {
        "info" => WordLintSeverity.Info,
        "warning" => WordLintSeverity.Warning,
        "error" => WordLintSeverity.Error,
        "fatal" => WordLintSeverity.Fatal,
        _ => throw new NativeToolException(
            "INVALID_INPUT",
            "minimum_severity must be info, warning, error, or fatal"
        ),
    };

    private static WordLintCategory? ParseLintCategory(string? value)
    {
        if (value is null)
        {
            return null;
        }
        return Enum.GetValues<WordLintCategory>()
            .Cast<WordLintCategory?>()
            .SingleOrDefault(item =>
                item is not null
                && string.Equals(
                    ToSnakeCase(item.Value.ToString()),
                    value,
                    StringComparison.Ordinal
                )
            ) ?? throw new NativeToolException(
                "INVALID_INPUT",
                "category is not a registered Word lint category"
            );
    }

    private static IReadOnlyList<string> OptionalBoundedStringArray(
        JsonElement arguments,
        string name,
        int maximumItems,
        int maximumCharacters
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
        var items = value.EnumerateArray().ToArray();
        if (items.Length > maximumItems)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"Argument '{name}' exceeds its {maximumItems}-item limit"
            );
        }
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"Every '{name}' item must be a string"
                );
            }
            var text = item.GetString();
            if (
                string.IsNullOrWhiteSpace(text)
                || text.Length > maximumCharacters
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"Every '{name}' item must contain 1 to {maximumCharacters} characters"
                );
            }
            if (!result.Add(text))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"Argument '{name}' contains a duplicate item"
                );
            }
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }

    private static bool IsLintLimit(Exception exception) => exception is
        WordDependencyLimitException
        or WordStyleLimitException
        or WordNumberingLimitException
        or WordReferenceLimitException
        or WordSectionLimitException
        or WordThemeLimitException
        or WordSettingsLimitException
        or WordFontTableLimitException
        or WordSemanticLimitException;

    private static bool IsLintProjectionFailure(Exception exception) => exception is
        WordLintProjectionException
        or WordDependencyProjectionException
        or WordStyleProjectionException
        or WordNumberingProjectionException
        or WordNumberingResolutionException
        or WordReferenceProjectionException
        or WordSectionProjectionException
        or WordThemeProjectionException
        or WordThemeResolutionException
        or WordSettingsProjectionException
        or WordFontTableProjectionException
        or WordSemanticProjectionException;
}
