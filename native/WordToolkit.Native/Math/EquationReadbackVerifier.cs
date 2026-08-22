using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Equations;

internal sealed record EquationReadbackVerification(
    string ExpectedContractSha256,
    string ActualContractSha256,
    int MathElementCount,
    int NaryCount,
    int DifferentialCount,
    bool DifferentialPlacementVerified
);

internal static class EquationReadbackVerifier
{
    private const string MathNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string StrictMathNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/math";
    private const int MaximumWordOpenXmlCharacters = 8_000_000;
    private const int MaximumElements = 100_000;
    private const int MaximumDepth = 256;

    private static readonly HashSet<char> ReadbackSensitiveCharacters =
    [
        '∑',
        '∏',
        '∐',
        '∫',
        '∬',
        '∭',
        '∮',
        '⋃',
        '⋂',
        WordLinearMathNormalizer.DifferentialD,
        'ℏ',
        'ħ',
        '†',
        '■',
        '⒨',
        'ⓢ',
        '⒱',
        '⒩',
        '█',
        'Ⓒ',
        '¦',
        '…',
        '⋯',
        '⋮',
        '⋱',
        '\u20D7',
        '\u0302',
        '\u0305',
        '\u0303',
        '\u0307',
        '\u0308',
        WordMathSpacing.CaseColumn,
        WordMathSpacing.TextBoundary,
    ];

    private static readonly string[] FunctionPowerNames =
    [
        "sin",
        "cos",
        "tan",
        "cot",
        "sec",
        "csc",
        "arcsin",
        "arccos",
        "arctan",
        "sinh",
        "cosh",
        "tanh",
        "log",
        "ln",
        "exp",
    ];

    internal static bool RequiresReadback(string linear)
    {
        ArgumentNullException.ThrowIfNull(linear);
        return linear.Any(ReadbackSensitiveCharacters.Contains)
            || MathAlphabetMapper.ContainsStyledCharacter(linear)
            || RequiresFunctionPowerReadback(linear);
    }

    private static bool RequiresFunctionPowerReadback(string linear)
    {
        foreach (var function in FunctionPowerNames)
        {
            var opening = linear.IndexOf($"({function} ", StringComparison.Ordinal);
            if (
                opening >= 0
                && linear.IndexOf(")^(", opening, StringComparison.Ordinal) >= 0
            )
            {
                return true;
            }
        }
        return false;
    }

    internal static string CanonicalizeForTesting(string value) => Canonicalize(value);

