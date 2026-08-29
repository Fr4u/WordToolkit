using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Equations;

internal sealed record DirectOmmlEquationPlan(
    string SourceOmml,
    string InsertXml,
    string NamespaceIdentity,
    string SemanticSha256,
    string EquationSemanticSha256,
    string LinearSemantic,
    int ElementCount,
    string? ParagraphPropertiesOmml = null,
    string? ParagraphPropertiesSemanticSha256 = null,
    string? ParagraphJustification = null
);

internal static class DirectOmmlEquationParser
{
    private const string TransitionalMathNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string StrictMathNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/math";
    private const string TransitionalWordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string StrictWordNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const int MaximumCharacters = 100_000;
    private const int MaximumElements = 10_000;
    private const int MaximumDepth = 64;

    private static readonly HashSet<string> ActiveOrExternalElementNames = new(
        new[]
        {
            "altChunk",
            "AlternateContent",
            "object",
            "oleObject",
            "OLEObject",
            "control",
            "pict",
            "drawing",
            "hyperlink",
            "subDoc",
            "txbxContent",
        },
        StringComparer.Ordinal
    );

    internal static DirectOmmlEquationPlan Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length == 0 || string.IsNullOrWhiteSpace(source))
        {
            throw Invalid("Direct OMML input is empty");
        }
        if (source.Length > MaximumCharacters)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                $"Direct OMML input exceeds {MaximumCharacters:N0} characters"
            );
        }
        EquationFormattingMarkers.RejectReservedInput(source, "omml");

        var document = ParseSecurely(source, MaximumCharacters);
        var root = document.Root
            ?? throw Invalid("Direct OMML input has no root element");
        var mathNamespace = RequireMathNamespace(root.Name.NamespaceName);
        var paragraphProperties = root.Name.LocalName == "oMathPara"
            ? root.Elements().FirstOrDefault(element =>
                element.Name.NamespaceName == mathNamespace
                && element.Name.LocalName == "oMathParaPr")
            : null;
        ValidateParagraphProperties(paragraphProperties, mathNamespace);
        var math = SelectSingleEquation(root, mathNamespace);
        var elements = math.DescendantsAndSelf()
            .Concat(paragraphProperties?.DescendantsAndSelf() ?? [])
            .Take(MaximumElements + 1)
            .ToArray();
        if (elements.Length > MaximumElements)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                $"Direct OMML exceeds {MaximumElements:N0} elements"
            );
        }
        ValidateTree(math, mathNamespace, depth: 0);

        var sourceOmml = Serialize(math);
        var conversion = MathMarkupToUnicodeMath.ConvertPlan(sourceOmml, "omml");
        var equationContract = new StringBuilder();
        AppendSemanticElement(equationContract, math, mathNamespace);
        var equationSemanticSha256 = Sha256(equationContract.ToString());
        var semanticContract = BuildSemanticContract(math, mathNamespace, paragraphProperties);
        var semanticSha256 = Sha256(semanticContract);
        string? paragraphPropertiesSemanticSha256 = null;
        string? paragraphJustification = null;
        if (paragraphProperties is not null)
        {
            var paragraphContract = new StringBuilder();
            AppendSemanticElement(
                paragraphContract,
                paragraphProperties,
                mathNamespace
            );
            paragraphPropertiesSemanticSha256 = Sha256(paragraphContract.ToString());
            var justification = paragraphProperties.Elements().FirstOrDefault(element =>
                element.Name.NamespaceName == mathNamespace
                && element.Name.LocalName == "jc"
            );
            paragraphJustification = justification?.Attribute(
                XName.Get("val", mathNamespace)
            )?.Value;
        }
        var namespaceIdentity = mathNamespace == StrictMathNamespace
            ? "strict"
            : "transitional";
        var insertXml = BuildInsertXml(sourceOmml, mathNamespace);
        return new DirectOmmlEquationPlan(
            sourceOmml,
            insertXml,
            namespaceIdentity,
            semanticSha256,
            equationSemanticSha256,
            conversion.Linear,
            elements.Length,
            paragraphProperties is null ? null : Serialize(paragraphProperties),
            paragraphPropertiesSemanticSha256,
            paragraphJustification
        );
    }

    private static void ValidateParagraphProperties(
        XElement? paragraphProperties,
        string mathNamespace
    )
    {
        if (paragraphProperties is null)
        {
            return;
        }
        var children = paragraphProperties.Elements().ToArray();
        if (
            children.Length != 1
            || children[0].Name.NamespaceName != mathNamespace
            || children[0].Name.LocalName != "jc"
            || children[0].Elements().Any()
            || children[0].Nodes().OfType<XText>().Any(text =>
                !string.IsNullOrWhiteSpace(text.Value)
            )
        )
        {
            throw Invalid(
                "Direct m:oMathParaPr must contain exactly one m:jc property"
            );
        }
        var attributes = children[0].Attributes()
            .Where(attribute => !attribute.IsNamespaceDeclaration)
            .ToArray();
        var value = attributes.Length == 1
            && attributes[0].Name == XName.Get("val", mathNamespace)
            ? attributes[0].Value
            : "";
        if (value is not ("left" or "right" or "center" or "centerGroup"))
        {
            throw Invalid(
                "Direct m:oMathParaPr justification must be left, right, center, or centerGroup"
            );
        }
        ValidateTree(paragraphProperties, mathNamespace, depth: 0);
    }

    internal static DirectOmmlEquationPlan ParseWordReadback(string wordOpenXml)
    {
        ArgumentNullException.ThrowIfNull(wordOpenXml);
        if (wordOpenXml.Length == 0 || wordOpenXml.Length > 8_000_000)
        {
            throw Invalid("Microsoft Word returned an empty or oversized OMML readback");
        }
        var document = ParseSecurely(
            wordOpenXml,
            8_000_000,
            allowWordGeneratedMetadata: true
        );
        var equations = document.Descendants()
            .Where(element =>
                element.Name.LocalName == "oMath"
                && IsMathNamespace(element.Name.NamespaceName)
                && !element.Ancestors().Any(ancestor =>
                    ancestor.Name.LocalName == "oMath"
                    && IsMathNamespace(ancestor.Name.NamespaceName)
                )
            )
            .Take(2)
            .ToArray();
        if (equations.Length != 1)
        {
            throw Invalid(
                "Microsoft Word did not return exactly one direct OMML equation",
                new { equation_count = equations.Length }
            );
        }
        var paragraph = equations[0].Ancestors().FirstOrDefault(ancestor =>
            ancestor.Name.LocalName == "oMathPara" && IsMathNamespace(ancestor.Name.NamespaceName));
        return Parse((paragraph ?? equations[0]).ToString(SaveOptions.DisableFormatting));
    }

    internal static string BuildWordInsertXml(
        string wordTemplate,
        DirectOmmlEquationPlan plan
    )
    {
        ArgumentNullException.ThrowIfNull(wordTemplate);
        ArgumentNullException.ThrowIfNull(plan);
        if (wordTemplate.Length == 0 || wordTemplate.Length > 8_000_000)
        {
            throw Invalid("Microsoft Word returned an empty or oversized XML template");
        }
        var template = ParseSecurely(
            wordTemplate,
            8_000_000,
            allowWordGeneratedMetadata: true
        );
        var equations = template.Descendants()
            .Where(element =>
                element.Name.LocalName == "oMath"
                && IsMathNamespace(element.Name.NamespaceName)
                && !element.Ancestors().Any(ancestor =>
                    ancestor.Name.LocalName == "oMath"
                    && IsMathNamespace(ancestor.Name.NamespaceName)
                )
            )
            .Take(2)
            .ToArray();
        if (equations.Length != 1)
        {
            throw Invalid(
                "Microsoft Word XML template does not contain exactly one equation",
                new { equation_count = equations.Length }
            );
        }
        var targetMathNamespace = equations[0].Name.NamespaceName;
        var source = ParseSecurely(plan.SourceOmml, MaximumCharacters).Root
            ?? throw Invalid("Direct OMML source has no root element");
        var normalized = NormalizeNamespaces(
            source,
            targetMathNamespace,
            WordNamespaceFor(targetMathNamespace)
        );
        equations[0].ReplaceWith(normalized);
        if (plan.ParagraphPropertiesOmml is not null)
        {
            var targetParagraph = template.Descendants().FirstOrDefault(element =>
                element.Name.NamespaceName == targetMathNamespace
                && element.Name.LocalName == "oMathPara");
            if (targetParagraph is null)
            {
                throw Invalid(
                    "Microsoft Word XML template does not expose m:oMathPara for the requested paragraph properties"
                );
            }
            var sourceProperties = ParseSecurely(
                plan.ParagraphPropertiesOmml,
                MaximumCharacters
            ).Root ?? throw Invalid("Direct OMML paragraph properties are malformed");
            var normalizedProperties = NormalizeNamespaces(
                sourceProperties,
                targetMathNamespace,
                WordNamespaceFor(targetMathNamespace)
            );
            targetParagraph.Elements().Where(element =>
                element.Name.NamespaceName == targetMathNamespace
                && element.Name.LocalName == "oMathParaPr").Remove();
            targetParagraph.AddFirst(normalizedProperties);
        }
        return template.ToString(SaveOptions.DisableFormatting);
    }

    private static XDocument ParseSecurely(
        string source,
        long maximumCharacters,
        bool allowWordGeneratedMetadata = false
    )
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = allowWordGeneratedMetadata,
                IgnoreProcessingInstructions = allowWordGeneratedMetadata,
                IgnoreWhitespace = false,
                MaxCharactersInDocument = maximumCharacters,
                MaxCharactersFromEntities = 0,
            };
            using var textReader = new StringReader(source);
            using var reader = XmlReader.Create(textReader, settings);
            var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            if (
                !allowWordGeneratedMetadata
                &&
                document.DescendantNodes().Any(node =>
                    node is XComment or XProcessingInstruction or XDocumentType
                )
            )
            {
                throw Invalid(
                    "Direct OMML cannot contain comments, processing instructions, or a document type"
                );
            }
            return document;
        }
        catch (NativeToolException)
        {
            throw;
        }
        catch (XmlException exception)
        {
            throw Invalid(
                "Direct OMML is malformed or contains prohibited XML",
                new
                {
                    exception = exception.GetType().Name,
                    line = exception.LineNumber,
                    position = exception.LinePosition,
                }
            );
        }
    }

    private static string RequireMathNamespace(string value)
    {
        if (!IsMathNamespace(value))
        {
            throw Invalid(
                "Direct OMML root must use the Transitional or Strict Office Math namespace"
            );
        }
        return value;
    }

    private static XElement SelectSingleEquation(XElement root, string mathNamespace)
    {
        if (root.Name.NamespaceName != mathNamespace)
        {
            throw Invalid("Direct OMML root mixes Office Math namespaces");
        }
        if (root.Name.LocalName == "oMath")
        {
            if (
                root.Descendants().Any(element =>
                    element.Name.LocalName is "oMath" or "oMathPara"
                    && IsMathNamespace(element.Name.NamespaceName)
                )
            )
            {
                throw Invalid("Direct OMML cannot nest another equation root");
            }
            return root;
        }
        if (root.Name.LocalName != "oMathPara")
        {
            throw Invalid("Direct OMML root must be m:oMath or m:oMathPara");
        }

        var children = root.Elements().ToArray();
        var equations = children
            .Where(element =>
                element.Name.NamespaceName == mathNamespace
                && element.Name.LocalName == "oMath"
            )
            .ToArray();
        if (equations.Length != 1)
        {
            throw Invalid(
                "m:oMathPara must contain exactly one direct m:oMath child",
                new { equation_count = equations.Length }
            );
        }
        if (
            children.Any(element =>
                element != equations[0]
                && !(
                    element.Name.NamespaceName == mathNamespace
                    && element.Name.LocalName == "oMathParaPr"
                )
            )
            || children.Count(element => element.Name.LocalName == "oMathParaPr") > 1
        )
        {
            throw Invalid("m:oMathPara contains an unsupported child");
        }
        return equations[0];
    }

    private static void ValidateTree(
        XElement element,
        string mathNamespace,
        int depth
    )
    {
        if (depth > MaximumDepth)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                $"Direct OMML exceeds the maximum depth of {MaximumDepth}"
            );
        }
        if (ActiveOrExternalElementNames.Contains(element.Name.LocalName))
        {
            throw Invalid(
                "Direct OMML contains active, external, or drawing content",
                new { element = element.Name.LocalName }
            );
        }

        if (element.Name.NamespaceName == mathNamespace)
        {
            if (
                depth > 0
                && element.Name.LocalName is "oMath" or "oMathPara"
            )
            {
                throw Invalid("Direct OMML contains a nested equation root");
            }
            foreach (var attribute in element.Attributes().Where(item =>
                !item.IsNamespaceDeclaration
            ))
            {
                if (
                    attribute.Name.NamespaceName.Length > 0
                    && attribute.Name.NamespaceName != mathNamespace
                )
                {
                    throw Invalid(
                        "Direct OMML contains an attribute from an unsupported namespace",
                        new { attribute = attribute.Name.LocalName }
                    );
                }
            }
            foreach (var node in element.Nodes())
            {
                if (node is XText text)
                {
                    if (
                        !string.IsNullOrWhiteSpace(text.Value)
                        && element.Name.LocalName != "t"
                    )
                    {
                        throw Invalid(
                            "Direct OMML contains text outside m:t",
                            new { element = element.Name.LocalName }
                        );
                    }
                    continue;
                }
                if (node is not XElement child)
                {
                    throw Invalid("Direct OMML contains an unsupported XML node");
                }
                ValidateTree(child, mathNamespace, depth + 1);
            }
            return;
        }

        if (!IsAllowedWordFormattingElement(element, mathNamespace))
        {
            throw Invalid(
                "Direct OMML contains an element from an unsupported namespace",
                new { element = element.Name.LocalName }
            );
        }
        var wordNamespace = element.Name.NamespaceName;
        foreach (var descendant in element.DescendantsAndSelf())
        {
            if (descendant.Name.NamespaceName != wordNamespace)
            {
                throw Invalid("Direct OMML formatting mixes namespaces");
            }
            if (ActiveOrExternalElementNames.Contains(descendant.Name.LocalName))
            {
                throw Invalid(
                    "Direct OMML formatting contains active or external content",
                    new { element = descendant.Name.LocalName }
                );
            }
            foreach (var attribute in descendant.Attributes().Where(item =>
                !item.IsNamespaceDeclaration
            ))
            {
                if (
                    attribute.Name.NamespaceName.Length > 0
                    && attribute.Name.NamespaceName != wordNamespace
                )
                {
                    throw Invalid(
                        "Direct OMML formatting contains an unsupported attribute namespace",
                        new { attribute = attribute.Name.LocalName }
                    );
                }
            }
        }
    }

    private static bool IsAllowedWordFormattingElement(
        XElement element,
        string mathNamespace
    )
    {
        var wordNamespace = WordNamespaceFor(mathNamespace);
        if (
            element.Name.NamespaceName != wordNamespace
            || element.Name.LocalName != "rPr"
        )
        {
            return element.Ancestors().Any(ancestor =>
                ancestor.Name.NamespaceName == wordNamespace
                && ancestor.Name.LocalName == "rPr"
                && IsAllowedWordFormattingRoot(ancestor, mathNamespace)
            );
        }
        return IsAllowedWordFormattingRoot(element, mathNamespace);
    }

    private static bool IsAllowedWordFormattingRoot(
        XElement element,
        string mathNamespace
    )
    {
        var parent = element.Parent;
        return parent is not null
            && parent.Name.NamespaceName == mathNamespace
            && parent.Name.LocalName is "r" or "ctrlPr";
    }

    private static string BuildSemanticContract(
        XElement math,
        string mathNamespace,
        XElement? paragraphProperties = null
    )
    {
        var output = new StringBuilder();
        if (paragraphProperties is not null)
        {
            AppendSemanticElement(output, paragraphProperties, mathNamespace);
        }
        AppendSemanticElement(output, math, mathNamespace);
        return output.ToString();
    }

    private static void AppendSemanticElement(
        StringBuilder output,
        XElement element,
        string mathNamespace
    )
    {
        var namespaceKind = element.Name.NamespaceName == mathNamespace ? "m" : "w";
        AppendToken(output, "E", namespaceKind + ":" + element.Name.LocalName);
        foreach (
            var attribute in element.Attributes()
                .Where(item => !item.IsNamespaceDeclaration)
                .OrderBy(item => item.Name.NamespaceName, StringComparer.Ordinal)
                .ThenBy(item => item.Name.LocalName, StringComparer.Ordinal)
        )
        {
            var attributeNamespace = attribute.Name.NamespaceName.Length == 0
                ? ""
                : attribute.Name.NamespaceName == mathNamespace
                    ? "m"
                    : "w";
            AppendToken(
                output,
                "A",
                attributeNamespace + ":" + attribute.Name.LocalName + "=" + attribute.Value
            );
        }
        IEnumerable<XNode> nodes = element.Nodes()
            .Where(node => node is not XElement child
                || !ShouldOmitSemanticElement(child, mathNamespace));
        if (
            element.Name.LocalName.EndsWith("Pr", StringComparison.Ordinal)
            && !element.Nodes().OfType<XText>().Any(text =>
                !string.IsNullOrWhiteSpace(text.Value)
            )
        )
        {
            nodes = element.Elements()
                .Where(child => !ShouldOmitSemanticElement(child, mathNamespace))
                .OrderBy(child => child.Name.NamespaceName, StringComparer.Ordinal)
                .ThenBy(child => child.Name.LocalName, StringComparer.Ordinal);
        }
        foreach (var node in nodes)
        {
            if (node is XElement child)
            {
                AppendSemanticElement(output, child, mathNamespace);
            }
            else if (
                node is XText text
                && (
                    element.Name.LocalName == "t"
                    || !string.IsNullOrWhiteSpace(text.Value)
                )
            )
            {
                AppendToken(output, "T", text.Value);
            }
        }
        AppendToken(output, "X", namespaceKind + ":" + element.Name.LocalName);
    }

    private static bool ShouldOmitSemanticElement(
        XElement element,
        string mathNamespace
    )
    {
        // Word injects font/run defaults into every equation readback. They are
        // presentation metadata, not the mathematical object contract.
        if (element.Name.NamespaceName != mathNamespace)
        {
            return true;
        }
        if (IsDefaultMathRunProperty(element, mathNamespace))
        {
            return true;
        }
        if (element.Name.LocalName == "ctrlPr")
        {
            return true;
        }
        if (!element.Name.LocalName.EndsWith("Pr", StringComparison.Ordinal))
        {
            return false;
        }
        var hasMeaningfulAttribute = element.Attributes().Any(attribute =>
            !attribute.IsNamespaceDeclaration
        );
        var hasMeaningfulText = element.Nodes().OfType<XText>().Any(text =>
            !string.IsNullOrWhiteSpace(text.Value)
        );
        var hasMeaningfulChild = element.Elements().Any(child =>
            !ShouldOmitSemanticElement(child, mathNamespace)
        );
        return !hasMeaningfulAttribute && !hasMeaningfulText && !hasMeaningfulChild;
    }

    private static bool IsDefaultMathRunProperty(
        XElement element,
        string mathNamespace
    )
    {
        if (
            element.Parent is not { } parent
            || parent.Name.NamespaceName != mathNamespace
            || parent.Name.LocalName != "rPr"
            || element.Elements().Any()
            || element.Nodes().OfType<XText>().Any(text =>
                !string.IsNullOrWhiteSpace(text.Value)
            )
        )
        {
            return false;
        }
        var attributes = element.Attributes()
            .Where(attribute => !attribute.IsNamespaceDeclaration)
            .ToArray();
        if (
            attributes.Length != 1
            || attributes[0].Name != XName.Get("val", mathNamespace)
        )
        {
            return false;
        }
        return (element.Name.LocalName, attributes[0].Value) is
            ("sty", "i") or ("scr", "roman");
    }

    private static void AppendToken(StringBuilder output, string kind, string value)
    {
        output.Append(kind)
            .Append(value.Length)
            .Append(':')
            .Append(value)
            .Append(';');
    }

    private static string BuildInsertXml(string sourceOmml, string mathNamespace)
    {
        var wordNamespace = WordNamespaceFor(mathNamespace);
        return $"<w:document xmlns:w=\"{wordNamespace}\" xmlns:m=\"{mathNamespace}\"><w:body><w:p>{sourceOmml}</w:p></w:body></w:document>";
    }

    private static XElement NormalizeNamespaces(
        XElement source,
        string targetMathNamespace,
        string targetWordNamespace
    )
    {
        var sourceMathNamespace = source.Name.NamespaceName;
        var sourceWordNamespace = WordNamespaceFor(sourceMathNamespace);
        XElement Clone(XElement element)
        {
            var elementNamespace = element.Name.NamespaceName switch
            {
                var value when value == sourceMathNamespace => targetMathNamespace,
                var value when value == sourceWordNamespace => targetWordNamespace,
                _ => element.Name.NamespaceName,
            };
            var clone = new XElement(XName.Get(element.Name.LocalName, elementNamespace));
            foreach (var attribute in element.Attributes().Where(item =>
                !item.IsNamespaceDeclaration
            ))
            {
                var attributeNamespace = attribute.Name.NamespaceName switch
                {
                    var value when value == sourceMathNamespace => targetMathNamespace,
                    var value when value == sourceWordNamespace => targetWordNamespace,
                    _ => attribute.Name.NamespaceName,
                };
                clone.Add(
                    new XAttribute(
                        XName.Get(attribute.Name.LocalName, attributeNamespace),
                        attribute.Value
                    )
                );
            }
            foreach (var node in element.Nodes())
            {
                clone.Add(
                    node is XElement child
                        ? Clone(child)
                        : node is XText text
                            ? new XText(text.Value)
                            : throw Invalid(
                                "Direct OMML source contains an unsupported XML node"
                            )
                );
            }
            return clone;
        }
        return Clone(source);
    }

    private static string Serialize(XElement element) =>
        element.ToString(SaveOptions.DisableFormatting);

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static bool IsMathNamespace(string value) =>
        value is TransitionalMathNamespace or StrictMathNamespace;

    private static string WordNamespaceFor(string mathNamespace) =>
        mathNamespace == StrictMathNamespace
            ? StrictWordNamespace
            : TransitionalWordNamespace;

    private static NativeToolException Invalid(string message, object? details = null) =>
        new("EQUATION_INVALID", message, details);
}
