using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordThemeIssueSeverity
{
    Warning,
    Error,
}

public sealed record WordThemeIssue(
    string Code,
    WordThemeIssueSeverity Severity,
    string Message,
    string? ColorSlot = null,
    string? FontCollection = null
);

public enum WordThemeColorSourceKind
{
    Rgb,
    System,
    ScRgb,
    Hsl,
    Preset,
    Scheme,
}

public sealed record WordThemeColorTransform(
    string Name,
    string? Value,
    int SourceElementOrdinal
);

public sealed class WordThemeColorDefinition
{
    internal WordThemeColorDefinition(
        string slot,
        WordThemeColorSourceKind sourceKind,
        string sourceValue,
        string? lastColor,
        string? baseRgb,
        int sourceElementOrdinal,
        IReadOnlyList<WordThemeColorTransform> transforms,
        IReadOnlyList<string> unmodeledElements
    )
    {
        Slot = slot;
        SourceKind = sourceKind;
        SourceValue = sourceValue;
        LastColor = lastColor;
        BaseRgb = baseRgb;
        SourceElementOrdinal = sourceElementOrdinal;
        Transforms = new ReadOnlyCollection<WordThemeColorTransform>(
            transforms.ToArray()
        );
        UnmodeledElements = new ReadOnlyCollection<string>(
            unmodeledElements.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
    }

    public string Slot { get; }

    public WordThemeColorSourceKind SourceKind { get; }

    public string SourceValue { get; }

    public string? LastColor { get; }

    public string? BaseRgb { get; }

    public int SourceElementOrdinal { get; }

    public IReadOnlyList<WordThemeColorTransform> Transforms { get; }

    public IReadOnlyList<string> UnmodeledElements { get; }

    public bool IsDeterministicallyResolvable => BaseRgb is not null
        && Transforms.Count == 0;
}

public sealed class WordThemeColorScheme
{
    private readonly IReadOnlyDictionary<string, WordThemeColorDefinition> _colors;

    internal WordThemeColorScheme(
        string name,
        int sourceElementOrdinal,
        IReadOnlyList<WordThemeColorDefinition> colors,
        IReadOnlyList<string> unmodeledElements
    )
    {
        Name = name;
        SourceElementOrdinal = sourceElementOrdinal;
        Colors = new ReadOnlyCollection<WordThemeColorDefinition>(
            colors.OrderBy(color => ColorSlotOrder[color.Slot]).ToArray()
        );
        _colors = new ReadOnlyDictionary<string, WordThemeColorDefinition>(
            Colors.ToDictionary(color => color.Slot, StringComparer.Ordinal)
        );
        UnmodeledElements = new ReadOnlyCollection<string>(
            unmodeledElements.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
    }

    public string Name { get; }

    public int SourceElementOrdinal { get; }

    public IReadOnlyList<WordThemeColorDefinition> Colors { get; }

    public IReadOnlyList<string> UnmodeledElements { get; }

    public bool TryGetColor(string slot, out WordThemeColorDefinition? color)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        return _colors.TryGetValue(slot, out color);
    }

    private static readonly IReadOnlyDictionary<string, int> ColorSlotOrder =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["dk1"] = 0,
            ["lt1"] = 1,
            ["dk2"] = 2,
            ["lt2"] = 3,
            ["accent1"] = 4,
            ["accent2"] = 5,
            ["accent3"] = 6,
            ["accent4"] = 7,
            ["accent5"] = 8,
            ["accent6"] = 9,
            ["hlink"] = 10,
            ["folHlink"] = 11,
        };
}

public enum WordThemeFontCollectionKind
{
    Major,
    Minor,
}

public enum WordThemeFontRole
{
    Latin,
    EastAsian,
    ComplexScript,
}

public enum WordThemeFontResolutionKind
{
    PrimaryTypeface,
    SupplementalLanguageTypeface,
    PrimaryTypefaceLanguageFallback,
}

public sealed record WordThemeTypeface(
    string Typeface,
    string? Panose,
    string? PitchFamily,
    string? CharacterSet,
    int SourceElementOrdinal,
    IReadOnlyList<string> UnmodeledAttributes
);

public sealed record WordThemeSupplementalFont(
    string Script,
    string Typeface,
    int SourceElementOrdinal,
    IReadOnlyList<string> UnmodeledAttributes
);

public sealed class WordThemeFontCollection
{
    private readonly IReadOnlyDictionary<string, WordThemeSupplementalFont>
        _supplementalByScript;

