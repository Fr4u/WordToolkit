using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private const int MaxBibliographyIdentitiesPerContributorResponse = 4;
    private const int MaxBibliographyNameCharacters = 256;
    private const int MaxBibliographyResponsePayloadCharacters = 64 * 1_024;
    private static readonly byte[] BibliographyFingerprintKey =
        RandomNumberGenerator.GetBytes(32);

    private Task<object> InspectPackageBibliographyAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        ValidateBibliographyInspectionArguments(arguments);
        var path = ResolveInspectablePackagePath(arguments);
        var view = arguments.String("view", "summary");
        if (
            view is not "summary"
                and not "collections"
                and not "sources"
                and not "fields"
                and not "contributors"
                and not "citations"
                and not "issues"
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "view must be summary, collections, sources, fields, contributors, citations, or issues"
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
        var sourceId = BoundedOptionalArgument(arguments, "source_id", 128);
        if (sourceId is not null && !IsBibliographySourceId(sourceId))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "source_id must match ^wbs_[a-f0-9]{24}$"
            );
        }
        var sourceTag = BoundedOptionalArgument(arguments, "source_tag", 4_096);
        var sourceType = BoundedOptionalArgument(arguments, "source_type", 128);
        var includeSensitive = arguments.Boolean("include_sensitive", false);
        var includeSource = arguments.Boolean("include_source", false);
        var includeIssues = arguments.Boolean("include_issues", true);

        try
        {
            var resourceLease = _operationResourceLeaseFactory()
                ?? throw new InvalidOperationException(
                    "The operation resource-lease factory returned null."
                );
            var package = new OpcPackageReader(
                OpcPackageLimits.Default,
                resourceLease
            ).Read(path, cancellationToken);
            var bibliography = new WordBibliographyGraphBuilder(
                null,
                resourceLease
            ).Build(
                package,
                cancellationToken
            );
            if (sourceId is not null && !bibliography.TryGetSource(sourceId, out _))
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "The bibliography source does not exist in this package fingerprint"
                );
            }
            var semantic = new WordSemanticProjector(null, resourceLease).Project(
                package,
                cancellationToken
            );
            var references = new WordReferenceGraphBuilder(null, resourceLease).Build(
                package,
                semantic,
                cancellationToken
            );
            var operationUsage = resourceLease.Snapshot();
            var sources = bibliography.Sources.Where(source =>
                    (sourceId is null || source.Id == sourceId)
                    && (sourceTag is null || string.Equals(
                        source.Tag,
                        sourceTag,
                        StringComparison.OrdinalIgnoreCase
                    ))
                    && (sourceType is null || string.Equals(
                        source.SourceType,
                        sourceType,
                        StringComparison.OrdinalIgnoreCase
                    ))
                )
                .ToArray();
            var selectedSourceIds = sources.Select(source => source.Id)
                .ToHashSet(StringComparer.Ordinal);
            var citationEdges = view == "citations"
                ? references.Edges.Where(edge =>
                        edge.TargetKind == WordReferenceTargetKind.Citation
                    )
                    .Select(edge =>
                    {
                        var resolved = bibliography.TryResolveCitationTag(
                            edge.TargetKey,
                            out var source
                        );
                        return new CitationInspection(edge, resolved ? source : null);
                    })
                    .Where(citation =>
                        (sourceId is null && sourceTag is null && sourceType is null)
                        || citation.Source is not null
                            && selectedSourceIds.Contains(citation.Source.Id)
                        || sourceTag is not null
                            && string.Equals(
                                citation.Edge.TargetKey,
                                sourceTag,
                                StringComparison.OrdinalIgnoreCase
                            )
                    )
                : [];
            var page = BibliographyInspectionPage(
                view,
                bibliography,
                sources,
                citationEdges,
                (int)offset,
                (int)maximum,
                includeSensitive,
                includeSource,
                cancellationToken
            );
            var consumed = (long)offset + page.Items.Count;
            var remainingResponseCharacters = Math.Max(
                0,
                MaxBibliographyResponsePayloadCharacters - page.ProjectedCharacters
            );
            BibliographyItemPage? issueProjection = includeIssues && view != "issues"
                ? PageBibliographyItems(
                    bibliography.Issues,
                    0,
                    30,
                    issue => BibliographyIssueItem(issue, includeSource),
                    cancellationToken,
                    remainingResponseCharacters,
                    rejectOversizedFirstItem: false
                )
                : null;
            var issuePage = issueProjection?.Items;
            var responsePayloadCharacters = page.ProjectedCharacters
                + (issueProjection?.ProjectedCharacters ?? 0);

            return Task.FromResult<object>(new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = bibliography.PackageFingerprint,
                collection_count = bibliography.Collections.Count,
                source_count = bibliography.Sources.Count,
                matched_source_count = sources.Length,
                uniquely_tagged_source_count = bibliography.Sources.Count(source =>
                    source.IsTagUnique
                ),
                duplicate_or_missing_tag_count = bibliography.Sources.Count(source =>
                    !source.IsTagUnique
                ),
                citation_count = references.Edges.Count(edge =>
                    edge.TargetKind == WordReferenceTargetKind.Citation
                ),
                resolved_citation_count = references.Edges.Count(edge =>
                    edge.TargetKind == WordReferenceTargetKind.Citation
                    && bibliography.TryResolveCitationTag(edge.TargetKey, out _)
                ),
                unresolved_citation_count = references.Edges.Count(edge =>
                    edge.TargetKind == WordReferenceTargetKind.Citation
                    && !bibliography.TryResolveCitationTag(edge.TargetKey, out _)
                ),
                style_name_count = bibliography.Collections.Count(collection =>
                    !string.IsNullOrWhiteSpace(collection.StyleName)
                ),
                locale_count = bibliography.Sources
                    .Where(source => source.Lcid is not null)
                    .Select(source => source.Lcid)
                    .Distinct()
                    .Count(),
                issue_count = bibliography.Issues.Count,
                issues_truncated_at_source = bibliography.IssuesTruncated,
                custom_xml_candidate_count = bibliography.CustomXmlCandidateCount,
                execution_policy =
                    "parse_package_only_never_open_word_evaluate_fields_execute_xslt_or_follow_external_targets",
                word_opened = false,
                fields_evaluated = false,
                bibliography_xslt_executed = false,
                external_targets_followed = false,
                sensitive_values_included = includeSensitive,
                source_locations_included = includeSource,
                view,
                source_id = sourceId,
                source_type = sourceType,
                source_tag_filter_fingerprint = FingerprintBibliographyValue(sourceTag),
                fingerprint_scope = "process_hmac_sha256_64",
                matched_item_count = page.MatchedCount,
                offset,
                returned_item_count = page.Items.Count,
                next_offset = consumed < page.MatchedCount ? (int)consumed : (int?)null,
                items = page.Items,
                issues = issuePage,
                issues_truncated = issuePage is not null
                    && bibliography.Issues.Count > issuePage.Count,
                response_budget = new
                {
                    model = "bibliography_projected_payload_characters_v1",
                    used = responsePayloadCharacters,
                    maximum = MaxBibliographyResponsePayloadCharacters,
                },
                response_budget_truncated = page.ResponseBudgetTruncated
                    || issueProjection?.ResponseBudgetTruncated == true,
                operation_budget = new
                {
                    model = "wop1",
                    used = operationUsage.AccountedBytes,
                    maximum = operationUsage.MaximumAccountedBytes,
                },
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            });
        }
        catch (WordOperationResourceLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The bibliography inspection exceeded its operation resource budget",
                new
                {
                    reason = "The operation resource budget was exhausted",
                    operation_budget = new
                    {
                        model = "wop1",
                        used = exception.AccountedBytes,
                        maximum = exception.MaximumAccountedBytes,
                        attempted = exception.AttemptedBytes,
                        stage = ToSnakeCase(exception.Stage.ToString()),
                    },
                }
            );
        }
        catch (WordBibliographyLimitException)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The bibliography graph exceeds a bounded safety limit",
                new { reason_code = "bibliography_graph_limit" }
            );
        }
        catch (WordReferenceLimitException)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The reference graph exceeds a bounded safety limit",
                new { reason_code = "reference_graph_limit" }
            );
        }
        catch (WordReferenceProjectionException)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a Word reference graph",
                new { reason_code = "reference_projection_failed" }
            );
        }
        catch (WordSemanticLimitException)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "Semantic projection exceeds a bounded safety limit",
                new { reason_code = "semantic_projection_limit" }
            );
        }
        catch (WordSemanticProjectionException)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be projected as a Word semantic document",
                new { reason_code = "semantic_projection_failed" }
            );
        }
        catch (OpcPackageLimitException)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The package exceeds a bounded safety limit",
                new { reason_code = "opc_package_limit" }
            );
        }
        catch (InvalidDataException)
        {
            throw new NativeToolException(
                "INVALID_PACKAGE",
                "The file is not a readable OPC ZIP package",
                new { reason_code = "invalid_opc_package" }
            );
        }
        catch (UnauthorizedAccessException)
        {
            throw new NativeToolException(
                "ACCESS_DENIED",
                "The Word package cannot be read with current permissions"
            );
        }
        catch (IOException)
        {
            throw new NativeToolException(
                "IO_ERROR",
                "The Word package could not be read",
                new { reason_code = "io_read_failed" }
            );
        }
    }

    private static BibliographyItemPage BibliographyInspectionPage(
        string view,
        WordBibliographyGraph graph,
        IReadOnlyList<WordBibliographySource> sources,
        IEnumerable<CitationInspection> citations,
        int offset,
        int maximum,
        bool includeSensitive,
        bool includeSource,
        CancellationToken cancellationToken
    ) => view switch
    {
        "summary" => PageBibliographyItems(
            sources.GroupBy(SafeSourceType)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal),
            offset,
            maximum,
            group => (object)new
            {
                source_type = BoundForResponse(group.Key, 128),
                count = group.Count(),
                unique_tag_count = group.Count(source => source.IsTagUnique),
                locale_count = group.Where(source => source.Lcid is not null)
                    .Select(source => source.Lcid)
                    .Distinct()
                    .Count(),
                contributor_count = group.Sum(source => source.Contributors.Count),
            },
            cancellationToken
        ),
        "collections" => PageBibliographyItems(
            graph.Collections,
            offset,
            maximum,
            collection => (object)new
            {
                collection_id = collection.Id,
                source_count = collection.SourceIds.Count,
                package_reachable = collection.IsPackageReachable,
                incoming_relationship_count = collection.IncomingRelationshipCount,
                namespace_kind = collection.NamespaceUri
                    == WordBibliographyGraphBuilder.TransitionalBibliographyNamespace
                        ? "openxml_2006"
                        : "word_2004_10",
                style_name = includeSensitive
                    ? BoundForResponse(collection.StyleName, 256)
                    : null,
                style_name_fingerprint = FingerprintBibliographyValue(collection.StyleName),
                version = includeSensitive
                    ? BoundForResponse(collection.Version, 64)
                    : null,
                version_fingerprint = FingerprintBibliographyValue(collection.Version),
                selected_style = includeSensitive
                    ? BoundForResponse(collection.SelectedStyle, 1_024)
                    : null,
                selected_style_fingerprint = FingerprintBibliographyValue(
                    collection.SelectedStyle
                ),
                uri = includeSensitive ? BoundForResponse(collection.Uri, 1_024) : null,
                uri_fingerprint = FingerprintBibliographyValue(collection.Uri),
                part_uri = includeSource
                    ? BoundForResponse(collection.PartUri, 512)
                    : null,
                source_element_ordinal = includeSource
                    ? collection.SourceElementOrdinal
                    : (int?)null,
            },
            cancellationToken
        ),
        "sources" => PageBibliographyItems(
            sources,
            offset,
            maximum,
            source => SourceItem(source, includeSensitive, includeSource),
            cancellationToken
        ),
        "fields" => PageBibliographyItems(
            sources.SelectMany(source => source.Fields.Select(field => (source, field))),
            offset,
            maximum,
            item => (object)new
            {
                source_id = item.source.Id,
                name = item.field.IsKnown
                    ? BoundForResponse(item.field.Name, 128)
                    : "(unmodeled)",
                name_fingerprint = item.field.IsKnown
                    ? null
                    : FingerprintBibliographyValue(item.field.Name),
                known = item.field.IsKnown,
                value = includeSensitive
                    ? BoundForResponse(item.field.Value, 4_096)
                    : null,
                value_redacted = includeSensitive || item.field.Value.Length == 0
                    ? (bool?)null
                    : true,
                value_character_count = item.field.Value.Length,
                value_fingerprint = FingerprintBibliographyValue(item.field.Value),
                part_uri = includeSource
                    ? BoundForResponse(item.source.PartUri, 512)
                    : null,
                source_element_ordinal = includeSource
                    ? item.field.SourceElementOrdinal
                    : (int?)null,
            },
            cancellationToken
        ),
        "contributors" => PageBibliographyItems(
            sources.SelectMany(source => source.Contributors.Select(contributor =>
                (source, contributor)
            )),
            offset,
            maximum,
            item => (object)new
            {
                source_id = item.source.Id,
                role = BoundForResponse(item.contributor.Role, 128),
                person_count = item.contributor.People.Count,
                corporate_name_count = item.contributor.CorporateNames.Count,
                people = includeSensitive
                    ? item.contributor.People
                        .Take(MaxBibliographyIdentitiesPerContributorResponse)
                        .Select(person => new
                        {
                            last = BoundForResponse(
                            person.Last,
                            MaxBibliographyNameCharacters
                        ),
                            first = BoundForResponse(
                            person.First,
                            MaxBibliographyNameCharacters
                        ),
                            middle = BoundForResponse(
                            person.Middle,
                            MaxBibliographyNameCharacters
                        ),
                        }).ToArray()
                    : null,
                people_truncated = includeSensitive
                    && item.contributor.People.Count
                        > MaxBibliographyIdentitiesPerContributorResponse
                    ? true
                    : (bool?)null,
                corporate_names = includeSensitive
                    ? item.contributor.CorporateNames
                        .Take(MaxBibliographyIdentitiesPerContributorResponse)
                        .Select(value => BoundForResponse(
                            value,
                            MaxBibliographyNameCharacters
                        ))
                        .ToArray()
                    : null,
                corporate_names_truncated = includeSensitive
                    && item.contributor.CorporateNames.Count
                        > MaxBibliographyIdentitiesPerContributorResponse
                        ? true
                        : (bool?)null,
                source_element_ordinal = includeSource
                    ? item.contributor.SourceElementOrdinal
                    : (int?)null,
            },
            cancellationToken
        ),
        "citations" => PageBibliographyItems(
            citations,
            offset,
            maximum,
            citation => (object)new
            {
                field_id = citation.Edge.SourceFieldId,
                source_id = citation.Source?.Id,
                resolved = citation.Source is not null,
                citation_tag = includeSensitive
                    ? BoundForResponse(citation.Edge.TargetKey, 4_096)
                    : null,
                citation_tag_redacted = includeSensitive ? (bool?)null : true,
                citation_tag_character_count = citation.Edge.TargetKey.Length,
                citation_tag_fingerprint = FingerprintBibliographyValue(
                    citation.Edge.TargetKey
                ),
            },
            cancellationToken
        ),
        _ => PageBibliographyItems(
            graph.Issues,
            offset,
            maximum,
            issue => BibliographyIssueItem(issue, includeSource),
            cancellationToken
        ),
    };

    private static BibliographyItemPage PageBibliographyItems<T>(
        IEnumerable<T> source,
        int offset,
        int maximum,
        Func<T, object> projector,
        CancellationToken cancellationToken,
        int maximumProjectedCharacters = MaxBibliographyResponsePayloadCharacters,
        bool rejectOversizedFirstItem = true
    )
    {
        var items = new List<object>(maximum);
        var matchedCount = 0;
        var projectedCharacters = 0;
        var responseBudgetTruncated = false;
        foreach (var item in source)
        {
            if ((matchedCount & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (
                matchedCount >= offset
                && items.Count < maximum
                && !responseBudgetTruncated
            )
            {
                var projected = projector(item);
                var serializedLength = JsonSerializer.Serialize(projected).Length
                    + (items.Count == 0 ? 0 : 1);
                if (projectedCharacters + serializedLength > maximumProjectedCharacters)
                {
                    if (items.Count == 0 && rejectOversizedFirstItem)
                    {
                        throw new NativeToolException(
                            "PACKAGE_LIMIT",
                            "A bibliography response item exceeds the bounded payload budget",
                            new
                            {
                                reason_code = "bibliography_response_item_limit",
                                response_budget = new
                                {
                                    model = "bibliography_projected_payload_characters_v1",
                                    attempted = serializedLength,
                                    maximum = maximumProjectedCharacters,
                                },
                            }
                        );
                    }
                    responseBudgetTruncated = true;
                }
                else
                {
                    items.Add(projected);
                    projectedCharacters += serializedLength;
                }
            }
            matchedCount++;
        }
        return new BibliographyItemPage(
            items,
            matchedCount,
            projectedCharacters,
            responseBudgetTruncated
        );
    }

    private static object SourceItem(
        WordBibliographySource source,
        bool includeSensitive,
        bool includeSource
    ) => new
    {
        source_id = source.Id,
        collection_id = source.CollectionId,
        source_type = source.IsSourceTypeKnown
            ? BoundForResponse(source.SourceType, 128)
            : null,
        source_type_status = SourceTypeStatus(source),
        source_type_fingerprint = source.IsSourceTypeKnown
            ? null
            : FingerprintBibliographyValue(source.SourceType),
        lcid = includeSensitive ? source.Lcid : null,
        lcid_present = source.Lcid is not null,
        tag = includeSensitive ? BoundForResponse(source.Tag, 4_096) : null,
        tag_redacted = includeSensitive || source.Tag is null ? (bool?)null : true,
        tag_character_count = source.Tag?.Length ?? 0,
        tag_fingerprint = FingerprintBibliographyValue(source.Tag),
        tag_unique = source.IsTagUnique,
        tag_ambiguous = source.HasAmbiguousTag,
        guid = includeSensitive ? BoundForResponse(source.Guid, 128) : null,
        guid_fingerprint = FingerprintBibliographyValue(source.Guid),
        guid_unique = source.IsGuidUnique,
        guid_ambiguous = source.HasAmbiguousGuid,
        title = includeSensitive ? BoundForResponse(source.Title, 4_096) : null,
        title_redacted = includeSensitive || source.Title is null ? (bool?)null : true,
        title_character_count = source.Title?.Length ?? 0,
        title_fingerprint = FingerprintBibliographyValue(source.Title),
        year = includeSensitive ? BoundForResponse(source.Year, 128) : null,
        year_redacted = includeSensitive || source.Year is null ? (bool?)null : true,
        year_character_count = source.Year?.Length ?? 0,
        year_fingerprint = FingerprintBibliographyValue(source.Year),
        field_count = source.Fields.Count,
        contributor_count = source.Contributors.Count,
        person_count = source.Contributors.Sum(contributor => contributor.People.Count),
        corporate_name_count = source.Contributors.Sum(contributor =>
            contributor.CorporateNames.Count
        ),
        unmodeled_elements = includeSensitive
            ? source.UnmodeledElements.Take(64)
                .Select(value => BoundForResponse(value, 128))
                .ToArray()
            : null,
        unmodeled_element_fingerprints = includeSensitive
            ? null
            : source.UnmodeledElements.Take(64)
                .Select(FingerprintBibliographyValue)
                .ToArray(),
        unmodeled_element_count = source.UnmodeledElements.Count,
        unmodeled_elements_truncated = source.UnmodeledElements.Count > 64
            ? true
            : (bool?)null,
        part_uri = includeSource ? BoundForResponse(source.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? source.SourceElementOrdinal
            : (int?)null,
    };

    private static object BibliographyIssueItem(
        WordBibliographyIssue issue,
        bool includeSource
    ) => new
    {
        code = BoundForResponse(issue.Code, 128),
        severity = ToSnakeCase(issue.Severity.ToString()),
        message = BoundForResponse(issue.Message, 512),
        source_id = issue.SourceId,
        part_uri = includeSource ? BoundForResponse(issue.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? issue.SourceElementOrdinal
            : null,
    };

    private static string SafeSourceType(WordBibliographySource source) =>
        source.IsSourceTypeKnown ? source.SourceType! : $"({SourceTypeStatus(source)})";

    private static string SourceTypeStatus(WordBibliographySource source) =>
        source.IsSourceTypeKnown
            ? "known"
            : source.HasAmbiguousSourceType
                ? "ambiguous"
                : string.IsNullOrWhiteSpace(source.SourceType)
                    ? "missing"
                    : "unknown";

    private static string? FingerprintBibliographyValue(string? value)
    {
        if (value is null)
        {
            return null;
        }
        return Convert.ToHexString(
            HMACSHA256.HashData(
                BibliographyFingerprintKey,
                Encoding.UTF8.GetBytes(value)
            )
        ).ToLowerInvariant()[..16];
    }

    private static bool IsBibliographySourceId(string value)
    {
        if (value.Length != 28 || !value.StartsWith("wbs_", StringComparison.Ordinal))
        {
            return false;
        }
        foreach (var character in value.AsSpan(4))
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }
        return true;
    }

    private static void ValidateBibliographyInspectionArguments(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new NativeToolException("INVALID_INPUT", "arguments must be an object");
        }
        var allowed = new Dictionary<string, JsonValueKind>(StringComparer.Ordinal)
        {
            ["local_path"] = JsonValueKind.String,
            ["view"] = JsonValueKind.String,
            ["source_id"] = JsonValueKind.String,
            ["source_tag"] = JsonValueKind.String,
            ["source_type"] = JsonValueKind.String,
            ["offset"] = JsonValueKind.Number,
            ["max_items"] = JsonValueKind.Number,
            ["include_sensitive"] = JsonValueKind.True,
            ["include_source"] = JsonValueKind.True,
            ["include_issues"] = JsonValueKind.True,
        };
        foreach (var property in arguments.EnumerateObject())
        {
            if (!allowed.TryGetValue(property.Name, out var expected))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "inspect_ooxml_bibliography received an unknown argument"
                );
            }
            var validKind = expected == JsonValueKind.True
                ? property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False
                : property.Value.ValueKind == expected;
            if (!validKind)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"{property.Name} has the wrong JSON type"
                );
            }
            if (
                expected == JsonValueKind.Number
                && !property.Value.TryGetInt64(out _)
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"{property.Name} must be an integer"
                );
            }
        }
    }

    private sealed record BibliographyItemPage(
        IReadOnlyList<object> Items,
        int MatchedCount,
        int ProjectedCharacters,
        bool ResponseBudgetTruncated
    );

    private readonly record struct CitationInspection(
        WordReferenceEdge Edge,
        WordBibliographySource? Source
    );
}
