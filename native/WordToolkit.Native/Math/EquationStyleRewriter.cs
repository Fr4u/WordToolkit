using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Equations;

internal sealed record EquationStyleRewriteResult(
    string WordOpenXml,
    string StyleContractSha256,
    int RegionCount,
    int StyledRunCount,
    int BoldRunCount,
    int BoldItalicRunCount,
    int BoldControlCount,
    int BoldItalicControlCount
);

internal sealed record EquationStyleVerification(
    string ExpectedContractSha256,
    string ActualContractSha256,
    int StyledRunCount,
    int BoldRunCount,
    int BoldItalicRunCount,
    int BoldControlCount,
    int BoldItalicControlCount
);

internal static class EquationStyleRewriter
{
    private const string MathNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string StrictMathNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/math";
    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string StrictWordNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const int MaximumWordOpenXmlCharacters = 8_000_000;
    private const int MaximumElements = 100_000;
    private const int MaximumDepth = 256;

    private static readonly IReadOnlyDictionary<string, string> ControlProperties =
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

    internal static EquationStyleRewriteResult Rewrite(
        string wordOpenXml,
        EquationStyleCounts expectedCounts
    )
    {
        if (expectedCounts.Total < 1)
        {
            throw Invalid("An equation style rewrite requires at least one region");
        }
        var document = Parse(wordOpenXml);
        var equation = SingleEquation(document);
        var controlCounts = RewriteControlStyles(equation);
        var stack = new Stack<EquationMathStyle>();
        var observedBold = 0;
        var observedBoldItalic = 0;
        var styledRuns = 0;
        var boldRuns = 0;
        var boldItalicRuns = 0;

        foreach (var run in equation.Descendants().Where(IsMathRun).ToArray())
        {
            var textElements = run.Elements().Where(IsMathText).ToArray();
            var containsMarker = textElements.Any(text =>
                text.Value.Any(EquationFormattingMarkers.IsReserved)
            );
            if (!containsMarker)
            {
                if (stack.TryPeek(out var active) && textElements.Length > 0)
                {
                    ApplyStyle(run, active);
                    CountRun(active, ref styledRuns, ref boldRuns, ref boldItalicRuns);
                }
                continue;
            }
            if (
                textElements.Length != 1
                || run.Elements().Any(element =>
                    !IsMathText(element)
                    && !(IsMathElement(element) && element.Name.LocalName == "rPr")
                    && !IsWordRunProperties(element)
                )
            )
            {
                throw Invalid(
                    "Microsoft Word returned a formatting marker in an unsupported math run"
                );
            }

            var replacements = new List<XElement>();
            var text = textElements[0].Value;
            var segmentStart = 0;
            for (var index = 0; index <= text.Length; index++)
            {
                var marker = index < text.Length && EquationFormattingMarkers.IsReserved(text[index]);
                if (!marker && index < text.Length)
                {
                    continue;
                }
                if (index > segmentStart)
                {
                    var replacement = new XElement(run);
                    replacement.Elements().Single(IsMathText).Value = text[segmentStart..index];
                    if (stack.TryPeek(out var active))
                    {
                        ApplyStyle(replacement, active);
                        CountRun(
                            active,
                            ref styledRuns,
                            ref boldRuns,
                            ref boldItalicRuns
                        );
                    }
                    replacements.Add(replacement);
                }
                if (index == text.Length)
                {
                    break;
                }
                if (EquationFormattingMarkers.TryStart(text[index], out var started))
                {
                    stack.Push(started);
                    if (started == EquationMathStyle.Bold)
                    {
                        observedBold++;
                    }
                    else
                    {
                        observedBoldItalic++;
                    }
                }
                else if (EquationFormattingMarkers.TryEnd(text[index], out var ended))
                {
                    if (stack.Count == 0 || stack.Pop() != ended)
                    {
                        throw Invalid(
                            "Microsoft Word changed the equation formatting marker order"
                        );
                    }
                }
                segmentStart = index + 1;
            }
            run.ReplaceWith(replacements);
        }

        if (stack.Count != 0)
        {
            throw Invalid("Microsoft Word dropped an equation formatting end marker");
        }
        var observed = new EquationStyleCounts(observedBold, observedBoldItalic);
        if (observed != expectedCounts)
        {
            throw Invalid(
                "Microsoft Word changed the equation formatting marker count",
                new
                {
                    expected_bold_regions = expectedCounts.Bold,
                    actual_bold_regions = observed.Bold,
                    expected_bold_italic_regions = expectedCounts.BoldItalic,
                    actual_bold_italic_regions = observed.BoldItalic,
                }
            );
        }
        if (
            equation.Descendants()
                .Where(IsMathText)
                .Any(text => text.Value.Any(EquationFormattingMarkers.IsReserved))
        )
        {
            throw Invalid("An internal equation formatting marker survived rewriting");
        }
        var contract = StyleContract(equation);
        return new EquationStyleRewriteResult(
            document.ToString(SaveOptions.DisableFormatting),
            Hash(contract),
            expectedCounts.Total,
            styledRuns,
            boldRuns,
            boldItalicRuns,
            controlCounts.Bold,
            controlCounts.BoldItalic
        );
    }

