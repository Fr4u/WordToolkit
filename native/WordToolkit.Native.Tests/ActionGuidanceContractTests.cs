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
}