    internal WordThemeFontCollection(
        WordThemeFontCollectionKind kind,
        int sourceElementOrdinal,
        WordThemeTypeface latin,
        WordThemeTypeface eastAsian,
        WordThemeTypeface complexScript,
        IReadOnlyList<WordThemeSupplementalFont> supplementalFonts,
        IReadOnlyList<string> unmodeledElements
    )
    {
        Kind = kind;
        SourceElementOrdinal = sourceElementOrdinal;
        Latin = latin;
        EastAsian = eastAsian;
        ComplexScript = complexScript;
        SupplementalFonts = new ReadOnlyCollection<WordThemeSupplementalFont>(
            supplementalFonts.OrderBy(font => font.Script, StringComparer.Ordinal).ToArray()
        );
        _supplementalByScript = new ReadOnlyDictionary<
            string,
            WordThemeSupplementalFont
        >(
            SupplementalFonts.ToDictionary(
                font => font.Script,
                StringComparer.Ordinal
            )
        );
        UnmodeledElements = new ReadOnlyCollection<string>(
            unmodeledElements.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
    }

    public WordThemeFontCollectionKind Kind { get; }

    public int SourceElementOrdinal { get; }

    public WordThemeTypeface Latin { get; }

    public WordThemeTypeface EastAsian { get; }

    public WordThemeTypeface ComplexScript { get; }

    public IReadOnlyList<WordThemeSupplementalFont> SupplementalFonts { get; }

    public IReadOnlyList<string> UnmodeledElements { get; }

    public bool TryGetSupplementalFont(
        string script,
        out WordThemeSupplementalFont? font
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        return _supplementalByScript.TryGetValue(script, out font);
    }
}

public sealed class WordThemeFontScheme
{
    internal WordThemeFontScheme(
        string name,
        int sourceElementOrdinal,
        WordThemeFontCollection major,
        WordThemeFontCollection minor,
        IReadOnlyList<string> unmodeledElements
    )
    {
        Name = name;
        SourceElementOrdinal = sourceElementOrdinal;
        Major = major;
        Minor = minor;
        UnmodeledElements = new ReadOnlyCollection<string>(
            unmodeledElements.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
    }

    public string Name { get; }

    public int SourceElementOrdinal { get; }

    public WordThemeFontCollection Major { get; }

    public WordThemeFontCollection Minor { get; }

    public IReadOnlyList<string> UnmodeledElements { get; }
}

public sealed record WordThemeFormatScheme(
    string Name,
    int SourceElementOrdinal,
    int FillStyleCount,
    int LineStyleCount,
    int EffectStyleCount,
    int BackgroundFillStyleCount,
    IReadOnlyList<string> UnmodeledElements
);

public sealed record WordResolvedThemeColor(
    string RequestedToken,
    string ColorSlot,
    string BaseRgb,
    string EffectiveRgb,
    string? ThemeTint,
    string? ThemeShade,
    int SourceElementOrdinal
);

public sealed record WordResolvedThemeFont(
    string RequestedToken,
    WordThemeFontCollectionKind CollectionKind,
    WordThemeFontRole Role,
    string Typeface,
    int SourceElementOrdinal,
    string? LanguageTag,
    string? Script,
    WordThemeFontResolutionKind ResolutionKind
);

public sealed class WordThemeGraph
{
    internal WordThemeGraph(
        string packageFingerprint,
        string mainPartUri,
        string? themePartUri,
        string? name,
        WordThemeColorScheme? colorScheme,
        WordThemeFontScheme? fontScheme,
        WordThemeFormatScheme? formatScheme,
        IReadOnlyList<WordThemeIssue> issues,
        IReadOnlyList<string> unmodeledRootElements,
        IReadOnlyList<string> unmodeledThemeElements
    )
    {
        PackageFingerprint = packageFingerprint;
        MainPartUri = mainPartUri;
        ThemePartUri = themePartUri;
        Name = name;
        ColorScheme = colorScheme;
        FontScheme = fontScheme;
        FormatScheme = formatScheme;
        Issues = new ReadOnlyCollection<WordThemeIssue>(issues.ToArray());
        UnmodeledRootElements = new ReadOnlyCollection<string>(
            unmodeledRootElements.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
        UnmodeledThemeElements = new ReadOnlyCollection<string>(
            unmodeledThemeElements.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
    }

    public string PackageFingerprint { get; }

    public string MainPartUri { get; }

    public string? ThemePartUri { get; }

    public bool HasThemePart => ThemePartUri is not null;

    public string? Name { get; }

    public WordThemeColorScheme? ColorScheme { get; }

    public WordThemeFontScheme? FontScheme { get; }

    public WordThemeFormatScheme? FormatScheme { get; }

    public IReadOnlyList<WordThemeIssue> Issues { get; }

    public IReadOnlyList<string> UnmodeledRootElements { get; }

    public IReadOnlyList<string> UnmodeledThemeElements { get; }

    public WordResolvedThemeColor ResolveColor(
        string themeToken,
        string? themeTint = null,
        string? themeShade = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeToken);
        if (!HasThemePart || ColorScheme is null)
        {
            throw new WordThemeResolutionException(
                "The document has no usable theme color scheme."
            );
        }

        if (!ThemeColorAliases.TryGetValue(themeToken, out var slot))
        {
            throw new WordThemeResolutionException(
                $"Theme color token '{themeToken}' is not supported by Word's color mapping."
            );
        }

        if (!ColorScheme.TryGetColor(slot, out var definition) || definition is null)
        {
            throw new WordThemeResolutionException(
                $"Theme color slot '{slot}' is missing."
            );
        }

        if (!definition.IsDeterministicallyResolvable || definition.BaseRgb is null)
        {
            throw new WordThemeResolutionException(
                $"Theme color slot '{slot}' depends on an unresolved source or transform."
            );
        }

        var tint = ParseOptionalHexByte(themeTint, "themeTint");
        var shade = ParseOptionalHexByte(themeShade, "themeShade");
        var effective = tint is not null
            ? ApplyLuminanceTransform(definition.BaseRgb, tint.Value, isTint: true)
            : shade is not null
                ? ApplyLuminanceTransform(definition.BaseRgb, shade.Value, isTint: false)
                : definition.BaseRgb;
        return new WordResolvedThemeColor(
            themeToken,
            slot,
            definition.BaseRgb,
            effective,
            themeTint,
            themeShade,
            definition.SourceElementOrdinal
        );
    }

    public WordResolvedThemeFont ResolveFont(string themeToken) =>
        ResolveFont(themeToken, languages: null);

    public WordResolvedThemeFont ResolveFont(
        string themeToken,
        WordThemeFontLanguages? languages
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeToken);
        if (!HasThemePart || FontScheme is null)
        {
            throw new WordThemeResolutionException(
                "The document has no usable theme font scheme."
            );
        }

        if (!ThemeFontTokens.TryGetValue(themeToken, out var request))
        {
            throw new WordThemeResolutionException(
                $"Theme font token '{themeToken}' is not a supported Word theme font value."
            );
        }

        var collection = request.CollectionKind == WordThemeFontCollectionKind.Major
            ? FontScheme.Major
            : FontScheme.Minor;
        var typeface = request.Role switch
        {
            WordThemeFontRole.Latin => collection.Latin,
            WordThemeFontRole.EastAsian => collection.EastAsian,
            WordThemeFontRole.ComplexScript => collection.ComplexScript,
            _ => throw new WordThemeResolutionException(
                $"Theme font role '{request.Role}' is not supported."
            ),
        };
        var language = languages?.ForRole(request.Role);
        if (!string.IsNullOrWhiteSpace(language))
        {
            var script = ResolveLanguageScript(language);
            if (
                script is not null
                && collection.TryGetSupplementalFont(script, out var supplemental)
                && supplemental is not null
            )
            {
                return new WordResolvedThemeFont(
                    themeToken,
                    request.CollectionKind,
                    request.Role,
                    supplemental.Typeface,
                    supplemental.SourceElementOrdinal,
                    language,
                    script,
                    WordThemeFontResolutionKind.SupplementalLanguageTypeface
                );
            }
            if (script is null && collection.SupplementalFonts.Count > 0)
            {
                throw new WordThemeResolutionException(
                    $"Theme font token '{themeToken}' requires language-dependent font selection, but language tag '{language}' cannot be mapped safely to a DrawingML script."
                );
            }
            if (!string.IsNullOrWhiteSpace(typeface.Typeface))
            {
                return new WordResolvedThemeFont(
                    themeToken,
                    request.CollectionKind,
                    request.Role,
                    typeface.Typeface,
                    typeface.SourceElementOrdinal,
                    language,
                    script,
                    WordThemeFontResolutionKind.PrimaryTypefaceLanguageFallback
                );
            }
        }
        if (string.IsNullOrWhiteSpace(typeface.Typeface))
        {
            throw new WordThemeResolutionException(
                $"Theme font token '{themeToken}' requires language-dependent font selection because its primary typeface is empty."
            );
        }

        return new WordResolvedThemeFont(
            themeToken,
            request.CollectionKind,
            request.Role,
            typeface.Typeface,
            typeface.SourceElementOrdinal,
            null,
            null,
            WordThemeFontResolutionKind.PrimaryTypeface
        );
    }

