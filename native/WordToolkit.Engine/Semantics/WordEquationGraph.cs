using System.Collections.ObjectModel;

namespace WordToolkit.Engine.Semantics;

public enum WordEquationIssueSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record WordEquationIssue(
    string Code,
    WordEquationIssueSeverity Severity,
    string Message,
    string? PartUri = null,
    int? SourceElementOrdinal = null,
    string? EquationId = null,
    string? NodeId = null
);

public enum WordEquationStatus
{
    Complete,
    CompleteWithWarnings,
    UnsupportedContent,
    Malformed,
}

public enum WordMathNodeKind
{
    Sequence,
    Text,
    Run,
    Accent,
    Bar,
    BorderBox,
    Box,
    Delimiter,
    EquationArray,
    Fraction,
    Function,
    GroupCharacter,
    LowerLimit,
    UpperLimit,
    Matrix,
    MatrixRow,
    MatrixCell,
    Nary,
    Phantom,
    Radical,
    PreSubSuperscript,
    Subscript,
    SubSuperscript,
    Superscript,
    WordprocessingContainer,
    Extension,
    UnknownMath,
}

public sealed class WordMathNode
{
    internal WordMathNode(
        string id,
        string? parentId,
        WordMathNodeKind kind,
        string sourceName,
        string role,
        int depth,
        string partUri,
        int sourceElementOrdinal,
        SemanticNodeId? semanticNodeId,
        string? text,
        IReadOnlyDictionary<string, string> properties,
        IReadOnlyList<WordMathNode> children
    )
    {
        Id = id;
        ParentId = parentId;
        Kind = kind;
        SourceName = sourceName;
        Role = role;
        Depth = depth;
        PartUri = partUri;
        SourceElementOrdinal = sourceElementOrdinal;
        SemanticNodeId = semanticNodeId;
        Text = text;
        Properties = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(properties, StringComparer.Ordinal)
        );
        Children = new ReadOnlyCollection<WordMathNode>(children.ToArray());
    }

    public string Id { get; }

    public string? ParentId { get; }

    public WordMathNodeKind Kind { get; }

    public string SourceName { get; }

    public string Role { get; }

    public int Depth { get; }

    public string PartUri { get; }

    public int SourceElementOrdinal { get; }

    public SemanticNodeId? SemanticNodeId { get; }

    public string? Text { get; }

    public IReadOnlyDictionary<string, string> Properties { get; }

    public IReadOnlyList<WordMathNode> Children { get; }

    public IEnumerable<WordMathNode> DescendantsAndSelf()
    {
        var stack = new Stack<WordMathNode>();
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
}

public sealed class WordEquationDefinition
{
    internal WordEquationDefinition(
        string id,
        WordEquationStatus status,
        string partUri,
        WordStoryKind storyKind,
        SemanticNodeId? storyNodeId,
        SemanticNodeId? paragraphNodeId,
        string? mathParagraphId,
        int indexInMathParagraph,
        bool isDisplay,
        bool isInDeletedContent,
        int sourceElementOrdinal,
        string? sourcePath,
        SemanticNodeId? semanticNodeId,
        WordMathNode root,
        string text,
        bool textTruncated,
        int nodeCount,
        int maximumDepth,
        int unsupportedNodeCount
    )
    {
        Id = id;
        Status = status;
        PartUri = partUri;
        StoryKind = storyKind;
        StoryNodeId = storyNodeId;
        ParagraphNodeId = paragraphNodeId;
        MathParagraphId = mathParagraphId;
        IndexInMathParagraph = indexInMathParagraph;
        IsDisplay = isDisplay;
        IsInDeletedContent = isInDeletedContent;
        SourceElementOrdinal = sourceElementOrdinal;
        SourcePath = sourcePath;
        SemanticNodeId = semanticNodeId;
        Root = root;
        Text = text;
        TextTruncated = textTruncated;
        NodeCount = nodeCount;
        MaximumDepth = maximumDepth;
        UnsupportedNodeCount = unsupportedNodeCount;
    }

    public string Id { get; }

    public WordEquationStatus Status { get; }

    public string PartUri { get; }

    public WordStoryKind StoryKind { get; }

    public SemanticNodeId? StoryNodeId { get; }

    public SemanticNodeId? ParagraphNodeId { get; }

    public string? MathParagraphId { get; }

    public int IndexInMathParagraph { get; }

    public bool IsDisplay { get; }

    public bool IsInDeletedContent { get; }

    public int SourceElementOrdinal { get; }

    public string? SourcePath { get; }

    public SemanticNodeId? SemanticNodeId { get; }

    public WordMathNode Root { get; }

    public string Text { get; }

    public int TextCharacterCount => Text.Length;

    public bool TextTruncated { get; }

    public int NodeCount { get; }

    public int MaximumDepth { get; }

    public int UnsupportedNodeCount { get; }

    public bool IsCanonical =>
        Status is WordEquationStatus.Complete
            or WordEquationStatus.CompleteWithWarnings;
}

