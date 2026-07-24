using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class FigurePackageInspectionTests
{
    [Fact]
    public async Task DefaultFigureInspectionIsCompactRedactedAndNeverInvokesWordOrTargets()
    {
        var path = CreateTemporaryPackage();
        try
        {
            var host = new NoInvokeHost();
            var service = new WordLiveService(host);
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path })
            );

            var result = await service.CallAsync(
                "inspect_ooxml_figures",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;

            Assert.Equal("summary", root.GetProperty("view").GetString());
            Assert.Equal(1, root.GetProperty("figure_count").GetInt32());
            Assert.Equal(1, root.GetProperty("caption_count").GetInt32());
            Assert.Equal(1, root.GetProperty("selected_association_count").GetInt32());
            Assert.Equal(1, root.GetProperty("external_resource_count").GetInt32());
            Assert.False(root.GetProperty("word_opened").GetBoolean());
            Assert.False(root.GetProperty("binary_resources_decoded").GetBoolean());
            Assert.False(root.GetProperty("external_targets_followed").GetBoolean());
            Assert.False(root.GetProperty("active_content_executed").GetBoolean());
            Assert.False(root.GetProperty("text_included").GetBoolean());
            Assert.False(root.GetProperty("source_included").GetBoolean());
            Assert.False(root.GetProperty("relationship_targets_included").GetBoolean());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("issues").ValueKind);
            var raw = root.GetRawText();
            Assert.DoesNotContain("PRIVATE FIGURE TITLE", raw);
            Assert.DoesNotContain("SECRET ALT DESCRIPTION", raw);
            Assert.DoesNotContain("CONFIDENTIAL CAPTION", raw);
            Assert.DoesNotContain("secret.example", raw);
            Assert.DoesNotContain("word/document.xml", raw);
            Assert.True(raw.Length < 5_000, $"Default figure response is too large: {raw.Length}");
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TextAndRelationshipTargetsRequireSeparateExplicitOptIns()
    {
        var path = CreateTemporaryPackage();
        try
        {
            var service = new WordLiveService(new NoInvokeHost());
            using var figuresArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "figures",
                    include_text = true,
                })
            );
            var figuresResult = await service.CallAsync(
                "inspect_ooxml_figures",
                figuresArguments.RootElement,
                CancellationToken.None
            );
            var figuresRaw = JsonSerializer.Serialize(figuresResult);
            using var figuresJson = JsonDocument.Parse(figuresRaw);
            var figure = Assert.Single(
                figuresJson.RootElement.GetProperty("items").EnumerateArray()
            );
            Assert.Equal(
                "single_declared_representation",
                figure.GetProperty("representation_selection_basis").GetString()
            );
            Assert.Contains("PRIVATE FIGURE TITLE", figuresRaw);
            Assert.Contains("SECRET ALT DESCRIPTION", figuresRaw);
            Assert.DoesNotContain("secret.example", figuresRaw);

            using var captionsArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "captions",
                    include_text = true,
                })
            );
            var captionsResult = await service.CallAsync(
                "inspect_ooxml_figures",
                captionsArguments.RootElement,
                CancellationToken.None
            );
            Assert.Contains("CONFIDENTIAL CAPTION", JsonSerializer.Serialize(captionsResult));

            using var resourcesArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "resources",
                    detail = "declared",
                    include_source = true,
                    include_relationship_targets = true,
                })
            );
            var resourcesResult = await service.CallAsync(
                "inspect_ooxml_figures",
                resourcesArguments.RootElement,
                CancellationToken.None
            );
            using var resourcesJson = JsonDocument.Parse(
                JsonSerializer.Serialize(resourcesResult)
            );
            var resource = Assert.Single(
                resourcesJson.RootElement.GetProperty("items").EnumerateArray()
            );
            Assert.Equal(
                "https://secret.example/private-figure.png",
                resource.GetProperty("target").GetString()
            );
            Assert.True(resource.GetProperty("external").GetBoolean());
            Assert.False(resource.GetProperty("resolved").GetBoolean());
            Assert.False(
                resourcesJson.RootElement.GetProperty("external_targets_followed").GetBoolean()
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RejectsInvalidViewAndUnknownFigureId()
    {
        var path = CreateTemporaryPackage();
        try
        {
            var service = new WordLiveService(new NoInvokeHost());
            using var invalidArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path, view = "xml" })
            );
            var invalid = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_figures",
                    invalidArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_INPUT", invalid.ErrorCode);

            using var missingArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "figures",
                    figure_id = "wdfig_AAAAAAAAAAAAAAAAAAAA",
                })
            );
            var missing = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_figures",
                    missingArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("NOT_FOUND", missing.ErrorCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GatewayEnforcesClosedCaseSensitiveFigureInputContract()
    {
        var path = CreateTemporaryPackage();
        try
        {
            foreach (var invalidArguments in new object[]
            {
                new { local_path = path, figure_id = "garbage" },
                new { local_path = path, object_kind = "Picture" },
                new { local_path = path, unknown_argument = true },
            })
            {
                using var response = await CallFigureGatewayAsync(invalidArguments);
                var result = response.RootElement.GetProperty("result");
                Assert.True(result.GetProperty("isError").GetBoolean());
                Assert.Equal(
                    "INVALID_INPUT",
                    result.GetProperty("structuredContent")
                        .GetProperty("error")
                        .GetProperty("code")
                        .GetString()
                );
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PackageErrorsDoNotLeakLocalPathsWithoutOptIn()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"PRIVATE-FIGURE-PATH-{Guid.NewGuid():N}.docx"
        );
        await File.WriteAllTextAsync(path, "not-a-package");
        try
        {
            var service = new WordLiveService(new NoInvokeHost());
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path })
            );
            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_figures",
                    arguments.RootElement,
                    CancellationToken.None
                )
            );
            var serialized = JsonSerializer.Serialize(exception.Details);
            Assert.DoesNotContain(path, serialized, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PRIVATE-FIGURE-PATH", serialized);
            Assert.Contains("reason_code", serialized);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FigureGatewayKeepsCompleteDefaultEnvelopeBoundedAndRedacted()
    {
        var path = CreateTemporaryPackage();
        try
        {
            var request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/call",
                @params = new
                {
                    name = "execute_wordtoolkit_action",
                    arguments = new
                    {
                        action = "inspect_ooxml_figures",
                        arguments = new { local_path = path },
                    },
                },
            });
            var output = new StringWriter();
            var host = new NoInvokeHost();
            var server = new McpServer(
                new StringReader(request + "\n"),
                output,
                ToolCatalog.LoadNativeWordTools(),
                new WordLiveService(host)
            );

            await server.RunAsync();

            var responseLine = output.ToString().TrimEnd('\r', '\n');
            using var response = JsonDocument.Parse(responseLine);
            var result = response.RootElement.GetProperty("result");
            var contentText = result.GetProperty("content")[0].GetProperty("text").GetString()!;
            var data = result.GetProperty("structuredContent").GetProperty("data");
            Assert.True(data.GetRawText().Length < 5_000);
            Assert.True(contentText.Length < 5_000);
            Assert.True(responseLine.Length < 8_000);
            Assert.DoesNotContain("PRIVATE FIGURE TITLE", responseLine);
            Assert.DoesNotContain("SECRET ALT DESCRIPTION", responseLine);
            Assert.DoesNotContain("CONFIDENTIAL CAPTION", responseLine);
            Assert.DoesNotContain("secret.example", responseLine);
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExactFiltersPreserveAmbiguousAssociationEvidence()
    {
        var path = CreateTemporaryPackage(ambiguous: true);
        try
        {
            var service = new WordLiveService(new NoInvokeHost());
            using var figureListArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path, view = "figures" })
            );
            var figureList = await service.CallAsync(
                "inspect_ooxml_figures",
                figureListArguments.RootElement,
                CancellationToken.None
            );
            using var figureListJson = JsonDocument.Parse(JsonSerializer.Serialize(figureList));
            var figureIds = figureListJson.RootElement.GetProperty("items")
                .EnumerateArray()
                .Select(item => item.GetProperty("figure_id").GetString()!)
                .ToArray();
            Assert.Equal(2, figureIds.Length);

            using var captionListArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path, view = "captions" })
            );
            var captionList = await service.CallAsync(
                "inspect_ooxml_figures",
                captionListArguments.RootElement,
                CancellationToken.None
            );
            using var captionListJson = JsonDocument.Parse(JsonSerializer.Serialize(captionList));
            var captionId = Assert.Single(
                captionListJson.RootElement.GetProperty("items").EnumerateArray()
            ).GetProperty("caption_id").GetString()!;

            foreach (var filter in new object[]
            {
                new { local_path = path, view = "associations", figure_id = figureIds[0] },
                new { local_path = path, view = "associations", caption_id = captionId },
                new { local_path = path, view = "associations", object_kind = "picture" },
            })
            {
                using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(filter));
                var result = await service.CallAsync(
                    "inspect_ooxml_figures",
                    arguments.RootElement,
                    CancellationToken.None
                );
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
                var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
                Assert.NotEmpty(items);
                Assert.All(items, item =>
                    Assert.Equal("ambiguous", item.GetProperty("status").GetString())
                );
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DeclaredLayoutStaysCompactAndPolygonCoordinatesRequireOptIn()
    {
        var path = CreateTemporaryPackage(advancedLayout: true);
        try
        {
            var host = new NoInvokeHost();
            var service = new WordLiveService(host);
            using var compactArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "representations",
                    detail = "declared",
                })
            );
            var compactResult = await service.CallAsync(
                "inspect_ooxml_figures",
                compactArguments.RootElement,
                CancellationToken.None
            );
            using var compactJson = JsonDocument.Parse(JsonSerializer.Serialize(compactResult));
            var compactRoot = compactJson.RootElement;
            Assert.False(compactRoot.GetProperty("geometry_included").GetBoolean());
            var compactPlacement = Assert.Single(
                compactRoot.GetProperty("items").EnumerateArray()
            ).GetProperty("placement");
            Assert.True(compactPlacement.GetProperty("declared_only_not_rendered_geometry").GetBoolean());
            Assert.Equal(
                2,
                compactPlacement.GetProperty("wrap")
                    .GetProperty("polygon_line_point_count")
                    .GetInt32()
            );
            Assert.Equal(
                JsonValueKind.Null,
                compactPlacement.GetProperty("wrap")
                    .GetProperty("polygon_line_points")
                    .ValueKind
            );

            using var fullArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "representations",
                    detail = "declared",
                    include_geometry = true,
                })
            );
            var fullResult = await service.CallAsync(
                "inspect_ooxml_figures",
                fullArguments.RootElement,
                CancellationToken.None
            );
            using var fullJson = JsonDocument.Parse(JsonSerializer.Serialize(fullResult));
            var fullRoot = fullJson.RootElement;
            Assert.True(fullRoot.GetProperty("geometry_included").GetBoolean());
            var wrap = Assert.Single(fullRoot.GetProperty("items").EnumerateArray())
                .GetProperty("placement")
                .GetProperty("wrap");
            Assert.Equal(2, wrap.GetProperty("polygon_line_points").GetArrayLength());
            Assert.False(wrap.GetProperty("polygon_line_points_truncated").GetBoolean());
            Assert.True(fullRoot.GetRawText().Length < 10_000);
            Assert.Equal(0, host.InvocationCount);

            using var invalidArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path, include_geometry = true })
            );
            var invalid = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_figures",
                    invalidArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_INPUT", invalid.ErrorCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTemporaryPackage(
        bool ambiguous = false,
        bool advancedLayout = false
    )
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-figure-{Guid.NewGuid():N}.docx"
        );
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(
            archive,
            "[Content_Types].xml",
            """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """
        );
        AddEntry(
            archive,
            "_rels/.rels",
            """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """
        );
        var secondDrawing = ambiguous
            ? """
                <w:r><w:drawing><wp:inline>
                  <wp:extent cx="914400" cy="457200"/>
                  <wp:docPr id="2" name="SECOND PRIVATE FIGURE" descr="SECOND SECRET DESCRIPTION"/>
                  <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                    <pic:pic><pic:nvPicPr><pic:cNvPr id="0" name="second.png"/><pic:cNvPicPr/></pic:nvPicPr>
                      <pic:blipFill><a:blip r:link="rIdExternal"/></pic:blipFill><pic:spPr/>
                    </pic:pic>
                  </a:graphicData></a:graphic>
                </wp:inline></w:drawing></w:r>
                """
            : string.Empty;
        var firstDrawing = advancedLayout
            ? """
                <w:r><w:drawing><wp:anchor distT="0" distB="0" distL="114300" distR="114300"
                  simplePos="0" relativeHeight="251658240" behindDoc="0" locked="1"
                  layoutInCell="1" allowOverlap="1">
                  <wp:simplePos x="0" y="0"/>
                  <wp:positionH relativeFrom="margin"><wp:align>center</wp:align></wp:positionH>
                  <wp:positionV relativeFrom="paragraph"><wp:posOffset>202364</wp:posOffset></wp:positionV>
                  <wp:extent cx="914400" cy="457200"/>
                  <wp:effectExtent l="100" t="200" r="300" b="400"/>
                  <wp:wrapTight wrapText="bothSides"><wp:wrapPolygon edited="1">
                    <wp:start x="0" y="0"/><wp:lineTo x="21600" y="0"/><wp:lineTo x="21600" y="21600"/>
                  </wp:wrapPolygon></wp:wrapTight>
                  <wp:docPr id="1" name="PRIVATE FIGURE NAME" title="PRIVATE FIGURE TITLE" descr="SECRET ALT DESCRIPTION"/>
                  <wp:cNvGraphicFramePr/>
                  <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                    <pic:pic><pic:nvPicPr><pic:cNvPr id="0" name="private.png"/><pic:cNvPicPr/></pic:nvPicPr>
                      <pic:blipFill><a:blip r:link="rIdExternal"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
                      <pic:spPr><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>
                    </pic:pic>
                  </a:graphicData></a:graphic>
                  <wp14:sizeRelH relativeFrom="margin"><wp14:pctWidth>50000</wp14:pctWidth></wp14:sizeRelH>
                  <wp14:sizeRelV relativeFrom="margin"><wp14:pctHeight>25000</wp14:pctHeight></wp14:sizeRelV>
                </wp:anchor></w:drawing></w:r>
                """
            : """
                <w:r><w:drawing><wp:inline>
                  <wp:extent cx="914400" cy="457200"/>
                  <wp:docPr id="1" name="PRIVATE FIGURE NAME" title="PRIVATE FIGURE TITLE" descr="SECRET ALT DESCRIPTION"/>
                  <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                    <pic:pic><pic:nvPicPr><pic:cNvPr id="0" name="private.png"/><pic:cNvPicPr/></pic:nvPicPr>
                      <pic:blipFill><a:blip r:link="rIdExternal"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
                      <pic:spPr><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>
                    </pic:pic>
                  </a:graphicData></a:graphic>
                </wp:inline></w:drawing></w:r>
                """;
        AddEntry(
            archive,
            "word/document.xml",
            $$"""
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
              xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
              xmlns:wp14="http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing"
              xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
              xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"
              xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body>
                <w:p>{{firstDrawing}}{{secondDrawing}}</w:p>
                <w:p><w:pPr><w:pStyle w:val="Caption"/></w:pPr>
                  <w:r><w:fldChar w:fldCharType="begin"/></w:r>
                  <w:r><w:instrText xml:space="preserve"> SEQ Figure \* ARABIC </w:instrText></w:r>
                  <w:r><w:fldChar w:fldCharType="separate"/></w:r>
                  <w:r><w:t>1</w:t></w:r>
                  <w:r><w:fldChar w:fldCharType="end"/></w:r>
                  <w:r><w:t> — CONFIDENTIAL CAPTION</w:t></w:r>
                </w:p>
                <w:sectPr/>
              </w:body>
            </w:document>
            """
        );
        AddEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdExternal" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="https://secret.example/private-figure.png" TargetMode="External"/>
            </Relationships>
            """
        );
        return path;
    }

    private static async Task<JsonDocument> CallFigureGatewayAsync(object actionArguments)
    {
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = "execute_wordtoolkit_action",
                arguments = new
                {
                    action = "inspect_ooxml_figures",
                    arguments = actionArguments,
                },
            },
        });
        var output = new StringWriter();
        var server = new McpServer(
            new StringReader(request + "\n"),
            output,
            ToolCatalog.LoadNativeWordTools(),
            new WordLiveService(new NoInvokeHost())
        );
        await server.RunAsync();
        return JsonDocument.Parse(output.ToString().Trim());
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private sealed class NoInvokeHost : IWordComHost
    {
        public int InvocationCount { get; private set; }

        public Task<T> InvokeAsync<T>(
            Func<dynamic, T> operation,
            CancellationToken cancellationToken = default,
            bool launchIfMissing = false
        )
        {
            InvocationCount++;
            throw new InvalidOperationException("COM must not be invoked");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