    private static string? ResolveLanguageScript(string languageTag)
    {
        var subtags = languageTag.Split(
            ['-', '_'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (subtags.Length == 0)
        {
            return null;
        }
        for (var index = 1; index < subtags.Length; index++)
        {
            var subtag = subtags[index];
            if (subtag.Length == 1)
            {
                break;
            }
            if (subtag.Length == 4 && subtag.All(IsAsciiLetter))
            {
                return char.ToUpperInvariant(subtag[0])
                    + subtag[1..].ToLowerInvariant();
            }
        }

        var language = subtags[0].ToLowerInvariant();
        var region = subtags.Skip(1)
            .FirstOrDefault(subtag =>
                (subtag.Length == 2 && subtag.All(IsAsciiLetter))
                || (subtag.Length == 3 && subtag.All(char.IsAsciiDigit))
            )
            ?.ToUpperInvariant();
        if (language == "zh")
        {
            return region is "TW" or "HK" or "MO" ? "Hant" : "Hans";
        }
        if (language == "pa")
        {
            return region == "PK" ? "Arab" : "Guru";
        }
        return LanguageToScript.TryGetValue(language, out var script)
            ? script
            : null;
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    private static byte? ParseOptionalHexByte(string? value, string name)
    {
        if (value is null)
        {
            return null;
        }

        if (
            value.Length != 2
            || !byte.TryParse(
                value,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var parsed
            )
        )
        {
            throw new WordThemeResolutionException(
                $"{name} '{value}' is not one hexadecimal byte."
            );
        }

        return parsed;
    }

    private static string ApplyLuminanceTransform(
        string rgb,
        byte amount,
        bool isTint
    )
    {
        var red = int.Parse(rgb.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        var green = int.Parse(rgb.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        var blue = int.Parse(rgb.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        RgbToHsl(red, green, blue, out var hue, out var saturation, out var luminance);
        var factor = amount / 255d;
        luminance = isTint
            ? luminance * factor + (1d - factor)
            : luminance * factor;
        var transformed = HslToRgb(hue, saturation, Math.Clamp(luminance, 0d, 1d));
        return $"{ToByte(transformed.Red):X2}{ToByte(transformed.Green):X2}{ToByte(transformed.Blue):X2}";
    }

    private static void RgbToHsl(
        double red,
        double green,
        double blue,
        out double hue,
        out double saturation,
        out double luminance
    )
    {
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        luminance = (maximum + minimum) / 2d;
        if (Math.Abs(maximum - minimum) < double.Epsilon)
        {
            hue = 0d;
            saturation = 0d;
            return;
        }

        var delta = maximum - minimum;
        saturation = luminance > 0.5d
            ? delta / (2d - maximum - minimum)
            : delta / (maximum + minimum);
        if (Math.Abs(maximum - red) < double.Epsilon)
        {
            hue = (green - blue) / delta + (green < blue ? 6d : 0d);
        }
        else if (Math.Abs(maximum - green) < double.Epsilon)
        {
            hue = (blue - red) / delta + 2d;
        }
        else
        {
            hue = (red - green) / delta + 4d;
        }

        hue /= 6d;
    }

    private static (double Red, double Green, double Blue) HslToRgb(
        double hue,
        double saturation,
        double luminance
    )
    {
        if (saturation == 0d)
        {
            return (luminance, luminance, luminance);
        }

        var q = luminance < 0.5d
            ? luminance * (1d + saturation)
            : luminance + saturation - luminance * saturation;
        var p = 2d * luminance - q;
        return (
            HueToRgb(p, q, hue + 1d / 3d),
            HueToRgb(p, q, hue),
            HueToRgb(p, q, hue - 1d / 3d)
        );
    }

    private static double HueToRgb(double p, double q, double hue)
    {
        if (hue < 0d)
        {
            hue += 1d;
        }
        if (hue > 1d)
        {
            hue -= 1d;
        }
        if (hue < 1d / 6d)
        {
            return p + (q - p) * 6d * hue;
        }
        if (hue < 0.5d)
        {
            return q;
        }
        if (hue < 2d / 3d)
        {
            return p + (q - p) * (2d / 3d - hue) * 6d;
        }
        return p;
    }

    private static int ToByte(double value) => Math.Clamp(
        (int)Math.Floor(value * 255d + 1e-12d),
        0,
        255
    );

    private sealed record ThemeFontRequest(
        WordThemeFontCollectionKind CollectionKind,
        WordThemeFontRole Role
    );

    private static readonly IReadOnlyDictionary<string, string> ThemeColorAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dark1"] = "dk1",
            ["text1"] = "dk1",
            ["dk1"] = "dk1",
            ["light1"] = "lt1",
            ["background1"] = "lt1",
            ["lt1"] = "lt1",
            ["dark2"] = "dk2",
            ["text2"] = "dk2",
            ["dk2"] = "dk2",
            ["light2"] = "lt2",
            ["background2"] = "lt2",
            ["lt2"] = "lt2",
            ["accent1"] = "accent1",
            ["accent2"] = "accent2",
            ["accent3"] = "accent3",
            ["accent4"] = "accent4",
            ["accent5"] = "accent5",
            ["accent6"] = "accent6",
            ["hyperlink"] = "hlink",
            ["hlink"] = "hlink",
            ["followedHyperlink"] = "folHlink",
            ["folHlink"] = "folHlink",
        };

    private static readonly IReadOnlyDictionary<string, ThemeFontRequest>
        ThemeFontTokens = new Dictionary<string, ThemeFontRequest>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["majorAscii"] = new(WordThemeFontCollectionKind.Major, WordThemeFontRole.Latin),
            ["majorHAnsi"] = new(WordThemeFontCollectionKind.Major, WordThemeFontRole.Latin),
            ["majorEastAsia"] = new(WordThemeFontCollectionKind.Major, WordThemeFontRole.EastAsian),
            ["majorBidi"] = new(WordThemeFontCollectionKind.Major, WordThemeFontRole.ComplexScript),
            ["minorAscii"] = new(WordThemeFontCollectionKind.Minor, WordThemeFontRole.Latin),
            ["minorHAnsi"] = new(WordThemeFontCollectionKind.Minor, WordThemeFontRole.Latin),
            ["minorEastAsia"] = new(WordThemeFontCollectionKind.Minor, WordThemeFontRole.EastAsian),
            ["minorBidi"] = new(WordThemeFontCollectionKind.Minor, WordThemeFontRole.ComplexScript),
        };

    // Compact CLDR/ISO 15924 likely-script map for the script families emitted by
    // Office themes. Explicit BCP 47 script subtags always take precedence.
    private static readonly IReadOnlyDictionary<string, string> LanguageToScript =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["am"] = "Ethi",
            ["ar"] = "Arab",
            ["as"] = "Beng",
            ["az"] = "Latn",
            ["be"] = "Cyrl",
            ["bg"] = "Cyrl",
            ["bn"] = "Beng",
            ["bo"] = "Tibt",
            ["cs"] = "Latn",
            ["da"] = "Latn",
            ["de"] = "Latn",
            ["dv"] = "Thaa",
            ["el"] = "Grek",
            ["en"] = "Latn",
            ["es"] = "Latn",
            ["et"] = "Latn",
            ["fa"] = "Arab",
            ["fi"] = "Latn",
            ["fr"] = "Latn",
            ["gu"] = "Gujr",
            ["he"] = "Hebr",
            ["hi"] = "Deva",
            ["hr"] = "Latn",
            ["hu"] = "Latn",
            ["hy"] = "Armn",
            ["id"] = "Latn",
            ["is"] = "Latn",
            ["it"] = "Latn",
            ["ja"] = "Jpan",
            ["ka"] = "Geor",
            ["kk"] = "Cyrl",
            ["km"] = "Khmr",
            ["kn"] = "Knda",
            ["ko"] = "Hang",
            ["ky"] = "Cyrl",
            ["lo"] = "Laoo",
            ["lt"] = "Latn",
            ["lv"] = "Latn",
            ["mk"] = "Cyrl",
            ["ml"] = "Mlym",
            ["mn"] = "Cyrl",
            ["mr"] = "Deva",
            ["ms"] = "Latn",
            ["my"] = "Mymr",
            ["ne"] = "Deva",
            ["nl"] = "Latn",
            ["no"] = "Latn",
            ["or"] = "Orya",
            ["pl"] = "Latn",
            ["ps"] = "Arab",
            ["pt"] = "Latn",
            ["ro"] = "Latn",
            ["ru"] = "Cyrl",
            ["sa"] = "Deva",
            ["sd"] = "Arab",
            ["si"] = "Sinh",
            ["sk"] = "Latn",
            ["sl"] = "Latn",
            ["sq"] = "Latn",
            ["sr"] = "Cyrl",
            ["sv"] = "Latn",
            ["syr"] = "Syrc",
            ["ta"] = "Taml",
            ["te"] = "Telu",
            ["th"] = "Thai",
            ["ti"] = "Ethi",
            ["tr"] = "Latn",
            ["ug"] = "Arab",
            ["uk"] = "Cyrl",
            ["ur"] = "Arab",
            ["uz"] = "Latn",
            ["vi"] = "Latn",
            ["yi"] = "Hebr",
        };
}

public sealed record WordThemeGraphOptions
{
    public static WordThemeGraphOptions Default { get; } = new();

    public int MaxThemePartBytes { get; init; } = 32 * 1024 * 1024;

    public int MaxSupplementalFontsPerCollection { get; init; } = 2_048;

    public int MaxColorTransformsPerSlot { get; init; } = 256;

    internal void Validate()
    {
        if (MaxThemePartBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxThemePartBytes));
        }
        if (MaxSupplementalFontsPerCollection <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxSupplementalFontsPerCollection)
            );
        }
        if (MaxColorTransformsPerSlot <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxColorTransformsPerSlot));
        }
    }
}

