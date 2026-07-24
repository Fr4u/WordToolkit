using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Packaging;

public sealed record FlatOpcPackageStatistics(
    int PartCount,
    int XmlPartCount,
    int BinaryPartCount,
    long TotalPartBytes
);

/// <summary>
/// Converts between an OPC ZIP package and Microsoft's single-XML Flat OPC
/// representation without depending on Word automation or the Open XML SDK.
/// Input is decoded under the same package limits as ZIP OPC input, and the
/// reconstructed package is always re-read by <see cref="OpcPackageReader"/>.
/// </summary>
public sealed class FlatOpcPackageCodec
{
    public const string Namespace =
        "http://schemas.microsoft.com/office/2006/xmlPackage";

    private const string ContentTypesNamespace =
        "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string RelationshipsContentType =
        "application/vnd.openxmlformats-package.relationships+xml";
    private const string AltChunkRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/aFChunk";
    private const int MaximumAttributeCharacters = 8 * 1024;

    private static readonly XNamespace Pkg = Namespace;
    private readonly OpcPackageLimits _limits;
    private readonly OpcPackageReader _reader;

    public FlatOpcPackageCodec(OpcPackageLimits? limits = null)
    {
        _limits = limits ?? OpcPackageLimits.Default;
        _limits.Validate();
        _reader = new OpcPackageReader(_limits);
    }

    public OpcPackageSnapshot Read(
        Stream flatOpcSource,
        CancellationToken cancellationToken = default
    )
    {
        using var package = new MemoryStream();
        _ = ConvertToPackage(flatOpcSource, package, cancellationToken);
        package.Position = 0;
        return _reader.Read(package, cancellationToken);
    }

