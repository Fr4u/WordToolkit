using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static readonly IReadOnlyDictionary<string, WordSemanticNodeKind>
        SemanticNodeKinds = Enum.GetValues<WordSemanticNodeKind>()
            .ToDictionary(
                kind => ToSnakeCase(kind.ToString()),
                kind => kind,
                StringComparer.Ordinal
            );

    private Task<object> QueryPackageSemanticsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var query = ParseSemanticQuery(arguments);
            var semanticIndexId = OptionalString(arguments, "semantic_index_id");
            WordSemanticQueryResult result;
            string fileName;
            if (semanticIndexId is not null)
            {
                if (arguments.TryGetProperty("local_path", out _))
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "Use exactly one of local_path or semantic_index_id"
                    );
                }
                var expectedFingerprint = RequiredSha256(
                    arguments,
                    "expected_package_fingerprint"
                );
                var entry = GetSemanticIndex(RequiredSemanticIndexId(arguments));
                if (!string.Equals(
                        expectedFingerprint,
                        entry.Index.PackageFingerprint,
                        StringComparison.OrdinalIgnoreCase
                    ))
                {
                    throw new NativeToolException(
                        "VERSION_CONFLICT",
                        "The semantic index does not match expected_package_fingerprint"
                    );
                }
                result = new WordSemanticQueryEngine().Query(
                    entry.Index,
                    query,
                    cancellationToken
                );
                fileName = entry.FileName;
            }
            else
            {
                if (!arguments.TryGetProperty("local_path", out _))
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "Use exactly one of local_path or semantic_index_id"
                    );
                }
                if (arguments.TryGetProperty("expected_package_fingerprint", out _))
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "expected_package_fingerprint is only valid with semantic_index_id"
                    );
                }
                var path = ResolveInspectablePackagePath(arguments);
                var package = new OpcPackageReader().Read(path, cancellationToken);
                var document = new WordSemanticProjector().Project(
                    package,
                    cancellationToken
                );
                result = new WordSemanticQueryEngine().Query(
                    document,
                    query,
                    cancellationToken
                );
                fileName = Path.GetFileName(path);
            }
            var matches = result.Matches.Select(match => new
            {
                node_id = match.NodeId.Value,
                kind = ToSnakeCase(match.Kind.ToString()),
                parent_id = match.ParentId?.Value,
                source_order = match.SourceOrder,
                text_preview = match.TextPreview,
                text_preview_truncated = match.TextPreviewTruncated,
                properties = match.Properties is null
                    ? null
                    : BoundProperties(match.Properties, 160),
                source_part_uri = match.SourcePartUri is null
                    ? null
                    : BoundForResponse(match.SourcePartUri, 512),
                source_path = match.SourcePath is null
                    ? null
                    : BoundForResponse(match.SourcePath, 1024),
                source_element_ordinal = match.SourceElementOrdinal,
            }).ToArray();
            return Task.FromResult<object>(new
            {
                file_name = fileName,
                package_fingerprint = result.PackageFingerprint,
                semantic_index_used = result.SemanticIndexUsed,
                semantic_index_id = semanticIndexId,
                semantic_index_fingerprint = result.SemanticIndexFingerprint,
                candidate_seed = result.CandidateSeed,
                total_node_count = result.TotalNodeCount,
                scanned_node_count = result.ScannedNodeCount,
                matched_node_count = result.MatchedNodeCount,
                offset = result.Offset,
                returned_node_count = result.ReturnedNodeCount,
                next_offset = result.NextOffset,
                matches,
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            });
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
        catch (ArgumentException exception)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                BoundForResponse(exception.Message, 512) ?? "Invalid semantic query"
            );
        }
        catch (KeyNotFoundException exception)
        {
            throw new NativeToolException(
                "TARGET_NOT_FOUND",
                BoundForResponse(exception.Message, 512) ?? "Semantic scope was not found"
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

    private static WordSemanticQuery ParseSemanticQuery(JsonElement arguments)
    {
        var offset = arguments.NullableInt64("offset") ?? 0;
        var limit = arguments.NullableInt64("max_results") ?? 80;
        var preview = arguments.NullableInt64("text_preview_chars") ?? 160;
        if (offset is < 0 or > int.MaxValue)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "offset must be between 0 and 2147483647"
            );
        }

        if (limit is < 1 or > 200)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_results must be between 1 and 200"
            );
        }

        if (preview is < 0 or > 400)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "text_preview_chars must be between 0 and 400"
            );
        }

        var textMatch = arguments.String("text_match", "contains") switch
        {
            "contains" => WordSemanticTextMatchMode.Contains,
            "equals" => WordSemanticTextMatchMode.Equals,
            "starts_with" => WordSemanticTextMatchMode.StartsWith,
            "ends_with" => WordSemanticTextMatchMode.EndsWith,
            _ => throw new NativeToolException(
                "INVALID_INPUT",
                "text_match must be contains, equals, starts_with, or ends_with"
            ),
        };
        var textScope = arguments.String("text_scope", "node") switch
        {
            "node" => WordSemanticTextScope.Node,
            "subtree" => WordSemanticTextScope.Subtree,
            _ => throw new NativeToolException(
                "INVALID_INPUT",
                "text_scope must be node or subtree"
            ),
        };
        return new WordSemanticQuery
        {
            Kinds = ParseSemanticKinds(arguments),
            Text = OptionalString(arguments, "text"),
            TextMatch = textMatch,
            TextScope = textScope,
            CaseSensitive = arguments.Boolean("case_sensitive", false),
            PropertyEquals = ParsePropertyEquals(arguments),
            Ancestor = ParseRelatedPredicate(arguments, "ancestor"),
            Descendant = ParseRelatedPredicate(arguments, "descendant"),
            WithinNodeId = OptionalString(arguments, "within_node_id") is { } nodeId
                ? new SemanticNodeId(nodeId)
                : null,
            SourcePartUri = OptionalString(arguments, "source_part_uri"),
            Offset = (int)offset,
            Limit = (int)limit,
            TextPreviewCharacters = (int)preview,
            IncludeProperties = arguments.Boolean("include_properties", false),
            IncludeSource = arguments.Boolean("include_source", false),
        };
    }

    private static WordSemanticRelatedNodePredicate? ParseRelatedPredicate(
        JsonElement arguments,
        string name
    )
    {
        if (!arguments.TryGetProperty(name, out var value))
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} must be an object"
            );
        }

        foreach (var property in value.EnumerateObject())
        {
            if (property.Name is not "kinds" and not "property_equals")
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"{name} contains an unknown predicate"
                );
            }
        }

        return new WordSemanticRelatedNodePredicate
        {
            Kinds = ParseSemanticKinds(value),
            PropertyEquals = ParsePropertyEquals(value),
        };
    }

    private static IReadOnlyCollection<WordSemanticNodeKind>? ParseSemanticKinds(
        JsonElement arguments
    )
    {
        if (!arguments.TryGetProperty("kinds", out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new NativeToolException("INVALID_INPUT", "kinds must be an array");
        }

        var result = new HashSet<WordSemanticNodeKind>();
        foreach (var item in value.EnumerateArray())
        {
            if (
                item.ValueKind != JsonValueKind.String
                || item.GetString() is not { } raw
                || !SemanticNodeKinds.TryGetValue(raw, out var kind)
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "kinds contains an unknown semantic node kind"
                );
            }

            if (!result.Add(kind))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "kinds cannot contain duplicates"
                );
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string>? ParsePropertyEquals(
        JsonElement arguments
    )
    {
        if (!arguments.TryGetProperty("property_equals", out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "property_equals must be an object"
            );
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "property_equals values must be strings"
                );
            }

            if (!result.TryAdd(property.Name, property.Value.GetString() ?? string.Empty))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "property_equals cannot contain duplicate names"
                );
            }
        }

        return result;
    }

    private static string? OptionalString(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"Argument '{name}' must be string"
            );
        }

        return value.GetString() ?? string.Empty;
    }
}
