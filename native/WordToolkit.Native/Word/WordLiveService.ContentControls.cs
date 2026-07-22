using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private const int ContentControlNestedIdLimit = 100;
    private const int ContentControlNestedMetadataLimit = 20;

    private static Task<object> InspectPackageContentControlsAsync(
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
                and not "controls"
                and not "stores"
                and not "bindings"
                and not "targets"
                and not "repeating_sections"
                and not "issues"
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "view must be summary, controls, stores, bindings, targets, repeating_sections, or issues"
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

        var controlId = BoundedOptionalArgument(arguments, "control_id", 128);
        var storeId = BoundedOptionalArgument(arguments, "store_id", 128);
        var bindingId = BoundedOptionalArgument(arguments, "binding_id", 128);
        var includeNames = arguments.Boolean("include_names", false);
        var includeBindingDetails = arguments.Boolean(
            "include_binding_details",
            false
        );
        var includeSource = arguments.Boolean("include_source", false);
        var includeIssues = arguments.Boolean("include_issues", true);

        try
        {
            var package = new OpcPackageReader().Read(path, cancellationToken);
            var semantic = new WordSemanticProjector().Project(
                package,
                cancellationToken
            );
            var graph = new WordContentControlBindingGraphBuilder().Build(
                package,
                semantic,
                cancellationToken
            );
            if (controlId is not null && !graph.TryGetControl(controlId, out _))
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "The content control does not exist in this package fingerprint"
                );
            }
            if (storeId is not null && !graph.TryGetStore(storeId, out _))
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "The custom XML store does not exist in this package fingerprint"
                );
            }
            if (
                bindingId is not null
                && !graph.Bindings.Any(binding => binding.Id == bindingId)
            )
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "The content-control binding does not exist in this package fingerprint"
                );
            }

            var selectedBindings = FilterContentControlBindings(
                graph,
                controlId,
                storeId,
                bindingId
            );
            var selectedBindingIds = selectedBindings.Select(binding => binding.Id)
                .ToHashSet(StringComparer.Ordinal);
            var selectedControlIds = selectedBindings.Select(binding => binding.ControlId)
                .ToHashSet(StringComparer.Ordinal);
            if (controlId is not null)
            {
                selectedControlIds.Add(controlId);
            }
            var selectedStoreIds = selectedBindings
                .Where(binding => binding.StoreId is not null)
                .Select(binding => binding.StoreId!)
                .ToHashSet(StringComparer.Ordinal);
            if (storeId is not null)
            {
                selectedStoreIds.Add(storeId);
            }
            var selectedControls = graph.Controls.Where(control =>
                controlId is null && storeId is null && bindingId is null
                    || selectedControlIds.Contains(control.Id)
            ).ToArray();
            var selectedStores = graph.Stores.Where(store =>
                controlId is null && storeId is null && bindingId is null
                    || selectedStoreIds.Contains(store.Id)
            ).ToArray();
            var selectedTargets = graph.Targets.Where(target =>
                selectedBindingIds.Contains(target.BindingId)
                || controlId is null && storeId is null && bindingId is null
            ).ToArray();
            var selectedRepeatingSections = graph.RepeatingSections.Where(section =>
                controlId is null && storeId is null && bindingId is null
                    || selectedControlIds.Contains(section.ControlId)
            ).ToArray();
            var selectedIssues = FilterContentControlIssues(
                graph,
                controlId,
                storeId,
                bindingId
            );

            var page = ContentControlInspectionItems(
                graph,
                view,
                selectedControls,
                selectedStores,
                selectedBindings,
                selectedTargets,
                selectedRepeatingSections,
                selectedIssues,
                includeNames,
                includeBindingDetails,
                includeSource,
                (int)offset,
                (int)maximum
            );
            var consumed = (long)offset + page.Items.Length;
            var issuePage = includeIssues && view != "issues"
                ? selectedIssues.Take(20)
                    .Select(issue => ContentControlIssueItem(issue, includeSource))
                    .ToArray()
                : null;

            return Task.FromResult<object>(new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = graph.PackageFingerprint,
                control_count = selectedControls.Length,
                bound_control_count = selectedControls.Count(control =>
                    control.BindingId is not null
                ),
                store_count = selectedStores.Length,
                custom_xml_store_count = selectedStores.Count(store =>
                    store.Kind == WordBindingStoreKind.CustomXml
                ),
                built_in_property_store_count = selectedStores.Count(store =>
                    store.Kind != WordBindingStoreKind.CustomXml
                ),
                unreadable_store_count = selectedStores.Count(store => !store.Parsed),
                binding_count = selectedBindings.Length,
                resolved_binding_count = selectedBindings.Count(binding =>
                    binding.Status == WordBindingResolutionStatus.Resolved
                ),
                binding_target_count = selectedTargets.Length,
                repeating_section_count = selectedRepeatingSections.Length,
                repeating_cardinality_mismatch_count = selectedRepeatingSections.Count(
                    section => section.CardinalityMatches == false
                ),
                issue_count = selectedIssues.Count,
                issues_truncated_at_source = graph.IssuesTruncated,
                parsed_xml_bytes = graph.ParsedXmlBytes,
                parsed_xml_elements = graph.ParsedXmlElements,
                execution_policy =
                    "parse_only_restricted_xpath_never_return_custom_xml_values_or_raw_xml",
                word_opened = false,
                package_mutated = false,
                custom_xml_values_included = false,
                raw_xml_included = false,
                names_included = includeNames,
                binding_details_included = includeBindingDetails,
                source_included = includeSource,
                view,
                control_id = controlId,
                store_id = storeId,
                binding_id = bindingId,
                matched_item_count = page.MatchedCount,
                offset,
                returned_item_count = page.Items.Length,
                next_offset = consumed < page.MatchedCount
                    ? (int)consumed
                    : (int?)null,
                items = page.Items,
                issues = issuePage,
                issues_truncated = issuePage is not null
                    && selectedIssues.Count > issuePage.Length,
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            });
        }
        catch (WordContentControlLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The content-control binding graph exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordContentControlProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a content-control binding graph",
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
                "The content-control binding graph could not be read",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
    }

    private static WordContentControlBindingDefinition[] FilterContentControlBindings(
        WordContentControlBindingGraph graph,
        string? controlId,
        string? storeId,
        string? bindingId
    ) => graph.Bindings.Where(binding =>
        (controlId is null || binding.ControlId == controlId)
        && (storeId is null || binding.StoreId == storeId)
        && (bindingId is null || binding.Id == bindingId)
    ).ToArray();

    private static IReadOnlyList<WordContentControlIssue> FilterContentControlIssues(
        WordContentControlBindingGraph graph,
        string? controlId,
        string? storeId,
        string? bindingId
    ) => graph.Issues.Where(issue =>
        (controlId is null || issue.ControlId == controlId)
        && (storeId is null || issue.StoreId == storeId)
        && (bindingId is null || issue.BindingId == bindingId)
    ).ToArray();

    private static ContentControlInspectionPage ContentControlInspectionItems(
        WordContentControlBindingGraph graph,
        string view,
        IReadOnlyList<WordContentControlDefinition> controls,
        IReadOnlyList<WordCustomXmlStoreDefinition> stores,
        IReadOnlyList<WordContentControlBindingDefinition> bindings,
        IReadOnlyList<WordContentControlBindingTarget> targets,
        IReadOnlyList<WordRepeatingSectionDefinition> repeatingSections,
        IReadOnlyList<WordContentControlIssue> issues,
        bool includeNames,
        bool includeBindingDetails,
        bool includeSource,
        int offset,
        int maximum
    )
    {
        IEnumerable<object> items;
        int matchedCount;
        switch (view)
        {
            case "summary":
                var controlTypes = controls.GroupBy(control => control.Type)
                    .OrderBy(group => group.Key)
                    .Select(group => (object)new
                    {
                        category = "control_type",
                        name = ToSnakeCase(group.Key.ToString()),
                        count = group.Count(),
                    });
                var bindingStatuses = bindings.GroupBy(binding => binding.Status)
                    .OrderBy(group => group.Key)
                    .Select(group => (object)new
                    {
                        category = "binding_status",
                        name = ToSnakeCase(group.Key.ToString()),
                        count = group.Count(),
                    });
                var summary = controlTypes.Concat(bindingStatuses).ToArray();
                items = summary;
                matchedCount = summary.Length;
                break;
            case "controls":
                items = controls.Select(control => ContentControlItem(
                    control,
                    includeNames,
                    includeSource
                ));
                matchedCount = controls.Count;
                break;
            case "stores":
                items = stores.Select(store => ContentControlStoreItem(
                    store,
                    includeBindingDetails,
                    includeSource
                ));
                matchedCount = stores.Count;
                break;
            case "bindings":
                items = bindings.Select(binding => ContentControlBindingItem(
                    binding,
                    includeBindingDetails,
                    includeSource
                ));
                matchedCount = bindings.Count;
                break;
            case "targets":
                items = targets.Select(target => ContentControlTargetItem(
                    target,
                    includeBindingDetails,
                    includeSource
                ));
                matchedCount = targets.Count;
                break;
            case "repeating_sections":
                items = repeatingSections.Select(section => new
                {
                    repeating_section_id = section.Id,
                    control_id = section.ControlId,
                    item_control_count = section.ItemControlIds.Count,
                    item_control_ids = section.ItemControlIds
                        .Take(ContentControlNestedIdLimit)
                        .ToArray(),
                    item_control_ids_truncated =
                        section.ItemControlIds.Count > ContentControlNestedIdLimit,
                    binding_target_count = section.BindingTargetCount,
                    cardinality_matches = section.CardinalityMatches,
                    insert_delete_locked = section.DoNotAllowInsertDeleteSection,
                    part_uri = includeSource
                        ? BoundForResponse(section.PartUri, 512)
                        : null,
                    source_element_ordinal = includeSource
                        ? section.SourceElementOrdinal
                        : (int?)null,
                });
                matchedCount = repeatingSections.Count;
                break;
            case "issues":
                items = issues.Select(issue => ContentControlIssueItem(
                    issue,
                    includeSource
                ));
                matchedCount = issues.Count;
                break;
            default:
                throw new UnreachableException();
        }
        return new ContentControlInspectionPage(
            items.Skip(offset).Take(maximum).ToArray(),
            matchedCount
        );
    }

    private static object ContentControlItem(
        WordContentControlDefinition control,
        bool includeNames,
        bool includeSource
    ) => new
    {
        control_id = control.Id,
        parent_control_id = control.ParentControlId,
        binding_id = control.BindingId,
        type = ToSnakeCase(control.Type.ToString()),
        type_explicit = control.TypeExplicit,
        level = ToSnakeCase(control.Level.ToString()),
        @lock = ToSnakeCase(control.Lock.ToString()),
        showing_placeholder = control.ShowingPlaceholder,
        temporary = control.Temporary,
        insert_delete_locked = control.DoNotAllowInsertDeleteSection,
        alias = includeNames ? BoundForResponse(control.Alias, 512) : null,
        tag = includeNames ? BoundForResponse(control.Tag, 512) : null,
        placeholder_building_block = includeNames
            ? BoundForResponse(control.PlaceholderBuildingBlock, 512)
            : null,
        repeating_section_title = includeNames
            ? BoundForResponse(control.RepeatingSectionTitle, 512)
            : null,
        native_id = includeSource ? BoundForResponse(control.NativeId, 128) : null,
        semantic_node_id = includeSource ? control.SemanticNodeId.Value : null,
        part_uri = includeSource ? BoundForResponse(control.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? control.SourceElementOrdinal
            : (int?)null,
    };

    private static object ContentControlStoreItem(
        WordCustomXmlStoreDefinition store,
        bool includeBindingDetails,
        bool includeSource
    ) => new
    {
        store_id = store.Id,
        kind = ToSnakeCase(store.Kind.ToString()),
        parsed = store.Parsed,
        xml_element_count = store.XmlElementCount,
        schema_reference_count = store.SchemaReferences.Count,
        incoming_relationship_count = store.IncomingRelationshipCount,
        properties_relationship_resolved = store.PropertiesRelationshipResolved,
        item_id = includeBindingDetails ? store.ItemId : null,
        root_namespace_uri = includeBindingDetails
            ? BoundForResponse(store.RootNamespaceUri, 2_048)
            : null,
        root_local_name = includeBindingDetails
            ? BoundForResponse(store.RootLocalName, 512)
            : null,
        schema_references = includeBindingDetails
            ? store.SchemaReferences.Take(ContentControlNestedMetadataLimit)
                .Select(value => BoundForResponse(value, 2_048)).ToArray()
            : null,
        schema_references_truncated = includeBindingDetails
            && store.SchemaReferences.Count > ContentControlNestedMetadataLimit,
        part_uri = includeSource ? BoundForResponse(store.PartUri, 512) : null,
        content_type = includeSource ? BoundForResponse(store.ContentType, 512) : null,
        properties_part_uri = includeSource
            ? BoundForResponse(store.PropertiesPartUri, 512)
            : null,
    };

    private static object ContentControlBindingItem(
        WordContentControlBindingDefinition binding,
        bool includeBindingDetails,
        bool includeSource
    ) => new
    {
        binding_id = binding.Id,
        control_id = binding.ControlId,
        store_id = binding.StoreId,
        status = ToSnakeCase(binding.Status.ToString()),
        office2013_rich_text_binding = binding.IsOffice2013RichTextBinding,
        target_count = binding.TargetIds.Count,
        target_ids = binding.TargetIds.Take(ContentControlNestedIdLimit).ToArray(),
        target_ids_truncated = binding.TargetIds.Count > ContentControlNestedIdLimit,
        store_item_id = includeBindingDetails ? binding.StoreItemId : null,
        xpath = includeBindingDetails ? BoundForResponse(binding.XPath, 4_096) : null,
        prefix_mappings = includeBindingDetails
            ? BoundForResponse(binding.PrefixMappings, 32_768)
            : null,
        namespace_mappings = includeBindingDetails
            ? binding.NamespaceMappings.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Take(ContentControlNestedMetadataLimit)
                .Select(pair => new
                {
                    prefix = BoundForResponse(pair.Key, 256),
                    namespace_uri = BoundForResponse(pair.Value, 2_048),
                }).ToArray()
            : null,
        namespace_mappings_truncated = includeBindingDetails
            && binding.NamespaceMappings.Count > ContentControlNestedMetadataLimit,
        part_uri = includeSource ? BoundForResponse(binding.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? binding.SourceElementOrdinal
            : (int?)null,
    };

    private static object ContentControlTargetItem(
        WordContentControlBindingTarget target,
        bool includeBindingDetails,
        bool includeSource
    ) => new
    {
        target_id = target.Id,
        binding_id = target.BindingId,
        store_id = target.StoreId,
        namespace_uri = includeBindingDetails
            ? BoundForResponse(target.NamespaceUri, 2_048)
            : null,
        local_name = includeBindingDetails
            ? BoundForResponse(target.LocalName, 512)
            : null,
        source_element_ordinal = includeSource
            ? target.SourceElementOrdinal
            : (int?)null,
    };

    private static object ContentControlIssueItem(
        WordContentControlIssue issue,
        bool includeSource
    ) => new
    {
        issue_id = issue.Id,
        code = BoundForResponse(issue.Code, 128),
        severity = ToSnakeCase(issue.Severity.ToString()),
        message = BoundForResponse(issue.Message, 512),
        control_id = issue.ControlId,
        store_id = issue.StoreId,
        binding_id = issue.BindingId,
        part_uri = includeSource ? BoundForResponse(issue.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? issue.SourceElementOrdinal
            : null,
    };

    private sealed record ContentControlInspectionPage(
        object[] Items,
        int MatchedCount
    );
}
