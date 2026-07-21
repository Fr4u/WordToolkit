using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace WordToolkit.Engine.Xml;

public sealed class LosslessXmlDocument
{
    private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";
    private readonly byte[] _sourceBytes;
    private readonly SourceEncoding _sourceEncoding;
    private readonly LosslessXmlOptions _options;
    private readonly XDocument _parsedDocument;
    private readonly IReadOnlyDictionary<XElement, int> _elementOrdinals;

    private LosslessXmlDocument(
        byte[] sourceBytes,
        SourceEncoding sourceEncoding,
        LosslessXmlOptions options,
        XDocument parsedDocument,
        IReadOnlyList<XmlSourceElement> elements,
        IReadOnlyDictionary<XElement, int> elementOrdinals
    )
    {
        _sourceBytes = sourceBytes;
        _sourceEncoding = sourceEncoding;
        _options = options;
        _parsedDocument = parsedDocument;
        _elementOrdinals = elementOrdinals;
        Elements = new ReadOnlyCollection<XmlSourceElement>(elements.ToArray());
        Root = Elements.Single(element => element.ParentOrdinal is null);
        SourceSha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
    }

    public ReadOnlyMemory<byte> SourceBytes => _sourceBytes;

    public string SourceSha256 { get; }

    public string EncodingName => _sourceEncoding.Encoding.WebName;

    public int ByteOrderMarkLength => _sourceEncoding.ByteOrderMarkLength;

    public XmlSourceElement Root { get; }

    public IReadOnlyList<XmlSourceElement> Elements { get; }

    internal XDocument ParsedDocument => _parsedDocument;

    public static LosslessXmlDocument Parse(
        ReadOnlyMemory<byte> source,
        LosslessXmlOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        options ??= LosslessXmlOptions.Default;
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (source.IsEmpty)
        {
            throw new LosslessXmlParseException("XML source is empty.");
        }

        if (source.Length > options.MaxSourceBytes)
        {
            throw new LosslessXmlLimitException(
                $"XML source exceeds {options.MaxSourceBytes} bytes."
            );
        }

        var bytes = source.ToArray();
        AuditXml(bytes, options, cancellationToken);
        var sourceEncoding = DetectEncoding(bytes);
        string decoded;
        try
        {
            decoded = sourceEncoding.Encoding.GetString(
                bytes,
                sourceEncoding.ByteOrderMarkLength,
                bytes.Length - sourceEncoding.ByteOrderMarkLength
            );
        }
        catch (DecoderFallbackException exception)
        {
            throw new LosslessXmlEncodingException(
                $"XML bytes are not valid {sourceEncoding.Encoding.WebName}.",
                exception
            );
        }

        if (decoded.Length > options.MaxXmlCharacters)
        {
            throw new LosslessXmlLimitException(
                $"Decoded XML exceeds {options.MaxXmlCharacters} characters."
            );
        }

        XDocument parsedDocument;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(stream, CreateXmlSettings(options));
            parsedDocument = XDocument.Load(
                reader,
                LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo
            );
        }
        catch (XmlException exception)
        {
            throw new LosslessXmlParseException(
                "XML source is not safe, well-formed XML.",
                exception
            );
        }

