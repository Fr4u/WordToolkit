using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public sealed record WordEquationParagraphRewriteOptions
{
    public static WordEquationParagraphRewriteOptions Default { get; } = new();

    public int MaxParagraphs { get; init; } = 100_000;

    public int MaxEquationAnchorsPerParagraph { get; init; } = 128;

    public int MaxTextSlotsPerParagraph { get; init; } = 129;

    public int MaxTextNodesPerParagraph { get; init; } = 2_000;

    public int MaxTextCharactersPerParagraph { get; init; } = 1_000_000;

    public long MaxTotalTextCharacters { get; init; } = 32L * 1024 * 1024;

    internal void Validate()
    {
        if (MaxParagraphs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxParagraphs));
        }
        if (MaxEquationAnchorsPerParagraph <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxEquationAnchorsPerParagraph));
        }
        if (MaxTextSlotsPerParagraph <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTextSlotsPerParagraph));
        }
        if (MaxTextNodesPerParagraph <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTextNodesPerParagraph));
        }
        if (MaxTextCharactersPerParagraph <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTextCharactersPerParagraph));
        }
        if (MaxTotalTextCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTotalTextCharacters));
        }
    }
}

public sealed record WordEquationParagraphTextSlot(
    int Index,
    string Text,
    string TextSha256,
    int CharacterCount,
    IReadOnlyList<SemanticNodeId> TextNodeIds,
    IReadOnlyList<int> TextElementOrdinals,
    bool CanRewrite
);

public sealed record WordEquationParagraphAnchor(
    int Index,
    string Kind,
    int SourceElementOrdinal,
    string ExactXmlSha256,
    int ContainedEquationCount
);

public sealed class WordEquationParagraphRewriteCandidate
{
    internal WordEquationParagraphRewriteCandidate(
        string id,
        string fingerprint,
        SemanticNodeId paragraphNodeId,
        WordStoryKind storyKind,
        string sourcePartUri,
        int sourceElementOrdinal,
        string paragraphStructuralFingerprint,
        IReadOnlyList<WordEquationParagraphTextSlot> textSlots,
        IReadOnlyList<WordEquationParagraphAnchor> equationAnchors,
        IReadOnlyList<string> blockedReasons
    )
    {
        Id = id;
        Fingerprint = fingerprint;
        ParagraphNodeId = paragraphNodeId;
        StoryKind = storyKind;
        SourcePartUri = sourcePartUri;
        SourceElementOrdinal = sourceElementOrdinal;
        ParagraphStructuralFingerprint = paragraphStructuralFingerprint;
        TextSlots = new ReadOnlyCollection<WordEquationParagraphTextSlot>(
            textSlots.ToArray()
        );
        EquationAnchors = new ReadOnlyCollection<WordEquationParagraphAnchor>(
            equationAnchors.ToArray()
        );
        BlockedReasons = new ReadOnlyCollection<string>(blockedReasons.ToArray());
    }

    public string Id { get; }

    public string Fingerprint { get; }

    public SemanticNodeId ParagraphNodeId { get; }

    public WordStoryKind StoryKind { get; }

    public string SourcePartUri { get; }

    public int SourceElementOrdinal { get; }

    public string ParagraphStructuralFingerprint { get; }

    public IReadOnlyList<WordEquationParagraphTextSlot> TextSlots { get; }

    public IReadOnlyList<WordEquationParagraphAnchor> EquationAnchors { get; }

    public IReadOnlyList<string> BlockedReasons { get; }

    public int TextSlotCount => TextSlots.Count;

    public int EquationAnchorCount => EquationAnchors.Count;

    public int TextNodeCount => TextSlots.Sum(slot => slot.TextNodeIds.Count);

    public int TextCharacterCount => TextSlots.Sum(slot => slot.CharacterCount);

    public bool CanRewrite => BlockedReasons.Count == 0
        && EquationAnchors.Count > 0
        && TextSlots.Any(slot => slot.CanRewrite);
}

public sealed class WordEquationParagraphRewriteCatalog
{
    private readonly IReadOnlyDictionary<string, WordEquationParagraphRewriteCandidate> _byId;

    internal WordEquationParagraphRewriteCatalog(
        string packageFingerprint,
        IReadOnlyList<WordEquationParagraphRewriteCandidate> candidates
    )
    {
        PackageFingerprint = packageFingerprint;
        Candidates = new ReadOnlyCollection<WordEquationParagraphRewriteCandidate>(
            candidates.ToArray()
        );
        _byId = new ReadOnlyDictionary<string, WordEquationParagraphRewriteCandidate>(
            candidates.ToDictionary(candidate => candidate.Id, StringComparer.Ordinal)
        );
    }

    public string PackageFingerprint { get; }

    public IReadOnlyList<WordEquationParagraphRewriteCandidate> Candidates { get; }

