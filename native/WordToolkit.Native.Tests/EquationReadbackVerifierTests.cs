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
