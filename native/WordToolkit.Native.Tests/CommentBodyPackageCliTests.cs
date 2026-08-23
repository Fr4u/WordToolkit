using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class CommentBodyPackageCliTests
{
    [Fact]
    public void CliPlanAndApplyUseThePublicEngineContractWithoutReturningText()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-comment-cli-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(
                FindRepositoryRoot(),
                "tests",
                "upstream",
                "fixtures",
                "mammoth_comments.docx"
            );
            var path = Path.Combine(directory, "comments.docx");
            File.Copy(source, path);
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var semantic = new WordSemanticProjector().Project(before);
            var comment = new WordReviewGraphBuilder().Build(before, semantic)
                .Comments.First(item => !string.IsNullOrEmpty(item.Text));
            var replacement = "CLI rewritten comment body";
            var commands = new[]
            {
                new
                {
                    type = "replace_comment_body_text",
                    comment_id = comment.Id,
                    find_text = comment.Text,
                    replacement_text = replacement,
                    expected_match_count = 1,
                },
            };
            var planRequest = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                commands,
                include_details = true,
            });
            var planOutput = new StringWriter();
            var planError = new StringWriter();

            var planExit = CommentBodyPackageCli.Run(
                ["--mode", "plan", "--request", "-", "--format", "json"],
                new StringReader(planRequest),
                planOutput,
                planError
            );

            Assert.Equal(0, planExit);
            Assert.Equal(string.Empty, planError.ToString());
            Assert.DoesNotContain(comment.Text, planOutput.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(replacement, planOutput.ToString(), StringComparison.Ordinal);
            using var planned = JsonDocument.Parse(planOutput.ToString());
            var root = planned.RootElement;
            Assert.Equal(
                CommentBodyWordPackageContract.PlanContract,
                root.GetProperty("operation_contract").GetString()
            );
            var planId = root.GetProperty("plan_id").GetString()!;
            Assert.Equal(before.Fingerprint, reader.Read(path).Fingerprint);

            var applyRequest = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                expected_plan_id = planId,
                commands,
                keep_backup = false,
            });
            var applyOutput = new StringWriter();
            var applyError = new StringWriter();
            var applyExit = CommentBodyPackageCli.Run(
                ["--mode", "apply", "--request", "-", "--format", "json"],
                new StringReader(applyRequest),
                applyOutput,
                applyError
            );

            Assert.Equal(0, applyExit);
            Assert.Equal(string.Empty, applyError.ToString());
            using var applied = JsonDocument.Parse(applyOutput.ToString());
            Assert.Equal(
                CommentBodyWordPackageContract.ApplyContract,
                applied.RootElement.GetProperty("operation_contract").GetString()
            );
            Assert.True(applied.RootElement.GetProperty("applied").GetBoolean());
            Assert.False(applied.RootElement.GetProperty("raw_text_returned").GetBoolean());
            Assert.False(applied.RootElement.GetProperty("raw_xml_returned").GetBoolean());
            var after = reader.Read(path);
            var afterSemantic = new WordSemanticProjector().Project(after);
            var changed = new WordReviewGraphBuilder().Build(after, afterSemantic)
                .Comments.Single(item => item.Id == comment.Id);
            Assert.Equal(replacement, changed.Text);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CatalogKeepsCommentBodyActionsLazyAndVersioned()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        Assert.True(catalog.IsAction(CommentBodyWordPackageContract.PlanOperationName));
        Assert.True(catalog.IsAction(CommentBodyWordPackageContract.ApplyOperationName));
        Assert.DoesNotContain(catalog.Tools, node =>
            node?["name"]?.GetValue<string>()
                is "plan_ooxml_comment_body_edits" or "apply_ooxml_comment_body_edits"
        );
        Assert.Contains(
            CommentBodyWordPackageContract.PlanContract,
            catalog.InspectAction(CommentBodyWordPackageContract.PlanOperationName)
                .ToJsonString(),
            StringComparison.Ordinal
        );
        Assert.Contains(
            CommentBodyWordPackageContract.ApplyContract,
            catalog.InspectAction(CommentBodyWordPackageContract.ApplyOperationName)
                .ToJsonString(),
            StringComparison.Ordinal
        );
        var plan = catalog.InspectAction(
            CommentBodyWordPackageContract.PlanOperationName
        )["tool"]!.AsObject();
        var apply = catalog.InspectAction(
            CommentBodyWordPackageContract.ApplyOperationName
        )["tool"]!.AsObject();
        var planData = plan["outputSchema"]!["properties"]!["data"]!;
        Assert.Equal(
            "^wcbplan_[A-Za-z0-9_-]+$",
            planData["properties"]!["protection_authorization_id"]!["pattern"]!
                .GetValue<string>()
        );
        Assert.DoesNotContain(
            "protection_authorization_id",
            planData["required"]!.AsArray().Select(item => item!.GetValue<string>())
        );
        Assert.Contains(
            "protection",
            planData["required"]!.AsArray().Select(item => item!.GetValue<string>())
        );
        Assert.Equal(
            "^wcbplan_[A-Za-z0-9_-]+$",
            apply["inputSchema"]!["properties"]!["protected_edit_authorization"]!["pattern"]!
                .GetValue<string>()
        );
        Assert.Contains(
            "explicit_authorizations",
            apply["outputSchema"]!["properties"]!["data"]!["required"]!
                .AsArray()
                .Select(item => item!.GetValue<string>())
        );
    }

    [Fact]
    public async Task McpAdapterPlansThroughTheSameEngineWithoutInvokingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-comment-mcp-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "comments.docx");
            File.Copy(
                Path.Combine(
                    FindRepositoryRoot(),
                    "tests",
                    "upstream",
                    "fixtures",
                    "mammoth_comments.docx"
                ),
                path
            );
            var package = new OpcPackageReader().Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            var comment = new WordReviewGraphBuilder().Build(package, semantic)
                .Comments.First(item => !string.IsNullOrEmpty(item.Text));
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                commands = new[]
                {
                    new
                    {
                        type = "replace_comment_body_text",
                        comment_id = comment.Id,
                        find_text = comment.Text,
                        replacement_text = "MCP body",
                        expected_match_count = 1,
                    },
                },
                include_details = false,
            }));

            var result = await new WordLiveService(new NoInvokeHost()).CallAsync(
                CommentBodyWordPackageContract.PlanOperationName,
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(
                result,
                JsonDefaults.Compact
            ));
            Assert.Equal(
                CommentBodyWordPackageContract.PlanContract,
                json.RootElement.GetProperty("operation_contract").GetString()
            );
            Assert.Equal("dotnet-native", json.RootElement.GetProperty("runtime").GetString());
            Assert.False(json.RootElement.GetProperty("python_used").GetBoolean());
            Assert.False(json.RootElement.GetProperty("raw_text_returned").GetBoolean());
            Assert.False(json.RootElement.GetProperty("mutation_performed").GetBoolean());
            Assert.Equal(package.Fingerprint, new OpcPackageReader().Read(path).Fingerprint);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "pyproject.toml")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed class NoInvokeHost : IWordComHost
    {
        public Task<T> InvokeAsync<T>(
            Func<dynamic, T> operation,
            CancellationToken cancellationToken = default,
            bool launchIfMissing = false
        ) => throw new Xunit.Sdk.XunitException(
            "Saved-package comment edits must not invoke Word COM."
        );

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
