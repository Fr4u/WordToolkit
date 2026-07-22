using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public sealed record WordSemanticTransactionOptions
{
    public static WordSemanticTransactionOptions Default { get; } = new();

    public int MaxCommands { get; init; } = 1_000;

    public long MaxTotalReplacementCharacters { get; init; } = 16L * 1024 * 1024;

    public long MaxTotalStyleIdCharacters { get; init; } = 1_000_000;

    internal void Validate()
    {
        if (MaxCommands <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCommands));
        }

        if (MaxTotalReplacementCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTotalReplacementCharacters));
        }

        if (MaxTotalStyleIdCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTotalStyleIdCharacters));
        }
    }
}

public sealed record WordTextReplacementCommand(
    SemanticNodeId NodeId,
    string NewValue,
    string? ExpectedText = null
);

public sealed record WordStyleAssignmentCommand(
    SemanticNodeId NodeId,
    string StyleId,
    string? ExpectedStyleId = null,
    bool RequireNoExplicitStyle = false
);

public sealed record WordSemanticOperationPlan(
    int Index,
    string Kind,
    SemanticNodeId NodeId,
    string SourcePartUri,
    int SourceElementOrdinal,
    int BeforeCharacters,
    int AfterCharacters,
    int XmlByteDelta,
    bool HasChange,
    string? PropertyName = null,
    string? BeforeValue = null,
    string? AfterValue = null
);

public sealed record WordSemanticPartChange(
    string PartUri,
    string EntryName,
    string BeforeSha256,
    string AfterSha256,
    int BeforeBytes,
    int AfterBytes
);

public sealed class WordSemanticTransactionPlan
{
    private readonly WordPackageTransactionCore _transaction;

    internal WordSemanticTransactionPlan(
        string planId,
        string basePackageFingerprint,
        string resultPackageFingerprint,
        IReadOnlyList<WordSemanticOperationPlan> operations,
        IReadOnlyDictionary<string, WordPackagePartPayload> parts
    )
    {
        PlanId = planId;
        BasePackageFingerprint = basePackageFingerprint;
        ResultPackageFingerprint = resultPackageFingerprint;
        Operations = new ReadOnlyCollection<WordSemanticOperationPlan>(operations.ToArray());
        _transaction = new WordPackageTransactionCore(
            basePackageFingerprint,
            resultPackageFingerprint,
            parts
        );
        ChangedParts = new ReadOnlyCollection<WordSemanticPartChange>(
            _transaction.Parts
                .OrderBy(part => part.PartUri, StringComparer.Ordinal)
                .Select(part => new WordSemanticPartChange(
                    part.PartUri,
                    part.EntryName,
                    part.BeforeSha256,
                    part.AfterSha256,
                    part.BeforeContent.Length,
                    part.AfterContent.Length
                ))
                .ToArray()
        );
    }

    public string PlanId { get; }

    public string BasePackageFingerprint { get; }

    public string ResultPackageFingerprint { get; }

    public IReadOnlyList<WordSemanticOperationPlan> Operations { get; }

    public IReadOnlyList<WordSemanticPartChange> ChangedParts { get; }

    public bool HasChanges => _transaction.HasChanges;

    public int OperationCount => Operations.Count;

    public int ChangedPartCount => ChangedParts.Count;

    public int ChangedOperationCount => Operations.Count(operation => operation.HasChange);

    public long TotalXmlByteDelta => ChangedParts.Sum(part =>
        (long)part.AfterBytes - part.BeforeBytes
    );

    public OpcPackageMutationBuilder CreateMutation(OpcPackageSnapshot currentSnapshot)
        => _transaction.CreateMutation(currentSnapshot);

    public OpcPackageMutationBuilder CreateInverseMutation(
        OpcPackageSnapshot appliedSnapshot
    )
        => _transaction.CreateInverseMutation(appliedSnapshot);
}

public sealed class WordSemanticTransactionPlanner
{
    private readonly WordSemanticTransactionOptions _options;
    private readonly LosslessXmlOptions _xmlOptions;

