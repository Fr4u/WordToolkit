using System.Text;
using System.Xml;
using System.Xml.Linq;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Equations;

internal static class MathMarkupToUnicodeMath
{
    private const char NaryBodySeparator = '▒';
    private const char MatrixMarker = '■';
    private const char EquationArrayMarker = '█';
    private const char BelowMarker = '┬';
    private const char AboveMarker = '┴';
    private const char PhantomMarker = '⟡';

    private static readonly HashSet<string> NaryOperators =
    [
        "∑",
        "∏",
        "∐",
        "∫",
        "∬",
        "∭",
        "∮",
        "∱",
        "∲",
        "∳",
        "∯",
        "∰",
        "⋃",
        "⋂",
    ];

    private const string MathMlNamespace = "http://www.w3.org/1998/Math/MathML";
    private const string OfficeMathNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string OfficeMathStrictNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/math";
    private const string WordprocessingNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordprocessingStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const int MaximumDepth = 64;
    private const int MaximumElements = 10_000;
    private const int MaximumOutputLength = 100_000;

    private static readonly IReadOnlyDictionary<string, string> OmmlControlProperties =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["acc"] = "accPr",
            ["bar"] = "barPr",
            ["borderBox"] = "borderBoxPr",
            ["box"] = "boxPr",
            ["d"] = "dPr",
            ["eqArr"] = "eqArrPr",
            ["f"] = "fPr",
            ["func"] = "funcPr",
            ["groupChr"] = "groupChrPr",
            ["limLow"] = "limLowPr",
            ["limUpp"] = "limUppPr",
            ["m"] = "mPr",
            ["nary"] = "naryPr",
            ["phant"] = "phantPr",
            ["rad"] = "radPr",
            ["sPre"] = "sPrePr",
            ["sSub"] = "sSubPr",
            ["sSubSup"] = "sSubSupPr",
            ["sSup"] = "sSupPr",
        };

    public static string Convert(string source, string inputFormat) =>
        ConvertPlan(source, inputFormat).Linear;

    internal static EquationConversionPlan ConvertPlan(string source, string inputFormat)
    {
        if (source.Length is < 1 or > MaximumOutputLength)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "MathML or OMML length must be between 1 and 100,000 characters"
            );
        }
        EquationFormattingMarkers.RejectReservedInput(source, inputFormat);

        var root = ParseSecurely(source);
        var elements = root.DescendantsAndSelf().Take(MaximumElements + 1).Count();
        if (elements > MaximumElements)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                $"Equation markup exceeds {MaximumElements:N0} XML elements"
            );
        }

        var result = inputFormat switch
        {
            "mathml" => ConvertMathMl(root),
            "omml" => ConvertOmml(root),
            _ => throw new NativeToolException(
                "EQUATION_INVALID",
                "Markup conversion requires MathML or OMML input"
            ),
        };
        result = WordLinearMathNormalizer.NormalizeForWord(result.Trim());
        if (result.Length == 0)
        {
            throw new NativeToolException(
                "EQUATION_INVALID",
                "Equation markup did not contain a mathematical expression"
            );
        }
        if (result.Length > MaximumOutputLength)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "Converted Word linear math exceeds 100,000 characters"
            );
        }
        return EquationFormattingMarkers.FromMarkedLinear(result);
    }

    private static XElement ParseSecurely(string source)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                MaxCharactersInDocument = 1_000_000,
            };
            using var textReader = new StringReader(source);
            using var reader = XmlReader.Create(textReader, settings);
            return XDocument.Load(reader, LoadOptions.None).Root
                ?? throw new NativeToolException(
                    "EQUATION_INVALID",
                    "Equation XML has no document element"
                );
        }
        catch (NativeToolException)
        {
            throw;
        }
        catch (XmlException exception)
        {
            throw new NativeToolException(
                "EQUATION_INVALID",
                "Equation XML is malformed or contains a prohibited construct",
                new { line = exception.LineNumber, position = exception.LinePosition }
            );
        }
    }

    private static string ConvertMathMl(XElement root)
    {
        var rootNamespace = root.Name.NamespaceName;
        if (
            root.Name.LocalName != "math"
            || (rootNamespace.Length > 0 && rootNamespace != MathMlNamespace)
        )
        {
            throw new NativeToolException(
                "EQUATION_INVALID",
                "MathML root must be a math element in the MathML namespace"
            );
        }
        foreach (var element in root.DescendantsAndSelf())
        {
            var namespaceName = element.Name.NamespaceName;
            if (namespaceName.Length > 0 && namespaceName != MathMlNamespace)
            {
                throw new NativeToolException(
                    "EQUATION_INVALID",
                    "MathML contains an element from an unsupported namespace",
                    new { element = element.Name.LocalName, namespace_name = namespaceName }
                );
            }
        }
        AnnotateMathMlVariants(root);
        return MathMlNode(root, 0);
    }

    private static string MathMlNode(XElement element, int depth)
    {
        RequireDepth(depth);
        var children = element.Elements().ToArray();
        return element.Name.LocalName switch
        {
            "math" or "mrow" or "mstyle" =>
                MathMlSequence(children, depth + 1),
            "mpadded" => ConvertMathMlPadded(element, children, depth),
            "mphantom" => $"{PhantomMarker}({MathMlSequence(children, depth + 1)})",
            "semantics" => MathMlSemantics(children, depth + 1),
            "mspace" => ConvertMathMlSpace(element, children),
            "annotation" or "annotation-xml" or "none" or "maligngroup"
                or "malignmark" => "",
            "mi" => ConvertMathMlIdentifier(element),
            "mn" => ConvertMathMlToken(element, isIdentifier: false, asText: false),
            "mo" => ConvertMathMlToken(element, isIdentifier: false, asText: false),
            "mtext" => ConvertMathMlToken(element, isIdentifier: false, asText: true),
            // <ms> is the MathML string-literal token.  Word linear math has
            // no distinct string token, so preserve it as quoted text (the
            // same representation used by mtext/OMML nor runs).
            "ms" => $"\"{CleanQuotedText(element.Value)}\"",
            "mfrac" => BinaryMathMl(
                children,
                depth,
                (left, right) => ConvertMathMlFraction(element, left, right),
                "mfrac"
            ),
            "msup" => BinaryMathMl(
                children,
                depth,
                (left, right) => $"{ParenthesizeBase(left)}^({right})",
                "msup"
            ),
            "msub" => BinaryMathMl(
                children,
                depth,
                (left, right) => $"{ParenthesizeBase(left)}_({right})",
                "msub"
            ),
            "msubsup" => TernaryMathMl(
                children,
                depth,
                (basis, subscript, superscript) =>
                    $"{ParenthesizeBase(basis)}_({subscript})^({superscript})",
                "msubsup"
            ),
            "mmultiscripts" => ConvertMathMlMultiscripts(element, children, depth),
            "msqrt" => $"√({MathMlSequence(children, depth + 1)})",
            "mroot" => BinaryMathMl(
                children,
                depth,
                (radicand, degree) => $"√({degree}&{radicand})",
                "mroot"
            ),
            "munder" => ConvertMathMlUnder(element, children, depth),
            "mover" => ConvertMathMlOver(children, depth),
            "munderover" => TernaryMathMl(
                children,
                depth,
                (basis, lower, upper) =>
                    $"{ParenthesizeBase(basis)}_({lower})^({upper})",
                "munderover"
            ),
            "mfenced" => ConvertMathMlFence(element, children, depth),
            "mtable" => ConvertMathMlTable(element, children, depth),
            "maction" => ConvertMathMlAction(element, children, depth),
            "menclose" => ConvertMathMlEnclose(element, children, depth),
            _ => throw new NativeToolException(
                "EQUATION_INVALID",
                $"Unsupported MathML element: {element.Name.LocalName}"
            ),
        };
    }

    private static string ConvertMathMlFraction(XElement element, string left, string right)
    {
        var thickness = element.Attribute("linethickness")?.Value.Trim().ToLowerInvariant();
        if (thickness is "0" or "0px" or "none")
        {
            return $"({left})¦({right})";
        }
        return $"({left})/({right})";
    }

    private static string ConvertMathMlUnder(XElement element, XElement[] children, int depth)
    {
        if (children.Length != 2) throw Arity("munder", 2, children.Length);
        var basis = MathMlNode(children[0], depth + 1);
        var lower = MathMlNode(children[1], depth + 1);
        var accentUnder = element.Attribute("accentunder")?.Value.Trim().ToLowerInvariant() == "true";
        if (accentUnder)
        {
            return ApplyUnderAccent(basis, lower);
        }
        return $"{ParenthesizeBase(basis)}_({lower})";
    }

    private static string ConvertMathMlAction(XElement element, XElement[] children, int depth)
    {
        if (children.Length == 0) throw Arity("maction", 1, 0);
        var selection = element.Attribute("selection")?.Value ?? "1";
        if (!int.TryParse(selection, out var selected) || selected < 1 || selected > children.Length)
            throw new NativeToolException("EQUATION_INVALID", "MathML maction selection must identify an existing child");
        return MathMlNode(children[selected - 1], depth + 1);
    }

    private static string ConvertMathMlEnclose(XElement element, XElement[] children, int depth)
    {
        var notation = element.Attribute("notation")?.Value.Trim().ToLowerInvariant() ?? "longdiv";
        if (notation == "radical")
        {
            return $"√({MathMlSequence(children, depth + 1)})";
        }
        if (notation == "box")
        {
            return $"▭({MathMlSequence(children, depth + 1)})";
        }
        if (notation.Contains(' ', StringComparison.Ordinal))
        {
            throw new NativeToolException("EQUATION_INVALID", "MathML menclose combines notations that Word linear OMath cannot preserve", new { notation });
        }
        if (notation is not ("box" or "radical"))
            throw new NativeToolException("EQUATION_INVALID", "MathML menclose notation is not losslessly representable", new { notation });
        throw new NativeToolException(
            "EQUATION_INVALID",
            "MathML menclose notation is not losslessly representable",
            new { notation }
        );
    }

    private static string ConvertMathMlPadded(
        XElement element,
        XElement[] children,
        int depth
    )
    {
        var layoutAttributes = new[] { "width", "height", "depth", "lspace", "voffset" }
            .Where(name => element.Attribute(name) is not null)
            .ToArray();
        if (layoutAttributes.Length > 0)
        {
            throw new NativeToolException(
                "EQUATION_INVALID",
                "MathML mpadded layout offsets cannot be represented losslessly by Word linear OMath",
                new { attributes = layoutAttributes }
            );
        }
        return MathMlSequence(children, depth + 1);
    }

    private static string ConvertMathMlSpace(XElement element, XElement[] children)
    {
        if (children.Length != 0)
        {
            throw Arity("mspace", 0, children.Length);
        }
        var width = element.Attribute("width")?.Value.Trim() ?? "0";
        var height = element.Attribute("height")?.Value.Trim() ?? "0";
        var depth = element.Attribute("depth")?.Value.Trim() ?? "0";
        if (IsZeroMathLength(width) && IsZeroMathLength(height) && IsZeroMathLength(depth))
        {
            return "";
        }
        throw new NativeToolException(
            "EQUATION_INVALID",
            "Non-zero MathML mspace geometry cannot be represented losslessly by Word linear OMath",
            new { width, height, depth }
        );
    }

    private static bool IsZeroMathLength(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "0" or "0px" or "0pt" or "0em" or "0ex" or "0%";
    }

    private static string ConvertMathMlMultiscripts(
        XElement element,
        XElement[] children,
        int depth
    )
    {
        if (children.Length < 1)
        {
            throw Arity("mmultiscripts", 1, children.Length);
        }
        var baseValue = MathMlNode(children[0], depth + 1);
        var result = baseValue;
        var index = 1;
        while (index < children.Length && children[index].Name.LocalName != "mprescripts")
        {
            if (index + 1 >= children.Length)
                throw Arity("mmultiscripts", 3, children.Length);
            var sub = MathMlNode(children[index], depth + 1);
            var sup = MathMlNode(children[index + 1], depth + 1);
            result = ApplyPostScripts(result, sub, sup);
            index += 2;
        }
        if (index < children.Length)
        {
            index++;
            var prefix = new List<(string Sub, string Sup)>();
            while (index < children.Length)
            {
                if (children[index].Name.LocalName == "mprescripts")
                {
                    throw new NativeToolException(
                        "EQUATION_INVALID",
                        "MathML mmultiscripts contains more than one mprescripts marker"
                    );
                }
                if (index + 1 >= children.Length)
                    throw Arity("mmultiscripts", 3, children.Length);
                prefix.Add((MathMlNode(children[index], depth + 1), MathMlNode(children[index + 1], depth + 1)));
                index += 2;
            }
            for (var pair = prefix.Count - 1; pair >= 0; pair--)
                result = ApplyPreScripts(result, prefix[pair].Sub, prefix[pair].Sup);
        }
        _ = element;
        return result;
    }

    private static string ApplyPostScripts(string basis, string sub, string sup)
    {
        if (sub.Length == 0 && sup.Length == 0)
        {
            return basis;
        }
        var builder = new StringBuilder(ParenthesizeBase(basis));
        if (sub.Length > 0) builder.Append("_(").Append(sub).Append(')');
        if (sup.Length > 0) builder.Append("^(").Append(sup).Append(')');
        return builder.ToString();
    }

    private static string ApplyPreScripts(string basis, string sub, string sup)
    {
        if (sub.Length == 0 && sup.Length == 0)
        {
            return basis;
        }
        var builder = new StringBuilder();
        if (sub.Length > 0) builder.Append("_(").Append(sub).Append(')');
        if (sup.Length > 0) builder.Append("^(").Append(sup).Append(')');
        return builder.Append(ParenthesizeBase(basis)).ToString();
    }

    private static string CleanQuotedText(string value) =>
        value.Replace("\"", "\"\"", StringComparison.Ordinal).Trim();

    private static string MathMlSemantics(XElement[] children, int depth)
    {
        var expression = children.FirstOrDefault(
            child => child.Name.LocalName is not ("annotation" or "annotation-xml")
        );
        return expression is null ? "" : MathMlNode(expression, depth);
    }

    private static string ConvertMathMlIdentifier(XElement element)
    {
        var value = CleanLeaf(element.Value);
        var variant = element.Annotation<MathMlVariantDirective>();
        if (value == "d" && variant?.Name == "normal")
        {
            return WordLinearMathNormalizer.DifferentialD.ToString();
        }
        return ApplyMathMlVariant(element, value, isIdentifier: true, asText: false);
    }

    private static string ConvertMathMlToken(
        XElement element,
        bool isIdentifier,
        bool asText
    ) =>
        ApplyMathMlVariant(
            element,
            CleanLeaf(element.Value),
            isIdentifier,
            asText
        );

    private static string ApplyMathMlVariant(
        XElement element,
        string value,
        bool isIdentifier,
        bool asText
    )
    {
        if (value.Length == 0)
        {
            return "";
        }
        var directive = element.Annotation<MathMlVariantDirective>();
        if (directive is null)
        {
            var rendered = asText ? WordText(value) : value;
            return isIdentifier && value.EnumerateRunes().Count() != 1
                ? EquationFormattingMarkers.Wrap(
                    EquationMathStyle.Plain,
                    EquationStyleTarget.RunsOnly,
                    rendered
                )
                : rendered;
        }

        var plan = MathMlVariantPlan(directive.Name);
        var converted = plan.Alphabet is null
            ? value
            : MathAlphabetMapper.Apply(value, plan.Alphabet.Value);
        var linear = asText ? WordText(converted) : converted;
        return EquationFormattingMarkers.Wrap(
            plan.Style,
            EquationStyleTarget.RunsAndControls,
            linear
        );
    }

    private static void AnnotateMathMlVariants(XElement root) =>
        AnnotateMathMlVariants(root, inheritedVariant: null);

    private static void AnnotateMathMlVariants(
        XElement element,
        string? inheritedVariant
    )
    {
        var name = element.Name.LocalName;
        var attribute = element.Attribute("mathvariant");
        if (
            attribute is not null
            && name is not ("math" or "mstyle" or "mi" or "mn" or "mo" or "mtext" or "ms")
        )
        {
            throw new NativeToolException(
                "EQUATION_INVALID",
                "MathML mathvariant is present on an unsupported element",
                new { element = name }
            );
        }

        var own = attribute is null ? null : NormalizeMathMlVariant(attribute.Value);
        var descendantDefault = name is "math" or "mstyle"
            ? own ?? inheritedVariant
            : inheritedVariant;
        if (name is "mi" or "mn" or "mo" or "mtext")
        {
            var resolved = own ?? inheritedVariant;
            if (resolved is not null)
            {
                element.AddAnnotation(new MathMlVariantDirective(resolved));
            }
        }
        foreach (var child in element.Elements())
        {
            AnnotateMathMlVariants(child, descendantDefault);
        }
    }

    private static string NormalizeMathMlVariant(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        _ = MathMlVariantPlan(normalized);
        return normalized;
    }

    private static MathMlStylePlan MathMlVariantPlan(string variant) =>
        variant switch
        {
            "normal" => new(null, EquationMathStyle.Plain),
            "bold" => new(null, EquationMathStyle.Bold),
            "italic" => new(null, EquationMathStyle.Italic),
            "bold-italic" => new(null, EquationMathStyle.BoldItalic),
            "double-struck" => new(MathAlphabetStyle.DoubleStruck, EquationMathStyle.Plain),
            "script" => new(MathAlphabetStyle.Script, EquationMathStyle.Plain),
            "bold-script" => new(MathAlphabetStyle.Script, EquationMathStyle.Bold),
            "fraktur" => new(MathAlphabetStyle.Fraktur, EquationMathStyle.Plain),
            "bold-fraktur" => new(MathAlphabetStyle.Fraktur, EquationMathStyle.Bold),
            "sans-serif" => new(MathAlphabetStyle.SansSerif, EquationMathStyle.Plain),
            "bold-sans-serif" => new(MathAlphabetStyle.SansSerif, EquationMathStyle.Bold),
            "sans-serif-italic" => new(MathAlphabetStyle.SansSerif, EquationMathStyle.Italic),
            "sans-serif-bold-italic" =>
                new(MathAlphabetStyle.SansSerif, EquationMathStyle.BoldItalic),
            "monospace" => new(MathAlphabetStyle.Monospace, EquationMathStyle.Plain),
            _ => throw new NativeToolException(
                "EQUATION_INVALID",
                "MathML mathvariant cannot be represented losslessly by the native Word equation engine",
                new { mathvariant = variant }
            ),
        };

    private static string MathMlSequence(IEnumerable<XElement> children, int depth)
    {
        var builder = new StringBuilder();
        foreach (var child in children)
        {
            builder.Append(MathMlNode(child, depth));
            if (IsMathMlNaryExpression(child))
            {
                builder.Append(NaryBodySeparator);
            }
        }
        return builder.ToString();
    }

    private static bool IsMathMlNaryExpression(XElement element)
    {
        var name = element.Name.LocalName;
        if (name == "mo")
        {
            return NaryOperators.Contains(CleanLeaf(element.Value));
        }
        if (
            name
                is "msub"
                    or "msup"
                    or "msubsup"
                    or "munder"
                    or "mover"
                    or "munderover"
        )
        {
            var basis = element.Elements().FirstOrDefault();
            return basis is not null && IsMathMlNaryExpression(basis);
        }
        return false;
    }

    private static string BinaryMathMl(
        XElement[] children,
        int depth,
        Func<string, string, string> builder,
        string elementName
    )
    {
        if (children.Length != 2)
        {
            throw Arity(elementName, 2, children.Length);
        }
        return builder(
            MathMlNode(children[0], depth + 1),
            MathMlNode(children[1], depth + 1)
        );
    }

    private static string TernaryMathMl(
        XElement[] children,
        int depth,
        Func<string, string, string, string> builder,
        string elementName
    )
    {
        if (children.Length != 3)
        {
            throw Arity(elementName, 3, children.Length);
        }
        return builder(
            MathMlNode(children[0], depth + 1),
            MathMlNode(children[1], depth + 1),
            MathMlNode(children[2], depth + 1)
        );
    }

    private static string ConvertMathMlOver(XElement[] children, int depth)
    {
        if (children.Length != 2)
        {
            throw Arity("mover", 2, children.Length);
        }
        var basis = MathMlNode(children[0], depth + 1);
        var accent = MathMlNode(children[1], depth + 1);
        var function = accent switch
        {
            "¯" or "‾" => "bar",
            "→" => "vec",
            "^" or "ˆ" => "hat",
            "~" or "˜" => "tilde",
            "˙" or "·" => "dot",
            "¨" => "ddot",
            _ => "",
        };
        return function.Length > 0
            ? ApplyAccent(basis, function)
            : $"{ParenthesizeBase(basis)}^({accent})";
    }

    private static string ConvertMathMlFence(
        XElement element,
        XElement[] children,
        int depth
    )
    {
        var open = element.Attribute("open")?.Value ?? "(";
        var close = element.Attribute("close")?.Value ?? ")";
        var separator = element.Attribute("separators")?.Value.FirstOrDefault() ?? ',';
        var body = string.Join(
            separator,
            children.Select(child => MathMlNode(child, depth + 1))
        );
        return $"{CleanLeaf(open)}{body}{CleanLeaf(close)}";
    }

    private static string ConvertMathMlTable(
        XElement element,
        XElement[] children,
        int depth
    )
    {
        var rows = new List<string>();
        foreach (var row in children)
        {
            if (row.Name.LocalName == "mlabeledtr")
                throw new NativeToolException("EQUATION_INVALID", "MathML mlabeledtr labels cannot be preserved in Word linear math");
            if (row.Name.LocalName is not "mtr")
            {
                throw new NativeToolException(
                    "EQUATION_INVALID",
                    $"Unsupported MathML table child: {row.Name.LocalName}"
                );
            }
            var cells = row.Elements()
                .Where(cell => cell.Name.LocalName == "mtd")
                .Select(cell => MathMlSequence(cell.Elements(), depth + 2))
                .ToArray();
            if (cells.Length == 0)
            {
                throw new NativeToolException(
                    "EQUATION_INVALID",
                    "Every MathML table row must contain at least one mtd cell"
                );
            }
            rows.Add(string.Join("&", cells));
        }
        if (rows.Count == 0)
        {
            throw new NativeToolException(
                "EQUATION_INVALID",
                "MathML mtable must contain at least one row"
            );
        }
        var function = string.Equals(
            element.Attribute("columnalign")?.Value,
            "left",
            StringComparison.OrdinalIgnoreCase
        )
            ? EquationArrayMarker.ToString()
            : MatrixMarker.ToString();
        return $"{function}({string.Join("@", rows)})";
    }

    private static string ConvertOmml(XElement root)
    {
        if (
            !IsOfficeMathNamespace(root.Name.NamespaceName)
            || root.Name.LocalName is not ("oMath" or "oMathPara")
        )
        {
            throw new NativeToolException(
                "EQUATION_INVALID",
                "OMML root must be m:oMath or m:oMathPara"
            );
        }
        foreach (var element in root.DescendantsAndSelf())
        {
            if (
                IsOfficeMathNamespace(element.Name.NamespaceName)
                && element.Name.NamespaceName != root.Name.NamespaceName
            )
            {
                throw new NativeToolException(
                    "EQUATION_INVALID",
                    "OMML mixes Transitional and Strict Office Math namespaces",
                    new { element = element.Name.LocalName }
                );
            }
            if (
                !IsOfficeMathNamespace(element.Name.NamespaceName)
                && !IsAllowedWordFormattingElement(element)
            )
            {
                throw new NativeToolException(
                    "EQUATION_INVALID",
                    "OMML contains an element from an unsupported namespace",
                    new { element = element.Name.LocalName, namespace_name = element.Name.NamespaceName }
                );
            }
            if (
                IsWordprocessingNamespace(element.Name.NamespaceName)
                && element.Name.NamespaceName
                    != (root.Name.NamespaceName == OfficeMathStrictNamespace
                        ? WordprocessingStrictNamespace
                        : WordprocessingNamespace)
            )
            {
                throw new NativeToolException(
                    "EQUATION_INVALID",
                    "OMML mixes incompatible Office Math and WordprocessingML namespaces",
                    new { element = element.Name.LocalName }
                );
            }
        }
        if (root.Name.LocalName == "oMathPara")
        {
            root = Child(root, "oMath")
                ?? throw new NativeToolException(
                    "EQUATION_INVALID",
                    "m:oMathPara must contain m:oMath"
                );
        }
        return OmmlSequence(root.Elements(), 0);
    }

    private static string OmmlSequence(
        IEnumerable<XElement> elements,
        int depth,
        bool normalizeDifferential = false
    )
    {
        RequireDepth(depth);
        return string.Concat(
            elements.Select(element =>
                OmmlNode(element, depth + 1, normalizeDifferential)
            )
        );
    }

    private static string OmmlNode(
        XElement element,
        int depth,
        bool normalizeDifferential = false
    )
    {
        RequireDepth(depth);
        var result = element.Name.LocalName switch
        {
            "oMath" or "oMathPara" or "e" or "num" or "den" or "sub" or "sup"
                or "deg" or "fName" or "lim" => OmmlSequence(
                    element.Elements(),
                    depth,
                    normalizeDifferential
                ),
            "r" => ConvertOmmlRun(element, normalizeDifferential),
            "f" => ConvertOmmlFraction(element, depth),
            "sSup" => $"{ParenthesizeBase(OmmlContainer(element, "e", depth))}^({OmmlContainer(element, "sup", depth)})",
            "sSub" => $"{ParenthesizeBase(OmmlContainer(element, "e", depth))}_({OmmlContainer(element, "sub", depth)})",
            "sSubSup" =>
                $"{ParenthesizeBase(OmmlContainer(element, "e", depth))}_({OmmlContainer(element, "sub", depth)})^({OmmlContainer(element, "sup", depth)})",
            "sPre" => ConvertOmmlPreScript(element, depth),
            "rad" => ConvertOmmlRadical(element, depth),
            "nary" => ConvertOmmlNary(element, depth),
            "d" => ConvertOmmlDelimiter(element, depth),
            "m" => ConvertOmmlMatrix(element, depth),
            "eqArr" =>
                $"{EquationArrayMarker}({string.Join("@", Children(element, "e").Select(item => OmmlSequence(item.Elements(), depth + 1)))})",
            "acc" => ConvertOmmlAccent(element, depth),
            "bar" => ConvertOmmlBar(element, depth),
            "limLow" =>
                $"{LimitBase(OmmlContainer(element, "e", depth))}{BelowMarker}({OmmlContainer(element, "lim", depth)})",
            "limUpp" =>
                $"{LimitBase(OmmlContainer(element, "e", depth))}{AboveMarker}({OmmlContainer(element, "lim", depth)})",
            "func" => ConvertOmmlFunction(element, depth),
            "box" or "borderBox" => $"▭({OmmlContainer(element, "e", depth)})",
            "groupChr" => ConvertOmmlGroupChr(element, depth),
            "phant" => ConvertOmmlPhantom(element, depth),
            var name when name.EndsWith("Pr", StringComparison.Ordinal) => "",
            _ => throw new NativeToolException(
                "EQUATION_INVALID",
                $"Unsupported OMML element: {element.Name.LocalName}"
            ),
        };
        return ApplyOmmlControlStyle(element, result);
    }

    private static string ConvertOmmlPreScript(XElement element, int depth)
    {
        var body = OmmlContainer(element, "e", depth);
        var sub = OmmlContainer(element, "sub", depth, required: false);
        var sup = OmmlContainer(element, "sup", depth, required: false);
        if (sub.Length == 0 && sup.Length == 0) return body;
        return ApplyPreScripts(body, sub, sup);
    }

    private static string ConvertOmmlGroupChr(XElement element, int depth)
    {
        var body = OmmlContainer(element, "e", depth);
        var properties = Child(element, "groupChrPr");
        var defaultsToBottom = element.Ancestors().Any(ancestor =>
            IsOfficeMathElement(ancestor, "limLow")
        );
        var defaultCharacter = defaultsToBottom ? "⏟" : "⏞";
        var defaultPosition = defaultsToBottom ? "bot" : "top";
        var chr = properties is null
            ? defaultCharacter
            : ReadVal(Child(properties, "chr"), defaultCharacter);
        var pos = properties is null
            ? defaultPosition
            : ReadVal(Child(properties, "pos"), defaultPosition);
        if (pos is not ("top" or "bot"))
        {
            throw new NativeToolException(
                "EQUATION_INVALID",
                "OMML groupChr position must be top or bot",
                new { pos }
            );
        }
        const string topStretch = "⏜⏞⏠⎴";
        const string bottomStretch = "⏝⏟⏡⎵";
        if (topStretch.Contains(chr, StringComparison.Ordinal))
        {
            if (pos != "top")
            {
                throw new NativeToolException("EQUATION_INVALID", "OMML upper group character has a bottom position", new { chr, pos });
            }
            return $"{chr}({body})";
        }
        if (bottomStretch.Contains(chr, StringComparison.Ordinal))
        {
            if (pos != "bot")
            {
                throw new NativeToolException("EQUATION_INVALID", "OMML lower group character has a top position", new { chr, pos });
            }
            return $"{chr}({body})";
        }
        if (chr is "‾" or "¯" or "_")
        {
            return pos == "bot"
                ? ApplyUnderAccent(body, "_")
                : ApplyAccent(body, "bar");
        }
        if (chr == "→")
        {
            return pos == "bot"
                ? ApplyUnderAccent(body, chr)
                : ApplyAccent(body, "vec");
        }
        throw new NativeToolException(
            "EQUATION_INVALID",
            "OMML groupChr character is not representable by Word linear OMath",
            new { chr, pos }
        );
    }

    private static string ConvertOmmlBar(XElement element, int depth)
    {
        var body = OmmlContainer(element, "e", depth);
        var properties = Child(element, "barPr");
        var position = properties is null
            ? "top"
            : ReadVal(Child(properties, "pos"), "top");
        return position switch
        {
            "top" => ApplyAccent(body, "bar"),
            "bot" => ApplyUnderAccent(body, "_"),
            _ => throw new NativeToolException(
                "EQUATION_INVALID",
                "OMML bar position must be top or bot",
                new { position }
            ),
        };
    }

    private static string ConvertOmmlPhantom(XElement element, int depth)
    {
        var body = OmmlContainer(element, "e", depth);
        var properties = Child(element, "phantPr");
        var hasExplicitGeometry = properties?.Elements().Any(child =>
            IsOfficeMathNamespace(child.Name.NamespaceName)
            && child.Name.LocalName
                is "show" or "zeroWid" or "zeroAsc" or "zeroDesc" or "transp"
        ) == true;
        var show = ReadOmmlToggle(
            properties,
            "show",
            defaultValue: hasExplicitGeometry
        );
        var zeroWidth = ReadOmmlToggle(properties, "zeroWid", defaultValue: false);
        var zeroAscent = ReadOmmlToggle(properties, "zeroAsc", defaultValue: false);
        var zeroDescent = ReadOmmlToggle(properties, "zeroDesc", defaultValue: false);
        var transparent = ReadOmmlToggle(properties, "transp", defaultValue: false);

        var marker = (show, zeroWidth, zeroAscent, zeroDescent, transparent) switch
        {
            (false, false, false, false, false) => "⟡",
            (false, false, true, true, false) => "⬄",
            (false, true, false, false, false) => "⇳",
            (true, false, true, true, false) => "⬍",
            (true, true, false, false, false) => "⬌",
            (true, false, true, false, false) => "⬆",
            (true, false, false, true, false) => "⬇",
            _ => "",
        };
        if (marker.Length > 0)
        {
            return $"{marker}({body})";
        }
        var flags = (show ? 1 : 0)
            | (zeroWidth ? 2 : 0)
            | (zeroAscent ? 4 : 0)
            | (zeroDescent ? 8 : 0)
            | (transparent ? 16 : 0);
        return $"{PhantomMarker}({flags}&{body})";
    }

    private static bool ReadOmmlToggle(
        XElement? properties,
        string name,
        bool defaultValue
    )
    {
        var element = properties is null ? null : Child(properties, name);
        if (element is null)
        {
            return defaultValue;
        }
        return ReadVal(element, "1").Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "on" or "yes" => true,
            "0" or "false" or "off" or "no" => false,
            var value => throw new NativeToolException(
                "EQUATION_INVALID",
                "OMML phantom property contains an invalid on/off value",
                new { property = name, value }
            ),
        };
    }

    private static string ConvertOmmlRun(
        XElement element,
        bool normalizeDifferential = false
    )
    {
        var text = string.Concat(
            element.Descendants()
                .Where(item => IsOfficeMathElement(item, "t"))
                .Select(item => item.Value)
        );
        var normal = element.Descendants().Any(item => IsOfficeMathElement(item, "nor"));
        if (
            text == WordLinearMathNormalizer.DifferentialD.ToString()
            || normalizeDifferential && normal && text == "d"
        )
        {
            return ApplyOmmlRunStyle(
                element,
                WordLinearMathNormalizer.DifferentialD.ToString()
            );
        }
        if (normal)
        {
            return ApplyOmmlRunStyle(element, WordText(text));
        }
        var clean = CleanLeaf(text);
        var scriptElement = element.Descendants()
            .FirstOrDefault(item => IsOfficeMathElement(item, "scr"));
        var script = scriptElement is null ? "" : ReadVal(scriptElement, "");
        var converted = MathAlphabetMapper.TryFromOmmlScript(clean, script, out var styled)
            ? styled
            : clean;
        return ApplyOmmlRunStyle(element, converted);
    }

    private static string ApplyOmmlRunStyle(XElement run, string linear)
    {
        var properties = Child(run, "rPr");
        var styleElement = properties is null ? null : Child(properties, "sty");
        if (styleElement is null || linear.Length == 0)
        {
            return linear;
        }
        var style = ParseOmmlStyle(ReadVal(styleElement, ""), "m:sty");
        return EquationFormattingMarkers.Wrap(
            style,
            EquationStyleTarget.RunsOnly,
            linear
        );
    }

    private static string ApplyOmmlControlStyle(XElement element, string linear)
    {
        if (
            linear.Length == 0
            || !OmmlControlProperties.TryGetValue(
                element.Name.LocalName,
                out var propertyName
            )
        )
        {
            return linear;
        }
        var properties = Child(element, propertyName);
        var control = properties is null ? null : Child(properties, "ctrlPr");
        var runProperties = control?.Elements()
            .FirstOrDefault(item =>
                item.Name.LocalName == "rPr"
                && IsWordprocessingNamespace(item.Name.NamespaceName)
            );
        if (runProperties is null)
        {
            return linear;
        }
        var bold = ReadWordToggle(runProperties, "b");
        var italic = ReadWordToggle(runProperties, "i");
        if (bold is null && italic is null)
        {
            return linear;
        }
        var style = (bold ?? false, italic ?? false) switch
        {
            (false, false) => EquationMathStyle.Plain,
            (true, false) => EquationMathStyle.Bold,
            (false, true) => EquationMathStyle.Italic,
            _ => EquationMathStyle.BoldItalic,
        };
        return EquationFormattingMarkers.Wrap(
            style,
            EquationStyleTarget.FirstControl,
            linear
        );
    }

    private static bool? ReadWordToggle(XElement properties, string localName)
    {
        var element = properties.Elements()
            .FirstOrDefault(item =>
                item.Name.LocalName == localName
                && IsWordprocessingNamespace(item.Name.NamespaceName)
            );
        if (element is null)
        {
            return null;
        }
        var value = element.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == "val")
            ?.Value;
        if (value is null)
        {
            return true;
        }
        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "on" or "yes" => true,
            "0" or "false" or "off" or "no" => false,
            _ => throw new NativeToolException(
                "EQUATION_INVALID",
                "OMML control formatting contains an invalid on/off value",
                new { property = localName, value }
            ),
        };
    }

    private static EquationMathStyle ParseOmmlStyle(string value, string property) =>
        value.Trim().ToLowerInvariant() switch
        {
            "p" => EquationMathStyle.Plain,
            "b" => EquationMathStyle.Bold,
            "i" => EquationMathStyle.Italic,
            "bi" => EquationMathStyle.BoldItalic,
            _ => throw new NativeToolException(
                "EQUATION_INVALID",
                "OMML contains an unsupported mathematical style value",
                new { property, value }
            ),
        };

    private static string ConvertOmmlRadical(XElement element, int depth)
    {
        var body = OmmlContainer(element, "e", depth);
        var properties = Child(element, "radPr");
        var degreeHidden = properties is not null
            && Child(properties, "degHide") is { } hidden
            && ReadVal(hidden, "1") != "0";
        var degreeElement = Child(element, "deg");
        var degree = degreeElement is null
            ? ""
            : OmmlSequence(degreeElement.Elements(), depth + 1);
        return degreeHidden || degree.Length == 0
            ? $"√({body})"
            : $"√({degree}&{body})";
    }

    private static string ConvertOmmlNary(XElement element, int depth)
    {
        var properties = Child(element, "naryPr");
        var character = properties is null
            ? "∫"
            : ReadVal(Child(properties, "chr"), "∫");
        var lower = OmmlContainer(element, "sub", depth, required: false);
        var upper = OmmlContainer(element, "sup", depth, required: false);
        var bodyElement = Child(element, "e");
        var body = bodyElement is null
            ? ""
            : OmmlSequence(
                bodyElement.Elements(),
                depth + 1,
                normalizeDifferential: IsIntegralCharacter(character)
            );
        var builder = new StringBuilder(CleanLeaf(character));
        if (lower.Length > 0)
        {
            builder.Append("_(").Append(lower).Append(')');
        }
        if (upper.Length > 0)
        {
            builder.Append("^(").Append(upper).Append(')');
        }
        builder.Append(NaryBodySeparator);
        if (body.Length > 0)
        {
            builder.Append(body);
        }
        return builder.ToString();
    }

    private static string ConvertOmmlDelimiter(XElement element, int depth)
    {
        var properties = Child(element, "dPr");
        var begin = properties is null ? "(" : ReadVal(Child(properties, "begChr"), "(");
        var end = properties is null ? ")" : ReadVal(Child(properties, "endChr"), ")");
        var bodyElements = Children(element, "e").ToArray();
        var separator = properties is null ? "|" : ReadVal(Child(properties, "sepChr"), "|");
        var body = string.Join(
            CleanLeaf(separator),
            bodyElements.Select(bodyElement =>
                OmmlSequence(bodyElement.Elements(), depth + 1)
            )
        );
        if (
            begin == "("
            && end == ")"
            && bodyElements.Length == 1
            && IsSingleNoBarFraction(bodyElements[0])
        )
        {
            return body;
        }
        return $"{CleanLeaf(begin)}{body}{CleanLeaf(end)}";
    }

    private static bool IsSingleNoBarFraction(XElement container)
    {
        var children = container.Elements()
            .Where(element => !element.Name.LocalName.EndsWith("Pr", StringComparison.Ordinal))
            .ToArray();
        if (children.Length != 1 || !IsOfficeMathElement(children[0], "f"))
        {
            return false;
        }
        var properties = Child(children[0], "fPr");
        return properties is not null
            && ReadVal(Child(properties, "type"), "bar") == "noBar";
    }

    private static string ConvertOmmlFunction(XElement element, int depth)
    {
        var name = UnwrapNamedBase(OmmlContainer(element, "fName", depth));
        var argument = OmmlContainer(element, "e", depth);
        return $"{name}\u2061{argument}";
    }

    private static string ConvertOmmlMatrix(XElement element, int depth)
    {
        var rows = Children(element, "mr")
            .Select(
                row =>
                    string.Join(
                        "&",
                        Children(row, "e")
                            .Select(cell => OmmlSequence(cell.Elements(), depth + 2))
                    )
            )
            .ToArray();
        if (rows.Length == 0 || rows.Any(row => row.Length == 0))
        {
            throw new NativeToolException(
                "EQUATION_INVALID",
                "OMML matrix must contain non-empty rows and cells"
            );
        }
        return $"{MatrixMarker}({string.Join("@", rows)})";
    }

    private static string ConvertOmmlAccent(XElement element, int depth)
    {
        var properties = Child(element, "accPr");
        var character = properties is null ? "^" : ReadVal(Child(properties, "chr"), "^");
        var body = OmmlContainer(element, "e", depth);
        var function = character switch
        {
            "¯" or "‾" or "\u0305" => "bar",
            "→" or "\u20D7" => "vec",
            "^" or "ˆ" or "\u0302" => "hat",
            "~" or "˜" or "\u0303" => "tilde",
            "˙" or "·" or "\u0307" => "dot",
            "¨" or "\u0308" => "ddot",
            "\u0301" => "acute",
            "\u0300" => "grave",
            "\u20DB" => "dddot",
            "\u20DC" => "ddddot",
            "\u030C" => "check",
            "\u0306" => "breve",
            "\u0332" => "underline",
            _ => "",
        };
        return function.Length > 0
            ? ApplyAccent(body, function)
            : $"{ParenthesizeBase(body)}^({CleanLeaf(character)})";
    }

    private static string ConvertOmmlFraction(XElement element, int depth)
    {
        var properties = Child(element, "fPr");
        var fractionType = properties is null
            ? "bar"
            : ReadVal(Child(properties, "type"), "bar");
        var numerator = OmmlContainer(element, "num", depth);
        var denominator = OmmlContainer(element, "den", depth);
        return fractionType == "noBar"
            ? $"({numerator}¦{denominator})"
            : $"({numerator})/({denominator})";
    }

    private static string ApplyAccent(string body, string function)
    {
        var mark = function switch
        {
            "vec" => "\u20D7",
            "hat" => "\u0302",
            "bar" => "\u0305",
            "tilde" => "\u0303",
            "dot" => "\u0307",
            "ddot" => "\u0308",
            "acute" => "\u0301",
            "grave" => "\u0300",
            "dddot" => "\u20DB",
            "ddddot" => "\u20DC",
            "check" => "\u030C",
            "breve" => "\u0306",
            "underline" => "\u0332",
            _ => throw new NativeToolException(
                "EQUATION_INVALID",
                $"Unsupported Word accent: {function}"
            ),
        };
        return $"{ParenthesizeBase(body)}{mark}";
    }

    private static string ApplyUnderAccent(string body, string accent)
    {
        var mark = accent switch
        {
            "_" or "¯" or "‾" => "\u0332",
            "→" => "\u20EF",
            "^" or "ˆ" => "\u032D",
            "~" or "˜" => "\u0330",
            "˙" or "·" => "\u0323",
            "¨" => "\u0324",
            _ => throw new NativeToolException(
                "EQUATION_INVALID",
                "MathML accentunder cannot be represented losslessly by Word linear OMath",
                new { accent }
            ),
        };
        return $"{ParenthesizeBase(body)}{mark}";
    }

    private static string OmmlContainer(
        XElement element,
        string name,
        int depth,
        bool required = true
    )
    {
        var child = Child(element, name);
        if (child is null)
        {
            if (!required)
            {
                return "";
            }
            throw new NativeToolException(
                "EQUATION_INVALID",
                $"OMML {element.Name.LocalName} is missing m:{name}"
            );
        }
        return OmmlSequence(child.Elements(), depth + 1);
    }

    private static XElement? Child(XElement element, string localName)
    {
        return element.Elements()
            .FirstOrDefault(child => IsOfficeMathElement(child, localName));
    }

    private static IEnumerable<XElement> Children(XElement element, string localName)
    {
        return element.Elements()
            .Where(child => IsOfficeMathElement(child, localName));
    }

    private static bool IsOfficeMathNamespace(string value) =>
        value is OfficeMathNamespace or OfficeMathStrictNamespace;

    private static bool IsOfficeMathElement(XElement element, string localName) =>
        IsOfficeMathNamespace(element.Name.NamespaceName)
        && element.Name.LocalName == localName;

    private static bool IsWordprocessingNamespace(string value) =>
        value is WordprocessingNamespace or WordprocessingStrictNamespace;

    private static bool IsAllowedWordFormattingElement(XElement element)
    {
        if (!IsWordprocessingNamespace(element.Name.NamespaceName))
        {
            return false;
        }
        var formattingRoot = element.AncestorsAndSelf()
            .LastOrDefault(item => IsWordprocessingNamespace(item.Name.NamespaceName));
        if (formattingRoot?.Name.LocalName != "rPr")
        {
            return false;
        }
        var mathParent = formattingRoot.Ancestors()
            .FirstOrDefault(item => IsOfficeMathNamespace(item.Name.NamespaceName));
        return mathParent?.Name.LocalName is "r" or "ctrlPr";
    }

    private static bool IsIntegralCharacter(string value) =>
        value is "∫" or "∬" or "∭" or "∮";

    private static string ReadVal(XElement? element, string defaultValue)
    {
        return element?.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == "val")
                ?.Value
            ?? defaultValue;
    }

    private static string ParenthesizeBase(string value)
    {
        var visible = WithoutFormattingMarkers(value);
        return visible.Length == 1 || HasSingleOuterGroup(visible)
            ? value
            : $"({value})";
    }

    private static bool HasSingleOuterGroup(string value)
    {
        if (value.Length < 2)
        {
            return false;
        }
        var closing = value[0] switch
        {
            '(' => ')',
            '[' => ']',
            '{' => '}',
            '〖' => '〗',
            _ => '\0',
        };
        if (closing == '\0' || value[^1] != closing)
        {
            return false;
        }
        var depth = 0;
        var inQuotedText = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '"')
            {
                if (inQuotedText && index + 1 < value.Length && value[index + 1] == '"')
                {
                    index++;
                    continue;
                }
                inQuotedText = !inQuotedText;
                continue;
            }
            if (inQuotedText)
            {
                continue;
            }
            if (character == value[0])
            {
                depth++;
            }
            else if (character == closing)
            {
                depth--;
                if (depth == 0 && index != value.Length - 1)
                {
                    return false;
                }
                if (depth < 0)
                {
                    return false;
                }
            }
        }
        return depth == 0 && !inQuotedText;
    }

    private static string LimitBase(string value) =>
        IsSimpleName(value) ? value : ParenthesizeBase(value);

    private static string UnwrapNamedBase(string value)
    {
        if (value.Length < 4 || value[0] != '(')
        {
            return value;
        }
        var closing = value.IndexOf(')');
        if (
            closing <= 1
            || closing + 1 >= value.Length
            || value[closing + 1] is not ('_' or '^' or BelowMarker or AboveMarker)
        )
        {
            return value;
        }
        var candidate = value[1..closing];
        return IsSimpleName(candidate)
            ? candidate + value[(closing + 1)..]
            : value;
    }

    private static bool IsSimpleName(string value)
    {
        var visible = WithoutFormattingMarkers(value);
        if (visible.Length == 0)
        {
            return false;
        }
        var candidate = visible;
        if (visible.Length >= 2 && visible[0] == '"' && visible[^1] == '"')
        {
            candidate = visible[1..^1];
        }
        return candidate.Length > 0 && candidate.All(char.IsLetterOrDigit);
    }

    private static string WithoutFormattingMarkers(string value)
    {
        if (!value.Any(EquationFormattingMarkers.IsReserved))
        {
            return value;
        }
        return string.Concat(value.Where(character =>
            !EquationFormattingMarkers.IsReserved(character)
        ));
    }

    private static string WordText(string value)
    {
        return $"\"{CleanLeaf(value).Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string CleanLeaf(string value)
    {
        if (
            value.Any(
                character =>
                    (character < 32 && character is not ('\t' or '\n' or '\r'))
                    || character == 127
            )
        )
        {
            throw new NativeToolException(
                "EQUATION_INVALID",
                "Equation markup contains unsafe control characters"
            );
        }
        return TrimInsignificantWhitespace(value);
    }

    private static string TrimInsignificantWhitespace(string value)
    {
        var start = 0;
        while (
            start < value.Length
            && char.IsWhiteSpace(value[start])
            && !WordMathSpacing.IsSignificant(value[start])
        )
        {
            start++;
        }
        var end = value.Length;
        while (
            end > start
            && char.IsWhiteSpace(value[end - 1])
            && !WordMathSpacing.IsSignificant(value[end - 1])
        )
        {
            end--;
        }
        return value[start..end];
    }

    private static void RequireDepth(int depth)
    {
        if (depth > MaximumDepth)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                $"Equation markup exceeds the maximum depth of {MaximumDepth}"
            );
        }
    }

    private static NativeToolException Arity(
        string element,
        int expected,
        int actual
    )
    {
        return new NativeToolException(
            "EQUATION_INVALID",
            $"MathML {element} requires exactly {expected} child elements",
            new { expected, actual }
        );
    }

    private sealed record MathMlVariantDirective(string Name);

    private readonly record struct MathMlStylePlan(
        MathAlphabetStyle? Alphabet,
        EquationMathStyle Style
    );
}
