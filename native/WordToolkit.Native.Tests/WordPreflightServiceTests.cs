using System.Text.Json;
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
    public async Task EquationPreflightRejectsFormatAliasesInsteadOfGuessing()
    {
        await using var host = new LifecycleFakeHost();
        var service = new WordLiveService(host);
        using var arguments = JsonDocument.Parse(
            """
            {
              "equations": [
                {
                  "value": "<m:oMath />",
                  "source_format": "omml"
                }
              ]
            }
            """
        );

        var error = await Assert.ThrowsAsync<NativeToolException>(() =>
            service.CallAsync(
                "preflight_live_word_equations",
                arguments.RootElement,
                CancellationToken.None
            )
        );

        Assert.Equal("INVALID_INPUT", error.ErrorCode);
        Assert.Contains("input_format", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EquationPreflightReportsNativeBoldRegionsWithoutLeakingMarkers()
    {
        await using var host = new LifecycleFakeHost();
        var service = new WordLiveService(host);
        using var arguments = JsonDocument.Parse(
            """
            {
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
}