public sealed class WordThemeGraphBuilder
{
    private const string DrawingTransitionalNamespace =
        "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string DrawingStrictNamespace =
        "http://purl.oclc.org/ooxml/drawingml/main";
    private const string ThemeRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";
    private const string StrictThemeRelationship =
        "http://purl.oclc.org/ooxml/officeDocument/relationships/theme";
    private const string ThemeContentType =
        "application/vnd.openxmlformats-officedocument.theme+xml";

    private static readonly string[] RequiredColorSlots =
    [
        "dk1",
        "lt1",
        "dk2",
        "lt2",
        "accent1",
        "accent2",
        "accent3",
        "accent4",
        "accent5",
        "accent6",
        "hlink",
        "folHlink",
    ];

    private readonly WordThemeGraphOptions _options;
    private readonly WordOperationResourceLease? _resourceLease;

    public WordThemeGraphBuilder(WordThemeGraphOptions? options = null)
    {
        _options = options ?? WordThemeGraphOptions.Default;
        _options.Validate();
    }

    public WordThemeGraphBuilder(
        WordThemeGraphOptions? options,
        WordOperationResourceLease resourceLease
    )
    {
        ArgumentNullException.ThrowIfNull(resourceLease);
        _options = options ?? WordThemeGraphOptions.Default;
        _resourceLease = resourceLease;
        _options.Validate();
    }

