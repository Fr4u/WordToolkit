using System.Text.Json;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class WordMemberCapabilityProjectionTests
{
    [Fact]
    public void Summary_projection_is_flat_compact_and_schema_free()
    {
        var parameter = new WordComParameter(
            "Text",
            "BSTR",
            1,
            ["in"],
            false
        );
        var member = new WordComMember(
            "InsertAfter",
            "method",
            2,
            0,
            4,
            1,
            4,
            0,
            [parameter],
            1,
            0,
            false,
            "VOID",
            0,
            []
        );
        var type = new WordComType(
            "Range",
            "dispatch",
            3,
            "{RANGE}",
            0,
            1,
            0,
            [],
            [member]
        );
        var capability = new WordMemberCapability(
            "wmc1_test",
            "wma1_test",
            "word_range_insert_after",
            type,
            member,
            ["document_content", "selection_range", "result"],
            new WordMemberPolicy(
                "content",
                "write_allowed",
                "document_scoped_content_write",
                true
            )
        );

        var summaryJson = JsonSerializer.Serialize(
            WordLiveService.CapabilitySummaryPayload(capability)
        );
        var fullJson = JsonSerializer.Serialize(WordLiveService.CapabilityPayload(capability));
        using var summary = JsonDocument.Parse(summaryJson);
        var root = summary.RootElement;

        Assert.Equal(
            new[]
            {
                "allowed_roots",
                "capability_id",
                "constant",
                "effect",
                "execution",
                "member_kind",
                "member_name",
                "mutating",
                "optional_parameter_count",
                "parameter_count",
                "reason",
                "return_type",
                "type_name",
                "variadic",
            },
            root.EnumerateObject().Select(item => item.Name).Order().ToArray()
        );
        Assert.Equal("wmc1_test", root.GetProperty("capability_id").GetString());
        Assert.Equal("Range", root.GetProperty("type_name").GetString());
        Assert.Equal("InsertAfter", root.GetProperty("member_name").GetString());
        Assert.Equal("method", root.GetProperty("member_kind").GetString());
        Assert.Equal(1, root.GetProperty("parameter_count").GetInt32());
        Assert.Equal("write_allowed", root.GetProperty("execution").GetString());
        Assert.Equal(3, root.GetProperty("allowed_roots").GetArrayLength());
        Assert.False(root.TryGetProperty("virtual_tool", out _));
        Assert.False(root.TryGetProperty("signature", out _));
        Assert.False(root.TryGetProperty("accessor_group_id", out _));
        Assert.Contains("input_schema", fullJson, StringComparison.Ordinal);
        Assert.True(summaryJson.Length * 2 < fullJson.Length);
    }
}