    public bool TryGetCandidate(
        string id,
        out WordEquationParagraphRewriteCandidate? candidate
    ) => _byId.TryGetValue(id, out candidate);
}

/// <summary>
/// Projects a deliberately closed subset of Word paragraphs as ordinary text slots
/// separated by immutable direct OfficeMath anchors. Fields, revisions, range markers,
/// content controls, drawings, hyperlinks, tabs, breaks and every other inline shape
/// stay outside this mutation model and make the candidate fail closed.
/// </summary>
public sealed class WordEquationParagraphRewriteCatalogBuilder
{
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string MathTransitionalNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string MathStrictNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/math";
    private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";

    private readonly WordEquationParagraphRewriteOptions _options;

    public WordEquationParagraphRewriteCatalogBuilder(
        WordEquationParagraphRewriteOptions? options = null
    )
    {
        _options = options ?? WordEquationParagraphRewriteOptions.Default;
        _options.Validate();
    }

    public WordEquationParagraphRewriteCatalog Build(
        OpcPackageSnapshot package,
        WordSemanticDocument semantic,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semantic);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(
                package.Fingerprint,
                semantic.PackageFingerprint,
                StringComparison.Ordinal
            ))
        {
            throw new WordSemanticPreconditionException(
                "Semantic projection and package snapshot have different fingerprints."
            );
        }

        var paragraphs = semantic.Nodes
            .Where(node => node.Kind == WordSemanticNodeKind.Paragraph)
            .Where(node => node.DescendantsAndSelf().Any(descendant =>
                descendant.Kind == WordSemanticNodeKind.Equation
            ))
            .OrderBy(node => node.SourceOrder)
            .ToArray();
        if (paragraphs.Length > _options.MaxParagraphs)
        {
            throw new WordEquationParagraphRewriteLimitException(
                $"Equation paragraph projection exceeds {_options.MaxParagraphs} paragraphs."
            );
        }

        var sources = new Dictionary<string, LosslessXmlDocument>(StringComparer.Ordinal);
        var candidates = new List<WordEquationParagraphRewriteCandidate>(paragraphs.Length);
        long totalCharacters = 0;
        foreach (var paragraph in paragraphs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!package.Parts.TryGetValue(paragraph.SourcePartUri, out var part))
            {
                throw new WordSemanticPreconditionException(
                    $"Source part '{paragraph.SourcePartUri}' no longer exists."
                );
            }
            if (!sources.TryGetValue(part.Uri, out var source))
            {
                source = LosslessXmlDocument.Parse(
                    part.Entry.Content,
                    cancellationToken: cancellationToken
                );
                sources.Add(part.Uri, source);
            }
            var candidate = BuildCandidate(
                semantic,
                paragraph,
                source,
                cancellationToken
            );
            checked
            {
                totalCharacters += candidate.TextCharacterCount;
            }
            if (totalCharacters > _options.MaxTotalTextCharacters)
            {
                throw new WordEquationParagraphRewriteLimitException(
                    "Equation paragraph text exceeds the configured total character limit."
                );
            }
            candidates.Add(candidate);
        }
        return new WordEquationParagraphRewriteCatalog(package.Fingerprint, candidates);
    }

    private WordEquationParagraphRewriteCandidate BuildCandidate(
        WordSemanticDocument semantic,
        WordSemanticNode paragraph,
        LosslessXmlDocument source,
        CancellationToken cancellationToken
    )
    {
        var blocked = new SortedSet<string>(StringComparer.Ordinal);
        XElement element;
        try
        {
            element = source.GetParsedElement(paragraph.SourceElementOrdinal);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new WordSemanticPreconditionException(
                "An equation paragraph no longer binds to its source element.",
                exception
            );
        }
        if (!IsWordElement(element, "p"))
        {
            throw new WordSemanticPreconditionException(
                "An equation paragraph semantic node no longer binds to w:p."
            );
        }

        var textNodesByOrdinal = paragraph.DescendantsAndSelf()
            .Where(node => node.Kind == WordSemanticNodeKind.Text)
            .Where(node => string.Equals(
                node.SourcePartUri,
                paragraph.SourcePartUri,
                StringComparison.Ordinal
            ))
            .ToDictionary(node => node.SourceElementOrdinal);
        var slots = new List<MutableSlot> { new(0) };
        var anchors = new List<WordEquationParagraphAnchor>();
        var sawContent = false;
        var sawProperties = false;
        foreach (var child in element.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsWordElement(child, "pPr"))
            {
                if (sawContent || sawProperties)
                {
                    blocked.Add("invalid_paragraph_properties_position");
                }
                sawProperties = true;
                continue;
            }
            sawContent = true;
            if (IsMathAnchor(child))
            {
                if (anchors.Count >= _options.MaxEquationAnchorsPerParagraph)
                {
                    throw new WordEquationParagraphRewriteLimitException(
                        "An equation paragraph exceeds the equation-anchor limit."
                    );
                }
                var sourceAnchor = source.GetElement(source.GetElementOrdinal(child));
                var exactBytes = source.SourceBytes.Slice(
                    sourceAnchor.FullSpan.ByteOffset,
                    sourceAnchor.FullSpan.ByteLength
                );
                anchors.Add(new WordEquationParagraphAnchor(
                    anchors.Count,
                    child.Name.LocalName == "oMathPara" ? "display_math" : "inline_math",
                    sourceAnchor.Ordinal,
                    HashBytes(exactBytes.Span),
                    child.DescendantsAndSelf().Count(candidate =>
                        candidate.Name.LocalName == "oMath"
                        && IsMathNamespace(candidate.Name.NamespaceName)
                    )
                ));
                if (slots.Count >= _options.MaxTextSlotsPerParagraph)
                {
                    throw new WordEquationParagraphRewriteLimitException(
                        "An equation paragraph exceeds the text-slot limit."
                    );
                }
                slots.Add(new MutableSlot(slots.Count));
                continue;
            }
            if (!IsWordElement(child, "r"))
            {
                blocked.Add("unsupported_inline_structure");
                continue;
            }
            if (!TryAddOrdinaryRun(
                    source,
                    child,
                    textNodesByOrdinal,
                    slots[^1],
                    blocked
                ))
            {
                blocked.Add("unsupported_run_content");
            }
        }
        if (anchors.Count == 0)
        {
            blocked.Add("no_direct_officemath_anchor");
        }
        if (slots.All(slot => slot.Nodes.Count == 0))
        {
            blocked.Add("no_editable_text_slots");
        }
        var textNodeCount = slots.Sum(slot => slot.Nodes.Count);
        if (textNodeCount > _options.MaxTextNodesPerParagraph)
        {
            throw new WordEquationParagraphRewriteLimitException(
                "An equation paragraph exceeds the text-node limit."
            );
        }
        var frozenSlots = slots.Select(slot => slot.Freeze()).ToArray();
        if (frozenSlots.Sum(slot => slot.CharacterCount)
            > _options.MaxTextCharactersPerParagraph)
        {
            throw new WordEquationParagraphRewriteLimitException(
                "An equation paragraph exceeds the per-paragraph text limit."
            );
        }

        var storyKind = ResolveStoryKind(semantic, paragraph);
        var rewriteStructureFingerprint = RewriteStructureFingerprint(element);
        var fingerprint = CreateCandidateFingerprint(
            paragraph,
            storyKind,
            rewriteStructureFingerprint,
            frozenSlots,
            anchors,
            blocked
        );
        return new WordEquationParagraphRewriteCandidate(
            CreateCandidateId(fingerprint),
            fingerprint,
            paragraph.Id,
            storyKind,
            paragraph.SourcePartUri,
            paragraph.SourceElementOrdinal,
            rewriteStructureFingerprint,
            frozenSlots,
            anchors,
            blocked.ToArray()
        );
    }

    private static bool TryAddOrdinaryRun(
        LosslessXmlDocument source,
        XElement run,
        IReadOnlyDictionary<int, WordSemanticNode> textNodesByOrdinal,
        MutableSlot slot,
        ISet<string> blocked
    )
    {
        var sawRunProperties = false;
        var sawText = false;
        foreach (var child in run.Elements())
        {
            if (IsWordElement(child, "rPr"))
            {
                if (sawRunProperties || sawText)
                {
                    blocked.Add("invalid_run_properties_position");
                    return false;
                }
                sawRunProperties = true;
                continue;
            }
            if (!IsWordElement(child, "t"))
            {
                return false;
            }
            sawText = true;
            var ordinal = source.GetElementOrdinal(child);
            if (!textNodesByOrdinal.TryGetValue(ordinal, out var semanticText))
            {
                blocked.Add("unbound_text_node");
                return false;
            }
            slot.Nodes.Add(semanticText);
            slot.Ordinals.Add(ordinal);
        }
        return sawText;
    }

    private static WordStoryKind ResolveStoryKind(
        WordSemanticDocument semantic,
        WordSemanticNode paragraph
    )
    {
        var current = paragraph;
        while (current.ParentId is { } parentId)
        {
            if (!semantic.TryGetNode(parentId, out var parent) || parent is null)
            {
                throw new WordSemanticPreconditionException(
                    "An equation paragraph has incomplete semantic ancestry."
                );
            }
            var resolved = parent.Kind switch
            {
                WordSemanticNodeKind.Header => WordStoryKind.Header,
                WordSemanticNodeKind.Footer => WordStoryKind.Footer,
                WordSemanticNodeKind.Footnote => WordStoryKind.Footnote,
                WordSemanticNodeKind.Endnote => WordStoryKind.Endnote,
                WordSemanticNodeKind.Comment => WordStoryKind.Comment,
                WordSemanticNodeKind.GlossaryEntry => WordStoryKind.GlossaryEntry,
                WordSemanticNodeKind.TextBox => WordStoryKind.TextBox,
                _ => (WordStoryKind?)null,
            };
            if (resolved is not null)
            {
                return resolved.Value;
            }
            current = parent;
        }
        return WordStoryKind.Main;
    }

    private static string CreateCandidateFingerprint(
        WordSemanticNode paragraph,
        WordStoryKind storyKind,
        string rewriteStructureFingerprint,
        IReadOnlyList<WordEquationParagraphTextSlot> slots,
        IReadOnlyList<WordEquationParagraphAnchor> anchors,
        IEnumerable<string> blockedReasons
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "wordtoolkit-equation-paragraph-candidate-v1");
        Append(hash, paragraph.Id.Value);
        Append(hash, paragraph.SourcePartUri);
        Append(hash, paragraph.SourceElementOrdinal.ToString(
            System.Globalization.CultureInfo.InvariantCulture
        ));
        Append(hash, rewriteStructureFingerprint);
        Append(hash, storyKind.ToString());
        foreach (var slot in slots)
        {
            Append(hash, "slot");
            Append(hash, slot.Index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, slot.TextSha256);
            foreach (var ordinal in slot.TextElementOrdinals)
            {
                Append(hash, ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        foreach (var anchor in anchors)
        {
            Append(hash, "anchor");
            Append(hash, anchor.Index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, anchor.Kind);
            Append(hash, anchor.SourceElementOrdinal.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ));
            Append(hash, anchor.ExactXmlSha256);
            Append(hash, anchor.ContainedEquationCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ));
        }
        foreach (var reason in blockedReasons)
        {
            Append(hash, reason);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string CreateCandidateId(string fingerprint)
    {
        var bytes = Convert.FromHexString(fingerprint);
        return "wepr_" + Convert.ToBase64String(bytes.AsSpan(0, 15))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static string HashText(string value) => HashBytes(Encoding.UTF8.GetBytes(value));

    private static string RewriteStructureFingerprint(XElement element)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendStructure(element, hash);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendStructure(XElement element, IncrementalHash hash)
    {
        Append(hash, element.Name.NamespaceName);
        Append(hash, element.Name.LocalName);
        foreach (var attribute in element.Attributes()
            .Where(attribute =>
                !attribute.IsNamespaceDeclaration
                && !(IsWordNamespace(attribute.Name.NamespaceName)
                    && attribute.Name.LocalName.StartsWith(
                        "rsid",
                        StringComparison.OrdinalIgnoreCase
                    ))
                && !(IsWordElement(element, "t")
                    && attribute.Name.NamespaceName == XmlNamespace
                    && attribute.Name.LocalName == "space")
            )
            .OrderBy(attribute => attribute.Name.NamespaceName, StringComparer.Ordinal)
            .ThenBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal))
        {
            Append(hash, attribute.Name.NamespaceName);
            Append(hash, attribute.Name.LocalName);
            Append(hash, attribute.Value);
        }
        foreach (var child in element.Elements())
        {
            AppendStructure(child, hash);
        }
    }

    private static string HashBytes(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static bool IsWordElement(XElement element, string localName) =>
        element.Name.LocalName == localName
        && IsWordNamespace(element.Name.NamespaceName);

    private static bool IsWordNamespace(string value) =>
        value is WordTransitionalNamespace or WordStrictNamespace;

    private static bool IsMathAnchor(XElement element) =>
        element.Name.LocalName is "oMath" or "oMathPara"
        && IsMathNamespace(element.Name.NamespaceName);

    private static bool IsMathNamespace(string value) =>
        value is MathTransitionalNamespace or MathStrictNamespace;

    private sealed class MutableSlot(int index)
    {
        internal int Index { get; } = index;

        internal List<WordSemanticNode> Nodes { get; } = [];

        internal List<int> Ordinals { get; } = [];

        internal WordEquationParagraphTextSlot Freeze()
        {
            var text = string.Concat(Nodes.Select(node => node.Text ?? string.Empty));
            return new WordEquationParagraphTextSlot(
                Index,
                text,
                HashText(text),
                text.Length,
                Nodes.Select(node => node.Id).ToArray(),
                Ordinals.ToArray(),
                Nodes.Count != 0
            );
        }
    }
}

public sealed class WordEquationParagraphRewriteLimitException : InvalidOperationException
{
    public WordEquationParagraphRewriteLimitException(string message)
        : base(message)
    {
    }
}
