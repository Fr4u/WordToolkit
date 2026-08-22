using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class InspectPackageCliTests
{
    [Fact]
    public async Task EngineCliAndMcpReturnTheSameCanonicalDataWithoutWord()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "parity.docx");
            CreatePackage(path, externalTarget: "https://private.example/hidden");
            var before = SHA256.HashData(File.ReadAllBytes(path));
            var engineResult = new InspectWordPackageOperation().Execute(
                new InspectWordPackageRequest(path, IncludeDetails: true, MaxItems: 20)
            );
            var engineJson = WordToolkitOperationJson.Serialize(engineResult);

            var output = new StringWriter();
            var error = new StringWriter();
            var exitCode = InspectPackageCli.Run(
                [path, "--include-details", "--max-items", "20", "--format", "json"],
                output,
                error
            );
            var cliJson = JsonNode.Parse(output.ToString())!.ToJsonString(JsonDefaults.Compact);

            var host = new NoInvokeHost();
            var mcpResponse = await CallMcpAsync(
                host,
                path,
                includeDetails: true,
                maxItems: 20
            );
            var structured = mcpResponse
                .GetProperty("result")
                .GetProperty("structuredContent");
            var mcpJson = JsonNode
                .Parse(structured.GetProperty("data").GetRawText())!
                .ToJsonString(JsonDefaults.Compact);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, error.ToString());
            Assert.Equal(engineJson, cliJson);
            Assert.Equal(engineJson, mcpJson);
            Assert.True(structured.GetProperty("ok").GetBoolean());
            Assert.False(mcpResponse.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Equal(0, host.InvocationCount);
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task EngineCliAndMcpExposeTheSameStableErrorCodes()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var validPath = Path.Combine(directory, "valid.docx");
            CreatePackage(validPath);
            var corruptPath = Path.Combine(directory, "corrupt.docx");
            File.WriteAllText(corruptPath, "not a ZIP package");
            var unsupportedPath = Path.Combine(directory, "unsupported.txt");
            File.Copy(validPath, unsupportedPath);
            var limitedPath = Path.Combine(directory, "limited.docx");
            CreatePackage(limitedPath, extraEntries: 20_001);

            await AssertErrorParityAsync(
                Path.Combine(directory, "missing.docx"),
                maxItems: 20,
                expectedCode: "NOT_FOUND",
                expectedExitCode: 66
            );
            await AssertErrorParityAsync(
                unsupportedPath,
                maxItems: 20,
                expectedCode: "INVALID_INPUT",
                expectedExitCode: 64
            );
            await AssertErrorParityAsync(
                corruptPath,
                maxItems: 20,
                expectedCode: "INVALID_PACKAGE",
                expectedExitCode: 65
            );
            await AssertErrorParityAsync(
                validPath,
                maxItems: 0,
                expectedCode: "INVALID_INPUT",
                expectedExitCode: 64
            );
            await AssertErrorParityAsync(
                limitedPath,
                maxItems: 20,
                expectedCode: "PACKAGE_LIMIT",
                expectedExitCode: 65
            );

            using var lockStream = new FileStream(
                validPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None
            );
            await AssertErrorParityAsync(
                validPath,
                maxItems: 20,
                expectedCode: "IO_ERROR",
                expectedExitCode: 74
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CliHelpAndParserErrorsUseStableChannels()
    {
        var helpOutput = new StringWriter();
        var helpError = new StringWriter();
        var helpExit = InspectPackageCli.Run(["--help"], helpOutput, helpError);
        Assert.Equal(0, helpExit);
        Assert.Contains("inspect-package", helpOutput.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, helpError.ToString());

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = InspectPackageCli.Run(
            ["sample.docx", "--max-items", "not-a-number", "--format", "json"],
            output,
            error
        );
        Assert.Equal(64, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        using var errorJson = JsonDocument.Parse(error.ToString());
        Assert.Equal(
            "INVALID_INPUT",
            errorJson.RootElement.GetProperty("error").GetProperty("code").GetString()
        );
    }

    [Fact]
    public void CliMapsAChangingSourceToTheRetryableTemporaryFailureExitCode()
    {
        Assert.Equal(75, InspectPackageCli.ExitCode("SOURCE_CHANGED"));
    }

    [Fact]
    public async Task McpRejectsUnknownArgumentsDeclaredClosedByTheSchema()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "closed-arguments.docx");
            CreatePackage(path);
            var host = new NoInvokeHost();
            var response = await CallMcpAsync(
                host,
                path,
                includeDetails: false,
                maxItems: 20,
                additionalArguments: new JsonObject { ["include_detailz"] = true }
            );
            var structured = response
                .GetProperty("result")
                .GetProperty("structuredContent");

            Assert.True(response.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Equal(
                "INVALID_INPUT",
                structured.GetProperty("error").GetProperty("code").GetString()
            );
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task AssertErrorParityAsync(
        string path,
        long maxItems,
        string expectedCode,
        int expectedExitCode
    )
    {
        var engineError = Assert.Throws<WordToolkitOperationException>(() =>
            new InspectWordPackageOperation().Execute(
                new InspectWordPackageRequest(path, MaxItems: maxItems)
            )
        );

        var output = new StringWriter();
        var error = new StringWriter();
        var cliExit = InspectPackageCli.Run(
            [path, "--max-items", maxItems.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            output,
            error
        );
        using var cliJson = JsonDocument.Parse(error.ToString());
        var cliCode = cliJson.RootElement
            .GetProperty("error")
            .GetProperty("code")
            .GetString();

        var host = new NoInvokeHost();
        var mcpResponse = await CallMcpAsync(host, path, includeDetails: false, maxItems);
        var structured = mcpResponse
            .GetProperty("result")
            .GetProperty("structuredContent");
        var mcpCode = structured
            .GetProperty("error")
            .GetProperty("code")
            .GetString();

        Assert.Equal(expectedCode, engineError.Code);
        Assert.Equal(expectedCode, cliCode);
        Assert.Equal(expectedCode, mcpCode);
        Assert.Equal(expectedExitCode, cliExit);
        Assert.Equal(string.Empty, output.ToString());
        Assert.True(mcpResponse.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Equal(0, host.InvocationCount);
    }

    private static async Task<JsonElement> CallMcpAsync(
        NoInvokeHost host,
        string path,
        bool includeDetails,
        long maxItems,
        JsonObject? additionalArguments = null
    )
    {
        var toolArguments = new JsonObject
        {
            ["local_path"] = path,
            ["include_details"] = includeDetails,
            ["max_items"] = maxItems,
        };
        if (additionalArguments is not null)
        {
            foreach (var property in additionalArguments)
            {
                toolArguments[property.Key] = property.Value?.DeepClone();
            }
        }
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = "inspect_ooxml_package",
                ["arguments"] = toolArguments,
            },
        };
        var output = new StringWriter();
        var server = new McpServer(
            new StringReader(request.ToJsonString(JsonDefaults.Compact) + Environment.NewLine),
            output,
            ToolCatalog.LoadNativeWordTools(),
            new WordLiveService(host)
        );
        await server.RunAsync();
        using var document = JsonDocument.Parse(output.ToString().Trim());
        return document.RootElement.Clone();
    }

    private static void CreatePackage(
        string path,
        string? externalTarget = null,
        int extraEntries = 0
    )
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "[Content_Types].xml",
            """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
              <Default Extension="xml" ContentType="application/xml" />
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" />
            </Types>
            """
        );
        WriteEntry(
            archive,
            "_rels/.rels",
            $"""
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="{WordPackageConformance.TransitionalOfficeDocumentRelationship}" Target="word/document.xml" />
            </Relationships>
            """
        );
        WriteEntry(
            archive,
            "word/document.xml",
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p /></w:body></w:document>
            """
        );
        if (externalTarget is not null)
        {
            WriteEntry(
                archive,
                "word/_rels/document.xml.rels",
                $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdExternal" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="{externalTarget}" TargetMode="External" />
                </Relationships>
                """
            );
        }
        for (var index = 0; index < extraEntries; index++)
        {
            WriteEntry(archive, $"custom/empty-{index:D5}.bin", "");
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(Encoding.UTF8.GetBytes(content));
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-inspect-cli-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        return directory;
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
                "Package inspection must not invoke or launch Microsoft Word."
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
