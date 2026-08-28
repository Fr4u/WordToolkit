using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Rendering;

namespace WordToolkit.Native.Tests;

public sealed class EquationRenderQaTests
{
    [Fact]
    public void PackageScanDistinguishesRawEqarrayTextFromStructuredEqarray()
    {
        var directory = TemporaryDirectory();
        try
        {
            var raw = Path.Combine(directory, "raw.docx");
            var structured = Path.Combine(directory, "structured.docx");
            var mixed = Path.Combine(directory, "mixed.docx");
            CreatePackage(raw, "<m:oMath><m:r><m:t>eqarray(x@y)</m:t></m:r></m:oMath>");
            CreatePackage(structured, "<m:oMath><m:eqArr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:eqArr></m:oMath>");
            CreatePackage(mixed, "<m:oMath><m:eqArr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:eqArr><m:r><m:t>eqarray(raw)</m:t></m:r></m:oMath>");

            var rawScan = EquationRenderQa.ScanPackage(raw);
            var structuredScan = EquationRenderQa.ScanPackage(structured);
            var mixedScan = EquationRenderQa.ScanPackage(mixed);

            Assert.True(rawScan.Performed);
            Assert.Equal(1, rawScan.RawControlSyntaxCount);
            Assert.Equal(new[] { 1 }, rawScan.EquationIndexes);
            Assert.True(structuredScan.Performed);
            Assert.Equal(0, structuredScan.RawControlSyntaxCount);
            Assert.Equal(1, mixedScan.RawControlSyntaxCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RasterQaDetectsEdgeInkAndNearFullPageWidthWithoutReturningPixels()
    {
        var directory = TemporaryDirectory();
        try
        {
            var png = Path.Combine(directory, "page-1.png");
            WriteRgbPng(png, 100, 100, (x, y) =>
                y == 50 && (x == 0 || x == 99)
            );
            var raster = Raster(directory, png, 100, 100);
            var result = EquationRenderQa.Analyze(
                raster,
                new EquationRenderSourceScan(true, 1, 0, [], "test")
            );
            using var json = JsonDocument.Parse(
                JsonSerializer.Serialize(result, JsonDefaults.Compact)
            );
            var root = json.RootElement;
            Assert.True(root.GetProperty("raster_check_performed").GetBoolean());
            var risks = root.GetProperty("risk_codes").EnumerateArray()
                .Select(item => item.GetString()).ToArray();
            Assert.Contains("PAGE_EDGE_INK", risks);
            Assert.Contains("CONTENT_EXCEEDS_USABLE_PAGE_WIDTH", risks);
            Assert.False(root.GetProperty("raw_pixels_returned").GetBoolean());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RasterQaLeavesCenteredInkUnflagged()
    {
        var directory = TemporaryDirectory();
        try
        {
            var png = Path.Combine(directory, "page-1.png");
            WriteRgbPng(png, 100, 100, (x, y) =>
                x is >= 30 and <= 70 && y is >= 45 and <= 55
            );
            var result = EquationRenderQa.Analyze(
                Raster(directory, png, 100, 100),
                new EquationRenderSourceScan(true, 1, 0, [], "test")
            );
            using var json = JsonDocument.Parse(
                JsonSerializer.Serialize(result, JsonDefaults.Compact)
            );
            Assert.Empty(json.RootElement.GetProperty("risk_codes").EnumerateArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static PopplerRasterizationStagingResult Raster(
        string directory,
        string png,
        int width,
        int height
    )
    {
        var provenance = new PopplerBackendProvenance(
            "test",
            new PopplerToolProvenance("pdfinfo", "test", "test"),
            new PopplerToolProvenance("rasterizer", "test", "test")
        );
        return new PopplerRasterizationStagingResult(
            directory,
            true,
            144,
            [new PopplerRasterizedPage(1, png, new FileInfo(png).Length, width, height, "")],
            provenance
        );
    }

    private static void WriteRgbPng(
        string path,
        int width,
        int height,
        Func<int, int, bool> ink
    )
    {
        using var output = File.Create(path);
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..8], height);
        ihdr[8] = 8;
        ihdr[9] = 2;
        WriteChunk(output, "IHDR", ihdr.ToArray());
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            for (var x = 0; x < width; x++)
            {
                var component = ink(x, y) ? (byte)0 : (byte)255;
                raw.WriteByte(component);
                raw.WriteByte(component);
                raw.WriteByte(component);
            }
        }
        raw.Position = 0;
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
        {
            raw.CopyTo(zlib);
        }
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        output.Write(Encoding.ASCII.GetBytes(type));
        output.Write(data);
        output.Write([0, 0, 0, 0]);
    }

    private static void CreatePackage(string path, string math)
    {
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        Add(archive, "[Content_Types].xml",
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
        Add(archive, "_rels/.rels",
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
        Add(archive, "word/document.xml",
            $"<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"><w:body><w:p>{math}</w:p><w:sectPr/></w:body></w:document>");
    }

    private static void Add(ZipArchive archive, string name, string text)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wordtoolkit-equation-qa-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
