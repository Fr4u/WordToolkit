using System.Collections.ObjectModel;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Operations;

public sealed class DocumentAnalysisWordPackageOperation
{
    private static readonly HashSet<string> SupportedExtensions = new(
        [".docx", ".docm", ".dotx", ".dotm"],
        StringComparer.OrdinalIgnoreCase
    );

    private readonly OpcPackageLimits _packageLimits;

    public DocumentAnalysisWordPackageOperation(OpcPackageLimits? packageLimits = null)
    {
        _packageLimits = packageLimits ?? OpcPackageLimits.Default;
    }

    public DocumentAnalysisResult Analyze(
        DocumentAnalysisRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (request is null)
        {
            throw Invalid("Document-analysis request is required");
        }
        Validate(request, requireLeafName: false);
        var path = ResolvePath(request.LocalPath);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return AnalyzeCore(
                stream,
                Path.GetFileName(path),
                stream.Length,
                request,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw MapFailure(exception, path);
        }
    }

    public DocumentAnalysisResult Analyze(
        Stream packageStream,
        string fileName,
        DocumentAnalysisRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(packageStream);
        ArgumentNullException.ThrowIfNull(request);
        Validate(request with { LocalPath = fileName }, requireLeafName: true);

        long? originalPosition = null;
        try
        {
            if (!packageStream.CanRead || !packageStream.CanSeek)
            {
                throw Invalid("Package stream must be readable and seekable");
            }
            originalPosition = packageStream.Position;
            packageStream.Position = 0;
            var result = AnalyzeCore(
                packageStream,
                fileName,
                packageStream.Length,
                request,
                cancellationToken
            );
            packageStream.Position = originalPosition.Value;
            originalPosition = null;
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw MapFailure(exception, localPath: null);
        }
        finally
        {
            if (originalPosition.HasValue)
            {
                try
                {
                    packageStream.Position = originalPosition.Value;
                }
                catch (Exception)
                {
                    // Preserve the analysis failure.
                }
            }
        }
    }

