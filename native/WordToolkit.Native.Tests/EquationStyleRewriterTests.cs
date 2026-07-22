using System.Xml.Linq;
using WordToolkit.Native.Equations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Tests;

public sealed class EquationStyleRewriterTests
{
    private const string MathNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";

    [Fact]
    public void RewritesNestedMarkersAcrossAFractionAndVerifiesTheStyleContract()
    {
        var result = EquationStyleRewriter.Rewrite(
            NestedFractionWordOpenXml,
            new EquationStyleCounts(Bold: 1, BoldItalic: 1)
        );

        Assert.DoesNotContain('\uE100', result.WordOpenXml);
        Assert.DoesNotContain('\uE101', result.WordOpenXml);
        Assert.DoesNotContain('\uE102', result.WordOpenXml);
        Assert.DoesNotContain('\uE103', result.WordOpenXml);
        Assert.Equal(2, result.RegionCount);
        Assert.Equal(2, result.StyledRunCount);
        Assert.Equal(1, result.BoldRunCount);
        Assert.Equal(1, result.BoldItalicRunCount);
        Assert.Equal(1, result.BoldControlCount);
        Assert.Equal(0, result.BoldItalicControlCount);

        var document = XDocument.Parse(result.WordOpenXml);
        XNamespace math = MathNamespace;
        var runs = document.Descendants(math + "r")
            .Select(run => new
            {
                Text = run.Element(math + "t")?.Value ?? "",
                Style = run.Element(math + "rPr")
                    ?.Element(math + "sty")
                    ?.Attribute(math + "val")
                    ?.Value ?? "",
            })
            .Where(run => run.Text.Length > 0)
            .ToArray();

        Assert.Contains(runs, run => run.Text == "x" && run.Style == "b");
        Assert.Contains(runs, run => run.Text == "y" && run.Style == "bi");

        var verification = EquationStyleRewriter.Verify(result.WordOpenXml, result);
        Assert.Equal(
            verification.ExpectedContractSha256,
            verification.ActualContractSha256
        );
        Assert.Equal(2, verification.StyledRunCount);
        Assert.Equal(1, verification.BoldControlCount);
    }

    [Fact]
    public void AppliesBoldItalicToNaryControlPropertiesAsWellAsTextRuns()
    {
        const string source =
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                        xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <w:body><w:p><m:oMath>
                <m:r><m:t></m:t></m:r>
                <m:nary>
                  <m:naryPr><m:chr m:val="∑"/><m:ctrlPr><w:rPr><w:rFonts w:ascii="Cambria Math"/><w:i/><w:sz w:val="24"/></w:rPr></m:ctrlPr></m:naryPr>
                  <m:sub><m:r><m:t>i=1</m:t></m:r></m:sub>
                  <m:sup><m:r><m:t>n</m:t></m:r></m:sup>
                  <m:e><m:r><m:t>x</m:t></m:r></m:e>
                </m:nary>
                <m:r><m:t></m:t></m:r>
              </m:oMath></w:p></w:body>
            </w:document>
            """;

        var result = EquationStyleRewriter.Rewrite(
            source,
            new EquationStyleCounts(Bold: 0, BoldItalic: 1)
        );
        var document = XDocument.Parse(result.WordOpenXml);
        XNamespace math = MathNamespace;
        XNamespace word =
            "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var controlProperties = document.Descendants(math + "naryPr")
            .Single()
            .Element(math + "ctrlPr")!
            .Element(word + "rPr")!;

        Assert.NotNull(controlProperties.Element(word + "b"));
        Assert.NotNull(controlProperties.Element(word + "i"));
        Assert.Equal(
            ["rFonts", "b", "i", "sz"],
            controlProperties.Elements().Select(element => element.Name.LocalName)
        );
        Assert.Equal(0, result.BoldControlCount);
        Assert.Equal(1, result.BoldItalicControlCount);
        var verification = EquationStyleRewriter.Verify(result.WordOpenXml, result);
        Assert.Equal(1, verification.BoldItalicControlCount);
    }

    [Fact]
    public void SplitsVisibleTextWhenWordCoalescesMarkersIntoOneMathRun()
    {
        const string source =
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                        xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <w:body><w:p><m:oMath><m:r><m:t>x+y</m:t></m:r></m:oMath></w:p></w:body>
            </w:document>
            """;

        var result = EquationStyleRewriter.Rewrite(
            source,
            new EquationStyleCounts(Bold: 1, BoldItalic: 0)
        );
        var document = XDocument.Parse(result.WordOpenXml);
        XNamespace math = MathNamespace;
        var run = Assert.Single(
            document.Descendants(math + "r"),
            item => item.Element(math + "t")?.Value == "x+y"
        );

        Assert.Equal(
            "b",
            run.Element(math + "rPr")
                ?.Element(math + "sty")
                ?.Attribute(math + "val")
                ?.Value
        );
    }

