namespace WordToolkit.Native.Word;

internal sealed class LiveDocumentRecord
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public required int WindowHwnd { get; init; }
    public long Version { get; set; }
}

internal sealed record SelectionGrant(
    string Token,
    string DocumentId,
    long Version,
    int WindowHwnd,
    int StoryType,
    int Start,
    int End,
    string ContextHash
);

internal sealed record UndoGrant(
    string Token,
    string DocumentId,
    long Version,
    string TopEntry
);

internal sealed record RangeGrant(
    string Token,
    string DocumentId,
    long Version,
    int Start,
    int End,
    string ContextHash
);

internal sealed record SmartArtTextEditGrant(
    string Token,
    string DocumentId,
    long Version,
    string StoryType,
    int StoryLinkIndex,
    string CollectionKind,
    int SourceIndex,
    int NodeIndex,
    string RootStructureFingerprint,
    string RootContextFingerprint,
    string NodeTextHash,
    IReadOnlyList<string> BaselineNodeTextHashes
);
