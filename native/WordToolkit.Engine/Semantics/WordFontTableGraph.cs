using System.Collections.ObjectModel;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordFontTableIssueSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record WordFontTableIssue(
    string Code,
    WordFontTableIssueSeverity Severity,
    string Message,
    string? FontName = null,
    string? RelationshipId = null,
    string? PartUri = null
);

public enum WordEmbeddedFontFaceKind
{
    Regular,
    Bold,
    Italic,
    BoldItalic,
}

public sealed record WordFontSignature(
    string? UnicodeSubset0,
    string? UnicodeSubset1,
    string? UnicodeSubset2,
    string? UnicodeSubset3,
    string? CodePageSubset0,
    string? CodePageSubset1,
    int SourceElementOrdinal
);

public sealed record WordEmbeddedFontFace(
    WordEmbeddedFontFaceKind Kind,
    string RelationshipId,
    string? FontKey,
    bool HasValidFontKey,
    bool HasAllZeroFontKey,
    string? PartUri,
    string? ContentType,
    long? ByteLength,
    string? Sha256,
    bool IsObfuscated,
    bool IsWordReadable,
    int SourceElementOrdinal,
    IReadOnlyList<string> UnmodeledAttributes
);

public sealed class WordFontDefinition
{
    internal WordFontDefinition(
        string name,
        string? alternateName,
        string? characterSet,
        string? family,
        string? pitch,
        string? panose,
        bool notTrueType,
        WordFontSignature? signature,
        IReadOnlyList<WordEmbeddedFontFace> embeddedFaces,
        int sourceElementOrdinal,
        IReadOnlyList<string> unmodeledElements,
        IReadOnlyList<string> unmodeledAttributes
    )
    {
        Name = name;
        AlternateName = alternateName;
        CharacterSet = characterSet;
        Family = family;
        Pitch = pitch;
        Panose = panose;
        NotTrueType = notTrueType;
        Signature = signature;
        EmbeddedFaces = new ReadOnlyCollection<WordEmbeddedFontFace>(
            embeddedFaces.ToArray()
        );
        SourceElementOrdinal = sourceElementOrdinal;
        UnmodeledElements = new ReadOnlyCollection<string>(
            unmodeledElements.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
        UnmodeledAttributes = new ReadOnlyCollection<string>(
            unmodeledAttributes.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
    }

    public string Name { get; }

    public string? AlternateName { get; }

    public string? CharacterSet { get; }

    public string? Family { get; }

    public string? Pitch { get; }

    public string? Panose { get; }

    public bool NotTrueType { get; }

    public WordFontSignature? Signature { get; }

    public IReadOnlyList<WordEmbeddedFontFace> EmbeddedFaces { get; }

    public bool HasWordReadableEmbeddedFace => EmbeddedFaces.Any(face =>
        face.IsWordReadable
    );

    public int SourceElementOrdinal { get; }

    public IReadOnlyList<string> UnmodeledElements { get; }

    public IReadOnlyList<string> UnmodeledAttributes { get; }
}

public sealed class WordFontTableGraph
{
    private readonly IReadOnlyDictionary<string, WordFontDefinition> _uniqueFonts;

    internal WordFontTableGraph(
        string packageFingerprint,
        string mainPartUri,
        string? fontTablePartUri,
        IReadOnlyList<WordFontDefinition> fonts,
        IReadOnlyList<WordFontTableIssue> issues,
        IReadOnlyList<string> referencedEmbeddedFontPartUris,
        IReadOnlyList<string> unreferencedEmbeddedFontPartUris,
        IReadOnlyList<string> unmodeledRootElements
    )
    {
        PackageFingerprint = packageFingerprint;
        MainPartUri = mainPartUri;
        FontTablePartUri = fontTablePartUri;
        Fonts = new ReadOnlyCollection<WordFontDefinition>(fonts.ToArray());
        _uniqueFonts = new ReadOnlyDictionary<string, WordFontDefinition>(
            fonts.GroupBy(font => font.Name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() == 1)
                .ToDictionary(
                    group => group.Key,
                    group => group.Single(),
                    StringComparer.OrdinalIgnoreCase
                )
        );
        Issues = new ReadOnlyCollection<WordFontTableIssue>(issues.ToArray());
        ReferencedEmbeddedFontPartUris = new ReadOnlyCollection<string>(
            referencedEmbeddedFontPartUris.ToArray()
        );
        UnreferencedEmbeddedFontPartUris = new ReadOnlyCollection<string>(
            unreferencedEmbeddedFontPartUris.ToArray()
        );
        UnmodeledRootElements = new ReadOnlyCollection<string>(
            unmodeledRootElements.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
    }

    public string PackageFingerprint { get; }

    public string MainPartUri { get; }

    public string? FontTablePartUri { get; }

    public bool HasFontTablePart => FontTablePartUri is not null;

    public IReadOnlyList<WordFontDefinition> Fonts { get; }

    public IReadOnlyList<WordFontTableIssue> Issues { get; }

    public IReadOnlyList<string> ReferencedEmbeddedFontPartUris { get; }

    public IReadOnlyList<string> UnreferencedEmbeddedFontPartUris { get; }

    public IReadOnlyList<string> UnmodeledRootElements { get; }

    public bool TryGetFont(string name, out WordFontDefinition? font)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _uniqueFonts.TryGetValue(name, out font);
    }
}

public sealed record WordFontTableGraphOptions
{
    public static WordFontTableGraphOptions Default { get; } = new();

    public int MaxFontTablePartBytes { get; init; } = 32 * 1024 * 1024;

    public int MaxFonts { get; init; } = 100_000;

    public int MaxEmbeddedFaces { get; init; } = 400_000;

    internal void Validate()
    {
        if (MaxFontTablePartBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxFontTablePartBytes));
        }
        if (MaxFonts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxFonts));
        }
        if (MaxEmbeddedFaces <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxEmbeddedFaces));
        }
    }
}

