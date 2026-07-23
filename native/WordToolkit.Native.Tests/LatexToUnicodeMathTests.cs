using WordToolkit.Native.Equations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Tests;

public sealed class LatexToUnicodeMathTests
{
    [Theory]
    [InlineData(@"\frac{x^2+1}{\sqrt[3]{y}}", "(x^(2)+1)/(√(3&y))")]
    [InlineData(@"\sum_{i=1}^{n} i^2", "∑_(i=1)^(n)▒i^(2)")]
    [InlineData(@"\sum\limits_{i=1}^{n} i^2", "∑_(i=1)^(n)▒i^(2)")]
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
        @"\begin{cases}x^2&\text{gdy }x\ge0\\-x&\text{gdy }x<0\end{cases}",
        "Ⓒ(x^(2)\u2003\"gdy\"\u2005x≥0@-x\u2003\"gdy\"\u2005x<0)"
    )]
    [InlineData(
        @"\begin{aligned}x+y&=1\\2x-y&=0\end{aligned}",
        "█(x+y =1@2x-y =0)"
    )]
    [InlineData(@"\vec{x}+\hat{y}+\bar{z}", "x⃗+ŷ+z̅")]
    [InlineData(@"\text{speed}+\operatorname{rank}(A)", @"""speed""+""rank""(A)")]
    [InlineData(@"x \text{units}", "x\u2005\"units\"")]
    [InlineData(@"\text{given }x", "\"given\"\u2005x")]
    [InlineData(@"\lim_{x\to 0}\sin x", "lim┬(x→ 0)⁡sin x")]
    [InlineData(@"\min_{x\in S}f(x)", @"""min""┬(x∈ S)⁡f(x)")]
    [InlineData(@"\max_{x\in S}f(x)", @"""max""┬(x∈ S)⁡f(x)")]
    [InlineData(
        @"\lim_{x\to0}\frac{\sin x}{x}",
        "lim┬(x→0)⁡〖(sin x)/(x)〗"
    )]
    [InlineData(@"\left\|u\right\|", "‖u‖")]
    [InlineData(@"\|u\|", "‖u‖")]
    [InlineData(@"\mathrm{speed}", @"""speed""")]
    [InlineData(@"\mathbb{R}+\mathbb{C}", "ℝ+ℂ")]
    [InlineData(@"\mathcal{F}+\mathcal{l}", "ℱ+ℓ")]
    [InlineData(@"\mathfrak{R}+\mathfrak{I}", "ℜ+ℑ")]
    [InlineData(@"\mathsf{A}", "𝖠")]
    [InlineData(@"\mathtt{x}", "𝚡")]
    [InlineData(
        @"\int x^3e^{2x}\sin(3x)\,dx",
        "∫▒〖x^(3) e^(2x) sin(3x) ⅆx〗"
    )]
    [InlineData(@"\frac{a}{b}x+(u+v)y", "(a)/(b) x+(u+v) y")]
    [InlineData(
        @"e^{\lambda x}\left(\frac{x^3}{\lambda}\right)",
        "e^(λ x) ((x^(3))/(λ))"
    )]
    [InlineData(@"(a+b)(c+d)", "(a+b) (c+d)")]
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
    [InlineData(@"\mathbf{x+y}", "x+y", 1, 0)]
    [InlineData(@"\boldsymbol{\alpha+\frac{x}{y}}", "α+(x)/(y)", 0, 1)]
    [InlineData(@"\mathbf{x+\boldsymbol{y}}", "x+y", 1, 1)]
    public void PreservesNativeBoldMathAsAnInternalBuildPlan(
        string latex,
        string expected,
        int bold,
        int boldItalic
    )
    {
        var plan = LatexToUnicodeMath.ConvertPlan(latex);

        Assert.Equal(expected, plan.Linear);
        Assert.Equal(expected, LatexToUnicodeMath.Convert(latex));
        Assert.NotEqual(plan.Linear, plan.BuildLinear);
        Assert.Equal(bold, plan.StyleCounts.Bold);
        Assert.Equal(boldItalic, plan.StyleCounts.BoldItalic);
        Assert.Equal(bold + boldItalic, plan.StyleCounts.Total);
    }

    [Fact]
    public void RejectsReservedInternalFormattingMarkersFromLatexInput()
    {
        var error = Assert.Throws<NativeToolException>(() =>
            LatexToUnicodeMath.Convert("x\uE100+y")
        );

        Assert.Equal("EQUATION_INVALID", error.ErrorCode);
        Assert.Contains("reserved", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"\unknown{x}")]
    [InlineData(@"\frac{x}")]
    [InlineData(@"{x")]
    [InlineData(@"x_")]
    [InlineData(@"\sum\nolimits_{i=1}^{n}i")]
    [InlineData(@"x\limits_0")]
    public void FailsClosedForUnsupportedOrMalformedLatex(string latex)
    {
        var error = Assert.Throws<NativeToolException>(
            () => LatexToUnicodeMath.Convert(latex)
        );
        Assert.Equal("EQUATION_INVALID", error.ErrorCode);
    }
}
