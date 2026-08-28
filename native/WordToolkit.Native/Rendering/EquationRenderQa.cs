using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace WordToolkit.Native.Rendering;

internal sealed record EquationRenderSourceScan(
    bool Performed,
    int EquationCount,
    int RawControlSyntaxCount,
    IReadOnlyList<int> EquationIndexes,
    string SourceKind
);

internal static class EquationRenderQa
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly Regex RawControlSyntax = new(
        @"(?i)(?:^|[^A-Za-z])eqarray\s*\(|\\(?:begin|end)\s*\{(?:eqnarray|array)\}",
        RegexOptions.CultureInvariant
    );
    private const long MaximumDecodedBytes = 512L * 1024 * 1024;
    private const int MaximumDimension = 32_768;

    internal static EquationRenderSourceScan ScanLiveDocument(dynamic document)
    {
        dynamic? equations = null;
        var rawIndexes = new List<int>();
        var count = 0;
        try
        {
            equations = document.OMaths;
            count = (int)equations.Count;
            for (var index = 1; index <= count; index++)
            {
                dynamic? equation = null;
                dynamic? range = null;
                try
                {
                    equation = equations.Item(index);
                    range = equation.Range;
                    if (IsRawControlSyntax(
                        (string?)range.Text ?? "",
                        (string?)range.WordOpenXML ?? ""
                    ))
                    {
                        rawIndexes.Add(index);
                    }
                }
                finally
                {
                    FinalRelease(range);
                    FinalRelease(equation);
                }
            }
        }
        catch
        {
            return new EquationRenderSourceScan(false, count, 0, [], "live_word");
        }
        finally
        {
            FinalRelease(equations);
        }
        return new EquationRenderSourceScan(true, count, rawIndexes.Count, rawIndexes, "live_word");
    }

    internal static EquationRenderSourceScan ScanPackage(string path)
    {
        var rawIndexes = new List<int>();
        var equationCount = 0;
        try
        {
            using var archive = ZipFile.OpenRead(path);
            foreach (var entry in archive.Entries.Where(entry =>
                    entry.FullName.StartsWith("word/", StringComparison.Ordinal)
                    && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                    && entry.Length <= 32 * 1024 * 1024)
                .OrderBy(entry => entry.FullName, StringComparer.Ordinal))
            {
                using var stream = entry.Open();
                using var reader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = 32 * 1024 * 1024,
                });
                var document = XDocument.Load(reader, LoadOptions.None);
                foreach (var equation in document.Descendants().Where(element =>
                    element.Name.LocalName == "oMath"
                    && !element.Ancestors().Any(ancestor => ancestor.Name.LocalName == "oMath")))
                {
                    equationCount++;
                    var text = string.Concat(equation.Descendants()
                        .Where(element => element.Name.LocalName == "t")
                        .Select(element => element.Value));
                    if (IsRawControlSyntax(text, equation.ToString(SaveOptions.DisableFormatting)))
                    {
                        rawIndexes.Add(equationCount);
                    }
                }
            }
            return new EquationRenderSourceScan(true, equationCount, rawIndexes.Count, rawIndexes, "saved_package");
        }
        catch
        {
            return new EquationRenderSourceScan(false, equationCount, 0, [], "saved_package");
        }
    }

    internal static object Analyze(
        PopplerRasterizationStagingResult? raster,
        EquationRenderSourceScan source
    )
    {
        var pages = new SortedSet<int>();
        var risks = new SortedSet<string>(StringComparer.Ordinal);
        if (source.RawControlSyntaxCount > 0) risks.Add("RAW_LINEAR_CONTROL_SYNTAX");
        var analyzedPages = 0;
        var unreadablePages = 0;
        if (raster is not null)
        {
            foreach (var page in raster.Pages.OrderBy(page => page.PageNumber))
            {
                if (!TryBounds(page.StagingPath, out var width, out var height,
                    out var minX, out var maxX, out var minY, out var maxY))
                {
                    unreadablePages++;
                    continue;
                }
                analyzedPages++;
                var edge = Math.Max(2, Math.Min(width, height) / 100);
                if (minX <= edge || minY <= edge || maxX >= width - 1 - edge || maxY >= height - 1 - edge)
                {
                    pages.Add(page.PageNumber);
                    risks.Add("PAGE_EDGE_INK");
                }
                if (maxX - minX + 1 > width * 0.97)
                {
                    pages.Add(page.PageNumber);
                    risks.Add("CONTENT_EXCEEDS_USABLE_PAGE_WIDTH");
                }
            }
        }
        return new
        {
            performed = source.Performed || analyzedPages > 0,
            source_check_performed = source.Performed,
            raster_check_performed = analyzedPages > 0,
            raster_check_reason = raster is null ? "png_not_requested"
                : analyzedPages == 0 ? "png_decode_unavailable" : null,
            equation_count = source.EquationCount,
            raw_control_syntax_count = source.RawControlSyntaxCount,
            equation_indexes = source.EquationIndexes,
            analyzed_page_count = analyzedPages,
            unreadable_page_count = unreadablePages,
            page_numbers = pages.ToArray(),
            risk_codes = risks.ToArray(),
            subjective_visual_review_required = true,
            raw_equation_text_returned = false,
            raw_pixels_returned = false,
        };
    }

    private static bool IsRawControlSyntax(string text, string wordOpenXml)
    {
        try
        {
            var document = XDocument.Parse(wordOpenXml, LoadOptions.None);
            var matchingTextNodes = document.Descendants()
                .Where(element => element.Name.LocalName == "t"
                    && RawControlSyntax.IsMatch(element.Value))
                .ToArray();
            if (matchingTextNodes.Length > 0)
            {
                return matchingTextNodes.Any(element => !element.Ancestors().Any(
                    ancestor => ancestor.Name.LocalName is "eqArr" or "m"
                ));
            }
            if (!RawControlSyntax.IsMatch(text)) return false;
            return !document.Descendants().Any(
                element => element.Name.LocalName is "eqArr" or "m"
            );
        }
        catch { return RawControlSyntax.IsMatch(text); }
    }

    private static bool TryBounds(string path, out int width, out int height,
        out int minX, out int maxX, out int minY, out int maxY)
    {
        width = height = 0; minX = minY = int.MaxValue; maxX = maxY = -1;
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (!reader.ReadBytes(PngSignature.Length).SequenceEqual(PngSignature)) return false;
            using var compressed = new MemoryStream();
            byte bitDepth = 0, colorType = 0, interlace = 1;
            while (stream.Position < stream.Length)
            {
                var length = ReadBigEndianInt32(reader);
                if (length < 0 || length > 128 * 1024 * 1024) return false;
                var typeBytes = reader.ReadBytes(4);
                if (typeBytes.Length != 4) return false;
                var type = Encoding.ASCII.GetString(typeBytes);
                var data = reader.ReadBytes(length);
                if (data.Length != length || reader.ReadBytes(4).Length != 4) return false;
                if (type == "IHDR")
                {
                    if (length != 13) return false;
                    width = ReadBigEndianInt32(data, 0); height = ReadBigEndianInt32(data, 4);
                    bitDepth = data[8]; colorType = data[9]; interlace = data[12];
                }
                else if (type == "IDAT")
                {
                    if (compressed.Length + data.Length > MaximumDecodedBytes) return false;
                    compressed.Write(data);
                }
                else if (type == "IEND") break;
            }
            if (width is < 1 or > MaximumDimension || height is < 1 or > MaximumDimension
                || bitDepth != 8 || colorType is not (2 or 6) || interlace != 0) return false;
            var bytesPerPixel = colorType == 6 ? 4 : 3;
            var stride = checked(width * bytesPerPixel);
            if (checked((long)(stride + 1) * height) > MaximumDecodedBytes) return false;
            compressed.Position = 0;
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            var previous = new byte[stride];
            var scanline = new byte[stride + 1];
            for (var y = 0; y < height; y++)
            {
                if (!ReadExactly(zlib, scanline) || scanline[0] > 4) return false;
                var filter = scanline[0];
                var row = new byte[stride];
                for (var offset = 0; offset < stride; offset++)
                {
                    var value = scanline[offset + 1];
                    var left = offset >= bytesPerPixel ? row[offset - bytesPerPixel] : 0;
                    var above = previous[offset];
                    var upperLeft = offset >= bytesPerPixel ? previous[offset - bytesPerPixel] : 0;
                    row[offset] = filter switch
                    {
                        0 => value,
                        1 => unchecked((byte)(value + left)),
                        2 => unchecked((byte)(value + above)),
                        3 => unchecked((byte)(value + ((left + above) / 2))),
                        4 => unchecked((byte)(value + Paeth(left, above, upperLeft))),
                        _ => value,
                    };
                }
                for (var x = 0; x < width; x++)
                {
                    var offset = x * bytesPerPixel;
                    var alpha = colorType == 6 ? row[offset + 3] : (byte)255;
                    if (alpha == 0) continue;
                    if (CompositeOnWhite(row[offset], alpha) < 245
                        || CompositeOnWhite(row[offset + 1], alpha) < 245
                        || CompositeOnWhite(row[offset + 2], alpha) < 245)
                    {
                        minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    }
                }
                previous = row;
            }
            return zlib.ReadByte() == -1 && maxX >= 0;
        }
        catch { return false; }
    }

    private static int CompositeOnWhite(byte component, byte alpha) =>
        255 - ((255 - component) * alpha / 255);
    private static bool ReadExactly(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }
    private static int ReadBigEndianInt32(BinaryReader reader)
    {
        var value = reader.ReadBytes(4);
        return value.Length == 4 ? BinaryPrimitives.ReadInt32BigEndian(value) : -1;
    }
    private static int ReadBigEndianInt32(byte[] value, int offset) =>
        BinaryPrimitives.ReadInt32BigEndian(value.AsSpan(offset, 4));
    private static int Paeth(int left, int above, int upperLeft)
    {
        var estimate = left + above - upperLeft;
        var dl = Math.Abs(estimate - left); var da = Math.Abs(estimate - above);
        var du = Math.Abs(estimate - upperLeft);
        return dl <= da && dl <= du ? left : da <= du ? above : upperLeft;
    }
    private static void FinalRelease(object? value)
    {
        if (value is null || !Marshal.IsComObject(value)) return;
        try { Marshal.FinalReleaseComObject(value); }
        catch (InvalidComObjectException) { }
    }
}
