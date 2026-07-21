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
}