        var lexicalElements = LexicalScanner.Scan(decoded, options, cancellationToken);
        var parsedElements = parsedDocument.Root?.DescendantsAndSelf().ToArray()
            ?? Array.Empty<XElement>();
        BindParsedElements(lexicalElements, parsedElements);
        var byteOffsets = BuildByteOffsetMap(
            decoded,
            bytes.Length,
            sourceEncoding,
            lexicalElements
        );
        var elements = FreezeElements(lexicalElements, byteOffsets);
        var ordinals = new Dictionary<XElement, int>(ReferenceEqualityComparer.Instance);
        for (var ordinal = 0; ordinal < parsedElements.Length; ordinal++)
        {
            ordinals.Add(parsedElements[ordinal], ordinal);
        }
        return new LosslessXmlDocument(
            bytes,
            sourceEncoding,
            options,
            parsedDocument,
            elements,
            ordinals
        );
    }

    public XmlSourceElement GetElement(int ordinal)
    {
        if ((uint)ordinal >= (uint)Elements.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        return Elements[ordinal];
    }

    public byte[] ReplaceElementText(
        int elementOrdinal,
        string newValue,
        string? expectedValue = null,
        string? expectedSourceSha256 = null,
        bool preserveBoundaryWhitespace = false,
        CancellationToken cancellationToken = default
    )
    {
        var patches = CreateElementTextPatches(
            elementOrdinal,
            newValue,
            expectedValue,
            preserveBoundaryWhitespace,
            cancellationToken
        );
        return ApplyPatches(patches, expectedSourceSha256, cancellationToken);
    }

    internal IReadOnlyList<XmlSourcePatch> CreateElementTextPatches(
        int elementOrdinal,
        string newValue,
        string? expectedValue,
        bool preserveBoundaryWhitespace,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(newValue);
        cancellationToken.ThrowIfCancellationRequested();
        var element = GetElement(elementOrdinal);
        if (element.Children.Count != 0)
        {
            throw new LosslessXmlEditException(
                $"Element {element.Ordinal} ('{element.QualifiedName}') contains child elements; "
                    + "a leaf-text replacement cannot own that mixed structure."
            );
        }

        if (
            expectedValue is not null
            && !string.Equals(element.Value, expectedValue, StringComparison.Ordinal)
        )
        {
            throw new LosslessXmlPreconditionException(
                $"Element {element.Ordinal} text changed before the edit was applied."
            );
        }

        if (string.Equals(element.Value, newValue, StringComparison.Ordinal))
        {
            return Array.Empty<XmlSourcePatch>();
        }

        if (!element.IsSelfClosing && element.HasLexicalMarkupInContent)
        {
            throw new LosslessXmlEditException(
                $"Element {element.Ordinal} contains comments, CDATA, or processing markup. "
                    + "Replacing it as plain text would destroy lexical source."
            );
        }

        try
        {
            XmlConvert.VerifyXmlChars(newValue);
        }
        catch (XmlException exception)
        {
            throw new LosslessXmlEditException(
                "Replacement contains a character forbidden by XML 1.0.",
                exception
            );
        }

        var escapedText = EscapeElementText(newValue);
        var patches = new List<XmlSourcePatch>();
        var preserveRequired = preserveBoundaryWhitespace
            && HasBoundaryXmlWhitespace(newValue);
        var xmlSpace = element.Attributes.FirstOrDefault(attribute =>
            attribute.NamespaceUri == XmlNamespace && attribute.LocalName == "space"
        );
        var attributeInsertion = string.Empty;
        if (preserveRequired && xmlSpace is null)
        {
            attributeInsertion = " xml:space=\"preserve\"";
        }
        else if (
            preserveRequired
            && xmlSpace is not null
            && !string.Equals(xmlSpace.Value, "preserve", StringComparison.Ordinal)
        )
        {
            patches.Add(
                new XmlSourcePatch(
                    xmlSpace.ValueSpan.ByteOffset,
                    xmlSpace.ValueSpan.ByteLength,
                    EncodeMarkup("preserve")
                )
            );
        }

        if (element.IsSelfClosing)
        {
            var slashOffset = element.SelfClosingSlashByteOffset
                ?? throw new LosslessXmlEditException(
                    "Self-closing element has no lexical slash position."
                );
            var expanded = attributeInsertion
                + ">"
                + escapedText
                + "</"
                + element.QualifiedName;
            patches.Add(
                new XmlSourcePatch(
                    slashOffset,
                    _sourceEncoding.Encoding.GetByteCount("/"),
                    EncodeMarkup(expanded)
                )
            );
        }
        else
        {
            if (attributeInsertion.Length != 0)
            {
                patches.Add(
                    new XmlSourcePatch(
                        element.StartTagCloseByteOffset,
                        0,
                        EncodeMarkup(attributeInsertion)
                    )
                );
            }

            patches.Add(
                new XmlSourcePatch(
                    element.ContentSpan.ByteOffset,
                    element.ContentSpan.ByteLength,
                    EncodeMarkup(escapedText)
                )
            );
        }

        return patches;
    }

    public byte[] ApplyPatches(
        IEnumerable<XmlSourcePatch> patches,
        string? expectedSourceSha256 = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(patches);
        cancellationToken.ThrowIfCancellationRequested();
        VerifySourcePrecondition(expectedSourceSha256);
        var ordered = patches
            .OrderBy(patch => patch.ByteOffset)
            .ThenBy(patch => patch.ByteLength)
            .ToArray();
        if (ordered.Length == 0)
        {
            return _sourceBytes.ToArray();
        }

        long resultLength = _sourceBytes.Length;
        var sourceCursor = 0;
        XmlSourcePatch? previous = null;
        foreach (var patch in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int end;
            try
            {
                end = checked(patch.ByteOffset + patch.ByteLength);
                resultLength = checked(
                    resultLength - patch.ByteLength + patch.Replacement.Length
                );
            }
            catch (OverflowException exception)
            {
                throw new LosslessXmlEditException(
                    "XML patch range or result length overflowed.",
                    exception
                );
            }

            if (end > _sourceBytes.Length)
            {
                throw new LosslessXmlEditException("XML patch lies outside the source bytes.");
            }

            if (patch.ByteOffset < sourceCursor)
            {
                throw new LosslessXmlEditException("XML patches overlap.");
            }

            if (
                previous is not null
                && previous.ByteOffset == patch.ByteOffset
                && (previous.ByteLength == 0 || patch.ByteLength == 0)
            )
            {
                throw new LosslessXmlEditException(
                    "Multiple XML patches share an ambiguous insertion boundary."
                );
            }

            sourceCursor = end;
            previous = patch;
        }

        if (resultLength > _options.MaxSourceBytes || resultLength > int.MaxValue)
        {
            throw new LosslessXmlLimitException(
                $"Patched XML exceeds {_options.MaxSourceBytes} bytes."
            );
        }

        var output = new byte[(int)resultLength];
        sourceCursor = 0;
        var outputCursor = 0;
        foreach (var patch in ordered)
        {
            var unchangedLength = patch.ByteOffset - sourceCursor;
            _sourceBytes.AsSpan(sourceCursor, unchangedLength)
                .CopyTo(output.AsSpan(outputCursor));
            outputCursor += unchangedLength;
            patch.Replacement.Span.CopyTo(output.AsSpan(outputCursor));
            outputCursor += patch.Replacement.Length;
            sourceCursor = patch.ByteOffset + patch.ByteLength;
        }

        _sourceBytes.AsSpan(sourceCursor).CopyTo(output.AsSpan(outputCursor));
        _ = Parse(output, _options, cancellationToken);
        return output;
    }

    internal int GetElementOrdinal(XElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return _elementOrdinals.TryGetValue(element, out var ordinal)
            ? ordinal
            : throw new InvalidOperationException(
                "Element does not belong to this lossless XML source."
            );
    }

    private void VerifySourcePrecondition(string? expectedSourceSha256)
    {
        if (expectedSourceSha256 is null)
        {
            return;
        }

        if (
            !string.Equals(
                SourceSha256,
                expectedSourceSha256.Trim(),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new LosslessXmlPreconditionException(
                $"XML source changed: expected SHA-256 '{expectedSourceSha256}', "
                    + $"actual '{SourceSha256}'."
            );
        }
    }

    private byte[] EncodeMarkup(string markup)
    {
        try
        {
            return _sourceEncoding.Encoding.GetBytes(markup);
        }
        catch (EncoderFallbackException)
        {
            var escaped = new StringBuilder(markup.Length);
            foreach (var rune in markup.EnumerateRunes())
            {
                var value = rune.ToString();
                try
                {
                    _ = _sourceEncoding.Encoding.GetByteCount(value);
                    escaped.Append(value);
                }
                catch (EncoderFallbackException)
                {
                    escaped.Append("&#x");
                    escaped.Append(rune.Value.ToString("X", System.Globalization.CultureInfo.InvariantCulture));
                    escaped.Append(';');
                }
            }

            return _sourceEncoding.Encoding.GetBytes(escaped.ToString());
        }
    }

    private static string EscapeElementText(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            result.Append(
                character switch
                {
                    '&' => "&amp;",
                    '<' => "&lt;",
                    '>' => "&gt;",
                    '\r' => "&#xD;",
                    _ => character.ToString(),
                }
            );
        }

        return result.ToString();
    }

    private static bool HasBoundaryXmlWhitespace(string value) =>
        value.Length != 0
        && (IsXmlWhitespace(value[0]) || IsXmlWhitespace(value[^1]));

    private static bool IsXmlWhitespace(char value) =>
        value is ' ' or '\t' or '\r' or '\n';

    private static void AuditXml(
        byte[] source,
        LosslessXmlOptions options,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var stream = new MemoryStream(source, writable: false);
            using var reader = XmlReader.Create(stream, CreateXmlSettings(options));
            var elementCount = 0;
            long textCharacters = 0;
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.Depth > options.MaxXmlDepth)
                {
                    throw new LosslessXmlLimitException(
                        $"XML depth exceeds {options.MaxXmlDepth}."
                    );
                }

                if (
                    reader.NodeType == XmlNodeType.Element
                    && ++elementCount > options.MaxXmlElements
                )
                {
                    throw new LosslessXmlLimitException(
                        $"XML contains more than {options.MaxXmlElements} elements."
                    );
                }

                if (
                    reader.NodeType is XmlNodeType.Text
                        or XmlNodeType.CDATA
                        or XmlNodeType.SignificantWhitespace
                )
                {
                    checked
                    {
                        textCharacters += reader.Value.Length;
                    }

                    if (textCharacters > options.MaxTextCharacters)
                    {
                        throw new LosslessXmlLimitException(
                            $"XML text exceeds {options.MaxTextCharacters} characters."
                        );
                    }
                }
            }
        }
        catch (LosslessXmlLimitException)
        {
            throw;
        }
        catch (XmlException exception)
        {
            throw new LosslessXmlParseException(
                "XML source is not safe, well-formed XML.",
                exception
            );
        }
        catch (OverflowException exception)
        {
            throw new LosslessXmlLimitException(
                $"XML text accounting overflowed: {exception.Message}"
            );
        }
    }

    private static XmlReaderSettings CreateXmlSettings(LosslessXmlOptions options) => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = options.MaxXmlCharacters,
        MaxCharactersFromEntities = 0,
        IgnoreComments = false,
        IgnoreProcessingInstructions = false,
        IgnoreWhitespace = false,
        CheckCharacters = true,
    };

    private static SourceEncoding DetectEncoding(byte[] source)
    {
        var span = source.AsSpan();
        if (span.StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
        {
            return new SourceEncoding(
                new UTF32Encoding(true, false, true),
                4
            );
        }

        if (span.StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }))
        {
            return new SourceEncoding(
                new UTF32Encoding(false, false, true),
                4
            );
        }

        if (
            span.StartsWith(new byte[] { 0x00, 0x00, 0xFF, 0xFE })
            || span.StartsWith(new byte[] { 0xFE, 0xFF, 0x00, 0x00 })
        )
        {
            throw new LosslessXmlEncodingException(
                "Unusual UCS-4 byte orders 2143 and 3412 are not editable losslessly."
            );
        }

        if (span.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            return new SourceEncoding(new UTF8Encoding(false, true), 3);
        }

        if (span.StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            return new SourceEncoding(new UnicodeEncoding(true, false, true), 2);
        }

        if (span.StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            return new SourceEncoding(new UnicodeEncoding(false, false, true), 2);
        }

        if (span.Length >= 4)
        {
            if (span[..4].SequenceEqual(new byte[] { 0x00, 0x00, 0x00, 0x3C }))
            {
                return new SourceEncoding(new UTF32Encoding(true, false, true), 0);
            }

            if (span[..4].SequenceEqual(new byte[] { 0x3C, 0x00, 0x00, 0x00 }))
            {
                return new SourceEncoding(new UTF32Encoding(false, false, true), 0);
            }

            if (span[..4].SequenceEqual(new byte[] { 0x00, 0x3C, 0x00, 0x3F }))
            {
                return new SourceEncoding(new UnicodeEncoding(true, false, true), 0);
            }

            if (span[..4].SequenceEqual(new byte[] { 0x3C, 0x00, 0x3F, 0x00 }))
            {
                return new SourceEncoding(new UnicodeEncoding(false, false, true), 0);
            }

        }

        var declared = TryReadAsciiCompatibleEncodingDeclaration(source) ?? "utf-8";
        Encoding encoding;
        try
        {
            encoding = Encoding.GetEncoding(
                declared,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback
            );
        }
        catch (ArgumentException exception)
        {
            throw new LosslessXmlEncodingException(
                $"XML encoding '{declared}' is not available in this runtime.",
                exception
            );
        }

        if (
            !encoding.IsSingleByte
            && encoding.CodePage is not 65001 and not 1200 and not 1201
                and not 12000 and not 12001
        )
        {
            throw new LosslessXmlEncodingException(
                $"XML encoding '{declared}' is stateful or cannot be byte-mapped safely."
            );
        }

        return new SourceEncoding(encoding, 0);
    }

    private static string? TryReadAsciiCompatibleEncodingDeclaration(byte[] source)
    {
        var length = Math.Min(source.Length, 1_024);
        var header = Encoding.ASCII.GetString(source, 0, length);
        if (!header.StartsWith("<?xml", StringComparison.Ordinal))
        {
            return null;
        }

        var declarationEnd = header.IndexOf("?>", StringComparison.Ordinal);
        if (declarationEnd < 0)
        {
            return null;
        }

        var match = Regex.Match(
            header[..declarationEnd],
            "\\bencoding\\s*=\\s*(['\"])(?<name>[A-Za-z][A-Za-z0-9._-]*)\\1",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100)
        );
        return match.Success ? match.Groups["name"].Value : null;
    }

    private static void BindParsedElements(
        IReadOnlyList<MutableElement> lexical,
        IReadOnlyList<XElement> parsed
    )
    {
        if (lexical.Count != parsed.Count)
        {
            throw new LosslessXmlParseException(
                "Lexical and parsed XML element counts do not agree."
            );
        }

        for (var index = 0; index < lexical.Count; index++)
        {
            var raw = lexical[index];
            var typed = parsed[index];
            if (!string.Equals(raw.LocalName, typed.Name.LocalName, StringComparison.Ordinal))
            {
                throw new LosslessXmlParseException(
                    $"Lexical element '{raw.QualifiedName}' does not match parsed element "
                        + $"'{typed.Name}'."
                );
            }

            raw.ParsedElement = typed;
            var typedAttributes = typed.Attributes().ToArray();
            if (raw.Attributes.Count != typedAttributes.Length)
            {
                throw new LosslessXmlParseException(
                    $"Attribute count mismatch on element '{raw.QualifiedName}'."
                );
            }

            for (var attributeIndex = 0; attributeIndex < raw.Attributes.Count; attributeIndex++)
            {
                var rawAttribute = raw.Attributes[attributeIndex];
                var typedAttribute = typedAttributes[attributeIndex];
                if (
                    !string.Equals(
                        rawAttribute.LocalName,
                        typedAttribute.Name.LocalName,
                        StringComparison.Ordinal
                    )
                )
                {
                    throw new LosslessXmlParseException(
                        $"Lexical attribute '{rawAttribute.QualifiedName}' does not match "
                            + $"parsed attribute '{typedAttribute.Name}'."
                    );
                }

                rawAttribute.ParsedAttribute = typedAttribute;
            }
        }
    }

    private static IReadOnlyDictionary<int, int> BuildByteOffsetMap(
        string decoded,
        int sourceByteLength,
        SourceEncoding sourceEncoding,
        IReadOnlyList<MutableElement> elements
    )
    {
        var boundaries = new HashSet<int> { 0, decoded.Length };
        foreach (var element in elements)
        {
            boundaries.Add(element.FullStart);
            boundaries.Add(element.StartTagEnd);
            boundaries.Add(element.ContentStart);
            boundaries.Add(element.ContentEnd);
            boundaries.Add(element.EndTagStart);
            boundaries.Add(element.FullEnd);
            boundaries.Add(element.StartTagClose);
            if (element.SelfClosingSlash is int slash)
            {
                boundaries.Add(slash);
            }

            foreach (var attribute in element.Attributes)
            {
                boundaries.Add(attribute.FullStart);
                boundaries.Add(attribute.FullEnd);
                boundaries.Add(attribute.ValueStart);
                boundaries.Add(attribute.ValueEnd);
            }
        }

        var ordered = boundaries.Order().ToArray();
        var result = new Dictionary<int, int>(ordered.Length);
        var previousCharacter = 0;
        var byteOffset = sourceEncoding.ByteOrderMarkLength;
        foreach (var characterOffset in ordered)
        {
            try
            {
                byteOffset = checked(
                    byteOffset
                        + sourceEncoding.Encoding.GetByteCount(
                            decoded.AsSpan(
                                previousCharacter,
                                characterOffset - previousCharacter
                            )
                        )
                );
            }
            catch (EncoderFallbackException exception)
            {
                throw new LosslessXmlEncodingException(
                    "Decoded XML cannot be mapped back to its original byte encoding.",
                    exception
                );
            }

            result[characterOffset] = byteOffset;
            previousCharacter = characterOffset;
        }

        if (byteOffset != sourceByteLength)
        {
            throw new LosslessXmlEncodingException(
                "Decoded XML does not have a one-to-one boundary map to the source bytes."
            );
        }

        return result;
    }

    private static IReadOnlyList<XmlSourceElement> FreezeElements(
        IReadOnlyList<MutableElement> source,
        IReadOnlyDictionary<int, int> byteOffsets
    )
    {
        var result = new XmlSourceElement[source.Count];
        foreach (var element in source)
        {
            var parsed = element.ParsedElement
                ?? throw new InvalidOperationException("XML element was not bound.");
            var attributes = element.Attributes.Select(attribute =>
            {
                var typed = attribute.ParsedAttribute
                    ?? throw new InvalidOperationException("XML attribute was not bound.");
                var namespaceUri = typed.IsNamespaceDeclaration
                    ? XNamespace.Xmlns.NamespaceName
                    : typed.Name.NamespaceName;
                return new XmlSourceAttribute(
                    attribute.QualifiedName,
                    attribute.Prefix,
                    attribute.LocalName,
                    namespaceUri,
                    typed.Value,
                    attribute.Quote,
                    Span(attribute.FullStart, attribute.FullEnd, byteOffsets),
                    Span(attribute.ValueStart, attribute.ValueEnd, byteOffsets)
                );
            }).ToArray();
            var hasContentMarkup = !element.IsSelfClosing
                && element.ContentEnd > element.ContentStart
                && element.DecodedSource.AsSpan(
                    element.ContentStart,
                    element.ContentEnd - element.ContentStart
                ).IndexOf('<') >= 0;
            result[element.Ordinal] = new XmlSourceElement(
                element.Ordinal,
                element.ParentOrdinal,
                element.QualifiedName,
                element.Prefix,
                element.LocalName,
                parsed.Name.NamespaceName,
                element.IsSelfClosing,
                hasContentMarkup,
                parsed.HasElements ? string.Empty : parsed.Value,
                Span(element.FullStart, element.FullEnd, byteOffsets),
                Span(element.FullStart, element.StartTagEnd, byteOffsets),
                Span(element.ContentStart, element.ContentEnd, byteOffsets),
                element.IsSelfClosing
                    ? null
                    : Span(element.EndTagStart, element.FullEnd, byteOffsets),
                byteOffsets[element.StartTagClose],
                element.SelfClosingSlash is int slash ? byteOffsets[slash] : null,
                attributes
            );
        }

        foreach (var element in source)
        {
            result[element.Ordinal].SetChildren(
                element.ChildOrdinals.Select(ordinal => result[ordinal]).ToArray()
            );
        }

        return result;
    }

    private static XmlSourceSpan Span(
        int start,
        int end,
        IReadOnlyDictionary<int, int> byteOffsets
    )
    {
        var byteStart = byteOffsets[start];
        return new XmlSourceSpan(byteStart, byteOffsets[end] - byteStart);
    }

    private sealed record SourceEncoding(Encoding Encoding, int ByteOrderMarkLength);

    private sealed class MutableAttribute
    {
        public required string QualifiedName { get; init; }

        public required string Prefix { get; init; }

        public required string LocalName { get; init; }

        public required char Quote { get; init; }

        public required int FullStart { get; init; }

        public required int FullEnd { get; init; }

        public required int ValueStart { get; init; }

        public required int ValueEnd { get; init; }

        public XAttribute? ParsedAttribute { get; set; }
    }

    private sealed class MutableElement
    {
        public required int Ordinal { get; init; }

        public required int? ParentOrdinal { get; init; }

        public required string QualifiedName { get; init; }

        public required string Prefix { get; init; }

        public required string LocalName { get; init; }

        public required string DecodedSource { get; init; }

        public required int FullStart { get; init; }

        public required int StartTagEnd { get; set; }

        public required int ContentStart { get; set; }

        public required int ContentEnd { get; set; }

        public required int EndTagStart { get; set; }

        public required int FullEnd { get; set; }

        public required int StartTagClose { get; set; }

        public required bool IsSelfClosing { get; set; }

        public int? SelfClosingSlash { get; set; }

        public List<int> ChildOrdinals { get; } = [];

        public List<MutableAttribute> Attributes { get; } = [];

        public XElement? ParsedElement { get; set; }
    }

    private static class LexicalScanner
    {
        public static IReadOnlyList<MutableElement> Scan(
            string source,
            LosslessXmlOptions options,
            CancellationToken cancellationToken
        )
        {
            var elements = new List<MutableElement>();
            var stack = new Stack<MutableElement>();
            var index = 0;
            while (index < source.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var markup = source.IndexOf('<', index);
                if (markup < 0)
                {
                    break;
                }

                index = markup;
                if (StartsWith(source, index, "<!--"))
                {
                    index = FindTerminator(source, index + 4, "-->") + 3;
                    continue;
                }

                if (StartsWith(source, index, "<![CDATA["))
                {
                    index = FindTerminator(source, index + 9, "]]>") + 3;
                    continue;
                }

                if (StartsWith(source, index, "<?"))
                {
                    index = FindTerminator(source, index + 2, "?>") + 2;
                    continue;
                }

                if (StartsWith(source, index, "<!DOCTYPE"))
                {
                    throw new LosslessXmlParseException(
                        "DTD declarations are forbidden in lossless XML source."
                    );
                }

                if (StartsWith(source, index, "<!"))
                {
                    throw new LosslessXmlParseException("Unsupported XML declaration markup.");
                }

                if (StartsWith(source, index, "</"))
                {
                    if (!stack.TryPop(out var current))
                    {
                        throw new LosslessXmlParseException("XML end tag has no start tag.");
                    }

                    var endTagStart = index;
                    index += 2;
                    var endName = ReadName(source, ref index);
                    SkipWhitespace(source, ref index);
                    if (index >= source.Length || source[index] != '>')
                    {
                        throw new LosslessXmlParseException("Malformed XML end tag.");
                    }

                    index++;
                    if (!string.Equals(current.QualifiedName, endName, StringComparison.Ordinal))
                    {
                        throw new LosslessXmlParseException(
                            $"XML end tag '{endName}' does not match '{current.QualifiedName}'."
                        );
                    }

                    current.ContentEnd = endTagStart;
                    current.EndTagStart = endTagStart;
                    current.FullEnd = index;
                    continue;
                }

                var fullStart = index;
                index++;
                var qualifiedName = ReadName(source, ref index);
                SplitQualifiedName(qualifiedName, out var prefix, out var localName);
                var parent = stack.TryPeek(out var owner) ? owner : null;
                var element = new MutableElement
                {
                    Ordinal = elements.Count,
                    ParentOrdinal = parent?.Ordinal,
                    QualifiedName = qualifiedName,
                    Prefix = prefix,
                    LocalName = localName,
                    DecodedSource = source,
                    FullStart = fullStart,
                    StartTagEnd = -1,
                    ContentStart = -1,
                    ContentEnd = -1,
                    EndTagStart = -1,
                    FullEnd = -1,
                    StartTagClose = -1,
                    IsSelfClosing = false,
                };
                elements.Add(element);
                parent?.ChildOrdinals.Add(element.Ordinal);
                if (elements.Count > options.MaxXmlElements)
                {
                    throw new LosslessXmlLimitException(
                        $"XML contains more than {options.MaxXmlElements} elements."
                    );
                }

                while (true)
                {
                    SkipWhitespace(source, ref index);
                    if (index >= source.Length)
                    {
                        throw new LosslessXmlParseException("Unterminated XML start tag.");
                    }

                    if (source[index] == '>')
                    {
                        element.StartTagClose = index;
                        index++;
                        element.StartTagEnd = index;
                        element.ContentStart = index;
                        stack.Push(element);
                        if (stack.Count > options.MaxXmlDepth + 1)
                        {
                            throw new LosslessXmlLimitException(
                                $"XML depth exceeds {options.MaxXmlDepth}."
                            );
                        }

                        break;
                    }

                    if (
                        source[index] == '/'
                        && index + 1 < source.Length
                        && source[index + 1] == '>'
                    )
                    {
                        element.IsSelfClosing = true;
                        element.SelfClosingSlash = index;
                        element.StartTagClose = index;
                        element.ContentStart = index;
                        element.ContentEnd = index;
                        element.EndTagStart = index;
                        index += 2;
                        element.StartTagEnd = index;
                        element.FullEnd = index;
                        break;
                    }

                    var attributeStart = index;
                    var attributeName = ReadName(source, ref index);
                    SplitQualifiedName(
                        attributeName,
                        out var attributePrefix,
                        out var attributeLocalName
                    );
                    SkipWhitespace(source, ref index);
                    if (index >= source.Length || source[index] != '=')
                    {
                        throw new LosslessXmlParseException(
                            $"Attribute '{attributeName}' has no equals sign."
                        );
                    }

                    index++;
                    SkipWhitespace(source, ref index);
                    if (
                        index >= source.Length
                        || source[index] is not '\'' and not '"'
                    )
                    {
                        throw new LosslessXmlParseException(
                            $"Attribute '{attributeName}' has no quoted value."
                        );
                    }

                    var quote = source[index++];
                    var valueStart = index;
                    var valueEnd = source.IndexOf(quote, valueStart);
                    if (valueEnd < 0)
                    {
                        throw new LosslessXmlParseException(
                            $"Attribute '{attributeName}' value is unterminated."
                        );
                    }

                    index = valueEnd + 1;
                    element.Attributes.Add(
                        new MutableAttribute
                        {
                            QualifiedName = attributeName,
                            Prefix = attributePrefix,
                            LocalName = attributeLocalName,
                            Quote = quote,
                            FullStart = attributeStart,
                            FullEnd = index,
                            ValueStart = valueStart,
                            ValueEnd = valueEnd,
                        }
                    );
                }
            }

            if (stack.Count != 0)
            {
                throw new LosslessXmlParseException("XML source has unclosed elements.");
            }

            if (elements.Count == 0 || elements.Count(element => element.ParentOrdinal is null) != 1)
            {
                throw new LosslessXmlParseException(
                    "XML source must contain exactly one document element."
                );
            }

            return elements;
        }

        private static bool StartsWith(string source, int index, string value) =>
            source.AsSpan(index).StartsWith(value, StringComparison.Ordinal);

        private static int FindTerminator(string source, int start, string terminator)
        {
            var result = source.IndexOf(terminator, start, StringComparison.Ordinal);
            return result >= 0
                ? result
                : throw new LosslessXmlParseException(
                    $"XML markup has no '{terminator}' terminator."
                );
        }

        private static string ReadName(string source, ref int index)
        {
            var start = index;
            while (
                index < source.Length
                && !IsXmlWhitespace(source[index])
                && source[index] is not '/' and not '>' and not '=' and not '?'
            )
            {
                index++;
            }

            if (index == start)
            {
                throw new LosslessXmlParseException("XML name is missing.");
            }

            return source[start..index];
        }

        private static void SkipWhitespace(string source, ref int index)
        {
            while (index < source.Length && IsXmlWhitespace(source[index]))
            {
                index++;
            }
        }

        private static void SplitQualifiedName(
            string qualifiedName,
            out string prefix,
            out string localName
        )
        {
            var separator = qualifiedName.IndexOf(':');
            if (separator < 0)
            {
                prefix = string.Empty;
                localName = qualifiedName;
                return;
            }

            prefix = qualifiedName[..separator];
            localName = qualifiedName[(separator + 1)..];
        }
    }
}
