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
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Observability;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Publishing;
using WordToolkit.LibreOffice;
using WordToolkit.Native.Equations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService : IToolHandler
{
    private readonly LiveOperationReceiptStore _operationReceipts = new();
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
    private readonly Func<WordOperationResourceLease> _operationResourceLeaseFactory;
    private readonly WordOperationObservability _observability;
    private readonly ILibreOfficeBackendProbeProvider _libreOfficeBackendProbeProvider;
    private readonly ILibreOfficeUnoRenderProvider _libreOfficeUnoRenderProvider;
    private readonly ConcurrentDictionary<string, LiveDocumentRecord> _records = new();
    private readonly ConcurrentDictionary<string, QuarantinedLiveDocumentRecord> _quarantinedRecords =
        new();
    private readonly ConcurrentDictionary<string, SelectionGrant> _selectionGrants = new();
    private readonly ConcurrentDictionary<string, UndoGrant> _undoGrants = new();
    private readonly ConcurrentDictionary<string, RangeGrant> _rangeGrants = new();
    private readonly ConcurrentDictionary<string, EquationGrant> _equationGrants = new();
    private readonly ConcurrentDictionary<string, SmartArtTextEditGrant> _smartArtTextEditGrants =
        new();
    private readonly ConcurrentDictionary<string, SmartArtLayoutGrant> _smartArtLayoutGrants = new();
    private readonly byte[] _smartArtFingerprintKey = RandomNumberGenerator.GetBytes(32);
    private readonly ConcurrentDictionary<string, CachedSemanticIndex> _semanticIndexes = new();
    private readonly object _semanticIndexGate = new();

    internal Action<string, string>? BeforeCreateNewPublication { get; set; }

    public WordLiveService(IWordComHost host)
        : this(
            host,
            () => new WordOperationResourceLease(),
            WordOperationObservability.Disabled,
            new LibreOfficeBackendProbeProvider(),
            new LibreOfficeUnoRenderProvider()
        )
    { }

    internal WordLiveService(
        IWordComHost host,
        Func<WordOperationResourceLease> operationResourceLeaseFactory
    )
        : this(
            host,
            operationResourceLeaseFactory,
            WordOperationObservability.Disabled,
            new LibreOfficeBackendProbeProvider(),
            new LibreOfficeUnoRenderProvider()
        )
    { }

    internal WordLiveService(
        IWordComHost host,
        Func<WordOperationResourceLease> operationResourceLeaseFactory,
        WordOperationObservability observability
    )
        : this(
            host,
            operationResourceLeaseFactory,
            observability,
            new LibreOfficeBackendProbeProvider(),
            new LibreOfficeUnoRenderProvider()
        )
    { }

    internal WordLiveService(
        IWordComHost host,
        Func<WordOperationResourceLease> operationResourceLeaseFactory,
        WordOperationObservability observability,
        ILibreOfficeBackendProbeProvider libreOfficeBackendProbeProvider
    )
        : this(
            host,
            operationResourceLeaseFactory,
            observability,
            libreOfficeBackendProbeProvider,
            new LibreOfficeUnoRenderProvider()
        )
    { }

    internal WordLiveService(
        IWordComHost host,
        Func<WordOperationResourceLease> operationResourceLeaseFactory,
        WordOperationObservability observability,
        ILibreOfficeBackendProbeProvider libreOfficeBackendProbeProvider,
        ILibreOfficeUnoRenderProvider libreOfficeUnoRenderProvider
    )
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(operationResourceLeaseFactory);
        ArgumentNullException.ThrowIfNull(observability);
        ArgumentNullException.ThrowIfNull(libreOfficeBackendProbeProvider);
        ArgumentNullException.ThrowIfNull(libreOfficeUnoRenderProvider);
        _host = host;
        _operationResourceLeaseFactory = operationResourceLeaseFactory;
        _observability = observability;
        _libreOfficeBackendProbeProvider = libreOfficeBackendProbeProvider;
        _libreOfficeUnoRenderProvider = libreOfficeUnoRenderProvider;
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
            "create_live_word_equation_document" => CreateEquationDocumentAsync(arguments, cancellationToken),
            "compile_tex_document" => CompileTexDocumentAsync(arguments, cancellationToken),
            "open_live_word_document" => OpenDocumentAsync(
                arguments,
                cancellationToken
            ),
            "publish_ooxml_package_to_live_word" => PublishOoxmlPackageToLiveWordAsync(
                arguments,
                cancellationToken
            ),
            "connect_live_word_document" => ConnectAsync(arguments, cancellationToken),
            "inspect_ooxml_package" => InspectPackageAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_encryption" => InspectEncryptionAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_signatures" => InspectSignaturesAsync(
                arguments,
                cancellationToken
            ),
            "transform_ooxml_package" => TransformPackageAsync(
                arguments,
                cancellationToken
            ),
            "convert_ooxml_flat_opc" => ConvertFlatOpcPackageAsync(
                arguments,
                cancellationToken
            ),
            "inspect_wordtoolkit_extensions" => InspectExtensionsAsync(
                arguments,
                cancellationToken
            ),
            "inspect_libreoffice_backend" => InspectLibreOfficeBackendAsync(
                arguments,
                cancellationToken
            ),
            "inspect_wordtoolkit_observability" => InspectObservabilityAsync(
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
            "render_ooxml_semantic_html" => RenderPackageSemanticHtmlAsync(
                arguments,
                cancellationToken
            ),
            "render_ooxml_semantic_svg" => RenderPackageSemanticSvgAsync(
                arguments,
                cancellationToken
            ),
            "render_ooxml_fixed_artifacts" => RenderPackageFixedArtifactsAsync(
                arguments,
                cancellationToken
            ),
            "render_ooxml_libreoffice_artifacts" =>
                RenderPackageLibreOfficeArtifactsAsync(arguments, cancellationToken),
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
                cancellationToken,
                BeforeCreateNewPublication
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
            "plan_ooxml_patch_rollback" => PlanPackagePatchRollbackAsync(
                arguments,
                cancellationToken
            ),
            "apply_ooxml_patch_rollback" => ApplyPackagePatchRollbackAsync(
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
            "inspect_ooxml_template_style_alignment" =>
                InspectPackageTemplateStyleAlignmentAsync(
                    arguments,
                    cancellationToken
                ),
            "plan_ooxml_template_style_alignment" =>
                PlanPackageTemplateStyleAlignmentAsync(
                    arguments,
                    cancellationToken
                ),
            "apply_ooxml_template_style_alignment" =>
                ApplyPackageTemplateStyleAlignmentAsync(
                    arguments,
                    cancellationToken
                ),
            "inspect_ooxml_numbering" => InspectPackageNumberingAsync(
                arguments,
                cancellationToken
            ),
            "plan_ooxml_numbering_repair" => PlanPackageNumberingRepairAsync(
                arguments,
                cancellationToken
            ),
            "apply_ooxml_numbering_repair" => ApplyPackageNumberingRepairAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_numbering_rebuild_candidates" =>
                InspectPackageNumberingRebuildCandidatesAsync(
                    arguments,
                    cancellationToken
                ),
            "plan_ooxml_numbering_rebuild" => PlanPackageNumberingRebuildAsync(
                arguments,
                cancellationToken
            ),
            "apply_ooxml_numbering_rebuild" => ApplyPackageNumberingRebuildAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_notes" => InspectPackageNotesAsync(
                arguments,
                cancellationToken
            ),
            "plan_ooxml_note_repair" => PlanPackageNoteRepairAsync(
                arguments,
                cancellationToken
            ),
            "apply_ooxml_note_repair" => ApplyPackageNoteRepairAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_equation_repairs" => InspectPackageEquationRepairsAsync(
                arguments,
                cancellationToken
            ),
            "plan_ooxml_equation_repair" => PlanPackageEquationRepairAsync(
                arguments,
                cancellationToken
            ),
            "apply_ooxml_equation_repair" => ApplyPackageEquationRepairAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_relationships" => InspectPackageRelationshipsAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_heading_outline" => InspectPackageHeadingOutlineAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_semantic_roles" => InspectPackageSemanticRolesAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_ocr_candidates" => InspectPackageOcrCandidatesAsync(
                arguments,
                cancellationToken
            ),
            "run_ooxml_ocr" => RunPackageOcrAsync(
                arguments,
                cancellationToken
            ),
            "plan_ooxml_relationship_repair" => PlanPackageRelationshipRepairAsync(
                arguments,
                cancellationToken
            ),
            "apply_ooxml_relationship_repair" => ApplyPackageRelationshipRepairAsync(
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
            "inspect_ooxml_bibliography" => InspectPackageBibliographyAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_mail_merge" => InspectPackageMailMergeAsync(
                arguments,
                cancellationToken
            ),
            "plan_ooxml_mail_merge_schema_binding" =>
                PlanPackageMailMergeSchemaBindingAsync(
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
            "inspect_ooxml_diagrams" => InspectPackageDiagramsAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_figures" => InspectPackageFiguresAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_content_controls" =>
                InspectPackageContentControlsAsync(arguments, cancellationToken),
            "inspect_ooxml_active_content" => InspectPackageActiveContentAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_properties" => InspectPackageDocumentPropertiesAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_tables" => InspectPackageTablesAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_markup_compatibility" =>
                InspectPackageMarkupCompatibilityAsync(arguments, cancellationToken),
            "analyze_ooxml_document" => AnalyzePackageDocumentAsync(
                arguments,
                cancellationToken
            ),
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
            "plan_ooxml_format" => PlanPackageFormatAsync(
                arguments,
                cancellationToken
            ),
            "apply_ooxml_format" => ApplyPackageFormatAsync(
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
            "plan_ooxml_comment_body_edits" => PlanPackageCommentBodyEditsAsync(
                arguments,
                cancellationToken
            ),
            "apply_ooxml_comment_body_edits" => ApplyPackageCommentBodyEditsAsync(
                arguments,
                cancellationToken
            ),
            "inspect_ooxml_equation_paragraph_rewrites" =>
                InspectPackageEquationParagraphRewritesAsync(
                    arguments,
                    cancellationToken
                ),
            "plan_ooxml_equation_paragraph_rewrites" =>
                PlanPackageEquationParagraphRewritesAsync(
                    arguments,
                    cancellationToken
                ),
            "apply_ooxml_equation_paragraph_rewrites" =>
                ApplyPackageEquationParagraphRewritesAsync(
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
            "inspect_live_word_drawing_layout" => InspectDrawingLayoutAsync(
                arguments,
                cancellationToken
            ),
            "inspect_live_word_version_profile" => InspectVersionProfileAsync(
                arguments,
                cancellationToken
            ),
            "probe_live_word_feature_behaviors" => ProbeFeatureBehaviorsAsync(
                arguments,
                cancellationToken
            ),
            "prepare_live_word_smartart_text_edits" => PrepareSmartArtTextEditsAsync(
                arguments,
                cancellationToken
            ),
            "apply_live_word_smartart_text_edits" => ApplySmartArtTextEditsAsync(
                arguments,
                cancellationToken
            ),
            "inspect_live_word_smartart_layouts" => InspectSmartArtLayoutsAsync(arguments, cancellationToken),
            "insert_live_word_smartart" => InsertSmartArtAsync(arguments, cancellationToken),
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
            "insert_live_word_dropdowns" => InsertDropdownControlsAsync(
                arguments,
                cancellationToken
            ),
            "insert_live_word_caption" => InsertCaptionAsync(
                arguments,
                cancellationToken
            ),
            "insert_live_word_table_of_figures" => InsertTableOfFiguresAsync(
                arguments,
                cancellationToken
            ),
            "insert_live_word_table_of_contents" => InsertTableOfContentsAsync(
                arguments,
                cancellationToken
            ),
            "mark_live_word_authority_citation" => MarkAuthorityCitationAsync(
                arguments,
                cancellationToken
            ),
            "insert_live_word_table_of_authorities" => InsertTableOfAuthoritiesAsync(
                arguments,
                cancellationToken
            ),
            "mark_live_word_index_entry" => MarkIndexEntryAsync(
                arguments,
                cancellationToken
            ),
            "insert_live_word_index" => InsertIndexAsync(arguments, cancellationToken),
            "update_live_word_reference_tables" => UpdateReferenceTablesAsync(
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
            "preflight_live_word_equations" => PreflightEquationsAsync(
                arguments,
                cancellationToken
            ),
            "inspect_live_word_equations" => InspectLiveEquationsAsync(
                arguments,
                cancellationToken
            ),
            "update_live_word_equation" => UpdateLiveEquationAsync(
                arguments,
                cancellationToken
            ),
            "apply_live_word_operations" => ApplyOperationsAsync(
                arguments,
                cancellationToken
            ),
            "get_live_word_operation_status" => Task.FromResult(GetOperationStatus(arguments)),
            "preflight_live_word_operations" => PreflightOperationsAsync(
                arguments,
                cancellationToken
            ),
            "validate_live_word_document" => ValidateLiveDocumentAsync(
                arguments,
                cancellationToken
            ),
            "export_live_word_pdf" => ExportPdfAsync(arguments, cancellationToken),
            "export_live_word_artifacts" => ExportLiveWordArtifactsAsync(arguments, cancellationToken),
            "save_live_word_document" => SaveAsync(arguments, cancellationToken),
            "close_live_word_document" => CloseDocumentAsync(
                arguments,
                cancellationToken
            ),
            "quit_word_application" => QuitWordAsync(arguments, cancellationToken),
            "disconnect_live_word_document" => DisconnectAsync(arguments, cancellationToken),
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
                WordComReplaySafety.ReplaySafe,
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
                    application_owned_by_runtime = _host.ApplicationOwnedByRuntime,
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
                ThrowIfDocumentIdentityQuarantined(identity);
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
                ThrowIfDocumentIdentityQuarantined(identity);
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
        var lifecycle = arguments.String("lifecycle", "persistent");
        if (lifecycle is not ("persistent" or "scratch"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "lifecycle must be persistent or scratch"
            );
        }
        if (lifecycle == "scratch" && outputPath.Length > 0)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "A scratch document cannot have output_path; save a reviewed result explicitly as a persistent document"
            );
        }
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
                dynamic document = lifecycle == "scratch"
                    ? application.Documents.Add(Visible: false)
                    : application.Documents.Add();
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
                    RuntimeCreated = true,
                    AutoCloseOnDisconnect = lifecycle == "scratch",
                    Version = 0,
                };
                _records[record.Id] = record;
                return new
                {
                    live_document_id = record.Id,
                    live_version = record.Version,
                    created = true,
                    saved_to_disk = resolvedOutputPath.Length > 0,
                    lifecycle,
                    auto_close_on_disconnect = record.AutoCloseOnDisconnect,
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
            WordComReplaySafety.ReplaySafe,
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
            WordComReplaySafety.ReplaySafe,
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
            WordComReplaySafety.ReplaySafe,
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
                var rollbackSnapshot = CaptureLiveRollbackSnapshot(document, record.Version);
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
                catch (Exception exception)
                {
                    RollbackPreparedOperationsOrThrow(
                        document,
                        undoRecord,
                        ref undoStarted,
                        undoRecord is not null,
                        rollbackSnapshot,
                        record,
                        exception
                    );
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
        var formatting = PrepareFormattingArgument(arguments);
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
                var rollbackSnapshot = CaptureLiveRollbackSnapshot(document, record.Version);
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
                    var formattingReadback = formatting is null
                        ? null
                        : PublicFormattingReadback(
                            CaptureRequestedFormatting(range, formatting.Value),
                            formatting.Value
                        );
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
                        native_formatting_verified = formatting is not null,
                        formatting_readback = formattingReadback,
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
                        undoRecord is not null,
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
        var formatting = PrepareFormattingArgument(arguments);
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
                var rollbackSnapshot = CaptureLiveRollbackSnapshot(document, record.Version);
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
                    IReadOnlyDictionary<string, object?>? formattingReadback = null;
                    if (formatting is not null)
                    {
                        ApplyFormatting(inserted, formatting.Value);
                        formattingReadback = PublicFormattingReadback(
                            CaptureRequestedFormatting(inserted, formatting.Value),
                            formatting.Value
                        );
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
                        native_formatting_verified = formatting is not null,
                        formatting_readback = formattingReadback,
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
                        undoRecord is not null,
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
                            .Replace("\n", "\n", StringComparison.Ordinal)
                            .Replace('\n', '\n')
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
        var tsv = string.Join("\n", rows.Select(row => string.Join("\t", row)));
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
                var rollbackSnapshot = CaptureLiveRollbackSnapshot(document, record.Version);
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
                catch (Exception exception)
                {
                    RollbackPreparedOperationsOrThrow(
                        document,
                        undoRecord,
                        ref undoStarted,
                        undoRecord is not null,
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
                .Replace("\n", "\n", StringComparison.Ordinal)
                .Replace('\n', '\n')
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
        var formatting = PrepareFormattingArgument(arguments);
        CheckVersion(record, expectedVersion);
        var listText = string.Join("\n", items);
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
                var rollbackSnapshot = CaptureLiveRollbackSnapshot(document, record.Version);
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
                catch (Exception exception)
                {
                    RollbackPreparedOperationsOrThrow(
                        document,
                        undoRecord,
                        ref undoStarted,
                        undoRecord is not null,
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
                var rollbackSnapshot = CaptureLiveRollbackSnapshot(document, record.Version);
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
                catch (Exception exception)
                {
                    RollbackPreparedOperationsOrThrow(
                        document,
                        undoRecord,
                        ref undoStarted,
                        undoRecord is not null,
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
                var rollbackSnapshot = CaptureLiveRollbackSnapshot(document, record.Version);
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
                catch (Exception exception)
                {
                    RollbackPreparedOperationsOrThrow(
                        document,
                        undoRecord,
                        ref undoStarted,
                        undoRecord is not null,
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
                var rollbackSnapshot = CaptureLiveRollbackSnapshot(document, record.Version);
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
                catch (Exception exception)
                {
                    RollbackPreparedOperationsOrThrow(
                        document,
                        undoRecord,
                        ref undoStarted,
                        undoRecord is not null,
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
        var formatting = PrepareFormattingArgument(arguments);
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
                var rollbackSnapshot = CaptureLiveRollbackSnapshot(document, record.Version);
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
                catch (Exception exception)
                {
                    RollbackPreparedOperationsOrThrow(
                        document,
                        undoRecord,
                        ref undoStarted,
                        undoRecord is not null,
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
                }
            },
            cancellationToken
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
            WordComReplaySafety.ReplaySafe,
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
            WordComReplaySafety.ReplaySafe,
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

    private async Task<object> ApplyOperationsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var prepared = PrepareOperations(arguments.RequiredArray("operations"));
        var record = Record(arguments.String("live_document_id"));
        var expectedVersion = arguments.NullableInt64("expected_version");
        var receiptIntent = PrepareLiveOperationReceiptIntent(arguments);
        var receipt = _operationReceipts.GetOrCreate(
            receiptIntent.OperationId,
            receiptIntent.Fingerprint,
            () => ApplyPreparedOperationsAsync(
                record,
                prepared,
                arguments.Boolean("activate", true),
                expectedVersion,
                arguments.Boolean("optimize_screen_updates", true),
                target: "document_end",
                selectionToken: "",
                replaceSelection: false,
                CancellationToken.None
            )
        );
        var result = await receipt.Execution.Value.WaitAsync(cancellationToken);
        return AddLiveOperationReceiptMetadata(
            result,
            receipt.OperationId,
            replayed: !receipt.WasCreatedForCaller
        );
    }

    private object GetOperationStatus(JsonElement arguments) =>
        _operationReceipts.Status(arguments.String("operation_id"));

    private static IReadOnlyList<PreparedOperation> PrepareOperations(JsonElement operations)
    {
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
        var operationIndex = 0;
        foreach (var item in operations.EnumerateArray())
        {
            try
            {
                var type = item.String("type");
                if (type == "text")
                {
                    var hasRuns = item.TryGetProperty("runs", out var runsNode);
                    var hasText = item.TryGetProperty("text", out _);
                    if (hasRuns && hasText)
                    {
                        throw new NativeToolException(
                            "INVALID_INPUT",
                            "A text operation accepts either text or runs, not both"
                        );
                    }
                    var runs = hasRuns
                        ? ParseTextRuns(runsNode)
                        : Array.Empty<PreparedTextRun>();
                    var text = hasRuns
                        ? string.Concat(runs.Select(run => run.Text))
                        : item.String("text");
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
                    var formatting = PrepareFormattingArgument(item);
                    prepared.Add(
                        new PreparedTextOperation(
                            text,
                            item.Boolean("as_new_paragraph", false),
                            item.String("style"),
                            formatting,
                            runs
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
            catch (Exception exception)
            {
                throw WithFailedOperationIndex(exception, operationIndex);
            }
            operationIndex++;
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
        return prepared;
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
        var batchComplexity = BatchComplexity.For(operations);
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
                var insertionEnd = (int)targetRange.End;
                var rollbackSnapshot = CaptureLiveRollbackSnapshot(
                    document,
                    insertionStart,
                    insertionEnd,
                    record.Version
                );
                var previous = insertionStart > 0
                    ? (string?)document.Range(insertionStart - 1, insertionStart).Text ?? ""
                    : "";
                var batchPayload = BuildPreparedBatchPayload(
                    operations,
                    insertionStart,
                    previous
                );
                StagedPreparedBatch? staged = null;
                var stagingOpen = false;
                try
                {
                    staged = StagePreparedBatch(
                        application,
                        document,
                        operations,
                        batchPayload.Payload,
                        batchPayload.Segments
                    );
                    stagingOpen = true;
                    EnsureTargetUnchangedBeforePublication(
                        document,
                        rollbackSnapshot,
                        record
                    );
                }
                catch (Exception stagingException)
                {
                    Exception effectiveException = stagingException;
                    if (staged is not null && stagingOpen)
                    {
                        try
                        {
                            CloseStagedPreparedBatch(
                                staged,
                                targetMutationAttempted: false,
                                originalFailure: stagingException
                            );
                        }
                        catch (Exception cleanupException)
                        {
                            effectiveException = cleanupException;
                        }
                        stagingOpen = false;
                    }
                    if (
                        effectiveException is NativeToolException
                        {
                            ErrorCode: "ROLLBACK_FAILED"
                        }
                    )
                    {
                        throw effectiveException;
                    }
                    EnsureTargetUnchangedBeforePublication(
                        document,
                        rollbackSnapshot,
                        record,
                        effectiveException
                    );
                    throw effectiveException;
                }

                var beforeContentEnd = (int)document.Content.End;
                var beforeEquations = (int)document.OMaths.Count;
                var replacedEquations = (int)targetRange.OMaths.Count;
                var replacedLength = insertionEnd - insertionStart;
                dynamic? undoRecord = null;
                var undoStarted = false;
                var mutationAttempted = false;
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
                    mutationAttempted = true;
                    dynamic stagedRange = staged!.PublicationRange;
                    targetRange.FormattedText = stagedRange.FormattedText;

                    CloseStagedPreparedBatch(staged, targetMutationAttempted: true);
                    stagingOpen = false;
                    document.Activate();

                    var afterContentEnd = (int)document.Content.End;
                    var publishedLength = afterContentEnd - beforeContentEnd + replacedLength;
                    if (publishedLength != staged.PublicationLength)
                    {
                        throw new NativeToolException(
                            "PUBLICATION_INVALID",
                            "Microsoft Word published a different range length than the verified isolated batch",
                            new
                            {
                                expected_length = staged.PublicationLength,
                                actual_length = publishedLength,
                                raw_document_content_returned = false,
                            }
                        );
                    }
                    dynamic publishedRange = document.Range(
                        insertionStart,
                        insertionStart + publishedLength
                    );
                    if (
                        !string.Equals(
                            staged.TextSha256,
                            RollbackSha256((string?)publishedRange.Text ?? ""),
                            StringComparison.Ordinal
                        )
                    )
                    {
                        throw new NativeToolException(
                            "PUBLICATION_INVALID",
                            "Microsoft Word changed staged text during live publication",
                            new { raw_document_content_returned = false }
                        );
                    }

                    var expectedEquationCount =
                        beforeEquations - replacedEquations + staged.EquationIndexes.Count;
                    var afterEquations = (int)document.OMaths.Count;
                    var publishedEquations = (int)publishedRange.OMaths.Count;
                    if (
                        afterEquations != expectedEquationCount
                        || publishedEquations != staged.EquationIndexes.Count
                    )
                    {
                        throw new NativeToolException(
                            "EQUATION_INVALID",
                            "Microsoft Word did not preserve the staged native equation set during publication",
                            new
                            {
                                before = beforeEquations,
                                replaced = replacedEquations,
                                after = afterEquations,
                                expected = expectedEquationCount,
                                published = publishedEquations,
                                expected_published = staged.EquationIndexes.Count,
                            }
                        );
                    }

                    for (var index = 0; index < operations.Count; index++)
                    {
                        if (operations[index] is not PreparedTextOperation textOperation)
                        {
                            continue;
                        }
                        dynamic? inserted = null;
                        try
                        {
                            var expectedRange = staged.OperationRanges[index];
                            inserted = document.Range(
                                insertionStart + expectedRange.Start,
                                insertionStart + expectedRange.End
                            );
                            VerifyPublishedTextOperation(
                                inserted,
                                textOperation,
                                expectedRange,
                                index
                            );
                            textRanges[index] = (object)inserted;
                            inserted = null;
                        }
                        catch (Exception exception)
                        {
                            throw WithFailedOperationIndex(exception, index);
                        }
                        finally
                        {
                            FinalReleaseBatchComObject(inserted);
                        }
                    }
                    for (var ordinal = 0; ordinal < staged.EquationIndexes.Count; ordinal++)
                    {
                        var index = staged.EquationIndexes[ordinal];
                        dynamic? equation = null;
                        try
                        {
                            equation = publishedRange.OMaths.Item(ordinal + 1);
                            var expectedRange = staged.OperationRanges[index];
                            var actualStart = (int)equation.Range.Start;
                            var actualEnd = (int)equation.Range.End;
                            if (
                                actualStart != insertionStart + expectedRange.Start
                                || actualEnd != insertionStart + expectedRange.End
                            )
                            {
                                throw new NativeToolException(
                                    "EQUATION_INVALID",
                                    "Microsoft Word moved or resized a staged equation during publication",
                                    new
                                    {
                                        expected_start = insertionStart + expectedRange.Start,
                                        expected_end = insertionStart + expectedRange.End,
                                        actual_start = actualStart,
                                        actual_end = actualEnd,
                                    }
                                );
                            }
                            builtEquations[index] = VerifyPublishedEquation(
                                equation,
                                staged.Equations[index]
                            );
                            equation = null;
                        }
                        catch (Exception exception)
                        {
                            throw WithFailedOperationIndex(exception, index);
                        }
                        finally
                        {
                            FinalReleaseBatchComObject(equation);
                        }
                    }
                    for (var index = 0; index < operations.Count; index++)
                    {
                        try
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
                                    run_count = textOperation.Runs.Count > 0 ? textOperation.Runs.Count : 1,
                                    native_formatting_verified = textOperation.Formatting is not null
                                        || textOperation.Runs.Any(run => run.Formatting is not null),
                                    formatting_readback_returned = false,
                                };
                                continue;
                            }
                            var built = builtEquations[index];
                            dynamic finalEquation = built.Equation;
                            var equationOperation = built.Operation;
                            var readback = built.Readback;
                            var styleRewrite = built.StyleRewrite;
                            var styleVerification = built.StyleVerification;
                            var directOmml = built.DirectOmml;
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
                                    direct_omml = equationOperation.DirectPlan is null
                                        ? null
                                        : new
                                        {
                                            source_validated = true,
                                            native_semantic_verified = directOmml is not null,
                                            namespace_identity = equationOperation.DirectPlan.NamespaceIdentity,
                                            expected_semantic_sha256 =
                                                equationOperation.DirectPlan.SemanticSha256,
                                            actual_semantic_sha256 =
                                                directOmml?.ActualCombinedSemanticSha256,
                                            expected_equation_semantic_sha256 =
                                                directOmml?.ExpectedEquationSemanticSha256,
                                            actual_equation_semantic_sha256 =
                                                directOmml?.ActualEquationSemanticSha256,
                                            expected_paragraph_properties_sha256 =
                                                directOmml?.ExpectedParagraphPropertiesSha256,
                                            actual_paragraph_properties_sha256 =
                                                directOmml?.ActualParagraphPropertiesSha256,
                                            expected_paragraph_justification =
                                                directOmml?.ExpectedParagraphJustification,
                                            actual_paragraph_justification =
                                                directOmml?.ActualParagraphJustification,
                                            element_count = directOmml?.ElementCount,
                                            raw_omml_returned = false,
                                        },
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
                        catch (Exception exception)
                        {
                            throw WithFailedOperationIndex(exception, index);
                        }
                    }
                    undoRecord.EndCustomRecord();
                    undoStarted = false;
                    foreach (var equationIndex in staged.EquationIndexes)
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
                        text_operation_count = operations.Count
                            - staged.EquationIndexes.Count,
                        equation_operation_count = staged.EquationIndexes.Count,
                        operations = results,
                        document = DocumentInfo(application, document),
                        performance = Performance(started, batchComplexity),
                    };
                }
                catch (Exception exception)
                {
                    Exception effectiveException = exception;
                    if (stagingOpen)
                    {
                        try
                        {
                            CloseStagedPreparedBatch(
                                staged!,
                                targetMutationAttempted: mutationAttempted,
                                originalFailure: exception
                            );
                        }
                        catch (Exception cleanupException)
                        {
                            effectiveException = cleanupException;
                        }
                        stagingOpen = false;
                    }
                    try
                    {
                        document.Activate();
                    }
                    catch
                    {
                        // Rollback verification below is authoritative.
                    }
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
                    RollbackPreparedOperationsOrThrow(
                        document,
                        undoRecord,
                        ref undoStarted,
                        mutationAttempted,
                        rollbackSnapshot,
                        record,
                        effectiveException,
                        independentRestore: (Action)(() =>
                            RestoreLiveMainStoryFromFlatOpc(
                                application,
                                document,
                                staged!.BaselineFlatOpc
                            ))
                    );
                    throw effectiveException;
                }
                finally
                {
                    foreach (var range in textRanges.Values)
                    {
                        FinalReleaseBatchComObject(range);
                    }
                    foreach (var built in builtEquations.Values)
                    {
                        FinalReleaseBatchComObject(built.Equation);
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

    private static void FinalReleaseBatchComObject(object? value)
    {
        if (value is null)
        {
            return;
        }
        try
        {
            if (Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
            }
        }
        catch (InvalidComObjectException)
        {
            // Another owned reference already released this RCW. Cleanup must not
            // replace the authoritative publication or rollback result.
        }
    }

    private static BuiltEquationResult BuildVerifiedNativeEquation(
        dynamic document,
        int start,
        int end,
        PreparedEquationOperation equationOperation,
        int expectedEquationCollectionIndex
    )
    {
        if (equationOperation.DirectPlan is not null)
            return BuildVerifiedDirectOmmlEquation(document, start, end, equationOperation, expectedEquationCollectionIndex);
        dynamic? equationRange = null;
        dynamic? added = null;
        dynamic? equation = null;
        var equationOwnershipTransferred = false;
        try
        {
            try
            {
                equationRange = document.Range(start, end);
                dynamic addedOMaths = document.OMaths;
                try
                {
                    added = addedOMaths.Add(equationRange);
                    dynamic addedEquationOMaths = added.OMaths;
                    try
                    {
                        var addedEquationCount = (int)addedEquationOMaths.Count;
                        if (addedEquationCount != 1)
                        {
                            throw new NativeToolException(
                                "EQUATION_INVALID",
                                "Microsoft Word did not create exactly one native equation for the staged operation",
                                new { equation_count = addedEquationCount }
                            );
                        }
                        equation = addedEquationOMaths.Item(1);
                    }
                    finally
                    {
                        FinalReleaseBatchComObject(addedEquationOMaths);
                    }
                }
                finally
                {
                    FinalReleaseBatchComObject(addedOMaths);
                }
            }
            finally
            {
                FinalReleaseBatchComObject(added);
                FinalReleaseBatchComObject(equationRange);
            }
            if (equation is null)
            {
                throw new NativeToolException(
                    "EQUATION_INVALID",
                    "Microsoft Word did not return the native equation created for the staged operation"
                );
            }
            equation.BuildUp();
            EquationStyleRewriteResult? styleRewrite = null;
            EquationStyleVerification? styleVerification = null;
            string readbackXml = "";
            if (equationOperation.HasFormatting)
            {
                dynamic equationStartRange = equation.Range;
                int equationStart;
                try
                {
                    equationStart = (int)equationStartRange.Start;
                }
                finally
                {
                    FinalReleaseBatchComObject(equationStartRange);
                }
                dynamic rewriteOMaths = document.OMaths;
                int equationsBeforeRewrite;
                try
                {
                    equationsBeforeRewrite = (int)rewriteOMaths.Count;
                }
                finally
                {
                    FinalReleaseBatchComObject(rewriteOMaths);
                }
                dynamic equationXmlRange = equation.Range;
                string equationXml;
                try
                {
                    equationXml = (string?)equationXmlRange.WordOpenXML ?? "";
                }
                finally
                {
                    FinalReleaseBatchComObject(equationXmlRange);
                }
                styleRewrite = EquationStyleRewriter.Rewrite(equationXml, equationOperation.StyleCounts);
                dynamic rewriteSourceRange = equation.Range;
                dynamic rewriteRange;
                try
                {
                    rewriteRange = rewriteSourceRange.Duplicate;
                }
                finally
                {
                    FinalReleaseBatchComObject(rewriteSourceRange);
                }
                try { rewriteRange.InsertXML(styleRewrite.WordOpenXml); }
                finally { if (Marshal.IsComObject(rewriteRange)) Marshal.FinalReleaseComObject(rewriteRange); }
                // InsertXML replaces the OMath and Word may expand the duplicate range
                // to include adjacent tail content. Binding through that range is
                // therefore unsafe: it can return two equations or a range that escapes
                // the publication segment. Re-bind the replacement by its stable story
                // position instead, then keep the existing semantic verification below.
                dynamic? rewrittenEquation = null;
                dynamic allEquations = document.OMaths;
                int allEquationCount;
                try { allEquationCount = (int)allEquations.Count; }
                catch
                {
                    FinalReleaseBatchComObject(allEquations);
                    throw;
                }
                if (allEquationCount != equationsBeforeRewrite)
                {
                    FinalReleaseBatchComObject(allEquations);
                    throw new NativeToolException(
                        "EQUATION_INVALID",
                        "Microsoft Word changed the native equation collection while rewriting one equation",
                        new { before = equationsBeforeRewrite, after = allEquationCount, equation_start = equationStart }
                    );
                }
                try
                {
                    if (
                        expectedEquationCollectionIndex < 1
                        || expectedEquationCollectionIndex > allEquationCount
                    )
                    {
                        throw new NativeToolException(
                            "EQUATION_INVALID",
                            "The expected styled equation collection index is unavailable",
                            new
                            {
                                expected_equation_index = expectedEquationCollectionIndex,
                                equation_count = allEquationCount,
                                equation_start = equationStart,
                            }
                        );
                    }
                    // Current callers build into an empty scratch document in reverse
                    // story order, so the equation just rewritten is at this explicit
                    // leading index. The range check below fails closed if Word ever
                    // violates that invariant; no whole-collection scan is needed.
                    rewrittenEquation = allEquations.Item(expectedEquationCollectionIndex);
                    dynamic candidateRange = rewrittenEquation.Range;
                    try
                    {
                        var candidateStart = (int)candidateRange.Start;
                        var candidateEnd = (int)candidateRange.End;
                        if (candidateStart != equationStart || candidateEnd <= candidateStart)
                        {
                            throw new NativeToolException(
                                "EQUATION_INVALID",
                                "Microsoft Word changed native equation style placement during reinsertion",
                                new
                                {
                                    expected_start = equationStart,
                                    actual_start = candidateStart,
                                    actual_end = candidateEnd,
                                    expected_equation_index = expectedEquationCollectionIndex,
                                }
                            );
                        }
                    }
                    finally
                    {
                        FinalReleaseBatchComObject(candidateRange);
                    }
                }
                catch
                {
                    FinalReleaseBatchComObject(rewrittenEquation);
                    rewrittenEquation = null;
                    throw;
                }
                finally
                {
                    FinalReleaseBatchComObject(allEquations);
                }
                if (rewrittenEquation is null)
                {
                    throw new NativeToolException(
                        "EQUATION_INVALID",
                        "Microsoft Word did not preserve exactly one styled native equation",
                        new
                        {
                            equation_count = 0,
                            equation_start = equationStart,
                        }
                    );
                }
                var sameEquationIdentity = false;
                try
                {
                    sameEquationIdentity = SameWordComIdentity(equation, rewrittenEquation);
                }
                catch (InvalidComObjectException)
                {
                    // InsertXML can invalidate the replaced wrapper before identity comparison.
                }
                if (!sameEquationIdentity)
                {
                    FinalReleaseBatchComObject(equation);
                }
                equation = rewrittenEquation;
            }
            equation.Type = equationOperation.Display ? 0 : 1;
            if (styleRewrite is not null)
            {
                dynamic equationReadbackRange = equation.Range;
                try
                {
                    readbackXml = (string?)equationReadbackRange.WordOpenXML ?? "";
                }
                finally
                {
                    FinalReleaseBatchComObject(equationReadbackRange);
                }
                styleVerification = EquationStyleRewriter.Verify(readbackXml, styleRewrite);
            }
            EquationReadbackVerification? readback = null;
            if (equationOperation.VerifyReadback)
            {
                if (readbackXml.Length == 0)
                {
                    dynamic equationReadbackRange = equation.Range;
                    try
                    {
                        readbackXml = (string?)equationReadbackRange.WordOpenXML ?? "";
                    }
                    finally
                    {
                        FinalReleaseBatchComObject(equationReadbackRange);
                    }
                }
                readback = EquationReadbackVerifier.Verify(
                    readbackXml,
                    equationOperation.Linear
                );
            }
            var result = new BuiltEquationResult(
                (object)equation,
                equationOperation,
                readback,
                styleRewrite,
                styleVerification
            );
            equationOwnershipTransferred = true;
            return result;
        }
        finally
        {
            if (!equationOwnershipTransferred)
            {
                FinalReleaseBatchComObject(equation);
            }
        }
    }

    private static BuiltEquationResult BuildVerifiedDirectOmmlEquation(
        dynamic document,
        int start,
        int end,
        PreparedEquationOperation operation,
        int expectedIndex
    )
    {
        var plan = operation.DirectPlan
            ?? throw new InvalidOperationException("Direct OMML plan is unavailable.");
        dynamic? sourceRange = null;
        dynamic? added = null;
        dynamic? placeholderEquation = null;
        dynamic? placeholderRange = null;
        dynamic? replacementRange = null;
        dynamic? equations = null;
        dynamic? equation = null;
        dynamic? equationRange = null;
        var transferred = false;
        try
        {
            equations = document.OMaths;
            var beforeCount = (int)equations.Count;
            FinalReleaseBatchComObject(equations);
            equations = null;

            sourceRange = document.Range(start, end);
            var sourceStart = (int)sourceRange.Start;
            equations = document.OMaths;
            added = equations.Add(sourceRange);
            FinalReleaseBatchComObject(equations);
            equations = null;
            dynamic addedEquations = added.OMaths;
            try
            {
                if ((int)addedEquations.Count != 1)
                {
                    throw new NativeToolException(
                        "EQUATION_INVALID",
                        "Direct OMML placeholder did not create exactly one native equation"
                    );
                }
                placeholderEquation = addedEquations.Item(1);
            }
            finally
            {
                FinalReleaseBatchComObject(addedEquations);
                FinalReleaseBatchComObject(added);
                added = null;
            }
            placeholderEquation.BuildUp();
            placeholderEquation.Type = operation.Display ? 0 : 1;
            placeholderRange = placeholderEquation.Range;
            var template = (string?)placeholderRange.WordOpenXML ?? "";
            var replacement = DirectOmmlEquationParser.BuildWordInsertXml(
                template,
                plan
            );
            replacementRange = placeholderRange.Duplicate;
            replacementRange.InsertXML(replacement);
            FinalReleaseBatchComObject(replacementRange);
            replacementRange = null;
            FinalReleaseBatchComObject(placeholderRange);
            placeholderRange = null;
            FinalReleaseBatchComObject(placeholderEquation);
            placeholderEquation = null;
            FinalReleaseBatchComObject(sourceRange);
            sourceRange = null;

            equations = document.OMaths;
            var afterCount = (int)equations.Count;
            if (afterCount != beforeCount + 1)
            {
                throw new NativeToolException(
                    "EQUATION_INVALID",
                    "Direct OMML did not create exactly one native equation",
                    new
                    {
                        before = beforeCount,
                        after = afterCount,
                        expected = beforeCount + 1,
                    }
                );
            }
            if (expectedIndex < 1 || expectedIndex > afterCount)
            {
                throw new NativeToolException(
                    "EQUATION_INVALID",
                    "The expected direct OMML collection index is unavailable",
                    new { expected_equation_index = expectedIndex, equation_count = afterCount }
                );
            }
            equation = equations.Item(expectedIndex);
            FinalReleaseBatchComObject(equations);
            equations = null;

            equationRange = equation.Range;
            var actualStart = (int)equationRange.Start;
            var actualEnd = (int)equationRange.End;
            if (actualStart != sourceStart || actualEnd <= actualStart)
            {
                throw new NativeToolException(
                    "EQUATION_INVALID",
                    "Microsoft Word changed direct OMML placement during insertion",
                    new
                    {
                        expected_start = sourceStart,
                        actual_start = actualStart,
                        actual_end = actualEnd,
                    }
                );
            }
            equation.Type = operation.Display ? 0 : 1;
            var actualParagraphJustification = VerifyDirectOmmlParagraphProperties(
                equation,
                plan,
                applyRequestedValue: true
            );
            var xml = ReadDirectOmmlWordOpenXml(
                equationRange,
                includeParagraphProperties: plan.ParagraphPropertiesOmml is not null
            );
            FinalReleaseBatchComObject(equationRange);
            equationRange = null;
            var parsed = DirectOmmlEquationParser.ParseWordReadback(xml);
            if (
                !string.Equals(
                    parsed.EquationSemanticSha256,
                    plan.EquationSemanticSha256,
                    StringComparison.Ordinal
                )
            )
            {
                throw new NativeToolException(
                    "EQUATION_INVALID",
                    "Microsoft Word changed the direct OMML semantic contract",
                    new
                    {
                        expected_semantic_sha256 = plan.EquationSemanticSha256,
                        actual_semantic_sha256 = parsed.EquationSemanticSha256,
                        expected_paragraph_properties_sha256 =
                            plan.ParagraphPropertiesSemanticSha256,
                        actual_paragraph_properties_sha256 =
                            actualParagraphJustification is null
                                ? null
                                : plan.ParagraphPropertiesSemanticSha256,
                        expected_paragraph_justification = plan.ParagraphJustification,
                        actual_paragraph_justification = actualParagraphJustification,
                    }
                );
            }
            var readback = EquationReadbackVerifier.Verify(xml, plan.LinearSemantic);
            var directVerification = new DirectOmmlVerification(
                plan.NamespaceIdentity,
                plan.SemanticSha256,
                ActualCombinedSemanticSha256: null,
                plan.EquationSemanticSha256,
                parsed.EquationSemanticSha256,
                plan.ParagraphPropertiesSemanticSha256,
                actualParagraphJustification is null
                    ? null
                    : plan.ParagraphPropertiesSemanticSha256,
                plan.ParagraphJustification,
                actualParagraphJustification,
                parsed.ElementCount
            );
            var result = new BuiltEquationResult(
                (object)equation,
                operation,
                readback,
                null,
                null,
                directVerification
            );
            transferred = true;
            return result;
        }
        catch (COMException exception)
        {
            throw new NativeToolException(
                "EQUATION_INVALID",
                "Microsoft Word rejected direct OMML insertion or readback",
                new
                {
                    stage = "direct_omml_insert_or_readback",
                    hresult = exception.HResult,
                    exception_type = exception.GetType().Name,
                }
            );
        }
        finally
        {
            FinalReleaseBatchComObject(equationRange);
            FinalReleaseBatchComObject(equations);
            FinalReleaseBatchComObject(replacementRange);
            FinalReleaseBatchComObject(placeholderRange);
            FinalReleaseBatchComObject(placeholderEquation);
            FinalReleaseBatchComObject(added);
            FinalReleaseBatchComObject(sourceRange);
            if (!transferred)
            {
                FinalReleaseBatchComObject(equation);
            }
        }
    }

    private static string ReadDirectOmmlWordOpenXml(
        dynamic equationRange,
        bool includeParagraphProperties
    )
    {
        if (!includeParagraphProperties)
        {
            return (string?)equationRange.WordOpenXML ?? "";
        }
        dynamic? paragraphs = null;
        dynamic? paragraph = null;
        dynamic? paragraphRange = null;
        try
        {
            paragraphs = equationRange.Paragraphs;
            if ((int)paragraphs.Count != 1)
            {
                throw new NativeToolException(
                    "EQUATION_INVALID",
                    "Direct OMML paragraph properties require exactly one equation paragraph"
                );
            }
            paragraph = paragraphs.Item(1);
            paragraphRange = paragraph.Range;
            return (string?)paragraphRange.WordOpenXML ?? "";
        }
        finally
        {
            FinalReleaseBatchComObject(paragraphRange);
            FinalReleaseBatchComObject(paragraph);
            FinalReleaseBatchComObject(paragraphs);
        }
    }

    private static string? VerifyDirectOmmlParagraphProperties(
        dynamic equation,
        DirectOmmlEquationPlan plan,
        bool applyRequestedValue
    )
    {
        if (plan.ParagraphJustification is null)
        {
            return null;
        }
        var expected = plan.ParagraphJustification switch
        {
            "centerGroup" => 1,
            "center" => 2,
            "left" => 3,
            "right" => 4,
            _ => throw new NativeToolException(
                "EQUATION_INVALID",
                "Direct OMML paragraph justification is unsupported"
            ),
        };
        if (applyRequestedValue)
        {
            equation.Justification = expected;
        }
        var actual = (int)equation.Justification;
        if (actual != expected)
        {
            throw new NativeToolException(
                "EQUATION_INVALID",
                "Microsoft Word changed direct OMML paragraph justification",
                new
                {
                    expected_paragraph_justification = plan.ParagraphJustification,
                    actual_paragraph_justification = DirectOmmlJustificationName(actual),
                }
            );
        }
        return DirectOmmlJustificationName(actual);
    }

    private static string DirectOmmlJustificationName(int value) => value switch
    {
        1 => "centerGroup",
        2 => "center",
        3 => "left",
        4 => "right",
        7 => "inline",
        _ => "unknown",
    };

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
            PublishStagedPdf(temporaryPath, outputPath, overwrite, BeforeCreateNewPublication);
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

    internal static void PublishStagedPdf(string stagedPath, string outputPath, bool overwrite, Action<string, string>? beforeCreateNewPublication = null)
    {
        if (overwrite && File.Exists(outputPath))
        {
            File.Replace(stagedPath, outputPath, null, true);
            return;
        }
        try
        {
            beforeCreateNewPublication?.Invoke(stagedPath, outputPath);
            AtomicFilePublisher.PublishCreateNew(stagedPath, outputPath);
        }
        catch (IOException exception) when (AtomicFilePublisher.IsAlreadyExists(exception))
        {
            throw new NativeToolException("VERSION_CONFLICT", "The PDF output file was created concurrently; it was not overwritten");
        }
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
        _quarantinedRecords.Clear();
        _selectionGrants.Clear();
        _undoGrants.Clear();
        _rangeGrants.Clear();
        _smartArtTextEditGrants.Clear();
        _smartArtLayoutGrants.Clear();
        _reviewGrants.Clear();
        return result;
    }

    private async Task<object> DisconnectAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var id = arguments.String("live_document_id");
        var scratchClosed = false;
        var disconnected = _records.TryGetValue(id, out var record);
        var quarantineCleared = _quarantinedRecords.TryRemove(id, out _);
        if (!disconnected && !quarantineCleared)
        {
            throw new NativeToolException(
                "DOCUMENT_NOT_FOUND",
                "The Word Live document handle was not found"
            );
        }
        if (record is { AutoCloseOnDisconnect: true })
        {
            await _host.InvokeAsync<object>(
                application =>
                {
                    dynamic document = ResolveDocument(application, record);
                    document.Close(WordDoNotSaveChanges);
                    scratchClosed = true;
                    return new { closed = true };
                },
                cancellationToken
            );
        }
        if (disconnected)
        {
            _records.TryRemove(id, out _);
        }
        InvalidateSelectionGrants(id);
        InvalidateRangeGrants(id);
        InvalidateUndoGrants(id);
        return new
        {
            live_document_id = id,
            disconnected = true,
            quarantine_cleared = quarantineCleared,
            scratch_document_closed = scratchClosed,
        };
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
        var display = arguments.Boolean("display", true);
        DirectOmmlEquationPlan? directPlan = inputFormat == "omml" ? DirectOmmlEquationParser.Parse(value) : null;
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
        if (directPlan?.ParagraphPropertiesOmml is not null && !display)
        {
            throw new NativeToolException(
                "EQUATION_INVALID",
                "m:oMathParaPr requires display=true for direct OMML"
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
                    (character < 32 && character is not ('\t' or '\n' or '\n'))
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
            directPlan is not null
            || conversion.HasFormatting
            || EquationReadbackVerifier.RequiresReadback(conversion.Linear);
        return new PreparedEquationOperation(
            conversion.Linear,
            directPlan is null ? conversion.BuildLinear : "x",
            display,
            inputFormat,
            arguments.Boolean("verify_readback", false) || readbackRequired,
            readbackRequired,
            conversion.StyleCounts,
            directPlan
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
        var candidates = new List<object>(count);
        var matches = new List<object>();
        var windowMatches = new List<object>();
        for (var index = 1; index <= count; index++)
        {
            dynamic document = application.Documents.Item(index);
            candidates.Add((object)document);
            var name = DocumentName(document);
            var fullName = DocumentFullName(document);
            if (NormalizeIdentity(fullName, name) == identity)
            {
                matches.Add(document);
            }
            if (
                record.WindowHwnd != 0
                && DocumentWindowHwnd(document) == record.WindowHwnd
            )
            {
                windowMatches.Add(document);
            }
        }
        object? selected = null;
        if (matches.Count == 1)
        {
            selected = matches[0];
        }
        else if (windowMatches.Count == 1)
        {
            selected = windowMatches[0];
        }
        foreach (var candidate in candidates)
        {
            if (!ReferenceEquals(candidate, selected))
            {
                FinalReleaseBatchComObject(candidate);
            }
        }
        if (selected is not null)
        {
            return selected;
        }
        throw new NativeToolException(
            "DOCUMENT_NOT_FOUND",
            "The connected Word document is no longer open or became ambiguous",
            new { identity_matches = matches.Count, window_matches = windowMatches.Count },
            retryable: true
        );
    }

    private static int DocumentWindowHwnd(dynamic document)
    {
        dynamic? windows = null;
        dynamic? window = null;
        try
        {
            windows = document.Windows;
            if ((int)windows.Count < 1)
            {
                return 0;
            }
            window = windows.Item(1);
            return (int)window.Hwnd;
        }
        catch
        {
            return 0;
        }
        finally
        {
            FinalReleaseBatchComObject(window);
            FinalReleaseBatchComObject(windows);
        }
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
        if (id.Length == 0)
        {
            throw new NativeToolException(
                "DOCUMENT_NOT_FOUND",
                "The Word Live document handle was not found"
            );
        }
        if (_records.TryGetValue(id, out var record))
        {
            return record;
        }
        if (_quarantinedRecords.TryGetValue(id, out var quarantine))
        {
            throw new NativeToolException(
                "LIVE_DOCUMENT_QUARANTINED",
                "The Word Live document handle was invalidated after rollback could not be proven",
                new
                {
                    live_document_id = id,
                    reason_code = quarantine.ReasonCode,
                    requires_explicit_disconnect = true,
                }
            );
        }
        throw new NativeToolException(
            "DOCUMENT_NOT_FOUND",
            "The Word Live document handle was not found"
        );
    }

    private void ThrowIfDocumentIdentityQuarantined(string identity)
    {
        var quarantine = _quarantinedRecords.Values.FirstOrDefault(
            item => NormalizeIdentity(item.FullName, item.Name) == identity
        );
        if (quarantine is null)
        {
            return;
        }
        throw new NativeToolException(
            "LIVE_DOCUMENT_QUARANTINED",
            "This open Word document is quarantined because rollback could not be proven",
            new
            {
                live_document_id = quarantine.Id,
                reason_code = quarantine.ReasonCode,
                requires_explicit_disconnect = true,
            }
        );
    }

    private static void CheckVersion(LiveDocumentRecord record, long? expectedVersion)
    {
        if (expectedVersion is null)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "expected_version is required for every Word Live write",
                new { field = "expected_version" }
            );
        }
        if (expectedVersion.Value != record.Version)
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
                    (character < 32 && character is not ('\t' or '\n' or '\n' or '\f'))
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
            .Replace('\n', '\n')
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
        var prefix = asNewParagraph && start > 0 && previous != "\n" ? "\n" : "";
        var suffix = asNewParagraph ? "\n" : "";
        return (prefix + normalized + suffix, prefix, suffix);
    }

    private static string NormalizeWordText(string value)
    {
        return value
            .Replace("\n", "\n", StringComparison.Ordinal)
            .Replace('\n', '\n')
            .Replace('\n', '\n');
    }

    private static JsonElement? PrepareFormattingArgument(
        JsonElement container,
        bool allowParagraphFormatting = true
    )
    {
        if (!container.TryGetProperty("formatting", out var formatting))
        {
            return null;
        }
        if (formatting.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (formatting.ValueKind != JsonValueKind.Object)
        {
            throw new NativeToolException("INVALID_INPUT", "formatting must be an object or null");
        }
        return NormalizeFormatting(formatting, allowParagraphFormatting);
    }

    private static JsonElement NormalizeFormatting(
        JsonElement formatting,
        bool allowParagraphFormatting
    )
    {
        var normalized = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in formatting.EnumerateObject())
        {
            var canonicalName = property.Name switch
            {
                "font_size" => "font_size_pt",
                "alignment" => "paragraph_alignment",
                _ => property.Name,
            };
            if (!allowParagraphFormatting && canonicalName == "paragraph_alignment")
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"Unsupported inline run formatting field: {property.Name}"
                );
            }
            if (!normalized.TryAdd(canonicalName, property.Value.Clone()))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"Formatting field {canonicalName} was supplied more than once or through both its alias and canonical name"
                );
            }
        }

        foreach (var property in normalized)
        {
            ValidateFormattingValue(property.Key, property.Value, allowParagraphFormatting);
        }
        if (
            normalized.TryGetValue("strike", out var strike)
            && strike.GetBoolean()
            && normalized.TryGetValue("double_strike", out var doubleStrike)
            && doubleStrike.GetBoolean()
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "strike and double_strike cannot both be true because Microsoft Word preserves only one strike mode"
            );
        }
        if (normalized.ContainsKey("underline") && normalized.ContainsKey("underline_style"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Use either deprecated underline or canonical underline_style, not both"
            );
        }
        if (
            normalized.TryGetValue("subscript", out var subscript)
            && subscript.GetBoolean()
            && normalized.TryGetValue("superscript", out var superscript)
            && superscript.GetBoolean()
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "subscript and superscript cannot both be true"
            );
        }
        if (
            normalized.TryGetValue("emboss", out var emboss)
            && emboss.GetBoolean()
            && normalized.TryGetValue("engrave", out var engrave)
            && engrave.GetBoolean()
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "emboss and engrave cannot both be true"
            );
        }
        if (
            normalized.ContainsKey("position_pt")
            && (
                normalized.TryGetValue("subscript", out subscript) && subscript.GetBoolean()
                || normalized.TryGetValue("superscript", out superscript)
                    && superscript.GetBoolean()
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "position_pt cannot be combined with an enabled subscript or superscript"
            );
        }
        if (normalized.ContainsKey("font_color_rgb") && normalized.ContainsKey("font_color_index"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Use either font_color_rgb or font_color_index, not both"
            );
        }
        return JsonSerializer.SerializeToElement(normalized);
    }

    private static void ValidateFormattingValue(
        string name,
        JsonElement value,
        bool allowParagraphFormatting
    )
    {
        if (
            name
                is "font_name"
                    or "font_name_ascii"
                    or "font_name_bidi"
                    or "font_name_far_east"
                    or "font_name_other"
        )
        {
            if (
                value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(value.GetString())
                || value.GetString()!.Length > 128
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"{name} must be a non-empty string of at most 128 characters"
                );
            }
            return;
        }
        if (name == "font_color_rgb")
        {
            var color = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
            if (
                color.Length != 7
                || color[0] != '#'
                || !color.AsSpan(1).ToArray().All(Uri.IsHexDigit)
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "font_color_rgb must use #RRGGBB"
                );
            }
            return;
        }
        if (name is "diacritic_color" or "underline_color")
        {
            var color = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
            if (!string.Equals(color, "automatic", StringComparison.Ordinal) && !IsRgbColor(color))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"{name} must be automatic or use #RRGGBB"
                );
            }
            return;
        }
        if (
            name is "bold"
                or "italic"
                or "bold_bidi"
                or "italic_bidi"
                or "underline"
                or "strike"
                or "double_strike"
                or "subscript"
                or "superscript"
                or "all_caps"
                or "small_caps"
                or "hidden"
                or "shadow"
                or "outline"
                or "emboss"
                or "engrave"
                or "disable_character_space_grid"
                or "contextual_alternates"
                or "clear_character_formatting"
                or "keep_with_next"
                or "keep_together"
                or "page_break_before"
                or "widow_control"
        )
        {
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new NativeToolException("INVALID_INPUT", $"{name} must be true or false");
            }
            if (
                !allowParagraphFormatting
                && name
                    is (
                        "keep_with_next"
                        or "keep_together"
                        or "page_break_before"
                        or "widow_control"
                    )
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"Unsupported inline run formatting field: {name}"
                );
            }
            return;
        }
        if (name == "underline_style")
        {
            if (
                value.ValueKind != JsonValueKind.String
                || !TryUnderlineStyle(value.GetString(), out _)
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "underline_style is not a supported Microsoft Word underline style"
                );
            }
            return;
        }
        if (name == "emphasis_mark")
        {
            if (
                value.ValueKind != JsonValueKind.String
                || !TryEmphasisMark(value.GetString(), out _)
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "emphasis_mark must be none, over_solid_circle, over_comma, over_white_circle, or under_solid_circle"
                );
            }
            return;
        }
        if (name == "ligatures")
        {
            if (
                value.ValueKind != JsonValueKind.String
                || !TryLigatures(value.GetString(), out _)
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "ligatures is not a supported Microsoft Word ligature combination"
                );
            }
            return;
        }
        if (name == "number_form")
        {
            if (
                value.ValueKind != JsonValueKind.String
                || !TryNumberForm(value.GetString(), out _)
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "number_form must be default, lining, or old_style"
                );
            }
            return;
        }
        if (name == "number_spacing")
        {
            if (
                value.ValueKind != JsonValueKind.String
                || !TryNumberSpacing(value.GetString(), out _)
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "number_spacing must be default, proportional, or tabular"
                );
            }
            return;
        }
        if (name == "stylistic_sets")
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 20)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "stylistic_sets must be an array of unique integers from 1 to 20"
                );
            }
            var seen = new HashSet<int>();
            foreach (var item in value.EnumerateArray())
            {
                if (
                    item.ValueKind != JsonValueKind.Number
                    || !item.TryGetInt32(out var set)
                    || set is < 1 or > 20
                    || !seen.Add(set)
                )
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "stylistic_sets must be an array of unique integers from 1 to 20"
                    );
                }
            }
            return;
        }
        if (name is "font_color_index" or "font_color_bidi_index")
        {
            if (
                value.ValueKind != JsonValueKind.Number
                || !value.TryGetInt32(out var colorIndex)
                || colorIndex is < 0 or > 16
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"{name} must be 0 (automatic) or an integer from 1 to 16"
                );
            }
            return;
        }
        if (name == "paragraph_alignment")
        {
            var alignment = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (
                !allowParagraphFormatting
                || alignment is not ("left" or "center" or "right" or "justify" or "distribute")
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "paragraph_alignment must be left, center, right, justify, or distribute"
                );
            }
            return;
        }
        if (name == "highlight_color_index")
        {
            if (
                value.ValueKind != JsonValueKind.Number
                || !value.TryGetInt32(out var colorIndex)
                || colorIndex is < 0 or > 16
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "highlight_color_index must be an integer from 0 to 16"
                );
            }
            return;
        }

        var numericRange = name switch
        {
            "font_size_pt" or "font_size_bidi_pt" => (Minimum: 1d, Maximum: 1638d),
            "scaling_percent" => (Minimum: 1d, Maximum: 600d),
            "spacing_pt" or "position_pt" => (Minimum: -1584d, Maximum: 1584d),
            "kerning_pt" => (Minimum: 0d, Maximum: 1638d),
            "space_before_pt" or "space_after_pt" => (Minimum: 0d, Maximum: 1584d),
            "left_indent_pt" or "right_indent_pt" or "first_line_indent_pt" =>
                (Minimum: -1584d, Maximum: 1584d),
            _ => ((double Minimum, double Maximum)?)null,
        };
        if (numericRange is not null)
        {
            if (
                (
                    !allowParagraphFormatting
                    && name
                        is not (
                            "font_size_pt"
                            or "font_size_bidi_pt"
                            or "scaling_percent"
                            or "spacing_pt"
                            or "position_pt"
                            or "kerning_pt"
                        )
                )
                || value.ValueKind != JsonValueKind.Number
                || !value.TryGetDouble(out var number)
                || !double.IsFinite(number)
                || number < numericRange.Value.Minimum
                || number > numericRange.Value.Maximum
                || (name is "scaling_percent" or "position_pt" && number != Math.Truncate(number))
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    $"{name} is outside the supported numeric range"
                );
            }
            return;
        }

        throw new NativeToolException(
            "INVALID_INPUT",
            allowParagraphFormatting
                ? $"Unsupported formatting field: {name}"
                : $"Unsupported inline run formatting field: {name}"
        );
    }

    private static bool IsRgbColor(string value) =>
        value.Length == 7
        && value[0] == '#'
        && value.AsSpan(1).ToArray().All(Uri.IsHexDigit);

    private static bool TryUnderlineStyle(string? value, out int wordValue)
    {
        wordValue = value switch
        {
            "none" => 0,
            "single" => 1,
            "words" => 2,
            "double" => 3,
            "dotted" => 4,
            "thick" => 6,
            "dash" => 7,
            "dot_dash" => 9,
            "dot_dot_dash" => 10,
            "wavy" => 11,
            "dotted_heavy" => 20,
            "dash_heavy" => 23,
            "dot_dash_heavy" => 25,
            "dot_dot_dash_heavy" => 26,
            "wavy_heavy" => 27,
            "dash_long" => 39,
            "wavy_double" => 43,
            "dash_long_heavy" => 55,
            _ => int.MinValue,
        };
        return wordValue != int.MinValue;
    }

    private static bool TryEmphasisMark(string? value, out int wordValue)
    {
        wordValue = value switch
        {
            "none" => 0,
            "over_solid_circle" => 1,
            "over_comma" => 2,
            "over_white_circle" => 3,
            "under_solid_circle" => 4,
            _ => int.MinValue,
        };
        return wordValue != int.MinValue;
    }

    private static bool TryLigatures(string? value, out int wordValue)
    {
        wordValue = value switch
        {
            "none" => 0,
            "standard" => 1,
            "contextual" => 2,
            "standard_contextual" => 3,
            "historical" => 4,
            "standard_historical" => 5,
            "contextual_historical" => 6,
            "standard_contextual_historical" => 7,
            "discretionary" => 8,
            "standard_discretionary" => 9,
            "contextual_discretionary" => 10,
            "standard_contextual_discretionary" => 11,
            "historical_discretionary" => 12,
            "standard_historical_discretionary" => 13,
            "contextual_historical_discretionary" => 14,
            "all" => 15,
            _ => int.MinValue,
        };
        return wordValue != int.MinValue;
    }

    private static bool TryNumberForm(string? value, out int wordValue)
    {
        wordValue = value switch
        {
            "default" => 0,
            "lining" => 1,
            "old_style" => 2,
            _ => int.MinValue,
        };
        return wordValue != int.MinValue;
    }

    private static bool TryNumberSpacing(string? value, out int wordValue)
    {
        wordValue = value switch
        {
            "default" => 0,
            "proportional" => 1,
            "tabular" => 2,
            _ => int.MinValue,
        };
        return wordValue != int.MinValue;
    }

    internal static JsonElement NormalizeFormattingForTesting(
        JsonElement formatting,
        bool allowParagraphFormatting = true
    ) => NormalizeFormatting(formatting, allowParagraphFormatting);

    internal static IReadOnlyDictionary<string, string> ApplyAndCaptureFormattingForTesting(
        object range,
        JsonElement formatting,
        bool allowParagraphFormatting = true
    )
    {
        var normalized = NormalizeFormatting(formatting, allowParagraphFormatting);
        ApplyFormatting((dynamic)range, normalized);
        return CaptureRequestedFormatting((dynamic)range, normalized);
    }

    private static IReadOnlyList<PreparedTextRun> ParseTextRuns(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Array || node.GetArrayLength() is < 1 or > 1000)
        {
            throw new NativeToolException("INVALID_INPUT", "runs must contain between 1 and 1000 items");
        }
        var result = new List<PreparedTextRun>(node.GetArrayLength());
        var totalCharacters = 0;
        foreach (var run in node.EnumerateArray())
        {
            if (
                run.ValueKind != JsonValueKind.Object
                || !run.TryGetProperty("text", out var textNode)
                || textNode.ValueKind != JsonValueKind.String
            )
            {
                throw new NativeToolException("INVALID_INPUT", "Every run requires non-empty text");
            }
            var text = NormalizeWordText(textNode.GetString() ?? "");
            if (text.Length == 0)
            {
                throw new NativeToolException("INVALID_INPUT", "Every run requires non-empty text");
            }
            totalCharacters = checked(totalCharacters + text.Length);
            if (totalCharacters > 200_000)
            {
                throw new NativeToolException(
                    "LIMIT_EXCEEDED",
                    "Combined inline run text exceeds 200,000 characters"
                );
            }
            JsonElement? formatting = null;
            if (run.TryGetProperty("formatting", out var formatNode))
            {
                if (formatNode.ValueKind != JsonValueKind.Object)
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "run formatting must be an object"
                    );
                }
                formatting = NormalizeFormatting(formatNode, allowParagraphFormatting: false);
            }
            result.Add(new PreparedTextRun(text, formatting));
        }
        return result;
    }

    internal static (string Text, int RunCount) ParseTextRunsForTesting(JsonElement node)
    {
        var runs = ParseTextRuns(node);
        return (string.Concat(runs.Select(run => run.Text)), runs.Count);
    }

    private static void ApplyFormatting(dynamic range, JsonElement formatting)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "font_name",
            "font_name_ascii",
            "font_name_bidi",
            "font_name_far_east",
            "font_name_other",
            "font_size_pt",
            "font_size_bidi_pt",
            "font_color_rgb",
            "font_color_index",
            "font_color_bidi_index",
            "diacritic_color",
            "bold",
            "italic",
            "bold_bidi",
            "italic_bidi",
            "underline",
            "underline_style",
            "underline_color",
            "strike",
            "double_strike",
            "subscript",
            "superscript",
            "all_caps",
            "small_caps",
            "hidden",
            "shadow",
            "outline",
            "emboss",
            "engrave",
            "scaling_percent",
            "spacing_pt",
            "position_pt",
            "kerning_pt",
            "disable_character_space_grid",
            "emphasis_mark",
            "ligatures",
            "number_form",
            "number_spacing",
            "stylistic_sets",
            "contextual_alternates",
            "clear_character_formatting",
            "highlight_color_index",
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
        if (
            formatting.TryGetProperty("clear_character_formatting", out var clearFormatting)
            && clearFormatting.GetBoolean()
        )
        {
            font.Reset();
            SetDynamicProperty(range, "HighlightColorIndex", 0);
        }
        if (formatting.TryGetProperty("font_name", out var fontName))
        {
            font.Name = fontName.GetString() ?? "";
        }
        SetString(font, formatting, "font_name_ascii", "NameAscii");
        SetString(font, formatting, "font_name_bidi", "NameBi");
        SetString(font, formatting, "font_name_far_east", "NameFarEast");
        SetString(font, formatting, "font_name_other", "NameOther");
        if (formatting.TryGetProperty("font_size_pt", out var fontSize))
        {
            var value = fontSize.GetDouble();
            if (value is < 1 or > 1638)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "font_size_pt must be between 1 and 1638"
                );
            }
            font.Size = (float)value;
        }
        SetFloat(font, formatting, "font_size_bidi_pt", "SizeBi", 1, 1638);
        if (formatting.TryGetProperty("font_color_rgb", out var fontColor))
        {
            font.Color = ParseWordColor(fontColor.GetString() ?? "");
        }
        SetInteger(font, formatting, "font_color_index", "ColorIndex", 0, 16);
        SetInteger(font, formatting, "font_color_bidi_index", "ColorIndexBi", 0, 16);
        SetWordColor(font, formatting, "diacritic_color", "DiacriticColor");
        SetWordBoolean(font, formatting, "bold", "Bold");
        SetWordBoolean(font, formatting, "italic", "Italic");
        SetWordBoolean(font, formatting, "bold_bidi", "BoldBi");
        SetWordBoolean(font, formatting, "italic_bidi", "ItalicBi");
        SetWordBoolean(font, formatting, "strike", "StrikeThrough");
        SetWordBoolean(font, formatting, "double_strike", "DoubleStrikeThrough");
        SetWordBoolean(font, formatting, "subscript", "Subscript");
        SetWordBoolean(font, formatting, "superscript", "Superscript");
        SetWordBoolean(font, formatting, "all_caps", "AllCaps");
        SetWordBoolean(font, formatting, "small_caps", "SmallCaps");
        SetWordBoolean(font, formatting, "hidden", "Hidden");
        SetWordBoolean(font, formatting, "shadow", "Shadow");
        SetWordBoolean(font, formatting, "outline", "Outline");
        SetWordBoolean(font, formatting, "emboss", "Emboss");
        SetWordBoolean(font, formatting, "engrave", "Engrave");
        SetWordBoolean(
            font,
            formatting,
            "disable_character_space_grid",
            "DisableCharacterSpaceGrid"
        );
        SetWordBoolean(font, formatting, "contextual_alternates", "ContextualAlternates");
        if (formatting.TryGetProperty("underline", out var underline))
        {
            font.Underline = underline.GetBoolean() ? 1 : 0;
        }
        if (formatting.TryGetProperty("underline_style", out var underlineStyle))
        {
            _ = TryUnderlineStyle(underlineStyle.GetString(), out var wordUnderline);
            SetDynamicProperty(font, "Underline", wordUnderline);
        }
        SetWordColor(font, formatting, "underline_color", "UnderlineColor");
        SetInteger(font, formatting, "scaling_percent", "Scaling", 1, 600);
        SetFloat(font, formatting, "spacing_pt", "Spacing", -1584, 1584);
        SetInteger(font, formatting, "position_pt", "Position", -1584, 1584);
        SetFloat(font, formatting, "kerning_pt", "Kerning", 0, 1638);
        if (formatting.TryGetProperty("emphasis_mark", out var emphasisMark))
        {
            _ = TryEmphasisMark(emphasisMark.GetString(), out var wordEmphasisMark);
            SetDynamicProperty(font, "EmphasisMark", wordEmphasisMark);
        }
        if (formatting.TryGetProperty("ligatures", out var ligatures))
        {
            _ = TryLigatures(ligatures.GetString(), out var wordLigatures);
            SetDynamicProperty(font, "Ligatures", wordLigatures);
        }
        if (formatting.TryGetProperty("number_form", out var numberForm))
        {
            _ = TryNumberForm(numberForm.GetString(), out var wordNumberForm);
            SetDynamicProperty(font, "NumberForm", wordNumberForm);
        }
        if (formatting.TryGetProperty("number_spacing", out var numberSpacing))
        {
            _ = TryNumberSpacing(numberSpacing.GetString(), out var wordNumberSpacing);
            SetDynamicProperty(font, "NumberSpacing", wordNumberSpacing);
        }
        if (formatting.TryGetProperty("stylistic_sets", out var stylisticSets))
        {
            var wordStylisticSets = 0;
            foreach (var item in stylisticSets.EnumerateArray())
            {
                wordStylisticSets |= 1 << (item.GetInt32() - 1);
            }
            SetDynamicProperty(font, "StylisticSet", wordStylisticSets);
        }
        if (formatting.TryGetProperty("highlight_color_index", out var highlightColorIndex))
        {
            SetDynamicProperty(range, "HighlightColorIndex", highlightColorIndex.GetInt32());
        }
        if (formatting.TryGetProperty("paragraph_alignment", out var alignment))
        {
            paragraph.Alignment = (alignment.GetString() ?? "") switch
            {
                "left" => 0,
                "center" => 1,
                "right" => 2,
                "justify" => 3,
                "distribute" => 4,
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
        VerifyRequestedFormatting(range, formatting);
    }

    private static void SetString(
        dynamic target,
        JsonElement formatting,
        string source,
        string destination
    )
    {
        if (formatting.TryGetProperty(source, out var value))
        {
            SetDynamicProperty(target, destination, value.GetString() ?? "");
        }
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
        SetDynamicProperty(target, destination, (float)value);
    }

    private static void SetInteger(
        dynamic target,
        JsonElement formatting,
        string source,
        string destination,
        int minimum,
        int maximum
    )
    {
        if (!formatting.TryGetProperty(source, out var valueNode))
        {
            return;
        }
        if (!valueNode.TryGetInt32(out var value) || value < minimum || value > maximum)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{source} must be an integer between {minimum} and {maximum}"
            );
        }
        SetDynamicProperty(target, destination, value);
    }

    private static void SetWordColor(
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
        var text = value.GetString() ?? "";
        SetDynamicProperty(
            target,
            destination,
            string.Equals(text, "automatic", StringComparison.Ordinal)
                ? unchecked((int)0xFF000000)
                : ParseWordColor(text)
        );
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

    private static object Performance(long startedTimestamp, BatchComplexity? batchComplexity = null)
    {
        var result = new
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
        if (batchComplexity is null)
        {
            return result;
        }
        return new
        {
            result.runtime,
            result.python_used,
            result.persistent_com_sta,
            result.com_attachments,
            result.total_ms,
            complexity = batchComplexity
        };
    }

    private static string SelectionContextHash(dynamic document, int start, int end)
    {
        dynamic? content = null;
        dynamic? contextRange = null;
        try
        {
            content = document.Content;
            var documentEnd = Math.Max(0, (int)content.End - 1);
            var contextStart = Math.Max(0, start - 64);
            var contextEnd = Math.Min(documentEnd, end + 64);
            contextRange = document.Range(contextStart, contextEnd);
            var text = (string?)contextRange.Text ?? "";
            var payload = $"{start}\0{end}\0{text}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
                .ToLowerInvariant();
        }
        finally
        {
            FinalReleaseBatchComObject(contextRange);
            FinalReleaseBatchComObject(content);
        }
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

    private void TrimEquationGrants()
    {
        if (_equationGrants.Count <= 2_048)
        {
            return;
        }
        foreach (var key in _equationGrants.Keys.Take(_equationGrants.Count - 1_024))
        {
            _equationGrants.TryRemove(key, out _);
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
        InvalidateSmartArtTextEditGrants(documentId);
    }

    private void InvalidateSmartArtTextEditGrants(string documentId)
    {
        foreach (
            var pair in _smartArtTextEditGrants.Where(
                item => item.Value.DocumentId == documentId
            )
        )
        {
            _smartArtTextEditGrants.TryRemove(pair.Key, out _);
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
        InvalidateEquationGrants(documentId);
        InvalidateReviewGrants(documentId);
        InvalidateSmartArtLayoutGrants(documentId);
    }

    private void InvalidateEquationGrants(string documentId)
    {
        foreach (
            var pair in _equationGrants.Where(item => item.Value.DocumentId == documentId)
        )
        {
            _equationGrants.TryRemove(pair.Key, out _);
        }
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

    internal abstract record PreparedOperation(string Value, bool AsNewParagraph);

    private static PreparedBatchPayload BuildPreparedBatchPayload(
        IReadOnlyList<PreparedOperation> operations,
        int insertionStart,
        string previousText
    )
    {
        var pieces = new List<string>(operations.Count);
        var segments = new List<(int Start, int End)>(operations.Count);
        var offset = 0;
        var previous = previousText;
        foreach (var operation in operations)
        {
            var raw = NormalizeWordText(operation.Value);
            var prefix = operation.AsNewParagraph
                && insertionStart + offset > 0
                && previous != "\n"
                    ? "\n"
                    : "";
            var suffix = operation.AsNewParagraph ? "\n" : "";
            var piece = prefix + raw + suffix;
            segments.Add((offset + prefix.Length, offset + prefix.Length + raw.Length));
            pieces.Add(piece);
            offset += piece.Length;
            if (piece.Length > 0)
            {
                previous = piece[^1].ToString(CultureInfo.InvariantCulture);
            }
        }
        return new PreparedBatchPayload(string.Concat(pieces), segments);
    }

    private sealed record PreparedBatchPayload(
        string Payload,
        IReadOnlyList<(int Start, int End)> Segments
    );

    internal sealed record BatchComplexity(
        int OperationCount,
        int EquationCount,
        int StyledEquationCount,
        int TextCharacters,
        int FormattedRunCount,
        int EstimatedStagingContentComCalls,
        int BatchBoundaryEquationCountReads)
    {
        internal static BatchComplexity For(IReadOnlyList<PreparedOperation> operations)
        {
            var equations = operations.Count(operation => operation is PreparedEquationOperation);
            var styledEquations = operations
                .OfType<PreparedEquationOperation>()
                .Count(operation => operation.HasFormatting);
            var textCharacters = operations
                .OfType<PreparedTextOperation>()
                .Sum(operation => operation.Text.Length);
            var runs = operations
                .OfType<PreparedTextOperation>()
                .Sum(operation => operation.Runs.Count(run => run.Formatting is not null));
            return FromCounts(
                operations.Count,
                equations,
                styledEquations,
                textCharacters,
                runs
            );
        }

        internal static BatchComplexity FromCounts(
            int operationCount,
            int equationCount,
            int styledEquationCount,
            int textCharacters,
            int formattedRunCount
        )
        {
            // This deliberately estimates only staging content calls whose count is
            // driven by the request. Snapshot, publication and rollback verification
            // have fixed extra costs and are not smuggled into a fake precise total.
            var estimatedStagingContentComCalls = checked(
                operationCount
                    + formattedRunCount
                    + equationCount
                    + (2 * styledEquationCount)
            );
            return new(
                operationCount,
                equationCount,
                styledEquationCount,
                textCharacters,
                formattedRunCount,
                estimatedStagingContentComCalls,
                BatchBoundaryEquationCountReads: 2
            );
        }
    }

    private sealed record PreparedTextOperation(
        string Text,
        bool NewParagraph,
        string Style,
        JsonElement? Formatting,
        IReadOnlyList<PreparedTextRun> Runs
    ) : PreparedOperation(Text, NewParagraph);

    private sealed record PreparedTextRun(string Text, JsonElement? Formatting);

    private sealed record PreparedEquationOperation(
        string Linear,
        string BuildLinear,
        bool Display,
        string InputFormat,
        bool VerifyReadback,
        bool ReadbackRequired,
        EquationStyleCounts StyleCounts,
        DirectOmmlEquationPlan? DirectPlan = null
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
        EquationStyleVerification? StyleVerification,
        DirectOmmlVerification? DirectOmml = null
    );

    private sealed record DirectOmmlVerification(
        string NamespaceIdentity,
        string ExpectedSemanticSha256,
        string? ActualCombinedSemanticSha256,
        string ExpectedEquationSemanticSha256,
        string ActualEquationSemanticSha256,
        string? ExpectedParagraphPropertiesSha256,
        string? ActualParagraphPropertiesSha256,
        string? ExpectedParagraphJustification,
        string? ActualParagraphJustification,
        int ElementCount
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