public sealed class WordFontTableGraphBuilder
{
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string RelationshipsTransitionalNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RelationshipsStrictNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/relationships";
    private const string FontTableRelationship =
        RelationshipsTransitionalNamespace + "/fontTable";
    private const string StrictFontTableRelationship =
        RelationshipsStrictNamespace + "/fontTable";
    private const string FontRelationship =
        RelationshipsTransitionalNamespace + "/font";
    private const string StrictFontRelationship =
        RelationshipsStrictNamespace + "/font";
    private const string FontTableContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml";
    private const string ObfuscatedFontContentType =
        "application/vnd.openxmlformats-officedocument.obfuscatedFont";
    private const string TrueTypeFontContentType = "application/x-font-ttf";
    private const string BitmapFontContentType = "application/x-fontdata";

    private static readonly IReadOnlyDictionary<string, WordEmbeddedFontFaceKind>
        EmbeddedFaceElements = new Dictionary<string, WordEmbeddedFontFaceKind>(
            StringComparer.Ordinal
        )
        {
            ["embedRegular"] = WordEmbeddedFontFaceKind.Regular,
            ["embedBold"] = WordEmbeddedFontFaceKind.Bold,
            ["embedItalic"] = WordEmbeddedFontFaceKind.Italic,
            ["embedBoldItalic"] = WordEmbeddedFontFaceKind.BoldItalic,
        };

    private readonly WordFontTableGraphOptions _options;

    public WordFontTableGraphBuilder(WordFontTableGraphOptions? options = null)
    {
        _options = options ?? WordFontTableGraphOptions.Default;
        _options.Validate();
    }

