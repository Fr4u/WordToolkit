using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public abstract record WordStyleDefinitionCommand;

public sealed record WordStyleCreateCommand(
    string StyleId,
    string Name,
    WordStyleType StyleType,
    string? BasedOnStyleId = null,
    string? NextStyleId = null,
    bool? QuickFormat = null,
    int? UiPriority = null
) : WordStyleDefinitionCommand;

public sealed record WordStyleCloneCommand(
    string SourceStyleId,
    string StyleId,
    string Name
) : WordStyleDefinitionCommand;

public sealed record WordStyleConsolidateCommand(
    string SourceStyleId,
    string TargetStyleId
) : WordStyleDefinitionCommand;

public sealed record WordStyleDeleteUnusedCommand(
    string StyleId
) : WordStyleDefinitionCommand;

public sealed partial class WordSemanticTransactionPlanner
{
    public WordSemanticTransactionPlan PlanStyleEdits(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        IEnumerable<WordStyleDefinitionCommand> definitionCommands,
        IEnumerable<WordStyleAssignmentCommand> assignmentCommands,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        ArgumentNullException.ThrowIfNull(definitionCommands);
        ArgumentNullException.ThrowIfNull(assignmentCommands);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(
            package.Fingerprint,
            semanticDocument.PackageFingerprint,
            StringComparison.Ordinal
        ))
        {
            throw new WordSemanticPreconditionException(
                "Semantic projection and package snapshot have different fingerprints."
            );
        }

        var definitions = definitionCommands.ToArray();
        var assignments = assignmentCommands.ToArray();
        if (definitions.Length == 0)
        {
            return PlanStyleAssignments(
                package,
                semanticDocument,
                assignments,
                cancellationToken
            );
        }
        if (definitions.Length + assignments.Length > _options.MaxCommands)
        {
            throw new WordSemanticTransactionLimitException(
                $"Semantic transaction exceeds {_options.MaxCommands} commands."
            );
        }

        var indexed = definitions
            .Select((command, index) => new IndexedStyleDefinitionCommand(index, command))
            .ToArray();
        var creations = indexed.Where(item =>
            item.Command is WordStyleCreateCommand or WordStyleCloneCommand
        ).ToArray();
        var consolidations = indexed.Where(item =>
            item.Command is WordStyleConsolidateCommand
        ).ToArray();
        var deletions = indexed.Where(item =>
            item.Command is WordStyleDeleteUnusedCommand
        ).ToArray();
        if (
            creations.Length + consolidations.Length + deletions.Length
            != definitions.Length
        )
        {
            throw new WordSemanticEditException(
                "The semantic transaction contains an unsupported style-definition command."
            );
        }
        ValidateStyleDefinitionStageInteractions(creations, consolidations, deletions);

        var stages = new List<IReadOnlyList<WordPackagePartPayload>>();
        var definitionOperations = new List<WordStyleDefinitionOperationPlan>(
            definitions.Length
        );
        var intermediate = package;
        var intermediateSemantic = semanticDocument;
        if (creations.Length != 0)
        {
            var creationDraft = BuildStyleCreationDraft(
                intermediate,
                intermediateSemantic,
                creations,
                cancellationToken
            );
            stages.Add(creationDraft.Payloads);
            definitionOperations.AddRange(creationDraft.Operations);
            intermediate = MaterializeSnapshot(
                intermediate,
                creationDraft.Payloads,
                cancellationToken
            );
            intermediateSemantic = new WordSemanticProjector().Project(
                intermediate,
                cancellationToken
            );
            ValidateCreatedStyles(
                intermediate,
                intermediateSemantic,
                creationDraft.Operations,
                cancellationToken
            );
        }

