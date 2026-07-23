using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordReferenceIssueSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record WordReferenceIssue(
    string Code,
    WordReferenceIssueSeverity Severity,
    string Message,
    string? PartUri = null,
    string? StoryId = null,
    int? SourceElementOrdinal = null,
    string? SubjectId = null
);

public enum WordStoryKind
{
    Main,
    Header,
    Footer,
    Footnote,
    Endnote,
    Comment,
    GlossaryEntry,
    TextBox,
    Other,
}

public sealed record WordStoryDescriptor(
    string Id,
    WordStoryKind Kind,
    string PartUri,
    int RootElementOrdinal,
    string? OoxmlKey,
    int Sequence
);

public enum WordBookmarkStatus
{
    Complete,
    MissingEnd,
    AmbiguousPair,
}

public sealed record WordBookmarkDefinition(
    string Id,
    string StoryId,
    string PartUri,
    string? OoxmlId,
    string? Name,
    WordBookmarkStatus Status,
    bool IsEffectiveByName,
    int StartElementOrdinal,
    int? EndElementOrdinal,
    SemanticNodeId? StartNodeId,
    SemanticNodeId? EndNodeId,
    int? ColumnFirst,
    int? ColumnLast
)
{
    public bool IsComplete => Status == WordBookmarkStatus.Complete;
}

public enum WordFieldKind
{
    Complex,
    Simple,
}

public enum WordFieldStatus
{
    Complete,
    MissingEnd,
    MissingInstruction,
    MalformedInstruction,
}

public enum WordFieldTokenKind
{
    Word,
    QuotedText,
    Switch,
}

public sealed record WordFieldToken(
    WordFieldTokenKind Kind,
    string Raw,
    string Value,
    int CharacterOffset,
    int CharacterLength
);

public enum WordFieldClassification
{
    Unknown,
    DocumentReference,
    Sequence,
    TableOfContents,
    Index,
    Citation,
    MailMerge,
    DocumentMetadata,
    Formula,
    Hyperlink,
    ExternalContent,
    Automation,
    Form,
    DateTime,
    Layout,
    EmbeddedObject,
}

public sealed class WordFieldDefinition
{
    internal WordFieldDefinition(
        string id,
        WordFieldKind kind,
        WordFieldStatus status,
        string storyId,
        string partUri,
        string? parentFieldId,
        IReadOnlyList<string> childFieldIds,
        int startElementOrdinal,
        int? separatorElementOrdinal,
        int? endElementOrdinal,
        SemanticNodeId? startNodeId,
        SemanticNodeId? separatorNodeId,
        SemanticNodeId? endNodeId,
        string instruction,
        int instructionFragmentCount,
        int? firstInstructionElementOrdinal,
        int? lastInstructionElementOrdinal,
        IReadOnlyList<WordFieldToken> tokens,
        string? fieldType,
        bool isImplicitReference,
        WordFieldClassification classification,
        bool isDirty,
        bool isLocked,
        bool isInDeletedContent,
        bool hasSeparator,
        bool hasDynamicInstruction,
        bool instructionParseComplete,
        string resultText,
        int resultCharacterCount,
        bool resultTruncated,
        bool requiresExternalAccess,
        bool mayInvokeApplication
    )
    {
        Id = id;
        Kind = kind;
        Status = status;
        StoryId = storyId;
        PartUri = partUri;
        ParentFieldId = parentFieldId;
        ChildFieldIds = new ReadOnlyCollection<string>(childFieldIds.ToArray());
        StartElementOrdinal = startElementOrdinal;
        SeparatorElementOrdinal = separatorElementOrdinal;
        EndElementOrdinal = endElementOrdinal;
        StartNodeId = startNodeId;
        SeparatorNodeId = separatorNodeId;
        EndNodeId = endNodeId;
        Instruction = instruction;
        InstructionFragmentCount = instructionFragmentCount;
        FirstInstructionElementOrdinal = firstInstructionElementOrdinal;
        LastInstructionElementOrdinal = lastInstructionElementOrdinal;
        Tokens = new ReadOnlyCollection<WordFieldToken>(tokens.ToArray());
        FieldType = fieldType;
        IsImplicitReference = isImplicitReference;
        Classification = classification;
        IsDirty = isDirty;
        IsLocked = isLocked;
        IsInDeletedContent = isInDeletedContent;
        HasSeparator = hasSeparator;
        HasDynamicInstruction = hasDynamicInstruction;
        InstructionParseComplete = instructionParseComplete;
        ResultText = resultText;
        ResultCharacterCount = resultCharacterCount;
        ResultTruncated = resultTruncated;
        RequiresExternalAccess = requiresExternalAccess;
        MayInvokeApplication = mayInvokeApplication;
    }

    public string Id { get; }

    public WordFieldKind Kind { get; }

    public WordFieldStatus Status { get; }

    public string StoryId { get; }

    public string PartUri { get; }

    public string? ParentFieldId { get; }

    public IReadOnlyList<string> ChildFieldIds { get; }

    public int StartElementOrdinal { get; }

    public int? SeparatorElementOrdinal { get; }

    public int? EndElementOrdinal { get; }

    public SemanticNodeId? StartNodeId { get; }

    public SemanticNodeId? SeparatorNodeId { get; }

    public SemanticNodeId? EndNodeId { get; }

    public string Instruction { get; }

    public int InstructionCharacterCount => Instruction.Length;

    public int InstructionFragmentCount { get; }

    public int? FirstInstructionElementOrdinal { get; }

    public int? LastInstructionElementOrdinal { get; }

    public IReadOnlyList<WordFieldToken> Tokens { get; }

    public string? FieldType { get; }

    public bool IsImplicitReference { get; }

    public WordFieldClassification Classification { get; }

    public bool IsDirty { get; }

    public bool IsLocked { get; }

    public bool IsInDeletedContent { get; }

    public bool HasSeparator { get; }

    public bool HasDynamicInstruction { get; }

    public bool InstructionParseComplete { get; }

    public string ResultText { get; }

    public int ResultCharacterCount { get; }

    public bool ResultTruncated { get; }

    public bool RequiresExternalAccess { get; }

    public bool MayInvokeApplication { get; }
}

public enum WordReferenceTargetKind
{
    Bookmark,
    Sequence,
    DocumentVariable,
    MergeField,
    Citation,
    ExternalResource,
    IndexEntry,
    Style,
}

public enum WordReferenceEdgeKind
{
    Reads,
    Writes,
    Includes,
    Navigates,
    Generates,
}

public sealed record WordReferenceEdge(
    string Id,
    string SourceFieldId,
    WordReferenceEdgeKind Kind,
    WordReferenceTargetKind TargetKind,
    string TargetKey,
    bool IsResolved,
    bool IsExternal,
    string? ResolvedBookmarkId
);

public sealed class WordReferenceGraph
{
    private readonly IReadOnlyDictionary<string, WordBookmarkDefinition>
        _effectiveBookmarks;

    internal WordReferenceGraph(
        string packageFingerprint,
        string mainPartUri,
        IReadOnlyList<WordStoryDescriptor> stories,
        IReadOnlyList<WordBookmarkDefinition> bookmarks,
        IReadOnlyList<WordFieldDefinition> fields,
        IReadOnlyList<WordReferenceEdge> edges,
        IReadOnlyList<WordReferenceIssue> issues,
        bool issuesTruncated
    )
    {
        PackageFingerprint = packageFingerprint;
        MainPartUri = mainPartUri;
        Stories = new ReadOnlyCollection<WordStoryDescriptor>(stories.ToArray());
        Bookmarks = new ReadOnlyCollection<WordBookmarkDefinition>(
            bookmarks.ToArray()
        );
        Fields = new ReadOnlyCollection<WordFieldDefinition>(fields.ToArray());
        Edges = new ReadOnlyCollection<WordReferenceEdge>(edges.ToArray());
        Issues = new ReadOnlyCollection<WordReferenceIssue>(issues.ToArray());
        IssuesTruncated = issuesTruncated;
        _effectiveBookmarks = new ReadOnlyDictionary<string, WordBookmarkDefinition>(
            bookmarks.Where(bookmark =>
                    bookmark.IsEffectiveByName
                    && !string.IsNullOrWhiteSpace(bookmark.Name)
                )
                .ToDictionary(
                    bookmark => bookmark.Name!,
                    StringComparer.OrdinalIgnoreCase
                )
        );
    }

    public string PackageFingerprint { get; }

    public string MainPartUri { get; }

    public IReadOnlyList<WordStoryDescriptor> Stories { get; }

    public IReadOnlyList<WordBookmarkDefinition> Bookmarks { get; }

