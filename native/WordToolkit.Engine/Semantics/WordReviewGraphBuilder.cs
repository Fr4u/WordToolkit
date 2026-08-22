using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public sealed class WordReviewGraphBuilder
{
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string Word2010Namespace =
        "http://schemas.microsoft.com/office/word/2010/wordml";
    private const string Word2012Namespace =
        "http://schemas.microsoft.com/office/word/2012/wordml";
    private const string Word2016CommentIdNamespace =
        "http://schemas.microsoft.com/office/word/2016/wordml/cid";
    private const string Word2018CommentExtensibleNamespace =
        "http://schemas.microsoft.com/office/word/2018/wordml/cex";
    private const string MathTransitionalNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string MathStrictNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/math";

    private readonly WordReviewGraphOptions _options;

    public WordReviewGraphBuilder(WordReviewGraphOptions? options = null)
    {
        _options = options ?? WordReviewGraphOptions.Default;
        _options.Validate();
    }

    public WordReviewGraph Build(
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
            throw new WordReviewProjectionException(
                "Review graph requires a semantic projection of the same package snapshot."
            );
        }

        var state = new BuildState(_options, semanticDocument);
        foreach (var partUri in semanticDocument.ProjectedPartUris)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.SourceFor(package, partUri, this, cancellationToken);
        }

        var commentsPart = RelatedPart(
            package,
            semanticDocument.MainPartUri,
            "comments",
            state
        );
        var commentsExtendedPart = RelatedPart(
            package,
            semanticDocument.MainPartUri,
            "commentsExtended",
            state
        );
        var commentsIdsPart = RelatedPart(
            package,
            semanticDocument.MainPartUri,
            "commentsIds",
            state
        );
        var commentsExtensiblePart = RelatedPart(
            package,
            semanticDocument.MainPartUri,
            "commentsExtensible",
            state
        );
        var peoplePart = RelatedPart(
            package,
            semanticDocument.MainPartUri,
            "people",
            state
        );
        var settingsPart = RelatedPart(
            package,
            semanticDocument.MainPartUri,
            "settings",
            state
        );

        if (commentsPart is not null)
        {
            ParseComments(package, commentsPart, state, cancellationToken);
        }
        if (commentsExtendedPart is not null)
        {
            ParseCommentsExtended(
                package,
                commentsExtendedPart,
                state,
                cancellationToken
            );
        }
        if (commentsIdsPart is not null)
        {
            ParseCommentsIds(package, commentsIdsPart, state, cancellationToken);
        }
        if (commentsExtensiblePart is not null)
        {
            ParseCommentsExtensible(
                package,
                commentsExtensiblePart,
                state,
                cancellationToken
            );
        }
        if (peoplePart is not null)
        {
            ParsePeople(package, peoplePart, state, cancellationToken);
        }
        if (settingsPart is not null)
        {
            state.Settings = ParseSettings(
                package,
                settingsPart,
                state,
                cancellationToken
            );
        }

        foreach (var partUri in semanticDocument.ProjectedPartUris)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ParseStoryMarkup(partUri, state.Sources[partUri], state, cancellationToken);
        }

        FinalizeAnchors(state);
        FinalizeMoves(state);
        FinalizePermissions(state);
        FinalizeThreads(state);
        FinalizePeople(state);
        AuditRevisionIds(state);

        return new WordReviewGraph(
            package.Fingerprint,
            semanticDocument.MainPartUri,
            state.Comments.Select(comment => comment.Freeze()).ToArray(),
            state.Anchors,
            state.People.Select(person => person.Freeze()).ToArray(),
            state.Revisions.Select(revision => revision.Freeze()).ToArray(),
            state.MoveRanges,
            state.Moves,
            state.Permissions,
            state.Settings,
            state.Issues,
            state.IssuesTruncated
        );
    }

    private static OpcPart? RelatedPart(
        OpcPackageSnapshot package,
        string mainPartUri,
        string relationshipName,
        BuildState state
    )
    {
        var relationships = package.RelationshipsFrom(mainPartUri)
            .Where(relationship => RelationshipName(relationship.Type) == relationshipName)
            .OrderBy(relationship => relationship.Id, StringComparer.Ordinal)
            .ToArray();
        if (relationships.Length == 0)
        {
            return null;
        }
        if (relationships.Length > 1)
        {
            state.AddIssue(
                "REVIEW_PART_RELATIONSHIP_DUPLICATE",
                WordReviewIssueSeverity.Error,
                $"Main document has more than one {relationshipName} relationship.",
                mainPartUri
            );
        }
        var relationship = relationships[0];
        if (
            relationship.TargetMode != OpcRelationshipTargetMode.Internal
            || relationship.ResolvedTargetPartUri is null
            || !package.Parts.TryGetValue(relationship.ResolvedTargetPartUri, out var part)
        )
        {
            state.AddIssue(
                "REVIEW_PART_RELATIONSHIP_UNRESOLVED",
                WordReviewIssueSeverity.Error,
                $"The {relationshipName} relationship does not resolve to an internal part.",
                mainPartUri
            );
            return null;
        }
        return part;
    }

    private static string? RelationshipName(string relationshipType)
    {
        var slash = relationshipType.LastIndexOf('/');
        return slash < 0 || slash == relationshipType.Length - 1
            ? null
            : relationshipType[(slash + 1)..];
    }

    private void ParseComments(
        OpcPackageSnapshot package,
        OpcPart part,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        var source = state.SourceFor(package, part.Uri, this, cancellationToken);
        var root = RequireRoot(
            part.Uri,
            source,
            "comments",
            element => IsWordElement(element)
        );
        foreach (
            var element in root.Elements()
                .Where(element => IsWordElement(element, "comment"))
                .OrderBy(source.GetElementOrdinal)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (state.Comments.Count >= _options.MaxComments)
            {
                throw new WordReviewLimitException(
                    $"Document contains more than {_options.MaxComments} comments."
                );
            }
            var ordinal = source.GetElementOrdinal(element);
            var semantic = state.NodeFor(part.Uri, ordinal);
            var ooxmlId = WordAttribute(element, "id");
            var paragraphs = element.Descendants()
                .Where(IsWordParagraph)
                .Select(paragraph => paragraph.Attribute(
                        XName.Get("paraId", Word2010Namespace)
                    )?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray();
            var lastParagraphId = paragraphs.LastOrDefault();
            var id = StableId(
                "wdc_",
                part.Uri,
                semantic?.Id.Value
                    ?? ooxmlId
                    ?? ordinal.ToString(CultureInfo.InvariantCulture)
            );
            var capture = CaptureText(element.DescendantsAndSelf(), state);
            var mutable = new MutableComment(
                id,
                ooxmlId,
                part.Uri,
                ordinal,
                semantic?.Id,
                WordAttribute(element, "author"),
                WordAttribute(element, "initials"),
                WordAttribute(element, "date"),
                LocalAttribute(element, "dateUtc"),
                capture,
                paragraphs,
                lastParagraphId
            );
            state.Comments.Add(mutable);
            if (string.IsNullOrWhiteSpace(ooxmlId))
            {
                state.AddIssue(
                    "COMMENT_ID_MISSING",
                    WordReviewIssueSeverity.Error,
                    "Comment definition has no w:id.",
                    part.Uri,
                    null,
                    ordinal,
                    id
                );
            }
            else if (!state.EffectiveCommentsByOoxmlId.TryAdd(ooxmlId, mutable))
            {
                mutable.IsEffectiveByOoxmlId = false;
                state.AddIssue(
                    "COMMENT_ID_DUPLICATE",
                    WordReviewIssueSeverity.Error,
                    "More than one comment definition has the same w:id; only the first is effective.",
                    part.Uri,
                    null,
                    ordinal,
                    id
                );
            }
            if (!string.IsNullOrWhiteSpace(lastParagraphId))
            {
                if (!state.CommentsByLastParagraphId.TryAdd(lastParagraphId, mutable))
                {
                    state.AddIssue(
                        "COMMENT_LAST_PARAGRAPH_ID_DUPLICATE",
                        WordReviewIssueSeverity.Error,
                        "More than one comment ends with the same paragraph identifier.",
                        part.Uri,
                        null,
                        ordinal,
                        id
                    );
                }
            }
        }
    }

    private void ParseCommentsExtended(
        OpcPackageSnapshot package,
        OpcPart part,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        var source = state.SourceFor(package, part.Uri, this, cancellationToken);
        var root = RequireRoot(
            part.Uri,
            source,
            "commentsEx",
            element => element.Name.NamespaceName == Word2012Namespace
        );
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (
            var element in root.Elements()
                .Where(element =>
                    element.Name.NamespaceName == Word2012Namespace
                    && element.Name.LocalName == "commentEx"
                )
                .OrderBy(source.GetElementOrdinal)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = source.GetElementOrdinal(element);
            var paraId = LocalAttribute(element, "paraId");
            if (string.IsNullOrWhiteSpace(paraId))
            {
                state.AddIssue(
                    "COMMENT_EX_PARAGRAPH_ID_MISSING",
                    WordReviewIssueSeverity.Error,
                    "Extended comment record has no paraId.",
                    part.Uri,
                    null,
                    ordinal
                );
                continue;
            }
            if (!seen.Add(paraId))
            {
                state.AddIssue(
                    "COMMENT_EX_DUPLICATE",
                    WordReviewIssueSeverity.Error,
                    "More than one extended comment record targets the same paragraph.",
                    part.Uri,
                    null,
                    ordinal
                );
            }
            if (!state.CommentsByLastParagraphId.TryGetValue(paraId, out var comment))
            {
                state.AddIssue(
                    "COMMENT_EX_ORPHAN",
                    WordReviewIssueSeverity.Warning,
                    "Extended comment record does not resolve to a comment's last paragraph.",
                    part.Uri,
                    null,
                    ordinal
                );
                continue;
            }
            comment.IsDone = ParseOnOff(
                LocalAttribute(element, "done"),
                defaultValue: false,
                state,
                part.Uri,
                ordinal,
                comment.Id
            );
            comment.ParentParagraphId = LocalAttribute(element, "paraIdParent");
        }
    }

    private void ParseCommentsIds(
        OpcPackageSnapshot package,
        OpcPart part,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        var source = state.SourceFor(package, part.Uri, this, cancellationToken);
        var root = RequireRoot(
            part.Uri,
            source,
            "commentsIds",
            element => element.Name.NamespaceName == Word2016CommentIdNamespace
        );
        var durableIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (
            var element in root.Elements()
                .Where(element =>
                    element.Name.NamespaceName == Word2016CommentIdNamespace
                    && element.Name.LocalName == "commentId"
                )
                .OrderBy(source.GetElementOrdinal)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = source.GetElementOrdinal(element);
            var paraId = LocalAttribute(element, "paraId");
            var durableId = LocalAttribute(element, "durableId");
            if (
                string.IsNullOrWhiteSpace(paraId)
                || !state.CommentsByLastParagraphId.TryGetValue(paraId, out var comment)
            )
            {
                state.AddIssue(
                    "COMMENT_DURABLE_ID_ORPHAN",
                    WordReviewIssueSeverity.Warning,
                    "Durable comment identifier does not resolve to a comment's last paragraph.",
                    part.Uri,
                    null,
                    ordinal
                );
                continue;
            }
            if (!IsValidDurableId(durableId))
            {
                state.AddIssue(
                    "COMMENT_DURABLE_ID_INVALID",
                    WordReviewIssueSeverity.Error,
                    "Durable comment identifier must be a hexadecimal value from 1 through 7FFFFFFE.",
                    part.Uri,
                    null,
                    ordinal,
                    comment.Id
                );
                continue;
            }
            if (!durableIds.Add(durableId!))
            {
                state.AddIssue(
                    "COMMENT_DURABLE_ID_DUPLICATE",
                    WordReviewIssueSeverity.Error,
                    "Durable comment identifier is used more than once.",
                    part.Uri,
                    null,
                    ordinal,
                    comment.Id
                );
            }
            if (comment.DurableId is not null)
            {
                state.AddIssue(
                    "COMMENT_DURABLE_MAPPING_DUPLICATE",
                    WordReviewIssueSeverity.Error,
                    "Comment has more than one durable identifier mapping.",
                    part.Uri,
                    null,
                    ordinal,
                    comment.Id
                );
                continue;
            }
            comment.DurableId = durableId;
            state.CommentsByDurableId.TryAdd(durableId!, comment);
        }
    }

    private void ParseCommentsExtensible(
        OpcPackageSnapshot package,
        OpcPart part,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        var source = state.SourceFor(package, part.Uri, this, cancellationToken);
        var root = RequireRoot(
            part.Uri,
            source,
            "commentsExtensible",
            element => element.Name.NamespaceName == Word2018CommentExtensibleNamespace
        );
        foreach (
            var element in root.Elements()
                .Where(element =>
                    element.Name.NamespaceName == Word2018CommentExtensibleNamespace
                    && element.Name.LocalName == "commentExtensible"
                )
                .OrderBy(source.GetElementOrdinal)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = source.GetElementOrdinal(element);
            var durableId = LocalAttribute(element, "durableId");
            if (
                string.IsNullOrWhiteSpace(durableId)
                || !state.CommentsByDurableId.TryGetValue(durableId, out var comment)
            )
            {
                state.AddIssue(
                    "COMMENT_EXTENSIBLE_ORPHAN",
                    WordReviewIssueSeverity.Warning,
                    "Extensible comment record does not resolve through a durable identifier.",
                    part.Uri,
                    null,
                    ordinal
                );
                continue;
            }
            if (comment.HasExtensibleRecord)
            {
                state.AddIssue(
                    "COMMENT_EXTENSIBLE_DUPLICATE",
                    WordReviewIssueSeverity.Error,
                    "Comment has more than one extensible metadata record.",
                    part.Uri,
                    null,
                    ordinal,
                    comment.Id
                );
                continue;
            }
            comment.HasExtensibleRecord = true;
            comment.ExtensibleDateUtc = LocalAttribute(element, "dateUtc");
            if (!IsValidOptionalDate(comment.ExtensibleDateUtc))
            {
                state.AddIssue(
                    "COMMENT_EXTENSIBLE_DATE_INVALID",
                    WordReviewIssueSeverity.Warning,
                    "Extensible comment UTC date is not a valid date-time value.",
                    part.Uri,
                    null,
                    ordinal,
                    comment.Id
                );
            }
            comment.IsIntelligentPlaceholder = ParseOnOff(
                LocalAttribute(element, "intelligentPlaceholder"),
                defaultValue: false,
                state,
                part.Uri,
                ordinal,
                comment.Id
            );
            comment.ExtensionCount = element.Descendants()
                .Count(descendant => descendant.Name.LocalName == "ext");
            comment.HasReactions = element.Descendants()
                .Any(descendant => descendant.Name.LocalName == "reactions");
        }
    }

    private void ParsePeople(
        OpcPackageSnapshot package,
        OpcPart part,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        state.PeoplePartPresent = true;
        var source = state.SourceFor(package, part.Uri, this, cancellationToken);
        var root = RequireRoot(
            part.Uri,
            source,
            "people",
            element => element.Name.NamespaceName == Word2012Namespace
        );
        foreach (
            var element in root.Elements()
                .Where(element =>
                    element.Name.NamespaceName == Word2012Namespace
                    && element.Name.LocalName == "person"
                )
                .OrderBy(source.GetElementOrdinal)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (state.People.Count >= _options.MaxPeople)
            {
                throw new WordReviewLimitException(
                    $"Document contains more than {_options.MaxPeople} people records."
                );
            }
            var ordinal = source.GetElementOrdinal(element);
            var author = LocalAttribute(element, "author");
            var presence = element.Elements().FirstOrDefault(child =>
                child.Name.NamespaceName == Word2012Namespace
                && child.Name.LocalName == "presenceInfo"
            );
            var id = StableId(
                "wdp_",
                part.Uri,
                author ?? ordinal.ToString(CultureInfo.InvariantCulture),
                ordinal.ToString(CultureInfo.InvariantCulture)
            );
            var person = new MutablePerson(
                id,
                part.Uri,
                ordinal,
                author,
                presence is null ? null : LocalAttribute(presence, "providerId"),
                presence is null ? null : LocalAttribute(presence, "userId")
            );
            state.People.Add(person);
            if (string.IsNullOrWhiteSpace(author))
            {
                state.AddIssue(
                    "PERSON_AUTHOR_MISSING",
                    WordReviewIssueSeverity.Error,
                    "People record has no author attribute.",
                    part.Uri,
                    null,
                    ordinal,
                    id
                );
            }
            else if (!state.PeopleByAuthor.TryAdd(author, person))
            {
                state.AddIssue(
                    "PERSON_AUTHOR_DUPLICATE",
                    WordReviewIssueSeverity.Warning,
                    "People part contains more than one record for the same author.",
                    part.Uri,
                    null,
                    ordinal,
                    id
                );
            }
        }
    }

    private WordReviewSettingsDefinition? ParseSettings(
        OpcPackageSnapshot package,
        OpcPart part,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        var source = state.SourceFor(package, part.Uri, this, cancellationToken);
        var root = source.ParsedDocument.Root;
        if (root is null || !IsWordElement(root, "settings"))
        {
            state.AddIssue(
                "REVIEW_SETTINGS_ROOT_INVALID",
                WordReviewIssueSeverity.Warning,
                "Settings part does not have a w:settings root; review toggles are unavailable.",
                part.Uri
            );
            return null;
        }
        return new WordReviewSettingsDefinition(
            part.Uri,
            ElementOnOff(root, "trackRevisions"),
            ElementOnOff(root, "doNotTrackMoves"),
            ElementOnOff(root, "doNotTrackFormatting")
        );
    }

    private void ParseStoryMarkup(
        string partUri,
        LosslessXmlDocument source,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        var root = source.ParsedDocument.Root
            ?? throw new WordReviewProjectionException(
                $"Projected story part '{partUri}' has no root element."
            );
        var revisionElements = new List<(XElement Element, WordRevisionKind Kind)>();
        foreach (var element in root.DescendantsAndSelf().OrderBy(source.GetElementOrdinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsWordElement(element))
            {
                var localName = element.Name.LocalName;
                if (localName is "commentRangeStart" or "commentRangeEnd" or "commentReference")
                {
                    AddCommentMarker(partUri, source, element, localName, state);
                    continue;
                }
                if (localName is "moveFromRangeStart" or "moveFromRangeEnd" or "moveToRangeStart" or "moveToRangeEnd")
                {
                    AddMoveMarker(partUri, source, element, localName, state);
                    continue;
                }
                if (localName is "permStart" or "permEnd")
                {
                    AddPermissionMarker(
                        partUri,
                        source,
                        element,
                        localName,
                        root.Name.Namespace,
                        state
                    );
                    continue;
                }
            }
            var kind = RevisionKind(element);
            if (kind is not null)
            {
                revisionElements.Add((element, kind.Value));
            }
        }

        if (state.Revisions.Count + revisionElements.Count > _options.MaxRevisions)
        {
            throw new WordReviewLimitException(
                $"Document contains more than {_options.MaxRevisions} revisions."
            );
        }
        var ids = new Dictionary<XElement, string>(ReferenceEqualityComparer.Instance);
        foreach (var item in revisionElements)
        {
            ids.Add(
                item.Element,
                RevisionStableId(partUri, source, item.Element, item.Kind, state)
            );
        }
        foreach (var (element, kind) in revisionElements)
        {
            var ordinal = source.GetElementOrdinal(element);
            var id = ids[element];
            var location = LocationFor(partUri, source, element, state);
            var paragraph = NearestSemanticAncestor(
                location.NearestNode,
                state.SemanticDocument,
                WordSemanticNodeKind.Paragraph
            );
            var parentRevision = element.Ancestors()
                .FirstOrDefault(ancestor => ids.ContainsKey(ancestor));
            var ooxmlId = WordAttribute(element, "id") ?? LocalAttribute(element, "id");
            var date = WordAttribute(element, "date") ?? LocalAttribute(element, "date");
            var dateUtc = LocalAttribute(element, "dateUtc");
            var status = string.IsNullOrWhiteSpace(ooxmlId)
                ? WordRevisionStatus.MissingId
                : !IsValidOptionalDate(date) || !IsValidOptionalDate(dateUtc)
                    ? WordRevisionStatus.InvalidDate
                    : WordRevisionStatus.Complete;
            if (status == WordRevisionStatus.MissingId)
            {
                state.AddIssue(
                    "REVISION_ID_MISSING",
                    WordReviewIssueSeverity.Error,
                    "Tracked revision has no annotation identifier.",
                    partUri,
                    location.StoryId,
                    ordinal,
                    id
                );
            }
            else if (status == WordRevisionStatus.InvalidDate)
            {
                state.AddIssue(
                    "REVISION_DATE_INVALID",
                    WordReviewIssueSeverity.Warning,
                    "Tracked revision has an invalid date-time value.",
                    partUri,
                    location.StoryId,
                    ordinal,
                    id
                );
            }
            var capture = CaptureText(element.DescendantsAndSelf(), state);
            state.Revisions.Add(
                new MutableRevision(
                    id,
                    kind,
                    status,
                    SourceName(element),
                    location.StoryId,
                    location.Kind,
                    location.StoryNode?.Id,
                    paragraph?.Id,
                    partUri,
                    ordinal,
                    state.NodeFor(partUri, ordinal)?.Id,
                    parentRevision is null ? null : ids[parentRevision],
                    ooxmlId,
                    WordAttribute(element, "author") ?? LocalAttribute(element, "author"),
                    date,
                    dateUtc,
                    capture,
                    element.Descendants().Count(),
                    element.DescendantsAndSelf().Any(IsMathElement),
                    kind is WordRevisionKind.Deletion or WordRevisionKind.MoveFrom
                        || element.Ancestors().Any(ancestor =>
                            IsWordElement(ancestor)
                            && ancestor.Name.LocalName is "del" or "moveFrom"
                        )
                )
            );
        }
    }

    private void AddCommentMarker(
        string partUri,
        LosslessXmlDocument source,
        XElement element,
        string localName,
        BuildState state
    )
    {
        if (state.CommentMarkers.Count >= _options.MaxAnchors * 3L)
        {
            throw new WordReviewLimitException(
                $"Document contains too many comment anchor markers for {_options.MaxAnchors} anchors."
            );
        }
        var ordinal = source.GetElementOrdinal(element);
        var location = LocationFor(partUri, source, element, state);
        var ooxmlId = WordAttribute(element, "id");
        if (string.IsNullOrWhiteSpace(ooxmlId))
        {
            state.AddIssue(
                "COMMENT_ANCHOR_ID_MISSING",
                WordReviewIssueSeverity.Error,
                "Comment anchor marker has no w:id.",
                partUri,
                location.StoryId,
                ordinal
            );
        }
        state.CommentMarkers.Add(
            new Marker(
                location,
                partUri,
                ooxmlId,
                localName,
                ordinal,
                state.NodeFor(partUri, ordinal)?.Id,
                element
            )
        );
    }

    private void AddMoveMarker(
        string partUri,
        LosslessXmlDocument source,
        XElement element,
        string localName,
        BuildState state
    )
    {
        if (state.MoveMarkers.Count >= _options.MaxMoveRanges * 2L)
        {
            throw new WordReviewLimitException(
                $"Document contains too many move markers for {_options.MaxMoveRanges} ranges."
            );
        }
        var ordinal = source.GetElementOrdinal(element);
        var location = LocationFor(partUri, source, element, state);
        var ooxmlId = WordAttribute(element, "id");
        if (string.IsNullOrWhiteSpace(ooxmlId))
        {
            state.AddIssue(
                "MOVE_RANGE_ID_MISSING",
                WordReviewIssueSeverity.Error,
                "Move range marker has no w:id.",
                partUri,
                location.StoryId,
                ordinal
            );
        }
        state.MoveMarkers.Add(
            new Marker(
                location,
                partUri,
                ooxmlId,
                localName,
                ordinal,
                null,
                element
            )
        );
    }

    private void AddPermissionMarker(
        string partUri,
        LosslessXmlDocument source,
        XElement element,
        string localName,
        XNamespace storyNamespace,
        BuildState state
    )
    {
        if (state.PermissionMarkers.Count >= _options.MaxPermissions * 2L)
        {
            throw new WordReviewLimitException(
                $"Document contains too many permission markers for {_options.MaxPermissions} ranges."
            );
        }
        var ordinal = source.GetElementOrdinal(element);
        var location = LocationFor(partUri, source, element, state);
        ValidatePermissionMarkerNamespace(
            partUri,
            element,
            storyNamespace,
            location,
            ordinal,
            state
        );
        ValidatePermissionAttributeNamespaces(
            partUri,
            element,
            location,
            ordinal,
            state
        );
        ValidatePermissionAttributePlacement(
            partUri,
            element,
            localName,
            location,
            ordinal,
            state
        );
        var ooxmlId = PermissionAttribute(element, "id");
        if (string.IsNullOrWhiteSpace(ooxmlId))
        {
            state.AddIssue(
                "PERMISSION_RANGE_ID_MISSING",
                WordReviewIssueSeverity.Error,
                "Permission range marker has no w:id.",
                partUri,
                location.StoryId,
                ordinal
            );
        }
        else if (ParseInteger(ooxmlId) is null)
        {
            state.AddIssue(
                "PERMISSION_RANGE_ID_INVALID",
                WordReviewIssueSeverity.Error,
                "Permission range marker w:id is not a valid Int32 value.",
                partUri,
                location.StoryId,
                ordinal
            );
        }
        var displacedByCustomXml = PermissionAttribute(element, "displacedByCustomXml");
        if (
            displacedByCustomXml is not null
            && displacedByCustomXml is not ("next" or "prev")
        )
        {
            state.AddIssue(
                "PERMISSION_DISPLACEMENT_INVALID",
                WordReviewIssueSeverity.Error,
                "Permission range displacement metadata is invalid.",
                partUri,
                location.StoryId,
                ordinal
            );
        }
        state.PermissionMarkers.Add(
            new Marker(
                location,
                partUri,
                ooxmlId,
                localName,
                ordinal,
                null,
                element
            )
        );
    }

    private void FinalizeAnchors(BuildState state)
    {
        foreach (
            var group in state.CommentMarkers
                .GroupBy(
                    marker => new
                    {
                        marker.Location.StoryId,
                        Key = marker.OoxmlId
                            ?? "missing:" + marker.Ordinal.ToString(CultureInfo.InvariantCulture),
                    }
                )
                .OrderBy(group => group.Min(marker => marker.Ordinal))
        )
        {
            if (state.Anchors.Count >= _options.MaxAnchors)
            {
                throw new WordReviewLimitException(
                    $"Document contains more than {_options.MaxAnchors} comment anchors."
                );
            }
            var markers = group.OrderBy(marker => marker.Ordinal).ToArray();
            var first = markers[0];
            var starts = markers.Where(marker => marker.Kind == "commentRangeStart").ToArray();
            var ends = markers.Where(marker => marker.Kind == "commentRangeEnd").ToArray();
            var references = markers.Where(marker => marker.Kind == "commentReference").ToArray();
            var status = CommentAnchorStatus(starts, ends, references);
            var ooxmlId = first.OoxmlId;
            var comment = ooxmlId is not null
                && state.EffectiveCommentsByOoxmlId.TryGetValue(ooxmlId, out var resolved)
                    ? resolved
                    : null;
            var id = StableId(
                "wda_",
                first.Location.StoryId,
                ooxmlId ?? first.Ordinal.ToString(CultureInfo.InvariantCulture)
            );
            TextCapture capture = TextCapture.Empty;
            if (
                starts.Length == 1
                && ends.Length == 1
                && starts[0].Ordinal < ends[0].Ordinal
            )
            {
                var source = state.Sources[first.PartUri];
                capture = CaptureText(
                    source.ParsedDocument.Root!.DescendantsAndSelf().Where(element =>
                        source.GetElementOrdinal(element) > starts[0].Ordinal
                        && source.GetElementOrdinal(element) < ends[0].Ordinal
                    ),
                    state
                );
            }
            var anchor = new WordCommentAnchor(
                id,
                first.Location.StoryId,
                first.Location.Kind,
                first.Location.StoryNode?.Id,
                first.PartUri,
                ooxmlId,
                comment?.Id,
                status,
                starts.Length,
                ends.Length,
                references.Length,
                starts.FirstOrDefault()?.Ordinal,
                ends.FirstOrDefault()?.Ordinal,
                references.FirstOrDefault()?.Ordinal,
                starts.FirstOrDefault()?.NodeId,
                ends.FirstOrDefault()?.NodeId,
                references.FirstOrDefault()?.NodeId,
                capture.Text,
                capture.CharacterCount,
                capture.Truncated
            );
            state.Anchors.Add(anchor);
            comment?.AnchorIds.Add(id);
            if (comment is null)
            {
                state.AddIssue(
                    "COMMENT_DEFINITION_MISSING",
                    WordReviewIssueSeverity.Error,
                    "Comment anchor does not resolve to a comment definition.",
                    first.PartUri,
                    first.Location.StoryId,
                    first.Ordinal,
                    id
                );
            }
            if (status is not WordCommentAnchorStatus.Complete
                and not WordCommentAnchorStatus.PointReference)
            {
                state.AddIssue(
                    "COMMENT_ANCHOR_INCOMPLETE",
                    WordReviewIssueSeverity.Error,
                    $"Comment anchor is {ToSnakeCase(status.ToString())}.",
                    first.PartUri,
                    first.Location.StoryId,
                    first.Ordinal,
                    id
                );
            }
        }
        foreach (var comment in state.Comments.Where(comment => comment.AnchorIds.Count == 0))
        {
            state.AddIssue(
                "COMMENT_UNREFERENCED",
                WordReviewIssueSeverity.Warning,
                "Comment definition is not referenced by a comment anchor.",
                comment.PartUri,
                null,
                comment.SourceElementOrdinal,
                comment.Id
            );
        }
    }

    private void FinalizeMoves(BuildState state)
    {
        foreach (
            var group in state.MoveMarkers
                .GroupBy(marker => new
                {
                    marker.Location.StoryId,
                    RangeKind = marker.Kind.StartsWith("moveFrom", StringComparison.Ordinal)
                        ? WordMoveRangeKind.Source
                        : WordMoveRangeKind.Destination,
                    Key = marker.OoxmlId
                        ?? "missing:" + marker.Ordinal.ToString(CultureInfo.InvariantCulture),
                })
                .OrderBy(group => group.Min(marker => marker.Ordinal))
        )
        {
            if (state.MoveRanges.Count >= _options.MaxMoveRanges)
            {
                throw new WordReviewLimitException(
                    $"Document contains more than {_options.MaxMoveRanges} move ranges."
                );
            }
            var markers = group.OrderBy(marker => marker.Ordinal).ToArray();
            var first = markers[0];
            var starts = markers.Where(marker => marker.Kind.EndsWith("RangeStart", StringComparison.Ordinal)).ToArray();
            var ends = markers.Where(marker => marker.Kind.EndsWith("RangeEnd", StringComparison.Ordinal)).ToArray();
            var status = RangeStatus(starts, ends);
            var start = starts.FirstOrDefault();
            var end = ends.FirstOrDefault();
            var revisions = start is not null && end is not null
                ? state.Revisions.Where(revision =>
                        revision.StoryId == first.Location.StoryId
                        && revision.SourceElementOrdinal > start.Ordinal
                        && revision.SourceElementOrdinal < end.Ordinal
                        && (group.Key.RangeKind == WordMoveRangeKind.Source
                            ? revision.Kind == WordRevisionKind.MoveFrom
                            : revision.Kind == WordRevisionKind.MoveTo)
                    )
                    .Select(revision => revision.Id)
                    .ToArray()
                : Array.Empty<string>();
            var id = StableId(
                "wdmr_",
                first.Location.StoryId,
                group.Key.RangeKind.ToString(),
                first.OoxmlId ?? first.Ordinal.ToString(CultureInfo.InvariantCulture)
            );
            var range = new WordMoveRangeDefinition(
                id,
                group.Key.RangeKind,
                status,
                first.Location.StoryId,
                first.Location.Kind,
                first.Location.StoryNode?.Id,
                first.PartUri,
                first.OoxmlId,
                start is null ? null : WordAttribute(start.Element, "name"),
                start is null ? null : WordAttribute(start.Element, "author"),
                start is null ? null : WordAttribute(start.Element, "date"),
                starts.Length,
                ends.Length,
                start?.Ordinal,
                end?.Ordinal,
                revisions
            );
            state.MoveRanges.Add(range);
            if (status != WordReviewRangeStatus.Complete)
            {
                state.AddIssue(
                    "MOVE_RANGE_INCOMPLETE",
                    WordReviewIssueSeverity.Error,
                    $"Move range is {ToSnakeCase(status.ToString())}.",
                    first.PartUri,
                    first.Location.StoryId,
                    first.Ordinal,
                    id
                );
            }
        }

        var named = state.MoveRanges.Where(range => !string.IsNullOrWhiteSpace(range.Name))
            .GroupBy(range => range.Name!, StringComparer.Ordinal);
        var pairedRangeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in named.OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var sources = group.Where(range => range.Kind == WordMoveRangeKind.Source).ToArray();
            var destinations = group.Where(range => range.Kind == WordMoveRangeKind.Destination).ToArray();
            var status = sources.Length == 1 && destinations.Length == 1
                ? WordMovePairStatus.Complete
                : sources.Length == 0
                    ? WordMovePairStatus.MissingSource
                    : destinations.Length == 0
                        ? WordMovePairStatus.MissingDestination
                        : WordMovePairStatus.Ambiguous;
            var move = new WordMovePairDefinition(
                StableId("wdm_", group.Key),
                group.Key,
                status,
                sources.Length == 1 ? sources[0].Id : null,
                destinations.Length == 1 ? destinations[0].Id : null
            );
            state.Moves.Add(move);
            foreach (var range in sources.Concat(destinations)) pairedRangeIds.Add(range.Id);
            if (status != WordMovePairStatus.Complete)
            {
                state.AddIssue(
                    "MOVE_PAIR_INCOMPLETE",
                    WordReviewIssueSeverity.Error,
                    $"Named move pair is {ToSnakeCase(status.ToString())}.",
                    subjectId: move.Id
                );
            }
        }
        foreach (var range in state.MoveRanges.Where(range => !pairedRangeIds.Contains(range.Id)))
        {
            var status = range.Kind == WordMoveRangeKind.Source
                ? WordMovePairStatus.MissingDestination
                : WordMovePairStatus.MissingSource;
            var move = new WordMovePairDefinition(
                StableId("wdm_", range.Id),
                null,
                status,
                range.Kind == WordMoveRangeKind.Source ? range.Id : null,
                range.Kind == WordMoveRangeKind.Destination ? range.Id : null
            );
            state.Moves.Add(move);
            state.AddIssue(
                "MOVE_NAME_MISSING",
                WordReviewIssueSeverity.Warning,
                "Move range cannot be paired because its start marker has no move name.",
                range.PartUri,
                range.StoryId,
                range.StartElementOrdinal,
                move.Id
            );
        }
    }

    private void FinalizePermissions(BuildState state)
    {
        foreach (
            var group in state.PermissionMarkers
                .GroupBy(marker => new
                {
                    marker.Location.StoryId,
                    Key = marker.OoxmlId
                        ?? "missing:" + marker.Ordinal.ToString(CultureInfo.InvariantCulture),
                })
                .OrderBy(group => group.Min(marker => marker.Ordinal))
        )
        {
            if (state.Permissions.Count >= _options.MaxPermissions)
            {
                throw new WordReviewLimitException(
                    $"Document contains more than {_options.MaxPermissions} permission ranges."
                );
            }
            var markers = group.OrderBy(marker => marker.Ordinal).ToArray();
            var first = markers[0];
            var starts = markers.Where(marker => marker.Kind == "permStart").ToArray();
            var ends = markers.Where(marker => marker.Kind == "permEnd").ToArray();
            var start = starts.FirstOrDefault();
            var end = ends.FirstOrDefault();
            var status = RangeStatus(starts, ends);
            var editor = start is null ? null : PermissionAttribute(start.Element, "ed");
            var editorGroup = start is null
                ? null
                : PermissionAttribute(start.Element, "edGrp");
            var rawColumnFirst = start is null
                ? null
                : PermissionAttribute(start.Element, "colFirst");
            var rawColumnLast = start is null
                ? null
                : PermissionAttribute(start.Element, "colLast");
            var columnFirst = ParseInteger(rawColumnFirst);
            var columnLast = ParseInteger(rawColumnLast);
            var id = StableId(
                "wdpr_",
                first.Location.StoryId,
                first.OoxmlId ?? first.Ordinal.ToString(CultureInfo.InvariantCulture)
            );
            var permission = new WordPermissionRangeDefinition(
                id,
                status,
                first.Location.StoryId,
                first.Location.Kind,
                first.Location.StoryNode?.Id,
                first.PartUri,
                first.OoxmlId,
                editor,
                editorGroup,
                columnFirst,
                columnLast,
                starts.Length,
                ends.Length,
                start?.Ordinal,
                end?.Ordinal
            );
            state.Permissions.Add(permission);
            if (status != WordReviewRangeStatus.Complete)
            {
                state.AddIssue(
                    "PERMISSION_RANGE_INCOMPLETE",
                    WordReviewIssueSeverity.Error,
                    $"Permission range is {ToSnakeCase(status.ToString())}.",
                    first.PartUri,
                    first.Location.StoryId,
                    first.Ordinal,
                    id
                );
            }
            if (start is not null)
            {
                if ((rawColumnFirst is null) != (rawColumnLast is null))
                {
                    state.AddIssue(
                        "PERMISSION_COLUMN_RANGE_INCOMPLETE",
                        WordReviewIssueSeverity.Error,
                        "Table-column permission must define both colFirst and colLast.",
                        first.PartUri,
                        first.Location.StoryId,
                        start.Ordinal,
                        id
                    );
                }
                if (
                    rawColumnFirst is not null && columnFirst is null
                    || rawColumnLast is not null && columnLast is null
                    || columnFirst is < 0
                    || columnLast is < 0
                    || columnFirst is { } firstColumn
                        && columnLast is { } lastColumn
                        && firstColumn > lastColumn
                )
                {
                    state.AddIssue(
                        "PERMISSION_COLUMN_RANGE_INVALID",
                        WordReviewIssueSeverity.Error,
                        "Table-column permission contains invalid column bounds; values must be non-negative Int32 numbers with colFirst less than or equal to colLast.",
                        first.PartUri,
                        first.Location.StoryId,
                        start.Ordinal,
                        id
                    );
                }
                if (
                    editorGroup is not null
                    && !IsPermissionEditingGroup(editorGroup)
                )
                {
                    state.AddIssue(
                        "PERMISSION_EDITOR_GROUP_INVALID",
                        WordReviewIssueSeverity.Error,
                        "Permission editor-group metadata is invalid.",
                        first.PartUri,
                        first.Location.StoryId,
                        start.Ordinal,
                        id
                    );
                }
            }
        }
    }

    private void FinalizeThreads(BuildState state)
    {
        foreach (var comment in state.Comments)
        {
            if (string.IsNullOrWhiteSpace(comment.ParentParagraphId)) continue;
            if (!state.CommentsByLastParagraphId.TryGetValue(
                    comment.ParentParagraphId,
                    out var parent
                ))
            {
                state.AddIssue(
                    "COMMENT_THREAD_PARENT_MISSING",
                    WordReviewIssueSeverity.Error,
                    "Reply comment does not resolve to its parent comment paragraph.",
                    comment.PartUri,
                    null,
                    comment.SourceElementOrdinal,
                    comment.Id
                );
                continue;
            }
            comment.Parent = parent;
        }
        foreach (var comment in state.Comments)
        {
            ResolveThread(comment, state, new HashSet<string>(StringComparer.Ordinal));
            if (comment.IsIntelligentPlaceholder && comment.Parent is not null)
            {
                state.AddIssue(
                    "COMMENT_REPLY_PLACEHOLDER_INVALID",
                    WordReviewIssueSeverity.Error,
                    "Reply comment must not be marked as an intelligent placeholder.",
                    comment.PartUri,
                    null,
                    comment.SourceElementOrdinal,
                    comment.Id
                );
            }
        }
    }

    private void ResolveThread(
        MutableComment comment,
        BuildState state,
        HashSet<string> path
    )
    {
        if (comment.ThreadResolved) return;
        if (!path.Add(comment.Id))
        {
            state.AddIssue(
                "COMMENT_THREAD_CYCLE",
                WordReviewIssueSeverity.Error,
                "Comment reply graph contains a cycle.",
                comment.PartUri,
                null,
                comment.SourceElementOrdinal,
                comment.Id
            );
            comment.Parent = null;
            comment.ThreadRootId = comment.Id;
            comment.ThreadDepth = 0;
            comment.ThreadResolved = true;
            return;
        }
        if (comment.Parent is null)
        {
            comment.ThreadRootId = comment.Id;
            comment.ThreadDepth = 0;
        }
        else
        {
            ResolveThread(comment.Parent, state, path);
            comment.ThreadRootId = comment.Parent.ThreadRootId;
            comment.ThreadDepth = checked(comment.Parent.ThreadDepth + 1);
            if (comment.ThreadDepth > _options.MaxThreadDepth)
            {
                throw new WordReviewLimitException(
                    $"Comment thread exceeds {_options.MaxThreadDepth} levels."
                );
            }
        }
        comment.ThreadResolved = true;
        path.Remove(comment.Id);
    }

    private static void FinalizePeople(BuildState state)
    {
        foreach (var comment in state.Comments)
        {
            if (
                !string.IsNullOrWhiteSpace(comment.Author)
                && state.PeopleByAuthor.TryGetValue(comment.Author, out var person)
            )
            {
                comment.PersonId = person.Id;
                person.CommentCount++;
            }
            else if (state.PeoplePartPresent && !string.IsNullOrWhiteSpace(comment.Author))
            {
                state.AddIssue(
                    "COMMENT_PERSON_MISSING",
                    WordReviewIssueSeverity.Warning,
                    "Comment author has no matching people-part record.",
                    comment.PartUri,
                    null,
                    comment.SourceElementOrdinal,
                    comment.Id
                );
            }
        }
        foreach (var revision in state.Revisions)
        {
            if (
                !string.IsNullOrWhiteSpace(revision.Author)
                && state.PeopleByAuthor.TryGetValue(revision.Author, out var person)
            )
            {
                revision.PersonId = person.Id;
                person.RevisionCount++;
            }
            else if (state.PeoplePartPresent && !string.IsNullOrWhiteSpace(revision.Author))
            {
                state.AddIssue(
                    "REVISION_PERSON_MISSING",
                    WordReviewIssueSeverity.Warning,
                    "Revision author has no matching people-part record.",
                    revision.PartUri,
                    revision.StoryId,
                    revision.SourceElementOrdinal,
                    revision.Id
                );
            }
        }
        foreach (var person in state.People.Where(person =>
            person.CommentCount == 0 && person.RevisionCount == 0
        ))
        {
            state.AddIssue(
                "PERSON_UNREFERENCED",
                WordReviewIssueSeverity.Warning,
                "People-part record does not match any comment or revision author.",
                person.PartUri,
                null,
                person.SourceElementOrdinal,
                person.Id
            );
        }
    }

    private static void AuditRevisionIds(BuildState state)
    {
        foreach (
            var duplicate in state.Revisions
                .Where(revision => !string.IsNullOrWhiteSpace(revision.OoxmlId))
                .GroupBy(
                    revision => (revision.StoryId, revision.OoxmlId!),
                    new StoryRevisionIdComparer()
                )
                .Where(group => group.Count() > 1)
        )
        {
            foreach (var revision in duplicate.Skip(1))
            {
                state.AddIssue(
                    "REVISION_ID_DUPLICATE",
                    WordReviewIssueSeverity.Error,
                    "Annotation identifier is reused by more than one revision in the same story.",
                    revision.PartUri,
                    revision.StoryId,
                    revision.SourceElementOrdinal,
                    revision.Id
                );
            }
        }
    }

    private LosslessXmlDocument ParsePart(
        OpcPart part,
        CancellationToken cancellationToken
    )
    {
        if (part.Entry.Content.Length > _options.MaxPartBytes)
        {
            throw new WordReviewLimitException(
                $"Review part '{part.Uri}' exceeds {_options.MaxPartBytes} bytes."
            );
        }
        try
        {
            return LosslessXmlDocument.Parse(
                part.Entry.Content,
                new LosslessXmlOptions
                {
                    MaxSourceBytes = _options.MaxPartBytes,
                    MaxXmlCharacters = _options.MaxPartBytes,
                    MaxXmlElements = 1_000_000,
                    MaxXmlDepth = 256,
                    MaxTextCharacters = _options.MaxPartBytes,
                },
                cancellationToken
            );
        }
        catch (LosslessXmlLimitException exception)
        {
            throw new WordReviewLimitException(
                $"Review part '{part.Uri}' exceeds an XML safety limit: {exception.Message}"
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordReviewProjectionException(
                $"Review part '{part.Uri}' is not safe, well-formed XML.",
                exception
            );
        }
    }

    private static XElement RequireRoot(
        string partUri,
        LosslessXmlDocument source,
        string localName,
        Func<XElement, bool> namespacePredicate
    )
    {
        var root = source.ParsedDocument.Root;
        if (
            root is null
            || root.Name.LocalName != localName
            || !namespacePredicate(root)
        )
        {
            throw new WordReviewProjectionException(
                $"Review part '{partUri}' does not have the expected {localName} root element."
            );
        }
        return root;
    }

    private TextCapture CaptureText(
        IEnumerable<XElement> elements,
        BuildState state
    )
    {
        var builder = new StringBuilder();
        long count = 0;
        foreach (var element in elements)
        {
            if (!IsTextElement(element)) continue;
            var value = element.Value;
            checked
            {
                count += value.Length;
                state.TotalTextCharacters += value.Length;
            }
            if (state.TotalTextCharacters > _options.MaxTotalTextCharacters)
            {
                throw new WordReviewLimitException(
                    $"Review text exceeds {_options.MaxTotalTextCharacters} characters."
                );
            }
            var remaining = _options.MaxTextCharactersPerItem - builder.Length;
            if (remaining > 0)
            {
                builder.Append(value.AsSpan(0, Math.Min(remaining, value.Length)));
            }
        }
        if (count > int.MaxValue)
        {
            throw new WordReviewLimitException(
                "A single review item contains more than 2147483647 text characters."
            );
        }
        return new TextCapture(builder.ToString(), (int)count, count > builder.Length);
    }

    private static WordCommentAnchorStatus CommentAnchorStatus(
        IReadOnlyList<Marker> starts,
        IReadOnlyList<Marker> ends,
        IReadOnlyList<Marker> references
    )
    {
        if (starts.Count > 1 || ends.Count > 1 || references.Count > 1)
            return WordCommentAnchorStatus.Ambiguous;
        if (starts.Count == 0 && ends.Count == 0 && references.Count == 1)
            return WordCommentAnchorStatus.PointReference;
        if (starts.Count == 0) return WordCommentAnchorStatus.MissingStart;
        if (ends.Count == 0) return WordCommentAnchorStatus.MissingEnd;
        if (starts[0].Ordinal >= ends[0].Ordinal) return WordCommentAnchorStatus.Reversed;
        if (references.Count == 0) return WordCommentAnchorStatus.MissingReference;
        return WordCommentAnchorStatus.Complete;
    }

    private static WordReviewRangeStatus RangeStatus(
        IReadOnlyList<Marker> starts,
        IReadOnlyList<Marker> ends
    )
    {
        if (starts.Count > 1 || ends.Count > 1) return WordReviewRangeStatus.Ambiguous;
        if (starts.Count == 0) return WordReviewRangeStatus.MissingStart;
        if (ends.Count == 0) return WordReviewRangeStatus.MissingEnd;
        return starts[0].Ordinal < ends[0].Ordinal
            ? WordReviewRangeStatus.Complete
            : WordReviewRangeStatus.Reversed;
    }

    private static StoryLocation LocationFor(
        string partUri,
        LosslessXmlDocument source,
        XElement element,
        BuildState state
    )
    {
        WordSemanticNode? nearest = null;
        foreach (var candidate in element.AncestorsAndSelf())
        {
            nearest = state.NodeFor(partUri, source.GetElementOrdinal(candidate));
            if (nearest is not null) break;
        }
        var current = nearest;
        while (current is not null)
        {
            var kind = current.Kind switch
            {
                WordSemanticNodeKind.TextBox => WordStoryKind.TextBox,
                WordSemanticNodeKind.Footnote => WordStoryKind.Footnote,
                WordSemanticNodeKind.Endnote => WordStoryKind.Endnote,
                WordSemanticNodeKind.Comment => WordStoryKind.Comment,
                WordSemanticNodeKind.GlossaryEntry => WordStoryKind.GlossaryEntry,
                WordSemanticNodeKind.Header => WordStoryKind.Header,
                WordSemanticNodeKind.Footer => WordStoryKind.Footer,
                WordSemanticNodeKind.Document => WordStoryKind.Main,
                _ => (WordStoryKind?)null,
            };
            if (kind is not null)
            {
                return new StoryLocation(
                    StableId("wds_", current.Id.Value),
                    kind.Value,
                    current,
                    nearest
                );
            }
            current = Parent(current, state.SemanticDocument);
        }
        return new StoryLocation(
            StableId("wds_", partUri),
            WordStoryKind.Other,
            null,
            nearest
        );
    }

    private static WordSemanticNode? Parent(
        WordSemanticNode node,
        WordSemanticDocument document
    ) => node.ParentId is { } parentId && document.TryGetNode(parentId, out var parent)
        ? parent
        : null;

    private static WordSemanticNode? NearestSemanticAncestor(
        WordSemanticNode? node,
        WordSemanticDocument document,
        WordSemanticNodeKind kind
    )
    {
        var current = node;
        while (current is not null)
        {
            if (current.Kind == kind) return current;
            current = Parent(current, document);
        }
        return null;
    }

    private static string RevisionStableId(
        string partUri,
        LosslessXmlDocument source,
        XElement element,
        WordRevisionKind kind,
        BuildState state
    )
    {
        var ordinal = source.GetElementOrdinal(element);
        var semantic = state.NodeFor(partUri, ordinal);
        return StableId(
            "wdr_",
            partUri,
            semantic?.Id.Value ?? ordinal.ToString(CultureInfo.InvariantCulture),
            kind.ToString()
        );
    }

    private static WordRevisionKind? RevisionKind(XElement element)
    {
        if (element.Name.NamespaceName == Word2010Namespace)
        {
            return element.Name.LocalName switch
            {
                "conflictIns" => WordRevisionKind.ConflictInsertion,
                "conflictDel" => WordRevisionKind.ConflictDeletion,
                _ => null,
            };
        }
        if (!IsWordElement(element)) return null;
        return element.Name.LocalName switch
        {
            "ins" => WordRevisionKind.Insertion,
            "del" => WordRevisionKind.Deletion,
            "moveFrom" => WordRevisionKind.MoveFrom,
            "moveTo" => WordRevisionKind.MoveTo,
            "rPrChange" => WordRevisionKind.RunPropertiesChange,
            "pPrChange" => WordRevisionKind.ParagraphPropertiesChange,
            "tblPrChange" => WordRevisionKind.TablePropertiesChange,
            "tblGridChange" => WordRevisionKind.TableGridChange,
            "trPrChange" => WordRevisionKind.TableRowPropertiesChange,
            "tcPrChange" => WordRevisionKind.TableCellPropertiesChange,
            "sectPrChange" => WordRevisionKind.SectionPropertiesChange,
            "numPrChange" => WordRevisionKind.NumberingPropertiesChange,
            "tblPrExChange" => WordRevisionKind.OtherPropertyChange,
            "numberingChange" => WordRevisionKind.NumberingChange,
            "cellIns" => WordRevisionKind.CellInsertion,
            "cellDel" => WordRevisionKind.CellDeletion,
            "cellMerge" => WordRevisionKind.CellMerge,
            "customXmlInsRangeStart" => WordRevisionKind.CustomXmlInsertion,
            "customXmlDelRangeStart" => WordRevisionKind.CustomXmlDeletion,
            _ when element.Name.LocalName.EndsWith("PrChange", StringComparison.Ordinal)
                => WordRevisionKind.OtherPropertyChange,
            _ => null,
        };
    }

    private static bool IsValidDurableId(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed)
        && parsed > 0
        && parsed < 0x7FFFFFFF;

    private static bool IsValidOptionalDate(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
            out _
        );

    private static bool ParseOnOff(
        string? value,
        bool defaultValue,
        BuildState state,
        string partUri,
        int ordinal,
        string? subjectId
    )
    {
        if (value is null) return defaultValue;
        return value.ToLowerInvariant() switch
        {
            "true" or "1" or "on" => true,
            "false" or "0" or "off" => false,
            _ => InvalidOnOff(state, partUri, ordinal, subjectId, defaultValue),
        };
    }

    private static bool InvalidOnOff(
        BuildState state,
        string partUri,
        int ordinal,
        string? subjectId,
        bool defaultValue
    )
    {
        state.AddIssue(
            "REVIEW_ON_OFF_VALUE_INVALID",
            WordReviewIssueSeverity.Warning,
            "Review metadata contains an invalid on/off value; the schema default is used.",
            partUri,
            null,
            ordinal,
            subjectId
        );
        return defaultValue;
    }

    private static bool ElementOnOff(XElement root, string localName)
    {
        var element = root.Elements().FirstOrDefault(child => IsWordElement(child, localName));
        if (element is null) return false;
        var value = WordAttribute(element, "val");
        return value?.ToLowerInvariant() is not ("false" or "0" or "off");
    }

    private static int? ParseInteger(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static bool IsPermissionEditingGroup(string value) => value is
        "none"
        or "everyone"
        or "administrators"
        or "contributors"
        or "editors"
        or "owners"
        or "current";

    private static readonly IReadOnlySet<string> PermissionAttributeNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "id",
            "ed",
            "edGrp",
            "colFirst",
            "colLast",
            "displacedByCustomXml",
        };

    private static readonly IReadOnlySet<string> PermissionStartOnlyAttributeNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "ed",
            "edGrp",
            "colFirst",
            "colLast",
        };

    private static string? PermissionAttribute(XElement element, string localName) =>
        element.Attribute(element.Name.Namespace + localName)?.Value;

    private static void ValidatePermissionMarkerNamespace(
        string partUri,
        XElement element,
        XNamespace storyNamespace,
        StoryLocation location,
        int ordinal,
        BuildState state
    )
    {
        if (element.Name.Namespace == storyNamespace)
        {
            return;
        }
        state.AddIssue(
            "PERMISSION_MARKER_NAMESPACE_INVALID",
            WordReviewIssueSeverity.Error,
            "Permission-range markers must use the same WordprocessingML namespace as their containing story part.",
            partUri,
            location.StoryId,
            ordinal
        );
    }

    private static void ValidatePermissionAttributeNamespaces(
        string partUri,
        XElement element,
        StoryLocation location,
        int ordinal,
        BuildState state
    )
    {
        if (
            !element.Attributes().Any(attribute =>
                !attribute.IsNamespaceDeclaration
                && PermissionAttributeNames.Contains(attribute.Name.LocalName)
                && attribute.Name.Namespace != element.Name.Namespace
            )
        )
        {
            return;
        }
        state.AddIssue(
            "PERMISSION_ATTRIBUTE_NAMESPACE_INVALID",
            WordReviewIssueSeverity.Error,
            "Permission-range attributes must use the same WordprocessingML namespace as their marker element.",
            partUri,
            location.StoryId,
            ordinal
        );
    }

    private static void ValidatePermissionAttributePlacement(
        string partUri,
        XElement element,
        string localName,
        StoryLocation location,
        int ordinal,
        BuildState state
    )
    {
        if (
            localName != "permEnd"
            || !element.Attributes().Any(attribute =>
                !attribute.IsNamespaceDeclaration
                && attribute.Name.Namespace == element.Name.Namespace
                && PermissionStartOnlyAttributeNames.Contains(attribute.Name.LocalName)
            )
        )
        {
            return;
        }
        state.AddIssue(
            "PERMISSION_ATTRIBUTE_PLACEMENT_INVALID",
            WordReviewIssueSeverity.Error,
            "Permission end markers cannot carry editor or table-column attributes reserved for permission start markers.",
            partUri,
            location.StoryId,
            ordinal
        );
    }

    private static string? WordAttribute(XElement element, string localName) =>
        element.Attribute(element.Name.Namespace + localName)?.Value
        ?? element.Attributes().FirstOrDefault(attribute =>
            !attribute.IsNamespaceDeclaration
            && IsWordNamespace(attribute.Name.NamespaceName)
            && attribute.Name.LocalName == localName
        )?.Value;

    private static string? LocalAttribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute =>
            !attribute.IsNamespaceDeclaration && attribute.Name.LocalName == localName
        )?.Value;

    private static bool IsWordParagraph(XElement element) => IsWordElement(element, "p");

    private static bool IsWordNamespace(string namespaceName) =>
        namespaceName is WordTransitionalNamespace or WordStrictNamespace;

    private static bool IsWordElement(XElement element, string? localName = null) =>
        IsWordNamespace(element.Name.NamespaceName)
        && (localName is null || element.Name.LocalName == localName);

    private static bool IsMathElement(XElement element) =>
        element.Name.NamespaceName is MathTransitionalNamespace or MathStrictNamespace;

    private static bool IsTextElement(XElement element) =>
        IsWordElement(element)
        && element.Name.LocalName is "t" or "delText" or "instrText" or "delInstrText";

    private static string SourceName(XElement element) =>
        element.Name.NamespaceName == Word2010Namespace
            ? $"w14:{element.Name.LocalName}"
            : $"w:{element.Name.LocalName}";

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

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (
                char.IsUpper(character)
                && index > 0
                && (char.IsLower(value[index - 1])
                    || index + 1 < value.Length && char.IsLower(value[index + 1]))
            )
            {
                builder.Append('_');
            }
            builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }

    private sealed record StoryLocation(
        string StoryId,
        WordStoryKind Kind,
        WordSemanticNode? StoryNode,
        WordSemanticNode? NearestNode
    );

    private sealed record Marker(
        StoryLocation Location,
        string PartUri,
        string? OoxmlId,
        string Kind,
        int Ordinal,
        SemanticNodeId? NodeId,
        XElement Element
    );

    private sealed record TextCapture(string Text, int CharacterCount, bool Truncated)
    {
        internal static TextCapture Empty { get; } = new(string.Empty, 0, false);
    }

    private sealed class MutableComment
    {
        internal MutableComment(
            string id,
            string? ooxmlId,
            string partUri,
            int sourceElementOrdinal,
            SemanticNodeId? semanticNodeId,
            string? author,
            string? initials,
            string? date,
            string? dateUtc,
            TextCapture capture,
            IReadOnlyList<string> paragraphIds,
            string? lastParagraphId
        )
        {
            Id = id;
            OoxmlId = ooxmlId;
            PartUri = partUri;
            SourceElementOrdinal = sourceElementOrdinal;
            SemanticNodeId = semanticNodeId;
            Author = author;
            Initials = initials;
            Date = date;
            DateUtc = dateUtc;
            Capture = capture;
            ParagraphIds = paragraphIds;
            LastParagraphId = lastParagraphId;
            ThreadRootId = id;
        }

        internal string Id { get; }
        internal string? OoxmlId { get; }
        internal bool IsEffectiveByOoxmlId { get; set; } = true;
        internal string PartUri { get; }
        internal int SourceElementOrdinal { get; }
        internal SemanticNodeId? SemanticNodeId { get; }
        internal string? Author { get; }
        internal string? Initials { get; }
        internal string? Date { get; }
        internal string? DateUtc { get; }
        internal TextCapture Capture { get; }
        internal IReadOnlyList<string> ParagraphIds { get; }
        internal string? LastParagraphId { get; }
        internal List<string> AnchorIds { get; } = new();
        internal string? ParentParagraphId { get; set; }
        internal MutableComment? Parent { get; set; }
        internal string ThreadRootId { get; set; }
        internal int ThreadDepth { get; set; }
        internal bool ThreadResolved { get; set; }
        internal bool IsDone { get; set; }
        internal string? DurableId { get; set; }
        internal string? ExtensibleDateUtc { get; set; }
        internal bool IsIntelligentPlaceholder { get; set; }
        internal bool HasReactions { get; set; }
        internal int ExtensionCount { get; set; }
        internal bool HasExtensibleRecord { get; set; }
        internal string? PersonId { get; set; }

        internal WordCommentDefinition Freeze() => new(
            Id,
            OoxmlId,
            IsEffectiveByOoxmlId,
            PartUri,
            SourceElementOrdinal,
            SemanticNodeId,
            Author,
            Initials,
            Date,
            DateUtc,
            Capture.Text,
            Capture.CharacterCount,
            Capture.Truncated,
            ParagraphIds,
            LastParagraphId,
            AnchorIds,
            Parent?.Id,
            ThreadRootId,
            ThreadDepth,
            IsDone,
            DurableId,
            ExtensibleDateUtc,
            IsIntelligentPlaceholder,
            HasReactions,
            ExtensionCount,
            PersonId
        );
    }

    private sealed class MutablePerson
    {
        internal MutablePerson(
            string id,
            string partUri,
            int sourceElementOrdinal,
            string? author,
            string? providerId,
            string? userId
        )
        {
            Id = id;
            PartUri = partUri;
            SourceElementOrdinal = sourceElementOrdinal;
            Author = author;
            ProviderId = providerId;
            UserId = userId;
        }

        internal string Id { get; }
        internal string PartUri { get; }
        internal int SourceElementOrdinal { get; }
        internal string? Author { get; }
        internal string? ProviderId { get; }
        internal string? UserId { get; }
        internal int CommentCount { get; set; }
        internal int RevisionCount { get; set; }

        internal WordReviewPersonDefinition Freeze() => new(
            Id,
            PartUri,
            SourceElementOrdinal,
            Author,
            ProviderId,
            UserId,
            CommentCount,
            RevisionCount
        );
    }

    private sealed class MutableRevision
    {
        internal MutableRevision(
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
            TextCapture capture,
            int contentElementCount,
            bool containsMath,
            bool isInDeletedContent
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
            Capture = capture;
            ContentElementCount = contentElementCount;
            ContainsMath = containsMath;
            IsInDeletedContent = isInDeletedContent;
        }

        internal string Id { get; }
        internal WordRevisionKind Kind { get; }
        internal WordRevisionStatus Status { get; }
        internal string SourceName { get; }
        internal string StoryId { get; }
        internal WordStoryKind StoryKind { get; }
        internal SemanticNodeId? StoryNodeId { get; }
        internal SemanticNodeId? ParagraphNodeId { get; }
        internal string PartUri { get; }
        internal int SourceElementOrdinal { get; }
        internal SemanticNodeId? SemanticNodeId { get; }
        internal string? ParentRevisionId { get; }
        internal string? OoxmlId { get; }
        internal string? Author { get; }
        internal string? Date { get; }
        internal string? DateUtc { get; }
        internal TextCapture Capture { get; }
        internal int ContentElementCount { get; }
        internal bool ContainsMath { get; }
        internal bool IsInDeletedContent { get; }
        internal string? PersonId { get; set; }

        internal WordRevisionDefinition Freeze() => new(
            Id,
            Kind,
            Status,
            SourceName,
            StoryId,
            StoryKind,
            StoryNodeId,
            ParagraphNodeId,
            PartUri,
            SourceElementOrdinal,
            SemanticNodeId,
            ParentRevisionId,
            OoxmlId,
            Author,
            Date,
            DateUtc,
            Capture.Text,
            Capture.CharacterCount,
            Capture.Truncated,
            ContentElementCount,
            ContainsMath,
            IsInDeletedContent,
            PersonId
        );
    }

    private sealed class BuildState
    {
        private readonly Dictionary<(string PartUri, int Ordinal), WordSemanticNode>
            _semanticBySource;

        internal BuildState(
            WordReviewGraphOptions options,
            WordSemanticDocument semanticDocument
        )
        {
            Options = options;
            SemanticDocument = semanticDocument;
            _semanticBySource = semanticDocument.Nodes.ToDictionary(node =>
                (node.SourcePartUri, node.SourceElementOrdinal)
            );
        }

        internal WordReviewGraphOptions Options { get; }
        internal WordSemanticDocument SemanticDocument { get; }
        internal Dictionary<string, LosslessXmlDocument> Sources { get; } =
            new(StringComparer.Ordinal);
        internal List<MutableComment> Comments { get; } = new();
        internal Dictionary<string, MutableComment> EffectiveCommentsByOoxmlId { get; } =
            new(StringComparer.Ordinal);
        internal Dictionary<string, MutableComment> CommentsByLastParagraphId { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, MutableComment> CommentsByDurableId { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        internal List<Marker> CommentMarkers { get; } = new();
        internal List<WordCommentAnchor> Anchors { get; } = new();
        internal List<MutablePerson> People { get; } = new();
        internal Dictionary<string, MutablePerson> PeopleByAuthor { get; } =
            new(StringComparer.Ordinal);
        internal bool PeoplePartPresent { get; set; }
        internal List<MutableRevision> Revisions { get; } = new();
        internal List<Marker> MoveMarkers { get; } = new();
        internal List<WordMoveRangeDefinition> MoveRanges { get; } = new();
        internal List<WordMovePairDefinition> Moves { get; } = new();
        internal List<Marker> PermissionMarkers { get; } = new();
        internal List<WordPermissionRangeDefinition> Permissions { get; } = new();
        internal WordReviewSettingsDefinition? Settings { get; set; }
        internal List<WordReviewIssue> Issues { get; } = new();
        internal bool IssuesTruncated { get; private set; }
        internal long TotalTextCharacters { get; set; }

        internal LosslessXmlDocument SourceFor(
            OpcPackageSnapshot package,
            string partUri,
            WordReviewGraphBuilder owner,
            CancellationToken cancellationToken
        )
        {
            if (Sources.TryGetValue(partUri, out var source)) return source;
            if (!package.Parts.TryGetValue(partUri, out var part))
            {
                throw new WordReviewProjectionException(
                    $"Review part '{partUri}' is missing from the package."
                );
            }
            source = owner.ParsePart(part, cancellationToken);
            Sources.Add(partUri, source);
            return source;
        }

        internal WordSemanticNode? NodeFor(string partUri, int ordinal) =>
            _semanticBySource.TryGetValue((partUri, ordinal), out var node) ? node : null;

        internal void AddIssue(
            string code,
            WordReviewIssueSeverity severity,
            string message,
            string? partUri = null,
            string? storyId = null,
            int? sourceElementOrdinal = null,
            string? subjectId = null
        )
        {
            if (Issues.Count >= Options.MaxIssues)
            {
                IssuesTruncated = true;
                return;
            }
            Issues.Add(
                new WordReviewIssue(
                    code,
                    severity,
                    message,
                    partUri,
                    storyId,
                    sourceElementOrdinal,
                    subjectId
                )
            );
        }
    }

    private sealed class StoryRevisionIdComparer
        : IEqualityComparer<(string StoryId, string Id)>
    {
        public bool Equals(
            (string StoryId, string Id) x,
            (string StoryId, string Id) y
        ) => string.Equals(x.StoryId, y.StoryId, StringComparison.Ordinal)
            && string.Equals(x.Id, y.Id, StringComparison.Ordinal);

        public int GetHashCode((string StoryId, string Id) value) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(value.StoryId),
                StringComparer.Ordinal.GetHashCode(value.Id)
            );
    }
}
