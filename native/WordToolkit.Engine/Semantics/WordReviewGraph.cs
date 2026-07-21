using System.Collections.ObjectModel;

namespace WordToolkit.Engine.Semantics;

public enum WordReviewIssueSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record WordReviewIssue(
    string Code,
    WordReviewIssueSeverity Severity,
    string Message,
    string? PartUri = null,
    string? StoryId = null,
    int? SourceElementOrdinal = null,
    string? SubjectId = null
);

public enum WordCommentAnchorStatus
{
    Complete,
    PointReference,
    MissingStart,
    MissingEnd,
    MissingReference,
    Ambiguous,
    Reversed,
}

public sealed class WordCommentDefinition
{
    internal WordCommentDefinition(
        string id,
        string? ooxmlId,
        bool isEffectiveByOoxmlId,
        string partUri,
        int sourceElementOrdinal,
        SemanticNodeId? semanticNodeId,
        string? author,
        string? initials,
        string? date,
        string? dateUtc,
        string text,
        int textCharacterCount,
        bool textTruncated,
        IReadOnlyList<string> paragraphIds,
        string? lastParagraphId,
        IReadOnlyList<string> anchorIds,
        string? parentCommentId,
        string threadRootCommentId,
        int threadDepth,
        bool isDone,
        string? durableId,
        string? extensibleDateUtc,
        bool isIntelligentPlaceholder,
        bool hasReactions,
        int extensionCount,
        string? personId
    )
    {
        Id = id;
        OoxmlId = ooxmlId;
        IsEffectiveByOoxmlId = isEffectiveByOoxmlId;
        PartUri = partUri;
        SourceElementOrdinal = sourceElementOrdinal;
        SemanticNodeId = semanticNodeId;
        Author = author;
        Initials = initials;
        Date = date;
        DateUtc = dateUtc;
        Text = text;
        TextCharacterCount = textCharacterCount;
        TextTruncated = textTruncated;
        ParagraphIds = new ReadOnlyCollection<string>(paragraphIds.ToArray());
        LastParagraphId = lastParagraphId;
        AnchorIds = new ReadOnlyCollection<string>(anchorIds.ToArray());
        ParentCommentId = parentCommentId;
        ThreadRootCommentId = threadRootCommentId;
        ThreadDepth = threadDepth;
        IsDone = isDone;
        DurableId = durableId;
        ExtensibleDateUtc = extensibleDateUtc;
        IsIntelligentPlaceholder = isIntelligentPlaceholder;
        HasReactions = hasReactions;
        ExtensionCount = extensionCount;
        PersonId = personId;
    }

    public string Id { get; }

    public string? OoxmlId { get; }

    public bool IsEffectiveByOoxmlId { get; }

    public string PartUri { get; }

    public int SourceElementOrdinal { get; }

    public SemanticNodeId? SemanticNodeId { get; }

    public string? Author { get; }

    public string? Initials { get; }

    public string? Date { get; }

    public string? DateUtc { get; }

    public string Text { get; }

    public int TextCharacterCount { get; }

    public bool TextTruncated { get; }

    public IReadOnlyList<string> ParagraphIds { get; }

    public string? LastParagraphId { get; }

    public IReadOnlyList<string> AnchorIds { get; }

    public string? ParentCommentId { get; }

    public string ThreadRootCommentId { get; }

    public int ThreadDepth { get; }

    public bool IsReply => ParentCommentId is not null;

    public bool IsDone { get; }

    public string? DurableId { get; }

    public string? ExtensibleDateUtc { get; }

    public bool IsIntelligentPlaceholder { get; }

    public bool HasReactions { get; }

    public int ExtensionCount { get; }

    public string? PersonId { get; }

    public bool HasExtendedMetadata =>
        ParentCommentId is not null
        || IsDone
        || DurableId is not null
        || ExtensibleDateUtc is not null
        || IsIntelligentPlaceholder
        || HasReactions
        || ExtensionCount > 0;
}

