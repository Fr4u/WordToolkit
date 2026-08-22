using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class InlineTextRunContractTests
{
    [Fact]
    public void ParsesInlineRunsAsOneBoundedTextOperation()
    {
        using var document = JsonDocument.Parse(
            """
            [
              {"text":"Normal "},
              {"text":"bold","formatting":{"bold":true}},
              {"text":" and italic","formatting":{"italic":true}}
            ]
            """
        );

        var summary = WordLiveService.ParseTextRunsForTesting(document.RootElement);

        Assert.Equal("Normal bold and italic", summary.Text);
        Assert.Equal(3, summary.RunCount);
    }

    [Fact]
    public void RejectsParagraphFormattingInsideAnInlineRun()
    {
        using var document = JsonDocument.Parse(
            """[{"text":"x","formatting":{"paragraph_alignment":"center"}}]"""
        );

        var error = Assert.Throws<NativeToolException>(() =>
            WordLiveService.ParseTextRunsForTesting(document.RootElement)
        );

        Assert.Equal("INVALID_INPUT", error.ErrorCode);
    }

    [Fact]
    public void RejectsCombinedInlineTextAboveTheOperationLimitBeforeConcatenation()
    {
        var oversized = new string('x', 100_001);
        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(new[] { new { text = oversized }, new { text = oversized } })
        );

        var error = Assert.Throws<NativeToolException>(() =>
            WordLiveService.ParseTextRunsForTesting(document.RootElement)
        );

        Assert.Equal("LIMIT_EXCEEDED", error.ErrorCode);
    }

    [Fact]
    public void NormalizesDocumentFormattingAliasesToCanonicalNames()
    {
        using var document = JsonDocument.Parse(
            """{"font_size":12,"alignment":"center","font_name":"Times New Roman"}"""
        );

        var normalized = WordLiveService.NormalizeFormattingForTesting(document.RootElement);

        Assert.Equal(12, normalized.GetProperty("font_size_pt").GetDouble());
        Assert.Equal("center", normalized.GetProperty("paragraph_alignment").GetString());
        Assert.False(normalized.TryGetProperty("font_size", out _));
        Assert.False(normalized.TryGetProperty("alignment", out _));
    }

    [Fact]
    public void RejectsAliasAndCanonicalFormattingConflictBeforeCom()
    {
        using var document = JsonDocument.Parse("""{"font_size":12,"font_size_pt":14}""");

        var error = Assert.Throws<NativeToolException>(() =>
            WordLiveService.NormalizeFormattingForTesting(document.RootElement)
        );

        Assert.Equal("INVALID_INPUT", error.ErrorCode);
    }

    [Theory]
    [InlineData("{\"bold\":\"yes\"}")]
    [InlineData("{\"font_size_pt\":\"12\"}")]
    [InlineData("{\"font_name\":7}")]
    [InlineData("{\"font_color_rgb\":\"red\"}")]
    public void RejectsInvalidInlineFormattingTypesBeforeCom(string json)
    {
        using var document = JsonDocument.Parse(json);

        var error = Assert.Throws<NativeToolException>(() =>
            WordLiveService.NormalizeFormattingForTesting(
                document.RootElement,
                allowParagraphFormatting: false
            )
        );

        Assert.Equal("INVALID_INPUT", error.ErrorCode);
    }

    [Fact]
    public void PublishedActionSchemaKeepsLegacyTextAndInlineRunsMutuallyExclusive()
    {
        var tool = ToolCatalog.LoadNativeWordTools()
            .InspectAction("apply_live_word_operations")["tool"]!
            .AsObject();
        var input = tool["inputSchema"]!.AsObject();
        var definitions = input["$defs"]!.AsObject();
        var item = input["properties"]!["operations"]!["items"]!.AsObject();
        var textVariant = item["oneOf"]![0]!.AsObject();

        Assert.True(definitions.ContainsKey("liveTextFormatting"));
        Assert.True(definitions.ContainsKey("liveRunFormatting"));
        var textFormatting = definitions["liveTextFormatting"]!.AsObject();
        var formattingProperties = textFormatting["properties"]!.AsObject();
        Assert.Equal("number", formattingProperties["font_size_pt"]!["type"]!.GetValue<string>());
        Assert.True(formattingProperties["font_size"]!["deprecated"]!.GetValue<bool>());
        Assert.Equal("boolean", formattingProperties["bold"]!["type"]!.GetValue<string>());
        Assert.Equal("boolean", formattingProperties["underline"]!["type"]!.GetValue<string>());
        Assert.True(formattingProperties["alignment"]!["deprecated"]!.GetValue<bool>());
        Assert.Contains("font_size_pt", textFormatting["allOf"]!.ToJsonString());
        Assert.Contains("paragraph_alignment", textFormatting["allOf"]!.ToJsonString());
        Assert.Equal(2, textVariant["oneOf"]!.AsArray().Count);
        Assert.Contains(
            "#/$defs/liveRunFormatting",
            item.ToJsonString(),
            StringComparison.Ordinal
        );
        Assert.Contains("\"type\":\"null\"", item.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("#/definitions/", item.ToJsonString(), StringComparison.Ordinal);
    }
}
