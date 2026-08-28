using System.Text.Json;
using WordToolkit.Native.Equations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Tests;

public sealed class EquationReadbackVerifierTests
{
    private const string MathNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void VerifiesCanonicalWordIntegralAndDifferentialPlacement()
    {
        var result = EquationReadbackVerifier.Verify(
            Wrap(IntegralOmml(differentialInsideBody: true, variable: "x")),
            "∫_(0)^(1)▒〖x ⅆx〗"
        );

        Assert.Equal(result.ExpectedContractSha256, result.ActualContractSha256);
        Assert.Equal(1, result.NaryCount);
        Assert.Equal(1, result.DifferentialCount);
        Assert.True(result.DifferentialPlacementVerified);
        Assert.True(result.MathElementCount >= 10);
    }

    [Fact]
    public void VerifiesDifferentialsForMultipleIntegralsInOneEquation()
    {
        const string omml = """
            <m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <m:nary><m:naryPr><m:chr m:val="∫" /></m:naryPr><m:sub><m:r><m:t> </m:t></m:r></m:sub><m:sup><m:r><m:t> </m:t></m:r></m:sup><m:e><m:r><m:t>u v'</m:t></m:r><m:r><m:t> ⅆx</m:t></m:r></m:e></m:nary>
              <m:r><m:t> =u v − </m:t></m:r>
              <m:nary><m:naryPr><m:chr m:val="∫" /></m:naryPr><m:sub><m:r><m:t> </m:t></m:r></m:sub><m:sup><m:r><m:t> </m:t></m:r></m:sup><m:e><m:r><m:t>u' v</m:t></m:r><m:r><m:t> ⅆx</m:t></m:r></m:e></m:nary>
            </m:oMath>
            """;

        var result = EquationReadbackVerifier.Verify(
            Wrap(omml),
            "∫▒〖u v' ⅆx〗 =uv-∫▒〖u' v ⅆx〗"
        );

        Assert.Equal(2, result.NaryCount);
        Assert.Equal(2, result.DifferentialCount);
        Assert.True(result.DifferentialPlacementVerified);
    }

    [Fact]
    public void AllowsWordToPlaceAdjacentDifferentialRunsOutsideMultipleIntegralOperands()
    {
        const string omml = """
            <m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <m:nary><m:naryPr><m:chr m:val="∫" /></m:naryPr><m:e><m:r><m:t>u v'</m:t></m:r></m:e></m:nary><m:r><m:t> ⅆx =u v − </m:t></m:r>
              <m:nary><m:naryPr><m:chr m:val="∫" /></m:naryPr><m:e><m:r><m:t>u' v</m:t></m:r></m:e></m:nary><m:r><m:t> ⅆx</m:t></m:r>
            </m:oMath>
            """;
        var actual = MathMarkupToUnicodeMath.Convert(omml, "omml");
        Assert.Equal("∫▒〖u v' ⅆx〗 =u v −∫▒〖u' v ⅆx〗", actual);
        var result = EquationReadbackVerifier.Verify(
            Wrap(omml),
            "∫▒〖u v' ⅆx〗 =u v −∫▒〖u' v ⅆx〗"
        );
        Assert.Equal(2, result.NaryCount);
        Assert.Equal(2, result.DifferentialCount);
        Assert.True(result.DifferentialPlacementVerified);
    }

    [Fact]
    public void RejectsTwoDifferentialsAttachedToFirstOfTwoIntegrals()
    {
        const string omml = """
            <m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <m:nary><m:naryPr><m:chr m:val="∫" /></m:naryPr><m:e><m:r><m:t>u</m:t></m:r><m:r><m:t> ⅆx ⅆx</m:t></m:r></m:e></m:nary>
              <m:r><m:t> =u − </m:t></m:r>
              <m:nary><m:naryPr><m:chr m:val="∫" /></m:naryPr><m:e><m:r><m:t>v</m:t></m:r></m:e></m:nary>
            </m:oMath>
            """;

        var error = Assert.Throws<NativeToolException>(() =>
            EquationReadbackVerifier.Verify(
                Wrap(omml),
                "∫▒〖u ⅆx〗 =u-∫▒〖v ⅆx〗"
            )
        );

        Assert.Equal("EQUATION_INVALID", error.ErrorCode);
    }

