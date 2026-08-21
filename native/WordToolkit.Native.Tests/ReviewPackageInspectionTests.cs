using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class ReviewPackageInspectionTests
{
    [Fact]
    public async Task DefaultsToCompactRedactedParseOnlyReviewSummary()
    {
        var path = Fixture("pandoc_comments.docx");
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new { local_path = path })
        );

        var result = await new WordLiveService(new NoInvokeHost()).CallAsync(
            "inspect_ooxml_review",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        var root = json.RootElement;
        var raw = root.GetRawText();

        Assert.Equal("summary", root.GetProperty("view").GetString());
        Assert.Equal(5, root.GetProperty("comment_count").GetInt32());
        Assert.Equal(5, root.GetProperty("comment_anchor_count").GetInt32());
        Assert.Equal(1, root.GetProperty("reply_count").GetInt32());
        Assert.Equal(4, root.GetProperty("thread_count").GetInt32());
        Assert.Equal(1, root.GetProperty("person_count").GetInt32());
        Assert.False(root.GetProperty("word_opened").GetBoolean());
        Assert.False(root.GetProperty("mutation_performed").GetBoolean());
        Assert.False(root.GetProperty("raw_xml_returned").GetBoolean());
        Assert.False(root.GetProperty("external_content_followed").GetBoolean());
        Assert.False(root.GetProperty("sensitive_values_included").GetBoolean());
        Assert.Equal(
            "parse_only_no_word_no_mutation_no_external_access",
            root.GetProperty("execution_policy").GetString()
        );
        Assert.Equal("dotnet-native", root.GetProperty("runtime").GetString());
        Assert.False(root.GetProperty("python_used").GetBoolean());
        Assert.True(raw.Length < 6_000, $"Review summary is too large: {raw.Length}");
        Assert.DoesNotContain("Jesse Rosenthal", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("I left a comment", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("<w:", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitOptInReturnsBoundedLinkedCommentAndAnchorPreviews()
    {
        var path = Fixture("pandoc_comments.docx");
        var service = new WordLiveService(new NoInvokeHost());
        using var commentArguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new
            {
                local_path = path,
                view = "comments",
                detail = "links",
                include_sensitive = true,
                text_preview_chars = 40,
                include_source = true,
                max_items = 1,
            })
        );

        var commentResult = await service.CallAsync(
            "inspect_ooxml_review",
            commentArguments.RootElement,
            CancellationToken.None
        );
        using var commentJson = JsonDocument.Parse(
            JsonSerializer.Serialize(commentResult)
        );
        var comment = Assert.Single(
            commentJson.RootElement.GetProperty("items").EnumerateArray()
        );
        var commentId = comment.GetProperty("comment_id").GetString();

        Assert.StartsWith("wdc_", commentId, StringComparison.Ordinal);
        Assert.Equal("Jesse Rosenthal", comment.GetProperty("author").GetString());
        Assert.Equal("I left a comment.", comment.GetProperty("text_preview").GetString());
        Assert.Equal(16, comment.GetProperty("text_fingerprint").GetString()!.Length);
        Assert.Equal("/word/comments.xml", comment.GetProperty("part_uri").GetString());
        Assert.Single(comment.GetProperty("anchor_ids").EnumerateArray());

        using var anchorArguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new
            {
                local_path = path,
                view = "anchors",
                comment_id = commentId,
                include_sensitive = true,
                text_preview_chars = 40,
                include_source = true,
            })
        );
        var anchorResult = await service.CallAsync(
            "inspect_ooxml_review",
            anchorArguments.RootElement,
            CancellationToken.None
        );
        using var anchorJson = JsonDocument.Parse(JsonSerializer.Serialize(anchorResult));
        var anchor = Assert.Single(
            anchorJson.RootElement.GetProperty("items").EnumerateArray()
        );

        Assert.Equal(commentId, anchor.GetProperty("comment_id").GetString());
        Assert.Equal("complete", anchor.GetProperty("status").GetString());
        Assert.Contains(
            "some text to have a comment",
            anchor.GetProperty("text_preview").GetString(),
            StringComparison.Ordinal
        );
        Assert.StartsWith(
            "wdn_",
            anchor.GetProperty("start_node_id").GetString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task ExposesPairedMoveRangesAndRevisionKindsWithoutNamesByDefault()
    {
        var path = Fixture("pandoc_track_move.docx");
        var service = new WordLiveService(new NoInvokeHost());
        using var moveArguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new
            {
                local_path = path,
                view = "move_ranges",
                detail = "links",
            })
        );
        var moveResult = await service.CallAsync(
            "inspect_ooxml_review",
            moveArguments.RootElement,
            CancellationToken.None
        );
        using var moveJson = JsonDocument.Parse(JsonSerializer.Serialize(moveResult));
        var root = moveJson.RootElement;
        var ranges = root.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(2, root.GetProperty("move_range_count").GetInt32());
        Assert.Equal(1, root.GetProperty("move_count").GetInt32());
        Assert.Equal(2, ranges.Length);
        Assert.All(ranges, range =>
        {
            Assert.Equal("complete", range.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, range.GetProperty("name").ValueKind);
            Assert.Equal(16, range.GetProperty("name_fingerprint").GetString()!.Length);
            Assert.Single(range.GetProperty("revision_ids").EnumerateArray());
        });

        using var revisionArguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new
            {
                local_path = path,
                view = "revisions",
                revision_kind = "move_from",
            })
        );
        var revisionResult = await service.CallAsync(
            "inspect_ooxml_review",
            revisionArguments.RootElement,
            CancellationToken.None
        );
        using var revisionJson = JsonDocument.Parse(
            JsonSerializer.Serialize(revisionResult)
        );
        var revision = Assert.Single(
            revisionJson.RootElement.GetProperty("items").EnumerateArray()
        );
        Assert.Equal("move_from", revision.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, revision.GetProperty("author").ValueKind);
        Assert.Equal(16, revision.GetProperty("author_fingerprint").GetString()!.Length);
        Assert.Equal(JsonValueKind.Null, revision.GetProperty("text_preview").ValueKind);
    }

    [Fact]
    public async Task RejectsSensitivePreviewWithoutConsentAndMisplacedFilters()
    {
        var path = Fixture("pandoc_comments.docx");
        var service = new WordLiveService(new NoInvokeHost());
        using var previewArguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new
            {
                local_path = path,
                view = "comments",
                text_preview_chars = 20,
            })
        );
        var previewException = await Assert.ThrowsAsync<NativeToolException>(() =>
            service.CallAsync(
                "inspect_ooxml_review",
                previewArguments.RootElement,
                CancellationToken.None
            )
        );
        Assert.Equal("INVALID_INPUT", previewException.ErrorCode);

        using var filterArguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new
            {
                local_path = path,
                view = "comments",
                revision_kind = "deletion",
            })
        );
        var filterException = await Assert.ThrowsAsync<NativeToolException>(() =>
            service.CallAsync(
                "inspect_ooxml_review",
                filterArguments.RootElement,
                CancellationToken.None
            )
        );
        Assert.Equal("INVALID_INPUT", filterException.ErrorCode);
    }

    private static string Fixture(string name) => Path.Combine(
        FindRepositoryRoot(),
        "tests",
        "upstream",
        "fixtures",
        name
    );

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "pyproject.toml")))
                return current.FullName;
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
            "Saved-package review inspection must not invoke the Word COM host."
        );

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
