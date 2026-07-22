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
        '\u20D7',
        '\u0302',
        '\u0305',
        '\u0303',
        '\u0307',
        '\u0308',
    ];

    internal static bool RequiresReadback(string linear)
    {
        ArgumentNullException.ThrowIfNull(linear);
        return linear.Any(ReadbackSensitiveCharacters.Contains);
    }

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
            var placementVerified = differentialTextNodes.All(element =>
                element.Ancestors().Any(ancestor =>
                    IsMathElement(ancestor, "e")
                    && ancestor.Parent is { } parent
                    && IsMathElement(parent, "nary")
                )
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
            if (!inQuotedText && char.IsWhiteSpace(character))
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