        if (consolidations.Length != 0)
        {
            var consolidationDraft = BuildStyleConsolidationDraft(
                intermediate,
                intermediateSemantic,
                consolidations,
                cancellationToken
            );
            stages.Add(consolidationDraft.Payloads);
            definitionOperations.AddRange(consolidationDraft.Operations);
            intermediate = MaterializeSnapshot(
                intermediate,
                consolidationDraft.Payloads,
                cancellationToken
            );
            intermediateSemantic = new WordSemanticProjector().Project(
                intermediate,
                cancellationToken
            );
            ValidateConsolidatedStyles(
                intermediate,
                intermediateSemantic,
                consolidations,
                cancellationToken
            );
        }

        if (deletions.Length != 0)
        {
            var deletionDraft = BuildUnusedStyleDeletionDraft(
                intermediate,
                intermediateSemantic,
                deletions,
                cancellationToken
            );
            stages.Add(deletionDraft.Payloads);
            definitionOperations.AddRange(deletionDraft.Operations);
            intermediate = MaterializeSnapshot(
                intermediate,
                deletionDraft.Payloads,
                cancellationToken
            );
            intermediateSemantic = new WordSemanticProjector().Project(
                intermediate,
                cancellationToken
            );
            ValidateDeletedStyles(
                intermediate,
                intermediateSemantic,
                deletions,
                cancellationToken
            );
        }

        WordSemanticTransactionPlan? assignmentPlan = null;
        if (assignments.Length != 0)
        {
            assignmentPlan = PlanStyleAssignments(
                intermediate,
                intermediateSemantic,
                assignments,
                cancellationToken
            );
            stages.Add(assignmentPlan.PartPayloads.ToArray());
        }

        var payloads = ComposeSequentialStylePayloads(package, stages);

