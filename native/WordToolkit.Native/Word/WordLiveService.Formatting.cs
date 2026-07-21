using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> ResolvePackageFormattingAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveInspectablePackagePath(arguments);
        var nodeIdValue = arguments.String("node_id");
        if (
            nodeIdValue.Length is < 5 or > 256
            || !nodeIdValue.StartsWith("wdn_", StringComparison.Ordinal)
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "node_id must be a Word semantic node ID"
            );
        }

        var maximum = arguments.NullableInt64("max_properties") ?? 80;
        if (maximum is < 1 or > 200)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_properties must be between 1 and 200"
            );
        }

        var includeProvenance = arguments.Boolean("include_provenance", false);
        var includeSource = arguments.Boolean("include_source", false);
        var includeUnmodeled = arguments.Boolean("include_unmodeled", false);
        var requestedProperties = ReadFormattingPropertyFilter(arguments);
        try
        {
            var package = new OpcPackageReader().Read(path, cancellationToken);
            var semantic = new WordSemanticProjector().Project(
                package,
                cancellationToken
            );
            var styles = new WordStyleGraphBuilder().Build(
                package,
                semantic,
                cancellationToken
            );
            var numbering = new WordNumberingGraphBuilder().Build(
                package,
                semantic,
                styles,
                cancellationToken
            );
            var theme = new WordThemeGraphBuilder().Build(
                package,
                semantic,
                cancellationToken
            );
            var formatting = new WordEffectiveFormattingResolver().Resolve(
                package,
                semantic,
                styles,
                numbering,
                theme,
                new SemanticNodeId(nodeIdValue),
                cancellationToken
            );
            var paragraph = SelectFormattingProperties(
                formatting.ParagraphProperties,
                requestedProperties,
                (int)maximum
            );
            var run = SelectFormattingProperties(
                formatting.RunProperties,
                requestedProperties,
                (int)maximum
            );
            return Task.FromResult<object>(new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = package.Fingerprint,
                node_id = formatting.NodeId.Value,
                node_kind = ToSnakeCase(formatting.NodeKind.ToString()),
                paragraph_node_id = formatting.ParagraphNodeId.Value,
                paragraph_style_id = BoundForResponse(
                    formatting.ParagraphStyleId,
                    253
                ),
                character_style_id = BoundForResponse(
                    formatting.CharacterStyleId,
                    253
                ),
                numbering = FormattingNumbering(formatting.Numbering, includeSource),
                numbering_removed = formatting.NumberingRemoved ? true : (bool?)null,
                theme = new
                {
                    has_theme_part = theme.HasThemePart,
                    name = BoundForResponse(theme.Name, 512),
                    color_scheme_name = BoundForResponse(
                        theme.ColorScheme?.Name,
                        512
                    ),
                    font_scheme_name = BoundForResponse(
                        theme.FontScheme?.Name,
                        512
                    ),
                    part_uri = includeSource
                        ? BoundForResponse(theme.ThemePartUri, 512)
                        : null,
                },
                source_part_uri = includeSource
                    ? BoundForResponse(formatting.SourcePartUri, 512)
                    : null,
                fully_resolved = formatting.IsFullyResolved,
                paragraph_property_count = formatting.ParagraphProperties.Count,
                returned_paragraph_property_count = paragraph.Length,
                paragraph_properties_truncated = paragraph.Length
                    < CountSelected(formatting.ParagraphProperties, requestedProperties)
                        ? true
                        : (bool?)null,
                paragraph_properties = paragraph.ToDictionary(
                    item => item.Name,
                    item => BoundForResponse(item.Value, 160)!,
                    StringComparer.Ordinal
                ),
                run_property_count = formatting.RunProperties.Count,
                returned_run_property_count = run.Length,
                run_properties_truncated = run.Length
                    < CountSelected(formatting.RunProperties, requestedProperties)
                        ? true
                        : (bool?)null,
                run_properties = run.ToDictionary(
                    item => item.Name,
                    item => BoundForResponse(item.Value, 160)!,
                    StringComparer.Ordinal
                ),
                unmatched_property_names = UnmatchedFormattingProperties(
                    formatting,
                    requestedProperties
                ),
                provenance = includeProvenance
                    ? new
                    {
                        paragraph = Provenance(paragraph, includeSource),
                        run = Provenance(run, includeSource),
                    }
                    : null,
                coverage_omissions = formatting.CoverageOmissions
                    .Take(40)
                    .Select(value => BoundForResponse(value, 256))
                    .ToArray(),
                compatibility_warnings = formatting.CompatibilityWarnings.Count == 0
                    ? null
                    : formatting.CompatibilityWarnings.Take(20)
                        .Select(value => BoundForResponse(value, 512))
                        .ToArray(),
                unmodeled_elements = includeUnmodeled
                    ? formatting.UnmodeledElements.Take(80)
                        .Select(value => BoundForResponse(value, 512))
                        .ToArray()
                    : null,
                unmodeled_elements_truncated = includeUnmodeled
                    && formatting.UnmodeledElements.Count > 80
                        ? true
                        : (bool?)null,
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            });
        }
        catch (WordNumberingLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "Numbering graph exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordNumberingProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a Word numbering graph",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
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
        catch (WordFormattingLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "Effective formatting resolution exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordFormattingResolutionException exception)
        {
            throw new NativeToolException(
                "FORMATTING_UNRESOLVED",
                "The requested Word formatting cannot be resolved safely",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordStyleLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "Style graph exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordStyleProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a Word style graph",
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

    private static HashSet<string>? ReadFormattingPropertyFilter(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("property_names", out var node))
        {
            return null;
        }

        if (node.ValueKind != JsonValueKind.Array)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "property_names must be an array of property names"
            );
        }

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in node.EnumerateArray())
        {
            if (
                item.ValueKind != JsonValueKind.String
                || item.GetString() is not { Length: > 0 and <= 128 } value
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Each property_names item must be a non-empty string within 128 characters"
                );
            }

            result.Add(value);
            if (result.Count > 64)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "property_names accepts at most 64 unique names"
                );
            }
        }

        if (result.Count == 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "property_names must not be empty when supplied"
            );
        }

        return result;
    }

    private static WordEffectiveFormattingProperty[] SelectFormattingProperties(
        IReadOnlyDictionary<string, WordEffectiveFormattingProperty> properties,
        IReadOnlySet<string>? requested,
        int maximum
    ) => properties.Values
        .Where(property => requested is null || requested.Contains(property.Name))
        .OrderBy(property => property.Name, StringComparer.Ordinal)
        .Take(maximum)
        .ToArray();

    private static int CountSelected(
        IReadOnlyDictionary<string, WordEffectiveFormattingProperty> properties,
        IReadOnlySet<string>? requested
    ) => requested is null
        ? properties.Count
        : properties.Keys.Count(requested.Contains);

    private static string?[]? UnmatchedFormattingProperties(
        WordEffectiveFormatting formatting,
        IReadOnlySet<string>? requested
    )
    {
        if (requested is null)
        {
            return null;
        }

        var unmatched = requested
            .Where(name =>
                !formatting.ParagraphProperties.ContainsKey(name)
                && !formatting.RunProperties.ContainsKey(name)
            )
            .Order(StringComparer.Ordinal)
            .Select(name => BoundForResponse(name, 128))
            .ToArray();
        return unmatched.Length == 0 ? null : unmatched;
    }

    private static IReadOnlyDictionary<string, object> Provenance(
        IEnumerable<WordEffectiveFormattingProperty> properties,
        bool includeSource
    ) => properties.ToDictionary(
        property => property.Name,
        property => (object)new
        {
            toggle = property.IsToggle ? true : (bool?)null,
            contributions = property.Contributions.Select(item => new
            {
                layer = ToSnakeCase(item.SourceKind.ToString()),
                style_id = BoundForResponse(item.StyleId, 253),
                number_id = item.NumberId,
                abstract_number_id = item.AbstractNumberId,
                level_index = item.LevelIndex,
                numbering_level_source = item.NumberingLevelSourceKind is { } levelSource
                    ? ToSnakeCase(levelSource.ToString())
                    : null,
                theme_token = BoundForResponse(item.ThemeToken, 128),
                theme_color_slot = BoundForResponse(item.ThemeColorSlot, 64),
                theme_font_collection = item.ThemeFontCollection is { } fontCollection
                    ? ToSnakeCase(fontCollection.ToString())
                    : null,
                theme_font_role = item.ThemeFontRole is { } fontRole
                    ? ToSnakeCase(fontRole.ToString())
                    : null,
                declared_value = BoundForResponse(item.DeclaredValue, 160),
                resulting_value = BoundForResponse(item.ResultingValue, 160),
                source_part_uri = includeSource
                    ? BoundForResponse(item.SourcePartUri, 512)
                    : null,
                source_element_ordinal = includeSource
                    ? item.SourceElementOrdinal
                    : null,
            }).ToArray(),
        },
        StringComparer.Ordinal
    );

    private static object? FormattingNumbering(
        WordResolvedNumberingLevel? numbering,
        bool includeSource
    ) => numbering is null
        ? null
        : new
        {
            number_id = numbering.NumberId,
            requested_abstract_number_id = numbering.RequestedAbstractNumberId,
            effective_abstract_number_id = numbering.EffectiveAbstractNumberId,
            level_index = numbering.LevelIndex,
            number_format = BoundForResponse(numbering.Level.NumberFormat, 128),
            custom_number_format = BoundForResponse(
                numbering.Level.CustomNumberFormat,
                256
            ),
            level_text = BoundForResponse(numbering.Level.LevelText, 512),
            suffix = BoundForResponse(numbering.Level.Suffix, 64),
            effective_start = numbering.EffectiveStart,
            level_source = ToSnakeCase(numbering.LevelSourceKind.ToString()),
            start_source = ToSnakeCase(numbering.StartSourceKind.ToString()),
            abstract_number_chain = numbering.AbstractNumberChain.Count > 1
                ? numbering.AbstractNumberChain.ToArray()
                : null,
            numbering_style_chain = numbering.NumberingStyleChain.Count == 0
                ? null
                : numbering.NumberingStyleChain
                    .Select(value => BoundForResponse(value, 253))
                    .ToArray(),
            source_element_ordinal = includeSource
                ? numbering.SourceElementOrdinal
                : (int?)null,
            start_source_element_ordinal = includeSource
                ? numbering.StartSourceElementOrdinal
                : null,
        };
}
