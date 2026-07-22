using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using WordToolkit.Native.Equations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService : IToolHandler
{
    private const int MainTextStory = 1;
    private const int NoProtection = -1;
    private const int WordTrue = -1;
    private const int WordFalse = 0;
    private const int WordDoNotSaveChanges = 0;
    private const int WordFormatDocumentDefault = 16;
    private const int WordExportFormatPdf = 17;
    private const int OfficeAutomationSecurityForceDisable = 3;
    private const int MaxSemanticIndexEntries = 4;
    private const int MaxSemanticIndexNodesPerEntry = 100_000;
    private const int MaxSemanticIndexCachedNodes = 250_000;
    private static readonly HashSet<string> OpenableDocumentExtensions = new(
        [
            ".doc",
            ".docx",
            ".docm",
            ".dot",
            ".dotx",
            ".dotm",
            ".htm",
            ".html",
            ".mht",
            ".mhtml",
            ".odt",
            ".pdf",
            ".rtf",
            ".txt",
            ".xml",
        ],
        StringComparer.OrdinalIgnoreCase
    );
    private static readonly HashSet<string> ImageExtensions = new(
        [
            ".bmp",
            ".emf",
            ".gif",
            ".jpeg",
            ".jpg",
            ".png",
            ".svg",
            ".tif",
            ".tiff",
            ".wmf",
        ],
        StringComparer.OrdinalIgnoreCase
    );
    private readonly IWordComHost _host;
    private readonly ConcurrentDictionary<string, LiveDocumentRecord> _records = new();
    private readonly ConcurrentDictionary<string, SelectionGrant> _selectionGrants = new();
    private readonly ConcurrentDictionary<string, UndoGrant> _undoGrants = new();
    private readonly ConcurrentDictionary<string, RangeGrant> _rangeGrants = new();
    private readonly ConcurrentDictionary<string, CachedSemanticIndex> _semanticIndexes = new();
    private readonly object _semanticIndexGate = new();

    public WordLiveService(IWordComHost host)
    {
        _host = host;
    }

    public Task<object> CallAsync(
        string name,
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        return name switch
        {
            "list_live_word_documents" => ListDocumentsAsync(cancellationToken),
            "start_word_application" => StartWordAsync(arguments, cancellationToken),
            "create_live_word_document" => CreateDocumentAsync(
                arguments,
                cancellationToken
            ),
            "open_live_word_document" => OpenDocumentAsync(
                arguments,
                cancellationToken
            ),
            "connect_live_word_document" => ConnectAsync(arguments, cancellationToken),
            "inspect_ooxml_package" => InspectPackageAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_semantics" => InspectPackageSemanticsAsync(
                arguments,
                cancellationToken
            ),
            "query_ooxml_semantics" => QueryPackageSemanticsAsync(
                arguments,
                cancellationToken
            ),
            "manage_ooxml_semantic_index" => ManagePackageSemanticIndexAsync(
                arguments,
                cancellationToken
            ),
            "compare_ooxml_semantics" => ComparePackageSemanticsAsync(
                arguments,
                cancellationToken
            ),
            "plan_ooxml_patch" => PlanPackagePatchAsync(
                arguments,
                cancellationToken
            ),
            "create_ooxml_patch" => CreatePackagePatchAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_patch" => InspectPackagePatchAsync(
                arguments,
                cancellationToken
            ),
            "plan_ooxml_patch_apply" => PlanPackagePatchApplyAsync(
                arguments,
                cancellationToken
            ),
            "apply_ooxml_patch" => ApplyPackagePatchAsync(
                arguments,
                cancellationToken
            ),
            "plan_ooxml_merge" => PlanPackageMergeAsync(
                arguments,
                cancellationToken
            ),
            "apply_ooxml_merge" => ApplyPackageMergeAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_sections" => InspectPackageSectionsAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_styles" => InspectPackageStylesAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_numbering" => InspectPackageNumberingAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_theme" => InspectPackageThemeAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_settings" => InspectPackageSettingsAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_references" => InspectPackageReferencesAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_dependencies" => InspectPackageDependenciesAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_charts" => InspectPackageChartsAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_content_controls" =>
                InspectPackageContentControlsAsync(arguments, cancellationToken),
            "inspect_ooxml_tables" => InspectPackageTablesAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_markup_compatibility" =>
                InspectPackageMarkupCompatibilityAsync(arguments, cancellationToken),
            "lint_ooxml_document" => LintPackageAsync(
                arguments,
                cancellationToken
            ),
            "plan_ooxml_lint_repair" => PlanPackageLintRepairAsync(
                arguments,
                cancellationToken
            ),
            "apply_ooxml_lint_repair" => ApplyPackageLintRepairAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_equations" => InspectPackageEquationsAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_review" => InspectPackageReviewAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_fonts" => InspectPackageFontsAsync(
                arguments,
                cancellationToken
            ),
            "resolve_ooxml_formatting" => ResolvePackageFormattingAsync(
                arguments,
                cancellationToken
            ),
            "plan_ooxml_text_edits" => PlanPackageTextEditsAsync(
                arguments,
                cancellationToken
            ),
            "apply_ooxml_text_edits" => ApplyPackageTextEditsAsync(
                arguments,
                cancellationToken
            ),
            "plan_ooxml_semantic_edits" => PlanPackageSemanticEditsAsync(
                arguments,
                cancellationToken
            ),
            "apply_ooxml_semantic_edits" => ApplyPackageSemanticEditsAsync(
                arguments,
                cancellationToken
            ),
            "plan_ooxml_review_decisions" => PlanPackageReviewDecisionsAsync(
                arguments,
                cancellationToken
            ),
            "apply_ooxml_review_decisions" => ApplyPackageReviewDecisionsAsync(
                arguments,
                cancellationToken
            ),
            "inspect_live_word_document" => InspectAsync(arguments, cancellationToken),
            "map_live_word_structures" => MapStructuresAsync(
                arguments,
                cancellationToken
            ),
            "inspect_live_word_structure_items" => InspectStructureItemsAsync(
                arguments,
                cancellationToken
            ),
            "inspect_live_word_equation_learning" => InspectEquationLearning(),
            "inspect_live_word_structure_learning" => InspectStructureLearning(),
            "inspect_live_word_object_model_types" => InspectObjectModelTypesAsync(
                arguments,
                cancellationToken
            ),
            "inspect_live_word_object_model_members" => InspectObjectModelMembersAsync(
                arguments,
                cancellationToken
            ),
            "inspect_live_word_member_capabilities" => InspectMemberCapabilitiesAsync(
                arguments,
                cancellationToken
            ),
            "preflight_live_word_member_operations" => PreflightMemberOperationsAsync(
                arguments,
                cancellationToken
            ),
            "execute_live_word_member_operations" => ExecuteMemberOperationsAsync(
                arguments,
                cancellationToken
            ),
            "find_live_word_text" => FindTextAsync(arguments, cancellationToken),
            "replace_live_word_text" => ReplaceTextAsync(arguments, cancellationToken),
            "inspect_live_word_review" => InspectReviewAsync(
                arguments,
                cancellationToken
            ),
            "manage_live_word_review" => ManageReviewAsync(
                arguments,
                cancellationToken
            ),
            "diagnose_live_word_layout" => DiagnoseLayoutAsync(
                arguments,
                cancellationToken
            ),
            "get_live_word_selection" => GetSelectionAsync(arguments, cancellationToken),
            "inspect_live_word_undo" => InspectUndoAsync(arguments, cancellationToken),
            "undo_live_word_operation" => UndoOperationAsync(arguments, cancellationToken),
            "insert_live_word_text" => InsertTextAsync(arguments, cancellationToken),
            "format_live_word_selection" => FormatSelectionAsync(
                arguments,
                cancellationToken
            ),
            "insert_live_word_table" => InsertTableAsync(arguments, cancellationToken),
            "preflight_live_word_table_formulas" => Task.FromResult(
                PreflightTableFormulas(arguments)
            ),
            "insert_live_word_table_formulas" => InsertTableFormulasAsync(
                arguments,
                cancellationToken
            ),
            "update_live_word_table_fields" => UpdateTableFieldsAsync(
                arguments,
                cancellationToken
            ),
            "insert_live_word_list" => InsertListAsync(arguments, cancellationToken),
            "preflight_live_word_bookmarks" => Task.FromResult(
                PreflightBookmarks(arguments)
            ),
            "insert_live_word_bookmarks" => InsertBookmarksAsync(
                arguments,
                cancellationToken
            ),
            "preflight_live_word_fields" => Task.FromResult(
                PreflightFields(arguments)
            ),
            "insert_live_word_fields" => InsertFieldsAsync(
                arguments,
                cancellationToken
            ),
            "insert_live_word_image" => InsertImageAsync(arguments, cancellationToken),
            "insert_live_word_comment" => InsertCommentAsync(
                arguments,
                cancellationToken
            ),
            "insert_live_word_note" => InsertNoteAsync(arguments, cancellationToken),
            "set_live_word_header_footer" => SetHeaderFooterAsync(
                arguments,
                cancellationToken
            ),
            "insert_live_word_equation" => InsertEquationAsync(arguments, cancellationToken),
            "insert_live_word_equations_batch" => InsertEquationBatchAsync(
                arguments,
                cancellationToken
            ),
            "preflight_live_word_equations" => PreflightEquationsAsync(arguments),
            "apply_live_word_operations" => ApplyOperationsAsync(
                arguments,
                cancellationToken
            ),
            "validate_live_word_document" => ValidateLiveDocumentAsync(
                arguments,
                cancellationToken
            ),
            "export_live_word_pdf" => ExportPdfAsync(arguments, cancellationToken),
            "save_live_word_document" => SaveAsync(arguments, cancellationToken),
            "close_live_word_document" => CloseDocumentAsync(
                arguments,
                cancellationToken
            ),
            "quit_word_application" => QuitWordAsync(arguments, cancellationToken),
            "disconnect_live_word_document" => DisconnectAsync(arguments),
            _ => throw new NativeToolException(
                "INVALID_INPUT",
                $"Native tool is not implemented: {name}"
            ),
        };
    }

    private async Task<object> ListDocumentsAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            return await _host.InvokeAsync<object>(
                application =>
                {
                    var documents = new List<object>();
                    var count = (int)application.Documents.Count;
                    for (var index = 1; index <= count; index++)
                    {
                        dynamic document = application.Documents.Item(index);
                        documents.Add(DocumentInfo(application, document));
                    }
                    return new
                    {
                        word_running = true,
                        visible = (bool)application.Visible,
                        document_count = documents.Count,
                        documents,
                        performance = Performance(started),
                    };
                },
                cancellationToken
            );
        }
        catch (NativeToolException exception)
            when (exception.ErrorCode == "LIVE_WORD_UNAVAILABLE")
        {
            return new
            {
                word_running = false,
                visible = false,
                document_count = 0,
                documents = Array.Empty<object>(),
                notice = exception.Message,
                performance = Performance(started),
            };
        }
    }

    private async Task<object> StartWordAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var visible = arguments.Boolean("visible", true);
        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                application.Visible = visible;
                return new
                {
                    word_running = true,
                    visible = (bool)application.Visible,
                    document_count = (int)application.Documents.Count,
                    started_or_attached = true,
                    runtime = "dotnet-native",
                    python_used = false,
                    performance = Performance(started),
                };
            },
            cancellationToken,
            launchIfMissing: true
        );
    }

    private async Task<object> OpenDocumentAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var filePath = ValidateExistingDocumentPath(arguments.String("file_path"));
        var readOnly = arguments.Boolean("read_only", false);
        var activate = arguments.Boolean("activate", true);
        var visible = arguments.Boolean("visible", true);
        var addToRecentFiles = arguments.Boolean("add_to_recent_files", false);
        var openAndRepair = arguments.Boolean("open_and_repair", false);
        var launchIfNeeded = arguments.Boolean("launch_if_needed", true);
        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                dynamic? document = null;
                var alreadyOpen = false;
                var count = (int)application.Documents.Count;
                for (var index = 1; index <= count; index++)
                {
                    dynamic candidate = application.Documents.Item(index);
                    if (
                        string.Equals(
                            NormalizePath(DocumentFullName(candidate)),
                            NormalizePath(filePath),
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        document = candidate;
                        alreadyOpen = true;
                        break;
                    }
                }
                if (document is null)
                {
                    var originalAutomationSecurity = (int)application.AutomationSecurity;
                    var originalUpdateLinksAtOpen =
                        (bool)application.Options.UpdateLinksAtOpen;
                    try
                    {
                        application.AutomationSecurity =
                            OfficeAutomationSecurityForceDisable;
                        application.Options.UpdateLinksAtOpen = false;
                        document = application.Documents.Open(
                            FileName: filePath,
                            ConfirmConversions: false,
                            ReadOnly: readOnly,
                            AddToRecentFiles: addToRecentFiles,
                            Revert: false,
                            Visible: visible,
                            OpenAndRepair: openAndRepair,
                            NoEncodingDialog: true
                        );
                    }
                    finally
                    {
                        application.Options.UpdateLinksAtOpen =
                            originalUpdateLinksAtOpen;
                        application.AutomationSecurity = originalAutomationSecurity;
                    }
                }
                if (visible)
                {
                    application.Visible = true;
                }
                if (activate)
                {
                    document.Activate();
                }
                var name = DocumentName(document);
                var fullName = DocumentFullName(document);
                var identity = NormalizeIdentity(fullName, name);
                var record = _records.Values.SingleOrDefault(
                    item => NormalizeIdentity(item.FullName, item.Name) == identity
                );
                if (record is null)
                {
                    record = new LiveDocumentRecord
                    {
                        Id = $"live_{Guid.NewGuid():N}",
                        Name = name,
                        FullName = fullName,
                        WindowHwnd = ActiveWindowHwnd(application),
                        Version = 0,
                    };
                    _records[record.Id] = record;
                }
                return new
                {
                    live_document_id = record.Id,
                    live_version = record.Version,
                    opened = !alreadyOpen,
                    already_open = alreadyOpen,
                    file_path = filePath,
                    document = DocumentInfo(application, document),
                    performance = Performance(started),
                };
            },
            cancellationToken,
            launchIfMissing: launchIfNeeded
        );
    }

    private async Task<object> ConnectAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var documentName = arguments.String("document_name");
        var fullPath = arguments.String("full_path");
        var useActive = arguments.Boolean("use_active", true);
        var activate = arguments.Boolean("activate", true);
        if (documentName.Length > 260 || fullPath.Length > 32_767)
        {
            throw new NativeToolException("LIMIT_EXCEEDED", "Document selector is too long");
        }
        if (documentName.Length > 0 && fullPath.Length > 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Provide document_name or full_path, not both"
            );
        }
        if (documentName.Length > 0 || fullPath.Length > 0)
        {
            useActive = false;
        }

        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                var matches = ResolveSelector(application, documentName, fullPath, useActive);
                if (matches.Count != 1)
                {
                    throw new NativeToolException(
                        "DOCUMENT_NOT_FOUND",
                        "The live document selector did not resolve to exactly one document",
                        new { matches = matches.Count }
                    );
                }
                dynamic document = matches[0];
                if (activate)
                {
                    document.Activate();
                }
                var name = DocumentName(document);
                var resolvedFullName = DocumentFullName(document);
                var hwnd = ActiveWindowHwnd(application);
                var identity = NormalizeIdentity(resolvedFullName, name);
                var record = _records.Values.SingleOrDefault(
                    item => NormalizeIdentity(item.FullName, item.Name) == identity
                );
                if (record is null)
                {
                    record = new LiveDocumentRecord
                    {
                        Id = $"live_{Guid.NewGuid():N}",
                        Name = name,
                        FullName = resolvedFullName,
                        WindowHwnd = hwnd,
                        Version = 0,
                    };
                    _records[record.Id] = record;
                }
                return new
                {
                    live_document_id = record.Id,
                    live_version = record.Version,
                    document = DocumentInfo(application, document),
                    performance = Performance(started),
                };
            },
            cancellationToken
        );
    }

    private async Task<object> CreateDocumentAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var outputPath = arguments.String("output_path");
        var activate = arguments.Boolean("activate", true);
        string resolvedOutputPath = "";
        if (outputPath.Length > 0)
        {
            if (outputPath.Length > 32_767)
            {
                throw new NativeToolException(
                    "LIMIT_EXCEEDED",
                    "output_path exceeds the Windows path limit"
                );
            }
            resolvedOutputPath = Path.GetFullPath(outputPath);
            if (
                !string.Equals(
                    Path.GetExtension(resolvedOutputPath),
                    ".docx",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                throw new NativeToolException(
                    "UNSUPPORTED_FORMAT",
                    "A new live Word document must use the .docx extension"
                );
            }
            var parent = Path.GetDirectoryName(resolvedOutputPath) ?? "";
            if (parent.Length == 0 || !Directory.Exists(parent))
            {
                throw new NativeToolException(
                    "DOCUMENT_NOT_FOUND",
                    "The output directory does not exist"
                );
            }
            if (File.Exists(resolvedOutputPath))
            {
                throw new NativeToolException(
                    "VERSION_CONFLICT",
                    "The output file already exists; native creation never overwrites files"
                );
            }
        }

        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                dynamic document = application.Documents.Add();
                if (resolvedOutputPath.Length > 0)
                {
                    try
                    {
                        document.SaveAs2(resolvedOutputPath, 16);
                    }
                    catch
                    {
                        try
                        {
                            document.Close(0);
                        }
                        catch
                        {
                            // The original SaveAs failure remains authoritative.
                        }
                        throw;
                    }
                }
                if (activate)
                {
                    document.Activate();
                }
                var name = DocumentName(document);
                var fullName = DocumentFullName(document);
                var record = new LiveDocumentRecord
                {
                    Id = $"live_{Guid.NewGuid():N}",
                    Name = name,
                    FullName = fullName,
                    WindowHwnd = ActiveWindowHwnd(application),
                    Version = 0,
                };
                _records[record.Id] = record;
                return new
                {
                    live_document_id = record.Id,
                    live_version = record.Version,
                    created = true,
                    saved_to_disk = resolvedOutputPath.Length > 0,
                    output_path = resolvedOutputPath,
                    document = DocumentInfo(application, document),
                    performance = Performance(started),
                };
            },
            cancellationToken
        );
    }

    private async Task<object> InspectAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                dynamic document = ResolveDocument(application, record);
                return new
                {
                    live_document_id = record.Id,
                    live_version = record.Version,
                    document = DocumentInfo(application, document),
                    performance = Performance(started),
                };
            },
            cancellationToken
        );
    }

    private async Task<object> GetSelectionAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        return await _host.InvokeAsync<object>(
            application =>
            {
                dynamic document = ResolveDocument(application, record);
                RequireActive(application, document);
                dynamic selection = application.Selection;
                dynamic range = selection.Range;
                var start = (int)range.Start;
                var end = (int)range.End;
                var storyType = (int)range.StoryType;
                var selectionType = (int)selection.Type;
                var text = ((string?)range.Text ?? "")
                    .Replace("\0", "", StringComparison.Ordinal);
                var contextHash = SelectionContextHash(document, start, end);
                var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                _selectionGrants[token] = new SelectionGrant(
                    token,
                    record.Id,
                    record.Version,
                    ActiveWindowHwnd(application),
                    storyType,
                    start,
                    end,
                    contextHash
                );
                TrimSelectionGrants();
                return new
                {
                    live_document_id = record.Id,
                    live_version = record.Version,
                    selection = new
                    {
                        start,
                        end,
                        collapsed = start == end,
                        story_type = storyType,
                        selection_type = selectionType,
                        text_preview = text[..Math.Min(text.Length, 10_000)],
                        text_truncated = text.Length > 10_000,
                        selection_token = token,
                    },
                    document = DocumentInfo(application, document),
                };
            },
            cancellationToken
        );
    }

    private async Task<object> FindTextAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var searchText = arguments.String("search_text");
        var matchCase = arguments.Boolean("match_case", false);
        var wholeWord = arguments.Boolean("whole_word", false);
        var useWildcards = arguments.Boolean("use_wildcards", false);
        var contextChars = (int)(arguments.NullableInt64("context_chars") ?? 80);
        var maxResults = (int)(arguments.NullableInt64("max_results") ?? 100);
        ValidateFindArguments(searchText, contextChars, maxResults);
        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                dynamic document = ResolveDocument(application, record);
                var foundRanges = FindRanges(
                    (object)document,
                    searchText,
                    matchCase,
                    wholeWord,
                    useWildcards,
                    contextChars,
                    maxResults,
                    out var truncated
                );
                var matches = foundRanges
                    .Select(match =>
                    {
                        var token = Convert.ToHexString(
                            RandomNumberGenerator.GetBytes(32)
                        ).ToLowerInvariant();
                        _rangeGrants[token] = new RangeGrant(
                            token,
                            record.Id,
                            record.Version,
                            match.Start,
                            match.End,
                            SelectionContextHash(document, match.Start, match.End)
                        );
                        return new
                        {
                            start = match.Start,
                            end = match.End,
                            text = match.Text,
                            text_truncated = match.TextTruncated,
                            context = match.Context,
                            context_truncated = match.ContextTruncated,
                            range_token = token,
                        };
                    })
                    .ToArray();
                TrimRangeGrants();
                return new
                {
                    live_document_id = record.Id,
                    live_version = record.Version,
                    search = new
                    {
                        text = searchText,
                        match_case = matchCase,
                        whole_word = wholeWord && !useWildcards,
                        whole_word_ignored_for_wildcards = wholeWord && useWildcards,
                        use_wildcards = useWildcards,
                    },
                    match_count = matches.Length,
                    truncated,
                    matches,
                    document = DocumentInfo(application, document),
                    performance = new
                    {
                        runtime = "dotnet-native",
                        python_used = false,
                        persistent_com_sta = true,
                        native_find = true,
                        total_ms = Math.Round(
                            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                            3
                        ),
                    },
                };
            },
            cancellationToken
        );
    }

    private async Task<object> ReplaceTextAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var searchText = arguments.String("search_text");
        var replacementText = arguments.String("replacement_text");
        var matchCase = arguments.Boolean("match_case", false);
        var wholeWord = arguments.Boolean("whole_word", false);
        var useWildcards = arguments.Boolean("use_wildcards", false);
        var replaceAll = arguments.Boolean("replace_all", true);
        var trackChanges = arguments.String("track_changes", "preserve");
        var maxReplacements = (int)(arguments.NullableInt64("max_replacements") ?? 1_000);
        var optimizeScreenUpdates = arguments.Boolean("optimize_screen_updates", true);
        var expectedVersion = arguments.NullableInt64("expected_version");
        ValidateFindArguments(searchText, contextChars: 0, maxReplacements);
        if (replacementText.Length > 200_000)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "replacement_text exceeds 200,000 characters"
            );
        }
        if (trackChanges is not ("preserve" or "enable" or "disable"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "track_changes must be preserve, enable, or disable"
            );
        }
        CheckVersion(record, expectedVersion);
        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                var matches = FindRanges(
                    (object)document,
                    searchText,
                    matchCase,
                    wholeWord,
                    useWildcards,
                    contextChars: 0,
                    maxResults: maxReplacements + 1,
                    out var truncated
                );
                if (truncated || matches.Count > maxReplacements)
                {
                    throw new NativeToolException(
                        "LIMIT_EXCEEDED",
                        "The replacement match set exceeds max_replacements",
                        new { max_replacements = maxReplacements }
                    );
                }
                if (!replaceAll && matches.Count > 1)
                {
                    matches = [matches[0]];
                }
                var normalizedReplacement = NormalizeWordText(replacementText);
                bool? originalScreenUpdating = null;
                var originalTrackChanges = (bool)document.TrackRevisions;
                dynamic? undoRecord = null;
                var undoStarted = false;
                try
                {
                    if (optimizeScreenUpdates)
                    {
                        originalScreenUpdating = (bool)application.ScreenUpdating;
                        application.ScreenUpdating = false;
                    }
                    if (trackChanges == "enable")
                    {
                        document.TrackRevisions = true;
                    }
                    else if (trackChanges == "disable")
                    {
                        document.TrackRevisions = false;
                    }
                    undoRecord = application.UndoRecord;
                    undoRecord.StartCustomRecord("WordToolkit: native replace text");
                    undoStarted = true;
                    foreach (var match in matches.OrderByDescending(item => item.Start))
                    {
                        dynamic range = document.Range(match.Start, match.End);
                        range.Text = normalizedReplacement;
                    }
                    undoRecord.EndCustomRecord();
                    undoStarted = false;
                    if (matches.Count > 0)
                    {
                        record.Version++;
                        InvalidateSelectionGrants(record.Id);
                        InvalidateRangeGrants(record.Id);
                        InvalidateUndoGrants(record.Id);
                    }
                    return new
                    {
                        live_document_id = record.Id,
                        live_version = record.Version,
                        replacements = matches.Count,
                        replace_all = replaceAll,
                        track_changes = trackChanges,
                        execution = new
                        {
                            native_find = true,
                            single_undo_record = true,
                            rollback_on_error = true,
                            track_changes_restored = true,
                        },
                        document = DocumentInfo(application, document),
                        performance = Performance(started),
                    };
                }
                catch
                {
                    Rollback(document, undoRecord, ref undoStarted);
                    throw;
                }
                finally
                {
                    try
                    {
                        document.TrackRevisions = originalTrackChanges;
                    }
                    catch
                    {
                        // The native error remains authoritative.
                    }
                    if (originalScreenUpdating is not null)
                    {
                        application.ScreenUpdating = originalScreenUpdating.Value;
                    }
                }
            },
            cancellationToken
        );
    }

    private async Task<object> FormatSelectionAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var token = arguments.String("selection_token");
        var style = arguments.String("style");
        var expectedVersion = arguments.NullableInt64("expected_version");
        var optimizeScreenUpdates = arguments.Boolean("optimize_screen_updates", true);
        var formatting = arguments.TryGetProperty("formatting", out var formatNode)
            && formatNode.ValueKind == JsonValueKind.Object
            ? formatNode.Clone()
            : (JsonElement?)null;
        if (style.Length == 0 && formatting is null)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "A style or formatting object is required"
            );
        }
        CheckVersion(record, expectedVersion);
        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                dynamic range = ResolveInsertionRange(
                    (object)application,
                    (object)document,
                    record,
                    "selection",
                    token,
                    replaceSelection: true
                );
                if ((int)range.Start == (int)range.End)
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "The current Word selection is empty"
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
                    undoRecord.StartCustomRecord("WordToolkit: native format selection");
                    undoStarted = true;
                    if (style.Length > 0)
                    {
                        range.Style = style;
                    }
                    if (formatting is not null)
                    {
                        ApplyFormatting(range, formatting.Value);
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
                        formatted_range = new
                        {
                            start = (int)range.Start,
                            end = (int)range.End,
                        },
                        style,
                        formatting_applied = formatting is not null,
                        document = DocumentInfo(application, document),
                        performance = Performance(started),
                    };
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
            cancellationToken
        );
    }

    private async Task<object> InsertTextAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var text = arguments.String("text");
        if (text.Length == 0)
        {
            throw new NativeToolException("INVALID_INPUT", "text must not be empty");
        }
        if (text.Length > 200_000)
        {
            throw new NativeToolException("LIMIT_EXCEEDED", "text exceeds 200,000 characters");
        }
        var target = arguments.String("target", "cursor");
        var asNewParagraph = arguments.Boolean("as_new_paragraph", false);
        var style = arguments.String("style");
        var selectionToken = arguments.String("selection_token");
        var replaceSelection = arguments.Boolean("replace_selection", false);
        var activate = arguments.Boolean("activate", true);
        var expectedVersion = arguments.NullableInt64("expected_version");
        var formatting = arguments.TryGetProperty("formatting", out var formatNode)
            && formatNode.ValueKind == JsonValueKind.Object
            ? formatNode.Clone()
            : (JsonElement?)null;
        CheckVersion(record, expectedVersion);

        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                if (activate)
                {
                    document.Activate();
                }
                dynamic insertion = ResolveInsertionRange(
                    (object)application,
                    (object)document,
                    record,
                    target,
                    selectionToken,
                    replaceSelection
                );
                var insertionStart = (int)insertion.Start;
                var payload = ParagraphPayload(
                    (object)document,
                    insertionStart,
                    text,
                    asNewParagraph
                );
                var prefixLength = payload.Prefix.Length;
                var originalScreenUpdating = (bool?)null;
                dynamic? undoRecord = null;
                var undoStarted = false;
                try
                {
                    originalScreenUpdating = (bool)application.ScreenUpdating;
                    application.ScreenUpdating = false;
                    undoRecord = application.UndoRecord;
                    undoRecord.StartCustomRecord("WordToolkit: native text insertion");
                    undoStarted = true;
                    insertion.Text = payload.Text;
                    dynamic inserted = document.Range(
                        insertionStart + prefixLength,
                        insertionStart + prefixLength + NormalizeWordText(text).Length
                    );
                    if (style.Length > 0)
                    {
                        inserted.Style = style;
                    }
                    if (formatting is not null)
                    {
                        ApplyFormatting(inserted, formatting.Value);
                    }
                    undoRecord.EndCustomRecord();
                    undoStarted = false;
                    record.Version++;
                    InvalidateSelectionGrants(record.Id);
                    InvalidateRangeGrants(record.Id);
                    return new
                    {
                        live_document_id = record.Id,
                        live_version = record.Version,
                        inserted_range = new
                        {
                            start = (int)inserted.Start,
                            end = (int)inserted.End,
                        },
                        characters = text.Length,
                        style,
                        document = DocumentInfo(application, document),
                        performance = Performance(started),
                    };
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
            cancellationToken
        );
    }

    private async Task<object> InsertTableAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var rowsNode = arguments.RequiredArray("rows");
        if (rowsNode.GetArrayLength() is < 1 or > 200)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "rows must contain between 1 and 200 rows"
            );
        }
        var rows = new List<string[]>();
        var columnCount = -1;
        var totalCharacters = 0;
        foreach (var rowNode in rowsNode.EnumerateArray())
        {
            if (rowNode.ValueKind != JsonValueKind.Array)
            {
                throw new NativeToolException("INVALID_INPUT", "Every table row must be an array");
            }
            var row = rowNode.EnumerateArray()
                .Select(
                    cell =>
                    {
                        if (cell.ValueKind != JsonValueKind.String)
                        {
                            throw new NativeToolException(
                                "INVALID_INPUT",
                                "Every table cell must be a string"
                            );
                        }
                        var value = cell.GetString() ?? "";
                        if (value.Contains('\t') || value.Contains('\x07'))
                        {
                            throw new NativeToolException(
                                "INVALID_INPUT",
                                "Table cells cannot contain tabs or Word cell markers"
                            );
                        }
                        return value
                            .Replace("\r\n", "\n", StringComparison.Ordinal)
                            .Replace('\r', '\n')
                            .Replace('\n', '\v');
                    }
                )
                .ToArray();
            if (row.Length is < 1 or > 50)
            {
                throw new NativeToolException(
                    "LIMIT_EXCEEDED",
                    "A table must contain between 1 and 50 columns"
                );
            }
            columnCount = columnCount < 0 ? row.Length : columnCount;
            if (row.Length != columnCount)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "All table rows must contain the same number of cells"
                );
            }
            totalCharacters += row.Sum(cell => cell.Length);
            rows.Add(row);
        }
        if (rows.Count * columnCount > 5_000 || totalCharacters > 500_000)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "The table exceeds the native cell or character limit"
            );
        }
        var target = arguments.String("target", "document_end");
        var selectionToken = arguments.String("selection_token");
        var replaceSelection = arguments.Boolean("replace_selection", false);
        var style = arguments.String("style");
        var headerRow = arguments.Boolean("header_row", true);
        var autofit = arguments.String("autofit", "window");
        var alignment = arguments.String("alignment", "left");
        var activate = arguments.Boolean("activate", true);
        var optimizeScreenUpdates = arguments.Boolean("optimize_screen_updates", true);
        var expectedVersion = arguments.NullableInt64("expected_version");
        var autofitValue = autofit switch
        {
            "fixed" => 0,
            "content" => 1,
            "window" => 2,
            _ => throw new NativeToolException(
                "INVALID_INPUT",
                "autofit must be fixed, content, or window"
            ),
        };
        var alignmentValue = alignment switch
        {
            "left" => 0,
            "center" => 1,
            "right" => 2,
            _ => throw new NativeToolException(
                "INVALID_INPUT",
                "alignment must be left, center, or right"
            ),
        };
        CheckVersion(record, expectedVersion);
        var tsv = string.Join("\r", rows.Select(row => string.Join("\t", row)));
        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                if (activate)
                {
                    document.Activate();
                }
                dynamic targetRange = ResolveInsertionRange(
                    (object)application,
                    (object)document,
                    record,
                    target,
                    selectionToken,
                    replaceSelection
                );
                var start = (int)targetRange.Start;
                var payload = ParagraphPayload(
                    (object)document,
                    start,
                    tsv,
                    asNewParagraph: true
                );
                var before = DocumentTableCount(document);
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
                    undoRecord.StartCustomRecord("WordToolkit: native table insertion");
                    undoStarted = true;
                    targetRange.Text = payload.Text;
                    var tableStart = start + payload.Prefix.Length;
                    var tableEnd = tableStart + NormalizeWordText(tsv).Length;
                    dynamic conversionRange = document.Range(tableStart, tableEnd);
                    dynamic table = conversionRange.ConvertToTable(
                        1,
                        rows.Count,
                        columnCount
                    );
                    var after = DocumentTableCount(document);
                    if (after != before + 1)
                    {
                        throw new NativeToolException(
                            "EXTERNAL_TOOL_FAILED",
                            "Microsoft Word did not create exactly one native table",
                            new { before, after }
                        );
                    }
                    if (style.Length > 0)
                    {
                        table.Style = style;
                    }
                    table.AllowAutoFit = autofit != "fixed";
                    table.AutoFitBehavior(autofitValue);
                    table.Rows.Alignment = alignmentValue;
                    if (headerRow)
                    {
                        table.Rows.Item(1).HeadingFormat = -1;
                    }
                    dynamic tableRange = table.Range.Duplicate;
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
                        table = new
                        {
                            index = after,
                            rows = rows.Count,
                            columns = columnCount,
                            range = new
                            {
                                start = (int)tableRange.Start,
                                end = (int)tableRange.End,
                            },
                            style,
                            header_row = headerRow,
                            autofit,
                            alignment,
                            native_verified = true,
                        },
                        content_returned = false,
                        document = DocumentInfo(application, document),
                        performance = Performance(started),
                    };
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
            cancellationToken
        );
    }

    private async Task<object> InsertListAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var itemsNode = arguments.RequiredArray("items");
        if (itemsNode.GetArrayLength() is < 1 or > 1_000)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "items must contain between 1 and 1,000 entries"
            );
        }
        var items = new List<string>();
        var totalCharacters = 0;
        foreach (var itemNode in itemsNode.EnumerateArray())
        {
            if (itemNode.ValueKind != JsonValueKind.String)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Every list item must be a string"
                );
            }
            var item = itemNode.GetString() ?? "";
            if (item.Length == 0 || item.Contains('\x07'))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Every list item must be non-empty and cannot contain Word cell markers"
                );
            }
            var normalized = item
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Replace('\n', '\v');
            if (normalized.Length > 50_000)
            {
                throw new NativeToolException(
                    "LIMIT_EXCEEDED",
                    "One list item exceeds 50,000 characters"
                );
            }
            totalCharacters += normalized.Length;
            items.Add(normalized);
        }
        if (totalCharacters > 500_000)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "List text exceeds 500,000 characters"
            );
        }
        var listKind = arguments.String("list_kind", "bullet");
        var expectedListType = listKind switch
        {
            "bullet" => 2,
            "numbered" => 3,
            _ => throw new NativeToolException(
                "INVALID_INPUT",
                "list_kind must be bullet or numbered"
            ),
        };
        var target = arguments.String("target", "document_end");
        var selectionToken = arguments.String("selection_token");
        var replaceSelection = arguments.Boolean("replace_selection", false);
        var style = arguments.String("style");
        var activate = arguments.Boolean("activate", true);
        var optimizeScreenUpdates = arguments.Boolean("optimize_screen_updates", true);
        var expectedVersion = arguments.NullableInt64("expected_version");
        var formatting = arguments.TryGetProperty("formatting", out var formatNode)
            && formatNode.ValueKind == JsonValueKind.Object
            ? formatNode.Clone()
            : (JsonElement?)null;
        CheckVersion(record, expectedVersion);
        var listText = string.Join("\r", items);
        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                if (activate)
                {
                    document.Activate();
                }
                dynamic targetRange = ResolveInsertionRange(
                    (object)application,
                    (object)document,
                    record,
                    target,
                    selectionToken,
                    replaceSelection
                );
                var start = (int)targetRange.Start;
                var payload = ParagraphPayload(
                    (object)document,
                    start,
                    listText,
                    asNewParagraph: true
                );
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
                    undoRecord.StartCustomRecord("WordToolkit: native list insertion");
                    undoStarted = true;
                    targetRange.Text = payload.Text;
                    var listStart = start + payload.Prefix.Length;
                    var listEnd = listStart + NormalizeWordText(listText).Length;
                    dynamic listRange = document.Range(listStart, listEnd);
                    if (style.Length > 0)
                    {
                        listRange.Style = style;
                    }
                    if (formatting is not null)
                    {
                        ApplyFormatting(listRange, formatting.Value);
                    }
                    dynamic listFormat = listRange.ListFormat;
                    if (listKind == "bullet")
                    {
                        listFormat.ApplyBulletDefault(1);
                    }
                    else
                    {
                        listFormat.ApplyNumberDefault(1);
                    }
                    var actualListType = (int)listFormat.ListType;
                    if (actualListType != expectedListType)
                    {
                        throw new NativeToolException(
                            "EXTERNAL_TOOL_FAILED",
                            "Microsoft Word did not create the requested native list type",
                            new { expected = expectedListType, actual = actualListType }
                        );
                    }
                    dynamic resultRange = listRange.Duplicate;
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
                        list = new
                        {
                            kind = listKind,
                            item_count = items.Count,
                            list_type = actualListType,
                            range = new
                            {
                                start = (int)resultRange.Start,
                                end = (int)resultRange.End,
                            },
                            style,
                            native_verified = true,
                        },
                        content_returned = false,
                        document = DocumentInfo(application, document),
                        performance = Performance(started),
                    };
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
            cancellationToken
        );
    }

    private async Task<object> InsertImageAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var filePath = ValidateImagePath(arguments.String("file_path"));
        var target = arguments.String("target", "document_end");
        var selectionToken = arguments.String("selection_token");
        var replaceSelection = arguments.Boolean("replace_selection", false);
        var alternativeText = arguments.String("alternative_text");
        var title = arguments.String("title");
        var widthPoints = arguments.NullableDouble("width_points");
        var heightPoints = arguments.NullableDouble("height_points");
        var lockAspectRatio = arguments.Boolean("lock_aspect_ratio", true);
        var activate = arguments.Boolean("activate", true);
        var optimizeScreenUpdates = arguments.Boolean("optimize_screen_updates", true);
        var expectedVersion = arguments.NullableInt64("expected_version");
        if (alternativeText.Length > 2_000 || title.Length > 512)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "Image alternative_text or title exceeds the supported limit"
            );
        }
        ValidatePointDimension(widthPoints, "width_points");
        ValidatePointDimension(heightPoints, "height_points");
        CheckVersion(record, expectedVersion);
        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                if (activate)
                {
                    document.Activate();
                }
                dynamic range = ResolveInsertionRange(
                    (object)application,
                    (object)document,
                    record,
                    target,
                    selectionToken,
                    replaceSelection
                );
                var before = (int)document.InlineShapes.Count;
                dynamic? undoRecord = null;
                var undoStarted = false;
                bool? originalScreenUpdating = null;
                try
                {
                    if (optimizeScreenUpdates)
                    {
                        originalScreenUpdating = (bool)application.ScreenUpdating;
                        application.ScreenUpdating = false;
                    }
                    undoRecord = application.UndoRecord;
                    undoRecord.StartCustomRecord("WordToolkit: insert native image");
                    undoStarted = true;
                    dynamic image = document.InlineShapes.AddPicture(
                        filePath,
                        false,
                        true,
                        range
                    );
                    image.LockAspectRatio = lockAspectRatio ? WordTrue : WordFalse;
                    if (widthPoints is not null)
                    {
                        image.Width = (float)widthPoints.Value;
                    }
                    if (heightPoints is not null)
                    {
                        image.Height = (float)heightPoints.Value;
                    }
                    if (alternativeText.Length > 0)
                    {
                        image.AlternativeText = alternativeText;
                    }
                    if (title.Length > 0)
                    {
                        image.Title = title;
                    }
                    var after = (int)document.InlineShapes.Count;
                    if (after != before + 1)
                    {
                        throw new NativeToolException(
                            "EXTERNAL_TOOL_FAILED",
                            "Word did not create exactly one inline image",
                            new { before, after }
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
                        image = new
                        {
                            inline = true,
                            file_path = filePath,
                            width_points = Math.Round((double)image.Width, 3),
                            height_points = Math.Round((double)image.Height, 3),
                            lock_aspect_ratio = lockAspectRatio,
                            range = new
                            {
                                start = (int)image.Range.Start,
                                end = (int)image.Range.End,
                            },
                            native_verified = true,
                        },
                        document = DocumentInfo(application, document),
                        performance = Performance(started),
                    };
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
            cancellationToken
        );
    }

    private async Task<object> InsertCommentAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var text = arguments.String("text");
        var selectionToken = arguments.String("selection_token");
        var rangeToken = arguments.String("range_token");
        var optimizeScreenUpdates = arguments.Boolean("optimize_screen_updates", true);
        var expectedVersion = arguments.NullableInt64("expected_version");
        if (text.Length is < 1 or > 20_000)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "Comment text must be between 1 and 20,000 characters"
            );
        }
        if ((selectionToken.Length == 0) == (rangeToken.Length == 0))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Provide exactly one fresh selection_token or range_token"
            );
        }
        CheckVersion(record, expectedVersion);
        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                dynamic range = rangeToken.Length > 0
                    ? ResolveVerifiedRange(
                        (object)document,
                        record,
                        rangeToken,
                        requireNonEmpty: true
                    )
                    : ResolveVerifiedSelectionRange(
                        (object)application,
                        (object)document,
                        record,
                        selectionToken,
                        requireNonEmpty: true
                    );
                var before = (int)document.Comments.Count;
                dynamic? undoRecord = null;
                var undoStarted = false;
                bool? originalScreenUpdating = null;
                try
                {
                    if (optimizeScreenUpdates)
                    {
                        originalScreenUpdating = (bool)application.ScreenUpdating;
                        application.ScreenUpdating = false;
                    }
                    undoRecord = application.UndoRecord;
                    undoRecord.StartCustomRecord("WordToolkit: insert comment");
                    undoStarted = true;
                    dynamic comment = document.Comments.Add(range, text);
                    var after = (int)document.Comments.Count;
                    if (after != before + 1)
                    {
                        throw new NativeToolException(
                            "EXTERNAL_TOOL_FAILED",
                            "Word did not create exactly one comment",
                            new { before, after }
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
                        comment = new
                        {
                            index = after,
                            author = SafeString(() => (string?)comment.Author),
                            range = new
                            {
                                start = (int)comment.Scope.Start,
                                end = (int)comment.Scope.End,
                            },
                            native_verified = true,
                        },
                        content_returned = false,
                        document = DocumentInfo(application, document),
                        performance = Performance(started),
                    };
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
            cancellationToken
        );
    }

    private async Task<object> InsertNoteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var kind = arguments.String("kind", "footnote");
        var text = arguments.String("text");
        var customMark = arguments.String("custom_mark");
        var target = arguments.String("target", "cursor");
        var selectionToken = arguments.String("selection_token");
        var activate = arguments.Boolean("activate", true);
        var optimizeScreenUpdates = arguments.Boolean("optimize_screen_updates", true);
        var expectedVersion = arguments.NullableInt64("expected_version");
        if (kind is not ("footnote" or "endnote"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "kind must be footnote or endnote"
            );
        }
        if (text.Length is < 1 or > 100_000)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "Note text must be between 1 and 100,000 characters"
            );
        }
        if (customMark.Length > 10)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "custom_mark must not exceed 10 characters"
            );
        }
        if (target is not ("cursor" or "selection" or "document_end"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "target must be cursor, selection, or document_end"
            );
        }
        CheckVersion(record, expectedVersion);
        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                if (activate)
                {
                    document.Activate();
                }
                dynamic range;
                if (target == "document_end")
                {
                    var end = Math.Max(0, (int)document.Content.End - 1);
                    range = document.Range(end, end);
                }
                else
                {
                    range = ResolveVerifiedSelectionRange(
                        (object)application,
                        (object)document,
                        record,
                        selectionToken,
                        requireNonEmpty: false
                    );
                    var position = target == "cursor"
                        ? (int)range.End
                        : (int)range.End;
                    range.SetRange(position, position);
                }
                dynamic notes = kind == "footnote"
                    ? document.Footnotes
                    : document.Endnotes;
                var before = (int)notes.Count;
                dynamic? undoRecord = null;
                var undoStarted = false;
                bool? originalScreenUpdating = null;
                try
                {
                    if (optimizeScreenUpdates)
                    {
                        originalScreenUpdating = (bool)application.ScreenUpdating;
                        application.ScreenUpdating = false;
                    }
                    undoRecord = application.UndoRecord;
                    undoRecord.StartCustomRecord(
                        kind == "footnote"
                            ? "WordToolkit: insert footnote"
                            : "WordToolkit: insert endnote"
                    );
                    undoStarted = true;
                    var reference = customMark.Length > 0 ? customMark : Type.Missing;
                    dynamic note = notes.Add(range, reference, text);
                    var after = (int)notes.Count;
                    if (after != before + 1)
                    {
                        throw new NativeToolException(
                            "EXTERNAL_TOOL_FAILED",
                            "Word did not create exactly one note",
                            new { kind, before, after }
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
                        note = new
                        {
                            kind,
                            index = after,
                            custom_mark = customMark.Length > 0,
                            reference_range = new
                            {
                                start = (int)note.Reference.Start,
                                end = (int)note.Reference.End,
                            },
                            native_verified = true,
                        },
                        content_returned = false,
                        document = DocumentInfo(application, document),
                        performance = Performance(started),
                    };
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
            cancellationToken
        );
    }

    private async Task<object> SetHeaderFooterAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var sectionIndex = (int)(arguments.NullableInt64("section_index") ?? 1);
        var kind = arguments.String("kind");
        var variant = arguments.String("variant", "primary");
        var text = arguments.String("text");
        var enabled = arguments.Boolean("enabled", true);
        var linkToPrevious = arguments.Boolean("link_to_previous", false);
        var style = arguments.String("style");
        var formatting = arguments.TryGetProperty("formatting", out var formattingNode)
            && formattingNode.ValueKind == JsonValueKind.Object
            ? formattingNode.Clone()
            : (JsonElement?)null;
        var optimizeScreenUpdates = arguments.Boolean("optimize_screen_updates", true);
        var expectedVersion = arguments.NullableInt64("expected_version");
        if (sectionIndex is < 1 or > 10_000)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "section_index must be between 1 and 10,000"
            );
        }
        if (kind is not ("header" or "footer"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "kind must be header or footer"
            );
        }
        if (variant is not ("primary" or "first_page" or "even_pages"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "variant must be primary, first_page, or even_pages"
            );
        }
        if (text.Length > 200_000 || style.Length > 128)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "Header or footer text/style exceeds the supported limit"
            );
        }
        if (enabled && text.Length == 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Enabled headers and footers require non-empty text"
            );
        }
        CheckVersion(record, expectedVersion);
        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                var sectionCount = (int)document.Sections.Count;
                if (sectionIndex > sectionCount)
                {
                    throw new NativeToolException(
                        "DOCUMENT_NOT_FOUND",
                        "section_index exceeds the document section count",
                        new { section_index = sectionIndex, section_count = sectionCount }
                    );
                }
                dynamic section = document.Sections.Item(sectionIndex);
                var headerFooterIndex = variant switch
                {
                    "primary" => 1,
                    "first_page" => 2,
                    "even_pages" => 3,
                    _ => throw new InvalidOperationException(),
                };
                dynamic collection = kind == "header"
                    ? section.Headers
                    : section.Footers;
                dynamic headerFooter = collection.Item(headerFooterIndex);
                dynamic? undoRecord = null;
                var undoStarted = false;
                bool? originalScreenUpdating = null;
                try
                {
                    if (optimizeScreenUpdates)
                    {
                        originalScreenUpdating = (bool)application.ScreenUpdating;
                        application.ScreenUpdating = false;
                    }
                    undoRecord = application.UndoRecord;
                    undoRecord.StartCustomRecord(
                        enabled
                            ? $"WordToolkit: set {kind}"
                            : $"WordToolkit: clear {kind}"
                    );
                    undoStarted = true;
                    if (variant == "first_page")
                    {
                        section.PageSetup.DifferentFirstPageHeaderFooter = WordTrue;
                    }
                    else if (variant == "even_pages")
                    {
                        section.PageSetup.OddAndEvenPagesHeaderFooter = WordTrue;
                    }
                    headerFooter.LinkToPrevious = linkToPrevious;
                    if (enabled)
                    {
                        headerFooter.Exists = true;
                        dynamic range = headerFooter.Range;
                        range.Text = NormalizeWordText(text);
                        if (style.Length > 0)
                        {
                            range.Style = style;
                        }
                        if (formatting is not null)
                        {
                            ApplyFormatting(range, formatting.Value);
                        }
                    }
                    else
                    {
                        headerFooter.Range.Text = "";
                        headerFooter.Exists = false;
                    }
                    if ((bool)headerFooter.Exists != enabled)
                    {
                        throw new NativeToolException(
                            "EXTERNAL_TOOL_FAILED",
                            "Word did not apply the requested header/footer state"
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
                        header_footer = new
                        {
                            section_index = sectionIndex,
                            kind,
                            variant,
                            enabled,
                            link_to_previous = (bool)headerFooter.LinkToPrevious,
                            native_verified = true,
                        },
                        content_returned = false,
                        document = DocumentInfo(application, document),
                        performance = Performance(started),
                    };
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
            cancellationToken
        );
    }

    private Task<object> PreflightEquationsAsync(JsonElement arguments)
    {
        var equations = arguments.RequiredArray("equations");
        if (equations.GetArrayLength() is < 1 or > 200)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "equations must contain between 1 and 200 items"
            );
        }
        var results = equations
            .EnumerateArray()
            .Select(
                (equation, index) =>
                {
                    var prepared = EquationOperationFromArguments(equation);
                    return new
                    {
                        index,
                        valid = true,
                        input_format = prepared.InputFormat,
                        word_linear = prepared.Linear,
                        word_linear_characters = prepared.Linear.Length,
                        display = prepared.Display,
                        native_readback_required = prepared.ReadbackRequired,
                        native_readback_enabled = prepared.VerifyReadback,
                        native_style_rewrite_required = prepared.HasFormatting,
                        formatting_region_count = prepared.StyleCounts.Total,
                        formatting_regions = new
                        {
                            plain = prepared.StyleCounts.Plain,
                            bold = prepared.StyleCounts.Bold,
                            italic = prepared.StyleCounts.Italic,
                            bold_italic = prepared.StyleCounts.BoldItalic,
                            runs_and_controls = prepared.StyleCounts.RunsAndControls,
                            runs_only = prepared.StyleCounts.RunsOnly,
                            first_control = prepared.StyleCounts.FirstControl,
                        },
                        rules = new[]
                            {
                                prepared.InputFormat switch
                                {
                                    "latex" => "native_latex_to_unicodemath",
                                    "mathml" => "secure_mathml_to_unicodemath",
                                    "omml" => "secure_omml_to_unicodemath",
                                    _ => "native_unicodemath",
                                },
                                "single_com_omath_build_up",
                            }
                            .Concat(
                                prepared.VerifyReadback
                                    ? new[] { "bounded_native_omml_readback" }
                                    : Array.Empty<string>()
                            )
                            .Concat(
                                prepared.HasFormatting
                                    ? new[] { "verified_native_omml_style_rewrite" }
                                    : Array.Empty<string>()
                            )
                            .ToArray(),
                        warnings = Array.Empty<string>(),
                    };
                }
            )
            .ToArray();
        return Task.FromResult<object>(
            new
            {
                valid = true,
                equation_count = results.Length,
                equations = results,
                mutated_word = false,
                runtime = "dotnet-native",
                python_used = false,
            }
        );
    }

    private async Task<object> ValidateLiveDocumentAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var snapshot = await _host.InvokeAsync(
            application =>
            {
                dynamic document = ResolveDocument(application, record);
                if (!(bool)document.Saved)
                {
                    throw new NativeToolException(
                        "VERSION_CONFLICT",
                        "The live Word document has unsaved changes; validation never saves implicitly",
                        retryable: true
                    );
                }
                var path = DocumentFullName(document);
                if (DocumentPath(document).Length == 0 || !File.Exists(path))
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "The connected document has no saved DOCX path"
                    );
                }
                return new
                {
                    Path = path,
                    Info = DocumentInfo(application, document),
                };
            },
            cancellationToken
        );
        var temporary = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-native-validation-{Guid.NewGuid():N}.docx"
        );
        var issues = new List<object>();
        try
        {
            File.Copy(snapshot.Path, temporary, overwrite: false);
            using var package = WordprocessingDocument.Open(temporary, false);
            var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
            foreach (var error in validator.Validate(package).Take(500))
            {
                issues.Add(
                    new
                    {
                        id = error.Id,
                        description = error.Description,
                        error_type = error.ErrorType.ToString(),
                        part = error.Part?.Uri.ToString(),
                        path = error.Path?.XPath,
                        node = error.Node?.LocalName,
                    }
                );
            }
        }
        catch (OpenXmlPackageException exception)
        {
            throw new NativeToolException(
                "OOXML_INVALID",
                "Microsoft Open XML SDK could not open the live document snapshot",
                new { exception = exception.GetType().Name }
            );
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
        return new
        {
            live_document_id = record.Id,
            live_version = record.Version,
            validation = new
            {
                valid = issues.Count == 0,
                errors = issues.Count,
                issues,
                microsoft_sdk = new
                {
                    available = true,
                    valid = issues.Count == 0,
                    errors = issues.Count,
                },
            },
            snapshot_deleted = !File.Exists(temporary),
            document = snapshot.Info,
            runtime = "dotnet-native",
            python_used = false,
        };
    }

    private async Task<object> InspectUndoAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var maxEntries = (int)(arguments.NullableInt64("max_entries") ?? 20);
        if (maxEntries is < 1 or > 50)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_entries must be between 1 and 50"
            );
        }
        return await _host.InvokeAsync<object>(
            application =>
            {
                dynamic document = ResolveDocument(application, record);
                RequireActive(application, document);
                var (entries, available) = UndoEntries((object)application, maxEntries);
                var topEntry = entries.FirstOrDefault() ?? "";
                var eligible = available
                    && topEntry.StartsWith("WordToolkit:", StringComparison.Ordinal);
                var token = "";
                if (eligible)
                {
                    token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
                        .ToLowerInvariant();
                    _undoGrants[token] = new UndoGrant(
                        token,
                        record.Id,
                        record.Version,
                        topEntry
                    );
                    TrimUndoGrants();
                }
                return new
                {
                    live_document_id = record.Id,
                    live_version = record.Version,
                    available,
                    entries,
                    returned_count = entries.Count,
                    top_entry = topEntry,
                    wordtoolkit_undo_eligible = eligible,
                    undo_token = token,
                    policy = new
                    {
                        only_top_entry = true,
                        wordtoolkit_prefix_required = true,
                        raw_times_allowed = false,
                        fresh_token_required = true,
                        fails_closed_when_history_unavailable = true,
                    },
                    document = DocumentInfo(application, document),
                };
            },
            cancellationToken
        );
    }

    private async Task<object> UndoOperationAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var expectedVersion = arguments.NullableInt64("expected_version")
            ?? throw new NativeToolException(
                "INVALID_INPUT",
                "expected_version is required for guarded WordToolkit Undo"
            );
        var token = arguments.String("undo_token");
        CheckVersion(record, expectedVersion);
        if (!_undoGrants.TryGetValue(token, out var grant))
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "A fresh undo_token is required",
                retryable: true
            );
        }
        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireActive(application, document);
                var (entries, available) = UndoEntries((object)application, 1);
                var topEntry = entries.FirstOrDefault() ?? "";
                if (!available)
                {
                    throw new NativeToolException(
                        "LIVE_WORD_UNAVAILABLE",
                        "Word's Undo history is not accessible; guarded Undo fails closed",
                        retryable: true
                    );
                }
                if (
                    grant.DocumentId != record.Id
                    || grant.Version != record.Version
                    || !CryptographicOperations.FixedTimeEquals(
                        SHA256.HashData(Encoding.UTF8.GetBytes(grant.TopEntry)),
                        SHA256.HashData(Encoding.UTF8.GetBytes(topEntry))
                    )
                )
                {
                    throw new NativeToolException(
                        "VERSION_CONFLICT",
                        "The Word Undo stack changed after inspection",
                        retryable: true
                    );
                }
                if (!topEntry.StartsWith("WordToolkit:", StringComparison.Ordinal))
                {
                    throw new NativeToolException(
                        "AUTH_FORBIDDEN",
                        "The latest Word action was not created by WordToolkit",
                        new { top_entry = topEntry }
                    );
                }
                var undone = (bool)document.Undo(1);
                if (!undone)
                {
                    throw new NativeToolException(
                        "EXTERNAL_TOOL_FAILED",
                        "Word refused to undo the latest WordToolkit operation",
                        retryable: true
                    );
                }
                record.Version++;
                InvalidateSelectionGrants(record.Id);
                InvalidateRangeGrants(record.Id);
                InvalidateUndoGrants(record.Id);
                return new
                {
                    live_document_id = record.Id,
                    live_version = record.Version,
                    undone = true,
                    entry = topEntry,
                    document = DocumentInfo(application, document),
                };
            },
            cancellationToken
        );
    }

    private Task<object> InsertEquationAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var operation = EquationOperationFromArguments(arguments);
        return ApplyPreparedOperationsAsync(
            Record(arguments.String("live_document_id")),
            [operation],
            arguments.Boolean("activate", true),
            arguments.NullableInt64("expected_version"),
            optimizeScreenUpdates: true,
            arguments.String("target", "cursor"),
            arguments.String("selection_token"),
            arguments.Boolean("replace_selection", false),
            cancellationToken
        );
    }

    private Task<object> InsertEquationBatchAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var equations = arguments.RequiredArray("equations");
        if (equations.GetArrayLength() is < 1 or > 100)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "equations must contain between 1 and 100 items"
            );
        }
        var prepared = equations.EnumerateArray()
            .Select(EquationOperationFromArguments)
            .Cast<PreparedOperation>()
            .ToList();
        return ApplyPreparedOperationsAsync(
            Record(arguments.String("live_document_id")),
            prepared,
            arguments.Boolean("activate", true),
            arguments.NullableInt64("expected_version"),
            optimizeScreenUpdates: true,
            target: "document_end",
            selectionToken: "",
            replaceSelection: false,
            cancellationToken
        );
    }

    private Task<object> ApplyOperationsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var operations = arguments.RequiredArray("operations");
        if (operations.GetArrayLength() is < 1 or > 200)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "operations must contain between 1 and 200 items"
            );
        }
        var prepared = new List<PreparedOperation>();
        var totalText = 0;
        var equations = 0;
        foreach (var item in operations.EnumerateArray())
        {
            var type = item.String("type");
            if (type == "text")
            {
                var text = item.String("text");
                if (text.Length == 0)
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "Every text operation requires non-empty text"
                    );
                }
                if (text.Length > 200_000)
                {
                    throw new NativeToolException(
                        "LIMIT_EXCEEDED",
                        "One text operation exceeds 200,000 characters"
                    );
                }
                totalText += text.Length;
                var formatting = item.TryGetProperty("formatting", out var formatNode)
                    && formatNode.ValueKind == JsonValueKind.Object
                    ? formatNode.Clone()
                    : (JsonElement?)null;
                prepared.Add(
                    new PreparedTextOperation(
                        text,
                        item.Boolean("as_new_paragraph", false),
                        item.String("style"),
                        formatting
                    )
                );
            }
            else if (type == "equation")
            {
                equations++;
                prepared.Add(EquationOperationFromArguments(item));
            }
            else
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Operation type must be 'text' or 'equation'"
                );
            }
        }
        if (totalText > 500_000)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "Combined text exceeds 500,000 characters"
            );
        }
        if (equations > 100)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "At most 100 equations may be applied in one batch"
            );
        }
        return ApplyPreparedOperationsAsync(
            Record(arguments.String("live_document_id")),
            prepared,
            arguments.Boolean("activate", true),
            arguments.NullableInt64("expected_version"),
            arguments.Boolean("optimize_screen_updates", true),
            target: "document_end",
            selectionToken: "",
            replaceSelection: false,
            cancellationToken
        );
    }

    private async Task<object> ApplyPreparedOperationsAsync(
        LiveDocumentRecord record,
        IReadOnlyList<PreparedOperation> operations,
        bool activate,
        long? expectedVersion,
        bool optimizeScreenUpdates,
        string target,
        string selectionToken,
        bool replaceSelection,
        CancellationToken cancellationToken
    )
    {
        CheckVersion(record, expectedVersion);
        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                if (activate)
                {
                    document.Activate();
                }
                dynamic targetRange = ResolveInsertionRange(
                    (object)application,
                    (object)document,
                    record,
                    target,
                    selectionToken,
                    replaceSelection
                );
                var insertionStart = (int)targetRange.Start;
                var pieces = new List<string>(operations.Count);
                var segments = new List<(int Start, int End)>(operations.Count);
                var offset = 0;
                var previous = insertionStart > 0
                    ? (string?)document.Range(insertionStart - 1, insertionStart).Text ?? ""
                    : "";
                foreach (var operation in operations)
                {
                    var raw = NormalizeWordText(operation.Value);
                    var newParagraph = operation.AsNewParagraph;
                    var prefix = newParagraph
                        && insertionStart + offset > 0
                        && previous != "\r"
                            ? "\r"
                            : "";
                    var suffix = newParagraph ? "\r" : "";
                    var piece = prefix + raw + suffix;
                    segments.Add((offset + prefix.Length, offset + prefix.Length + raw.Length));
                    pieces.Add(piece);
                    offset += piece.Length;
                    if (piece.Length > 0)
                    {
                        previous = piece[^1].ToString(CultureInfo.InvariantCulture);
                    }
                }
                var payload = string.Concat(pieces);
                var beforeEquations = (int)document.OMaths.Count;
                dynamic? undoRecord = null;
                var undoStarted = false;
                bool? originalScreenUpdating = null;
                var results = new object?[operations.Count];
                var textRanges = new Dictionary<int, object>();
                var builtEquations = new Dictionary<int, BuiltEquationResult>();
                try
                {
                    if (optimizeScreenUpdates)
                    {
                        originalScreenUpdating = (bool)application.ScreenUpdating;
                        application.ScreenUpdating = false;
                    }
                    undoRecord = application.UndoRecord;
                    undoRecord.StartCustomRecord("WordToolkit: native mixed live batch");
                    undoStarted = true;
                    targetRange.Text = payload;

                    for (var index = 0; index < operations.Count; index++)
                    {
                        if (operations[index] is not PreparedTextOperation textOperation)
                        {
                            continue;
                        }
                        var segment = segments[index];
                        dynamic inserted = document.Range(
                            insertionStart + segment.Start,
                            insertionStart + segment.End
                        );
                        if (textOperation.Style.Length > 0)
                        {
                            inserted.Style = textOperation.Style;
                        }
                        if (textOperation.Formatting is not null)
                        {
                            ApplyFormatting(inserted, textOperation.Formatting.Value);
                        }
                        textRanges[index] = (object)inserted;
                    }

                    var equationIndexes = Enumerable.Range(0, operations.Count)
                        .Where(index => operations[index] is PreparedEquationOperation)
                        .ToArray();
                    for (var reverse = equationIndexes.Length - 1; reverse >= 0; reverse--)
                    {
                        var index = equationIndexes[reverse];
                        var equationOperation = (PreparedEquationOperation)operations[index];
                        var segment = segments[index];
                        dynamic equationRange = document.Range(
                            insertionStart + segment.Start,
                            insertionStart + segment.End
                        );
                        dynamic added = document.OMaths.Add(equationRange);
                        dynamic equation = added.OMaths.Item(1);
                        equation.BuildUp();
                        EquationStyleRewriteResult? styleRewrite = null;
                        EquationStyleVerification? styleVerification = null;
                        string readbackXml = "";
                        if (equationOperation.HasFormatting)
                        {
                            var equationStart = (int)equation.Range.Start;
                            styleRewrite = EquationStyleRewriter.Rewrite(
                                (string?)equation.Range.WordOpenXML ?? "",
                                equationOperation.StyleCounts
                            );
                            dynamic rewriteRange = equation.Range.Duplicate;
                            rewriteRange.InsertXML(styleRewrite.WordOpenXml);
                            dynamic rewrittenEquations = rewriteRange.OMaths;
                            if ((int)rewrittenEquations.Count != 1)
                            {
                                throw new NativeToolException(
                                    "EQUATION_INVALID",
                                    "Microsoft Word did not preserve exactly one styled native equation",
                                    new
                                    {
                                        equation_count = (int)rewrittenEquations.Count,
                                        equation_start = equationStart,
                                    }
                                );
                            }
                            equation = rewrittenEquations.Item(1);
                        }
                        equation.Type = equationOperation.Display ? 0 : 1;
                        if (styleRewrite is not null)
                        {
                            readbackXml = (string?)equation.Range.WordOpenXML ?? "";
                            styleVerification = EquationStyleRewriter.Verify(
                                readbackXml,
                                styleRewrite
                            );
                        }
                        EquationReadbackVerification? readback = null;
                        if (equationOperation.VerifyReadback)
                        {
                            if (readbackXml.Length == 0)
                            {
                                readbackXml =
                                    (string?)equation.Range.WordOpenXML ?? "";
                            }
                            readback = EquationReadbackVerifier.Verify(
                                readbackXml,
                                equationOperation.Linear
                            );
                        }
                        builtEquations[index] = new BuiltEquationResult(
                            (object)equation,
                            equationOperation,
                            readback,
                            styleRewrite,
                            styleVerification
                        );
                    }
                    var afterEquations = (int)document.OMaths.Count;
                    if (afterEquations != beforeEquations + equationIndexes.Length)
                    {
                        throw new NativeToolException(
                            "EQUATION_INVALID",
                            "Microsoft Word did not create the expected number of native equations",
                            new
                            {
                                before = beforeEquations,
                                after = afterEquations,
                                expected = equationIndexes.Length,
                            }
                        );
                    }
                    for (var index = 0; index < operations.Count; index++)
                    {
                        if (operations[index] is PreparedTextOperation textOperation)
                        {
                            dynamic inserted = textRanges[index];
                            results[index] = new
                            {
                                type = "text",
                                range = new
                                {
                                    start = (int)inserted.Start,
                                    end = (int)inserted.End,
                                },
                                style = textOperation.Style,
                            };
                            continue;
                        }
                        var built = builtEquations[index];
                        dynamic finalEquation = built.Equation;
                        var equationOperation = built.Operation;
                        var readback = built.Readback;
                        var styleRewrite = built.StyleRewrite;
                        var styleVerification = built.StyleVerification;
                        results[index] = new
                        {
                            type = "equation",
                            equation = new
                            {
                                input_format = equationOperation.InputFormat,
                                display = equationOperation.Display,
                                linear_input = equationOperation.Linear,
                                native_verified = true,
                                readback_verified = readback is not null,
                                readback_required = equationOperation.ReadbackRequired,
                                native_style_verified = styleVerification is not null,
                                formatting = styleVerification is null
                                    ? null
                                    : new
                                    {
                                        region_count = styleRewrite!.RegionCount,
                                        plain_region_count = equationOperation.StyleCounts.Plain,
                                        bold_region_count = equationOperation.StyleCounts.Bold,
                                        italic_region_count = equationOperation.StyleCounts.Italic,
                                        bold_italic_region_count = equationOperation.StyleCounts.BoldItalic,
                                        styled_run_count = styleVerification.StyledRunCount,
                                        plain_run_count = styleVerification.PlainRunCount,
                                        bold_run_count = styleVerification.BoldRunCount,
                                        italic_run_count = styleVerification.ItalicRunCount,
                                        bold_italic_run_count = styleVerification.BoldItalicRunCount,
                                        plain_control_count = styleVerification.PlainControlCount,
                                        bold_control_count = styleVerification.BoldControlCount,
                                        italic_control_count = styleVerification.ItalicControlCount,
                                        bold_italic_control_count = styleVerification.BoldItalicControlCount,
                                        expected_contract_sha256 = styleVerification.ExpectedContractSha256,
                                        actual_contract_sha256 = styleVerification.ActualContractSha256,
                                        internal_markers_returned = false,
                                        raw_omml_returned = false,
                                    },
                                readback = readback is null
                                    ? null
                                    : new
                                    {
                                        expected_contract_sha256 = readback.ExpectedContractSha256,
                                        actual_contract_sha256 = readback.ActualContractSha256,
                                        math_element_count = readback.MathElementCount,
                                        nary_count = readback.NaryCount,
                                        differential_count = readback.DifferentialCount,
                                        differential_placement_verified = readback.DifferentialPlacementVerified,
                                        raw_omml_returned = false,
                                    },
                                range = new
                                {
                                    start = (int)finalEquation.Range.Start,
                                    end = (int)finalEquation.Range.End,
                                },
                            },
                        };
                    }
                    undoRecord.EndCustomRecord();
                    undoStarted = false;
                    foreach (var equationIndex in equationIndexes)
                    {
                        var equationOperation =
                            (PreparedEquationOperation)operations[equationIndex];
                        _equationLearning.AddOrUpdate(
                            $"success:{equationOperation.InputFormat}",
                            1,
                            static (_, current) => current + 1
                        );
                    }
                    record.Version += operations.Count;
                    InvalidateSelectionGrants(record.Id);
                    InvalidateRangeGrants(record.Id);
                    return new
                    {
                        live_document_id = record.Id,
                        live_version = record.Version,
                        operation_count = operations.Count,
                        text_operation_count = operations.Count - equationIndexes.Length,
                        equation_operation_count = equationIndexes.Length,
                        operations = results,
                        document = DocumentInfo(application, document),
                        performance = Performance(started),
                    };
                }
                catch
                {
                    foreach (
                        var equationOperation in operations.OfType<PreparedEquationOperation>()
                    )
                    {
                        _equationLearning.AddOrUpdate(
                            $"failure:{equationOperation.InputFormat}",
                            1,
                            static (_, current) => current + 1
                        );
                    }
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
            cancellationToken
        );
    }

    private async Task<object> SaveAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var expectedVersion = arguments.NullableInt64("expected_version");
        CheckVersion(record, expectedVersion);
        return await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                RequireEditable(document);
                var path = DocumentPath(document);
                if (path.Length == 0)
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "The connected document has no file path; save it in Word before using live save"
                    );
                }
                document.Save();
                return new
                {
                    live_document_id = record.Id,
                    live_version = record.Version,
                    saved = (bool)document.Saved,
                    document = DocumentInfo(application, document),
                };
            },
            cancellationToken
        );
    }

    private async Task<object> ExportPdfAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var outputPath = ValidatePdfOutputPath(arguments.String("output_path"));
        var overwrite = arguments.Boolean("overwrite", false);
        var optimizeFor = arguments.String("optimize_for", "print");
        var bookmarkMode = arguments.String("bookmarks", "headings");
        var includeDocumentProperties = arguments.Boolean(
            "include_document_properties",
            true
        );
        var pdfA = arguments.Boolean("pdf_a", false);
        if (optimizeFor is not ("print" or "screen"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "optimize_for must be print or screen"
            );
        }
        if (bookmarkMode is not ("none" or "headings" or "bookmarks"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "bookmarks must be none, headings, or bookmarks"
            );
        }
        if (File.Exists(outputPath) && !overwrite)
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "The PDF output file already exists; set overwrite=true to replace it"
            );
        }
        var directory = Path.GetDirectoryName(outputPath)!;
        var temporaryPath = Path.Combine(
            directory,
            $".wordtoolkit-pdf-{Guid.NewGuid():N}.pdf"
        );
        var started = Stopwatch.GetTimestamp();
        object snapshot;
        try
        {
            snapshot = await _host.InvokeAsync<object>(
                application =>
                {
                    dynamic document = ResolveDocument(application, record);
                    document.ExportAsFixedFormat(
                        temporaryPath,
                        WordExportFormatPdf,
                        false,
                        optimizeFor == "print" ? 0 : 1,
                        0,
                        1,
                        1,
                        0,
                        includeDocumentProperties,
                        true,
                        bookmarkMode switch
                        {
                            "none" => 0,
                            "headings" => 1,
                            "bookmarks" => 2,
                            _ => 0,
                        },
                        true,
                        true,
                        pdfA
                    );
                    return DocumentInfo(application, document);
                },
                cancellationToken
            );
            if (!File.Exists(temporaryPath) || new FileInfo(temporaryPath).Length == 0)
            {
                throw new NativeToolException(
                    "EXTERNAL_TOOL_FAILED",
                    "Word did not create a non-empty PDF"
                );
            }
            if (overwrite && File.Exists(outputPath))
            {
                File.Replace(
                    temporaryPath,
                    outputPath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true
                );
            }
            else
            {
                File.Move(temporaryPath, outputPath, overwrite: false);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        return new
        {
            live_document_id = record.Id,
            live_version = record.Version,
            exported = true,
            output_path = outputPath,
            bytes = new FileInfo(outputPath).Length,
            overwrite,
            source_included_unsaved_changes = true,
            document = snapshot,
            performance = Performance(started),
        };
    }

    private async Task<object> CloseDocumentAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var record = Record(arguments.String("live_document_id"));
        var expectedVersion = arguments.NullableInt64("expected_version")
            ?? throw new NativeToolException(
                "INVALID_INPUT",
                "expected_version is required when closing a live document"
            );
        var saveChanges = arguments.String("save_changes");
        var outputPath = arguments.String("output_path");
        if (saveChanges is not ("save" or "discard"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "save_changes must be save or discard"
            );
        }
        if (saveChanges == "discard" && outputPath.Length > 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "output_path cannot be used when save_changes=discard"
            );
        }
        var resolvedOutputPath = outputPath.Length > 0
            ? ValidateNewDocxOutputPath(outputPath)
            : "";
        CheckVersion(record, expectedVersion);
        var result = await _host.InvokeAsync<object>(
            application =>
            {
                CheckVersion(record, expectedVersion);
                dynamic document = ResolveDocument(application, record);
                var documentInfo = DocumentInfo(application, document);
                var savedTo = "";
                if (saveChanges == "save")
                {
                    if (DocumentPath(document).Length == 0)
                    {
                        if (resolvedOutputPath.Length == 0)
                        {
                            throw new NativeToolException(
                                "INVALID_INPUT",
                                "An unsaved document requires a new output_path before it can be closed with save"
                            );
                        }
                        RequireEditable(document);
                        document.SaveAs2(
                            resolvedOutputPath,
                            WordFormatDocumentDefault
                        );
                        savedTo = resolvedOutputPath;
                    }
                    else
                    {
                        if (resolvedOutputPath.Length > 0)
                        {
                            throw new NativeToolException(
                                "INVALID_INPUT",
                                "output_path is only accepted for a previously unsaved document"
                            );
                        }
                        RequireEditable(document);
                        document.Save();
                        savedTo = DocumentFullName(document);
                    }
                }
                document.Close(WordDoNotSaveChanges);
                return new
                {
                    live_document_id = record.Id,
                    closed = true,
                    save_changes = saveChanges,
                    saved_to = savedTo,
                    previous_document = documentInfo,
                };
            },
            cancellationToken
        );
        _records.TryRemove(record.Id, out _);
        InvalidateSelectionGrants(record.Id);
        InvalidateRangeGrants(record.Id);
        InvalidateUndoGrants(record.Id);
        return result;
    }

    private async Task<object> QuitWordAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        if (!arguments.Boolean("confirm", false))
        {
            throw new NativeToolException(
                "AUTH_FORBIDDEN",
                "quit_word_application requires confirm=true"
            );
        }
        var saveChanges = arguments.String("save_changes");
        if (saveChanges is not ("save_all" or "discard_all"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "save_changes must be save_all or discard_all"
            );
        }
        var started = Stopwatch.GetTimestamp();
        var result = await _host.InvokeAsync<object>(
            application =>
            {
                var documentCount = (int)application.Documents.Count;
                var savedCount = 0;
                if (saveChanges == "save_all")
                {
                    for (var index = 1; index <= documentCount; index++)
                    {
                        dynamic document = application.Documents.Item(index);
                        if ((bool)document.Saved)
                        {
                            continue;
                        }
                        if (DocumentPath(document).Length == 0)
                        {
                            throw new NativeToolException(
                                "VERSION_CONFLICT",
                                "Word has an unsaved document without a path; save it explicitly before quit_word_application with save_all",
                                new { document = DocumentName(document) }
                            );
                        }
                        RequireEditable(document);
                    }
                    for (var index = 1; index <= documentCount; index++)
                    {
                        dynamic document = application.Documents.Item(index);
                        if (!(bool)document.Saved)
                        {
                            document.Save();
                            savedCount++;
                        }
                    }
                }
                application.Quit(WordDoNotSaveChanges);
                return new
                {
                    quit = true,
                    save_changes = saveChanges,
                    document_count = documentCount,
                    saved_document_count = savedCount,
                    runtime = "dotnet-native",
                    python_used = false,
                    performance = Performance(started),
                };
            },
            cancellationToken
        );
        _records.Clear();
        _selectionGrants.Clear();
        _undoGrants.Clear();
        _rangeGrants.Clear();
        _reviewGrants.Clear();
        return result;
    }

    private Task<object> DisconnectAsync(JsonElement arguments)
    {
        var id = arguments.String("live_document_id");
        _ = Record(id);
        _records.TryRemove(id, out _);
        InvalidateSelectionGrants(id);
        InvalidateRangeGrants(id);
        InvalidateUndoGrants(id);
        return Task.FromResult<object>(new { live_document_id = id, disconnected = true });
    }

    internal async Task<bool> UndoBenchmarkTransactionAsync(
        string documentId,
        long expectedVersion,
        CancellationToken cancellationToken = default
    )
    {
        var record = Record(documentId);
        CheckVersion(record, expectedVersion);
        return await _host.InvokeAsync(
            application =>
            {
                dynamic document = ResolveDocument(application, record);
                dynamic control = application.CommandBars.FindControl(Type: 6, Id: 128);
                if (control is null || (int)control.ListCount < 1)
                {
                    return false;
                }
                var top = ((string?)control.List(1) ?? "");
                if (!top.StartsWith("WordToolkit:", StringComparison.Ordinal))
                {
                    return false;
                }
                var undone = (bool)document.Undo(1);
                if (undone)
                {
                    record.Version++;
                }
                return undone;
            },
            cancellationToken
        );
    }

    private static PreparedEquationOperation EquationOperationFromArguments(
        JsonElement arguments
    )
    {
        foreach (var unsupportedAlias in new[]
        {
            "source_format",
            "equation_source_format",
            "format",
        })
        {
            if (arguments.TryGetProperty(unsupportedAlias, out _))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"Unknown equation field '{unsupportedAlias}'; use 'input_format'",
                    new
                    {
                        unsupported_field = unsupportedAlias,
                        accepted_field = "input_format",
                    }
                );
            }
        }
        var valueNode = arguments.Required("value");
        if (valueNode.ValueKind != JsonValueKind.String)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "The native preview currently requires a string equation value"
            );
        }
        var value = valueNode.GetString() ?? "";
        var inputFormat = arguments.String("input_format", "latex");
        if (inputFormat is not ("latex" or "unicodemath" or "mathml" or "omml"))
        {
            throw new NativeToolException(
                "EQUATION_INVALID",
                "The native equation engine accepts LaTeX, UnicodeMath, MathML, or OMML",
                new
                {
                    input_format = inputFormat,
                    supported = new[] { "latex", "unicodemath", "mathml", "omml" },
                }
            );
        }
        if (value.Length is < 1 or > 100_000)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "Equation length must be between 1 and 100,000 characters"
            );
        }
        var conversion = inputFormat switch
        {
            "latex" => LatexToUnicodeMath.ConvertPlan(value),
            "mathml" or "omml" => MathMarkupToUnicodeMath.ConvertPlan(
                value,
                inputFormat
            ),
            _ => EquationFormattingMarkers.Unstyled(
                WordLinearMathNormalizer.NormalizeForWord(value.Trim()),
                inputFormat
            ),
        };
        if (
            conversion.BuildLinear.Any(
                character =>
                    (character < 32 && character is not ('\t' or '\n' or '\r'))
                    || character == 127
            )
        )
        {
            throw new NativeToolException(
                "EQUATION_INVALID",
                "Equation input contains unsafe control characters"
            );
        }
        var readbackRequired =
            conversion.HasFormatting
            || EquationReadbackVerifier.RequiresReadback(conversion.Linear);
        return new PreparedEquationOperation(
            conversion.Linear,
            conversion.BuildLinear,
            arguments.Boolean("display", true),
            inputFormat,
            arguments.Boolean("verify_readback", false) || readbackRequired,
            readbackRequired,
            conversion.StyleCounts
        );
    }

    private static List<dynamic> ResolveSelector(
        dynamic application,
        string documentName,
        string fullPath,
        bool useActive
    )
    {
        var count = (int)application.Documents.Count;
        if (count == 0)
        {
            throw new NativeToolException(
                "DOCUMENT_NOT_FOUND",
                "Microsoft Word has no open documents"
            );
        }
        var matches = new List<dynamic>();
        if (useActive)
        {
            matches.Add(application.ActiveDocument);
            return matches;
        }
        for (var index = 1; index <= count; index++)
        {
            dynamic document = application.Documents.Item(index);
            var name = DocumentName(document);
            var candidateFullName = DocumentFullName(document);
            if (
                (documentName.Length > 0
                    && string.Equals(name, documentName, StringComparison.OrdinalIgnoreCase))
                || (fullPath.Length > 0
                    && string.Equals(
                        NormalizePath(candidateFullName),
                        NormalizePath(fullPath),
                        StringComparison.OrdinalIgnoreCase
                    ))
            )
            {
                matches.Add(document);
            }
        }
        if (documentName.Length == 0 && fullPath.Length == 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Choose the active document or provide an exact document selector"
            );
        }
        return matches;
    }

    private dynamic ResolveDocument(dynamic application, LiveDocumentRecord record)
    {
        var count = (int)application.Documents.Count;
        var identity = NormalizeIdentity(record.FullName, record.Name);
        var matches = new List<dynamic>();
        for (var index = 1; index <= count; index++)
        {
            dynamic document = application.Documents.Item(index);
            var name = DocumentName(document);
            var fullName = DocumentFullName(document);
            if (NormalizeIdentity(fullName, name) == identity)
            {
                matches.Add(document);
            }
        }
        if (matches.Count != 1)
        {
            throw new NativeToolException(
                "DOCUMENT_NOT_FOUND",
                "The connected Word document is no longer open or became ambiguous",
                new { matches = matches.Count },
                retryable: true
            );
        }
        return matches[0];
    }

    private dynamic ResolveInsertionRange(
        object applicationObject,
        object documentObject,
        LiveDocumentRecord record,
        string target,
        string selectionToken,
        bool replaceSelection
    )
    {
        dynamic application = applicationObject;
        dynamic document = documentObject;
        if (target == "document_end")
        {
            var end = Math.Max(0, (int)document.Content.End - 1);
            return document.Range(end, end);
        }
        if (target is not ("cursor" or "selection"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "target must be selection, cursor, or document_end"
            );
        }
        RequireActive(application, document);
        if (!_selectionGrants.TryGetValue(selectionToken, out var grant))
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "A fresh selection_token is required",
                retryable: true
            );
        }
        dynamic selection = application.Selection;
        dynamic range = selection.Range;
        var currentStart = (int)range.Start;
        var currentEnd = (int)range.End;
        var currentStory = (int)range.StoryType;
        var currentHash = SelectionContextHash(document, currentStart, currentEnd);
        if (
            grant.DocumentId != record.Id
            || grant.Version != record.Version
            || grant.WindowHwnd != ActiveWindowHwnd(application)
            || grant.StoryType != currentStory
            || grant.Start != currentStart
            || grant.End != currentEnd
            || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(grant.ContextHash),
                Convert.FromHexString(currentHash)
            )
        )
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "The Word selection changed after the token was issued",
                retryable: true
            );
        }
        if (currentStory != MainTextStory)
        {
            throw new NativeToolException(
                "AUTH_FORBIDDEN",
                "Cursor editing is limited to the main document story"
            );
        }
        if (currentStart != currentEnd && !replaceSelection)
        {
            throw new NativeToolException(
                "AUTH_FORBIDDEN",
                "A non-empty selection requires replace_selection=true"
            );
        }
        if (target == "cursor")
        {
            range.SetRange(currentEnd, currentEnd);
        }
        return range;
    }

    private dynamic ResolveVerifiedSelectionRange(
        object applicationObject,
        object documentObject,
        LiveDocumentRecord record,
        string selectionToken,
        bool requireNonEmpty
    )
    {
        dynamic application = applicationObject;
        dynamic document = documentObject;
        RequireActive(application, document);
        if (!_selectionGrants.TryGetValue(selectionToken, out var grant))
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "A fresh selection_token is required",
                retryable: true
            );
        }
        dynamic selection = application.Selection;
        dynamic range = selection.Range;
        var currentStart = (int)range.Start;
        var currentEnd = (int)range.End;
        var currentStory = (int)range.StoryType;
        var currentHash = SelectionContextHash(document, currentStart, currentEnd);
        if (
            grant.DocumentId != record.Id
            || grant.Version != record.Version
            || grant.WindowHwnd != ActiveWindowHwnd(application)
            || grant.StoryType != currentStory
            || grant.Start != currentStart
            || grant.End != currentEnd
            || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(grant.ContextHash),
                Convert.FromHexString(currentHash)
            )
        )
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "The Word selection changed after the token was issued",
                retryable: true
            );
        }
        if (currentStory != MainTextStory)
        {
            throw new NativeToolException(
                "AUTH_FORBIDDEN",
                "This operation is limited to a selection in the main document story"
            );
        }
        if (requireNonEmpty && currentStart == currentEnd)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "This operation requires a non-empty Word selection"
            );
        }
        return range;
    }

    private dynamic ResolveVerifiedRange(
        object documentObject,
        LiveDocumentRecord record,
        string rangeToken,
        bool requireNonEmpty
    )
    {
        dynamic document = documentObject;
        if (!_rangeGrants.TryGetValue(rangeToken, out var grant))
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "A fresh range_token from find_live_word_text is required",
                retryable: true
            );
        }
        var documentEnd = Math.Max(0, (int)document.Content.End - 1);
        if (
            grant.DocumentId != record.Id
            || grant.Version != record.Version
            || grant.Start < 0
            || grant.End < grant.Start
            || grant.End > documentEnd
        )
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "The Word range changed after the token was issued",
                retryable: true
            );
        }
        var currentHash = SelectionContextHash(document, grant.Start, grant.End);
        if (
            !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(grant.ContextHash),
                Convert.FromHexString(currentHash)
            )
        )
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "The Word range content changed after the token was issued",
                retryable: true
            );
        }
        if (requireNonEmpty && grant.Start == grant.End)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "This operation requires a non-empty Word range"
            );
        }
        return document.Range(grant.Start, grant.End);
    }

    private LiveDocumentRecord Record(string id)
    {
        if (id.Length == 0 || !_records.TryGetValue(id, out var record))
        {
            throw new NativeToolException(
                "DOCUMENT_NOT_FOUND",
                "The Word Live document handle was not found"
            );
        }
        return record;
    }

    private static void CheckVersion(LiveDocumentRecord record, long? expectedVersion)
    {
        if (expectedVersion is not null && expectedVersion.Value != record.Version)
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "The Word Live document version changed",
                new
                {
                    expected_version = expectedVersion.Value,
                    actual_version = record.Version,
                },
                retryable: true
            );
        }
    }

    private static void ValidateFindArguments(
        string searchText,
        int contextChars,
        int maxResults
    )
    {
        if (searchText.Length == 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "search_text must be a non-empty string"
            );
        }
        if (searchText.Length > 255)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "search_text exceeds Word's 255-character Find limit",
                new { length = searchText.Length, limit = 255 }
            );
        }
        var unsafeCharacters = searchText
            .Where(
                character =>
                    (character < 32 && character is not ('\t' or '\n' or '\r' or '\f'))
                    || character == 127
            )
            .Select(character => $"U+{(int)character:X4}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unsafeCharacters.Length > 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "search_text contains control characters that are unsafe for Word Find",
                new { characters = unsafeCharacters }
            );
        }
        if (contextChars is < 0 or > 2_000)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "context_chars must be between 0 and 2,000"
            );
        }
        if (maxResults is < 1 or > 5_001)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_results must be between 1 and 5,001"
            );
        }
    }

    private static List<LiveMatch> FindRanges(
        object documentObject,
        string searchText,
        bool matchCase,
        bool wholeWord,
        bool useWildcards,
        int contextChars,
        int maxResults,
        out bool truncated
    )
    {
        dynamic document = documentObject;
        var contentEnd = Math.Max(0, (int)document.Content.End);
        var cursor = 0;
        var matches = new List<LiveMatch>();
        while (cursor <= contentEnd && matches.Count <= maxResults)
        {
            dynamic searchRange = document.Range(cursor, contentEnd);
            dynamic find = searchRange.Find;
            find.ClearFormatting();
            find.Text = searchText;
            find.MatchCase = matchCase;
            find.MatchWholeWord = wholeWord && !useWildcards;
            find.MatchWildcards = useWildcards;
            find.Forward = true;
            find.Wrap = 0;
            find.Format = false;
            var found = (bool)find.Execute();
            if (!found)
            {
                break;
            }
            var start = (int)searchRange.Start;
            var end = (int)searchRange.End;
            if (end < start)
            {
                throw new NativeToolException(
                    "EXTERNAL_TOOL_FAILED",
                    "Word Find returned an invalid or backward range",
                    new { cursor, start, end },
                    retryable: true
                );
            }
            // Word can expand a Find range back to the containing OMath run.
            // The next forward search may therefore return the match that ended
            // at the current cursor. Ignore that duplicate and keep moving until
            // the range has left the native equation instead of failing the
            // whole find/replace transaction.
            if (start < cursor)
            {
                cursor = Math.Min(contentEnd + 1, cursor + 1);
                continue;
            }
            if (start == end)
            {
                cursor = Math.Min(contentEnd + 1, Math.Max(cursor + 1, end + 1));
                continue;
            }
            var contextStart = Math.Max(0, start - contextChars);
            var contextEnd = Math.Min(contentEnd, end + contextChars);
            var rawContext = (string?)document.Range(contextStart, contextEnd).Text ?? "";
            var (context, contextTruncated) = CleanWordPreview(
                rawContext,
                Math.Max(1, (contextChars * 2) + 255)
            );
            var rawMatch = (string?)searchRange.Text ?? "";
            var (matchText, matchTruncated) = CleanWordPreview(rawMatch, 255);
            matches.Add(
                new LiveMatch(
                    start,
                    end,
                    matchText,
                    matchTruncated,
                    context,
                    contextTruncated
                )
            );
            cursor = end > cursor ? end : cursor + 1;
        }
        truncated = matches.Count > maxResults;
        if (truncated)
        {
            matches.RemoveRange(maxResults, matches.Count - maxResults);
        }
        return matches;
    }

    private static (string Text, bool Truncated) CleanWordPreview(
        string value,
        int maxCharacters
    )
    {
        var cleaned = value
            .Replace('\r', '\n')
            .Replace("\x07", "", StringComparison.Ordinal)
            .Replace("\0", "", StringComparison.Ordinal);
        return (
            cleaned[..Math.Min(cleaned.Length, maxCharacters)],
            cleaned.Length > maxCharacters
        );
    }

    private static object DocumentInfo(dynamic application, dynamic document)
    {
        var name = DocumentName(document);
        var fullName = DocumentFullName(document);
        var path = DocumentPath(document);
        var activeName = "";
        var activeFullName = "";
        try
        {
            activeName = DocumentName(application.ActiveDocument);
            activeFullName = DocumentFullName(application.ActiveDocument);
        }
        catch
        {
            // Word may have no active document during a UI transition.
        }
        return new
        {
            name,
            full_name = fullName,
            path,
            saved_to_disk = path.Length > 0,
            active = NormalizeIdentity(fullName, name)
                == NormalizeIdentity(activeFullName, activeName),
            window_hwnd = ActiveWindowHwnd(application),
            saved = DocumentSaved(document),
            read_only = DocumentReadOnly(document),
            compatibility_mode = DocumentCompatibilityMode(document),
            paragraph_count = DocumentParagraphCount(document),
            equation_count = DocumentEquationCount(document),
            table_count = DocumentTableCount(document),
            field_count = DocumentFieldCount(document),
            bookmark_count = DocumentBookmarkCount(document),
            inline_image_count = DocumentInlineShapeCount(document),
            floating_shape_count = DocumentShapeCount(document),
            comment_count = DocumentCommentCount(document),
            footnote_count = DocumentFootnoteCount(document),
            endnote_count = DocumentEndnoteCount(document),
            section_count = DocumentSectionCount(document),
        };
    }

    private static void RequireEditable(dynamic document)
    {
        if (DocumentReadOnly(document))
        {
            throw new NativeToolException("AUTH_FORBIDDEN", "The Word document is read-only");
        }
        var protection = DocumentProtectionType(document);
        if (protection != NoProtection)
        {
            throw new NativeToolException("AUTH_FORBIDDEN", "The Word document is protected");
        }
        try
        {
            if ((bool)document.Final)
            {
                throw new NativeToolException(
                    "AUTH_FORBIDDEN",
                    "The Word document is marked final"
                );
            }
        }
        catch (NativeToolException)
        {
            throw;
        }
        catch
        {
            // Older Word versions may not expose Document.Final.
        }
    }

    private static void RequireActive(dynamic application, dynamic document)
    {
        var documentIdentity = NormalizeIdentity(
            DocumentFullName(document),
            DocumentName(document)
        );
        var activeIdentity = NormalizeIdentity(
            DocumentFullName(application.ActiveDocument),
            DocumentName(application.ActiveDocument)
        );
        if (documentIdentity != activeIdentity)
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "The connected document is not the active Word document",
                retryable: true
            );
        }
    }

    private static (string Text, string Prefix, string Suffix) ParagraphPayload(
        dynamic document,
        int start,
        string text,
        bool asNewParagraph
    )
    {
        var normalized = NormalizeWordText(text);
        var previous = start > 0
            ? (string?)document.Range(start - 1, start).Text ?? ""
            : "";
        var prefix = asNewParagraph && start > 0 && previous != "\r" ? "\r" : "";
        var suffix = asNewParagraph ? "\r" : "";
        return (prefix + normalized + suffix, prefix, suffix);
    }

    private static string NormalizeWordText(string value)
    {
        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace('\n', '\r');
    }

    private static void ApplyFormatting(dynamic range, JsonElement formatting)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "font_name",
            "font_size_pt",
            "font_color_rgb",
            "bold",
            "italic",
            "underline",
            "strike",
            "all_caps",
            "small_caps",
            "hidden",
            "paragraph_alignment",
            "space_before_pt",
            "space_after_pt",
            "left_indent_pt",
            "right_indent_pt",
            "first_line_indent_pt",
            "keep_with_next",
            "keep_together",
            "page_break_before",
            "widow_control",
        };
        foreach (var property in formatting.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"Unsupported formatting field: {property.Name}"
                );
            }
        }
        dynamic font = range.Font;
        dynamic paragraph = range.ParagraphFormat;
        if (formatting.TryGetProperty("font_name", out var fontName))
        {
            font.Name = fontName.GetString() ?? "";
        }
        if (formatting.TryGetProperty("font_size_pt", out var fontSize))
        {
            var value = fontSize.GetDouble();
            if (value is < 1 or > 200)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "font_size_pt must be between 1 and 200"
                );
            }
            font.Size = value;
        }
        if (formatting.TryGetProperty("font_color_rgb", out var fontColor))
        {
            font.Color = ParseWordColor(fontColor.GetString() ?? "");
        }
        SetWordBoolean(font, formatting, "bold", "Bold");
        SetWordBoolean(font, formatting, "italic", "Italic");
        SetWordBoolean(font, formatting, "strike", "StrikeThrough");
        SetWordBoolean(font, formatting, "all_caps", "AllCaps");
        SetWordBoolean(font, formatting, "small_caps", "SmallCaps");
        SetWordBoolean(font, formatting, "hidden", "Hidden");
        if (formatting.TryGetProperty("underline", out var underline))
        {
            font.Underline = underline.GetBoolean() ? 1 : 0;
        }
        if (formatting.TryGetProperty("paragraph_alignment", out var alignment))
        {
            paragraph.Alignment = (alignment.GetString() ?? "") switch
            {
                "left" => 0,
                "center" => 1,
                "right" => 2,
                "justify" => 3,
                _ => throw new NativeToolException(
                    "INVALID_INPUT",
                    "Unsupported paragraph_alignment"
                ),
            };
        }
        SetFloat(paragraph, formatting, "space_before_pt", "SpaceBefore", 0, 1584);
        SetFloat(paragraph, formatting, "space_after_pt", "SpaceAfter", 0, 1584);
        SetFloat(paragraph, formatting, "left_indent_pt", "LeftIndent", -1584, 1584);
        SetFloat(paragraph, formatting, "right_indent_pt", "RightIndent", -1584, 1584);
        SetFloat(
            paragraph,
            formatting,
            "first_line_indent_pt",
            "FirstLineIndent",
            -1584,
            1584
        );
        SetWordBoolean(paragraph, formatting, "keep_with_next", "KeepWithNext");
        SetWordBoolean(paragraph, formatting, "keep_together", "KeepTogether");
        SetWordBoolean(paragraph, formatting, "page_break_before", "PageBreakBefore");
        SetWordBoolean(paragraph, formatting, "widow_control", "WidowControl");
    }

    private static void SetWordBoolean(
        dynamic target,
        JsonElement formatting,
        string source,
        string destination
    )
    {
        if (!formatting.TryGetProperty(source, out var value))
        {
            return;
        }
        SetDynamicProperty(target, destination, value.GetBoolean() ? -1 : 0);
    }

    private static void SetFloat(
        dynamic target,
        JsonElement formatting,
        string source,
        string destination,
        double minimum,
        double maximum
    )
    {
        if (!formatting.TryGetProperty(source, out var valueNode))
        {
            return;
        }
        var value = valueNode.GetDouble();
        if (value < minimum || value > maximum)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{source} must be between {minimum} and {maximum}"
            );
        }
        SetDynamicProperty(target, destination, value);
    }

    private static void SetDynamicProperty(dynamic target, string property, object value)
    {
        var dispatch = (object)target;
        dispatch.GetType().InvokeMember(
            property,
            System.Reflection.BindingFlags.SetProperty,
            binder: null,
            target: dispatch,
            args: [value],
            culture: CultureInfo.InvariantCulture
        );
    }

    private static int ParseWordColor(string value)
    {
        if (
            value.Length != 7
            || value[0] != '#'
            || !int.TryParse(
                value.AsSpan(1),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var rgb
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "font_color_rgb must use #RRGGBB"
            );
        }
        var red = (rgb >> 16) & 0xFF;
        var green = (rgb >> 8) & 0xFF;
        var blue = rgb & 0xFF;
        return red | (green << 8) | (blue << 16);
    }

    private static void Rollback(dynamic document, dynamic? undoRecord, ref bool undoStarted)
    {
        if (!undoStarted)
        {
            return;
        }
        try
        {
            undoRecord?.EndCustomRecord();
        }
        catch
        {
            // The rollback still attempts one bounded document Undo.
        }
        undoStarted = false;
        try
        {
            _ = (bool)document.Undo(1);
        }
        catch
        {
            // The original error remains authoritative.
        }
    }

    private static object Performance(long startedTimestamp)
    {
        return new
        {
            runtime = "dotnet-native",
            python_used = false,
            persistent_com_sta = true,
            com_attachments = 0,
            total_ms = Math.Round(
                Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds,
                3
            ),
        };
    }

    private static string SelectionContextHash(dynamic document, int start, int end)
    {
        var documentEnd = Math.Max(0, (int)document.Content.End - 1);
        var contextStart = Math.Max(0, start - 64);
        var contextEnd = Math.Min(documentEnd, end + 64);
        var text = (string?)document.Range(contextStart, contextEnd).Text ?? "";
        var payload = $"{start}\0{end}\0{text}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }

    private void TrimSelectionGrants()
    {
        if (_selectionGrants.Count <= 2_048)
        {
            return;
        }
        foreach (var key in _selectionGrants.Keys.Take(_selectionGrants.Count - 1_024))
        {
            _selectionGrants.TryRemove(key, out _);
        }
    }

    private void TrimUndoGrants()
    {
        if (_undoGrants.Count <= 1_024)
        {
            return;
        }
        foreach (var key in _undoGrants.Keys.Take(_undoGrants.Count - 512))
        {
            _undoGrants.TryRemove(key, out _);
        }
    }

    private void TrimRangeGrants()
    {
        if (_rangeGrants.Count <= 2_048)
        {
            return;
        }
        foreach (var key in _rangeGrants.Keys.Take(_rangeGrants.Count - 1_024))
        {
            _rangeGrants.TryRemove(key, out _);
        }
    }

    private void InvalidateSelectionGrants(string documentId)
    {
        foreach (
            var pair in _selectionGrants.Where(
                item => item.Value.DocumentId == documentId
            )
        )
        {
            _selectionGrants.TryRemove(pair.Key, out _);
        }
    }

    private void InvalidateUndoGrants(string documentId)
    {
        foreach (var pair in _undoGrants.Where(item => item.Value.DocumentId == documentId))
        {
            _undoGrants.TryRemove(pair.Key, out _);
        }
    }

    private void InvalidateRangeGrants(string documentId)
    {
        foreach (var pair in _rangeGrants.Where(item => item.Value.DocumentId == documentId))
        {
            _rangeGrants.TryRemove(pair.Key, out _);
        }
        InvalidateReviewGrants(documentId);
    }

    private static (List<string> Entries, bool Available) UndoEntries(
        object applicationObject,
        int maxEntries
    )
    {
        try
        {
            dynamic application = applicationObject;
            dynamic control = application.CommandBars.FindControl(Type: 6, Id: 128);
            if (control is null)
            {
                return ([], false);
            }
            var count = Math.Max(0, (int)control.ListCount);
            var entries = new List<string>();
            for (var index = 1; index <= Math.Min(count, maxEntries); index++)
            {
                var value = (string?)control.List(index) ?? "";
                entries.Add(value[..Math.Min(value.Length, 512)]);
            }
            return (entries, true);
        }
        catch
        {
            return ([], false);
        }
    }

    private static int ActiveWindowHwnd(dynamic application)
    {
        try
        {
            return (int)application.ActiveWindow.Hwnd;
        }
        catch
        {
            return 0;
        }
    }

    private static string DocumentName(dynamic document)
    {
        try
        {
            return Convert.ToString(document.Name, CultureInfo.InvariantCulture) ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string DocumentFullName(dynamic document)
    {
        try
        {
            return Convert.ToString(document.FullName, CultureInfo.InvariantCulture) ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string DocumentPath(dynamic document)
    {
        try
        {
            return Convert.ToString(document.Path, CultureInfo.InvariantCulture) ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static bool DocumentSaved(dynamic document)
    {
        try
        {
            return (bool)document.Saved;
        }
        catch
        {
            return false;
        }
    }

    private static bool DocumentReadOnly(dynamic document)
    {
        try
        {
            return (bool)document.ReadOnly;
        }
        catch
        {
            return false;
        }
    }

    private static int DocumentCompatibilityMode(dynamic document)
    {
        try
        {
            return (int)document.CompatibilityMode;
        }
        catch
        {
            return 0;
        }
    }

    private static int DocumentProtectionType(dynamic document)
    {
        try
        {
            return (int)document.ProtectionType;
        }
        catch
        {
            return NoProtection;
        }
    }

    private static int DocumentParagraphCount(dynamic document)
    {
        try
        {
            return (int)document.Paragraphs.Count;
        }
        catch
        {
            return 0;
        }
    }

    private static int DocumentEquationCount(dynamic document)
    {
        try
        {
            return (int)document.OMaths.Count;
        }
        catch
        {
            return 0;
        }
    }

    private static int DocumentTableCount(dynamic document)
    {
        try
        {
            return (int)document.Tables.Count;
        }
        catch
        {
            return 0;
        }
    }

    private static int DocumentFieldCount(dynamic document)
    {
        try
        {
            return (int)document.Fields.Count;
        }
        catch
        {
            return 0;
        }
    }

    private static int DocumentBookmarkCount(dynamic document)
    {
        try
        {
            return (int)document.Bookmarks.Count;
        }
        catch
        {
            return 0;
        }
    }

    private static int DocumentInlineShapeCount(dynamic document)
    {
        try
        {
            return (int)document.InlineShapes.Count;
        }
        catch
        {
            return 0;
        }
    }

    private static int DocumentShapeCount(dynamic document)
    {
        try
        {
            return (int)document.Shapes.Count;
        }
        catch
        {
            return 0;
        }
    }

    private static int DocumentCommentCount(dynamic document)
    {
        try
        {
            return (int)document.Comments.Count;
        }
        catch
        {
            return 0;
        }
    }

    private static int DocumentFootnoteCount(dynamic document)
    {
        try
        {
            return (int)document.Footnotes.Count;
        }
        catch
        {
            return 0;
        }
    }

    private static int DocumentEndnoteCount(dynamic document)
    {
        try
        {
            return (int)document.Endnotes.Count;
        }
        catch
        {
            return 0;
        }
    }

    private static int DocumentSectionCount(dynamic document)
    {
        try
        {
            return (int)document.Sections.Count;
        }
        catch
        {
            return 0;
        }
    }

    private static string SafeString(Func<string?> value)
    {
        try
        {
            return value() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string ValidateExistingDocumentPath(string value)
    {
        var path = ResolveAbsolutePath(value, "file_path");
        if (!OpenableDocumentExtensions.Contains(Path.GetExtension(path)))
        {
            throw new NativeToolException(
                "UNSUPPORTED_FORMAT",
                "The native opener accepts Word-readable DOC, DOCX, DOCM, DOT, DOTX, DOTM, ODT, RTF, TXT, PDF, HTML/MHTML, or XML files"
            );
        }
        if (!File.Exists(path))
        {
            throw new NativeToolException(
                "DOCUMENT_NOT_FOUND",
                "The requested Word document does not exist"
            );
        }
        return path;
    }

    private static string ValidateImagePath(string value)
    {
        var path = ResolveAbsolutePath(value, "file_path");
        if (!ImageExtensions.Contains(Path.GetExtension(path)))
        {
            throw new NativeToolException(
                "UNSUPPORTED_FORMAT",
                "Image must use BMP, EMF, GIF, JPEG, PNG, SVG, TIFF, or WMF"
            );
        }
        if (!File.Exists(path))
        {
            throw new NativeToolException(
                "DOCUMENT_NOT_FOUND",
                "The requested image file does not exist"
            );
        }
        if (new FileInfo(path).Length > 100 * 1024 * 1024)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "Image file exceeds the 100 MiB limit"
            );
        }
        return path;
    }

    private static string ValidateNewDocxOutputPath(string value)
    {
        var path = ResolveAbsolutePath(value, "output_path");
        if (
            !string.Equals(
                Path.GetExtension(path),
                ".docx",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new NativeToolException(
                "UNSUPPORTED_FORMAT",
                "output_path must use the .docx extension"
            );
        }
        ValidateOutputDirectory(path);
        if (File.Exists(path))
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "The DOCX output file already exists"
            );
        }
        return path;
    }

    private static string ValidatePdfOutputPath(string value)
    {
        var path = ResolveAbsolutePath(value, "output_path");
        if (
            !string.Equals(
                Path.GetExtension(path),
                ".pdf",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new NativeToolException(
                "UNSUPPORTED_FORMAT",
                "output_path must use the .pdf extension"
            );
        }
        ValidateOutputDirectory(path);
        return path;
    }

    private static string ResolveAbsolutePath(string value, string argumentName)
    {
        if (value.Length == 0 || value.Length > 32_767)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{argumentName} must be a non-empty Windows path within 32,767 characters"
            );
        }
        if (!Path.IsPathFullyQualified(value))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{argumentName} must be an absolute path"
            );
        }
        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception exception)
            when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{argumentName} is not a valid Windows path"
            );
        }
    }

    private static void ValidateOutputDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? "";
        if (directory.Length == 0 || !Directory.Exists(directory))
        {
            throw new NativeToolException(
                "DOCUMENT_NOT_FOUND",
                "The output directory does not exist"
            );
        }
    }

    private static void ValidatePointDimension(double? value, string name)
    {
        if (value is not null && value is (< 1 or > 10_000))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} must be between 1 and 10,000 points"
            );
        }
    }

    private static string NormalizeIdentity(string fullName, string name)
    {
        var selected = fullName.Length > 0 ? fullName : name;
        return NormalizePath(selected);
    }

    private static string NormalizePath(string value)
    {
        if (value.Length == 0)
        {
            return "";
        }
        try
        {
            return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        }
        catch
        {
            return value.ToUpperInvariant();
        }
    }

    private abstract record PreparedOperation(string Value, bool AsNewParagraph);

    private sealed record PreparedTextOperation(
        string Text,
        bool NewParagraph,
        string Style,
        JsonElement? Formatting
    ) : PreparedOperation(Text, NewParagraph);

    private sealed record PreparedEquationOperation(
        string Linear,
        string BuildLinear,
        bool Display,
        string InputFormat,
        bool VerifyReadback,
        bool ReadbackRequired,
        EquationStyleCounts StyleCounts
    )
        : PreparedOperation(BuildLinear, Display)
    {
        internal bool HasFormatting => StyleCounts.Total > 0;
    }

    private sealed record BuiltEquationResult(
        object Equation,
        PreparedEquationOperation Operation,
        EquationReadbackVerification? Readback,
        EquationStyleRewriteResult? StyleRewrite,
        EquationStyleVerification? StyleVerification
    );

    private sealed record LiveMatch(
        int Start,
        int End,
        string Text,
        bool TextTruncated,
        string Context,
        bool ContextTruncated
    );
}
