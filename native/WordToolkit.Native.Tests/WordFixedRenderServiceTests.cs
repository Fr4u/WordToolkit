using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Packaging;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class WordFixedRenderServiceTests
{
    private const string ContentTypesNamespace =
        "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string PackageRelationshipsNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRelationshipsNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public async Task PublishesWordPdfAndManifestWithoutMutatingPackage()
    {
        var directory = TemporaryDirectory();
        try
        {
            var source = Path.Combine(directory, "source.docx");
            var output = Path.Combine(directory, "output");
            Directory.CreateDirectory(output);
            CreatePackage(source);
            var before = File.ReadAllBytes(source);
            var fingerprint = new OpcPackageReader().Read(source).Fingerprint;
            await using var host = new FixedFormatFakeHost(pageCount: 3);
            var service = new WordLiveService(host);
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    new
                    {
                        local_path = source,
                        expected_package_fingerprint = fingerprint,
                        output_directory = output,
                        artifact_stem = "proof",
                        output = "pdf",
                        first_page = 2,
                        last_page = 3,
                    }
                )
            );

            var raw = await service.CallAsync(
                "render_ooxml_fixed_artifacts",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(
                JsonSerializer.Serialize(raw, JsonDefaults.Compact)
            );
            var result = json.RootElement;

            Assert.Equal(
                "wordtoolkit.render_ooxml_fixed_artifacts/1.0",
                result.GetProperty("operation_contract").GetString()
            );
            Assert.Equal(fingerprint, result.GetProperty("package_fingerprint").GetString());
            Assert.False(result.GetProperty("source_mutated").GetBoolean());
            Assert.True(result.GetProperty("output_created").GetBoolean());
            Assert.Equal(2, result.GetProperty("artifact_count").GetInt32());
            Assert.Equal(2, result.GetProperty("exported_page_count").GetInt32());
            Assert.False(
                result.GetProperty("safety")
                    .GetProperty("silent_backend_fallback")
                    .GetBoolean()
            );

            var pdf = Path.Combine(output, "proof.pdf");
            var manifest = Path.Combine(output, "proof.render.json");
            Assert.True(File.Exists(pdf));
            Assert.True(File.Exists(manifest));
            Assert.True(File.ReadAllBytes(pdf).AsSpan().StartsWith("%PDF-"u8));
            Assert.Equal(before, File.ReadAllBytes(source));
            Assert.Empty(
                Directory.EnumerateDirectories(
                    output,
                    ".wordtoolkit-fixed-render-*"
                )
            );
            Assert.True(host.Application.Documents.LastOpenedDocument!.CloseCalled);
            Assert.Equal(3, host.Application.Documents.AutomationSecurityDuringOpen);
            Assert.False(host.Application.Documents.UpdateLinksAtOpenDuringOpen);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task McpEnvelopeConformsToPublishedClosedOutputSchema()
    {
        var directory = TemporaryDirectory();
        try
        {
            var source = Path.Combine(directory, "source.docx");
            var outputDirectory = Path.Combine(directory, "output");
            Directory.CreateDirectory(outputDirectory);
            CreatePackage(source);
            var fingerprint = new OpcPackageReader().Read(source).Fingerprint;
            var request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "tools/call",
                ["params"] = new JsonObject
                {
                    ["name"] = "execute_wordtoolkit_action",
                    ["arguments"] = new JsonObject
                    {
                        ["action"] = "render_ooxml_fixed_artifacts",
                        ["arguments"] = new JsonObject
                        {
                            ["local_path"] = source,
                            ["expected_package_fingerprint"] = fingerprint,
                            ["output_directory"] = outputDirectory,
                            ["artifact_stem"] = "mcp_proof",
                            ["output"] = "pdf",
                        },
                        ["response_mode"] = "full",
                    },
                },
            };
            using var responseWriter = new StringWriter();
            await using var host = new FixedFormatFakeHost(pageCount: 2);
            var catalog = ToolCatalog.LoadNativeWordTools();
            var server = new McpServer(
                new StringReader(request.ToJsonString(JsonDefaults.Compact) + Environment.NewLine),
                responseWriter,
                catalog,
                new WordLiveService(host)
            );

            await server.RunAsync();

            var response = JsonNode.Parse(responseWriter.ToString().Trim())!.AsObject();
            var structured = response["result"]!["structuredContent"]!;
            var schema = catalog.InspectAction("render_ooxml_fixed_artifacts")["tool"]![
                "outputSchema"
            ]!.AsObject();
            PublishedOutputSchemaAssertions.AssertConforms(structured, schema, schema);
            var data = structured["data"]!.AsObject();
            Assert.Equal(0, data["page_geometry_count"]!.GetValue<int>());
            Assert.Empty(data["page_geometries"]!.AsArray());
            Assert.False(data["backend"]!["pdf_geometry_inspected"]!.GetValue<bool>());
            Assert.True(data["execution"]!["all_resolved"]!.GetValue<bool>());
            Assert.False(data["execution"]!["silent_fallback"]!.GetValue<bool>());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task StaleFingerprintFailsBeforeWordAndCreatesNothing()
    {
        var directory = TemporaryDirectory();
        try
        {
            var source = Path.Combine(directory, "source.docx");
            var output = Path.Combine(directory, "output");
            Directory.CreateDirectory(output);
            CreatePackage(source);
            await using var host = new FixedFormatFakeHost(pageCount: 1);
            var service = new WordLiveService(host);
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    new
                    {
                        local_path = source,
                        expected_package_fingerprint = new string('0', 64),
                        output_directory = output,
                        artifact_stem = "proof",
                    }
                )
            );

            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "render_ooxml_fixed_artifacts",
                    arguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("VERSION_CONFLICT", exception.ErrorCode);
            Assert.Null(host.Application.Documents.LastOpenedDocument);
            Assert.Empty(Directory.EnumerateFileSystemEntries(output));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingOutputRollsBackEveryNewArtifact()
    {
        var directory = TemporaryDirectory();
        try
        {
            var source = Path.Combine(directory, "source.docx");
            var output = Path.Combine(directory, "output");
            Directory.CreateDirectory(output);
            CreatePackage(source);
            var fingerprint = new OpcPackageReader().Read(source).Fingerprint;
            var existing = Path.Combine(output, "proof.pdf");
            await File.WriteAllTextAsync(existing, "keep");
            await using var host = new FixedFormatFakeHost(pageCount: 1);
            var service = new WordLiveService(host);
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    new
                    {
                        local_path = source,
                        expected_package_fingerprint = fingerprint,
                        output_directory = output,
                        artifact_stem = "proof",
                    }
                )
            );

            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "render_ooxml_fixed_artifacts",
                    arguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("OUTPUT_EXISTS", exception.ErrorCode);
            Assert.Equal("keep", await File.ReadAllTextAsync(existing));
            Assert.False(File.Exists(Path.Combine(output, "proof.render.json")));
            Assert.Empty(
                Directory.EnumerateDirectories(
                    output,
                    ".wordtoolkit-fixed-render-*"
                )
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    internal static void CreatePackage(string path)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        Add(
            archive,
            "[Content_Types].xml",
            $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="{ContentTypesNamespace}"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>"""
        );
        Add(
            archive,
            "_rels/.rels",
            $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="{PackageRelationshipsNamespace}"><Relationship Id="rId1" Type="{OfficeRelationshipsNamespace}/officeDocument" Target="word/document.xml"/></Relationships>"""
        );
        Add(
            archive,
            "word/document.xml",
            $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="{WordNamespace}"><w:body><w:p><w:r><w:t>render proof</w:t></w:r></w:p><w:sectPr/></w:body></w:document>"""
        );
    }

    private static void Add(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-fixed-render-service-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(path);
        return path;
    }
}

internal sealed class FixedFormatFakeHost(int pageCount) : IWordComHost
{
    public FixedFormatFakeApplication Application { get; } = new(pageCount);

    public Task<T> InvokeAsync<T>(
        Func<dynamic, T> operation,
        CancellationToken cancellationToken = default,
        bool launchIfMissing = false
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(operation(Application));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