public sealed class WordCommentAnchor
{
    internal WordCommentAnchor(
        string id,
        string storyId,
        WordStoryKind storyKind,
        SemanticNodeId? storyNodeId,
        string partUri,
        string? ooxmlId,
        string? commentId,
        WordCommentAnchorStatus status,
        int startCount,
        int endCount,
        int referenceCount,
        int? startElementOrdinal,
        int? endElementOrdinal,
        int? referenceElementOrdinal,
        SemanticNodeId? startNodeId,
        SemanticNodeId? endNodeId,
        SemanticNodeId? referenceNodeId,
        string text,
        int textCharacterCount,
        bool textTruncated
    )
    {
        Id = id;
        StoryId = storyId;
        StoryKind = storyKind;
        StoryNodeId = storyNodeId;
        PartUri = partUri;
        OoxmlId = ooxmlId;
        CommentId = commentId;
        Status = status;
        StartCount = startCount;
        EndCount = endCount;
        ReferenceCount = referenceCount;
        StartElementOrdinal = startElementOrdinal;
        EndElementOrdinal = endElementOrdinal;
        ReferenceElementOrdinal = referenceElementOrdinal;
        StartNodeId = startNodeId;
        EndNodeId = endNodeId;
        ReferenceNodeId = referenceNodeId;
        Text = text;
        TextCharacterCount = textCharacterCount;
        TextTruncated = textTruncated;
    }

    public string Id { get; }
    public string StoryId { get; }
    public WordStoryKind StoryKind { get; }
    public SemanticNodeId? StoryNodeId { get; }
    public string PartUri { get; }
    public string? OoxmlId { get; }
    public string? CommentId { get; }
    public WordCommentAnchorStatus Status { get; }
    public int StartCount { get; }
    public int EndCount { get; }
    public int ReferenceCount { get; }
    public int? StartElementOrdinal { get; }
    public int? EndElementOrdinal { get; }
    public int? ReferenceElementOrdinal { get; }
    public SemanticNodeId? StartNodeId { get; }
    public SemanticNodeId? EndNodeId { get; }
    public SemanticNodeId? ReferenceNodeId { get; }
    public string Text { get; }
    public int TextCharacterCount { get; }
    public bool TextTruncated { get; }
    public bool HasDefinition => CommentId is not null;
}

public sealed record WordReviewPersonDefinition(
    string Id,
    string PartUri,
    int SourceElementOrdinal,
    string? Author,
    string? ProviderId,
    string? UserId,
    int CommentCount,
    int RevisionCount
);

public enum WordRevisionKind
{
    Insertion,
    Deletion,
    MoveFrom,
    MoveTo,
    ConflictInsertion,
    ConflictDeletion,
    RunPropertiesChange,
    ParagraphPropertiesChange,
    TablePropertiesChange,
    TableGridChange,
    TableRowPropertiesChange,
    TableCellPropertiesChange,
    SectionPropertiesChange,
    NumberingPropertiesChange,
    NumberingChange,
    CellInsertion,
    CellDeletion,
    CellMerge,
    CustomXmlInsertion,
    CustomXmlDeletion,
    OtherPropertyChange,
}

public enum WordRevisionStatus
{
    Complete,
    MissingId,
    InvalidDate,
}

