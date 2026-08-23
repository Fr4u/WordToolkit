using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private const int SmartArtLayoutScanLimit = 2_048;
    private const int SmartArtLayoutPageLimit = 100;
    private const int SmartArtLayoutIdLimit = 512;
    private const int SmartArtLayoutNameLimit = 256;
    private const int SmartArtLayoutDescriptionLimit = 1_024;

    private async Task<object> InspectSmartArtLayoutsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var offset = (int)(arguments.NullableInt64("offset") ?? 0);
        var limit = (int)(arguments.NullableInt64("limit") ?? 50);
        if (offset is < 0 or > SmartArtLayoutScanLimit)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"offset must be between 0 and {SmartArtLayoutScanLimit}"
            );
        }
        if (limit is < 1 or > SmartArtLayoutPageLimit)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"limit must be between 1 and {SmartArtLayoutPageLimit}"
            );
        }

        var includeDescription = arguments.Boolean("include_description", false);
        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                object? layoutsObject = null;
                try
                {
                    dynamic document = ResolveDocument(application, record);
                    layoutsObject = application.SmartArtLayouts;
                    if (layoutsObject is null)
                    {
                        throw SmartArtCatalogUnavailable();
                    }

                    dynamic layouts = layoutsObject;
                    int reportedCount;
                    try
                    {
                        reportedCount = Convert.ToInt32(
                            layouts.Count,
                            CultureInfo.InvariantCulture
                        );
                    }
                    catch
                    {
                        throw SmartArtCatalogUnavailable();
                    }
                    if (reportedCount < 0)
                    {
                        throw SmartArtCatalogUnavailable();
                    }

                    var total = Math.Min(reportedCount, SmartArtLayoutScanLimit);
                    var catalogTruncated = reportedCount > SmartArtLayoutScanLimit;
                    var first = Math.Min(offset, total) + 1;
                    var last = Math.Min(total, offset + limit);
                    var items = new List<object>(Math.Max(0, last - first + 1));
                    for (var index = first; index <= last; index++)
                    {
                        object? layoutObject = null;
                        try
                        {
                            try
                            {
                                layoutObject = layouts.Item(index);
                            }
                            catch
                            {
                                items.Add(
                                    SmartArtLayoutItem(
                                        index,
                                        id: null,
                                        token: null,
                                        name: null,
                                        category: null,
                                        description: null,
                                        descriptionReturned: false,
                                        metadataTruncated: false,
                                        issueCode: "LAYOUT_ID_UNAVAILABLE"
                                    )
                                );
                                continue;
                            }

                            dynamic layout = layoutObject;
                            if (
                                !TryReadRequiredSmartArtLayoutId(
                                    () => layout.Id,
                                    out var id
                                )
                            )
                            {
                                items.Add(
                                    SmartArtLayoutItem(
                                        index,
                                        id: null,
                                        token: null,
                                        name: null,
                                        category: null,
                                        description: null,
                                        descriptionReturned: false,
                                        metadataTruncated: false,
                                        issueCode: "LAYOUT_ID_UNAVAILABLE"
                                    )
                                );
                                continue;
                            }

                            var metadataAvailable = true;
                            var metadataTruncated = false;
                            var name = ReadOptionalSmartArtLayoutText(
                                () => layout.Name,
                                SmartArtLayoutNameLimit,
                                ref metadataAvailable,
                                ref metadataTruncated
                            );
                            var category = ReadOptionalSmartArtLayoutText(
                                () => layout.Category,
                                SmartArtLayoutNameLimit,
                                ref metadataAvailable,
                                ref metadataTruncated
                            );
                            string? description = null;
                            if (includeDescription)
                            {
                                description = ReadOptionalSmartArtLayoutText(
                                    () => layout.Description,
                                    SmartArtLayoutDescriptionLimit,
                                    ref metadataAvailable,
                                    ref metadataTruncated
                                );
                            }

                            var token = Convert.ToHexString(
                                    RandomNumberGenerator.GetBytes(32)
                                )
                                .ToLowerInvariant();
                            _smartArtLayoutGrants[token] = new SmartArtLayoutGrant(
                                token,
                                record.Id,
                                record.Version,
                                index,
                                id
                            );
                            items.Add(
                                SmartArtLayoutItem(
                                    index,
                                    id,
                                    token,
                                    name,
                                    category,
                                    description,
                                    includeDescription && description is not null,
                                    metadataTruncated,
                                    metadataAvailable
                                        ? null
                                        : "LAYOUT_METADATA_UNAVAILABLE"
                                )
                            );
                        }
                        finally
                        {
                            FinalReleaseBatchComObject(layoutObject);
                        }
                    }

                    TrimSmartArtLayoutGrants();
                    var nextOffset = offset + items.Count < total
                        ? offset + items.Count
                        : (int?)null;
                    return new
                    {
                        operation_contract =
                            "wordtoolkit.inspect_live_word_smartart_layouts/1.0",
                        live_document_id = record.Id,
                        live_version = record.Version,
                        total_count = total,
                        scan_limit = SmartArtLayoutScanLimit,
                        catalog_truncated = catalogTruncated,
                        offset,
                        limit,
                        returned_count = items.Count,
                        truncated = nextOffset is not null || catalogTruncated,
                        next_offset = nextOffset,
                        layouts = items,
                        identity_scope =
                            "connected_word_process_and_live_document_version",
                        raw_com_objects_returned = false,
                        raw_xml_returned = false,
                        document = DocumentInfo(application, document),
                        performance = Performance(started),
                    };
                }
                finally
                {
                    FinalReleaseBatchComObject(layoutsObject);
                }
            },
            WordComReplaySafety.ReplaySafe,
            cancellationToken
        );
    }

    private async Task<object> InsertSmartArtAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var expectedVersion = arguments.NullableInt64("expected_version")
            ?? throw new NativeToolException(
                "INVALID_INPUT",
                "expected_version is required for SmartArt insertion"
            );
        var layoutToken = arguments.String("smartart_layout_token");
        if (
            layoutToken.Length != 64
            || layoutToken.Any(
                character =>
                    character is not (>= '0' and <= '9')
                    && character is not (>= 'a' and <= 'f')
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "smartart_layout_token must be 64 lowercase hexadecimal characters"
            );
        }

        var selectionToken = arguments.String("selection_token");
        var rangeToken = arguments.String("range_token");
        if ((selectionToken.Length == 0) == (rangeToken.Length == 0))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Provide exactly one fresh selection_token or range_token"
            );
        }
        if (selectionToken.Length > 128 || rangeToken.Length > 128)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "selection_token and range_token are bounded to 128 characters"
            );
        }

        var optimizeScreenUpdates = arguments.Boolean("optimize_screen_updates", true);
        CheckVersion(record, expectedVersion);
        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);

                object? targetObject = null;
                object? layoutsObject = null;
                object? layoutObject = null;
                object? insertedShapeObject = null;
                object? insertedRangeObject = null;
                object? smartArtObject = null;
                object? insertedLayoutObject = null;
                object? undoRecordObject = null;
                bool? originalScreenUpdating = null;
                var undoStarted = false;
                try
                {
                    targetObject = selectionToken.Length > 0
                        ? ResolveVerifiedSelectionRange(
                            (object)application,
                            (object)document,
                            record,
                            selectionToken,
                            requireNonEmpty: false
                        )
                        : ResolveVerifiedRange(
                            (object)document,
                            record,
                            rangeToken,
                            requireNonEmpty: false
                        );
                    dynamic target = targetObject;
                    var sourceStart = Convert.ToInt32(
                        target.Start,
                        CultureInfo.InvariantCulture
                    );
                    var sourceEnd = Convert.ToInt32(
                        target.End,
                        CultureInfo.InvariantCulture
                    );

                    if (
                        !_smartArtLayoutGrants.TryGetValue(
                            layoutToken,
                            out var grant
                        )
                        || grant.DocumentId != record.Id
                        || grant.Version != record.Version
                    )
                    {
                        throw FreshSmartArtLayoutTokenRequired();
                    }

                    try
                    {
                        layoutsObject = application.SmartArtLayouts;
                        if (layoutsObject is null)
                        {
                            throw FreshSmartArtLayoutTokenRequired();
                        }
                        dynamic layouts = layoutsObject;
                        layoutObject = layouts.Item(grant.LayoutIndex);
                    }
                    catch (NativeToolException)
                    {
                        throw;
                    }
                    catch
                    {
                        throw FreshSmartArtLayoutTokenRequired();
                    }

                    dynamic layout = layoutObject;
                    if (
                        !TryReadRequiredSmartArtLayoutId(
                            () => layout.Id,
                            out var currentLayoutId
                        )
                        || !string.Equals(
                            currentLayoutId,
                            grant.LayoutId,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        throw FreshSmartArtLayoutTokenRequired();
                    }

                    target.SetRange(sourceStart, sourceStart);
                    var inlineShapeCountBefore = Convert.ToInt32(
                        document.InlineShapes.Count,
                        CultureInfo.InvariantCulture
                    );
                    var rollbackSnapshot = CaptureLiveRollbackSnapshot(
                        document,
                        record.Version
                    );
                    if (!_smartArtLayoutGrants.TryRemove(layoutToken, out _))
                    {
                        throw FreshSmartArtLayoutTokenRequired();
                    }

                    if (optimizeScreenUpdates)
                    {
                        originalScreenUpdating = Convert.ToBoolean(
                            application.ScreenUpdating,
                            CultureInfo.InvariantCulture
                        );
                        application.ScreenUpdating = false;
                    }

                    try
                    {
                        undoRecordObject = application.UndoRecord;
                        dynamic undoRecord = undoRecordObject;
                        undoRecord.StartCustomRecord("WordToolkit: insert SmartArt");
                        undoStarted = true;

                        insertedShapeObject = document.InlineShapes.AddSmartArt(
                            layout,
                            target
                        );
                        if (insertedShapeObject is null)
                        {
                            throw SmartArtInsertionValidationFailed();
                        }

                        dynamic insertedShape = insertedShapeObject;
                        var inlineShapeCountAfter = Convert.ToInt32(
                            document.InlineShapes.Count,
                            CultureInfo.InvariantCulture
                        );
                        var hasSmartArt = Convert.ToInt32(
                            insertedShape.HasSmartArt,
                            CultureInfo.InvariantCulture
                        ) != 0;
                        insertedRangeObject = insertedShape.Range;
                        dynamic insertedRange = insertedRangeObject;
                        var insertedStart = Convert.ToInt32(
                            insertedRange.Start,
                            CultureInfo.InvariantCulture
                        );
                        var insertedEnd = Convert.ToInt32(
                            insertedRange.End,
                            CultureInfo.InvariantCulture
                        );
                        smartArtObject = insertedShape.SmartArt;
                        dynamic smartArt = smartArtObject;
                        insertedLayoutObject = smartArt.Layout;
                        dynamic insertedLayout = insertedLayoutObject;
                        var layoutVerified = TryReadRequiredSmartArtLayoutId(
                            () => insertedLayout.Id,
                            out var insertedLayoutId
                        ) && string.Equals(
                            insertedLayoutId,
                            grant.LayoutId,
                            StringComparison.Ordinal
                        );

                        if (
                            inlineShapeCountAfter != inlineShapeCountBefore + 1
                            || !hasSmartArt
                            || insertedStart < 0
                            || insertedEnd <= insertedStart
                            || !layoutVerified
                        )
                        {
                            throw SmartArtInsertionValidationFailed();
                        }

                        undoRecord.EndCustomRecord();
                        undoStarted = false;
                        record.Version++;
                        InvalidateSelectionGrants(record.Id);
                        InvalidateRangeGrants(record.Id);
                        InvalidateUndoGrants(record.Id);
                        InvalidateSmartArtLayoutGrants(record.Id);
                        return new
                        {
                            operation_contract =
                                "wordtoolkit.insert_live_word_smartart/1.0",
                            live_document_id = record.Id,
                            live_version = record.Version,
                            target_source = selectionToken.Length > 0
                                ? "selection_token"
                                : "range_token",
                            source_range = new { start = sourceStart, end = sourceEnd },
                            inserted_range = new
                            {
                                start = insertedStart,
                                end = insertedEnd,
                            },
                            layout_id = grant.LayoutId,
                            layout_token_consumed = true,
                            inline_shape_count_before = inlineShapeCountBefore,
                            inline_shape_count_after = inlineShapeCountAfter,
                            native_verified = true,
                            raw_com_objects_returned = false,
                            raw_xml_returned = false,
                            document = DocumentInfo(application, document),
                            performance = Performance(started),
                        };
                    }
                    catch (Exception exception)
                    {
                        RollbackPreparedOperationsOrThrow(
                            document,
                            undoRecordObject,
                            ref undoStarted,
                            undoRecordObject is not null,
                            rollbackSnapshot,
                            record,
                            exception
                        );
                        throw;
                    }
                }
                finally
                {
                    if (originalScreenUpdating is not null)
                    {
                        try
                        {
                            application.ScreenUpdating = originalScreenUpdating.Value;
                        }
                        catch
                        {
                            // Restoration is best-effort and must not mask the mutation result
                            // or the original rollback exception.
                        }
                    }
                    FinalReleaseBatchComObject(insertedLayoutObject);
                    FinalReleaseBatchComObject(smartArtObject);
                    FinalReleaseBatchComObject(insertedRangeObject);
                    FinalReleaseBatchComObject(insertedShapeObject);
                    FinalReleaseBatchComObject(undoRecordObject);
                    FinalReleaseBatchComObject(layoutObject);
                    FinalReleaseBatchComObject(layoutsObject);
                    FinalReleaseBatchComObject(targetObject);
                }
            },
            WordComReplaySafety.NonReplayable,
            cancellationToken
        );
    }

    private void TrimSmartArtLayoutGrants()
    {
        if (_smartArtLayoutGrants.Count <= SmartArtLayoutScanLimit)
        {
            return;
        }
        foreach (
            var key in _smartArtLayoutGrants.Keys.Take(
                _smartArtLayoutGrants.Count - (SmartArtLayoutScanLimit / 2)
            )
        )
        {
            _smartArtLayoutGrants.TryRemove(key, out _);
        }
    }

    private void InvalidateSmartArtLayoutGrants(string documentId)
    {
        foreach (
            var pair in _smartArtLayoutGrants.Where(
                item => item.Value.DocumentId == documentId
            )
        )
        {
            _smartArtLayoutGrants.TryRemove(pair.Key, out _);
        }
    }

    private static object SmartArtLayoutItem(
        int sourceIndex,
        string? id,
        string? token,
        string? name,
        string? category,
        string? description,
        bool descriptionReturned,
        bool metadataTruncated,
        string? issueCode
    ) =>
        new
        {
            source_index = sourceIndex,
            available = id is not null && token is not null,
            layout_id = id,
            smartart_layout_token = token,
            name,
            category,
            description,
            description_returned = descriptionReturned,
            metadata_truncated = metadataTruncated,
            issue_code = issueCode,
        };

    private static bool TryReadRequiredSmartArtLayoutId(
        Func<object?> read,
        out string id
    )
    {
        id = "";
        try
        {
            var raw = read();
            if (raw is null || Marshal.IsComObject(raw))
            {
                return false;
            }
            var value = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "";
            if (value.Length is < 1 or > SmartArtLayoutIdLimit)
            {
                return false;
            }
            id = value;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadOptionalSmartArtLayoutText(
        Func<object?> read,
        int limit,
        ref bool metadataAvailable,
        ref bool metadataTruncated
    )
    {
        try
        {
            var raw = read();
            if (raw is null)
            {
                return null;
            }
            if (Marshal.IsComObject(raw))
            {
                metadataAvailable = false;
                return null;
            }
            var value = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "";
            if (value.Length > limit)
            {
                metadataTruncated = true;
                return value[..limit];
            }
            return value;
        }
        catch
        {
            metadataAvailable = false;
            return null;
        }
    }

    private static NativeToolException SmartArtCatalogUnavailable() =>
        new(
            "SMARTART_UNAVAILABLE",
            "The connected Word application did not expose a readable SmartArt layout catalog"
        );

    private static NativeToolException FreshSmartArtLayoutTokenRequired() =>
        new(
            "VERSION_CONFLICT",
            "A fresh smartart_layout_token from inspect_live_word_smartart_layouts is required",
            retryable: true
        );

    private static NativeToolException SmartArtInsertionValidationFailed() =>
        new(
            "VALIDATION_FAILED",
            "Word did not retain exactly one inline SmartArt object with the reviewed layout"
        );
}
