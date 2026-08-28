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
                    operation_id = "wlop_example",
                    operation_status = "succeeded",
                    receipt_replayed = false,
                    outcome_known = true,
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
                    performance = new
                    {
                        complexity = new
                        {
                            operation_count = 49,
                            equation_count = 8,
                            styled_equation_count = 2,
                            text_characters = 820,
                            formatted_run_count = 4,
                            estimated_staging_content_com_calls = 65,
                            batch_boundary_equation_count_reads = 2,
                        },
                    },
                }
            )
        );

        Assert.True(compact["native_style_verified"]!.GetValue<bool>());
        Assert.Equal("wlop_example", compact["operation_id"]!.GetValue<string>());
        Assert.Equal("succeeded", compact["operation_status"]!.GetValue<string>());
        Assert.False(compact["receipt_replayed"]!.GetValue<bool>());
        Assert.True(compact["outcome_known"]!.GetValue<bool>());
        Assert.Equal(1, compact["native_style_verified_count"]!.GetValue<int>());
        Assert.Equal(2, compact["formatting_region_count"]!.GetValue<int>());
        Assert.Equal(49, compact["complexity"]!["operation_count"]!.GetValue<int>());
        Assert.Equal(8, compact["complexity"]!["equation_count"]!.GetValue<int>());
        Assert.DoesNotContain("secret", compact.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain(new string('a', 64), compact.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CompactTargetBoundPreflightOmitsPerOperationHashes()
    {
        var compact = Assert.IsType<JsonObject>(
            ToolResponseCompactor.Compact(
                "preflight_live_word_operations",
                new
                {
                    operation_contract = "wordtoolkit.preflight_live_word_operations/1.1",
                    live_document_id = "live_1",
                    live_version = 7,
                    expected_version = 7,
                    validation_mode = "target_bound_exact_batch_staging",
                    valid = true,
                    published = false,
                    target_document_mutated = false,
                    operation_count = 2,
                    text_operation_count = 1,
                    equation_operation_count = 1,
                    operations = new[]
                    {
                        new { index = 0, content_sha256 = new string('a', 64) },
                        new { index = 1, content_sha256 = new string('b', 64) },
                    },
                    complexity = new { operation_count = 2, equation_count = 1 },
                }
            )
        );

        Assert.True(compact["valid"]!.GetValue<bool>());
        Assert.True(compact["native_verified"]!.GetValue<bool>());
        Assert.False(compact["operation_proofs_returned"]!.GetValue<bool>());
        Assert.Null(compact["operations"]);
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
