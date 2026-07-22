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
    public void RewritesPlainBoldItalicAndBoldItalicRunScopes()
    {
        var marked = string.Concat(
            EquationFormattingMarkers.Wrap(
                EquationMathStyle.Plain,
                EquationStyleTarget.RunsOnly,
                "a"
            ),
            EquationFormattingMarkers.Wrap(
                EquationMathStyle.Bold,
                EquationStyleTarget.RunsOnly,
                "b"
            ),
            EquationFormattingMarkers.Wrap(
                EquationMathStyle.Italic,
                EquationStyleTarget.RunsOnly,
                "c"
            ),
            EquationFormattingMarkers.Wrap(
                EquationMathStyle.BoldItalic,
                EquationStyleTarget.RunsOnly,
                "d"
            )
        );
        var plan = EquationFormattingMarkers.FromMarkedLinear(marked);
        var source =
            $"""
            <m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <m:r><m:t>{marked}</m:t></m:r>
            </m:oMath>
            """;

        var result = EquationStyleRewriter.Rewrite(source, plan.StyleCounts);
        var document = XDocument.Parse(result.WordOpenXml);
        XNamespace math = MathNamespace;
        var styles = document.Descendants(math + "r")
            .Where(run => run.Element(math + "t")?.Value.Length > 0)
            .ToDictionary(
                run => run.Element(math + "t")!.Value,
                run => run.Element(math + "rPr")!
                    .Element(math + "sty")!
                    .Attribute(math + "val")!
                    .Value
            );

        Assert.Equal("p", styles["a"]);
        Assert.Equal("b", styles["b"]);
        Assert.Equal("i", styles["c"]);
        Assert.Equal("bi", styles["d"]);
        Assert.Equal(1, result.PlainRunCount);
        Assert.Equal(1, result.BoldRunCount);
        Assert.Equal(1, result.ItalicRunCount);
        Assert.Equal(1, result.BoldItalicRunCount);
        var verification = EquationStyleRewriter.Verify(result.WordOpenXml, result);
        Assert.Equal(4, verification.StyledRunCount);
    }

    [Fact]
    public void AppliesAControlOnlyItalicScopeToOneFractionWithoutStylingItsChildren()
    {
        var marked = EquationFormattingMarkers.Wrap(
            EquationMathStyle.Italic,
            EquationStyleTarget.FirstControl,
            "(x)/(y)"
        );
        var plan = EquationFormattingMarkers.FromMarkedLinear(marked);
        var start = marked[0];
        var end = marked[^1];
        var source =
            $"""
            <m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math"
                     xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <m:r><m:t>{start}</m:t></m:r>
              <m:f>
                <m:num><m:r><m:t>x</m:t></m:r></m:num>
                <m:den><m:r><m:t>y</m:t></m:r></m:den>
              </m:f>
              <m:r><m:t>{end}</m:t></m:r>
            </m:oMath>
            """;

        var result = EquationStyleRewriter.Rewrite(source, plan.StyleCounts);
        var document = XDocument.Parse(result.WordOpenXml);
        XNamespace math = MathNamespace;
        XNamespace word =
            "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var control = document.Descendants(math + "fPr")
            .Single()
            .Element(math + "ctrlPr")!
            .Element(word + "rPr")!;

        Assert.Equal("0", control.Element(word + "b")!.Attribute(word + "val")!.Value);
        Assert.NotNull(control.Element(word + "i"));
        Assert.All(
            document.Descendants(math + "r")
                .Where(run => run.Element(math + "t")?.Value is "x" or "y"),
            run => Assert.Null(run.Element(math + "rPr")?.Element(math + "sty"))
        );
        Assert.Equal(0, result.StyledRunCount);
        Assert.Equal(1, result.ItalicControlCount);
        var verification = EquationStyleRewriter.Verify(result.WordOpenXml, result);
        Assert.Equal(1, verification.ItalicControlCount);
    }

    [Fact]
    public void FailsClosedWhenAControlOnlyScopeDoesNotBindToAControl()
    {
        var marked = EquationFormattingMarkers.Wrap(
            EquationMathStyle.Plain,
            EquationStyleTarget.FirstControl,
            "x"
        );
        var plan = EquationFormattingMarkers.FromMarkedLinear(marked);
        var source =
            $"""
            <m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <m:r><m:t>{marked}</m:t></m:r>
            </m:oMath>
            """;

        var error = Assert.Throws<NativeToolException>(() =>
            EquationStyleRewriter.Rewrite(source, plan.StyleCounts)
        );

        Assert.Equal("EQUATION_INVALID", error.ErrorCode);
        Assert.Contains("control-only", error.Message, StringComparison.OrdinalIgnoreCase);
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
    public void AcceptsWordsDefaultEquivalentItalicAndRomanCanonicalization()
    {
        var marked = EquationFormattingMarkers.Wrap(
            EquationMathStyle.Italic,
            EquationStyleTarget.RunsOnly,
            "x"
        );
        var plan = EquationFormattingMarkers.FromMarkedLinear(marked);
        var source =
            $"""
            <m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <m:r><m:rPr><m:scr m:val="roman"/></m:rPr><m:t>{marked}</m:t></m:r>
            </m:oMath>
            """;

        var result = EquationStyleRewriter.Rewrite(source, plan.StyleCounts);
        var canonicalized = result.WordOpenXml
            .Replace("<m:sty m:val=\"i\" />", "", StringComparison.Ordinal)
            .Replace("<m:scr m:val=\"roman\" />", "", StringComparison.Ordinal);

        Assert.NotEqual(result.WordOpenXml, canonicalized);
        var verification = EquationStyleRewriter.Verify(canonicalized, result);
        Assert.Equal(1, verification.StyledRunCount);
        Assert.Equal(1, verification.ItalicRunCount);
        Assert.Equal(
            verification.ExpectedContractSha256,
            verification.ActualContractSha256
        );
    }

    [Fact]
    public void AcceptsReadbackCoalescingOfAdjacentSemanticallyEqualRuns()
    {
        var marked = string.Concat(
            EquationFormattingMarkers.Wrap(
                EquationMathStyle.Plain,
                EquationStyleTarget.RunsOnly,
                "a"
            ),
            "+",
            EquationFormattingMarkers.Wrap(
                EquationMathStyle.Bold,
                EquationStyleTarget.RunsOnly,
                "b"
            ),
            "+",
            EquationFormattingMarkers.Wrap(
                EquationMathStyle.Italic,
                EquationStyleTarget.RunsOnly,
                "c"
            ),
            "+",
            EquationFormattingMarkers.Wrap(
                EquationMathStyle.BoldItalic,
                EquationStyleTarget.RunsOnly,
                "d"
            )
        );
        var plan = EquationFormattingMarkers.FromMarkedLinear(marked);
        var source =
            $"""
            <m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <m:r><m:t>{marked}</m:t></m:r>
            </m:oMath>
            """;

        var result = EquationStyleRewriter.Rewrite(source, plan.StyleCounts);
        var document = XDocument.Parse(result.WordOpenXml);
        XNamespace math = MathNamespace;
        var runs = document.Descendants(math + "r")
            .Where(run => run.Element(math + "t")?.Value.Length > 0)
            .ToArray();
        var middle = runs.Skip(3).Take(3).ToArray();
        Assert.Equal(["+", "c", "+"], middle.Select(run => run.Element(math + "t")!.Value));
        middle[0].Element(math + "t")!.Value = "+c+";
        middle[0].Element(math + "rPr")?.Remove();
        middle[1].Remove();
        middle[2].Remove();

        var verification = EquationStyleRewriter.Verify(
            document.ToString(SaveOptions.DisableFormatting),
            result
        );
        Assert.Equal(4, verification.StyledRunCount);
        Assert.Equal(1, verification.ItalicRunCount);
        Assert.Equal(
            verification.ExpectedContractSha256,
            verification.ActualContractSha256
        );
    }

    [Fact]
    public void DetectsLossOfNormalTextSemanticsDuringReadback()
    {
        var marked = EquationFormattingMarkers.Wrap(
            EquationMathStyle.Bold,
            EquationStyleTarget.RunsOnly,
            "x"
        );
        var plan = EquationFormattingMarkers.FromMarkedLinear(marked);
        var source =
            $"""
            <m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <m:r><m:t>{marked}</m:t></m:r>
              <m:r><m:rPr><m:nor/></m:rPr><m:t>text</m:t></m:r>
            </m:oMath>
            """;

        var result = EquationStyleRewriter.Rewrite(source, plan.StyleCounts);
        var changed = result.WordOpenXml.Replace("<m:nor />", "", StringComparison.Ordinal);

        Assert.NotEqual(result.WordOpenXml, changed);
        var error = Assert.Throws<NativeToolException>(() =>
            EquationStyleRewriter.Verify(changed, result)
        );
        Assert.Equal("EQUATION_INVALID", error.ErrorCode);
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
