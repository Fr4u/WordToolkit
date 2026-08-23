using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class SignatureInspectionTests
{
    [Fact]
    public void CatalogKeepsSignatureInspectionLazyAndPublishesClosedMetadata()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        Assert.Equal(151, catalog.ActionCount);
        Assert.DoesNotContain(
            catalog.Tools,
            tool => tool!["name"]!.GetValue<string>()
                == InspectOoxmlSignaturesContract.OperationName
        );
        var tool = catalog.InspectAction(
            InspectOoxmlSignaturesContract.OperationName
        )["tool"]!.AsObject();

        Assert.Equal("1.0", tool["operationVersion"]!.GetValue<string>());
        Assert.False(tool["inputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.False(tool["outputSchema"]!["additionalProperties"]!.GetValue<bool>());
        var data = tool["outputSchema"]!["properties"]!["data"]!;
        Assert.Equal(
            InspectOoxmlSignaturesContract.Contract,
            data["properties"]!["operation_contract"]!["const"]!.GetValue<string>()
        );
        Assert.False(
            data["properties"]!["certificate_chain_trust_verified"]!["const"]!
                .GetValue<bool>()
        );
        Assert.False(
            data["properties"]!["revocation_checked"]!["const"]!.GetValue<bool>()
        );
        Assert.False(
            data["properties"]!["security"]!["properties"]![
                "returns_certificate_identity"
            ]!["const"]!.GetValue<bool>()
        );
    }

    [Fact]
    public async Task NativeServiceInspectsAnUnsignedPackageWithoutInvokingWord()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "unsigned.docx");
            CreateUnsignedPackage(path);
            var host = new NoInvokeHost();
            var service = new WordLiveService(host);
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path })
            );

            var result = await service.CallAsync(
                InspectOoxmlSignaturesContract.OperationName,
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(
                JsonSerializer.Serialize(result, JsonDefaults.Compact)
            );
            var root = json.RootElement;

            Assert.Equal(0, host.InvocationCount);
            Assert.Equal(
                InspectOoxmlSignaturesContract.Contract,
                root.GetProperty("operation_contract").GetString()
            );
            Assert.Equal(0, root.GetProperty("signature_count").GetInt32());
            Assert.False(root.GetProperty("signature_origin_declared").GetBoolean());
            Assert.False(
                root.GetProperty("cryptographic_integrity_validation_performed")
                    .GetBoolean()
            );
            Assert.False(root.GetProperty("certificate_chain_trust_verified").GetBoolean());
            Assert.False(root.GetProperty("revocation_checked").GetBoolean());
            Assert.False(
                root.GetProperty("security").GetProperty("uses_network").GetBoolean()
            );
            Assert.Equal("dotnet-native", root.GetProperty("runtime").GetString());
            Assert.False(root.GetProperty("python_used").GetBoolean());

            using var invalid = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path, trust_signer = true })
            );
            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    InspectOoxmlSignaturesContract.OperationName,
                    invalid.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_INPUT", exception.ErrorCode);
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CliIsPagedStrictAndDoesNotReturnTheLocalPath()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "unsigned.docx");
            CreateUnsignedPackage(path);
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = InspectSignaturesCli.Run(
                [path, "--view", "signatures", "--limit", "1", "--format", "json"],
                output,
                error
            );

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, error.ToString());
            Assert.DoesNotContain(directory, output.ToString(), StringComparison.OrdinalIgnoreCase);
            using var json = JsonDocument.Parse(output.ToString());
            Assert.Equal(
                InspectOoxmlSignaturesContract.Contract,
                json.RootElement.GetProperty("operation_contract").GetString()
            );
            Assert.Equal("signatures", json.RootElement.GetProperty("view").GetString());
            Assert.Equal(1, json.RootElement.GetProperty("paging").GetProperty("limit").GetInt32());

            output.GetStringBuilder().Clear();
            error.GetStringBuilder().Clear();
            exitCode = InspectSignaturesCli.Run(
                [path, "--trust-signer"],
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
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CreateUnsignedPackage(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
            """
        );
        AddEntry(
            archive,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
            """
        );
        AddEntry(
            archive,
            "word/document.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p/><w:sectPr/></w:body></w:document>
            """
        );
    }

    private static void AddEntry(ZipArchive archive, string name, string value)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(value));
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-signatures-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
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
                "OOXML signature inspection must not invoke Microsoft Word."
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
