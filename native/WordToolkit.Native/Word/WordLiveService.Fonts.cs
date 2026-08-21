using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> InspectPackageFontsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveInspectablePackagePath(arguments);
        var view = arguments.String("view", "fonts");
        if (view is not "fonts" and not "embedded_faces" and not "unreferenced")
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "view must be fonts, embedded_faces, or unreferenced"
            );
        }
        var detail = arguments.String("detail", "metadata");
        if (detail is not "metadata" and not "declared")
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "detail must be metadata or declared"
            );
        }
        string? fontName = null;
        if (arguments.TryGetProperty("font_name", out var fontNameNode))
        {
            if (
                fontNameNode.ValueKind != JsonValueKind.String
                || fontNameNode.GetString() is not { Length: >= 1 and <= 1_024 } value
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "font_name must contain between 1 and 1024 characters"
                );
            }
            fontName = value;
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

        var includeHashes = arguments.Boolean("include_hashes", false);
        var includeIssues = arguments.Boolean("include_issues", true);
        var includeSource = arguments.Boolean("include_source", false);
        try
        {
            var package = new OpcPackageReader().Read(path, cancellationToken);
            var semantic = new WordSemanticProjector().Project(
                package,
                cancellationToken
            );
            var graph = new WordFontTableGraphBuilder().Build(
                package,
                semantic,
                cancellationToken
            );
            var matching = FontTableItems(
                graph,
                view,
                detail,
                fontName,
                includeHashes,
                includeSource
            );
            var page = matching.Skip((int)offset).Take((int)maximum).ToArray();
            var consumed = (long)offset + page.Length;
            var returnedIssues = includeIssues
                ? graph.Issues.Take(40).Select(issue => new
                {
                    code = BoundForResponse(issue.Code, 128),
                    severity = ToSnakeCase(issue.Severity.ToString()),
                    font_name = BoundForResponse(issue.FontName, 1_024),
                    relationship_id = BoundForResponse(
                        issue.RelationshipId,
                        1_024
                    ),
                    part_uri = includeSource
                        ? BoundForResponse(issue.PartUri, 2_048)
                        : null,
                    message = BoundForResponse(issue.Message, 512),
                }).ToArray()
                : null;
            return Task.FromResult<object>(new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = graph.PackageFingerprint,
                main_part_uri = graph.MainPartUri,
                font_table_part_uri = includeSource
                    ? BoundForResponse(graph.FontTablePartUri, 512)
                    : null,
                has_font_table_part = graph.HasFontTablePart,
                font_count = graph.Fonts.Count,
                embedded_face_count = graph.Fonts.Sum(font =>
                    font.EmbeddedFaces.Count
                ),
                word_readable_embedded_face_count = graph.Fonts.Sum(font =>
                    font.EmbeddedFaces.Count(face => face.IsWordReadable)
                ),
                referenced_embedded_font_part_count =
                    graph.ReferencedEmbeddedFontPartUris.Count,
                unreferenced_embedded_font_part_count =
                    graph.UnreferencedEmbeddedFontPartUris.Count,
                view,
                detail,
                font_name_filter = BoundForResponse(fontName, 1_024),
                hashes_included = includeHashes,
                matched_item_count = matching.Count,
                offset,
                returned_item_count = page.Length,
                next_offset = consumed < matching.Count ? (int)consumed : (int?)null,
                items = page,
                issue_count = graph.Issues.Count,
                issues = returnedIssues,
                issues_truncated = returnedIssues is not null
                    && graph.Issues.Count > returnedIssues.Length
                        ? true
                        : (bool?)null,
                unmodeled_root_elements = detail == "declared"
                    && graph.UnmodeledRootElements.Count > 0
                        ? graph.UnmodeledRootElements.Take(40)
                            .Select(value => BoundForResponse(value, 256))
                            .ToArray()
                        : null,
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            });
        }
        catch (WordFontTableLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "Font-table graph exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordFontTableProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a Word font-table graph",
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

    private static IReadOnlyList<object> FontTableItems(
        WordFontTableGraph graph,
        string view,
        string detail,
        string? fontName,
        bool includeHashes,
        bool includeSource
    )
    {
        var fonts = graph.Fonts.Where(font =>
            fontName is null
            || string.Equals(font.Name, fontName, StringComparison.OrdinalIgnoreCase)
        );
        return view switch
        {
            "fonts" => fonts.Select(font => (object)new
            {
                name = BoundForResponse(font.Name, 1_024),
                alternate_name = BoundForResponse(font.AlternateName, 1_024),
                family = BoundForResponse(font.Family, 128),
                pitch = BoundForResponse(font.Pitch, 128),
                not_true_type = font.NotTrueType,
                embedded_face_count = font.EmbeddedFaces.Count,
                has_word_readable_embedded_face =
                    font.HasWordReadableEmbeddedFace,
                character_set = detail == "declared"
                    ? BoundForResponse(font.CharacterSet, 128)
                    : null,
                panose = detail == "declared"
                    ? BoundForResponse(font.Panose, 128)
                    : null,
                signature = detail == "declared" && font.Signature is { } signature
                    ? new
                    {
                        unicode_subset_0 = signature.UnicodeSubset0,
                        unicode_subset_1 = signature.UnicodeSubset1,
                        unicode_subset_2 = signature.UnicodeSubset2,
                        unicode_subset_3 = signature.UnicodeSubset3,
                        code_page_subset_0 = signature.CodePageSubset0,
                        code_page_subset_1 = signature.CodePageSubset1,
                    }
                    : null,
                unmodeled_elements = detail == "declared"
                    && font.UnmodeledElements.Count > 0
                        ? font.UnmodeledElements.Take(40)
                            .Select(value => BoundForResponse(value, 256))
                            .ToArray()
                        : null,
                unmodeled_attributes = detail == "declared"
                    && font.UnmodeledAttributes.Count > 0
                        ? font.UnmodeledAttributes.Take(40)
                            .Select(value => BoundForResponse(value, 256))
                            .ToArray()
                        : null,
                source_element_ordinal = includeSource
                    ? font.SourceElementOrdinal
                    : (int?)null,
            }).ToArray(),
            "embedded_faces" => fonts.SelectMany(font =>
                font.EmbeddedFaces.Select(face => (object)new
                {
                    font_name = BoundForResponse(font.Name, 1_024),
                    face_kind = ToSnakeCase(face.Kind.ToString()),
                    relationship_id = BoundForResponse(
                        face.RelationshipId,
                        1_024
                    ),
                    part_uri = includeSource
                        ? BoundForResponse(face.PartUri, 2_048)
                        : null,
                    content_type = BoundForResponse(face.ContentType, 512),
                    byte_length = face.ByteLength,
                    sha256 = includeHashes ? face.Sha256 : null,
                    is_obfuscated = face.IsObfuscated,
                    word_readable = face.IsWordReadable,
                    has_valid_font_key = face.HasValidFontKey,
                    has_all_zero_font_key = face.HasAllZeroFontKey,
                    font_key = detail == "declared"
                        ? BoundForResponse(face.FontKey, 128)
                        : null,
                    unmodeled_attributes = detail == "declared"
                        && face.UnmodeledAttributes.Count > 0
                            ? face.UnmodeledAttributes.Take(40)
                                .Select(value => BoundForResponse(value, 256))
                                .ToArray()
                            : null,
                    source_element_ordinal = includeSource
                        ? face.SourceElementOrdinal
                        : (int?)null,
                })
            ).ToArray(),
            _ => graph.UnreferencedEmbeddedFontPartUris.Select(uri => (object)new
            {
                part_uri = BoundForResponse(uri, 2_048),
            }).ToArray(),
        };
    }
}