    public WordFontTableGraph Build(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        cancellationToken.ThrowIfCancellationRequested();
        if (
            !string.Equals(
                package.Fingerprint,
                semanticDocument.PackageFingerprint,
                StringComparison.Ordinal
            )
        )
        {
            throw new WordFontTableProjectionException(
                "Font-table graph requires package and semantic snapshots from the same document version."
            );
        }

        var part = ResolveFontTablePart(package, semanticDocument.MainPartUri);
        if (part is null)
        {
            return EmptyGraph(package.Fingerprint, semanticDocument.MainPartUri);
        }
        var source = ParseFontTablePart(part, cancellationToken);
        var root = source.ParsedDocument.Root;
        if (
            root is null
            || !IsWordNamespace(root.Name.NamespaceName)
            || root.Name.LocalName != "fonts"
        )
        {
            throw new WordFontTableProjectionException(
                "Word font-table part does not have a w:fonts root element."
            );
        }
        var w = root.Name.Namespace;
        var fontElements = root.Elements(w + "font").ToArray();
        if (fontElements.Length > _options.MaxFonts)
        {
            throw new WordFontTableLimitException(
                "Font table exceeds the configured font-definition limit."
            );
        }
        var issues = new List<WordFontTableIssue>();
        var faceCount = 0;
        var fonts = new List<WordFontDefinition>(fontElements.Length);
        foreach (var element in fontElements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var definition = ParseFontDefinition(
                package,
                part.Uri,
                element,
                w,
                source,
                issues,
                ref faceCount
            );
            fonts.Add(definition);
        }
        foreach (
            var duplicate in fonts.GroupBy(
                font => font.Name,
                StringComparer.OrdinalIgnoreCase
            ).Where(group => group.Count() > 1)
        )
        {
            issues.Add(
                new WordFontTableIssue(
                    "FONT_TABLE_DUPLICATE_NAME",
                    WordFontTableIssueSeverity.Error,
                    $"Font name '{duplicate.Key}' is declared more than once; case-insensitive lookup is ambiguous.",
                    duplicate.Key
                )
            );
        }

        var referenced = fonts.SelectMany(font => font.EmbeddedFaces)
            .Select(face => face.PartUri)
            .Where(uri => uri is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var referencedSet = referenced.ToHashSet(StringComparer.Ordinal);
        var unreferenced = package.RelationshipsFrom(part.Uri)
            .Where(relationship => IsFontRelationship(relationship.Type))
            .Select(relationship => relationship.ResolvedTargetPartUri)
            .Where(uri => uri is not null && !referencedSet.Contains(uri))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (var uri in unreferenced)
        {
            issues.Add(
                new WordFontTableIssue(
                    "FONT_TABLE_UNREFERENCED_FONT_PART",
                    WordFontTableIssueSeverity.Warning,
                    "The font table has a font relationship whose target is not referenced by any font face.",
                    PartUri: uri
                )
            );
        }
        var unmodeledRoot = root.Elements()
            .Where(element => element.Name != w + "font")
            .Select(element => QualifiedName(element.Name))
            .Distinct(StringComparer.Ordinal)
            .Order()
            .ToArray();
        return new WordFontTableGraph(
            package.Fingerprint,
            semanticDocument.MainPartUri,
            part.Uri,
            fonts,
            issues,
            referenced,
            unreferenced,
            unmodeledRoot
        );
    }

    private static WordFontTableGraph EmptyGraph(
        string fingerprint,
        string mainPartUri
    ) => new(
        fingerprint,
        mainPartUri,
        null,
        Array.Empty<WordFontDefinition>(),
        Array.Empty<WordFontTableIssue>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>()
    );

    private WordFontDefinition ParseFontDefinition(
        OpcPackageSnapshot package,
        string fontTablePartUri,
        XElement element,
        XNamespace w,
        LosslessXmlDocument source,
        List<WordFontTableIssue> issues,
        ref int faceCount
    )
    {
        var name = RequiredBoundedAttribute(element, w + "name", 1_024);
        var alternateName = ChildValue(element, w, "altName", 1_024);
        var characterSet = ChildValue(element, w, "charset", 128);
        var family = ChildValue(element, w, "family", 128);
        var pitch = ChildValue(element, w, "pitch", 128);
        var panose = ChildValue(element, w, "panose1", 128);
        ValidateHex(characterSet, 2, "FONT_TABLE_CHARSET_INVALID", name, "character set", issues);
        ValidateHex(panose, 20, "FONT_TABLE_PANOSE_INVALID", name, "PANOSE value", issues);
        var notTrueTypeElement = OptionalSingleChild(element, w + "notTrueType");
        var signature = ParseSignature(element, w, source, name, issues);
        var faces = new List<WordEmbeddedFontFace>();
        foreach (var pair in EmbeddedFaceElements)
        {
            var faceElement = OptionalSingleChild(element, w + pair.Key);
            if (faceElement is null)
            {
                continue;
            }
            faceCount++;
            if (faceCount > _options.MaxEmbeddedFaces)
            {
                throw new WordFontTableLimitException(
                    "Font table exceeds the configured embedded-face limit."
                );
            }
            faces.Add(
                ParseEmbeddedFace(
                    package,
                    fontTablePartUri,
                    faceElement,
                    pair.Value,
                    w,
                    source,
                    name,
                    issues
                )
            );
        }

        var knownChildren = new HashSet<XName>
        {
            w + "altName",
            w + "charset",
            w + "family",
            w + "notTrueType",
            w + "panose1",
            w + "pitch",
            w + "sig",
        };
        foreach (var faceName in EmbeddedFaceElements.Keys)
        {
            knownChildren.Add(w + faceName);
        }
        return new WordFontDefinition(
            name,
            alternateName,
            characterSet,
            family,
            pitch,
            panose,
            notTrueTypeElement is not null
                && ParseOnOff(notTrueTypeElement, "notTrueType"),
            signature,
            faces,
            source.GetElementOrdinal(element),
            element.Elements()
                .Where(child => !knownChildren.Contains(child.Name))
                .Select(child => QualifiedName(child.Name))
                .Distinct(StringComparer.Ordinal)
                .Order()
                .ToArray(),
            UnknownAttributes(
                element,
                new HashSet<string>(StringComparer.Ordinal) { "name" }
            )
        );
    }

    private static WordFontSignature? ParseSignature(
        XElement font,
        XNamespace w,
        LosslessXmlDocument source,
        string fontName,
        List<WordFontTableIssue> issues
    )
    {
        var element = OptionalSingleChild(font, w + "sig");
        if (element is null)
        {
            return null;
        }
        var usb0 = OptionalBoundedAttribute(element, w + "usb0", 64);
        var usb1 = OptionalBoundedAttribute(element, w + "usb1", 64);
        var usb2 = OptionalBoundedAttribute(element, w + "usb2", 64);
        var usb3 = OptionalBoundedAttribute(element, w + "usb3", 64);
        var csb0 = OptionalBoundedAttribute(element, w + "csb0", 64);
        var csb1 = OptionalBoundedAttribute(element, w + "csb1", 64);
        foreach (
            var value in new[]
            {
                (Name: "usb0", Value: usb0),
                (Name: "usb1", Value: usb1),
                (Name: "usb2", Value: usb2),
                (Name: "usb3", Value: usb3),
                (Name: "csb0", Value: csb0),
                (Name: "csb1", Value: csb1),
            }
        )
        {
            ValidateHex(
                value.Value,
                8,
                "FONT_TABLE_SIGNATURE_INVALID",
                fontName,
                $"signature {value.Name}",
                issues
            );
        }
        return new WordFontSignature(
            usb0,
            usb1,
            usb2,
            usb3,
            csb0,
            csb1,
            source.GetElementOrdinal(element)
        );
    }

    private static WordEmbeddedFontFace ParseEmbeddedFace(
        OpcPackageSnapshot package,
        string fontTablePartUri,
        XElement element,
        WordEmbeddedFontFaceKind kind,
        XNamespace w,
        LosslessXmlDocument source,
        string fontName,
        List<WordFontTableIssue> issues
    )
    {
        var relationshipId = RequiredRelationshipId(element);
        var key = OptionalBoundedAttribute(element, w + "fontKey", 128);
        var parsedKey = Guid.Empty;
        var validKey = key is not null && Guid.TryParse(key, out parsedKey);
        var zeroKey = validKey && parsedKey == Guid.Empty;
        if (!validKey)
        {
            issues.Add(
                new WordFontTableIssue(
                    "FONT_TABLE_FONT_KEY_INVALID",
                    WordFontTableIssueSeverity.Error,
                    key is null
                        ? "Embedded font face has no fontKey."
                        : $"Embedded font face has invalid fontKey '{key}'.",
                    fontName,
                    relationshipId
                )
            );
        }

        var candidates = package.RelationshipsFrom(fontTablePartUri)
            .Where(relationship =>
                string.Equals(relationship.Id, relationshipId, StringComparison.Ordinal)
            )
            .Take(2)
            .ToArray();
        OpcPart? targetPart = null;
        if (candidates.Length != 1)
        {
            issues.Add(
                new WordFontTableIssue(
                    "FONT_TABLE_RELATIONSHIP_UNRESOLVED",
                    WordFontTableIssueSeverity.Error,
                    candidates.Length == 0
                        ? $"Embedded font relationship '{relationshipId}' is missing."
                        : $"Embedded font relationship '{relationshipId}' is duplicated.",
                    fontName,
                    relationshipId
                )
            );
        }
        else
        {
            var relationship = candidates[0];
            if (!IsFontRelationship(relationship.Type))
            {
                issues.Add(
                    new WordFontTableIssue(
                        "FONT_TABLE_RELATIONSHIP_TYPE_INVALID",
                        WordFontTableIssueSeverity.Error,
                        $"Relationship '{relationshipId}' does not use the Word font relationship type.",
                        fontName,
                        relationshipId,
                        relationship.ResolvedTargetPartUri
                    )
                );
            }
            else if (
                relationship.TargetMode != OpcRelationshipTargetMode.Internal
                || relationship.ResolvedTargetPartUri is null
                || !package.Parts.TryGetValue(
                    relationship.ResolvedTargetPartUri,
                    out targetPart
                )
            )
            {
                issues.Add(
                    new WordFontTableIssue(
                        "FONT_TABLE_FONT_TARGET_INVALID",
                        WordFontTableIssueSeverity.Error,
                        $"Relationship '{relationshipId}' does not resolve to an internal font part.",
                        fontName,
                        relationshipId,
                        relationship.ResolvedTargetPartUri
                    )
                );
            }
        }

        var contentType = targetPart?.ContentType;
        var isObfuscated = string.Equals(
            contentType,
            ObfuscatedFontContentType,
            StringComparison.OrdinalIgnoreCase
        );
        var isTrueType = string.Equals(
            contentType,
            TrueTypeFontContentType,
            StringComparison.OrdinalIgnoreCase
        );
        var isWordReadable = targetPart is not null
            && validKey
            && (isObfuscated || isTrueType && zeroKey);
        if (targetPart is not null && !isWordReadable)
        {
            var code = string.Equals(
                contentType,
                BitmapFontContentType,
                StringComparison.OrdinalIgnoreCase
            )
                ? "FONT_TABLE_BITMAP_FONT_UNSUPPORTED_BY_WORD"
                : isTrueType && !zeroKey
                    ? "FONT_TABLE_TTF_REQUIRES_ZERO_KEY"
                    : "FONT_TABLE_CONTENT_TYPE_UNSUPPORTED_BY_WORD";
            issues.Add(
                new WordFontTableIssue(
                    code,
                    WordFontTableIssueSeverity.Warning,
                    $"Embedded font content type '{contentType ?? "(missing)"}' is not usable by Word with the declared key.",
                    fontName,
                    relationshipId,
                    targetPart.Uri
                )
            );
        }
        return new WordEmbeddedFontFace(
            kind,
            relationshipId,
            key,
            validKey,
            zeroKey,
            targetPart?.Uri,
            contentType,
            targetPart?.Entry.UncompressedLength,
            targetPart?.Entry.Sha256,
            isObfuscated,
            isWordReadable,
            source.GetElementOrdinal(element),
            UnknownAttributes(
                element,
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "fontKey",
                    "id",
                    "subsetted",
                }
            )
        );
    }

