using WordToolkit.Native.Equations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Tests;

/// <summary>
/// Execution-backed negative corpus for equation front-ends.  These cases must
/// fail closed; accepting them by flattening would silently change meaning.
/// </summary>
public sealed class EquationCoverageNegativeAtlasTests
{
    [Theory]
    [MemberData(nameof(MalformedLatex))]
    public void LatexRejectsMalformedGroupsScriptsEnvironmentsAndUnsupportedMacros(string latex)
        => AssertEquationInvalid(() => LatexToUnicodeMath.Convert(latex));

    [Theory]
    [MemberData(nameof(UnsupportedLatexFeatures))]
    public void LatexRejectsUnsupportedPackagesColorChemistryAndUnsafeControls(string latex)
        => AssertEquationInvalid(() => LatexToUnicodeMath.Convert(latex));

    [Theory]
    [MemberData(nameof(InvalidMathMl))]
    public void MathMlRejectsWrongNamespaceAndLossyOrUnsupportedNodes(string source)
        => AssertEquationInvalid(() => MathMarkupToUnicodeMath.Convert(source, "mathml"));

    [Theory]
    [MemberData(nameof(InvalidOmml))]
    public void OmmlRejectsWrongMixedNamespaceUnsupportedNodesAndUnsafeXml(string source)
        => AssertEquationInvalid(() => MathMarkupToUnicodeMath.Convert(source, "omml"));

    [Fact]
    public void DirectOmmlRejectsDepthSizeAndMultipleEquationBombs()
    {
        const string ns = "http://schemas.openxmlformats.org/officeDocument/2006/math";
        var deep = $"<m:oMath xmlns:m=\"{ns}\">{string.Concat(Enumerable.Repeat("<m:e>", 70))}<m:r><m:t>x</m:t></m:r>{string.Concat(Enumerable.Repeat("</m:e>", 70))}</m:oMath>";
        var many = $"<m:oMath xmlns:m=\"{ns}\">{string.Concat(Enumerable.Repeat("<m:e/>", 10001))}</m:oMath>";
        Assert.Equal("LIMIT_EXCEEDED", Assert.Throws<NativeToolException>(() => DirectOmmlEquationParser.Parse(deep)).ErrorCode);
        Assert.Equal("LIMIT_EXCEEDED", Assert.Throws<NativeToolException>(() => DirectOmmlEquationParser.Parse(many)).ErrorCode);
    }

    [Fact]
    public void DirectOmmlRejectsDtdCommentsProcessingInstructionsAndMultipleEquations()
    {
        const string ns = "http://schemas.openxmlformats.org/officeDocument/2006/math";
        var cases = new[]
        {
            "<!DOCTYPE x [<!ENTITY e 'x'>]><m:oMath xmlns:m=\"" + ns + "\"><m:r><m:t>&e;</m:t></m:r></m:oMath>",
            "<?probe x?><m:oMath xmlns:m=\"" + ns + "\"><m:r><m:t>x</m:t></m:r></m:oMath>",
            "<m:oMath xmlns:m=\"" + ns + "\"><!--secret--><m:r><m:t>x</m:t></m:r></m:oMath>",
            "<m:oMathPara xmlns:m=\"" + ns + "\"><m:oMath><m:r><m:t>x</m:t></m:r></m:oMath><m:oMath><m:r><m:t>y</m:t></m:r></m:oMath></m:oMathPara>"
        };
        foreach (var xml in cases) AssertEquationInvalid(() => DirectOmmlEquationParser.Parse(xml));
    }

    public static IEnumerable<object[]> MalformedLatex() => Cases(
        @"{x", @"x}", @"\frac{x}", @"\sqrt{", @"x_", @"x^", @"\left(x", @"\right)",
        @"\begin{matrix}a&b\c&d", @"\end{matrix}", @"\begin{unknown}x\end{unknown}",
        @"\left\middle|x\right)", @"\root \of x", @"\choose", @"x\over y", @"\phantom"
    );

    public static IEnumerable<object[]> UnsupportedLatexFeatures() => Cases(
        @"\usepackage{amsmath} x", @"\color{red}x", @"\textcolor{red}{x}", @"\ce{H2O}",
        @"\chemfig{C-C}", @"\input{secret}", @"\include{secret}", "x\uE100+y"
    );

    public static IEnumerable<object[]> InvalidMathMl() => Cases(
        "<math xmlns=\"urn:wrong\"><mi>x</mi></math>",
        "<math xmlns=\"http://www.w3.org/1998/Math/MathML\"><mfoo><mi>x</mi></mfoo></math>",
        "<math xmlns=\"http://www.w3.org/1998/Math/MathML\"><mpadded width=\"2em\"><mi>x</mi></mpadded></math>",
        "<math xmlns=\"http://www.w3.org/1998/Math/MathML\"><mspace width=\"1em\"/></math>",
        "<math xmlns=\"http://www.w3.org/1998/Math/MathML\"><menclose notation=\"circle\"><mi>x</mi></menclose></math>",
        "<math xmlns=\"http://www.w3.org/1998/Math/MathML\"><mtable><mlabeledtr><mtd><mi>x</mi></mtd></mlabeledtr></mtable></math>",
        "<math xmlns=\"http://www.w3.org/1998/Math/MathML\"><maction selection=\"0\"><mi>x</mi><mi>y</mi></maction></math>",
        "<math xmlns=\"http://www.w3.org/1998/Math/MathML\"><mmultiscripts><mi>x</mi><none/></mmultiscripts></math>"
    );

    public static IEnumerable<object[]> InvalidOmml() => Cases(
        "<m:oMath xmlns:m=\"urn:wrong\"><m:r><m:t>x</m:t></m:r></m:oMath>",
        "<m:oMath xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\" xmlns:s=\"http://purl.oclc.org/ooxml/officeDocument/math\"><s:r><s:t>x</s:t></s:r></m:oMath>",
        "<m:oMath xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"><m:unsupported><m:r><m:t>x</m:t></m:r></m:unsupported></m:oMath>",
        "<m:oMath xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"><w:hyperlink xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><m:r><m:t>x</m:t></m:r></w:hyperlink></m:oMath>"
    );

    private static IEnumerable<object[]> Cases(params string[] values) => values.Select(v => new object[] { v });

    private static void AssertEquationInvalid(Action action)
    {
        var error = Assert.Throws<NativeToolException>(action);
        Assert.Equal("EQUATION_INVALID", error.ErrorCode);
        Assert.InRange(error.Message.Length, 1, 4096);
    }
}
