using System.Text;

namespace WordToolkit.Native.Equations;

internal static class WordLinearMathNormalizer
{
    internal const char DifferentialD = '\u2146';

    internal static string NormalizeForWord(string value)
    {
        var normalized = NormalizeDifferentialSpacing(value);
        return GroupIntegralOperands(normalized);
    }

    internal static string NormalizeDifferentialSpacing(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.Contains(DifferentialD, StringComparison.Ordinal))
        {
            return value;
        }

        var output = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == DifferentialD)
            {
                while (output.Length > 0 && char.IsWhiteSpace(output[^1]))
                {
                    output.Length--;
                }
                if (
                    output.Length > 0
                    && output[^1] is not ('▒' or '(' or '[' or '{' or '&' or '@')
                )
                {
                    output.Append(' ');
                }
                output.Append(character);
                continue;
            }
            if (!char.IsWhiteSpace(character))
            {
                output.Append(character);
                continue;
            }

            var previous = PreviousNonWhitespace(value, index - 1);
            if (previous == DifferentialD)
            {
                continue;
            }
            output.Append(character);
        }
        return output.ToString();
    }

    private static char? PreviousNonWhitespace(string value, int index)
    {
        while (index >= 0)
        {
            if (!char.IsWhiteSpace(value[index]))
            {
                return value[index];
            }
            index--;
        }
        return null;
    }

    private static string GroupIntegralOperands(string value)
    {
        if (
            !value.Contains(DifferentialD, StringComparison.Ordinal)
            || !value.Any(IsIntegralOperator)
        )
        {
            return value;
        }

        var output = new StringBuilder(value);
        var searchBefore = output.Length;
        while (TryFindPreviousIntegral(output, searchBefore, out var integralIndex))
        {
            searchBefore = integralIndex;
            if (
                !TryFindNaryBodySeparator(output, integralIndex, out var bodySeparator)
                || !TryFindIntegralOperandEnd(
                    output,
                    bodySeparator + 1,
                    DifferentialCount(output[integralIndex]),
                    out var operandEnd
                )
            )
            {
                continue;
            }

            output.Insert(operandEnd, '〗');
            output.Insert(bodySeparator + 1, '〖');
        }
        return output.ToString();
    }

    private static bool TryFindPreviousIntegral(
        StringBuilder value,
        int searchBefore,
        out int index
    )
    {
        for (index = Math.Min(searchBefore, value.Length) - 1; index >= 0; index--)
        {
            if (IsIntegralOperator(value[index]))
            {
                return true;
            }
        }
        index = -1;
        return false;
    }

    private static bool TryFindNaryBodySeparator(
        StringBuilder value,
        int integralIndex,
        out int separatorIndex
    )
    {
        var depth = 0;
        var inQuotedText = false;
        for (var index = integralIndex + 1; index < value.Length; index++)
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
            if (IsOpeningDelimiter(character))
            {
                depth++;
                continue;
            }
            if (IsClosingDelimiter(character))
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }
            if (character == '▒' && depth == 0)
            {
                separatorIndex = index;
                return true;
            }
        }
        separatorIndex = -1;
        return false;
    }

    private static bool TryFindIntegralOperandEnd(
        StringBuilder value,
        int bodyStart,
        int requiredDifferentials,
        out int operandEnd
    )
    {
        var depth = 0;
        var inQuotedText = false;
        var differentialCount = CountDifferentialsInLeadingGroup(value, bodyStart);
        if (differentialCount >= requiredDifferentials)
        {
            operandEnd = -1;
            return false;
        }
        for (var index = bodyStart; index < value.Length; index++)
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
            if (IsOpeningDelimiter(character))
            {
                depth++;
                continue;
            }
            if (IsClosingDelimiter(character))
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }
            if (character != DifferentialD || depth != 0)
            {
                continue;
            }

            var variableEnd = ReadDifferentialVariableEnd(value, index + 1);
            if (variableEnd < 0)
            {
                operandEnd = -1;
                return false;
            }
            differentialCount++;
            if (differentialCount == requiredDifferentials)
            {
                operandEnd = variableEnd;
                return true;
            }
            index = variableEnd - 1;
        }
        operandEnd = -1;
        return false;
    }

    private static int CountDifferentialsInLeadingGroup(
        StringBuilder value,
        int bodyStart
    )
    {
        var start = bodyStart;
        while (start < value.Length && char.IsWhiteSpace(value[start]))
        {
            start++;
        }
        if (start >= value.Length || value[start] != '〖')
        {
            return 0;
        }

        var depth = 1;
        var count = 0;
        for (var index = start + 1; index < value.Length; index++)
        {
            if (value[index] == '〖')
            {
                depth++;
            }
            else if (value[index] == '〗' && --depth == 0)
            {
                return count;
            }
            else if (value[index] == DifferentialD)
            {
                count++;
            }
        }
        return 0;
    }

    private static int ReadDifferentialVariableEnd(StringBuilder value, int start)
    {
        var index = start;
        while (index < value.Length && char.IsWhiteSpace(value[index]))
        {
            index++;
        }
        if (index >= value.Length)
        {
            return -1;
        }

        index = ReadAtom(value, index);
        if (index < 0)
        {
            return -1;
        }
        while (index < value.Length)
        {
            if (char.GetUnicodeCategory(value[index]) == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                index++;
                continue;
            }
            if (value[index] is not ('_' or '^'))
            {
                break;
            }
            var scriptEnd = ReadAtom(value, index + 1);
            if (scriptEnd < 0)
            {
                return -1;
            }
            index = scriptEnd;
        }
        return index;
    }

    private static int ReadAtom(StringBuilder value, int start)
    {
        if (start >= value.Length)
        {
            return -1;
        }
        var character = value[start];
        if (IsOpeningDelimiter(character))
        {
            var depth = 1;
            for (var index = start + 1; index < value.Length; index++)
            {
                if (IsOpeningDelimiter(value[index]))
                {
                    depth++;
                }
                else if (IsClosingDelimiter(value[index]) && --depth == 0)
                {
                    return index + 1;
                }
            }
            return -1;
        }
        if (character == '\\')
        {
            var index = start + 1;
            while (index < value.Length && char.IsLetter(value[index]))
            {
                index++;
            }
            return index == start + 1 ? Math.Min(start + 2, value.Length) : index;
        }
        return char.IsHighSurrogate(character)
            && start + 1 < value.Length
            && char.IsLowSurrogate(value[start + 1])
            ? start + 2
            : start + 1;
    }

    private static bool IsIntegralOperator(char character) =>
        character is '∫' or '∬' or '∭' or '∮';

    private static int DifferentialCount(char integralOperator) =>
        integralOperator switch
        {
            '∬' => 2,
            '∭' => 3,
            _ => 1,
        };

    private static bool IsOpeningDelimiter(char character) =>
        character is '(' or '[' or '{' or '〖';

    private static bool IsClosingDelimiter(char character) =>
        character is ')' or ']' or '}' or '〗';

}
