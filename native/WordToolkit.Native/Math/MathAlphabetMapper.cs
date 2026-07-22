using System.Text;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Equations;

internal enum MathAlphabetStyle
{
    Script,
    Fraktur,
    DoubleStruck,
    SansSerif,
    Monospace,
}

internal static class MathAlphabetMapper
{
    private static readonly IReadOnlyDictionary<char, int> ScriptExceptions =
        new Dictionary<char, int>
        {
            ['B'] = 0x212C,
            ['E'] = 0x2130,
            ['F'] = 0x2131,
            ['H'] = 0x210B,
            ['I'] = 0x2110,
            ['L'] = 0x2112,
            ['M'] = 0x2133,
            ['R'] = 0x211B,
            ['e'] = 0x212F,
            ['g'] = 0x210A,
            ['l'] = 0x2113,
            ['o'] = 0x2134,
        };

    private static readonly IReadOnlyDictionary<char, int> FrakturExceptions =
        new Dictionary<char, int>
        {
            ['C'] = 0x212D,
            ['H'] = 0x210C,
            ['I'] = 0x2111,
            ['R'] = 0x211C,
            ['Z'] = 0x2128,
        };

    private static readonly IReadOnlyDictionary<char, int> DoubleStruckExceptions =
        new Dictionary<char, int>
        {
            ['C'] = 0x2102,
            ['H'] = 0x210D,
            ['N'] = 0x2115,
            ['P'] = 0x2119,
            ['Q'] = 0x211A,
            ['R'] = 0x211D,
            ['Z'] = 0x2124,
        };

    private static readonly HashSet<int> LegacyCharacters =
    [
        .. ScriptExceptions.Values,
        .. FrakturExceptions.Values,
        .. DoubleStruckExceptions.Values,
    ];

    internal static string Apply(string value, MathAlphabetStyle style)
    {
        ArgumentNullException.ThrowIfNull(value);
        var output = new StringBuilder(value.Length * 2);
        var mapped = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Value <= 0x7F && char.IsLetterOrDigit((char)rune.Value))
            {
                var codePoint = MapAscii((char)rune.Value, style);
                if (codePoint < 0)
                {
                    throw Unsupported(style, rune);
                }
                output.Append(char.ConvertFromUtf32(codePoint));
                mapped++;
                continue;
            }
            if (Rune.IsLetterOrDigit(rune))
            {
                throw Unsupported(style, rune);
            }
            output.Append(rune.ToString());
        }
        if (mapped == 0)
        {
            throw new NativeToolException(
                "EQUATION_INVALID",
                "A mathematical alphabet command must contain at least one supported Latin letter or digit",
                new { alphabet = Name(style) }
            );
        }
        return output.ToString();
    }

    internal static bool TryFromOmmlScript(
        string value,
        string script,
        out string converted
    )
    {
        var style = script switch
        {
            "script" => MathAlphabetStyle.Script,
            "fraktur" => MathAlphabetStyle.Fraktur,
            "double-struck" => MathAlphabetStyle.DoubleStruck,
            "sans-serif" => MathAlphabetStyle.SansSerif,
            "monospace" => MathAlphabetStyle.Monospace,
            _ => (MathAlphabetStyle?)null,
        };
        if (style is null)
        {
            converted = value;
            return false;
        }
        converted = Apply(value, style.Value);
        return true;
    }

    internal static bool ContainsStyledCharacter(string value) =>
        value.EnumerateRunes().Any(rune =>
            rune.Value is >= 0x1D400 and <= 0x1D7FF
            || LegacyCharacters.Contains(rune.Value)
        );

    private static int MapAscii(char character, MathAlphabetStyle style)
    {
        if (character is >= 'A' and <= 'Z')
        {
            var offset = character - 'A';
            return style switch
            {
                MathAlphabetStyle.Script =>
                    ScriptExceptions.TryGetValue(character, out var script)
                        ? script
                        : 0x1D49C + offset,
                MathAlphabetStyle.Fraktur =>
                    FrakturExceptions.TryGetValue(character, out var fraktur)
                        ? fraktur
                        : 0x1D504 + offset,
                MathAlphabetStyle.DoubleStruck =>
                    DoubleStruckExceptions.TryGetValue(character, out var doubleStruck)
                        ? doubleStruck
                        : 0x1D538 + offset,
                MathAlphabetStyle.SansSerif => 0x1D5A0 + offset,
                MathAlphabetStyle.Monospace => 0x1D670 + offset,
                _ => -1,
            };
        }
        if (character is >= 'a' and <= 'z')
        {
            var offset = character - 'a';
            return style switch
            {
                MathAlphabetStyle.Script =>
                    ScriptExceptions.TryGetValue(character, out var script)
                        ? script
                        : 0x1D4B6 + offset,
                MathAlphabetStyle.Fraktur => 0x1D51E + offset,
                MathAlphabetStyle.DoubleStruck => 0x1D552 + offset,
                MathAlphabetStyle.SansSerif => 0x1D5BA + offset,
                MathAlphabetStyle.Monospace => 0x1D68A + offset,
                _ => -1,
            };
        }
        if (character is >= '0' and <= '9')
        {
            var offset = character - '0';
            return style switch
            {
                MathAlphabetStyle.DoubleStruck => 0x1D7D8 + offset,
                MathAlphabetStyle.SansSerif => 0x1D7E2 + offset,
                MathAlphabetStyle.Monospace => 0x1D7F6 + offset,
                _ => -1,
            };
        }
        return -1;
    }

    private static NativeToolException Unsupported(
        MathAlphabetStyle style,
        Rune rune
    ) =>
        new(
            "EQUATION_INVALID",
            "The requested mathematical alphabet has no supported mapping for one of its characters",
            new
            {
                alphabet = Name(style),
                code_point = $"U+{rune.Value:X4}",
            }
        );

    private static string Name(MathAlphabetStyle style) =>
        style switch
        {
            MathAlphabetStyle.Script => "script",
            MathAlphabetStyle.Fraktur => "fraktur",
            MathAlphabetStyle.DoubleStruck => "double_struck",
            MathAlphabetStyle.SansSerif => "sans_serif",
            MathAlphabetStyle.Monospace => "monospace",
            _ => style.ToString(),
        };
}