    private DocumentAnalysisResult AnalyzeCore(
        Stream stream,
        string fileName,
        long fileBytes,
        DocumentAnalysisRequest request,
        CancellationToken cancellationToken
    )
    {
        var resourceLease = new WordOperationResourceLease();
        var package = new OpcPackageReader(_packageLimits, resourceLease).Read(
            stream,
            cancellationToken
        );
        if (request.ExpectedPackageFingerprint is not null
            && !string.Equals(
                request.ExpectedPackageFingerprint,
                package.Fingerprint,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new WordToolkitOperationException(
                "VERSION_CONFLICT",
                "The Word package changed after the inspected fingerprint was issued"
            );
        }

        var semantic = new WordSemanticProjector(null, resourceLease).Project(
            package,
            cancellationToken
        );
        var styles = new WordStyleGraphBuilder(null, resourceLease).Build(
            package,
            semantic,
            cancellationToken
        );
        var numbering = new WordNumberingGraphBuilder(null, resourceLease).Build(
            package,
            semantic,
            styles,
            cancellationToken
        );
        var references = new WordReferenceGraphBuilder(null, resourceLease).Build(
            package,
            semantic,
            cancellationToken
        );
        var sections = new WordSectionGraphBuilder(null, resourceLease).Build(
            package,
            semantic,
            cancellationToken
        );
        var theme = new WordThemeGraphBuilder(null, resourceLease).Build(
            package,
            semantic,
            cancellationToken
        );
        var settings = new WordSettingsGraphBuilder(null, resourceLease).Build(
            package,
            semantic,
            cancellationToken
        );
        var mailMerge = new WordMailMergeGraphBuilder(null, resourceLease).Build(
            package,
            semantic,
            settings,
            references,
            cancellationToken
        );
        var fonts = new WordFontTableGraphBuilder(null, resourceLease).Build(
            package,
            semantic,
            cancellationToken
        );
        var charts = new WordChartGraphBuilder(null, resourceLease).Build(
            package,
            cancellationToken
        );
        var contentControls = new WordContentControlBindingGraphBuilder(
            null,
            resourceLease
        ).Build(package, semantic, cancellationToken);
        var tables = new WordTableGraphBuilder(null, resourceLease).Build(
            package,
            semantic,
            cancellationToken
        );
        var dependencies = new WordDependencyGraphBuilder(null, resourceLease).Build(
            package,
            semantic,
            styles,
            numbering,
            references,
            sections,
            charts,
            contentControls,
            tables,
            mailMerge,
            cancellationToken
        );
        var lint = new WordDocumentLinter(null, resourceLease).Analyze(
            package,
            semantic,
            styles,
            numbering,
            references,
            sections,
            theme,
            settings,
            fonts,
            dependencies,
            tables,
            cancellationToken
        );
        var compatibility = new WordMarkupCompatibilityGraphBuilder(
            null,
            resourceLease
        ).Build(
            package,
            WordMceApplicationConfiguration.Empty,
            cancellationToken
        );

        var semanticCounts = semantic.Nodes
            .GroupBy(node => node.Kind)
            .OrderBy(group => group.Key.ToString(), StringComparer.Ordinal)
            .Select(group => new DocumentAnalysisCount(SnakeCase(group.Key), group.Count()))
            .ToArray();
        var unreachablePartNodes = dependencies.Nodes.Count(node =>
            node.Kind == WordDependencyNodeKind.Part && !node.IsPackageReachable
        );
        var diagnosticDomains = DependencyDiagnosticDomains(dependencies);
        var findingCategories = lint.CategoryCounts
            .Where(pair => pair.Value != 0)
            .OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
            .Select(pair => new DocumentAnalysisCount(SnakeCase(pair.Key), pair.Value))
            .ToArray();
        var opportunities = lint.Findings
            .GroupBy(finding => new
            {
                finding.Fix.Kind,
                finding.Fix.Safety,
                finding.Fix.IsImplemented,
            })
            .OrderBy(group => group.Key.Kind, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Safety.ToString(), StringComparer.Ordinal)
            .Select(group => new DocumentAnalysisOpportunity(
                group.Key.Kind,
                SnakeCase(group.Key.Safety),
                group.Key.IsImplemented,
                group.Count()
            ))
            .ToArray();

        var activeDeclarations = dependencies.Nodes.Count(node =>
            node.Kind == WordDependencyNodeKind.ActiveContentDeclaration
        );
        var activePayloads = dependencies.Nodes.Count(node =>
            node.Kind == WordDependencyNodeKind.ActiveContentPayload
        );
        var activeXControls = dependencies.Nodes.Count(node =>
            node.Kind == WordDependencyNodeKind.ActiveXControl
        );
        var unresolvedActive = dependencies.Nodes.Count(node =>
            node.Kind is WordDependencyNodeKind.ActiveContentDeclaration
                or WordDependencyNodeKind.ActiveContentPayload
                or WordDependencyNodeKind.ActiveXControl
            && !node.IsResolved
        );
        var externalRelationships = package.Relationships.Count(relationship =>
            relationship.TargetMode == OpcRelationshipTargetMode.External
        );

        var allSignals = BuildSignals(
            package,
            dependencies,
            lint,
            compatibility,
            mailMerge,
            activeDeclarations + activePayloads + activeXControls,
            externalRelationships
        );
        var signals = allSignals.Take(request.MaxSignals).ToArray();
        var unmodeled = dependencies.Coverage.ExplicitlyUnmodeledDomains
            .Concat(lint.Coverage.ExplicitlyUnmodeledDomains)
            .Concat(
                [
                    "font_metrics_and_rendered_layout",
                    "full_office_application_configuration_for_markup_compatibility",
                ]
            )
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var omissions = lint.Coverage.Omissions
            .Concat(
                [
                    "selected_list_sequence_and_lint_temporary_allocations_not_operation_accounted",
                ]
            )
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var executionComplete = lint.Coverage.ExecutionComplete
            && !lint.FindingsTruncated
            && !compatibility.IssuesTruncated;
        var budget = resourceLease.Snapshot();
        var xmlParseCache = resourceLease.SnapshotXmlParseCache();

        return new DocumentAnalysisResult(
            DocumentAnalysisWordPackageContract.Contract,
            fileName,
            package.Fingerprint,
            semantic.MainPartUri,
            new DocumentAnalysisPackageSummary(
                fileBytes,
                package.Entries.Count,
                package.Parts.Count,
                package.Relationships.Count,
                externalRelationships,
                unreachablePartNodes,
                package.Diagnostics.Count,
                package.IsStructurallyValid
            ),
            new DocumentAnalysisSemanticSummary(
                semantic.ProjectedPartCount,
                semantic.NodeCount,
                semantic.Warnings.Count,
                semanticCounts
            ),
            new DocumentAnalysisDependencySummary(
                dependencies.Nodes.Count,
                dependencies.Edges.Count,
                dependencies.Issues.Count,
                dependencies.Nodes.Count(node => !node.IsResolved),
                dependencies.Edges.Count(edge => !edge.IsResolved),
                dependencies.Nodes.Count(node => node.IsExternal),
                unreachablePartNodes,
                diagnosticDomains
            ),
            new DocumentAnalysisQualitySummary(
                lint.VisibleFindingCount,
                Count(lint.SeverityCounts, WordLintSeverity.Info),
                Count(lint.SeverityCounts, WordLintSeverity.Warning),
                Count(lint.SeverityCounts, WordLintSeverity.Error),
                Count(lint.SeverityCounts, WordLintSeverity.Fatal),
                lint.Findings.Count(finding => finding.Fix.IsImplemented),
                lint.Findings.Count(finding =>
                    finding.Fix.Safety == WordLintFixSafety.ReviewRequired
                ),
                lint.FindingsTruncated,
                lint.Coverage.ExecutionComplete && !lint.FindingsTruncated,
                lint.Coverage.DocumentCoverageComplete && !lint.FindingsTruncated,
                findingCategories,
                opportunities
            ),
            new DocumentAnalysisSafetySummary(
                externalRelationships,
                activeDeclarations,
                activePayloads,
                activeXControls,
                unresolvedActive,
                activeDeclarations + activePayloads + activeXControls > 0,
                BinaryPayloadsDecoded: false,
                EmbeddedPackagesOpened: false,
                ExternalTargetsFollowed: false,
                CryptographicSignatureValidationPerformed: false
            ),
            new DocumentAnalysisCompatibilitySummary(
                compatibility.Parts.Count,
                compatibility.ParsedElementCount,
                compatibility.Namespaces.Count,
                compatibility.Rules.Count,
                compatibility.AlternateContent.Count,
                compatibility.MustUnderstandMismatches.Count,
                compatibility.Issues.Count,
                compatibility.IssuesTruncated,
                "empty_understood_namespace_and_extension_profile"
            ),
            new DocumentAnalysisMailMergeSummary(
                mailMerge.HasMailMergeEvidence,
                mailMerge.Configuration is null ? 0 : 1,
                mailMerge.Mappings.Count,
                mailMerge.Recipients.Count,
                mailMerge.Recipients.Count(recipient => recipient.IsIncluded),
                mailMerge.Fields.Count,
                mailMerge.Fields.Count(field =>
                    field.BindingStatus is WordMailMergeFieldBindingStatus.ResolvedBySourceColumnName
                        or WordMailMergeFieldBindingStatus.ResolvedByWordPredefinedName
                ),
                mailMerge.Issues.Count,
                mailMerge.IssuesTruncated,
                mailMerge.Configuration?.HasExternalDataSource == true,
                mailMerge.Configuration?.HasSensitiveConnectionMetadata == true,
                ExternalDataSourcesOpened: false
            ),
            allSignals.Count,
            signals.Length,
            signals.Length < allSignals.Count,
            signals,
            new DocumentAnalysisCoverage(
                executionComplete,
                DocumentCoverageComplete: false,
                SemanticCompletenessClaimed: false,
                OperationBudgetCoverageComplete: false,
                unmodeled,
                omissions
            ),
            new DocumentAnalysisDisclosure(
                DocumentTextReturned: false,
                RawXmlReturned: false,
                SourceLocationsReturned: false,
                ExternalRelationshipTargetsReturned: false,
                ExternalRelationshipsFollowed: false,
                ActiveContentExecuted: false,
                MutationPerformed: false,
                WordOpened: false,
                DocumentContentIsUntrusted: true
            ),
            new DocumentAnalysisOperationBudget(
                budget.AccountingModel,
                budget.AccountedBytes,
                budget.MaximumAccountedBytes,
                new DocumentAnalysisXmlParseCache(
                    xmlParseCache.Model,
                    xmlParseCache.Requests,
                    xmlParseCache.UniqueParses,
                    xmlParseCache.CacheHits,
                    xmlParseCache.AvoidedAccountedBytes
                )
            )
        );
    }

    private static IReadOnlyList<DocumentAnalysisSignal> BuildSignals(
        OpcPackageSnapshot package,
        WordDependencyGraph dependencies,
        WordLintReport lint,
        WordMarkupCompatibilityGraph compatibility,
        WordMailMergeGraph mailMerge,
        int activeContentNodeCount,
        int externalRelationshipCount
    )
    {
        var signals = new List<DocumentAnalysisSignal>();
        var fatal = Count(lint.SeverityCounts, WordLintSeverity.Fatal);
        var errors = Count(lint.SeverityCounts, WordLintSeverity.Error);
        if (fatal > 0 || !package.IsStructurallyValid)
        {
            signals.Add(new(
                "STRUCTURAL_PACKAGE_INVALID",
                DocumentAnalysisSignalSeverity.Critical,
                "package",
                Math.Max(fatal, package.Diagnostics.Count),
                "lint_ooxml_document",
                BlocksAutomaticMutation: true
            ));
        }
        if (errors > 0)
        {
            signals.Add(new(
                "LINT_ERROR_FINDINGS",
                DocumentAnalysisSignalSeverity.Error,
                "quality",
                errors,
                "lint_ooxml_document",
                BlocksAutomaticMutation: true
            ));
        }
        if (activeContentNodeCount > 0)
        {
            signals.Add(new(
                "ACTIVE_CONTENT_PRESENT",
                DocumentAnalysisSignalSeverity.Error,
                "security",
                activeContentNodeCount,
                "inspect_ooxml_active_content",
                BlocksAutomaticMutation: true
            ));
        }
        if (externalRelationshipCount > 0)
        {
            signals.Add(new(
                "EXTERNAL_RELATIONSHIPS_PRESENT",
                DocumentAnalysisSignalSeverity.Warning,
                "security",
                externalRelationshipCount,
                "inspect_ooxml_dependencies",
                BlocksAutomaticMutation: true
            ));
        }
        if (mailMerge.HasMailMergeEvidence)
        {
            var hasBlockingEvidence = mailMerge.Configuration?.HasExternalDataSource == true
                || mailMerge.Configuration?.HasSensitiveConnectionMetadata == true
                || mailMerge.Issues.Any(issue =>
                    issue.Severity == WordMailMergeIssueSeverity.Error
                );
            signals.Add(new(
                "MAIL_MERGE_EVIDENCE",
                hasBlockingEvidence
                    ? DocumentAnalysisSignalSeverity.Warning
                    : DocumentAnalysisSignalSeverity.Info,
                "mail_merge",
                Math.Max(
                    1,
                    mailMerge.Fields.Count
                        + mailMerge.Mappings.Count
                        + mailMerge.Recipients.Count
                ),
                "inspect_ooxml_mail_merge",
                BlocksAutomaticMutation: hasBlockingEvidence
            ));
        }
        AddCategorySignal(
            signals,
            lint,
            WordLintCategory.Accessibility,
            "ACCESSIBILITY_FINDINGS",
            "accessibility",
            "lint_ooxml_document"
        );
        AddCategorySignal(
            signals,
            lint,
            WordLintCategory.Style,
            "STYLE_FINDINGS",
            "styles",
            "inspect_ooxml_styles"
        );
        AddCategorySignal(
            signals,
            lint,
            WordLintCategory.Formatting,
            "FORMATTING_FINDINGS",
            "formatting",
            "plan_ooxml_format"
        );
        AddCategorySignal(
            signals,
            lint,
            WordLintCategory.Numbering,
            "NUMBERING_FINDINGS",
            "numbering",
            "inspect_ooxml_numbering"
        );
        AddCategorySignal(
            signals,
            lint,
            WordLintCategory.Reference,
            "REFERENCE_FINDINGS",
            "references",
            "inspect_ooxml_references"
        );
        if (dependencies.Issues.Count > 0)
        {
            signals.Add(new(
                "DEPENDENCY_DIAGNOSTICS",
                DocumentAnalysisSignalSeverity.Warning,
                "dependencies",
                dependencies.Issues.Count,
                "inspect_ooxml_dependencies",
                BlocksAutomaticMutation: true
            ));
        }
        if (compatibility.MustUnderstandMismatches.Count > 0)
        {
            signals.Add(new(
                "MUST_UNDERSTAND_MISMATCH",
                DocumentAnalysisSignalSeverity.Error,
                "markup_compatibility",
                compatibility.MustUnderstandMismatches.Count,
                "inspect_ooxml_markup_compatibility",
                BlocksAutomaticMutation: true
            ));
        }
        else if (compatibility.Issues.Count > 0)
        {
            signals.Add(new(
                "MARKUP_COMPATIBILITY_DIAGNOSTICS",
                DocumentAnalysisSignalSeverity.Warning,
                "markup_compatibility",
                compatibility.Issues.Count,
                "inspect_ooxml_markup_compatibility",
                BlocksAutomaticMutation: true
            ));
        }
        if (lint.Findings.Any(finding => finding.Fix.IsImplemented))
        {
            signals.Add(new(
                "IMPLEMENTED_REPAIR_CANDIDATES",
                DocumentAnalysisSignalSeverity.Info,
                "repair",
                lint.Findings.Count(finding => finding.Fix.IsImplemented),
                "plan_ooxml_lint_repair",
                BlocksAutomaticMutation: false
            ));
        }

        return new ReadOnlyCollection<DocumentAnalysisSignal>(
            signals
                .OrderByDescending(signal => signal.Severity)
                .ThenBy(signal => signal.Code, StringComparer.Ordinal)
                .ToArray()
        );
    }

    private static void AddCategorySignal(
        ICollection<DocumentAnalysisSignal> signals,
        WordLintReport lint,
        WordLintCategory category,
        string code,
        string domain,
        string nextAction
    )
    {
        var count = Count(lint.CategoryCounts, category);
        if (count == 0)
        {
            return;
        }
        signals.Add(new(
            code,
            DocumentAnalysisSignalSeverity.Warning,
            domain,
            count,
            nextAction,
            BlocksAutomaticMutation: false
        ));
    }

    private static IReadOnlyList<DocumentAnalysisCount> DependencyDiagnosticDomains(
        WordDependencyGraph graph
    )
    {
        var values = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["package"] = graph.PackageDiagnosticCount,
            ["styles"] = graph.StyleIssueCount,
            ["numbering"] = graph.NumberingIssueCount,
            ["references"] = graph.ReferenceIssueCount,
            ["sections"] = graph.UnboundSectionStoryCount,
            ["charts"] = graph.ChartIssueCount,
            ["figures"] = graph.FigureIssueCount,
            ["content_controls"] = graph.ContentControlIssueCount,
            ["tables"] = graph.TableIssueCount,
            ["bibliography"] = graph.BibliographyIssueCount,
            ["active_content"] = graph.ActiveContentIssueCount,
            ["document_properties"] = graph.DocumentPropertyIssueCount,
            ["settings"] = graph.SettingsIssueCount,
            ["diagrams"] = graph.DiagramIssueCount,
            ["outline"] = graph.OutlineIssueCount,
            ["mail_merge"] = graph.MailMergeIssueCount,
        };
        return new ReadOnlyCollection<DocumentAnalysisCount>(
            values
                .Where(pair => pair.Value != 0)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new DocumentAnalysisCount(pair.Key, pair.Value))
                .ToArray()
        );
    }

    private static int Count<TKey>(IReadOnlyDictionary<TKey, int> values, TKey key)
        where TKey : notnull => values.TryGetValue(key, out var value) ? value : 0;

    private static void Validate(DocumentAnalysisRequest request, bool requireLeafName)
    {
        if (string.IsNullOrWhiteSpace(request.LocalPath)
            || request.LocalPath.Length > DocumentAnalysisWordPackageContract.MaximumLocalPathCharacters)
        {
            throw Invalid("local_path must be a non-empty bounded path");
        }
        if (requireLeafName && Path.GetFileName(request.LocalPath) != request.LocalPath)
        {
            throw Invalid("Stream analysis requires a leaf file name");
        }
        if (!SupportedExtensions.Contains(Path.GetExtension(request.LocalPath)))
        {
            throw Invalid("local_path must end in DOCX, DOCM, DOTX, or DOTM");
        }
        if (request.ExpectedPackageFingerprint is not null
            && !IsSha256(request.ExpectedPackageFingerprint))
        {
            throw Invalid(
                "expected_package_fingerprint must be exactly 64 hexadecimal characters"
            );
        }
        if (request.MaxSignals is < 1 or > DocumentAnalysisWordPackageContract.MaximumMaxSignals)
        {
            throw Invalid(
                $"max_signals must be between 1 and {DocumentAnalysisWordPackageContract.MaximumMaxSignals}"
            );
        }
    }

    private static string ResolvePath(string localPath)
    {
        try
        {
            var path = Path.GetFullPath(localPath);
            if (!File.Exists(path))
            {
                throw new WordToolkitOperationException(
                    "NOT_FOUND",
                    "The requested Word package does not exist"
                );
            }
            return path;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException
        )
        {
            throw Invalid("local_path is invalid", exception);
        }
    }

    private static WordToolkitOperationException MapFailure(
        Exception exception,
        string? localPath
    )
    {
        if (exception is WordOperationResourceLimitException resourceLimit)
        {
            return new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "Document analysis exceeded its operation resource budget",
                details: new
                {
                    operation_budget = new
                    {
                        model = "wop1",
                        used = resourceLimit.AccountedBytes,
                        maximum = resourceLimit.MaximumAccountedBytes,
                        attempted = resourceLimit.AttemptedBytes,
                        stage = SnakeCase(resourceLimit.Stage),
                    },
                },
                innerException: exception
            );
        }
        if (IsLimit(exception))
        {
            return new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "The Word package exceeds a bounded document-analysis limit",
                details: new { reason_code = "document_analysis_limit" },
                innerException: exception
            );
        }
        if (IsProjectionFailure(exception))
        {
            return new WordToolkitOperationException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be projected into the complete analysis profile",
                details: new { reason_code = "document_analysis_projection_failed" },
                innerException: exception
            );
        }
        return exception switch
        {
            InvalidDataException => new WordToolkitOperationException(
                "INVALID_PACKAGE",
                "The file is not a readable OPC ZIP package",
                innerException: exception
            ),
            UnauthorizedAccessException => new WordToolkitOperationException(
                "ACCESS_DENIED",
                "The Word package cannot be read with current permissions",
                innerException: exception
            ),
            IOException => new WordToolkitOperationException(
                "IO_ERROR",
                localPath is null
                    ? "The Word package stream could not be read"
                    : "The Word package could not be read",
                retryable: true,
                innerException: exception
            ),
            _ => new WordToolkitOperationException(
                "INTERNAL_ERROR",
                "Document analysis failed",
                innerException: exception
            ),
        };
    }

    private static bool IsLimit(Exception exception) => exception is
        OpcPackageLimitException
        or WordSemanticLimitException
        or WordStyleLimitException
        or WordNumberingLimitException
        or WordReferenceLimitException
        or WordSectionLimitException
        or WordThemeLimitException
        or WordSettingsLimitException
        or WordFontTableLimitException
        or WordChartLimitException
        or WordContentControlLimitException
        or WordTableLimitException
        or WordDependencyLimitException
        or WordListSequenceLimitException
        or WordOutlineLimitException
        or WordMceLimitException
        or WordFigureLimitException
        or WordBibliographyLimitException
        or WordActiveContentLimitException
        or WordDocumentPropertyLimitException
        or WordDiagramLimitException;

    private static bool IsProjectionFailure(Exception exception) => exception is
        WordSemanticProjectionException
        or WordStyleProjectionException
        or WordNumberingProjectionException
        or WordReferenceProjectionException
        or WordSectionProjectionException
        or WordThemeProjectionException
        or WordSettingsProjectionException
        or WordFontTableProjectionException
        or WordChartProjectionException
        or WordContentControlProjectionException
        or WordTableProjectionException
        or WordDependencyProjectionException
        or WordListSequenceProjectionException
        or WordOutlineProjectionException
        or WordMceProjectionException
        or WordFigureProjectionException
        or WordActiveContentProjectionException
        or WordDocumentPropertyProjectionException
        or WordDiagramProjectionException
        or WordLintProjectionException;

    private static bool IsSha256(string value) => value.Length == 64
        && value.All(character =>
            character is >= '0' and <= '9'
                or >= 'a' and <= 'f'
                or >= 'A' and <= 'F'
        );

    private static string SnakeCase<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var source = value.ToString();
        var result = new System.Text.StringBuilder(source.Length + 8);
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (char.IsUpper(character) && index != 0)
            {
                result.Append('_');
            }
            result.Append(char.ToLowerInvariant(character));
        }
        return result.ToString();
    }

    private static WordToolkitOperationException Invalid(
        string message,
        Exception? innerException = null
    ) => new("INVALID_INPUT", message, innerException: innerException);
}
