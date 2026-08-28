using WordToolkit.Native.Equations;

namespace WordToolkit.Native.Tests;

public sealed class LatexSymbolCatalogTests
{
    [Fact]
    public void EveryEntryIsSingleUnicodeScalarWithoutFormattingMarkers()
    {
        Assert.True(LatexSymbolCatalog.Symbols.Count >= 400);
        Assert.Equal(LatexSymbolCatalog.Symbols.Count, LatexSymbolCatalog.Symbols.Keys.Distinct(StringComparer.Ordinal).Count());
        foreach (var (command, value) in LatexSymbolCatalog.Symbols)
        {
            Assert.False(string.IsNullOrWhiteSpace(command));
            Assert.False(string.IsNullOrWhiteSpace(value));
            foreach (var marker in "▒■⒨ⓢ⒱⒩█Ⓒ┬┴")
                Assert.DoesNotContain(marker, value);
            Assert.Single(value.EnumerateRunes());
        }
    }

    [Theory]
    [InlineData("alpha", "α")]
    [InlineData("sum", "∑")]
    [InlineData("rightarrow", "→")]
    [InlineData("subseteq", "⊆")]
    [InlineData("amalg", "∐")]
    [InlineData("iiiint", "⨌")]
    [InlineData("oiint", "∯")]
    [InlineData("bigsqcap", "⨅")]
    [InlineData("lparen", "(")]
    public void RepresentativeFamilies(string command, string expected)
        => Assert.Equal(expected, LatexSymbolCatalog.Symbols[command]);

    [Theory]
    [InlineData("sum")]
    [InlineData("iiiint")]
    [InlineData("oiint")]
    [InlineData("oiiint")]
    [InlineData("aoint")]
    [InlineData("coint")]
    [InlineData("cwint")]
    [InlineData("bigoplus")]
    [InlineData("bigsqcap")]
    public void EveryRepresentativeNaryAliasGetsExactlyOneBodySeparator(
        string command
    )
    {
        var converted = LatexToUnicodeMath.Convert($@"\{command}_{{i=1}}^n x");

        Assert.Equal(1, converted.Count(character => character == '▒'));
        Assert.EndsWith("▒x", converted, StringComparison.Ordinal);
    }
}