public sealed class WordRevisionDefinition
{
    internal WordRevisionDefinition(
        string id,
        WordRevisionKind kind,
        WordRevisionStatus status,
        string sourceName,
        string storyId,
        WordStoryKind storyKind,
        SemanticNodeId? storyNodeId,
        SemanticNodeId? paragraphNodeId,
        string partUri,
        int sourceElementOrdinal,
        SemanticNodeId? semanticNodeId,
        string? parentRevisionId,
        string? ooxmlId,
        string? author,
        string? date,
        string? dateUtc,
        string text,
        int textCharacterCount,
        bool textTruncated,
        int contentElementCount,
        bool containsMath,
        bool isInDeletedContent,
        string? personId
    )
    {
        Id = id;
        Kind = kind;
        Status = status;
        SourceName = sourceName;
        StoryId = storyId;
        StoryKind = storyKind;
        StoryNodeId = storyNodeId;
        ParagraphNodeId = paragraphNodeId;
        PartUri = partUri;
        SourceElementOrdinal = sourceElementOrdinal;
        SemanticNodeId = semanticNodeId;
        ParentRevisionId = parentRevisionId;
        OoxmlId = ooxmlId;
        Author = author;
        Date = date;
        DateUtc = dateUtc;
        Text = text;
        TextCharacterCount = textCharacterCount;
        TextTruncated = textTruncated;
        ContentElementCount = contentElementCount;
        ContainsMath = containsMath;
        IsInDeletedContent = isInDeletedContent;
        PersonId = personId;
    }

    public string Id { get; }
    public WordRevisionKind Kind { get; }
    public WordRevisionStatus Status { get; }
    public string SourceName { get; }
    public string StoryId { get; }
    public WordStoryKind StoryKind { get; }
    public SemanticNodeId? StoryNodeId { get; }
    public SemanticNodeId? ParagraphNodeId { get; }
    public string PartUri { get; }
    public int SourceElementOrdinal { get; }
    public SemanticNodeId? SemanticNodeId { get; }
    public string? ParentRevisionId { get; }
    public string? OoxmlId { get; }
    public string? Author { get; }
    public string? Date { get; }
    public string? DateUtc { get; }
    public string Text { get; }
    public int TextCharacterCount { get; }
    public bool TextTruncated { get; }
    public int ContentElementCount { get; }
    public bool ContainsMath { get; }
    public bool IsInDeletedContent { get; }
    public string? PersonId { get; }
}

public enum WordMoveRangeKind
{
    Source,
    Destination,
}

public enum WordReviewRangeStatus
{
    Complete,
    MissingStart,
    MissingEnd,
    Ambiguous,
    Reversed,
}

public sealed class WordMoveRangeDefinition
{
    internal WordMoveRangeDefinition(
        string id,
        WordMoveRangeKind kind,
        WordReviewRangeStatus status,
        string storyId,
        WordStoryKind storyKind,
        SemanticNodeId? storyNodeId,
        string partUri,
        string? ooxmlId,
        string? name,
        string? author,
        string? date,
        int startCount,
        int endCount,
        int? startElementOrdinal,
        int? endElementOrdinal,
        IReadOnlyList<string> revisionIds
    )
    {
        Id = id;
        Kind = kind;
        Status = status;
        StoryId = storyId;
        StoryKind = storyKind;
        StoryNodeId = storyNodeId;
        PartUri = partUri;
        OoxmlId = ooxmlId;
        Name = name;
        Author = author;
        Date = date;
        StartCount = startCount;
        EndCount = endCount;
        StartElementOrdinal = startElementOrdinal;
        EndElementOrdinal = endElementOrdinal;
        RevisionIds = new ReadOnlyCollection<string>(revisionIds.ToArray());
    }

    public string Id { get; }
    public WordMoveRangeKind Kind { get; }
    public WordReviewRangeStatus Status { get; }
    public string StoryId { get; }
    public WordStoryKind StoryKind { get; }
    public SemanticNodeId? StoryNodeId { get; }
    public string PartUri { get; }
    public string? OoxmlId { get; }
    public string? Name { get; }
    public string? Author { get; }
    public string? Date { get; }
    public int StartCount { get; }
    public int EndCount { get; }
    public int? StartElementOrdinal { get; }
    public int? EndElementOrdinal { get; }
    public IReadOnlyList<string> RevisionIds { get; }
}

public enum WordMovePairStatus
{
    Complete,
    MissingSource,
    MissingDestination,
    Ambiguous,
}

public sealed record WordMovePairDefinition(
    string Id,
    string? Name,
    WordMovePairStatus Status,
    string? SourceRangeId,
    string? DestinationRangeId
);

