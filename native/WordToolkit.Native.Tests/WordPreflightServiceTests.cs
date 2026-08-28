using System.Text.Json;
using WordToolkit.Native.Equations;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class WordPreflightServiceTests
{
    [Fact]
    public async Task TableFormulaPreflightAcceptsTypedFormulaAndRejectsRawCodeShape()
    {
        await using var host = new LifecycleFakeHost();
        var service = new WordLiveService(host);
        using var valid = JsonDocument.Parse(
            """
            {
              "formulas": [
                {
                  "row": 3,
                  "column": 2,
                  "function": "sum",
                  "directions": ["above"],
                  "numeric_format": "0.00"
                }
              ]
            }
            """
        );

        var result = await service.CallAsync(
            "preflight_live_word_table_formulas",
            valid.RootElement,
            CancellationToken.None
        );
        using var resultJson = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.True(resultJson.RootElement.GetProperty("valid").GetBoolean());
        Assert.False(
            resultJson.RootElement
                .GetProperty("raw_field_codes_accepted")
                .GetBoolean()
        );

        using var invalid = JsonDocument.Parse(
            """{"formulas":[{"row":1,"column":1,"function":"sum","field_code":"=SUM(ABOVE)"}]}"""
        );
        var invalidResult = await service.CallAsync(
            "preflight_live_word_table_formulas",
            invalid.RootElement,
            CancellationToken.None
        );
        using var invalidJson = JsonDocument.Parse(
            JsonSerializer.Serialize(invalidResult)
        );
        Assert.False(invalidJson.RootElement.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task TableFormulaExpressionsCoverDurationCalendarAndParameterizedTaxMath()
    {
        await using var host = new LifecycleFakeHost();
        var service = new WordLiveService(host);
        using var valid = JsonDocument.Parse(
            """
            {
              "formulas": [
                {"row":2,"column":3,"function":"expression","expression":"(B2-A2)*24","numeric_format":"0.00"},
                {"row":3,"column":3,"function":"expression","expression":"INT(B3/7)-INT((A3-1)/7)"},
                {"row":4,"column":4,"function":"expression","expression":"IF(A4>120000,(A4-120000)*0.32+A4*0.12,A4*0.12)","numeric_format":"0.00"},
                {"row":5,"column":4,"function":"expression","expression":"MAX(0,A5-B5)*C5","numeric_format":"0.00"}
              ]
            }
            """
        );

        var result = await service.CallAsync(
            "preflight_live_word_table_formulas",
            valid.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(result, JsonDefaults.Compact)
        );

        Assert.True(json.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal(4, json.RootElement.GetProperty("valid_count").GetInt32());
        Assert.All(
            json.RootElement.GetProperty("formulas").EnumerateArray(),
            formula => Assert.Equal("expression", formula.GetProperty("source").GetString())
        );

        using var invalid = JsonDocument.Parse(
            """
            {"formulas":[
              {"row":2,"column":3,"function":"expression","expression":"C2+1"},
              {"row":3,"column":3,"function":"expression","expression":"WEEKDAY(A3)"}
            ]}
            """
        );
        var invalidResult = await service.CallAsync(
            "preflight_live_word_table_formulas",
            invalid.RootElement,
            CancellationToken.None
        );
        using var invalidJson = JsonDocument.Parse(
            JsonSerializer.Serialize(invalidResult, JsonDefaults.Compact)
        );
        Assert.False(invalidJson.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal(2, invalidJson.RootElement.GetProperty("invalid_count").GetInt32());

        using var mixedShapes = JsonDocument.Parse(
            """
            {"formulas":[
              {"row":2,"column":3,"function":"expression","expression":"A2+B2","directions":["above"]},
              {"row":3,"column":3,"function":"sum","expression":"A3+B3","directions":["above"]}
            ]}
            """
        );
        var mixedResult = await service.CallAsync(
            "preflight_live_word_table_formulas",
            mixedShapes.RootElement,
            CancellationToken.None
        );
        using var mixedJson = JsonDocument.Parse(
            JsonSerializer.Serialize(mixedResult, JsonDefaults.Compact)
        );
        Assert.False(mixedJson.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal(2, mixedJson.RootElement.GetProperty("invalid_count").GetInt32());
    }

    [Fact]
    public async Task BookmarkAndFieldPreflightsRemainTypedAndBounded()
    {
        await using var host = new LifecycleFakeHost();
        var service = new WordLiveService(host);
        using var bookmarks = JsonDocument.Parse(
            """
            {
              "bookmarks": [
                {"name":"Result_1","text":"42","as_new_paragraph":true}
              ]
            }
            """
        );
        var bookmarkResult = await service.CallAsync(
            "preflight_live_word_bookmarks",
            bookmarks.RootElement,
            CancellationToken.None
        );
        using var bookmarkJson = JsonDocument.Parse(
            JsonSerializer.Serialize(bookmarkResult)
        );
        Assert.True(bookmarkJson.RootElement.GetProperty("valid").GetBoolean());

        using var fields = JsonDocument.Parse(
            """
            {
              "fields": [
                {"kind":"page"},
                {"kind":"formula","expression":"SUM(1,2,3)","numeric_format":"0.00"}
              ]
            }
            """
        );
        var fieldResult = await service.CallAsync(
            "preflight_live_word_fields",
            fields.RootElement,
            CancellationToken.None
        );
        using var fieldJson = JsonDocument.Parse(JsonSerializer.Serialize(fieldResult));
        Assert.True(fieldJson.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal(
            2,
            fieldJson.RootElement.GetProperty("valid_count").GetInt32()
        );
        Assert.False(
            fieldJson.RootElement
                .GetProperty("raw_field_codes_accepted")
                .GetBoolean()
        );
    }

    [Fact]
    public async Task DuplicateBookmarkNamesFailBeforeWordIsTouched()
    {
        await using var host = new LifecycleFakeHost();
        var service = new WordLiveService(host);
        using var arguments = JsonDocument.Parse(
            """
            {
              "bookmarks": [
                {"name":"Same","text":"one"},
                {"name":"same","text":"two"}
              ]
            }
            """
        );

        var result = await service.CallAsync(
            "preflight_live_word_bookmarks",
            arguments.RootElement,
            CancellationToken.None
        );
        using var resultJson = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.False(resultJson.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal(
            1,
            resultJson.RootElement.GetProperty("invalid_count").GetInt32()
        );
    }

    [Fact]
    public async Task EquationPreflightUsesCanonicalDifferentialsAndRequiresReadback()
    {
        await using var host = new LifecycleFakeHost();
        var service = new WordLiveService(host);
        using var arguments = JsonDocument.Parse(
            """
            {
              "validation_mode": "conversion_only",
              "equations": [
                {
                  "value": "\\int_{-\\infty}^{\\infty} e^{-x^2}\\,d x",
                  "input_format": "latex"
                },
                {
                  "value": "x+1",
                  "input_format": "unicodemath",
                  "verify_readback": true
                }
              ]
            }
            """
        );

        var result = await service.CallAsync(
            "preflight_live_word_equations",
            arguments.RootElement,
            CancellationToken.None
        );
        using var resultJson = JsonDocument.Parse(JsonSerializer.Serialize(result));
        var equations = resultJson.RootElement.GetProperty("equations");

        Assert.Contains(
            "ⅆx",
            equations[0].GetProperty("word_linear").GetString(),
            StringComparison.Ordinal
        );
        Assert.True(
            equations[0].GetProperty("native_readback_required").GetBoolean()
        );
        Assert.True(
            equations[0].GetProperty("native_readback_enabled").GetBoolean()
        );
        Assert.False(
            equations[1].GetProperty("native_readback_required").GetBoolean()
        );
        Assert.True(
            equations[1].GetProperty("native_readback_enabled").GetBoolean()
        );
    }

    [Fact]
    public async Task OmmlConversionOnlyReportsDirectPlanWithoutReturningRawXml()
    {
        await using var host = new LifecycleFakeHost();
        var service = new WordLiveService(host);
        using var arguments = JsonDocument.Parse(
            """
            {
              "validation_mode": "conversion_only",
              "equations": [
                {
                  "value": "<m:oMath xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"><m:f><m:num><m:r><m:t>a</m:t></m:r></m:num><m:den><m:r><m:t>b</m:t></m:r></m:den></m:f></m:oMath>",
                  "input_format": "omml"
                }
              ]
            }
            """
        );

        var result = await service.CallAsync(
            "preflight_live_word_equations",
            arguments.RootElement,
            CancellationToken.None
        );
        var serialized = JsonSerializer.Serialize(result, JsonDefaults.Compact);
        using var json = JsonDocument.Parse(serialized);
        var item = json.RootElement.GetProperty("equations")[0];
        Assert.True(
            item.TryGetProperty("direct_omml", out var direct),
            serialized
        );

        Assert.False(item.TryGetProperty("valid", out _));
        Assert.True(item.GetProperty("conversion_valid").GetBoolean());
        Assert.True(item.GetProperty("native_readback_required").GetBoolean());
        Assert.True(direct.GetProperty("source_validated").GetBoolean());
        Assert.False(direct.GetProperty("native_semantic_verified").GetBoolean());
        Assert.Equal("transitional", direct.GetProperty("namespace_identity").GetString());
        Assert.Matches(
            "^[0-9a-f]{64}$",
            direct.GetProperty("expected_semantic_sha256").GetString()
        );
        Assert.False(direct.GetProperty("raw_omml_returned").GetBoolean());
        Assert.DoesNotContain("<m:oMath", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("<m:f>", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EquationPreflightRejectsFormatAliasesInsteadOfGuessing()
    {
        await using var host = new LifecycleFakeHost();
        var service = new WordLiveService(host);
        using var arguments = JsonDocument.Parse(
            """
            {
              "validation_mode": "conversion_only",
              "equations": [
                {
                  "value": "<m:oMath />",
                  "source_format": "omml"
                }
              ]
            }
            """
        );

        var result = await service.CallAsync(
            "preflight_live_word_equations",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(result, JsonDefaults.Compact)
        );
        Assert.False(json.RootElement.GetProperty("conversion_valid").GetBoolean());
        var item = json.RootElement.GetProperty("equations")[0];
        Assert.Equal("INVALID_INPUT", item.GetProperty("error_code").GetString());
        Assert.Equal("conversion", item.GetProperty("stage").GetString());
    }

    [Fact]
    public async Task EquationPreflightConversionFailureReportsExactInputIndex()
    {
        await using var host = new LifecycleFakeHost();
        var service = new WordLiveService(host);
        using var arguments = JsonDocument.Parse(
            """
            {
              "validation_mode": "conversion_only",
              "equations": [
                {"value":"x+1","input_format":"latex"},
                {"value":"\\unsupportedcommand{x}","input_format":"latex"}
              ]
            }
            """
        );

        var result = await service.CallAsync(
            "preflight_live_word_equations",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(result, JsonDefaults.Compact)
        );
        var item = json.RootElement.GetProperty("equations")[1];
        Assert.Equal(1, item.GetProperty("index").GetInt32());
        Assert.Equal("conversion", item.GetProperty("stage").GetString());
        Assert.Equal(
            "USE_SUPPORTED_LATEX_OR_UNICODEMATH",
            item.GetProperty("suggestion_code").GetString()
        );
    }

    [Fact]
    public async Task EquationPreflightReportsNativeBoldRegionsWithoutLeakingMarkers()
    {
        await using var host = new LifecycleFakeHost();
        var service = new WordLiveService(host);
        using var arguments = JsonDocument.Parse(
            """
            {
              "validation_mode": "conversion_only",
              "equations": [
                {
                  "value": "\\mathbf{x+\\boldsymbol{y}}",
                  "input_format": "latex"
                }
              ]
            }
            """
        );

        var result = await service.CallAsync(
            "preflight_live_word_equations",
            arguments.RootElement,
            CancellationToken.None
        );
        using var resultJson = JsonDocument.Parse(JsonSerializer.Serialize(result));
        var equation = resultJson.RootElement.GetProperty("equations")[0];

        Assert.Equal("x+y", equation.GetProperty("word_linear").GetString());
        Assert.DoesNotContain(
            '\uE100',
            equation.GetProperty("word_linear").GetString() ?? ""
        );
        Assert.True(equation.GetProperty("native_style_rewrite_required").GetBoolean());
        Assert.True(equation.GetProperty("native_readback_required").GetBoolean());
        Assert.Equal(2, equation.GetProperty("formatting_region_count").GetInt32());
        var regions = equation.GetProperty("formatting_regions");
        Assert.Equal(1, regions.GetProperty("bold").GetInt32());
        Assert.Equal(1, regions.GetProperty("bold_italic").GetInt32());
        Assert.Contains(
            equation.GetProperty("rules").EnumerateArray(),
            rule => rule.GetString() == "verified_native_omml_style_rewrite"
        );
    }

    [Fact]
    public async Task EquationPreflightCarriesMathMlAndOmmlStyleScopesWithoutLeakingMarkers()
    {
        await using var host = new LifecycleFakeHost();
        var service = new WordLiveService(host);
        using var arguments = JsonDocument.Parse(
            """
            {
              "validation_mode": "conversion_only",
              "equations": [
                {
                  "value": "<math xmlns=\"http://www.w3.org/1998/Math/MathML\"><mi mathvariant=\"normal\">a</mi><mi mathvariant=\"bold\">b</mi><mi mathvariant=\"italic\">c</mi><mi mathvariant=\"bold-italic\">d</mi></math>",
                  "input_format": "mathml"
                },
                {
                  "value": "<m:oMath xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\" xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><m:f><m:fPr><m:ctrlPr><w:rPr><w:i/></w:rPr></m:ctrlPr></m:fPr><m:num><m:r><m:rPr><m:sty m:val=\"p\"/></m:rPr><m:t>x</m:t></m:r></m:num><m:den><m:r><m:rPr><m:sty m:val=\"bi\"/></m:rPr><m:t>y</m:t></m:r></m:den></m:f></m:oMath>",
                  "input_format": "omml"
                }
              ]
            }
            """
        );

        var result = await service.CallAsync(
            "preflight_live_word_equations",
            arguments.RootElement,
            CancellationToken.None
        );
        using var resultJson = JsonDocument.Parse(JsonSerializer.Serialize(result));
        var mathml = resultJson.RootElement.GetProperty("equations")[0];
        var omml = resultJson.RootElement.GetProperty("equations")[1];

        Assert.Equal("abcd", mathml.GetProperty("word_linear").GetString());
        Assert.Equal(4, mathml.GetProperty("formatting_region_count").GetInt32());
        Assert.Equal(
            1,
            mathml.GetProperty("formatting_regions").GetProperty("plain").GetInt32()
        );
        Assert.Equal(
            1,
            mathml.GetProperty("formatting_regions").GetProperty("italic").GetInt32()
        );
        Assert.Equal("(x)/(y)", omml.GetProperty("word_linear").GetString());
        Assert.Equal(
            1,
            omml.GetProperty("formatting_regions").GetProperty("first_control").GetInt32()
        );
        Assert.DoesNotContain(
            omml.GetProperty("word_linear").GetString() ?? "",
            character => EquationFormattingMarkers.IsReserved(character)
        );
        Assert.True(omml.GetProperty("native_readback_required").GetBoolean());
    }

    [Fact]
    public async Task NativeEquationPreflightBuildsInScratchAndRestoresWordState()
    {
        await using var host = new FeatureBehaviorFakeHost();
        var service = new WordLiveService(host);
        var originalDocument = host.Application.ActiveDocument;
        var originalWindow = host.Application.ActiveWindow;
        using var arguments = JsonDocument.Parse(
            """
            {
              "validation_mode": "native",
              "equations": [
                {
                  "value": "x+1",
                  "input_format": "latex",
                  "verify_readback": false
                }
              ]
            }
            """
        );

        var result = await service.CallAsync(
            "preflight_live_word_equations",
            arguments.RootElement,
            CancellationToken.None
        );
        using var resultJson = JsonDocument.Parse(JsonSerializer.Serialize(result));
        var data = resultJson.RootElement;

        Assert.True(data.GetProperty("valid").GetBoolean());
        Assert.True(data.GetProperty("native_execution_verified").GetBoolean());
        Assert.True(
            data.GetProperty("equations")[0]
                .GetProperty("native_execution_verified")
                .GetBoolean()
        );
        Assert.Equal(1, host.Application.Documents.CreatedCount);
        Assert.Equal(1, host.Application.Documents.ClosedCount);
        Assert.Equal(1, host.Application.Documents.Count);
        Assert.Same(originalDocument, host.Application.ActiveDocument);
        Assert.Same(originalWindow, host.Application.ActiveWindow);
    }

    [Fact]
    public async Task ConversionOnlyEquationPreflightNeverReturnsGreenValidity()
    {
        await using var host = new LifecycleFakeHost();
        var service = new WordLiveService(host);
        using var arguments = JsonDocument.Parse(
            """{"validation_mode":"conversion_only","equations":[{"value":"x+1"}]}"""
        );

        var result = await service.CallAsync(
            "preflight_live_word_equations",
            arguments.RootElement,
            CancellationToken.None
        );
        using var resultJson = JsonDocument.Parse(JsonSerializer.Serialize(result));
        var data = resultJson.RootElement;

        Assert.Equal(JsonValueKind.Null, data.GetProperty("valid").ValueKind);
        Assert.True(data.GetProperty("conversion_valid").GetBoolean());
        Assert.False(data.GetProperty("native_execution_verified").GetBoolean());
        Assert.Equal(0, host.Application.Documents.Count);
    }

    [Fact]
    public async Task ConversionOnlyPreflightCollectsAllErrorsAndKeepsStableEquationIds()
    {
        await using var host = new LifecycleFakeHost();
        var service = new WordLiveService(host);
        using var first = JsonDocument.Parse(
            """
            {
              "validation_mode":"conversion_only",
              "equations":[
                {"value":"x+1","input_format":"latex"},
                {"value":"\\unsupportedcommand{x}","input_format":"latex"},
                {"value":"y+1","input_format":"latex"}
              ]
            }
            """
        );
        var result = await service.CallAsync(
            "preflight_live_word_equations",
            first.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(result, JsonDefaults.Compact)
        );
        var root = json.RootElement;
        Assert.Equal(3, root.GetProperty("equation_count").GetInt32());
        Assert.Equal(2, root.GetProperty("valid_count").GetInt32());
        Assert.Equal(1, root.GetProperty("invalid_count").GetInt32());
        Assert.False(root.GetProperty("conversion_valid").GetBoolean());
        var items = root.GetProperty("equations");
        Assert.Equal(new[] { 0, 1, 2 }, items.EnumerateArray()
            .Select(item => item.GetProperty("index").GetInt32()).ToArray());
        Assert.False(items[1].GetProperty("valid").GetBoolean());
        Assert.Equal(
            "USE_SUPPORTED_LATEX_OR_UNICODEMATH",
            items[1].GetProperty("suggestion_code").GetString()
        );
        var stableId = items[0].GetProperty("equation_id").GetString();

        using var reordered = JsonDocument.Parse(
            """
            {"validation_mode":"conversion_only","equations":[
              {"value":"y+1","input_format":"latex"},
              {"value":"x+1","input_format":"latex"}
            ]}
            """
        );
        var reorderedResult = await service.CallAsync(
            "preflight_live_word_equations",
            reordered.RootElement,
            CancellationToken.None
        );
        using var reorderedJson = JsonDocument.Parse(
            JsonSerializer.Serialize(reorderedResult, JsonDefaults.Compact)
        );
        Assert.Equal(
            stableId,
            reorderedJson.RootElement.GetProperty("equations")[1]
                .GetProperty("equation_id").GetString()
        );
    }
}
