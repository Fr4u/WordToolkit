using System.Collections.ObjectModel;
using System.Text;

namespace WordToolkit.Engine.Semantics;

public readonly record struct SemanticNodeId(string Value)
{
    public override string ToString() => Value;
}

public enum WordSemanticNodeKind
{
    Document,
    Body,
    Paragraph,
    Run,
    Text,
    Tab,
    Break,
    Table,
    TableRow,
    TableCell,
    Hyperlink,
    Field,
    Equation,
    EquationComponent,
    ContentControl,
    Bookmark,
    CommentAnchor,
    Revision,
    Drawing,
    AlternateContent,
    ExtensionIsland,
    Header,
    Footer,
    Footnotes,
    Footnote,
    Endnotes,
    Endnote,
    Comments,
    Comment,
    GlossaryDocument,
    GlossaryEntry,
    TextBox,
    HeaderReference,
    FooterReference,
    FootnoteReference,
    EndnoteReference,
    Section,
}

public sealed class WordSemanticNode
{
    internal WordSemanticNode(
        SemanticNodeId id,
        WordSemanticNodeKind kind,
        SemanticNodeId? parentId,
        int sourceOrder,
        int sourceElementOrdinal,
        string sourcePartUri,
        string sourcePath,
        string? text,
        IDictionary<string, string> properties,
        IReadOnlyList<WordSemanticNode> children
    )
    {
        Id = id;
        Kind = kind;
        ParentId = parentId;
        SourceOrder = sourceOrder;
        SourceElementOrdinal = sourceElementOrdinal;
        SourcePartUri = sourcePartUri;
        SourcePath = sourcePath;
        Text = text;
        Properties = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(properties, StringComparer.Ordinal)
        );
        Children = children;
    }

    public SemanticNodeId Id { get; }

    public WordSemanticNodeKind Kind { get; }

    public SemanticNodeId? ParentId { get; }

    public int SourceOrder { get; }

    public int SourceElementOrdinal { get; }

    public string SourcePartUri { get; }

    public string SourcePath { get; }

    public string? Text { get; }

    public IReadOnlyDictionary<string, string> Properties { get; }

    public IReadOnlyList<WordSemanticNode> Children { get; }

    public IEnumerable<WordSemanticNode> DescendantsAndSelf()
    {
        var stack = new Stack<WordSemanticNode>();
        stack.Push(this);
        while (stack.TryPop(out var node))
        {
            yield return node;
            for (var index = node.Children.Count - 1; index >= 0; index--)
            {
                stack.Push(node.Children[index]);
            }
        }
    }

    public string TextPreview(int maxCharacters = 160)
    {
        if (maxCharacters < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        }

        var builder = new StringBuilder(Math.Min(maxCharacters, 256));
        foreach (var node in DescendantsAndSelf())
        {
            if (node.Kind == WordSemanticNodeKind.Paragraph && builder.Length != 0)
            {
                if (builder.Length == maxCharacters)
                {
                    break;
                }

                builder.Append('\n');
            }

            var value = node.Kind switch
            {
                WordSemanticNodeKind.Text or WordSemanticNodeKind.Field => node.Text,
                WordSemanticNodeKind.Tab => "\t",
                WordSemanticNodeKind.Break => "\n",
                _ => null,
            };
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            var remaining = maxCharacters - builder.Length;
            if (remaining <= 0)
            {
                break;
            }

            builder.Append(value.AsSpan(0, Math.Min(value.Length, remaining)));
        }

        return builder.ToString();
    }
}

public sealed class WordSemanticDocument
{
    private readonly IReadOnlyDictionary<SemanticNodeId, WordSemanticNode> _nodes;
    private readonly IReadOnlyList<WordSemanticNode> _nodesInSourceOrder;

    internal WordSemanticDocument(
        string packageFingerprint,
        string mainPartUri,
        WordSemanticNode root,
        IReadOnlyList<string> warnings
    )
    {
        PackageFingerprint = packageFingerprint;
        MainPartUri = mainPartUri;
        Root = root;
        Warnings = warnings;
        var nodes = root.DescendantsAndSelf()
            .OrderBy(node => node.SourceOrder)
            .ToArray();
        _nodesInSourceOrder = new ReadOnlyCollection<WordSemanticNode>(nodes);
        _nodes = new ReadOnlyDictionary<SemanticNodeId, WordSemanticNode>(
            nodes.ToDictionary(node => node.Id)
        );
        ProjectedPartUris = new ReadOnlyCollection<string>(
            nodes.Select(node => node.SourcePartUri)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        );
    }

    public string PackageFingerprint { get; }

    public string MainPartUri { get; }

    public WordSemanticNode Root { get; }

    public IReadOnlyList<string> Warnings { get; }

    public IReadOnlyList<string> ProjectedPartUris { get; }

    public int ProjectedPartCount => ProjectedPartUris.Count;

    public int NodeCount => _nodes.Count;

    public IEnumerable<WordSemanticNode> Nodes => _nodesInSourceOrder;

    public bool TryGetNode(SemanticNodeId id, out WordSemanticNode? node) =>
        _nodes.TryGetValue(id, out node);
}
