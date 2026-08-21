using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using WordToolkit.Engine.Packaging;

namespace WordToolkit.Engine.Tests;

public sealed class FlatOpcPackageCodecTests
{
    private const string Pkg = FlatOpcPackageCodec.Namespace;
    private static readonly JsonSerializerOptions CorpusJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    [Fact]
    public void PublishedCorruptionCorpusFailsClosedBeforeDestinationMutation()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(
            root,
            "native",
            "WordToolkit.Engine.Tests",
            "Corpus",
            "flat-opc-corruption-v1.json"
        );
        var corpus = JsonSerializer.Deserialize<FlatOpcCorruptionCorpus>(
            File.ReadAllText(path, Encoding.UTF8),
            CorpusJsonOptions
        );
        Assert.NotNull(corpus);
        Assert.Equal("wordtoolkit.flat_opc_corruption_corpus/1.0", corpus.Schema);
        Assert.Equal(Pkg, corpus.Namespace);
        Assert.Equal(13, corpus.Cases.Count);
        Assert.Equal(13, corpus.Cases.Select(item => item.Id).Distinct().Count());
        Assert.All(corpus.Cases, item =>
        {
            using var source = Utf8(item.Xml);
            using var destination = new MemoryStream();
            Assert.Throws<InvalidDataException>(() =>
                new FlatOpcPackageCodec().ConvertToPackage(source, destination)
            );
            Assert.Equal(0, destination.Length);
        });
    }

    [Fact]
    public void ExportRoundTripsXmlBinaryRelationshipsAndOfficialSdk()
    {
        using var source = BuildWordPackage();
        var reader = new OpcPackageReader();
        var baseline = reader.Read(source);
        var codec = new FlatOpcPackageCodec();

        using var flat = new MemoryStream();
        var written = codec.Write(flat, baseline);

        Assert.Equal(4, written.PartCount);
        Assert.Equal(3, written.XmlPartCount);
        Assert.Equal(1, written.BinaryPartCount);
        flat.Position = 0;
        var xml = XDocument.Load(flat, LoadOptions.PreserveWhitespace);
        Assert.Equal(XName.Get("package", Pkg), xml.Root!.Name);
        Assert.Equal(
            "store",
            xml.Descendants(XName.Get("part", Pkg))
                .Single(part => (string?)part.Attribute(XName.Get("name", Pkg)) == "/word/media/image1.png")
                .Attribute(XName.Get("compression", Pkg))!
                .Value
        );

        using (var sdkDocument = WordprocessingDocument.FromFlatOpcDocument(xml))
        {
            Assert.NotNull(sdkDocument.MainDocumentPart);
            Assert.Single(sdkDocument.MainDocumentPart!.ImageParts);
        }

        flat.Position = 0;
        var roundTrip = codec.Read(flat);
        Assert.True(roundTrip.IsStructurallyValid);
        Assert.Equal(
            baseline.Parts["/word/media/image1.png"].Entry.Sha256,
            roundTrip.Parts["/word/media/image1.png"].Entry.Sha256
        );
        Assert.Equal(
            baseline.Relationships.Select(RelationshipIdentity).Order().ToArray(),
            roundTrip.Relationships.Select(RelationshipIdentity).Order().ToArray()
        );
        Assert.Contains(
            "Flat OPC",
            Encoding.UTF8.GetString(
                roundTrip.Parts["/word/document.xml"].Entry.Content.Span
            ),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void ImportsFlatOpcProducedByOfficialSdk()
    {
        using var source = BuildWordPackage();
        using var document = WordprocessingDocument.Open(source, false);
        var officialFlatOpc = document.ToFlatOpcDocument();
        using var flat = new MemoryStream();
        officialFlatOpc.Save(flat);
        flat.Position = 0;

        var imported = new FlatOpcPackageCodec().Read(flat);

        Assert.True(imported.IsStructurallyValid);
        Assert.Equal(2, imported.Parts.Count);
        Assert.Single(
            imported.Relationships,
            relationship =>
                relationship.SourcePartUri == "/word/document.xml"
                && relationship.ResolvedTargetPartUri == "/word/media/image1.png"
        );
    }

    [Fact]
    public void XmlTypedAltChunkIsExportedAsBinary()
    {
        using var source = BuildWordPackage(includeAltChunk: true);
        var package = new OpcPackageReader().Read(source);
        using var flat = new MemoryStream();

        new FlatOpcPackageCodec().Write(flat, package);

        flat.Position = 0;
        var xml = XDocument.Load(flat);
        var altChunk = xml.Descendants(XName.Get("part", Pkg))
            .Single(part => (string?)part.Attribute(XName.Get("name", Pkg)) == "/word/altChunk.xhtml");
        Assert.NotNull(altChunk.Element(XName.Get("binaryData", Pkg)));
        Assert.Null(altChunk.Element(XName.Get("xmlData", Pkg)));
    }

    [Theory]
    [InlineData("<package/>")]
    [InlineData("<pkg:package xmlns:pkg='http://schemas.microsoft.com/office/2006/xmlPackage'><pkg:unknown/></pkg:package>")]
    [InlineData("<pkg:package xmlns:pkg='http://schemas.microsoft.com/office/2006/xmlPackage'><pkg:part pkg:name='/word/document.xml' pkg:contentType='application/xml'/></pkg:package>")]
    [InlineData("<pkg:package xmlns:pkg='http://schemas.microsoft.com/office/2006/xmlPackage'><pkg:part pkg:name='/word/document.xml' pkg:contentType='application/xml'><pkg:xmlData><a/><b/></pkg:xmlData></pkg:part></pkg:package>")]
    [InlineData("<pkg:package xmlns:pkg='http://schemas.microsoft.com/office/2006/xmlPackage'><pkg:part pkg:name='/word/document.xml' pkg:contentType='application/xml'><pkg:xmlData><a/></pkg:xmlData><pkg:binaryData/></pkg:part></pkg:package>")]
    [InlineData("<pkg:package xmlns:pkg='http://schemas.microsoft.com/office/2006/xmlPackage'><pkg:part pkg:name='/[Content_Types].xml' pkg:contentType='application/xml'><pkg:xmlData><a/></pkg:xmlData></pkg:part></pkg:package>")]
    public void RejectsMalformedFlatOpcStructure(string xml)
    {
        using var source = Utf8(xml);
        using var destination = new MemoryStream();

        Assert.Throws<InvalidDataException>(() =>
            new FlatOpcPackageCodec().ConvertToPackage(source, destination)
        );
    }

    [Fact]
    public void RejectsDtdBeforePublishingPackageBytes()
    {
        using var source = Utf8(
            "<!DOCTYPE pkg:package [<!ENTITY x 'poison'>]>"
                + "<pkg:package xmlns:pkg='http://schemas.microsoft.com/office/2006/xmlPackage'>&x;</pkg:package>"
        );
        using var destination = new MemoryStream();

        Assert.Throws<InvalidDataException>(() =>
            new FlatOpcPackageCodec().ConvertToPackage(source, destination)
        );
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public void RejectsInvalidBase64AndCaseCollidingPartUris()
    {
        var invalidBase64 = Flat(
            Part("/word/media/image.png", "image/png", "<pkg:binaryData>%%%not-base64%%%</pkg:binaryData>")
        );
        using (var source = Utf8(invalidBase64))
        using (var destination = new MemoryStream())
        {
            Assert.Throws<InvalidDataException>(() =>
                new FlatOpcPackageCodec().ConvertToPackage(source, destination)
            );
        }

        var collision = Flat(
            Part("/word/A.xml", "application/xml", "<pkg:xmlData><a/></pkg:xmlData>")
                + Part("/word/a.xml", "application/xml", "<pkg:xmlData><a/></pkg:xmlData>")
        );
        using (var source = Utf8(collision))
        using (var destination = new MemoryStream())
        {
            Assert.Throws<InvalidDataException>(() =>
                new FlatOpcPackageCodec().ConvertToPackage(source, destination)
            );
        }
    }

    [Fact]
    public void EnforcesDecodedPartAndTotalByteLimits()
    {
        var limits = new OpcPackageLimits
        {
            MaxEntryUncompressedBytes = 4,
            MaxTotalUncompressedBytes = 6,
        };
        var tooLargePart = Flat(
            Part("/a.bin", "application/octet-stream", "<pkg:binaryData>AQIDBAU=</pkg:binaryData>")
        );
        using (var source = Utf8(tooLargePart))
        using (var destination = new MemoryStream())
        {
            Assert.Throws<OpcPackageLimitException>(() =>
                new FlatOpcPackageCodec(limits).ConvertToPackage(source, destination)
            );
        }

        var tooLargeTotal = Flat(
            Part("/a.bin", "application/octet-stream", "<pkg:binaryData>AQIDBA==</pkg:binaryData>")
                + Part("/b.bin", "application/octet-stream", "<pkg:binaryData>AQIDBA==</pkg:binaryData>")
        );
        using (var source = Utf8(tooLargeTotal))
        using (var destination = new MemoryStream())
        {
            Assert.Throws<OpcPackageLimitException>(() =>
                new FlatOpcPackageCodec(limits).ConvertToPackage(source, destination)
            );
        }
    }

    [Fact]
    public void ConversionWritesDeterministicPackageAndReconstructedContentTypes()
    {
        var flatOpc = Flat(
            Part("/_rels/.rels", "application/vnd.openxmlformats-package.relationships+xml", "<pkg:xmlData><Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'/></pkg:xmlData>")
                + Part("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", "<pkg:xmlData><w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body/></w:document></pkg:xmlData>")
        );
        using var firstSource = Utf8(flatOpc);
        using var first = new MemoryStream();
        using var secondSource = Utf8(flatOpc);
        using var second = new MemoryStream();
        var codec = new FlatOpcPackageCodec();

        codec.ConvertToPackage(firstSource, first);
        codec.ConvertToPackage(secondSource, second);

        Assert.Equal(first.ToArray(), second.ToArray());
        first.Position = 0;
        using var archive = new ZipArchive(first, ZipArchiveMode.Read, leaveOpen: true);
        var manifest = archive.GetEntry("[Content_Types].xml");
        Assert.NotNull(manifest);
        using var manifestReader = new StreamReader(manifest!.Open());
        var text = manifestReader.ReadToEnd();
        Assert.Contains("/word/document.xml", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[Content_Types].xml", text, StringComparison.Ordinal);
    }

    private static string RelationshipIdentity(OpcRelationship relationship) =>
        string.Join(
            "|",
            relationship.SourcePartUri,
            relationship.Id,
            relationship.Type,
            relationship.Target,
            relationship.TargetMode
        );

    private static MemoryStream Utf8(string value) =>
        new(Encoding.UTF8.GetBytes(value), writable: false);

    private static string Flat(string parts) =>
        $"<pkg:package xmlns:pkg='{Pkg}'>{parts}</pkg:package>";

    private static string Part(string name, string contentType, string payload) =>
        $"<pkg:part pkg:name='{name}' pkg:contentType='{contentType}'>{payload}</pkg:part>";

    internal static MemoryStream BuildWordPackage(bool includeAltChunk = false)
    {
        const string contentTypesNamespace =
            "http://schemas.openxmlformats.org/package/2006/content-types";
        const string relationshipsNamespace =
            "http://schemas.openxmlformats.org/package/2006/relationships";
        const string officeRelationships =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(
                archive,
                "[Content_Types].xml",
                $"<Types xmlns='{contentTypesNamespace}'>"
                    + "<Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/>"
                    + "<Default Extension='png' ContentType='image/png'/>"
                    + "<Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/>"
                    + (includeAltChunk
                        ? "<Override PartName='/word/altChunk.xhtml' ContentType='application/xhtml+xml'/>"
                        : string.Empty)
                    + "</Types>"
            );
            Write(
                archive,
                "_rels/.rels",
                $"<Relationships xmlns='{relationshipsNamespace}'>"
                    + $"<Relationship Id='rId1' Type='{officeRelationships}/officeDocument' Target='word/document.xml'/>"
                    + "</Relationships>"
            );
            Write(
                archive,
                "word/document.xml",
                "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'>"
                    + "<w:body><w:p><w:r><w:t>Flat OPC</w:t></w:r></w:p></w:body></w:document>"
            );
            Write(
                archive,
                "word/_rels/document.xml.rels",
                $"<Relationships xmlns='{relationshipsNamespace}'>"
                    + $"<Relationship Id='rIdImage' Type='{officeRelationships}/image' Target='media/image1.png'/>"
                    + (includeAltChunk
                        ? $"<Relationship Id='rIdAlt' Type='{officeRelationships}/aFChunk' Target='altChunk.xhtml'/>"
                        : string.Empty)
                    + "</Relationships>"
            );
            Write(archive, "word/media/image1.png", new byte[] { 137, 80, 78, 71, 1, 2, 3 });
            if (includeAltChunk)
            {
                Write(
                    archive,
                    "word/altChunk.xhtml",
                    "<!DOCTYPE html><html xmlns='http://www.w3.org/1999/xhtml'><body>chunk</body></html>"
                );
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static void Write(ZipArchive archive, string name, string value) =>
        Write(archive, name, Encoding.UTF8.GetBytes(value));

    private static void Write(ZipArchive archive, string name, byte[] value)
    {
        var entry = archive.CreateEntry(name);
        entry.LastWriteTime = OpcPackageSerializer.DeterministicTimestamp;
        using var destination = entry.Open();
        destination.Write(value);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "global.json")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed record FlatOpcCorruptionCorpus(
        string Schema,
        string Namespace,
        IReadOnlyList<FlatOpcCorruptionCase> Cases
    );

    private sealed record FlatOpcCorruptionCase(
        string Id,
        string Category,
        string Xml
    );
}
