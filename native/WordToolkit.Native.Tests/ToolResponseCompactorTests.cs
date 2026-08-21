using System.Text.Json.Nodes;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Tests;

public sealed class ToolResponseCompactorTests
{
    [Fact]
    public void CompactPreflightRetainsOnlyBoundedStyleProof()
    {
        var compact = Assert.IsType<JsonObject>(
            ToolResponseCompactor.Compact(
                "preflight_live_word_equations",
                new
                {
                    valid = true,
                    equation_count = 1,
                    equations = new[]
                    {
                        new
                        {
                            index = 0,
                            valid = true,
                            input_format = "latex",
                            word_linear = "x+y",
                            word_linear_characters = 3,
                            display = true,
                            native_readback_required = true,
                            native_readback_enabled = true,
                            native_style_rewrite_required = true,
                            formatting_region_count = 2,
                        },
                    },
                }
            )
        );

        Assert.Equal(1, compact["native_style_rewrite_required_count"]!.GetValue<int>());
        var equation = compact["equations"]!.AsArray()[0]!.AsObject();
        Assert.True(equation["native_style_rewrite_required"]!.GetValue<bool>());
        Assert.Equal(2, equation["formatting_region_count"]!.GetValue<int>());
        Assert.Null(equation["word_linear"]);
        Assert.True(compact.ToJsonString().Length < 600);
    }

    [Fact]
    public void CompactMutationAggregatesStyleVerificationWithoutFormulaOrOmml()
    {
        var compact = Assert.IsType<JsonObject>(
            ToolResponseCompactor.Compact(
                "apply_live_word_operations",
                new
                {
                    live_document_id = "live_1",
                    live_version = 2,
                    operation_count = 1,
                    text_operation_count = 0,
                    equation_operation_count = 1,
                    operations = new[]
                    {
                        new
                        {
                            type = "equation",
                            equation = new
                            {
                                linear_input = "secret",
                                native_style_verified = true,
                                formatting = new
                                {
                                    region_count = 2,
                                    expected_contract_sha256 = new string('a', 64),
                                },
                            },
                        },
                    },
                }
            )
        );

        Assert.True(compact["native_style_verified"]!.GetValue<bool>());
        Assert.Equal(1, compact["native_style_verified_count"]!.GetValue<int>());
        Assert.Equal(2, compact["formatting_region_count"]!.GetValue<int>());
        Assert.DoesNotContain("secret", compact.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain(new string('a', 64), compact.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CompactReadResponseDropsOnlyZeroSourceDiagnostics()
    {
        var compact = Assert.IsType<JsonObject>(
            ToolResponseCompactor.Compact(
                "inspect_ooxml_dependencies",
                new
                {
                    node_count = 5,
                    source_diagnostics = new
                    {
                        package = 0,
                        references = 2,
                        bibliography = 0,
                    },
                }
            )
        );

        var diagnostics = compact["source_diagnostics"]!.AsObject();
        Assert.Single(diagnostics);
        Assert.Equal(2, diagnostics["references"]!.GetValue<int>());
        Assert.Null(diagnostics["package"]);
        Assert.Null(diagnostics["bibliography"]);
    }
}
