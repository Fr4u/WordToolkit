using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class LiveSmartArtCreationTests
{
    [Fact]
    public async Task InspectsBoundedScalarLayoutsAndInsertsOneVerifiedInlineSmartArt()
    {
        await using var host = new CaptionFakeHost();
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);
        var inspected = await InspectLayoutsAsync(service, documentId, includeDescription: true);
        var first = inspected.GetProperty("layouts")[0];

        Assert.Equal(2, inspected.GetProperty("total_count").GetInt32());
        Assert.Equal(1, inspected.GetProperty("returned_count").GetInt32());
        Assert.True(inspected.GetProperty("truncated").GetBoolean());
        Assert.Equal(1, inspected.GetProperty("next_offset").GetInt32());
        Assert.Equal("Basic Process", first.GetProperty("name").GetString());
        Assert.Equal("Process", first.GetProperty("category").GetString());
        Assert.True(first.GetProperty("description_returned").GetBoolean());
        Assert.Matches(
            "^[0-9a-f]{64}$",
            first.GetProperty("smartart_layout_token").GetString()!
        );
        Assert.DoesNotContain(
            "__ComObject",
            inspected.GetRawText(),
            StringComparison.Ordinal
        );

        var selectionToken = await SelectionTokenAsync(service, documentId);
        using var insertArguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = 0,
                    smartart_layout_token = first
                        .GetProperty("smartart_layout_token")
                        .GetString(),
                    selection_token = selectionToken,
                }
            )
        );
        var inserted = await service.CallAsync(
            "insert_live_word_smartart",
            insertArguments.RootElement,
            CancellationToken.None
        );
        var insertedNode = JsonNode.Parse(
            JsonSerializer.Serialize(inserted, JsonDefaults.Compact)
        )!;
        var insertedJson = insertedNode.AsObject();

        Assert.Equal(1, insertedJson["live_version"]!.GetValue<long>());
        Assert.Equal(1, insertedJson["inline_shape_count_before"]!.GetValue<int>());
        Assert.Equal(2, insertedJson["inline_shape_count_after"]!.GetValue<int>());
        Assert.True(insertedJson["native_verified"]!.GetValue<bool>());
        Assert.True(insertedJson["layout_token_consumed"]!.GetValue<bool>());
        Assert.True(host.Application.ScreenUpdating);
        Assert.Equal(1, host.Application.UndoRecord.StartCount);
        Assert.Equal(1, host.Application.UndoRecord.EndCount);
        Assert.Equal(2, host.Application.ActiveDocument.InlineShapes.Count);

        AssertOutputConforms("inspect_live_word_smartart_layouts", inspected);
        using var insertedDocument = JsonDocument.Parse(insertedNode.ToJsonString());
        AssertOutputConforms("insert_live_word_smartart", insertedDocument.RootElement);
    }

    [Fact]
    public async Task FailedSmartArtReadbackRollsBackAndKeepsTheVersionUnchanged()
    {
        await using var host = new CaptionFakeHost();
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);
        var inspected = await InspectLayoutsAsync(service, documentId, includeDescription: false);
        var token = inspected
            .GetProperty("layouts")[0]
            .GetProperty("smartart_layout_token")
            .GetString()!;
        var selectionToken = await SelectionTokenAsync(service, documentId);
        host.Application.ActiveDocument.InlineShapes.ReturnWrongLayout = true;
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = 0,
                    smartart_layout_token = token,
                    selection_token = selectionToken,
                }
            )
        );

        var exception = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "insert_live_word_smartart",
                    arguments.RootElement,
                    CancellationToken.None
                )
        );

        Assert.Equal("VALIDATION_FAILED", exception.ErrorCode);
        Assert.Equal(1, host.Application.ActiveDocument.InlineShapes.Count);
        Assert.Equal(1, host.Application.ActiveDocument.UndoCount);
        Assert.True(host.Application.ScreenUpdating);
        using var inspectArguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new { live_document_id = documentId })
        );
        var state = await service.CallAsync(
            "inspect_live_word_document",
            inspectArguments.RootElement,
            CancellationToken.None
        );
        using var stateJson = JsonDocument.Parse(
            JsonSerializer.Serialize(state, JsonDefaults.Compact)
        );
        Assert.Equal(0, stateJson.RootElement.GetProperty("live_version").GetInt64());
    }

    [Fact]
    public async Task ConsumedLayoutTokenCannotBeReusedAfterSuccessfulInsertion()
    {
        await using var host = new CaptionFakeHost();
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);
        var inspected = await InspectLayoutsAsync(service, documentId, includeDescription: false);
        var layoutToken = inspected
            .GetProperty("layouts")[0]
            .GetProperty("smartart_layout_token")
            .GetString()!;
        var selectionToken = await SelectionTokenAsync(service, documentId);
        using (var firstArguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = 0,
                    smartart_layout_token = layoutToken,
                    selection_token = selectionToken,
                }
            )
        ))
        {
            await service.CallAsync(
                "insert_live_word_smartart",
                firstArguments.RootElement,
                CancellationToken.None
            );
        }

        var freshSelectionToken = await SelectionTokenAsync(service, documentId);
        using var retryArguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = 1,
                    smartart_layout_token = layoutToken,
                    selection_token = freshSelectionToken,
                }
            )
        );
        var exception = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "insert_live_word_smartart",
                    retryArguments.RootElement,
                    CancellationToken.None
                )
        );

        Assert.Equal("VERSION_CONFLICT", exception.ErrorCode);
        Assert.Equal(2, host.Application.ActiveDocument.InlineShapes.Count);
    }

    [Fact]
    public async Task UnknownWellFormedLayoutTokenIsRejectedBeforeMutation()
    {
        await using var host = new CaptionFakeHost();
        var service = new WordLiveService(host);
        var documentId = await ConnectAsync(service);
        var selectionToken = await SelectionTokenAsync(service, documentId);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    expected_version = 0,
                    smartart_layout_token = new string('0', 64),
                    selection_token = selectionToken,
                }
            )
        );

        var exception = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "insert_live_word_smartart",
                    arguments.RootElement,
                    CancellationToken.None
                )
        );

        Assert.Equal("VERSION_CONFLICT", exception.ErrorCode);
        Assert.Equal(1, host.Application.ActiveDocument.InlineShapes.Count);
        Assert.Equal(0, host.Application.ActiveDocument.UndoCount);
    }

    [Fact]
    public void PublishesReadOnlyCatalogAndGuardedInsertionContracts()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        var inspect = catalog.InspectAction("inspect_live_word_smartart_layouts")["tool"]!.AsObject();
        var insert = catalog.InspectAction("insert_live_word_smartart")["tool"]!.AsObject();

        Assert.Equal("1.0", inspect["operationVersion"]!.GetValue<string>());
        Assert.True(inspect["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.False(inspect["annotations"]!["destructiveHint"]!.GetValue<bool>());
        Assert.Equal("wordtoolkit.inspect_live_word_smartart_layouts/1.0",
            inspect["outputSchema"]!["properties"]!["data"]!["properties"]!["operation_contract"]!["const"]!.GetValue<string>());
        Assert.Equal("1.0", insert["operationVersion"]!.GetValue<string>());
        Assert.False(insert["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.True(insert["annotations"]!["destructiveHint"]!.GetValue<bool>());
        Assert.Equal("wordtoolkit.insert_live_word_smartart/1.0",
            insert["outputSchema"]!["properties"]!["data"]!["properties"]!["operation_contract"]!["const"]!.GetValue<string>());
    }

    [Fact]
    public void InsertionRequiresExactlyOneSelectionOrRangeToken()
    {
        var tool = ToolCatalog.LoadNativeWordTools()
            .InspectAction("insert_live_word_smartart")["tool"]!.AsObject();
        var schema = tool["inputSchema"]!.AsObject();
        var oneOf = schema["oneOf"]!.AsArray();

        Assert.Equal(2, oneOf.Count);
        foreach (var branch in oneOf)
        {
            var required = branch!["required"]!.AsArray()
                .Select(x => x!.GetValue<string>())
                .ToHashSet(StringComparer.Ordinal);
            Assert.Single(required);
            Assert.Contains(required.Single(), new[] { "selection_token", "range_token" });
            var forbidden = branch["not"]!["required"]!.AsArray()
                .Select(x => x!.GetValue<string>())
                .ToArray();
            Assert.Single(forbidden);
            Assert.Contains(forbidden[0], new[] { "selection_token", "range_token" });
            Assert.NotEqual(required.Single(), forbidden[0]);
        }
    }

    [Fact]
    public void OutputContractIsScalarAndNeverAdvertisesComObjects()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        foreach (var name in new[] { "inspect_live_word_smartart_layouts", "insert_live_word_smartart" })
        {
            var tool = catalog.InspectAction(name)["tool"]!.AsObject();
            var output = tool["outputSchema"]!.ToJsonString();
            Assert.DoesNotContain("__ComObject", output, StringComparison.Ordinal);
            Assert.Contains("raw_com_objects_returned", output, StringComparison.Ordinal);
            var dataProperties = tool["outputSchema"]!["properties"]!["data"]!["properties"]!;
            Assert.False(
                dataProperties["raw_com_objects_returned"]!["const"]!.GetValue<bool>()
            );
            Assert.False(dataProperties["raw_xml_returned"]!["const"]!.GetValue<bool>());
        }
    }

    [Fact]
    public void InsertionContractRequiresVersionAndBoundedLayoutToken()
    {
        var schema = ToolCatalog.LoadNativeWordTools()
            .InspectAction("insert_live_word_smartart")["tool"]!["inputSchema"]!.AsObject();
        var required = schema["required"]!.AsArray()
            .Select(x => x!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("expected_version", required);
        Assert.Contains("smartart_layout_token", required);
        Assert.Equal("^[0-9a-f]{64}$",
            schema["properties"]!["smartart_layout_token"]!["pattern"]!.GetValue<string>());
    }

    [Fact]
    public void CatalogContractMakesMissingLayoutsAndMetadataExplicit()
    {
        var data = ToolCatalog.LoadNativeWordTools()
            .InspectAction("inspect_live_word_smartart_layouts")["tool"]!["outputSchema"]!
            ["properties"]!["data"]!.AsObject();
        var layout = data["properties"]!["layouts"]!["items"]!.AsObject();
        var required = layout["required"]!.AsArray()
            .Select(x => x!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("available", required);
        Assert.Contains("metadata_truncated", required);
        Assert.NotNull(layout["properties"]!["layout_id"]);
        Assert.NotNull(layout["properties"]!["smartart_layout_token"]);
        Assert.NotNull(layout["properties"]!["issue_code"]);
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

    private static async Task<JsonElement> InspectLayoutsAsync(
        WordLiveService service,
        string documentId,
        bool includeDescription
    )
    {
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    live_document_id = documentId,
                    offset = 0,
                    limit = 1,
                    include_description = includeDescription,
                }
            )
        );
        var result = await service.CallAsync(
            "inspect_live_word_smartart_layouts",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(result, JsonDefaults.Compact)
        );
        return json.RootElement.Clone();
    }

    private static async Task<string> SelectionTokenAsync(
        WordLiveService service,
        string documentId
    )
    {
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new { live_document_id = documentId })
        );
        var result = await service.CallAsync(
            "get_live_word_selection",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(result, JsonDefaults.Compact)
        );
        return json.RootElement
            .GetProperty("selection")
            .GetProperty("selection_token")
            .GetString()!;
    }

    private static void AssertOutputConforms(string action, JsonElement data)
    {
        var tool = ToolCatalog.LoadNativeWordTools().InspectAction(action)["tool"]!.AsObject();
        var schema = tool["outputSchema"]!.AsObject();
        var envelope = new JsonObject
        {
            ["ok"] = true,
            ["data"] = JsonNode.Parse(data.GetRawText()),
        };
        PublishedOutputSchemaAssertions.AssertConforms(envelope, schema, schema);
    }
}
