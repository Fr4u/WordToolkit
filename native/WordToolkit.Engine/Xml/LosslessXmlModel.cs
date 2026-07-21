using System.Collections.ObjectModel;

namespace WordToolkit.Engine.Xml;

public readonly record struct XmlSourceSpan(int ByteOffset, int ByteLength)
{
    public int EndByteOffset => checked(ByteOffset + ByteLength);
}

public sealed class XmlSourceAttribute
{
    internal XmlSourceAttribute(
        string qualifiedName,
        string prefix,
        string localName,
        string namespaceUri,
        string value,
        char quote,
        XmlSourceSpan fullSpan,
        XmlSourceSpan valueSpan
    )
    {
        QualifiedName = qualifiedName;
        Prefix = prefix;
        LocalName = localName;
        NamespaceUri = namespaceUri;
        Value = value;
        Quote = quote;
        FullSpan = fullSpan;
        ValueSpan = valueSpan;
    }

    public string QualifiedName { get; }

    public string Prefix { get; }

    public string LocalName { get; }

    public string NamespaceUri { get; }

    public string Value { get; }

    public char Quote { get; }

    public XmlSourceSpan FullSpan { get; }

    public XmlSourceSpan ValueSpan { get; }
}

public sealed class XmlSourceElement
{
    private IReadOnlyList<XmlSourceElement> _children = Array.Empty<XmlSourceElement>();

    internal XmlSourceElement(
        int ordinal,
        int? parentOrdinal,
        string qualifiedName,
        string prefix,
        string localName,
        string namespaceUri,
        bool isSelfClosing,
        bool hasLexicalMarkupInContent,
        string value,
        XmlSourceSpan fullSpan,
        XmlSourceSpan startTagSpan,
        XmlSourceSpan contentSpan,
        XmlSourceSpan? endTagSpan,
        int startTagCloseByteOffset,
        int? selfClosingSlashByteOffset,
        IReadOnlyList<XmlSourceAttribute> attributes
    )
    {
        Ordinal = ordinal;
        ParentOrdinal = parentOrdinal;
        QualifiedName = qualifiedName;
        Prefix = prefix;
        LocalName = localName;
        NamespaceUri = namespaceUri;
        IsSelfClosing = isSelfClosing;
        HasLexicalMarkupInContent = hasLexicalMarkupInContent;
        Value = value;
        FullSpan = fullSpan;
        StartTagSpan = startTagSpan;
        ContentSpan = contentSpan;
        EndTagSpan = endTagSpan;
        StartTagCloseByteOffset = startTagCloseByteOffset;
        SelfClosingSlashByteOffset = selfClosingSlashByteOffset;
        Attributes = new ReadOnlyCollection<XmlSourceAttribute>(attributes.ToArray());
    }

    public int Ordinal { get; }

    public int? ParentOrdinal { get; }

    public string QualifiedName { get; }

    public string Prefix { get; }

    public string LocalName { get; }

    public string NamespaceUri { get; }

    public bool IsSelfClosing { get; }

    public bool HasLexicalMarkupInContent { get; }

    public string Value { get; }

    public XmlSourceSpan FullSpan { get; }

    public XmlSourceSpan StartTagSpan { get; }

    public XmlSourceSpan ContentSpan { get; }

    public XmlSourceSpan? EndTagSpan { get; }

    public int StartTagCloseByteOffset { get; }

    public int? SelfClosingSlashByteOffset { get; }

    public IReadOnlyList<XmlSourceAttribute> Attributes { get; }

    public IReadOnlyList<XmlSourceElement> Children => _children;

    internal void SetChildren(IReadOnlyList<XmlSourceElement> children)
    {
        _children = new ReadOnlyCollection<XmlSourceElement>(children.ToArray());
    }
}

public sealed class XmlSourcePatch
{
    private readonly byte[] _replacement;

    public XmlSourcePatch(
        int byteOffset,
        int byteLength,
        ReadOnlyMemory<byte> replacement
    )
    {
        if (byteOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteOffset));
        }

        if (byteLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        }

        ByteOffset = byteOffset;
        ByteLength = byteLength;
        _replacement = replacement.ToArray();
    }

    public int ByteOffset { get; }

    public int ByteLength { get; }

    public ReadOnlyMemory<byte> Replacement => _replacement;
}
