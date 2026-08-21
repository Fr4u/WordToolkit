using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public sealed class WordEquationGraphBuilder
{
    private const string MathTransitionalNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string MathStrictNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/math";
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";

    private static readonly IReadOnlyDictionary<string, ObjectSpec> ObjectSpecs =
        new Dictionary<string, ObjectSpec>(StringComparer.Ordinal)
        {
            ["acc"] = new(
                WordMathNodeKind.Accent,
                "accPr",
                new ArgumentSpec("e", "base", 1, 1)
            ),
            ["bar"] = new(
                WordMathNodeKind.Bar,
                "barPr",
                new ArgumentSpec("e", "base", 1, 1)
            ),
            ["borderBox"] = new(
                WordMathNodeKind.BorderBox,
                "borderBoxPr",
                new ArgumentSpec("e", "base", 1, 1)
            ),
            ["box"] = new(
                WordMathNodeKind.Box,
                "boxPr",
                new ArgumentSpec("e", "base", 1, 1)
            ),
            ["d"] = new(
                WordMathNodeKind.Delimiter,
                "dPr",
                new ArgumentSpec("e", "argument", 1, int.MaxValue)
            ),
            ["eqArr"] = new(
                WordMathNodeKind.EquationArray,
                "eqArrPr",
                new ArgumentSpec("e", "row", 1, int.MaxValue)
            ),
            ["f"] = new(
                WordMathNodeKind.Fraction,
                "fPr",
                new ArgumentSpec("num", "numerator", 1, 1),
                new ArgumentSpec("den", "denominator", 1, 1)
            ),
            ["func"] = new(
                WordMathNodeKind.Function,
                "funcPr",
                new ArgumentSpec("fName", "function_name", 1, 1),
                new ArgumentSpec("e", "argument", 1, 1)
            ),
            ["groupChr"] = new(
                WordMathNodeKind.GroupCharacter,
                "groupChrPr",
                new ArgumentSpec("e", "base", 1, 1)
            ),
            ["limLow"] = new(
                WordMathNodeKind.LowerLimit,
                "limLowPr",
                new ArgumentSpec("e", "base", 1, 1),
                new ArgumentSpec("lim", "limit", 1, 1)
            ),
            ["limUpp"] = new(
                WordMathNodeKind.UpperLimit,
                "limUppPr",
                new ArgumentSpec("e", "base", 1, 1),
                new ArgumentSpec("lim", "limit", 1, 1)
            ),
            ["nary"] = new(
                WordMathNodeKind.Nary,
                "naryPr",
                new ArgumentSpec("sub", "lower_limit", 1, 1),
                new ArgumentSpec("sup", "upper_limit", 1, 1),
                new ArgumentSpec("e", "base", 1, 1)
            ),
            ["phant"] = new(
                WordMathNodeKind.Phantom,
                "phantPr",
                new ArgumentSpec("e", "base", 1, 1)
            ),
            ["rad"] = new(
                WordMathNodeKind.Radical,
                "radPr",
                new ArgumentSpec("deg", "degree", 1, 1),
                new ArgumentSpec("e", "base", 1, 1)
            ),
            ["sPre"] = new(
                WordMathNodeKind.PreSubSuperscript,
                "sPrePr",
                new ArgumentSpec("sub", "pre_subscript", 1, 1),
                new ArgumentSpec("sup", "pre_superscript", 1, 1),
                new ArgumentSpec("e", "base", 1, 1)
            ),
            ["sSub"] = new(
                WordMathNodeKind.Subscript,
                "sSubPr",
                new ArgumentSpec("e", "base", 1, 1),
                new ArgumentSpec("sub", "subscript", 1, 1)
            ),
            ["sSubSup"] = new(
                WordMathNodeKind.SubSuperscript,
                "sSubSupPr",
                new ArgumentSpec("e", "base", 1, 1),
                new ArgumentSpec("sub", "subscript", 1, 1),
                new ArgumentSpec("sup", "superscript", 1, 1)
            ),
            ["sSup"] = new(
                WordMathNodeKind.Superscript,
                "sSupPr",
                new ArgumentSpec("e", "base", 1, 1),
                new ArgumentSpec("sup", "superscript", 1, 1)
            ),
        };

    private static readonly HashSet<string> ArgumentElementNames = new(
        new[] { "deg", "den", "e", "fName", "lim", "num", "sub", "sup" },
        StringComparer.Ordinal
    );

    private static readonly HashSet<string> PropertyContainerNames = new(
        new[]
        {
            "accPr", "argPr", "barPr", "borderBoxPr", "boxPr", "ctrlPr",
            "dPr", "eqArrPr", "fPr", "funcPr", "groupChrPr", "limLowPr",
            "limUppPr", "mPr", "mcPr", "naryPr", "oMathParaPr", "phantPr",
            "radPr", "rPr", "sPrePr", "sSubPr", "sSubSupPr", "sSupPr",
        },
        StringComparer.Ordinal
    );

    private static readonly HashSet<string> BooleanPropertyNames = new(
        new[]
        {
            "align", "align_scripts", "degree_hidden", "differential",
            "display_defaults", "grow", "hide_bottom", "hide_left",
            "hide_placeholder", "hide_right", "hide_top", "literal",
            "maximum_distribution", "no_break", "normal_text",
            "object_distribution", "operator_emulator", "show_phantom",
            "small_fraction", "strike_bltr", "strike_horizontal",
            "strike_tlbr", "strike_vertical", "subscript_hidden",
            "superscript_hidden", "transparent", "wrap_right", "zero_ascent",
            "zero_descent", "zero_width",
        },
        StringComparer.Ordinal
    );

    private static readonly HashSet<string> IntegerPropertyNames = new(
        new[]
        {
            "alignment_at", "argument_size", "column_gap", "column_gap_rule",
            "column_spacing", "count", "inter_spacing", "intra_spacing",
            "left_margin", "post_spacing", "pre_spacing", "right_margin",
            "row_spacing", "row_spacing_rule", "wrap_indent",
        },
        StringComparer.Ordinal
    );

    private static readonly IReadOnlyDictionary<string, HashSet<string>> EnumValues =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["base_justification"] = Set("left", "center", "right"),
            ["break_binary"] = Set("before", "after", "repeat"),
            ["break_binary_subtraction"] = Set("--", "-+", "+-"),
            ["default_justification"] = Set("left", "right", "center", "centerGroup"),
            ["fraction_type"] = Set("bar", "skw", "lin", "noBar"),
            ["integral_limit_location"] = Set("undOvr", "subSup"),
            ["justification"] = Set("left", "right", "center", "centerGroup"),
            ["limit_location"] = Set("undOvr", "subSup"),
            ["matrix_column_justification"] = Set("left", "center", "right"),
            ["nary_limit_location"] = Set("undOvr", "subSup"),
            ["position"] = Set("top", "bot"),
            ["script"] = Set(
                "roman",
                "script",
                "fraktur",
                "double-struck",
                "sans-serif",
                "monospace"
            ),
            ["shape"] = Set("centered", "match"),
            ["style"] = Set("p", "b", "i", "bi"),
            ["vertical_justification"] = Set("top", "bot"),
        };

    private readonly WordEquationGraphOptions _options;

    public WordEquationGraphBuilder(WordEquationGraphOptions? options = null)
    {
        _options = options ?? WordEquationGraphOptions.Default;
        _options.Validate();
    }

    public WordEquationGraph Build(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        cancellationToken.ThrowIfCancellationRequested();
        if (
            !string.Equals(
                package.Fingerprint,
                semanticDocument.PackageFingerprint,
                StringComparison.Ordinal
            )
        )
        {
            throw new WordEquationProjectionException(
                "Equation graph requires a semantic projection of the same package snapshot."
            );
        }

        var state = new BuildState(_options, semanticDocument);
        foreach (var partUri in semanticDocument.ProjectedPartUris)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!package.Parts.TryGetValue(partUri, out var part))
            {
                throw new WordEquationProjectionException(
                    $"Projected story part '{partUri}' is missing from the package."
                );
            }
            if (part.Entry.Content.Length > _options.MaxPartBytes)
            {
                throw new WordEquationLimitException(
                    $"Story part '{partUri}' exceeds {_options.MaxPartBytes} bytes."
                );
            }

            var source = ParsePart(part, cancellationToken);
            ParseStoryPart(partUri, source, state, cancellationToken);
        }

        state.Settings = ParseMathSettings(
            package,
            semanticDocument,
            state,
            cancellationToken
        );
        return new WordEquationGraph(
            package.Fingerprint,
            semanticDocument.MainPartUri,
            state.Equations,
            state.MathParagraphs,
            state.Settings,
            state.Issues,
            state.IssuesTruncated
        );
    }

    private LosslessXmlDocument ParsePart(
        OpcPart part,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return LosslessXmlDocument.Parse(
                part.Entry.Content,
                new LosslessXmlOptions
                {
                    MaxSourceBytes = _options.MaxPartBytes,
                    MaxXmlCharacters = _options.MaxPartBytes,
                    MaxXmlElements = 1_000_000,
                    MaxXmlDepth = 256,
                    MaxTextCharacters = _options.MaxPartBytes,
                },
                cancellationToken
            );
        }
        catch (LosslessXmlLimitException exception)
        {
            throw new WordEquationLimitException(
                $"Part '{part.Uri}' exceeds an XML safety limit: {exception.Message}"
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordEquationProjectionException(
                $"Part '{part.Uri}' is not safe, well-formed XML.",
                exception
            );
        }
    }

    private void ParseStoryPart(
        string partUri,
        LosslessXmlDocument source,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        var root = source.ParsedDocument.Root
            ?? throw new WordEquationProjectionException(
                $"Projected story part '{partUri}' has no root element."
            );
        var paragraphByElement = new Dictionary<XElement, MutableMathParagraph>(
            ReferenceEqualityComparer.Instance
        );
        foreach (
            var paragraph in root.DescendantsAndSelf()
                .Where(element => IsMathElement(element, "oMathPara"))
                .OrderBy(source.GetElementOrdinal)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++state.MathParagraphCount > _options.MaxMathParagraphs)
            {
                throw new WordEquationLimitException(
                    $"Document contains more than {_options.MaxMathParagraphs} math paragraphs."
                );
            }
            var ordinal = source.GetElementOrdinal(paragraph);
            var semantic = state.NodeFor(partUri, ordinal);
            var wordParagraph = NearestSemanticAncestor(
                semantic,
                state.SemanticDocument,
                WordSemanticNodeKind.Paragraph
            );
            var story = StoryFor(semantic, state.SemanticDocument);
            var id = StableId(
                "wdmp_",
                partUri,
                semantic?.Id.Value ?? ordinal.ToString(CultureInfo.InvariantCulture)
            );
            var properties = paragraph.Elements()
                .Where(element => IsMathElement(element, "oMathParaPr"))
                .ToArray();
            var paragraphHasErrors = false;
            var paragraphHasWarnings = false;
            var paragraphChildren = paragraph.Elements().ToArray();
            if (properties.Length > 1)
            {
                paragraphHasErrors = true;
                state.AddIssue(
                    null,
                    "MATH_PARAGRAPH_PROPERTIES_DUPLICATE",
                    WordEquationIssueSeverity.Error,
                    "Math paragraph contains more than one m:oMathParaPr element.",
                    partUri,
                    ordinal,
                    null
                );
            }
            if (
                properties.Length > 0
                && !ReferenceEquals(paragraphChildren[0], properties[0])
            )
            {
                paragraphHasErrors = true;
                state.AddIssue(
                    null,
                    "MATH_PARAGRAPH_PROPERTY_ORDER_INVALID",
                    WordEquationIssueSeverity.Error,
                    "m:oMathParaPr must precede equations in a math paragraph.",
                    partUri,
                    source.GetElementOrdinal(properties[0]),
                    null
                );
            }
            foreach (
                var unexpected in paragraphChildren.Where(child =>
                    !IsMathElement(child, "oMathParaPr")
                    && !IsMathElement(child, "oMath")
                )
            )
            {
                paragraphHasErrors = true;
                state.AddIssue(
                    null,
                    "MATH_PARAGRAPH_CHILD_INVALID",
                    WordEquationIssueSeverity.Error,
                    $"Math paragraph contains unexpected child {QualifiedName(unexpected)}.",
                    partUri,
                    source.GetElementOrdinal(unexpected),
                    null
                );
            }
            if (HasSignificantDirectText(paragraph))
            {
                paragraphHasErrors = true;
                state.AddIssue(
                    null,
                    "MATH_UNEXPECTED_DIRECT_TEXT",
                    WordEquationIssueSeverity.Error,
                    "Math paragraph contains text outside an m:t element.",
                    partUri,
                    ordinal,
                    null
                );
            }
            string? justification = null;
            var justificationElement = properties.FirstOrDefault()
                ?.Elements()
                .FirstOrDefault(element => IsMathElement(element, "jc"));
            if (justificationElement is not null)
            {
                justification = ReadValue(justificationElement);
                RequirePropertyValueLength(justification);
                paragraphHasWarnings = !IsPropertyValueValid(
                    "justification",
                    justification
                );
                ValidatePropertyValue(
                    "justification",
                    justification,
                    null,
                    state,
                    partUri,
                    source.GetElementOrdinal(justificationElement),
                    null
                );
            }
            if (!paragraph.Ancestors().Any(IsWordParagraph))
            {
                paragraphHasErrors = true;
                state.AddIssue(
                    null,
                    "MATH_PARAGRAPH_OUTSIDE_WORD_PARAGRAPH",
                    WordEquationIssueSeverity.Error,
                    "Word does not open math paragraphs that occur outside w:p.",
                    partUri,
                    ordinal,
                    null
                );
            }
            if (paragraph.Ancestors().Any(IsMathNamespaceElement))
            {
                paragraphHasErrors = true;
                state.AddIssue(
                    null,
                    "MATH_PARAGRAPH_NESTED_IN_MATH",
                    WordEquationIssueSeverity.Error,
                    "Word does not open an m:oMathPara nested in another math element.",
                    partUri,
                    ordinal,
                    null
                );
            }

            var mutable = new MutableMathParagraph(
                id,
                partUri,
                story.Kind,
                story.Node?.Id,
                wordParagraph?.Id,
                ordinal,
                semantic?.SourcePath,
                semantic?.Id,
                justification,
                paragraphHasErrors,
                paragraphHasWarnings
            );
            paragraphByElement.Add(paragraph, mutable);
        }

        var equations = root.DescendantsAndSelf()
            .Where(element => IsMathElement(element, "oMath"))
            .OrderBy(source.GetElementOrdinal)
            .ToArray();
        foreach (var element in equations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++state.EquationCount > _options.MaxEquations)
            {
                throw new WordEquationLimitException(
                    $"Document contains more than {_options.MaxEquations} equations."
                );
            }
            var ordinal = source.GetElementOrdinal(element);
            var semantic = state.NodeFor(partUri, ordinal);
            var id = StableId(
                "wde_",
                partUri,
                semantic?.Id.Value ?? ordinal.ToString(CultureInfo.InvariantCulture)
            );
            var mathParagraphElement = element.Ancestors()
                .FirstOrDefault(ancestor => IsMathElement(ancestor, "oMathPara"));
            MutableMathParagraph? mathParagraph = null;
            if (mathParagraphElement is not null)
            {
                paragraphByElement.TryGetValue(
                    mathParagraphElement,
                    out mathParagraph
                );
            }
            var indexInMathParagraph = mathParagraph?.EquationIds.Count ?? 0;
            var wordParagraph = NearestSemanticAncestor(
                semantic,
                state.SemanticDocument,
                WordSemanticNodeKind.Paragraph
            );
            var story = StoryFor(semantic, state.SemanticDocument);
            var context = new EquationContext(
                id,
                partUri,
                source,
                state,
                cancellationToken
            );
            if (mathParagraph?.HasErrors == true)
            {
                context.AddIssue(
                    "MATH_EQUATION_CONTAINER_INVALID",
                    WordEquationIssueSeverity.Error,
                    "The containing math paragraph is malformed.",
                    ordinal,
                    null
                );
            }
            else if (mathParagraph?.HasWarnings == true)
            {
                context.AddIssue(
                    "MATH_EQUATION_CONTAINER_WARNING",
                    WordEquationIssueSeverity.Warning,
                    "The containing math paragraph has invalid or unsupported properties.",
                    ordinal,
                    null
                );
            }
            if (
                element.Ancestors().Any(ancestor =>
                    IsMathNamespaceElement(ancestor)
                    && !IsMathElement(ancestor, "oMathPara")
                )
            )
            {
                context.AddIssue(
                    "MATH_NESTED_EQUATION",
                    WordEquationIssueSeverity.Error,
                    "Word does not open an m:oMath nested inside another equation.",
                    ordinal,
                    null
                );
            }
            if (!element.Ancestors().Any(IsWordParagraph))
            {
                context.AddIssue(
                    "MATH_EQUATION_OUTSIDE_WORD_PARAGRAPH",
                    WordEquationIssueSeverity.Error,
                    "Word does not open an equation that occurs outside w:p.",
                    ordinal,
                    null
                );
            }
            var rootNode = ParseSequence(
                element,
                "equation",
                null,
                0,
                context
            );
            if (rootNode.Children.Count == 0)
            {
                context.AddIssue(
                    "MATH_EMPTY_EQUATION",
                    WordEquationIssueSeverity.Warning,
                    "Equation contains no mathematical content.",
                    ordinal,
                    rootNode.Id
                );
            }
            var allNodes = rootNode.DescendantsAndSelf().ToArray();
            var status = context.HasErrors
                ? WordEquationStatus.Malformed
                : context.HasUnsupportedContent
                    ? WordEquationStatus.UnsupportedContent
                    : context.HasWarnings
                        ? WordEquationStatus.CompleteWithWarnings
                        : WordEquationStatus.Complete;
            var definition = new WordEquationDefinition(
                id,
                status,
                partUri,
                story.Kind,
                story.Node?.Id,
                wordParagraph?.Id,
                mathParagraph?.Id,
                indexInMathParagraph,
                mathParagraph is not null,
                element.Ancestors().Any(IsDeletedWordContent),
                ordinal,
                semantic?.SourcePath,
                semantic?.Id,
                rootNode,
                context.Text.ToString(),
                context.TextTruncated,
                allNodes.Length,
                allNodes.Max(node => node.Depth),
                allNodes.Count(node =>
                    node.Kind is WordMathNodeKind.Extension
                        or WordMathNodeKind.UnknownMath
                )
            );
            state.Equations.Add(definition);
            mathParagraph?.EquationIds.Add(id);
        }

        foreach (var paragraph in paragraphByElement.Values)
        {
            if (paragraph.EquationIds.Count == 0)
            {
                state.AddIssue(
                    null,
                    "MATH_PARAGRAPH_HAS_NO_EQUATION",
                    WordEquationIssueSeverity.Error,
                    "Math paragraph contains no m:oMath equation.",
                    paragraph.PartUri,
                    paragraph.SourceElementOrdinal,
                    null
                );
            }
            state.MathParagraphs.Add(paragraph.Freeze());
        }

        ReportMathOutsideEquations(root, partUri, source, state);
        ReportAdjacentEquationMerges(root, partUri, source, state);
    }

    private WordMathNode ParseSequence(
        XElement container,
        string role,
        string? parentId,
        int depth,
        EquationContext context
    )
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        RequireDepth(depth);
        var ordinal = context.Source.GetElementOrdinal(container);
        var id = context.ReserveNode(
            WordMathNodeKind.Sequence,
            container,
            role,
            depth
        );
        ReportUnexpectedDirectText(container, context, id);
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (
            var propertyContainer in container.Elements().Where(element =>
                IsMathElement(element, "argPr") || IsMathElement(element, "ctrlPr")
            )
        )
        {
            MergeProperties(
                properties,
                ExtractProperties(propertyContainer, context),
                context,
                ordinal,
                id
            );
        }
        var children = new List<WordMathNode>();
        foreach (var child in container.Elements())
        {
            if (IsMathElement(child, "argPr") || IsMathElement(child, "ctrlPr"))
            {
                continue;
            }
            children.Add(ParseContentElement(child, "content", id, depth + 1, context));
        }
        return context.CreateNode(
            id,
            parentId,
            WordMathNodeKind.Sequence,
            MathSourceName(container),
            role,
            depth,
            ordinal,
            null,
            properties,
            children
        );
    }

    private WordMathNode ParseContentElement(
        XElement element,
        string role,
        string parentId,
        int depth,
        EquationContext context
    )
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        RequireDepth(depth);
        if (IsMathNamespaceElement(element))
        {
            if (element.Name.LocalName == "r")
            {
                return ParseRun(element, role, parentId, depth, context);
            }
            if (ObjectSpecs.TryGetValue(element.Name.LocalName, out var spec))
            {
                return ParseObject(element, role, parentId, depth, spec, context);
            }
            if (element.Name.LocalName == "m")
            {
                return ParseMatrix(element, role, parentId, depth, context);
            }
            if (element.Name.LocalName == "t")
            {
                return ParseText(element, role, parentId, depth, context);
            }
            if (ArgumentElementNames.Contains(element.Name.LocalName))
            {
                context.AddIssue(
                    "MATH_UNEXPECTED_ARGUMENT_CONTAINER",
                    WordEquationIssueSeverity.Error,
                    $"Unexpected m:{element.Name.LocalName} argument container.",
                    context.Source.GetElementOrdinal(element),
                    parentId
                );
                return ParseSequence(
                    element,
                    PropertyKey(element.Name.LocalName),
                    parentId,
                    depth,
                    context
                );
            }
            if (element.Name.LocalName is "oMath" or "oMathPara")
            {
                context.AddIssue(
                    "MATH_NESTED_EQUATION",
                    WordEquationIssueSeverity.Error,
                    $"Nested m:{element.Name.LocalName} is rejected by Microsoft Word.",
                    context.Source.GetElementOrdinal(element),
                    parentId
                );
                return ParseUnknownMath(element, role, parentId, depth, context, true);
            }
            return ParseUnknownMath(element, role, parentId, depth, context, false);
        }
        return ParseForeign(element, role, parentId, depth, context);
    }

    private WordMathNode ParseObject(
        XElement element,
        string role,
        string parentId,
        int depth,
        ObjectSpec spec,
        EquationContext context
    )
    {
        var ordinal = context.Source.GetElementOrdinal(element);
        var id = context.ReserveNode(spec.Kind, element, role, depth);
        ReportUnexpectedDirectText(element, context, id);
        var propertyContainers = element.Elements()
            .Where(child => IsMathElement(child, spec.PropertyContainerName))
            .ToArray();
        if (propertyContainers.Length > 1)
        {
            context.AddIssue(
                "MATH_PROPERTIES_DUPLICATE",
                WordEquationIssueSeverity.Error,
                $"m:{element.Name.LocalName} contains more than one m:{spec.PropertyContainerName}.",
                ordinal,
                id
            );
        }
        var properties = propertyContainers.Length == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : ExtractProperties(propertyContainers[0], context);
        var consumed = new HashSet<XElement>(ReferenceEqualityComparer.Instance);
        foreach (var propertyContainer in propertyContainers)
        {
            consumed.Add(propertyContainer);
        }
        ValidateObjectChildOrder(element, spec, context, id);
        var children = new List<WordMathNode>();
        foreach (var argument in spec.Arguments)
        {
            var matches = element.Elements()
                .Where(child => IsMathElement(child, argument.ElementName))
                .ToArray();
            if (matches.Length < argument.Minimum)
            {
                context.AddIssue(
                    "MATH_REQUIRED_ARGUMENT_MISSING",
                    WordEquationIssueSeverity.Error,
                    $"m:{element.Name.LocalName} requires m:{argument.ElementName}.",
                    ordinal,
                    id
                );
            }
            if (matches.Length > argument.Maximum)
            {
                context.AddIssue(
                    "MATH_ARGUMENT_CARDINALITY",
                    WordEquationIssueSeverity.Error,
                    $"m:{element.Name.LocalName} contains {matches.Length} m:{argument.ElementName} arguments; the maximum is {argument.Maximum}.",
                    ordinal,
                    id
                );
            }
            for (var index = 0; index < matches.Length; index++)
            {
                consumed.Add(matches[index]);
                children.Add(
                    ParseSequence(
                        matches[index],
                        argument.Role,
                        id,
                        depth + 1,
                        context
                    )
                );
            }
            if (argument.Maximum == int.MaxValue)
            {
                properties[$"{argument.Role}_count"] = matches.Length.ToString(
                    CultureInfo.InvariantCulture
                );
            }
        }
        foreach (var child in element.Elements().Where(child => !consumed.Contains(child)))
        {
            context.AddIssue(
                "MATH_UNEXPECTED_OBJECT_CHILD",
                WordEquationIssueSeverity.Error,
                $"m:{element.Name.LocalName} contains unexpected child {QualifiedName(child)}.",
                context.Source.GetElementOrdinal(child),
                id
            );
            children.Add(
                ParseContentElement(child, "unexpected", id, depth + 1, context)
            );
        }
        return context.CreateNode(
            id,
            parentId,
            spec.Kind,
            MathSourceName(element),
            role,
            depth,
            ordinal,
            null,
            properties,
            children
        );
    }

    private WordMathNode ParseMatrix(
        XElement element,
        string role,
        string parentId,
        int depth,
        EquationContext context
    )
    {
        var ordinal = context.Source.GetElementOrdinal(element);
        var id = context.ReserveNode(WordMathNodeKind.Matrix, element, role, depth);
        ReportUnexpectedDirectText(element, context, id);
        var propertiesElements = element.Elements()
            .Where(child => IsMathElement(child, "mPr"))
            .ToArray();
        if (propertiesElements.Length > 1)
        {
            context.AddIssue(
                "MATH_PROPERTIES_DUPLICATE",
                WordEquationIssueSeverity.Error,
                "Matrix contains more than one m:mPr element.",
                ordinal,
                id
            );
        }
        var properties = propertiesElements.Length == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : ExtractProperties(
                propertiesElements[0],
                context,
                new HashSet<string>(new[] { "mcs" }, StringComparer.Ordinal)
            );
        var declaredColumns = propertiesElements.Length == 0
            ? null
            : ParseMatrixColumns(propertiesElements[0], properties, context, id);
        var rows = element.Elements()
            .Where(child => IsMathElement(child, "mr"))
            .ToArray();
        if (rows.Length == 0)
        {
            context.AddIssue(
                "MATH_MATRIX_HAS_NO_ROWS",
                WordEquationIssueSeverity.Error,
                "Matrix must contain at least one m:mr row.",
                ordinal,
                id
            );
        }
        var children = new List<WordMathNode>();
        var widths = new List<int>();
        foreach (var row in rows)
        {
            var parsed = ParseMatrixRow(row, id, depth + 1, context);
            children.Add(parsed);
            widths.Add(parsed.Children.Count);
        }
        foreach (
            var unexpected in element.Elements().Where(child =>
                !IsMathElement(child, "mPr") && !IsMathElement(child, "mr")
            )
        )
        {
            context.AddIssue(
                "MATH_UNEXPECTED_OBJECT_CHILD",
                WordEquationIssueSeverity.Error,
                $"m:m contains unexpected child {QualifiedName(unexpected)}.",
                context.Source.GetElementOrdinal(unexpected),
                id
            );
            children.Add(
                ParseContentElement(unexpected, "unexpected", id, depth + 1, context)
            );
        }
        if (widths.Distinct().Count() > 1)
        {
            context.AddIssue(
                "MATH_MATRIX_RAGGED_ROWS",
                WordEquationIssueSeverity.Warning,
                "Matrix rows contain different numbers of cells.",
                ordinal,
                id
            );
        }
        var inferredColumns = widths.Count == 0 ? 0 : widths.Max();
        if (declaredColumns is not null && widths.Any(width => width != declaredColumns))
        {
            context.AddIssue(
                "MATH_MATRIX_COLUMN_DECLARATION_MISMATCH",
                WordEquationIssueSeverity.Warning,
                $"Matrix declares {declaredColumns} columns, but at least one row has a different width.",
                ordinal,
                id
            );
        }
        properties["row_count"] = rows.Length.ToString(CultureInfo.InvariantCulture);
        properties["inferred_column_count"] = inferredColumns.ToString(
            CultureInfo.InvariantCulture
        );
        return context.CreateNode(
            id,
            parentId,
            WordMathNodeKind.Matrix,
            "m:m",
            role,
            depth,
            ordinal,
            null,
            properties,
            children
        );
    }

    private int? ParseMatrixColumns(
        XElement matrixProperties,
        IDictionary<string, string> properties,
        EquationContext context,
        string nodeId
    )
    {
        var columns = matrixProperties.Elements()
            .FirstOrDefault(child => IsMathElement(child, "mcs"));
        if (columns is null)
        {
            return null;
        }
        long total = 0;
        var groups = new StringBuilder();
        foreach (var column in columns.Elements().Where(child => IsMathElement(child, "mc")))
        {
            var columnProperties = column.Elements()
                .FirstOrDefault(child => IsMathElement(child, "mcPr"));
            var countElement = columnProperties?.Elements()
                .FirstOrDefault(child => IsMathElement(child, "count"));
            var justificationElement = columnProperties?.Elements()
                .FirstOrDefault(child => IsMathElement(child, "mcJc"));
            var countText = countElement is null ? "1" : ReadValue(countElement);
            if (
                !int.TryParse(
                    countText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var count
                )
                || count <= 0
            )
            {
                context.AddIssue(
                    "MATH_MATRIX_COLUMN_COUNT_INVALID",
                    WordEquationIssueSeverity.Error,
                    "Matrix column-group count must be a positive integer.",
                    context.Source.GetElementOrdinal(countElement ?? column),
                    nodeId
                );
                count = 0;
            }
            var justification = justificationElement is null
                ? null
                : ReadValue(justificationElement);
            if (justification is not null)
            {
                ValidatePropertyValue(
                    "matrix_column_justification",
                    justification,
                    context,
                    context.State,
                    context.PartUri,
                    context.Source.GetElementOrdinal(justificationElement!),
                    nodeId
                );
            }
            total += count;
            if (total > int.MaxValue)
            {
                throw new WordEquationLimitException(
                    "Matrix declares more than 2147483647 columns."
                );
            }
            var group = $"{count}:{justification ?? "center"}";
            var separatorLength = groups.Length == 0 ? 0 : 1;
            if (
                groups.Length + separatorLength + group.Length
                > _options.MaxPropertyValueCharacters
            )
            {
                throw new WordEquationLimitException(
                    $"Math property value exceeds {_options.MaxPropertyValueCharacters} characters."
                );
            }
            if (groups.Length > 0)
            {
                groups.Append(',');
            }
            groups.Append(group);
        }
        properties["declared_column_count"] = total.ToString(
            CultureInfo.InvariantCulture
        );
        properties["column_groups"] = groups.ToString();
        return (int)total;
    }

    private WordMathNode ParseMatrixRow(
        XElement element,
        string parentId,
        int depth,
        EquationContext context
    )
    {
        var ordinal = context.Source.GetElementOrdinal(element);
        var id = context.ReserveNode(
            WordMathNodeKind.MatrixRow,
            element,
            "matrix_row",
            depth
        );
        ReportUnexpectedDirectText(element, context, id);
        var children = new List<WordMathNode>();
        foreach (var cell in element.Elements().Where(child => IsMathElement(child, "e")))
        {
            children.Add(ParseMatrixCell(cell, id, depth + 1, context));
        }
        foreach (
            var unexpected in element.Elements().Where(child => !IsMathElement(child, "e"))
        )
        {
            context.AddIssue(
                "MATH_MATRIX_ROW_CHILD_INVALID",
                WordEquationIssueSeverity.Error,
                $"m:mr contains unexpected child {QualifiedName(unexpected)}.",
                context.Source.GetElementOrdinal(unexpected),
                id
            );
            children.Add(
                ParseContentElement(unexpected, "unexpected", id, depth + 1, context)
            );
        }
        if (children.Count == 0)
        {
            context.AddIssue(
                "MATH_MATRIX_ROW_EMPTY",
                WordEquationIssueSeverity.Error,
                "Matrix row contains no cells.",
                ordinal,
                id
            );
        }
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cell_count"] = children.Count.ToString(CultureInfo.InvariantCulture),
        };
        return context.CreateNode(
            id,
            parentId,
            WordMathNodeKind.MatrixRow,
            "m:mr",
            "matrix_row",
            depth,
            ordinal,
            null,
            properties,
            children
        );
    }

    private WordMathNode ParseMatrixCell(
        XElement element,
        string parentId,
        int depth,
        EquationContext context
    )
    {
        var ordinal = context.Source.GetElementOrdinal(element);
        var id = context.ReserveNode(
            WordMathNodeKind.MatrixCell,
            element,
            "matrix_cell",
            depth
        );
        ReportUnexpectedDirectText(element, context, id);
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (
            var propertyContainer in element.Elements().Where(child =>
                IsMathElement(child, "argPr") || IsMathElement(child, "ctrlPr")
            )
        )
        {
            MergeProperties(
                properties,
                ExtractProperties(propertyContainer, context),
                context,
                ordinal,
                id
            );
        }
        var children = element.Elements()
            .Where(child =>
                !IsMathElement(child, "argPr") && !IsMathElement(child, "ctrlPr")
            )
            .Select(child =>
                ParseContentElement(child, "content", id, depth + 1, context)
            )
            .ToArray();
        return context.CreateNode(
            id,
            parentId,
            WordMathNodeKind.MatrixCell,
            "m:e",
            "matrix_cell",
            depth,
            ordinal,
            null,
            properties,
            children
        );
    }

    private WordMathNode ParseRun(
        XElement element,
        string role,
        string parentId,
        int depth,
        EquationContext context
    )
    {
        var ordinal = context.Source.GetElementOrdinal(element);
        var id = context.ReserveNode(WordMathNodeKind.Run, element, role, depth);
        ReportUnexpectedDirectText(element, context, id);
        var mathProperties = element.Elements()
            .Where(child => IsMathElement(child, "rPr"))
            .ToArray();
        if (mathProperties.Length > 1)
        {
            context.AddIssue(
                "MATH_RUN_PROPERTIES_DUPLICATE",
                WordEquationIssueSeverity.Error,
                "Math run contains more than one m:rPr element.",
                ordinal,
                id
            );
        }
        var properties = mathProperties.Length == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : ExtractProperties(mathProperties[0], context);
        if (
            element.Elements().Any(child =>
                IsWordElement(child) && child.Name.LocalName == "rPr"
            )
        )
        {
            properties["word_run_properties_present"] = "true";
        }
        var children = new List<WordMathNode>();
        foreach (var child in element.Elements())
        {
            if (
                IsMathElement(child, "rPr")
                || IsWordElement(child) && child.Name.LocalName == "rPr"
            )
            {
                continue;
            }
            children.Add(ParseContentElement(child, "run_content", id, depth + 1, context));
        }
        if (children.Count == 0)
        {
            context.AddIssue(
                "MATH_RUN_EMPTY",
                WordEquationIssueSeverity.Warning,
                "Math run contains no text or preserved WordprocessingML content.",
                ordinal,
                id
            );
        }
        return context.CreateNode(
            id,
            parentId,
            WordMathNodeKind.Run,
            "m:r",
            role,
            depth,
            ordinal,
            null,
            properties,
            children
        );
    }

    private WordMathNode ParseText(
        XElement element,
        string role,
        string parentId,
        int depth,
        EquationContext context
    )
    {
        var ordinal = context.Source.GetElementOrdinal(element);
        var id = context.ReserveNode(WordMathNodeKind.Text, element, role, depth);
        if (element.HasElements)
        {
            context.AddIssue(
                "MATH_TEXT_HAS_CHILDREN",
                WordEquationIssueSeverity.Error,
                "m:t must be a leaf text element.",
                ordinal,
                id
            );
        }
        var text = element.Value;
        context.AppendText(text, ordinal, id);
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        var space = element.Attributes()
            .FirstOrDefault(attribute =>
                attribute.Name.NamespaceName == "http://www.w3.org/XML/1998/namespace"
                && attribute.Name.LocalName == "space"
            )
            ?.Value;
        if (space is not null)
        {
            properties["xml_space"] = space;
        }
        return context.CreateNode(
            id,
            parentId,
            WordMathNodeKind.Text,
            "m:t",
            role,
            depth,
            ordinal,
            text,
            properties,
            Array.Empty<WordMathNode>()
        );
    }

    private WordMathNode ParseForeign(
        XElement element,
        string role,
        string parentId,
        int depth,
        EquationContext context
    )
    {
        var isWord = IsWordElement(element);
        var kind = isWord
            ? WordMathNodeKind.WordprocessingContainer
            : WordMathNodeKind.Extension;
        var ordinal = context.Source.GetElementOrdinal(element);
        var id = context.ReserveNode(kind, element, role, depth);
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        if (isWord)
        {
            properties["wordprocessingml"] = "true";
            if (
                element.Name.LocalName is "ins" or "del" or "moveFrom" or "moveTo"
            )
            {
                properties["revision_kind"] = PropertyKey(element.Name.LocalName);
            }
        }
        else
        {
            context.HasUnsupportedContent = true;
            properties["namespace_fingerprint"] = ShortFingerprint(
                element.Name.NamespaceName
            );
            context.AddIssue(
                "MATH_EXTENSION_CONTENT_PRESERVED",
                WordEquationIssueSeverity.Warning,
                $"Equation contains extension element {QualifiedName(element)}; it is source-linked but not interpreted.",
                ordinal,
                id
            );
        }
        string? text = null;
        if (
            isWord
            && element.Name.LocalName is "t" or "delText" or "instrText"
            && !element.HasElements
        )
        {
            text = element.Value;
            context.AppendText(text, ordinal, id);
        }
        var children = element.Elements()
            .Select(child =>
                ParseContentElement(child, "wrapped_content", id, depth + 1, context)
            )
            .ToArray();
        return context.CreateNode(
            id,
            parentId,
            kind,
            isWord ? $"w:{element.Name.LocalName}" : $"ext:{element.Name.LocalName}",
            role,
            depth,
            ordinal,
            text,
            properties,
            children
        );
    }

    private WordMathNode ParseUnknownMath(
        XElement element,
        string role,
        string parentId,
        int depth,
        EquationContext context,
        bool alreadyReported
    )
    {
        var ordinal = context.Source.GetElementOrdinal(element);
        var id = context.ReserveNode(
            WordMathNodeKind.UnknownMath,
            element,
            role,
            depth
        );
        context.HasUnsupportedContent = true;
        if (!alreadyReported)
        {
            context.AddIssue(
                "MATH_UNKNOWN_ELEMENT_PRESERVED",
                WordEquationIssueSeverity.Warning,
                $"Unknown OMML element m:{element.Name.LocalName} is source-linked but not interpreted.",
                ordinal,
                id
            );
        }
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!element.HasElements && element.Value.Length > 0)
        {
            properties["scalar_value_present"] = "true";
        }
        var children = element.Elements()
            .Select(child =>
                ParseContentElement(child, "unknown_content", id, depth + 1, context)
            )
            .ToArray();
        return context.CreateNode(
            id,
            parentId,
            WordMathNodeKind.UnknownMath,
            $"m:{element.Name.LocalName}",
            role,
            depth,
            ordinal,
            null,
            properties,
            children
        );
    }

    private Dictionary<string, string> ExtractProperties(
        XElement container,
        EquationContext? context,
        ISet<string>? ignoredChildren = null,
        BuildState? explicitState = null,
        string? explicitPartUri = null,
        LosslessXmlDocument? explicitSource = null
    )
    {
        var state = context?.State ?? explicitState
            ?? throw new ArgumentNullException(nameof(explicitState));
        var partUri = context?.PartUri ?? explicitPartUri
            ?? throw new ArgumentNullException(nameof(explicitPartUri));
        var source = context?.Source ?? explicitSource
            ?? throw new ArgumentNullException(nameof(explicitSource));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (HasSignificantDirectText(container))
        {
            state.AddIssue(
                context,
                "MATH_PROPERTY_TEXT_INVALID",
                WordEquationIssueSeverity.Error,
                "OMML property container contains unexpected direct text.",
                partUri,
                source.GetElementOrdinal(container),
                null
            );
        }
        foreach (var child in container.Elements())
        {
            if (ignoredChildren?.Contains(child.Name.LocalName) == true)
            {
                continue;
            }
            if (IsMathElement(child, "ctrlPr"))
            {
                AddProperty(
                    result,
                    "control_properties_present",
                    "true",
                    context,
                    state,
                    partUri,
                    source.GetElementOrdinal(child),
                    null
                );
                continue;
            }
            if (IsWordElement(child))
            {
                AddProperty(
                    result,
                    "word_properties_present",
                    "true",
                    context,
                    state,
                    partUri,
                    source.GetElementOrdinal(child),
                    null
                );
                continue;
            }
            if (!IsMathNamespaceElement(child))
            {
                context?.MarkUnsupported();
                state.AddIssue(
                    context,
                    "MATH_EXTENSION_PROPERTIES_PRESERVED",
                    WordEquationIssueSeverity.Warning,
                    "An extension property was preserved but is not interpreted.",
                    partUri,
                    source.GetElementOrdinal(child),
                    null
                );
                AddProperty(
                    result,
                    "extension_properties_present",
                    "true",
                    context,
                    state,
                    partUri,
                    source.GetElementOrdinal(child),
                    null
                );
                continue;
            }
            var key = PropertyKey(child.Name.LocalName);
            var value = PropertyValue(child, key);
            ValidatePropertyValue(
                key,
                value,
                context,
                state,
                partUri,
                source.GetElementOrdinal(child),
                null
            );
            AddProperty(
                result,
                key,
                NormalizePropertyValue(key, value),
                context,
                state,
                partUri,
                source.GetElementOrdinal(child),
                null
            );
        }
        return result;
    }

    private void ValidateObjectChildOrder(
        XElement element,
        ObjectSpec spec,
        EquationContext context,
        string nodeId
    )
    {
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [spec.PropertyContainerName] = 0,
        };
        for (var index = 0; index < spec.Arguments.Count; index++)
        {
            ranks[spec.Arguments[index].ElementName] = index + 1;
        }
        var previous = -1;
        foreach (var child in element.Elements().Where(IsMathNamespaceElement))
        {
            if (!ranks.TryGetValue(child.Name.LocalName, out var rank))
            {
                continue;
            }
            if (rank < previous)
            {
                context.AddIssue(
                    "MATH_CHILD_ORDER_INVALID",
                    WordEquationIssueSeverity.Error,
                    $"Children of m:{element.Name.LocalName} do not follow the OMML schema order.",
                    context.Source.GetElementOrdinal(child),
                    nodeId
                );
                return;
            }
            previous = rank;
        }
    }

    private WordMathSettingsDefinition? ParseMathSettings(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        WordSettingsGraph settingsGraph;
        try
        {
            settingsGraph = new WordSettingsGraphBuilder().Build(
                package,
                semanticDocument,
                cancellationToken
            );
        }
        catch (WordSettingsLimitException exception)
        {
            throw new WordEquationLimitException(
                $"Equation defaults exceed a bounded settings limit: {exception.Message}"
            );
        }
        catch (WordSettingsProjectionException)
        {
            state.AddIssue(
                null,
                "MATH_SETTINGS_UNAVAILABLE",
                WordEquationIssueSeverity.Warning,
                "Document math defaults could not be projected from the settings part.",
                semanticDocument.MainPartUri,
                0,
                null
            );
            return null;
        }
        if (settingsGraph.SettingsPartUri is null)
        {
            return null;
        }
        if (!package.Parts.TryGetValue(settingsGraph.SettingsPartUri, out var part))
        {
            throw new WordEquationProjectionException(
                $"Settings part '{settingsGraph.SettingsPartUri}' is missing."
            );
        }
        var source = ParsePart(part, cancellationToken);
        var root = source.ParsedDocument.Root
            ?? throw new WordEquationProjectionException(
                "Word settings part has no root element."
            );
        var mathProperties = root.Elements()
            .Where(element => IsMathElement(element, "mathPr"))
            .ToArray();
        if (mathProperties.Length == 0)
        {
            return null;
        }
        if (mathProperties.Length > 1)
        {
            state.AddIssue(
                null,
                "MATH_SETTINGS_DUPLICATE",
                WordEquationIssueSeverity.Error,
                "Settings part contains more than one m:mathPr element.",
                part.Uri,
                source.GetElementOrdinal(mathProperties[1]),
                null
            );
        }
        var selected = mathProperties[0];
        var properties = ExtractProperties(
            selected,
            null,
            null,
            state,
            part.Uri,
            source
        );
        return new WordMathSettingsDefinition(
            part.Uri,
            source.GetElementOrdinal(selected),
            properties
        );
    }

    private void ReportMathOutsideEquations(
        XElement root,
        string partUri,
        LosslessXmlDocument source,
        BuildState state
    )
    {
        foreach (
            var element in root.DescendantsAndSelf().Where(element =>
                IsMathNamespaceElement(element)
                && (element.Name.LocalName == "r"
                    || element.Name.LocalName == "m"
                    || ObjectSpecs.ContainsKey(element.Name.LocalName))
                && !element.Ancestors().Any(ancestor => IsMathElement(ancestor, "oMath"))
            )
        )
        {
            state.AddIssue(
                null,
                "MATH_CONTENT_OUTSIDE_EQUATION",
                WordEquationIssueSeverity.Error,
                $"Math object m:{element.Name.LocalName} occurs outside m:oMath; Word rejects this structure.",
                partUri,
                source.GetElementOrdinal(element),
                null
            );
        }
    }

    private void ReportAdjacentEquationMerges(
        XElement root,
        string partUri,
        LosslessXmlDocument source,
        BuildState state
    )
    {
        foreach (var parent in root.DescendantsAndSelf())
        {
            XElement? previous = null;
            foreach (var child in parent.Elements())
            {
                if (
                    previous is not null
                    && IsMathElement(previous, "oMath")
                    && IsMathElement(child, "oMath")
                )
                {
                    state.AddIssue(
                        null,
                        "MATH_ADJACENT_EQUATIONS_MERGED_BY_WORD",
                        WordEquationIssueSeverity.Warning,
                        "Microsoft Word merges adjacent m:oMath elements that are not separated by a WordprocessingML break.",
                        partUri,
                        source.GetElementOrdinal(child),
                        null
                    );
                }
                if (
                    previous is not null
                    && IsMathElement(previous, "oMathPara")
                    && IsMathElement(child, "oMathPara")
                )
                {
                    state.AddIssue(
                        null,
                        "MATH_ADJACENT_PARAGRAPHS_MERGED_BY_WORD",
                        WordEquationIssueSeverity.Warning,
                        "Microsoft Word merges adjacent m:oMathPara elements within one w:p.",
                        partUri,
                        source.GetElementOrdinal(child),
                        null
                    );
                }
                previous = child;
            }
        }
    }

    private static WordSemanticNode? NearestSemanticAncestor(
        WordSemanticNode? node,
        WordSemanticDocument document,
        WordSemanticNodeKind kind
    )
    {
        var current = node;
        while (current is not null)
        {
            if (current.Kind == kind)
            {
                return current;
            }
            current = current.ParentId is { } parentId
                && document.TryGetNode(parentId, out var parent)
                    ? parent
                    : null;
        }
        return null;
    }

    private static StoryLocation StoryFor(
        WordSemanticNode? node,
        WordSemanticDocument document
    )
    {
        var current = node;
        while (current is not null)
        {
            var kind = current.Kind switch
            {
                WordSemanticNodeKind.TextBox => WordStoryKind.TextBox,
                WordSemanticNodeKind.Footnote => WordStoryKind.Footnote,
                WordSemanticNodeKind.Endnote => WordStoryKind.Endnote,
                WordSemanticNodeKind.Comment => WordStoryKind.Comment,
                WordSemanticNodeKind.GlossaryEntry => WordStoryKind.GlossaryEntry,
                WordSemanticNodeKind.Header => WordStoryKind.Header,
                WordSemanticNodeKind.Footer => WordStoryKind.Footer,
                WordSemanticNodeKind.Document => WordStoryKind.Main,
                _ => (WordStoryKind?)null,
            };
            if (kind is not null)
            {
                return new StoryLocation(kind.Value, current);
            }
            current = current.ParentId is { } parentId
                && document.TryGetNode(parentId, out var parent)
                    ? parent
                    : null;
        }
        return new StoryLocation(WordStoryKind.Other, null);
    }

    private static string PropertyValue(XElement element, string key)
    {
        if (key == "alignment_at")
        {
            return element.Attributes()
                    .FirstOrDefault(attribute => attribute.Name.LocalName == "alnAt")
                    ?.Value
                ?? "true";
        }
        var value = ReadValue(element);
        if (value.Length > 0)
        {
            return value;
        }
        var attributes = element.Attributes()
            .Where(attribute => !attribute.IsNamespaceDeclaration)
            .OrderBy(attribute => attribute.Name.NamespaceName, StringComparer.Ordinal)
            .ThenBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal)
            .Select(attribute => $"{attribute.Name.LocalName}={attribute.Value}")
            .ToArray();
        return attributes.Length == 0 ? "true" : string.Join(";", attributes);
    }

    private static string ReadValue(XElement element) =>
        element.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == "val")
            ?.Value
        ?? (element.HasElements ? string.Empty : element.Value);

    private static string NormalizePropertyValue(string key, string value)
    {
        if (!BooleanPropertyNames.Contains(key))
        {
            return value;
        }
        return value switch
        {
            "true" or "on" or "1" => "true",
            "false" or "off" or "0" => "false",
            _ => value,
        };
    }

    private static void ValidatePropertyValue(
        string key,
        string value,
        EquationContext? context,
        BuildState state,
        string partUri,
        int ordinal,
        string? nodeId
    )
    {
        if (!IsPropertyValueValid(key, value))
        {
            state.AddIssue(
                context,
                "MATH_PROPERTY_VALUE_INVALID",
                WordEquationIssueSeverity.Warning,
                $"OMML property '{key}' has an unsupported value.",
                partUri,
                ordinal,
                nodeId
            );
        }
    }

    private static bool IsPropertyValueValid(string key, string value)
    {
        if (BooleanPropertyNames.Contains(key))
        {
            return value is "true" or "false" or "on" or "off" or "0" or "1";
        }
        if (IntegerPropertyNames.Contains(key))
        {
            return long.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out _
            );
        }
        return !EnumValues.TryGetValue(key, out var values) || values.Contains(value);
    }

    private void AddProperty(
        IDictionary<string, string> properties,
        string key,
        string value,
        EquationContext? context,
        BuildState state,
        string partUri,
        int ordinal,
        string? nodeId
    )
    {
        RequirePropertyValueLength(value);
        if (properties.Count >= _options.MaxPropertiesPerNode)
        {
            throw new WordEquationLimitException(
                $"Math node contains more than {_options.MaxPropertiesPerNode} properties."
            );
        }
        if (!properties.TryAdd(key, value))
        {
            state.AddIssue(
                context,
                "MATH_PROPERTY_DUPLICATE",
                WordEquationIssueSeverity.Warning,
                $"OMML property '{key}' occurs more than once.",
                partUri,
                ordinal,
                nodeId
            );
            var suffix = 2;
            while (!properties.TryAdd($"{key}_{suffix}", value))
            {
                suffix++;
            }
        }
    }

    private void RequirePropertyValueLength(string value)
    {
        if (value.Length > _options.MaxPropertyValueCharacters)
        {
            throw new WordEquationLimitException(
                $"Math property value exceeds {_options.MaxPropertyValueCharacters} characters."
            );
        }
    }

    private void MergeProperties(
        IDictionary<string, string> target,
        IReadOnlyDictionary<string, string> source,
        EquationContext context,
        int ordinal,
        string nodeId
    )
    {
        foreach (var (key, value) in source)
        {
            AddProperty(
                target,
                key,
                value,
                context,
                context.State,
                context.PartUri,
                ordinal,
                nodeId
            );
        }
    }

    private void RequireDepth(int depth)
    {
        if (depth > _options.MaxDepth)
        {
            throw new WordEquationLimitException(
                $"Equation nesting exceeds {_options.MaxDepth} levels."
            );
        }
    }

    private static void ReportUnexpectedDirectText(
        XElement element,
        EquationContext context,
        string nodeId
    )
    {
        if (!HasSignificantDirectText(element))
        {
            return;
        }
        context.AddIssue(
            "MATH_UNEXPECTED_DIRECT_TEXT",
            WordEquationIssueSeverity.Error,
            $"{MathSourceName(element)} contains text outside an m:t element.",
            context.Source.GetElementOrdinal(element),
            nodeId
        );
    }

    private static bool HasSignificantDirectText(XElement element) =>
        element.Nodes().OfType<XText>().Any(text =>
            !string.IsNullOrWhiteSpace(text.Value)
        );

    private static string PropertyKey(string localName) => localName switch
    {
        "aln" => "align",
        "alnAt" => "alignment_at",
        "alnScr" => "align_scripts",
        "argSz" => "argument_size",
        "baseJc" => "base_justification",
        "begChr" => "begin_character",
        "brk" => "alignment_at",
        "brkBin" => "break_binary",
        "brkBinSub" => "break_binary_subtraction",
        "cGp" => "column_gap",
        "cGpRule" => "column_gap_rule",
        "chr" => "character",
        "cSp" => "column_spacing",
        "defJc" => "default_justification",
        "degHide" => "degree_hidden",
        "diff" => "differential",
        "dispDef" => "display_defaults",
        "endChr" => "end_character",
        "hideBot" => "hide_bottom",
        "hideLeft" => "hide_left",
        "hideRight" => "hide_right",
        "hideTop" => "hide_top",
        "interSp" => "inter_spacing",
        "intLim" => "integral_limit_location",
        "intraSp" => "intra_spacing",
        "jc" => "justification",
        "lMargin" => "left_margin",
        "limLoc" => "limit_location",
        "lit" => "literal",
        "mathFont" => "math_font",
        "maxDist" => "maximum_distribution",
        "mcJc" => "matrix_column_justification",
        "naryLim" => "nary_limit_location",
        "noBreak" => "no_break",
        "nor" => "normal_text",
        "objDist" => "object_distribution",
        "opEmu" => "operator_emulator",
        "plcHide" => "hide_placeholder",
        "pos" => "position",
        "postSp" => "post_spacing",
        "preSp" => "pre_spacing",
        "rMargin" => "right_margin",
        "rSp" => "row_spacing",
        "rSpRule" => "row_spacing_rule",
        "scr" => "script",
        "sepChr" => "separator_character",
        "show" => "show_phantom",
        "shp" => "shape",
        "smallFrac" => "small_fraction",
        "strikeBLTR" => "strike_bltr",
        "strikeH" => "strike_horizontal",
        "strikeTLBR" => "strike_tlbr",
        "strikeV" => "strike_vertical",
        "sty" => "style",
        "subHide" => "subscript_hidden",
        "supHide" => "superscript_hidden",
        "transp" => "transparent",
        "type" => "fraction_type",
        "vertJc" => "vertical_justification",
        "wrapIndent" => "wrap_indent",
        "wrapRight" => "wrap_right",
        "zeroAsc" => "zero_ascent",
        "zeroDesc" => "zero_descent",
        "zeroWid" => "zero_width",
        _ => ToSnakeCase(localName),
    };

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (
                char.IsUpper(character)
                && index > 0
                && (char.IsLower(value[index - 1])
                    || index + 1 < value.Length && char.IsLower(value[index + 1]))
            )
            {
                builder.Append('_');
            }
            builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }

    private static bool IsMathNamespaceElement(XElement element) =>
        element.Name.NamespaceName is MathTransitionalNamespace or MathStrictNamespace;

    private static bool IsMathElement(XElement element, string localName) =>
        IsMathNamespaceElement(element) && element.Name.LocalName == localName;

    private static bool IsWordElement(XElement element) =>
        element.Name.NamespaceName is WordTransitionalNamespace or WordStrictNamespace;

    private static bool IsWordParagraph(XElement element) =>
        IsWordElement(element) && element.Name.LocalName == "p";

    private static bool IsDeletedWordContent(XElement element) =>
        IsWordElement(element) && element.Name.LocalName is "del" or "moveFrom";

    private static string MathSourceName(XElement element) =>
        $"m:{element.Name.LocalName}";

    private static string QualifiedName(XElement element) =>
        IsMathNamespaceElement(element)
            ? $"m:{element.Name.LocalName}"
            : IsWordElement(element)
                ? $"w:{element.Name.LocalName}"
                : $"ext:{element.Name.LocalName}";

    private static string StableId(string prefix, params string[] values)
    {
        var material = string.Join('\u001f', values);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var encoded = Convert.ToBase64String(digest.AsSpan(0, 15))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return prefix + encoded;
    }

    private static string ShortFingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant()[..16];

    private static string Bound(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    private static HashSet<string> Set(params string[] values) =>
        new(values, StringComparer.Ordinal);

    private sealed record ArgumentSpec(
        string ElementName,
        string Role,
        int Minimum,
        int Maximum
    );

    private sealed record ObjectSpec(
        WordMathNodeKind Kind,
        string PropertyContainerName,
        params ArgumentSpec[] ArgumentArray
    )
    {
        public IReadOnlyList<ArgumentSpec> Arguments { get; } = ArgumentArray;
    }

    private sealed record StoryLocation(WordStoryKind Kind, WordSemanticNode? Node);

    private sealed class MutableMathParagraph
    {
        internal MutableMathParagraph(
            string id,
            string partUri,
            WordStoryKind storyKind,
            SemanticNodeId? storyNodeId,
            SemanticNodeId? paragraphNodeId,
            int sourceElementOrdinal,
            string? sourcePath,
            SemanticNodeId? semanticNodeId,
            string? justification,
            bool hasErrors,
            bool hasWarnings
        )
        {
            Id = id;
            PartUri = partUri;
            StoryKind = storyKind;
            StoryNodeId = storyNodeId;
            ParagraphNodeId = paragraphNodeId;
            SourceElementOrdinal = sourceElementOrdinal;
            SourcePath = sourcePath;
            SemanticNodeId = semanticNodeId;
            Justification = justification;
            HasErrors = hasErrors;
            HasWarnings = hasWarnings;
        }

        internal string Id { get; }

        internal string PartUri { get; }

        internal WordStoryKind StoryKind { get; }

        internal SemanticNodeId? StoryNodeId { get; }

        internal SemanticNodeId? ParagraphNodeId { get; }

        internal int SourceElementOrdinal { get; }

        internal string? SourcePath { get; }

        internal SemanticNodeId? SemanticNodeId { get; }

        internal string? Justification { get; }

        internal bool HasErrors { get; }

        internal bool HasWarnings { get; }

        internal List<string> EquationIds { get; } = new();

        internal WordMathParagraphDefinition Freeze() => new(
            Id,
            PartUri,
            StoryKind,
            StoryNodeId,
            ParagraphNodeId,
            SourceElementOrdinal,
            SourcePath,
            SemanticNodeId,
            Justification,
            EquationIds.ToArray()
        );
    }

    private sealed class EquationContext
    {
        internal EquationContext(
            string equationId,
            string partUri,
            LosslessXmlDocument source,
            BuildState state,
            CancellationToken cancellationToken
        )
        {
            EquationId = equationId;
            PartUri = partUri;
            Source = source;
            State = state;
            CancellationToken = cancellationToken;
        }

        internal string EquationId { get; }

        internal string PartUri { get; }

        internal LosslessXmlDocument Source { get; }

        internal BuildState State { get; }

        internal CancellationToken CancellationToken { get; }

        internal StringBuilder Text { get; } = new();

        internal bool TextTruncated { get; private set; }

        internal bool HasErrors { get; private set; }

        internal bool HasWarnings { get; private set; }

        internal bool HasUnsupportedContent { get; set; }

        internal void MarkUnsupported() => HasUnsupportedContent = true;

        internal string ReserveNode(
            WordMathNodeKind kind,
            XElement element,
            string role,
            int depth
        )
        {
            if (++State.NodeCount > State.Options.MaxNodes)
            {
                throw new WordEquationLimitException(
                    $"Equation graph contains more than {State.Options.MaxNodes} nodes."
                );
            }
            var ordinal = Source.GetElementOrdinal(element);
            var semanticNodeId = State.NodeFor(PartUri, ordinal)?.Id.Value;
            return StableId(
                "wdmn_",
                EquationId,
                semanticNodeId
                    ?? ordinal.ToString(CultureInfo.InvariantCulture),
                kind.ToString(),
                role
            );
        }

        internal WordMathNode CreateNode(
            string id,
            string? parentId,
            WordMathNodeKind kind,
            string sourceName,
            string role,
            int depth,
            int ordinal,
            string? text,
            IReadOnlyDictionary<string, string> properties,
            IReadOnlyList<WordMathNode> children
        ) => new(
            id,
            parentId,
            kind,
            sourceName,
            role,
            depth,
            PartUri,
            ordinal,
            State.NodeFor(PartUri, ordinal)?.Id,
            text,
            properties,
            children
        );

        internal void AppendText(string value, int ordinal, string nodeId)
        {
            if (value.Length > State.Options.MaxTextCharactersPerNode)
            {
                throw new WordEquationLimitException(
                    $"Math text node exceeds {State.Options.MaxTextCharactersPerNode} characters."
                );
            }
            checked
            {
                State.TotalTextCharacters += value.Length;
            }
            if (State.TotalTextCharacters > State.Options.MaxTotalTextCharacters)
            {
                throw new WordEquationLimitException(
                    $"Equation text exceeds {State.Options.MaxTotalTextCharacters} total characters."
                );
            }
            var remaining = State.Options.MaxTextCharactersPerEquation - Text.Length;
            if (remaining > 0)
            {
                Text.Append(value.AsSpan(0, Math.Min(value.Length, remaining)));
            }
            if (value.Length > remaining && !TextTruncated)
            {
                TextTruncated = true;
                AddIssue(
                    "MATH_EQUATION_TEXT_CAPTURE_TRUNCATED",
                    WordEquationIssueSeverity.Warning,
                    $"Equation text capture is limited to {State.Options.MaxTextCharactersPerEquation} characters.",
                    ordinal,
                    nodeId
                );
            }
        }

        internal void AddIssue(
            string code,
            WordEquationIssueSeverity severity,
            string message,
            int ordinal,
            string? nodeId
        ) => State.AddIssue(
            this,
            code,
            severity,
            message,
            PartUri,
            ordinal,
            nodeId
        );

        internal void MarkSeverity(WordEquationIssueSeverity severity)
        {
            HasErrors |= severity == WordEquationIssueSeverity.Error;
            HasWarnings |= severity == WordEquationIssueSeverity.Warning;
        }
    }

    private sealed class BuildState
    {
        private readonly IReadOnlyDictionary<(string PartUri, int Ordinal), WordSemanticNode>
            _nodesBySource;

        internal BuildState(
            WordEquationGraphOptions options,
            WordSemanticDocument semanticDocument
        )
        {
            Options = options;
            SemanticDocument = semanticDocument;
            _nodesBySource = semanticDocument.Nodes.ToDictionary(node =>
                (node.SourcePartUri, node.SourceElementOrdinal)
            );
        }

        internal WordEquationGraphOptions Options { get; }

        internal WordSemanticDocument SemanticDocument { get; }

        internal List<WordEquationDefinition> Equations { get; } = new();

        internal List<WordMathParagraphDefinition> MathParagraphs { get; } = new();

        internal List<WordEquationIssue> Issues { get; } = new();

        internal WordMathSettingsDefinition? Settings { get; set; }

        internal int EquationCount { get; set; }

        internal int MathParagraphCount { get; set; }

        internal int NodeCount { get; set; }

        internal long TotalTextCharacters { get; set; }

        internal bool IssuesTruncated { get; private set; }

        internal WordSemanticNode? NodeFor(string partUri, int ordinal) =>
            _nodesBySource.TryGetValue((partUri, ordinal), out var node) ? node : null;

        internal void AddIssue(
            EquationContext? context,
            string code,
            WordEquationIssueSeverity severity,
            string message,
            string partUri,
            int ordinal,
            string? nodeId
        )
        {
            context?.MarkSeverity(severity);
            if (Issues.Count >= Options.MaxIssues)
            {
                IssuesTruncated = true;
                return;
            }
            Issues.Add(
                new WordEquationIssue(
                    code,
                    severity,
                    message,
                    partUri,
                    ordinal,
                    context?.EquationId,
                    nodeId
                )
            );
        }
    }
}
