using System.IO.Compression;
using System.Text;
using System.Text.Json;
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
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;

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
                text_preview_chars = 5,
                include_source_paths = true,
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
                node => Assert.StartsWith(
                    "wdn_",
                    node.GetProperty("node_id").GetString(),
                    StringComparison.Ordinal
                )
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CreatePackage(string path)
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
            """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml" />
            </Relationships>
            """
        );
        WriteEntry(
            archive,
            "word/document.xml",
            """
            <w:document
                xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml"
                xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <w:body>
                <w:p w14:paraId="00112233">
                  <w:r><w:t>Hello </w:t></w:r>
                  <m:oMath><m:f><m:num><m:r><m:t>a</m:t></m:r></m:num><m:den><m:r><m:t>b</m:t></m:r></m:den></m:f></m:oMath>
                </w:p>
              </w:body>
            </w:document>
            """
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