    public WordThemeGraph Build(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        cancellationToken.ThrowIfCancellationRequested();
        WordOperationResourceAccounting.ChargeProjectionBase(
            _resourceLease,
            WordOperationResourceStage.Theme
        );
        if (
            !string.Equals(
                package.Fingerprint,
                semanticDocument.PackageFingerprint,
                StringComparison.Ordinal
            )
        )
        {
            throw new WordThemeProjectionException(
                "Theme graph requires package and semantic snapshots from the same document version."
            );
        }

        var themePart = ResolveThemePart(package, semanticDocument.MainPartUri);
        if (themePart is null)
        {
            return new WordThemeGraph(
                package.Fingerprint,
                semanticDocument.MainPartUri,
                null,
                null,
                null,
                null,
                null,
                Array.Empty<WordThemeIssue>(),
                Array.Empty<string>(),
                Array.Empty<string>()
            );
        }

        var source = ParseThemePart(themePart, cancellationToken);
        WordOperationResourceAccounting.ChargeItems(
            _resourceLease,
            WordOperationResourceStage.Theme,
            source.Elements.Count,
            768
        );
        var root = source.ParsedDocument.Root;
        if (
            root is null
            || !IsDrawingNamespace(root.Name.NamespaceName)
            || root.Name.LocalName != "theme"
        )
        {
            throw new WordThemeProjectionException(
                "Word theme part does not have an a:theme root element."
            );
        }

        var a = root.Name.Namespace;
        var themeElements = RequiredSingleChild(root, a + "themeElements");
        var issues = new List<WordThemeIssue>();
        var colorScheme = ParseColorScheme(
            RequiredSingleChild(themeElements, a + "clrScheme"),
            a,
            source,
            issues
        );
        var fontScheme = ParseFontScheme(
            RequiredSingleChild(themeElements, a + "fontScheme"),
            a,
            source
        );
        var formatScheme = ParseFormatScheme(
            RequiredSingleChild(themeElements, a + "fmtScheme"),
            a,
            source
        );
        var knownRoot = new HashSet<XName>
        {
            a + "themeElements",
        };
        var knownThemeElements = new HashSet<XName>
        {
            a + "clrScheme",
            a + "fontScheme",
            a + "fmtScheme",
        };
        return new WordThemeGraph(
            package.Fingerprint,
            semanticDocument.MainPartUri,
            themePart.Uri,
            RequiredAttribute(root, "name", "theme name", allowEmpty: true),
            colorScheme,
            fontScheme,
            formatScheme,
            issues,
            FindUnknownChildren(root, knownRoot),
            FindUnknownChildren(themeElements, knownThemeElements)
        );
    }