        var projectedEntries = payloads.Values.ToDictionary(
            payload => payload.EntryName,
            payload => (ReadOnlyMemory<byte>)payload.AfterContent,
            StringComparer.Ordinal
        );
        var resultFingerprint = OpcPackageFingerprint.ComputeProjected(
            package,
            projectedEntries
        );
        var assignmentOperations = assignmentPlan?.Operations
            ?? Array.Empty<WordSemanticOperationPlan>();
        return new WordSemanticTransactionPlan(
            CreateCombinedStylePlanId(
                package.Fingerprint,
                resultFingerprint,
                definitions,
                assignments
            ),
            package.Fingerprint,
            resultFingerprint,
            assignmentOperations,
            payloads,
            definitionOperations.OrderBy(operation => operation.Index).ToArray()
        );
    }

    private static void ValidateStyleDefinitionStageInteractions(
        IReadOnlyList<IndexedStyleDefinitionCommand> creations,
        IReadOnlyList<IndexedStyleDefinitionCommand> consolidations,
        IReadOnlyList<IndexedStyleDefinitionCommand> deletions
    )
    {
        var createdIds = creations.Select(item => item.Command switch
        {
            WordStyleCreateCommand create => create.StyleId,
            WordStyleCloneCommand clone => clone.StyleId,
            _ => throw new WordSemanticEditException(
                "The style-creation stage contains another command type."
            ),
        }).ToHashSet(StringComparer.Ordinal);
        foreach (var indexed in consolidations)
        {
            var command = (WordStyleConsolidateCommand)indexed.Command;
            if (createdIds.Contains(command.SourceStyleId))
            {
                throw new WordSemanticEditException(
                    "A style created in this plan cannot also be a consolidation source."
                );
            }
        }
        var consolidationIds = consolidations.SelectMany(item =>
        {
            var command = (WordStyleConsolidateCommand)item.Command;
            return new[] { command.SourceStyleId, command.TargetStyleId };
        }).ToHashSet(StringComparer.Ordinal);
        foreach (var indexed in deletions)
        {
            var styleId = ((WordStyleDeleteUnusedCommand)indexed.Command).StyleId;
            if (createdIds.Contains(styleId))
            {
                throw new WordSemanticEditException(
                    "A style created in this plan cannot also be deleted."
                );
            }
            if (consolidationIds.Contains(styleId))
            {
                throw new WordSemanticEditException(
                    "A style consolidated in this plan cannot also be deleted."
                );
            }
        }
    }

    private StyleDefinitionDraft BuildStyleCreationDraft(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        IReadOnlyList<IndexedStyleDefinitionCommand> commands,
        CancellationToken cancellationToken
    )
    {
        var graph = new WordStyleGraphBuilder().Build(
            package,
            semanticDocument,
            cancellationToken
        );
        if (!graph.HasStylesPart || graph.StylesPartUri is null)
        {
            throw new WordSemanticEditException(
                "Style definitions cannot be created because the package has no styles part."
            );
        }
        if (graph.StylesWithEffectsPartUri is not null)
        {
            throw new WordSemanticEditException(
                "Style definition edits require mirrored stylesWithEffects support."
            );
        }
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
        var root = source.Root;
        if (!IsWordNamespace(root.NamespaceUri) || root.LocalName != "styles")
        {
            throw new WordSemanticPreconditionException(
                "The styles source no longer has a Word styles root."
            );
        }

        var types = graph.Styles.ToDictionary(
            style => style.StyleId,
            style => style.Type,
            StringComparer.Ordinal
        );
        var newIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var indexedCommand in commands)
        {
            var command = indexedCommand.Command;
            ValidateDefinitionIdentity(command);
            var styleId = command switch
            {
                WordStyleCreateCommand create => create.StyleId,
                WordStyleCloneCommand clone => clone.StyleId,
                _ => throw new WordSemanticEditException(
                    $"Unsupported style creation command '{command.GetType().Name}'."
                ),
            };
            if (types.ContainsKey(styleId) || !newIds.Add(styleId))
            {
                throw new WordSemanticEditException(
                    $"Style ID '{styleId}' already exists or is created more than once."
                );
            }
            var type = command switch
            {
                WordStyleCreateCommand create => create.StyleType,
                WordStyleCloneCommand clone when graph.TryGetStyle(
                    clone.SourceStyleId,
                    out var sourceStyle
                ) && sourceStyle is not null => sourceStyle.Type,
                WordStyleCloneCommand clone => throw new WordSemanticEditException(
                    $"Source style '{clone.SourceStyleId}' does not exist."
                ),
                _ => throw new WordSemanticEditException(
                    $"Unsupported style definition command '{command.GetType().Name}'."
                ),
            };
            types.Add(styleId, type);
        }

        var fragments = new List<string>(commands.Count);
        var operations = new List<WordStyleDefinitionOperationPlan>(commands.Count);
        var sourceEncoding = Encoding.GetEncoding(
            source.EncodingName,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback
        );
        for (var index = 0; index < commands.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var indexedCommand = commands[index];
            var command = indexedCommand.Command;
            XElement element;
            WordStyleType type;
            string kind;
            string? sourceStyleId;
            string targetStyleId;
            switch (command)
            {
                case WordStyleCreateCommand create:
                    ValidateCreateReferences(create, types);
                    element = CreateStyleElement(root.NamespaceUri, create);
                    type = create.StyleType;
                    kind = "create_style";
                    sourceStyleId = null;
                    targetStyleId = create.StyleId;
                    break;
                case WordStyleCloneCommand clone:
                    var sourceDefinition = graph.Styles.Single(style =>
                        string.Equals(
                            style.StyleId,
                            clone.SourceStyleId,
                            StringComparison.Ordinal
                        )
                    );
                    element = CloneStyleElement(
                        source,
                        sourceDefinition,
                        clone,
                        root.NamespaceUri
                    );
                    type = sourceDefinition.Type;
                    kind = "clone_style";
                    sourceStyleId = clone.SourceStyleId;
                    targetStyleId = clone.StyleId;
                    break;
                default:
                    throw new WordSemanticEditException(
                        $"Unsupported style definition command '{command.GetType().Name}'."
                    );
            }
            var fragment = element.ToString(SaveOptions.DisableFormatting);
            fragments.Add(fragment);
            operations.Add(
                new WordStyleDefinitionOperationPlan(
                    indexedCommand.Index,
                    kind,
                    targetStyleId,
                    sourceStyleId,
                    type,
                    part.Uri,
                    root.Ordinal,
                    sourceEncoding.GetByteCount(fragment),
                    true
                )
            );
        }

        byte[] changed;
        try
        {
            changed = source.ApplyPatches(
                [source.CreateElementContentInsertionPatch(
                    root.Ordinal,
                    string.Concat(fragments),
                    XmlContentInsertionPosition.Append
                )],
                part.Entry.Sha256,
                cancellationToken
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordSemanticEditException(
                "Style definition fragments do not form safe XML.",
                exception
            );
        }
        var payload = new WordPackagePartPayload(
            part.Uri,
            part.Entry.Name,
            part.Entry.Content.ToArray(),
            changed
        );
        return new StyleDefinitionDraft([payload], operations);
    }

    private static void ValidateDefinitionIdentity(WordStyleDefinitionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var identity = command switch
        {
            WordStyleCreateCommand create => (create.StyleId, create.Name),
            WordStyleCloneCommand cloneCommand => (cloneCommand.StyleId, cloneCommand.Name),
            _ => throw new WordSemanticEditException(
                $"Unsupported style creation command '{command.GetType().Name}'."
            ),
        };
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.StyleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Name);
        if (identity.StyleId.Length > 253 || identity.Name.Length > 253)
        {
            throw new ArgumentException("Style IDs and names cannot exceed 253 characters.");
        }
        try
        {
            XmlConvert.VerifyXmlChars(identity.StyleId);
            XmlConvert.VerifyXmlChars(identity.Name);
        }
        catch (XmlException exception)
        {
            throw new WordSemanticEditException(
                "Style ID or name contains a character forbidden by XML 1.0.",
                exception
            );
        }
        if (
            command is WordStyleCloneCommand clone
            && (string.IsNullOrWhiteSpace(clone.SourceStyleId)
                || clone.SourceStyleId.Length > 253)
        )
        {
            throw new ArgumentException(
                "Source style IDs must contain between 1 and 253 characters."
            );
        }
    }

    private static void ValidateCreateReferences(
        WordStyleCreateCommand command,
        IReadOnlyDictionary<string, WordStyleType> types
    )
    {
        if (command.UiPriority is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command.UiPriority),
                "Style UI priority cannot be negative."
            );
        }
        ValidateStyleReference(
            command.StyleId,
            "basedOn",
            command.BasedOnStyleId,
            command.StyleType,
            types
        );
        if (command.NextStyleId is not null)
        {
            if (command.StyleType != WordStyleType.Paragraph)
            {
                throw new WordSemanticEditException(
                    $"Style '{command.StyleId}' cannot declare next because it is not a paragraph style."
                );
            }
            ValidateStyleReference(
                command.StyleId,
                "next",
                command.NextStyleId,
                WordStyleType.Paragraph,
                types
            );
        }
    }

    private static void ValidateStyleReference(
        string styleId,
        string relation,
        string? targetId,
        WordStyleType expectedType,
        IReadOnlyDictionary<string, WordStyleType> types
    )
    {
        if (targetId is null)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(targetId) || targetId.Length > 253)
        {
            throw new ArgumentException(
                $"Style '{styleId}' has an invalid {relation} style ID."
            );
        }
        if (!types.TryGetValue(targetId, out var type))
        {
            throw new WordSemanticEditException(
                $"Style '{styleId}' refers to missing {relation} style '{targetId}'."
            );
        }
        if (type != expectedType)
        {
            throw new WordSemanticEditException(
                $"Style '{styleId}' {relation} target '{targetId}' has incompatible type '{type}'."
            );
        }
    }

    private static XElement CreateStyleElement(
        string namespaceUri,
        WordStyleCreateCommand command
    )
    {
        XNamespace word = namespaceUri;
        var element = new XElement(
            word + "style",
            new XAttribute(XNamespace.Xmlns + "w", namespaceUri),
            new XAttribute(word + "type", StyleTypeValue(command.StyleType)),
            new XAttribute(word + "styleId", command.StyleId),
            new XAttribute(word + "customStyle", "1"),
            new XElement(word + "name", new XAttribute(word + "val", command.Name))
        );
        if (command.BasedOnStyleId is not null)
        {
            element.Add(
                new XElement(
                    word + "basedOn",
                    new XAttribute(word + "val", command.BasedOnStyleId)
                )
            );
        }
        if (command.NextStyleId is not null)
        {
            element.Add(
                new XElement(
                    word + "next",
                    new XAttribute(word + "val", command.NextStyleId)
                )
            );
        }
        if (command.UiPriority is not null)
        {
            element.Add(
                new XElement(
                    word + "uiPriority",
                    new XAttribute(
                        word + "val",
                        command.UiPriority.Value.ToString(CultureInfo.InvariantCulture)
                    )
                )
            );
        }
        if (command.QuickFormat == true)
        {
            element.Add(new XElement(word + "qFormat"));
        }
        return element;
    }

    private static XElement CloneStyleElement(
        LosslessXmlDocument source,
        WordStyleDefinition definition,
        WordStyleCloneCommand command,
        string namespaceUri
    )
    {
        XNamespace word = namespaceUri;
        var sourceElement = source.ParsedDocument.Root!
            .Elements(word + "style")
            .Single(element => source.GetElementOrdinal(element) == definition.SourceElementOrdinal);
        var clone = new XElement(sourceElement);
        CopyInheritedNamespaceDeclarations(sourceElement, clone);
        clone.SetAttributeValue(word + "styleId", command.StyleId);
        clone.SetAttributeValue(word + "default", null);
        clone.SetAttributeValue(word + "customStyle", "1");
        clone.Elements(word + "link").Remove();
        var names = clone.Elements(word + "name").ToArray();
        if (names.Length == 0)
        {
            clone.AddFirst(
                new XElement(word + "name", new XAttribute(word + "val", command.Name))
            );
        }
        else
        {
            names[0].SetAttributeValue(word + "val", command.Name);
        }
        foreach (var next in clone.Elements(word + "next"))
        {
            var value = next.Attribute(word + "val");
            if (string.Equals(value?.Value, command.SourceStyleId, StringComparison.Ordinal))
            {
                value!.Value = command.StyleId;
            }
        }
        return clone;
    }

    private static void CopyInheritedNamespaceDeclarations(
        XElement source,
        XElement clone
    )
    {
        var namespaces = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ancestor in source.AncestorsAndSelf().Reverse())
        {
            foreach (var attribute in ancestor.Attributes().Where(item =>
                item.IsNamespaceDeclaration
            ))
            {
                var prefix = attribute.Name.LocalName == "xmlns"
                    ? string.Empty
                    : attribute.Name.LocalName;
                namespaces[prefix] = attribute.Value;
            }
        }
        var declared = clone.Attributes()
            .Where(attribute => attribute.IsNamespaceDeclaration)
            .Select(attribute => attribute.Name.LocalName == "xmlns"
                ? string.Empty
                : attribute.Name.LocalName)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (prefix, uri) in namespaces.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (declared.Contains(prefix) || prefix == "xml")
            {
                continue;
            }
            clone.Add(
                prefix.Length == 0
                    ? new XAttribute("xmlns", uri)
                    : new XAttribute(XNamespace.Xmlns + prefix, uri)
            );
        }
    }

    private static void ValidateCreatedStyles(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        IReadOnlyList<WordStyleDefinitionOperationPlan> operations,
        CancellationToken cancellationToken
    )
    {
        var graph = new WordStyleGraphBuilder().Build(
            package,
            semanticDocument,
            cancellationToken
        );
        foreach (var operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                !graph.TryGetStyle(operation.StyleId, out var definition)
                || definition is null
                || definition.Type != operation.StyleType
            )
            {
                throw new WordSemanticEditException(
                    $"Created style '{operation.StyleId}' did not survive semantic projection."
                );
            }
            if (!definition.InheritanceResolvable)
            {
                throw new WordSemanticEditException(
                    $"Created style '{operation.StyleId}' has an unresolved inheritance chain."
                );
            }
            var issue = graph.Issues.FirstOrDefault(item =>
                string.Equals(item.StyleId, operation.StyleId, StringComparison.Ordinal)
            );
            if (issue is not null)
            {
                throw new WordSemanticEditException(
                    $"Created style '{operation.StyleId}' is invalid: {issue.Message}"
                );
            }
        }
    }

    private static OpcPackageSnapshot MaterializeSnapshot(
        OpcPackageSnapshot package,
        IEnumerable<WordPackagePartPayload> payloads,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mutation = new OpcPackageMutationBuilder(package);
        foreach (var payload in payloads)
        {
            mutation.ReplacePart(
                payload.PartUri,
                payload.AfterContent,
                payload.BeforeSha256
            );
        }
        using var stream = new MemoryStream();
        new OpcPackageSerializer().Write(stream, mutation);
        stream.Position = 0;
        return new OpcPackageReader().Read(stream, cancellationToken);
    }

    private static string CreateCombinedStylePlanId(
        string packageFingerprint,
        string resultFingerprint,
        IReadOnlyList<WordStyleDefinitionCommand> definitions,
        IReadOnlyList<WordStyleAssignmentCommand> assignments
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHashField(hash, "word-semantic-style-definition-plan-v1");
        AppendHashField(hash, packageFingerprint);
        AppendHashField(hash, resultFingerprint);
        foreach (var command in definitions)
        {
            switch (command)
            {
                case WordStyleCreateCommand create:
                    AppendHashField(hash, "create");
                    AppendHashField(hash, create.StyleId);
                    AppendHashField(hash, create.Name);
                    AppendHashField(hash, create.StyleType.ToString());
                    AppendHashField(hash, create.BasedOnStyleId ?? "\0");
                    AppendHashField(hash, create.NextStyleId ?? "\0");
                    AppendHashField(hash, create.QuickFormat?.ToString() ?? "\0");
                    AppendHashField(hash, create.UiPriority?.ToString(
                        CultureInfo.InvariantCulture
                    ) ?? "\0");
                    break;
                case WordStyleCloneCommand clone:
                    AppendHashField(hash, "clone");
                    AppendHashField(hash, clone.SourceStyleId);
                    AppendHashField(hash, clone.StyleId);
                    AppendHashField(hash, clone.Name);
                    break;
                case WordStyleConsolidateCommand consolidate:
                    AppendHashField(hash, "consolidate");
                    AppendHashField(hash, consolidate.SourceStyleId);
                    AppendHashField(hash, consolidate.TargetStyleId);
                    break;
                case WordStyleDeleteUnusedCommand delete:
                    AppendHashField(hash, "delete-unused");
                    AppendHashField(hash, delete.StyleId);
                    break;
            }
        }
        foreach (var assignment in assignments)
        {
            AppendHashField(hash, assignment.NodeId.Value);
            AppendHashField(hash, assignment.StyleId);
            AppendHashField(hash, assignment.ExpectedStyleId ?? "\0");
            AppendHashField(hash, assignment.RequireNoExplicitStyle ? "1" : "0");
        }
        return "wseplan_" + Convert.ToBase64String(
            hash.GetHashAndReset().AsSpan(0, 15)
        ).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool IsWordNamespace(string namespaceUri) =>
        namespaceUri is WordSemanticTextTarget.WordTransitionalNamespace
            or WordSemanticTextTarget.WordStrictNamespace;

    private static string StyleTypeValue(WordStyleType type) => type switch
    {
        WordStyleType.Paragraph => "paragraph",
        WordStyleType.Character => "character",
        WordStyleType.Table => "table",
        WordStyleType.Numbering => "numbering",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private sealed record StyleDefinitionDraft(
        IReadOnlyList<WordPackagePartPayload> Payloads,
        IReadOnlyList<WordStyleDefinitionOperationPlan> Operations
    );

    private sealed record IndexedStyleDefinitionCommand(
        int Index,
        WordStyleDefinitionCommand Command
    );
}
