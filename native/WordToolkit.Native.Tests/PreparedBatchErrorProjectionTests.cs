using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class PreparedBatchErrorProjectionTests
{
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
}
