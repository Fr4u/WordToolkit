using WordToolkit.Native.Equations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Tests;

public sealed class LatexToUnicodeMathTests
{
    [Theory]
    [InlineData(@"\frac{x^2+1}{\sqrt[3]{y}}", "(x^(2)+1)/(√(3&y))")]
    [InlineData(@"\sum_{i=1}^{n} i^2", "∑_(i=1)^(n)▒i^(2)")]
    [InlineData(@"\int_0^1 e^{-x^2}\,d x", "∫_(0)^(1)▒〖e^(-x^(2)) ⅆx〗")]
    [InlineData(@"\int x\,d x", "∫▒〖x ⅆx〗")]
    [InlineData(@"\int f(x)\,\mathrm{d}x", "∫▒〖f(x) ⅆx〗")]
    [InlineData(@"\int f(x)\,\dd x", "∫▒〖f(x) ⅆx〗")]
    [InlineData(
        @"\int_{-\infty}^{\infty}\int_{-\infty}^{\infty}e^{-(x^2+y^2)}\,d x\,d y",
        "∫_(-∞)^(∞)▒〖∫_(-∞)^(∞)▒〖e^(-(x^(2)+y^(2))) ⅆx〗 ⅆy〗"
    )]
    [InlineData(
        @"\iint_D f(x,y)\,\mathrm{d}x\,\mathrm{d}y",
        "∬_(D)▒〖f(x,y) ⅆx ⅆy〗"
    )]
    [InlineData(
        @"\begin{matrix}a&b\\c&d\end{matrix}",
        "■(a&b@c&d)"
    )]
    [InlineData(
        @"\begin{pmatrix}a&b\\c&d\end{pmatrix}",
        "⒨(a&b@c&d)"
    )]
    [InlineData(
        @"\begin{bmatrix}a&b\\c&d\end{bmatrix}",
        "ⓢ(a&b@c&d)"
    )]
    [InlineData(
        @"\begin{vmatrix}a&b\\c&d\end{vmatrix}",
        "⒱(a&b@c&d)"
    )]
    [InlineData(
        @"\begin{Vmatrix}a&b\\c&d\end{Vmatrix}",
        "⒩(a&b@c&d)"
    )]
    [InlineData(
        @"\begin{cases}x+y=1\\2x-y=0\end{cases}",
        "Ⓒ(x+y=1@2x-y=0)"
    )]
    [InlineData(
        @"\begin{aligned}x+y&=1\\2x-y&=0\end{aligned}",
        "█(x+y =1@2x-y =0)"
    )]
    [InlineData(@"\vec{x}+\hat{y}+\bar{z}", "x⃗+ŷ+z̅")]
    [InlineData(@"\text{speed}+\operatorname{rank}(A)", @"""speed""+""rank""(A)")]
    [InlineData(@"\lim_{x\to 0}\sin x", "lim┬(x→ 0)sin x")]
    [InlineData(@"\min_{x\in S}f(x)", @"""min""_(x∈ S)f(x)")]
    public void ConvertsCommonWordMath(string latex, string expected)
    {
        Assert.Equal(expected, LatexToUnicodeMath.Convert(latex));
    }

    [Fact]
    public void DoesNotInventADifferentialWithoutDifferentialNotation()
    {
        Assert.Equal("a d+b", LatexToUnicodeMath.Convert(@"a\,d+b"));
    }

    [Theory]
    [InlineData(@"\unknown{x}")]
    [InlineData(@"\frac{x}")]
    [InlineData(@"{x")]
    [InlineData(@"x_")]
    public void FailsClosedForUnsupportedOrMalformedLatex(string latex)
    {
        var error = Assert.Throws<NativeToolException>(
            () => LatexToUnicodeMath.Convert(latex)
        );
        Assert.Equal("EQUATION_INVALID", error.ErrorCode);
    }
}
