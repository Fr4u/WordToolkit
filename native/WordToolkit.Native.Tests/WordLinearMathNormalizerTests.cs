using WordToolkit.Native.Equations;

namespace WordToolkit.Native.Tests;

public sealed class WordLinearMathNormalizerTests
{
    [Theory]
    [InlineData("∫_(0)^(1)▒x ⅆx", "∫_(0)^(1)▒〖x ⅆx〗")]
    [InlineData("∫_(0)^(1)▒xⅆx", "∫_(0)^(1)▒〖x ⅆx〗")]
    [InlineData("∬_(D)▒f(x,y) ⅆx ⅆy", "∬_(D)▒〖f(x,y) ⅆx ⅆy〗")]
    [InlineData(
        "∫_(0)^(1)▒∫_(0)^(x)▒f(x,y) ⅆy ⅆx",
        "∫_(0)^(1)▒〖∫_(0)^(x)▒〖f(x,y) ⅆy〗 ⅆx〗"
    )]
    [InlineData(
        "∫▒f ⅆx+∫▒g ⅆy",
        "∫▒〖f ⅆx〗+∫▒〖g ⅆy〗"
    )]
    [InlineData("∫▒〖f ⅆx〗", "∫▒〖f ⅆx〗")]
    [InlineData("∑_(i=1)^(n)▒i", "∑_(i=1)^(n)▒i")]
    public void ProducesIdempotentWordSafeIntegralOperands(
        string source,
        string expected
    )
    {
        var normalized = WordLinearMathNormalizer.NormalizeForWord(source);

        Assert.Equal(expected, normalized);
        Assert.Equal(normalized, WordLinearMathNormalizer.NormalizeForWord(normalized));
    }
}