    public IReadOnlyList<WordFieldDefinition> Fields { get; }

    public IReadOnlyList<WordReferenceEdge> Edges { get; }

    public IReadOnlyList<WordReferenceIssue> Issues { get; }

    public bool IssuesTruncated { get; }

    public bool TryGetEffectiveBookmark(
        string name,
        out WordBookmarkDefinition? bookmark
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _effectiveBookmarks.TryGetValue(name, out bookmark);
    }
}

public sealed record WordReferenceGraphOptions
{
    public static WordReferenceGraphOptions Default { get; } = new();

    public int MaxStories { get; init; } = 65_536;

    public int MaxBookmarks { get; init; } = 250_000;

    public int MaxFields { get; init; } = 250_000;

    public int MaxFieldNestingDepth { get; init; } = 128;

    public int MaxInstructionCharactersPerField { get; init; } = 65_536;

    public int MaxInstructionFragmentsPerField { get; init; } = 4_096;

    public long MaxTotalInstructionCharacters { get; init; } = 16L * 1024 * 1024;

    public int MaxResultCharactersPerField { get; init; } = 8_192;

    public long MaxTotalResultCharacters { get; init; } = 64L * 1024 * 1024;

    public int MaxTokensPerField { get; init; } = 512;

    public long MaxTotalTokens { get; init; } = 2_000_000;

    public int MaxIssues { get; init; } = 10_000;

    public int MaxStoryPartBytes { get; init; } = 128 * 1024 * 1024;

    internal void Validate()
    {
        if (MaxStories <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxStories));
        }
        if (MaxBookmarks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxBookmarks));
        }
        if (MaxFields <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxFields));
        }
        if (MaxFieldNestingDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxFieldNestingDepth));
        }
        if (MaxInstructionCharactersPerField <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxInstructionCharactersPerField)
            );
        }
        if (MaxInstructionFragmentsPerField <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxInstructionFragmentsPerField)
            );
        }
        if (MaxTotalInstructionCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxTotalInstructionCharacters)
            );
        }
        if (MaxResultCharactersPerField <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxResultCharactersPerField)
            );
        }
        if (MaxTotalResultCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxTotalResultCharacters)
            );
        }
        if (MaxTokensPerField <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTokensPerField));
        }
        if (MaxTotalTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTotalTokens));
        }
        if (MaxIssues <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxIssues));
        }
        if (MaxStoryPartBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxStoryPartBytes));
        }
    }
}

public sealed class WordReferenceGraphBuilder
{
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";

    private static readonly HashSet<string> KnownFieldTypes = new(
        new[]
        {
            "ADDIN", "ADDRESSBLOCK", "ADVANCE", "ASK", "AUTHOR", "AUTONUM",
            "AUTONUMLGL", "AUTONUMOUT", "AUTOTEXT", "AUTOTEXTLIST", "BARCODE",
            "BIBLIOGRAPHY", "BIDIOUTLINE", "CITATION", "COMMENTS", "COMPARE",
            "CONTROL", "CREATEDATE", "DATA", "DATABASE", "DATE", "DDE",
            "DDEAUTO", "DISPLAYNFC", "DOCVARIABLE", "DOCPROPERTY", "EDITTIME",
            "EMBED", "EQ", "FILENAME", "FILESIZE", "FILLIN", "FORMCHECKBOX",
            "FORMDROPDOWN", "FORMTEXT", "FTNREF", "GLOSSARY", "GOTOBUTTON",
            "GREETINGLINE", "HTMLCONTROL", "HYPERLINK", "IF", "IMPORT",
            "INCLUDE", "INCLUDEPICTURE", "INCLUDETEXT", "INDEX", "INFO",
            "KEYWORDS", "LASTSAVEDBY", "LINK", "LISTNUM", "MACROBUTTON",
            "MERGEBARCODE", "MERGEFIELD", "MERGEREC", "MERGESEQ", "NEXT",
            "NEXTIF", "NOTEREF", "NUMCHARS", "NUMPAGES", "NUMWORDS", "PAGE",
            "PAGEREF", "PRINT", "PRINTDATE", "PRIVATE", "QUOTE", "RD", "REF",
            "REVNUM", "SAVEDATE", "SECTION", "SECTIONPAGES", "SEQ", "SET",
            "SHAPE", "SKIPIF", "STYLEREF", "SUBJECT", "SUBSCRIBER", "SYMBOL",
            "TA", "TC", "TEMPLATE", "TIME", "TITLE", "TOA", "TOC",
            "USERADDRESS", "USERINITIALS", "USERNAME", "XE",
        },
        StringComparer.OrdinalIgnoreCase
    );

    private readonly WordReferenceGraphOptions _options;
    private readonly WordOperationResourceLease? _resourceLease;

    public WordReferenceGraphBuilder(WordReferenceGraphOptions? options = null)
    {
        _options = options ?? WordReferenceGraphOptions.Default;
        _options.Validate();
    }

    public WordReferenceGraphBuilder(
        WordReferenceGraphOptions? options,
        WordOperationResourceLease resourceLease
    )
    {
        ArgumentNullException.ThrowIfNull(resourceLease);
        _options = options ?? WordReferenceGraphOptions.Default;
        _resourceLease = resourceLease;
        _options.Validate();
    }

    public WordReferenceGraph Build(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        cancellationToken.ThrowIfCancellationRequested();
        WordOperationResourceAccounting.ChargeProjectionBase(
            _resourceLease,
            WordOperationResourceStage.References
        );
        if (
            !string.Equals(
                package.Fingerprint,
                semanticDocument.PackageFingerprint,
                StringComparison.Ordinal
            )
        )
        {
            throw new WordReferenceProjectionException(
                "Reference graph requires a semantic projection of the same package snapshot."
            );
        }

        var state = new BuildState(_options, semanticDocument, _resourceLease);
        foreach (var partUri in semanticDocument.ProjectedPartUris)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!package.Parts.TryGetValue(partUri, out var part))
            {
                throw new WordReferenceProjectionException(
                    $"Projected story part '{partUri}' is missing from the package."
                );
            }
            if (part.Entry.Content.Length > _options.MaxStoryPartBytes)
            {
                throw new WordReferenceLimitException(
                    $"Story part '{partUri}' exceeds {_options.MaxStoryPartBytes} bytes."
                );
            }

