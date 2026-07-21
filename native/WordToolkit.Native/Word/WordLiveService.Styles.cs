using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> InspectPackageStylesAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveInspectablePackagePath(arguments);
        var offset = arguments.NullableInt64("offset") ?? 0;
        var maximum = arguments.NullableInt64("max_styles") ?? 30;
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
                "max_styles must be between 1 and 100"
            );
        }

        var detail = arguments.String("detail", "metadata");
        if (detail is not "metadata" and not "declared" and not "inheritance")
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "detail must be metadata, declared, or inheritance"
            );
        }

        var styleType = arguments.String("style_type", "any");
        if (
            styleType is not "any"
                and not "paragraph"
                and not "character"
                and not "table"
                and not "numbering"
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "style_type must be any, paragraph, character, table, or numbering"
            );
        }

        var includeDocumentDefaults = arguments.Boolean(
            "include_document_defaults",
            false
        );
        var includeLatentStyles = arguments.Boolean(
            "include_latent_styles",
            false
        );
        var includeIssues = arguments.Boolean("include_issues", true);
        try
        {
            var package = new OpcPackageReader().Read(path, cancellationToken);
            var semantic = new WordSemanticProjector().Project(
                package,
                cancellationToken
            );
            var graph = new WordStyleGraphBuilder().Build(
                package,
                semantic,
                cancellationToken
            );
            var matching = graph.Styles
                .Where(style => styleType == "any" || ToSnakeCase(style.Type.ToString()) == styleType)
                .OrderBy(style => style.SourceElementOrdinal)
                .ToArray();
            var page = matching
                .Skip((int)offset)
                .Take((int)maximum)
                .Select(style => new
                {
                    style_id = BoundForResponse(style.StyleId, 253),
                    type = ToSnakeCase(style.Type.ToString()),
                    name = BoundForResponse(style.Name, 512),
                    aliases = style.Aliases.Count == 0
                        ? null
                        : style.Aliases.Take(20)
                            .Select(alias => BoundForResponse(alias, 256))
                            .ToArray(),
                    aliases_truncated = style.Aliases.Count > 20
                        ? true
                        : (bool?)null,
                    based_on_style_id = BoundForResponse(style.BasedOnStyleId, 253),
                    next_style_id = BoundForResponse(style.NextStyleId, 253),
                    linked_style_id = BoundForResponse(style.LinkedStyleId, 253),
                    default_style = style.IsDefault,
                    custom_style = style.IsCustom,
                    quick_format = style.QuickFormat,
                    semi_hidden = style.SemiHidden,
                    unhide_when_used = style.UnhideWhenUsed,
                    locked = style.Locked,
                    ui_priority = style.UiPriority,
                    inheritance_resolvable = style.InheritanceResolvable,
                    inheritance_failure = style.InheritanceResolvable
                        ? null
                        : BoundForResponse(style.InheritanceFailure, 512),
                    inheritance_chain_style_ids = detail == "inheritance"
                        ? style.InheritanceChainStyleIds
                            .Take(80)
                            .Select(id => BoundForResponse(id, 253))
                            .ToArray()
                        : null,
                    inheritance_chain_truncated = detail == "inheritance"
                        && style.InheritanceChainStyleIds.Count > 80
                            ? true
                            : (bool?)null,
                    declared_properties = detail is "declared" or "inheritance"
                        ? FormattingBlocks(style)
                        : null,
                    source_element_ordinal = detail == "inheritance"
                        ? style.SourceElementOrdinal
                        : (int?)null,
                })
                .ToArray();
            var consumed = (long)offset + page.Length;
            var returnedIssues = includeIssues
                ? graph.Issues.Take(40).Select(issue => new
                {
                    code = BoundForResponse(issue.Code, 128),
                    severity = ToSnakeCase(issue.Severity.ToString()),
                    style_id = BoundForResponse(issue.StyleId, 253),
                    message = BoundForResponse(issue.Message, 512),
                }).ToArray()
                : null;
            var latent = includeLatentStyles && graph.LatentStyles is { } latentStyles
                ? new
                {
                    declared_count = latentStyles.DeclaredCount,
                    exception_count = latentStyles.Exceptions.Count,
                    default_locked = latentStyles.DefaultLocked,
                    default_semi_hidden = latentStyles.DefaultSemiHidden,
                    default_unhide_when_used = latentStyles.DefaultUnhideWhenUsed,
                    default_quick_format = latentStyles.DefaultQuickFormat,
                    default_ui_priority = latentStyles.DefaultUiPriority,
                    exceptions = latentStyles.Exceptions.Take(40).Select(item => new
                    {
                        name = BoundForResponse(item.Name, 512),
                        locked = item.Locked,
                        semi_hidden = item.SemiHidden,
                        unhide_when_used = item.UnhideWhenUsed,
                        quick_format = item.QuickFormat,
                        ui_priority = item.UiPriority,
                    }).ToArray(),
                    exceptions_truncated = latentStyles.Exceptions.Count > 40
                        ? true
                        : (bool?)null,
                }
                : null;
            return Task.FromResult<object>(new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = graph.PackageFingerprint,
                main_part_uri = graph.MainPartUri,
                styles_part_uri = graph.StylesPartUri,
                styles_with_effects_part_uri = graph.StylesWithEffectsPartUri,
                has_styles_part = graph.HasStylesPart,
                style_count = graph.Styles.Count,
                matched_style_count = matching.Length,
                style_type = styleType,
                offset,
                returned_style_count = page.Length,
                next_offset = consumed < matching.Length ? (int)consumed : (int?)null,
                default_style_ids = graph.DefaultStyleIds.ToDictionary(
                    pair => ToSnakeCase(pair.Key.ToString()),
                    pair => BoundForResponse(pair.Value, 253)!,
                    StringComparer.Ordinal
                ),
                document_defaults = includeDocumentDefaults
                    ? new
                    {
                        paragraph = FormattingBlock(graph.DefaultParagraphProperties),
                        run = FormattingBlock(graph.DefaultRunProperties),
                    }
                    : null,
                latent_styles = latent,
                issue_count = graph.Issues.Count,
                issues = returnedIssues,
                issues_truncated = returnedIssues is not null
                    && graph.Issues.Count > returnedIssues.Length
                        ? true
                        : (bool?)null,
                styles = page,
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            });
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

    private static object FormattingBlocks(WordStyleDefinition style) => new
    {
        paragraph = FormattingBlock(style.ParagraphProperties),
        run = FormattingBlock(style.RunProperties),
        table = FormattingBlock(style.TableProperties),
        table_cell = FormattingBlock(style.TableCellProperties),
    };

    private static object? FormattingBlock(WordStylePropertySet properties) =>
        properties.Values.Count == 0 && properties.UnmodeledElements.Count == 0
            ? null
            : new
            {
                values = BoundProperties(properties.Values, 160),
                fully_modeled = properties.IsFullyModeled,
                unmodeled_elements = properties.UnmodeledElements.Count == 0
                    ? null
                    : properties.UnmodeledElements.Take(40)
                        .Select(name => BoundForResponse(name, 256))
                        .ToArray(),
                unmodeled_elements_truncated = properties.UnmodeledElements.Count > 40
                    ? true
                    : (bool?)null,
            };
}
