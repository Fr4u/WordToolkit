using System.Text.Json.Nodes;
using WordToolkit.Native.Protocol;
using Xunit;

namespace WordToolkit.Native.Tests;

public sealed class ActionGuidanceContractTests
{
    [Fact]
    public void CatalogLoadsGuidanceForEveryNativeAction()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        var result = catalog.InspectAction("apply_live_word_operations");
        Assert.NotNull(result["guidance"]);
        Assert.Equal("apply_live_word_operations", result["action"]!.GetValue<string>());
        var guidance = result["guidance"]!.AsObject();
        Assert.Contains(guidance["prerequisites"]!.AsArray(), x => x!.GetValue<string>().Contains("expected_version", StringComparison.Ordinal));
        Assert.NotNull(guidance["example"]!["arguments"]!["expected_version"]);
    }

    [Fact]
    public void SearchReturnsStructuredFirstStep()
    {
        var result = ToolCatalog.LoadNativeWordTools().SearchActions("equation", 1);
        var action = result["actions"]!.AsArray().Single();
        Assert.NotNull(action!["first_step"]);
        Assert.Equal(System.Text.Json.JsonValueKind.String, action["first_step"]!.GetValueKind());
        Assert.DoesNotContain("\\\"", action["first_step"]!.GetValue<string>());
    }

    [Fact]
    public void SearchIncludesTopSchemaByDefault()
    {
        var result = ToolCatalog.LoadNativeWordTools().SearchActions("equation", 1);

        Assert.False(result["inspect_call_required_for_top_action"]!.GetValue<bool>());
        Assert.NotNull(result["top_action"]!["tool"]!["inputSchema"]);
    }

    [Fact]
    public void SearchCanOptOutOfTopSchemaForCompactResponses()
    {
        var result = ToolCatalog.LoadNativeWordTools().SearchActions("equation", 1, includeTopSchema: false);

        Assert.Null(result["top_action"]);
        Assert.Null(result["inspect_call_required_for_top_action"]);
    }

    [Fact]
    public void SearchCanInlineOnlyTheTopInspectedSchemaAndGuidance()
    {
        var result = ToolCatalog.LoadNativeWordTools().SearchActions(
            "equation preflight",
            5,
            includeTopSchema: true
        );

        Assert.False(result["inspect_call_required_for_top_action"]!.GetValue<bool>());
        var top = result["top_action"]!.AsObject();
        Assert.False(string.IsNullOrWhiteSpace(top["action"]!.GetValue<string>()));
        Assert.NotNull(top["tool"]!["inputSchema"]);
        Assert.NotNull(top["guidance"]!["example"]);
    }

    [Theory]
    [InlineData("apply_live_word_operations")]
    [InlineData("apply live word operations")]
    public void SearchRanksExactApplyActionFirst(string query)
    {
        var result = ToolCatalog.LoadNativeWordTools().SearchActions(query, 5, includeTopSchema: false);
        Assert.Equal("apply_live_word_operations", result["actions"]![0]!["action"]!.GetValue<string>());
    }

    [Fact]
    public void SearchRanksExactLiveBatchPreflightForBroadPreflightQuery()
    {
        var result = ToolCatalog.LoadNativeWordTools().SearchActions("preflight live operations", 1, includeTopSchema: false);
        Assert.Equal("preflight_live_word_operations", result["actions"]![0]!["action"]!.GetValue<string>());
    }

    [Fact]
    public void SearchRanksEquationPreflightByName()
    {
        var result = ToolCatalog.LoadNativeWordTools().SearchActions("equation preflight", 5, includeTopSchema: false);
        var actions = result["actions"]!.AsArray().Select(x => x!["action"]!.GetValue<string>()).ToArray();
        Assert.Equal("preflight_live_word_equations", actions[0]);
    }
}
