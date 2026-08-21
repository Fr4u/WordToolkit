using System.Globalization;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public sealed partial class WordSemanticTransactionPlanner
{
    private static readonly IReadOnlySet<string> StyleReferenceElementNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "basedOn",
            "link",
            "next",
            "numStyleLink",
            "pStyle",
            "rStyle",
            "style",
            "styleLink",
            "tblStyle",
        };

    private StyleDefinitionDraft BuildStyleConsolidationDraft(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        IReadOnlyList<IndexedStyleDefinitionCommand> indexedCommands,
        CancellationToken cancellationToken
    )
    {
        var commands = indexedCommands.Select(item =>
            item.Command as WordStyleConsolidateCommand
            ?? throw new WordSemanticEditException(
                "The style-consolidation stage contains another command type."
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
                "Styles cannot be consolidated because the package has no styles part."
            );
        }
        if (graph.StylesWithEffectsPartUri is not null)
        {
            throw new WordSemanticEditException(
                "Style consolidation requires mirrored stylesWithEffects support."
            );
        }
        if (graph.Issues.Count != 0)
        {
            throw new WordSemanticEditException(
                "Style consolidation requires a style graph with no existing issues."
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
                "Style consolidation requires a numbering graph with no existing issues."
            );
        }
        ValidateStyleRemovalEnvironment(
            package,
            semanticDocument,
            graph,
            commands.Select(command => command.SourceStyleId),
            cancellationToken
        );

        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var sourceDefinitions = new Dictionary<string, WordStyleDefinition>(
            StringComparer.Ordinal
        );
        var targetDefinitions = new Dictionary<string, WordStyleDefinition>(
            StringComparer.Ordinal
        );
        foreach (var command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateStyleId(command.SourceStyleId, "source_style_id");
            ValidateStyleId(command.TargetStyleId, "target_style_id");
            if (string.Equals(
                command.SourceStyleId,
                command.TargetStyleId,
                StringComparison.Ordinal
            ))
            {
                throw new WordSemanticEditException(
                    $"Style '{command.SourceStyleId}' cannot be consolidated into itself."
                );
            }
            if (!sourceIds.Add(command.SourceStyleId))
            {
                throw new WordSemanticEditException(
                    $"Style '{command.SourceStyleId}' is consolidated more than once."
                );
            }
            map.Add(command.SourceStyleId, command.TargetStyleId);
        }
        foreach (var command in commands)
        {
            if (sourceIds.Contains(command.TargetStyleId))
            {
                throw new WordSemanticEditException(
                    "Chained or circular style consolidations are not allowed in one plan."
                );
            }
            if (
                !graph.TryGetStyle(command.SourceStyleId, out var sourceDefinition)
                || sourceDefinition is null
            )
            {
                throw new WordSemanticEditException(
                    $"Source style '{command.SourceStyleId}' does not exist."
                );
            }
            if (
                !graph.TryGetStyle(command.TargetStyleId, out var targetDefinition)
                || targetDefinition is null
            )
            {
                throw new WordSemanticEditException(
                    $"Target style '{command.TargetStyleId}' does not exist."
                );
            }
            if (sourceDefinition.IsDefault || !sourceDefinition.IsCustom)
            {
                throw new WordSemanticEditException(
                    $"Source style '{command.SourceStyleId}' must be custom and non-default."
                );
            }
            if (sourceDefinition.Type != targetDefinition.Type)
            {
                throw new WordSemanticEditException(
                    $"Style '{command.SourceStyleId}' is {sourceDefinition.Type}, not {targetDefinition.Type}."
                );
            }
            sourceDefinitions.Add(command.SourceStyleId, sourceDefinition);
            targetDefinitions.Add(command.SourceStyleId, targetDefinition);
        }

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
            sourceIds,
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
                    $"Style-consumer part '{partUri}' cannot be edited losslessly.",
                    exception
                );
            }
            if (source.Elements.Any(element =>
                IsWordNamespace(element.NamespaceUri) && element.LocalName == "altChunk"
            ))
            {
                throw new WordSemanticEditException(
                    "Style consolidation is blocked because a projected story contains altChunk content."
                );
            }
            sources.Add(partUri, source);
            sourceParts.Add(partUri, part);
            patches.Add(partUri, []);
        }

        var stylesSource = sources[graph.StylesPartUri];
        foreach (var command in commands)
        {
            var sourceElement = stylesSource.GetElement(
                sourceDefinitions[command.SourceStyleId].SourceElementOrdinal
            );
            var targetElement = stylesSource.GetElement(
                targetDefinitions[command.SourceStyleId].SourceElementOrdinal
            );
            if (
                !StylesAreExactlyConsolidatable(
                    stylesSource,
                    sourceElement,
                    targetElement,
                    command.SourceStyleId,
                    command.TargetStyleId,
                    map
                )
            )
            {
                throw new WordSemanticEditException(
                    $"Styles '{command.SourceStyleId}' and '{command.TargetStyleId}' are not exactly equivalent after identity normalization."
                );
            }
        }

        var removedStyleOrdinals = sourceDefinitions.Values
            .Select(style => style.SourceElementOrdinal)
            .ToHashSet();
        var referenceCounts = sourceIds.ToDictionary(
            styleId => styleId,
            _ => 0,
            StringComparer.Ordinal
        );
        var byteDeltas = sourceIds.ToDictionary(
            styleId => styleId,
            _ => 0,
            StringComparer.Ordinal
        );
        var totalReferenceUpdates = 0;
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
                );
                if (value is null || !map.TryGetValue(value.Value, out var targetStyleId))
                {
                    continue;
                }
                var sourceDefinition = sourceDefinitions[value.Value];
                var expectedType = ExpectedStyleReferenceType(
                    partUri,
                    source,
                    element,
                    graph,
                    numbering.NumberingPartUri
                );
                if (expectedType != sourceDefinition.Type)
                {
                    throw new WordSemanticEditException(
                        $"Style reference w:{element.LocalName} in '{partUri}' uses '{value.Value}' as {expectedType}, but the style is {sourceDefinition.Type}."
                    );
                }
                var elementPatches = source.CreateElementAttributeValuePatches(
                    element.Ordinal,
                    element.NamespaceUri,
                    "val",
                    targetStyleId,
                    expectedValue: value.Value,
                    preferredPrefix: string.IsNullOrEmpty(element.Prefix)
                        ? "wtk"
                        : element.Prefix
                );
                patches[partUri].AddRange(elementPatches);
                referenceCounts[value.Value]++;
                byteDeltas[value.Value] += elementPatches.Sum(patch =>
                    patch.Replacement.Length - patch.ByteLength
                );
                totalReferenceUpdates++;
                if (totalReferenceUpdates > _options.MaxStyleReferenceUpdates)
                {
                    throw new WordSemanticTransactionLimitException(
                        $"Style consolidation exceeds {_options.MaxStyleReferenceUpdates} reference updates."
                    );
                }
            }
        }

        foreach (var command in commands)
        {
            var sourceElement = stylesSource.GetElement(
                sourceDefinitions[command.SourceStyleId].SourceElementOrdinal
            );
            var removal = stylesSource.CreateElementRemovalPatch(sourceElement.Ordinal);
            patches[graph.StylesPartUri].Add(removal);
            byteDeltas[command.SourceStyleId] -= removal.ByteLength;
        }

        var payloads = BuildPartPayloads(
            sources,
            sourceParts,
            patches,
            cancellationToken
        );
        var operations = indexedCommands.Select(indexed =>
        {
            var command = (WordStyleConsolidateCommand)indexed.Command;
            var source = sourceDefinitions[command.SourceStyleId];
            return new WordStyleDefinitionOperationPlan(
                indexed.Index,
                "consolidate_style",
                command.TargetStyleId,
                command.SourceStyleId,
                source.Type,
                graph.StylesPartUri,
                source.SourceElementOrdinal,
                byteDeltas[command.SourceStyleId],
                true,
                referenceCounts[command.SourceStyleId]
            );
        }).ToArray();
        return new StyleDefinitionDraft(payloads.Values.ToArray(), operations);
    }

    private static void ValidateStyleRemovalEnvironment(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph graph,
        IEnumerable<string> sourceStyleIds,
        CancellationToken cancellationToken
    )
    {
        ValidateStyleMutationContainerEnvironment(
            package,
            semanticDocument,
            cancellationToken
        );

        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceStyleId in sourceStyleIds)
        {
            if (!graph.TryGetStyle(sourceStyleId, out var source) || source is null)
            {
                continue;
            }
            identities.Add(source.StyleId);
            if (!string.IsNullOrWhiteSpace(source.Name))
            {
                identities.Add(source.Name);
            }
            foreach (var alias in source.Aliases)
            {
                identities.Add(alias);
            }
        }
        if (
            graph.LatentStyles?.Exceptions.Any(exception =>
                identities.Contains(exception.Name)
            ) == true
        )
        {
            throw new WordSemanticEditException(
                "Style removal is blocked because latent-style behavior addresses a source style name or alias."
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
            if (!field.InstructionParseComplete || field.HasDynamicInstruction)
            {
                throw new WordSemanticEditException(
                    "Style removal is blocked by a dynamic or incompletely parsed STYLEREF field."
                );
            }
            var targets = references.Edges.Where(edge =>
                string.Equals(edge.SourceFieldId, field.Id, StringComparison.Ordinal)
                && edge.TargetKind == WordReferenceTargetKind.Style
            ).Select(edge => edge.TargetKey).ToArray();
            if (targets.Length != 1)
            {
                throw new WordSemanticEditException(
                    "Style removal is blocked by an ambiguous STYLEREF field."
                );
            }
            if (identities.Contains(targets[0]))
            {
                throw new WordSemanticEditException(
                    "Style removal is blocked because STYLEREF addresses a source style by ID, name, or alias."
                );
            }
        }
    }

    private static void ValidateStyleMutationContainerEnvironment(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        CancellationToken cancellationToken
    )
    {
        if (package.Parts.Values.Any(part =>
            part.Uri.EndsWith("/vbaProject.bin", StringComparison.OrdinalIgnoreCase)
            || (part.ContentType?.Contains(
                "macroEnabled",
                StringComparison.OrdinalIgnoreCase
            ) ?? false)
            || (part.ContentType?.Contains(
                "vbaProject",
                StringComparison.OrdinalIgnoreCase
            ) ?? false)
        ))
        {
            throw new WordSemanticEditException(
                "Style mutation is blocked for macro-enabled packages because VBA style consumers are not modeled."
            );
        }

        var settings = new WordSettingsGraphBuilder().Build(
            package,
            semanticDocument,
            cancellationToken
        );
        if (
            settings.TryGetBoolean("linkStyles", out var linkStyles)
            && linkStyles is not null
            && linkStyles.Value
        )
        {
            throw new WordSemanticEditException(
                "Style mutation is blocked while automatic linked-template style updates are enabled."
            );
        }
    }

    private void ValidateNoUnmodeledStyleConsumers(
        OpcPackageSnapshot package,
        IReadOnlySet<string> modeledPartUris,
        IReadOnlySet<string> sourceStyleIds,
        CancellationToken cancellationToken
    )
    {
        foreach (var part in package.Parts.Values.Where(part =>
            !modeledPartUris.Contains(part.Uri) && LooksLikeXmlPart(part)
        ))
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                    $"Unmodeled XML part '{part.Uri}' cannot be cleared as a style consumer.",
                    exception
                );
            }
            foreach (var element in source.Elements)
            {
                if (
                    !IsWordNamespace(element.NamespaceUri)
                    || !StyleReferenceElementNames.Contains(element.LocalName)
                )
                {
                    continue;
                }
                var value = element.Attributes.SingleOrDefault(attribute =>
                    IsWordNamespace(attribute.NamespaceUri)
                    && attribute.LocalName == "val"
                )?.Value;
                if (value is not null && sourceStyleIds.Contains(value))
                {
                    throw new WordSemanticEditException(
                        $"Unmodeled XML part '{part.Uri}' contains a source-style reference."
                    );
                }
            }
            if (source.ParsedDocument.Descendants().Any(IsStyleRefFieldElement))
            {
                throw new WordSemanticEditException(
                    $"Unmodeled XML part '{part.Uri}' contains a STYLEREF instruction."
                );
            }
        }
    }

    private static bool LooksLikeXmlPart(OpcPart part) =>
        part.Entry.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
        || (part.ContentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool IsStyleRefFieldElement(XElement element)
    {
        if (!IsWordNamespace(element.Name.NamespaceName))
        {
            return false;
        }
        if (element.Name.LocalName == "instrText")
        {
            return element.Value.Contains("STYLEREF", StringComparison.OrdinalIgnoreCase);
        }
        if (element.Name.LocalName != "fldSimple")
        {
            return false;
        }
        return element.Attributes().Any(attribute =>
            IsWordNamespace(attribute.Name.NamespaceName)
            && attribute.Name.LocalName == "instr"
            && attribute.Value.Contains("STYLEREF", StringComparison.OrdinalIgnoreCase)
        );
    }

    private static void ValidateStyleId(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 253)
        {
            throw new ArgumentException(
                $"{fieldName} must contain between 1 and 253 characters."
            );
        }
        try
        {
            System.Xml.XmlConvert.VerifyXmlChars(value);
        }
        catch (System.Xml.XmlException exception)
        {
            throw new WordSemanticEditException(
                $"{fieldName} contains a character forbidden by XML 1.0.",
                exception
            );
        }
    }

    private static bool StylesAreExactlyConsolidatable(
        LosslessXmlDocument source,
        XmlSourceElement sourceElement,
        XmlSourceElement targetElement,
        string sourceStyleId,
        string targetStyleId,
        IReadOnlyDictionary<string, string> consolidationMap
    )
    {
        var sourceXml = SourceElement(source, sourceElement.Ordinal);
        var targetXml = SourceElement(source, targetElement.Ordinal);
        return string.Equals(
            CanonicalStyle(sourceXml, sourceStyleId, consolidationMap),
            CanonicalStyle(targetXml, targetStyleId, consolidationMap),
            StringComparison.Ordinal
        );
    }

    private static XElement SourceElement(
        LosslessXmlDocument source,
        int ordinal
    ) => source.ParsedDocument.Root!.DescendantsAndSelf().Single(element =>
        source.GetElementOrdinal(element) == ordinal
    );

    private static string CanonicalStyle(
        XElement style,
        string ownStyleId,
        IReadOnlyDictionary<string, string> consolidationMap
    )
    {
        var builder = new StringBuilder();
        AppendCanonicalElement(
            builder,
            style,
            ownStyleId,
            consolidationMap,
            isStyleRoot: true
        );
        return builder.ToString();
    }

    private static void AppendCanonicalElement(
        StringBuilder builder,
        XElement element,
        string ownStyleId,
        IReadOnlyDictionary<string, string> consolidationMap,
        bool isStyleRoot
    )
    {
        AppendCanonicalValue(builder, element.Name.NamespaceName);
        AppendCanonicalValue(builder, element.Name.LocalName);
        foreach (var attribute in element.Attributes()
            .Where(attribute =>
                !attribute.IsNamespaceDeclaration
                && !(isStyleRoot
                    && IsWordNamespace(attribute.Name.NamespaceName)
                    && attribute.Name.LocalName == "styleId")
            )
            .OrderBy(attribute => attribute.Name.NamespaceName, StringComparer.Ordinal)
            .ThenBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal))
        {
            AppendCanonicalValue(builder, attribute.Name.NamespaceName);
            AppendCanonicalValue(builder, attribute.Name.LocalName);
            var value = attribute.Value;
            if (
                IsWordNamespace(element.Name.NamespaceName)
                && StyleReferenceElementNames.Contains(element.Name.LocalName)
                && IsWordNamespace(attribute.Name.NamespaceName)
                && attribute.Name.LocalName == "val"
            )
            {
                if (
                    element.Name.LocalName == "next"
                    && string.Equals(value, ownStyleId, StringComparison.Ordinal)
                )
                {
                    value = "\0self";
                }
                else if (consolidationMap.TryGetValue(value, out var mapped))
                {
                    value = mapped;
                }
            }
            AppendCanonicalValue(builder, value);
        }
        builder.Append('[');
        foreach (var node in element.Nodes())
        {
            if (
                isStyleRoot
                && node is XElement identity
                && IsWordNamespace(identity.Name.NamespaceName)
                && identity.Name.LocalName is "name" or "aliases" or "rsid"
            )
            {
                continue;
            }
            switch (node)
            {
                case XElement child:
                    AppendCanonicalElement(
                        builder,
                        child,
                        ownStyleId,
                        consolidationMap,
                        isStyleRoot: false
                    );
                    break;
                case XText text:
                    AppendCanonicalValue(builder, "#text");
                    AppendCanonicalValue(builder, text.Value);
                    break;
                case XComment comment:
                    AppendCanonicalValue(builder, "#comment");
                    AppendCanonicalValue(builder, comment.Value);
                    break;
                case XProcessingInstruction instruction:
                    AppendCanonicalValue(builder, "#processing-instruction");
                    AppendCanonicalValue(builder, instruction.Target);
                    AppendCanonicalValue(builder, instruction.Data);
                    break;
            }
        }
        builder.Append(']');
    }

    private static void AppendCanonicalValue(StringBuilder builder, string value) =>
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append(';');

    private static bool IsInsideRemovedStyle(
        LosslessXmlDocument source,
        XmlSourceElement element,
        IReadOnlySet<int> removedStyleOrdinals
    )
    {
        var current = element;
        while (current.ParentOrdinal is { } parentOrdinal)
        {
            if (removedStyleOrdinals.Contains(parentOrdinal))
            {
                return true;
            }
            current = source.GetElement(parentOrdinal);
        }
        return false;
    }

    private static WordStyleType ExpectedStyleReferenceType(
        string partUri,
        LosslessXmlDocument source,
        XmlSourceElement element,
        WordStyleGraph graph,
        string? numberingPartUri
    ) => element.LocalName switch
    {
        "pStyle" => WordStyleType.Paragraph,
        "rStyle" => WordStyleType.Character,
        "style" => ExpectedGlossaryStyleType(source, element),
        "tblStyle" => WordStyleType.Table,
        "next" => ExpectedNextStyleType(graph, source, element),
        "numStyleLink" or "styleLink" when string.Equals(
            partUri,
            numberingPartUri,
            StringComparison.Ordinal
        ) => WordStyleType.Numbering,
        "basedOn" => ContainingStyle(graph, source, element).Type,
        "link" => ContainingStyle(graph, source, element).Type switch
        {
            WordStyleType.Paragraph => WordStyleType.Character,
            WordStyleType.Character => WordStyleType.Paragraph,
            _ => throw new WordSemanticEditException(
                "A table or numbering style contains an unsupported linked-style reference."
            ),
        },
        _ => throw new WordSemanticEditException(
            $"Style reference w:{element.LocalName} appears outside its modeled OOXML context."
        ),
    };

    private static WordStyleType ExpectedGlossaryStyleType(
        LosslessXmlDocument source,
        XmlSourceElement element
    )
    {
        if (element.ParentOrdinal is not { } parentOrdinal)
        {
            throw new WordSemanticEditException(
                "A w:style reference has no modeled glossary-property parent."
            );
        }
        var parent = source.GetElement(parentOrdinal);
        if (!IsWordNamespace(parent.NamespaceUri) || parent.LocalName != "docPartPr")
        {
            throw new WordSemanticEditException(
                "A w:style reference appears outside glossary document-part properties."
            );
        }
        return WordStyleType.Paragraph;
    }

    private static WordStyleType ExpectedNextStyleType(
        WordStyleGraph graph,
        LosslessXmlDocument source,
        XmlSourceElement element
    )
    {
        var owner = ContainingStyle(graph, source, element);
        if (owner.Type != WordStyleType.Paragraph)
        {
            throw new WordSemanticEditException(
                "A non-paragraph style contains an unsupported next-style reference."
            );
        }
        return WordStyleType.Paragraph;
    }

    private static WordStyleDefinition ContainingStyle(
        WordStyleGraph graph,
        LosslessXmlDocument source,
        XmlSourceElement element
    )
    {
        var current = element;
        while (current.ParentOrdinal is { } parentOrdinal)
        {
            var parent = source.GetElement(parentOrdinal);
            if (IsWordNamespace(parent.NamespaceUri) && parent.LocalName == "style")
            {
                var styleId = parent.Attributes.SingleOrDefault(attribute =>
                    IsWordNamespace(attribute.NamespaceUri)
                    && attribute.LocalName == "styleId"
                )?.Value;
                if (
                    styleId is not null
                    && graph.TryGetStyle(styleId, out var style)
                    && style is not null
                )
                {
                    return style;
                }
                break;
            }
            current = parent;
        }
        throw new WordSemanticEditException(
            $"Style relation w:{element.LocalName} is not owned by one known style definition."
        );
    }

    private static void ValidateConsolidatedStyles(
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
                "The consolidated package contains style-graph issues."
            );
        }
        foreach (var indexed in indexedCommands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var command = (WordStyleConsolidateCommand)indexed.Command;
            if (graph.TryGetStyle(command.SourceStyleId, out _))
            {
                throw new WordSemanticEditException(
                    $"Source style '{command.SourceStyleId}' survived consolidation."
                );
            }
            if (
                !graph.TryGetStyle(command.TargetStyleId, out var target)
                || target is null
                || !target.InheritanceResolvable
            )
            {
                throw new WordSemanticEditException(
                    $"Target style '{command.TargetStyleId}' did not survive as a resolvable definition."
                );
            }
            if (semanticDocument.Nodes.Any(node =>
                node.Properties.TryGetValue("style_id", out var styleId)
                && string.Equals(
                    styleId,
                    command.SourceStyleId,
                    StringComparison.Ordinal
                )
            ))
            {
                throw new WordSemanticEditException(
                    $"Semantic content still refers to consolidated style '{command.SourceStyleId}'."
                );
            }
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
                "The consolidated package contains numbering-graph issues."
            );
        }
    }

    private static Dictionary<string, WordPackagePartPayload>
        ComposeSequentialStylePayloads(
            OpcPackageSnapshot basePackage,
            IReadOnlyList<IReadOnlyList<WordPackagePartPayload>> stages
        )
    {
        var result = new Dictionary<string, WordPackagePartPayload>(StringComparer.Ordinal);
        foreach (var stage in stages)
        {
            foreach (var payload in stage)
            {
                if (result.TryGetValue(payload.PartUri, out var earlier))
                {
                    if (
                        !string.Equals(
                            earlier.AfterSha256,
                            payload.BeforeSha256,
                            StringComparison.OrdinalIgnoreCase
                        )
                        || !string.Equals(
                            earlier.EntryName,
                            payload.EntryName,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        throw new WordSemanticPreconditionException(
                            $"Sequential style edits for '{payload.PartUri}' do not form one exact byte chain."
                        );
                    }
                    result[payload.PartUri] = new WordPackagePartPayload(
                        payload.PartUri,
                        payload.EntryName,
                        earlier.BeforeContent,
                        payload.AfterContent
                    );
                    continue;
                }
                if (
                    !basePackage.Parts.TryGetValue(payload.PartUri, out var basePart)
                    || !string.Equals(
                        basePart.Entry.Sha256,
                        payload.BeforeSha256,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    throw new WordSemanticPreconditionException(
                        $"Sequential style edit for '{payload.PartUri}' is not based on the original package."
                    );
                }
                result.Add(payload.PartUri, payload);
            }
        }
        foreach (var partUri in result.Where(pair =>
            pair.Value.BeforeContent.AsSpan().SequenceEqual(pair.Value.AfterContent)
        ).Select(pair => pair.Key).ToArray())
        {
            result.Remove(partUri);
        }
        return result;
    }
}
