using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class SemanticDiffServiceTests
{
    private const string SecretBefore = "confidential before sentence";
    private const string SecretAfter = "confidential revised sentence";

    [Fact]
    public async Task SummaryIsCompactRedactedAndNeverOpensWord()
    {
        var (beforePath, afterPath) = CreateComparedFiles();
        try
        {
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                before_path = beforePath,
                after_path = afterPath,
            }));

            var result = await new WordLiveService(new NoInvokeHost()).CallAsync(
                "compare_ooxml_semantics",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;
            var raw = root.GetRawText();

            Assert.StartsWith("wddiff_", root.GetProperty("diff_id").GetString());
            Assert.False(root.GetProperty("package_equivalent").GetBoolean());
            Assert.False(root.GetProperty("semantically_equivalent").GetBoolean());
            Assert.True(root.GetProperty("matching_complete").GetBoolean());
            Assert.True(root.GetProperty("semantic_difference_count").GetInt32() > 0);
            Assert.Equal(0, root.GetProperty("returned_item_count").GetInt32());
            Assert.Empty(root.GetProperty("items").EnumerateArray());
            Assert.False(root.GetProperty("sensitive_values_included").GetBoolean());
            Assert.False(root.GetProperty("raw_xml_returned").GetBoolean());
            Assert.False(root.GetProperty("mutation_performed").GetBoolean());
            Assert.False(root.GetProperty("word_opened").GetBoolean());
            Assert.DoesNotContain(SecretBefore, raw, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretAfter, raw, StringComparison.Ordinal);
            Assert.DoesNotContain("<w:", raw, StringComparison.Ordinal);
            Assert.True(raw.Length < 5_000, $"Semantic diff summary is too large: {raw.Length}");
        }
        finally
        {
            DeleteComparedFiles(beforePath, afterPath);
        }
    }

    [Fact]
    public async Task ChangeViewKeepsContentHiddenUntilExplicitOptIn()
    {
        var (beforePath, afterPath) = CreateComparedFiles();
        try
        {
            var service = new WordLiveService(new NoInvokeHost());
            using var redactedArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                before_path = beforePath,
                after_path = afterPath,
                view = "changes",
                node_kinds = new[] { "paragraph" },
                change_kinds = new[] { "text_changed" },
            }));
            var redactedObject = await service.CallAsync(
                "compare_ooxml_semantics",
                redactedArguments.RootElement,
                CancellationToken.None
            );
            using var redactedJson = JsonDocument.Parse(JsonSerializer.Serialize(redactedObject));
            var redacted = Assert.Single(redactedJson.RootElement.GetProperty("items").EnumerateArray());

            Assert.Equal(JsonValueKind.Null, redacted.GetProperty("text")
                .GetProperty("before").GetProperty("text_preview").ValueKind);
            Assert.Equal(JsonValueKind.Null, redacted.GetProperty("text")
                .GetProperty("before").GetProperty("comparison_fingerprint").ValueKind);
            Assert.DoesNotContain(SecretBefore, redacted.GetRawText(), StringComparison.Ordinal);

            using var revealedArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                before_path = beforePath,
                after_path = afterPath,
                view = "changes",
                node_kinds = new[] { "paragraph" },
                change_kinds = new[] { "text_changed" },
                include_sensitive = true,
                text_preview_chars = 12,
                include_source = true,
                include_hashes = true,
            }));
            var revealedObject = await service.CallAsync(
                "compare_ooxml_semantics",
                revealedArguments.RootElement,
                CancellationToken.None
            );
            using var revealedJson = JsonDocument.Parse(JsonSerializer.Serialize(revealedObject));
            var revealed = Assert.Single(revealedJson.RootElement.GetProperty("items").EnumerateArray());
            var beforeText = revealed.GetProperty("text").GetProperty("before");

            Assert.Equal(SecretBefore[..12], beforeText.GetProperty("text_preview").GetString());
            Assert.Equal(
                64,
                beforeText.GetProperty("comparison_fingerprint").GetString()!.Length
            );
            Assert.Equal(
                "/word/document.xml",
                revealed.GetProperty("before").GetProperty("source_part_uri").GetString()
            );
            Assert.True(revealedJson.RootElement.GetProperty("sensitive_values_included").GetBoolean());
        }
        finally
        {
            DeleteComparedFiles(beforePath, afterPath);
        }
    }

    [Fact]
    public async Task EntryViewReportsOpaqueDifferenceAndHonorsFingerprintPreconditions()
    {
        var beforePath = CreateDocument(SecretBefore, [1, 2, 3]);
        var afterPath = CreateDocument(SecretBefore, [1, 2, 4]);
        try
        {
            var reader = new OpcPackageReader();
            var before = reader.Read(beforePath);
            var after = reader.Read(afterPath);
            var service = new WordLiveService(new NoInvokeHost());
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                before_path = beforePath,
                after_path = afterPath,
                expected_before_fingerprint = before.Fingerprint,
                expected_after_fingerprint = after.Fingerprint,
                view = "entries",
                include_hashes = true,
            }));

            var result = await service.CallAsync(
                "compare_ooxml_semantics",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;
            var item = Assert.Single(root.GetProperty("items").EnumerateArray());

            Assert.False(root.GetProperty("package_equivalent").GetBoolean());
            Assert.True(root.GetProperty("semantically_equivalent").GetBoolean());
            Assert.Equal("custom/opaque.bin", item.GetProperty("entry_name").GetString());
            Assert.Equal(64, item.GetProperty("before_sha256").GetString()!.Length);
            Assert.False(item.GetProperty("is_projected_semantic_part").GetBoolean());

            using var staleArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                before_path = beforePath,
                after_path = afterPath,
                expected_before_fingerprint = new string('0', 64),
            }));
            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "compare_ooxml_semantics",
                    staleArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("VERSION_CONFLICT", exception.ErrorCode);
        }
        finally
        {
            DeleteComparedFiles(beforePath, afterPath);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectsUnsafePreviewAndDuplicateFilters(bool previewCase)
    {
        var (beforePath, afterPath) = CreateComparedFiles();
        try
        {
            object request = previewCase
                ? new
                {
                    before_path = beforePath,
                    after_path = afterPath,
                    view = "changes",
                    text_preview_chars = 10,
                }
                : new
                {
                    before_path = beforePath,
                    after_path = afterPath,
                    view = "changes",
                    node_kinds = new[] { "paragraph", "paragraph" },
                };
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(request));

            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                new WordLiveService(new NoInvokeHost()).CallAsync(
                    "compare_ooxml_semantics",
                    arguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("INVALID_INPUT", exception.ErrorCode);
        }
        finally
        {
            DeleteComparedFiles(beforePath, afterPath);
        }
    }

    private static (string BeforePath, string AfterPath) CreateComparedFiles() => (
        CreateDocument(SecretBefore),
        CreateDocument(SecretAfter)
    );

    private static string CreateDocument(string text, byte[]? opaque = null)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-diff-{Guid.NewGuid():N}.docx"
        );
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        Write(archive, "[Content_Types].xml", ContentTypes(opaque is not null));
        Write(archive, "_rels/.rels", RootRelationships());
        Write(archive, "word/document.xml", DocumentXml(text));
        if (opaque is not null)
        {
            Write(archive, "custom/opaque.bin", opaque);
        }
        return path;
    }

    private static string DocumentXml(string text) =>
        "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main' "
        + "xmlns:w14='http://schemas.microsoft.com/office/word/2010/wordml'>"
        + $"<w:body><w:p w14:paraId='00112233'><w:pPr><w:pStyle w:val='BodyText'/></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>"
        + "</w:body></w:document>";

    private static string ContentTypes(bool opaque) =>
        "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>"
        + "<Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/>"
        + "<Default Extension='xml' ContentType='application/xml'/>"
        + (opaque ? "<Default Extension='bin' ContentType='application/octet-stream'/>" : string.Empty)
        + "<Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/>"
        + "</Types>";

    private static string RootRelationships() =>
        "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
        + "<Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='word/document.xml'/>"
        + "</Relationships>";

    private static void Write(ZipArchive archive, string name, string value) =>
        Write(archive, name, Encoding.UTF8.GetBytes(value));

    private static void Write(ZipArchive archive, string name, byte[] value)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var target = entry.Open();
        target.Write(value);
    }

    private static void DeleteComparedFiles(params string[] paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class NoInvokeHost : IWordComHost
    {
        public Task<T> InvokeAsync<T>(
            Func<dynamic, T> operation,
            CancellationToken cancellationToken = default,
            bool launchIfMissing = false
        ) => throw new Xunit.Sdk.XunitException(
            "Saved-package semantic diff must not invoke the Word COM host."
        );

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
