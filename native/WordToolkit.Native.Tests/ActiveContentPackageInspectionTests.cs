using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Resources;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class ActiveContentPackageInspectionTests
{
    private const string SecretProgramId = "Secret.Application.42";
    private const string SecretControlName = "SECRET-CONTROL-NAME";
    private const string SecretClassId = "{11111111-2222-3333-4444-555555555555}";
    private const string SecretTarget = "https://secret.example.invalid/private-object";
    private const string SecretLicense = "TOP-SECRET-ACTIVEX-LICENSE";
    private const string SecretPropertyValue = "TOP-SECRET-ACTIVEX-PROPERTY";
    private const string SecretFieldCode = "TOP-SECRET-FIELD-CODE";
    private const string SecretBinary = "TOP-SECRET-ACTIVEX-BINARY";

    [Fact]
    public async Task DefaultInspectionIsCompactRedactedAndNeverInvokesWordOrPayloads()
    {
        var fixture = CreateTemporaryPackage();
        try
        {
            var host = new NoInvokeHost();
            var service = new WordLiveService(host);
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = fixture.Path })
            );

            var result = await service.CallAsync(
                "inspect_ooxml_active_content",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;
            var raw = root.GetRawText();

            Assert.Equal("summary", root.GetProperty("view").GetString());
            Assert.True(root.GetProperty("main_document_macro_enabled").GetBoolean());
            Assert.Equal(3, root.GetProperty("declaration_count").GetInt32());
            Assert.Equal(1, root.GetProperty("control_count").GetInt32());
            Assert.Equal(5, root.GetProperty("payload_count").GetInt32());
            Assert.Equal(6, root.GetProperty("relationship_count").GetInt32());
            Assert.Equal(1, root.GetProperty("external_relationship_count").GetInt32());
            Assert.False(root.GetProperty("word_opened").GetBoolean());
            Assert.False(root.GetProperty("package_mutated").GetBoolean());
            Assert.False(root.GetProperty("macros_executed").GetBoolean());
            Assert.False(root.GetProperty("binary_payloads_decoded").GetBoolean());
            Assert.False(root.GetProperty("embedded_packages_opened").GetBoolean());
            Assert.False(
                root.GetProperty("cryptographic_signature_validation_performed")
                    .GetBoolean()
            );
            Assert.False(root.GetProperty("external_targets_followed").GetBoolean());
            Assert.False(root.GetProperty("raw_xml_included").GetBoolean());
            Assert.False(root.GetProperty("field_codes_included").GetBoolean());
            Assert.False(root.GetProperty("activex_licenses_included").GetBoolean());
            Assert.False(root.GetProperty("names_included").GetBoolean());
            Assert.False(root.GetProperty("targets_included").GetBoolean());
            Assert.False(root.GetProperty("hashes_included").GetBoolean());
            Assert.False(root.GetProperty("source_included").GetBoolean());
            Assert.Equal(
                "wop1",
                root.GetProperty("operation_budget").GetProperty("model").GetString()
            );
            Assert.True(
                root.GetProperty("operation_budget").GetProperty("used").GetInt64() > 0
            );
            AssertRedacted(raw, fixture.ActiveXBinarySha256);
            Assert.True(
                raw.Length < 5_000,
                $"Default active-content response is too large: {raw.Length} characters"
            );
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            File.Delete(fixture.Path);
        }
    }

    [Fact]
    public async Task NamesTargetsHashesAndSourceRequireIndependentOptIns()
    {
        var fixture = CreateTemporaryPackage();
        try
        {
            var host = new NoInvokeHost();
            var service = new WordLiveService(host);

            var namesRaw = await InspectRaw(service, new
            {
                local_path = fixture.Path,
                view = "declarations",
                include_names = true,
            });
            Assert.Contains(SecretProgramId, namesRaw);
            Assert.Contains(SecretControlName, namesRaw);
            Assert.DoesNotContain(SecretTarget, namesRaw);
            Assert.DoesNotContain(fixture.ActiveXBinarySha256, namesRaw);
            Assert.DoesNotContain("/word/document.xml", namesRaw);

            var controlRaw = await InspectRaw(service, new
            {
                local_path = fixture.Path,
                view = "controls",
                include_names = true,
            });
            Assert.Contains(SecretClassId, controlRaw);
            Assert.Contains("\"property_count\":1", controlRaw);
            Assert.DoesNotContain(SecretLicense, controlRaw);
            Assert.DoesNotContain(SecretPropertyValue, controlRaw);

            var targetsRaw = await InspectRaw(service, new
            {
                local_path = fixture.Path,
                view = "relationships",
                include_targets = true,
            });
            Assert.Contains(SecretTarget, targetsRaw);
            Assert.Contains("embeddings/oleObject1.bin", targetsRaw);
            Assert.DoesNotContain(SecretProgramId, targetsRaw);
            Assert.DoesNotContain(fixture.ActiveXBinarySha256, targetsRaw);
            Assert.DoesNotContain("source_part_uri\":\"/word/document.xml", targetsRaw);

            var payloadRaw = await InspectRaw(service, new
            {
                local_path = fixture.Path,
                view = "payloads",
                include_hashes = true,
                include_source = true,
            });
            Assert.Contains(fixture.ActiveXBinarySha256, payloadRaw);
            Assert.Contains("/word/activeX/activeX1.bin", payloadRaw);
            Assert.DoesNotContain(SecretProgramId, payloadRaw);
            Assert.DoesNotContain(SecretTarget, payloadRaw);
            Assert.DoesNotContain(SecretBinary, payloadRaw);
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            File.Delete(fixture.Path);
        }
    }

    [Fact]
    public async Task ExactFiltersFailClosedAndShareOneDeterministicOperationBudget()
    {
        var fixture = CreateTemporaryPackage();
        try
        {
            var service = new WordLiveService(new NoInvokeHost());
            using var discoveryArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = fixture.Path,
                    view = "payloads",
                    payload_kind = "active_x_binary",
                })
            );
            var discovery = await service.CallAsync(
                "inspect_ooxml_active_content",
                discoveryArguments.RootElement,
                CancellationToken.None
            );
            using var discoveryJson = JsonDocument.Parse(
                JsonSerializer.Serialize(discovery)
            );
            var root = discoveryJson.RootElement;
            var payload = Assert.Single(root.GetProperty("items").EnumerateArray());
            var payloadId = payload.GetProperty("payload_id").GetString()!;
            var used = root.GetProperty("operation_budget").GetProperty("used").GetInt64();

            using var filteredArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = fixture.Path,
                    view = "relationships",
                    payload_id = payloadId,
                })
            );
            var filtered = await service.CallAsync(
                "inspect_ooxml_active_content",
                filteredArguments.RootElement,
                CancellationToken.None
            );
            using var filteredJson = JsonDocument.Parse(JsonSerializer.Serialize(filtered));
            Assert.Single(filteredJson.RootElement.GetProperty("items").EnumerateArray());

            using var kindFilteredArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = fixture.Path,
                    view = "relationships",
                    payload_kind = "active_x_binary",
                })
            );
            var kindFiltered = await service.CallAsync(
                "inspect_ooxml_active_content",
                kindFilteredArguments.RootElement,
                CancellationToken.None
            );
            using var kindFilteredJson = JsonDocument.Parse(
                JsonSerializer.Serialize(kindFiltered)
            );
            var activeXRelationship = Assert.Single(
                kindFilteredJson.RootElement.GetProperty("items").EnumerateArray()
            );
            Assert.Equal(
                "active_x_control_binary",
                activeXRelationship.GetProperty("role").GetString()
            );

            using var roleFilteredArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = fixture.Path,
                    view = "payloads",
                    relationship_role = "vba_project",
                })
            );
            var roleFiltered = await service.CallAsync(
                "inspect_ooxml_active_content",
                roleFilteredArguments.RootElement,
                CancellationToken.None
            );
            using var roleFilteredJson = JsonDocument.Parse(
                JsonSerializer.Serialize(roleFiltered)
            );
            var vbaPayload = Assert.Single(
                roleFilteredJson.RootElement.GetProperty("items").EnumerateArray()
            );
            Assert.Equal("vba_project", vbaPayload.GetProperty("kind").GetString());

            using var unknownArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = fixture.Path,
                    view = "payloads",
                    payload_id = "wdap_AAAAAAAAAAAAAAAAAAAA",
                })
            );
            var unknown = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_active_content",
                    unknownArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("NOT_FOUND", unknown.ErrorCode);

            using var invalidArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = fixture.Path,
                    view = "raw_binary",
                })
            );
            var invalid = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_active_content",
                    invalidArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_INPUT", invalid.ErrorCode);

            var constrained = new WordLiveService(
                new NoInvokeHost(),
                () => new WordOperationResourceLease(used - 1)
            );
            var limited = await Assert.ThrowsAsync<NativeToolException>(() =>
                constrained.CallAsync(
                    "inspect_ooxml_active_content",
                    discoveryArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("PACKAGE_LIMIT", limited.ErrorCode);

            var exact = new WordLiveService(
                new NoInvokeHost(),
                () => new WordOperationResourceLease(used)
            );
            var exactResult = await exact.CallAsync(
                "inspect_ooxml_active_content",
                discoveryArguments.RootElement,
                CancellationToken.None
            );
            using var exactJson = JsonDocument.Parse(JsonSerializer.Serialize(exactResult));
            Assert.Equal(
                used,
                exactJson.RootElement.GetProperty("operation_budget")
                    .GetProperty("used").GetInt64()
            );
        }
        finally
        {
            File.Delete(fixture.Path);
        }
    }

    [Fact]
    public async Task CompleteDefaultGatewayEnvelopeStaysBoundedAndRedacted()
    {
        var fixture = CreateTemporaryPackage();
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
                        action = "inspect_ooxml_active_content",
                        arguments = new { local_path = fixture.Path },
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
            var contentText = result.GetProperty("content")[0]
                .GetProperty("text").GetString()!;
            var data = result.GetProperty("structuredContent").GetProperty("data");
            Assert.True(data.GetRawText().Length < 5_000);
            Assert.True(contentText.Length < 5_000);
            Assert.True(responseLine.Length < 8_000);
            AssertRedacted(responseLine, fixture.ActiveXBinarySha256);
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            File.Delete(fixture.Path);
        }
    }

    private static async Task<string> InspectRaw(WordLiveService service, object arguments)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        var result = await service.CallAsync(
            "inspect_ooxml_active_content",
            document.RootElement,
            CancellationToken.None
        );
        return JsonSerializer.Serialize(result);
    }

    private static void AssertRedacted(string value, string activeXBinarySha256)
    {
        Assert.DoesNotContain(SecretProgramId, value);
        Assert.DoesNotContain(SecretControlName, value);
        Assert.DoesNotContain(SecretClassId, value);
        Assert.DoesNotContain(SecretTarget, value);
        Assert.DoesNotContain(SecretLicense, value);
        Assert.DoesNotContain(SecretPropertyValue, value);
        Assert.DoesNotContain(SecretFieldCode, value);
        Assert.DoesNotContain(SecretBinary, value);
        Assert.DoesNotContain(activeXBinarySha256, value);
        Assert.DoesNotContain("/word/document.xml", value);
        Assert.DoesNotContain("/word/activeX/activeX1.bin", value);
        Assert.DoesNotContain("/word/embeddings/oleObject1.bin", value);
    }

    private static ActiveContentFixture CreateTemporaryPackage()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-active-content-{Guid.NewGuid():N}.docm"
        );
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(
            archive,
            "[Content_Types].xml",
            "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>"
                + "<Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/>"
                + "<Default Extension='xml' ContentType='application/xml'/>"
                + "<Default Extension='bin' ContentType='application/octet-stream'/>"
                + "<Override PartName='/word/document.xml' ContentType='application/vnd.ms-word.document.macroEnabled.main+xml'/>"
                + "<Override PartName='/word/embeddings/oleObject1.bin' ContentType='application/vnd.openxmlformats-officedocument.oleObject'/>"
                + "<Override PartName='/word/embeddings/package1.xlsx' ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'/>"
                + "<Override PartName='/word/activeX/activeX1.xml' ContentType='application/vnd.ms-office.activeX+xml'/>"
                + "<Override PartName='/word/activeX/activeX1.bin' ContentType='application/vnd.ms-office.activeX'/>"
                + "<Override PartName='/word/vbaProject.bin' ContentType='application/vnd.ms-office.vbaProject'/>"
                + "</Types>"
        );
        AddEntry(
            archive,
            "_rels/.rels",
            Relationships(
                "<Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='word/document.xml'/>"
            )
        );
        AddEntry(
            archive,
            "word/document.xml",
            $$"""
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body><w:p><w:r>
                <w:object><w:objectEmbed r:id="rOle" progId="{{SecretProgramId}}" objectType="Embed" fieldCodes="{{SecretFieldCode}}"/></w:object>
                <w:object><w:objectLink r:id="rLink" progId="Package" updateMode="OnCall" serverFormat="Picture"/></w:object>
                <w:control r:id="rControl" name="{{SecretControlName}}" shapeid="shape1"/>
              </w:r></w:p></w:body>
            </w:document>
            """
        );
        AddEntry(
            archive,
            "word/_rels/document.xml.rels",
            Relationships(
                "<Relationship Id='rOle' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject' Target='embeddings/oleObject1.bin'/>"
                + $"<Relationship Id='rLink' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject' Target='{SecretTarget}' TargetMode='External'/>"
                + "<Relationship Id='rPackage' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/package' Target='embeddings/package1.xlsx'/>"
                + "<Relationship Id='rControl' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/control' Target='activeX/activeX1.xml'/>"
                + "<Relationship Id='rVba' Type='http://schemas.microsoft.com/office/2006/relationships/vbaProject' Target='vbaProject.bin'/>"
            )
        );
        AddEntry(archive, "word/embeddings/oleObject1.bin", "OLE-BINARY");
        AddEntry(archive, "word/embeddings/package1.xlsx", "EMBEDDED-XLSX");
        AddEntry(
            archive,
            "word/activeX/activeX1.xml",
            $$"""
            <ax:ocx xmlns:ax="http://schemas.microsoft.com/office/2006/activeX" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" classid="{{SecretClassId}}" persistence="persistStreamInit" license="{{SecretLicense}}" r:id="rBin"><ax:ocxPr name="Caption" value="{{SecretPropertyValue}}"/><ax:notProperty/></ax:ocx>
            """
        );
        AddEntry(
            archive,
            "word/activeX/_rels/activeX1.xml.rels",
            Relationships(
                "<Relationship Id='rBin' Type='http://schemas.microsoft.com/office/2006/relationships/activeXControlBinary' Target='activeX1.bin'/>"
            )
        );
        AddEntry(archive, "word/activeX/activeX1.bin", SecretBinary);
        AddEntry(archive, "word/vbaProject.bin", "VBA-BINARY");
        return new ActiveContentFixture(
            path,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SecretBinary)))
                .ToLowerInvariant()
        );
    }

    private static string Relationships(string children) =>
        $"<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>{children}</Relationships>";

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private sealed record ActiveContentFixture(string Path, string ActiveXBinarySha256);

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
