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
    private const int MaxMailMergeResponsePayloadCharacters = 64 * 1_024;
    private static readonly byte[] MailMergeFingerprintKey =
        RandomNumberGenerator.GetBytes(32);

    private Task<object> InspectPackageMailMergeAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        ValidateMailMergeInspectionArguments(arguments);
        var path = ResolveInspectablePackagePath(arguments);
        var view = arguments.String("view", "summary");
        if (view is not "summary"
            and not "configuration"
            and not "relationships"
            and not "mappings"
            and not "recipients"
            and not "fields"
            and not "issues")
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "view must be summary, configuration, relationships, mappings, recipients, fields, or issues"
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
        var includeSource = arguments.Boolean("include_source", false);
        var includeRelationshipTargets = arguments.Boolean(
            "include_relationship_targets",
            false
        );
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
            var semantic = new WordSemanticProjector(null, resourceLease).Project(
                package,
                cancellationToken
            );
            var settings = new WordSettingsGraphBuilder(null, resourceLease).Build(
                package,
                semantic,
                cancellationToken
            );
            var references = new WordReferenceGraphBuilder(null, resourceLease).Build(
                package,
                semantic,
                cancellationToken
            );
            var graph = new WordMailMergeGraphBuilder(null, resourceLease).Build(
                package,
                semantic,
                settings,
                references,
                cancellationToken
            );
            var page = MailMergeInspectionPage(
                view,
                graph,
                (int)offset,
                (int)maximum,
                includeSensitive,
                includeSource,
                includeRelationshipTargets,
                cancellationToken
            );
            var consumed = (long)offset + page.Items.Count;
            var remainingResponseCharacters = Math.Max(
                0,
                MaxMailMergeResponsePayloadCharacters - page.ProjectedCharacters
            );
            MailMergeItemPage? issueProjection = includeIssues && view != "issues"
                ? PageMailMergeItems(
                    graph.Issues,
                    0,
                    30,
                    issue => MailMergeIssueItem(issue, includeSource),
                    cancellationToken,
                    remainingResponseCharacters,
                    rejectOversizedFirstItem: false
                )
                : null;
            var issuePage = issueProjection?.Items;
            var operationUsage = resourceLease.Snapshot();

            return Task.FromResult<object>(new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = graph.PackageFingerprint,
                present = graph.HasMailMergeEvidence,
                configuration_count = graph.Configuration is null ? 0 : 1,
                mapping_count = graph.Mappings.Count,
                recipient_count = graph.Recipients.Count,
                included_recipient_count = graph.Recipients.Count(item => item.IsIncluded),
                field_count = graph.Fields.Count,
                resolved_field_count = graph.Fields.Count(field =>
                    field.BindingStatus is WordMailMergeFieldBindingStatus.ResolvedBySourceColumnName
                        or WordMailMergeFieldBindingStatus.ResolvedByWordPredefinedName
                ),
                unresolved_or_ambiguous_field_count = graph.Fields.Count(field =>
                    field.BindingStatus is WordMailMergeFieldBindingStatus.Missing
                        or WordMailMergeFieldBindingStatus.Ambiguous
                ),
                issue_count = graph.Issues.Count,
                issues_truncated_at_source = graph.IssuesTruncated,
                has_external_data_source = graph.Configuration?.HasExternalDataSource == true,
                has_sensitive_connection_metadata =
                    graph.Configuration?.HasSensitiveConnectionMetadata == true,
                execution_policy =
                    "parse_saved_package_only_never_open_word_execute_mail_merge_open_data_source_run_query_or_follow_external_targets",
                word_opened = false,
                mail_merge_executed = false,
                data_sources_opened = false,
                queries_executed = false,
                external_targets_followed = false,
                sensitive_values_included = includeSensitive,
                source_locations_included = includeSource,
                relationship_targets_included = includeRelationshipTargets,
                fingerprint_scope = "process_hmac_sha256_64",
                view,
                matched_item_count = page.MatchedCount,
                offset,
                returned_item_count = page.Items.Count,
                next_offset = consumed < page.MatchedCount ? (int)consumed : (int?)null,
                items = page.Items,
                issues = issuePage,
                issues_truncated = issuePage is not null
                    && graph.Issues.Count > issuePage.Count,
                response_budget = new
                {
                    model = "mail_merge_projected_payload_characters_v1",
                    used = page.ProjectedCharacters
                        + (issueProjection?.ProjectedCharacters ?? 0),
                    maximum = MaxMailMergeResponsePayloadCharacters,
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
                "The mail-merge inspection exceeded its operation resource budget",
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
        catch (WordMailMergeLimitException)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The mail-merge graph exceeds a bounded safety limit",
                new { reason_code = "mail_merge_graph_limit" }
            );
        }
        catch (WordMailMergeProjectionException)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a mail-merge graph",
                new { reason_code = "mail_merge_projection_failed" }
            );
        }
        catch (WordSettingsLimitException)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The settings graph exceeds a bounded safety limit",
                new { reason_code = "settings_graph_limit" }
            );
        }
        catch (WordSettingsProjectionException)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a Word settings graph",
                new { reason_code = "settings_projection_failed" }
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

    private static MailMergeItemPage MailMergeInspectionPage(
        string view,
        WordMailMergeGraph graph,
        int offset,
        int maximum,
        bool includeSensitive,
        bool includeSource,
        bool includeRelationshipTargets,
        CancellationToken cancellationToken
    ) => view switch
    {
        "summary" => PageMailMergeItems(
            MailMergeSummaryItems(graph),
            offset,
            maximum,
            item => item,
            cancellationToken
        ),
        "configuration" => PageMailMergeItems(
            graph.Configuration is { } configuration
                ? new[] { configuration }
                : Array.Empty<WordMailMergeConfiguration>(),
            offset,
            maximum,
            item => ConfigurationItem(
                item,
                includeSensitive,
                includeSource
            ),
            cancellationToken
        ),
        "relationships" => PageMailMergeItems(
            MailMergeRelationships(graph),
            offset,
            maximum,
            item => RelationshipItem(
                item,
                includeRelationshipTargets,
                includeSource
            ),
            cancellationToken
        ),
        "mappings" => PageMailMergeItems(
            graph.Mappings,
            offset,
            maximum,
            item => MappingItem(item, includeSensitive, includeSource),
            cancellationToken
        ),
        "recipients" => PageMailMergeItems(
            graph.Recipients,
            offset,
            maximum,
            item => RecipientItem(item, includeSensitive, includeSource),
            cancellationToken
        ),
        "fields" => PageMailMergeItems(
            graph.Fields,
            offset,
            maximum,
            item => FieldItem(item, includeSensitive, includeSource),
            cancellationToken
        ),
        _ => PageMailMergeItems(
            graph.Issues,
            offset,
            maximum,
            issue => MailMergeIssueItem(issue, includeSource),
            cancellationToken
        ),
    };

    private static IEnumerable<object> MailMergeSummaryItems(WordMailMergeGraph graph)
    {
        yield return new
        {
            kind = "configuration",
            count = graph.Configuration is null ? 0 : 1,
            resolved_count = graph.Configuration is null ? 0 : 1,
            warning_or_error_count = graph.Issues.Count(issue =>
                issue.Code.StartsWith("MAIL_MERGE_CONFIGURATION", StringComparison.Ordinal)
                    || issue.Code.StartsWith("MAIL_MERGE_MAIN_DOCUMENT", StringComparison.Ordinal)
            ),
        };
        yield return new
        {
            kind = "field_mapping",
            count = graph.Mappings.Count,
            resolved_count = graph.Mappings.Count,
            warning_or_error_count = graph.Issues.Count(issue =>
                issue.Code.Contains("MAPPING", StringComparison.Ordinal)
            ),
        };
        yield return new
        {
            kind = "recipient",
            count = graph.Recipients.Count,
            resolved_count = graph.Recipients.Count(recipient =>
                recipient.IdentityKind is WordMailMergeRecipientIdentityKind.UniqueTag
                    or WordMailMergeRecipientIdentityKind.Hash
            ),
            warning_or_error_count = graph.Issues.Count(issue =>
                issue.Code.Contains("RECIPIENT", StringComparison.Ordinal)
            ),
        };
        yield return new
        {
            kind = "field",
            count = graph.Fields.Count,
            resolved_count = graph.Fields.Count(field =>
                field.BindingStatus is WordMailMergeFieldBindingStatus.ResolvedBySourceColumnName
                    or WordMailMergeFieldBindingStatus.ResolvedByWordPredefinedName
                    or WordMailMergeFieldBindingStatus.NotApplicable
            ),
            warning_or_error_count = graph.Issues.Count(issue =>
                issue.Code.Contains("FIELD_BINDING", StringComparison.Ordinal)
            ),
        };
    }

    private static IEnumerable<WordMailMergeRelationship> MailMergeRelationships(
        WordMailMergeGraph graph
    )
    {
        if (graph.Configuration?.DataSourceRelationship is { } dataSource)
        {
            yield return dataSource;
        }
        if (graph.Configuration?.HeaderSourceRelationship is { } headerSource)
        {
            yield return headerSource;
        }
        if (graph.Configuration?.DataSourceObject?.SourceRelationship is { } source)
        {
            yield return source;
        }
        if (graph.Configuration?.DataSourceObject?.RecipientDataRelationship is { } recipients)
        {
            yield return recipients;
        }
    }

    private static object ConfigurationItem(
        WordMailMergeConfiguration item,
        bool includeSensitive,
        bool includeSource
    ) => new
    {
        configuration_id = item.Id,
        main_document_type = BoundForResponse(item.MainDocumentType, 128),
        data_type = BoundForResponse(item.DataType, 128),
        destination = BoundForResponse(item.Destination, 128),
        link_to_query = item.LinkToQuery,
        do_not_suppress_blank_lines = item.DoNotSuppressBlankLines,
        mail_as_attachment = item.MailAsAttachment,
        view_merged_data = item.ViewMergedData,
        active_record = item.ActiveRecord,
        has_external_data_source = item.HasExternalDataSource,
        has_sensitive_connection_metadata = item.HasSensitiveConnectionMetadata,
        query = includeSensitive ? BoundForResponse(item.Query, 4_096) : null,
        query_fingerprint = FingerprintMailMergeValue(item.Query),
        connection_string = includeSensitive
            ? BoundForResponse(item.ConnectionString, 4_096)
            : null,
        connection_string_fingerprint = FingerprintMailMergeValue(item.ConnectionString),
        address_field_name = includeSensitive
            ? BoundForResponse(item.AddressFieldName, 1_024)
            : null,
        address_field_name_fingerprint = FingerprintMailMergeValue(item.AddressFieldName),
        mail_subject = includeSensitive ? BoundForResponse(item.MailSubject, 4_096) : null,
        mail_subject_fingerprint = FingerprintMailMergeValue(item.MailSubject),
        odso_present = item.DataSourceObject is not null,
        odso_udl = includeSensitive
            ? BoundForResponse(item.DataSourceObject?.UdlConnectionString, 4_096)
            : null,
        odso_udl_fingerprint = FingerprintMailMergeValue(
            item.DataSourceObject?.UdlConnectionString
        ),
        odso_table = includeSensitive
            ? BoundForResponse(item.DataSourceObject?.TableName, 1_024)
            : null,
        odso_table_fingerprint = FingerprintMailMergeValue(
            item.DataSourceObject?.TableName
        ),
        part_uri = includeSource ? BoundForResponse(item.SettingsPartUri, 512) : null,
        source_element_ordinal = includeSource ? item.SourceElementOrdinal : (int?)null,
    };

    private static object RelationshipItem(
        WordMailMergeRelationship item,
        bool includeRelationshipTargets,
        bool includeSource
    ) => new
    {
        relationship_object_id = item.Id,
        role = ToSnakeCase(item.Role.ToString()),
        relationship_id = includeSource
            ? BoundForResponse(item.RelationshipId, 256)
            : null,
        relationship_type = includeSource
            ? BoundForResponse(item.RelationshipType, 1_024)
            : null,
        relationship_type_fingerprint = FingerprintMailMergeValue(
            item.RelationshipType
        ),
        target_mode = item.TargetMode is null
            ? null
            : ToSnakeCase(item.TargetMode.Value.ToString()),
        external = item.IsExternal,
        relationship_exists = item.RelationshipExists,
        relationship_type_valid = item.RelationshipTypeValid,
        target_exists = item.TargetExists,
        resolved = item.IsResolved,
        target = includeRelationshipTargets
            ? BoundForResponse(item.Target, 4_096)
            : null,
        target_fingerprint = FingerprintMailMergeValue(item.Target),
        resolved_target_part_uri = includeSource
            ? BoundForResponse(item.ResolvedTargetPartUri, 512)
            : null,
        source_part_uri = includeSource
            ? BoundForResponse(item.SourcePartUri, 512)
            : null,
        source_element_ordinal = includeSource ? item.SourceElementOrdinal : (int?)null,
    };

    private static object MappingItem(
        WordMailMergeFieldMapping item,
        bool includeSensitive,
        bool includeSource
    ) => new
    {
        mapping_id = item.Id,
        position = item.Position,
        field_type = BoundForResponse(item.FieldType, 128),
        source_column_name = includeSensitive
            ? BoundForResponse(item.SourceColumnName, 4_096)
            : null,
        source_column_name_fingerprint = FingerprintMailMergeValue(item.SourceColumnName),
        declared_mapped_name = includeSensitive
            ? BoundForResponse(item.DeclaredMappedName, 4_096)
            : null,
        declared_mapped_name_fingerprint = FingerprintMailMergeValue(
            item.DeclaredMappedName
        ),
        word_effective_predefined_name = BoundForResponse(
            item.WordEffectivePredefinedName,
            128
        ),
        column_index = item.ColumnIndex,
        language_id = BoundForResponse(item.LanguageId, 128),
        dynamic_address = item.DynamicAddress,
        source_element_ordinal = includeSource ? item.SourceElementOrdinal : (int?)null,
    };

    private static object RecipientItem(
        WordMailMergeRecipient item,
        bool includeSensitive,
        bool includeSource
    ) => new
    {
        recipient_id = item.Id,
        sequence = item.Sequence,
        included = item.IsIncluded,
        column_index = item.ColumnIndex,
        identity_kind = ToSnakeCase(item.IdentityKind.ToString()),
        identity_value = includeSensitive
            ? BoundForResponse(item.IdentityValue, 4_096)
            : null,
        identity_value_fingerprint = FingerprintMailMergeValue(item.IdentityValue),
        part_uri = includeSource ? BoundForResponse(item.PartUri, 512) : null,
        source_element_ordinal = includeSource ? item.SourceElementOrdinal : (int?)null,
    };

    private static object FieldItem(
        WordMailMergeField item,
        bool includeSensitive,
        bool includeSource
    ) => new
    {
        field_id = item.Id,
        field_type = BoundForResponse(item.FieldType, 128),
        complete = item.IsComplete,
        in_deleted_content = item.IsInDeletedContent,
        binding_status = ToSnakeCase(item.BindingStatus.ToString()),
        mapping_ids = item.MappingIds.Take(64).ToArray(),
        mapping_ids_truncated = item.MappingIds.Count > 64 ? true : (bool?)null,
        target_name = includeSensitive
            ? BoundForResponse(item.TargetName, 4_096)
            : null,
        target_name_fingerprint = FingerprintMailMergeValue(item.TargetName),
        story_id = includeSource ? BoundForResponse(item.StoryId, 128) : null,
        part_uri = includeSource ? BoundForResponse(item.PartUri, 512) : null,
        source_element_ordinal = includeSource ? item.SourceElementOrdinal : (int?)null,
        semantic_node_id = includeSource ? item.SemanticNodeId?.Value : null,
    };

    private static object MailMergeIssueItem(
        WordMailMergeIssue issue,
        bool includeSource
    ) => new
    {
        code = BoundForResponse(issue.Code, 128),
        severity = ToSnakeCase(issue.Severity.ToString()),
        message = BoundForResponse(issue.Message, 512),
        subject_id = issue.SubjectId,
        part_uri = includeSource ? BoundForResponse(issue.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? issue.SourceElementOrdinal
            : null,
    };

    private static MailMergeItemPage PageMailMergeItems<T>(
        IEnumerable<T> source,
        int offset,
        int maximum,
        Func<T, object> projector,
        CancellationToken cancellationToken,
        int maximumProjectedCharacters = MaxMailMergeResponsePayloadCharacters,
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
            if (matchedCount >= offset && items.Count < maximum && !responseBudgetTruncated)
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
                            "A mail-merge response item exceeds the bounded payload budget",
                            new
                            {
                                reason_code = "mail_merge_response_item_limit",
                                response_budget = new
                                {
                                    model = "mail_merge_projected_payload_characters_v1",
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
        return new MailMergeItemPage(
            items,
            matchedCount,
            projectedCharacters,
            responseBudgetTruncated
        );
    }

    private static string? FingerprintMailMergeValue(string? value)
    {
        if (value is null)
        {
            return null;
        }
        return Convert.ToHexString(
            HMACSHA256.HashData(
                MailMergeFingerprintKey,
                Encoding.UTF8.GetBytes(value)
            )
        ).ToLowerInvariant()[..16];
    }

    private static void ValidateMailMergeInspectionArguments(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new NativeToolException("INVALID_INPUT", "arguments must be an object");
        }
        var allowed = new Dictionary<string, JsonValueKind>(StringComparer.Ordinal)
        {
            ["local_path"] = JsonValueKind.String,
            ["view"] = JsonValueKind.String,
            ["offset"] = JsonValueKind.Number,
            ["max_items"] = JsonValueKind.Number,
            ["include_sensitive"] = JsonValueKind.True,
            ["include_source"] = JsonValueKind.True,
            ["include_relationship_targets"] = JsonValueKind.True,
            ["include_issues"] = JsonValueKind.True,
        };
        foreach (var property in arguments.EnumerateObject())
        {
            if (!allowed.TryGetValue(property.Name, out var expected))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "inspect_ooxml_mail_merge received an unknown argument"
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
            if (expected == JsonValueKind.Number && !property.Value.TryGetInt64(out _))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"{property.Name} must be an integer"
                );
            }
        }
    }

    private sealed record MailMergeItemPage(
        IReadOnlyList<object> Items,
        int MatchedCount,
        int ProjectedCharacters,
        bool ResponseBudgetTruncated
    );
}
