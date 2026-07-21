using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> InspectPackageSemanticsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveInspectablePackagePath(arguments);
        var requestedMaximum = arguments.NullableInt64("max_nodes") ?? 80;
        if (requestedMaximum is < 1 or > 200)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_nodes must be between 1 and 200"
            );
        }

        var requestedPreview = arguments.NullableInt64("text_preview_chars") ?? 160;
        if (requestedPreview is < 0 or > 400)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "text_preview_chars must be between 0 and 400"
            );
        }

        var includeSourcePaths = arguments.Boolean("include_source_paths", false);
        try
        {
            var package = new OpcPackageReader().Read(path, cancellationToken);
            var document = new WordSemanticProjector().Project(
                package,
                cancellationToken
            );
            var counts = document.Nodes
                .GroupBy(node => node.Kind)
                .OrderBy(group => group.Key)
                .ToDictionary(
                    group => ToSnakeCase(group.Key.ToString()),
                    group => group.Count(),
                    StringComparer.Ordinal
                );
            var outlineCandidates = document.Nodes
                .Where(node => IsOutlineNode(node.Kind))
                .OrderBy(node => node.SourceOrder)
                .ToArray();
            var maxNodes = (int)requestedMaximum;
            var previewCharacters = (int)requestedPreview;
            var outline = outlineCandidates
                .Take(maxNodes)
                .Select(node =>
                {
                    var rawPreview = previewCharacters == 0
                        ? null
                        : node.TextPreview(previewCharacters + 1);
                    var previewTruncated = rawPreview?.Length > previewCharacters;
                    var preview = previewTruncated == true
                        ? rawPreview![..previewCharacters]
                        : rawPreview;
                    return new
                    {
                        node_id = node.Id.Value,
                        kind = ToSnakeCase(node.Kind.ToString()),
                        parent_id = node.ParentId?.Value,
                        text_preview = preview,
                        text_preview_truncated = previewTruncated,
                        properties = BoundProperties(node.Properties, 160),
                        child_count = node.Children.Count,
                        source_part_uri = includeSourcePaths
                            ? BoundForResponse(node.SourcePartUri, 512)
                            : null,
                        source_path = includeSourcePaths
                            ? BoundForResponse(node.SourcePath, 1024)
                            : null,
                        source_element_ordinal = includeSourcePaths
                            ? node.SourceElementOrdinal
                            : (int?)null,
                    };
                })
                .ToArray();
            var returnedWarnings = document.Warnings
                .Take(40)
                .Select(warning => BoundForResponse(warning, 512))
                .ToArray();
            var result = new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = package.Fingerprint,
                main_part_uri = document.MainPartUri,
                semantic_root_id = document.Root.Id.Value,
                semantic_node_count = document.NodeCount,
                node_counts = counts,
                outline,
                outline_truncated = outlineCandidates.Length > outline.Length,
                warnings = returnedWarnings,
                warnings_truncated = document.Warnings.Count > returnedWarnings.Length,
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            };
            return Task.FromResult<object>(result);
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

    private static bool IsOutlineNode(WordSemanticNodeKind kind) => kind is
        WordSemanticNodeKind.Paragraph
        or WordSemanticNodeKind.Table
        or WordSemanticNodeKind.Equation
        or WordSemanticNodeKind.Field
        or WordSemanticNodeKind.ContentControl
        or WordSemanticNodeKind.Bookmark
        or WordSemanticNodeKind.CommentAnchor
        or WordSemanticNodeKind.Revision
        or WordSemanticNodeKind.Drawing
        or WordSemanticNodeKind.AlternateContent
        or WordSemanticNodeKind.ExtensionIsland;

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (
                index > 0
                && char.IsUpper(character)
                && (
                    char.IsLower(value[index - 1])
                    || (
                        index + 1 < value.Length
                        && char.IsLower(value[index + 1])
                    )
                )
            )
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static IReadOnlyDictionary<string, string>? BoundProperties(
        IReadOnlyDictionary<string, string> properties,
        int maxValueCharacters
    )
    {
        return properties.Count == 0
            ? null
            : properties.ToDictionary(
                pair => BoundForResponse(pair.Key, 128)!,
                pair => BoundForResponse(pair.Value, maxValueCharacters)!,
                StringComparer.Ordinal
            );
    }
}