    [Fact]
    public void VerifiesNestedIntegralDifferentialsPerOperand()
    {
        const string omml = """
            <m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <m:nary>
                <m:naryPr><m:chr m:val="∫" /></m:naryPr>
                <m:e>
                  <m:nary><m:naryPr><m:chr m:val="∫" /></m:naryPr><m:e><m:r><m:t>f</m:t></m:r><m:r><m:t> ⅆx</m:t></m:r></m:e></m:nary>
                  <m:r><m:t> ⅆy</m:t></m:r>
                </m:e>
              </m:nary>
            </m:oMath>
            """;

        var result = EquationReadbackVerifier.Verify(
            Wrap(omml),
            "∫▒〖∫▒〖f ⅆx〗 ⅆy〗"
        );

        Assert.Equal(2, result.NaryCount);
        Assert.Equal(2, result.DifferentialCount);
        Assert.True(result.DifferentialPlacementVerified);
    }

    [Fact]
    public void AllowsNestedIntegralDifferentialsInAdjacentWordRuns()
    {
        const string omml = """
            <m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <m:nary>
                <m:naryPr><m:chr m:val="∫" /></m:naryPr>
                <m:e>
                  <m:nary><m:naryPr><m:chr m:val="∫" /></m:naryPr><m:e><m:r><m:t>f</m:t></m:r></m:e></m:nary>
                  <m:r><m:t> ⅆx ⅆy</m:t></m:r>
                </m:e>
              </m:nary>
            </m:oMath>
            """;

        var result = EquationReadbackVerifier.Verify(
            Wrap(omml),
            "∫▒〖∫▒〖f ⅆx〗 ⅆy〗"
        );

        Assert.Equal(2, result.NaryCount);
        Assert.Equal(2, result.DifferentialCount);
        Assert.True(result.DifferentialPlacementVerified);
    }

    [Fact]
    public void RejectsDifferentialThatWordPlacedOutsideTheNaryBody()
    {
        var error = Assert.Throws<NativeToolException>(() =>
            EquationReadbackVerifier.Verify(
                Wrap(IntegralOmml(differentialInsideBody: false, variable: "x")),
                "∫_(0)^(1)▒〖x ⅆx〗"
            )
        );

        Assert.Equal("EQUATION_INVALID", error.ErrorCode);
        Assert.Contains("differential placement", error.Message, StringComparison.Ordinal);
        using var details = JsonDocument.Parse(
            JsonSerializer.Serialize(error.Details, JsonDefaults.Compact)
        );
        var diagnostic = details.RootElement.GetProperty("diagnostic");
        Assert.Equal("differential_placement", diagnostic.GetProperty("mismatch_kind").GetString());
        Assert.Equal(
            "equation/integral_operand",
            diagnostic.GetProperty("node_path").GetString()
        );
    }

