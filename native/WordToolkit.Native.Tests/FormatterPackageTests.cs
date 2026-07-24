using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class FormatterPackageTests
{
    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void CatalogPublishesVersionedFormatterEffectsAndResultContracts()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        var plan = catalog.InspectAction("plan_ooxml_format")["tool"]!.AsObject();
        var apply = catalog.InspectAction("apply_ooxml_format")["tool"]!.AsObject();

        Assert.Equal("1.0", plan["operationVersion"]!.GetValue<string>());
        Assert.Equal(
            "read_input_package",
            plan["permissions"]!["filesystem"]!.GetValue<string>()
        );
        Assert.False(plan["reversibility"]!["applicable"]!.GetValue<bool>());
        Assert.Equal(
            "wordtoolkit.plan_ooxml_format/1.0",
            plan["outputSchema"]!["properties"]!["data"]!["properties"]!["operation_contract"]!["const"]!.GetValue<string>()
        );
        Assert.Equal("1.0", apply["operationVersion"]!.GetValue<string>());
        Assert.Equal(
            "read_input_package_and_create_new_output",
            apply["permissions"]!["filesystem"]!.GetValue<string>()
        );
        Assert.Equal(
            "delete_created_output",
            apply["reversibility"]!["mechanism"]!.GetValue<string>()
        );
        Assert.Equal(
            "wordtoolkit.apply_ooxml_format/1.0",
            apply["outputSchema"]!["properties"]!["data"]!["properties"]!["operation_contract"]!["const"]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task PlansAppliesAndStabilizesAValidatedTokenLeanFormatWithoutOpeningWord()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(directory, "source.docx");
            var outputPath = Path.Combine(directory, "formatted.docx");
            var noOpOutputPath = Path.Combine(directory, "noop.docx");
            CreatePackage(sourcePath);
            var sourceHash = FileSha256(sourcePath);
            var package = new OpcPackageReader().Read(sourcePath);
            var host = new NoInvokeHost();
            var service = new WordLiveService(host);
            var request = new
            {
                local_path = sourcePath,
                output_path = outputPath,
                expected_package_fingerprint = package.Fingerprint,
                policies = new[] { "remove_redundant_direct_formatting" },
                include_details = true,
                include_source = true,
                detail_limit = 100,
            };

            var plan = await CallAsync(service, "plan_ooxml_format", request);

            Assert.Equal(
                "wordtoolkit.plan_ooxml_format/1.0",
                plan.GetProperty("operation_contract").GetString()
            );
            Assert.True(plan.GetProperty("has_changes").GetBoolean());
            Assert.Equal(6, plan.GetProperty("removed_element_count").GetInt32());
            Assert.False(plan.GetProperty("apply_blocked").GetBoolean());
            Assert.True(plan.GetProperty("validation").GetProperty("engine_passed").GetBoolean());
            Assert.True(plan.GetProperty("validation").GetProperty("semantic_content_preserved").GetBoolean());
            Assert.True(plan.GetProperty("validation").GetProperty("effective_formatting_preserved").GetBoolean());
            Assert.True(plan.GetProperty("validation").GetProperty("openxml_performed").GetBoolean());
            Assert.True(plan.GetProperty("validation").GetProperty("openxml_no_new_errors").GetBoolean());
            Assert.False(plan.GetProperty("raw_text_returned").GetBoolean());
            Assert.False(plan.GetProperty("raw_xml_returned").GetBoolean());
            Assert.False(plan.GetProperty("word_opened").GetBoolean());
            Assert.False(File.Exists(outputPath));
            Assert.DoesNotContain("Formatter body", plan.GetRawText(), StringComparison.Ordinal);
            Assert.True(plan.GetRawText().Length < 14_000);

            var applied = await CallAsync(service, "apply_ooxml_format", new
            {
                request.local_path,
                request.output_path,
                request.expected_package_fingerprint,
                request.policies,
                expected_formatter_apply_plan_id = plan
                    .GetProperty("formatter_apply_plan_id").GetString(),
            });

            Assert.Equal(
                "wordtoolkit.apply_ooxml_format/1.0",
                applied.GetProperty("operation_contract").GetString()
            );
            Assert.True(applied.GetProperty("created").GetBoolean());
            Assert.False(applied.GetProperty("overwritten").GetBoolean());
            Assert.False(applied.GetProperty("word_opened").GetBoolean());
            Assert.True(File.Exists(outputPath));
            Assert.Equal(sourceHash, FileSha256(sourcePath));
            Assert.Equal(0, host.InvocationCount);
            AssertFormattingWasMinimallyRemoved(outputPath);

            var formattedFingerprint = applied.GetProperty("package_fingerprint").GetString();
            var noOpRequest = new
            {
                local_path = outputPath,
                output_path = noOpOutputPath,
                expected_package_fingerprint = formattedFingerprint,
                policies = request.policies,
            };
            var noOpPlan = await CallAsync(service, "plan_ooxml_format", noOpRequest);
            Assert.False(noOpPlan.GetProperty("has_changes").GetBoolean());
            Assert.False(noOpPlan.GetProperty("apply_blocked").GetBoolean());

            var noOpApply = await CallAsync(service, "apply_ooxml_format", new
            {
                noOpRequest.local_path,
                noOpRequest.output_path,
                noOpRequest.expected_package_fingerprint,
                noOpRequest.policies,
                expected_formatter_apply_plan_id = noOpPlan
                    .GetProperty("formatter_apply_plan_id").GetString(),
            });
            Assert.False(noOpApply.GetProperty("created").GetBoolean());
            Assert.True(noOpApply.GetProperty("no_op").GetBoolean());
            Assert.False(noOpApply.GetProperty("mutation_performed").GetBoolean());
            Assert.False(File.Exists(noOpOutputPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyRejectsAChangedOutputPathAndAnExistingDestination()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(directory, "source.docx");
            var reviewedOutputPath = Path.Combine(directory, "reviewed.docx");
            var changedOutputPath = Path.Combine(directory, "changed.docx");
            CreatePackage(sourcePath);
            var service = new WordLiveService(new NoInvokeHost());
            var fingerprint = new OpcPackageReader().Read(sourcePath).Fingerprint;
            var request = new
            {
                local_path = sourcePath,
                output_path = reviewedOutputPath,
                expected_package_fingerprint = fingerprint,
                policies = new[] { "remove_redundant_direct_formatting" },
            };
            var plan = await CallAsync(service, "plan_ooxml_format", request);
            var applyPlanId = plan.GetProperty("formatter_apply_plan_id").GetString();

            var mismatch = await Assert.ThrowsAsync<NativeToolException>(() =>
                CallAsync(service, "apply_ooxml_format", new
                {
                    request.local_path,
                    output_path = changedOutputPath,
                    request.expected_package_fingerprint,
                    request.policies,
                    expected_formatter_apply_plan_id = applyPlanId,
                })
            );
            Assert.Equal("PLAN_MISMATCH", mismatch.ErrorCode);
            Assert.False(File.Exists(reviewedOutputPath));
            Assert.False(File.Exists(changedOutputPath));

            File.WriteAllText(reviewedOutputPath, "occupied");
            var existing = await Assert.ThrowsAsync<NativeToolException>(() =>
                CallAsync(service, "plan_ooxml_format", request)
            );
            Assert.Equal("ALREADY_EXISTS", existing.ErrorCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PlanRejectsUnknownAndUnboundedPagingInputBeforeFormatting()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(directory, "source.docx");
            var outputPath = Path.Combine(directory, "formatted.docx");
            CreatePackage(sourcePath);
            var fingerprint = new OpcPackageReader().Read(sourcePath).Fingerprint;
            var service = new WordLiveService(new NoInvokeHost());

            var unknown = await Assert.ThrowsAsync<NativeToolException>(() =>
                CallAsync(service, "plan_ooxml_format", new
                {
                    local_path = sourcePath,
                    output_path = outputPath,
                    expected_package_fingerprint = fingerprint,
                    policies = new[] { "remove_redundant_direct_formatting" },
                    raw_xml = true,
                })
            );
            Assert.Equal("INVALID_INPUT", unknown.ErrorCode);

            var unbounded = await Assert.ThrowsAsync<NativeToolException>(() =>
                CallAsync(service, "plan_ooxml_format", new
                {
                    local_path = sourcePath,
                    output_path = outputPath,
                    expected_package_fingerprint = fingerprint,
                    policies = new[] { "remove_redundant_direct_formatting" },
                    detail_offset = 1_000_001,
                })
            );
            Assert.Equal("INVALID_INPUT", unbounded.ErrorCode);
            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SignedPackageCanBePlannedButApplyIsBlocked()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(directory, "signed.docx");
            var outputPath = Path.Combine(directory, "formatted.docx");
            CreatePackage(sourcePath, includeSignatureMaterial: true);
            var package = new OpcPackageReader().Read(sourcePath);
            var service = new WordLiveService(new NoInvokeHost());
            var request = new
            {
                local_path = sourcePath,
                output_path = outputPath,
                expected_package_fingerprint = package.Fingerprint,
                policies = new[] { "remove_redundant_direct_formatting" },
            };
            var plan = await CallAsync(service, "plan_ooxml_format", request);

            Assert.True(plan.GetProperty("apply_blocked").GetBoolean());
            Assert.Contains(
                plan.GetProperty("apply_block_codes").EnumerateArray(),
                item => item.GetString() == "digital_signature_present"
            );
            var blocked = await Assert.ThrowsAsync<NativeToolException>(() =>
                CallAsync(service, "apply_ooxml_format", new
                {
                    request.local_path,
                    request.output_path,
                    request.expected_package_fingerprint,
                    request.policies,
                    expected_formatter_apply_plan_id = plan
                        .GetProperty("formatter_apply_plan_id").GetString(),
                })
            );
            Assert.Equal("FORMAT_POLICY_BLOCKED", blocked.ErrorCode);
            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertFormattingWasMinimallyRemoved(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("word/document.xml is missing");
        using var input = entry.Open();
        var document = XDocument.Load(input);
        XNamespace w = WordNamespace;
        var paragraphProperties = document.Descendants(w + "pPr").Single();
        var runProperties = document.Descendants(w + "rPr").Single();
        Assert.Equal(
            ["pStyle", "shd"],
            paragraphProperties.Elements().Select(element => element.Name.LocalName).ToArray()
        );
        Assert.Equal(
            ["i", "color"],
            runProperties.Elements().Select(element => element.Name.LocalName).ToArray()
        );
        Assert.Equal("Formatter body", document.Descendants(w + "t").Single().Value);
    }

    private static async Task<JsonElement> CallAsync(
        WordLiveService service,
        string action,
        object arguments
    )
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        var result = await service.CallAsync(action, json.RootElement, CancellationToken.None);
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(result));
        return serialized.RootElement.Clone();
    }

    private static string FileSha256(string path) => Convert.ToHexString(
        SHA256.HashData(File.ReadAllBytes(path))
    ).ToLowerInvariant();

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-formatter-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void CreatePackage(
        string path,
        bool includeSignatureMaterial = false
    )
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
            </Types>
            """
        );
        AddEntry(
            archive,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """
        );
        AddEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """
        );
        AddEntry(
            archive,
            "word/styles.xml",
            $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="{WordNamespace}">
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
                <w:name w:val="Normal"/>
                <w:pPr>
                  <w:jc w:val="center"/>
                  <w:spacing w:after="200"/>
                  <w:ind w:left="720"/>
                  <w:keepNext w:val="0"/>
                  <w:shd w:val="clear" w:fill="FFFFFF"/>
                </w:pPr>
                <w:rPr>
                  <w:b/>
                  <w:sz w:val="24"/>
                  <w:color w:val="112233"/>
                </w:rPr>
              </w:style>
            </w:styles>
            """
        );
        AddEntry(
            archive,
            "word/document.xml",
            $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="{WordNamespace}">
              <w:body>
                <w:p>
                  <w:pPr>
                    <w:pStyle w:val="Normal"/>
                    <w:jc w:val="center"/>
                    <w:spacing w:after="200"/>
                    <w:ind w:left="720"/>
                    <w:keepNext w:val="0"/>
                    <w:shd w:val="clear" w:fill="FFFFFF"/>
                  </w:pPr>
                  <w:r>
                    <w:rPr>
                      <w:b/>
                      <w:sz w:val="24"/>
                      <w:i/>
                      <w:color w:val="112233"/>
                    </w:rPr>
                    <w:t>Formatter body</w:t>
                  </w:r>
                </w:p>
                <w:sectPr/>
              </w:body>
            </w:document>
            """
        );
        if (includeSignatureMaterial)
        {
            AddEntry(
                archive,
                "_xmlsignatures/sig1.xml",
                """<Signature xmlns="http://www.w3.org/2000/09/xmldsig#"/>"""
            );
        }
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var output = entry.Open();
        output.Write(Encoding.UTF8.GetBytes(content));
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
