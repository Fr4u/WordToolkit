using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public sealed partial class WordSemanticTransactionPlanner
{
    private StyleDefinitionDraft BuildUnusedStyleDeletionDraft(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        IReadOnlyList<IndexedStyleDefinitionCommand> indexedCommands,
        CancellationToken cancellationToken
    )
    {
        var commands = indexedCommands.Select(item =>
            item.Command as WordStyleDeleteUnusedCommand
            ?? throw new WordSemanticEditException(
                "The unused-style deletion stage contains another command type."
            )
        ).ToArray();
        var graph = new WordStyleGraphBuilder().Build(
            package,
            semanticDocument,
            cancellationToken
        );
        if (!graph.HasStylesPart || graph.StylesPartUri is null)
        {
            throw new WordSemanticEditException(
                "Unused styles cannot be deleted because the package has no styles part."
            );
        }
        if (graph.StylesWithEffectsPartUri is not null)
        {
            throw new WordSemanticEditException(
                "Unused-style deletion requires mirrored stylesWithEffects support."
            );
        }
        if (graph.Issues.Count != 0)
        {
            throw new WordSemanticEditException(
                "Unused-style deletion requires a style graph with no existing issues."
            );
        }
        var numbering = new WordNumberingGraphBuilder().Build(
            package,
            semanticDocument,
            graph,
            cancellationToken
        );
        if (numbering.Issues.Count != 0)
        {
            throw new WordSemanticEditException(
                "Unused-style deletion requires a numbering graph with no existing issues."
            );
        }

        var styleIds = new HashSet<string>(StringComparer.Ordinal);
        var definitions = new Dictionary<string, WordStyleDefinition>(
            StringComparer.Ordinal
        );
        foreach (var command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateStyleId(command.StyleId, "style_id");
            if (!styleIds.Add(command.StyleId))
            {
                throw new WordSemanticEditException(
                    $"Style '{command.StyleId}' is deleted more than once."
                );
            }
            if (
                !graph.TryGetStyle(command.StyleId, out var definition)
                || definition is null
            )
            {
                throw new WordSemanticEditException(
                    $"Style '{command.StyleId}' does not exist."
                );
            }
            if (definition.IsDefault || !definition.IsCustom)
            {
                throw new WordSemanticEditException(
                    $"Style '{command.StyleId}' must be custom and non-default."
                );
            }
            definitions.Add(command.StyleId, definition);
        }
        ValidateStyleRemovalEnvironment(
            package,
            semanticDocument,
            graph,
            styleIds,
            cancellationToken
        );

        var partUris = new HashSet<string>(
            semanticDocument.ProjectedPartUris,
            StringComparer.Ordinal
        )
        {
            graph.StylesPartUri,
        };
        if (numbering.NumberingPartUri is not null)
        {
            partUris.Add(numbering.NumberingPartUri);
        }
        ValidateNoUnmodeledStyleConsumers(
            package,
            partUris,
            styleIds,
            cancellationToken
        );

        var sources = new Dictionary<string, LosslessXmlDocument>(StringComparer.Ordinal);
        var sourceParts = new Dictionary<string, OpcPart>(StringComparer.Ordinal);
        var patches = new Dictionary<string, List<XmlSourcePatch>>(StringComparer.Ordinal);
        foreach (var partUri in partUris.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!package.Parts.TryGetValue(partUri, out var part))
            {
                throw new WordSemanticPreconditionException(
                    $"Style-consumer part '{partUri}' no longer exists."
                );
            }
            LosslessXmlDocument source;
            try
            {
                source = LosslessXmlDocument.Parse(
                    part.Entry.Content,
                    _xmlOptions,
                    cancellationToken
                );
            }
            catch (LosslessXmlException exception)
            {
                throw new WordSemanticEditException(
                    $"Style-consumer part '{partUri}' cannot be inspected losslessly.",
                    exception
                );
            }
            if (source.Elements.Any(element =>
                IsWordNamespace(element.NamespaceUri) && element.LocalName == "altChunk"
            ))
            {
                throw new WordSemanticEditException(
                    "Unused-style deletion is blocked because a projected story contains altChunk content."
                );
            }
            sources.Add(partUri, source);
            sourceParts.Add(partUri, part);
            patches.Add(partUri, []);
        }

        var stylesSource = sources[graph.StylesPartUri];
        var removedStyleOrdinals = definitions.Values
            .Select(style => style.SourceElementOrdinal)
            .ToHashSet();
        foreach (var (partUri, source) in sources)
        {
            foreach (var element in source.Elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    !IsWordNamespace(element.NamespaceUri)
                    || !StyleReferenceElementNames.Contains(element.LocalName)
                    || (
                        string.Equals(partUri, graph.StylesPartUri, StringComparison.Ordinal)
                        && IsInsideRemovedStyle(
                            source,
                            element,
                            removedStyleOrdinals
                        )
                    )
                )
                {
                    continue;
                }
                var value = element.Attributes.SingleOrDefault(attribute =>
                    IsWordNamespace(attribute.NamespaceUri)
                    && attribute.LocalName == "val"
                )?.Value;
                if (value is not null && styleIds.Contains(value))
                {
                    throw new WordSemanticEditException(
                        $"Style '{value}' is still referenced by w:{element.LocalName} in '{partUri}'."
                    );
                }
            }
        }

        var byteDeltas = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var command in commands)
        {
            var sourceElement = stylesSource.GetElement(
                definitions[command.StyleId].SourceElementOrdinal
            );
            var removal = stylesSource.CreateElementRemovalPatch(sourceElement.Ordinal);
            patches[graph.StylesPartUri].Add(removal);
            byteDeltas.Add(command.StyleId, -removal.ByteLength);
        }
        var payloads = BuildPartPayloads(
            sources,
            sourceParts,
            patches,
            cancellationToken
        );
        var operations = indexedCommands.Select(indexed =>
        {
            var command = (WordStyleDeleteUnusedCommand)indexed.Command;
            var definition = definitions[command.StyleId];
            return new WordStyleDefinitionOperationPlan(
                indexed.Index,
                "delete_unused_style",
                command.StyleId,
                null,
                definition.Type,
                graph.StylesPartUri,
                definition.SourceElementOrdinal,
                byteDeltas[command.StyleId],
                true
            );
        }).ToArray();
        return new StyleDefinitionDraft(payloads.Values.ToArray(), operations);
    }

    private static void ValidateDeletedStyles(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        IReadOnlyList<IndexedStyleDefinitionCommand> indexedCommands,
        CancellationToken cancellationToken
    )
    {
        var deletedIds = indexedCommands.Select(indexed =>
            ((WordStyleDeleteUnusedCommand)indexed.Command).StyleId
        ).ToHashSet(StringComparer.Ordinal);
        var graph = new WordStyleGraphBuilder().Build(
            package,
            semanticDocument,
            cancellationToken
        );
        if (graph.Issues.Count != 0)
        {
            throw new WordSemanticEditException(
                "The style-deleted package contains style-graph issues."
            );
        }
        foreach (var styleId in deletedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (graph.TryGetStyle(styleId, out _))
            {
                throw new WordSemanticEditException(
                    $"Style '{styleId}' survived unused-style deletion."
                );
            }
        }
        if (semanticDocument.Nodes.Any(node =>
            node.Properties.TryGetValue("style_id", out var styleId)
            && deletedIds.Contains(styleId)
        ))
        {
            throw new WordSemanticEditException(
                "Semantic content still refers to a deleted style."
            );
        }
        var numbering = new WordNumberingGraphBuilder().Build(
            package,
            semanticDocument,
            graph,
            cancellationToken
        );
        if (numbering.Issues.Count != 0)
        {
            throw new WordSemanticEditException(
                "The style-deleted package contains numbering-graph issues."
            );
        }
    }
}