    internal static EquationStyleVerification Verify(
        string wordOpenXml,
        EquationStyleRewriteResult expected
    )
    {
        var document = Parse(wordOpenXml);
        var equation = SingleEquation(document);
        if (
            equation.Descendants()
                .Where(IsMathText)
                .Any(text => text.Value.Any(EquationFormattingMarkers.IsReserved))
        )
        {
            throw Invalid("An internal equation formatting marker survived Word readback");
        }
        var runs = equation.Descendants().Where(IsMathRun).ToArray();
        var bold = runs.Count(run => ReadStyle(run) == "b");
        var boldItalic = runs.Count(run => ReadStyle(run) == "bi");
        var controls = equation.Descendants()
            .Where(IsControllableMathObject)
            .Select(ReadControlStyle)
            .ToArray();
        var boldControls = controls.Count(style => style == "b");
        var boldItalicControls = controls.Count(style => style == "bi");
        var directText = runs.SelectMany(run => run.Elements().Where(IsMathText)).ToArray();
        var descendantText = equation.Descendants().Where(IsMathText).ToArray();
        var actualHash = Hash(StyleContract(equation));
        if (!string.Equals(expected.StyleContractSha256, actualHash, StringComparison.Ordinal))
        {
            throw Invalid(
                "Microsoft Word changed native equation style placement during reinsertion",
                new
                {
                    expected_style_contract_sha256 = expected.StyleContractSha256,
                    actual_style_contract_sha256 = actualHash,
                    expected_bold_run_count = expected.BoldRunCount,
                    actual_bold_run_count = bold,
                    expected_bold_italic_run_count = expected.BoldItalicRunCount,
                    actual_bold_italic_run_count = boldItalic,
                    expected_bold_control_count = expected.BoldControlCount,
                    actual_bold_control_count = boldControls,
                    expected_bold_italic_control_count = expected.BoldItalicControlCount,
                    actual_bold_italic_control_count = boldItalicControls,
                    actual_math_run_count = runs.Length,
                    actual_direct_math_text_count = directText.Length,
                    actual_descendant_math_text_count = descendantText.Length,
                    actual_math_text_character_count = descendantText.Sum(item =>
                        item.Value.Length
                    ),
                }
            );
        }
        return new EquationStyleVerification(
            expected.StyleContractSha256,
            actualHash,
            bold + boldItalic,
            bold,
            boldItalic,
            boldControls,
            boldItalicControls
        );
    }

