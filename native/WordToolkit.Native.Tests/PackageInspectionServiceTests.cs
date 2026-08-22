using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class PackageInspectionServiceTests
{
    [Fact]
    public async Task InspectPackageReturnsBoundedGraphWithoutStartingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-package-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "sample.docx");
            CreatePackage(path);
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                include_details = true,
                max_items = 10,
            }));
            var host = new NoInvokeHost();
            var service = new WordLiveService(host);

            var result = await service.CallAsync(
                "inspect_ooxml_package",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(
                JsonSerializer.Serialize(result, JsonDefaults.Compact)
            );
            var root = json.RootElement;

            Assert.Equal(
                InspectWordPackageContract.Contract,
                root.GetProperty("operation_contract").GetString()
            );
            Assert.Equal("dotnet-native", root.GetProperty("runtime").GetString());
            Assert.False(root.GetProperty("python_used").GetBoolean());
            Assert.True(root.GetProperty("performance").TryGetProperty("total_ms", out _));
            Assert.True(root.GetProperty("structurally_valid").GetBoolean());
            Assert.True(root.GetProperty("word_document_detected").GetBoolean());
            Assert.True(root.GetProperty("valid_word_package").GetBoolean());
            Assert.Equal(1, root.GetProperty("part_count").GetInt32());
            Assert.Equal(1, root.GetProperty("relationship_count").GetInt32());
            Assert.Equal(
                "/word/document.xml",
                root.GetProperty("office_document_part").GetString()
            );
            Assert.Equal(
                1,
                root.GetProperty("details").GetProperty("parts").GetArrayLength()
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectSemanticsReturnsStableBoundedOutlineWithoutStartingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-semantic-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "semantic.docx");
            CreatePackage(path);
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                max_nodes = 10,
                text_preview_chars = 6,
                include_source_paths = true,
                include_text_node_locators = true,
                max_text_node_locators = 2,
            }));
            var service = new WordLiveService(new NoInvokeHost());

            var result = await service.CallAsync(
                "inspect_ooxml_semantics",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;

            Assert.True(root.GetProperty("semantic_node_count").GetInt32() >= 8);
            Assert.Equal(1, root.GetProperty("projected_part_count").GetInt32());
            Assert.Equal(
                "/word/document.xml",
                Assert.Single(
                    root.GetProperty("projected_part_uris").EnumerateArray()
                ).GetString()
            );
            Assert.Equal(
                1,
                root.GetProperty("node_counts").GetProperty("paragraph").GetInt32()
            );
            Assert.Equal(
                1,
                root.GetProperty("node_counts").GetProperty("equation").GetInt32()
            );
            var outline = root.GetProperty("outline");
            Assert.Contains(
                outline.EnumerateArray(),
                node => node.GetProperty("kind").GetString() == "paragraph"
                    && node.GetProperty("text_preview").GetString()!.Contains(
                        "Hello",
                        StringComparison.Ordinal
                    )
                    && node.GetProperty("text_preview_truncated").GetBoolean()
            );
            Assert.All(
                outline.EnumerateArray(),
                node =>
                {
                    Assert.StartsWith(
                        "wdn_",
                        node.GetProperty("node_id").GetString(),
                        StringComparison.Ordinal
                    );
                    Assert.True(
                        node.GetProperty("source_element_ordinal").GetInt32() >= 0
                    );
                }
            );
            var paragraph = Assert.Single(
                outline.EnumerateArray(),
                node => node.GetProperty("kind").GetString() == "paragraph"
            );
            var locators = root.GetProperty("text_node_locators");
            Assert.Equal(
                "returned_outline_paragraphs",
                root.GetProperty("text_node_locator_scope").GetString()
            );
            Assert.Equal(3, root.GetProperty("text_node_locator_count").GetInt32());
            Assert.Equal(
                2,
                root.GetProperty("returned_text_node_locator_count").GetInt32()
            );
            Assert.True(root.GetProperty("text_node_locators_truncated").GetBoolean());
            Assert.Equal(2, locators.GetArrayLength());
            Assert.All(
                locators.EnumerateArray(),
                locator =>
                {
                    Assert.StartsWith(
                        "wdn_",
                        locator.GetProperty("node_id").GetString(),
                        StringComparison.Ordinal
                    );
                    Assert.Equal(
                        paragraph.GetProperty("node_id").GetString(),
                        locator.GetProperty("paragraph_node_id").GetString()
                    );
                    Assert.Equal(
                        "content_fingerprint",
                        locator.GetProperty("identity_kind").GetString()
                    );
                }
            );
            Assert.Equal(
                "Hello ",
                locators[0].GetProperty("text_preview").GetString()
            );
            Assert.False(
                locators[0].GetProperty("text_preview_truncated").GetBoolean()
            );
            Assert.Equal("a", locators[1].GetProperty("text_preview").GetString());
            Assert.False(
                locators[1].GetProperty("text_preview_truncated").GetBoolean()
            );

            var nodeId = locators[0].GetProperty("node_id").GetString()!;
            var packageFingerprint = root.GetProperty("package_fingerprint").GetString()!;
            using var planArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = packageFingerprint,
                commands = new[]
                {
                    new
                    {
                        node_id = nodeId,
                        new_text = "Changed ",
                        expected_text = "Hello ",
                    },
                },
                include_details = true,
            }));
            var planObject = await service.CallAsync(
                "plan_ooxml_text_edits",
                planArguments.RootElement,
                CancellationToken.None
            );
            using var planJson = JsonDocument.Parse(JsonSerializer.Serialize(planObject));
            var operation = Assert.Single(
                planJson.RootElement.GetProperty("operations").EnumerateArray()
            );

            Assert.True(planJson.RootElement.GetProperty("has_changes").GetBoolean());
            Assert.Equal(nodeId, operation.GetProperty("node_id").GetString());

            using var staleTextArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = packageFingerprint,
                commands = new[]
                {
                    new
                    {
                        node_id = nodeId,
                        new_text = "Changed ",
                        expected_text = "stale",
                    },
                },
            }));
            var staleText = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "plan_ooxml_text_edits",
                    staleTextArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("VERSION_CONFLICT", staleText.ErrorCode);

            using var staleFingerprintArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    expected_package_fingerprint = new string('0', 64),
                    commands = new[]
                    {
                        new
                        {
                            node_id = nodeId,
                            new_text = "Changed ",
                            expected_text = "Hello ",
                        },
                    },
                })
            );
            var staleFingerprint = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "plan_ooxml_text_edits",
                    staleFingerprintArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("VERSION_CONFLICT", staleFingerprint.ErrorCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectSemanticsOmitsTextNodeLocatorsByDefault()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-semantic-default-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "semantic.docx");
            CreatePackage(path);
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
            }));
            var service = new WordLiveService(new NoInvokeHost());

            var result = await service.CallAsync(
                "inspect_ooxml_semantics",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(
                JsonSerializer.Serialize(result, JsonDefaults.Compact)
            );
            var root = json.RootElement;

            Assert.False(root.TryGetProperty("text_node_locator_scope", out _));
            Assert.False(root.TryGetProperty("text_node_locator_count", out _));
            Assert.False(root.TryGetProperty("returned_text_node_locator_count", out _));
            Assert.False(root.TryGetProperty("text_node_locators", out _));
            Assert.False(root.TryGetProperty("text_node_locators_truncated", out _));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectSemanticsTextNodeLocatorsRespectZeroPreviewBudget()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-semantic-private-locator-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "semantic.docx");
            CreatePackage(path);
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                text_preview_chars = 0,
                include_text_node_locators = true,
                max_text_node_locators = 1,
            }));
            var service = new WordLiveService(new NoInvokeHost());

            var result = await service.CallAsync(
                "inspect_ooxml_semantics",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(
                JsonSerializer.Serialize(result, JsonDefaults.Compact)
            );
            var locator = Assert.Single(
                json.RootElement.GetProperty("text_node_locators").EnumerateArray()
            );

            Assert.False(locator.TryGetProperty("text_preview", out _));
            Assert.False(
                locator.GetProperty("text_preview_truncated").GetBoolean()
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectSemanticsBindsNestedTextToNearestReturnedParagraph()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-semantic-nested-paragraph-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "semantic.docx");
            CreatePackage(
                path,
                additionalBodyXml:
                    """
                    <w:p w14:paraId="10112233">
                      <w:r>
                        <w:t>Outer</w:t>
                        <w:txbxContent>
                          <w:p w14:paraId="20112233"><w:r><w:t>Inner</w:t></w:r></w:p>
                        </w:txbxContent>
                      </w:r>
                    </w:p>
                    """
            );
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                max_nodes = 20,
                text_preview_chars = 20,
                include_text_node_locators = true,
                max_text_node_locators = 20,
            }));

            var result = await new WordLiveService(new NoInvokeHost()).CallAsync(
                "inspect_ooxml_semantics",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;
            var paragraphs = root.GetProperty("outline").EnumerateArray()
                .Where(node => node.GetProperty("kind").GetString() == "paragraph")
                .ToArray();
            var outerParagraph = Assert.Single(paragraphs, node =>
                node.GetProperty("text_preview").GetString()!.StartsWith(
                    "Outer",
                    StringComparison.Ordinal
                )
            );
            var innerParagraph = Assert.Single(paragraphs, node =>
                node.GetProperty("text_preview").GetString() == "Inner"
            );
            var locators = root.GetProperty("text_node_locators").EnumerateArray()
                .ToArray();
            var outerLocator = Assert.Single(locators, locator =>
                locator.GetProperty("text_preview").GetString() == "Outer"
            );
            var innerLocator = Assert.Single(locators, locator =>
                locator.GetProperty("text_preview").GetString() == "Inner"
            );

            Assert.Equal(
                outerParagraph.GetProperty("node_id").GetString(),
                outerLocator.GetProperty("paragraph_node_id").GetString()
            );
            Assert.Equal(
                innerParagraph.GetProperty("node_id").GetString(),
                innerLocator.GetProperty("paragraph_node_id").GetString()
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectSemanticsReturnsTextNodeLocatorsAcrossMcpBoundary()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-semantic-mcp-locator-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "semantic.docx");
            CreatePackage(path);
            var request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/call",
                @params = new
                {
                    name = "inspect_ooxml_semantics",
                    arguments = new
                    {
                        local_path = path,
                        max_nodes = 10,
                        text_preview_chars = 20,
                        include_text_node_locators = true,
                        max_text_node_locators = 1,
                    },
                },
            });
            var output = new StringWriter();
            var server = new McpServer(
                new StringReader(request + Environment.NewLine),
                output,
                ToolCatalog.LoadNativeWordTools(),
                new WordLiveService(new NoInvokeHost())
            );

            await server.RunAsync();

            using var response = JsonDocument.Parse(output.ToString().Trim());
            var result = response.RootElement.GetProperty("result");
            var structured = result.GetProperty("structuredContent");
            var data = structured.GetProperty("data");
            var locator = Assert.Single(
                data.GetProperty("text_node_locators").EnumerateArray()
            );

            Assert.False(result.GetProperty("isError").GetBoolean());
            Assert.True(structured.GetProperty("ok").GetBoolean());
            Assert.Equal("Hello ", locator.GetProperty("text_preview").GetString());
            Assert.StartsWith(
                "wdn_",
                locator.GetProperty("node_id").GetString(),
                StringComparison.Ordinal
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task QuerySemanticsReturnsTextNodeLocatorWithoutStartingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-query-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "query.docx");
            CreatePackage(path);
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                kinds = new[] { "text" },
                text = "hello",
                text_match = "contains",
                case_sensitive = false,
                max_results = 10,
                text_preview_chars = 20,
                include_source = true,
            }));
            var service = new WordLiveService(new NoInvokeHost());

            var result = await service.CallAsync(
                "query_ooxml_semantics",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;
            var match = Assert.Single(root.GetProperty("matches").EnumerateArray());

            Assert.Equal(1, root.GetProperty("matched_node_count").GetInt32());
            Assert.Equal(1, root.GetProperty("returned_node_count").GetInt32());
            Assert.Equal("text", match.GetProperty("kind").GetString());
            Assert.Equal("Hello ", match.GetProperty("text_preview").GetString());
            Assert.StartsWith(
                "wdn_",
                match.GetProperty("node_id").GetString(),
                StringComparison.Ordinal
            );
            Assert.Equal(
                "/word/document.xml",
                match.GetProperty("source_part_uri").GetString()
            );
            Assert.True(match.GetProperty("source_element_ordinal").GetInt32() >= 0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task QuerySemanticsSupportsStrictStructuralRelationsWithoutStartingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-query-relations-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "relations.docx");
            CreatePackage(path);
            var service = new WordLiveService(new NoInvokeHost());
            using var paragraphArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                kinds = new[] { "paragraph" },
                descendant = new { kinds = new[] { "equation" } },
            }));
            using var equationArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                kinds = new[] { "equation" },
                ancestor = new { kinds = new[] { "paragraph" } },
            }));

            var paragraphObject = await service.CallAsync(
                "query_ooxml_semantics",
                paragraphArguments.RootElement,
                CancellationToken.None
            );
            var equationObject = await service.CallAsync(
                "query_ooxml_semantics",
                equationArguments.RootElement,
                CancellationToken.None
            );
            using var paragraphJson = JsonDocument.Parse(
                JsonSerializer.Serialize(paragraphObject)
            );
            using var equationJson = JsonDocument.Parse(JsonSerializer.Serialize(equationObject));

            Assert.Equal(
                "paragraph",
                Assert.Single(
                        paragraphJson.RootElement.GetProperty("matches").EnumerateArray()
                    )
                    .GetProperty("kind")
                    .GetString()
            );
            Assert.Equal(
                "equation",
                Assert.Single(
                        equationJson.RootElement.GetProperty("matches").EnumerateArray()
                    )
                    .GetProperty("kind")
                    .GetString()
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task IndexedStructuralQueryNarrowsCandidatesAndRejectsMalformedRelations()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-indexed-relations-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "indexed-relations.docx");
            CreatePackage(path);
            var service = new WordLiveService(new NoInvokeHost());
            using var createArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                operation = "create",
                local_path = path,
            }));
            var createdObject = await service.CallAsync(
                "manage_ooxml_semantic_index",
                createArguments.RootElement,
                CancellationToken.None
            );
            using var createdJson = JsonDocument.Parse(JsonSerializer.Serialize(createdObject));
            var indexId = createdJson.RootElement.GetProperty("semantic_index_id").GetString();
            var fingerprint = createdJson.RootElement
                .GetProperty("package_fingerprint")
                .GetString();
            using var queryArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                semantic_index_id = indexId,
                expected_package_fingerprint = fingerprint,
                descendant = new { kinds = new[] { "equation" } },
            }));

            var queryObject = await service.CallAsync(
                "query_ooxml_semantics",
                queryArguments.RootElement,
                CancellationToken.None
            );
            using var queryJson = JsonDocument.Parse(JsonSerializer.Serialize(queryObject));
            var query = queryJson.RootElement;
            Assert.True(query.GetProperty("semantic_index_used").GetBoolean());
            Assert.Equal("descendant_relation", query.GetProperty("candidate_seed").GetString());
            Assert.True(
                query.GetProperty("scanned_node_count").GetInt32()
                    < query.GetProperty("total_node_count").GetInt32()
            );
            Assert.Contains(
                query.GetProperty("matches").EnumerateArray(),
                match => match.GetProperty("kind").GetString() == "paragraph"
            );
            Assert.DoesNotContain(
                query.GetProperty("matches").EnumerateArray(),
                match => match.GetProperty("kind").GetString() == "equation"
            );

            using var emptyRelation = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                ancestor = new { },
            }));
            var emptyException = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "query_ooxml_semantics",
                    emptyRelation.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_INPUT", emptyException.ErrorCode);

            using var unknownRelation = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                descendant = new { kind = "equation" },
            }));
            var unknownException = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "query_ooxml_semantics",
                    unknownRelation.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_INPUT", unknownException.ErrorCode);

            using var releaseArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                operation = "release",
                semantic_index_id = indexId,
                expected_package_fingerprint = fingerprint,
            }));
            await service.CallAsync(
                "manage_ooxml_semantic_index",
                releaseArguments.RootElement,
                CancellationToken.None
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SemanticIndexCanBeCreatedQueriedInspectedAndReleasedWithoutStartingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-semantic-index-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "indexed.docx");
            CreatePackage(path);
            var service = new WordLiveService(new NoInvokeHost());
            using var createArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                operation = "create",
                local_path = path,
                ttl_seconds = 300,
            }));

            var createdObject = await service.CallAsync(
                "manage_ooxml_semantic_index",
                createArguments.RootElement,
                CancellationToken.None
            );
            using var createdJson = JsonDocument.Parse(
                JsonSerializer.Serialize(createdObject)
            );
            var created = createdJson.RootElement;
            var indexId = created.GetProperty("semantic_index_id").GetString();
            var fingerprint = created.GetProperty("package_fingerprint").GetString();
            Assert.StartsWith("wsi_", indexId, StringComparison.Ordinal);
            Assert.Equal("process_memory_only", created.GetProperty("persistence").GetString());
            Assert.False(created.GetProperty("raw_text_returned").GetBoolean());
            Assert.False(created.GetProperty("word_opened").GetBoolean());

            using var queryArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                semantic_index_id = indexId,
                expected_package_fingerprint = fingerprint,
                kinds = new[] { "text" },
                text = "hello",
                include_source = true,
            }));
            var queryObject = await service.CallAsync(
                "query_ooxml_semantics",
                queryArguments.RootElement,
                CancellationToken.None
            );
            using var queryJson = JsonDocument.Parse(JsonSerializer.Serialize(queryObject));
            var query = queryJson.RootElement;
            Assert.True(query.GetProperty("semantic_index_used").GetBoolean());
            Assert.Equal(indexId, query.GetProperty("semantic_index_id").GetString());
            Assert.Equal("kind", query.GetProperty("candidate_seed").GetString());
            Assert.True(
                query.GetProperty("scanned_node_count").GetInt32()
                    < query.GetProperty("total_node_count").GetInt32()
            );
            Assert.Single(query.GetProperty("matches").EnumerateArray());

            using var inspectArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                operation = "inspect",
                semantic_index_id = indexId,
                expected_package_fingerprint = fingerprint,
            }));
            var inspectedObject = await service.CallAsync(
                "manage_ooxml_semantic_index",
                inspectArguments.RootElement,
                CancellationToken.None
            );
            using var inspectedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(inspectedObject)
            );
            Assert.Equal(
                created.GetProperty("semantic_index_fingerprint").GetString(),
                inspectedJson.RootElement
                    .GetProperty("semantic_index_fingerprint")
                    .GetString()
            );

            using var releaseArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                operation = "release",
                semantic_index_id = indexId,
                expected_package_fingerprint = fingerprint,
            }));
            var releasedObject = await service.CallAsync(
                "manage_ooxml_semantic_index",
                releaseArguments.RootElement,
                CancellationToken.None
            );
            using var releasedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(releasedObject)
            );
            Assert.True(releasedJson.RootElement.GetProperty("released").GetBoolean());

            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "query_ooxml_semantics",
                    queryArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INDEX_NOT_FOUND", exception.ErrorCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SemanticIndexReusesAnUnchangedPackageAndRejectsStaleFingerprint()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-semantic-index-reuse-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "reuse.docx");
            CreatePackage(path);
            var service = new WordLiveService(new NoInvokeHost());
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                operation = "create",
                local_path = path,
            }));

            var firstObject = await service.CallAsync(
                "manage_ooxml_semantic_index",
                arguments.RootElement,
                CancellationToken.None
            );
            var secondObject = await service.CallAsync(
                "manage_ooxml_semantic_index",
                arguments.RootElement,
                CancellationToken.None
            );
            using var firstJson = JsonDocument.Parse(JsonSerializer.Serialize(firstObject));
            using var secondJson = JsonDocument.Parse(JsonSerializer.Serialize(secondObject));
            Assert.Equal(
                firstJson.RootElement.GetProperty("semantic_index_id").GetString(),
                secondJson.RootElement.GetProperty("semantic_index_id").GetString()
            );
            Assert.False(firstJson.RootElement.GetProperty("cache_hit").GetBoolean());
            Assert.True(secondJson.RootElement.GetProperty("cache_hit").GetBoolean());

            using var staleQuery = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                semantic_index_id = firstJson.RootElement
                    .GetProperty("semantic_index_id")
                    .GetString(),
                expected_package_fingerprint = new string('0', 64),
                kinds = new[] { "text" },
            }));
            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "query_ooxml_semantics",
                    staleQuery.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("VERSION_CONFLICT", exception.ErrorCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task QueryPlanAndApplyCanTargetAHeaderStoryWithoutStartingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-header-story-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "header.docx");
            CreatePackage(path, headerText: "Header token");
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var mainBytes = before.Parts["/word/document.xml"].Entry.Content.ToArray();
            var service = new WordLiveService(new NoInvokeHost());
            using var queryArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                kinds = new[] { "text" },
                text = "Header token",
                source_part_uri = "/word/header1.xml",
                include_source = true,
            }));

            var queryObject = await service.CallAsync(
                "query_ooxml_semantics",
                queryArguments.RootElement,
                CancellationToken.None
            );
            using var queryJson = JsonDocument.Parse(JsonSerializer.Serialize(queryObject));
            var query = queryJson.RootElement;
            var match = Assert.Single(query.GetProperty("matches").EnumerateArray());
            var nodeId = match.GetProperty("node_id").GetString();
            Assert.Equal(
                "/word/header1.xml",
                match.GetProperty("source_part_uri").GetString()
            );
            var commands = new[]
            {
                new
                {
                    node_id = nodeId,
                    new_text = "Changed header",
                    expected_text = "Header token",
                },
            };
            using var planArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                commands,
            }));
            var planObject = await service.CallAsync(
                "plan_ooxml_text_edits",
                planArguments.RootElement,
                CancellationToken.None
            );
            using var planJson = JsonDocument.Parse(JsonSerializer.Serialize(planObject));
            var planId = planJson.RootElement.GetProperty("plan_id").GetString();
            using var applyArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                expected_plan_id = planId,
                commands,
                keep_backup = false,
            }));

            var applyObject = await service.CallAsync(
                "apply_ooxml_text_edits",
                applyArguments.RootElement,
                CancellationToken.None
            );
            using var applyJson = JsonDocument.Parse(JsonSerializer.Serialize(applyObject));
            var after = reader.Read(path);

            Assert.True(applyJson.RootElement.GetProperty("applied").GetBoolean());
            Assert.Equal(
                mainBytes,
                after.Parts["/word/document.xml"].Entry.Content.ToArray()
            );
            Assert.Contains(
                new WordSemanticProjector().Project(after).Nodes,
                node => node.Kind == WordSemanticNodeKind.Text
                    && node.Text == "Changed header"
                    && node.SourcePartUri == "/word/header1.xml"
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectSectionsResolvesEffectiveHeaderBindingsWithoutStartingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-section-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "sections.docx");
            CreatePackage(path, headerText: "Section header");
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                binding_detail = "full",
                include_properties = true,
                include_story_part_uris = true,
            }));

            var result = await new WordLiveService(new NoInvokeHost()).CallAsync(
                "inspect_ooxml_sections",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;
            var section = Assert.Single(root.GetProperty("sections").EnumerateArray());
            var bindings = section.GetProperty("bindings");
            var defaultHeader = bindings.EnumerateArray().Single(binding =>
                binding.GetProperty("slot").GetString() == "header_default"
            );
            var firstHeader = bindings.EnumerateArray().Single(binding =>
                binding.GetProperty("slot").GetString() == "header_first"
            );

            Assert.Equal(1, root.GetProperty("section_count").GetInt32());
            Assert.False(root.GetProperty("even_and_odd_headers").GetBoolean());
            Assert.Equal(1, root.GetProperty("referenced_story_part_count").GetInt32());
            Assert.Equal(0, root.GetProperty("unbound_story_part_count").GetInt32());
            Assert.StartsWith(
                "wdn_",
                section.GetProperty("node_id").GetString(),
                StringComparison.Ordinal
            );
            Assert.Equal("nextPage", section.GetProperty("break_type").GetString());
            Assert.Equal("explicit", defaultHeader.GetProperty("origin").GetString());
            Assert.Equal(
                "/word/header1.xml",
                defaultHeader.GetProperty("effective_part_uri").GetString()
            );
            Assert.False(firstHeader.GetProperty("enabled").GetBoolean());
            Assert.Equal("blank", firstHeader.GetProperty("origin").GetString());
            Assert.Equal(
                "default",
                firstHeader.GetProperty("display_fallback_variant").GetString()
            );
            Assert.Equal(
                "/word/header1.xml",
                firstHeader.GetProperty("effective_part_uri").GetString()
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectStylesPagesTypedMetadataWithoutStartingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-style-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "styles.docx");
            CreatePackage(
                path,
                stylesXml: """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:docDefaults><w:rPrDefault><w:rPr><w:sz w:val="22"/></w:rPr></w:rPrDefault></w:docDefaults>
                  <w:latentStyles w:count="1"><w:lsdException w:name="Normal" w:qFormat="1"/></w:latentStyles>
                  <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
                  <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="Heading 1"/><w:basedOn w:val="Normal"/><w:pPr><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:b/></w:rPr></w:style>
                </w:styles>
                """
            );
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                style_type = "paragraph",
                detail = "inheritance",
                include_document_defaults = true,
                include_latent_styles = true,
            }));

            var result = await new WordLiveService(new NoInvokeHost()).CallAsync(
                "inspect_ooxml_styles",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;
            var styles = root.GetProperty("styles").EnumerateArray().ToArray();
            var heading = styles.Single(style =>
                style.GetProperty("style_id").GetString() == "Heading1"
            );

            Assert.True(root.GetProperty("has_styles_part").GetBoolean());
            Assert.Equal(2, root.GetProperty("style_count").GetInt32());
            Assert.Equal(2, root.GetProperty("matched_style_count").GetInt32());
            Assert.Equal(
                "Normal",
                root.GetProperty("default_style_ids")
                    .GetProperty("paragraph")
                    .GetString()
            );
            Assert.Equal(
                "22",
                root.GetProperty("document_defaults")
                    .GetProperty("run")
                    .GetProperty("values")
                    .GetProperty("size_half_points")
                    .GetString()
            );
            Assert.Equal(
                ["Normal", "Heading1"],
                heading.GetProperty("inheritance_chain_style_ids")
                    .EnumerateArray()
                    .Select(value => value.GetString()!)
                    .ToArray()
            );
            Assert.Equal(
                "0",
                heading.GetProperty("declared_properties")
                    .GetProperty("paragraph")
                    .GetProperty("values")
                    .GetProperty("outline_level")
                    .GetString()
            );
            Assert.Equal(
                1,
                root.GetProperty("latent_styles")
                    .GetProperty("exception_count")
                    .GetInt32()
            );
            Assert.Equal(0, root.GetProperty("issue_count").GetInt32());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveFormattingReturnsFilteredProvenanceWithoutStartingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-formatting-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "formatting.docx");
            CreatePackage(
                path,
                stylesXml: """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:docDefaults><w:rPrDefault><w:rPr><w:b w:val="0"/><w:sz w:val="22"/></w:rPr></w:rPrDefault></w:docDefaults>
                  <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:pPr><w:jc w:val="left"/></w:pPr><w:rPr><w:b/></w:rPr></w:style>
                  <w:style w:type="paragraph" w:styleId="Heading1"><w:basedOn w:val="Normal"/><w:rPr><w:b/><w:sz w:val="32"/></w:rPr></w:style>
                </w:styles>
                """,
                paragraphPropertiesXml: "<w:pPr><w:pStyle w:val=\"Heading1\"/><w:jc w:val=\"right\"/></w:pPr>",
                runPropertiesXml: "<w:rPr><w:b w:val=\"0\"/><w:sz w:val=\"24\"/></w:rPr>"
            );
            var package = new OpcPackageReader().Read(path);
            var runId = new WordSemanticProjector().Project(package).Nodes.Single(node =>
                node.Kind == WordSemanticNodeKind.Run
                && node.SourcePartUri == "/word/document.xml"
            ).Id.Value;
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                node_id = runId,
                property_names = new[] { "alignment", "bold", "size_half_points" },
                include_provenance = true,
                include_source = true,
            }));

            var result = await new WordLiveService(new NoInvokeHost()).CallAsync(
                "resolve_ooxml_formatting",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;
            var boldContributions = root.GetProperty("provenance")
                .GetProperty("run")
                .GetProperty("bold")
                .GetProperty("contributions")
                .EnumerateArray()
                .ToArray();

            Assert.Equal("Heading1", root.GetProperty("paragraph_style_id").GetString());
            Assert.Equal(
                "right",
                root.GetProperty("paragraph_properties")
                    .GetProperty("alignment")
                    .GetString()
            );
            Assert.Equal(
                "false",
                root.GetProperty("run_properties").GetProperty("bold").GetString()
            );
            Assert.Equal(
                "24",
                root.GetProperty("run_properties")
                    .GetProperty("size_half_points")
                    .GetString()
            );
            Assert.Equal(4, boldContributions.Length);
            Assert.Equal(
                "direct_run_formatting",
                boldContributions[^1].GetProperty("layer").GetString()
            );
            Assert.Equal(
                "/word/document.xml",
                boldContributions[^1].GetProperty("source_part_uri").GetString()
            );
            Assert.Contains(
                root.GetProperty("coverage_omissions").EnumerateArray(),
                value => value.GetString()
                    == "application_defaults_for_unspecified_properties"
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectNumberingAndResolveItsFormattingWithoutStartingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-numbering-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "numbering.docx");
            CreatePackage(
                path,
                paragraphPropertiesXml: "<w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"6\"/></w:numPr><w:ind w:left=\"1000\"/></w:pPr>",
                runPropertiesXml: "<w:rPr><w:b w:val=\"0\"/></w:rPr>",
                numberingXml:
                    """
                    <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:abstractNum w:abstractNumId="4"><w:nsid w:val="ABCDEF01"/><w:multiLevelType w:val="multilevel"/><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/><w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr><w:rPr><w:b/></w:rPr></w:lvl></w:abstractNum>
                      <w:num w:numId="6"><w:abstractNumId w:val="4"/><w:lvlOverride w:ilvl="0"><w:startOverride w:val="3"/></w:lvlOverride></w:num>
                    </w:numbering>
                    """
            );
            using var numberingArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "resolved_level",
                    number_id = 6,
                    level_index = 0,
                    detail = "declared",
                    include_source = true,
                })
            );
            var service = new WordLiveService(new NoInvokeHost());

            var numberingResult = await service.CallAsync(
                "inspect_ooxml_numbering",
                numberingArguments.RootElement,
                CancellationToken.None
            );
            using var numberingJson = JsonDocument.Parse(
                JsonSerializer.Serialize(numberingResult)
            );
            var resolved = numberingJson.RootElement.GetProperty("resolved_level");
            Assert.Equal(4, resolved.GetProperty("effective_abstract_number_id").GetInt32());
            Assert.Equal(3, resolved.GetProperty("effective_start").GetInt32());
            Assert.Equal(
                "decimal",
                resolved.GetProperty("level").GetProperty("number_format").GetString()
            );
            Assert.Equal(
                "720",
                resolved.GetProperty("level")
                    .GetProperty("declared_properties")
                    .GetProperty("paragraph")
                    .GetProperty("values")
                    .GetProperty("indent_left_twips")
                    .GetString()
            );

            using var sequenceArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "sequences",
                    number_id = 6,
                    story_kind = "main",
                    detail = "levels",
                    include_source = true,
                })
            );
            var sequenceResult = await service.CallAsync(
                "inspect_ooxml_numbering",
                sequenceArguments.RootElement,
                CancellationToken.None
            );
            using var sequenceJson = JsonDocument.Parse(
                JsonSerializer.Serialize(sequenceResult)
            );
            var sequenceRoot = sequenceJson.RootElement;
            Assert.Equal(
                "wordtoolkit.inspect_ooxml_numbering/1.0",
                sequenceRoot.GetProperty("operation_contract").GetString()
            );
            var sequenceItem = Assert.Single(
                sequenceRoot.GetProperty("items").EnumerateArray().ToArray()
            );
            Assert.Equal("3.", sequenceItem.GetProperty("label").GetString());
            Assert.Equal(3, sequenceItem.GetProperty("counter_value").GetInt64());
            Assert.True(sequenceItem.GetProperty("counter_exact").GetBoolean());
            Assert.True(sequenceItem.GetProperty("label_exact").GetBoolean());
            Assert.Equal("main", sequenceItem.GetProperty("story_kind").GetString());
            Assert.StartsWith(
                "wdli_",
                sequenceItem.GetProperty("item_id").GetString(),
                StringComparison.Ordinal
            );
            Assert.StartsWith(
                "wdls_",
                sequenceItem.GetProperty("sequence_id").GetString(),
                StringComparison.Ordinal
            );
            var sequenceAnalysis = sequenceRoot.GetProperty("sequence_analysis");
            Assert.Equal(
                "microsoft_word_compatibility",
                sequenceAnalysis.GetProperty("execution_profile").GetString()
            );
            Assert.True(
                sequenceAnalysis.GetProperty("counter_coverage_complete").GetBoolean()
            );
            Assert.True(
                sequenceAnalysis.GetProperty("label_coverage_complete").GetBoolean()
            );
            Assert.DoesNotContain(
                "Hello",
                sequenceRoot.GetRawText(),
                StringComparison.Ordinal
            );

            var numberingAction = ToolCatalog.LoadNativeWordTools().InspectAction(
                "inspect_ooxml_numbering"
            )["tool"]!.AsObject();
            Assert.Equal(
                "1.0",
                numberingAction["operationVersion"]!.GetValue<string>()
            );
            Assert.Equal(
                "read_local_word_package",
                numberingAction["permissions"]!["filesystem"]!.GetValue<string>()
            );
            Assert.False(
                numberingAction["reversibility"]!["applicable"]!.GetValue<bool>()
            );
            var outputSchema = numberingAction["outputSchema"]!.AsObject();
            Assert.False(outputSchema["additionalProperties"]!.GetValue<bool>());
            var dataSchema = outputSchema["properties"]!["data"]!.AsObject();
            Assert.False(dataSchema["additionalProperties"]!.GetValue<bool>());
            Assert.Equal(
                dataSchema["required"]!.AsArray()
                    .Select(value => value!.GetValue<string>())
                    .Order(StringComparer.Ordinal),
                sequenceRoot.EnumerateObject()
                    .Select(property => property.Name)
                    .Order(StringComparer.Ordinal)
            );
            var sequenceItemSchema = outputSchema["$defs"]!["sequenceItem"]!.AsObject();
            Assert.False(sequenceItemSchema["additionalProperties"]!.GetValue<bool>());
            Assert.Equal(
                sequenceItemSchema["required"]!.AsArray()
                    .Select(value => value!.GetValue<string>())
                    .Order(StringComparer.Ordinal),
                sequenceItem.EnumerateObject()
                    .Select(property => property.Name)
                    .Order(StringComparer.Ordinal)
            );

            using var misplacedStoryFilter = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "instances",
                    story_kind = "main",
                })
            );
            var misplacedStoryException = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_numbering",
                    misplacedStoryFilter.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_INPUT", misplacedStoryException.ErrorCode);

            using var invalidStoryFilter = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "sequences",
                    story_kind = "not_a_word_story",
                })
            );
            var invalidStoryException = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_numbering",
                    invalidStoryFilter.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_INPUT", invalidStoryException.ErrorCode);

            var package = new OpcPackageReader().Read(path);
            var runId = new WordSemanticProjector().Project(package).Nodes.Single(node =>
                node.Kind == WordSemanticNodeKind.Run
                && node.SourcePartUri == "/word/document.xml"
            ).Id.Value;
            using var formattingArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    node_id = runId,
                    property_names = new[] { "indent_left_twips", "bold" },
                    include_provenance = true,
                })
            );
            var formattingResult = await service.CallAsync(
                "resolve_ooxml_formatting",
                formattingArguments.RootElement,
                CancellationToken.None
            );
            using var formattingJson = JsonDocument.Parse(
                JsonSerializer.Serialize(formattingResult)
            );
            var formatting = formattingJson.RootElement;
            Assert.Equal(
                6,
                formatting.GetProperty("numbering").GetProperty("number_id").GetInt32()
            );
            Assert.Equal(
                "1000",
                formatting.GetProperty("paragraph_properties")
                    .GetProperty("indent_left_twips")
                    .GetString()
            );
            Assert.Equal(
                "false",
                formatting.GetProperty("run_properties").GetProperty("bold").GetString()
            );
            Assert.Contains(
                formatting.GetProperty("provenance")
                    .GetProperty("run")
                    .GetProperty("bold")
                    .GetProperty("contributions")
                    .EnumerateArray(),
                contribution => contribution.GetProperty("layer").GetString()
                    == "numbering_level"
                    && contribution.GetProperty("number_id").GetInt32() == 6
            );
            Assert.DoesNotContain(
                formatting.GetProperty("coverage_omissions").EnumerateArray(),
                value => value.GetString() == "numbering_level_properties"
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task NumberingInspectionRejectsUnknownArgumentsBeforeReadingThePackage()
    {
        using var arguments = JsonDocument.Parse(
            """
            {"local_path":"Z:\\does-not-exist.docx","unknown_field":true}
            """
        );
        var service = new WordLiveService(new NoInvokeHost());

        var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
            service.CallAsync(
                "inspect_ooxml_numbering",
                arguments.RootElement,
                CancellationToken.None
            )
        );

        Assert.Equal("INVALID_INPUT", exception.ErrorCode);
        Assert.Equal(
            "inspect_ooxml_numbering received an unknown argument",
            exception.Message
        );
    }

    [Fact]
    public async Task PlansAndAppliesNumberingTailRepairWithoutStartingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-numbering-repair-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "numbering-repair.docx");
            CreatePackage(
                path,
                paragraphPropertiesXml: "<w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"5\"/></w:numPr></w:pPr>",
                additionalBodyXml:
                    """
                    <w:p w14:paraId="11223344"><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="5"/></w:numPr></w:pPr><w:r><w:t>Second secret item</w:t></w:r></w:p>
                    <w:p w14:paraId="22334455"><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="5"/></w:numPr></w:pPr><w:r><w:t>Third secret item</w:t></w:r></w:p>
                    """,
                numberingXml:
                    """
                    <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:abstractNum w:abstractNumId="1"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl></w:abstractNum>
                      <w:num w:numId="5"><w:abstractNumId w:val="1"/></w:num>
                    </w:numbering>
                    """
            );
            var reader = new OpcPackageReader();
            var beforeBytes = File.ReadAllBytes(path);
            var before = reader.Read(path);
            var target = new WordSemanticProjector().Project(before).Nodes.Single(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
                && node.SourcePartUri == "/word/document.xml"
                && node.SourcePath.EndsWith("/w:p[1]", StringComparison.Ordinal)
            );
            var service = new WordLiveService(new NoInvokeHost());
            using var planArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                target_paragraph_node_id = target.Id.Value,
                expected_number_id = 5,
                expected_level_index = 0,
                start_value = 4,
                include_details = true,
            }));

            var plannedObject = await service.CallAsync(
                "plan_ooxml_numbering_repair",
                planArguments.RootElement,
                CancellationToken.None
            );
            using var plannedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(plannedObject)
            );
            var planned = plannedJson.RootElement;
            var planId = planned.GetProperty("plan_id").GetString()!;
            var predicted = planned.GetProperty("result_package_fingerprint").GetString();

            Assert.Equal(beforeBytes, File.ReadAllBytes(path));
            Assert.StartsWith("wnrplan_", planId, StringComparison.Ordinal);
            Assert.Equal(3, planned.GetProperty("affected_paragraph_count").GetInt32());
            Assert.Equal(6, planned.GetProperty("new_number_id").GetInt32());
            Assert.Equal(4, planned.GetProperty("target_counter_after").GetInt64());
            Assert.True(planned.GetProperty("can_apply").GetBoolean());
            Assert.True(planned.GetProperty("engine_validation")
                .GetProperty("passed").GetBoolean());
            Assert.True(planned.GetProperty("candidate_validation")
                .GetProperty("no_new_errors").GetBoolean());
            Assert.DoesNotContain("Hello", planned.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("secret", planned.GetRawText(), StringComparison.OrdinalIgnoreCase);
            Assert.False(planned.GetProperty("paragraph_text_returned").GetBoolean());

            using var applyArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                expected_plan_id = planId,
                target_paragraph_node_id = target.Id.Value,
                expected_number_id = 5,
                expected_level_index = 0,
                start_value = 4,
                keep_backup = false,
            }));
            var appliedObject = await service.CallAsync(
                "apply_ooxml_numbering_repair",
                applyArguments.RootElement,
                CancellationToken.None
            );
            using var appliedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(appliedObject)
            );
            var applied = appliedJson.RootElement;

            Assert.True(applied.GetProperty("applied").GetBoolean());
            Assert.Equal(predicted, applied.GetProperty("package_fingerprint").GetString());
            Assert.Equal(JsonValueKind.Null, applied.GetProperty("backup_path").ValueKind);
            Assert.Equal(2, applied.GetProperty("changed_entry_names").GetArrayLength());
            Assert.False(applied.GetProperty("paragraph_text_returned").GetBoolean());
            Assert.Empty(Directory.GetFiles(directory, "*.bak"));

            var after = reader.Read(path);
            var semantic = new WordSemanticProjector().Project(after);
            var styles = new WordStyleGraphBuilder().Build(after, semantic);
            var numbering = new WordNumberingGraphBuilder().Build(after, semantic, styles);
            var sequence = new WordListSequenceGraphBuilder().Build(
                after,
                semantic,
                styles,
                numbering
            );
            Assert.Equal(
                new string?[] { "4.", "5.", "6." },
                sequence.Items.Select(item => item.Label).ToArray()
            );

            var catalog = ToolCatalog.LoadNativeWordTools();
            foreach (var actionName in new[]
            {
                "plan_ooxml_numbering_repair",
                "apply_ooxml_numbering_repair",
            })
            {
                var action = catalog.InspectAction(actionName)["tool"]!.AsObject();
                Assert.Equal("1.0", action["operationVersion"]!.GetValue<string>());
                Assert.NotNull(action["outputSchema"]);
                Assert.NotNull(action["permissions"]);
                Assert.NotNull(action["reversibility"]);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task NumberingRepairRejectsUnknownArgumentsBeforeReadingThePackage()
    {
        using var arguments = JsonDocument.Parse(
            """
            {"local_path":"Z:\\does-not-exist.docx","expected_package_fingerprint":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","target_paragraph_node_id":"wdn_abcde","expected_number_id":5,"expected_level_index":0,"start_value":1,"unknown_field":true}
            """
        );
        var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
            new WordLiveService(new NoInvokeHost()).CallAsync(
                "plan_ooxml_numbering_repair",
                arguments.RootElement,
                CancellationToken.None
            )
        );
        Assert.Equal("INVALID_INPUT", exception.ErrorCode);
    }

    [Fact]
    public async Task InspectsThemeAndResolvesThemeFormattingWithoutStartingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-theme-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "theme.docx");
            CreatePackage(
                path,
                stylesXml: """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
                    <w:rPr><w:rFonts w:asciiTheme="minorHAnsi"/><w:color w:val="95B3D7" w:themeColor="accent1" w:themeTint="99"/></w:rPr>
                  </w:style>
                </w:styles>
                """,
                themeXml: ThemeXml()
            );
            var service = new WordLiveService(new NoInvokeHost());
            using var colorArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "colors",
                    detail = "declared",
                    offset = 4,
                    max_items = 1,
                    include_source = true,
                })
            );

            var colorResult = await service.CallAsync(
                "inspect_ooxml_theme",
                colorArguments.RootElement,
                CancellationToken.None
            );
            using var colorJson = JsonDocument.Parse(JsonSerializer.Serialize(colorResult));
            var colorRoot = colorJson.RootElement;
            var accent1 = colorRoot.GetProperty("items").EnumerateArray().Single();

            Assert.True(colorRoot.GetProperty("has_theme_part").GetBoolean());
            Assert.Equal("Office", colorRoot.GetProperty("theme_name").GetString());
            Assert.Equal(12, colorRoot.GetProperty("matched_item_count").GetInt32());
            Assert.Equal(5, colorRoot.GetProperty("next_offset").GetInt32());
            Assert.Equal("accent1", accent1.GetProperty("slot").GetString());
            Assert.Equal("4F81BD", accent1.GetProperty("base_rgb").GetString());
            Assert.True(accent1.GetProperty("deterministically_resolvable").GetBoolean());
            Assert.True(accent1.GetProperty("source_element_ordinal").GetInt32() > 0);

            using var fontArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path, view = "fonts" })
            );
            var fontResult = await service.CallAsync(
                "inspect_ooxml_theme",
                fontArguments.RootElement,
                CancellationToken.None
            );
            using var fontJson = JsonDocument.Parse(JsonSerializer.Serialize(fontResult));
            Assert.Contains(
                fontJson.RootElement.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("collection").GetString() == "minor"
                    && item.GetProperty("role").GetString() == "latin"
                    && item.GetProperty("typeface").GetString() == "Calibri"
            );

            var package = new OpcPackageReader().Read(path);
            var runId = new WordSemanticProjector().Project(package).Nodes.Single(node =>
                node.Kind == WordSemanticNodeKind.Run
                && node.SourcePartUri == "/word/document.xml"
            ).Id.Value;
            using var formattingArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    node_id = runId,
                    property_names = new[]
                    {
                        "font_ascii_theme",
                        "font_ascii_resolved",
                        "color_resolved_rgb",
                    },
                    include_provenance = true,
                    include_source = true,
                })
            );
            var formattingResult = await service.CallAsync(
                "resolve_ooxml_formatting",
                formattingArguments.RootElement,
                CancellationToken.None
            );
            using var formattingJson = JsonDocument.Parse(
                JsonSerializer.Serialize(formattingResult)
            );
            var formatting = formattingJson.RootElement;

            Assert.True(
                formatting.GetProperty("theme").GetProperty("has_theme_part").GetBoolean()
            );
            Assert.Equal(
                "Calibri",
                formatting.GetProperty("run_properties")
                    .GetProperty("font_ascii_resolved")
                    .GetString()
            );
            Assert.Equal(
                "95B3D7",
                formatting.GetProperty("run_properties")
                    .GetProperty("color_resolved_rgb")
                    .GetString()
            );
            var themeContribution = formatting.GetProperty("provenance")
                .GetProperty("run")
                .GetProperty("font_ascii_resolved")
                .GetProperty("contributions")
                .EnumerateArray()
                .Single();
            Assert.Equal("theme", themeContribution.GetProperty("layer").GetString());
            Assert.Equal(
                "minorHAnsi",
                themeContribution.GetProperty("theme_token").GetString()
            );
            Assert.Equal(
                "minor",
                themeContribution.GetProperty("theme_font_collection").GetString()
            );
            Assert.Equal(
                "/word/theme/theme1.xml",
                themeContribution.GetProperty("source_part_uri").GetString()
            );
            Assert.DoesNotContain(
                formatting.GetProperty("coverage_omissions").EnumerateArray(),
                value => value.GetString() == "theme_value_resolution"
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectsSettingsAndFontsWithPrivacyAndResolvesLanguageFontWithoutWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-settings-font-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings-fonts.docx");
            CreatePackage(
                path,
                stylesXml: """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
                    <w:rPr><w:rFonts w:asciiTheme="minorHAnsi"/></w:rPr>
                  </w:style>
                </w:styles>
                """,
                themeXml: ThemeXml(),
                settingsXml: """
                <w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:trackRevisions/>
                  <w:embedTrueTypeFonts/>
                  <w:themeFontLang w:val="ja-JP"/>
                  <w:compat><w:compatSetting w:name="compatibilityMode" w:uri="http://schemas.microsoft.com/office/word" w:val="15"/></w:compat>
                  <w:documentProtection w:edit="comments" w:enforcement="1" w:hash="never-return-this" w:salt="never-return-this-either"/>
                  <w:docVars><w:docVar w:name="CustomerSecret" w:val="secret-value"/></w:docVars>
                </w:settings>
                """,
                fontTableXml: """
                <w:fonts xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:font w:name="ＭＳ 明朝">
                    <w:family w:val="roman"/>
                    <w:embedRegular r:id="rIdEmbeddedFont" w:fontKey="11111111-2222-3333-4444-555555555555"/>
                  </w:font>
                </w:fonts>
                """
            );
            var service = new WordLiveService(new NoInvokeHost());
            using var redactedArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "variables",
                })
            );

            var settingsObject = await service.CallAsync(
                "inspect_ooxml_settings",
                redactedArguments.RootElement,
                CancellationToken.None
            );
            using var settingsJson = JsonDocument.Parse(
                JsonSerializer.Serialize(settingsObject)
            );
            var settings = settingsJson.RootElement;
            Assert.True(settings.GetProperty("track_revisions").GetBoolean());
            Assert.Equal(15, settings.GetProperty("compatibility_mode").GetInt32());
            Assert.False(
                settings.GetProperty("document_protection")
                    .GetProperty("security_boundary")
                    .GetBoolean()
            );
            var redactedVariable = settings.GetProperty("items")
                .EnumerateArray()
                .Single();
            Assert.Equal(JsonValueKind.Null, redactedVariable.GetProperty("name").ValueKind);
            Assert.Equal(JsonValueKind.Null, redactedVariable.GetProperty("value").ValueKind);
            Assert.True(redactedVariable.GetProperty("value_redacted").GetBoolean());
            Assert.DoesNotContain("never-return-this", settings.GetRawText());
            Assert.DoesNotContain("secret-value", settings.GetRawText());

            using var sensitiveArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "variables",
                    include_sensitive = true,
                })
            );
            var sensitiveObject = await service.CallAsync(
                "inspect_ooxml_settings",
                sensitiveArguments.RootElement,
                CancellationToken.None
            );
            using var sensitiveJson = JsonDocument.Parse(
                JsonSerializer.Serialize(sensitiveObject)
            );
            Assert.Equal(
                "secret-value",
                sensitiveJson.RootElement.GetProperty("items")
                    .EnumerateArray()
                    .Single()
                    .GetProperty("value")
                    .GetString()
            );
            Assert.DoesNotContain("never-return-this", sensitiveJson.RootElement.GetRawText());

            using var fontArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "embedded_faces",
                    include_source = true,
                })
            );
            var fontsObject = await service.CallAsync(
                "inspect_ooxml_fonts",
                fontArguments.RootElement,
                CancellationToken.None
            );
            using var fontsJson = JsonDocument.Parse(JsonSerializer.Serialize(fontsObject));
            var face = fontsJson.RootElement.GetProperty("items")
                .EnumerateArray()
                .Single();
            Assert.Equal("ＭＳ 明朝", face.GetProperty("font_name").GetString());
            Assert.True(face.GetProperty("word_readable").GetBoolean());
            Assert.Equal(JsonValueKind.Null, face.GetProperty("sha256").ValueKind);
            Assert.Equal(
                "/word/fonts/font1.odttf",
                face.GetProperty("part_uri").GetString()
            );

            var package = new OpcPackageReader().Read(path);
            var runId = new WordSemanticProjector().Project(package).Nodes.Single(node =>
                node.Kind == WordSemanticNodeKind.Run
                && node.SourcePartUri == "/word/document.xml"
            ).Id.Value;
            using var formattingArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    node_id = runId,
                    property_names = new[]
                    {
                        "font_ascii_resolved",
                        "font_ascii_document_font",
                    },
                    include_provenance = true,
                })
            );
            var formattingObject = await service.CallAsync(
                "resolve_ooxml_formatting",
                formattingArguments.RootElement,
                CancellationToken.None
            );
            using var formattingJson = JsonDocument.Parse(
                JsonSerializer.Serialize(formattingObject)
            );
            var formatting = formattingJson.RootElement;
            Assert.Equal(
                "ＭＳ 明朝",
                formatting.GetProperty("run_properties")
                    .GetProperty("font_ascii_resolved")
                    .GetString()
            );
            Assert.Equal(
                "declared_embedded",
                formatting.GetProperty("run_properties")
                    .GetProperty("font_ascii_document_font")
                    .GetString()
            );
            var contribution = formatting.GetProperty("provenance")
                .GetProperty("run")
                .GetProperty("font_ascii_resolved")
                .GetProperty("contributions")
                .EnumerateArray()
                .Single();
            Assert.Equal("ja-JP", contribution.GetProperty("theme_language_tag").GetString());
            Assert.Equal("Jpan", contribution.GetProperty("theme_script").GetString());
            Assert.Equal(
                "supplemental_language_typeface",
                contribution.GetProperty("theme_font_resolution").GetString()
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PlansAndAtomicallyAppliesReviewedTextEditWithoutStartingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-text-transaction-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "edit.docx");
            CreatePackage(path);
            var beforeFile = File.ReadAllBytes(path);
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var semantic = new WordSemanticProjector().Project(before);
            var text = semantic.Nodes.Single(node =>
                node.Kind == WordSemanticNodeKind.Text && node.Text == "Hello "
            );
            var commands = new[]
            {
                new
                {
                    node_id = text.Id.Value,
                    new_text = " Changed & safe ",
                    expected_text = "Hello ",
                },
            };
            using var planArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                commands,
                include_details = true,
            }));
            var service = new WordLiveService(new NoInvokeHost());

            var plannedObject = await service.CallAsync(
                "plan_ooxml_text_edits",
                planArguments.RootElement,
                CancellationToken.None
            );
            using var plannedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(plannedObject)
            );
            var planned = plannedJson.RootElement;
            var planId = planned.GetProperty("plan_id").GetString()!;
            var predicted = planned
                .GetProperty("result_package_fingerprint")
                .GetString();

            Assert.Equal(beforeFile, File.ReadAllBytes(path));
            Assert.False(planned.GetProperty("apply_blocked").GetBoolean());
            Assert.True(planned.GetProperty("has_changes").GetBoolean());
            Assert.Equal(1, planned.GetProperty("operation_count").GetInt32());
            Assert.Equal(1, planned.GetProperty("changed_part_count").GetInt32());
            Assert.Single(planned.GetProperty("operations").EnumerateArray());

            using var applyArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                expected_plan_id = planId,
                commands,
                keep_backup = true,
            }));
            var appliedObject = await service.CallAsync(
                "apply_ooxml_text_edits",
                applyArguments.RootElement,
                CancellationToken.None
            );
            using var appliedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(appliedObject)
            );
            var applied = appliedJson.RootElement;
            var backupPath = applied.GetProperty("backup_path").GetString();

            Assert.True(applied.GetProperty("applied").GetBoolean());
            Assert.False(applied.GetProperty("no_op").GetBoolean());
            Assert.Equal(predicted, applied.GetProperty("package_fingerprint").GetString());
            Assert.Equal(
                predicted,
                applied.GetProperty("predicted_package_fingerprint").GetString()
            );
            Assert.NotNull(backupPath);
            Assert.True(File.Exists(backupPath));
            Assert.Equal(before.Fingerprint, reader.Read(backupPath!).Fingerprint);
            var after = reader.Read(path);
            Assert.Equal(predicted, after.Fingerprint);
            Assert.Equal(
                " Changed & safe ",
                new WordSemanticProjector().Project(after).Nodes.Single(node =>
                    node.Kind == WordSemanticNodeKind.Text
                    && node.Text == " Changed & safe "
                ).Text
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PlansAndAtomicallyAppliesReviewedSemanticStyleWithoutStartingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-semantic-edit-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "style.docx");
            CreatePackage(
                path,
                stylesXml: SemanticEditStylesXml(),
                paragraphPropertiesXml: "<w:pPr><w:pStyle w:val=\"OldPara\"/></w:pPr>"
            );
            var beforeBytes = File.ReadAllBytes(path);
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var paragraph = new WordSemanticProjector().Project(before).Nodes.Single(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
                && node.Properties.GetValueOrDefault("style_id") == "OldPara"
            );
            var commands = new[]
            {
                new
                {
                    type = "set_style",
                    node_id = paragraph.Id.Value,
                    style_id = "Definition",
                    expected_style_id = "OldPara",
                },
            };
            var service = new WordLiveService(new NoInvokeHost());
            using var planArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                commands,
                include_details = true,
            }));

            var plannedObject = await service.CallAsync(
                "plan_ooxml_semantic_edits",
                planArguments.RootElement,
                CancellationToken.None
            );
            using var plannedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(plannedObject)
            );
            var planned = plannedJson.RootElement;
            var planId = planned.GetProperty("plan_id").GetString()!;
            var predicted = planned
                .GetProperty("result_package_fingerprint")
                .GetString();

            Assert.Equal(beforeBytes, File.ReadAllBytes(path));
            Assert.StartsWith("wseplan_", planId, StringComparison.Ordinal);
            Assert.False(planned.GetProperty("apply_blocked").GetBoolean());
            Assert.True(
                planned.GetProperty("can_apply").GetBoolean(),
                planned.GetRawText()
            );
            Assert.True(planned.GetProperty("has_changes").GetBoolean());
            Assert.Equal(1, planned.GetProperty("operation_count").GetInt32());
            Assert.Equal(1, planned.GetProperty("changed_part_count").GetInt32());
            Assert.True(
                planned
                    .GetProperty("candidate_validation")
                    .GetProperty("performed")
                    .GetBoolean()
            );
            Assert.True(
                planned
                    .GetProperty("candidate_validation")
                    .GetProperty("no_new_errors")
                    .GetBoolean()
            );
            Assert.Equal(
                "set_style",
                Assert.Single(planned.GetProperty("operations").EnumerateArray())
                    .GetProperty("kind")
                    .GetString()
            );
            Assert.False(planned.GetProperty("raw_xml_returned").GetBoolean());
            Assert.False(planned.GetProperty("word_opened").GetBoolean());

            using var applyArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                expected_plan_id = planId,
                commands,
                keep_backup = false,
            }));
            var appliedObject = await service.CallAsync(
                "apply_ooxml_semantic_edits",
                applyArguments.RootElement,
                CancellationToken.None
            );
            using var appliedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(appliedObject)
            );
            var applied = appliedJson.RootElement;

            Assert.True(applied.GetProperty("applied").GetBoolean());
            Assert.False(applied.GetProperty("no_op").GetBoolean());
            Assert.Equal(predicted, applied.GetProperty("package_fingerprint").GetString());
            Assert.Equal(
                predicted,
                applied.GetProperty("predicted_package_fingerprint").GetString()
            );
            Assert.Equal(JsonValueKind.Null, applied.GetProperty("backup_path").ValueKind);
            Assert.Equal(
                "word/document.xml",
                Assert.Single(
                    applied.GetProperty("changed_entry_names").EnumerateArray()
                ).GetString()
            );
            Assert.False(applied.GetProperty("raw_xml_returned").GetBoolean());
            Assert.False(applied.GetProperty("word_opened").GetBoolean());
            var after = reader.Read(path);
            var changedParagraph = new WordSemanticProjector().Project(after).Nodes.Single(
                node => node.Kind == WordSemanticNodeKind.Paragraph
            );
            Assert.Equal("Definition", changedParagraph.Properties["style_id"]);
            Assert.Equal(
                before.Parts["/word/styles.xml"].Entry.Sha256,
                after.Parts["/word/styles.xml"].Entry.Sha256
            );
            Assert.Empty(Directory.GetFiles(directory, "*.bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SemanticStyleEditsRejectUnsafeCommandsAndPlanDriftWithoutWriting()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-semantic-edit-rejection-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "reject.docx");
            CreatePackage(
                path,
                stylesXml: SemanticEditStylesXml(),
                paragraphPropertiesXml: "<w:pPr><w:pStyle w:val=\"OldPara\"/></w:pPr>"
            );
            var beforeBytes = File.ReadAllBytes(path);
            var package = new OpcPackageReader().Read(path);
            var paragraph = new WordSemanticProjector().Project(package).Nodes.Single(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
            );
            var service = new WordLiveService(new NoInvokeHost());
            using var unsafeArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                commands = new[]
                {
                    new
                    {
                        type = "set_style",
                        node_id = paragraph.Id.Value,
                        style_id = "Emphasis",
                    },
                },
            }));
            var unsafeException = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "plan_ooxml_semantic_edits",
                    unsafeArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("UNSAFE_EDIT", unsafeException.ErrorCode);

            using var conflictingArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                commands = new[]
                {
                    new
                    {
                        type = "set_style",
                        node_id = paragraph.Id.Value,
                        style_id = "Definition",
                        expected_style_id = "OldPara",
                        require_no_explicit_style = true,
                    },
                },
            }));
            var conflictingException = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "plan_ooxml_semantic_edits",
                    conflictingArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_INPUT", conflictingException.ErrorCode);

            var commands = new[]
            {
                new
                {
                    type = "set_style",
                    node_id = paragraph.Id.Value,
                    style_id = "Definition",
                    expected_style_id = "OldPara",
                },
            };
            using var driftArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                expected_plan_id = "wseplan_wrong",
                commands,
            }));
            var driftException = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "apply_ooxml_semantic_edits",
                    driftArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("PLAN_MISMATCH", driftException.ErrorCode);
            Assert.Equal(beforeBytes, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(directory, "*.bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SemanticStyleDefinitionsCloneCreateAndAssignAtomicallyWithoutWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-style-definition-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "definitions.docx");
            CreatePackage(
                path,
                stylesXml: SemanticEditStylesXml(),
                paragraphPropertiesXml: "<w:pPr><w:pStyle w:val=\"OldPara\"/></w:pPr>"
            );
            var beforeBytes = File.ReadAllBytes(path);
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var beforeHashes = before.Entries.ToDictionary(
                entry => entry.Name,
                entry => entry.Sha256,
                StringComparer.Ordinal
            );

            static object[] Commands(bool reordered) => reordered
                ?
                [
                    new Dictionary<string, object?>
                    {
                        ["name"] = "Copied paragraph",
                        ["style_id"] = "CopiedPara",
                        ["source_style_id"] = "OldPara",
                        ["type"] = "clone_style",
                    },
                    new Dictionary<string, object?>
                    {
                        ["ui_priority"] = 21,
                        ["quick_format"] = true,
                        ["based_on_style_id"] = "CopiedPara",
                        ["style_type"] = "paragraph",
                        ["name"] = "Fresh definition",
                        ["style_id"] = "FreshDefinition",
                        ["type"] = "create_style",
                    },
                    new Dictionary<string, object?>
                    {
                        ["max_matches"] = 1,
                        ["expected_style_id"] = "OldPara",
                        ["style_id"] = "FreshDefinition",
                        ["selector"] = new Dictionary<string, object?>
                        {
                            ["property_equals"] = new Dictionary<string, string>
                            {
                                ["style_id"] = "OldPara",
                            },
                            ["kind"] = "paragraph",
                        },
                        ["type"] = "set_style_where",
                    },
                ]
                :
                [
                    new Dictionary<string, object?>
                    {
                        ["type"] = "clone_style",
                        ["source_style_id"] = "OldPara",
                        ["style_id"] = "CopiedPara",
                        ["name"] = "Copied paragraph",
                    },
                    new Dictionary<string, object?>
                    {
                        ["type"] = "create_style",
                        ["style_id"] = "FreshDefinition",
                        ["name"] = "Fresh definition",
                        ["style_type"] = "paragraph",
                        ["based_on_style_id"] = "CopiedPara",
                        ["quick_format"] = true,
                        ["ui_priority"] = 21,
                    },
                    new Dictionary<string, object?>
                    {
                        ["type"] = "set_style_where",
                        ["selector"] = new Dictionary<string, object?>
                        {
                            ["kind"] = "paragraph",
                            ["property_equals"] = new Dictionary<string, string>
                            {
                                ["style_id"] = "OldPara",
                            },
                        },
                        ["style_id"] = "FreshDefinition",
                        ["expected_style_id"] = "OldPara",
                        ["max_matches"] = 1,
                    },
                ];

            var service = new WordLiveService(new NoInvokeHost());
            using var planArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                commands = Commands(false),
                include_details = true,
            }));
            using var reorderedArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                commands = Commands(true),
            }));

            var plannedObject = await service.CallAsync(
                "plan_ooxml_semantic_edits",
                planArguments.RootElement,
                CancellationToken.None
            );
            var reorderedObject = await service.CallAsync(
                "plan_ooxml_semantic_edits",
                reorderedArguments.RootElement,
                CancellationToken.None
            );
            using var plannedJson = JsonDocument.Parse(JsonSerializer.Serialize(plannedObject));
            using var reorderedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(reorderedObject)
            );
            var planned = plannedJson.RootElement;
            var planId = planned.GetProperty("plan_id").GetString()!;

            Assert.Equal(beforeBytes, File.ReadAllBytes(path));
            Assert.Equal(planId, reorderedJson.RootElement.GetProperty("plan_id").GetString());
            Assert.Equal(3, planned.GetProperty("submitted_command_count").GetInt32());
            Assert.Equal(2, planned.GetProperty("style_definition_count").GetInt32());
            Assert.Equal(1, planned.GetProperty("style_assignment_count").GetInt32());
            Assert.Equal(1, planned.GetProperty("selector_command_count").GetInt32());
            Assert.Equal(1, planned.GetProperty("selector_match_count").GetInt32());
            Assert.Equal(3, planned.GetProperty("operation_count").GetInt32());
            Assert.Equal(2, planned.GetProperty("changed_part_count").GetInt32());
            Assert.True(
                planned.GetProperty("can_apply").GetBoolean(),
                planned.GetRawText()
            );
            Assert.True(
                planned.GetProperty("candidate_validation")
                    .GetProperty("no_new_errors")
                    .GetBoolean()
            );
            Assert.Equal(
                ["clone_style", "create_style"],
                planned.GetProperty("style_definition_operations")
                    .EnumerateArray()
                    .Select(item => item.GetProperty("kind").GetString()!)
                    .ToArray()
            );
            Assert.Equal(
                "set_style",
                Assert.Single(planned.GetProperty("operations").EnumerateArray())
                    .GetProperty("kind")
                    .GetString()
            );
            Assert.False(planned.GetProperty("raw_xml_returned").GetBoolean());
            Assert.False(planned.GetProperty("word_opened").GetBoolean());

            using var applyArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                expected_plan_id = planId,
                commands = Commands(true),
                keep_backup = true,
            }));
            var appliedObject = await service.CallAsync(
                "apply_ooxml_semantic_edits",
                applyArguments.RootElement,
                CancellationToken.None
            );
            using var appliedJson = JsonDocument.Parse(JsonSerializer.Serialize(appliedObject));
            var applied = appliedJson.RootElement;

            Assert.True(applied.GetProperty("applied").GetBoolean());
            Assert.Equal(
                planned.GetProperty("result_package_fingerprint").GetString(),
                applied.GetProperty("package_fingerprint").GetString()
            );
            Assert.NotEqual(JsonValueKind.Null, applied.GetProperty("backup_path").ValueKind);
            Assert.Equal(
                ["word/document.xml", "word/styles.xml"],
                applied.GetProperty("changed_entry_names")
                    .EnumerateArray()
                    .Select(item => item.GetString()!)
                    .Order(StringComparer.Ordinal)
                    .ToArray()
            );
            var after = reader.Read(path);
            var afterSemantic = new WordSemanticProjector().Project(after);
            var styles = new WordStyleGraphBuilder().Build(after, afterSemantic);
            Assert.True(styles.TryGetStyle("CopiedPara", out var copied));
            Assert.Equal("Copied paragraph", copied!.Name);
            Assert.True(styles.TryGetStyle("FreshDefinition", out var created));
            Assert.Equal("CopiedPara", created!.BasedOnStyleId);
            Assert.True(created.QuickFormat);
            Assert.Equal(21, created.UiPriority);
            Assert.True(created.InheritanceResolvable);
            Assert.Equal(
                "FreshDefinition",
                afterSemantic.Nodes.Single(node =>
                    node.Kind == WordSemanticNodeKind.Paragraph
                ).Properties["style_id"]
            );
            Assert.All(after.Entries.Where(entry =>
                entry.Name is not "word/document.xml" and not "word/styles.xml"
            ), entry => Assert.Equal(beforeHashes[entry.Name], entry.Sha256));
            Assert.True(File.Exists(applied.GetProperty("backup_path").GetString()));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SemanticStyleConsolidationPlansAndAppliesExactValidatedRepairWithoutWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-style-consolidation-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "consolidate.docx");
            CreatePackage(
                path,
                stylesXml: SemanticConsolidationStylesXml(),
                paragraphPropertiesXml: "<w:pPr><w:pStyle w:val=\"Source\"/></w:pPr>"
            );
            var beforeBytes = File.ReadAllBytes(path);
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var beforeHashes = before.Entries.ToDictionary(
                entry => entry.Name,
                entry => entry.Sha256,
                StringComparer.Ordinal
            );

            static object[] Commands(bool reordered) => reordered
                ?
                [
                    new Dictionary<string, object?>
                    {
                        ["target_style_id"] = "Target",
                        ["source_style_id"] = "Source",
                        ["type"] = "consolidate_style",
                    },
                ]
                :
                [
                    new Dictionary<string, object?>
                    {
                        ["type"] = "consolidate_style",
                        ["source_style_id"] = "Source",
                        ["target_style_id"] = "Target",
                    },
                ];

            var service = new WordLiveService(new NoInvokeHost());
            using var planArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                commands = Commands(false),
                include_details = true,
            }));
            using var reorderedArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                commands = Commands(true),
            }));

            var plannedObject = await service.CallAsync(
                "plan_ooxml_semantic_edits",
                planArguments.RootElement,
                CancellationToken.None
            );
            var reorderedObject = await service.CallAsync(
                "plan_ooxml_semantic_edits",
                reorderedArguments.RootElement,
                CancellationToken.None
            );
            using var plannedJson = JsonDocument.Parse(JsonSerializer.Serialize(plannedObject));
            using var reorderedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(reorderedObject)
            );
            var planned = plannedJson.RootElement;
            var planId = planned.GetProperty("plan_id").GetString()!;

            Assert.Equal(beforeBytes, File.ReadAllBytes(path));
            Assert.Equal(planId, reorderedJson.RootElement.GetProperty("plan_id").GetString());
            Assert.Equal(1, planned.GetProperty("submitted_command_count").GetInt32());
            Assert.Equal(1, planned.GetProperty("style_definition_count").GetInt32());
            Assert.Equal(1, planned.GetProperty("style_consolidation_count").GetInt32());
            Assert.Equal(2, planned.GetProperty("style_reference_update_count").GetInt32());
            Assert.Equal(0, planned.GetProperty("style_assignment_count").GetInt32());
            Assert.Equal(1, planned.GetProperty("operation_count").GetInt32());
            Assert.Equal(2, planned.GetProperty("changed_part_count").GetInt32());
            Assert.True(planned.GetProperty("can_apply").GetBoolean(), planned.GetRawText());
            Assert.True(
                planned.GetProperty("candidate_validation")
                    .GetProperty("no_new_errors")
                    .GetBoolean()
            );
            var definition = Assert.Single(
                planned.GetProperty("style_definition_operations").EnumerateArray()
            );
            Assert.Equal("consolidate_style", definition.GetProperty("kind").GetString());
            Assert.Equal("Target", definition.GetProperty("style_id").GetString());
            Assert.Equal("Source", definition.GetProperty("source_style_id").GetString());
            Assert.Equal(2, definition.GetProperty("reference_update_count").GetInt32());
            Assert.False(planned.GetProperty("raw_xml_returned").GetBoolean());
            Assert.False(planned.GetProperty("word_opened").GetBoolean());

            using var applyArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                expected_plan_id = planId,
                commands = Commands(true),
                keep_backup = true,
            }));
            var appliedObject = await service.CallAsync(
                "apply_ooxml_semantic_edits",
                applyArguments.RootElement,
                CancellationToken.None
            );
            using var appliedJson = JsonDocument.Parse(JsonSerializer.Serialize(appliedObject));
            var applied = appliedJson.RootElement;

            Assert.True(applied.GetProperty("applied").GetBoolean());
            Assert.Equal(
                planned.GetProperty("result_package_fingerprint").GetString(),
                applied.GetProperty("package_fingerprint").GetString()
            );
            Assert.Equal(
                ["word/document.xml", "word/styles.xml"],
                applied.GetProperty("changed_entry_names")
                    .EnumerateArray()
                    .Select(item => item.GetString()!)
                    .Order(StringComparer.Ordinal)
                    .ToArray()
            );
            var backupPath = applied.GetProperty("backup_path").GetString()!;
            Assert.Equal(beforeBytes, File.ReadAllBytes(backupPath));

            var after = reader.Read(path);
            var afterSemantic = new WordSemanticProjector().Project(after);
            var styles = new WordStyleGraphBuilder().Build(after, afterSemantic);
            Assert.False(styles.TryGetStyle("Source", out _));
            Assert.True(styles.TryGetStyle("Target", out var target));
            Assert.True(target!.InheritanceResolvable);
            Assert.Equal(
                "Target",
                styles.Styles.Single(style => style.StyleId == "Derived").BasedOnStyleId
            );
            Assert.Equal(
                "Target",
                afterSemantic.Nodes.Single(node =>
                    node.Kind == WordSemanticNodeKind.Paragraph
                ).Properties["style_id"]
            );
            Assert.All(after.Entries.Where(entry =>
                entry.Name is not "word/document.xml" and not "word/styles.xml"
            ), entry => Assert.Equal(beforeHashes[entry.Name], entry.Sha256));
            Assert.False(applied.GetProperty("raw_xml_returned").GetBoolean());
            Assert.False(applied.GetProperty("word_opened").GetBoolean());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SemanticUnusedStyleDeletionPlansAndAppliesValidatedRemovalWithoutWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-unused-style-deletion-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "delete-unused.docx");
            CreatePackage(
                path,
                stylesXml: SemanticUnusedStyleDeletionStylesXml(),
                paragraphPropertiesXml: "<w:pPr><w:pStyle w:val=\"Base\"/></w:pPr>"
            );
            var beforeBytes = File.ReadAllBytes(path);
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var beforeHashes = before.Entries.ToDictionary(
                entry => entry.Name,
                entry => entry.Sha256,
                StringComparer.Ordinal
            );

            static object[] Commands(bool reordered) => reordered
                ?
                [
                    new Dictionary<string, object?>
                    {
                        ["style_id"] = "Unused",
                        ["type"] = "delete_unused_style",
                    },
                ]
                :
                [
                    new Dictionary<string, object?>
                    {
                        ["type"] = "delete_unused_style",
                        ["style_id"] = "Unused",
                    },
                ];

            var service = new WordLiveService(new NoInvokeHost());
            using var planArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                commands = Commands(false),
                include_details = true,
            }));
            using var reorderedArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                commands = Commands(true),
            }));
            var plannedObject = await service.CallAsync(
                "plan_ooxml_semantic_edits",
                planArguments.RootElement,
                CancellationToken.None
            );
            var reorderedObject = await service.CallAsync(
                "plan_ooxml_semantic_edits",
                reorderedArguments.RootElement,
                CancellationToken.None
            );
            using var plannedJson = JsonDocument.Parse(JsonSerializer.Serialize(plannedObject));
            using var reorderedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(reorderedObject)
            );
            var planned = plannedJson.RootElement;
            var planId = planned.GetProperty("plan_id").GetString()!;

            Assert.Equal(beforeBytes, File.ReadAllBytes(path));
            Assert.Equal(planId, reorderedJson.RootElement.GetProperty("plan_id").GetString());
            Assert.Equal(1, planned.GetProperty("style_definition_count").GetInt32());
            Assert.Equal(0, planned.GetProperty("style_consolidation_count").GetInt32());
            Assert.Equal(1, planned.GetProperty("style_deletion_count").GetInt32());
            Assert.Equal(0, planned.GetProperty("style_reference_update_count").GetInt32());
            Assert.Equal(1, planned.GetProperty("operation_count").GetInt32());
            Assert.Equal(1, planned.GetProperty("changed_part_count").GetInt32());
            Assert.True(planned.GetProperty("can_apply").GetBoolean(), planned.GetRawText());
            Assert.True(
                planned.GetProperty("candidate_validation")
                    .GetProperty("no_new_errors")
                    .GetBoolean()
            );
            var operation = Assert.Single(
                planned.GetProperty("style_definition_operations").EnumerateArray()
            );
            Assert.Equal("delete_unused_style", operation.GetProperty("kind").GetString());
            Assert.Equal("Unused", operation.GetProperty("style_id").GetString());
            Assert.True(operation.GetProperty("xml_byte_delta").GetInt32() < 0);

            using var applyArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                expected_plan_id = planId,
                commands = Commands(true),
                keep_backup = true,
            }));
            var appliedObject = await service.CallAsync(
                "apply_ooxml_semantic_edits",
                applyArguments.RootElement,
                CancellationToken.None
            );
            using var appliedJson = JsonDocument.Parse(JsonSerializer.Serialize(appliedObject));
            var applied = appliedJson.RootElement;

            Assert.True(applied.GetProperty("applied").GetBoolean());
            Assert.Equal(
                planned.GetProperty("result_package_fingerprint").GetString(),
                applied.GetProperty("package_fingerprint").GetString()
            );
            Assert.Equal(
                ["word/styles.xml"],
                applied.GetProperty("changed_entry_names")
                    .EnumerateArray()
                    .Select(item => item.GetString()!)
                    .ToArray()
            );
            Assert.Equal(
                beforeBytes,
                File.ReadAllBytes(applied.GetProperty("backup_path").GetString()!)
            );

            var after = reader.Read(path);
            var afterSemantic = new WordSemanticProjector().Project(after);
            var styles = new WordStyleGraphBuilder().Build(after, afterSemantic);
            Assert.False(styles.TryGetStyle("Unused", out _));
            Assert.True(styles.TryGetStyle("Base", out _));
            Assert.True(styles.TryGetStyle("Keep", out _));
            Assert.Equal(
                "Base",
                afterSemantic.Nodes.Single(node =>
                    node.Kind == WordSemanticNodeKind.Paragraph
                ).Properties["style_id"]
            );
            Assert.All(after.Entries.Where(entry => entry.Name != "word/styles.xml"), entry =>
                Assert.Equal(beforeHashes[entry.Name], entry.Sha256)
            );
            Assert.False(applied.GetProperty("raw_xml_returned").GetBoolean());
            Assert.False(applied.GetProperty("word_opened").GetBoolean());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SemanticStyleApplyRejectsSignedPackageAndLeavesItUntouched()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-signed-semantic-edit-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "signed-style.docx");
            CreatePackage(
                path,
                signed: true,
                stylesXml: SemanticEditStylesXml(),
                paragraphPropertiesXml: "<w:pPr><w:pStyle w:val=\"OldPara\"/></w:pPr>"
            );
            var beforeBytes = File.ReadAllBytes(path);
            var package = new OpcPackageReader().Read(path);
            var paragraph = new WordSemanticProjector().Project(package).Nodes.Single(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
            );
            var commands = new[]
            {
                new
                {
                    type = "set_style",
                    node_id = paragraph.Id.Value,
                    style_id = "Definition",
                    expected_style_id = "OldPara",
                },
            };
            var service = new WordLiveService(new NoInvokeHost());
            using var planArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                commands,
            }));
            var planObject = await service.CallAsync(
                "plan_ooxml_semantic_edits",
                planArguments.RootElement,
                CancellationToken.None
            );
            using var planJson = JsonDocument.Parse(JsonSerializer.Serialize(planObject));
            Assert.True(planJson.RootElement.GetProperty("apply_blocked").GetBoolean());
            Assert.Contains(
                planJson.RootElement.GetProperty("apply_blocked_reasons").EnumerateArray(),
                item => item.GetString() == "digital_signature_present"
            );
            using var applyArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                expected_plan_id = planJson.RootElement.GetProperty("plan_id").GetString(),
                commands,
            }));

            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "apply_ooxml_semantic_edits",
                    applyArguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("SIGNED_PACKAGE", exception.ErrorCode);
            Assert.Equal(beforeBytes, File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SemanticStyleDefinitionInputsFailClosedAndBindPlanIntent()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-style-definition-rejection-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "reject-definitions.docx");
            CreatePackage(path, stylesXml: SemanticEditStylesXml());
            var beforeBytes = File.ReadAllBytes(path);
            var package = new OpcPackageReader().Read(path);
            var service = new WordLiveService(new NoInvokeHost());

            async Task<NativeToolException> RejectPlanAsync(object[] commands)
            {
                using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    local_path = path,
                    expected_package_fingerprint = package.Fingerprint,
                    commands,
                }));
                return await Assert.ThrowsAsync<NativeToolException>(() =>
                    service.CallAsync(
                        "plan_ooxml_semantic_edits",
                        arguments.RootElement,
                        CancellationToken.None
                    )
                );
            }

            var missingSource = await RejectPlanAsync(
            [
                new
                {
                    type = "clone_style",
                    source_style_id = "Missing",
                    style_id = "Copy",
                    name = "Copy",
                },
            ]);
            Assert.Equal("UNSAFE_EDIT", missingSource.ErrorCode);

            var invalidType = await RejectPlanAsync(
            [
                new
                {
                    type = "create_style",
                    style_id = "BadType",
                    name = "Bad type",
                    style_type = "shape",
                },
            ]);
            Assert.Equal("INVALID_INPUT", invalidType.ErrorCode);

            var invalidNext = await RejectPlanAsync(
            [
                new
                {
                    type = "create_style",
                    style_id = "CharacterWithNext",
                    name = "Character with next",
                    style_type = "character",
                    next_style_id = "OldPara",
                },
            ]);
            Assert.Equal("UNSAFE_EDIT", invalidNext.ErrorCode);

            var missingConsolidationTarget = await RejectPlanAsync(
            [
                new
                {
                    type = "consolidate_style",
                    source_style_id = "OldPara",
                },
            ]);
            Assert.Equal("INVALID_INPUT", missingConsolidationTarget.ErrorCode);

            var unsafeBuiltInConsolidation = await RejectPlanAsync(
            [
                new
                {
                    type = "consolidate_style",
                    source_style_id = "OldPara",
                    target_style_id = "Definition",
                },
            ]);
            Assert.Equal("UNSAFE_EDIT", unsafeBuiltInConsolidation.ErrorCode);

            var unknownConsolidationProperty = await RejectPlanAsync(
            [
                new
                {
                    type = "consolidate_style",
                    source_style_id = "OldPara",
                    target_style_id = "Definition",
                    force = true,
                },
            ]);
            Assert.Equal("INVALID_INPUT", unknownConsolidationProperty.ErrorCode);

            var missingDeletionStyle = await RejectPlanAsync(
            [
                new
                {
                    type = "delete_unused_style",
                },
            ]);
            Assert.Equal("INVALID_INPUT", missingDeletionStyle.ErrorCode);

            var unsafeBuiltInDeletion = await RejectPlanAsync(
            [
                new
                {
                    type = "delete_unused_style",
                    style_id = "OldPara",
                },
            ]);
            Assert.Equal("UNSAFE_EDIT", unsafeBuiltInDeletion.ErrorCode);

            var unknownDeletionProperty = await RejectPlanAsync(
            [
                new
                {
                    type = "delete_unused_style",
                    style_id = "OldPara",
                    force = true,
                },
            ]);
            Assert.Equal("INVALID_INPUT", unknownDeletionProperty.ErrorCode);

            var missingRenameName = await RejectPlanAsync(
            [
                new
                {
                    type = "rename_style",
                    style_id = "Definition",
                },
            ]);
            Assert.Equal("INVALID_INPUT", missingRenameName.ErrorCode);

            var unsafeBuiltInRename = await RejectPlanAsync(
            [
                new
                {
                    type = "rename_style",
                    style_id = "OldPara",
                    name = "Renamed",
                },
            ]);
            Assert.Equal("UNSAFE_EDIT", unsafeBuiltInRename.ErrorCode);

            var unknownRenameProperty = await RejectPlanAsync(
            [
                new
                {
                    type = "rename_style",
                    style_id = "Definition",
                    name = "Renamed",
                    new_style_id = "ChangedId",
                },
            ]);
            Assert.Equal("INVALID_INPUT", unknownRenameProperty.ErrorCode);

            using var duplicateArguments = JsonDocument.Parse(
                $$"""
                {
                  "local_path": {{JsonSerializer.Serialize(path)}},
                  "expected_package_fingerprint": "{{package.Fingerprint}}",
                  "commands": [{
                    "type": "create_style",
                    "style_id": "Duplicate",
                    "style_id": "DuplicateAgain",
                    "name": "Duplicate",
                    "style_type": "paragraph"
                  }]
                }
                """
            );
            var duplicate = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "plan_ooxml_semantic_edits",
                    duplicateArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_INPUT", duplicate.ErrorCode);

            object[] reviewed =
            [
                new
                {
                    type = "create_style",
                    style_id = "Reviewed",
                    name = "Reviewed name",
                    style_type = "paragraph",
                    based_on_style_id = "OldPara",
                },
            ];
            using var planArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                commands = reviewed,
            }));
            var planObject = await service.CallAsync(
                "plan_ooxml_semantic_edits",
                planArguments.RootElement,
                CancellationToken.None
            );
            using var planJson = JsonDocument.Parse(JsonSerializer.Serialize(planObject));
            object[] changedIntent =
            [
                new
                {
                    type = "create_style",
                    style_id = "Reviewed",
                    name = "Changed after review",
                    style_type = "paragraph",
                    based_on_style_id = "OldPara",
                },
            ];
            using var applyArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                expected_plan_id = planJson.RootElement.GetProperty("plan_id").GetString(),
                commands = changedIntent,
            }));
            var mismatch = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "apply_ooxml_semantic_edits",
                    applyArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("PLAN_MISMATCH", mismatch.ErrorCode);
            Assert.Equal(beforeBytes, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(directory, "*.bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SemanticStyleRenameKeepsTheInternalIdAndAppliesValidatedNameOnlyMutation()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-style-rename-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "rename-style.docx");
            CreatePackage(
                path,
                stylesXml: SemanticUnusedStyleDeletionStylesXml(),
                paragraphPropertiesXml: "<w:pPr><w:pStyle w:val=\"Unused\"/></w:pPr>"
            );
            var beforeBytes = File.ReadAllBytes(path);
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var beforeHashes = before.Entries.ToDictionary(
                entry => entry.Name,
                entry => entry.Sha256,
                StringComparer.Ordinal
            );

            static object[] Commands(bool reordered) => reordered
                ?
                [
                    new Dictionary<string, object?>
                    {
                        ["name"] = "Renamed & visible",
                        ["style_id"] = "Unused",
                        ["type"] = "rename_style",
                    },
                ]
                :
                [
                    new Dictionary<string, object?>
                    {
                        ["type"] = "rename_style",
                        ["style_id"] = "Unused",
                        ["name"] = "Renamed & visible",
                    },
                ];

            var service = new WordLiveService(new NoInvokeHost());
            using var planArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                commands = Commands(false),
                include_details = true,
            }));
            using var reorderedArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                commands = Commands(true),
            }));
            var plannedObject = await service.CallAsync(
                "plan_ooxml_semantic_edits",
                planArguments.RootElement,
                CancellationToken.None
            );
            var reorderedObject = await service.CallAsync(
                "plan_ooxml_semantic_edits",
                reorderedArguments.RootElement,
                CancellationToken.None
            );
            using var plannedJson = JsonDocument.Parse(JsonSerializer.Serialize(plannedObject));
            using var reorderedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(reorderedObject)
            );
            var planned = plannedJson.RootElement;
            var planId = planned.GetProperty("plan_id").GetString()!;

            Assert.Equal(beforeBytes, File.ReadAllBytes(path));
            Assert.Equal(planId, reorderedJson.RootElement.GetProperty("plan_id").GetString());
            Assert.Equal(1, planned.GetProperty("style_definition_count").GetInt32());
            Assert.Equal(0, planned.GetProperty("style_consolidation_count").GetInt32());
            Assert.Equal(0, planned.GetProperty("style_deletion_count").GetInt32());
            Assert.Equal(1, planned.GetProperty("style_rename_count").GetInt32());
            Assert.Equal(0, planned.GetProperty("style_reference_update_count").GetInt32());
            Assert.Equal(1, planned.GetProperty("operation_count").GetInt32());
            Assert.Equal(1, planned.GetProperty("changed_part_count").GetInt32());
            Assert.True(planned.GetProperty("can_apply").GetBoolean(), planned.GetRawText());
            Assert.True(
                planned.GetProperty("candidate_validation")
                    .GetProperty("no_new_errors")
                    .GetBoolean()
            );
            var operation = Assert.Single(
                planned.GetProperty("style_definition_operations").EnumerateArray()
            );
            Assert.Equal("rename_style", operation.GetProperty("kind").GetString());
            Assert.Equal("Unused", operation.GetProperty("style_id").GetString());
            Assert.Equal("Unused", operation.GetProperty("source_style_id").GetString());

            using var applyArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                expected_plan_id = planId,
                commands = Commands(true),
                keep_backup = true,
            }));
            var appliedObject = await service.CallAsync(
                "apply_ooxml_semantic_edits",
                applyArguments.RootElement,
                CancellationToken.None
            );
            using var appliedJson = JsonDocument.Parse(JsonSerializer.Serialize(appliedObject));
            var applied = appliedJson.RootElement;

            Assert.True(applied.GetProperty("applied").GetBoolean());
            Assert.Equal(
                planned.GetProperty("result_package_fingerprint").GetString(),
                applied.GetProperty("package_fingerprint").GetString()
            );
            Assert.Equal(
                ["word/styles.xml"],
                applied.GetProperty("changed_entry_names")
                    .EnumerateArray()
                    .Select(item => item.GetString()!)
                    .ToArray()
            );
            Assert.Equal(
                beforeBytes,
                File.ReadAllBytes(applied.GetProperty("backup_path").GetString()!)
            );

            var after = reader.Read(path);
            var afterSemantic = new WordSemanticProjector().Project(after);
            var styles = new WordStyleGraphBuilder().Build(after, afterSemantic);
            Assert.True(styles.TryGetStyle("Unused", out var renamed));
            Assert.Equal("Renamed & visible", renamed!.Name);
            Assert.Equal(
                "Unused",
                afterSemantic.Nodes.Single(node =>
                    node.Kind == WordSemanticNodeKind.Paragraph
                ).Properties["style_id"]
            );
            Assert.All(after.Entries.Where(entry => entry.Name != "word/styles.xml"), entry =>
                Assert.Equal(beforeHashes[entry.Name], entry.Sha256)
            );
            Assert.False(applied.GetProperty("raw_xml_returned").GetBoolean());
            Assert.False(applied.GetProperty("word_opened").GetBoolean());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BulkSemanticStyleSelectorExpandsCanonicallyAndAppliesWithoutNodeIds()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-bulk-semantic-edit-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "bulk-style.docx");
            CreatePackage(
                path,
                stylesXml: SemanticEditStylesXml(),
                paragraphPropertiesXml: "<w:pPr><w:pStyle w:val=\"OldPara\"/></w:pPr>",
                additionalBodyXml: """
                <w:p w14:paraId="00445566">
                  <w:pPr><w:pStyle w:val="OldPara"/></w:pPr>
                  <w:r><w:t>Second definition</w:t></w:r>
                </w:p>
                """
            );
            var beforeBytes = File.ReadAllBytes(path);
            var package = new OpcPackageReader().Read(path);
            var firstSelector = new Dictionary<string, object?>
            {
                ["kind"] = "paragraph",
                ["property_equals"] = new Dictionary<string, string>
                {
                    ["style_id"] = "OldPara",
                },
            };
            var reorderedSelector = new Dictionary<string, object?>
            {
                ["property_equals"] = new Dictionary<string, string>
                {
                    ["style_id"] = "OldPara",
                },
                ["kind"] = "paragraph",
            };
            static object[] Commands(Dictionary<string, object?> selector) =>
            [
                new Dictionary<string, object?>
                {
                    ["type"] = "set_style_where",
                    ["selector"] = selector,
                    ["style_id"] = "Definition",
                    ["expected_style_id"] = "OldPara",
                    ["max_matches"] = 2,
                },
            ];
            var service = new WordLiveService(new NoInvokeHost());
            using var firstArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                commands = Commands(firstSelector),
                include_details = true,
            }));
            using var reorderedArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                commands = Commands(reorderedSelector),
            }));

            var firstObject = await service.CallAsync(
                "plan_ooxml_semantic_edits",
                firstArguments.RootElement,
                CancellationToken.None
            );
            var reorderedObject = await service.CallAsync(
                "plan_ooxml_semantic_edits",
                reorderedArguments.RootElement,
                CancellationToken.None
            );
            using var firstJson = JsonDocument.Parse(JsonSerializer.Serialize(firstObject));
            using var reorderedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(reorderedObject)
            );
            var planned = firstJson.RootElement;
            var planId = planned.GetProperty("plan_id").GetString();

            Assert.Equal(beforeBytes, File.ReadAllBytes(path));
            Assert.Equal(planId, reorderedJson.RootElement.GetProperty("plan_id").GetString());
            Assert.Equal(1, planned.GetProperty("submitted_command_count").GetInt32());
            Assert.Equal(1, planned.GetProperty("selector_command_count").GetInt32());
            Assert.Equal(2, planned.GetProperty("selector_match_count").GetInt32());
            Assert.Equal(2, planned.GetProperty("operation_count").GetInt32());
            Assert.Equal(2, planned.GetProperty("changed_operation_count").GetInt32());
            var resolution = Assert.Single(
                planned.GetProperty("selector_resolutions").EnumerateArray()
            );
            Assert.Equal(2, resolution.GetProperty("matched_node_count").GetInt32());
            Assert.Equal("all_nodes", resolution.GetProperty("candidate_seed").GetString());

            using var applyArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                expected_plan_id = planId,
                commands = Commands(reorderedSelector),
                keep_backup = false,
            }));
            var appliedObject = await service.CallAsync(
                "apply_ooxml_semantic_edits",
                applyArguments.RootElement,
                CancellationToken.None
            );
            using var appliedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(appliedObject)
            );

            Assert.True(appliedJson.RootElement.GetProperty("applied").GetBoolean());
            var after = new OpcPackageReader().Read(path);
            Assert.Equal(
                2,
                new WordSemanticProjector().Project(after).Nodes.Count(node =>
                    node.Kind == WordSemanticNodeKind.Paragraph
                    && node.Properties.GetValueOrDefault("style_id") == "Definition"
                )
            );
            Assert.Equal(
                package.Parts["/word/styles.xml"].Entry.Sha256,
                after.Parts["/word/styles.xml"].Entry.Sha256
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BulkSemanticStyleSelectorUsesStructuralPredicates()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-structural-semantic-edit-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "structural-style.docx");
            CreatePackage(
                path,
                stylesXml: SemanticEditStylesXml(),
                paragraphPropertiesXml: "<w:pPr><w:pStyle w:val=\"OldPara\"/></w:pPr>",
                additionalBodyXml: """
                <w:p w14:paraId="00445566">
                  <w:pPr><w:pStyle w:val="OldPara"/></w:pPr>
                  <w:r><w:t>Paragraph without math</w:t></w:r>
                </w:p>
                """
            );
            var package = new OpcPackageReader().Read(path);
            var commands = new[]
            {
                new
                {
                    type = "set_style_where",
                    selector = new
                    {
                        kind = "paragraph",
                        descendant = new { kind = "equation" },
                    },
                    style_id = "Definition",
                    expected_style_id = "OldPara",
                    max_matches = 1,
                },
            };
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                commands,
            }));

            var resultObject = await new WordLiveService(new NoInvokeHost()).CallAsync(
                "plan_ooxml_semantic_edits",
                arguments.RootElement,
                CancellationToken.None
            );
            using var resultJson = JsonDocument.Parse(JsonSerializer.Serialize(resultObject));
            var result = resultJson.RootElement;

            Assert.Equal(1, result.GetProperty("selector_match_count").GetInt32());
            Assert.Equal(1, result.GetProperty("operation_count").GetInt32());
            Assert.True(result.GetProperty("can_apply").GetBoolean());
            Assert.Equal(package.Fingerprint, new OpcPackageReader().Read(path).Fingerprint);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BulkSemanticStyleSelectorFailsClosedOnEmptyBroadInvalidAndOverlap()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-bulk-semantic-edit-rejection-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "reject-bulk.docx");
            CreatePackage(
                path,
                stylesXml: SemanticEditStylesXml(),
                paragraphPropertiesXml: "<w:pPr><w:pStyle w:val=\"OldPara\"/></w:pPr>",
                additionalBodyXml: """
                <w:p><w:pPr><w:pStyle w:val="OldPara"/></w:pPr></w:p>
                """
            );
            var beforeBytes = File.ReadAllBytes(path);
            var package = new OpcPackageReader().Read(path);
            var paragraph = new WordSemanticProjector().Project(package).Nodes.First(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
            );
            var service = new WordLiveService(new NoInvokeHost());

            async Task<NativeToolException> RejectAsync(object[] commands)
            {
                using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    local_path = path,
                    expected_package_fingerprint = package.Fingerprint,
                    commands,
                }));
                return await Assert.ThrowsAsync<NativeToolException>(() =>
                    service.CallAsync(
                        "plan_ooxml_semantic_edits",
                        arguments.RootElement,
                        CancellationToken.None
                    )
                );
            }

            var broad = await RejectAsync(
            [
                new
                {
                    type = "set_style_where",
                    selector = new { kind = "paragraph" },
                    style_id = "Definition",
                    max_matches = 1,
                },
            ]);
            Assert.Equal("SELECTION_LIMIT", broad.ErrorCode);

            var empty = await RejectAsync(
            [
                new
                {
                    type = "set_style_where",
                    selector = new
                    {
                        kind = "paragraph",
                        property_equals = new { style_id = "Missing" },
                    },
                    style_id = "Definition",
                    max_matches = 2,
                },
            ]);
            Assert.Equal("EMPTY_SELECTION", empty.ErrorCode);

            var invalid = await RejectAsync(
            [
                new
                {
                    type = "set_style_where",
                    selector = new { kind = "equation" },
                    style_id = "Definition",
                    max_matches = 1,
                },
            ]);
            Assert.Equal("INVALID_INPUT", invalid.ErrorCode);

            using var duplicateArguments = JsonDocument.Parse(
                $$"""
                {
                  "local_path": {{JsonSerializer.Serialize(path)}},
                  "expected_package_fingerprint": "{{package.Fingerprint}}",
                  "commands": [{
                    "type": "set_style_where",
                    "selector": {"kind": "paragraph", "kind": "run"},
                    "style_id": "Definition",
                    "max_matches": 2
                  }]
                }
                """
            );
            var duplicate = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "plan_ooxml_semantic_edits",
                    duplicateArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_INPUT", duplicate.ErrorCode);

            var overlap = await RejectAsync(
            [
                new
                {
                    type = "set_style",
                    node_id = paragraph.Id.Value,
                    style_id = "Definition",
                },
                new
                {
                    type = "set_style_where",
                    selector = new
                    {
                        kind = "paragraph",
                        property_equals = new { style_id = "OldPara" },
                    },
                    style_id = "Definition",
                    max_matches = 2,
                },
            ]);
            Assert.Equal("UNSAFE_EDIT", overlap.ErrorCode);
            Assert.Equal(beforeBytes, File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectReferencesIsStoryAwareRedactedAndNeverStartsWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-reference-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "references.docx");
            CreatePackage(
                path,
                additionalBodyXml: """
                <w:p>
                  <w:bookmarkStart w:id="9" w:name="SecretAnchor"/>
                  <w:r><w:t>Secret result</w:t></w:r>
                  <w:bookmarkEnd w:id="9"/>
                </w:p>
                <w:p>
                  <w:r><w:fldChar w:fldCharType="begin"/></w:r>
                  <w:r><w:instrText xml:space="preserve"> REF secretanchor \h </w:instrText></w:r>
                  <w:r><w:fldChar w:fldCharType="separate"/></w:r>
                  <w:r><w:t>Secret result</w:t></w:r>
                  <w:r><w:fldChar w:fldCharType="end"/></w:r>
                </w:p>
                <w:p>
                  <w:r><w:fldChar w:fldCharType="begin"/></w:r>
                  <w:r><w:instrText xml:space="preserve"> DDEAUTO calc.exe &quot;private-file&quot; topic </w:instrText></w:r>
                  <w:r><w:fldChar w:fldCharType="end"/></w:r>
                </w:p>
                """
            );
            var service = new WordLiveService(new NoInvokeHost());
            using var dependenciesArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "dependencies",
                    max_items = 10,
                })
            );

            var dependenciesObject = await service.CallAsync(
                "inspect_ooxml_references",
                dependenciesArguments.RootElement,
                CancellationToken.None
            );
            using var dependenciesJson = JsonDocument.Parse(
                JsonSerializer.Serialize(dependenciesObject)
            );
            var dependencies = dependenciesJson.RootElement;
            Assert.Equal(2, dependencies.GetProperty("field_count").GetInt32());
            Assert.Equal(1, dependencies.GetProperty("external_field_count").GetInt32());
            Assert.Equal(2, dependencies.GetProperty("dependency_count").GetInt32());
            Assert.False(dependencies.GetProperty("word_opened").GetBoolean());
            Assert.False(
                dependencies.GetProperty("external_targets_followed").GetBoolean()
            );
            Assert.All(
                dependencies.GetProperty("items").EnumerateArray(),
                item =>
                {
                    Assert.Equal(
                        JsonValueKind.Null,
                        item.GetProperty("target_key").ValueKind
                    );
                    Assert.Equal(
                        16,
                        item.GetProperty("target_key_fingerprint")
                            .GetString()!
                            .Length
                    );
                }
            );
            Assert.DoesNotContain("SecretAnchor", dependencies.GetRawText());
            Assert.DoesNotContain("private-file", dependencies.GetRawText());

            using var fieldArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "fields",
                    field_type = "ref",
                    detail = "parsed",
                    include_sensitive = true,
                    include_result_text = true,
                    include_source = true,
                })
            );
            var fieldObject = await service.CallAsync(
                "inspect_ooxml_references",
                fieldArguments.RootElement,
                CancellationToken.None
            );
            using var fieldJson = JsonDocument.Parse(JsonSerializer.Serialize(fieldObject));
            var field = Assert.Single(
                fieldJson.RootElement.GetProperty("items").EnumerateArray()
            );
            Assert.Equal("REF", field.GetProperty("field_type").GetString());
            Assert.Contains(
                "secretanchor",
                field.GetProperty("instruction").GetString(),
                StringComparison.Ordinal
            );
            Assert.Equal(
                "Secret result",
                field.GetProperty("result_text").GetString()
            );
            Assert.StartsWith(
                "wdn_",
                field.GetProperty("start_node_id").GetString(),
                StringComparison.Ordinal
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DefaultReferenceSummaryStaysCompactOnFieldHeavyCorpusDocument()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "upstream",
            "fixtures",
            "lo_toc_preserve.docx"
        );
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new { local_path = path })
        );
        var service = new WordLiveService(new NoInvokeHost());

        var result = await service.CallAsync(
            "inspect_ooxml_references",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));

        Assert.Equal("summary", json.RootElement.GetProperty("view").GetString());
        Assert.True(json.RootElement.GetProperty("field_count").GetInt32() >= 4);
        Assert.True(json.RootElement.GetProperty("bookmark_count").GetInt32() >= 20);
        Assert.True(
            json.RootElement.GetRawText().Length < 5_000,
            $"Default reference response is too large: {json.RootElement.GetRawText().Length} characters"
        );
        Assert.DoesNotContain("target_key", json.RootElement.GetRawText());
    }

    [Fact]
    public async Task InspectEquationsDefaultsToCompactRedactedParseOnlySummary()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-equation-summary-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "equation-summary.docx");
            CreatePackage(path);
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path })
            );
            var result = await new WordLiveService(new NoInvokeHost()).CallAsync(
                "inspect_ooxml_equations",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;
            var raw = root.GetRawText();

            Assert.Equal("summary", root.GetProperty("view").GetString());
            Assert.Equal(1, root.GetProperty("equation_count").GetInt32());
            Assert.Equal(1, root.GetProperty("inline_equation_count").GetInt32());
            Assert.Equal(0, root.GetProperty("display_equation_count").GetInt32());
            Assert.Equal(1, root.GetProperty("canonical_equation_count").GetInt32());
            Assert.False(root.GetProperty("word_opened").GetBoolean());
            Assert.False(root.GetProperty("conversion_performed").GetBoolean());
            Assert.False(root.GetProperty("raw_omml_returned").GetBoolean());
            Assert.False(root.GetProperty("external_content_followed").GetBoolean());
            Assert.False(root.GetProperty("sensitive_text_included").GetBoolean());
            Assert.Equal(
                "parse_only_no_word_no_conversion_no_external_access",
                root.GetProperty("execution_policy").GetString()
            );
            Assert.True(
                raw.Length < 5_000,
                $"Default equation response is too large: {raw.Length} characters"
            );
            Assert.DoesNotContain("text_preview", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("<m:", raw, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectEquationNodesReturnsCanonicalPagedSourceLinkedGraphOnExplicitOptIn()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-equation-node-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "equation-nodes.docx");
            CreatePackage(path);
            var service = new WordLiveService(new NoInvokeHost());
            using var equationArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "equations",
                    detail = "properties",
                    include_sensitive = true,
                    text_preview_chars = 8,
                    include_source = true,
                })
            );
            var equationResult = await service.CallAsync(
                "inspect_ooxml_equations",
                equationArguments.RootElement,
                CancellationToken.None
            );
            using var equationJson = JsonDocument.Parse(
                JsonSerializer.Serialize(equationResult)
            );
            var equation = Assert.Single(
                equationJson.RootElement.GetProperty("items").EnumerateArray()
            );
            var equationId = equation.GetProperty("equation_id").GetString();

            Assert.NotNull(equationId);
            Assert.Equal("ab", equation.GetProperty("text_preview").GetString());
            Assert.Equal(
                16,
                equation.GetProperty("text_fingerprint").GetString()!.Length
            );
            Assert.Equal(
                "/word/document.xml",
                equation.GetProperty("part_uri").GetString()
            );
            Assert.StartsWith(
                "wdn_",
                equation.GetProperty("semantic_node_id").GetString(),
                StringComparison.Ordinal
            );

            using var nodeArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "nodes",
                    equation_id = equationId,
                    node_kind = "fraction",
                    detail = "properties",
                    include_sensitive = true,
                    text_preview_chars = 8,
                    include_source = true,
                    max_items = 10,
                })
            );
            var nodeResult = await service.CallAsync(
                "inspect_ooxml_equations",
                nodeArguments.RootElement,
                CancellationToken.None
            );
            using var nodeJson = JsonDocument.Parse(JsonSerializer.Serialize(nodeResult));
            var root = nodeJson.RootElement;
            var node = Assert.Single(root.GetProperty("items").EnumerateArray());

            Assert.Equal("fraction", node.GetProperty("kind").GetString());
            Assert.Equal("content", node.GetProperty("role").GetString());
            Assert.Equal(2, node.GetProperty("child_count").GetInt32());
            Assert.Equal(2, node.GetProperty("child_node_ids").GetArrayLength());
            Assert.Equal(JsonValueKind.Null, node.GetProperty("text_preview").ValueKind);
            Assert.Equal(1, root.GetProperty("matched_item_count").GetInt32());
            Assert.Equal(1, root.GetProperty("returned_item_count").GetInt32());
            Assert.Equal("dotnet-native", root.GetProperty("runtime").GetString());
            Assert.False(root.GetProperty("python_used").GetBoolean());

            using var pageArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "nodes",
                    equation_id = equationId,
                    max_items = 1,
                })
            );
            var pageResult = await service.CallAsync(
                "inspect_ooxml_equations",
                pageArguments.RootElement,
                CancellationToken.None
            );
            using var pageJson = JsonDocument.Parse(JsonSerializer.Serialize(pageResult));
            Assert.True(
                pageJson.RootElement.GetProperty("matched_item_count").GetInt32() > 1
            );
            Assert.Equal(
                1,
                pageJson.RootElement.GetProperty("returned_item_count").GetInt32()
            );
            Assert.Equal(1, pageJson.RootElement.GetProperty("next_offset").GetInt32());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectEquationsRejectsSensitivePreviewWithoutExplicitConsent()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-equation-privacy-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "equation-privacy.docx");
            CreatePackage(path);
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "equations",
                    text_preview_chars = 8,
                })
            );

            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                new WordLiveService(new NoInvokeHost()).CallAsync(
                    "inspect_ooxml_equations",
                    arguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("INVALID_INPUT", exception.ErrorCode);

            using var misplacedFilterArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "equations",
                    node_kind = "fraction",
                })
            );
            var misplacedFilter = await Assert.ThrowsAsync<NativeToolException>(() =>
                new WordLiveService(new NoInvokeHost()).CallAsync(
                    "inspect_ooxml_equations",
                    misplacedFilterArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_INPUT", misplacedFilter.ErrorCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyRejectsMismatchedPlanAndLeavesFileUntouched()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-plan-mismatch-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "mismatch.docx");
            CreatePackage(path);
            var bytes = File.ReadAllBytes(path);
            var package = new OpcPackageReader().Read(path);
            var text = new WordSemanticProjector().Project(package).Nodes.Single(node =>
                node.Kind == WordSemanticNodeKind.Text && node.Text == "Hello "
            );
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                expected_plan_id = "wplan_AAAAAAAAAAAAAAAAAAAA",
                commands = new[]
                {
                    new { node_id = text.Id.Value, new_text = "changed" },
                },
            }));

            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                new WordLiveService(new NoInvokeHost()).CallAsync(
                    "apply_ooxml_text_edits",
                    arguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("PLAN_MISMATCH", exception.ErrorCode);
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(directory, "*.bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyRejectsDigitallySignedPackageAndLeavesItUntouched()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-signed-package-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "signed.docx");
            CreatePackage(path, signed: true);
            var bytes = File.ReadAllBytes(path);
            var package = new OpcPackageReader().Read(path);
            var text = new WordSemanticProjector().Project(package).Nodes.Single(node =>
                node.Kind == WordSemanticNodeKind.Text && node.Text == "Hello "
            );
            var commands = new[]
            {
                new { node_id = text.Id.Value, new_text = "changed" },
            };
            var service = new WordLiveService(new NoInvokeHost());
            using var planArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                commands,
            }));
            var planObject = await service.CallAsync(
                "plan_ooxml_text_edits",
                planArguments.RootElement,
                CancellationToken.None
            );
            using var planJson = JsonDocument.Parse(JsonSerializer.Serialize(planObject));
            Assert.True(planJson.RootElement.GetProperty("apply_blocked").GetBoolean());
            var planId = planJson.RootElement.GetProperty("plan_id").GetString();
            using var applyArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                expected_plan_id = planId,
                commands,
            }));

            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "apply_ooxml_text_edits",
                    applyArguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("SIGNED_PACKAGE", exception.ErrorCode);
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReviewedNoOpDoesNotRewriteFileOrCreateBackup()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-noop-text-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "noop.docx");
            CreatePackage(path);
            var bytes = File.ReadAllBytes(path);
            var package = new OpcPackageReader().Read(path);
            var text = new WordSemanticProjector().Project(package).Nodes.Single(node =>
                node.Kind == WordSemanticNodeKind.Text && node.Text == "Hello "
            );
            var commands = new[]
            {
                new
                {
                    node_id = text.Id.Value,
                    new_text = "Hello ",
                    expected_text = "Hello ",
                },
            };
            var service = new WordLiveService(new NoInvokeHost());
            using var planArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                commands,
            }));
            var planObject = await service.CallAsync(
                "plan_ooxml_text_edits",
                planArguments.RootElement,
                CancellationToken.None
            );
            using var planJson = JsonDocument.Parse(JsonSerializer.Serialize(planObject));
            var planId = planJson.RootElement.GetProperty("plan_id").GetString();
            Assert.False(planJson.RootElement.GetProperty("has_changes").GetBoolean());
            using var applyArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                expected_plan_id = planId,
                commands,
            }));

            var applyObject = await service.CallAsync(
                "apply_ooxml_text_edits",
                applyArguments.RootElement,
                CancellationToken.None
            );
            using var applyJson = JsonDocument.Parse(JsonSerializer.Serialize(applyObject));

            Assert.False(applyJson.RootElement.GetProperty("applied").GetBoolean());
            Assert.True(applyJson.RootElement.GetProperty("no_op").GetBoolean());
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(directory, "*.bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CreatePackage(
        string path,
        bool signed = false,
        string? headerText = null,
        string? stylesXml = null,
        string? paragraphPropertiesXml = null,
        string? runPropertiesXml = null,
        string? numberingXml = null,
        string? themeXml = null,
        string? settingsXml = null,
        string? fontTableXml = null,
        string? additionalBodyXml = null
    )
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var signatureOverride = signed
            ? "<Override PartName=\"/_xmlsignatures/sig1.xml\" ContentType=\"application/vnd.openxmlformats-package.digital-signature-xmlsignature+xml\" />"
            : string.Empty;
        var headerOverride = headerText is null
            ? string.Empty
            : "<Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\" />";
        var stylesOverride = stylesXml is null
            ? string.Empty
            : "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\" />";
        var numberingOverride = numberingXml is null
            ? string.Empty
            : "<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\" />";
        var themeOverride = themeXml is null
            ? string.Empty
            : "<Override PartName=\"/word/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\" />";
        var settingsOverride = settingsXml is null
            ? string.Empty
            : "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\" />";
        var fontTableOverride = fontTableXml is null
            ? string.Empty
            : "<Override PartName=\"/word/fontTable.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml\" />";
        WriteEntry(
            archive,
            "[Content_Types].xml",
            $"""
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
              <Default Extension="xml" ContentType="application/xml" />
              <Default Extension="odttf" ContentType="application/vnd.openxmlformats-officedocument.obfuscatedFont" />
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" />
              {signatureOverride}
              {headerOverride}
              {stylesOverride}
              {numberingOverride}
              {themeOverride}
              {settingsOverride}
              {fontTableOverride}
            </Types>
            """
        );
        WriteEntry(
            archive,
            "_rels/.rels",
            """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml" />
            </Relationships>
            """
        );
        var headerReference = headerText is null
            ? string.Empty
            : "<w:sectPr><w:headerReference r:id=\"rIdHeader\" w:type=\"default\" /></w:sectPr>";
        WriteEntry(
            archive,
            "word/document.xml",
            $"""
            <w:document
                xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml"
                xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math"
                xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body>
                <w:p w14:paraId="00112233">
                  {paragraphPropertiesXml}
                  <w:r>{runPropertiesXml}<w:t>Hello </w:t></w:r>
                  <m:oMath><m:f><m:num><m:r><m:t>a</m:t></m:r></m:num><m:den><m:r><m:t>b</m:t></m:r></m:den></m:f></m:oMath>
                </w:p>
                {additionalBodyXml}
                {headerReference}
              </w:body>
            </w:document>
            """
        );
        if (
            headerText is not null
            || stylesXml is not null
            || numberingXml is not null
            || themeXml is not null
            || settingsXml is not null
            || fontTableXml is not null
        )
        {
            var headerRelationship = headerText is null
                ? string.Empty
                : "<Relationship Id=\"rIdHeader\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header1.xml\" />";
            var stylesRelationship = stylesXml is null
                ? string.Empty
                : "<Relationship Id=\"rIdStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\" />";
            var numberingRelationship = numberingXml is null
                ? string.Empty
                : "<Relationship Id=\"rIdNumbering\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering\" Target=\"numbering.xml\" />";
            var themeRelationship = themeXml is null
                ? string.Empty
                : "<Relationship Id=\"rIdTheme\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme\" Target=\"theme/theme1.xml\" />";
            var settingsRelationship = settingsXml is null
                ? string.Empty
                : "<Relationship Id=\"rIdSettings\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\" />";
            var fontTableRelationship = fontTableXml is null
                ? string.Empty
                : "<Relationship Id=\"rIdFontTable\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/fontTable\" Target=\"fontTable.xml\" />";
            WriteEntry(
                archive,
                "word/_rels/document.xml.rels",
                $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  {headerRelationship}
                  {stylesRelationship}
                  {numberingRelationship}
                  {themeRelationship}
                  {settingsRelationship}
                  {fontTableRelationship}
                </Relationships>
                """
            );
        }
        if (headerText is not null)
        {
            WriteEntry(
                archive,
                "word/header1.xml",
                $"""
                <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:p><w:r><w:t>{headerText}</w:t></w:r></w:p>
                </w:hdr>
                """
            );
        }
        if (stylesXml is not null)
        {
            WriteEntry(archive, "word/styles.xml", stylesXml);
        }
        if (numberingXml is not null)
        {
            WriteEntry(archive, "word/numbering.xml", numberingXml);
        }
        if (themeXml is not null)
        {
            WriteEntry(archive, "word/theme/theme1.xml", themeXml);
        }
        if (settingsXml is not null)
        {
            WriteEntry(archive, "word/settings.xml", settingsXml);
        }
        if (fontTableXml is not null)
        {
            WriteEntry(archive, "word/fontTable.xml", fontTableXml);
            WriteEntry(
                archive,
                "word/_rels/fontTable.xml.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdEmbeddedFont" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/font" Target="fonts/font1.odttf" />
                </Relationships>
                """
            );
            var fontEntry = archive.CreateEntry(
                "word/fonts/font1.odttf",
                CompressionLevel.Optimal
            );
            using var fontStream = fontEntry.Open();
            fontStream.Write([1, 2, 3, 4]);
        }
        if (signed)
        {
            WriteEntry(
                archive,
                "_xmlsignatures/sig1.xml",
                "<Signature xmlns=\"http://www.w3.org/2000/09/xmldsig#\" />"
            );
        }
    }

    private static string SemanticEditStylesXml() =>
        """
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:style w:type="paragraph" w:styleId="OldPara"><w:name w:val="Old paragraph"/></w:style>
          <w:style w:type="paragraph" w:styleId="Definition"><w:name w:val="Definition"/></w:style>
          <w:style w:type="character" w:styleId="Emphasis"><w:name w:val="Emphasis"/></w:style>
          <w:style w:type="table" w:styleId="Grid"><w:name w:val="Grid"/></w:style>
        </w:styles>
        """;

    private static string SemanticConsolidationStylesXml() =>
        """
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:style w:type="paragraph" w:styleId="Base"><w:name w:val="Base"/></w:style>
          <w:style w:type="paragraph" w:styleId="Source" w:customStyle="1"><w:name w:val="Source name"/><w:aliases w:val="Source alias"/><w:basedOn w:val="Base"/><w:next w:val="Source"/><w:qFormat/><w:rsid w:val="11111111"/><w:pPr><w:keepNext/></w:pPr><w:rPr><w:b/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Target" w:customStyle="1"><w:name w:val="Target name"/><w:aliases w:val="Target alias"/><w:basedOn w:val="Base"/><w:next w:val="Target"/><w:qFormat/><w:rsid w:val="22222222"/><w:pPr><w:keepNext/></w:pPr><w:rPr><w:b/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Derived" w:customStyle="1"><w:name w:val="Derived"/><w:basedOn w:val="Source"/></w:style>
        </w:styles>
        """;

    private static string SemanticUnusedStyleDeletionStylesXml() =>
        """
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml">
          <w:style w:type="paragraph" w:default="1" w:styleId="Base"><w:name w:val="Base"/></w:style>
          <w:style w:type="paragraph" w:styleId="Unused" w:customStyle="1"><w:name w:val="Unused"/><w:basedOn w:val="Base"/><w14:opaque w14:val="remove-with-style"/><w:pPr><w:keepNext/></w:pPr><w:rPr><w:b/></w:rPr></w:style>
          <w:style w:type="character" w:styleId="Keep" w:customStyle="1"><w:name w:val="Keep"/></w:style>
        </w:styles>
        """;

    private static string ThemeXml() =>
        """
        <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Office">
          <a:themeElements>
            <a:clrScheme name="Office">
              <a:dk1><a:sysClr val="windowText" lastClr="000000"/></a:dk1>
              <a:lt1><a:sysClr val="window" lastClr="FFFFFF"/></a:lt1>
              <a:dk2><a:srgbClr val="1F497D"/></a:dk2>
              <a:lt2><a:srgbClr val="EEECE1"/></a:lt2>
              <a:accent1><a:srgbClr val="4F81BD"/></a:accent1>
              <a:accent2><a:srgbClr val="C0504D"/></a:accent2>
              <a:accent3><a:srgbClr val="9BBB59"/></a:accent3>
              <a:accent4><a:srgbClr val="8064A2"/></a:accent4>
              <a:accent5><a:srgbClr val="4BACC6"/></a:accent5>
              <a:accent6><a:srgbClr val="F79646"/></a:accent6>
              <a:hlink><a:srgbClr val="0000FF"/></a:hlink>
              <a:folHlink><a:srgbClr val="800080"/></a:folHlink>
            </a:clrScheme>
            <a:fontScheme name="Office">
              <a:majorFont><a:latin typeface="Cambria"/><a:ea typeface=""/><a:cs typeface=""/><a:font script="Jpan" typeface="ＭＳ ゴシック"/></a:majorFont>
              <a:minorFont><a:latin typeface="Calibri"/><a:ea typeface=""/><a:cs typeface=""/><a:font script="Jpan" typeface="ＭＳ 明朝"/></a:minorFont>
            </a:fontScheme>
            <a:fmtScheme name="Office"><a:fillStyleLst/><a:lnStyleLst/><a:effectStyleLst/><a:bgFillStyleLst/></a:fmtScheme>
          </a:themeElements>
        </a:theme>
        """;

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
        throw new DirectoryNotFoundException(
            "Could not locate the WordToolkit repository root."
        );
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    private sealed class NoInvokeHost : IWordComHost
    {
        public Task<T> InvokeAsync<T>(
            Func<dynamic, T> operation,
            CancellationToken cancellationToken = default,
            bool launchIfMissing = false
        )
        {
            throw new Xunit.Sdk.XunitException(
                "Package inspection must not invoke the Word COM host."
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
