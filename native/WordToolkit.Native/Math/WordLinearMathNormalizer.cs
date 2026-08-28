using System.Text;

namespace WordToolkit.Native.Equations;

internal static class WordLinearMathNormalizer
{
    internal const char DifferentialD = '\u2146';
    internal const char InvisibleTimes = '\u2062';

    internal static string NormalizeForWord(string value)
    {
        var normalized = NormalizePrescriptBoundaries(value);
        normalized = NormalizeDifferentialSpacing(normalized);
        return GroupIntegralOperands(normalized);
    }

    private static string NormalizePrescriptBoundaries(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var output = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            if (
                value[index] is not ('_' or '^')
                || !IsPrescriptStart(value, index)
                || !TryReadPrescript(value, index, out var end, out var scripts)
            )
            {
                output.Append(value[index]);
                continue;
            }

            output.Append(scripts);
            var baseStart = end;
            while (baseStart < value.Length && char.IsWhiteSpace(value[baseStart]))
            {
                baseStart++;
            }
            if (
                baseStart < value.Length
                && value[baseStart] != InvisibleTimes
            )
            {
                output.Append(InvisibleTimes);
            }
            index = baseStart - 1;
        }
        return output.ToString();
    }

    private static bool IsPrescriptStart(string value, int index)
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

    private static bool TryReadPrescript(
        string value,
        int start,
        out int end,
        out string scripts
    )
    {
        var cursor = start;
        var output = new StringBuilder();
        var count = 0;
        while (cursor < value.Length && value[cursor] is '_' or '^' && count < 2)
        {
            var marker = value[cursor++];
            while (cursor < value.Length && char.IsWhiteSpace(value[cursor]))
            {
                cursor++;
            }
            if (!TryReadScriptArgument(value, ref cursor, out var argument))
            {
                end = start;
                scripts = "";
                return false;
            }
            output.Append(marker).Append('(').Append(argument).Append(')');
            count++;
            while (cursor < value.Length && char.IsWhiteSpace(value[cursor]))
            {
                cursor++;
            }
        }
        if (count == 0 || cursor >= value.Length)
        {
            end = start;
            scripts = "";
            return false;
        }
        end = cursor;
        scripts = output.ToString();
        return true;
    }

    private static bool TryReadScriptArgument(
        string value,
        ref int cursor,
        out string argument
    )
    {
        if (cursor >= value.Length)
        {
            argument = "";
            return false;
        }
        if (value[cursor] != '(')
        {
            var length = char.IsHighSurrogate(value[cursor])
                && cursor + 1 < value.Length
                && char.IsLowSurrogate(value[cursor + 1])
                    ? 2
                    : 1;
            argument = value.Substring(cursor, length);
            cursor += length;
            return true;
        }
        var opening = cursor++;
        var depth = 1;
        var inQuotedText = false;
        while (cursor < value.Length)
        {
            var character = value[cursor++];
            if (character == '"')
            {
                if (
                    inQuotedText
                    && cursor < value.Length
                    && value[cursor] == '"'
                )
                {
                    cursor++;
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
                argument = value[(opening + 1)..(cursor - 1)];
                return true;
            }
        }
        argument = "";
        return false;
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
            // UnicodeMath structure separators (eqarray/matrix/cases and
            // above/below constructs) terminate an ungrouped integral body.
            // Do not let a differential in the next cell/row get absorbed.
            if (depth == 0 && IsStructureSeparator(character))
            {
                operandEnd = -1;
                return false;
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
        character is '∫' or '∬' or '∭' or '∮' or '∱' or '∲' or '∳' or '∯' or '∰'
            // UTN28/UnicodeMath additional contour, surface and volume
            // integral glyphs. They have one differential unless explicitly
            // represented by the double/triple glyphs above.
            or '\u2A0C' or '\u2A0D' or '\u2A0E' or '\u2A0F'
            or '\u2A10' or '\u2A11' or '\u2A12' or '\u2A13'
            or '\u2A14' or '\u2A15' or '\u2A16' or '\u2A17'
            or '\u2A18' or '\u2A19' or '\u2A1A' or '\u2A1B' or '\u2A1C';

    private static int DifferentialCount(char integralOperator) =>
        integralOperator switch
        {
            '∬' => 2,
            '∭' => 3,
            '∯' => 2,
            '∰' => 3,
            '\u2A0C' => 4, // quadruple integral
            _ => 1,
        };

    private static bool IsOpeningDelimiter(char character) =>
        character is '(' or '[' or '{' or '〖';

    private static bool IsClosingDelimiter(char character) =>
        character is ')' or ']' or '}' or '〗';

    private static bool IsStructureSeparator(char character) =>
        character is '&' or '@' or '┴' or '┬';

}