public sealed class WordPermissionRangeDefinition
{
    internal WordPermissionRangeDefinition(
        string id,
        WordReviewRangeStatus status,
        string storyId,
        WordStoryKind storyKind,
        SemanticNodeId? storyNodeId,
        string partUri,
        string? ooxmlId,
        string? editor,
        string? editorGroup,
        int? columnFirst,
        int? columnLast,
        int startCount,
        int endCount,
        int? startElementOrdinal,
        int? endElementOrdinal
    )
    {
        Id = id;
        Status = status;
        StoryId = storyId;
        StoryKind = storyKind;
        StoryNodeId = storyNodeId;
        PartUri = partUri;
        OoxmlId = ooxmlId;
        Editor = editor;
        EditorGroup = editorGroup;
        ColumnFirst = columnFirst;
        ColumnLast = columnLast;
        StartCount = startCount;
        EndCount = endCount;
        StartElementOrdinal = startElementOrdinal;
        EndElementOrdinal = endElementOrdinal;
    }

    public string Id { get; }
    public WordReviewRangeStatus Status { get; }
    public string StoryId { get; }
    public WordStoryKind StoryKind { get; }
    public SemanticNodeId? StoryNodeId { get; }
    public string PartUri { get; }
    public string? OoxmlId { get; }
    public string? Editor { get; }
    public string? EditorGroup { get; }
    public int? ColumnFirst { get; }
    public int? ColumnLast { get; }
    public int StartCount { get; }
    public int EndCount { get; }
    public int? StartElementOrdinal { get; }
    public int? EndElementOrdinal { get; }
}

public sealed record WordReviewSettingsDefinition(
    string PartUri,
    bool TrackRevisions,
    bool DoNotTrackMoves,
    bool DoNotTrackFormatting
);

public sealed class WordReviewGraph
{
    private readonly IReadOnlyDictionary<string, WordCommentDefinition> _commentsById;
    private readonly IReadOnlyDictionary<string, WordRevisionDefinition> _revisionsById;

    internal WordReviewGraph(
        string packageFingerprint,
        string mainPartUri,
        IReadOnlyList<WordCommentDefinition> comments,
        IReadOnlyList<WordCommentAnchor> anchors,
        IReadOnlyList<WordReviewPersonDefinition> people,
        IReadOnlyList<WordRevisionDefinition> revisions,
        IReadOnlyList<WordMoveRangeDefinition> moveRanges,
        IReadOnlyList<WordMovePairDefinition> moves,
        IReadOnlyList<WordPermissionRangeDefinition> permissions,
        WordReviewSettingsDefinition? settings,
        IReadOnlyList<WordReviewIssue> issues,
        bool issuesTruncated
    )
    {
        PackageFingerprint = packageFingerprint;
        MainPartUri = mainPartUri;
        Comments = new ReadOnlyCollection<WordCommentDefinition>(comments.ToArray());
        Anchors = new ReadOnlyCollection<WordCommentAnchor>(anchors.ToArray());
        People = new ReadOnlyCollection<WordReviewPersonDefinition>(people.ToArray());
        Revisions = new ReadOnlyCollection<WordRevisionDefinition>(revisions.ToArray());
        MoveRanges = new ReadOnlyCollection<WordMoveRangeDefinition>(moveRanges.ToArray());
        Moves = new ReadOnlyCollection<WordMovePairDefinition>(moves.ToArray());
        Permissions = new ReadOnlyCollection<WordPermissionRangeDefinition>(
            permissions.ToArray()
        );
        Settings = settings;
        Issues = new ReadOnlyCollection<WordReviewIssue>(issues.ToArray());
        IssuesTruncated = issuesTruncated;
        _commentsById = new ReadOnlyDictionary<string, WordCommentDefinition>(
            comments.ToDictionary(comment => comment.Id, StringComparer.Ordinal)
        );
        _revisionsById = new ReadOnlyDictionary<string, WordRevisionDefinition>(
            revisions.ToDictionary(revision => revision.Id, StringComparer.Ordinal)
        );
    }

