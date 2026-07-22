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

    private static readonly HashSet<string> NaryOperators =
    [
        "∑",
        "∏",
        "∐",
        "∫",
        "∬",
        "∭",
        "∮",
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

    public static string Convert(string source, string inputFormat)
    {
        if (source.Length is < 1 or > MaximumOutputLength)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "MathML or OMML length must be between 1 and 100,000 characters"
            );
        }

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
        return result;
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
        return MathMlNode(root, 0);
    }

    private static string MathMlNode(XElement element, int depth)
    {
        RequireDepth(depth);
        var children = element.Elements().ToArray();
        return element.Name.LocalName switch
        {
            "math" or "mrow" or "mstyle" or "mpadded" or "mphantom" =>
                MathMlSequence(children, depth + 1),
            "semantics" => MathMlSemantics(children, depth + 1),
            "annotation" or "annotation-xml" or "mspace" or "none" or "maligngroup"
                or "malignmark" => "",
            "mi" => ConvertMathMlIdentifier(element),
            "mn" or "mo" => CleanLeaf(element.Value),
            "mtext" => WordText(element.Value),
            "mfrac" => BinaryMathMl(
                children,
                depth,
                (left, right) => $"({left})/({right})",
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
            "msqrt" => $"√({MathMlSequence(children, depth + 1)})",
            "mroot" => BinaryMathMl(
                children,
                depth,
                (radicand, degree) => $"√({degree}&{radicand})",
                "mroot"
            ),
            "munder" => BinaryMathMl(
                children,
                depth,
                (basis, lower) => $"{ParenthesizeBase(basis)}_({lower})",
                "munder"
            ),
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
            "menclose" => $"box({MathMlSequence(children, depth + 1)})",
            _ => throw new NativeToolException(
                "EQUATION_INVALID",
                $"Unsupported MathML element: {element.Name.LocalName}"
            ),
        };
    }

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
        return value == "d"
            && string.Equals(
                element.Attribute("mathvariant")?.Value,
                "normal",
                StringComparison.OrdinalIgnoreCase
            )
            ? WordLinearMathNormalizer.DifferentialD.ToString()
            : value;
    }

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
            if (row.Name.LocalName is not ("mtr" or "mlabeledtr"))
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
        return element.Name.LocalName switch
        {
            "oMath" or "oMathPara" or "e" or "num" or "den" or "sub" or "sup"
                or "deg" or "fName" or "lim" => OmmlSequence(
                    element.Elements(),
                    depth,
                    normalizeDifferential
                ),
            "r" => ConvertOmmlRun(element, normalizeDifferential),
            "f" => $"({OmmlContainer(element, "num", depth)})/({OmmlContainer(element, "den", depth)})",
            "sSup" => $"{ParenthesizeBase(OmmlContainer(element, "e", depth))}^({OmmlContainer(element, "sup", depth)})",
            "sSub" => $"{ParenthesizeBase(OmmlContainer(element, "e", depth))}_({OmmlContainer(element, "sub", depth)})",
            "sSubSup" =>
                $"{ParenthesizeBase(OmmlContainer(element, "e", depth))}_({OmmlContainer(element, "sub", depth)})^({OmmlContainer(element, "sup", depth)})",
            "rad" => ConvertOmmlRadical(element, depth),
            "nary" => ConvertOmmlNary(element, depth),
            "d" => ConvertOmmlDelimiter(element, depth),
            "m" => ConvertOmmlMatrix(element, depth),
            "eqArr" =>
                $"{EquationArrayMarker}({string.Join("@", Children(element, "e").Select(item => OmmlSequence(item.Elements(), depth + 1)))})",
            "acc" => ConvertOmmlAccent(element, depth),
            "bar" => ApplyAccent(OmmlContainer(element, "e", depth), "bar"),
            "limLow" =>
                $"{LimitBase(OmmlContainer(element, "e", depth))}{BelowMarker}({OmmlContainer(element, "lim", depth)})",
            "limUpp" =>
                $"{LimitBase(OmmlContainer(element, "e", depth))}{AboveMarker}({OmmlContainer(element, "lim", depth)})",
            "func" => ConvertOmmlFunction(element, depth),
            "box" or "borderBox" => $"▭({OmmlContainer(element, "e", depth)})",
            "groupChr" or "phant" => OmmlContainer(element, "e", depth),
            var name when name.EndsWith("Pr", StringComparison.Ordinal) => "",
            _ => throw new NativeToolException(
                "EQUATION_INVALID",
                $"Unsupported OMML element: {element.Name.LocalName}"
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
            return WordLinearMathNormalizer.DifferentialD.ToString();
        }
        if (normal)
        {
            return WordText(text);
        }
        var clean = CleanLeaf(text);
        var scriptElement = element.Descendants()
            .FirstOrDefault(item => IsOfficeMathElement(item, "scr"));
        var script = scriptElement is null ? "" : ReadVal(scriptElement, "");
        return MathAlphabetMapper.TryFromOmmlScript(clean, script, out var styled)
            ? styled
            : clean;
    }

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
        return $"{CleanLeaf(begin)}{OmmlContainer(element, "e", depth)}{CleanLeaf(end)}";
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
            _ => "",
        };
        return function.Length > 0
            ? ApplyAccent(body, function)
            : $"{ParenthesizeBase(body)}^({CleanLeaf(character)})";
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
            _ => throw new NativeToolException(
                "EQUATION_INVALID",
                $"Unsupported Word accent: {function}"
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
        return value.Length == 1 ? value : $"({value})";
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
        if (value.Length == 0)
        {
            return false;
        }
        var candidate = value;
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            candidate = value[1..^1];
        }
        return candidate.Length > 0 && candidate.All(char.IsLetterOrDigit);
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
        return value.Trim();
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
}
