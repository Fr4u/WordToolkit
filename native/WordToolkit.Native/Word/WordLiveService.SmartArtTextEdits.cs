using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private const int SmartArtEditRootNodeLimit = 128;
    private const int SmartArtEditReturnedNodeLimit = 32;
    private const int SmartArtEditNodeTextLimit = 16_384;
    private const int SmartArtEditReplacementLimit = 4_096;
    private const int SmartArtEditTotalTextLimit = 65_536;

    private async Task<object> PrepareSmartArtTextEditsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var locator = ParseSmartArtLocator(arguments);
        var nodeOffset = (int)(arguments.NullableInt64("node_offset") ?? 0);
        var maxNodes = (int)(arguments.NullableInt64("max_nodes") ?? 16);
        var includeText = arguments.Boolean("include_text", false);
        var maxTextChars = (int)(arguments.NullableInt64("max_text_chars") ?? 160);

        if (nodeOffset is < 0 or > 127)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "node_offset must be between 0 and 127"
            );
        }
        if (maxNodes is < 1 or > SmartArtEditReturnedNodeLimit)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"max_nodes must be between 1 and {SmartArtEditReturnedNodeLimit}"
            );
        }
        if (maxTextChars is < 1 or > 512)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_text_chars must be between 1 and 512"
            );
        }

        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                dynamic document = ResolveDocument(application, record);
                dynamic root = ResolveSmartArtRoot(document, locator);
                var snapshot = CaptureSmartArtSnapshot((object)root, locator);
                var returned = new List<object>();
                var endExclusive = Math.Min(snapshot.Nodes.Count, nodeOffset + maxNodes);
                for (var offset = nodeOffset; offset < endExclusive; offset++)
                {
                    var node = snapshot.Nodes[offset];
                    string? token = null;
                    if (node.Editable)
                    {
                        token = Convert
                            .ToHexString(RandomNumberGenerator.GetBytes(32))
                            .ToLowerInvariant();
                        _smartArtTextEditGrants[token] = new SmartArtTextEditGrant(
                            token,
                            record.Id,
                            record.Version,
                            locator.StoryType,
                            locator.StoryLinkIndex,
                            locator.CollectionKind,
                            locator.SourceIndex,
                            node.Index,
                            snapshot.StructureFingerprint,
                            snapshot.ContextFingerprint,
                            node.TextHash,
                            snapshot.Nodes.Select(item => item.TextHash).ToArray()
                        );
                    }

                    var payload = new Dictionary<string, object?>
                    {
                        ["node_index"] = node.Index,
                        ["level"] = node.Level,
                        ["hidden"] = node.Hidden,
                        ["native_type"] = node.NativeType,
                        ["child_count"] = node.ChildCount,
                        ["text_length"] = node.Text.Length,
                        ["text_fingerprint"] = SmartArtTextFingerprint(
                            record.Id,
                            locator,
                            node.Index,
                            node.TextHash
                        ),
                        ["editable"] = node.Editable,
                        ["smartart_node_token"] = token,
                        ["edit_rejection_reason"] = node.Editable
                            ? null
                            : SmartArtEditRejectionReason(node.Text),
                    };
                    if (includeText)
                    {
                        payload["text"] = node.Text[..Math.Min(node.Text.Length, maxTextChars)];
                        payload["text_truncated"] = node.Text.Length > maxTextChars;
                    }
                    returned.Add(payload);
                }
                TrimSmartArtTextEditGrants();
                var nextOffset = endExclusive < snapshot.Nodes.Count
                    ? endExclusive
                    : (int?)null;
                return new
                {
                    operation_contract = "wordtoolkit.prepare_live_word_smartart_text_edits/1.0",
                    live_document_id = record.Id,
                    live_version = record.Version,
                    locator = new
                    {
                        story_type = locator.StoryType,
                        story_link_index = locator.StoryLinkIndex,
                        collection_kind = locator.CollectionKind,
                        source_index = locator.SourceIndex,
                    },
                    root = new
                    {
                        total_node_count = snapshot.Nodes.Count,
                        structure_fingerprint = snapshot.StructureFingerprint,
                        context_fingerprint = snapshot.ContextFingerprint,
                    },
                    page = new
                    {
                        node_offset = nodeOffset,
                        max_nodes = maxNodes,
                        returned_count = returned.Count,
                        next_node_offset = nextOffset,
                        response_truncated = nextOffset is not null,
                    },
                    nodes = returned,
                    disclosure = new
                    {
                        sensitive_text_read_for_guarding = true,
                        sensitive_text_returned = includeText,
                        raw_xml_returned = false,
                        raw_com_objects_returned = false,
                        document_content_is_untrusted = true,
                    },
                    document = DocumentInfo(application, document),
                    performance = Performance(started),
                };
            },
            WordComReplaySafety.ReplaySafe,
            cancellationToken
        );
    }

    private async Task<object> ApplySmartArtTextEditsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var expectedVersion = arguments.NullableInt64("expected_version")
            ?? throw new NativeToolException(
                "INVALID_INPUT",
                "expected_version is required for guarded SmartArt text edits"
            );
        var editNodes = arguments.RequiredArray("edits");
        if (editNodes.GetArrayLength() is < 1 or > SmartArtEditReturnedNodeLimit)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                $"edits must contain between 1 and {SmartArtEditReturnedNodeLimit} items"
            );
        }
        var repaginate = arguments.Boolean("repaginate", true);
        var optimizeScreenUpdates = arguments.Boolean("optimize_screen_updates", true);
        CheckVersion(record, expectedVersion);

        var edits = new List<PreparedSmartArtTextEdit>();
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in editNodes.EnumerateArray())
        {
            var token = node.String("smartart_node_token");
            var replacementText = node.String("replacement_text");
            if (token.Length == 0 || token.Length > 128)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "smartart_node_token must be a non-empty bounded token"
                );
            }
            if (!tokens.Add(token))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "A SmartArt text edit batch cannot reuse the same token"
                );
            }
            ValidateSmartArtReplacement(replacementText);
            if (!_smartArtTextEditGrants.TryGetValue(token, out var grant))
            {
                throw FreshSmartArtTokenRequired();
            }
            edits.Add(new PreparedSmartArtTextEdit(grant, replacementText));
        }

        var first = edits[0].Grant;
        if (
            edits.Any(edit =>
                edit.Grant.DocumentId != record.Id
                || edit.Grant.Version != expectedVersion
                || !SameSmartArtRoot(edit.Grant, first)
                || edit.Grant.RootStructureFingerprint != first.RootStructureFingerprint
                || edit.Grant.RootContextFingerprint != first.RootContextFingerprint
            )
        )
        {
            throw FreshSmartArtTokenRequired();
        }
        if (edits.Select(edit => edit.Grant.NodeIndex).Distinct().Count() != edits.Count)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "A SmartArt text edit batch cannot target the same node twice"
            );
        }

        foreach (var token in tokens)
        {
            _smartArtTextEditGrants.TryRemove(token, out _);
        }

        var locator = new SmartArtLocator(
            first.StoryType,
            first.StoryLinkIndex,
            first.CollectionKind,
            first.SourceIndex
        );
        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                dynamic root = ResolveSmartArtRoot(document, locator);
                var before = CaptureSmartArtSnapshot((object)root, locator);
                if (
                    before.StructureFingerprint != first.RootStructureFingerprint
                    || before.ContextFingerprint != first.RootContextFingerprint
                    || before.Nodes.Count != first.BaselineNodeTextHashes.Count
                )
                {
                    throw FreshSmartArtTokenRequired();
                }
                for (var index = 0; index < before.Nodes.Count; index++)
                {
                    if (before.Nodes[index].TextHash != first.BaselineNodeTextHashes[index])
                    {
                        throw FreshSmartArtTokenRequired();
                    }
                }

                var changedEdits = edits
                    .Where(edit =>
                        before.Nodes[edit.Grant.NodeIndex - 1].Text != edit.ReplacementText
                    )
                    .ToArray();
                if (changedEdits.Length == 0)
                {
                    return SmartArtApplyResult(
                        (object)application,
                        (object)document,
                        record,
                        locator,
                        before,
                        before,
                        edits,
                        changedCount: 0,
                        repaginateRequested: repaginate,
                        repaginationPerformed: false,
                        started
                    );
                }

                bool? originalScreenUpdating = null;
                dynamic? undoRecord = null;
                var undoStarted = false;
                try
                {
                    if (optimizeScreenUpdates)
                    {
                        originalScreenUpdating = (bool)application.ScreenUpdating;
                        application.ScreenUpdating = false;
                    }
                    undoRecord = application.UndoRecord;
                    undoRecord.StartCustomRecord("WordToolkit: edit SmartArt text");
                    undoStarted = true;
                    foreach (var edit in changedEdits)
                    {
                        dynamic node = root.SmartArt.AllNodes.Item(edit.Grant.NodeIndex);
                        node.TextFrame2.TextRange.Text = edit.ReplacementText;
                    }

                    var after = CaptureSmartArtSnapshot((object)root, locator);
                    VerifySmartArtMutation(before, after, edits);
                    var repaginationPerformed = false;
                    if (repaginate)
                    {
                        document.Repaginate();
                        repaginationPerformed = true;
                    }
                    undoRecord.EndCustomRecord();
                    undoStarted = false;
                    record.Version++;
                    InvalidateSelectionGrants(record.Id);
                    InvalidateRangeGrants(record.Id);
                    InvalidateUndoGrants(record.Id);
                    return SmartArtApplyResult(
                        (object)application,
                        (object)document,
                        record,
                        locator,
                        before,
                        after,
                        edits,
                        changedEdits.Length,
                        repaginate,
                        repaginationPerformed,
                        started
                    );
                }
                catch
                {
                    Rollback(document, undoRecord, ref undoStarted);
                    throw;
                }
                finally
                {
                    if (originalScreenUpdating is not null)
                    {
                        application.ScreenUpdating = originalScreenUpdating.Value;
                    }
                }
            },
            WordComReplaySafety.NonReplayable,
            cancellationToken
        );
    }

    private static SmartArtLocator ParseSmartArtLocator(JsonElement arguments)
    {
        var storyType = arguments.String("story_type");
        var storyLinkIndex = (int)(arguments.NullableInt64("story_link_index") ?? 0);
        var collectionKind = arguments.String("collection_kind");
        var sourceIndex = (int)(
            arguments.NullableInt64("source_index")
            ?? throw new NativeToolException("INVALID_INPUT", "source_index is required")
        );
        if (!WordStoryTypes.Values.Contains(storyType, StringComparer.Ordinal))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "story_type must identify one exact Word story"
            );
        }
        if (storyLinkIndex is < 0 or >= DrawingStoryLinkLimit)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"story_link_index must be between 0 and {DrawingStoryLinkLimit - 1}"
            );
        }
        if (collectionKind is not ("floating" or "inline"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "collection_kind must be floating or inline"
            );
        }
        if (sourceIndex is < 1 or > DrawingRootScanLimit)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"source_index must be between 1 and {DrawingRootScanLimit}"
            );
        }
        if (storyType == "main_text" && storyLinkIndex != 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "main_text requires story_link_index 0"
            );
        }
        return new SmartArtLocator(storyType, storyLinkIndex, collectionKind, sourceIndex);
    }

    private static dynamic ResolveSmartArtRoot(dynamic document, SmartArtLocator locator)
    {
        dynamic collection;
        if (locator.StoryType == "main_text")
        {
            collection = locator.CollectionKind == "floating"
                ? document.Shapes
                : document.InlineShapes;
        }
        else
        {
            var storyCode = WordStoryTypes.Single(pair => pair.Value == locator.StoryType).Key;
            dynamic range;
            try
            {
                range = document.StoryRanges.Item(storyCode);
                for (var index = 0; index < locator.StoryLinkIndex; index++)
                {
                    range = range.NextStoryRange
                        ?? throw new NativeToolException(
                            "DOCUMENT_NOT_FOUND",
                            "The requested Word story link no longer exists"
                        );
                }
            }
            catch (NativeToolException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new NativeToolException(
                    "DOCUMENT_NOT_FOUND",
                    "The requested Word story no longer exists",
                    new { exception = exception.GetType().Name }
                );
            }
            collection = locator.CollectionKind == "floating"
                ? range.ShapeRange
                : range.InlineShapes;
        }

        dynamic root;
        try
        {
            var count = Math.Max(0, (int)collection.Count);
            if (locator.SourceIndex > count)
            {
                throw new NativeToolException(
                    "DOCUMENT_NOT_FOUND",
                    "The requested drawing root no longer exists"
                );
            }
            root = collection.Item(locator.SourceIndex);
        }
        catch (NativeToolException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new NativeToolException(
                "DOCUMENT_NOT_FOUND",
                "The requested drawing root could not be resolved",
                new { exception = exception.GetType().Name }
            );
        }

        try
        {
            dynamic smartArt = root.SmartArt;
            _ = (int)smartArt.AllNodes.Count;
        }
        catch (Exception exception)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "The requested drawing root is not an editable SmartArt object",
                new { exception = exception.GetType().Name }
            );
        }
        return root;
    }

    private static SmartArtSnapshot CaptureSmartArtSnapshot(
        object rootObject,
        SmartArtLocator locator
    )
    {
        dynamic root = rootObject;
        dynamic smartArt = root.SmartArt;
        dynamic allNodes = smartArt.AllNodes;
        var count = Math.Max(0, (int)allNodes.Count);
        if (count > SmartArtEditRootNodeLimit)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                $"SmartArt contains {count} nodes; guarded editing supports at most {SmartArtEditRootNodeLimit}"
            );
        }
        var nodes = new List<SmartArtNodeSnapshot>(count);
        var totalText = 0;
        for (var index = 1; index <= count; index++)
        {
            dynamic node = allNodes.Item(index);
            var text = Convert.ToString(
                    node.TextFrame2.TextRange.Text,
                    CultureInfo.InvariantCulture
                )
                ?? "";
            if (text.Length > SmartArtEditNodeTextLimit)
            {
                throw new NativeToolException(
                    "LIMIT_EXCEEDED",
                    $"SmartArt node {index} exceeds the {SmartArtEditNodeTextLimit}-character snapshot limit"
                );
            }
            totalText = checked(totalText + text.Length);
            if (totalText > SmartArtEditTotalTextLimit)
            {
                throw new NativeToolException(
                    "LIMIT_EXCEEDED",
                    $"SmartArt text exceeds the {SmartArtEditTotalTextLimit}-character root snapshot limit"
                );
            }
            var level = (int)node.Level;
            var hidden = (int)node.Hidden;
            var nativeType = (int)node.Type;
            var childCount = Math.Max(0, (int)node.Nodes.Count);
            nodes.Add(
                new SmartArtNodeSnapshot(
                    index,
                    level,
                    hidden,
                    nativeType,
                    childCount,
                    text,
                    HashSmartArtText(text),
                    SmartArtTextIsEditable(text)
                )
            );
        }

        string layoutId;
        string quickStyleId;
        string colorId;
        int rootRangeStart;
        int rootRangeEnd;
        int rootStoryType;
        int rootNativeId;
        try
        {
            layoutId = Convert.ToString(smartArt.Layout.Id, CultureInfo.InvariantCulture) ?? "";
            quickStyleId = Convert.ToString(smartArt.QuickStyle.Id, CultureInfo.InvariantCulture) ?? "";
            colorId = Convert.ToString(smartArt.Color.Id, CultureInfo.InvariantCulture) ?? "";
            dynamic rootRange = locator.CollectionKind == "floating" ? root.Anchor : root.Range;
            rootRangeStart = (int)rootRange.Start;
            rootRangeEnd = (int)rootRange.End;
            rootStoryType = (int)rootRange.StoryType;
            rootNativeId = locator.CollectionKind == "floating" ? (int)root.ID : 0;
        }
        catch (Exception exception)
        {
            throw new NativeToolException(
                "VALIDATION_FAILED",
                "Word did not expose the complete SmartArt root identity required for guarded editing",
                new { exception = exception.GetType().Name }
            );
        }

        var structurePayload = new StringBuilder()
            .Append(locator.StoryType).Append('\0')
            .Append(locator.StoryLinkIndex).Append('\0')
            .Append(locator.CollectionKind).Append('\0')
            .Append(locator.SourceIndex).Append('\0')
            .Append(rootNativeId).Append('\0')
            .Append(rootStoryType).Append(':')
            .Append(rootRangeStart).Append(':')
            .Append(rootRangeEnd).Append('\0')
            .Append(layoutId).Append('\0')
            .Append(quickStyleId).Append('\0')
            .Append(colorId).Append('\0')
            .Append(count);
        foreach (var node in nodes)
        {
            structurePayload.Append('\0')
                .Append(node.Index).Append(':')
                .Append(node.Level).Append(':')
                .Append(node.Hidden).Append(':')
                .Append(node.NativeType).Append(':')
                .Append(node.ChildCount);
        }
        var structureFingerprint = HashSmartArtText(structurePayload.ToString());
        var contextPayload = new StringBuilder(structureFingerprint);
        foreach (var node in nodes)
        {
            contextPayload.Append('\0').Append(node.TextHash);
        }
        return new SmartArtSnapshot(
            nodes,
            structureFingerprint,
            HashSmartArtText(contextPayload.ToString())
        );
    }

    private static void VerifySmartArtMutation(
        SmartArtSnapshot before,
        SmartArtSnapshot after,
        IReadOnlyList<PreparedSmartArtTextEdit> edits
    )
    {
        if (
            after.StructureFingerprint != before.StructureFingerprint
            || after.Nodes.Count != before.Nodes.Count
        )
        {
            throw new NativeToolException(
                "VALIDATION_FAILED",
                "Word changed the SmartArt node structure while editing text; the operation was rolled back"
            );
        }
        var replacements = edits.ToDictionary(
            edit => edit.Grant.NodeIndex,
            edit => edit.ReplacementText
        );
        for (var index = 0; index < before.Nodes.Count; index++)
        {
            var nodeIndex = index + 1;
            if (replacements.TryGetValue(nodeIndex, out var expected))
            {
                if (!string.Equals(after.Nodes[index].Text, expected, StringComparison.Ordinal))
                {
                    throw new NativeToolException(
                        "VALIDATION_FAILED",
                        $"Word did not preserve the exact requested text for SmartArt node {nodeIndex}; the operation was rolled back"
                    );
                }
            }
            else if (after.Nodes[index].TextHash != before.Nodes[index].TextHash)
            {
                throw new NativeToolException(
                    "VALIDATION_FAILED",
                    $"Word changed untargeted SmartArt node {nodeIndex}; the operation was rolled back"
                );
            }
        }
    }

    private object SmartArtApplyResult(
        object applicationObject,
        object documentObject,
        LiveDocumentRecord record,
        SmartArtLocator locator,
        SmartArtSnapshot before,
        SmartArtSnapshot after,
        IReadOnlyList<PreparedSmartArtTextEdit> edits,
        int changedCount,
        bool repaginateRequested,
        bool repaginationPerformed,
        long started
    )
    {
        dynamic application = applicationObject;
        dynamic document = documentObject;
        return new
        {
            operation_contract = "wordtoolkit.apply_live_word_smartart_text_edits/1.0",
            live_document_id = record.Id,
            live_version = record.Version,
            mutated = changedCount > 0,
            changed_count = changedCount,
            unchanged_count = edits.Count - changedCount,
            repagination = new
            {
                requested = repaginateRequested,
                performed = repaginationPerformed,
            },
            locator = new
            {
                story_type = locator.StoryType,
                story_link_index = locator.StoryLinkIndex,
                collection_kind = locator.CollectionKind,
                source_index = locator.SourceIndex,
            },
            before = new
            {
                structure_fingerprint = before.StructureFingerprint,
                context_fingerprint = before.ContextFingerprint,
            },
            after = new
            {
                structure_fingerprint = after.StructureFingerprint,
                context_fingerprint = after.ContextFingerprint,
            },
            edits = edits.Select(edit =>
            {
                var beforeNode = before.Nodes[edit.Grant.NodeIndex - 1];
                var afterNode = after.Nodes[edit.Grant.NodeIndex - 1];
                return new
                {
                    node_index = edit.Grant.NodeIndex,
                    before_text_length = beforeNode.Text.Length,
                    after_text_length = afterNode.Text.Length,
                    before_text_fingerprint = SmartArtTextFingerprint(
                        record.Id,
                        locator,
                        edit.Grant.NodeIndex,
                        beforeNode.TextHash
                    ),
                    after_text_fingerprint = SmartArtTextFingerprint(
                        record.Id,
                        locator,
                        edit.Grant.NodeIndex,
                        afterNode.TextHash
                    ),
                    changed = beforeNode.TextHash != afterNode.TextHash,
                };
            }),
            raw_xml_returned = false,
            raw_com_objects_returned = false,
            document = DocumentInfo(application, document),
            performance = Performance(started),
        };
    }

    private string SmartArtTextFingerprint(
        string documentId,
        SmartArtLocator locator,
        int nodeIndex,
        string textHash
    )
    {
        using var hmac = new HMACSHA256(_smartArtFingerprintKey);
        var payload = string.Join(
            '\0',
            documentId,
            locator.StoryType,
            locator.StoryLinkIndex.ToString(CultureInfo.InvariantCulture),
            locator.CollectionKind,
            locator.SourceIndex.ToString(CultureInfo.InvariantCulture),
            nodeIndex.ToString(CultureInfo.InvariantCulture),
            textHash
        );
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }

    private static string HashSmartArtText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();

    private static bool SmartArtTextIsEditable(string text) =>
        text.Length <= SmartArtEditReplacementLimit
        && text.IndexOfAny(['\r', '\n', '\0']) < 0;

    private static string? SmartArtEditRejectionReason(string text)
    {
        if (text.Length > SmartArtEditReplacementLimit)
        {
            return "existing_text_exceeds_edit_limit";
        }
        return text.IndexOfAny(['\r', '\n', '\0']) >= 0
            ? "existing_text_is_not_single_line"
            : null;
    }

    private static void ValidateSmartArtReplacement(string replacementText)
    {
        if (replacementText.Length > SmartArtEditReplacementLimit)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                $"replacement_text exceeds {SmartArtEditReplacementLimit} characters"
            );
        }
        if (replacementText.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "replacement_text must be a single line without NUL characters"
            );
        }
    }

    private static bool SameSmartArtRoot(
        SmartArtTextEditGrant left,
        SmartArtTextEditGrant right
    ) =>
        left.StoryType == right.StoryType
        && left.StoryLinkIndex == right.StoryLinkIndex
        && left.CollectionKind == right.CollectionKind
        && left.SourceIndex == right.SourceIndex;

    private static NativeToolException FreshSmartArtTokenRequired() =>
        new(
            "VERSION_CONFLICT",
            "Fresh SmartArt node tokens are required because the document, diagram, or prepared context changed",
            retryable: true
        );

    private void TrimSmartArtTextEditGrants()
    {
        if (_smartArtTextEditGrants.Count <= 2_048)
        {
            return;
        }
        foreach (
            var key in _smartArtTextEditGrants.Keys.Take(
                _smartArtTextEditGrants.Count - 1_024
            )
        )
        {
            _smartArtTextEditGrants.TryRemove(key, out _);
        }
    }

    private sealed record SmartArtLocator(
        string StoryType,
        int StoryLinkIndex,
        string CollectionKind,
        int SourceIndex
    );

    private sealed record SmartArtNodeSnapshot(
        int Index,
        int Level,
        int Hidden,
        int NativeType,
        int ChildCount,
        string Text,
        string TextHash,
        bool Editable
    );

    private sealed record SmartArtSnapshot(
        IReadOnlyList<SmartArtNodeSnapshot> Nodes,
        string StructureFingerprint,
        string ContextFingerprint
    );

    private sealed record PreparedSmartArtTextEdit(
        SmartArtTextEditGrant Grant,
        string ReplacementText
    );
}
