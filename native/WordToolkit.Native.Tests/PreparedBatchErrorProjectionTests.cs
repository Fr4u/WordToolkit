using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class PreparedBatchErrorProjectionTests
{
    [Fact]
    public void StyleIdentityUsesThePublicStyleNameInsteadOfAComWrapperTypeName()
    {
        Assert.Equal(
            "Heading 1",
            WordLiveService.ReadStyleIdentity(
                new PublicFakeStyleRange(new PublicFakeWordStyle())
            )
        );
        Assert.Equal(
            "",
            WordLiveService.ReadStyleIdentity(
                new PublicFakeStyleRange("System.__ComObject")
            )
        );
    }

    [Fact]
    public void Projects_zero_based_failed_index_without_returning_operation_content()
    {
        var original = new NativeToolException(
            "EQUATION_INVALID",
            "The native equation verification failed",
            new { package_fingerprint = "abc123", linear_input = "secret-content" }
        );

        var projected = Assert.IsType<NativeToolException>(
            WordLiveService.WithFailedOperationIndex(original, 2)
        );
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(projected.Details));
        var details = json.RootElement;
        Assert.Equal(2, details.GetProperty("failed_operation_index").GetInt32());
        Assert.Equal("abc123", details.GetProperty("package_fingerprint").GetString());
        Assert.DoesNotContain("secret-content", JsonSerializer.Serialize(projected.Details));
        Assert.Equal("EQUATION_INVALID", projected.ErrorCode);
    }

    [Fact]
    public void CleanupProjectionCanRecoverTheOriginalFailedOperationIndex()
    {
        var original = WordLiveService.WithFailedOperationIndex(
            new NativeToolException("EQUATION_INVALID", "invalid equation"),
            12
        );

        Assert.Equal(12, WordLiveService.TryGetFailedOperationIndex(original));
        Assert.Null(
            WordLiveService.TryGetFailedOperationIndex(
                new NativeToolException("EQUATION_INVALID", "invalid equation")
            )
        );
    }

    [Fact]
    public void Preserves_index_for_a_twelve_operation_batch_boundary()
    {
        var projected = Assert.IsType<NativeToolException>(
            WordLiveService.WithFailedOperationIndex(
                new NativeToolException("PUBLICATION_INVALID", "controlled failure"),
                12
            )
        );
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(projected.Details));
        Assert.Equal(12, json.RootElement.GetProperty("failed_operation_index").GetInt32());
    }

    [Fact]
    public void RewrappingAnIndexedFailureNeverInventsADifferentOperationIndex()
    {
        var original = WordLiveService.WithFailedOperationIndex(
            new NativeToolException("EQUATION_INVALID", "invalid equation"),
            12
        );

        var projected = Assert.IsType<NativeToolException>(
            WordLiveService.WithFailedOperationIndex(original, 13)
        );

        Assert.Equal(12, WordLiveService.TryGetFailedOperationIndex(projected));
    }

    [Fact]
    public void IgnoresUnserializableDetailsWhenRecoveringFailedOperationIndex()
    {
        var details = new Dictionary<string, object?>();
        details["self"] = details;
        var error = new NativeToolException("STAGING_CLEANUP_FAILED", "cleanup", details);

        Assert.Null(WordLiveService.TryGetFailedOperationIndex(error));
    }
}

public sealed class PublicFakeStyleRange(object style)
{
    public object Style { get; } = style;
}

public sealed class PublicFakeWordStyle
{
    public string NameLocal => "Heading 1";

    public override string ToString() => "System.__ComObject";
}