    public WordSemanticTransactionPlanner(
        WordSemanticTransactionOptions? options = null,
        LosslessXmlOptions? xmlOptions = null
    )
    {
        _options = options ?? WordSemanticTransactionOptions.Default;
        _xmlOptions = xmlOptions ?? LosslessXmlOptions.Default;
        _options.Validate();
        _xmlOptions.Validate();
    }

    public WordSemanticTransactionPlan PlanTextReplacements(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        IEnumerable<WordTextReplacementCommand> commands,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        ArgumentNullException.ThrowIfNull(commands);
        cancellationToken.ThrowIfCancellationRequested();
        if (
            !string.Equals(
                package.Fingerprint,
                semanticDocument.PackageFingerprint,
                StringComparison.Ordinal
            )
        )
        {
            throw new WordSemanticPreconditionException(
                "Semantic projection and package snapshot have different fingerprints."
            );
        }

        var materialized = commands.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException(
                "A semantic transaction requires at least one command.",
                nameof(commands)
            );
        }

        if (materialized.Length > _options.MaxCommands)
        {
            throw new WordSemanticTransactionLimitException(
                $"Semantic transaction exceeds {_options.MaxCommands} commands."
            );
        }

        long replacementCharacters = 0;
        foreach (var command in materialized)
        {
            ArgumentNullException.ThrowIfNull(command);
            ArgumentNullException.ThrowIfNull(command.NewValue);
            checked
            {
                replacementCharacters += command.NewValue.Length;
            }

            if (replacementCharacters > _options.MaxTotalReplacementCharacters)
            {
                throw new WordSemanticTransactionLimitException(
                    "Semantic transaction replacement text exceeds the configured limit."
                );
            }
        }

        var seenNodes = new HashSet<SemanticNodeId>();
        var seenSourceElements = new HashSet<(string PartUri, int Ordinal)>();
        var sources = new Dictionary<string, LosslessXmlDocument>(StringComparer.Ordinal);
        var sourceParts = new Dictionary<string, OpcPart>(StringComparer.Ordinal);
        var patches = new Dictionary<string, List<XmlSourcePatch>>(StringComparer.Ordinal);
        var operations = new List<WordSemanticOperationPlan>(materialized.Length);
        for (var index = 0; index < materialized.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var command = materialized[index];
            if (!seenNodes.Add(command.NodeId))
            {
                throw new WordSemanticEditException(
                    $"Semantic node '{command.NodeId}' is targeted more than once."
                );
            }

            var node = ResolveTextNode(semanticDocument, command);
            if (!package.Parts.TryGetValue(node.SourcePartUri, out var part))
            {
                throw new WordSemanticPreconditionException(
                    $"Source part '{node.SourcePartUri}' no longer exists."
                );
            }

            if (!sources.TryGetValue(part.Uri, out var source))
            {
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
                        $"Source part '{part.Uri}' cannot be edited losslessly.",
                        exception
                    );
                }

