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
        result = result.Trim();
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
            "mi" or "mn" or "mo" => CleanLeaf(element.Value),
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
            root.Name.NamespaceName != OfficeMathNamespace
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
            if (element.Name.NamespaceName != OfficeMathNamespace)
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

    private static string OmmlSequence(IEnumerable<XElement> elements, int depth)
    {
        RequireDepth(depth);
        return string.Concat(elements.Select(element => OmmlNode(element, depth + 1)));
    }

    private static string OmmlNode(XElement element, int depth)
    {
        RequireDepth(depth);
        return element.Name.LocalName switch
        {
            "oMath" or "oMathPara" or "e" or "num" or "den" or "sub" or "sup"
                or "deg" or "fName" or "lim" => OmmlSequence(element.Elements(), depth),
            "r" => ConvertOmmlRun(element),
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
                $"{ParenthesizeBase(OmmlContainer(element, "e", depth))}{BelowMarker}({OmmlContainer(element, "lim", depth)})",
            "limUpp" =>
                $"{ParenthesizeBase(OmmlContainer(element, "e", depth))}{AboveMarker}({OmmlContainer(element, "lim", depth)})",
            "func" =>
                $"{OmmlContainer(element, "fName", depth)}\u2061({OmmlContainer(element, "e", depth)})",
            "box" or "borderBox" => $"▭({OmmlContainer(element, "e", depth)})",
            "groupChr" or "phant" => OmmlContainer(element, "e", depth),
            var name when name.EndsWith("Pr", StringComparison.Ordinal) => "",
            _ => throw new NativeToolException(
                "EQUATION_INVALID",
                $"Unsupported OMML element: {element.Name.LocalName}"
            ),
        };
    }

    private static string ConvertOmmlRun(XElement element)
    {
        var text = string.Concat(
            element.Descendants()
                .Where(item => item.Name.LocalName == "t")
                .Select(item => item.Value)
        );
        var normal = element.Descendants().Any(item => item.Name.LocalName == "nor");
        return normal ? WordText(text) : CleanLeaf(text);
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
            ? "∑"
            : ReadVal(Child(properties, "chr"), "∑");
        var lower = OmmlContainer(element, "sub", depth, required: false);
        var upper = OmmlContainer(element, "sup", depth, required: false);
        var body = OmmlContainer(element, "e", depth, required: false);
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
            "¯" or "‾" => "bar",
            "→" => "vec",
            "^" or "ˆ" => "hat",
            "~" or "˜" => "tilde",
            "˙" or "·" => "dot",
            "¨" => "ddot",
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
            .FirstOrDefault(
                child =>
                    child.Name.NamespaceName == OfficeMathNamespace
                    && child.Name.LocalName == localName
            );
    }

    private static IEnumerable<XElement> Children(XElement element, string localName)
    {
        return element.Elements()
            .Where(
                child =>
                    child.Name.NamespaceName == OfficeMathNamespace
                    && child.Name.LocalName == localName
            );
    }

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