    private WordThemeColorScheme ParseColorScheme(
        XElement element,
        XNamespace a,
        LosslessXmlDocument source,
        List<WordThemeIssue> issues
    )
    {
        var colors = new List<WordThemeColorDefinition>();
        var unmodeled = new List<string>();
        foreach (var child in element.Elements())
        {
            if (
                child.Name.Namespace != a
                || !RequiredColorSlots.Contains(child.Name.LocalName, StringComparer.Ordinal)
            )
            {
                unmodeled.Add(QualifiedName(child.Name));
                continue;
            }

            colors.Add(ParseColor(child, a, source, issues));
        }

        var duplicate = colors.GroupBy(color => color.Slot, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new WordThemeProjectionException(
                $"Theme color scheme contains duplicate slot '{duplicate.Key}'."
            );
        }

        var present = colors.Select(color => color.Slot).ToHashSet(StringComparer.Ordinal);
        foreach (var missing in RequiredColorSlots.Where(slot => !present.Contains(slot)))
        {
            issues.Add(
                new WordThemeIssue(
                    "THEME_COLOR_SLOT_MISSING",
                    WordThemeIssueSeverity.Error,
                    $"Theme color scheme has no '{missing}' slot.",
                    ColorSlot: missing
                )
            );
        }

        return new WordThemeColorScheme(
            RequiredAttribute(element, "name", "color scheme name", allowEmpty: true),
            source.GetElementOrdinal(element),
            colors,
            unmodeled
        );
    }

    private WordThemeColorDefinition ParseColor(
        XElement slot,
        XNamespace a,
        LosslessXmlDocument source,
        List<WordThemeIssue> issues
    )
    {
        var bases = slot.Elements().Take(2).ToArray();
        if (bases.Length != 1 || bases[0].Name.Namespace != a)
        {
            throw new WordThemeProjectionException(
                $"Theme color slot '{slot.Name.LocalName}' must contain exactly one DrawingML color source."
            );
        }

        var color = bases[0];
        var transforms = color.Elements().Select(transform =>
        {
            if (transform.Name.Namespace != a)
            {
                throw new WordThemeProjectionException(
                    $"Theme color slot '{slot.Name.LocalName}' contains a transform outside DrawingML."
                );
            }
            return new WordThemeColorTransform(
                transform.Name.LocalName,
                transform.Attribute("val")?.Value,
                source.GetElementOrdinal(transform)
            );
        }).ToArray();
        if (transforms.Length > _options.MaxColorTransformsPerSlot)
        {
            throw new WordThemeLimitException(
                $"Theme color slot '{slot.Name.LocalName}' exceeds the transform limit."
            );
        }

        var (kind, value, lastColor, baseRgb, knownAttributes) = color.Name.LocalName switch
        {
            "srgbClr" => ParseRgbColor(color),
            "sysClr" => ParseSystemColor(color),
            "scrgbClr" => ParseScRgbColor(color),
            "hslClr" => ParseHslColor(color),
            "prstClr" => ParseNamedColor(color, WordThemeColorSourceKind.Preset),
            "schemeClr" => ParseNamedColor(color, WordThemeColorSourceKind.Scheme),
            _ => throw new WordThemeProjectionException(
                $"Theme color slot '{slot.Name.LocalName}' uses unsupported source '{color.Name.LocalName}'."
            ),
        };
        if (baseRgb is null)
        {
            issues.Add(
                new WordThemeIssue(
                    "THEME_COLOR_SOURCE_ENVIRONMENTAL",
                    WordThemeIssueSeverity.Warning,
                    $"Theme color slot '{slot.Name.LocalName}' cannot be reduced to deterministic RGB without application or system state.",
                    ColorSlot: slot.Name.LocalName
                )
            );
        }
        if (transforms.Length != 0)
        {
            issues.Add(
                new WordThemeIssue(
                    "THEME_COLOR_TRANSFORMS_UNRESOLVED",
                    WordThemeIssueSeverity.Warning,
                    $"Theme color slot '{slot.Name.LocalName}' declares DrawingML transforms that are retained but not flattened.",
                    ColorSlot: slot.Name.LocalName
                )
            );
            baseRgb = null;
        }

        var unmodeledAttributes = color.Attributes()
            .Where(attribute =>
                !attribute.IsNamespaceDeclaration
                && !knownAttributes.Contains(attribute.Name.LocalName)
            )
            .Select(attribute => "@" + QualifiedName(attribute.Name));
        return new WordThemeColorDefinition(
            slot.Name.LocalName,
            kind,
            value,
            lastColor,
            baseRgb,
            source.GetElementOrdinal(color),
            transforms,
            unmodeledAttributes.ToArray()
        );
    }

    private WordThemeFontScheme ParseFontScheme(
        XElement element,
        XNamespace a,
        LosslessXmlDocument source
    )
    {
        var major = ParseFontCollection(
            RequiredSingleChild(element, a + "majorFont"),
            a,
            source,
            WordThemeFontCollectionKind.Major
        );
        var minor = ParseFontCollection(
            RequiredSingleChild(element, a + "minorFont"),
            a,
            source,
            WordThemeFontCollectionKind.Minor
        );
        return new WordThemeFontScheme(
            RequiredAttribute(element, "name", "font scheme name", allowEmpty: true),
            source.GetElementOrdinal(element),
            major,
            minor,
            FindUnknownChildren(
                element,
                new HashSet<XName> { a + "majorFont", a + "minorFont" }
            )
        );
    }