    [Fact]
    public void RejectsNativeReadbackWhoseCanonicalContentChanged()
    {
        var error = Assert.Throws<NativeToolException>(() =>
            EquationReadbackVerifier.Verify(
                Wrap(IntegralOmml(differentialInsideBody: true, variable: "y")),
                "∫_(0)^(1)▒〖x ⅆx〗"
            )
        );

        Assert.Equal("EQUATION_INVALID", error.ErrorCode);
        var serialized = JsonSerializer.Serialize(error.Details, JsonDefaults.Compact);
        using var details = JsonDocument.Parse(serialized);
        var diagnostic = details.RootElement.GetProperty("diagnostic");
        Assert.Equal("canonical_structure", diagnostic.GetProperty("mismatch_kind").GetString());
        Assert.True(diagnostic.GetProperty("first_difference_index").GetInt32() >= 0);
        Assert.Equal("letter", diagnostic.GetProperty("expected_token_kind").GetString());
        Assert.Equal("letter", diagnostic.GetProperty("actual_token_kind").GetString());
        Assert.True(diagnostic.GetProperty("expected_families").TryGetProperty("nary", out _));
        Assert.DoesNotContain("<m:", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("∫_(0)", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void AllowsDifferentialsOutsideAnIntegralOperand()
    {
        const string omml =
            """
            <m:oMath>
              <m:f>
                <m:num>
                  <m:r><m:rPr><m:nor/></m:rPr><m:t>ⅆ</m:t></m:r>
                  <m:r><m:t>y</m:t></m:r>
                </m:num>
                <m:den>
                  <m:r><m:rPr><m:nor/></m:rPr><m:t>ⅆ</m:t></m:r>
                  <m:r><m:t>x</m:t></m:r>
                </m:den>
              </m:f>
            </m:oMath>
            """;

        var result = EquationReadbackVerifier.Verify(
            Wrap(omml),
            "(ⅆy)/(ⅆx)"
        );

        Assert.Equal(2, result.DifferentialCount);
        Assert.True(result.DifferentialPlacementVerified);
        Assert.Equal(result.ExpectedContractSha256, result.ActualContractSha256);
    }

    [Fact]
    public void TreatsWordBuiltParenthesizedMatrixAsTheSameCanonicalStructure()
    {
        const string omml =
            """
            <m:oMath>
              <m:d>
                <m:dPr><m:begChr m:val="("/><m:endChr m:val=")"/></m:dPr>
                <m:e>
                  <m:m>
                    <m:mr><m:e><m:r><m:t>a</m:t></m:r></m:e><m:e><m:r><m:t>b</m:t></m:r></m:e></m:mr>
                    <m:mr><m:e><m:r><m:t>c</m:t></m:r></m:e><m:e><m:r><m:t>d</m:t></m:r></m:e></m:mr>
                  </m:m>
                </m:e>
              </m:d>
            </m:oMath>
            """;

        var result = EquationReadbackVerifier.Verify(
            Wrap(omml),
            "⒨(a&b@c&d)"
        );

        Assert.Equal(result.ExpectedContractSha256, result.ActualContractSha256);
    }

    [Fact]
    public void TreatsWordBuiltCasesAsTheSameCanonicalStructure()
    {
        const string omml =
            """
            <m:oMath>
              <m:r><m:t>{</m:t></m:r>
              <m:eqArr>
                <m:e><m:r><m:t>x=1</m:t></m:r></m:e>
                <m:e><m:r><m:t>y=2</m:t></m:r></m:e>
              </m:eqArr>
            </m:oMath>
            """;

        var result = EquationReadbackVerifier.Verify(
            Wrap(omml),
            "Ⓒ(x=1@y=2)"
        );

        Assert.Equal(result.ExpectedContractSha256, result.ActualContractSha256);
    }

    [Fact]
    public void TreatsWordFunctionApplicationMarkersAsCanonical()
    {
        const string omml =
            """
            <m:oMath>
              <m:func>
                <m:fName><m:r><m:t>sin</m:t></m:r></m:fName>
                <m:e><m:r><m:t>x</m:t></m:r></m:e>
              </m:func>
            </m:oMath>
            """;

        var result = EquationReadbackVerifier.Verify(Wrap(omml), "sin x");

        Assert.Equal(result.ExpectedContractSha256, result.ActualContractSha256);
    }

    [Theory]
    [InlineData("μ_(0) ϵ_(0)", "μ_(0)⁢ϵ_(0)")]
    [InlineData("2 (x)/(y)", "2⁢(x)/(y)")]
    [InlineData("(ⅆψ)/(ⅆt)", "⁢(ⅆψ)/(ⅆt)")]
    public void TreatsInvisibleTimesAsImplicitMultiplication(string expected, string actual)
    {
        Assert.Equal(
            EquationReadbackVerifier.CanonicalizeForTesting(expected),
            EquationReadbackVerifier.CanonicalizeForTesting(actual)
        );
    }

    [Fact]
    public void PreservesVisibleCrossProductOperator()
    {
        Assert.NotEqual(
            EquationReadbackVerifier.CanonicalizeForTesting("k×E"),
            EquationReadbackVerifier.CanonicalizeForTesting("k E")
        );
    }

    [Theory]
    [InlineData("(k)̂", "k̂")]
    [InlineData("(x)⃗", "x⃗")]
    public void TreatsSingleSymbolAccentGroupingAsEquivalent(string expected, string actual)
    {
        Assert.Equal(
            EquationReadbackVerifier.CanonicalizeForTesting(expected),
            EquationReadbackVerifier.CanonicalizeForTesting(actual)
        );
    }

    [Theory]
    [InlineData("μ_(0)ϵ_(0)(x)/(y)", "(μ_(0)ϵ_(0))(x)/(y)")]
    [InlineData("∇^(2)B-μ_(0)ϵ_(0)(x)/(y)=0", "∇^(2)B-(μ_(0)ϵ_(0))(x)/(y)=0")]
    public void TreatsRedundantCoefficientGroupingAsEquivalent(string expected, string actual)
    {
        Assert.Equal(
            EquationReadbackVerifier.CanonicalizeForTesting(expected),
            EquationReadbackVerifier.CanonicalizeForTesting(actual)
        );
    }

    [Theory]
    [InlineData("μ_(0)ϵ_(0)(∂^(2)E)/(∂t^(2))", "(μ_(0)ϵ_(0)(∂^(2)E))/(∂t^(2))")]
    [InlineData("∇^(2)B-μ_(0)ϵ_(0)(∂^(2)B)/(∂t^(2))=0", "∇^(2)B-(μ_(0)ϵ_(0)(∂^(2)B))/(∂t^(2))=0")]
    public void TreatsCoefficientOutsideOrInsideFractionNumeratorAsEquivalent(
        string expected,
        string actual
    )
    {
        Assert.Equal(
            EquationReadbackVerifier.CanonicalizeForTesting(expected),
            EquationReadbackVerifier.CanonicalizeForTesting(actual)
        );
    }

    [Theory]
    [InlineData("x²+y₁", "x^(2)+y_(1)")]
    [InlineData("x⁻¹+y₍ₙ₎", "x^(-1)+y_((n))")]
    public void TreatsUnicodeScriptCharactersAsBuiltWordScripts(
        string unicodeMath,
        string explicitScripts
    )
    {
        Assert.Equal(
            EquationReadbackVerifier.CanonicalizeForTesting(explicitScripts),
            EquationReadbackVerifier.CanonicalizeForTesting(unicodeMath)
        );
    }

    [Theory]
    [InlineData("_(a)^(b)x", "(_(a)^(b)x)")]
    [InlineData("_(a)^(b)x", "()_(a)^(b)x")]
    [InlineData("_(k)(T_(i)^(j))", "(_(k)(T_(i)^(j)))")]
    public void TreatsWordsOuterPrescriptGroupAsStructurallyRedundant(
        string expected,
        string actual
    )
    {
        Assert.Equal(
            EquationReadbackVerifier.CanonicalizeForTesting(expected),
            EquationReadbackVerifier.CanonicalizeForTesting(actual)
        );
    }

    [Theory]
    [InlineData("(a+b)c", "a+bc")]
    [InlineData("(ab)^(2)", "ab^(2)")]
    public void PreservesSemanticallyRequiredGroups(string left, string right)
    {
        Assert.NotEqual(
            EquationReadbackVerifier.CanonicalizeForTesting(left),
            EquationReadbackVerifier.CanonicalizeForTesting(right)
        );
    }

    [Fact]
    public void VerifiesNoBarFractionAsBinomialStack()
    {
        var omml = """
            <m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <m:f>
                <m:fPr><m:type m:val="noBar"/></m:fPr>
                <m:num><m:r><m:t>n</m:t></m:r></m:num>
                <m:den><m:r><m:t>k</m:t></m:r></m:den>
              </m:f>
            </m:oMath>
            """;

        var result = EquationReadbackVerifier.Verify(Wrap(omml), "(n¦k)");

        Assert.Equal(result.ExpectedContractSha256, result.ActualContractSha256);
    }

    [Fact]
    public void IgnoresTrimmedEdgesOfWordMathTextRuns()
    {
        const string omml =
            """
            <m:oMath>
              <m:r><m:rPr><m:nor/></m:rPr><m:t>gdy</m:t></m:r>
              <m:r><m:t>x</m:t></m:r>
            </m:oMath>
            """;

        var result = EquationReadbackVerifier.Verify(Wrap(omml), "\"gdy \"x");

        Assert.Equal(result.ExpectedContractSha256, result.ActualContractSha256);
    }

    [Fact]
    public void VerifiesSignificantWordMathSpacingOutsideQuotedText()
    {
        const string omml =
            """
            <m:oMath>
              <m:r><m:t>x</m:t></m:r>
              <m:r><m:t> </m:t></m:r>
              <m:r><m:rPr><m:nor/></m:rPr><m:t>gdy</m:t></m:r>
              <m:r><m:t> </m:t></m:r>
              <m:r><m:t>y</m:t></m:r>
            </m:oMath>
            """;

        var result = EquationReadbackVerifier.Verify(
            Wrap(omml),
            "x\u2003\"gdy\"\u2005y"
        );

        Assert.Equal(result.ExpectedContractSha256, result.ActualContractSha256);
        var error = Assert.Throws<NativeToolException>(() =>
            EquationReadbackVerifier.Verify(Wrap(omml), "x\"gdy\"\u2005y")
        );
        Assert.Equal("EQUATION_INVALID", error.ErrorCode);
    }

    [Fact]
    public void RejectsMultipleEquationsInOneReadbackRange()
    {
        var error = Assert.Throws<NativeToolException>(() =>
            EquationReadbackVerifier.Verify(
                Wrap($"{SimpleOmml("x")}{SimpleOmml("y")}"),
                "x"
            )
        );

        Assert.Equal("EQUATION_INVALID", error.ErrorCode);
    }

    [Fact]
    public void RejectsDtdBeforeInspectingEquationContent()
    {
        var xml =
            $"<!DOCTYPE w:document [<!ENTITY xxe SYSTEM \"file:///C:/Windows/win.ini\">]>"
            + Wrap(SimpleOmml("&xxe;"));

        var error = Assert.Throws<NativeToolException>(() =>
            EquationReadbackVerifier.Verify(xml, "x")
        );

        Assert.Equal("EQUATION_INVALID", error.ErrorCode);
    }

    [Theory]
    [InlineData("x+1", false)]
    [InlineData("∫_(0)^(1)▒〖x ⅆx〗", true)]
    [InlineData("■(a&b@c&d)", true)]
    [InlineData("x⃗", true)]
    [InlineData("ℝ", true)]
    [InlineData("𝖠", true)]
    [InlineData("(n¦k)", true)]
    [InlineData("x_(1),…,x_(n)", true)]
    [InlineData("(sin x)^(4)", true)]
    [InlineData("⨌▒f ⅆx ⅆy ⅆz ⅆw", true)]
    [InlineData("⨂_(i=1)^(n)▒A_i", true)]
    [InlineData("=┴(!)", true)]
    [InlineData("⏞(a+b)^(n)", true)]
    [InlineData("⟡(x)", true)]
    [InlineData("x̲", true)]
    [InlineData("x\u2003y", true)]
    [InlineData("x\u2005y", true)]
    public void SelectsOnlyStructurallySensitiveEquationsForAutomaticReadback(
        string linear,
        bool expected
    )
    {
        Assert.Equal(expected, EquationReadbackVerifier.RequiresReadback(linear));
    }

    private static string Wrap(string body) =>
        $"<w:document xmlns:w=\"{WordNamespace}\" xmlns:m=\"{MathNamespace}\"><w:body><w:p>{body}</w:p></w:body></w:document>";

    private static string SimpleOmml(string value) =>
        $"<m:oMath><m:r><m:t>{value}</m:t></m:r></m:oMath>";

    private static string IntegralOmml(
        bool differentialInsideBody,
        string variable
    )
    {
        var inside = differentialInsideBody
            ? "<m:r><m:t>ⅆ</m:t></m:r><m:r><m:t>x</m:t></m:r>"
            : "";
        var outside = differentialInsideBody
            ? ""
            : "<m:r><m:t>ⅆ</m:t></m:r><m:r><m:t>x</m:t></m:r>";
        return
            $"""
            <m:oMath>
              <m:nary>
                <m:naryPr><m:ctrlPr><w:rPr><w:rFonts w:ascii="Cambria Math"/></w:rPr></m:ctrlPr></m:naryPr>
                <m:sub><m:r><m:t>0</m:t></m:r></m:sub>
                <m:sup><m:r><m:t>1</m:t></m:r></m:sup>
                <m:e><m:r><m:t>{variable}</m:t></m:r>{inside}</m:e>
              </m:nary>
              {outside}
            </m:oMath>
            """;
    }
}