    private static OpcPart? ResolveFontTablePart(
        OpcPackageSnapshot package,
        string mainPartUri
    )
    {
        var relationships = package.RelationshipsFrom(mainPartUri)
            .Where(relationship =>
                relationship.Type is FontTableRelationship
                    or StrictFontTableRelationship
            )
            .ToArray();
        if (relationships.Length == 0)
        {
            return null;
        }
        if (relationships.Length != 1)
        {
            throw new WordFontTableProjectionException(
                "Main document part contains multiple fontTable relationships."
            );
        }
        var relationship = relationships[0];
        if (
            relationship.TargetMode != OpcRelationshipTargetMode.Internal
            || relationship.ResolvedTargetPartUri is null
            || !package.Parts.TryGetValue(relationship.ResolvedTargetPartUri, out var part)
            || !string.Equals(
                part.ContentType,
                FontTableContentType,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new WordFontTableProjectionException(
                "fontTable relationship does not resolve to a valid Word font-table part."
            );
        }
        return part;
    }

    private LosslessXmlDocument ParseFontTablePart(
        OpcPart part,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return LosslessXmlDocument.Parse(
                part.Entry.Content,
                new LosslessXmlOptions
                {
                    MaxSourceBytes = _options.MaxFontTablePartBytes,
                    MaxXmlCharacters = _options.MaxFontTablePartBytes,
                    MaxXmlElements = 524_288,
                    MaxXmlDepth = 128,
                    MaxTextCharacters = _options.MaxFontTablePartBytes,
                },
                cancellationToken
            );
        }
        catch (LosslessXmlLimitException exception)
        {
            throw new WordFontTableLimitException(
                "Word font-table part exceeds a font-table graph XML limit: "
                    + exception.Message
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordFontTableProjectionException(
                "Word font-table part is not safe, bounded, well-formed XML.",
                exception
            );
        }
    }

    private static string? ChildValue(
        XElement parent,
        XNamespace w,
        string name,
        int maximumLength
    )
    {
        var child = OptionalSingleChild(parent, w + name);
        return child is null
            ? null
            : OptionalBoundedAttribute(child, w + "val", maximumLength);
    }

    private static XElement? OptionalSingleChild(XElement parent, XName name)
    {
        var children = parent.Elements(name).Take(2).ToArray();
        if (children.Length > 1)
        {
            throw new WordFontTableProjectionException(
                $"Font element '{parent.Name.LocalName}' contains duplicate '{name.LocalName}' children."
            );
        }
        return children.SingleOrDefault();
    }

    private static string RequiredRelationshipId(XElement element)
    {
        var values = element.Attributes()
            .Where(attribute =>
                attribute.Name.LocalName == "id"
                && attribute.Name.NamespaceName is RelationshipsTransitionalNamespace
                    or RelationshipsStrictNamespace
            )
            .Select(attribute => attribute.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0]))
        {
            throw new WordFontTableProjectionException(
                $"Element '{element.Name.LocalName}' does not declare exactly one relationship ID."
            );
        }
        if (values[0].Length > 1_024)
        {
            throw new WordFontTableProjectionException(
                "Embedded font relationship ID exceeds its bound."
            );
        }
        return values[0];
    }

    private static string RequiredBoundedAttribute(
        XElement element,
        XName name,
        int maximumLength
    )
    {
        var value = element.Attribute(name)?.Value;
        if (string.IsNullOrEmpty(value) || value.Length > maximumLength)
        {
            throw new WordFontTableProjectionException(
                $"Attribute '{name.LocalName}' on '{element.Name.LocalName}' is missing or exceeds its bound."
            );
        }
        return value;
    }

    private static string? OptionalBoundedAttribute(
        XElement element,
        XName name,
        int maximumLength
    )
    {
        var value = element.Attribute(name)?.Value;
        if (value is not null && value.Length > maximumLength)
        {
            throw new WordFontTableProjectionException(
                $"Attribute '{name.LocalName}' on '{element.Name.LocalName}' exceeds its bound."
            );
        }
        return value;
    }

    private static bool ParseOnOff(XElement element, string description)
    {
        var raw = element.Attribute(element.Name.Namespace + "val")?.Value;
        return raw?.ToLowerInvariant() switch
        {
            null or "true" or "1" or "on" => true,
            "false" or "0" or "off" => false,
            _ => throw new WordFontTableProjectionException(
                $"Font property '{description}' has invalid on/off value '{raw}'."
            ),
        };
    }

    private static void ValidateHex(
        string? value,
        int expectedLength,
        string code,
        string fontName,
        string description,
        List<WordFontTableIssue> issues
    )
    {
        if (value is null)
        {
            return;
        }
        if (
            value.Length == expectedLength
            && value.All(character =>
                character is >= '0' and <= '9'
                    or >= 'a' and <= 'f'
                    or >= 'A' and <= 'F'
            )
        )
        {
            return;
        }
        issues.Add(
            new WordFontTableIssue(
                code,
                WordFontTableIssueSeverity.Warning,
                $"Font {description} '{value}' is not {expectedLength} hexadecimal digits.",
                fontName
            )
        );
    }

    private static IReadOnlyList<string> UnknownAttributes(
        XElement element,
        IReadOnlySet<string> knownLocalNames
    ) => element.Attributes()
        .Where(attribute =>
            !attribute.IsNamespaceDeclaration
            && !knownLocalNames.Contains(attribute.Name.LocalName)
        )
        .Select(attribute => "@" + QualifiedName(attribute.Name))
        .Distinct(StringComparer.Ordinal)
        .Order()
        .ToArray();

    private static bool IsFontRelationship(string value) =>
        value is FontRelationship or StrictFontRelationship;

    private static bool IsWordNamespace(string value) =>
        value is WordTransitionalNamespace or WordStrictNamespace;

    private static string QualifiedName(XName name) =>
        $"{{{name.NamespaceName}}}{name.LocalName}";
}

public class WordFontTableProjectionException : IOException
{
    public WordFontTableProjectionException(string message)
        : base(message)
    {
    }

    public WordFontTableProjectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class WordFontTableLimitException : WordFontTableProjectionException
{
    public WordFontTableLimitException(string message)
        : base(message)
    {
    }
}