    internal static EquationReadbackVerification Verify(
        string wordOpenXml,
        string expectedLinear
    )
    {
        ArgumentNullException.ThrowIfNull(wordOpenXml);
        ArgumentNullException.ThrowIfNull(expectedLinear);
        if (wordOpenXml.Length is < 1 or > MaximumWordOpenXmlCharacters)
        {
            throw new NativeToolException(
                "EQUATION_INVALID",
                "Microsoft Word returned an empty or oversized equation readback",
                new
                {
                    readback_characters = wordOpenXml.Length,
                    maximum_characters = MaximumWordOpenXmlCharacters,
                }
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
            var document = XDocument.Load(reader, LoadOptions.None);
            var root = document.Root
                ?? throw Invalid("Microsoft Word returned equation XML without a root");
            var elements = root.DescendantsAndSelf().Take(MaximumElements + 1).ToArray();
            if (elements.Length > MaximumElements)
            {
                throw Invalid(
                    "Microsoft Word equation readback exceeds the element limit",
                    new { maximum_elements = MaximumElements }
                );
            }
            if (
                elements.Any(element =>
                    element.Ancestors().Take(MaximumDepth + 1).Count() > MaximumDepth
                )
            )
            {
                throw Invalid(
                    "Microsoft Word equation readback exceeds the depth limit",
                    new { maximum_depth = MaximumDepth }
                );
            }

            var equations = elements.Where(element =>
                    IsMathElement(element, "oMath")
                    && !element.Ancestors().Any(ancestor =>
                        IsMathElement(ancestor, "oMath")
                    )
                )
                .ToArray();
            if (equations.Length != 1)
            {
                throw Invalid(
                    "Microsoft Word did not return exactly one native Office Math equation",
                    new { equation_count = equations.Length }
                );
            }

            var equation = equations[0];
            var actualLinear = MathMarkupToUnicodeMath.Convert(
                equation.ToString(SaveOptions.DisableFormatting),
                "omml"
            );
            var expectedContract = Canonicalize(expectedLinear);
            var actualContract = Canonicalize(actualLinear);
            var expectedHash = Hash(expectedContract);
            var actualHash = Hash(actualContract);

            var differentialTextNodes = equation.Descendants()
                .Where(element => IsMathElement(element, "t"))
                .Where(element => element.Value.Contains(
                    WordLinearMathNormalizer.DifferentialD,
                    StringComparison.Ordinal
                ))
                .ToArray();
            var differentialCount = differentialTextNodes.Sum(element =>
                element.Value.Count(character =>
                    character == WordLinearMathNormalizer.DifferentialD
                )
            );
            var expectedDifferentials = expectedContract.Count(character =>
                character == WordLinearMathNormalizer.DifferentialD
            );
            var expectedIntegralDifferentials =
                CountIntegralOperandDifferentials(expectedContract);
            var actualIntegralDifferentials = differentialTextNodes
                .Where(IsInsideIntegralOperand)
                .Sum(element =>
                    element.Value.Count(character =>
                        character == WordLinearMathNormalizer.DifferentialD
                    )
                );
            var integralNaryCount = equation.Descendants()
                .Count(element => IsMathElement(element, "nary") && IsIntegralNary(element));
            var placementVerified =
                actualIntegralDifferentials == expectedIntegralDifferentials
                || (
                    integralNaryCount > 1
                    && expectedIntegralDifferentials == expectedDifferentials
                    && differentialCount == expectedDifferentials
                );
            var naryCount = equation.Descendants()
                .Count(element => IsMathElement(element, "nary"));

            if (
                !string.Equals(expectedContract, actualContract, StringComparison.Ordinal)
                || differentialCount != expectedDifferentials
                || !placementVerified
            )
            {
                throw Invalid(
                    "Microsoft Word changed equation text, structure, or differential placement during native build-up",
                    new
                    {
                        expected_contract_sha256 = expectedHash,
                        actual_contract_sha256 = actualHash,
                        expected_differential_count = expectedDifferentials,
                        actual_differential_count = differentialCount,
                        expected_integral_differential_count = expectedIntegralDifferentials,
                        actual_integral_differential_count = actualIntegralDifferentials,
                        differential_placement_verified = placementVerified,
                        nary_count = naryCount,
                    }
                );
            }

            return new EquationReadbackVerification(
                expectedHash,
                actualHash,
                elements.Count(IsMathElement),
                naryCount,
                differentialCount,
                placementVerified
            );
        }
        catch (NativeToolException)
        {
            throw;
        }
        catch (XmlException exception)
        {
            throw Invalid(
                "Microsoft Word returned malformed or prohibited equation XML",
                new
                {
                    exception = exception.GetType().Name,
                    line = exception.LineNumber,
                    position = exception.LinePosition,
                }
            );
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or ArgumentException)
        {
            throw Invalid(
                "Microsoft Word equation readback could not be verified",
                new { exception = exception.GetType().Name }
            );
        }
    }

    private static string Canonicalize(string value)
    {
        var normalized = WordLinearMathNormalizer.NormalizeForWord(
            ExpandCompositeMarkers(value)
        );
        normalized = NormalizeQuotedTextWhitespace(normalized);
        normalized = RemoveFunctionApplicationGroups(normalized);
        var output = new StringBuilder(normalized.Length);
        var inQuotedText = false;
        for (var index = 0; index < normalized.Length; index++)
        {
            var character = normalized[index];
            if (character == '"')
            {
                if (
                    inQuotedText
                    && index + 1 < normalized.Length
                    && normalized[index + 1] == '"'
                )
                {
                    output.Append("\"\"");
                    index++;
                    continue;
                }
                inQuotedText = !inQuotedText;
                output.Append(character);
                continue;
            }
            if (!inQuotedText && WordMathSpacing.IsSignificant(character))
            {
                output.Append(character);
                continue;
            }
            if (!inQuotedText && char.IsWhiteSpace(character))
            {
                continue;
            }
            if (!inQuotedText && character == '\u2061')
            {
                continue;
            }
            output.Append(character switch
            {
                '−' or '‐' or '‑' => '-',
                'ħ' => 'ℏ',
                _ => character,
            });
        }
        return output.ToString();
    }

    private static string NormalizeQuotedTextWhitespace(string value)
    {
        var output = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '"')
            {
                output.Append(value[index]);
                continue;
            }
            var text = new StringBuilder();
            var closing = -1;
            for (var cursor = index + 1; cursor < value.Length; cursor++)
            {
                if (value[cursor] != '"')
                {
                    text.Append(value[cursor]);
                    continue;
                }
                if (cursor + 1 < value.Length && value[cursor + 1] == '"')
                {
                    text.Append("\"\"");
                    cursor++;
                    continue;
                }
                closing = cursor;
                break;
            }
            if (closing < 0)
            {
                output.Append(value.AsSpan(index));
                break;
            }
            output.Append('"').Append(text.ToString().Trim()).Append('"');
            index = closing;
        }
        return output.ToString();
    }

    private static string RemoveFunctionApplicationGroups(string value)
    {
        var groups = new Stack<(int Opening, bool Remove)>();
        var removed = new HashSet<int>();
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '〖')
            {
                groups.Push((
                    index,
                    index > 0 && value[index - 1] == '\u2061'
                ));
            }
            else if (value[index] == '〗' && groups.Count > 0)
            {
                var group = groups.Pop();
                if (group.Remove)
                {
                    removed.Add(group.Opening);
                    removed.Add(index);
                }
            }
        }
        if (removed.Count == 0)
        {
            return value;
        }
        var output = new StringBuilder(value.Length - removed.Count);
        for (var index = 0; index < value.Length; index++)
        {
            if (!removed.Contains(index))
            {
                output.Append(value[index]);
            }
        }
        return output.ToString();
    }

    private static string ExpandCompositeMarkers(string value)
    {
        var output = new StringBuilder(value.Length + 16);
        for (var index = 0; index < value.Length; index++)
        {
            var marker = value[index];
            if (
                marker is not ('⒨' or 'ⓢ' or '⒱' or '⒩' or 'Ⓒ')
                || index + 1 >= value.Length
                || value[index + 1] != '('
            )
            {
                output.Append(marker);
                continue;
            }
            var end = MatchingParenthesis(value, index + 1);
            if (end < 0)
            {
                output.Append(marker);
                continue;
            }
            var body = ExpandCompositeMarkers(value[(index + 2)..end]);
            switch (marker)
            {
                case '⒨':
                    output.Append("(■(").Append(body).Append("))");
                    break;
                case 'ⓢ':
                    output.Append("[■(").Append(body).Append(")]");
                    break;
                case '⒱':
                    output.Append("|■(").Append(body).Append(")|");
                    break;
                case '⒩':
                    output.Append("‖■(").Append(body).Append(")‖");
                    break;
                default:
                    output.Append("{█(").Append(body).Append(')');
                    break;
            }
            index = end;
        }
        return output.ToString();
    }

    private static int CountIntegralOperandDifferentials(string value)
    {
        var positions = new HashSet<int>();
        var inQuotedText = false;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '"')
            {
                if (
                    inQuotedText
                    && index + 1 < value.Length
                    && value[index + 1] == '"'
                )
                {
                    index++;
                    continue;
                }
                inQuotedText = !inQuotedText;
                continue;
            }
            if (inQuotedText || !IsIntegralCharacter(value[index]))
            {
                continue;
            }
            if (!TryFindBodySeparator(value, index, out var separator))
            {
                continue;
            }
            var opening = separator + 1;
            while (opening < value.Length && char.IsWhiteSpace(value[opening]))
            {
                opening++;
            }
            if (opening >= value.Length || value[opening] != '〖')
            {
                continue;
            }
            var closing = MatchingInvisibleGroup(value, opening);
            if (closing < 0)
            {
                continue;
            }
            for (var cursor = opening + 1; cursor < closing; cursor++)
            {
                if (value[cursor] == WordLinearMathNormalizer.DifferentialD)
                {
                    positions.Add(cursor);
                }
            }
        }
        return positions.Count;
    }

    private static bool TryFindBodySeparator(
        string value,
        int operatorIndex,
        out int separator
    )
    {
        var depth = 0;
        var inQuotedText = false;
        for (var index = operatorIndex + 1; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '"')
            {
                if (
                    inQuotedText
                    && index + 1 < value.Length
                    && value[index + 1] == '"'
                )
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
            if (character is '(' or '[' or '{' or '〖')
            {
                depth++;
                continue;
            }
            if (character is ')' or ']' or '}' or '〗')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }
            if (character == '▒' && depth == 0)
            {
                separator = index;
                return true;
            }
        }
        separator = -1;
        return false;
    }

    private static int MatchingInvisibleGroup(string value, int opening)
    {
        var depth = 1;
        for (var index = opening + 1; index < value.Length; index++)
        {
            if (value[index] == '〖')
            {
                depth++;
            }
            else if (value[index] == '〗' && --depth == 0)
            {
                return index;
            }
        }
        return -1;
    }

    private static bool IsInsideIntegralOperand(XElement element) =>
        element.Ancestors().Any(ancestor =>
            IsMathElement(ancestor, "e")
            && ancestor.Parent is { } parent
            && IsMathElement(parent, "nary")
            && IsIntegralNary(parent)
        );

    private static bool IsIntegralNary(XElement element)
    {
        var character = element.Descendants()
            .FirstOrDefault(item => IsMathElement(item, "chr"))
            ?.Attributes()
            .FirstOrDefault(attribute =>
                attribute.Name.LocalName == "val"
                && IsMathNamespace(attribute.Name.NamespaceName)
            )
            ?.Value ?? "∫";
        return character.Length == 1 && IsIntegralCharacter(character[0]);
    }

    private static bool IsIntegralCharacter(char character) =>
        character is '∫' or '∬' or '∭' or '∮';

    private static int MatchingParenthesis(string value, int openingIndex)
    {
        var depth = 0;
        var inQuotedText = false;
        for (var index = openingIndex; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '"')
            {
                if (
                    inQuotedText
                    && index + 1 < value.Length
                    && value[index + 1] == '"'
                )
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
            if (character == '(')
            {
                depth++;
            }
            else if (character == ')' && --depth == 0)
            {
                return index;
            }
        }
        return -1;
    }

    private static bool IsMathElement(XElement element) =>
        IsMathNamespace(element.Name.NamespaceName);

    private static bool IsMathElement(XElement element, string localName) =>
        IsMathElement(element) && element.Name.LocalName == localName;

    private static bool IsMathNamespace(string value) =>
        value is MathNamespace or StrictMathNamespace;

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static NativeToolException Invalid(
        string message,
        object? details = null
    ) => new("EQUATION_INVALID", message, details);
}