                sources.Add(part.Uri, source);
                sourceParts.Add(part.Uri, part);
                patches.Add(part.Uri, []);
            }

            XmlSourceElement element;
            try
            {
                element = source.GetElement(node.SourceElementOrdinal);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new WordSemanticPreconditionException(
                    $"Source element {node.SourceElementOrdinal} no longer exists.",
                    exception
                );
            }

            if (!WordSemanticTextTarget.IsProjectedTextElement(element))
            {
                throw new WordSemanticPreconditionException(
                    $"Source element {node.SourceElementOrdinal} is no longer a Word text element."
                );
            }

            if (!seenSourceElements.Add((part.Uri, element.Ordinal)))
            {
                throw new WordSemanticEditException(
                    $"Source element {element.Ordinal} in '{part.Uri}' is targeted more than once."
                );
            }

            var before = node.Text ?? string.Empty;
            if (!string.Equals(element.Value, before, StringComparison.Ordinal))
            {
                throw new WordSemanticPreconditionException(
                    $"Source text for semantic node '{node.Id}' changed after projection."
                );
            }

            IReadOnlyList<XmlSourcePatch> commandPatches;
            try
            {
                commandPatches = source.CreateElementTextPatches(
                    element.Ordinal,
                    command.NewValue,
                    before,
                    preserveBoundaryWhitespace: true,
                    cancellationToken
                );
            }
            catch (LosslessXmlPreconditionException exception)
            {
                throw new WordSemanticPreconditionException(exception.Message, exception);
            }
            catch (LosslessXmlException exception)
            {
                throw new WordSemanticEditException(
                    $"Text node '{node.Id}' cannot be changed without destroying source markup.",
                    exception
                );
            }

            patches[part.Uri].AddRange(commandPatches);
            var byteDelta = commandPatches.Sum(patch =>
                patch.Replacement.Length - patch.ByteLength
            );
            operations.Add(
                new WordSemanticOperationPlan(
                    index,
                    "replace_text",
                    node.Id,
                    part.Uri,
                    element.Ordinal,
                    before.Length,
                    command.NewValue.Length,
                    byteDelta,
                    commandPatches.Count != 0
                )
            );
        }

        var payloads = new Dictionary<string, WordPackagePartPayload>(StringComparer.Ordinal);
        var projectedEntries = new Dictionary<string, ReadOnlyMemory<byte>>(
            StringComparer.Ordinal
        );
        foreach (var (partUri, source) in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (patches[partUri].Count == 0)
            {
                continue;
            }

            var part = sourceParts[partUri];
            byte[] changed;
            try
            {
                changed = source.ApplyPatches(
                    patches[partUri],
                    part.Entry.Sha256,
                    cancellationToken
                );
            }
            catch (LosslessXmlPreconditionException exception)
            {
                throw new WordSemanticPreconditionException(exception.Message, exception);
            }
            catch (LosslessXmlException exception)
            {
                throw new WordSemanticEditException(
                    $"Planned edits for '{partUri}' do not form safe XML.",
                    exception
                );
            }

            if (changed.AsSpan().SequenceEqual(part.Entry.Content.Span))
            {
                continue;
            }

            var payload = new WordPackagePartPayload(
                part.Uri,
                part.Entry.Name,
                part.Entry.Content.ToArray(),
                changed
            );
            payloads.Add(part.Uri, payload);
            projectedEntries.Add(part.Entry.Name, changed);
        }

        var resultFingerprint = payloads.Count == 0
            ? package.Fingerprint
            : OpcPackageFingerprint.ComputeProjected(package, projectedEntries);
        var planId = CreatePlanId(package.Fingerprint, operations, materialized);
        return new WordSemanticTransactionPlan(
            planId,
            package.Fingerprint,
            resultFingerprint,
            operations,
            payloads
        );
    }

    public WordSemanticTransactionPlan PlanStyleAssignments(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        IEnumerable<WordStyleAssignmentCommand> commands,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        ArgumentNullException.ThrowIfNull(commands);
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

        var materialized = commands.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException(
                "A semantic style transaction requires at least one command.",
                nameof(commands)
            );
        }
        if (materialized.Length > _options.MaxCommands)
        {
            throw new WordSemanticTransactionLimitException(
                $"Semantic transaction exceeds {_options.MaxCommands} commands."
            );
        }

        long styleIdCharacters = 0;
        foreach (var command in materialized)
        {
            ArgumentNullException.ThrowIfNull(command);
            ArgumentException.ThrowIfNullOrWhiteSpace(command.StyleId);
            if (command.StyleId.Length > 253 || command.ExpectedStyleId?.Length > 253)
            {
                throw new ArgumentException(
                    "Style IDs cannot exceed 253 characters.",
                    nameof(commands)
                );
            }
            if (command.RequireNoExplicitStyle && command.ExpectedStyleId is not null)
            {
                throw new ArgumentException(
                    "A style command cannot require both an expected style and no explicit style.",
                    nameof(commands)
                );
            }
            checked
            {
                styleIdCharacters += command.StyleId.Length;
                styleIdCharacters += command.ExpectedStyleId?.Length ?? 0;
            }
            if (styleIdCharacters > _options.MaxTotalStyleIdCharacters)
            {
                throw new WordSemanticTransactionLimitException(
                    "Semantic transaction style IDs exceed the configured limit."
                );
            }
        }

        var styleGraph = new WordStyleGraphBuilder().Build(
            package,
            semanticDocument,
            cancellationToken
        );
        if (!styleGraph.HasStylesPart)
        {
            throw new WordSemanticEditException(
                "A named style cannot be assigned because the package has no styles part."
            );
        }

        var seenNodes = new HashSet<SemanticNodeId>();
        var seenSourceElements = new HashSet<(string PartUri, int Ordinal)>();
        var sources = new Dictionary<string, LosslessXmlDocument>(StringComparer.Ordinal);
        var sourceParts = new Dictionary<string, OpcPart>(StringComparer.Ordinal);
        var patches = new Dictionary<string, List<XmlSourcePatch>>(StringComparer.Ordinal);
        var operations = new List<WordSemanticOperationPlan>(materialized.Length);
        for (var index = 0; index < materialized.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var command = materialized[index];
            if (!seenNodes.Add(command.NodeId))
            {
                throw new WordSemanticEditException(
                    $"Semantic node '{command.NodeId}' is targeted more than once."
                );
            }

            var target = ResolveStyleTarget(semanticDocument, command);
            var definition = ResolveStyleDefinition(styleGraph, target, command.StyleId);
            if (!package.Parts.TryGetValue(target.Node.SourcePartUri, out var part))
            {
                throw new WordSemanticPreconditionException(
                    $"Source part '{target.Node.SourcePartUri}' no longer exists."
                );
            }
            if (!sources.TryGetValue(part.Uri, out var source))
            {
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
                        $"Source part '{part.Uri}' cannot be edited losslessly.",
                        exception
                    );
                }
                sources.Add(part.Uri, source);
                sourceParts.Add(part.Uri, part);
                patches.Add(part.Uri, []);
            }

            XmlSourceElement element;
            try
            {
                element = source.GetElement(target.Node.SourceElementOrdinal);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new WordSemanticPreconditionException(
                    $"Source element {target.Node.SourceElementOrdinal} no longer exists.",
                    exception
                );
            }
            ValidateStyleSourceElement(element, target);
            if (!seenSourceElements.Add((part.Uri, element.Ordinal)))
            {
                throw new WordSemanticEditException(
                    $"Source element {element.Ordinal} in '{part.Uri}' is targeted more than once."
                );
            }

            var commandPatches = CreateStylePatches(
                source,
                element,
                target,
                command,
                cancellationToken
            );
            patches[part.Uri].AddRange(commandPatches);
            var byteDelta = commandPatches.Sum(patch =>
                patch.Replacement.Length - patch.ByteLength
            );
            operations.Add(
                new WordSemanticOperationPlan(
                    index,
                    "set_style",
                    target.Node.Id,
                    part.Uri,
                    element.Ordinal,
                    target.CurrentStyleId?.Length ?? 0,
                    definition.StyleId.Length,
                    byteDelta,
                    commandPatches.Count != 0,
                    "style_id",
                    target.CurrentStyleId,
                    definition.StyleId
                )
            );
        }

        var payloads = BuildPartPayloads(
            sources,
            sourceParts,
            patches,
            cancellationToken
        );
        var projectedEntries = payloads.Values.ToDictionary(
            payload => payload.EntryName,
            payload => (ReadOnlyMemory<byte>)payload.AfterContent,
            StringComparer.Ordinal
        );
        var resultFingerprint = payloads.Count == 0
            ? package.Fingerprint
            : OpcPackageFingerprint.ComputeProjected(package, projectedEntries);
        return new WordSemanticTransactionPlan(
            CreateStylePlanId(package.Fingerprint, operations, materialized),
            package.Fingerprint,
            resultFingerprint,
            operations,
            payloads
        );
    }

    private static IReadOnlyDictionary<string, WordPackagePartPayload> BuildPartPayloads(
        IReadOnlyDictionary<string, LosslessXmlDocument> sources,
        IReadOnlyDictionary<string, OpcPart> sourceParts,
        IReadOnlyDictionary<string, List<XmlSourcePatch>> patches,
        CancellationToken cancellationToken
    )
    {
        var payloads = new Dictionary<string, WordPackagePartPayload>(StringComparer.Ordinal);
        foreach (var (partUri, source) in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (patches[partUri].Count == 0)
            {
                continue;
            }
            var part = sourceParts[partUri];
            byte[] changed;
            try
            {
                changed = source.ApplyPatches(
                    patches[partUri],
                    part.Entry.Sha256,
                    cancellationToken
                );
            }
            catch (LosslessXmlPreconditionException exception)
            {
                throw new WordSemanticPreconditionException(exception.Message, exception);
            }
            catch (LosslessXmlException exception)
            {
                throw new WordSemanticEditException(
                    $"Planned edits for '{partUri}' do not form safe XML.",
                    exception
                );
            }
            if (!changed.AsSpan().SequenceEqual(part.Entry.Content.Span))
            {
                payloads.Add(
                    part.Uri,
                    new WordPackagePartPayload(
                        part.Uri,
                        part.Entry.Name,
                        part.Entry.Content.ToArray(),
                        changed
                    )
                );
            }
        }
        return payloads;
    }

    private static StyleTarget ResolveStyleTarget(
        WordSemanticDocument document,
        WordStyleAssignmentCommand command
    )
    {
        if (!document.TryGetNode(command.NodeId, out var node) || node is null)
        {
            throw new WordSemanticEditException(
                $"Semantic node '{command.NodeId}' does not exist."
            );
        }
        var shape = node.Kind switch
        {
            WordSemanticNodeKind.Paragraph => new StyleShape(
                WordStyleType.Paragraph,
                "p",
                "pPr",
                "pStyle"
            ),
            WordSemanticNodeKind.Run => new StyleShape(
                WordStyleType.Character,
                "r",
                "rPr",
                "rStyle"
            ),
            WordSemanticNodeKind.Table => new StyleShape(
                WordStyleType.Table,
                "tbl",
                "tblPr",
                "tblStyle"
            ),
            _ => throw new WordSemanticEditException(
                $"Semantic node '{command.NodeId}' is {node.Kind}; only paragraph, run, and table styles are assignable."
            ),
        };
        node.Properties.TryGetValue("style_id", out var currentStyleId);
        if (
            command.ExpectedStyleId is not null
            && !string.Equals(
                currentStyleId,
                command.ExpectedStyleId,
                StringComparison.Ordinal
            )
        )
        {
            throw new WordSemanticPreconditionException(
                $"Semantic node '{command.NodeId}' no longer has expected style '{command.ExpectedStyleId}'."
            );
        }
        if (command.RequireNoExplicitStyle && currentStyleId is not null)
        {
            throw new WordSemanticPreconditionException(
                $"Semantic node '{command.NodeId}' now has explicit style '{currentStyleId}'."
            );
        }
        return new StyleTarget(node, shape, currentStyleId);
    }

    private static WordStyleDefinition ResolveStyleDefinition(
        WordStyleGraph graph,
        StyleTarget target,
        string styleId
    )
    {
        if (!graph.TryGetStyle(styleId, out var definition) || definition is null)
        {
            throw new WordSemanticEditException(
                $"Style '{styleId}' does not exist in the package style graph."
            );
        }
        if (definition.Type != target.Shape.StyleType)
        {
            throw new WordSemanticEditException(
                $"Style '{styleId}' is {definition.Type}, not {target.Shape.StyleType}."
            );
        }
        if (!definition.InheritanceResolvable)
        {
            throw new WordSemanticEditException(
                $"Style '{styleId}' has an unresolved inheritance chain."
            );
        }
        return definition;
    }

    private static void ValidateStyleSourceElement(
        XmlSourceElement element,
        StyleTarget target
    )
    {
        if (
            element.NamespaceUri is not WordSemanticTextTarget.WordTransitionalNamespace
                and not WordSemanticTextTarget.WordStrictNamespace
            || !string.Equals(
                element.LocalName,
                target.Shape.TargetElement,
                StringComparison.Ordinal
            )
        )
        {
            throw new WordSemanticPreconditionException(
                $"Source element {element.Ordinal} is no longer the projected {target.Node.Kind} element."
            );
        }
    }

    private static IReadOnlyList<XmlSourcePatch> CreateStylePatches(
        LosslessXmlDocument source,
        XmlSourceElement targetElement,
        StyleTarget target,
        WordStyleAssignmentCommand command,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var properties = targetElement.Children.Where(child =>
            string.Equals(child.NamespaceUri, targetElement.NamespaceUri, StringComparison.Ordinal)
            && string.Equals(
                child.LocalName,
                target.Shape.PropertiesElement,
                StringComparison.Ordinal
            )
        ).ToArray();
        if (properties.Length > 1)
        {
            throw new WordSemanticEditException(
                $"Semantic node '{target.Node.Id}' contains duplicate {target.Shape.PropertiesElement} elements."
            );
        }
        if (properties.Length == 0)
        {
            var fragment = StyleMarkup(
                targetElement.NamespaceUri,
                target.Shape.PropertiesElement,
                target.Shape.StyleElement,
                command.StyleId
            );
            return
            [
                source.CreateElementContentInsertionPatch(
                    targetElement.Ordinal,
                    fragment,
                    XmlContentInsertionPosition.Prepend
                ),
            ];
        }

        var propertyElement = properties[0];
        var styles = propertyElement.Children.Where(child =>
            string.Equals(child.NamespaceUri, targetElement.NamespaceUri, StringComparison.Ordinal)
            && string.Equals(child.LocalName, target.Shape.StyleElement, StringComparison.Ordinal)
        ).ToArray();
        if (styles.Length > 1)
        {
            throw new WordSemanticEditException(
                $"Semantic node '{target.Node.Id}' contains duplicate {target.Shape.StyleElement} elements."
            );
        }
        if (styles.Length == 0)
        {
            return
            [
                source.CreateElementContentInsertionPatch(
                    propertyElement.Ordinal,
                    StyleMarkup(
                        targetElement.NamespaceUri,
                        null,
                        target.Shape.StyleElement,
                        command.StyleId
                    ),
                    XmlContentInsertionPosition.Prepend
                ),
            ];
        }

        var styleElement = styles[0];
        var values = styleElement.Attributes.Where(attribute =>
            string.Equals(attribute.NamespaceUri, targetElement.NamespaceUri, StringComparison.Ordinal)
            && string.Equals(attribute.LocalName, "val", StringComparison.Ordinal)
        ).ToArray();
        if (values.Length > 1)
        {
            throw new WordSemanticEditException(
                $"Semantic node '{target.Node.Id}' contains duplicate style values."
            );
        }
        var lexicalStyleId = values.SingleOrDefault()?.Value;
        if (!string.Equals(lexicalStyleId, target.CurrentStyleId, StringComparison.Ordinal))
        {
            throw new WordSemanticPreconditionException(
                $"Source style for semantic node '{target.Node.Id}' changed after projection."
            );
        }
        return source.CreateElementAttributeValuePatches(
            styleElement.Ordinal,
            targetElement.NamespaceUri,
            "val",
            command.StyleId,
            expectedValue: lexicalStyleId,
            preferredPrefix: string.IsNullOrEmpty(targetElement.Prefix)
                ? "wtk"
                : targetElement.Prefix
        );
    }

    private static string StyleMarkup(
        string namespaceUri,
        string? containerName,
        string styleElementName,
        string styleId
    )
    {
        XNamespace word = namespaceUri;
        var style = new XElement(
            word + styleElementName,
            new XAttribute(word + "val", styleId)
        );
        var root = containerName is null
            ? style
            : new XElement(word + containerName, style);
        root.Add(new XAttribute(XNamespace.Xmlns + "wtk", namespaceUri));
        return root.ToString(SaveOptions.DisableFormatting);
    }

    private static string CreateStylePlanId(
        string packageFingerprint,
        IReadOnlyList<WordSemanticOperationPlan> operations,
        IReadOnlyList<WordStyleAssignmentCommand> commands
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHashField(hash, "word-semantic-style-plan-v1");
        AppendHashField(hash, packageFingerprint);
        foreach (var operation in operations)
        {
            var command = commands[operation.Index];
            AppendHashField(hash, operation.Index.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ));
            AppendHashField(hash, operation.NodeId.Value);
            AppendHashField(hash, command.StyleId);
            AppendHashField(hash, command.ExpectedStyleId ?? "\u0000");
            AppendHashField(hash, command.RequireNoExplicitStyle ? "1" : "0");
        }
        var digest = hash.GetHashAndReset();
        return "wseplan_" + Convert.ToBase64String(digest.AsSpan(0, 15))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed record StyleShape(
        WordStyleType StyleType,
        string TargetElement,
        string PropertiesElement,
        string StyleElement
    );

    private sealed record StyleTarget(
        WordSemanticNode Node,
        StyleShape Shape,
        string? CurrentStyleId
    );

    private static WordSemanticNode ResolveTextNode(
        WordSemanticDocument semanticDocument,
        WordTextReplacementCommand command
    )
    {
        if (!semanticDocument.TryGetNode(command.NodeId, out var node) || node is null)
        {
            throw new KeyNotFoundException(
                $"Semantic node '{command.NodeId}' does not exist."
            );
        }

        if (node.Kind != WordSemanticNodeKind.Text)
        {
            throw new WordSemanticEditException(
                $"Semantic node '{command.NodeId}' is {node.Kind}, not editable text."
            );
        }

        var projectedText = node.Text ?? string.Empty;
        if (
            command.ExpectedText is not null
            && !string.Equals(
                projectedText,
                command.ExpectedText,
                StringComparison.Ordinal
            )
        )
        {
            throw new WordSemanticPreconditionException(
                $"Semantic node '{command.NodeId}' no longer contains the expected text."
            );
        }

        return node;
    }

    private static string CreatePlanId(
        string packageFingerprint,
        IReadOnlyList<WordSemanticOperationPlan> operations,
        IReadOnlyList<WordTextReplacementCommand> commands
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHashField(hash, packageFingerprint);
        foreach (var operation in operations)
        {
            AppendHashField(hash, operation.Index.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ));
            AppendHashField(hash, operation.NodeId.Value);
            AppendHashField(hash, commands[operation.Index].NewValue);
            AppendHashField(hash, commands[operation.Index].ExpectedText ?? "\u0000");
        }

        var digest = hash.GetHashAndReset();
        return "wplan_" + Convert.ToBase64String(digest.AsSpan(0, 15))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void AppendHashField(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
            length,
            bytes.Length
        );
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}

internal static class WordSemanticTextTarget
{
    internal const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    internal const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string MathTransitionalNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string MathStrictNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/math";

    public static bool IsProjectedTextElement(XmlSourceElement element) =>
        (
            element.NamespaceUri is WordTransitionalNamespace or WordStrictNamespace
            && element.LocalName is "t" or "delText"
        )
        || (
            element.NamespaceUri is MathTransitionalNamespace or MathStrictNamespace
            && element.LocalName == "t"
        );
}

public sealed class WordSemanticTransactionLimitException : WordSemanticEditException
{
    public WordSemanticTransactionLimitException(string message)
        : base(message)
    {
    }
}
