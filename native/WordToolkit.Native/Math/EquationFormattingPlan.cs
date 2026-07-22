using System.Text;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Equations;

internal enum EquationMathStyle
{
    Bold,
    BoldItalic,
}

internal readonly record struct EquationStyleCounts(int Bold, int BoldItalic)
{
    internal int Total => checked(Bold + BoldItalic);

    internal int For(EquationMathStyle style) =>
        style == EquationMathStyle.Bold ? Bold : BoldItalic;
}

internal sealed record EquationConversionPlan(
    string Linear,
    string BuildLinear,
    EquationStyleCounts StyleCounts
)
{
    internal bool HasFormatting => StyleCounts.Total > 0;
}

internal static class EquationFormattingMarkers
{
    // These private-use characters are internal build sentinels. Real Word preserves
    // them as ordinary m:t nodes through OMath.BuildUp(), after which they are removed
    // before the equation is exposed or saved.
    private const char BoldStart = '\uE100';
    private const char BoldEnd = '\uE101';
    private const char BoldItalicStart = '\uE102';
    private const char BoldItalicEnd = '\uE103';

    private static readonly HashSet<char> Reserved =
    [
        BoldStart,
        BoldEnd,
        BoldItalicStart,
        BoldItalicEnd,
    ];

    internal static EquationConversionPlan Unstyled(string linear, string inputFormat)
    {
        RejectReservedInput(linear, inputFormat);
        return new EquationConversionPlan(linear, linear, default);
    }

    internal static EquationConversionPlan FromMarkedLinear(string markedLinear)
    {
        ArgumentNullException.ThrowIfNull(markedLinear);
        var clean = new StringBuilder(markedLinear.Length);
        var stack = new Stack<EquationMathStyle>();
        var bold = 0;
        var boldItalic = 0;
        foreach (var character in markedLinear)
        {
            if (TryStart(character, out var started))
            {
                stack.Push(started);
                if (started == EquationMathStyle.Bold)
                {
                    bold++;
                }
                else
                {
                    boldItalic++;
                }
                continue;
            }
            if (TryEnd(character, out var ended))
            {
                if (stack.Count == 0 || stack.Pop() != ended)
                {
                    throw Invalid("Equation formatting markers are unbalanced");
                }
                continue;
            }
            clean.Append(character);
        }
        if (stack.Count != 0)
        {
            throw Invalid("Equation formatting markers are unbalanced");
        }
        return new EquationConversionPlan(
            clean.ToString(),
            markedLinear,
            new EquationStyleCounts(bold, boldItalic)
        );
    }

    internal static string Wrap(EquationMathStyle style, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw Invalid("A styled LaTeX argument cannot be empty");
        }
        var (start, end) = style switch
        {
            EquationMathStyle.Bold => (BoldStart, BoldEnd),
            _ => (BoldItalicStart, BoldItalicEnd),
        };
        return string.Concat(start, body.Trim(), end);
    }

    internal static void RejectReservedInput(string value, string inputFormat)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Any(Reserved.Contains))
        {
            throw Invalid(
                "Equation input contains a reserved internal formatting marker",
                new { input_format = inputFormat }
            );
        }
    }

    internal static bool TryStart(char value, out EquationMathStyle style)
    {
        style = value switch
        {
            BoldStart => EquationMathStyle.Bold,
            BoldItalicStart => EquationMathStyle.BoldItalic,
            _ => default,
        };
        return value is BoldStart or BoldItalicStart;
    }

    internal static bool TryEnd(char value, out EquationMathStyle style)
    {
        style = value switch
        {
            BoldEnd => EquationMathStyle.Bold,
            BoldItalicEnd => EquationMathStyle.BoldItalic,
            _ => default,
        };
        return value is BoldEnd or BoldItalicEnd;
    }

    internal static bool IsReserved(char value) => Reserved.Contains(value);

    private static NativeToolException Invalid(string message, object? details = null) =>
        new("EQUATION_INVALID", message, details);
}
