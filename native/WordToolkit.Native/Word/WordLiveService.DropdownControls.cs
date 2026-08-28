using System.Runtime.InteropServices;
using System.Text.Json;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private async Task<object> InsertDropdownControlsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var expectedVersion = arguments.NullableInt64("expected_version")
            ?? throw new NativeToolException(
                "INVALID_INPUT",
                "expected_version is required for dropdown creation"
            );
        var controls = PrepareDropdownControls(arguments.RequiredArray("controls"));
        CheckVersion(record, expectedVersion);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                var resolved = new List<PreparedDropdownControl>(controls.Count);
                foreach (var control in controls)
                {
                    dynamic? range = null;
                    try
                    {
                        range = ResolveVerifiedRange(
                            (object)document,
                            record,
                            control.RangeToken,
                            requireNonEmpty: false
                        );
                        resolved.Add(control with
                        {
                            Start = (int)range.Start,
                            End = (int)range.End,
                        });
                    }
                    finally
                    {
                        FinalReleaseBatchComObject(range);
                    }
                }
                var orderedRanges = resolved.OrderBy(item => item.Start).ToArray();
                for (var index = 1; index < orderedRanges.Length; index++)
                {
                    if (orderedRanges[index].Start < orderedRanges[index - 1].End)
                    {
                        throw new NativeToolException(
                            "INVALID_INPUT",
                            "Dropdown target ranges cannot overlap",
                            new { failed_operation_index = orderedRanges[index].Index }
                        );
                    }
                }

                var rollbackSnapshot = CaptureLiveRollbackSnapshot(document, record.Version);
                var beforeCount = (int)document.ContentControls.Count;
                dynamic? undoRecord = null;
                var undoStarted = false;
                var mutationAttempted = false;
                bool? originalScreenUpdating = null;
                var results = new object?[controls.Count];
                try
                {
                    originalScreenUpdating = (bool)application.ScreenUpdating;
                    application.ScreenUpdating = false;
                    undoRecord = application.UndoRecord;
                    undoRecord.StartCustomRecord("WordToolkit: insert native dropdown controls");
                    undoStarted = true;
                    foreach (var control in resolved.OrderByDescending(item => item.Start))
                    {
                        dynamic? range = null;
                        dynamic? contentControl = null;
                        dynamic? entries = null;
                        var operationPhase = "resolve_target_range";
                        try
                        {
                            mutationAttempted = true;
                            range = document.Range(control.Start, control.End);
                            operationPhase = "create_content_control";
                            contentControl = document.ContentControls.Add(4, range);
                            operationPhase = "set_title";
                            contentControl.Title = control.Title;
                            operationPhase = "set_tag";
                            contentControl.Tag = control.Tag;
                            operationPhase = "read_entries";
                            entries = contentControl.DropdownListEntries;
                            foreach (var item in control.Items)
                            {
                                operationPhase = "add_entry";
                                entries.Add(item);
                            }
                            if (control.SelectedIndex is int selectedIndex)
                            {
                                dynamic? selected = null;
                                try
                                {
                                    operationPhase = "select_entry";
                                    selected = entries.Item(selectedIndex + 1);
                                    selected.Select();
                                }
                                finally
                                {
                                    FinalReleaseBatchComObject(selected);
                                }
                            }
                            operationPhase = "set_locks";
                            contentControl.LockContents = control.LockContents;
                            contentControl.LockContentControl = control.LockControl;
                            operationPhase = "verify_type";
                            var readbackType = Convert.ToInt32(
                                ReadDropdownComProperty((object)contentControl, "Type"),
                                System.Globalization.CultureInfo.InvariantCulture
                            );
                            operationPhase = "verify_entry_count";
                            var readbackEntryCount = Convert.ToInt32(
                                ReadDropdownComProperty((object)entries, "Count"),
                                System.Globalization.CultureInfo.InvariantCulture
                            );
                            operationPhase = "verify_title";
                            var readbackTitle = Convert.ToString(
                                ReadDropdownComProperty((object)contentControl, "Title"),
                                System.Globalization.CultureInfo.InvariantCulture
                            ) ?? "";
                            operationPhase = "verify_tag";
                            var readbackTag = Convert.ToString(
                                ReadDropdownComProperty((object)contentControl, "Tag"),
                                System.Globalization.CultureInfo.InvariantCulture
                            ) ?? "";
                            if (
                                readbackType != 4
                                || readbackEntryCount != control.Items.Count
                                || !string.Equals(
                                    readbackTitle,
                                    control.Title,
                                    StringComparison.Ordinal
                                )
                                || !string.Equals(
                                    readbackTag,
                                    control.Tag,
                                    StringComparison.Ordinal
                                )
                            )
                            {
                                throw new NativeToolException(
                                    "NATIVE_READBACK_FAILED",
                                    "Word did not preserve the requested dropdown contract"
                                );
                            }
                            operationPhase = "read_content_control_id";
                            results[control.Index] = new
                            {
                                index = control.Index,
                                content_control_id = Convert.ToString(
                                    ReadDropdownComProperty((object)contentControl, "ID"),
                                    System.Globalization.CultureInfo.InvariantCulture
                                ) ?? "",
                                item_count = control.Items.Count,
                                selected_index = control.SelectedIndex,
                                title_sha256 = RollbackSha256(control.Title),
                                tag_sha256 = RollbackSha256(control.Tag),
                                native_verified = true,
                            };
                        }
                        catch (Exception exception)
                        {
                            throw DropdownOperationFailure(
                                exception,
                                control.Index,
                                operationPhase
                            );
                        }
                        finally
                        {
                            FinalReleaseBatchComObject(entries);
                            FinalReleaseBatchComObject(contentControl);
                            FinalReleaseBatchComObject(range);
                        }
                    }
                    var afterCount = (int)document.ContentControls.Count;
                    if (afterCount != beforeCount + controls.Count)
                    {
                        throw new NativeToolException(
                            "NATIVE_READBACK_FAILED",
                            "Word did not create the requested dropdown count",
                            new
                            {
                                before = beforeCount,
                                after = afterCount,
                                expected = controls.Count,
                                failed_operation_index_available = false,
                                failure_scope = "batch",
                            }
                        );
                    }
                    undoRecord.EndCustomRecord();
                    undoStarted = false;
                    record.Version++;
                    InvalidateSelectionGrants(record.Id);
                    InvalidateRangeGrants(record.Id);
                    InvalidateUndoGrants(record.Id);
                    return new
                    {
                        live_document_id = record.Id,
                        live_version = record.Version,
                        created_count = controls.Count,
                        controls = results,
                        native_verified = true,
                        single_undo_record = true,
                        document = DocumentInfo(application, document),
                        performance = Performance(started),
                    };
                }
                catch (Exception exception)
                {
                    RollbackPreparedOperationsOrThrow(
                        document,
                        undoRecord,
                        ref undoStarted,
                        mutationAttempted,
                        rollbackSnapshot,
                        record,
                        exception
                    );
                    throw;
                }
                finally
                {
                    if (originalScreenUpdating is not null)
                    {
                        application.ScreenUpdating = originalScreenUpdating.Value;
                    }
                    FinalReleaseBatchComObject(undoRecord);
                }
            },
            cancellationToken
        );
    }

    private static IReadOnlyList<PreparedDropdownControl> PrepareDropdownControls(
        JsonElement controls
    )
    {
        if (controls.GetArrayLength() is < 1 or > 100)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "controls must contain between 1 and 100 dropdown definitions"
            );
        }
        return controls.EnumerateArray()
            .Select((item, index) => PrepareDropdownControl(item, index))
            .ToArray();
    }

    private static NativeToolException DropdownOperationFailure(
        Exception exception,
        int index,
        string operationPhase
    )
    {
        var details = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["failed_operation_index"] = index,
            ["operation_phase"] = operationPhase,
            ["hresult"] = exception.HResult,
            ["exception_type"] = exception.GetType().Name,
            ["raw_document_content_returned"] = false,
        };
        if (exception is NativeToolException native)
        {
            details["failed_operation_index"] = TryGetFailedOperationIndex(native) ?? index;
            details["native_error_code"] = native.ErrorCode;
            return new NativeToolException(
                native.ErrorCode,
                native.Message,
                details,
                native.Retryable
            );
        }
        return new NativeToolException(
            "EXTERNAL_TOOL_FAILED",
            "Microsoft Word rejected one dropdown operation",
            details,
            retryable: true
        );
    }

    private static object? ReadDropdownComProperty(object target, string property)
    {
        return target.GetType().InvokeMember(
            property,
            System.Reflection.BindingFlags.GetProperty,
            binder: null,
            target,
            args: null,
            culture: System.Globalization.CultureInfo.InvariantCulture
        );
    }

    internal static (int ControlCount, int ItemCount, int SelectedCount)
        PrepareDropdownControlsForTesting(JsonElement controls)
    {
        var prepared = PrepareDropdownControls(controls);
        return (
            prepared.Count,
            prepared.Sum(control => control.Items.Count),
            prepared.Count(control => control.SelectedIndex is not null)
        );
    }

    private static PreparedDropdownControl PrepareDropdownControl(
        JsonElement item,
        int index
    )
    {
        RequireObject(item, "Each dropdown control must be an object");
        EnsureAllowedProperties(
            item,
            [
                "range_token",
                "title",
                "tag",
                "items",
                "selected_item",
                "lock_contents",
                "lock_control",
            ],
            "dropdown control",
            index
        );
        var rangeToken = item.String("range_token");
        var title = item.String("title");
        var tag = item.String("tag");
        if (rangeToken.Length is < 1 or > 128)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Every dropdown requires a fresh bounded range_token",
                new { failed_operation_index = index }
            );
        }
        if (title.Length > 256 || tag.Length > 256)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "Dropdown title and tag cannot exceed 256 characters",
                new { failed_operation_index = index }
            );
        }
        var itemsNode = item.RequiredArray("items");
        if (itemsNode.GetArrayLength() is < 1 or > 100)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "Dropdown items must contain between 1 and 100 values",
                new { failed_operation_index = index }
            );
        }
        var items = itemsNode.EnumerateArray().Select((value, itemIndex) =>
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Every dropdown item must be a string",
                    new { failed_operation_index = index, item_index = itemIndex }
                );
            }
            var text = value.GetString() ?? "";
            if (text.Length is < 1 or > 256 || text.Contains('\r') || text.Contains('\n'))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Dropdown items must be non-empty single-line strings up to 256 characters",
                    new { failed_operation_index = index, item_index = itemIndex }
                );
            }
            return text;
        }).ToArray();
        if (items.Distinct(StringComparer.Ordinal).Count() != items.Length)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Dropdown items must be unique",
                new { failed_operation_index = index }
            );
        }
        var selectedItem = item.String("selected_item");
        int? selectedIndex = null;
        if (selectedItem.Length > 0)
        {
            var found = Array.FindIndex(items, value =>
                string.Equals(value, selectedItem, StringComparison.Ordinal)
            );
            if (found < 0)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "selected_item must exactly match one dropdown item",
                    new { failed_operation_index = index }
                );
            }
            selectedIndex = found;
        }
        return new PreparedDropdownControl(
            index,
            rangeToken,
            title,
            tag,
            items,
            selectedIndex,
            item.Boolean("lock_contents", false),
            item.Boolean("lock_control", false),
            Start: 0,
            End: 0
        );
    }

    private sealed record PreparedDropdownControl(
        int Index,
        string RangeToken,
        string Title,
        string Tag,
        IReadOnlyList<string> Items,
        int? SelectedIndex,
        bool LockContents,
        bool LockControl,
        int Start,
        int End
    );
}