public sealed record WordMathParagraphDefinition(
    string Id,
    string PartUri,
    WordStoryKind StoryKind,
    SemanticNodeId? StoryNodeId,
    SemanticNodeId? ParagraphNodeId,
    int SourceElementOrdinal,
    string? SourcePath,
    SemanticNodeId? SemanticNodeId,
    string? Justification,
    IReadOnlyList<string> EquationIds
);

public sealed class WordMathSettingsDefinition
{
    internal WordMathSettingsDefinition(
        string partUri,
        int sourceElementOrdinal,
        IReadOnlyDictionary<string, string> properties
    )
    {
        PartUri = partUri;
        SourceElementOrdinal = sourceElementOrdinal;
        Properties = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(properties, StringComparer.Ordinal)
        );
    }

    public string PartUri { get; }

    public int SourceElementOrdinal { get; }

    public IReadOnlyDictionary<string, string> Properties { get; }
}

public sealed class WordEquationGraph
{
    private readonly IReadOnlyDictionary<string, WordEquationDefinition> _byId;

    internal WordEquationGraph(
        string packageFingerprint,
        string mainPartUri,
        IReadOnlyList<WordEquationDefinition> equations,
        IReadOnlyList<WordMathParagraphDefinition> mathParagraphs,
        WordMathSettingsDefinition? settings,
        IReadOnlyList<WordEquationIssue> issues,
        bool issuesTruncated
    )
    {
        PackageFingerprint = packageFingerprint;
        MainPartUri = mainPartUri;
        Equations = new ReadOnlyCollection<WordEquationDefinition>(
            equations.ToArray()
        );
        MathParagraphs = new ReadOnlyCollection<WordMathParagraphDefinition>(
            mathParagraphs.ToArray()
        );
        Settings = settings;
        Issues = new ReadOnlyCollection<WordEquationIssue>(issues.ToArray());
        IssuesTruncated = issuesTruncated;
        _byId = new ReadOnlyDictionary<string, WordEquationDefinition>(
            equations.ToDictionary(equation => equation.Id, StringComparer.Ordinal)
        );
    }

    public string PackageFingerprint { get; }

    public string MainPartUri { get; }

    public IReadOnlyList<WordEquationDefinition> Equations { get; }

    public IReadOnlyList<WordMathParagraphDefinition> MathParagraphs { get; }

    public WordMathSettingsDefinition? Settings { get; }

    public IReadOnlyList<WordEquationIssue> Issues { get; }

    public bool IssuesTruncated { get; }

    public int NodeCount => Equations.Sum(equation => equation.NodeCount);

    public int DisplayEquationCount => Equations.Count(equation => equation.IsDisplay);

    public int InlineEquationCount => Equations.Count - DisplayEquationCount;

    public int MalformedEquationCount => Equations.Count(equation =>
        equation.Status == WordEquationStatus.Malformed
    );

    public int UnsupportedEquationCount => Equations.Count(equation =>
        equation.Status == WordEquationStatus.UnsupportedContent
    );

    public bool TryGetEquation(
        string id,
        out WordEquationDefinition? equation
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _byId.TryGetValue(id, out equation);
    }
}

public sealed record WordEquationGraphOptions
{
    public static WordEquationGraphOptions Default { get; } = new();

    public int MaxEquations { get; init; } = 100_000;

    public int MaxMathParagraphs { get; init; } = 100_000;

    public int MaxNodes { get; init; } = 1_000_000;

    public int MaxDepth { get; init; } = 128;

    public int MaxPropertiesPerNode { get; init; } = 128;

    public int MaxPropertyValueCharacters { get; init; } = 4_096;

    public int MaxTextCharactersPerNode { get; init; } = 1_000_000;

    public int MaxTextCharactersPerEquation { get; init; } = 1_000_000;

    public long MaxTotalTextCharacters { get; init; } = 32L * 1024 * 1024;

    public int MaxIssues { get; init; } = 10_000;

    public int MaxPartBytes { get; init; } = 128 * 1024 * 1024;

    internal void Validate()
    {
        if (MaxEquations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxEquations));
        }
        if (MaxMathParagraphs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxMathParagraphs));
        }
        if (MaxNodes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxNodes));
        }
        if (MaxDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDepth));
        }
        if (MaxPropertiesPerNode <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPropertiesPerNode));
        }
        if (MaxPropertyValueCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxPropertyValueCharacters)
            );
        }
        if (MaxTextCharactersPerNode <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTextCharactersPerNode));
        }
        if (MaxTextCharactersPerEquation <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxTextCharactersPerEquation)
            );
        }
        if (MaxTotalTextCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTotalTextCharacters));
        }
        if (MaxIssues <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxIssues));
        }
        if (MaxPartBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPartBytes));
        }
    }
}

public sealed class WordEquationProjectionException : Exception
{
    public WordEquationProjectionException(string message)
        : base(message) { }

    public WordEquationProjectionException(string message, Exception innerException)
        : base(message, innerException) { }
}

public sealed class WordEquationLimitException : Exception
{
    public WordEquationLimitException(string message)
        : base(message) { }
}