    private static XDocument Parse(string wordOpenXml)
    {
        ArgumentNullException.ThrowIfNull(wordOpenXml);
        if (wordOpenXml.Length is < 1 or > MaximumWordOpenXmlCharacters)
        {
            throw Invalid(
                "Microsoft Word returned an empty or oversized styled-equation readback"
            );
        }
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                MaxCharactersInDocument = MaximumWordOpenXmlCharacters,
                MaxCharactersFromEntities = 0,
            };
            using var textReader = new StringReader(wordOpenXml);
            using var reader = XmlReader.Create(textReader, settings);
            var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            var root = document.Root
                ?? throw Invalid("Microsoft Word returned styled equation XML without a root");
            var elements = root.DescendantsAndSelf().Take(MaximumElements + 1).ToArray();
            if (elements.Length > MaximumElements)
            {
                throw Invalid("Styled equation readback exceeds the element limit");
            }
            if (
                elements.Any(element =>
                    element.Ancestors().Take(MaximumDepth + 1).Count() > MaximumDepth
                )
            )
            {
                throw Invalid("Styled equation readback exceeds the depth limit");
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
                "Microsoft Word returned malformed styled equation XML",
                new
                {
                    exception = exception.GetType().Name,
                    line = exception.LineNumber,
                    position = exception.LinePosition,
                }
            );
        }
    }

    private static XElement SingleEquation(XDocument document)
    {
        var equations = (document.Root?.DescendantsAndSelf() ?? [])
            .Where(element =>
                IsMathElement(element)
                && element.Name.LocalName == "oMath"
                && !element.Ancestors().Any(ancestor =>
                    IsMathElement(ancestor) && ancestor.Name.LocalName == "oMath"
                )
            )
            .ToArray();
        if (equations.Length != 1)
        {
            throw Invalid(
                "Microsoft Word did not return exactly one styled Office Math equation",
                new { equation_count = equations.Length }
            );
        }
        return equations[0];
    }

    private static void ApplyStyle(XElement run, EquationMathStyle style)
    {
        var math = run.Name.Namespace;
        var properties = run.Elements()
            .FirstOrDefault(element =>
                element.Name.Namespace == math && element.Name.LocalName == "rPr"
            );
        if (properties is null)
        {
            properties = new XElement(math + "rPr");
            run.AddFirst(properties);
        }
        var value = style == EquationMathStyle.Bold ? "b" : "bi";
        var styleElement = properties.Elements()
            .FirstOrDefault(element =>
                element.Name.Namespace == math && element.Name.LocalName == "sty"
            );
        if (styleElement is null)
        {
            styleElement = new XElement(math + "sty");
            var following = properties.Elements()
                .FirstOrDefault(element => element.Name.LocalName is "brk" or "aln");
            if (following is null)
            {
                properties.Add(styleElement);
            }
            else
            {
                following.AddBeforeSelf(styleElement);
            }
        }
        styleElement.SetAttributeValue(math + "val", value);
    }

    private static (int Bold, int BoldItalic) RewriteControlStyles(XElement equation)
    {
        var stack = new Stack<EquationMathStyle>();
        var bold = 0;
        var boldItalic = 0;
        foreach (var element in equation.DescendantNodes().OfType<XElement>())
        {
            if (IsMathText(element))
            {
                foreach (var character in element.Value)
                {
                    if (EquationFormattingMarkers.TryStart(character, out var started))
                    {
                        stack.Push(started);
                    }
                    else if (EquationFormattingMarkers.TryEnd(character, out var ended))
                    {
                        if (stack.Count == 0 || stack.Pop() != ended)
                        {
                            throw Invalid(
                                "Microsoft Word changed the equation formatting marker order"
                            );
                        }
                    }
                }
                continue;
            }
            if (
                stack.TryPeek(out var active)
                && IsControllableMathObject(element)
            )
            {
                ApplyControlStyle(element, active);
                if (active == EquationMathStyle.Bold)
                {
                    bold++;
                }
                else
                {
                    boldItalic++;
                }
            }
        }
        if (stack.Count != 0)
        {
            throw Invalid("Microsoft Word dropped an equation formatting end marker");
        }
        return (bold, boldItalic);
    }

    private static void ApplyControlStyle(XElement element, EquationMathStyle style)
    {
        var math = element.Name.Namespace;
        var propertyName = ControlProperties[element.Name.LocalName];
        var properties = element.Elements()
            .FirstOrDefault(child => child.Name == math + propertyName);
        if (properties is null)
        {
            properties = new XElement(math + propertyName);
            element.AddFirst(properties);
        }
        var control = properties.Elements()
            .FirstOrDefault(child => child.Name == math + "ctrlPr");
        if (control is null)
        {
            control = new XElement(math + "ctrlPr");
            properties.Add(control);
        }
        var runProperties = control.Elements()
            .FirstOrDefault(IsWordRunProperties);
        if (runProperties is null)
        {
            XNamespace word = math.NamespaceName == StrictMathNamespace
                ? StrictWordNamespace
                : WordNamespace;
            runProperties = new XElement(word + "rPr");
            control.Add(runProperties);
        }
        var wordNamespace = runProperties.Name.Namespace;
        SetOnOff(runProperties, wordNamespace + "b", enabled: true);
        SetOnOff(
            runProperties,
            wordNamespace + "i",
            enabled: style == EquationMathStyle.BoldItalic
        );
    }

    private static void SetOnOff(XElement properties, XName name, bool enabled)
    {
        var values = properties.Elements(name).ToArray();
        foreach (var duplicate in values.Skip(1))
        {
            duplicate.Remove();
        }
        if (!enabled)
        {
            values.FirstOrDefault()?.Remove();
            return;
        }
        var value = values.FirstOrDefault();
        if (value is null)
        {
            value = new XElement(name);
            var rank = WordRunPropertyRank(name.LocalName);
            var following = properties.Elements()
                .FirstOrDefault(element =>
                    element.Name.Namespace == name.Namespace
                    && WordRunPropertyRank(element.Name.LocalName) > rank
                );
            if (following is null)
            {
                properties.Add(value);
            }
            else
            {
                following.AddBeforeSelf(value);
            }
            return;
        }
        foreach (var attribute in value.Attributes().Where(attribute =>
            attribute.Name.LocalName == "val"
        ).ToArray())
        {
            attribute.Remove();
        }
    }

    private static string StyleContract(XElement equation)
    {
        var output = new StringBuilder();
        foreach (var element in equation.Descendants())
        {
            if (IsControllableMathObject(element))
            {
                var controlStyle = ReadControlStyle(element);
                output.Append('C')
                    .Append(element.Name.LocalName.Length)
                    .Append(':')
                    .Append(element.Name.LocalName)
                    .Append(':')
                    .Append(controlStyle.Length)
                    .Append(':')
                    .Append(controlStyle)
                    .Append(';');
                continue;
            }
            if (!IsMathRun(element))
            {
                continue;
            }
            var run = element;
            var text = string.Concat(run.Elements().Where(IsMathText).Select(item => item.Value));
            if (text.Length == 0)
            {
                continue;
            }
            var style = ReadStyle(run);
            output.Append('R').Append(style.Length).Append(':').Append(style);
            output.Append(':').Append(text.Length).Append(':').Append(text).Append(';');
        }
        return output.ToString();
    }

    private static string ReadStyle(XElement run)
    {
        var math = run.Name.Namespace;
        return run.Elements()
                .FirstOrDefault(element =>
                    element.Name.Namespace == math && element.Name.LocalName == "rPr"
                )
                ?.Elements()
                .FirstOrDefault(element =>
                    element.Name.Namespace == math && element.Name.LocalName == "sty"
                )
                ?.Attributes()
                .FirstOrDefault(attribute =>
                    attribute.Name.Namespace == math && attribute.Name.LocalName == "val"
                )
                ?.Value
            ?? "";
    }

    private static string ReadControlStyle(XElement element)
    {
        if (!ControlProperties.TryGetValue(element.Name.LocalName, out var propertyName))
        {
            return "";
        }
        var math = element.Name.Namespace;
        var runProperties = element.Element(math + propertyName)
            ?.Element(math + "ctrlPr")
            ?.Elements()
            .FirstOrDefault(IsWordRunProperties);
        if (runProperties is null)
        {
            return "";
        }
        var word = runProperties.Name.Namespace;
        var bold = IsOn(runProperties.Element(word + "b"));
        if (!bold)
        {
            return "";
        }
        return IsOn(runProperties.Element(word + "i")) ? "bi" : "b";
    }

    private static bool IsOn(XElement? element)
    {
        if (element is null)
        {
            return false;
        }
        var value = element.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == "val")
            ?.Value;
        return value is null
            || value is not ("0" or "false" or "off" or "no");
    }

    private static int WordRunPropertyRank(string localName) =>
        localName switch
        {
            "rStyle" => 0,
            "rFonts" => 1,
            "b" => 2,
            "bCs" => 3,
            "i" => 4,
            _ => 5,
        };

    private static void CountRun(
        EquationMathStyle style,
        ref int styled,
        ref int bold,
        ref int boldItalic
    )
    {
        styled++;
        if (style == EquationMathStyle.Bold)
        {
            bold++;
        }
        else
        {
            boldItalic++;
        }
    }

    private static bool IsMathRun(XElement element) =>
        IsMathElement(element) && element.Name.LocalName == "r";

    private static bool IsMathText(XElement element) =>
        IsMathElement(element) && element.Name.LocalName == "t";

    private static bool IsControllableMathObject(XElement element) =>
        IsMathElement(element) && ControlProperties.ContainsKey(element.Name.LocalName);

    private static bool IsMathElement(XElement element) =>
        element.Name.NamespaceName is MathNamespace or StrictMathNamespace;

    private static bool IsWordRunProperties(XElement element) =>
        element.Name.LocalName == "rPr"
        && element.Name.NamespaceName
            is WordNamespace or StrictWordNamespace;

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static NativeToolException Invalid(string message, object? details = null) =>
        new("EQUATION_INVALID", message, details);
}
