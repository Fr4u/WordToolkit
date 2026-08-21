using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> InspectPackageThemeAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveInspectablePackagePath(arguments);
        var view = arguments.String("view", "colors");
        if (view is not "colors" and not "fonts" and not "format")
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "view must be colors, fonts, or format"
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

        var includeIssues = arguments.Boolean("include_issues", true);
        var includeSource = arguments.Boolean("include_source", false);
        try
        {
            var package = new OpcPackageReader().Read(path, cancellationToken);
            var semantic = new WordSemanticProjector().Project(
                package,
                cancellationToken
            );
            var graph = new WordThemeGraphBuilder().Build(
                package,
                semantic,
                cancellationToken
            );
            var matching = ThemeItems(graph, view, detail, includeSource);
            var page = matching.Skip((int)offset).Take((int)maximum).ToArray();
            var consumed = (long)offset + page.Length;
            var returnedIssues = includeIssues
                ? graph.Issues.Take(40).Select(issue => new
                {
                    code = BoundForResponse(issue.Code, 128),
                    severity = ToSnakeCase(issue.Severity.ToString()),
                    color_slot = BoundForResponse(issue.ColorSlot, 64),
                    font_collection = BoundForResponse(issue.FontCollection, 64),
                    message = BoundForResponse(issue.Message, 512),
                }).ToArray()
                : null;
            return Task.FromResult<object>(new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = graph.PackageFingerprint,
                main_part_uri = graph.MainPartUri,
                theme_part_uri = graph.ThemePartUri,
                has_theme_part = graph.HasThemePart,
                theme_name = BoundForResponse(graph.Name, 512),
                color_scheme_name = BoundForResponse(graph.ColorScheme?.Name, 512),
                color_count = graph.ColorScheme?.Colors.Count ?? 0,
                font_scheme_name = BoundForResponse(graph.FontScheme?.Name, 512),
                font_item_count = CountThemeFontItems(graph.FontScheme),
                format_scheme_name = BoundForResponse(graph.FormatScheme?.Name, 512),
                view,
                detail,
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
                unmodeled_theme_elements = detail == "declared"
                    && graph.UnmodeledThemeElements.Count > 0
                        ? graph.UnmodeledThemeElements.Take(40)
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
        catch (WordThemeLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "Theme graph exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordThemeProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a Word theme graph",
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

    private static IReadOnlyList<object> ThemeItems(
        WordThemeGraph graph,
        string view,
        string detail,
        bool includeSource
    ) => view switch
    {
        "colors" => graph.ColorScheme?.Colors.Select(color => (object)new
        {
            slot = color.Slot,
            source_kind = ToSnakeCase(color.SourceKind.ToString()),
            base_rgb = color.BaseRgb,
            deterministically_resolvable = color.IsDeterministicallyResolvable,
            source_value = detail == "declared"
                ? BoundForResponse(color.SourceValue, 256)
                : null,
            last_color = detail == "declared" ? color.LastColor : null,
            transforms = detail == "declared" && color.Transforms.Count > 0
                ? color.Transforms.Take(40).Select(transform => new
                {
                    name = BoundForResponse(transform.Name, 128),
                    value = BoundForResponse(transform.Value, 128),
                    source_element_ordinal = includeSource
                        ? transform.SourceElementOrdinal
                        : (int?)null,
                }).ToArray()
                : null,
            transforms_truncated = detail == "declared"
                && color.Transforms.Count > 40
                    ? true
                    : (bool?)null,
            unmodeled_elements = detail == "declared"
                && color.UnmodeledElements.Count > 0
                    ? color.UnmodeledElements.Take(40)
                        .Select(value => BoundForResponse(value, 256))
                        .ToArray()
                    : null,
            source_element_ordinal = includeSource
                ? color.SourceElementOrdinal
                : (int?)null,
        }).ToArray() ?? Array.Empty<object>(),
        "fonts" => ThemeFontItems(graph.FontScheme, detail, includeSource),
        _ => graph.FormatScheme is { } format
            ?
            [
                new
                {
                    name = BoundForResponse(format.Name, 512),
                    fill_style_count = format.FillStyleCount,
                    line_style_count = format.LineStyleCount,
                    effect_style_count = format.EffectStyleCount,
                    background_fill_style_count = format.BackgroundFillStyleCount,
                    unmodeled_elements = detail == "declared"
                        && format.UnmodeledElements.Count > 0
                            ? format.UnmodeledElements.Take(40)
                                .Select(value => BoundForResponse(value, 256))
                                .ToArray()
                            : null,
                    source_element_ordinal = includeSource
                        ? format.SourceElementOrdinal
                        : (int?)null,
                },
            ]
            : Array.Empty<object>(),
    };

    private static IReadOnlyList<object> ThemeFontItems(
        WordThemeFontScheme? scheme,
        string detail,
        bool includeSource
    )
    {
        if (scheme is null)
        {
            return Array.Empty<object>();
        }

        var result = new List<object>();
        foreach (var collection in new[] { scheme.Major, scheme.Minor })
        {
            result.Add(ThemeTypefaceItem(collection, "latin", collection.Latin, detail, includeSource));
            result.Add(ThemeTypefaceItem(collection, "east_asian", collection.EastAsian, detail, includeSource));
            result.Add(ThemeTypefaceItem(collection, "complex_script", collection.ComplexScript, detail, includeSource));
            result.AddRange(collection.SupplementalFonts.Select(font => (object)new
            {
                collection = ToSnakeCase(collection.Kind.ToString()),
                role = "supplemental",
                script = BoundForResponse(font.Script, 64),
                typeface = BoundForResponse(font.Typeface, 512),
                panose = (string?)null,
                pitch_family = (string?)null,
                character_set = (string?)null,
                unmodeled_attributes = detail == "declared"
                    && font.UnmodeledAttributes.Count > 0
                        ? font.UnmodeledAttributes.Take(40)
                            .Select(value => BoundForResponse(value, 256))
                            .ToArray()
                        : null,
                source_element_ordinal = includeSource
                    ? font.SourceElementOrdinal
                    : (int?)null,
            }));
        }

        return result;
    }

    private static object ThemeTypefaceItem(
        WordThemeFontCollection collection,
        string role,
        WordThemeTypeface typeface,
        string detail,
        bool includeSource
    ) => new
    {
        collection = ToSnakeCase(collection.Kind.ToString()),
        role,
        script = (string?)null,
        typeface = BoundForResponse(typeface.Typeface, 512),
        panose = detail == "declared" ? BoundForResponse(typeface.Panose, 128) : null,
        pitch_family = detail == "declared"
            ? BoundForResponse(typeface.PitchFamily, 128)
            : null,
        character_set = detail == "declared"
            ? BoundForResponse(typeface.CharacterSet, 128)
            : null,
        unmodeled_attributes = detail == "declared"
            && typeface.UnmodeledAttributes.Count > 0
                ? typeface.UnmodeledAttributes.Take(40)
                    .Select(value => BoundForResponse(value, 256))
                    .ToArray()
                : null,
        source_element_ordinal = includeSource
            ? typeface.SourceElementOrdinal
            : (int?)null,
    };

    private static int CountThemeFontItems(WordThemeFontScheme? scheme) => scheme is null
        ? 0
        : 6
            + scheme.Major.SupplementalFonts.Count
            + scheme.Minor.SupplementalFonts.Count;
}
