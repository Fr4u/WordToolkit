using System.Text;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Equations;

internal enum EquationMathStyle
{
    Plain,
    Bold,
    Italic,
    BoldItalic,
}

internal enum EquationStyleTarget
{
    RunsAndControls,
    RunsOnly,
    FirstControl,
}

internal readonly record struct EquationStyleRegion(
    EquationMathStyle Style,
    EquationStyleTarget Target
)
{
    internal bool AppliesToRuns => Target is not EquationStyleTarget.FirstControl;

    internal bool AppliesToControls => Target is not EquationStyleTarget.RunsOnly;
}

internal readonly record struct EquationStyleCounts
{
    internal EquationStyleCounts(int Bold, int BoldItalic)
    {
        BoldRunsAndControls = Bold;
        BoldItalicRunsAndControls = BoldItalic;
    }

    internal int PlainRunsAndControls { get; init; }

    internal int BoldRunsAndControls { get; init; }

    internal int ItalicRunsAndControls { get; init; }

    internal int BoldItalicRunsAndControls { get; init; }

    internal int PlainRunsOnly { get; init; }

    internal int BoldRunsOnly { get; init; }

    internal int ItalicRunsOnly { get; init; }

    internal int BoldItalicRunsOnly { get; init; }

    internal int PlainFirstControl { get; init; }

    internal int BoldFirstControl { get; init; }

    internal int ItalicFirstControl { get; init; }

    internal int BoldItalicFirstControl { get; init; }

    internal int Plain => checked(PlainRunsAndControls + PlainRunsOnly + PlainFirstControl);

    internal int Bold => checked(BoldRunsAndControls + BoldRunsOnly + BoldFirstControl);

    internal int Italic => checked(ItalicRunsAndControls + ItalicRunsOnly + ItalicFirstControl);

    internal int BoldItalic => checked(
        BoldItalicRunsAndControls + BoldItalicRunsOnly + BoldItalicFirstControl
    );

    internal int RunsAndControls => checked(
        PlainRunsAndControls
            + BoldRunsAndControls
            + ItalicRunsAndControls
            + BoldItalicRunsAndControls
    );

    internal int RunsOnly => checked(
        PlainRunsOnly + BoldRunsOnly + ItalicRunsOnly + BoldItalicRunsOnly
    );

    internal int FirstControl => checked(
        PlainFirstControl + BoldFirstControl + ItalicFirstControl + BoldItalicFirstControl
    );

    internal int Total => checked(RunsAndControls + RunsOnly + FirstControl);

    internal int For(EquationStyleRegion region) =>
        (region.Style, region.Target) switch
        {
            (EquationMathStyle.Plain, EquationStyleTarget.RunsAndControls) =>
                PlainRunsAndControls,
            (EquationMathStyle.Bold, EquationStyleTarget.RunsAndControls) =>
                BoldRunsAndControls,
            (EquationMathStyle.Italic, EquationStyleTarget.RunsAndControls) =>
                ItalicRunsAndControls,
            (EquationMathStyle.BoldItalic, EquationStyleTarget.RunsAndControls) =>
                BoldItalicRunsAndControls,
            (EquationMathStyle.Plain, EquationStyleTarget.RunsOnly) => PlainRunsOnly,
            (EquationMathStyle.Bold, EquationStyleTarget.RunsOnly) => BoldRunsOnly,
            (EquationMathStyle.Italic, EquationStyleTarget.RunsOnly) => ItalicRunsOnly,
            (EquationMathStyle.BoldItalic, EquationStyleTarget.RunsOnly) => BoldItalicRunsOnly,
            (EquationMathStyle.Plain, EquationStyleTarget.FirstControl) => PlainFirstControl,
            (EquationMathStyle.Bold, EquationStyleTarget.FirstControl) => BoldFirstControl,
            (EquationMathStyle.Italic, EquationStyleTarget.FirstControl) => ItalicFirstControl,
            (EquationMathStyle.BoldItalic, EquationStyleTarget.FirstControl) =>
                BoldItalicFirstControl,
            _ => 0,
        };

    internal static EquationStyleCounts From(IEnumerable<EquationStyleRegion> regions)
    {
        var counts = new int[12];
        foreach (var region in regions)
        {
            counts[Index(region)] = checked(counts[Index(region)] + 1);
        }
        return new EquationStyleCounts
        {
            PlainRunsAndControls = counts[0],
            BoldRunsAndControls = counts[1],
            ItalicRunsAndControls = counts[2],
            BoldItalicRunsAndControls = counts[3],
            PlainRunsOnly = counts[4],
            BoldRunsOnly = counts[5],
            ItalicRunsOnly = counts[6],
            BoldItalicRunsOnly = counts[7],
            PlainFirstControl = counts[8],
            BoldFirstControl = counts[9],
            ItalicFirstControl = counts[10],
            BoldItalicFirstControl = counts[11],
        };
    }

    private static int Index(EquationStyleRegion region) =>
        checked((int)region.Target * 4 + (int)region.Style);
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
    // Private-use build sentinels. Real Word preserves them as ordinary m:t nodes
    // through OMath.BuildUp(); they are removed before an equation is exposed or saved.
    // The original four values are intentionally stable for previously verified LaTeX.
    private const char BoldBothStart = '\uE100';
    private const char BoldBothEnd = '\uE101';
    private const char BoldItalicBothStart = '\uE102';
    private const char BoldItalicBothEnd = '\uE103';
    private const char PlainBothStart = '\uE104';
    private const char PlainBothEnd = '\uE105';
    private const char ItalicBothStart = '\uE106';
    private const char ItalicBothEnd = '\uE107';
    private const char PlainRunsStart = '\uE108';
    private const char PlainRunsEnd = '\uE109';
    private const char BoldRunsStart = '\uE10A';
    private const char BoldRunsEnd = '\uE10B';
    private const char ItalicRunsStart = '\uE10C';
    private const char ItalicRunsEnd = '\uE10D';
    private const char BoldItalicRunsStart = '\uE10E';
    private const char BoldItalicRunsEnd = '\uE10F';
    private const char PlainControlStart = '\uE110';
    private const char PlainControlEnd = '\uE111';
    private const char BoldControlStart = '\uE112';
    private const char BoldControlEnd = '\uE113';
    private const char ItalicControlStart = '\uE114';
    private const char ItalicControlEnd = '\uE115';
    private const char BoldItalicControlStart = '\uE116';
    private const char BoldItalicControlEnd = '\uE117';

    private static readonly HashSet<char> Reserved =
    [
        BoldBothStart,
        BoldBothEnd,
        BoldItalicBothStart,
        BoldItalicBothEnd,
        PlainBothStart,
        PlainBothEnd,
        ItalicBothStart,
        ItalicBothEnd,
        PlainRunsStart,
        PlainRunsEnd,
        BoldRunsStart,
        BoldRunsEnd,
        ItalicRunsStart,
        ItalicRunsEnd,
        BoldItalicRunsStart,
        BoldItalicRunsEnd,
        PlainControlStart,
        PlainControlEnd,
        BoldControlStart,
        BoldControlEnd,
        ItalicControlStart,
        ItalicControlEnd,
        BoldItalicControlStart,
        BoldItalicControlEnd,
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
        var stack = new Stack<EquationStyleRegion>();
        var regions = new List<EquationStyleRegion>();
        foreach (var character in markedLinear)
        {
            if (TryStart(character, out var started))
            {
                stack.Push(started);
                regions.Add(started);
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
            EquationStyleCounts.From(regions)
        );
    }

    internal static string Wrap(EquationMathStyle style, string body) =>
        Wrap(style, EquationStyleTarget.RunsAndControls, body);

    internal static string Wrap(
        EquationMathStyle style,
        EquationStyleTarget target,
        string body
    )
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw Invalid("A styled equation region cannot be empty");
        }
        var (start, end) = MarkerPair(new EquationStyleRegion(style, target));
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

    internal static bool TryStart(char value, out EquationStyleRegion region)
    {
        region = value switch
        {
            BoldBothStart => Both(EquationMathStyle.Bold),
            BoldItalicBothStart => Both(EquationMathStyle.BoldItalic),
            PlainBothStart => Both(EquationMathStyle.Plain),
            ItalicBothStart => Both(EquationMathStyle.Italic),
            PlainRunsStart => Runs(EquationMathStyle.Plain),
            BoldRunsStart => Runs(EquationMathStyle.Bold),
            ItalicRunsStart => Runs(EquationMathStyle.Italic),
            BoldItalicRunsStart => Runs(EquationMathStyle.BoldItalic),
            PlainControlStart => Control(EquationMathStyle.Plain),
            BoldControlStart => Control(EquationMathStyle.Bold),
            ItalicControlStart => Control(EquationMathStyle.Italic),
            BoldItalicControlStart => Control(EquationMathStyle.BoldItalic),
            _ => default,
        };
        return value
            is BoldBothStart
                or BoldItalicBothStart
                or PlainBothStart
                or ItalicBothStart
                or PlainRunsStart
                or BoldRunsStart
                or ItalicRunsStart
                or BoldItalicRunsStart
                or PlainControlStart
                or BoldControlStart
                or ItalicControlStart
                or BoldItalicControlStart;
    }

    internal static bool TryEnd(char value, out EquationStyleRegion region)
    {
        region = value switch
        {
            BoldBothEnd => Both(EquationMathStyle.Bold),
            BoldItalicBothEnd => Both(EquationMathStyle.BoldItalic),
            PlainBothEnd => Both(EquationMathStyle.Plain),
            ItalicBothEnd => Both(EquationMathStyle.Italic),
            PlainRunsEnd => Runs(EquationMathStyle.Plain),
            BoldRunsEnd => Runs(EquationMathStyle.Bold),
            ItalicRunsEnd => Runs(EquationMathStyle.Italic),
            BoldItalicRunsEnd => Runs(EquationMathStyle.BoldItalic),
            PlainControlEnd => Control(EquationMathStyle.Plain),
            BoldControlEnd => Control(EquationMathStyle.Bold),
            ItalicControlEnd => Control(EquationMathStyle.Italic),
            BoldItalicControlEnd => Control(EquationMathStyle.BoldItalic),
            _ => default,
        };
        return value
            is BoldBothEnd
                or BoldItalicBothEnd
                or PlainBothEnd
                or ItalicBothEnd
                or PlainRunsEnd
                or BoldRunsEnd
                or ItalicRunsEnd
                or BoldItalicRunsEnd
                or PlainControlEnd
                or BoldControlEnd
                or ItalicControlEnd
                or BoldItalicControlEnd;
    }

    internal static bool IsReserved(char value) => Reserved.Contains(value);

    private static (char Start, char End) MarkerPair(EquationStyleRegion region) =>
        (region.Style, region.Target) switch
        {
            (EquationMathStyle.Bold, EquationStyleTarget.RunsAndControls) =>
                (BoldBothStart, BoldBothEnd),
            (EquationMathStyle.BoldItalic, EquationStyleTarget.RunsAndControls) =>
                (BoldItalicBothStart, BoldItalicBothEnd),
            (EquationMathStyle.Plain, EquationStyleTarget.RunsAndControls) =>
                (PlainBothStart, PlainBothEnd),
            (EquationMathStyle.Italic, EquationStyleTarget.RunsAndControls) =>
                (ItalicBothStart, ItalicBothEnd),
            (EquationMathStyle.Plain, EquationStyleTarget.RunsOnly) =>
                (PlainRunsStart, PlainRunsEnd),
            (EquationMathStyle.Bold, EquationStyleTarget.RunsOnly) =>
                (BoldRunsStart, BoldRunsEnd),
            (EquationMathStyle.Italic, EquationStyleTarget.RunsOnly) =>
                (ItalicRunsStart, ItalicRunsEnd),
            (EquationMathStyle.BoldItalic, EquationStyleTarget.RunsOnly) =>
                (BoldItalicRunsStart, BoldItalicRunsEnd),
            (EquationMathStyle.Plain, EquationStyleTarget.FirstControl) =>
                (PlainControlStart, PlainControlEnd),
            (EquationMathStyle.Bold, EquationStyleTarget.FirstControl) =>
                (BoldControlStart, BoldControlEnd),
            (EquationMathStyle.Italic, EquationStyleTarget.FirstControl) =>
                (ItalicControlStart, ItalicControlEnd),
            (EquationMathStyle.BoldItalic, EquationStyleTarget.FirstControl) =>
                (BoldItalicControlStart, BoldItalicControlEnd),
            _ => throw Invalid("Equation style region is not supported"),
        };

    private static EquationStyleRegion Both(EquationMathStyle style) =>
        new(style, EquationStyleTarget.RunsAndControls);

    private static EquationStyleRegion Runs(EquationMathStyle style) =>
        new(style, EquationStyleTarget.RunsOnly);

    private static EquationStyleRegion Control(EquationMathStyle style) =>
        new(style, EquationStyleTarget.FirstControl);

    private static NativeToolException Invalid(string message, object? details = null) =>
        new("EQUATION_INVALID", message, details);
}