    private WordThemeFontCollection ParseFontCollection(
        XElement element,
        XNamespace a,
        LosslessXmlDocument source,
        WordThemeFontCollectionKind kind
    )
    {
        var latin = ParseTypeface(RequiredSingleChild(element, a + "latin"), source);
        var eastAsian = ParseTypeface(RequiredSingleChild(element, a + "ea"), source);
        var complexScript = ParseTypeface(RequiredSingleChild(element, a + "cs"), source);
        var supplemental = element.Elements(a + "font")
            .Select(font => ParseSupplementalFont(font, source))
            .ToArray();
        if (supplemental.Length > _options.MaxSupplementalFontsPerCollection)
        {
            throw new WordThemeLimitException(
                $"Theme {kind.ToString().ToLowerInvariant()} font collection exceeds the supplemental-font limit."
            );
        }

        var duplicate = supplemental.GroupBy(font => font.Script, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new WordThemeProjectionException(
                $"Theme {kind.ToString().ToLowerInvariant()} font collection contains duplicate script '{duplicate.Key}'."
            );
        }

        return new WordThemeFontCollection(
            kind,
            source.GetElementOrdinal(element),
            latin,
            eastAsian,
            complexScript,
            supplemental,
            FindUnknownChildren(
                element,
                new HashSet<XName>
                {
                    a + "latin",
                    a + "ea",
                    a + "cs",
                    a + "font",
                }
            )
        );
    }