    public FlatOpcPackageStatistics ConvertToPackage(
        Stream flatOpcSource,
        Stream packageDestination,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(flatOpcSource);
        ArgumentNullException.ThrowIfNull(packageDestination);
        if (!flatOpcSource.CanRead)
        {
            throw new ArgumentException(
                "Flat OPC source stream must be readable.",
                nameof(flatOpcSource)
            );
        }
        ValidateEmptyWritableDestination(packageDestination, nameof(packageDestination));

        var parts = DecodeParts(flatOpcSource, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        WritePackage(packageDestination, parts, cancellationToken);
        return Statistics(parts);
    }

    public FlatOpcPackageStatistics Write(
        Stream flatOpcDestination,
        OpcPackageSnapshot package,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(flatOpcDestination);
        ArgumentNullException.ThrowIfNull(package);
        ValidateEmptyWritableDestination(flatOpcDestination, nameof(flatOpcDestination));
        if (!package.IsStructurallyValid)
        {
            throw new InvalidDataException(
                "A package with structural OPC errors cannot be exported as Flat OPC."
            );
        }

        var entries = package.Entries.Where(entry =>
                !entry.IsDirectory
                && !string.Equals(
                    entry.Name,
                    OpcPartUri.ContentTypesEntryName,
                    StringComparison.Ordinal
                )
            )
            .ToArray();
        if (entries.Length > _limits.MaxEntries)
        {
            throw new OpcPackageLimitException(
                $"Flat OPC export has {entries.Length} parts; limit is {_limits.MaxEntries}."
            );
        }

        var altChunkTargets = package.Relationships.Where(relationship =>
                relationship.TargetMode == OpcRelationshipTargetMode.Internal
                && string.Equals(
                    relationship.Type,
                    AltChunkRelationshipType,
                    StringComparison.Ordinal
                )
                && relationship.ResolvedTargetPartUri is not null
            )
            .Select(relationship => relationship.ResolvedTargetPartUri!)
            .ToHashSet(StringComparer.Ordinal);

        var statistics = WriteFlatOpc(
            flatOpcDestination,
            package,
            entries,
            altChunkTargets,
            cancellationToken
        );
        return statistics;
    }

    private IReadOnlyList<DecodedPart> DecodeParts(
        Stream source,
        CancellationToken cancellationToken
    )
    {
        var parts = new List<DecodedPart>();
        var rawNames = new HashSet<string>(StringComparer.Ordinal);
        var canonicalUris = new HashSet<string>(StringComparer.Ordinal);
        var caseInsensitiveUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;

        using var cancellationSource = new CancellationReadStream(
            source,
            cancellationToken
        );
        using var reader = XmlReader.Create(
            cancellationSource,
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = _limits.MaxFlatOpcXmlCharacters,
                MaxCharactersFromEntities = 0,
                IgnoreComments = false,
                IgnoreProcessingInstructions = false,
                IgnoreWhitespace = false,
                CloseInput = false,
            }
        );

        try
        {
            reader.MoveToContent();
            RequireElement(reader, Pkg + "package", "Flat OPC root must be pkg:package.");
            ValidatePackageAttributes(reader);
            var rootDepth = reader.Depth;
            if (reader.IsEmptyElement)
            {
                reader.Read();
                return parts;
            }

            reader.Read();
            while (!(reader.NodeType == XmlNodeType.EndElement && reader.Depth == rootDepth))
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureDepth(reader);
                if (IsIgnorableContainerNode(reader))
                {
                    reader.Read();
                    continue;
                }
                RequireElement(
                    reader,
                    Pkg + "part",
                    "Flat OPC package contains a node other than pkg:part."
                );
                if (reader.Depth != rootDepth + 1)
                {
                    throw new InvalidDataException(
                        "pkg:part must be a direct child of pkg:package."
                    );
                }
                if (parts.Count >= _limits.MaxEntries)
                {
                    throw new OpcPackageLimitException(
                        $"Flat OPC part count exceeds {_limits.MaxEntries}."
                    );
                }

                var part = DecodePart(reader, cancellationToken);
                if (!rawNames.Add(part.RawName))
                {
                    throw new InvalidDataException(
                        $"Flat OPC contains duplicate part name '{part.RawName}'."
                    );
                }
                if (!canonicalUris.Add(part.PartUri))
                {
                    throw new InvalidDataException(
                        $"Flat OPC part names collapse to duplicate URI '{part.PartUri}'."
                    );
                }
                if (!caseInsensitiveUris.Add(part.PartUri))
                {
                    throw new InvalidDataException(
                        $"Flat OPC part URI '{part.PartUri}' collides by case."
                    );
                }

                checked
                {
                    totalBytes += part.Content.Length;
                }
                if (totalBytes > _limits.MaxTotalUncompressedBytes)
                {
                    throw new OpcPackageLimitException(
                        "Flat OPC decoded parts exceed the total uncompressed-byte limit."
                    );
                }
                parts.Add(part);
            }

            reader.Read();
            while (!reader.EOF)
            {
                if (!IsIgnorableContainerNode(reader))
                {
                    throw new InvalidDataException(
                        "Flat OPC contains content after pkg:package."
                    );
                }
                reader.Read();
            }
            return parts;
        }
        catch (OpcPackageLimitException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is XmlException
                or FormatException
                or OverflowException
                or LosslessXmlException
        )
        {
            throw new InvalidDataException(
                $"Flat OPC is not safe, bounded, well-formed package XML: {exception.Message}",
                exception
            );
        }
    }

    private DecodedPart DecodePart(
        XmlReader reader,
        CancellationToken cancellationToken
    )
    {
        var rawName = RequiredPkgAttribute(reader, "name");
        var contentType = RequiredPkgAttribute(reader, "contentType");
        ValidatePartAttributes(reader);
        ValidatePartName(rawName, out var partUri, out var entryName);
        ValidateContentType(contentType);
        var compression = OptionalPkgAttribute(reader, "compression");
        ValidateCompression(compression);

        var partDepth = reader.Depth;
        if (reader.IsEmptyElement)
        {
            throw new InvalidDataException(
                $"Flat OPC part '{rawName}' has no pkg:xmlData or pkg:binaryData."
            );
        }
        reader.Read();
        SkipIgnorable(reader);
        EnsureDepth(reader);
        if (reader.Depth != partDepth + 1 || reader.NodeType != XmlNodeType.Element)
        {
            throw new InvalidDataException(
                $"Flat OPC part '{rawName}' has invalid payload structure."
            );
        }

        bool xml;
        byte[] content;
        if (reader.LocalName == "xmlData" && reader.NamespaceURI == Namespace)
        {
            xml = true;
            content = DecodeXmlData(reader, rawName, cancellationToken);
        }
        else if (reader.LocalName == "binaryData" && reader.NamespaceURI == Namespace)
        {
            xml = false;
            content = DecodeBinaryData(reader, rawName, cancellationToken);
        }
        else
        {
            throw new InvalidDataException(
                $"Flat OPC part '{rawName}' payload must be pkg:xmlData or pkg:binaryData."
            );
        }

        SkipIgnorable(reader);
        if (reader.NodeType != XmlNodeType.EndElement || reader.Depth != partDepth)
        {
            throw new InvalidDataException(
                $"Flat OPC part '{rawName}' contains more than one payload."
            );
        }
        reader.Read();
        return new DecodedPart(
            rawName,
            partUri!,
            entryName!,
            contentType,
            compression,
            xml,
            content
        );
    }

    private byte[] DecodeXmlData(
        XmlReader reader,
        string partName,
        CancellationToken cancellationToken
    )
    {
        ValidateDataElementAttributes(reader, "xmlData", partName);
        var dataDepth = reader.Depth;
        if (reader.IsEmptyElement)
        {
            throw new InvalidDataException(
                $"Flat OPC XML part '{partName}' has empty pkg:xmlData."
            );
        }
        reader.Read();
        SkipIgnorable(reader);
        if (reader.NodeType != XmlNodeType.Element || reader.Depth != dataDepth + 1)
        {
            throw new InvalidDataException(
                $"Flat OPC XML part '{partName}' must contain exactly one root element."
            );
        }

        using var buffer = new BoundedMemoryStream(_limits.MaxEntryUncompressedBytes);
        using (
            var writer = XmlWriter.Create(
                buffer,
                new XmlWriterSettings
                {
                    Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    OmitXmlDeclaration = true,
                    CloseOutput = false,
                    CheckCharacters = true,
                }
            )
        )
        {
            writer.WriteNode(reader, defattr: true);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var content = buffer.ToArray();
        _ = LosslessXmlDocument.Parse(
            content,
            new LosslessXmlOptions
            {
                MaxSourceBytes = CheckedIntLimit(_limits.MaxEntryUncompressedBytes),
                MaxXmlCharacters = _limits.MaxMetadataXmlCharacters,
                MaxXmlElements = _limits.MaxMetadataXmlElements,
                MaxXmlDepth = _limits.MaxFlatOpcXmlDepth,
                MaxTextCharacters = _limits.MaxMetadataXmlCharacters,
            },
            cancellationToken
        );

        SkipIgnorable(reader);
        if (reader.NodeType != XmlNodeType.EndElement || reader.Depth != dataDepth)
        {
            throw new InvalidDataException(
                $"Flat OPC XML part '{partName}' contains more than one root element."
            );
        }
        reader.Read();
        return content;
    }

    private byte[] DecodeBinaryData(
        XmlReader reader,
        string partName,
        CancellationToken cancellationToken
    )
    {
        ValidateDataElementAttributes(reader, "binaryData", partName);
        using var destination = new BoundedMemoryStream(
            _limits.MaxEntryUncompressedBytes
        );
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return destination.ToArray();
        }

        var depth = reader.Depth;
        reader.Read();
        var buffer = new byte[64 * 1024];
        while (!(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element)
            {
                throw new InvalidDataException(
                    $"Flat OPC binary part '{partName}' contains nested XML."
                );
            }
            var read = reader.ReadContentAsBase64(buffer, 0, buffer.Length);
            if (read == 0)
            {
                if (!reader.Read())
                {
                    throw new XmlException(
                        $"Flat OPC binary part '{partName}' is not closed."
                    );
                }
                continue;
            }
            destination.Write(buffer, 0, read);
        }
        reader.Read();
        return destination.ToArray();
    }

    private void WritePackage(
        Stream destination,
        IReadOnlyList<DecodedPart> parts,
        CancellationToken cancellationToken
    )
    {
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        WriteZipEntry(
            archive,
            OpcPartUri.ContentTypesEntryName,
            BuildContentTypes(parts),
            CompressionLevel.Optimal
        );
        foreach (var part in parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compression = string.Equals(
                part.Compression,
                "store",
                StringComparison.Ordinal
            )
                ? CompressionLevel.NoCompression
                : CompressionLevel.Optimal;
            WriteZipEntry(archive, part.EntryName, part.Content, compression);
        }
    }

    private FlatOpcPackageStatistics WriteFlatOpc(
        Stream destination,
        OpcPackageSnapshot package,
        IReadOnlyList<OpcPackageEntry> entries,
        IReadOnlySet<string> altChunkTargets,
        CancellationToken cancellationToken
    )
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false,
            Indent = false,
            NewLineChars = "\n",
            CloseOutput = false,
            CheckCharacters = true,
        };
        var xmlCount = 0;
        var binaryCount = 0;
        long totalBytes = 0;
        using var writer = XmlWriter.Create(destination, settings);
        writer.WriteStartDocument(standalone: true);
        writer.WriteProcessingInstruction(
            "mso-application",
            $"progid=\"{InferProgramId(package)}\""
        );
        writer.WriteStartElement("pkg", "package", Namespace);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.PartUri is null)
            {
                throw new InvalidDataException(
                    $"Package entry '{entry.Name}' has no valid OPC part URI."
                );
            }
            var contentType = entry.IsInfrastructure
                ? RelationshipsContentType
                : package.ContentTypes.Resolve(entry.PartUri);
            if (string.IsNullOrWhiteSpace(contentType))
            {
                throw new InvalidDataException(
                    $"Package part '{entry.PartUri}' has no content type."
                );
            }
            ValidateContentType(contentType);
            checked
            {
                totalBytes += entry.Content.Length;
            }
            if (totalBytes > _limits.MaxTotalUncompressedBytes)
            {
                throw new OpcPackageLimitException(
                    "Flat OPC export parts exceed the total uncompressed-byte limit."
                );
            }

            writer.WriteStartElement("pkg", "part", Namespace);
            writer.WriteAttributeString("pkg", "name", Namespace, "/" + entry.Name);
            writer.WriteAttributeString("pkg", "contentType", Namespace, contentType);
            if (
                ShouldWriteXml(entry, contentType, altChunkTargets)
                && TryParseXml(entry.Content, cancellationToken, out var document)
            )
            {
                xmlCount++;
                writer.WriteStartElement("pkg", "xmlData", Namespace);
                document!.Root!.WriteTo(writer);
                writer.WriteEndElement();
            }
            else
            {
                binaryCount++;
                writer.WriteAttributeString("pkg", "compression", Namespace, "store");
                writer.WriteStartElement("pkg", "binaryData", Namespace);
                WriteBase64(writer, entry.Content, cancellationToken);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndDocument();
        writer.Flush();
        return new FlatOpcPackageStatistics(
            entries.Count,
            xmlCount,
            binaryCount,
            totalBytes
        );
    }

    private bool TryParseXml(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken,
        out XDocument? document
    )
    {
        document = null;
        try
        {
            var parsed = LosslessXmlDocument.Parse(
                content,
                new LosslessXmlOptions
                {
                    MaxSourceBytes = CheckedIntLimit(_limits.MaxEntryUncompressedBytes),
                    MaxXmlCharacters = _limits.MaxMetadataXmlCharacters,
                    MaxXmlElements = _limits.MaxMetadataXmlElements,
                    MaxXmlDepth = _limits.MaxFlatOpcXmlDepth,
                    MaxTextCharacters = _limits.MaxMetadataXmlCharacters,
                },
                cancellationToken
            );
            document = parsed.ParsedDocument;
            return document.Root is not null;
        }
        catch (LosslessXmlException)
        {
            return false;
        }
    }

    private static bool ShouldWriteXml(
        OpcPackageEntry entry,
        string contentType,
        IReadOnlySet<string> altChunkTargets
    ) =>
        contentType.EndsWith("xml", StringComparison.Ordinal)
        && !altChunkTargets.Contains(entry.PartUri!);

    private byte[] BuildContentTypes(IReadOnlyList<DecodedPart> parts)
    {
        if (parts.Count > _limits.MaxContentTypeDeclarations)
        {
            throw new OpcPackageLimitException(
                $"Flat OPC requires {parts.Count} content-type declarations; limit is {_limits.MaxContentTypeDeclarations}."
            );
        }
        using var stream = new MemoryStream();
        using (
            var writer = XmlWriter.Create(
                stream,
                new XmlWriterSettings
                {
                    Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    OmitXmlDeclaration = false,
                    CloseOutput = false,
                }
            )
        )
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("Types", ContentTypesNamespace);
            foreach (var part in parts)
            {
                writer.WriteStartElement("Override", ContentTypesNamespace);
                writer.WriteAttributeString("PartName", part.RawName);
                writer.WriteAttributeString("ContentType", part.ContentType);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }
        return stream.ToArray();
    }

    private static void WriteZipEntry(
        ZipArchive archive,
        string name,
        ReadOnlyMemory<byte> content,
        CompressionLevel compression
    )
    {
        var entry = archive.CreateEntry(name, compression);
        entry.LastWriteTime = OpcPackageSerializer.DeterministicTimestamp;
        entry.ExternalAttributes = 0;
        using var stream = entry.Open();
        stream.Write(content.Span);
    }

    private static void WriteBase64(
        XmlWriter writer,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken
    )
    {
        const int chunkSize = 48 * 1024;
        if (!MemoryMarshal.TryGetArray(content, out ArraySegment<byte> segment))
        {
            segment = new ArraySegment<byte>(content.ToArray());
        }
        var offset = 0;
        while (offset < content.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = Math.Min(chunkSize, content.Length - offset);
            writer.WriteBase64(segment.Array!, segment.Offset + offset, length);
            offset += length;
        }
    }

    private static FlatOpcPackageStatistics Statistics(
        IReadOnlyCollection<DecodedPart> parts
    ) => new(
        parts.Count,
        parts.Count(part => part.IsXml),
        parts.Count(part => !part.IsXml),
        parts.Sum(part => (long)part.Content.Length)
    );

    private static string InferProgramId(OpcPackageSnapshot package)
    {
        var main = package.RelationshipsFrom(OpcPartUri.PackageRoot)
            .FirstOrDefault(relationship =>
                relationship.TargetMode == OpcRelationshipTargetMode.Internal
                && relationship.Type.EndsWith(
                    "/officeDocument",
                    StringComparison.Ordinal
                )
            )
            ?.ResolvedTargetPartUri;
        var contentType = main is null ? null : package.ContentTypes.Resolve(main);
        if (contentType?.Contains("macroEnabledTemplate", StringComparison.Ordinal) == true)
        {
            return "Word.TemplateMacroEnabled.12";
        }
        if (contentType?.Contains("macroEnabled", StringComparison.Ordinal) == true)
        {
            return "Word.DocumentMacroEnabled.12";
        }
        if (contentType?.Contains("template.main+xml", StringComparison.Ordinal) == true)
        {
            return "Word.Template";
        }
        return "Word.Document";
    }

    private static void ValidatePackageAttributes(XmlReader reader)
    {
        foreach (var attribute in Attributes(reader))
        {
            if (!attribute.IsNamespaceDeclaration)
            {
                throw new InvalidDataException(
                    $"pkg:package contains unsupported attribute '{attribute.DisplayName}'."
                );
            }
        }
    }

    private static void ValidatePartAttributes(XmlReader reader)
    {
        foreach (var attribute in Attributes(reader))
        {
            if (attribute.IsNamespaceDeclaration)
            {
                continue;
            }
            if (
                attribute.NamespaceUri == Namespace
                && attribute.LocalName is "name" or "contentType" or "compression" or "padding"
            )
            {
                if (attribute.Value.Length > MaximumAttributeCharacters)
                {
                    throw new OpcPackageLimitException(
                        $"Flat OPC attribute '{attribute.LocalName}' exceeds {MaximumAttributeCharacters} characters."
                    );
                }
                if (
                    attribute.LocalName == "padding"
                    && (!long.TryParse(attribute.Value, out var padding) || padding < 0)
                )
                {
                    throw new InvalidDataException(
                        "Flat OPC pkg:padding must be a non-negative integer."
                    );
                }
                continue;
            }
            throw new InvalidDataException(
                $"pkg:part contains unsupported attribute '{attribute.DisplayName}'."
            );
        }
    }

    private static void ValidateDataElementAttributes(
        XmlReader reader,
        string elementName,
        string partName
    )
    {
        foreach (var attribute in Attributes(reader))
        {
            if (!attribute.IsNamespaceDeclaration)
            {
                throw new InvalidDataException(
                    $"Flat OPC {elementName} for '{partName}' contains unsupported attribute '{attribute.DisplayName}'."
                );
            }
        }
    }

    private static IReadOnlyList<XmlAttributeValue> Attributes(XmlReader reader)
    {
        if (!reader.HasAttributes)
        {
            return Array.Empty<XmlAttributeValue>();
        }
        var attributes = new List<XmlAttributeValue>(reader.AttributeCount);
        while (reader.MoveToNextAttribute())
        {
            attributes.Add(
                new XmlAttributeValue(
                    reader.LocalName,
                    reader.NamespaceURI,
                    reader.Value,
                    reader.Prefix == "xmlns"
                        || (reader.Prefix.Length == 0 && reader.LocalName == "xmlns"),
                    reader.Name
                )
            );
        }
        reader.MoveToElement();
        return attributes;
    }

    private static string RequiredPkgAttribute(XmlReader reader, string localName)
    {
        var value = reader.GetAttribute(localName, Namespace);
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidDataException(
                $"pkg:part is missing required pkg:{localName}."
            );
        }
        if (value.Length > MaximumAttributeCharacters)
        {
            throw new OpcPackageLimitException(
                $"Flat OPC pkg:{localName} exceeds {MaximumAttributeCharacters} characters."
            );
        }
        return value;
    }

    private static string? OptionalPkgAttribute(XmlReader reader, string localName) =>
        reader.GetAttribute(localName, Namespace);

    private static void ValidatePartName(
        string rawName,
        out string? partUri,
        out string? entryName
    )
    {
        partUri = null;
        entryName = null;
        if (!rawName.StartsWith("/", StringComparison.Ordinal) || rawName.Length == 1)
        {
            throw new InvalidDataException(
                $"Flat OPC part name '{rawName}' must be a rooted OPC part URI."
            );
        }
        entryName = rawName[1..];
        if (!OpcPartUri.TryFromEntryName(entryName, out partUri, out var error))
        {
            throw new InvalidDataException(
                $"Flat OPC part name '{rawName}' is invalid: {error}"
            );
        }
        if (
            string.Equals(
                entryName,
                OpcPartUri.ContentTypesEntryName,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new InvalidDataException(
                "Flat OPC must express content types on pkg:part, not as [Content_Types].xml."
            );
        }
    }

    private static void ValidateContentType(string contentType)
    {
        if (
            contentType.Length > MaximumAttributeCharacters
            || !MediaTypeHeaderValue.TryParse(contentType, out var parsed)
            || string.IsNullOrWhiteSpace(parsed.MediaType)
            || !parsed.MediaType.Contains('/', StringComparison.Ordinal)
        )
        {
            throw new InvalidDataException(
                $"Flat OPC content type '{contentType}' is invalid."
            );
        }
    }

    private static void ValidateCompression(string? compression)
    {
        if (
            compression is not null
            && compression is not ("store" or "normal" or "maximum" or "fast" or "superFast")
        )
        {
            throw new InvalidDataException(
                $"Flat OPC compression value '{compression}' is unsupported."
            );
        }
    }

    private void EnsureDepth(XmlReader reader)
    {
        if (reader.Depth > _limits.MaxFlatOpcXmlDepth)
        {
            throw new OpcPackageLimitException(
                $"Flat OPC XML depth exceeds {_limits.MaxFlatOpcXmlDepth}."
            );
        }
    }

    private static void RequireElement(
        XmlReader reader,
        XName expected,
        string message
    )
    {
        if (
            reader.NodeType != XmlNodeType.Element
            || reader.LocalName != expected.LocalName
            || reader.NamespaceURI != expected.NamespaceName
        )
        {
            throw new InvalidDataException(message);
        }
    }

    private static bool IsIgnorableContainerNode(XmlReader reader) =>
        reader.NodeType
            is XmlNodeType.Whitespace
                or XmlNodeType.SignificantWhitespace
                or XmlNodeType.Comment
                or XmlNodeType.ProcessingInstruction;

    private static void SkipIgnorable(XmlReader reader)
    {
        while (IsIgnorableContainerNode(reader))
        {
            reader.Read();
        }
    }

    private static int CheckedIntLimit(long value) =>
        value > int.MaxValue ? int.MaxValue : checked((int)value);

    private static void ValidateEmptyWritableDestination(
        Stream destination,
        string parameterName
    )
    {
        if (!destination.CanWrite)
        {
            throw new ArgumentException(
                "Destination stream must be writable.",
                parameterName
            );
        }
        if (destination.CanSeek && (destination.Position != 0 || destination.Length != 0))
        {
            throw new ArgumentException(
                "Destination stream must be empty and positioned at zero.",
                parameterName
            );
        }
    }

    private sealed record DecodedPart(
        string RawName,
        string PartUri,
        string EntryName,
        string ContentType,
        string? Compression,
        bool IsXml,
        byte[] Content
    );

    private sealed record XmlAttributeValue(
        string LocalName,
        string NamespaceUri,
        string Value,
        bool IsNamespaceDeclaration,
        string DisplayName
    );

    private sealed class BoundedMemoryStream : MemoryStream
    {
        private readonly long _maximumLength;

        public BoundedMemoryStream(long maximumLength)
        {
            _maximumLength = maximumLength;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCapacity(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCapacity(buffer.Length);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            EnsureCapacity(1);
            base.WriteByte(value);
        }

        private void EnsureCapacity(int additionalBytes)
        {
            if (Length > _maximumLength - additionalBytes)
            {
                throw new OpcPackageLimitException(
                    $"Flat OPC part exceeds {_maximumLength} decoded bytes."
                );
            }
        }
    }

    private sealed class CancellationReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly CancellationToken _cancellationToken;

        public CancellationReadStream(
            Stream inner,
            CancellationToken cancellationToken
        )
        {
            _inner = inner;
            _cancellationToken = cancellationToken;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            return _inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            return _inner.Read(buffer);
        }

        public override int ReadByte()
        {
            _cancellationToken.ThrowIfCancellationRequested();
            return _inner.ReadByte();
        }

        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) =>
            _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
