using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public sealed partial class WordSemanticTransactionPlanner
{
    private StyleDefinitionDraft BuildStyleRenameDraft(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        IReadOnlyList<IndexedStyleDefinitionCommand> indexedCommands,
        CancellationToken cancellationToken
    )
    {
        var commands = indexedCommands.Select(item =>
            item.Command as WordStyleRenameCommand
            ?? throw new WordSemanticEditException(
                "The style-rename stage contains another command type."
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
                "Styles cannot be renamed because the package has no styles part."
            );
        }
        if (graph.StylesWithEffectsPartUri is not null)
        {
            throw new WordSemanticEditException(
                "Style rename requires mirrored stylesWithEffects support."
            );
        }
        if (graph.Issues.Count != 0)
        {
            throw new WordSemanticEditException(
                "Style rename requires a style graph with no existing issues."
            );
        }

        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        var newNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var definitions = new Dictionary<string, WordStyleDefinition>(
            StringComparer.Ordinal
        );
        foreach (var command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateStyleId(command.StyleId, "style_id");
            ValidateStyleId(command.Name, "name");
            if (!sourceIds.Add(command.StyleId))
            {
                throw new WordSemanticEditException(
                    $"Style '{command.StyleId}' is renamed more than once."
                );
            }
            if (!newNames.Add(command.Name))
            {
                throw new WordSemanticEditException(
                    $"Style name '{command.Name}' is assigned more than once."
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
            if (string.Equals(definition.Name, command.Name, StringComparison.Ordinal))
            {
                throw new WordSemanticEditException(
                    $"Style '{command.StyleId}' already has the requested primary name."
                );
            }
            ValidateStyleNameCollision(graph, definition, command.Name);
            definitions.Add(command.StyleId, definition);
        }

        ValidateStyleNameChangeEnvironment(
            package,
            semanticDocument,
            graph,
            commands,
            definitions,
            cancellationToken
        );

        var modeledPartUris = new HashSet<string>(
            semanticDocument.ProjectedPartUris,
            StringComparer.Ordinal
        )
        {
            graph.StylesPartUri,
        };
        var numbering = new WordNumberingGraphBuilder().Build(
            package,
            semanticDocument,
            graph,
            cancellationToken
        );
        if (numbering.NumberingPartUri is not null)
        {
            modeledPartUris.Add(numbering.NumberingPartUri);
        }
        ValidateNoUnmodeledStyleConsumers(
            package,
            modeledPartUris,
            new HashSet<string>(StringComparer.Ordinal),
            cancellationToken
        );
        ValidateNoModeledAltChunks(package, modeledPartUris, cancellationToken);

        if (!package.Parts.TryGetValue(graph.StylesPartUri, out var part))
        {
            throw new WordSemanticPreconditionException(
                $"Styles part '{graph.StylesPartUri}' no longer exists."
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
                "The styles part cannot be edited losslessly.",
                exception
            );
        }

        var patches = new List<XmlSourcePatch>();
        var byteDeltas = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var definition = definitions[command.StyleId];
            var styleElement = source.GetElement(definition.SourceElementOrdinal);
            var names = source.Elements.Where(element =>
                element.ParentOrdinal == styleElement.Ordinal
                && IsWordNamespace(element.NamespaceUri)
                && element.LocalName == "name"
            ).ToArray();
            if (names.Length > 1)
            {
                throw new WordSemanticEditException(
                    $"Style '{command.StyleId}' has duplicate primary-name elements."
                );
            }

            IReadOnlyList<XmlSourcePatch> namePatches;
            if (names.Length == 1)
            {
                namePatches = source.CreateElementAttributeValuePatches(
                    names[0].Ordinal,
                    names[0].NamespaceUri,
                    "val",
                    command.Name,
                    definition.Name
                );
            }
            else
            {
                XNamespace word = styleElement.NamespaceUri;
                var fragment = new XElement(
                    word + "name",
                    new XAttribute(XNamespace.Xmlns + "w", styleElement.NamespaceUri),
                    new XAttribute(word + "val", command.Name)
                ).ToString(SaveOptions.DisableFormatting);
                namePatches =
                [
                    source.CreateElementContentInsertionPatch(
                        styleElement.Ordinal,
                        fragment,
                        XmlContentInsertionPosition.Prepend
                    ),
                ];
            }
            patches.AddRange(namePatches);
            byteDeltas.Add(
                command.StyleId,
                namePatches.Sum(patch => patch.Replacement.Length - patch.ByteLength)
            );
        }

        byte[] changed;
        try
        {
            changed = source.ApplyPatches(
                patches,
                part.Entry.Sha256,
                cancellationToken
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordSemanticEditException(
                "Style-name edits do not form safe XML.",
                exception
            );
        }
        var payload = new WordPackagePartPayload(
            part.Uri,
            part.Entry.Name,
            part.Entry.Content.ToArray(),
            changed
        );
        var operations = indexedCommands.Select(indexed =>
        {
            var command = (WordStyleRenameCommand)indexed.Command;
            var definition = definitions[command.StyleId];
            return new WordStyleDefinitionOperationPlan(
                indexed.Index,
                "rename_style",
                command.StyleId,
                command.StyleId,
                definition.Type,
                part.Uri,
                definition.SourceElementOrdinal,
                byteDeltas[command.StyleId],
                true
            );
        }).ToArray();
        return new StyleDefinitionDraft([payload], operations);
    }

    private static void ValidateStyleNameCollision(
        WordStyleGraph graph,
        WordStyleDefinition source,
        string newName
    )
    {
        foreach (var style in graph.Styles)
        {
            if (
                string.Equals(style.StyleId, newName, StringComparison.OrdinalIgnoreCase)
                || style.Aliases.Any(alias =>
                    string.Equals(alias, newName, StringComparison.OrdinalIgnoreCase)
                )
                || (!string.Equals(style.StyleId, source.StyleId, StringComparison.Ordinal)
                    && string.Equals(
                        style.Name,
                        newName,
                        StringComparison.OrdinalIgnoreCase
                    ))
            )
            {
                throw new WordSemanticEditException(
                    $"Style name '{newName}' collides with an existing style ID, name, or alias."
                );
            }
        }
    }

    private static void ValidateStyleNameChangeEnvironment(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph graph,
        IReadOnlyList<WordStyleRenameCommand> commands,
        IReadOnlyDictionary<string, WordStyleDefinition> definitions,
        CancellationToken cancellationToken
    )
    {
        ValidateStyleMutationContainerEnvironment(
            package,
            semanticDocument,
            cancellationToken
        );

        var riskyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in commands)
        {
            var definition = definitions[command.StyleId];
            if (
                string.Equals(
                    definition.Name,
                    command.Name,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(definition.Name))
            {
                riskyNames.Add(definition.Name);
            }
            riskyNames.Add(command.Name);
        }
        if (
            graph.LatentStyles?.Exceptions.Any(exception =>
                riskyNames.Contains(exception.Name)
            ) == true
        )
        {
            throw new WordSemanticEditException(
                "Style rename is blocked because latent-style behavior addresses an old or new primary name."
            );
        }

        var references = new WordReferenceGraphBuilder().Build(
            package,
            semanticDocument,
            cancellationToken
        );
        foreach (var field in references.Fields.Where(field =>
            string.Equals(field.FieldType, "STYLEREF", StringComparison.OrdinalIgnoreCase)
        ))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!field.InstructionParseComplete || field.HasDynamicInstruction)
            {
                throw new WordSemanticEditException(
                    "Style rename is blocked by a dynamic or incompletely parsed STYLEREF field."
                );
            }
            var targets = references.Edges.Where(edge =>
                string.Equals(edge.SourceFieldId, field.Id, StringComparison.Ordinal)
                && edge.TargetKind == WordReferenceTargetKind.Style
            ).Select(edge => edge.TargetKey).ToArray();
            if (targets.Length != 1)
            {
                throw new WordSemanticEditException(
                    "Style rename is blocked by an ambiguous STYLEREF field."
                );
            }
            if (riskyNames.Contains(targets[0]))
            {
                throw new WordSemanticEditException(
                    "Style rename is blocked because STYLEREF addresses an old or new primary name."
                );
            }
        }
    }

    private void ValidateNoModeledAltChunks(
        OpcPackageSnapshot package,
        IReadOnlySet<string> modeledPartUris,
        CancellationToken cancellationToken
    )
    {
        foreach (var partUri in modeledPartUris)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!package.Parts.TryGetValue(partUri, out var part) || !LooksLikeXmlPart(part))
            {
                continue;
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
                    $"Modeled XML part '{partUri}' cannot be inspected for altChunk content.",
                    exception
                );
            }
            if (source.Elements.Any(element =>
                IsWordNamespace(element.NamespaceUri) && element.LocalName == "altChunk"
            ))
            {
                throw new WordSemanticEditException(
                    "Style rename is blocked because a projected story contains altChunk content."
                );
            }
        }
    }

    private static void ValidateRenamedStyles(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        IReadOnlyList<IndexedStyleDefinitionCommand> indexedCommands,
        CancellationToken cancellationToken
    )
    {
        var graph = new WordStyleGraphBuilder().Build(
            package,
            semanticDocument,
            cancellationToken
        );
        if (graph.Issues.Count != 0)
        {
            throw new WordSemanticEditException(
                "The style-renamed package contains style-graph issues."
            );
        }
        foreach (var indexed in indexedCommands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var command = (WordStyleRenameCommand)indexed.Command;
            if (
                !graph.TryGetStyle(command.StyleId, out var definition)
                || definition is null
                || !string.Equals(
                    definition.Name,
                    command.Name,
                    StringComparison.Ordinal
                )
            )
            {
                throw new WordSemanticEditException(
                    $"Style '{command.StyleId}' did not retain its ID and exact new primary name."
                );
            }
        }
    }
}
