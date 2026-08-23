using System.Runtime.InteropServices;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;
using Xunit.Abstractions;

namespace WordToolkit.Native.Tests;

[Collection("RealWordAcceptance")]
public sealed class RealWordSmartArtCreationAcceptanceTests
{
    private readonly ITestOutputHelper _output;

    public RealWordSmartArtCreationAcceptanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task InsertsOneReviewedInlineSmartArtAndReadsBackTheExactLayout()
    {
        if (
            !string.Equals(
                Environment.GetEnvironmentVariable(
                    "WORDTOOLKIT_REAL_WORD_SMARTART_CREATION_TEST"
                ),
                "1",
                StringComparison.Ordinal
            )
        )
        {
            return;
        }

        await using var host = new WordComHost();
        var service = new WordLiveService(host);
        object? documentObject = null;
        await host.InvokeAsync(
            application =>
            {
                _output.WriteLine(
                    "Microsoft Word Version={0}, Build={1}",
                    Convert.ToString(application.Version),
                    Convert.ToString(application.Build)
                );
                dynamic document = application.Documents.Add(Visible: false);
                documentObject = (object)document;
                document.Content.Text = "SMARTART_TARGET\r";
                document.Saved = true;
                document.Activate();
                return true;
            },
            launchIfMissing: true
        );

        try
        {
            var documentId = await ConnectAsync(service);
            using var inspectArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    new
                    {
                        live_document_id = documentId,
                        offset = 0,
                        limit = 1,
                        include_description = true,
                    }
                )
            );
            var inspected = await service.CallAsync(
                "inspect_live_word_smartart_layouts",
                inspectArguments.RootElement,
                CancellationToken.None
            );
            using var inspectedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(inspected, JsonDefaults.Compact)
            );
            var layout = inspectedJson.RootElement.GetProperty("layouts")[0];
            var layoutId = layout.GetProperty("layout_id").GetString()!;
            var layoutToken = layout.GetProperty("smartart_layout_token").GetString()!;
            Assert.NotEmpty(layoutId);
            Assert.Matches("^[0-9a-f]{64}$", layoutToken);

            using var findArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    new
                    {
                        live_document_id = documentId,
                        search_text = "SMARTART_TARGET",
                        match_case = true,
                        max_results = 1,
                    }
                )
            );
            var found = await service.CallAsync(
                "find_live_word_text",
                findArguments.RootElement,
                CancellationToken.None
            );
            using var foundJson = JsonDocument.Parse(
                JsonSerializer.Serialize(found, JsonDefaults.Compact)
            );
            var rangeToken = foundJson.RootElement
                .GetProperty("matches")[0]
                .GetProperty("range_token")
                .GetString()!;

            using var insertArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    new
                    {
                        live_document_id = documentId,
                        expected_version = 0,
                        smartart_layout_token = layoutToken,
                        range_token = rangeToken,
                    }
                )
            );
            var inserted = await service.CallAsync(
                "insert_live_word_smartart",
                insertArguments.RootElement,
                CancellationToken.None
            );
            using var insertedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(inserted, JsonDefaults.Compact)
            );
            Assert.Equal(1, insertedJson.RootElement.GetProperty("live_version").GetInt64());
            Assert.Equal(
                layoutId,
                insertedJson.RootElement.GetProperty("layout_id").GetString()
            );
            Assert.True(insertedJson.RootElement.GetProperty("native_verified").GetBoolean());

            await host.InvokeAsync(
                _ =>
                {
                    dynamic document = documentObject!;
                    Assert.Equal(1, (int)document.InlineShapes.Count);
                    object? inlineShapeObject = null;
                    object? smartArtObject = null;
                    object? layoutObject = null;
                    try
                    {
                        inlineShapeObject = document.InlineShapes.Item(1);
                        dynamic inlineShape = inlineShapeObject;
                        Assert.NotEqual(0, Convert.ToInt32(inlineShape.HasSmartArt));
                        smartArtObject = inlineShape.SmartArt;
                        dynamic smartArt = smartArtObject;
                        layoutObject = smartArt.Layout;
                        dynamic layout = layoutObject;
                        Assert.Equal(layoutId, Convert.ToString(layout.Id));
                    }
                    finally
                    {
                        ReleaseComObject(layoutObject);
                        ReleaseComObject(smartArtObject);
                        ReleaseComObject(inlineShapeObject);
                    }
                    return true;
                }
            );
        }
        finally
        {
            if (documentObject is not null)
            {
                await host.InvokeAsync(
                    _ =>
                    {
                        ((dynamic)documentObject).Close(0);
                        return true;
                    }
                );
            }
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private static async Task<string> ConnectAsync(WordLiveService service)
    {
        using var arguments = JsonDocument.Parse("""{"use_active":true,"activate":true}""");
        var connected = await service.CallAsync(
            "connect_live_word_document",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(connected, JsonDefaults.Compact)
        );
        return json.RootElement.GetProperty("live_document_id").GetString()!;
    }
}
