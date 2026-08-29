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
    [InlineData("{\"highlight_color_index\":17}")]
    [InlineData("{\"highlight_color_index\":1.5}")]
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
    public void RejectsDocumentAlignmentOutsideThePublishedContract()
    {
        using var document = JsonDocument.Parse("""{"paragraph_alignment":"thai"}""");

        var error = Assert.Throws<NativeToolException>(() =>
            WordLiveService.NormalizeFormattingForTesting(document.RootElement)
        );

        Assert.Equal("INVALID_INPUT", error.ErrorCode);
    }

    [Fact]
    public void RejectsFontNameAboveThePublishedLimit()
    {
        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(new { font_name = new string('x', 129) })
        );

        var error = Assert.Throws<NativeToolException>(() =>
            WordLiveService.NormalizeFormattingForTesting(document.RootElement)
        );

        Assert.Equal("INVALID_INPUT", error.ErrorCode);
    }

    [Fact]
    public void RejectsMutuallyEnabledStrikeModesBeforeCom()
    {
        using var document = JsonDocument.Parse("""{"strike":true,"double_strike":true}""");

        var error = Assert.Throws<NativeToolException>(() =>
            WordLiveService.NormalizeFormattingForTesting(
                document.RootElement,
                allowParagraphFormatting: false
            )
        );

        Assert.Equal("INVALID_INPUT", error.ErrorCode);
        Assert.Contains("cannot both be true", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AppliesAndCapturesExtendedNativeFormatting()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "strike": false,
              "double_strike": true,
              "highlight_color_index": 7,
              "paragraph_alignment": "distribute"
            }
            """
        );
        var range = new ExtendedFormattingFakeRange();

        var captured = WordLiveService.ApplyAndCaptureFormattingForTesting(
            range,
            document.RootElement
        );

        Assert.Equal(0, range.Font.StrikeThrough);
        Assert.Equal(-1, range.Font.DoubleStrikeThrough);
        Assert.Equal(7, range.HighlightColorIndex);
        Assert.Equal(4, range.ParagraphFormat.Alignment);
        Assert.Equal("false", captured["strike"]);
        Assert.Equal("true", captured["double_strike"]);
        Assert.Equal("7", captured["highlight_color_index"]);
        Assert.Equal("distribute", captured["paragraph_alignment"]);
    }

    [Fact]
    public void AppliesFullCharacterFormattingSurfaceAndReturnsComReadback()
    {
        using var document = JsonDocument.Parse("""
        {"font_name":"Aptos","font_name_ascii":"Arial","font_name_bidi":"Tahoma",
         "font_name_far_east":"Yu Gothic","font_name_other":"Courier New",
         "font_size_pt":11.5,"font_size_bidi_pt":12,"font_color_rgb":"#00FF00",
         "font_color_bidi_index":5,"diacritic_color":"#FF0000","bold":true,"italic":true,
         "bold_bidi":true,"italic_bidi":false,"underline_style":"double","underline_color":"#0000FF",
         "strike":true,"subscript":false,"superscript":false,"all_caps":true,"small_caps":false,
         "hidden":true,"shadow":true,"outline":true,"emboss":true,"engrave":false,
         "scaling_percent":90,"spacing_pt":1.25,"position_pt":-2,"kerning_pt":8,
         "disable_character_space_grid":true,"emphasis_mark":"over_solid_circle",
         "ligatures":"standard_contextual","number_form":"old_style","number_spacing":"proportional",
         "stylistic_sets":[1,3],"contextual_alternates":true,"highlight_color_index":7}
        """);
        var range = new ExtendedFormattingFakeRange();
        var captured = WordLiveService.ApplyAndCaptureFormattingForTesting(range, document.RootElement);

        Assert.Equal("Aptos", range.Font.Name);
        Assert.Equal("Arial", range.Font.NameAscii);
        Assert.Equal("Tahoma", range.Font.NameBi);
        Assert.Equal("Yu Gothic", range.Font.NameFarEast);
        Assert.Equal("Courier New", range.Font.NameOther);
        Assert.Equal(11.5f, range.Font.Size);
        Assert.Equal(12f, range.Font.SizeBi);
        Assert.Equal(0x00FF00, range.Font.Color);
        Assert.Equal(5, range.Font.ColorIndexBi);
        Assert.Equal(0x0000FF, range.Font.DiacriticColor);
        Assert.Equal(-1, range.Font.Bold);
        Assert.Equal(-1, range.Font.Italic);
        Assert.Equal(-1, range.Font.BoldBi);
        Assert.Equal(0, range.Font.ItalicBi);
        Assert.Equal(3, range.Font.Underline);
        Assert.Equal(0xFF0000, range.Font.UnderlineColor);
        Assert.Equal(-1, range.Font.StrikeThrough);
        Assert.Equal(0, range.Font.Subscript);
        Assert.Equal(0, range.Font.Superscript);
        Assert.Equal(-1, range.Font.AllCaps);
        Assert.Equal(0, range.Font.SmallCaps);
        Assert.Equal(-1, range.Font.Hidden);
        Assert.Equal(-1, range.Font.Shadow);
        Assert.Equal(-1, range.Font.Outline);
        Assert.Equal(-1, range.Font.Emboss);
        Assert.Equal(0, range.Font.Engrave);
        Assert.Equal(90, range.Font.Scaling);
        Assert.Equal(1.25f, range.Font.Spacing);
        Assert.Equal(-2, range.Font.Position);
        Assert.Equal(8f, range.Font.Kerning);
        Assert.Equal(-1, range.Font.DisableCharacterSpaceGrid);
        Assert.Equal(1, range.Font.EmphasisMark);
        Assert.Equal(3, range.Font.Ligatures);
        Assert.Equal(2, range.Font.NumberForm);
        Assert.Equal(1, range.Font.NumberSpacing);
        Assert.Equal(5, range.Font.StylisticSet);
        Assert.Equal(-1, range.Font.ContextualAlternates);
        Assert.Equal(7, range.HighlightColorIndex);
        Assert.Equal("Aptos", captured["font_name"]);
        Assert.Equal("#00FF00", captured["font_color_rgb"]);
        Assert.Equal("#FF0000", captured["diacritic_color"]);
        Assert.Equal("true", captured["bold"]);
        Assert.Equal("false", captured["italic_bidi"]);
        Assert.Equal("double", captured["underline_style"]);
        Assert.Equal("#0000FF", captured["underline_color"]);
        Assert.Equal("true", captured["shadow"]);
        Assert.Equal("90", captured["scaling_percent"]);
        Assert.Equal("1.25", captured["spacing_pt"]);
        Assert.Equal("-2", captured["position_pt"]);
        Assert.Equal("8", captured["kerning_pt"]);
        Assert.Equal("over_solid_circle", captured["emphasis_mark"]);
        Assert.Equal("standard_contextual", captured["ligatures"]);
        Assert.Equal("old_style", captured["number_form"]);
        Assert.Equal("proportional", captured["number_spacing"]);
        Assert.Equal("[1,3]", captured["stylistic_sets"]);
        Assert.Equal("true", captured["contextual_alternates"]);
        Assert.Equal("7", captured["highlight_color_index"]);
    }

    [Theory]
    [InlineData("none", 0)]
    [InlineData("single", 1)]
    [InlineData("words", 2)]
    [InlineData("double", 3)]
    [InlineData("dotted", 4)]
    [InlineData("thick", 6)]
    [InlineData("dash", 7)]
    [InlineData("dot_dash", 9)]
    [InlineData("dot_dot_dash", 10)]
    [InlineData("wavy", 11)]
    [InlineData("dotted_heavy", 20)]
    [InlineData("dash_heavy", 23)]
    [InlineData("dot_dash_heavy", 25)]
    [InlineData("dot_dot_dash_heavy", 26)]
    [InlineData("wavy_heavy", 27)]
    [InlineData("dash_long", 39)]
    [InlineData("wavy_double", 43)]
    [InlineData("dash_long_heavy", 55)]
    public void MapsEverySupportedUnderlineStyleToWord(string style, int expected)
    {
        using var document = JsonDocument.Parse($$"""{"underline_style":"{{style}}"}""");
        var range = new ExtendedFormattingFakeRange();

        var captured = WordLiveService.ApplyAndCaptureFormattingForTesting(
            range,
            document.RootElement,
            allowParagraphFormatting: false
        );

        Assert.Equal(expected, range.Font.Underline);
        Assert.Equal(style, captured["underline_style"]);
    }

    [Theory]
    [InlineData("subscript", "Subscript")]
    [InlineData("superscript", "Superscript")]
    public void AppliesBaselineScriptFormatting(string field, string propertyName)
    {
        using var document = JsonDocument.Parse($$"""{"{{field}}":true}""");
        var range = new ExtendedFormattingFakeRange();

        WordLiveService.ApplyAndCaptureFormattingForTesting(
            range,
            document.RootElement,
            allowParagraphFormatting: false
        );

        Assert.Equal(
            -1,
            (int)typeof(ExtendedFormattingFakeFont).GetProperty(propertyName)!.GetValue(range.Font)!
        );
    }

    [Theory]
    [InlineData("{\"underline_style\":\"not-a-style\"}")]
    [InlineData("{\"font_size_pt\":0}")]
    [InlineData("{\"scaling_percent\":601}")]
    [InlineData("{\"underline\":true,\"underline_style\":\"single\"}")]
    [InlineData("{\"subscript\":true,\"superscript\":true}")]
    [InlineData("{\"emboss\":true,\"engrave\":true}")]
    [InlineData("{\"position_pt\":1,\"superscript\":true}")]
    [InlineData("{\"font_color_rgb\":\"#010203\",\"font_color_index\":2}")]
    [InlineData("{\"stylistic_sets\":[1,1]}")]
    public void RejectsInvalidCharacterFormattingBeforeCom(string json)
    {
        using var document = JsonDocument.Parse(json);
        var error = Assert.Throws<NativeToolException>(() =>
            WordLiveService.NormalizeFormattingForTesting(document.RootElement, allowParagraphFormatting: false));
        Assert.Equal("INVALID_INPUT", error.ErrorCode);
    }

    [Fact]
    public void ClearCharacterFormattingCanBeOverriddenInSameRequest()
    {
        using var document = JsonDocument.Parse("{\"clear_character_formatting\":true,\"bold\":true,\"font_name\":\"Calibri\"}");
        var range = new ExtendedFormattingFakeRange();
        range.Font.Name = "Arial";
        range.Font.Color = 0x123456;
        range.Font.Underline = 43;
        range.Font.UnderlineColor = 0x654321;
        range.Font.Shadow = -1;
        range.Font.Spacing = 4;
        range.Font.StylisticSet = 7;
        range.HighlightColorIndex = 6;

        WordLiveService.ApplyAndCaptureFormattingForTesting(range, document.RootElement);

        Assert.Equal(-1, range.Font.Bold);
        Assert.Equal("Calibri", range.Font.Name);
        Assert.Equal(0, range.Font.Color);
        Assert.Equal(0, range.Font.Underline);
        Assert.Equal(unchecked((int)0xFF000000), range.Font.UnderlineColor);
        Assert.Equal(0, range.Font.Shadow);
        Assert.Equal(0, range.Font.Spacing);
        Assert.Equal(0, range.Font.StylisticSet);
        Assert.Equal(0, range.HighlightColorIndex);
    }

    [Fact]
    public void StopsBeforeLaterPropertiesWhenTheFirstComSetterFails()
    {
        using var document = JsonDocument.Parse("{\"font_name\":\"new-name\",\"bold\":true}");
        var range = new ExtendedFormattingFakeRange();
        range.Font.ThrowOnNameSet = true;
        Assert.ThrowsAny<Exception>(() =>
            WordLiveService.ApplyAndCaptureFormattingForTesting(range, document.RootElement));
        Assert.Equal("Calibri", range.Font.Name);
        Assert.Equal(0, range.Font.Bold);
    }

    [Fact]
    public void RejectsWordUndefinedInsteadOfCoercingAMixedRangeToBoolean()
    {
        using var document = JsonDocument.Parse("{\"bold\":true}");
        var range = new MixedFormattingFakeRange();

        var error = Assert.Throws<NativeToolException>(() =>
            WordLiveService.ApplyAndCaptureFormattingForTesting(
                range,
                document.RootElement,
                allowParagraphFormatting: false
            )
        );

        Assert.Equal("FORMATTING_INVALID", error.ErrorCode);
        using var details = JsonDocument.Parse(
            JsonSerializer.Serialize(error.Details, JsonDefaults.Compact)
        );
        Assert.Equal("bold", details.RootElement.GetProperty("field").GetString());
        Assert.Equal("true", details.RootElement.GetProperty("expected").GetString());
        Assert.Equal("9999999", details.RootElement.GetProperty("actual").GetString());
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
        Assert.True(definitions.ContainsKey("liveCharacterFormatting"));
        var textFormatting = definitions["liveTextFormatting"]!.AsObject();
        var characterFormatting = definitions["liveCharacterFormatting"]!.AsObject();
        var formattingProperties = characterFormatting["properties"]!.AsObject();
        var paragraphFormattingProperties = textFormatting["properties"]!.AsObject();
        Assert.Equal("number", formattingProperties["font_size_pt"]!["type"]!.GetValue<string>());
        Assert.Equal(1638, formattingProperties["font_size_pt"]!["maximum"]!.GetValue<int>());
        Assert.True(formattingProperties["font_size"]!["deprecated"]!.GetValue<bool>());
        Assert.Equal("boolean", formattingProperties["bold"]!["type"]!.GetValue<string>());
        Assert.Equal("boolean", formattingProperties["underline"]!["type"]!.GetValue<string>());
        Assert.True(formattingProperties["underline"]!["deprecated"]!.GetValue<bool>());
        Assert.Equal(
            18,
            formattingProperties["underline_style"]!["enum"]!.AsArray().Count
        );
        Assert.Equal("boolean", formattingProperties["subscript"]!["type"]!.GetValue<string>());
        Assert.Equal("boolean", formattingProperties["superscript"]!["type"]!.GetValue<string>());
        Assert.Equal(
            "integer",
            formattingProperties["scaling_percent"]!["type"]!.GetValue<string>()
        );
        Assert.Equal(
            "array",
            formattingProperties["stylistic_sets"]!["type"]!.GetValue<string>()
        );
        Assert.Equal(
            "boolean",
            formattingProperties["clear_character_formatting"]!["type"]!.GetValue<string>()
        );
        Assert.Equal(
            "boolean",
            formattingProperties["double_strike"]!["type"]!.GetValue<string>()
        );
        Assert.Equal(
            16,
            formattingProperties["highlight_color_index"]!["maximum"]!.GetValue<int>()
        );
        Assert.Contains(
            "distribute",
            paragraphFormattingProperties["paragraph_alignment"]!["enum"]!.ToJsonString(),
            StringComparison.Ordinal
        );
        Assert.True(paragraphFormattingProperties["alignment"]!["deprecated"]!.GetValue<bool>());
        Assert.Contains(
            "#/$defs/liveCharacterFormatting",
            textFormatting["allOf"]!.ToJsonString()
        );
        Assert.Contains("paragraph_alignment", textFormatting["allOf"]!.ToJsonString());
        Assert.Equal(2, textVariant["oneOf"]!.AsArray().Count);
        Assert.Contains(
            "#/$defs/liveRunFormatting",
            item.ToJsonString(),
            StringComparison.Ordinal
        );
        var runFormatting = definitions["liveRunFormatting"]!.AsObject();
        Assert.Contains(
            "#/$defs/liveCharacterFormatting",
            runFormatting["allOf"]!.ToJsonString()
        );
        Assert.False(textFormatting["properties"]!.AsObject().ContainsKey("font_name"));
        Assert.True(formattingProperties.ContainsKey("font_name_far_east"));
        Assert.True(formattingProperties.ContainsKey("underline_color"));
        Assert.True(formattingProperties.ContainsKey("ligatures"));
        Assert.False(formattingProperties.ContainsKey("paragraph_alignment"));
        Assert.Contains("\"type\":\"null\"", item.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("#/definitions/", item.ToJsonString(), StringComparison.Ordinal);
    }
}

public sealed class ExtendedFormattingFakeRange
{
    public ExtendedFormattingFakeRange()
    {
        Font = new ExtendedFormattingFakeFont(this);
    }

    public ExtendedFormattingFakeFont Font { get; }

    public ExtendedFormattingFakeParagraphFormat ParagraphFormat { get; } = new();

    public int HighlightColorIndex { get; set; }
}

public sealed class ExtendedFormattingFakeFont
{
    private readonly ExtendedFormattingFakeRange _range;
    private string _name = "Calibri";

    public ExtendedFormattingFakeFont(ExtendedFormattingFakeRange range)
    {
        _range = range;
    }
    public bool ThrowOnNameSet { get; set; }
    public string Name
    {
        get => _name;
        set
        {
            if (ThrowOnNameSet) throw new InvalidOperationException("Injected COM setter failure");
            _name = value;
        }
    }
    public string NameAscii { get; set; } = "Calibri";
    public string NameBi { get; set; } = "Calibri";
    public string NameFarEast { get; set; } = "Calibri";
    public string NameOther { get; set; } = "Calibri";
    public float Size { get; set; } = 11;
    public float SizeBi { get; set; } = 11;
    public int Color { get; set; }
    public int ColorIndex { get; set; }
    public int ColorIndexBi { get; set; }
    public int DiacriticColor { get; set; }
    public int Bold { get; set; }
    public int Italic { get; set; }
    public int BoldBi { get; set; }
    public int ItalicBi { get; set; }
    public int Underline { get; set; }
    public int UnderlineColor { get; set; }
    public int StrikeThrough { get; set; }

    public int DoubleStrikeThrough { get; set; }
    public int Subscript { get; set; }
    public int Superscript { get; set; }
    public int AllCaps { get; set; }
    public int SmallCaps { get; set; }
    public int Hidden { get; set; }
    public int Shadow { get; set; }
    public int Outline { get; set; }
    public int Emboss { get; set; }
    public int Engrave { get; set; }
    public int Scaling { get; set; }
    public float Spacing { get; set; }
    public int Position { get; set; }
    public float Kerning { get; set; }
    public int DisableCharacterSpaceGrid { get; set; }
    public int EmphasisMark { get; set; }
    public int Ligatures { get; set; }
    public int NumberForm { get; set; }
    public int NumberSpacing { get; set; }
    public int StylisticSet { get; set; }
    public int ContextualAlternates { get; set; }

    public void Reset()
    {
        _name = "Calibri";
        NameAscii = "Calibri";
        NameBi = "Calibri";
        NameFarEast = "Calibri";
        NameOther = "Calibri";
        Size = 11;
        SizeBi = 11;
        Color = 0;
        ColorIndex = -1;
        ColorIndexBi = -1;
        DiacriticColor = unchecked((int)0xFF000000);
        Bold = 0;
        Italic = 0;
        BoldBi = 0;
        ItalicBi = 0;
        Underline = 0;
        UnderlineColor = unchecked((int)0xFF000000);
        StrikeThrough = 0;
        DoubleStrikeThrough = 0;
        Subscript = 0;
        Superscript = 0;
        AllCaps = 0;
        SmallCaps = 0;
        Hidden = 0;
        Shadow = 0;
        Outline = 0;
        Emboss = 0;
        Engrave = 0;
        Scaling = 100;
        Spacing = 0;
        Position = 0;
        Kerning = 0;
        DisableCharacterSpaceGrid = 0;
        EmphasisMark = 0;
        Ligatures = 0;
        NumberForm = 0;
        NumberSpacing = 0;
        StylisticSet = 0;
        ContextualAlternates = 0;
        _range.HighlightColorIndex = 0;
    }
}

public sealed class ExtendedFormattingFakeParagraphFormat
{
    public int Alignment { get; set; }
}

public sealed class MixedFormattingFakeRange
{
    public MixedFormattingFakeFont Font { get; } = new();

    public ExtendedFormattingFakeParagraphFormat ParagraphFormat { get; } = new();
}

public sealed class MixedFormattingFakeFont
{
    public int Bold
    {
        get => 9_999_999;
        set { }
    }
}
