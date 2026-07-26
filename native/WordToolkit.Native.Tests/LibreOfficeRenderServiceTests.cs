using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Observability;
using WordToolkit.Engine.Resources;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class LibreOfficeRenderServiceTests
{
    [Fact]
    public async Task PublishesPdfAndManifestWithoutInvokingMicrosoftWord()
    {
        var directory = TemporaryDirectory();
        try
        {
            var fixture = CreateFixture(directory);
            var host = new NoInvokeHost();
            var renderer = new FakeUnoRenderProvider();
            var service = CreateService(host, renderer);
            using var arguments = Request(fixture, "proof");

            var raw = await service.CallAsync(
                LibreOfficeRenderWordPackageContract.OperationName,
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(
                JsonSerializer.Serialize(raw, JsonDefaults.Compact)
            );
            var result = json.RootElement;

            Assert.Equal(
                LibreOfficeRenderWordPackageContract.Contract,
                result.GetProperty("operation_contract").GetString()
            );
            Assert.Equal(0, host.InvocationCount);
            Assert.Equal(1, renderer.InvocationCount);
            Assert.False(result.GetProperty("source_mutated").GetBoolean());
            Assert.Equal(2, result.GetProperty("artifact_count").GetInt32());
            Assert.Equal(
                "libreoffice_writer_pdf",
                result.GetProperty("backend").GetProperty("primary").GetString()
            );
            Assert.False(
                result.GetProperty("fidelity")
                    .GetProperty("microsoft_word_layout_claimed")
                    .GetBoolean()
            );
            Assert.True(
                result.GetProperty("safety")
                    .GetProperty("public_artifacts_published_after_private_cleanup")
                    .GetBoolean()
            );
            Assert.True(File.Exists(Path.Combine(fixture.OutputDirectory, "proof.pdf")));
            Assert.True(
                File.Exists(Path.Combine(fixture.OutputDirectory, "proof.render.json"))
            );
            Assert.Empty(
                Directory.EnumerateDirectories(
                    fixture.OutputDirectory,
                    ".wordtoolkit-libreoffice-render-*"
                )
            );
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
            var fixture = CreateFixture(directory);
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
                        ["action"] = LibreOfficeRenderWordPackageContract.OperationName,
                        ["arguments"] = JsonNode.Parse(
                            RequestJson(fixture, "mcp_proof", string.Empty)
                        ),
                        ["response_mode"] = "full",
                    },
                },
            };
            using var responseWriter = new StringWriter();
            var host = new NoInvokeHost();
            var catalog = ToolCatalog.LoadNativeWordTools();
            var server = new McpServer(
                new StringReader(
                    request.ToJsonString(JsonDefaults.Compact) + Environment.NewLine
                ),
                responseWriter,
                catalog,
                CreateService(host, new FakeUnoRenderProvider())
            );

            await server.RunAsync();

            var response = JsonNode.Parse(responseWriter.ToString().Trim())!.AsObject();
            var structured = response["result"]!["structuredContent"]!;
            var schema = catalog.InspectAction(
                LibreOfficeRenderWordPackageContract.OperationName
            )["tool"]!["outputSchema"]!.AsObject();
            PublishedOutputSchemaAssertions.AssertConforms(structured, schema, schema);
            var data = structured["data"]!.AsObject();
            Assert.Equal(
                "libreoffice_writer_pdf",
                data["backend"]!["primary"]!.GetValue<string>()
            );
            Assert.False(
                data["fidelity"]!["microsoft_word_layout_claimed"]!.GetValue<bool>()
            );
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task StaleFingerprintFailsBeforeAnyBackendRuns()
    {
        var directory = TemporaryDirectory();
        try
        {
            var fixture = CreateFixture(directory);
            var host = new NoInvokeHost();
            var renderer = new FakeUnoRenderProvider();
            var probe = new FakeProbeProvider();
            var service = CreateService(host, renderer, probe);
            using var arguments = Request(
                fixture with { PackageFingerprint = new string('0', 64) },
                "proof"
            );

            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    LibreOfficeRenderWordPackageContract.OperationName,
                    arguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("VERSION_CONFLICT", exception.ErrorCode);
            Assert.Equal(0, host.InvocationCount);
            Assert.Equal(0, probe.InvocationCount);
            Assert.Equal(0, renderer.InvocationCount);
            Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputDirectory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SourceDriftAfterBackendPreventsPublication()
    {
        var directory = TemporaryDirectory();
        try
        {
            var fixture = CreateFixture(directory);
            var renderer = new FakeUnoRenderProvider(request =>
                File.AppendAllText(request.SourcePath, "drift", Encoding.UTF8)
            );
            var service = CreateService(new NoInvokeHost(), renderer);
            using var arguments = Request(fixture, "proof");

            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    LibreOfficeRenderWordPackageContract.OperationName,
                    arguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("VERSION_CONFLICT", exception.ErrorCode);
            Assert.False(File.Exists(Path.Combine(fixture.OutputDirectory, "proof.pdf")));
            Assert.False(
                File.Exists(Path.Combine(fixture.OutputDirectory, "proof.render.json"))
            );
            Assert.Empty(
                Directory.EnumerateDirectories(
                    fixture.OutputDirectory,
                    ".wordtoolkit-libreoffice-render-*"
                )
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingOutputCausesTransactionalPublicRollback()
    {
        var directory = TemporaryDirectory();
        try
        {
            var fixture = CreateFixture(directory);
            var existing = Path.Combine(fixture.OutputDirectory, "proof.pdf");
            await File.WriteAllTextAsync(existing, "keep");
            var service = CreateService(
                new NoInvokeHost(),
                new FakeUnoRenderProvider()
            );
            using var arguments = Request(fixture, "proof");

            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    LibreOfficeRenderWordPackageContract.OperationName,
                    arguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("OUTPUT_EXISTS", exception.ErrorCode);
            Assert.Equal("keep", await File.ReadAllTextAsync(existing));
            Assert.False(
                File.Exists(Path.Combine(fixture.OutputDirectory, "proof.render.json"))
            );
            Assert.Empty(
                Directory.EnumerateDirectories(
                    fixture.OutputDirectory,
                    ".wordtoolkit-libreoffice-render-*"
                )
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task UnknownArgumentsAreRejectedWithoutBackendExecution()
    {
        var directory = TemporaryDirectory();
        try
        {
            var fixture = CreateFixture(directory);
            var renderer = new FakeUnoRenderProvider();
            var service = CreateService(new NoInvokeHost(), renderer);
            using var arguments = JsonDocument.Parse(
                RequestJson(fixture, "proof", "\"unknown\":true,")
            );

            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    LibreOfficeRenderWordPackageContract.OperationName,
                    arguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("INVALID_INPUT", exception.ErrorCode);
            Assert.Equal(0, renderer.InvocationCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RealLinuxPublicActionPublishesPdfPngGeometryAndManifestForEveryWordPackageKind()
    {
        var office = Environment.GetEnvironmentVariable(
            "WORDTOOLKIT_TEST_LIBREOFFICE_UNO_PATH"
        );
        var java = Environment.GetEnvironmentVariable(
            "WORDTOOLKIT_TEST_LIBREOFFICE_UNO_JAVA_PATH"
        );
        var libreOfficeJar = Environment.GetEnvironmentVariable(
            "WORDTOOLKIT_TEST_LIBREOFFICE_UNO_JAR_PATH"
        );
        var source = Environment.GetEnvironmentVariable(
            "WORDTOOLKIT_TEST_LIBREOFFICE_UNO_SOURCE_PATH"
        );
        var pdfInfo = Environment.GetEnvironmentVariable(
            "WORDTOOLKIT_TEST_PDFINFO_PATH"
        );
        var rasterizer = Environment.GetEnvironmentVariable(
            "WORDTOOLKIT_TEST_PDF_RASTERIZER_PATH"
        );
        if (new[] { office, java, libreOfficeJar, source, pdfInfo, rasterizer }
            .Any(string.IsNullOrWhiteSpace))
        {
            return;
        }

        var directory = TemporaryDirectory();
        try
        {
            var host = new NoInvokeHost();
            var service = CreateService(
                host,
                new WordToolkit.LibreOffice.LibreOfficeUnoRenderProvider(),
                new WordToolkit.LibreOffice.LibreOfficeBackendProbeProvider()
            );
            var corpus = new[]
            {
                (Extension: ".docx", MainContentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"),
                (Extension: ".docm", MainContentType: "application/vnd.ms-word.document.macroEnabled.main+xml"),
                (Extension: ".dotx", MainContentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml"),
                (Extension: ".dotm", MainContentType: "application/vnd.ms-word.template.macroEnabledTemplate.main+xml"),
            };

            foreach (var item in corpus)
            {
                var sourcePath = Path.Combine(directory, "source" + item.Extension);
                var outputDirectory = Path.Combine(
                    directory,
                    "output-" + item.Extension[1..]
                );
                Directory.CreateDirectory(outputDirectory);
                CreateOnePagePackage(sourcePath, item.MainContentType);
                var before = File.ReadAllBytes(sourcePath);
                var fingerprint = new OpcPackageReader().Read(sourcePath).Fingerprint;
                var stem = "real_linux_" + item.Extension[1..];
                using var arguments = JsonDocument.Parse(
                    JsonSerializer.Serialize(
                        new
                        {
                            local_path = sourcePath,
                            expected_package_fingerprint = fingerprint,
                            output_directory = outputDirectory,
                            artifact_stem = stem,
                            libreoffice_executable_path = office,
                            expected_libreoffice_executable_sha256 = RequiredEnvironment(
                                "WORDTOOLKIT_TEST_LIBREOFFICE_UNO_SHA256"
                            ),
                            java_executable_path = java,
                            expected_java_executable_sha256 = RequiredEnvironment(
                                "WORDTOOLKIT_TEST_LIBREOFFICE_UNO_JAVA_SHA256"
                            ),
                            libreoffice_jar_path = libreOfficeJar,
                            expected_libreoffice_jar_sha256 = RequiredEnvironment(
                                "WORDTOOLKIT_TEST_LIBREOFFICE_UNO_JAR_SHA256"
                            ),
                            output = "pdf_and_png_pages",
                            dpi = 96,
                            pdfinfo_path = pdfInfo,
                            rasterizer_path = rasterizer,
                            timeout_milliseconds = 60_000,
                        }
                    )
                );

                var raw = await service.CallAsync(
                    LibreOfficeRenderWordPackageContract.OperationName,
                    arguments.RootElement,
                    CancellationToken.None
                );
                using var result = JsonDocument.Parse(
                    JsonSerializer.Serialize(raw, JsonDefaults.Compact)
                );

                AssertRealRasterBundle(
                    result.RootElement,
                    sourcePath,
                    outputDirectory,
                    stem,
                    before
                );
            }

            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertRealRasterBundle(
        JsonElement result,
        string sourcePath,
        string outputDirectory,
        string stem,
        byte[] sourceBefore
    )
    {
        Assert.Equal("pdf_and_png_pages", result.GetProperty("output").GetString());
        Assert.Equal(Path.GetFileName(sourcePath), result.GetProperty("source_file_name").GetString());
        Assert.False(result.GetProperty("source_mutated").GetBoolean());
        Assert.Equal(96, result.GetProperty("dpi").GetInt32());
        Assert.Equal(
            "libreoffice_writer_pdf",
            result.GetProperty("backend").GetProperty("primary").GetString()
        );
        Assert.Equal(
            "poppler",
            result.GetProperty("backend").GetProperty("rasterizer").GetString()
        );
        Assert.True(
            result.GetProperty("backend").GetProperty("pdf_geometry_inspected").GetBoolean()
        );
        Assert.True(
            result.GetProperty("fidelity").GetProperty("png_is_derived_from_exact_pdf").GetBoolean()
        );
        Assert.False(
            result.GetProperty("fidelity").GetProperty("microsoft_word_layout_claimed").GetBoolean()
        );
        Assert.True(
            result.GetProperty("cleanup").GetProperty("private_workspace_deleted").GetBoolean()
        );
        Assert.True(
            result.GetProperty("safety")
                .GetProperty("public_artifacts_published_after_private_cleanup")
                .GetBoolean()
        );
        Assert.False(
            result.GetProperty("safety")
                .GetProperty("macro_prevention_behaviorally_verified")
                .GetBoolean()
        );
        Assert.False(
            result.GetProperty("safety")
                .GetProperty("external_update_prevention_behaviorally_verified")
                .GetBoolean()
        );

        var pageCount = result.GetProperty("exported_page_count").GetInt32();
        Assert.True(pageCount >= 1);
        Assert.Equal(pageCount, result.GetProperty("page_geometry_count").GetInt32());
        var geometries = result.GetProperty("page_geometries").EnumerateArray().ToArray();
        Assert.Equal(pageCount, geometries.Length);
        for (var index = 0; index < geometries.Length; index++)
        {
            Assert.Equal(index + 1, geometries[index].GetProperty("exported_page_number").GetInt32());
            Assert.True(geometries[index].GetProperty("width_points").GetDouble() > 0);
            Assert.True(geometries[index].GetProperty("height_points").GetDouble() > 0);
        }

        var artifacts = result.GetProperty("artifacts").EnumerateArray().ToArray();
        Assert.Equal(pageCount + 2, artifacts.Length);
        Assert.Equal(artifacts.Length, result.GetProperty("artifact_count").GetInt32());
        Assert.Single(artifacts, artifact => artifact.GetProperty("format").GetString() == "pdf");
        Assert.Single(artifacts, artifact => artifact.GetProperty("format").GetString() == "json");
        Assert.Equal(
            pageCount,
            artifacts.Count(artifact => artifact.GetProperty("format").GetString() == "png")
        );
        foreach (var artifact in artifacts)
        {
            var path = artifact.GetProperty("output_path").GetString();
            Assert.NotNull(path);
            Assert.True(File.Exists(path));
            Assert.Equal(new FileInfo(path!).Length, artifact.GetProperty("bytes").GetInt64());
            Assert.Equal(Sha256(path), artifact.GetProperty("sha256").GetString());
            Assert.Equal("published", artifact.GetProperty("state").GetString());
        }

        var pdfPath = Path.Combine(outputDirectory, stem + ".pdf");
        var pngPath = Path.Combine(outputDirectory, stem + "-page-0001.png");
        var manifestPath = Path.Combine(outputDirectory, stem + ".render.json");
        Assert.True(File.ReadAllBytes(pdfPath).AsSpan().StartsWith("%PDF-"u8));
        Assert.True(
            File.ReadAllBytes(pngPath)
                .AsSpan()
                .StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
        );
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        Assert.Equal(
            LibreOfficeRenderWordPackageContract.Contract,
            manifest.RootElement.GetProperty("operation_contract").GetString()
        );
        Assert.Equal(
            Path.GetFileName(sourcePath),
            manifest.RootElement.GetProperty("source_file_name").GetString()
        );
        Assert.Equal(pageCount, manifest.RootElement.GetProperty("pdf_page_count").GetInt32());
        Assert.Equal(pageCount + 1, manifest.RootElement.GetProperty("artifacts").GetArrayLength());
        Assert.False(
            manifest.RootElement.GetProperty("macro_prevention_behaviorally_verified").GetBoolean()
        );
        Assert.False(
            manifest.RootElement.GetProperty("external_update_prevention_behaviorally_verified").GetBoolean()
        );
        Assert.Equal(sourceBefore, File.ReadAllBytes(sourcePath));
        Assert.Empty(
            Directory.EnumerateDirectories(
                outputDirectory,
                ".wordtoolkit-libreoffice-render-*"
            )
        );
        Assert.Empty(
            Directory.EnumerateDirectories(outputDirectory, ".wordtoolkit-poppler-*")
        );
    }

    private static void CreateOnePagePackage(string path, string mainContentType)
    {
        const string contentTypesNamespace =
            "http://schemas.openxmlformats.org/package/2006/content-types";
        const string packageRelationshipsNamespace =
            "http://schemas.openxmlformats.org/package/2006/relationships";
        const string officeRelationshipsNamespace =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string wordNamespace =
            "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        AddZipEntry(
            archive,
            "[Content_Types].xml",
            $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="{contentTypesNamespace}"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="{mainContentType}"/></Types>"""
        );
        AddZipEntry(
            archive,
            "_rels/.rels",
            $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="{packageRelationshipsNamespace}"><Relationship Id="rId1" Type="{officeRelationshipsNamespace}/officeDocument" Target="word/document.xml"/></Relationships>"""
        );
        AddZipEntry(
            archive,
            "word/document.xml",
            $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="{wordNamespace}"><w:body><w:p><w:r><w:t>WordToolkit LibreOffice raster proof.</w:t></w:r></w:p><w:sectPr><w:pgSz w:w="12240" w:h="15840"/></w:sectPr></w:body></w:document>"""
        );
    }

    private static void AddZipEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    private static WordLiveService CreateService(
        IWordComHost host,
        ILibreOfficeUnoRenderProvider renderer,
        ILibreOfficeBackendProbeProvider? probe = null
    ) => new(
        host,
        () => new WordOperationResourceLease(),
        WordOperationObservability.Disabled,
        probe ?? new FakeProbeProvider(),
        renderer
    );

    private static RenderFixture CreateFixture(string directory)
    {
        var source = Path.Combine(directory, "source.docx");
        var outputDirectory = Path.Combine(directory, "output");
        Directory.CreateDirectory(outputDirectory);
        WordFixedRenderServiceTests.CreatePackage(source);
        var libreOffice = CreateBinary(directory, "soffice", "libreoffice");
        var java = CreateBinary(directory, "java", "java");
        var jar = CreateBinary(directory, "libreoffice.jar", "uno");
        return new RenderFixture(
            source,
            new OpcPackageReader().Read(source).Fingerprint,
            outputDirectory,
            libreOffice,
            Sha256(libreOffice),
            java,
            Sha256(java),
            jar,
            Sha256(jar)
        );
    }

    private static string CreateBinary(string directory, string name, string content)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }

    private static JsonDocument Request(RenderFixture fixture, string stem) =>
        JsonDocument.Parse(RequestJson(fixture, stem, string.Empty));

    private static string RequestJson(
        RenderFixture fixture,
        string stem,
        string additionalProperty
    ) => $$"""
        {
          {{additionalProperty}}
          "local_path": {{JsonSerializer.Serialize(fixture.Source)}},
          "expected_package_fingerprint": "{{fixture.PackageFingerprint}}",
          "output_directory": {{JsonSerializer.Serialize(fixture.OutputDirectory)}},
          "artifact_stem": "{{stem}}",
          "libreoffice_executable_path": {{JsonSerializer.Serialize(fixture.LibreOffice)}},
          "expected_libreoffice_executable_sha256": "{{fixture.LibreOfficeSha256}}",
          "java_executable_path": {{JsonSerializer.Serialize(fixture.Java)}},
          "expected_java_executable_sha256": "{{fixture.JavaSha256}}",
          "libreoffice_jar_path": {{JsonSerializer.Serialize(fixture.LibreOfficeJar)}},
          "expected_libreoffice_jar_sha256": "{{fixture.LibreOfficeJarSha256}}",
          "output": "pdf"
        }
        """;

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-libreoffice-render-test-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string RequiredEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(value), $"{name} is required");
        return value!;
    }

    private sealed record RenderFixture(
        string Source,
        string PackageFingerprint,
        string OutputDirectory,
        string LibreOffice,
        string LibreOfficeSha256,
        string Java,
        string JavaSha256,
        string LibreOfficeJar,
        string LibreOfficeJarSha256
    );

    private sealed class FakeProbeProvider : ILibreOfficeBackendProbeProvider
    {
        public int InvocationCount { get; private set; }

        public Task<LibreOfficeBackendProbeObservation> ProbeAsync(
            LibreOfficeBackendProbeProviderRequest request,
            CancellationToken cancellationToken = default
        )
        {
            InvocationCount++;
            return Task.FromResult(
                new LibreOfficeBackendProbeObservation(
                    "LibreOffice",
                    "24.2.7.2",
                    "LibreOffice 24.2.7.2",
                    Path.GetFileName(request.ExecutablePath),
                    new FileInfo(request.ExecutablePath).Length,
                    request.ExpectedExecutableSha256 ?? Sha256(request.ExecutablePath),
                    true,
                    "linux",
                    "x64",
                    "x64"
                )
            );
        }
    }

    private sealed class FakeUnoRenderProvider(Action<LibreOfficeUnoRenderProviderRequest>? afterWrite = null)
        : ILibreOfficeUnoRenderProvider
    {
        private static readonly byte[] Pdf = "%PDF-1.4\n%%EOF\n"u8.ToArray();

        public int InvocationCount { get; private set; }

        public Task<LibreOfficeUnoRenderObservation> RenderAsync(
            LibreOfficeUnoRenderProviderRequest request,
            CancellationToken cancellationToken = default
        )
        {
            InvocationCount++;
            File.WriteAllBytes(request.OutputPdfPath, Pdf);
            afterWrite?.Invoke(request);
            var pdfSha = Convert.ToHexString(SHA256.HashData(Pdf)).ToLowerInvariant();
            return Task.FromResult(
                new LibreOfficeUnoRenderObservation(
                    LibreOfficeUnoRenderContract.ProviderContract,
                    Identity(request.LibreOfficeExecutablePath, request.ExpectedLibreOfficeExecutableSha256),
                    Identity(request.JavaExecutablePath, request.ExpectedJavaExecutableSha256),
                    Identity(request.LibreOfficeJarPath, request.ExpectedLibreOfficeJarSha256),
                    new LibreOfficeUnoBinaryIdentity(
                        "wordtoolkit-uno-helper.jar",
                        9_012,
                        "583ef85be3e0e9282cd1aec06161767606d1c5b9ce91228587fa8f14e57ad462",
                        true,
                        true
                    ),
                    request.ExpectedSourceSha256,
                    true,
                    new LibreOfficeUnoDocumentPolicyEvidence(
                        true,
                        true,
                        true,
                        true,
                        true,
                        true,
                        false,
                        true,
                        false,
                        request.InputFilterName,
                        true
                    ),
                    new LibreOfficeUnoExportEvidence(
                        true,
                        true,
                        true,
                        true,
                        true,
                        true,
                        request.FirstPage,
                        request.LastPage,
                        request.PdfA1b,
                        request.ExportBookmarks,
                        Pdf.LongLength,
                        pdfSha
                    ),
                    new LibreOfficeUnoCleanupEvidence(
                        true,
                        true,
                        true,
                        true,
                        false,
                        true,
                        true
                    ),
                    "linux",
                    "x64",
                    "x64",
                    ["libreoffice_layout_not_microsoft_word_layout"]
                )
            );
        }

        private static LibreOfficeUnoBinaryIdentity Identity(string path, string hash) =>
            new(Path.GetFileName(path), new FileInfo(path).Length, hash, true, true);
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
            throw new Xunit.Sdk.XunitException(
                "LibreOffice rendering must not invoke Microsoft Word"
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