    private static WordThemeTypeface ParseTypeface(
        XElement element,
        LosslessXmlDocument source
    )
    {
        var typeface = RequiredAttribute(
            element,
            "typeface",
            $"{element.Name.LocalName} typeface",
            allowEmpty: true
        );
        ValidateBoundedText(typeface, "theme typeface", 1_024, allowEmpty: true);
        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            "typeface",
            "panose",
            "pitchFamily",
            "charset",
        };
        return new WordThemeTypeface(
            typeface,
            OptionalAttribute(element, "panose"),
            OptionalAttribute(element, "pitchFamily"),
            OptionalAttribute(element, "charset"),
            source.GetElementOrdinal(element),
            UnknownAttributes(element, known)
        );
    }

    private static WordThemeSupplementalFont ParseSupplementalFont(
        XElement element,
        LosslessXmlDocument source
    )
    {
        var script = RequiredAttribute(element, "script", "supplemental font script");
        var typeface = RequiredAttribute(element, "typeface", "supplemental typeface");
        ValidateBoundedText(script, "supplemental font script", 32);
        ValidateBoundedText(typeface, "supplemental typeface", 1_024);
        return new WordThemeSupplementalFont(
            script,
            typeface,
            source.GetElementOrdinal(element),
            UnknownAttributes(
                element,
                new HashSet<string>(StringComparer.Ordinal) { "script", "typeface" }
            )
        );
    }

    private static WordThemeFormatScheme ParseFormatScheme(
        XElement element,
        XNamespace a,
        LosslessXmlDocument source
    )
    {
        var fill = RequiredSingleChild(element, a + "fillStyleLst");
        var line = RequiredSingleChild(element, a + "lnStyleLst");
        var effect = RequiredSingleChild(element, a + "effectStyleLst");
        var background = RequiredSingleChild(element, a + "bgFillStyleLst");
        return new WordThemeFormatScheme(
            RequiredAttribute(element, "name", "format scheme name", allowEmpty: true),
            source.GetElementOrdinal(element),
            fill.Elements().Count(),
            line.Elements().Count(),
            effect.Elements().Count(),
            background.Elements().Count(),
            FindUnknownChildren(
                element,
                new HashSet<XName>
                {
                    a + "fillStyleLst",
                    a + "lnStyleLst",
                    a + "effectStyleLst",
                    a + "bgFillStyleLst",
                }
            )
        );
    }

    private static OpcPart? ResolveThemePart(
        OpcPackageSnapshot package,
        string mainPartUri
    )
    {
        var relationships = package.RelationshipsFrom(mainPartUri)
            .Where(relationship => relationship.Type is ThemeRelationship or StrictThemeRelationship)
            .ToArray();
        if (relationships.Length == 0)
        {
            return null;
        }
        if (relationships.Length != 1)
        {
            throw new WordThemeProjectionException(
                "Main document part contains multiple theme relationships."
            );
        }

        var relationship = relationships[0];
        if (
            relationship.TargetMode != OpcRelationshipTargetMode.Internal
            || relationship.ResolvedTargetPartUri is null
            || !package.Parts.TryGetValue(relationship.ResolvedTargetPartUri, out var part)
            || !string.Equals(
                part.ContentType,
                ThemeContentType,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new WordThemeProjectionException(
                "The theme relationship does not resolve to a valid Office theme part."
            );
        }

        return part;
    }

    private LosslessXmlDocument ParseThemePart(
        OpcPart part,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var options = new LosslessXmlOptions
            {
                MaxSourceBytes = _options.MaxThemePartBytes,
                MaxXmlCharacters = _options.MaxThemePartBytes,
                MaxXmlElements = 262_144,
                MaxXmlDepth = 128,
                MaxTextCharacters = _options.MaxThemePartBytes,
            };
            return _resourceLease is null
                ? LosslessXmlDocument.Parse(part.Entry.Content, options, cancellationToken)
                : LosslessXmlDocument.Parse(
                    part.Entry.Content,
                    options,
                    _resourceLease,
                    WordOperationResourceStage.Theme,
                    cancellationToken
                );
        }
        catch (LosslessXmlLimitException exception)
        {
            throw new WordThemeLimitException(
                "Office theme part exceeds a theme-graph XML limit: "
                    + exception.Message
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordThemeProjectionException(
                "Office theme part is not safe, bounded, well-formed XML.",
                exception
            );
        }
    }

    private static (
        WordThemeColorSourceKind Kind,
        string Value,
        string? LastColor,
        string? BaseRgb,
        IReadOnlySet<string> KnownAttributes
    ) ParseRgbColor(XElement element)
    {
        var value = RequiredAttribute(element, "val", "RGB color");
        ValidateHexRgb(value, "RGB color");
        return (
            WordThemeColorSourceKind.Rgb,
            value.ToUpperInvariant(),
            null,
            value.ToUpperInvariant(),
            new HashSet<string>(StringComparer.Ordinal) { "val" }
        );
    }

    private static (
        WordThemeColorSourceKind Kind,
        string Value,
        string? LastColor,
        string? BaseRgb,
        IReadOnlySet<string> KnownAttributes
    ) ParseSystemColor(XElement element)
    {
        var value = RequiredAttribute(element, "val", "system color");
        var last = OptionalAttribute(element, "lastClr");
        if (last is not null)
        {
            ValidateHexRgb(last, "last system color");
            last = last.ToUpperInvariant();
        }
        return (
            WordThemeColorSourceKind.System,
            value,
            last,
            last,
            new HashSet<string>(StringComparer.Ordinal) { "val", "lastClr" }
        );
    }

    private static (
        WordThemeColorSourceKind Kind,
        string Value,
        string? LastColor,
        string? BaseRgb,
        IReadOnlySet<string> KnownAttributes
    ) ParseScRgbColor(XElement element)
    {
        var red = RequiredAttribute(element, "r", "scRGB red component");
        var green = RequiredAttribute(element, "g", "scRGB green component");
        var blue = RequiredAttribute(element, "b", "scRGB blue component");
        return (
            WordThemeColorSourceKind.ScRgb,
            $"r={red};g={green};b={blue}",
            null,
            null,
            new HashSet<string>(StringComparer.Ordinal) { "r", "g", "b" }
        );
    }

    private static (
        WordThemeColorSourceKind Kind,
        string Value,
        string? LastColor,
        string? BaseRgb,
        IReadOnlySet<string> KnownAttributes
    ) ParseHslColor(XElement element)
    {
        var hue = RequiredAttribute(element, "hue", "HSL hue");
        var saturation = RequiredAttribute(element, "sat", "HSL saturation");
        var luminance = RequiredAttribute(element, "lum", "HSL luminance");
        return (
            WordThemeColorSourceKind.Hsl,
            $"hue={hue};sat={saturation};lum={luminance}",
            null,
            null,
            new HashSet<string>(StringComparer.Ordinal) { "hue", "sat", "lum" }
        );
    }

    private static (
        WordThemeColorSourceKind Kind,
        string Value,
        string? LastColor,
        string? BaseRgb,
        IReadOnlySet<string> KnownAttributes
    ) ParseNamedColor(XElement element, WordThemeColorSourceKind kind)
    {
        var value = RequiredAttribute(element, "val", $"{kind} color");
        return (
            kind,
            value,
            null,
            null,
            new HashSet<string>(StringComparer.Ordinal) { "val" }
        );
    }

    private static void ValidateHexRgb(string value, string description)
    {
        if (
            value.Length != 6
            || !int.TryParse(
                value,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out _
            )
        )
        {
            throw new WordThemeProjectionException(
                $"{description} '{value}' is not six hexadecimal digits."
            );
        }
    }

    private static void ValidateBoundedText(
        string value,
        string description,
        int maximumLength,
        bool allowEmpty = false
    )
    {
        if ((!allowEmpty && value.Length == 0) || value.Length > maximumLength)
        {
            throw new WordThemeProjectionException(
                $"{description} has an invalid length."
            );
        }
    }

    private static XElement RequiredSingleChild(XElement parent, XName name)
    {
        var children = parent.Elements(name).Take(2).ToArray();
        if (children.Length != 1)
        {
            throw new WordThemeProjectionException(
                $"Element '{parent.Name.LocalName}' must contain exactly one '{name.LocalName}' child."
            );
        }
        return children[0];
    }

    private static string RequiredAttribute(
        XElement element,
        XName name,
        string description,
        bool allowEmpty = false
    )
    {
        var attribute = element.Attribute(name);
        if (attribute is null || !allowEmpty && attribute.Value.Length == 0)
        {
            throw new WordThemeProjectionException(
                $"Element '{element.Name.LocalName}' has no {description}."
            );
        }
        return attribute.Value;
    }

    private static string? OptionalAttribute(XElement element, XName name) =>
        element.Attribute(name)?.Value;

    private static IReadOnlyList<string> UnknownAttributes(
        XElement element,
        IReadOnlySet<string> known
    ) => element.Attributes()
        .Where(attribute =>
            !attribute.IsNamespaceDeclaration
            && !known.Contains(attribute.Name.LocalName)
        )
        .Select(attribute => "@" + QualifiedName(attribute.Name))
        .Distinct(StringComparer.Ordinal)
        .Order()
        .ToArray();

    private static IReadOnlyList<string> FindUnknownChildren(
        XElement element,
        IReadOnlySet<XName> known
    ) => element.Elements()
        .Where(child => !known.Contains(child.Name))
        .Select(child => QualifiedName(child.Name))
        .Distinct(StringComparer.Ordinal)
        .Order()
        .ToArray();

    private static bool IsDrawingNamespace(string value) =>
        value is DrawingTransitionalNamespace or DrawingStrictNamespace;

    private static string QualifiedName(XName name) =>
        $"{{{name.NamespaceName}}}{name.LocalName}";
}

public class WordThemeProjectionException : IOException
{
    public WordThemeProjectionException(string message)
        : base(message)
    {
    }

    public WordThemeProjectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class WordThemeLimitException : WordThemeProjectionException
{
    public WordThemeLimitException(string message)
        : base(message)
    {
    }
}

public sealed class WordThemeResolutionException : InvalidOperationException
{
    public WordThemeResolutionException(string message)
        : base(message)
    {
    }
}
