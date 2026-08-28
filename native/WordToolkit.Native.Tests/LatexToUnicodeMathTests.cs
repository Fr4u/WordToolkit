using WordToolkit.Native.Equations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Tests;

public sealed class LatexToUnicodeMathTests
{
    [Theory]
    [InlineData(@"\frac{x^2+1}{\sqrt[3]{y}}", "(x^(2)+1)/(√(3&y))")]
    [InlineData(@"A \implies B", "A ⇒ B")]
    [InlineData(@"\boxed{x+1}", "▭(x+1)")]
    [InlineData(@"\boxed{\frac{x}{y}}", "▭((x)/(y))")]
    [InlineData(@"\sum_{i=1}^{n} i^2", "∑_(i=1)^(n)▒i^(2)")]
    [InlineData(@"\sum\limits_{i=1}^{n} i^2", "∑_(i=1)^(n)▒i^(2)")]
    [InlineData(@"\sum_{\int_0^1}^{n} x", "∑_(∫_(0)^(1))^(n)▒x")]
    [InlineData(@"\prod_{\sum_{i=1}^{n}}^{m} a_i", "∏_(∑_(i=1)^(n))^(m)▒a_(i)")]
    [InlineData(@"\left(\sum_{i=1}^{n}\right)", "(∑_(i=1)^(n))")]
    [InlineData(@"\left(\sum_{i=1}^{n} i\right)", "(∑_(i=1)^(n)▒i)")]
    [InlineData(@"\int_0^1 e^{-x^2}\,d x", "∫_(0)^(1)▒〖e^(-x^(2)) ⅆx〗")]
    [InlineData(@"\int_{\sum_{i=1}^{n}}^{m} f(x)\,d x", "∫_(∑_(i=1)^(n))^(m)▒〖f(x) ⅆx〗")]
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
        @"\iiint_V f(x,y,z)\,\mathrm{d}x\,\mathrm{d}y\,\mathrm{d}z",
        "∭_(V)▒〖f(x,y,z) ⅆx ⅆy ⅆz〗"
    )]
    [InlineData(
        @"\int u v' \,\mathrm{d}x = uv - \int u' v \,\mathrm{d}x",
        "∫▒〖u v' ⅆx〗 = u v - ∫▒〖u' v ⅆx〗"
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
    [InlineData(@"\widehat{\mathbf{k}}", "(k)̂")]
    [InlineData(@"\mathbf{E}(\mathbf{r},t)", "E(r,t)")]
    [InlineData(@"\mathbf{k}\times\mathbf{E}", "k×E")]
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
    [InlineData(@"\mathrm{m/s}", @"""m/s""")]
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
    [InlineData(@"\binom{n}{k}", "(n¦k)")]
    [InlineData(@"x_1,\dots,x_n", "x_(1),…,x_(n)")]
    [InlineData(@"\sin^4 x+\cos^2(2x)", "(sin x)^(4)+(cos (2x))^(2)")]
    [InlineData(
        @"e^{\lambda x}\left(\frac{x^3}{\lambda}\right)",
        "e^(λ x) ((x^(3))/(λ))"
    )]
    [InlineData(@"(a+b)(c+d)", "(a+b) (c+d)")]
    [InlineData(@"x=3{,}14", "x=3,14")]
    [InlineData(@"x_{3{,}14}", "x_(3,14)")]
    [InlineData(@"\text{prędkość w m/s}=3{,}14", @"""prędkość w m/s""=3,14")]
    [InlineData(@"\begin{matrix}3{,}14&2\\1&0{,}5\end{matrix}", "■(3,14&2@1&0,5)")]
    [InlineData(@"\bra{\psi}", "⟨ψ∣")]
    [InlineData(@"\ket{\psi}", "∣ψ⟩")]
    [InlineData(@"\braket{\phi}{\psi}", "⟨φ∣ψ⟩")]
    [InlineData(@"\braket{\phi|\psi}", "⟨φ∣ψ⟩")]
    [InlineData(@"\matrixel{\phi}{H}{\psi}", "⟨φ∣H∣ψ⟩")]
    [InlineData(@"\expectation{H}", "⟨H⟩")]
    [InlineData(@"\dv{\psi}{t}", "(ⅆψ)/(ⅆt)")]
    [InlineData(@"\dv{t}", "(ⅆ)/(ⅆt)")]
    [InlineData(@"\pdv{\psi}{t}", "(∂ ψ)/(∂ t)")]
    [InlineData(@"\pdv{t}", "(∂)/(∂ t)")]
    [InlineData(@"\oplus a \otimes b \mid c", "⊕ a ⊗ b ∣ c")]
    [InlineData(@"\therefore x \because y", "∴ x ∵ y")]
    [InlineData(@"\asin x+\Pr(A)", "asin x+Pr(A)")]
    [InlineData(@"\check{x}+\breve{y}+\underline{z}", "x̌+y̆+z̲")]
    [InlineData(@"\dbinom{n}{k}+\tbinom{r}{s}", "(n¦k)+(r¦s)")]
    [InlineData(@"\cbrt{x}+\qdrt{y}", "√(3&x)+√(4&y)")]
    [InlineData(@"\overset{!}{=}+\underset{i}{x}", "=┴(!)+x┬(i)")]
    [InlineData(@"\overbrace{a+b}^{n}+\underbrace{c+d}_{m}", "⏞(a+b)┴(n)+⏟(c+d)┬(m)")]
    [InlineData(@"\overparen{x}+\underparen{y}+\overbracket{a}+\underbracket{b}", "⏜(x)+⏝(y)+⎴(a)+⎵(b)")]
    [InlineData(@"\substack{i=1\\j=2}", "█(i=1@j=2)")]
    [InlineData(@"\begin{split}a&=b\\c&=d\end{split}", "█(a =b@c =d)")]
    [InlineData(@"\begin{multline}a+b\\c+d\end{multline}", "█(a+b@c+d)")]
    [InlineData(@"\begin{equation*}x^2+y^2=1\end{equation*}", "x^(2)+y^(2)=1")]
    [InlineData(@"\begin{smallmatrix}1&0\\0&1\end{smallmatrix}", "■(1&0@0&1)")]
    [InlineData(@"\gamma^\mu\partial_\mu", "γ^(μ)\u2062∂_(μ)")]
    [InlineData(@"\iiiint_V f\,\mathrm{d}x\,\mathrm{d}y\,\mathrm{d}z\,\mathrm{d}w", "⨌_(V)▒〖f ⅆx ⅆy ⅆz ⅆw〗")]
    [InlineData(@"\root 5\of{x}", "√(5&x)")]
    [InlineData(@"\phantom{x}+\hphantom{y}+\vphantom{z}", "⟡(x)+⬄(y)+⇳(z)")]
    [InlineData(@"\smash{x}+\hsmash{y}+\asmash{z}+\dsmash{w}", "⬍(x)+⬌(y)+⬆(z)+⬇(w)")]
    [InlineData(@"\left\langle a\middle|b\right\rangle", "⟨ a║|b⟩")]
    [InlineData(@"\acute{x}+\grave{y}+\dddot{z}+\widetilde{w}+\underbar{q}", "x́+ỳ+z⃛+w̃+q̲")]
    public void ConvertsCommonWordMath(string latex, string expected)
    {
        Assert.Equal(expected, LatexToUnicodeMath.Convert(latex));
    }

    [Theory]
    [InlineData(@"\gets \iff \longrightarrow \hookrightarrow", "← ⇔ ⟶ ↪")]
    [InlineData(@"\setminus \uplus \sqcap \sqcup", "∖ ⊎ ⊓ ⊔")]
    [InlineData(@"\mathbb{R} \mathbb{N} \wp", "ℝ ℕ ℘")]
    [InlineData(@"\limsup x + \liminf x + \operatorname{rank}(A)", "\"limsup\" x +\u2005\"liminf\" x +\u2005\"rank\"(A)")]
    public void ConvertsExtendedStandardCatalog(string latex, string expected)
    {
        Assert.Equal(expected, LatexToUnicodeMath.Convert(latex));
    }

    [Theory]
    [InlineData(@"\sum_{i=1}^{n}▒i", "∑_(i=1)^(n)▒i")]
    [InlineData(@"\sum_{i=1}^{n}", "∑_(i=1)^(n)")]
    [InlineData(@"(\prod_{i=1}^{n})", "(∏_(i=1)^(n))")]
    [InlineData(
        @"\sum_{i=1}^{\prod_{j=1}^{m}} a_i",
        "∑_(i=1)^(∏_(j=1)^(m))▒a_(i)"
    )]
    public void PlacesExactlyOneNarySeparatorOnlyBeforeARealBody(
        string latex,
        string expected
    )
    {
        Assert.Equal(expected, LatexToUnicodeMath.Convert(latex));
    }

    [Fact]
    public void ConvertsTeXInfixChooseWithoutInventingPrefixArguments()
    {
        Assert.Equal("((n¦k))", LatexToUnicodeMath.Convert(@"{n\choose k}"));
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
    [InlineData(@"\left(x")]
    [InlineData(@"\right)")]
    [InlineData(@"\middle|x")]
    public void FailsClosedForUnsupportedOrMalformedLatex(string latex)
    {
        var error = Assert.Throws<NativeToolException>(
            () => LatexToUnicodeMath.Convert(latex)
        );
        Assert.Equal("EQUATION_INVALID", error.ErrorCode);
    }
}