    public string PackageFingerprint { get; }
    public string MainPartUri { get; }
    public IReadOnlyList<WordCommentDefinition> Comments { get; }
    public IReadOnlyList<WordCommentAnchor> Anchors { get; }
    public IReadOnlyList<WordReviewPersonDefinition> People { get; }
    public IReadOnlyList<WordRevisionDefinition> Revisions { get; }
    public IReadOnlyList<WordMoveRangeDefinition> MoveRanges { get; }
    public IReadOnlyList<WordMovePairDefinition> Moves { get; }
    public IReadOnlyList<WordPermissionRangeDefinition> Permissions { get; }
    public WordReviewSettingsDefinition? Settings { get; }
    public IReadOnlyList<WordReviewIssue> Issues { get; }
    public bool IssuesTruncated { get; }

    public int ReplyCount => Comments.Count(comment => comment.IsReply);
    public int ResolvedCommentCount => Comments.Count(comment => comment.IsDone);
    public int ThreadCount => Comments.Select(comment => comment.ThreadRootCommentId)
        .Distinct(StringComparer.Ordinal)
        .Count();
    public int TrackedTextCharacterCount => Revisions.Sum(revision => revision.TextCharacterCount);

    public bool TryGetComment(string id, out WordCommentDefinition? comment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _commentsById.TryGetValue(id, out comment);
    }

    public bool TryGetRevision(string id, out WordRevisionDefinition? revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _revisionsById.TryGetValue(id, out revision);
    }
}

public sealed record WordReviewGraphOptions
{
    public static WordReviewGraphOptions Default { get; } = new();

    public int MaxComments { get; init; } = 250_000;
    public int MaxAnchors { get; init; } = 500_000;
    public int MaxPeople { get; init; } = 250_000;
    public int MaxRevisions { get; init; } = 1_000_000;
    public int MaxMoveRanges { get; init; } = 250_000;
    public int MaxPermissions { get; init; } = 250_000;
    public int MaxIssues { get; init; } = 10_000;
    public int MaxPartBytes { get; init; } = 128 * 1024 * 1024;
    public int MaxTextCharactersPerItem { get; init; } = 1_000_000;
    public long MaxTotalTextCharacters { get; init; } = 64L * 1024 * 1024;
    public int MaxThreadDepth { get; init; } = 1_024;

    internal void Validate()
    {
        if (MaxComments <= 0) throw new ArgumentOutOfRangeException(nameof(MaxComments));
        if (MaxAnchors <= 0) throw new ArgumentOutOfRangeException(nameof(MaxAnchors));
        if (MaxPeople <= 0) throw new ArgumentOutOfRangeException(nameof(MaxPeople));
        if (MaxRevisions <= 0) throw new ArgumentOutOfRangeException(nameof(MaxRevisions));
        if (MaxMoveRanges <= 0) throw new ArgumentOutOfRangeException(nameof(MaxMoveRanges));
        if (MaxPermissions <= 0) throw new ArgumentOutOfRangeException(nameof(MaxPermissions));
        if (MaxIssues <= 0) throw new ArgumentOutOfRangeException(nameof(MaxIssues));
        if (MaxPartBytes <= 0) throw new ArgumentOutOfRangeException(nameof(MaxPartBytes));
        if (MaxTextCharactersPerItem <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxTextCharactersPerItem));
        if (MaxTotalTextCharacters <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxTotalTextCharacters));
        if (MaxThreadDepth <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxThreadDepth));
    }
}

public sealed class WordReviewProjectionException : Exception
{
    public WordReviewProjectionException(string message)
        : base(message) { }

    public WordReviewProjectionException(string message, Exception innerException)
        : base(message, innerException) { }
}

public sealed class WordReviewLimitException : Exception
{
    public WordReviewLimitException(string message)
        : base(message) { }
}
