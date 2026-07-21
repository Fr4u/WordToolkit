using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;

namespace WordToolkit.Engine.Semantics;

public sealed class WordSemanticProjector
{
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string MathTransitionalNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string MathStrictNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/math";
    private const string MarkupCompatibilityNamespace =
        "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string RelationshipsTransitionalNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RelationshipsStrictNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/relationships";
    private const string Word2010Namespace =
        "http://schemas.microsoft.com/office/word/2010/wordml";

    private readonly WordSemanticProjectionOptions _options;
    public WordSemanticProjector(WordSemanticProjectionOptions? options = null)
    {
        _options = options ?? WordSemanticProjectionOptions.Default;
        _options.Validate();
    }

    public WordSemanticDocument Project(
        OpcPackageSnapshot package,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();
        var state = new ProjectionState();

        var officeRelationships = package.Relationships
            .Where(relationship =>
                relationship.SourcePartUri == "/"
                && relationship.TargetMode == OpcRelationshipTargetMode.Internal
                && relationship.Type.EndsWith(
                    "/officeDocument",
                    StringComparison.Ordinal
                )
            )
            .ToArray();
        if (officeRelationships.Length == 0)
        {
            throw new WordSemanticProjectionException(
                "Package has no internal officeDocument relationship."
            );
        }

        if (officeRelationships.Length > 1)
        {
            throw new WordSemanticProjectionException(
                "Package has multiple officeDocument relationships."
            );
        }

        var mainPartUri = officeRelationships[0].ResolvedTargetPartUri;
        if (mainPartUri is null || !package.Parts.TryGetValue(mainPartUri, out var mainPart))
        {
            throw new WordSemanticProjectionException(
                "The officeDocument relationship does not resolve to an existing part."
            );
        }

        if (!IsWordMainContentType(mainPart.ContentType))
        {
            throw new WordSemanticProjectionException(
                $"Main part content type '{mainPart.ContentType ?? "(missing)"}' is not a Word main-document type."
            );
        }

        XDocument xml;
        try
        {
            var mainPartBytes = mainPart.Entry.Content.ToArray();
            AuditXml(mainPartBytes, cancellationToken);
            xml = LoadXml(mainPartBytes);
        }
        catch (WordSemanticLimitException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is XmlException or InvalidOperationException
        )
        {
            throw new WordSemanticProjectionException(
                "The Word main document part is not safe, well-formed XML.",
                exception
            );
        }

        var documentElement = xml.Root;
        if (
            documentElement is null
            || !IsWordNamespace(documentElement.Name.NamespaceName)
            || documentElement.Name.LocalName != "document"
        )
        {
            throw new WordSemanticProjectionException(
                "The Word main part does not have a w:document root element."
            );
        }

        var roots = new List<MutableSemanticNode>();
        var rootContext = new ProjectionContext(
            parent: null,
            identityContext: mainPartUri,
            occurrences: new Dictionary<string, int>(StringComparer.Ordinal),
            roots
        );
        ProjectElement(
            documentElement,
            mainPartUri,
            sourcePath: $"/{QualifiedName(documentElement.Name)}[1]",
            rootContext,
            state,
            cancellationToken
        );
        if (roots.Count != 1 || roots[0].Kind != WordSemanticNodeKind.Document)
        {
            throw new WordSemanticProjectionException(
                "Semantic projection did not produce exactly one document root."
            );
        }

        var frozenRoot = roots[0].Freeze();
        var warnings = package.Diagnostics
            .Where(diagnostic => diagnostic.Severity == OpcDiagnosticSeverity.Warning)
            .Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")
            .ToArray();
        return new WordSemanticDocument(
            package.Fingerprint,
            mainPartUri,
            frozenRoot,
            warnings
        );
    }

    private void AuditXml(
        byte[] content,
        CancellationToken cancellationToken
    )
    {
        using var stream = new MemoryStream(content, writable: false);
        using var reader = XmlReader.Create(stream, XmlSettings());
        var elements = 0;
        long textCharacters = 0;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.Depth > _options.MaxXmlDepth)
            {
                throw new WordSemanticLimitException(
                    $"Word XML depth exceeds {_options.MaxXmlDepth}."
                );
            }