    [Fact]
    public void AcceptsAnOMathDocumentRootInsteadOfRequiringAWordWrapper()
    {
        const string source =
            """
            <m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <m:r><m:t>x</m:t></m:r>
            </m:oMath>
            """;

        var result = EquationStyleRewriter.Rewrite(
            source,
            new EquationStyleCounts(Bold: 1, BoldItalic: 0)
        );

        Assert.Equal(1, result.BoldRunCount);
        Assert.DoesNotContain('', result.WordOpenXml);
        Assert.DoesNotContain('', result.WordOpenXml);
    }

    [Fact]
    public void CreatesStrictWordControlPropertiesForStrictOfficeMath()
    {
        const string source =
            """
            <m:oMath xmlns:m="http://purl.oclc.org/ooxml/officeDocument/math">
              <m:r><m:t></m:t></m:r>
              <m:f>
                <m:num><m:r><m:t>x</m:t></m:r></m:num>
                <m:den><m:r><m:t>y</m:t></m:r></m:den>
              </m:f>
              <m:r><m:t></m:t></m:r>
            </m:oMath>
            """;

        var result = EquationStyleRewriter.Rewrite(
            source,
            new EquationStyleCounts(Bold: 0, BoldItalic: 1)
        );
        var document = XDocument.Parse(result.WordOpenXml);
        XNamespace math = "http://purl.oclc.org/ooxml/officeDocument/math";
        XNamespace word = "http://purl.oclc.org/ooxml/wordprocessingml/main";

        Assert.NotNull(
            document.Root!
                .Element(math + "f")!
                .Element(math + "fPr")!
                .Element(math + "ctrlPr")!
                .Element(word + "rPr")
        );
        Assert.Equal(1, result.BoldItalicControlCount);
    }

    [Fact]
    public void FailsClosedWhenAStyleMarkerOrReadbackStyleIsChanged()
    {
        var missingEnd = NestedFractionWordOpenXml.Replace("", "", StringComparison.Ordinal);
        var markerError = Assert.Throws<NativeToolException>(() =>
            EquationStyleRewriter.Rewrite(
                missingEnd,
                new EquationStyleCounts(Bold: 1, BoldItalic: 1)
            )
        );
        Assert.Equal("EQUATION_INVALID", markerError.ErrorCode);

        var result = EquationStyleRewriter.Rewrite(
            NestedFractionWordOpenXml,
            new EquationStyleCounts(Bold: 1, BoldItalic: 1)
        );
        var changed = result.WordOpenXml.Replace(
            "m:val=\"bi\"",
            "m:val=\"b\"",
            StringComparison.Ordinal
        );
        var styleError = Assert.Throws<NativeToolException>(() =>
            EquationStyleRewriter.Verify(changed, result)
        );
        Assert.Equal("EQUATION_INVALID", styleError.ErrorCode);
    }

    [Fact]
    public void ProhibitsDtdsDuringTheInternalWordReadbackRewrite()
    {
        const string source =
            """
            <!DOCTYPE w:document [<!ENTITY x "boom">]>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                        xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <w:body><w:p><m:oMath><m:r><m:t>&x;</m:t></m:r></m:oMath></w:p></w:body>
            </w:document>
            """;

        var error = Assert.Throws<NativeToolException>(() =>
            EquationStyleRewriter.Rewrite(
                source,
                new EquationStyleCounts(Bold: 1, BoldItalic: 0)
            )
        );
        Assert.Equal("EQUATION_INVALID", error.ErrorCode);
    }

    private const string NestedFractionWordOpenXml =
        """
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                    xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
          <w:body>
            <w:p>
              <m:oMath>
                <m:r><m:t></m:t></m:r>
                <m:f>
                  <m:num><m:r><m:t>x</m:t></m:r></m:num>
                  <m:den>
                    <m:r><m:t></m:t></m:r>
                    <m:r><m:t>y</m:t></m:r>
                    <m:r><m:t></m:t></m:r>
                  </m:den>
                </m:f>
                <m:r><m:t></m:t></m:r>
              </m:oMath>
            </w:p>
          </w:body>
        </w:document>
        """;
}
