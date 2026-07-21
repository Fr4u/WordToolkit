using WordToolkit.Native.Equations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Tests;

public sealed class MathMarkupToUnicodeMathTests
{
    [Fact]
    public void ConvertsPresentationMathMlToWordLinearMath()
    {
        const string source =
            """
            <math xmlns="http://www.w3.org/1998/Math/MathML">
              <mfrac>
                <msup><mi>x</mi><mn>2</mn></msup>
                <msqrt><mi>y</mi></msqrt>
              </mfrac>
            </math>
            """;

        Assert.Equal(
            "(x^(2))/(√(y))",
            MathMarkupToUnicodeMath.Convert(source, "mathml")
        );
    }

    [Fact]
    public void ConvertsNativeOmmlToWordLinearMath()
    {
        const string source =
            """
            <m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <m:f>
                <m:num><m:r><m:t>1</m:t></m:r></m:num>
                <m:den>
                  <m:sSup>
                    <m:e><m:r><m:t>x</m:t></m:r></m:e>
                    <m:sup><m:r><m:t>2</m:t></m:r></m:sup>
                  </m:sSup>
                </m:den>
              </m:f>
            </m:oMath>
            """;

        Assert.Equal(
            "(1)/(x^(2))",
            MathMarkupToUnicodeMath.Convert(source, "omml")
        );
    }

    [Fact]
    public void SeparatesPresentationMathMlNaryLimitsFromTheBody()
    {
        const string source =
            """
            <math xmlns="http://www.w3.org/1998/Math/MathML">
              <msubsup>
                <mo>∫</mo>
                <mn>0</mn>
                <mn>1</mn>
              </msubsup>
              <mi>x</mi>
            </math>
            """;

        Assert.Equal(
            "∫_(0)^(1)▒x",
            MathMarkupToUnicodeMath.Convert(source, "mathml")
        );
    }

    [Fact]
    public void SeparatesNativeOmmlNaryLimitsFromTheBody()
    {
        const string source =
            """
            <m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <m:nary>
                <m:naryPr><m:chr m:val="∫" /></m:naryPr>
                <m:sub><m:r><m:t>0</m:t></m:r></m:sub>
                <m:sup><m:r><m:t>1</m:t></m:r></m:sup>
                <m:e><m:r><m:t>x</m:t></m:r></m:e>
              </m:nary>
            </m:oMath>
            """;

        Assert.Equal(
            "∫_(0)^(1)▒x",
            MathMarkupToUnicodeMath.Convert(source, "omml")
        );
    }

    [Fact]
    public void ConvertsMathMlMatrixAndAccentToWordControlWords()
    {
        const string source =
            """
            <math xmlns="http://www.w3.org/1998/Math/MathML">
              <mover accent="true"><mi>x</mi><mo>→</mo></mover>
              <mtable>
                <mtr><mtd><mi>a</mi></mtd><mtd><mi>b</mi></mtd></mtr>
                <mtr><mtd><mi>c</mi></mtd><mtd><mi>d</mi></mtd></mtr>
              </mtable>
            </math>
            """;

        Assert.Equal(
            "x⃗■(a&b@c&d)",
            MathMarkupToUnicodeMath.Convert(source, "mathml")
        );
    }

    [Fact]
    public void ConvertsOmmlMatrixAccentAndTextToWordControlWords()
    {
        const string source =
            """
            <m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <m:acc>
                <m:accPr><m:chr m:val="^" /></m:accPr>
                <m:e><m:r><m:t>x</m:t></m:r></m:e>
              </m:acc>
              <m:m>
                <m:mr>
                  <m:e><m:r><m:t>a</m:t></m:r></m:e>
                  <m:e><m:r><m:t>b</m:t></m:r></m:e>
                </m:mr>
              </m:m>
              <m:r><m:rPr><m:nor /></m:rPr><m:t>opis</m:t></m:r>
            </m:oMath>
            """;

        Assert.Equal(
            @"""opis""",
            MathMarkupToUnicodeMath.Convert(source, "omml")[^6..]
        );
        Assert.StartsWith(
            "x̂■(a&b)",
            MathMarkupToUnicodeMath.Convert(source, "omml")
        );
    }

    [Fact]
    public void RejectsDtdAndExternalEntityMarkup()
    {
        const string source =
            """
            <!DOCTYPE math [<!ENTITY xxe SYSTEM "file:///C:/Windows/win.ini">]>
            <math xmlns="http://www.w3.org/1998/Math/MathML"><mi>&xxe;</mi></math>
            """;

        var error = Assert.Throws<NativeToolException>(
            () => MathMarkupToUnicodeMath.Convert(source, "mathml")
        );
        Assert.Equal("EQUATION_INVALID", error.ErrorCode);
    }

    [Fact]
    public void RejectsForeignNamespacesAndUnsupportedMarkup()
    {
        const string source =
            """
            <math xmlns="http://www.w3.org/1998/Math/MathML">
              <evil:script xmlns:evil="urn:evil">x</evil:script>
            </math>
            """;

        var error = Assert.Throws<NativeToolException>(
            () => MathMarkupToUnicodeMath.Convert(source, "mathml")
        );
        Assert.Equal("EQUATION_INVALID", error.ErrorCode);
    }
}