            if (reader.NodeType == XmlNodeType.Element && ++elements > _options.MaxXmlElements)
            {
                throw new WordSemanticLimitException(
                    $"Word XML contains more than {_options.MaxXmlElements} elements."
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

                if (textCharacters > _options.MaxTextCharacters)
                {
                    throw new WordSemanticLimitException(
                        $"Word XML text exceeds {_options.MaxTextCharacters} characters."
                    );
                }
            }
        }
    }

    private XDocument LoadXml(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var reader = XmlReader.Create(stream, XmlSettings());
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
    }

    private XmlReaderSettings XmlSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = _options.MaxXmlCharacters,
        IgnoreComments = false,
        IgnoreWhitespace = false,
    };

    private void ProjectElement(
        XElement element,
        string sourcePartUri,
        string sourcePath,
        ProjectionContext context,
        ProjectionState state,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var kind = Classify(element);
        var childContext = context;
        if (kind is not null)
        {
            if (++state.SemanticNodeCount > _options.MaxXmlElements)
            {
                throw new WordSemanticLimitException(
                    $"Semantic projection exceeds {_options.MaxXmlElements} nodes."
                );
            }

            var anchor = DurableAnchor(element, kind.Value);
            var fingerprint = Fingerprint(element, state);
            var signature = anchor ?? $"fp:{fingerprint}";
            var occurrenceKey = $"{kind}:{signature}";
            var occurrence = context.occurrences.TryGetValue(occurrenceKey, out var count)
                ? count + 1
                : 1;
            context.occurrences[occurrenceKey] = occurrence;
            var id = CreateNodeId(
                sourcePartUri,
                kind.Value,
                context.identityContext,
                signature,
                occurrence
            );
            var mutable = new MutableSemanticNode(
                id,
                kind.Value,
                context.parent?.Id,
                state.SemanticNodeCount,
                sourcePartUri,
                sourcePath,
                NodeText(element, kind.Value),
                NodeProperties(element, kind.Value)
            );
            if (context.parent is null)
            {
                context.roots.Add(mutable);
            }
            else
            {
                context.parent.Children.Add(mutable);
            }

            var nextIdentityContext = kind.Value is WordSemanticNodeKind.Document
                or WordSemanticNodeKind.Body
                ? $"{context.identityContext}/{kind.Value}"
                : $"{context.identityContext}/{kind.Value}:{id.Value}";
            childContext = new ProjectionContext(
                mutable,
                nextIdentityContext,
                new Dictionary<string, int>(StringComparer.Ordinal),
                context.roots
            );
        }

        var nameOccurrences = new Dictionary<XName, int>();
        foreach (var child in element.Elements())
        {
            var index = nameOccurrences.TryGetValue(child.Name, out var count)
                ? count + 1
                : 1;
            nameOccurrences[child.Name] = index;
            ProjectElement(
                child,
                sourcePartUri,
                $"{sourcePath}/{QualifiedName(child.Name)}[{index}]",
                childContext,
                state,
                cancellationToken
            );
        }
    }

    private static WordSemanticNodeKind? Classify(XElement element)
    {
        var namespaceName = element.Name.NamespaceName;
        var localName = element.Name.LocalName;
        if (IsWordNamespace(namespaceName))
        {
            return localName switch
            {
                "document" => WordSemanticNodeKind.Document,
                "body" => WordSemanticNodeKind.Body,
                "p" => WordSemanticNodeKind.Paragraph,
                "r" => WordSemanticNodeKind.Run,
                "t" or "delText" => WordSemanticNodeKind.Text,
                "tab" => WordSemanticNodeKind.Tab,
                "br" or "cr" => WordSemanticNodeKind.Break,
                "tbl" => WordSemanticNodeKind.Table,
                "tr" => WordSemanticNodeKind.TableRow,
                "tc" => WordSemanticNodeKind.TableCell,
                "hyperlink" => WordSemanticNodeKind.Hyperlink,
                "fldSimple" or "fldChar" or "instrText" => WordSemanticNodeKind.Field,
                "sdt" => WordSemanticNodeKind.ContentControl,
                "bookmarkStart" => WordSemanticNodeKind.Bookmark,
                "commentRangeStart" or "commentRangeEnd" =>
                    WordSemanticNodeKind.CommentAnchor,
                "ins" or "del" or "moveFrom" or "moveTo" =>
                    WordSemanticNodeKind.Revision,
                "drawing" or "pict" or "object" => WordSemanticNodeKind.Drawing,
                _ => null,
            };
        }

        if (IsMathNamespace(namespaceName))
        {
            return localName switch
            {
                "oMath" or "oMathPara" => WordSemanticNodeKind.Equation,
                "t" => WordSemanticNodeKind.Text,
                _ => WordSemanticNodeKind.EquationComponent,
            };
        }

        if (
            namespaceName == MarkupCompatibilityNamespace
            && localName == "AlternateContent"
        )
        {
            return WordSemanticNodeKind.AlternateContent;
        }

        if (IsDrawingNamespace(namespaceName))
        {
            return WordSemanticNodeKind.Drawing;
        }

        return string.IsNullOrEmpty(namespaceName)
            ? null
            : WordSemanticNodeKind.ExtensionIsland;
    }

    private static string Fingerprint(XElement element, ProjectionState state)
    {
        if (state.Fingerprints.TryGetValue(element, out var cached))
        {
            return cached;
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, element.Name.NamespaceName);
        Append(hash, element.Name.LocalName);
        foreach (
            var attribute in element.Attributes()
                .Where(attribute =>
                    !attribute.IsNamespaceDeclaration
                    && !attribute.Name.LocalName.StartsWith(
                        "rsid",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .OrderBy(attribute => attribute.Name.NamespaceName, StringComparer.Ordinal)
                .ThenBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal)
        )
        {
            Append(hash, attribute.Name.NamespaceName);
            Append(hash, attribute.Name.LocalName);
            Append(hash, attribute.Value);
        }

        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XElement child:
                    Append(hash, Fingerprint(child, state));
                    break;
                case XText text when !string.IsNullOrWhiteSpace(text.Value):
                    Append(hash, text.Value);
                    break;
            }
        }

        var fingerprint = Convert.ToHexString(hash.GetHashAndReset())
            .ToLowerInvariant();
        state.Fingerprints[element] = fingerprint;
        return fingerprint;
    }

    private static string? DurableAnchor(
        XElement element,
        WordSemanticNodeKind kind
    )
    {
        var paragraphId = element.Attribute(XName.Get("paraId", Word2010Namespace))?.Value;
        if (!string.IsNullOrWhiteSpace(paragraphId))
        {
            return $"para:{paragraphId}";
        }

        var textId = element.Attribute(XName.Get("textId", Word2010Namespace))?.Value;
        if (!string.IsNullOrWhiteSpace(textId))
        {
            return $"text:{textId}";
        }

        var wordNamespace = element.Name.Namespace;
        var id = element.Attribute(wordNamespace + "id")?.Value;
        if (
            !string.IsNullOrWhiteSpace(id)
            && kind is WordSemanticNodeKind.Bookmark
                or WordSemanticNodeKind.CommentAnchor
                or WordSemanticNodeKind.Revision
        )
        {
            return $"{kind}:{id}";
        }

        if (kind == WordSemanticNodeKind.ContentControl)
        {
            var properties = element.Elements()
                .FirstOrDefault(child =>
                    IsWordNamespace(child.Name.NamespaceName)
                    && child.Name.LocalName == "sdtPr"
                );
            var tag = properties?.Elements()
                .FirstOrDefault(child => child.Name.LocalName == "tag")
                ?.Attribute(wordNamespace + "val")
                ?.Value;
            if (!string.IsNullOrWhiteSpace(tag))
            {
                return $"sdt-tag:{tag}";
            }

            var controlId = properties?.Elements()
                .FirstOrDefault(child => child.Name.LocalName == "id")
                ?.Attribute(wordNamespace + "val")
                ?.Value;
            if (!string.IsNullOrWhiteSpace(controlId))
            {
                return $"sdt-id:{controlId}";
            }
        }

        return null;
    }

    private static Dictionary<string, string> NodeProperties(
        XElement element,
        WordSemanticNodeKind kind
    )
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var wordNamespace = element.Name.Namespace;
        AddIfPresent(
            result,
            "paragraph_id",
            element.Attribute(XName.Get("paraId", Word2010Namespace))?.Value
        );
        if (kind == WordSemanticNodeKind.Paragraph)
        {
            var style = element.Elements(wordNamespace + "pPr")
                .Elements(wordNamespace + "pStyle")
                .Attributes(wordNamespace + "val")
                .Select(attribute => attribute.Value)
                .FirstOrDefault();
            AddIfPresent(result, "style_id", style);
        }
        else if (kind == WordSemanticNodeKind.Run)
        {
            var style = element.Elements(wordNamespace + "rPr")
                .Elements(wordNamespace + "rStyle")
                .Attributes(wordNamespace + "val")
                .Select(attribute => attribute.Value)
                .FirstOrDefault();
            AddIfPresent(result, "style_id", style);
        }
        else if (kind == WordSemanticNodeKind.Hyperlink)
        {
            AddIfPresent(
                result,
                "relationship_id",
                element.Attribute(XName.Get("id", RelationshipsTransitionalNamespace))?.Value
                    ?? element.Attribute(XName.Get("id", RelationshipsStrictNamespace))?.Value
            );
            AddIfPresent(
                result,
                "anchor",
                element.Attribute(wordNamespace + "anchor")?.Value
            );
        }
        else if (kind == WordSemanticNodeKind.Field)
        {
            AddIfPresent(
                result,
                "instruction",
                element.Attribute(wordNamespace + "instr")?.Value
            );
            AddIfPresent(
                result,
                "field_character_type",
                element.Attribute(wordNamespace + "fldCharType")?.Value
            );
        }
        else if (kind == WordSemanticNodeKind.EquationComponent)
        {
            result["math_element"] = element.Name.LocalName;
        }
        else if (kind == WordSemanticNodeKind.ExtensionIsland)
        {
            result["namespace"] = element.Name.NamespaceName;
            result["element"] = element.Name.LocalName;
        }

        return result;
    }

    private static string? NodeText(XElement element, WordSemanticNodeKind kind) =>
        kind switch
        {
            WordSemanticNodeKind.Text => element.Value,
            WordSemanticNodeKind.Field when element.Name.LocalName == "instrText" =>
                element.Value,
            WordSemanticNodeKind.Tab => "\t",
            WordSemanticNodeKind.Break => "\n",
            _ => null,
        };

    private static SemanticNodeId CreateNodeId(
        string sourcePartUri,
        WordSemanticNodeKind kind,
        string identityContext,
        string signature,
        int occurrence
    )
    {
        var material = string.Join(
            '\u001f',
            sourcePartUri,
            kind.ToString(),
            identityContext,
            signature,
            occurrence.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var encoded = Convert.ToBase64String(digest.AsSpan(0, 15))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return new SemanticNodeId("wdn_" + encoded);
    }

    private static string QualifiedName(XName name)
    {
        var prefix = name.NamespaceName switch
        {
            WordTransitionalNamespace or WordStrictNamespace => "w",
            MathTransitionalNamespace or MathStrictNamespace => "m",
            MarkupCompatibilityNamespace => "mc",
            _ when IsDrawingNamespace(name.NamespaceName) => "draw",
            "" => "none",
            _ => "ns" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(name.NamespaceName)).AsSpan(0, 3)
            ).ToLowerInvariant(),
        };
        return $"{prefix}:{name.LocalName}";
    }

    private static bool IsWordNamespace(string namespaceName) =>
        namespaceName is WordTransitionalNamespace or WordStrictNamespace;

    private static bool IsMathNamespace(string namespaceName) =>
        namespaceName is MathTransitionalNamespace or MathStrictNamespace;

    private static bool IsDrawingNamespace(string namespaceName) =>
        namespaceName.Contains("/drawingml/", StringComparison.Ordinal)
        || namespaceName.EndsWith("/wordprocessingDrawing", StringComparison.Ordinal);

    private static bool IsWordMainContentType(string? contentType) =>
        contentType is not null
        && contentType.EndsWith(".main+xml", StringComparison.OrdinalIgnoreCase)
        && (
            contentType.Contains("wordprocessingml", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("ms-word", StringComparison.OrdinalIgnoreCase)
        );

    private static void AddIfPresent(
        IDictionary<string, string> properties,
        string name,
        string? value
    )
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            properties[name] = value;
        }
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private sealed record ProjectionContext(
        MutableSemanticNode? parent,
        string identityContext,
        Dictionary<string, int> occurrences,
        List<MutableSemanticNode> roots
    );

    private sealed class ProjectionState
    {
        public Dictionary<XElement, string> Fingerprints { get; } = [];

        public int SemanticNodeCount { get; set; }
    }

    private sealed class MutableSemanticNode
    {
        public MutableSemanticNode(
            SemanticNodeId id,
            WordSemanticNodeKind kind,
            SemanticNodeId? parentId,
            int sourceOrder,
            string sourcePartUri,
            string sourcePath,
            string? text,
            IDictionary<string, string> properties
        )
        {
            Id = id;
            Kind = kind;
            ParentId = parentId;
            SourceOrder = sourceOrder;
            SourcePartUri = sourcePartUri;
            SourcePath = sourcePath;
            Text = text;
            Properties = properties;
        }

        public SemanticNodeId Id { get; }

        public WordSemanticNodeKind Kind { get; }

        public SemanticNodeId? ParentId { get; }

        public int SourceOrder { get; }

        public string SourcePartUri { get; }

        public string SourcePath { get; }

        public string? Text { get; }

        public IDictionary<string, string> Properties { get; }

        public List<MutableSemanticNode> Children { get; } = [];

        public WordSemanticNode Freeze() => new(
            Id,
            Kind,
            ParentId,
            SourceOrder,
            SourcePartUri,
            SourcePath,
            Text,
            Properties,
            Children.Select(child => child.Freeze()).ToArray()
        );
    }
}
