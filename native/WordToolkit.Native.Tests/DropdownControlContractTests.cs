using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class DropdownControlContractTests
{
    [Fact]
    public void AcceptsBoundedMonthAndYearDropdownDefinitions()
    {
        using var document = JsonDocument.Parse(
            """
            [
              {
                "range_token":"range_month",
                "title":"Month",
                "tag":"tax_month",
                "items":["January","February","March"],
                "selected_item":"February"
              },
              {
                "range_token":"range_year",
                "title":"Year",
                "tag":"tax_year",
                "items":["2025","2026","2027"],
                "selected_item":"2026",
                "lock_control":true
              }
            ]
            """
        );

        var summary = WordLiveService.PrepareDropdownControlsForTesting(
            document.RootElement
        );

        Assert.Equal(2, summary.ControlCount);
        Assert.Equal(6, summary.ItemCount);
        Assert.Equal(2, summary.SelectedCount);
    }

    [Theory]
    [InlineData("[{\"range_token\":\"r\",\"items\":[\"x\",\"x\"]}]")]
    [InlineData("[{\"range_token\":\"r\",\"items\":[]}]")]
    [InlineData("[{\"range_token\":\"r\",\"items\":[\"x\"],\"selected_item\":\"y\"}]")]
    [InlineData("[{\"items\":[\"x\"]}]")]
    public void RejectsAmbiguousOrUnboundDropdownDefinitions(string json)
    {
        using var document = JsonDocument.Parse(json);

        var error = Assert.Throws<NativeToolException>(() =>
            WordLiveService.PrepareDropdownControlsForTesting(document.RootElement)
        );

        Assert.Contains(error.ErrorCode, new[] { "INVALID_INPUT", "LIMIT_EXCEEDED" });
    }
}
