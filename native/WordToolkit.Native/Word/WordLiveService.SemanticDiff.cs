using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static readonly IReadOnlyDictionary<string, WordSemanticDifferenceKind>
        SemanticDifferenceKinds = Enum.GetValues<WordSemanticDifferenceKind>()
            .ToDictionary(
                kind => ToSnakeCase(kind.ToString()),
                kind => kind,
                StringComparer.Ordinal
            );

    private static Task<object> ComparePackageSemanticsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var request = ParseSemanticDiffRequest(arguments);
            var beforePath = ResolveComparedPackagePath(arguments, "before_path");
            var afterPath = ResolveComparedPackagePath(arguments, "after_path");
            var reader = new OpcPackageReader();
            var projector = new WordSemanticProjector();
            var beforePackage = reader.Read(beforePath, cancellationToken);
            VerifyOptionalFingerprint(
                beforePackage.Fingerprint,
                request.ExpectedBeforeFingerprint,
                "before"
            );
            var beforeDocument = projector.Project(beforePackage, cancellationToken);
            var afterPackage = reader.Read(afterPath, cancellationToken);
            VerifyOptionalFingerprint(
                afterPackage.Fingerprint,
                request.ExpectedAfterFingerprint,
                "after"
            );
            var afterDocument = projector.Project(afterPackage, cancellationToken);
            var result = new WordSemanticDiffEngine(request.Options).Compare(
                beforePackage,
                beforeDocument,
                afterPackage,
                afterDocument,
                cancellationToken
            );
            return Task.FromResult<object>(SemanticDiffResponse(
                result,
                request,
                beforePath,
                afterPath,
                started
            ));
        }
        catch (NativeToolException)
        {
            throw;
        }
        catch (WordSemanticDiffPreconditionException exception)
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                BoundForResponse(exception.Message, 512) ?? "Semantic diff precondition failed"
            );
        }
        catch (WordSemanticDiffLimitException exception)
        {
            throw new NativeToolException(
                "DIFF_LIMIT",
                BoundForResponse(exception.Message, 512) ?? "Semantic diff limit exceeded"
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
                "A package cannot be projected as a Word semantic document",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (OpcPackageLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "A package exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (ArgumentException exception)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                BoundForResponse(exception.Message, 512) ?? "Invalid semantic diff request"
            );
        }
        catch (InvalidDataException exception)
        {
            throw new NativeToolException(
                "INVALID_PACKAGE",
                "A compared file is not a readable OPC ZIP package",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (UnauthorizedAccessException)
        {
            throw new NativeToolException(
                "ACCESS_DENIED",
                "A compared Word package cannot be read with current permissions"
            );
        }
        catch (IOException exception)
        {
            throw new NativeToolException(
                "IO_ERROR",
                "A compared Word package could not be read",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
    }

    private static object SemanticDiffResponse(
        WordSemanticDiffResult result,
        SemanticDiffRequest request,
        string beforePath,
        string afterPath,
        long started
    )
    {
        var page = request.View switch
        {
            "changes" => PageSemanticDifferences(result, request),
            "entries" => PageEntryDifferences(result, request),
            "diagnostics" => PageDiffDiagnostics(result, request),
            _ => DiffPage.Empty,
        };
        return new
        {
            before_file_name = Path.GetFileName(beforePath),
            after_file_name = Path.GetFileName(afterPath),
            diff_id = result.DiffId,
            before_package_fingerprint = result.BeforePackageFingerprint,
            after_package_fingerprint = result.AfterPackageFingerprint,
            package_equivalent = result.PackageEquivalent,
            semantically_equivalent = result.SemanticallyEquivalent,
            matching_complete = result.MatchingComplete,
            package_entry_difference_count = result.EntryDifferences.Count,
            semantic_difference_count = result.SemanticDifferences.Count,
            unclassified_projected_entry_count = result.UnclassifiedProjectedEntryCount,
            node_counts = new
            {
                before = result.BeforeNodeCount,
                after = result.AfterNodeCount,
                matched = result.MatchedNodeCount,
            },
            match_counts = new
            {
                document_role = result.RoleMatchCount,
                exact_node_id = result.ExactNodeIdMatchCount,
                durable_identity = result.DurableIdentityMatchCount,
                exact_subtree = result.ExactSubtreeMatchCount,
                contextual_similarity = result.ContextualMatchCount,
                ambiguous_identity_groups = result.AmbiguousIdentityGroupCount,
                ambiguous_contextual_matches = result.AmbiguousContextualMatchCount,
                alignment_fallbacks = result.AlignmentFallbackCount,
                alignment_cells_evaluated = result.AlignmentCellsEvaluated,
            },
            change_counts = new
            {
                added = result.AddedNodeCount,
                removed = result.RemovedNodeCount,
                moved = result.MovedNodeCount,
                text_changed = result.TextChangedNodeCount,
                properties_changed = result.PropertiesChangedNodeCount,
                structure_changed = result.StructureChangedNodeCount,
                unmodeled_markup_changed = result.UnmodeledMarkupChangedNodeCount,
            },
            view = request.View,
            filtered_item_count = page.FilteredCount,
            offset = page.Offset,
            returned_item_count = page.Items.Length,
            next_offset = page.NextOffset,
            items = page.Items,
            sensitive_values_included = request.IncludeSensitive,
            source_locations_included = request.IncludeSource,
            package_hashes_included = request.IncludeHashes,
            raw_xml_returned = false,
            mutation_performed = false,
            word_opened = false,
            runtime = "dotnet-native",
            python_used = false,
            performance = new
            {
                total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            },
        };
    }

    private static DiffPage PageSemanticDifferences(
        WordSemanticDiffResult result,
        SemanticDiffRequest request
    )
    {
        var filtered = result.SemanticDifferences
            .Where(difference =>
                request.NodeKinds is null
                || request.NodeKinds.Contains(difference.NodeKind)
            )
            .Where(difference =>
                request.ChangeKinds is null
                || difference.Kinds.Any(request.ChangeKinds.Contains)
            )
            .Where(difference =>
                request.ScopeFamilies is null
                || request.ScopeFamilies.Contains(ToSnakeCase(
                    difference.After?.ScopeFamily
                        ?? difference.Before?.ScopeFamily
                        ?? string.Empty
                ))
            )
            .ToArray();
        return Page(
            filtered,
            request,
            difference => (object)new
            {
                difference_id = difference.DifferenceId,
                kinds = difference.Kinds.Select(kind => ToSnakeCase(kind.ToString())).ToArray(),
                node_kind = ToSnakeCase(difference.NodeKind.ToString()),
                match_basis = difference.MatchBasis is null
                    ? null
                    : ToSnakeCase(difference.MatchBasis.Value.ToString()),
                match_confidence = difference.MatchConfidence is null
                    ? null
                    : ToSnakeCase(difference.MatchConfidence.Value.ToString()),
                match_score = difference.MatchScore,
                before = DiffLocationItem(difference.Before, request.IncludeSource),
                after = DiffLocationItem(difference.After, request.IncludeSource),
                text = DiffTextItem(difference.Text, request),
                property_changes = difference.Properties.Select(property => new
                {
                    name = BoundForResponse(property.Name, 128),
                    before_present = property.BeforeValue is not null,
                    after_present = property.AfterValue is not null,
                    before_fingerprint = FingerprintSensitiveValue(property.BeforeValue),
                    after_fingerprint = FingerprintSensitiveValue(property.AfterValue),
                    before_value = request.IncludeSensitive
                        ? BoundForResponse(property.BeforeValue, 240)
                        : null,
                    after_value = request.IncludeSensitive
                        ? BoundForResponse(property.AfterValue, 240)
                        : null,
                }).ToArray(),
                before_subtree_fingerprint = request.IncludeHashes
                    ? difference.BeforeSubtreeFingerprint
                    : null,
                after_subtree_fingerprint = request.IncludeHashes
                    ? difference.AfterSubtreeFingerprint
                    : null,
            }
        );
    }

    private static object? DiffLocationItem(
        WordSemanticNodeLocation? location,
        bool includeSource
    ) => location is null ? null : new
    {
        node_id = location.NodeId.Value,
        parent_id = location.ParentId?.Value,
        sibling_index = location.SiblingIndex,
        scope_family = ToSnakeCase(location.ScopeFamily),
        source_part_uri = includeSource
            ? BoundForResponse(location.SourcePartUri, 512)
            : null,
        source_path = includeSource
            ? BoundForResponse(location.SourcePath, 1_024)
            : null,
        source_element_ordinal = includeSource
            ? location.SourceElementOrdinal
            : (int?)null,
    };

    private static object? DiffTextItem(
        WordSemanticTextDifference? text,
        SemanticDiffRequest request
    ) => text is null ? null : new
    {
        before = DiffTextSnapshotItem(text.Before, request),
        after = DiffTextSnapshotItem(text.After, request),
    };

    private static object? DiffTextSnapshotItem(
        WordSemanticTextSnapshot? snapshot,
        SemanticDiffRequest request
    )
    {
        if (snapshot is null)
        {
            return null;
        }
        string? preview = null;
        var previewTruncated = false;
        if (request.IncludeSensitive && request.TextPreviewCharacters > 0)
        {
            previewTruncated = snapshot.CharacterCount > request.TextPreviewCharacters;
            preview = snapshot.CapturedText[..Math.Min(
                request.TextPreviewCharacters,
                snapshot.CapturedText.Length
            )];
        }
        return new
        {
            character_count = snapshot.CharacterCount,
            comparison_fingerprint = request.IncludeHashes ? snapshot.Sha256 : null,
            text_preview = preview,
            text_preview_truncated = previewTruncated,
        };
    }

    private static DiffPage PageEntryDifferences(
        WordSemanticDiffResult result,
        SemanticDiffRequest request
    ) => Page(
        result.EntryDifferences,
        request,
        entry => (object)new
        {
            kind = ToSnakeCase(entry.Kind.ToString()),
            entry_name = BoundForResponse(entry.EntryName, 512),
            part_uri = BoundForResponse(entry.PartUri, 512),
            content_type = BoundForResponse(entry.ContentType, 256),
            before_bytes = entry.BeforeBytes,
            after_bytes = entry.AfterBytes,
            byte_delta = (entry.AfterBytes ?? 0) - (entry.BeforeBytes ?? 0),
            is_infrastructure = entry.IsInfrastructure,
            is_projected_semantic_part = entry.IsProjectedSemanticPart,
            before_sha256 = request.IncludeHashes ? entry.BeforeSha256 : null,
            after_sha256 = request.IncludeHashes ? entry.AfterSha256 : null,
        }
    );

    private static DiffPage PageDiffDiagnostics(
        WordSemanticDiffResult result,
        SemanticDiffRequest request
    ) => Page(
        result.Diagnostics,
        request,
        diagnostic => (object)new
        {
            code = diagnostic.Code,
            message = BoundForResponse(diagnostic.Message, 512),
            node_kind = diagnostic.NodeKind is null
                ? null
                : ToSnakeCase(diagnostic.NodeKind.Value.ToString()),
            scope_family = diagnostic.ScopeFamily is null
                ? null
                : ToSnakeCase(diagnostic.ScopeFamily),
            before_count = diagnostic.BeforeCount,
            after_count = diagnostic.AfterCount,
        }
    );

    private static DiffPage Page<T>(
        IReadOnlyList<T> source,
        SemanticDiffRequest request,
        Func<T, object> project
    )
    {
        var items = source.Skip(request.Offset)
            .Take(request.MaxItems)
            .Select(project)
            .ToArray();
        var nextOffset = request.Offset + items.Length < source.Count
            ? request.Offset + items.Length
            : (int?)null;
        return new DiffPage(source.Count, request.Offset, nextOffset, items);
    }

    private static SemanticDiffRequest ParseSemanticDiffRequest(JsonElement arguments)
    {
        var view = arguments.String("view", "summary");
        if (view is not ("summary" or "changes" or "entries" or "diagnostics"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "view must be summary, changes, entries, or diagnostics"
            );
        }
        var offsetValue = arguments.NullableInt64("offset") ?? 0;
        var maxItemsValue = arguments.NullableInt64("max_items") ?? 50;
        var previewValue = arguments.NullableInt64("text_preview_chars") ?? 0;
        if (offsetValue is < 0 or > int.MaxValue)
        {
            throw new NativeToolException("INVALID_INPUT", "offset is out of range");
        }
        if (maxItemsValue is < 1 or > 200)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_items must be between 1 and 200"
            );
        }
        if (previewValue is < 0 or > 400)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "text_preview_chars must be between 0 and 400"
            );
        }
        var includeSensitive = arguments.Boolean("include_sensitive", false);
        if (previewValue > 0 && !includeSensitive)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "text_preview_chars requires include_sensitive=true"
            );
        }
        var minimumSimilarity = arguments.NullableDouble("minimum_similarity") ?? 0.56;
        var options = new WordSemanticDiffOptions
        {
            CompareText = arguments.Boolean("compare_text", true),
            CompareProperties = arguments.Boolean("compare_properties", true),
            CompareWhitespace = arguments.Boolean("compare_whitespace", true),
            CaseSensitive = arguments.Boolean("case_sensitive", true),
            DetectMoves = arguments.Boolean("detect_moves", true),
            MinimumContextSimilarity = minimumSimilarity,
        };
        return new SemanticDiffRequest(
            view,
            (int)offsetValue,
            (int)maxItemsValue,
            includeSensitive,
            (int)previewValue,
            arguments.Boolean("include_source", false),
            arguments.Boolean("include_hashes", false),
            ParseDiffNodeKinds(arguments),
            ParseDifferenceKinds(arguments),
            ParseScopeFamilies(arguments),
            OptionalFingerprint(arguments, "expected_before_fingerprint"),
            OptionalFingerprint(arguments, "expected_after_fingerprint"),
            options
        );
    }

    private static IReadOnlySet<WordSemanticNodeKind>? ParseDiffNodeKinds(
        JsonElement arguments
    ) => ParseDiffEnumSet(
        arguments,
        "node_kinds",
        SemanticNodeKinds
    );

    private static IReadOnlySet<WordSemanticDifferenceKind>? ParseDifferenceKinds(
        JsonElement arguments
    ) => ParseDiffEnumSet(
        arguments,
        "change_kinds",
        SemanticDifferenceKinds
    );

    private static IReadOnlySet<TEnum>? ParseDiffEnumSet<TEnum>(
        JsonElement arguments,
        string name,
        IReadOnlyDictionary<string, TEnum> allowed
    ) where TEnum : struct, Enum
    {
        if (!arguments.TryGetProperty(name, out var node))
        {
            return null;
        }
        if (node.ValueKind != JsonValueKind.Array)
        {
            throw new NativeToolException("INVALID_INPUT", $"{name} must be an array");
        }
        var result = new HashSet<TEnum>();
        var itemCount = 0;
        foreach (var item in node.EnumerateArray())
        {
            itemCount++;
            if (
                itemCount > allowed.Count
                || item.ValueKind != JsonValueKind.String
                || item.GetString() is not { } raw
                || !allowed.TryGetValue(raw, out var value)
                || !result.Add(value)
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"{name} contains an invalid or duplicate value"
                );
            }
        }
        if (result.Count == 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} must not be empty when provided"
            );
        }
        return result;
    }

    private static IReadOnlySet<string>? ParseScopeFamilies(JsonElement arguments)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "main",
            "header",
            "footer",
            "footnotes",
            "endnotes",
            "comments",
            "glossary_document",
        };
        if (!arguments.TryGetProperty("scope_families", out var node))
        {
            return null;
        }
        if (node.ValueKind != JsonValueKind.Array)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "scope_families must be an array"
            );
        }
        var result = new HashSet<string>(StringComparer.Ordinal);
        var itemCount = 0;
        foreach (var item in node.EnumerateArray())
        {
            itemCount++;
            if (
                itemCount > allowed.Count
                || item.ValueKind != JsonValueKind.String
                || item.GetString() is not { } raw
                || !allowed.Contains(raw)
                || !result.Add(raw)
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "scope_families contains an invalid or duplicate value"
                );
            }
        }
        if (result.Count == 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "scope_families must not be empty when provided"
            );
        }
        return result;
    }

    private static string? OptionalFingerprint(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var node))
        {
            return null;
        }
        if (
            node.ValueKind != JsonValueKind.String
            || node.GetString() is not { } value
            || value.Length != 64
            || value.Any(character => !Uri.IsHexDigit(character))
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} must be a 64-character hexadecimal package fingerprint"
            );
        }
        return value.ToLowerInvariant();
    }

    private static void VerifyOptionalFingerprint(
        string actual,
        string? expected,
        string side
    )
    {
        if (
            expected is not null
            && !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                $"The {side} package does not match its expected fingerprint"
            );
        }
    }

    private static string ResolveComparedPackagePath(
        JsonElement arguments,
        string argumentName
    )
    {
        var rawPath = arguments.String(argumentName);
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{argumentName} must be a non-empty string"
            );
        }
        string path;
        try
        {
            path = Path.GetFullPath(rawPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{argumentName} is not a valid filesystem path"
            );
        }
        if (!File.Exists(path))
        {
            throw new NativeToolException(
                "NOT_FOUND",
                $"The package supplied as {argumentName} does not exist"
            );
        }
        if (!InspectWordPackageContract.IsSupportedFileName(path))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{argumentName} must be DOCX, DOCM, DOTX, or DOTM"
            );
        }
        return path;
    }

    private sealed record SemanticDiffRequest(
        string View,
        int Offset,
        int MaxItems,
        bool IncludeSensitive,
        int TextPreviewCharacters,
        bool IncludeSource,
        bool IncludeHashes,
        IReadOnlySet<WordSemanticNodeKind>? NodeKinds,
        IReadOnlySet<WordSemanticDifferenceKind>? ChangeKinds,
        IReadOnlySet<string>? ScopeFamilies,
        string? ExpectedBeforeFingerprint,
        string? ExpectedAfterFingerprint,
        WordSemanticDiffOptions Options
    );

    private sealed record DiffPage(
        int FilteredCount,
        int Offset,
        int? NextOffset,
        object[] Items
    )
    {
        internal static DiffPage Empty { get; } = new(0, 0, null, Array.Empty<object>());
    }
}