            var source = ParseStoryPart(part, cancellationToken);
            foreach (var input in DiscoverStories(partUri, source, state))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ParseStory(input, source, state, cancellationToken);
            }
        }

        MarkEffectiveBookmarks(state);
        AnalyzeFieldsAndBuildEdges(state);
        return new WordReferenceGraph(
            package.Fingerprint,
            semanticDocument.MainPartUri,
            state.Stories,
            state.Bookmarks.Select(bookmark => bookmark.Freeze()).ToArray(),
            state.Fields.Select(field => field.Freeze()).ToArray(),
            state.Edges,
            state.Issues,
            state.IssuesTruncated
        );
    }

    private LosslessXmlDocument ParseStoryPart(
        OpcPart part,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var options = new LosslessXmlOptions
            {
                MaxSourceBytes = _options.MaxStoryPartBytes,
                MaxXmlCharacters = _options.MaxStoryPartBytes,
                MaxXmlElements = 1_000_000,
                MaxXmlDepth = 256,
                MaxTextCharacters = _options.MaxStoryPartBytes,
            };
            return _resourceLease is null
                ? LosslessXmlDocument.Parse(
                    part.Entry.Content,
                    options,
                    cancellationToken
                )
                : LosslessXmlDocument.Parse(
                    part.Entry.Content,
                    options,
                    _resourceLease,
                    WordOperationResourceStage.References,
                    cancellationToken
                );
        }
        catch (LosslessXmlLimitException exception)
        {
            throw new WordReferenceLimitException(
                $"Story part '{part.Uri}' exceeds an XML safety limit: {exception.Message}"
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordReferenceProjectionException(
                $"Story part '{part.Uri}' is not safe, well-formed XML.",
                exception
            );
        }
    }

    private IReadOnlyList<StoryInput> DiscoverStories(
        string partUri,
        LosslessXmlDocument source,
        BuildState state
    )
    {
        var root = source.ParsedDocument.Root
            ?? throw new WordReferenceProjectionException(
                $"Story part '{partUri}' has no root element."
            );
        if (!IsWordElement(root))
        {
            throw new WordReferenceProjectionException(
                $"Projected story part '{partUri}' does not have a WordprocessingML root."
            );
        }

        var baseRoots = root.Name.LocalName switch
        {
            "document" => new[] { new StoryRoot(root, WordStoryKind.Main, null) },
            "hdr" => new[] { new StoryRoot(root, WordStoryKind.Header, null) },
            "ftr" => new[] { new StoryRoot(root, WordStoryKind.Footer, null) },
            "footnotes" => StoryChildren(root, "footnote", WordStoryKind.Footnote),
            "endnotes" => StoryChildren(root, "endnote", WordStoryKind.Endnote),
            "comments" => StoryChildren(root, "comment", WordStoryKind.Comment),
            "glossaryDocument" => StoryChildren(
                root,
                "docPart",
                WordStoryKind.GlossaryEntry
            ),
            _ => new[] { new StoryRoot(root, WordStoryKind.Other, null) },
        };
        var roots = new List<StoryRoot>(baseRoots);
        foreach (var baseRoot in baseRoots)
        {
            roots.AddRange(
                baseRoot.Element.Descendants()
                    .Where(element => IsWordElement(element, "txbxContent"))
                    .Select(element => new StoryRoot(
                        element,
                        WordStoryKind.TextBox,
                        null
                    ))
            );
        }

        var result = new List<StoryInput>(roots.Count);
        foreach (
            var storyRoot in roots.OrderBy(item => source.GetElementOrdinal(item.Element))
                .ThenBy(item => item.Kind)
        )
        {
            if (++state.StoryCount > _options.MaxStories)
            {
                throw new WordReferenceLimitException(
                    $"Document contains more than {_options.MaxStories} text stories."
                );
            }
            var ordinal = source.GetElementOrdinal(storyRoot.Element);
            var key = storyRoot.OoxmlKey ?? StoryKey(storyRoot.Element);
            var semanticAnchor = state.NodeIdFor(partUri, ordinal)?.Value;
            var stableRootAnchor = storyRoot.Kind is WordStoryKind.Footnote
                or WordStoryKind.Endnote
                or WordStoryKind.Comment
                or WordStoryKind.GlossaryEntry
                    ? semanticAnchor
                    : null;
            var id = StableId(
                "wds_",
                partUri,
                storyRoot.Kind.ToString(),
                stableRootAnchor ?? ordinal.ToString(CultureInfo.InvariantCulture),
                key ?? string.Empty
            );
            var descriptor = new WordStoryDescriptor(
                id,
                storyRoot.Kind,
                partUri,
                ordinal,
                key,
                state.StoryCount
            );
            state.Stories.Add(descriptor);
            result.Add(new StoryInput(descriptor, storyRoot.Element));
        }
        return result;
    }

    private static StoryRoot[] StoryChildren(
        XElement root,
        string childName,
        WordStoryKind kind
    ) => root.Descendants()
        .Where(element =>
            IsWordElement(element, childName)
            && !element.Ancestors().Any(ancestor =>
                ancestor != root && IsStoryBoundary(ancestor)
            )
        )
        .Select(element => new StoryRoot(element, kind, StoryKey(element)))
        .ToArray();

    private void ParseStory(
        StoryInput input,
        LosslessXmlDocument source,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        var parser = new StoryParser(this, input, source, state, cancellationToken);
        parser.Parse();
    }

    private void MarkEffectiveBookmarks(BuildState state)
    {
        foreach (
            var group in state.Bookmarks.Where(bookmark =>
                    !string.IsNullOrWhiteSpace(bookmark.Name)
                )
                .GroupBy(bookmark => bookmark.Name!, StringComparer.OrdinalIgnoreCase)
        )
        {
            var ordered = group.OrderBy(bookmark => bookmark.StorySequence)
                .ThenBy(bookmark => bookmark.StartElementOrdinal)
                .ToArray();
            ordered[^1].IsEffectiveByName = true;
            if (ordered.Length <= 1)
            {
                continue;
            }
            foreach (var bookmark in ordered)
            {
                state.AddIssue(
                    new WordReferenceIssue(
                        "BOOKMARK_DUPLICATE_NAME",
                        WordReferenceIssueSeverity.Warning,
                        "Bookmark name is duplicated case-insensitively; Word keeps the last definition.",
                        bookmark.PartUri,
                        bookmark.StoryId,
                        bookmark.StartElementOrdinal,
                        bookmark.Id
                    )
                );
            }
        }
    }

    private void AnalyzeFieldsAndBuildEdges(BuildState state)
    {
        var bookmarksByName = state.Bookmarks
            .Where(bookmark => !string.IsNullOrWhiteSpace(bookmark.Name))
            .GroupBy(bookmark => bookmark.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(bookmark => bookmark.StorySequence)
                    .ThenBy(bookmark => bookmark.StartElementOrdinal)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase
            );
        foreach (var field in state.Fields)
        {
            AnalyzeInstruction(field, state);
            BuildFieldEdges(field, bookmarksByName, state);
        }
    }

    private void AnalyzeInstruction(MutableField field, BuildState state)
    {
        field.Tokens.AddRange(Tokenize(field, state));
        field.InstructionParseComplete = !field.HasDynamicInstruction
            && !field.HasMalformedInstruction;
        var first = field.Tokens.FirstOrDefault();
        if (first is null || first.Kind == WordFieldTokenKind.Switch)
        {
            field.InstructionParseComplete = false;
            field.Status = field.Kind == WordFieldKind.Complex
                && field.EndElementOrdinal is null
                    ? WordFieldStatus.MissingEnd
                    : WordFieldStatus.MissingInstruction;
            state.AddIssue(
                new WordReferenceIssue(
                    "FIELD_INSTRUCTION_MISSING",
                    WordReferenceIssueSeverity.Warning,
                    "Field has no recognizable instruction type.",
                    field.PartUri,
                    field.StoryId,
                    field.StartElementOrdinal,
                    field.Id
                )
            );
            return;
        }

        var candidate = first.Value.ToUpperInvariant();
        if (KnownFieldTypes.Contains(candidate))
        {
            field.FieldType = candidate;
        }
        else
        {
            field.FieldType = "REF";
            field.IsImplicitReference = true;
        }
        field.Classification = ClassifyField(field.FieldType);
        field.RequiresExternalAccess = IsExternalField(field.FieldType);
        field.MayInvokeApplication = IsApplicationInvokingField(field.FieldType);
        AddSwitchDiagnostics(field, state);
        if (
            field.HasMalformedInstruction
            && field.Status != WordFieldStatus.MissingEnd
        )
        {
            field.Status = WordFieldStatus.MalformedInstruction;
        }
    }

    private static void AddSwitchDiagnostics(MutableField field, BuildState state)
    {
        var switches = field.Tokens.Where(token =>
                token.Kind == WordFieldTokenKind.Switch
            )
            .ToArray();
        var general = switches.Count(token => token.Value is "\\*" or "\\#" or "\\@");
        var specific = switches.Length - general;
        if (general > 10)
        {
            state.AddIssue(
                new WordReferenceIssue(
                    "FIELD_GENERAL_SWITCH_LIMIT_EXCEEDED",
                    WordReferenceIssueSeverity.Warning,
                    "Field contains more than Word's ten general-formatting switches.",
                    field.PartUri,
                    field.StoryId,
                    field.StartElementOrdinal,
                    field.Id
                )
            );
        }
        if (specific > 10)
        {
            state.AddIssue(
                new WordReferenceIssue(
                    "FIELD_SPECIFIC_SWITCH_LIMIT_EXCEEDED",
                    WordReferenceIssueSeverity.Warning,
                    "Field contains more than Word's ten field-specific switches.",
                    field.PartUri,
                    field.StoryId,
                    field.StartElementOrdinal,
                    field.Id
                )
            );
        }
        if (switches.Count(token => token.Value is "\\#" or "\\@") > 1)
        {
            state.AddIssue(
                new WordReferenceIssue(
                    "FIELD_PICTURE_SWITCH_CONFLICT",
                    WordReferenceIssueSeverity.Warning,
                    "Field contains more than one numeric or date/time picture switch.",
                    field.PartUri,
                    field.StoryId,
                    field.StartElementOrdinal,
                    field.Id
                )
            );
        }
    }

    private IReadOnlyList<WordFieldToken> Tokenize(
        MutableField field,
        BuildState state
    )
    {
        var value = field.Instruction.ToString();
        var result = new List<WordFieldToken>();
        var index = 0;
        while (index < value.Length)
        {
            while (index < value.Length && char.IsWhiteSpace(value[index]))
            {
                index++;
            }
            if (index >= value.Length)
            {
                break;
            }
            if (result.Count >= _options.MaxTokensPerField)
            {
                throw new WordReferenceLimitException(
                    $"Field '{field.Id}' contains more than {_options.MaxTokensPerField} tokens."
                );
            }
            if (++state.TotalTokens > _options.MaxTotalTokens)
            {
                throw new WordReferenceLimitException(
                    "Document fields exceed the configured aggregate token limit."
                );
            }

            var start = index;
            if (value[index] == '"')
            {
                index++;
                var decoded = new StringBuilder();
                var closed = false;
                while (index < value.Length)
                {
                    if (
                        value[index] == '\\'
                        && index + 1 < value.Length
                        && value[index + 1] == '"'
                    )
                    {
                        decoded.Append('"');
                        index += 2;
                        continue;
                    }
                    if (value[index] == '"')
                    {
                        index++;
                        closed = true;
                        break;
                    }
                    decoded.Append(value[index++]);
                }
                if (!closed)
                {
                    field.HasMalformedInstruction = true;
                    state.AddIssue(
                        new WordReferenceIssue(
                            "FIELD_UNTERMINATED_QUOTE",
                            WordReferenceIssueSeverity.Warning,
                            "Field instruction contains an unterminated quoted token.",
                            field.PartUri,
                            field.StoryId,
                            field.StartElementOrdinal,
                            field.Id
                        )
                    );
                }
                result.Add(
                    new WordFieldToken(
                        WordFieldTokenKind.QuotedText,
                        value[start..index],
                        decoded.ToString(),
                        start,
                        index - start
                    )
                );
                continue;
            }

            while (index < value.Length && !char.IsWhiteSpace(value[index]))
            {
                index++;
            }
            var raw = value[start..index];
            result.Add(
                new WordFieldToken(
                    raw.StartsWith('\\')
                        ? WordFieldTokenKind.Switch
                        : WordFieldTokenKind.Word,
                    raw,
                    raw,
                    start,
                    index - start
                )
            );
        }
        return result;
    }

    private void BuildFieldEdges(
        MutableField field,
        IReadOnlyDictionary<string, MutableBookmark[]> bookmarksByName,
        BuildState state
    )
    {
        if (field.FieldType is null || !field.InstructionParseComplete)
        {
            return;
        }
        var argumentIndex = field.IsImplicitReference ? 0 : 1;
        var firstArgument = PositionalAt(field.Tokens, argumentIndex);
        switch (field.FieldType)
        {
            case "REF":
            case "PAGEREF":
            case "NOTEREF":
                AddBookmarkEdge(field, firstArgument, bookmarksByName, state);
                break;
            case "TOC":
                AddBookmarkEdge(
                    field,
                    SwitchOperand(field.Tokens, "\\b"),
                    bookmarksByName,
                    state,
                    required: false,
                    edgeKind: WordReferenceEdgeKind.Generates
                );
                break;
            case "HYPERLINK":
                {
                    var local = SwitchOperand(field.Tokens, "\\l");
                    AddBookmarkEdge(
                        field,
                        local,
                        bookmarksByName,
                        state,
                        required: false,
                        edgeKind: WordReferenceEdgeKind.Navigates
                    );
                    if (
                        !string.IsNullOrWhiteSpace(firstArgument)
                        && !firstArgument.StartsWith('#')
                    )
                    {
                        field.RequiresExternalAccess = true;
                        AddEdge(
                            field,
                            WordReferenceEdgeKind.Navigates,
                            WordReferenceTargetKind.ExternalResource,
                            firstArgument,
                            isResolved: false,
                            isExternal: true,
                            resolvedBookmarkId: null,
                            state
                        );
                    }
                    break;
                }
            case "SEQ":
            case "LISTNUM":
                AddRequiredTypedEdge(
                    field,
                    firstArgument,
                    WordReferenceEdgeKind.Reads,
                    WordReferenceTargetKind.Sequence,
                    state
                );
                break;
            case "DOCVARIABLE":
                AddRequiredTypedEdge(
                    field,
                    firstArgument,
                    WordReferenceEdgeKind.Reads,
                    WordReferenceTargetKind.DocumentVariable,
                    state
                );
                break;
            case "SET":
            case "ASK":
                AddRequiredTypedEdge(
                    field,
                    firstArgument,
                    WordReferenceEdgeKind.Writes,
                    WordReferenceTargetKind.DocumentVariable,
                    state
                );
                break;
            case "MERGEFIELD":
                AddRequiredTypedEdge(
                    field,
                    firstArgument,
                    WordReferenceEdgeKind.Reads,
                    WordReferenceTargetKind.MergeField,
                    state
                );
                break;
            case "CITATION":
                AddRequiredTypedEdge(
                    field,
                    firstArgument,
                    WordReferenceEdgeKind.Reads,
                    WordReferenceTargetKind.Citation,
                    state
                );
                break;
            case "XE":
            case "TC":
            case "TA":
                AddRequiredTypedEdge(
                    field,
                    firstArgument,
                    WordReferenceEdgeKind.Generates,
                    WordReferenceTargetKind.IndexEntry,
                    state
                );
                break;
            case "STYLEREF":
                AddRequiredTypedEdge(
                    field,
                    firstArgument,
                    WordReferenceEdgeKind.Reads,
                    WordReferenceTargetKind.Style,
                    state
                );
                break;
            case "DDE":
            case "DDEAUTO":
            case "LINK":
            case "INCLUDE":
            case "INCLUDEPICTURE":
            case "INCLUDETEXT":
            case "IMPORT":
            case "DATABASE":
            case "RD":
                AddRequiredTypedEdge(
                    field,
                    firstArgument,
                    WordReferenceEdgeKind.Includes,
                    WordReferenceTargetKind.ExternalResource,
                    state,
                    isExternal: true
                );
                break;
        }
    }

    private void AddBookmarkEdge(
        MutableField field,
        string? target,
        IReadOnlyDictionary<string, MutableBookmark[]> bookmarksByName,
        BuildState state,
        bool required = true,
        WordReferenceEdgeKind edgeKind = WordReferenceEdgeKind.Reads
    )
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            if (required)
            {
                AddMissingTargetIssue(field, state);
            }
            return;
        }
        bookmarksByName.TryGetValue(target, out var candidates);
        var effective = candidates?.LastOrDefault(bookmark =>
            bookmark.IsEffectiveByName
        );
        var resolved = effective?.Status == WordBookmarkStatus.Complete;
        AddEdge(
            field,
            edgeKind,
            WordReferenceTargetKind.Bookmark,
            target,
            resolved,
            isExternal: false,
            resolved ? effective!.Id : null,
            state
        );
        if (candidates is null)
        {
            state.AddIssue(
                new WordReferenceIssue(
                    "FIELD_BOOKMARK_TARGET_MISSING",
                    WordReferenceIssueSeverity.Warning,
                    "Field references a bookmark name that is not defined.",
                    field.PartUri,
                    field.StoryId,
                    field.StartElementOrdinal,
                    field.Id
                )
            );
        }
        else if (candidates.Length > 1)
        {
            state.AddIssue(
                new WordReferenceIssue(
                    "FIELD_BOOKMARK_TARGET_DUPLICATE",
                    WordReferenceIssueSeverity.Warning,
                    "Field target has duplicate case-insensitive bookmark definitions; Word resolves the last one.",
                    field.PartUri,
                    field.StoryId,
                    field.StartElementOrdinal,
                    field.Id
                )
            );
        }
        else if (!resolved)
        {
            state.AddIssue(
                new WordReferenceIssue(
                    "FIELD_BOOKMARK_TARGET_MALFORMED",
                    WordReferenceIssueSeverity.Warning,
                    "Field target resolves to a bookmark whose range is malformed.",
                    field.PartUri,
                    field.StoryId,
                    field.StartElementOrdinal,
                    field.Id
                )
            );
        }
    }

    private void AddRequiredTypedEdge(
        MutableField field,
        string? target,
        WordReferenceEdgeKind edgeKind,
        WordReferenceTargetKind targetKind,
        BuildState state,
        bool isExternal = false
    )
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            AddMissingTargetIssue(field, state);
            return;
        }
        AddEdge(
            field,
            edgeKind,
            targetKind,
            target,
            isResolved: false,
            isExternal,
            resolvedBookmarkId: null,
            state
        );
    }

    private static void AddMissingTargetIssue(MutableField field, BuildState state) =>
        state.AddIssue(
            new WordReferenceIssue(
                "FIELD_TARGET_MISSING",
                WordReferenceIssueSeverity.Warning,
                "Field instruction is missing its required target argument.",
                field.PartUri,
                field.StoryId,
                field.StartElementOrdinal,
                field.Id
            )
        );

    private static void AddEdge(
        MutableField field,
        WordReferenceEdgeKind edgeKind,
        WordReferenceTargetKind targetKind,
        string target,
        bool isResolved,
        bool isExternal,
        string? resolvedBookmarkId,
        BuildState state
    )
    {
        var id = StableId(
            "wde_",
            field.Id,
            edgeKind.ToString(),
            targetKind.ToString(),
            target
        );
        state.Edges.Add(
            new WordReferenceEdge(
                id,
                field.Id,
                edgeKind,
                targetKind,
                target,
                isResolved,
                isExternal,
                resolvedBookmarkId
            )
        );
    }

    private static string? PositionalAt(
        IReadOnlyList<WordFieldToken> tokens,
        int index
    ) => index >= 0
        && index < tokens.Count
        && tokens[index].Kind != WordFieldTokenKind.Switch
            ? tokens[index].Value
            : null;

    private static string? SwitchOperand(
        IReadOnlyList<WordFieldToken> tokens,
        string switchName
    )
    {
        for (var index = 0; index < tokens.Count - 1; index++)
        {
            if (
                tokens[index].Kind == WordFieldTokenKind.Switch
                && string.Equals(
                    tokens[index].Value,
                    switchName,
                    StringComparison.OrdinalIgnoreCase
                )
                && tokens[index + 1].Kind != WordFieldTokenKind.Switch
            )
            {
                return tokens[index + 1].Value;
            }
        }
        return null;
    }

    private static WordFieldClassification ClassifyField(string fieldType) =>
        fieldType switch
        {
            "REF" or "PAGEREF" or "NOTEREF" or "STYLEREF" =>
                WordFieldClassification.DocumentReference,
            "SEQ" or "LISTNUM" or "AUTONUM" or "AUTONUMLGL" or "AUTONUMOUT" =>
                WordFieldClassification.Sequence,
            "TOC" or "TC" => WordFieldClassification.TableOfContents,
            "INDEX" or "XE" or "TOA" or "TA" or "RD" =>
                WordFieldClassification.Index,
            "CITATION" or "BIBLIOGRAPHY" => WordFieldClassification.Citation,
            "ADDRESSBLOCK" or "GREETINGLINE" or "MERGEBARCODE" or "MERGEFIELD"
                or "MERGEREC" or "MERGESEQ" or "NEXT" or "NEXTIF" or "SKIPIF" =>
                WordFieldClassification.MailMerge,
            "AUTHOR" or "COMMENTS" or "DOCPROPERTY" or "DOCVARIABLE" or "FILENAME"
                or "FILESIZE" or "INFO" or "KEYWORDS" or "LASTSAVEDBY" or "NUMCHARS"
                or "NUMWORDS" or "REVNUM" or "SUBJECT" or "TEMPLATE" or "TITLE"
                or "USERADDRESS" or "USERINITIALS" or "USERNAME" =>
                WordFieldClassification.DocumentMetadata,
            "EQ" or "IF" or "QUOTE" or "SET" or "ASK" or "FILLIN" or "SYMBOL" =>
                WordFieldClassification.Formula,
            "HYPERLINK" => WordFieldClassification.Hyperlink,
            "DATABASE" or "DDE" or "DDEAUTO" or "IMPORT" or "INCLUDE"
                or "INCLUDEPICTURE" or "INCLUDETEXT" or "LINK" =>
                WordFieldClassification.ExternalContent,
            "GOTOBUTTON" or "MACROBUTTON" or "PRINT" =>
                WordFieldClassification.Automation,
            "FORMCHECKBOX" or "FORMDROPDOWN" or "FORMTEXT" =>
                WordFieldClassification.Form,
            "CREATEDATE" or "DATE" or "EDITTIME" or "PRINTDATE" or "SAVEDATE"
                or "TIME" => WordFieldClassification.DateTime,
            "ADVANCE" or "NUMPAGES" or "PAGE" or "SECTION" or "SECTIONPAGES" =>
                WordFieldClassification.Layout,
            "ADDIN" or "CONTROL" or "DATA" or "EMBED" or "GLOSSARY"
                or "HTMLCONTROL" or "PRIVATE" or "SHAPE" or "SUBSCRIBER" =>
                WordFieldClassification.EmbeddedObject,
            _ => WordFieldClassification.Unknown,
        };

    private static bool IsExternalField(string fieldType) => fieldType is
        "DATABASE" or "DDE" or "DDEAUTO" or "IMPORT" or "INCLUDE"
        or "INCLUDEPICTURE" or "INCLUDETEXT" or "LINK" or "RD";

    private static bool IsApplicationInvokingField(string fieldType) => fieldType is
        "DDE" or "DDEAUTO" or "GOTOBUTTON" or "LINK" or "MACROBUTTON" or "PRINT";

    private static string? StoryKey(XElement element) => element.Attributes()
        .FirstOrDefault(attribute =>
            !attribute.IsNamespaceDeclaration && attribute.Name.LocalName == "id"
        )
        ?.Value;

    private static bool IsStoryBoundary(XElement element) =>
        IsWordElement(element)
        && element.Name.LocalName is "footnote" or "endnote" or "comment"
            or "docPart" or "txbxContent";

    private static bool IsWordElement(XElement element, string? localName = null) =>
        element.Name.NamespaceName is WordTransitionalNamespace or WordStrictNamespace
        && (localName is null || element.Name.LocalName == localName);

    private static string StableId(string prefix, params string[] values)
    {
        var material = string.Join('\u001f', values);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var encoded = Convert.ToBase64String(digest.AsSpan(0, 15))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return prefix + encoded;
    }

    private sealed class StoryParser
    {
        private readonly WordReferenceGraphBuilder _owner;
        private readonly StoryInput _input;
        private readonly LosslessXmlDocument _source;
        private readonly BuildState _state;
        private readonly CancellationToken _cancellationToken;
        private readonly List<MutableField> _openFields = new();
        private readonly Stack<int> _complexFloors = new();
        private readonly Dictionary<string, Queue<MutableBookmark>> _openBookmarks =
            new(StringComparer.Ordinal);

        internal StoryParser(
            WordReferenceGraphBuilder owner,
            StoryInput input,
            LosslessXmlDocument source,
            BuildState state,
            CancellationToken cancellationToken
        )
        {
            _owner = owner;
            _input = input;
            _source = source;
            _state = state;
            _cancellationToken = cancellationToken;
            _complexFloors.Push(0);
        }

        internal void Parse()
        {
            Visit(_input.Root, isRoot: true, insideDeletion: false);
            CloseUnfinishedComplexFields(0, boundaryOrdinal: null);
            foreach (var queue in _openBookmarks.Values)
            {
                while (queue.TryDequeue(out var bookmark))
                {
                    bookmark.Status = WordBookmarkStatus.MissingEnd;
                    _state.AddIssue(
                        new WordReferenceIssue(
                            "BOOKMARK_END_MISSING",
                            WordReferenceIssueSeverity.Error,
                            "Bookmark start has no subsequent end with the same id in this story.",
                            bookmark.PartUri,
                            bookmark.StoryId,
                            bookmark.StartElementOrdinal,
                            bookmark.Id
                        )
                    );
                }
            }
        }

        private void Visit(
            XElement element,
            bool isRoot,
            bool insideDeletion
        )
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (!isRoot && IsStoryBoundary(element))
            {
                return;
            }
            var deleted = insideDeletion || IsWordElement(element, "del");
            if (IsWordElement(element, "bookmarkStart"))
            {
                OpenBookmark(element);
            }
            else if (IsWordElement(element, "bookmarkEnd"))
            {
                CloseBookmark(element);
            }
            else if (IsWordElement(element, "fldSimple"))
            {
                VisitSimpleField(element, deleted);
                return;
            }
            else if (IsWordElement(element, "fldChar"))
            {
                VisitFieldCharacter(element, deleted);
            }
            else if (IsWordElement(element, "instrText"))
            {
                VisitInstructionText(element, deleted);
            }
            else if (IsWordElement(element, "t") || IsWordElement(element, "delText"))
            {
                AppendVisibleResult(element.Value);
            }
            else if (IsWordElement(element, "tab"))
            {
                AppendVisibleResult("\t");
            }
            else if (IsWordElement(element, "br") || IsWordElement(element, "cr"))
            {
                AppendVisibleResult("\n");
            }

            foreach (var child in element.Elements())
            {
                Visit(child, isRoot: false, deleted);
            }
            if (IsWordElement(element, "p"))
            {
                AppendVisibleResult("\n");
            }
        }

        private void VisitSimpleField(XElement element, bool insideDeletion)
        {
            var ordinal = _source.GetElementOrdinal(element);
            var parent = _openFields.LastOrDefault();
            if (parent is { Kind: WordFieldKind.Complex, HasSeparator: false })
            {
                parent.HasDynamicInstruction = true;
            }
            var field = CreateField(
                WordFieldKind.Simple,
                ordinal,
                parent,
                insideDeletion,
                element
            );
            field.EndElementOrdinal = ordinal;
            field.EndNodeId = field.StartNodeId;
            var instruction = WordAttribute(element, "instr");
            if (instruction is not null)
            {
                AppendInstruction(field, instruction, ordinal);
            }
            _openFields.Add(field);
            EnsureDepth();
            _complexFloors.Push(_openFields.Count);
            foreach (var child in element.Elements())
            {
                Visit(child, isRoot: false, insideDeletion);
            }
            CloseUnfinishedComplexFields(
                _complexFloors.Peek(),
                boundaryOrdinal: ordinal
            );
            _complexFloors.Pop();
            if (_openFields.Count == 0 || !ReferenceEquals(_openFields[^1], field))
            {
                throw new WordReferenceProjectionException(
                    $"Simple field stack became inconsistent in story '{_input.Descriptor.Id}'."
                );
            }
            _openFields.RemoveAt(_openFields.Count - 1);
        }

        private void VisitFieldCharacter(XElement element, bool insideDeletion)
        {
            var ordinal = _source.GetElementOrdinal(element);
            var rawType = WordAttribute(element, "fldCharType");
            if (string.Equals(rawType, "begin", StringComparison.OrdinalIgnoreCase))
            {
                var parent = _openFields.LastOrDefault();
                if (parent is { Kind: WordFieldKind.Complex, HasSeparator: false })
                {
                    parent.HasDynamicInstruction = true;
                }
                var field = CreateField(
                    WordFieldKind.Complex,
                    ordinal,
                    parent,
                    insideDeletion,
                    element
                );
                _openFields.Add(field);
                EnsureDepth();
                return;
            }

            var active = ActiveComplexField();
            if (string.Equals(rawType, "separate", StringComparison.OrdinalIgnoreCase))
            {
                if (active is null)
                {
                    AddOrphanFieldCharacterIssue("FIELD_SEPARATOR_ORPHAN", ordinal);
                }
                else if (active.HasSeparator)
                {
                    _state.AddIssue(
                        new WordReferenceIssue(
                            "FIELD_SEPARATOR_DUPLICATE",
                            WordReferenceIssueSeverity.Warning,
                            "Complex field contains more than one separator; later separators are ignored.",
                            active.PartUri,
                            active.StoryId,
                            ordinal,
                            active.Id
                        )
                    );
                }
                else
                {
                    active.HasSeparator = true;
                    active.SeparatorElementOrdinal = ordinal;
                    active.SeparatorNodeId = _state.NodeIdFor(
                        active.PartUri,
                        ordinal
                    );
                }
                return;
            }
            if (string.Equals(rawType, "end", StringComparison.OrdinalIgnoreCase))
            {
                if (active is null)
                {
                    AddOrphanFieldCharacterIssue("FIELD_END_ORPHAN", ordinal);
                    return;
                }
                if (!ReferenceEquals(_openFields[^1], active))
                {
                    throw new WordReferenceProjectionException(
                        $"Complex field stack became inconsistent in story '{_input.Descriptor.Id}'."
                    );
                }
                active.EndElementOrdinal = ordinal;
                active.EndNodeId = _state.NodeIdFor(active.PartUri, ordinal);
                _openFields.RemoveAt(_openFields.Count - 1);
                return;
            }

            _state.AddIssue(
                new WordReferenceIssue(
                    "FIELD_CHARACTER_TYPE_INVALID",
                    WordReferenceIssueSeverity.Warning,
                    "Field character has a missing or unknown type and is ignored.",
                    _input.Descriptor.PartUri,
                    _input.Descriptor.Id,
                    ordinal
                )
            );
        }

        private void VisitInstructionText(XElement element, bool insideDeletion)
        {
            var ordinal = _source.GetElementOrdinal(element);
            if (insideDeletion)
            {
                _state.AddIssue(
                    new WordReferenceIssue(
                        "FIELD_INSTRUCTION_IN_DELETION",
                        WordReferenceIssueSeverity.Warning,
                        "Instruction text occurs inside deleted content and is non-conformant.",
                        _input.Descriptor.PartUri,
                        _input.Descriptor.Id,
                        ordinal
                    )
                );
            }
            var active = ActiveComplexField();
            if (active is not null && !active.HasSeparator)
            {
                AppendInstruction(active, element.Value, ordinal);
                return;
            }
            _state.AddIssue(
                new WordReferenceIssue(
                    "FIELD_INSTRUCTION_TEXT_OUTSIDE_CODE",
                    WordReferenceIssueSeverity.Info,
                    "Instruction-text element is outside a complex field code region and is treated as ordinary text.",
                    _input.Descriptor.PartUri,
                    _input.Descriptor.Id,
                    ordinal
                )
            );
            AppendVisibleResult(element.Value);
        }

        private MutableField CreateField(
            WordFieldKind kind,
            int ordinal,
            MutableField? parent,
            bool insideDeletion,
            XElement element
        )
        {
            if (++_state.FieldCount > _owner._options.MaxFields)
            {
                throw new WordReferenceLimitException(
                    $"Document contains more than {_owner._options.MaxFields} fields."
                );
            }
            var startNodeId = _state.NodeIdFor(
                _input.Descriptor.PartUri,
                ordinal
            );
            var id = StableId(
                "wdf_",
                _input.Descriptor.PartUri,
                _input.Descriptor.Id,
                kind.ToString(),
                startNodeId?.Value ?? ordinal.ToString(CultureInfo.InvariantCulture)
            );
            var field = new MutableField(
                id,
                kind,
                _input.Descriptor.Id,
                _input.Descriptor.PartUri,
                parent?.Id,
                ordinal,
                startNodeId,
                ParseOnOffAttribute(element, "dirty", ordinal, id),
                ParseOnOffAttribute(element, "fldLock", ordinal, id),
                insideDeletion,
                _owner._options.MaxResultCharactersPerField
            );
            parent?.ChildFieldIds.Add(id);
            _state.Fields.Add(field);
            return field;
        }

        private void OpenBookmark(XElement element)
        {
            if (++_state.BookmarkCount > _owner._options.MaxBookmarks)
            {
                throw new WordReferenceLimitException(
                    $"Document contains more than {_owner._options.MaxBookmarks} bookmark starts."
                );
            }
            var ordinal = _source.GetElementOrdinal(element);
            var ooxmlId = WordAttribute(element, "id");
            var name = WordAttribute(element, "name");
            if (ooxmlId is { Length: > 128 })
            {
                throw new WordReferenceLimitException(
                    "Bookmark pairing id exceeds the configured attribute safety bound."
                );
            }
            if (name is { Length: > 4_096 })
            {
                throw new WordReferenceLimitException(
                    "Bookmark name exceeds the configured attribute safety bound."
                );
            }
            var startNodeId = _state.NodeIdFor(
                _input.Descriptor.PartUri,
                ordinal
            );
            var id = StableId(
                "wdb_",
                _input.Descriptor.PartUri,
                _input.Descriptor.Id,
                startNodeId?.Value ?? ordinal.ToString(CultureInfo.InvariantCulture),
                ooxmlId ?? string.Empty
            );
            var rawColumnFirst = WordAttribute(element, "colFirst");
            var rawColumnLast = WordAttribute(element, "colLast");
            var columnFirst = ParseNonNegativeInt(rawColumnFirst);
            var columnLast = ParseNonNegativeInt(rawColumnLast);
            var bookmark = new MutableBookmark(
                id,
                _input.Descriptor.Id,
                _input.Descriptor.PartUri,
                _input.Descriptor.Sequence,
                ooxmlId,
                name,
                ordinal,
                startNodeId,
                columnFirst,
                columnLast
            );
            _state.Bookmarks.Add(bookmark);
            if (
                rawColumnFirst is not null && columnFirst is null
                || rawColumnLast is not null && columnLast is null
            )
            {
                _state.AddIssue(
                    new WordReferenceIssue(
                        "BOOKMARK_COLUMN_RANGE_INVALID",
                        WordReferenceIssueSeverity.Warning,
                        "Bookmark table-column range contains a non-negative-integer violation.",
                        bookmark.PartUri,
                        bookmark.StoryId,
                        ordinal,
                        bookmark.Id
                    )
                );
            }
            if ((columnFirst is null) != (columnLast is null))
            {
                _state.AddIssue(
                    new WordReferenceIssue(
                        "BOOKMARK_COLUMN_RANGE_INCOMPLETE",
                        WordReferenceIssueSeverity.Warning,
                        "Bookmark table-column range has only one boundary.",
                        bookmark.PartUri,
                        bookmark.StoryId,
                        ordinal,
                        bookmark.Id
                    )
                );
            }
            else if (columnFirst > columnLast)
            {
                _state.AddIssue(
                    new WordReferenceIssue(
                        "BOOKMARK_COLUMN_RANGE_REVERSED",
                        WordReferenceIssueSeverity.Warning,
                        "Bookmark table-column range starts after it ends.",
                        bookmark.PartUri,
                        bookmark.StoryId,
                        ordinal,
                        bookmark.Id
                    )
                );
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                _state.AddIssue(
                    new WordReferenceIssue(
                        "BOOKMARK_NAME_MISSING",
                        WordReferenceIssueSeverity.Error,
                        "Bookmark start has no usable name.",
                        bookmark.PartUri,
                        bookmark.StoryId,
                        ordinal,
                        bookmark.Id
                    )
                );
            }
            else if (name.Length > 40)
            {
                _state.AddIssue(
                    new WordReferenceIssue(
                        "BOOKMARK_NAME_EXCEEDS_WORD_LIMIT",
                        WordReferenceIssueSeverity.Warning,
                        "Bookmark name exceeds Word's 40-character interoperability limit.",
                        bookmark.PartUri,
                        bookmark.StoryId,
                        ordinal,
                        bookmark.Id
                    )
                );
            }
            if (string.IsNullOrWhiteSpace(ooxmlId))
            {
                bookmark.Status = WordBookmarkStatus.MissingEnd;
                _state.AddIssue(
                    new WordReferenceIssue(
                        "BOOKMARK_ID_MISSING",
                        WordReferenceIssueSeverity.Error,
                        "Bookmark start has no usable pairing id.",
                        bookmark.PartUri,
                        bookmark.StoryId,
                        ordinal,
                        bookmark.Id
                    )
                );
                return;
            }
            if (!_openBookmarks.TryGetValue(ooxmlId, out var queue))
            {
                queue = new Queue<MutableBookmark>();
                _openBookmarks.Add(ooxmlId, queue);
            }
            if (queue.Count != 0)
            {
                bookmark.Status = WordBookmarkStatus.AmbiguousPair;
                foreach (var existing in queue)
                {
                    existing.Status = WordBookmarkStatus.AmbiguousPair;
                }
                _state.AddIssue(
                    new WordReferenceIssue(
                        "BOOKMARK_START_ID_DUPLICATE",
                        WordReferenceIssueSeverity.Error,
                        "More than one open bookmark start uses the same id in this story.",
                        bookmark.PartUri,
                        bookmark.StoryId,
                        ordinal,
                        bookmark.Id
                    )
                );
            }
            queue.Enqueue(bookmark);
        }

        private void CloseBookmark(XElement element)
        {
            var ordinal = _source.GetElementOrdinal(element);
            var ooxmlId = WordAttribute(element, "id");
            if (
                string.IsNullOrWhiteSpace(ooxmlId)
                || !_openBookmarks.TryGetValue(ooxmlId, out var queue)
                || queue.Count == 0
            )
            {
                _state.AddIssue(
                    new WordReferenceIssue(
                        "BOOKMARK_END_ORPHAN",
                        WordReferenceIssueSeverity.Error,
                        "Bookmark end has no preceding start with the same id in this story.",
                        _input.Descriptor.PartUri,
                        _input.Descriptor.Id,
                        ordinal,
                        _state.NodeIdFor(_input.Descriptor.PartUri, ordinal)?.Value
                    )
                );
                return;
            }
            var bookmark = queue.Dequeue();
            bookmark.EndElementOrdinal = ordinal;
            bookmark.EndNodeId = _state.NodeIdFor(
                _input.Descriptor.PartUri,
                ordinal
            );
            if (bookmark.Status != WordBookmarkStatus.AmbiguousPair)
            {
                bookmark.Status = WordBookmarkStatus.Complete;
            }
            if (queue.Count == 0)
            {
                _openBookmarks.Remove(ooxmlId);
            }
        }

        private void AppendInstruction(
            MutableField field,
            string value,
            int sourceElementOrdinal
        )
        {
            if (
                field.InstructionFragmentCount
                >= _owner._options.MaxInstructionFragmentsPerField
            )
            {
                throw new WordReferenceLimitException(
                    $"Field '{field.Id}' contains more than "
                        + $"{_owner._options.MaxInstructionFragmentsPerField} instruction fragments."
                );
            }
            if (
                field.Instruction.Length + value.Length
                > _owner._options.MaxInstructionCharactersPerField
            )
            {
                throw new WordReferenceLimitException(
                    $"Field '{field.Id}' instruction exceeds "
                        + $"{_owner._options.MaxInstructionCharactersPerField} characters."
                );
            }
            checked
            {
                _state.TotalInstructionCharacters += value.Length;
            }
            if (
                _state.TotalInstructionCharacters
                > _owner._options.MaxTotalInstructionCharacters
            )
            {
                throw new WordReferenceLimitException(
                    "Document fields exceed the configured aggregate instruction-text limit."
                );
            }
            field.Instruction.Append(value);
            field.InstructionFragmentCount++;
            field.FirstInstructionElementOrdinal ??= sourceElementOrdinal;
            field.LastInstructionElementOrdinal = sourceElementOrdinal;
        }

        private void AppendVisibleResult(string value)
        {
            if (string.IsNullOrEmpty(value) || _openFields.Count == 0)
            {
                return;
            }
            if (_openFields.Any(field =>
                field.Kind == WordFieldKind.Complex && !field.HasSeparator
            ))
            {
                return;
            }
            foreach (var field in _openFields)
            {
                checked
                {
                    _state.TotalResultCharacters += value.Length;
                }
                if (
                    _state.TotalResultCharacters
                    > _owner._options.MaxTotalResultCharacters
                )
                {
                    throw new WordReferenceLimitException(
                        "Document fields exceed the configured aggregate result-text limit."
                    );
                }
                field.AppendResult(value);
            }
        }

        private MutableField? ActiveComplexField()
        {
            var floor = _complexFloors.Peek();
            for (var index = _openFields.Count - 1; index >= floor; index--)
            {
                if (_openFields[index].Kind == WordFieldKind.Complex)
                {
                    return _openFields[index];
                }
            }
            return null;
        }

        private void CloseUnfinishedComplexFields(
            int floor,
            int? boundaryOrdinal
        )
        {
            while (_openFields.Count > floor)
            {
                var field = _openFields[^1];
                if (field.Kind == WordFieldKind.Simple)
                {
                    break;
                }
                field.Status = WordFieldStatus.MissingEnd;
                _state.AddIssue(
                    new WordReferenceIssue(
                        "FIELD_END_MISSING",
                        WordReferenceIssueSeverity.Error,
                        boundaryOrdinal is null
                            ? "Complex field reaches the end of its story without an end character."
                            : "Complex field reaches a simple-field boundary without an end character.",
                        field.PartUri,
                        field.StoryId,
                        field.StartElementOrdinal,
                        field.Id
                    )
                );
                _openFields.RemoveAt(_openFields.Count - 1);
            }
        }

        private void EnsureDepth()
        {
            if (_openFields.Count > _owner._options.MaxFieldNestingDepth)
            {
                throw new WordReferenceLimitException(
                    $"Field nesting exceeds {_owner._options.MaxFieldNestingDepth} levels."
                );
            }
        }

        private void AddOrphanFieldCharacterIssue(string code, int ordinal) =>
            _state.AddIssue(
                new WordReferenceIssue(
                    code,
                    WordReferenceIssueSeverity.Warning,
                    "Field character has no matching open complex field in this story and is ignored.",
                    _input.Descriptor.PartUri,
                    _input.Descriptor.Id,
                    ordinal,
                    _state.NodeIdFor(_input.Descriptor.PartUri, ordinal)?.Value
                )
            );

        private bool ParseOnOffAttribute(
            XElement element,
            string attributeName,
            int sourceElementOrdinal,
            string fieldId
        )
        {
            var raw = WordAttribute(element, attributeName);
            var parsed = raw?.ToLowerInvariant() switch
            {
                null or "false" or "0" or "off" => false,
                "true" or "1" or "on" => true,
                _ => (bool?)null,
            };
            if (parsed is not null)
            {
                return parsed.Value;
            }
            _state.AddIssue(
                new WordReferenceIssue(
                    "FIELD_ON_OFF_VALUE_INVALID",
                    WordReferenceIssueSeverity.Warning,
                    "Field Boolean attribute has an invalid on/off value and is treated as false.",
                    _input.Descriptor.PartUri,
                    _input.Descriptor.Id,
                    sourceElementOrdinal,
                    fieldId
                )
            );
            return false;
        }
    }

    private sealed class BuildState
    {
        private readonly WordReferenceGraphOptions _options;
        private readonly IReadOnlyDictionary<string, SemanticNodeId> _nodeIds;

        internal BuildState(
            WordReferenceGraphOptions options,
            WordSemanticDocument semanticDocument,
            WordOperationResourceLease? resourceLease
        )
        {
            _options = options;
            WordOperationResourceAccounting.ChargeItems(
                resourceLease,
                WordOperationResourceStage.References,
                semanticDocument.NodeCount,
                128
            );
            _nodeIds = semanticDocument.Nodes.ToDictionary(
                node => NodeKey(node.SourcePartUri, node.SourceElementOrdinal),
                node => node.Id,
                StringComparer.Ordinal
            );
        }

        internal List<WordStoryDescriptor> Stories { get; } = new();

        internal List<MutableBookmark> Bookmarks { get; } = new();

        internal List<MutableField> Fields { get; } = new();

        internal List<WordReferenceEdge> Edges { get; } = new();

        internal List<WordReferenceIssue> Issues { get; } = new();

        internal int StoryCount { get; set; }

        internal int BookmarkCount { get; set; }

        internal int FieldCount { get; set; }

        internal long TotalInstructionCharacters { get; set; }

        internal long TotalResultCharacters { get; set; }

        internal long TotalTokens { get; set; }

        internal bool IssuesTruncated { get; private set; }

        internal SemanticNodeId? NodeIdFor(string partUri, int elementOrdinal) =>
            _nodeIds.TryGetValue(NodeKey(partUri, elementOrdinal), out var id)
                ? id
                : null;

        internal void AddIssue(WordReferenceIssue issue)
        {
            if (Issues.Count < _options.MaxIssues)
            {
                Issues.Add(issue);
            }
            else
            {
                IssuesTruncated = true;
            }
        }

        private static string NodeKey(string partUri, int ordinal) =>
            partUri + '\u001f' + ordinal.ToString(CultureInfo.InvariantCulture);
    }

    private sealed class MutableBookmark
    {
        internal MutableBookmark(
            string id,
            string storyId,
            string partUri,
            int storySequence,
            string? ooxmlId,
            string? name,
            int startElementOrdinal,
            SemanticNodeId? startNodeId,
            int? columnFirst,
            int? columnLast
        )
        {
            Id = id;
            StoryId = storyId;
            PartUri = partUri;
            StorySequence = storySequence;
            OoxmlId = ooxmlId;
            Name = name;
            StartElementOrdinal = startElementOrdinal;
            StartNodeId = startNodeId;
            ColumnFirst = columnFirst;
            ColumnLast = columnLast;
        }

        internal string Id { get; }

        internal string StoryId { get; }

        internal string PartUri { get; }

        internal int StorySequence { get; }

        internal string? OoxmlId { get; }

        internal string? Name { get; }

        internal WordBookmarkStatus Status { get; set; } = WordBookmarkStatus.MissingEnd;

        internal bool IsEffectiveByName { get; set; }

        internal int StartElementOrdinal { get; }

        internal int? EndElementOrdinal { get; set; }

        internal SemanticNodeId? StartNodeId { get; }

        internal SemanticNodeId? EndNodeId { get; set; }

        internal int? ColumnFirst { get; }

        internal int? ColumnLast { get; }

        internal WordBookmarkDefinition Freeze() => new(
            Id,
            StoryId,
            PartUri,
            OoxmlId,
            Name,
            Status,
            IsEffectiveByName,
            StartElementOrdinal,
            EndElementOrdinal,
            StartNodeId,
            EndNodeId,
            ColumnFirst,
            ColumnLast
        );
    }

    private sealed class MutableField
    {
        private readonly int _resultLimit;

        internal MutableField(
            string id,
            WordFieldKind kind,
            string storyId,
            string partUri,
            string? parentFieldId,
            int startElementOrdinal,
            SemanticNodeId? startNodeId,
            bool isDirty,
            bool isLocked,
            bool isInDeletedContent,
            int resultLimit
        )
        {
            Id = id;
            Kind = kind;
            StoryId = storyId;
            PartUri = partUri;
            ParentFieldId = parentFieldId;
            StartElementOrdinal = startElementOrdinal;
            StartNodeId = startNodeId;
            IsDirty = isDirty;
            IsLocked = isLocked;
            IsInDeletedContent = isInDeletedContent;
            _resultLimit = resultLimit;
        }

        internal string Id { get; }

        internal WordFieldKind Kind { get; }

        internal WordFieldStatus Status { get; set; } = WordFieldStatus.Complete;

        internal string StoryId { get; }

        internal string PartUri { get; }

        internal string? ParentFieldId { get; }

        internal List<string> ChildFieldIds { get; } = new();

        internal int StartElementOrdinal { get; }

        internal int? SeparatorElementOrdinal { get; set; }

        internal int? EndElementOrdinal { get; set; }

        internal SemanticNodeId? StartNodeId { get; }

        internal SemanticNodeId? SeparatorNodeId { get; set; }

        internal SemanticNodeId? EndNodeId { get; set; }

        internal StringBuilder Instruction { get; } = new();

        internal int InstructionFragmentCount { get; set; }

        internal int? FirstInstructionElementOrdinal { get; set; }

        internal int? LastInstructionElementOrdinal { get; set; }

        internal List<WordFieldToken> Tokens { get; } = new();

        internal string? FieldType { get; set; }

        internal bool IsImplicitReference { get; set; }

        internal WordFieldClassification Classification { get; set; }

        internal bool IsDirty { get; }

        internal bool IsLocked { get; }

        internal bool IsInDeletedContent { get; }

        internal bool HasSeparator { get; set; }

        internal bool HasDynamicInstruction { get; set; }

        internal bool HasMalformedInstruction { get; set; }

        internal bool InstructionParseComplete { get; set; }

        internal StringBuilder Result { get; } = new();

        internal int ResultCharacterCount { get; private set; }

        internal bool ResultTruncated { get; private set; }

        internal bool RequiresExternalAccess { get; set; }

        internal bool MayInvokeApplication { get; set; }

        internal void AppendResult(string value)
        {
            checked
            {
                ResultCharacterCount += value.Length;
            }
            var remaining = _resultLimit - Result.Length;
            if (remaining > 0)
            {
                Result.Append(value.AsSpan(0, Math.Min(remaining, value.Length)));
            }
            if (ResultCharacterCount > Result.Length)
            {
                ResultTruncated = true;
            }
        }

        internal WordFieldDefinition Freeze() => new(
            Id,
            Kind,
            Status,
            StoryId,
            PartUri,
            ParentFieldId,
            ChildFieldIds,
            StartElementOrdinal,
            SeparatorElementOrdinal,
            EndElementOrdinal,
            StartNodeId,
            SeparatorNodeId,
            EndNodeId,
            Instruction.ToString(),
            InstructionFragmentCount,
            FirstInstructionElementOrdinal,
            LastInstructionElementOrdinal,
            Tokens,
            FieldType,
            IsImplicitReference,
            Classification,
            IsDirty,
            IsLocked,
            IsInDeletedContent,
            HasSeparator,
            HasDynamicInstruction,
            InstructionParseComplete,
            Result.ToString(),
            ResultCharacterCount,
            ResultTruncated,
            RequiresExternalAccess,
            MayInvokeApplication
        );
    }

    private sealed record StoryRoot(
        XElement Element,
        WordStoryKind Kind,
        string? OoxmlKey
    );

    private sealed record StoryInput(
        WordStoryDescriptor Descriptor,
        XElement Root
    );

    private static string? WordAttribute(XElement element, string localName)
    {
        var matches = element.Attributes()
            .Where(attribute =>
                !attribute.IsNamespaceDeclaration
                && attribute.Name.LocalName == localName
                && attribute.Name.NamespaceName is WordTransitionalNamespace
                    or WordStrictNamespace
            )
            .Take(2)
            .ToArray();
        if (matches.Length > 1)
        {
            throw new WordReferenceProjectionException(
                $"Element '{element.Name.LocalName}' has ambiguous '{localName}' attributes."
            );
        }
        return matches.SingleOrDefault()?.Value;
    }

    private static int? ParseNonNegativeInt(string? value) =>
        int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsed
        ) && parsed >= 0
            ? parsed
            : null;
}

public class WordReferenceProjectionException : IOException
{
    public WordReferenceProjectionException(string message)
        : base(message)
    {
    }

    public WordReferenceProjectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class WordReferenceLimitException : WordReferenceProjectionException
{
    public WordReferenceLimitException(string message)
        : base(message)
    {
    }
}
