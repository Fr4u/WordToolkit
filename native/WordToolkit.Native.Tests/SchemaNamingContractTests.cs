using System.Text.Json.Nodes;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Tests;

public sealed class SchemaNamingContractTests
{
    [Fact]
    public void GatewaySchemasUseActionAndNeverActionName()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();

        var inspect = Tool(catalog, "inspect_wordtoolkit_action");
        var execute = Tool(catalog, "execute_wordtoolkit_action");

        Assert.Contains("action", Properties(inspect));
        Assert.DoesNotContain("action_name", Properties(inspect));
        Assert.Contains("action", Properties(execute));
        Assert.Contains("arguments", Properties(execute));
        Assert.DoesNotContain("action_name", Properties(execute));
    }

    [Fact]
    public void RepresentativePackageSchemasKeepInputAndOutputPathRolesDistinct()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();

        AssertPathRoles(catalog, "transform_ooxml_package", expectsWritesField: true);
        AssertPathRoles(catalog, "convert_ooxml_flat_opc", expectsWritesField: false);
        AssertPathRoles(catalog, "render_ooxml_semantic_html", expectsWritesField: false);
    }

    private static void AssertPathRoles(
        ToolCatalog catalog,
        string actionName,
        bool expectsWritesField
    )
    {
        var tool = catalog.InspectAction(actionName)["tool"]!.AsObject();
        var input = tool["inputSchema"]!.AsObject();
        var required = input["required"]!.AsArray().Select(value => value!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var properties = input["properties"]!.AsObject();
        var permissions = tool["permissions"]!.AsObject();
        var reversibility = tool["reversibility"]!.AsObject();

        Assert.Contains("local_path", required);
        Assert.True(properties.ContainsKey("local_path"));
        Assert.Contains("output_path", required);
        Assert.True(properties.ContainsKey("output_path"));
        Assert.Contains("read", permissions["filesystem"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
        if (expectsWritesField)
        {
            Assert.Equal("output_path", reversibility["writes"]!.GetValue<string>());
        }
        else
        {
            Assert.Equal("delete_created_output", reversibility["mechanism"]!.GetValue<string>());
        }
    }

    private static JsonObject Tool(ToolCatalog catalog, string name) =>
        catalog.Tools.Single(tool => tool!["name"]!.GetValue<string>() == name)!.AsObject();

    private static JsonObject Properties(JsonObject tool) => tool["inputSchema"]!["properties"]!.AsObject();
}
