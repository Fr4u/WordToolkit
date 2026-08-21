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
    private const int MaxDocumentPropertyResponseCharacters = 32 * 1_024;
    private static readonly byte[] DocumentPropertyFingerprintKey =
        RandomNumberGenerator.GetBytes(32);

    private Task<object> InspectPackageDocumentPropertiesAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDocumentPropertyArguments(arguments);
        var path = ResolveInspectablePackagePath(arguments);
        var view = arguments.String("view", "summary");
        var propertyId = OptionalString(arguments, "property_id");
        var family = ParseDocumentPropertyFamily(
            OptionalString(arguments, "property_family")
        );
        var valueKind = ParseDocumentPropertyValueKind(
            OptionalString(arguments, "value_kind")
        );
        var offset = checked((int)(arguments.NullableInt64("offset") ?? 0));
        var maximum = checked((int)(arguments.NullableInt64("max_items") ?? 30));
        var includeNames = arguments.Boolean("include_names", false);
        var includeValues = arguments.Boolean("include_values", false);
        var includeHashes = arguments.Boolean("include_hashes", false);
        var includeSource = arguments.Boolean("include_source", false);
        var includeIssues = arguments.Boolean("include_issues", false);
        var resourceLease = _operationResourceLeaseFactory();

        try
        {
            var package = new OpcPackageReader(null, resourceLease).Read(
                path,
                cancellationToken
            );
            var graph = new WordDocumentPropertyGraphBuilder(
                null,
                resourceLease
            ).Build(package, cancellationToken);
            if (
                propertyId is not null
                && !graph.Properties.Any(item => item.Id == propertyId)
            )
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "property_id does not identify a document property"
                );
            }

            var selected = SelectDocumentPropertyItems(
                graph,
                view,
                propertyId,
                family,
                valueKind,
                includeNames,
                includeValues,
                includeHashes,
                includeSource,
                cancellationToken
            );
            var page = PageDocumentPropertyItems(
                selected,
                offset,
                maximum,
                cancellationToken
            );
            var issueItems = includeIssues
                ? graph.Issues.Take(50)
                    .Select(issue => DocumentPropertyIssueItem(issue, includeSource))
                    .ToArray()
                : null;
            var operationUsage = resourceLease.Snapshot();
            return Task.FromResult<object>(new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = graph.PackageFingerprint,
                part_count = graph.Parts.Count,
                property_count = graph.Properties.Count,
                core_property_count = graph.Properties.Count(item =>
                    item.Family == WordDocumentPropertyFamily.Core
                ),
                extended_property_count = graph.Properties.Count(item =>
                    item.Family == WordDocumentPropertyFamily.Extended
                ),
                custom_property_count = graph.Properties.Count(item =>
                    item.Family == WordDocumentPropertyFamily.Custom
                ),
                scalar_property_count = graph.Properties.Count(item =>
                    item.HasScalarValue
                ),
                invalid_property_count = graph.Properties.Count(item =>
                    !item.IsStructurallyValid || !item.IsUniquelyNamed
                ),
                issue_count = graph.Issues.Count,
                issues_truncated_at_source = graph.IssuesTruncated,
                execution_policy =
                    "metadata_only_never_open_word_evaluate_fields_decode_complex_values_or_return_raw_xml",
                word_opened = false,
                package_mutated = false,
                fields_evaluated = false,
                complex_values_decoded = false,
                raw_xml_included = false,
                view,
                custom_names_included = includeNames,
                values_included = includeValues,
                hashes_included = includeHashes,
                source_included = includeSource,
                matched_item_count = selected.Count,
                offset,
                returned_item_count = page.Items.Count,
                next_offset = page.NextOffset,
                response_truncated = page.WasTruncated ? true : (bool?)null,
                items = page.Items,
                issues = issueItems,
                issues_truncated = issueItems is not null
                    && graph.Issues.Count > issueItems.Length
                        ? true
                        : (bool?)null,
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
                "The document-property inspection exceeded its operation resource budget",
                new
                {
                    reason_code = "operation_resource_budget_exhausted",
                    operation_budget = new
                    {
                        model = "wop1",
                        used = exception.AccountedBytes,
                        maximum = exception.MaximumAccountedBytes,
                    },
                }
            );
        }
        catch (WordDocumentPropertyLimitException)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The document-property graph exceeds a bounded safety limit",
                new { reason_code = "document_property_graph_limit" }
            );
        }
        catch (WordDocumentPropertyProjectionException)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a document-property graph",
                new { reason_code = "document_property_projection_failed" }
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
                "The document-property graph could not be read",
                new { reason_code = "document_property_io_failed" }
            );
        }
    }

    private static IReadOnlyList<object> SelectDocumentPropertyItems(
        WordDocumentPropertyGraph graph,
        string view,
        string? propertyId,
        WordDocumentPropertyFamily? family,
        WordDocumentPropertyValueKind? valueKind,
        bool includeNames,
        bool includeValues,
        bool includeHashes,
        bool includeSource,
        CancellationToken cancellationToken
    )
    {
        var filteredProperties = graph.Properties.Where(item =>
            (propertyId is null || item.Id == propertyId)
            && (family is null || item.Family == family)
            && (valueKind is null || item.ValueKind == valueKind)
        ).ToArray();
        if (view == "summary")
        {
            return filteredProperties.GroupBy(item => item.Family)
                .OrderBy(item => item.Key)
                .Select(item => (object)new
                {
                    category = "property_family",
                    name = ToSnakeCase(item.Key.ToString()),
                    count = item.Count(),
                })
                .Concat(
                    filteredProperties.GroupBy(item => item.ValueKind)
                        .OrderBy(item => item.Key)
                        .Select(item => (object)new
                        {
                            category = "value_kind",
                            name = ToSnakeCase(item.Key.ToString()),
                            count = item.Count(),
                        })
                )
                .ToArray();
        }
        if (view == "parts")
        {
            var selectedPartUris = propertyId is null
                ? null
                : filteredProperties.Select(item => item.PartUri)
                    .ToHashSet(StringComparer.Ordinal);
            return graph.Parts
                .Where(item =>
                    (family is null || item.Family == family)
                    && (
                        selectedPartUris is null
                        || selectedPartUris.Contains(item.PartUri)
                    )
                )
                .Select(item => (object)new
                {
                    property_family = ToSnakeCase(item.Family.ToString()),
                    package_reachable = item.IsPackageReachable,
                    content_type = includeSource
                        ? BoundForResponse(item.ContentType, 512)
                        : null,
                    part_uri = includeSource
                        ? BoundForResponse(item.PartUri, 512)
                        : null,
                    source_sha256 = includeHashes ? item.SourceSha256 : null,
                })
                .ToArray();
        }
        if (view == "issues")
        {
            return graph.Issues
                .Where(item =>
                    propertyId is null || item.PropertyId == propertyId
                )
                .Select(item => DocumentPropertyIssueItem(item, includeSource))
                .ToArray();
        }

        var result = new List<object>();
        foreach (
            var property in filteredProperties
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isCustom = property.Family == WordDocumentPropertyFamily.Custom;
            var visibleName = !isCustom || includeNames;
            var projectedValue = includeValues && property.HasScalarValue
                ? BoundForResponse(property.Value, 2_048)
                : null;
            result.Add(new
            {
                property_id = property.Id,
                property_family = ToSnakeCase(property.Family.ToString()),
                value_kind = ToSnakeCase(property.ValueKind.ToString()),
                has_scalar_value = property.HasScalarValue,
                value_character_count = property.ValueCharacterCount,
                uniquely_named = property.IsUniquelyNamed,
                structurally_valid = property.IsStructurallyValid,
                package_reachable = property.IsPackageReachable,
                name = visibleName
                    ? BoundForResponse(property.Name, 512)
                    : null,
                name_redacted = visibleName ? (bool?)null : true,
                name_character_count = property.Name.Length,
                name_fingerprint = includeHashes
                    ? FingerprintDocumentPropertyValue(property.Name)
                    : null,
                value = projectedValue,
                value_redacted = includeValues || !property.HasScalarValue
                    ? (bool?)null
                    : true,
                value_truncated_for_response = projectedValue is not null
                    && projectedValue.Length < property.ValueCharacterCount,
                value_fingerprint = includeHashes && property.Value is not null
                    ? FingerprintDocumentPropertyValue(property.Value)
                    : null,
                numeric_property_id = includeSource ? property.PropertyId : null,
                format_id = includeSource
                    ? BoundForResponse(property.FormatId, 256)
                    : null,
                part_uri = includeSource
                    ? BoundForResponse(property.PartUri, 512)
                    : null,
                source_element_ordinal = includeSource
                    ? property.SourceElementOrdinal
                    : (int?)null,
            });
        }
        return result;
    }

    private static DocumentPropertyPage PageDocumentPropertyItems(
        IReadOnlyList<object> items,
        int offset,
        int maximum,
        CancellationToken cancellationToken
    )
    {
        var result = new List<object>(Math.Min(maximum, items.Count));
        var characters = 0;
        var index = offset;
        while (index < items.Count && result.Count < maximum)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[index];
            var itemCharacters = JsonSerializer.Serialize(
                item,
                JsonDefaults.Compact
            ).Length;
            if (
                result.Count > 0
                && characters > MaxDocumentPropertyResponseCharacters - itemCharacters
            )
            {
                break;
            }
            result.Add(item);
            characters = checked(characters + itemCharacters);
            index++;
        }
        var requestedEnd = Math.Min(
            (long)items.Count,
            (long)offset + maximum
        );
        return new DocumentPropertyPage(
            result,
            index < items.Count ? index : null,
            index < requestedEnd
        );
    }

    private static object DocumentPropertyIssueItem(
        WordDocumentPropertyIssue issue,
        bool includeSource
    ) => new
    {
        code = BoundForResponse(issue.Code, 128),
        severity = ToSnakeCase(issue.Severity.ToString()),
        message = BoundForResponse(issue.Message, 512),
        property_id = issue.PropertyId,
        part_uri = includeSource ? BoundForResponse(issue.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? issue.SourceElementOrdinal
            : null,
    };

    private static string FingerprintDocumentPropertyValue(string value) =>
        Convert.ToHexString(
            HMACSHA256.HashData(
                DocumentPropertyFingerprintKey,
                Encoding.UTF8.GetBytes(value)
            )
        ).ToLowerInvariant()[..16];

    private static WordDocumentPropertyFamily? ParseDocumentPropertyFamily(
        string? value
    ) => value switch
    {
        null => null,
        "core" => WordDocumentPropertyFamily.Core,
        "extended" => WordDocumentPropertyFamily.Extended,
        "custom" => WordDocumentPropertyFamily.Custom,
        _ => throw new NativeToolException(
            "INVALID_INPUT",
            "property_family must be core, extended, or custom"
        ),
    };

    private static WordDocumentPropertyValueKind? ParseDocumentPropertyValueKind(
        string? value
    ) => value switch
    {
        null => null,
        "text" => WordDocumentPropertyValueKind.Text,
        "integer" => WordDocumentPropertyValueKind.Integer,
        "unsigned_integer" => WordDocumentPropertyValueKind.UnsignedInteger,
        "floating_point" => WordDocumentPropertyValueKind.FloatingPoint,
        "decimal" => WordDocumentPropertyValueKind.Decimal,
        "boolean" => WordDocumentPropertyValueKind.Boolean,
        "date_time" => WordDocumentPropertyValueKind.DateTime,
        "currency" => WordDocumentPropertyValueKind.Currency,
        "error_code" => WordDocumentPropertyValueKind.ErrorCode,
        "class_id" => WordDocumentPropertyValueKind.ClassId,
        "binary" => WordDocumentPropertyValueKind.Binary,
        "vector" => WordDocumentPropertyValueKind.Vector,
        "array" => WordDocumentPropertyValueKind.Array,
        "variant" => WordDocumentPropertyValueKind.Variant,
        "empty" => WordDocumentPropertyValueKind.Empty,
        "unknown" => WordDocumentPropertyValueKind.Unknown,
        _ => throw new NativeToolException(
            "INVALID_INPUT",
            "value_kind is not supported"
        ),
    };

    private static void ValidateDocumentPropertyArguments(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new NativeToolException("INVALID_INPUT", "arguments must be an object");
        }
        var allowed = new Dictionary<string, JsonValueKind>(StringComparer.Ordinal)
        {
            ["local_path"] = JsonValueKind.String,
            ["view"] = JsonValueKind.String,
            ["property_id"] = JsonValueKind.String,
            ["property_family"] = JsonValueKind.String,
            ["value_kind"] = JsonValueKind.String,
            ["offset"] = JsonValueKind.Number,
            ["max_items"] = JsonValueKind.Number,
            ["include_names"] = JsonValueKind.True,
            ["include_values"] = JsonValueKind.True,
            ["include_hashes"] = JsonValueKind.True,
            ["include_source"] = JsonValueKind.True,
            ["include_issues"] = JsonValueKind.True,
        };
        foreach (var property in arguments.EnumerateObject())
        {
            if (!allowed.TryGetValue(property.Name, out var expected))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"unsupported argument: {property.Name}"
                );
            }
            if (
                expected == JsonValueKind.True
                    ? property.Value.ValueKind is not JsonValueKind.True
                        and not JsonValueKind.False
                    : property.Value.ValueKind != expected
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"{property.Name} has the wrong JSON type"
                );
            }
        }
        var view = arguments.String("view", "summary");
        if (view is not "summary" and not "properties" and not "parts" and not "issues")
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "view must be summary, properties, parts, or issues"
            );
        }
        var propertyId = OptionalString(arguments, "property_id");
        if (propertyId is not null && !IsDocumentPropertyId(propertyId))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "property_id must be a canonical wdp_ identifier"
            );
        }
        var offset = arguments.NullableInt64("offset") ?? 0;
        if (offset is < 0 or > int.MaxValue)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "offset must be between 0 and 2147483647"
            );
        }
        var maximum = arguments.NullableInt64("max_items") ?? 30;
        if (maximum is < 1 or > 50)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_items must be between 1 and 50"
            );
        }
    }

    private static bool IsDocumentPropertyId(string value)
    {
        if (value.Length != 28 || !value.StartsWith("wdp_", StringComparison.Ordinal))
        {
            return false;
        }
        return value.AsSpan(4).IndexOfAnyExcept("0123456789abcdef") < 0;
    }

    private sealed record DocumentPropertyPage(
        IReadOnlyList<object> Items,
        int? NextOffset,
        bool WasTruncated
    );
}
