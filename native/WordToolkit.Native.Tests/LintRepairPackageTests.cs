using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class LintRepairPackageTests
{
    [Fact]
    public async Task PlansAndCreatesValidatedTitleRepairWithoutOpeningWordOrEchoingTitle()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(directory, "source.docx");
            var outputPath = Path.Combine(directory, "repaired.docx");
            CreatePackage(sourcePath);
            var sourceHash = FileSha256(sourcePath);
            var host = new NoInvokeHost();
            var service = new WordLiveService(host);
            var lint = await CallAsync(service, "lint_ooxml_document", new
            {
                local_path = sourcePath,
                view = "findings",
                rule_pack = "accessibility",
                include_source = true,
                include_fix = true,
                max_items = 100,
            });
            var titleFinding = lint.GetProperty("items").EnumerateArray().Single(item =>
                item.GetProperty("rule_id").GetString()
                    == "WTL_ACCESSIBILITY_DOCUMENT_TITLE"
            );
            Assert.True(
                titleFinding.GetProperty("fix").GetProperty("implemented").GetBoolean()
            );
            Assert.True(
                titleFinding.GetProperty("source").GetProperty("byte_length").GetInt32() > 0
            );
            var packageFingerprint = lint.GetProperty("package_fingerprint").GetString();
            var findingId = titleFinding.GetProperty("finding_id").GetString();
            const string secretTitle = "Private <title> & evidence";
            var request = new
            {
                local_path = sourcePath,
                output_path = outputPath,
                expected_package_fingerprint = packageFingerprint,
                finding_id = findingId,
                repair_kind = "set_document_title",
                new_document_title = secretTitle,
                include_details = true,
            };

            var plan = await CallAsync(
                service,
                "plan_ooxml_lint_repair",
                request
            );

            Assert.False(plan.GetProperty("apply_blocked").GetBoolean());
            Assert.True(
                plan.GetProperty("validation")
                    .GetProperty("target_finding_resolved")
                    .GetBoolean()
            );
            Assert.True(
                plan.GetProperty("validation")
                    .GetProperty("openxml_no_new_errors")
                    .GetBoolean()
            );
            Assert.False(plan.GetProperty("word_opened").GetBoolean());
            Assert.False(plan.GetProperty("source_document_modified").GetBoolean());
            Assert.DoesNotContain(secretTitle, plan.GetRawText(), StringComparison.Ordinal);
            Assert.False(File.Exists(outputPath));

            var applyPlanId = plan.GetProperty("lint_repair_apply_plan_id").GetString();
            var applied = await CallAsync(
                service,
                "apply_ooxml_lint_repair",
                new
                {
                    request.local_path,
                    request.output_path,
                    request.expected_package_fingerprint,
                    request.finding_id,
                    request.repair_kind,
                    request.new_document_title,
                    expected_lint_repair_apply_plan_id = applyPlanId,
                }
            );

            Assert.True(applied.GetProperty("created").GetBoolean());
            Assert.False(applied.GetProperty("overwritten").GetBoolean());
            Assert.False(applied.GetProperty("word_opened").GetBoolean());
            Assert.True(File.Exists(outputPath));
            Assert.Equal(sourceHash, FileSha256(sourcePath));
            Assert.DoesNotContain(secretTitle, applied.GetRawText(), StringComparison.Ordinal);
            Assert.Equal(0, host.InvocationCount);

            var repairedLint = await CallAsync(service, "lint_ooxml_document", new
            {
                local_path = outputPath,
                view = "findings",
                rule_pack = "accessibility",
                max_items = 100,
            });
            Assert.DoesNotContain(
                repairedLint.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("rule_id").GetString()
                    == "WTL_ACCESSIBILITY_DOCUMENT_TITLE"
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyRejectsChangedRepairValueAndExistingOutput()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(directory, "source.docx");
            var outputPath = Path.Combine(directory, "repaired.docx");
            CreatePackage(sourcePath);
            var service = new WordLiveService(new NoInvokeHost());
            var lint = await CallAsync(service, "lint_ooxml_document", new
            {
                local_path = sourcePath,
                view = "findings",
                rule_id = "WTL_ACCESSIBILITY_DOCUMENT_TITLE",
                include_fix = true,
            });
            var findingId = lint.GetProperty("items")[0]
                .GetProperty("finding_id").GetString();
            var fingerprint = lint.GetProperty("package_fingerprint").GetString();
            var request = new
            {
                local_path = sourcePath,
                output_path = outputPath,
                expected_package_fingerprint = fingerprint,
                finding_id = findingId,
                repair_kind = "set_document_title",
                new_document_title = "reviewed",
            };
            var plan = await CallAsync(
                service,
                "plan_ooxml_lint_repair",
                request
            );
            var applyPlanId = plan.GetProperty("lint_repair_apply_plan_id").GetString();

            var mismatch = await Assert.ThrowsAsync<NativeToolException>(() =>
                CallAsync(service, "apply_ooxml_lint_repair", new
                {
                    request.local_path,
                    request.output_path,
                    request.expected_package_fingerprint,
                    request.finding_id,
                    request.repair_kind,
                    new_document_title = "changed",
                    expected_lint_repair_apply_plan_id = applyPlanId,
                })
            );
            Assert.Equal("PLAN_MISMATCH", mismatch.ErrorCode);
            Assert.False(File.Exists(outputPath));

            File.WriteAllText(outputPath, "occupied");
            var existing = await Assert.ThrowsAsync<NativeToolException>(() =>
                CallAsync(service, "plan_ooxml_lint_repair", request)
            );
            Assert.Equal("ALREADY_EXISTS", existing.ErrorCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SignedPackageCanBePreviewedButApplyIsBlocked()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(directory, "signed.docx");
            var outputPath = Path.Combine(directory, "repaired.docx");
            CreatePackage(sourcePath, includeSignatureMaterial: true);
            var service = new WordLiveService(new NoInvokeHost());
            var lint = await CallAsync(service, "lint_ooxml_document", new
            {
                local_path = sourcePath,
                view = "findings",
                rule_id = "WTL_ACCESSIBILITY_DOCUMENT_TITLE",
            });
            var request = new
            {
                local_path = sourcePath,
                output_path = outputPath,
                expected_package_fingerprint = lint.GetProperty("package_fingerprint")
                    .GetString(),
                finding_id = lint.GetProperty("items")[0]
                    .GetProperty("finding_id").GetString(),
                repair_kind = "set_document_title",
                new_document_title = "reviewed",
            };
            var plan = await CallAsync(
                service,
                "plan_ooxml_lint_repair",
                request
            );

            Assert.True(plan.GetProperty("apply_blocked").GetBoolean());
            Assert.Contains(
                plan.GetProperty("apply_block_codes").EnumerateArray(),
                item => item.GetString() == "digital_signature_present"
            );
            var blocked = await Assert.ThrowsAsync<NativeToolException>(() =>
                CallAsync(service, "apply_ooxml_lint_repair", new
                {
                    request.local_path,
                    request.output_path,
                    request.expected_package_fingerprint,
                    request.finding_id,
                    request.repair_kind,
                    request.new_document_title,
                    expected_lint_repair_apply_plan_id = plan
                        .GetProperty("lint_repair_apply_plan_id").GetString(),
                })
            );
            Assert.Equal("REPAIR_POLICY_BLOCKED", blocked.ErrorCode);
            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<JsonElement> CallAsync(
        WordLiveService service,
        string action,
        object arguments
    )
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        var result = await service.CallAsync(
            action,
            json.RootElement,
            CancellationToken.None
        );
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
            "wordtoolkit-native-lint-repair-tests",
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
              <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
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
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
            </Relationships>
            """
        );
        AddEntry(
            archive,
            "docProps/core.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title/></cp:coreProperties>
            """
        );
        AddEntry(
            archive,
            "word/document.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Body</w:t></w:r></w:p><w:sectPr/></w:body></w:document>
            """
        );
        if (includeSignatureMaterial)
        {
            AddEntry(
                archive,
                "_xmlsignatures/sig1.xml",
                """
                <Signature xmlns="http://www.w3.org/2000/09/xmldsig#"/>
                """
            );
        }
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
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
