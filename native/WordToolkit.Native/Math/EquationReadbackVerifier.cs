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

internal sealed record EquationReadbackDiagnostic(
    string MismatchKind,
    int? ExpectedCount = null,
    int? ActualCount = null,
    int? FirstDifferenceIndex = null,
    string? ExpectedTokenKind = null,
    string? ActualTokenKind = null,
    int? ExpectedCodePoint = null,
    int? ActualCodePoint = null,
    string? NodePath = null,
    IReadOnlyDictionary<string, int>? ExpectedFamilies = null,
    IReadOnlyDictionary<string, int>? ActualFamilies = null,
    IReadOnlyList<int>? ExpectedCodePointWindow = null,
    IReadOnlyList<int>? ActualCodePointWindow = null
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
        '∱',
        '∲',
        '∳',
        '∯',
        '∰',
        '⋃',
        '⋂',
        '⨁',
        '⨂',
        '⨀',
        '⨄',
        '⨆',
        '⋁',
        '⋀',
        '⨌',
        '⨍',
        '⨎',
        '⨏',
        '⨐',
        '⨑',
        '⨒',
        '⨓',
        '⨔',
        '⨕',
        '⨖',
        '⨗',
        '⨘',
        '⨙',
        '⨚',
        '⨛',
        '⨜',
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
        '▭',
        '┬',
        '┴',
        '║',
        '⟡',
        '⬄',
        '⇳',
        '⬍',
        '⬌',
        '⬆',
        '⬇',
        '⏜',
        '⏝',
        '⏞',
        '⏟',
        '⏠',
        '⏡',
        '⎴',
        '⎵',
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
        '\u0301',
        '\u0300',
        '\u20DB',
        '\u030C',
        '\u0306',
        '\u0332',
        '\u20EF',
        '\u032D',
        '\u0330',
        '\u0323',
        '\u0324',
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
                    new
                    {
                        equation_count = equations.Length,
                        diagnostic = new EquationReadbackDiagnostic(
                            "equation_count", ExpectedCount: 1, ActualCount: equations.Length)
                    }
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
            var expectedIntegralDifferentialCounts =
                CountTopLevelIntegralOperandDifferentialCounts(expectedContract);
            var expectedIntegralDifferentials =
                expectedIntegralDifferentialCounts.Sum();
            var actualIntegralDifferentials = differentialTextNodes
                .Where(IsInsideIntegralOperand)
                .Sum(element =>
                    element.Value.Count(character =>
                        character == WordLinearMathNormalizer.DifferentialD
                    )
                );
            var integralNaryCount = equation.Descendants()
                .Count(element => IsMathElement(element, "nary") && IsIntegralNary(element));
            var actualIntegralDifferentialCounts =
                CountActualTopLevelIntegralDifferentialCounts(equation);
            var perIntegralPlacementVerified =
                actualIntegralDifferentialCounts.Count == expectedIntegralDifferentialCounts.Count
                && actualIntegralDifferentialCounts
                    .Select((actual, index) => actual <= expectedIntegralDifferentialCounts[index])
                    .All(value => value);
            var placementVerified =
                perIntegralPlacementVerified
                && actualIntegralDifferentials <= expectedIntegralDifferentials
                && (actualIntegralDifferentials == expectedIntegralDifferentials
                    || integralNaryCount > 1);
            var naryCount = equation.Descendants()
                .Count(element => IsMathElement(element, "nary"));
            var mismatch = BuildDiagnostic(
                expectedContract,
                actualContract,
                expectedDifferentials,
                differentialCount,
                expectedIntegralDifferentialCounts,
                actualIntegralDifferentialCounts,
                placementVerified,
                equation
            );

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
                        expected_integral_differential_counts = expectedIntegralDifferentialCounts,
                        actual_integral_differential_counts = actualIntegralDifferentialCounts,
                        differential_placement_verified = placementVerified,
                        nary_count = naryCount,
                        diagnostic = mismatch,
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

    private static EquationReadbackDiagnostic BuildDiagnostic(
        string expected,
        string actual,
        int expectedDifferentials,
        int actualDifferentials,
        IReadOnlyList<int> expectedPlacement,
        IReadOnlyList<int> actualPlacement,
        bool placementVerified,
        XElement equation
    )
    {
        var expectedFamilies = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["nary"] = expected.Count(IsNaryCharacter),
            ["fraction"] = expected.Count(character => character == '/'),
            ["superscript"] = CountOccurrences(expected, "^("),
            ["subscript"] = CountOccurrences(expected, "_("),
            ["radical"] = expected.Count(character => character == '√'),
            ["matrix"] = expected.Count(character => character is '■' or '⒨' or 'ⓢ' or '⒱' or '⒩'),
        };
        var actualFamilies = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["nary"] = equation.Descendants().Count(element => IsMathElement(element, "nary")),
            ["fraction"] = equation.Descendants().Count(element => IsMathElement(element, "f")),
            ["superscript"] = equation.Descendants().Count(element => IsMathElement(element, "sSup")),
            ["subscript"] = equation.Descendants().Count(element => IsMathElement(element, "sSub")),
            ["radical"] = equation.Descendants().Count(element => IsMathElement(element, "rad")),
            ["matrix"] = equation.Descendants().Count(element => IsMathElement(element, "m")),
        };
        if (expectedDifferentials != actualDifferentials)
        {
            return new EquationReadbackDiagnostic(
                "differential_count",
                ExpectedCount: expectedDifferentials,
                ActualCount: actualDifferentials,
                NodePath: "equation/differential",
                ExpectedFamilies: expectedFamilies,
                ActualFamilies: actualFamilies
            );
        }
        if (!placementVerified)
        {
            return new EquationReadbackDiagnostic(
                "differential_placement",
                ExpectedCount: expectedPlacement.Count,
                ActualCount: actualPlacement.Count,
                NodePath: "equation/integral_operand",
                ExpectedFamilies: expectedFamilies,
                ActualFamilies: actualFamilies
            );
        }
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            var limit = Math.Min(expected.Length, actual.Length);
            var index = 0;
            while (index < limit && expected[index] == actual[index])
            {
                index++;
            }
            return new EquationReadbackDiagnostic(
                "canonical_structure",
                ExpectedCount: expected.Length,
                ActualCount: actual.Length,
                FirstDifferenceIndex: index,
                ExpectedTokenKind: index < expected.Length ? TokenKind(expected[index]) : "end",
                ActualTokenKind: index < actual.Length ? TokenKind(actual[index]) : "end",
                ExpectedCodePoint: index < expected.Length ? expected[index] : null,
                ActualCodePoint: index < actual.Length ? actual[index] : null,
                NodePath: $"equation/canonical/{index}",
                ExpectedFamilies: expectedFamilies,
                ActualFamilies: actualFamilies,
                ExpectedCodePointWindow: CodePointWindow(expected, index),
                ActualCodePointWindow: CodePointWindow(actual, index)
            );
        }
        return new EquationReadbackDiagnostic(
            "equation_structure",
            NodePath: "equation",
            ExpectedFamilies: expectedFamilies,
            ActualFamilies: actualFamilies
        );
    }

    private static int CountOccurrences(string value, string marker)
    {
        var count = 0;
        for (var index = 0; index <= value.Length - marker.Length; index++)
        {
            if (value.AsSpan(index, marker.Length).SequenceEqual(marker.AsSpan()))
            {
                count++;
            }
        }
        return count;
    }

    private static IReadOnlyList<int> CodePointWindow(string value, int center)
    {
        var start = Math.Max(0, center - 8);
        var end = Math.Min(value.Length, center + 16);
        return value.AsSpan(start, end - start)
            .ToArray()
            .Select(character => (int)character)
            .ToArray();
    }

    private static string TokenKind(char character) => character switch
    {
        WordLinearMathNormalizer.DifferentialD => "differential",
        _ when IsNaryCharacter(character) => "nary",
        '■' or '⒨' or 'ⓢ' or '⒱' or '⒩' => "matrix",
        '√' => "radical",
        _ when char.IsLetter(character) => "letter",
        _ when char.IsDigit(character) => "digit",
        _ when char.IsWhiteSpace(character) => "space",
        _ => "operator",
    };

    private static string Canonicalize(string value)
    {
        var normalized = WordLinearMathNormalizer.NormalizeForWord(
            ExpandCompositeMarkers(value)
        );
        normalized = NormalizeUnicodeScriptCharacters(normalized);
        normalized = NormalizeQuotedTextWhitespace(normalized);
        normalized = RemoveFunctionApplicationGroups(normalized);
        normalized = RemoveRedundantSingleSymbolAccentGroups(normalized);
        normalized = AbsorbSafeCoefficientIntoFractionNumerator(normalized);
        normalized = RemoveRedundantMultiplicativeCoefficientGroups(normalized);
        normalized = RemoveEmptyPrescriptBase(normalized);
        normalized = RemoveRedundantWholePrescriptGroup(normalized);
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
            // Word emits U+2062 (INVISIBLE TIMES) when it builds an explicit
            // multiplication between adjacent factors.  LaTeX/source linear
            // math commonly leaves that multiplication implicit.  U+2062 has
            // no operator/order semantics (unlike ×, ·, or juxtaposed
            // cross-product notation), so it is safe to ignore for the
            // canonical readback contract.  Keep U+2061 function-application
            // handling and visible operators intact.
            if (!inQuotedText && character is '\u2061' or '\u2062' or '║')
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

    private static string NormalizeUnicodeScriptCharacters(string value)
    {
        var output = new StringBuilder(value.Length + 8);
        var inQuotedText = false;
        for (var index = 0; index < value.Length; index++)
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
                    output.Append("\"\"");
                    index++;
                    continue;
                }
                inQuotedText = !inQuotedText;
                output.Append(character);
                continue;
            }
            if (
                inQuotedText
                || !TryUnicodeScript(character, out var scriptKind, out var mapped)
            )
            {
                output.Append(character);
                continue;
            }

            var script = new StringBuilder().Append(mapped);
            while (
                index + 1 < value.Length
                && TryUnicodeScript(
                    value[index + 1],
                    out var nextKind,
                    out var nextMapped
                )
                && nextKind == scriptKind
            )
            {
                script.Append(nextMapped);
                index++;
            }
            output.Append(scriptKind).Append('(').Append(script).Append(')');
        }
        return output.ToString();
    }

    private static bool TryUnicodeScript(
        char value,
        out char kind,
        out char mapped
    )
    {
        var result = value switch
        {
            '⁰' => ('^', '0'),
            '¹' => ('^', '1'),
            '²' => ('^', '2'),
            '³' => ('^', '3'),
            '⁴' => ('^', '4'),
            '⁵' => ('^', '5'),
            '⁶' => ('^', '6'),
            '⁷' => ('^', '7'),
            '⁸' => ('^', '8'),
            '⁹' => ('^', '9'),
            '⁺' => ('^', '+'),
            '⁻' => ('^', '-'),
            '⁼' => ('^', '='),
            '⁽' => ('^', '('),
            '⁾' => ('^', ')'),
            'ⁱ' => ('^', 'i'),
            'ⁿ' => ('^', 'n'),
            '₀' => ('_', '0'),
            '₁' => ('_', '1'),
            '₂' => ('_', '2'),
            '₃' => ('_', '3'),
            '₄' => ('_', '4'),
            '₅' => ('_', '5'),
            '₆' => ('_', '6'),
            '₇' => ('_', '7'),
            '₈' => ('_', '8'),
            '₉' => ('_', '9'),
            '₊' => ('_', '+'),
            '₋' => ('_', '-'),
            '₌' => ('_', '='),
            '₍' => ('_', '('),
            '₎' => ('_', ')'),
            'ₐ' => ('_', 'a'),
            'ₑ' => ('_', 'e'),
            'ₕ' => ('_', 'h'),
            'ᵢ' => ('_', 'i'),
            'ⱼ' => ('_', 'j'),
            'ₖ' => ('_', 'k'),
            'ₗ' => ('_', 'l'),
            'ₘ' => ('_', 'm'),
            'ₙ' => ('_', 'n'),
            'ₒ' => ('_', 'o'),
            'ₚ' => ('_', 'p'),
            'ᵣ' => ('_', 'r'),
            'ₛ' => ('_', 's'),
            'ₜ' => ('_', 't'),
            'ₓ' => ('_', 'x'),
            _ => ('\0', '\0'),
        };
        kind = result.Item1;
        mapped = result.Item2;
        return kind != '\0';
    }

    private static string RemoveRedundantWholePrescriptGroup(string value)
    {
        var current = value;
        while (
            current.Length > 3
            && current[0] == '('
            && MatchingParenthesis(current, 0) == current.Length - 1
            && current[1] is '_' or '^'
            && current[2] == '('
        )
        {
            current = current[1..^1];
        }
        return current;
    }

    private static string RemoveEmptyPrescriptBase(string value)
    {
        if (!value.Contains("()_(", StringComparison.Ordinal)
            && !value.Contains("()^(", StringComparison.Ordinal))
        {
            return value;
        }
        var output = new StringBuilder(value.Length);
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
                    output.Append("\"\"");
                    index++;
                    continue;
                }
                inQuotedText = !inQuotedText;
                output.Append(value[index]);
                continue;
            }
            if (
                !inQuotedText
                && index + 3 < value.Length
                && value[index] == '('
                && value[index + 1] == ')'
                && value[index + 2] is '_' or '^'
                && value[index + 3] == '('
                && IsEmptyPrescriptBasePosition(value, index)
            )
            {
                index++;
                continue;
            }
            output.Append(value[index]);
        }
        return output.ToString();
    }

    private static bool IsEmptyPrescriptBasePosition(string value, int index)
    {
        for (var previous = index - 1; previous >= 0; previous--)
        {
            if (char.IsWhiteSpace(value[previous]))
            {
                continue;
            }
            return value[previous]
                is '(' or '[' or '{' or '=' or '+' or '-' or '*' or '/'
                    or ',' or ';' or ':' or '&' or '@' or '▒' or '┬' or '┴';
        }
        return true;
    }

    private static string RemoveRedundantSingleSymbolAccentGroups(string value)
    {
        if (value.Length < 4)
        {
            return value;
        }
        var output = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (
                index + 3 < value.Length
                && value[index] == '('
                && value[index + 2] == ')'
                && IsCombiningAccent(value[index + 3])
                && !char.IsWhiteSpace(value[index + 1])
                && value[index + 1] is not '(' and not ')'
            )
            {
                output.Append(value[index + 1]);
                output.Append(value[index + 3]);
                index += 3;
                continue;
            }
            output.Append(value[index]);
        }
        return output.ToString();
    }

    private static bool IsCombiningAccent(char value) => value is
        '\u20D7' or '\u0302' or '\u0305' or '\u0303' or '\u0307' or '\u0308'
            or '\u0301' or '\u0300' or '\u20DB' or '\u030C' or '\u0306'
            or '\u0332' or '\u20EF' or '\u032D' or '\u0330' or '\u0323' or '\u0324';

    private static string RemoveRedundantMultiplicativeCoefficientGroups(string value)
    {
        var current = value;
        var changed = true;
        while (changed)
        {
            changed = false;
            var output = new StringBuilder(current.Length);
            for (var index = 0; index < current.Length; index++)
            {
                if (current[index] != '(')
                {
                    output.Append(current[index]);
                    continue;
                }
                var closing = MatchingCoefficientParenthesis(current, index);
                if (
                    closing <= index + 1
                    || !IsCoefficientGroupPosition(current, index)
                    || !IsSafeMultiplicativeCoefficient(
                        current.AsSpan(index + 1, closing - index - 1)
                    )
                    || !IsMultiplicativeFollower(current, closing + 1)
                )
                {
                    output.Append(current[index]);
                    continue;
                }
                output.Append(current, index + 1, closing - index - 1);
                index = closing;
                changed = true;
            }
            current = output.ToString();
        }
        return current;
    }

    private static string AbsorbSafeCoefficientIntoFractionNumerator(string value)
    {
        var current = value;
        for (var slash = 1; slash + 1 < current.Length; slash++)
        {
            if (current[slash] != '/' || current[slash - 1] != ')')
            {
                continue;
            }
            var numeratorOpen = MatchingOpeningParenthesis(current, slash - 1);
            if (numeratorOpen <= 0)
            {
                continue;
            }
            var prefixStart = MultiplicativePrefixStart(current, numeratorOpen);
            if (prefixStart >= numeratorOpen)
            {
                continue;
            }
            var prefix = current.AsSpan(prefixStart, numeratorOpen - prefixStart);
            if (
                !IsSafeMultiplicativeCoefficient(prefix)
                || !prefix.Contains("_(".AsSpan(), StringComparison.Ordinal)
                || prefix.IndexOf('"') >= 0
            )
            {
                continue;
            }
            var numeratorGroup = current.AsSpan(
                numeratorOpen,
                slash - numeratorOpen
            );
            var rewritten = new StringBuilder(current.Length + 2);
            rewritten.Append(current.AsSpan(0, prefixStart));
            rewritten.Append('(');
            rewritten.Append(prefix);
            rewritten.Append(numeratorGroup);
            rewritten.Append(')');
            rewritten.Append(current.AsSpan(slash));
            current = rewritten.ToString();
            slash = prefixStart;
        }
        return current;
    }

    private static int MatchingOpeningParenthesis(string value, int closing)
    {
        var depth = 0;
        for (var index = closing; index >= 0; index--)
        {
            if (value[index] == ')') depth++;
            else if (value[index] == '(' && --depth == 0) return index;
        }
        return -1;
    }

    private static int MultiplicativePrefixStart(string value, int end)
    {
        var depth = 0;
        for (var index = end - 1; index >= 0; index--)
        {
            var character = value[index];
            if (character == ')') { depth++; continue; }
            if (character == '(') { if (depth > 0) depth--; continue; }
            if (depth == 0 && character is '+' or '-' or '=' or ',' or ';')
            {
                return index + 1;
            }
        }
        return 0;
    }

    private static int MatchingCoefficientParenthesis(string value, int opening)
    {
        var depth = 0;
        for (var index = opening; index < value.Length; index++)
        {
            if (value[index] == '(') depth++;
            else if (value[index] == ')' && --depth == 0) return index;
        }
        return -1;
    }

    private static bool IsSafeMultiplicativeCoefficient(ReadOnlySpan<char> value)
    {
        var depth = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '(') { depth++; continue; }
            if (character == ')') { depth--; if (depth < 0) return false; continue; }
            if (
                depth == 0
                && character is '+' or '-' or '=' or '/' or ',' or ';' or ':'
            )
            {
                return false;
            }
        }
        return depth == 0;
    }

    private static bool IsCoefficientGroupPosition(string value, int opening)
    {
        var index = opening - 1;
        while (index >= 0 && char.IsWhiteSpace(value[index])) index--;
        return index < 0 || value[index] is '+' or '-' or '=' or '(' or '[' or '{' or ',';
    }

    private static bool IsMultiplicativeFollower(string value, int index)
    {
        while (index < value.Length && char.IsWhiteSpace(value[index])) index++;
        if (index >= value.Length) return false;
        return value[index] is not ('+' or '-' or '=' or '/' or '^' or '_' or ')' or ',' or ';');
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

    private static IReadOnlyList<int> CountTopLevelIntegralOperandDifferentialCounts(
        string value
    )
    {
        var operands = new List<(int Operator, int Opening, int Closing, int Count)>();
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
            var count = 0;
            for (var cursor = opening + 1; cursor < closing; cursor++)
            {
                if (value[cursor] == WordLinearMathNormalizer.DifferentialD)
                {
                    count++;
                }
            }
            operands.Add((index, opening, closing, count));
        }
        return operands
            .Where(candidate => !operands.Any(parent =>
                parent.Opening < candidate.Operator
                && candidate.Operator < parent.Closing
            ))
            .Select(candidate => candidate.Count)
            .ToArray();
    }

    private static IReadOnlyList<int> CountActualTopLevelIntegralDifferentialCounts(
        XElement equation
    )
    {
        var counts = new List<int>();
        CollectActualTopLevelIntegralDifferentialCounts(equation, counts);
        return counts;
    }

    private static void CollectActualTopLevelIntegralDifferentialCounts(
        XElement container,
        List<int> counts
    )
    {
        var children = container.Elements().ToArray();
        for (var index = 0; index < children.Length; index++)
        {
            var child = children[index];
            if (!IsMathElement(child, "nary") || !IsIntegralNary(child))
            {
                CollectActualTopLevelIntegralDifferentialCounts(child, counts);
                continue;
            }

            var count = child.Elements()
                .Where(item => IsMathElement(item, "e"))
                .Descendants()
                .Where(item => IsMathElement(item, "t"))
                .Sum(item => item.Value.Count(character =>
                    character == WordLinearMathNormalizer.DifferentialD));
            for (var sibling = index + 1; sibling < children.Length; sibling++)
            {
                if (IsMathElement(children[sibling], "nary")
                    && IsIntegralNary(children[sibling]))
                {
                    break;
                }
                count += children[sibling].DescendantsAndSelf()
                    .Where(item => IsMathElement(item, "t"))
                    .Where(item => !item.Ancestors().Any(ancestor =>
                        IsMathElement(ancestor, "nary") && IsIntegralNary(ancestor)))
                    .Sum(item => item.Value.Count(character =>
                        character == WordLinearMathNormalizer.DifferentialD));
            }
            counts.Add(count);
        }
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
        character is '∫' or '∬' or '∭' or '∮' or '∱' or '∲' or '∳' or '∯' or '∰'
            or '\u2A0C' or '\u2A0D' or '\u2A0E' or '\u2A0F'
            or '\u2A10' or '\u2A11' or '\u2A12' or '\u2A13'
            or '\u2A14' or '\u2A15' or '\u2A16' or '\u2A17'
            or '\u2A18' or '\u2A19' or '\u2A1A' or '\u2A1B' or '\u2A1C';

    private static bool IsNaryCharacter(char character) =>
        character is '∑' or '∏' or '∐' or '⋃' or '⋂'
            or '⨁' or '⨂' or '⨀' or '⨄' or '⨆' or '⋁' or '⋀'
            || IsIntegralCharacter(character);

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
