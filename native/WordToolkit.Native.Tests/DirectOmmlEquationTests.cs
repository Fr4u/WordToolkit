using System.Text.Json;
using WordToolkit.Native.Equations;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class DirectOmmlEquationTests
{
    private const string Transitional =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string Strict =
        "http://purl.oclc.org/ooxml/officeDocument/math";

    [Fact]
    public void ParsesOneComplexTransitionalEquationAndBuildsInsertXml()
    {
        var plan = DirectOmmlEquationParser.Parse(
            $"""
            <m:oMath xmlns:m="{Transitional}">
              <m:sPre>
                <m:sub><m:r><m:t>a</m:t></m:r></m:sub>
                <m:sup><m:r><m:t>b</m:t></m:r></m:sup>
                <m:e>
                  <m:groupChr>
                    <m:groupChrPr><m:chr m:val="⏞"/><m:pos m:val="top"/></m:groupChrPr>
                    <m:e><m:r><m:t>x+y</m:t></m:r></m:e>
                  </m:groupChr>
                </m:e>
              </m:sPre>
            </m:oMath>
            """
        );

        Assert.Equal("transitional", plan.NamespaceIdentity);
        Assert.Matches("^[0-9a-f]{64}$", plan.SemanticSha256);
        Assert.Contains("<m:sPre>", plan.SourceOmml, StringComparison.Ordinal);
        Assert.Contains("<w:document", plan.InsertXml, StringComparison.Ordinal);
        Assert.Contains("<m:oMath", plan.InsertXml, StringComparison.Ordinal);
        Assert.Contains("_(a)^(b)", plan.LinearSemantic, StringComparison.Ordinal);
        Assert.True(plan.ElementCount >= 10);
    }

    [Fact]
    public void AcceptsStrictOmmlAndStrictWordFormattingOnlyInsideMathFormatting()
    {
        var plan = DirectOmmlEquationParser.Parse(
            $"""
            <x:oMath xmlns:x="{Strict}"
                     xmlns:w="http://purl.oclc.org/ooxml/wordprocessingml/main">
              <x:r>
                <w:rPr><w:b/><w:color w:val="FF0000"/></w:rPr>
                <x:t>x</x:t>
              </x:r>
            </x:oMath>
            """
        );

        Assert.Equal("strict", plan.NamespaceIdentity);
        Assert.Contains(Strict, plan.InsertXml, StringComparison.Ordinal);
    }

    [Fact]
    public void SemanticHashIgnoresPrefixesFormattingWhitespaceAndAttributeOrder()
    {
        var first = DirectOmmlEquationParser.Parse(
            $"<m:oMath xmlns:m=\"{Transitional}\"><m:nary><m:naryPr><m:chr m:val=\"∑\"/><m:subHide m:val=\"0\"/></m:naryPr><m:sub><m:r><m:t>i</m:t></m:r></m:sub><m:sup><m:r><m:t>n</m:t></m:r></m:sup><m:e><m:r><m:t>x</m:t></m:r></m:e></m:nary></m:oMath>"
        );
        var second = DirectOmmlEquationParser.Parse(
            $"""
            <q:oMath xmlns:q="{Transitional}">
              <q:nary>
                <q:naryPr><q:subHide q:val="0"/><q:chr q:val="∑"/></q:naryPr>
                <q:sub><q:r><q:t>i</q:t></q:r></q:sub>
                <q:sup><q:r><q:t>n</q:t></q:r></q:sup>
                <q:e><q:r><q:t>x</q:t></q:r></q:e>
              </q:nary>
            </q:oMath>
            """
        );

        Assert.Equal(first.SemanticSha256, second.SemanticSha256);
        Assert.Equal(first.LinearSemantic, second.LinearSemantic);
    }

    [Fact]
    public void SemanticHashPreservesExactMathTextAndStructure()
    {
        var x = DirectOmmlEquationParser.Parse(SimpleRun("x"));
        var spaced = DirectOmmlEquationParser.Parse(SimpleRun(" x "));
        var y = DirectOmmlEquationParser.Parse(SimpleRun("y"));

        Assert.NotEqual(x.SemanticSha256, spaced.SemanticSha256);
        Assert.NotEqual(x.SemanticSha256, y.SemanticSha256);
    }

    [Fact]
    public void SemanticHashIgnoresWordInjectedRunAndControlFormattingDefaults()
    {
        var plain = DirectOmmlEquationParser.Parse(SimpleRun("x"));
        var withDefaults = DirectOmmlEquationParser.Parse(
            $"<m:oMath xmlns:m=\"{Transitional}\" xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><m:r><w:rPr><w:rFonts w:ascii=\"Cambria Math\"/></w:rPr><m:t>x</m:t></m:r></m:oMath>"
        );

        Assert.Equal(plain.SemanticSha256, withDefaults.SemanticSha256);
    }

    [Fact]
    public void SemanticHashNormalizesDocumentedDefaultMathStyleAndScript()
    {
        var implicitDefaults = DirectOmmlEquationParser.Parse(SimpleRun("x"));
        var explicitDefaults = DirectOmmlEquationParser.Parse(
            $"<m:oMath xmlns:m=\"{Transitional}\"><m:r><m:rPr><m:sty m:val=\"i\"/><m:scr m:val=\"roman\"/></m:rPr><m:t>x</m:t></m:r></m:oMath>"
        );
        var explicitPlain = DirectOmmlEquationParser.Parse(
            $"<m:oMath xmlns:m=\"{Transitional}\"><m:r><m:rPr><m:sty m:val=\"p\"/></m:rPr><m:t>x</m:t></m:r></m:oMath>"
        );

        Assert.Equal(implicitDefaults.SemanticSha256, explicitDefaults.SemanticSha256);
        Assert.NotEqual(implicitDefaults.SemanticSha256, explicitPlain.SemanticSha256);
    }

    [Fact]
    public void ParsesExactlyOneEquationFromWordReadbackWrapper()
    {
        var source = DirectOmmlEquationParser.Parse(SimpleRun("x"));
        var readback = DirectOmmlEquationParser.ParseWordReadback(source.InsertXml);

        Assert.Equal(source.SemanticSha256, readback.SemanticSha256);
    }

    [Fact]
    public void ParagraphPropertiesAreHashedAndSurvivePublicationAndReadback()
    {
        var withProps = DirectOmmlEquationParser.Parse(
            $"<m:oMathPara xmlns:m=\"{Transitional}\"><m:oMathParaPr><m:jc m:val=\"center\"/></m:oMathParaPr><m:oMath><m:r><m:t>x</m:t></m:r></m:oMath></m:oMathPara>"
        );
        var withoutProps = DirectOmmlEquationParser.Parse(SimpleRun("x"));
        Assert.NotEqual(withProps.SemanticSha256, withoutProps.SemanticSha256);
        Assert.Contains("jc", withProps.ParagraphPropertiesOmml!, StringComparison.Ordinal);
        var template =
            $"<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:m=\"{Transitional}\"><w:body><w:p><m:oMathPara><m:oMathParaPr/><m:oMath><m:r><m:t>z</m:t></m:r></m:oMath></m:oMathPara></w:p></w:body></w:document>";
        var published = DirectOmmlEquationParser.BuildWordInsertXml(template, withProps);
        Assert.Contains("m:jc", published, StringComparison.Ordinal);
        var readback = DirectOmmlEquationParser.ParseWordReadback(published);
        Assert.Equal(withProps.SemanticSha256, readback.SemanticSha256);
    }

    [Fact]
    public void ReplacesExactlyOneWordTemplateEquationAndNormalizesStrictNamespaces()
    {
        var strict = DirectOmmlEquationParser.Parse(
            $"<m:oMath xmlns:m=\"{Strict}\"><m:f><m:num><m:r><m:t>a</m:t></m:r></m:num><m:den><m:r><m:t>b</m:t></m:r></m:den></m:f></m:oMath>"
        );
        var transitionalTemplate = DirectOmmlEquationParser.Parse(SimpleRun("x"));

        var replaced = DirectOmmlEquationParser.BuildWordInsertXml(
            transitionalTemplate.InsertXml,
            strict
        );
        var readback = DirectOmmlEquationParser.ParseWordReadback(replaced);

        Assert.Equal("transitional", readback.NamespaceIdentity);
        Assert.Contains("<m:f>", replaced, StringComparison.Ordinal);
        Assert.DoesNotContain(Strict, replaced, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsParagraphPropertiesForInlineDirectOmmlBeforeWordStarts()
    {
        await using var host = new WordComHost(_ =>
            throw new InvalidOperationException("Word must not be started")
        );
        var service = new WordLiveService(host);
        var error = await Assert.ThrowsAsync<NativeToolException>(() =>
            service.CallAsync(
                "insert_live_word_equation",
                JsonSerializer.SerializeToElement(
                    new
                    {
                        live_document_id = "not-used",
                        expected_version = 0,
                        value =
                            $"<m:oMathPara xmlns:m=\"{Transitional}\"><m:oMathParaPr><m:jc m:val=\"center\"/></m:oMathParaPr><m:oMath><m:r><m:t>x</m:t></m:r></m:oMath></m:oMathPara>",
                        input_format = "omml",
                        display = false,
                    }
                ),
                CancellationToken.None
            )
        );
        Assert.Equal("EQUATION_INVALID", error.ErrorCode);
        Assert.Contains("display=true", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public void RejectsUnsafeAmbiguousOrUnsupportedFragments(string source)
    {
        var error = Assert.Throws<NativeToolException>(() =>
            DirectOmmlEquationParser.Parse(source)
        );

        Assert.Contains(error.ErrorCode, new[] { "EQUATION_INVALID", "LIMIT_EXCEEDED" });
    }

    [Fact]
    public void RejectsExcessiveDepthAndElementCount()
    {
        var opening = string.Concat(Enumerable.Repeat("<m:e>", 66));
        var closing = string.Concat(Enumerable.Repeat("</m:e>", 66));
        var deep = $"<m:oMath xmlns:m=\"{Transitional}\">{opening}<m:r><m:t>x</m:t></m:r>{closing}</m:oMath>";
        var depthError = Assert.Throws<NativeToolException>(() =>
            DirectOmmlEquationParser.Parse(deep)
        );
        Assert.Equal("LIMIT_EXCEEDED", depthError.ErrorCode);

        var cells = string.Concat(Enumerable.Repeat("<m:e/>", 10_000));
        var many = $"<m:oMath xmlns:m=\"{Transitional}\">{cells}</m:oMath>";
        var countError = Assert.Throws<NativeToolException>(() =>
            DirectOmmlEquationParser.Parse(many)
        );
        Assert.Equal("LIMIT_EXCEEDED", countError.ErrorCode);
    }

    public static IEnumerable<object[]> InvalidInputs()
    {
        yield return [""];
        yield return [$"<m:r xmlns:m=\"{Transitional}\"><m:t>x</m:t></m:r>"];
        yield return [
            $"<m:oMathPara xmlns:m=\"{Transitional}\"><m:oMath><m:r><m:t>x</m:t></m:r></m:oMath><m:oMath><m:r><m:t>y</m:t></m:r></m:oMath></m:oMathPara>"
        ];
        yield return [
            $"<m:oMathPara xmlns:m=\"{Transitional}\"><m:oMathParaPr/><m:oMath><m:r><m:t>x</m:t></m:r></m:oMath></m:oMathPara>"
        ];
        yield return [
            $"<m:oMathPara xmlns:m=\"{Transitional}\"><m:oMathParaPr><m:jc m:val=\"both\"/></m:oMathParaPr><m:oMath><m:r><m:t>x</m:t></m:r></m:oMath></m:oMathPara>"
        ];
        yield return [
            $"<m:oMath xmlns:m=\"{Transitional}\"><m:oMath><m:r><m:t>x</m:t></m:r></m:oMath></m:oMath>"
        ];
        yield return [
            $"<m:oMath xmlns:m=\"{Transitional}\" xmlns:s=\"{Strict}\"><s:r><s:t>x</s:t></s:r></m:oMath>"
        ];
        yield return [
            $"<m:oMath xmlns:m=\"{Transitional}\" xmlns:w=\"{Transitional.Replace("officeDocument/2006/math", "wordprocessingml/2006/main")}\"><w:hyperlink><m:r><m:t>x</m:t></m:r></w:hyperlink></m:oMath>"
        ];
        yield return [
            $"<m:oMath xmlns:m=\"{Transitional}\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"><mc:AlternateContent/></m:oMath>"
        ];
        yield return [
            $"<m:oMath xmlns:m=\"{Transitional}\"><!--secret--><m:r><m:t>x</m:t></m:r></m:oMath>"
        ];
        yield return [
            $"<?probe x?><m:oMath xmlns:m=\"{Transitional}\"><m:r><m:t>x</m:t></m:r></m:oMath>"
        ];
        yield return [
            $"<!DOCTYPE x [<!ENTITY e 'x'>]><m:oMath xmlns:m=\"{Transitional}\"><m:r><m:t>&e;</m:t></m:r></m:oMath>"
        ];
        yield return [
            $"<m:oMath xmlns:m=\"{Transitional}\">visible<m:r><m:t>x</m:t></m:r></m:oMath>"
        ];
        yield return [
            $"<m:oMath xmlns:m=\"{Transitional}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><m:r r:id=\"rId1\"><m:t>x</m:t></m:r></m:oMath>"
        ];
        yield return [
            $"<m:oMath xmlns:m=\"{Transitional}\"><m:unsupported><m:r><m:t>x</m:t></m:r></m:unsupported></m:oMath>"
        ];
    }

    private static string SimpleRun(string text) =>
        $"<m:oMath xmlns:m=\"{Transitional}\"><m:r><m:t>{text}</m:t></m:r></m:oMath>";
}
