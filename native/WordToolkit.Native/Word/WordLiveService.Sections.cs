using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> InspectPackageSectionsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveInspectablePackagePath(arguments);
        var offset = arguments.NullableInt64("offset") ?? 0;
        var maximum = arguments.NullableInt64("max_sections") ?? 20;
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
                "max_sections must be between 1 and 100"
            );
        }

        var bindingDetail = arguments.String("binding_detail", "effective");
        if (bindingDetail is not "none" and not "effective" and not "full")
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "binding_detail must be none, effective, or full"
            );
        }

        var includeProperties = arguments.Boolean("include_properties", false);
        var includeStoryPartUris = arguments.Boolean(
            "include_story_part_uris",
            false
        );
        try
        {
            var package = new OpcPackageReader().Read(path, cancellationToken);
            var semantic = new WordSemanticProjector().Project(
                package,
                cancellationToken
            );
            var graph = new WordSectionGraphBuilder().Build(
                package,
                semantic,
                cancellationToken
            );
            var page = graph.Sections
                .Skip((int)offset)
                .Take((int)maximum)
                .Select(section => new
                {
                    ordinal = section.Ordinal,
                    node_id = section.NodeId?.Value,
                    implicit_section = section.IsImplicit,
                    starts_after_paragraph_id = section.StartsAfterParagraphId?.Value,
                    ends_at_paragraph_id = section.EndsAtParagraphId?.Value,
                    break_type = BoundForResponse(section.BreakType, 80),
                    title_page = section.TitlePage,
                    properties = includeProperties
                        ? BoundProperties(section.Properties, 160)
                        : null,
                    bindings = bindingDetail == "none"
                        ? null
                        : section.Bindings.Select(binding => new
                        {
                            slot = ToSnakeCase(
                                binding.Kind + binding.Variant.ToString()
                            ),
                            kind = ToSnakeCase(binding.Kind.ToString()),
                            variant = ToSnakeCase(binding.Variant.ToString()),
                            enabled = binding.IsVariantEnabled,
                            origin = ToSnakeCase(binding.Origin.ToString()),
                            effective_part_uri = binding.EffectiveDisplayPartUri is null
                                ? null
                                : BoundForResponse(
                                    binding.EffectiveDisplayPartUri,
                                    512
                                ),
                            display_fallback_variant = binding.DisplayFallbackVariant is null
                                ? null
                                : ToSnakeCase(
                                    binding.DisplayFallbackVariant.Value.ToString()
                                ),
                            definition_section_ordinal = bindingDetail == "full"
                                ? binding.DefinitionSectionOrdinal
                                : null,
                            relationship_id = bindingDetail == "full"
                                ? BoundForResponse(binding.RelationshipId, 256)
                                : null,
                            defined_part_uri = bindingDetail == "full"
                                ? BoundForResponse(binding.PartUri, 512)
                                : null,
                        }).ToArray(),
                })
                .ToArray();
            var consumed = (long)offset + page.Length;
            var referencedPartUris = includeStoryPartUris
                ? graph.ReferencedStoryPartUris
                    .Take(80)
                    .Select(uri => BoundForResponse(uri, 512))
                    .ToArray()
                : null;
            var unboundPartUris = includeStoryPartUris
                ? graph.UnboundStoryPartUris
                    .Take(80)
                    .Select(uri => BoundForResponse(uri, 512))
                    .ToArray()
                : null;
            return Task.FromResult<object>(new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = graph.PackageFingerprint,
                main_part_uri = graph.MainPartUri,
                even_and_odd_headers = graph.EvenAndOddHeaders,
                section_count = graph.Sections.Count,
                offset,
                returned_section_count = page.Length,
                next_offset = consumed < graph.Sections.Count
                    ? (int)consumed
                    : (int?)null,
                referenced_story_part_count = graph.ReferencedStoryPartUris.Count,
                unbound_story_part_count = graph.UnboundStoryPartUris.Count,
                referenced_story_part_uris = referencedPartUris,
                referenced_story_parts_truncated = referencedPartUris is not null
                    && graph.ReferencedStoryPartUris.Count > referencedPartUris.Length,
                unbound_story_part_uris = unboundPartUris,
                unbound_story_parts_truncated = unboundPartUris is not null
                    && graph.UnboundStoryPartUris.Count > unboundPartUris.Length,
                sections = page,
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            });
        }
        catch (WordSectionLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "Section graph exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordSectionProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a Word section graph",
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
}
