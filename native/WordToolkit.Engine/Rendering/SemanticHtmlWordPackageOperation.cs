using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Rendering;

public static class SemanticHtmlWordPackageContract
{
    public const string OperationName = "render_ooxml_semantic_html";
    public const string Contract = "wordtoolkit.render_ooxml_semantic_html/1.0";
    public const string Backend = "wordtoolkit-semantic-html";
    public const string BackendVersion = "1.0";
    public const string FidelityClass = "semantic_preview_non_paginated";
    public const int MaximumLocalPathCharacters = 32_767;
    public const int MaximumLanguageCharacters = 35;
    public const int MaximumArtifactBytes = 256 * 1024 * 1024;
}

public enum SemanticHtmlStoryScope
{
    MainDocument,
    AllTextStories,
}

public enum SemanticHtmlFragmentWrapper
{
    None,
    TableBody,
    TableBodyRow,
    Table,
    TableBodies,
}

public sealed record SemanticHtmlWordPackageRequest(
    string LocalPath,
    string OutputPath,
    string? ExpectedPackageFingerprint = null,
    SemanticHtmlStoryScope StoryScope = SemanticHtmlStoryScope.MainDocument,
    string Language = "und",
    string? TargetNodeId = null
);

public static class SemanticHtmlWordPackageJson
{
    public static SemanticHtmlWordPackageRequest ParseRequest(string json)
    {
        var request = WordToolkitOperationJson.Deserialize<RequestJson>(json);
        return new SemanticHtmlWordPackageRequest(
            request.LocalPath,
            request.OutputPath,
            request.ExpectedPackageFingerprint,
            request.StoryScope,
            request.Language,
            request.TargetNodeId
        );
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record RequestJson
    {
        public required string LocalPath { get; init; }
        public required string OutputPath { get; init; }
        public string? ExpectedPackageFingerprint { get; init; }
        public SemanticHtmlStoryScope StoryScope { get; init; } =
            SemanticHtmlStoryScope.MainDocument;
        public string Language { get; init; } = "und";
        public string? TargetNodeId { get; init; }
    }
}

public sealed record SemanticHtmlWordPackageResult(
    string OperationContract,
    string InputFileName,
    string OutputFileName,
    string PackageFingerprint,
    string ArtifactSha256,
    long ArtifactBytes,
    string Backend,
    string BackendVersion,
    string FidelityClass,
    SemanticHtmlStoryScope StoryScope,
    int RenderedStoryCount,
    int RenderedNodeCount,
    int ParagraphCount,
    int TableCount,
    int EquationCount,
    int DrawingPlaceholderCount,
    int UnsupportedNodeCount,
    IReadOnlyList<string> Warnings,
    bool OutputCreated,
    bool SourceMutated,
    bool ArtifactContainsDocumentContent,
    bool ExternalResourcesLoaded,
    bool ActiveContentExecuted,
    bool RawXmlReturned,
    bool DocumentTextReturned,
    bool WordOpened,
    bool? SelectionApplied,
    string? TargetNodeId,
    WordSemanticNodeKind? TargetKind,
    string? TargetStoryKind,
    SemanticHtmlFragmentWrapper? FragmentWrapper,
    int? TargetRenderedNodeCount
);

public sealed class SemanticHtmlWordPackageOperation
{
    private readonly SemanticRenderPackageLoader _loader;
    private readonly ISemanticRenderBackend<
        SemanticHtmlBackendRequest,
        SemanticHtmlRenderArtifact
    > _backend;

    public SemanticHtmlWordPackageOperation(
        OpcPackageLimits? packageLimits = null,
        WordSemanticProjectionOptions? projectionOptions = null
    )
    {
        _loader = new SemanticRenderPackageLoader(packageLimits, projectionOptions);
        _backend = SemanticHtmlRenderBackend.Instance;
    }

    public SemanticHtmlWordPackageResult Execute(
        SemanticHtmlWordPackageRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (request is null)
        {
            throw InvalidInput("Render request is required");
        }

        var paths = ValidateAndResolve(request);
        try
        {
            var context = _loader.Load(
                paths.Input,
                request.ExpectedPackageFingerprint,
                cancellationToken
            );

            var selection = SemanticHtmlRenderSelection.Resolve(
                context.Document,
                request.TargetNodeId,
                request.StoryScope
            );
            var rendered = _backend.Render(
                context,
                new SemanticHtmlBackendRequest(
                    request.StoryScope,
                    request.Language,
                    selection
                ),
                cancellationToken
            );

            SemanticRenderArtifactPublisher.PublishCreateNew(
                paths.Output,
                rendered.Bytes,
                "The semantic HTML output already exists",
                cancellationToken
            );

            return new SemanticHtmlWordPackageResult(
                SemanticHtmlWordPackageContract.Contract,
                Path.GetFileName(paths.Input),
                Path.GetFileName(paths.Output),
                context.Package.Fingerprint,
                Convert.ToHexString(SHA256.HashData(rendered.Bytes)).ToLowerInvariant(),
                rendered.Bytes.LongLength,
                SemanticHtmlWordPackageContract.Backend,
                SemanticHtmlWordPackageContract.BackendVersion,
                SemanticHtmlWordPackageContract.FidelityClass,
                request.StoryScope,
                rendered.Statistics.RenderedStoryCount,
                rendered.Statistics.RenderedNodeCount,
                rendered.Statistics.ParagraphCount,
                rendered.Statistics.TableCount,
                rendered.Statistics.EquationCount,
                rendered.Statistics.DrawingPlaceholderCount,
                rendered.Statistics.UnsupportedNodeCount,
                rendered.Warnings,
                OutputCreated: true,
                SourceMutated: false,
                ArtifactContainsDocumentContent: true,
                ExternalResourcesLoaded: false,
                ActiveContentExecuted: false,
                RawXmlReturned: false,
                DocumentTextReturned: false,
                WordOpened: false,
                SelectionApplied: selection is null ? null : true,
                TargetNodeId: selection?.Target.Id.Value,
                TargetKind: selection?.Target.Kind,
                TargetStoryKind: selection?.StoryKind,
                FragmentWrapper: selection?.Wrapper,
                TargetRenderedNodeCount: selection is null
                    ? null
                    : rendered.Statistics.RenderedNodeCount
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
            throw MapFailure(exception);
        }
    }

    private static (string Input, string Output) ValidateAndResolve(
        SemanticHtmlWordPackageRequest request
    )
    {
        if (string.IsNullOrWhiteSpace(request.LocalPath))
        {
            throw InvalidInput("local_path must be a non-empty string");
        }
        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            throw InvalidInput("output_path must be a non-empty string");
        }
        if (
            request.LocalPath.Length > SemanticHtmlWordPackageContract.MaximumLocalPathCharacters
            || request.OutputPath.Length
                > SemanticHtmlWordPackageContract.MaximumLocalPathCharacters
        )
        {
            throw InvalidInput(
                $"Paths cannot exceed {SemanticHtmlWordPackageContract.MaximumLocalPathCharacters} characters"
            );
        }
        if (
            !SemanticRenderPathPolicy.IsAllowedLocalPath(request.LocalPath)
            || !SemanticRenderPathPolicy.IsAllowedLocalPath(request.OutputPath)
        )
        {
            throw InvalidInput("Render paths must be local and must not use UNC or device namespaces");
        }
        if (!InspectWordPackageContract.IsSupportedFileName(request.LocalPath))
        {
            throw InvalidInput("Semantic HTML rendering accepts DOCX, DOCM, DOTX, or DOTM files");
        }
        if (!string.Equals(Path.GetExtension(request.OutputPath), ".html", StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidInput("output_path must use the .html extension");
        }
        if (
            request.ExpectedPackageFingerprint is not null
            && !IsSha256(request.ExpectedPackageFingerprint)
        )
        {
            throw InvalidInput(
                "expected_package_fingerprint must be exactly 64 hexadecimal characters"
            );
        }
        if (request.TargetNodeId is not null)
        {
            if (!SemanticNodeId.HasValidSyntax(request.TargetNodeId))
            {
                throw InvalidInput(
                    $"target_node_id must use the wdn_ prefix, contain only URL-safe identifier characters, and not exceed {SemanticNodeId.MaximumCharacters} characters"
                );
            }
            if (request.ExpectedPackageFingerprint is null)
            {
                throw InvalidInput(
                    "expected_package_fingerprint is required when target_node_id is supplied"
                );
            }
        }
        if (!Enum.IsDefined(request.StoryScope))
        {
            throw InvalidInput("story_scope is not supported");
        }
        ValidateLanguage(request.Language);

        try
        {
            var input = Path.GetFullPath(request.LocalPath);
            var output = Path.GetFullPath(request.OutputPath);
            ValidateLeafName(Path.GetFileName(input), "local_path");
            ValidateLeafName(Path.GetFileName(output), "output_path");
            if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
            {
                throw InvalidInput("local_path and output_path must be different files");
            }
            if (!File.Exists(input))
            {
                throw new WordToolkitOperationException(
                    "NOT_FOUND",
                    "The requested Word package does not exist"
                );
            }
            if (File.Exists(output))
            {
                throw new WordToolkitOperationException(
                    "OUTPUT_EXISTS",
                    "The semantic HTML output already exists"
                );
            }
            var outputDirectory = Path.GetDirectoryName(output);
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
            {
                throw new WordToolkitOperationException(
                    "NOT_FOUND",
                    "The semantic HTML output directory does not exist"
                );
            }
            return (input, output);
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
        )
        {
            throw InvalidInput("A render path is not a valid filesystem path", exception);
        }
    }

    private static void ValidateLanguage(string language)
    {
        if (
            string.IsNullOrWhiteSpace(language)
            || language.Length > SemanticHtmlWordPackageContract.MaximumLanguageCharacters
            || language[0] == '-'
            || language[^1] == '-'
            || language.Contains("--", StringComparison.Ordinal)
            || language.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character == '-')
            )
        )
        {
            throw InvalidInput(
                "language must be a compact BCP 47-style tag containing only ASCII letters, digits, and hyphens"
            );
        }
    }

    private static void ValidateLeafName(string leafName, string field)
    {
        if (
            string.IsNullOrWhiteSpace(leafName)
            || leafName.Length > InspectWordPackageContract.MaximumStreamFileNameCharacters
            || leafName.Any(char.IsControl)
        )
        {
            throw InvalidInput(
                $"{field} must end in a file name of at most {InspectWordPackageContract.MaximumStreamFileNameCharacters} characters without control characters"
            );
        }
    }

    private static WordToolkitOperationException MapFailure(Exception exception) =>
        exception switch
        {
            WordSemanticLimitException limit => PackageLimit(limit),
            WordStyleLimitException limit => PackageLimit(limit),
            WordReviewLimitException limit => PackageLimit(limit),
            WordEquationLimitException limit => PackageLimit(limit),
            OpcPackageLimitException limit => PackageLimit(limit),
            WordSemanticProjectionException projection => InvalidWordPackage(projection),
            WordEquationProjectionException projection => InvalidWordPackage(projection),
            WordStyleProjectionException projection => InvalidWordPackage(projection),
            WordReviewProjectionException projection => InvalidWordPackage(projection),
            InvalidDataException invalid => new WordToolkitOperationException(
                "INVALID_PACKAGE",
                "The file is not a readable OPC ZIP package",
                innerException: invalid
            ),
            UnauthorizedAccessException denied => new WordToolkitOperationException(
                "ACCESS_DENIED",
                "The semantic HTML input or output cannot be accessed with current permissions",
                innerException: denied
            ),
            FileNotFoundException missing => new WordToolkitOperationException(
                "NOT_FOUND",
                "The semantic HTML input or output path no longer exists",
                innerException: missing
            ),
            DirectoryNotFoundException missing => new WordToolkitOperationException(
                "NOT_FOUND",
                "The semantic HTML input or output path no longer exists",
                innerException: missing
            ),
            IOException io => new WordToolkitOperationException(
                "IO_ERROR",
                "The semantic HTML artifact could not be written",
                retryable: true,
                innerException: io
            ),
            ArgumentException invalid => InvalidInput(
                "The semantic HTML render request is invalid",
                invalid
            ),
            _ => new WordToolkitOperationException(
                "INTERNAL_ERROR",
                "The semantic HTML render operation failed",
                innerException: exception
            ),
        };

    private static WordToolkitOperationException PackageLimit(Exception exception) =>
        new(
            "PACKAGE_LIMIT",
            "The package exceeds a bounded semantic rendering limit",
            innerException: exception
        );

    private static WordToolkitOperationException InvalidWordPackage(Exception exception) =>
        new(
            "INVALID_WORD_PACKAGE",
            "The package cannot be projected as a Word semantic document",
            innerException: exception
        );

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static WordToolkitOperationException InvalidInput(
        string message,
        Exception? innerException = null
    ) =>
        new("INVALID_INPUT", message, innerException: innerException);
}

internal sealed record SemanticHtmlRenderStatistics(
    int RenderedStoryCount,
    int RenderedNodeCount,
    int ParagraphCount,
    int TableCount,
    int EquationCount,
    int DrawingPlaceholderCount,
    int UnsupportedNodeCount
);

internal sealed record SemanticHtmlRenderArtifact(
    byte[] Bytes,
    SemanticHtmlRenderStatistics Statistics,
    IReadOnlyList<string> Warnings
);

internal sealed record SemanticHtmlBackendRequest(
    SemanticHtmlStoryScope StoryScope,
    string Language,
    SemanticHtmlRenderSelection? Selection
);

internal sealed class SemanticHtmlRenderBackend
    : ISemanticRenderBackend<SemanticHtmlBackendRequest, SemanticHtmlRenderArtifact>
{
    public static SemanticHtmlRenderBackend Instance { get; } = new();

    private SemanticHtmlRenderBackend() { }

    public SemanticRenderBackendDescriptor Descriptor { get; } = new(
        SemanticHtmlWordPackageContract.Backend,
        SemanticHtmlWordPackageContract.BackendVersion,
        "html",
        "text/html",
        SemanticHtmlWordPackageContract.FidelityClass,
        Paginated: false,
        ExactTextMetrics: false,
        LoadsExternalResources: false,
        ExecutesActiveContent: false
    );

    public SemanticHtmlRenderArtifact Render(
        SemanticRenderPackageContext context,
        SemanticHtmlBackendRequest request,
        CancellationToken cancellationToken
    ) =>
        SemanticHtmlRenderer.Render(
            context.Document,
            context.Styles,
            context.Reviews,
            context.Equations,
            context.InputFileName,
            request.StoryScope,
            request.Language,
            request.Selection,
            cancellationToken
        );
}

internal static class SemanticHtmlTableFragment
{
    public static bool IsRowContainer(WordSemanticNode node) =>
        node.Kind
            is not WordSemanticNodeKind.Table
            and not WordSemanticNodeKind.TableRow
            and not WordSemanticNodeKind.TableCell
        && node.Children.Count != 0
        && node.Children.All(child =>
            child.Kind == WordSemanticNodeKind.TableRow || IsRowContainer(child)
        );

    public static bool IsCellContainer(WordSemanticNode node) =>
        node.Kind
            is not WordSemanticNodeKind.Table
            and not WordSemanticNodeKind.TableRow
            and not WordSemanticNodeKind.TableCell
        && node.Children.Count != 0
        && node.Children.All(child =>
            child.Kind == WordSemanticNodeKind.TableCell || IsCellContainer(child)
        );

    public static bool ContainsUncontainedRowOrCell(WordSemanticNode node)
    {
        if (node.Kind == WordSemanticNodeKind.Table)
        {
            return false;
        }
        foreach (var child in node.Children)
        {
            if (
                child.Kind
                    is WordSemanticNodeKind.TableRow or WordSemanticNodeKind.TableCell
                || ContainsUncontainedRowOrCell(child)
            )
            {
                return true;
            }
        }
        return false;
    }

    public static bool IsSupportedTableChild(WordSemanticNode node) =>
        node.Kind == WordSemanticNodeKind.TableRow
        || IsRowContainer(node)
        || IsCellContainer(node)
        || !ContainsUncontainedRowOrCell(node);

    public static bool IsSupportedRowChild(WordSemanticNode node) =>
        node.Kind == WordSemanticNodeKind.TableCell
        || IsCellContainer(node)
        || !ContainsUncontainedRowOrCell(node);
}

internal sealed record SemanticHtmlRenderSelection(
    WordSemanticNode Target,
    WordSemanticNode StoryRoot,
    string StoryKind,
    SemanticHtmlFragmentWrapper Wrapper
)
{
    public static SemanticHtmlRenderSelection? Resolve(
        WordSemanticDocument document,
        string? targetNodeId,
        SemanticHtmlStoryScope storyScope
    )
    {
        if (targetNodeId is null)
        {
            return null;
        }
        var common = SemanticRenderTargetSelection.Resolve(
            document,
            targetNodeId,
            storyScope == SemanticHtmlStoryScope.AllTextStories
        );
        var target = common.Target;

        if (
            target.Kind == WordSemanticNodeKind.Table
            && target.Children.Any(child =>
                !SemanticHtmlTableFragment.IsSupportedTableChild(child)
            )
        )
        {
            throw new WordToolkitOperationException(
                "TARGET_NOT_RENDERABLE",
                "The requested table target contains an ambiguous nested row or cell wrapper"
            );
        }
        if (
            target.Kind == WordSemanticNodeKind.TableRow
            && target.Children.Any(child =>
                !SemanticHtmlTableFragment.IsSupportedRowChild(child)
            )
        )
        {
            throw new WordToolkitOperationException(
                "TARGET_NOT_RENDERABLE",
                "The requested table-row target contains an ambiguous nested cell wrapper"
            );
        }
        var rowContainer = SemanticHtmlTableFragment.IsRowContainer(target);
        var cellContainer = SemanticHtmlTableFragment.IsCellContainer(target);
        if (
            target.Kind
                is not WordSemanticNodeKind.Table
                and not WordSemanticNodeKind.TableRow
                and not WordSemanticNodeKind.TableCell
            && SemanticHtmlTableFragment.ContainsUncontainedRowOrCell(target)
            && !rowContainer
            && !cellContainer
        )
        {
            throw new WordToolkitOperationException(
                "TARGET_NOT_RENDERABLE",
                "The requested semantic target mixes incompatible nested table fragments"
            );
        }

        var wrapper = target.Kind switch
        {
            WordSemanticNodeKind.Table => SemanticHtmlFragmentWrapper.TableBodies,
            WordSemanticNodeKind.TableRow => SemanticHtmlFragmentWrapper.TableBody,
            WordSemanticNodeKind.TableCell => SemanticHtmlFragmentWrapper.TableBodyRow,
            _ when rowContainer => SemanticHtmlFragmentWrapper.Table,
            _ when cellContainer => SemanticHtmlFragmentWrapper.TableBodyRow,
            _ => SemanticHtmlFragmentWrapper.None,
        };
        return new SemanticHtmlRenderSelection(
            target,
            common.StoryRoot,
            common.StoryKind,
            wrapper
        );
    }
}

internal static class SemanticHtmlRenderer
{
    public static SemanticHtmlRenderArtifact Render(
        WordSemanticDocument document,
        WordStyleGraph styles,
        WordReviewGraph reviews,
        WordEquationGraph equations,
        string inputFileName,
        SemanticHtmlStoryScope storyScope,
        string language,
        SemanticHtmlRenderSelection? selection,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(styles);
        ArgumentNullException.ThrowIfNull(reviews);
        ArgumentNullException.ThrowIfNull(equations);

        var state = new RenderState(
            document,
            styles,
            reviews,
            equations,
            normalizeTableContexts: selection is not null
        );
        var estimatedNodeCount = selection?.Target.DescendantsAndSelf().Count()
            ?? document.NodeCount;
        var builder = new StringBuilder(Math.Max(4_096, estimatedNodeCount * 32));
        builder.Append("<!doctype html>\n<html lang=\"")
            .Append(Encode(language))
            .Append("\">\n<head>\n<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"generator\" content=\"WordToolkit semantic HTML 1.0\">\n")
            .Append("<meta name=\"wordtoolkit-fidelity\" content=\"semantic_preview_non_paginated\">\n")
            .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; form-action 'none'\">\n")
            .Append("<meta name=\"referrer\" content=\"no-referrer\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">\n")
            .Append("<title>")
            .Append(Encode(inputFileName))
            .Append("</title>\n<style>\n")
            .Append(Css)
            .Append("</style>\n</head>\n<body>\n")
            .Append("<aside class=\"wt-notice\" role=\"note\">Semantic, non-paginated preview. Visual parity with Microsoft Word is not claimed.</aside>\n")
            .Append("<main class=\"wt-document\" data-package-fingerprint=\"")
            .Append(Encode(document.PackageFingerprint))
            .Append('"');
        if (selection is not null)
        {
            builder.Append(" data-selection-applied=\"true\" data-target-node-id=\"")
                .Append(Encode(selection.Target.Id.Value))
                .Append("\" data-target-kind=\"")
                .Append(Encode(SnakeCase(selection.Target.Kind)))
                .Append("\"");
        }
        builder.Append(">\n");

        var storyRoots = selection is null
            ? SelectStoryRoots(document, storyScope)
            : [selection.StoryRoot];
        foreach (var root in storyRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.RenderedStoryCount++;
            builder.Append("<section class=\"wt-story wt-story-")
                .Append(Encode(StoryKind(root)))
                .Append("\" data-part-uri=\"")
                .Append(Encode(root.SourcePartUri))
                .Append("\" aria-label=\"")
                .Append(Encode(StoryLabel(root)))
                .Append('"');
            if (selection is not null)
            {
                builder.Append(" data-selection-root=\"true\" data-target-node-id=\"")
                    .Append(Encode(selection.Target.Id.Value))
                    .Append("\"");
            }
            builder.Append(">\n");
            if (storyScope == SemanticHtmlStoryScope.AllTextStories)
            {
                builder.Append("<div class=\"wt-story-label\" aria-hidden=\"true\">")
                    .Append(Encode(StoryLabel(root)))
                    .Append("</div>\n");
            }
            if (selection is null)
            {
                RenderNode(root, builder, state, cancellationToken);
            }
            else
            {
                RenderFragmentRoot(selection, builder, state, cancellationToken);
            }
            builder.Append("</section>\n");
        }

        builder.Append("</main>\n</body>\n</html>\n");
        var warnings = state.Warnings
            .Concat(selection is null ? [] : ["SEMANTIC_SUBTREE_SELECTED"])
            .Concat(
                selection is null || selection.Wrapper == SemanticHtmlFragmentWrapper.None
                    ? []
                    : ["FRAGMENT_TABLE_CONTEXT_SYNTHESIZED"]
            )
            .Concat(document.Warnings.Count == 0 ? [] : ["SEMANTIC_PROJECTION_WARNINGS"])
            .Concat(["SEMANTIC_PREVIEW_NON_PAGINATED", "VISUAL_FORMATTING_APPROXIMATED"])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(
            builder.ToString()
        );
        if (bytes.Length > SemanticHtmlWordPackageContract.MaximumArtifactBytes)
        {
            throw new WordSemanticLimitException(
                $"Semantic HTML artifact exceeds {SemanticHtmlWordPackageContract.MaximumArtifactBytes} bytes."
            );
        }
        return new SemanticHtmlRenderArtifact(
            bytes,
            new SemanticHtmlRenderStatistics(
                state.RenderedStoryCount,
                state.RenderedNodeCount,
                state.ParagraphCount,
                state.TableCount,
                state.EquationCount,
                state.DrawingPlaceholderCount,
                state.UnsupportedNodeCount
            ),
            warnings
        );
    }

    private static void RenderFragmentRoot(
        SemanticHtmlRenderSelection selection,
        StringBuilder builder,
        RenderState state,
        CancellationToken cancellationToken
    )
    {
        switch (selection.Wrapper)
        {
            case SemanticHtmlFragmentWrapper.TableBodies:
                RenderSelectedTable(
                    selection.Target,
                    builder,
                    state,
                    cancellationToken
                );
                break;
            case SemanticHtmlFragmentWrapper.TableBody:
                builder.Append("<table class=\"wt-table wt-fragment-context\"><tbody>");
                RenderSelectedRow(selection.Target, builder, state, cancellationToken);
                builder.Append("</tbody></table>\n");
                break;
            case SemanticHtmlFragmentWrapper.TableBodyRow
                when selection.Target.Kind == WordSemanticNodeKind.TableCell:
                builder.Append("<table class=\"wt-table wt-fragment-context\"><tbody><tr>");
                RenderNode(selection.Target, builder, state, cancellationToken);
                builder.Append("</tr></tbody></table>\n");
                break;
            case SemanticHtmlFragmentWrapper.Table:
                builder.Append("<table class=\"wt-table wt-fragment-context\">");
                RenderContextualFragmentContainer(
                    "tbody",
                    selection.Target,
                    builder,
                    state,
                    cancellationToken
                );
                builder.Append("</table>\n");
                break;
            case SemanticHtmlFragmentWrapper.TableBodyRow:
                builder.Append("<table class=\"wt-table wt-fragment-context\">");
                RenderContextualFragmentContainer(
                    "tbody",
                    selection.Target,
                    builder,
                    state,
                    cancellationToken,
                    innerTag: "tr"
                );
                builder.Append("</table>\n");
                break;
            default:
                RenderNode(selection.Target, builder, state, cancellationToken);
                break;
        }
    }

    private static void RenderSelectedTable(
        WordSemanticNode table,
        StringBuilder builder,
        RenderState state,
        CancellationToken cancellationToken,
        bool countNode = true
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (countNode)
        {
            state.RenderedNodeCount++;
        }
        state.TableCount++;
        state.Warnings.Add("FRAGMENT_TABLE_CONTEXT_SYNTHESIZED");
        builder.Append("<table class=\"wt-table\" data-node-id=\"")
            .Append(Encode(table.Id.Value))
            .Append("\">");
        var rawBodyOpen = false;
        foreach (var child in table.Children.OrderBy(child => child.SourceOrder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (child.Kind == WordSemanticNodeKind.TableRow)
            {
                if (!rawBodyOpen)
                {
                    builder.Append("<tbody class=\"wt-fragment-context\">");
                    rawBodyOpen = true;
                }
                RenderSelectedRow(child, builder, state, cancellationToken);
                continue;
            }
            if (rawBodyOpen)
            {
                builder.Append("</tbody>");
                rawBodyOpen = false;
            }

            if (
                child.Children.Count == 0
                || SemanticHtmlTableFragment.IsRowContainer(child)
            )
            {
                RenderContextualFragmentContainer(
                    "tbody",
                    child,
                    builder,
                    state,
                    cancellationToken
                );
            }
            else if (
                SemanticHtmlTableFragment.IsCellContainer(child)
            )
            {
                RenderContextualFragmentContainer(
                    "tbody",
                    child,
                    builder,
                    state,
                    cancellationToken,
                    innerTag: "tr"
                );
            }
            else
            {
                state.Warnings.Add("TABLE_CHILD_CONTEXT_APPROXIMATED");
                builder.Append("<tbody class=\"wt-fragment-context\"><tr><td>");
                RenderNode(child, builder, state, cancellationToken);
                builder.Append("</td></tr></tbody>");
            }
        }
        if (rawBodyOpen)
        {
            builder.Append("</tbody>");
        }
        builder.Append("</table>\n");
    }

    private static void RenderSelectedRow(
        WordSemanticNode row,
        StringBuilder builder,
        RenderState state,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.RenderedNodeCount++;
        builder.Append("<tr class=\"wt-table-row\" data-node-id=\"")
            .Append(Encode(row.Id.Value))
            .Append("\">");
        foreach (var child in row.Children.OrderBy(child => child.SourceOrder))
        {
            if (child.Kind == WordSemanticNodeKind.TableCell)
            {
                RenderNode(child, builder, state, cancellationToken);
            }
            else if (SemanticHtmlTableFragment.IsCellContainer(child))
            {
                state.Warnings.Add("NESTED_TABLE_FRAGMENT_WRAPPERS_FLATTENED");
                _ = AccountContextualTarget(child, state);
                RenderTableFragmentChildren(
                    child,
                    expectRows: false,
                    builder,
                    state,
                    cancellationToken
                );
            }
            else
            {
                state.Warnings.Add("TABLE_ROW_CHILD_CONTEXT_APPROXIMATED");
                builder.Append("<td class=\"wt-fragment-context\">");
                RenderNode(child, builder, state, cancellationToken);
                builder.Append("</td>");
            }
        }
        builder.Append("</tr>\n");
    }

    private static void RenderContextualFragmentContainer(
        string tag,
        WordSemanticNode target,
        StringBuilder builder,
        RenderState state,
        CancellationToken cancellationToken,
        string? innerTag = null
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var revisionKind = AccountContextualTarget(target, state);
        builder.Append('<').Append(tag)
            .Append(" class=\"wt-fragment-target wt-")
            .Append(Encode(CssKind(target.Kind)));
        if (revisionKind is not null)
        {
            builder.Append(" wt-revision-").Append(Encode(revisionKind));
        }
        builder.Append("\" data-node-id=\"")
            .Append(Encode(target.Id.Value))
            .Append('"');
        if (revisionKind is not null)
        {
            builder.Append(" data-revision-kind=\"")
                .Append(Encode(revisionKind))
                .Append('"');
        }
        builder.Append('>');
        if (innerTag is not null)
        {
            builder.Append('<').Append(innerTag).Append(" class=\"wt-fragment-context\">");
        }
        RenderTableFragmentChildren(
            target,
            expectRows: innerTag is null,
            builder,
            state,
            cancellationToken
        );
        if (innerTag is not null)
        {
            builder.Append("</").Append(innerTag).Append('>');
        }
        builder.Append("</").Append(tag).Append(">\n");
    }

    private static string? AccountContextualTarget(
        WordSemanticNode target,
        RenderState state
    )
    {
        state.RenderedNodeCount++;
        string? revisionKind = null;
        if (target.Kind == WordSemanticNodeKind.Revision)
        {
            state.Warnings.Add("TRACKED_REVISIONS_ANNOTATED");
            revisionKind = state.Revisions.TryGetValue(target.Id, out var revision)
                ? SnakeCase(revision.Kind)
                : "unknown";
            if (revision is null)
            {
                state.UnsupportedNodeCount++;
                state.Warnings.Add("REVISION_MAPPING_MISSING");
            }
        }
        else if (target.Kind == WordSemanticNodeKind.AlternateContent)
        {
            state.UnsupportedNodeCount++;
            state.Warnings.Add("ALTERNATE_CONTENT_APPROXIMATED");
        }
        return revisionKind;
    }

    private static void RenderTableFragmentChildren(
        WordSemanticNode container,
        bool expectRows,
        StringBuilder builder,
        RenderState state,
        CancellationToken cancellationToken
    )
    {
        foreach (var child in container.Children.OrderBy(child => child.SourceOrder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (expectRows && child.Kind == WordSemanticNodeKind.TableRow)
            {
                RenderSelectedRow(child, builder, state, cancellationToken);
            }
            else if (!expectRows && child.Kind == WordSemanticNodeKind.TableCell)
            {
                RenderNode(child, builder, state, cancellationToken);
            }
            else if (
                expectRows
                    ? SemanticHtmlTableFragment.IsRowContainer(child)
                    : SemanticHtmlTableFragment.IsCellContainer(child)
            )
            {
                state.Warnings.Add("NESTED_TABLE_FRAGMENT_WRAPPERS_FLATTENED");
                _ = AccountContextualTarget(child, state);
                RenderTableFragmentChildren(
                    child,
                    expectRows,
                    builder,
                    state,
                    cancellationToken
                );
            }
            else
            {
                throw new InvalidOperationException(
                    "A validated table fragment contains an incompatible child."
                );
            }
        }
    }

    private static IReadOnlyList<WordSemanticNode> SelectStoryRoots(
        WordSemanticDocument document,
        SemanticHtmlStoryScope scope
    )
    {
        var roots = document.Root.Children
            .Where(node => IsStoryRoot(node.Kind))
            .OrderBy(node => node.SourceOrder)
            .ToArray();
        if (scope == SemanticHtmlStoryScope.AllTextStories)
        {
            return roots;
        }
        var main = roots.Where(node =>
            node.Kind == WordSemanticNodeKind.Body
            && string.Equals(node.SourcePartUri, document.MainPartUri, StringComparison.Ordinal)
        ).ToArray();
        if (main.Length != 1)
        {
            throw new WordSemanticProjectionException(
                "The main Word document does not contain exactly one semantic body root."
            );
        }
        return main;
    }

    private static bool IsStoryRoot(WordSemanticNodeKind kind) =>
        kind is WordSemanticNodeKind.Body
            or WordSemanticNodeKind.Header
            or WordSemanticNodeKind.Footer
            or WordSemanticNodeKind.Footnotes
            or WordSemanticNodeKind.Endnotes
            or WordSemanticNodeKind.Comments
            or WordSemanticNodeKind.GlossaryDocument;

    private static void RenderNode(
        WordSemanticNode node,
        StringBuilder builder,
        RenderState state,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.RenderedNodeCount++;
        switch (node.Kind)
        {
            case WordSemanticNodeKind.Document:
            case WordSemanticNodeKind.Body:
            case WordSemanticNodeKind.Header:
            case WordSemanticNodeKind.Footer:
            case WordSemanticNodeKind.Footnotes:
            case WordSemanticNodeKind.Endnotes:
            case WordSemanticNodeKind.Comments:
            case WordSemanticNodeKind.GlossaryDocument:
                RenderChildren(node, builder, state, cancellationToken);
                break;
            case WordSemanticNodeKind.Footnote:
            case WordSemanticNodeKind.Endnote:
            case WordSemanticNodeKind.Comment:
            case WordSemanticNodeKind.GlossaryEntry:
            case WordSemanticNodeKind.TextBox:
                RenderAdaptiveContainer(
                    "wt-object wt-" + CssKind(node.Kind),
                    node,
                    builder,
                    state,
                    cancellationToken
                );
                break;
            case WordSemanticNodeKind.Paragraph:
                RenderParagraph(node, builder, state, cancellationToken);
                break;
            case WordSemanticNodeKind.Run:
                RenderContainer("span", "wt-run", node, builder, state, cancellationToken);
                break;
            case WordSemanticNodeKind.Text:
                builder.Append(Encode(node.Text ?? ""));
                break;
            case WordSemanticNodeKind.Tab:
                builder.Append("<span class=\"wt-tab\" aria-label=\"tab\">\t</span>");
                break;
            case WordSemanticNodeKind.Break:
                builder.Append("<br>\n");
                break;
            case WordSemanticNodeKind.Table:
                if (state.NormalizeTableContexts)
                {
                    RenderSelectedTable(
                        node,
                        builder,
                        state,
                        cancellationToken,
                        countNode: false
                    );
                }
                else
                {
                    state.TableCount++;
                    RenderContainer(
                        "table",
                        "wt-table",
                        node,
                        builder,
                        state,
                        cancellationToken
                    );
                }
                break;
            case WordSemanticNodeKind.TableRow:
                RenderContainer("tr", "wt-table-row", node, builder, state, cancellationToken);
                break;
            case WordSemanticNodeKind.TableCell:
                RenderContainer("td", "wt-table-cell", node, builder, state, cancellationToken);
                break;
            case WordSemanticNodeKind.Hyperlink:
                state.Warnings.Add("HYPERLINKS_RENDERED_INERT");
                RenderContainer("span", "wt-hyperlink-inert", node, builder, state, cancellationToken);
                break;
            case WordSemanticNodeKind.Field:
                state.Warnings.Add("FIELD_INSTRUCTIONS_SUPPRESSED");
                RenderChildren(node, builder, state, cancellationToken);
                break;
            case WordSemanticNodeKind.Equation:
                RenderEquation(node, builder, state);
                break;
            case WordSemanticNodeKind.EquationComponent:
                RenderChildren(node, builder, state, cancellationToken);
                break;
            case WordSemanticNodeKind.ContentControl:
                RenderAdaptiveContainer(
                    "wt-content-control",
                    node,
                    builder,
                    state,
                    cancellationToken
                );
                break;
            case WordSemanticNodeKind.Revision:
                RenderRevision(node, builder, state, cancellationToken);
                break;
            case WordSemanticNodeKind.Drawing:
                state.DrawingPlaceholderCount++;
                state.UnsupportedNodeCount++;
                state.Warnings.Add("DRAWINGS_RENDERED_AS_PLACEHOLDERS");
                builder.Append("<span class=\"wt-placeholder wt-drawing\" role=\"img\" aria-label=\"Drawing omitted\">[Drawing]</span>");
                break;
            case WordSemanticNodeKind.AlternateContent:
                state.UnsupportedNodeCount++;
                state.Warnings.Add("ALTERNATE_CONTENT_APPROXIMATED");
                RenderAdaptiveContainer(
                    "wt-alternate-content",
                    node,
                    builder,
                    state,
                    cancellationToken
                );
                break;
            case WordSemanticNodeKind.ExtensionIsland:
                state.UnsupportedNodeCount++;
                state.Warnings.Add("EXTENSION_CONTENT_RENDERED_AS_PLACEHOLDER");
                builder.Append("<span class=\"wt-placeholder wt-extension\">[Unsupported extension content]</span>");
                break;
            case WordSemanticNodeKind.Bookmark:
            case WordSemanticNodeKind.BookmarkEnd:
            case WordSemanticNodeKind.CommentAnchor:
            case WordSemanticNodeKind.HeaderReference:
            case WordSemanticNodeKind.FooterReference:
            case WordSemanticNodeKind.FootnoteReference:
            case WordSemanticNodeKind.EndnoteReference:
            case WordSemanticNodeKind.Section:
                RenderChildren(node, builder, state, cancellationToken);
                break;
            default:
                state.UnsupportedNodeCount++;
                state.Warnings.Add("UNSUPPORTED_SEMANTIC_NODES_OMITTED");
                RenderChildren(node, builder, state, cancellationToken);
                break;
        }
    }

    private static void RenderParagraph(
        WordSemanticNode node,
        StringBuilder builder,
        RenderState state,
        CancellationToken cancellationToken
    )
    {
        state.ParagraphCount++;
        var level = state.HeadingLevel(node);
        var tag = level is >= 1 and <= 6 ? $"h{level}" : "p";
        if (level is > 6 and <= 9)
        {
            tag = "div";
            state.Warnings.Add("HEADING_LEVELS_ABOVE_SIX_USE_ARIA");
        }
        builder.Append('<').Append(tag).Append(" class=\"wt-paragraph");
        if (level is > 6 and <= 9)
        {
            builder.Append(" wt-heading\" role=\"heading\" aria-level=\"")
                .Append(level.Value.ToString(CultureInfo.InvariantCulture));
        }
        builder.Append("\" data-node-id=\"")
            .Append(Encode(node.Id.Value))
            .Append("\">");
        RenderChildren(node, builder, state, cancellationToken);
        builder.Append("</").Append(tag).Append(">\n");
    }

    private static void RenderEquation(
        WordSemanticNode node,
        StringBuilder builder,
        RenderState state
    )
    {
        state.EquationCount++;
        state.Warnings.Add("EQUATIONS_RENDERED_AS_LINEAR_TEXT");
        if (
            state.Equations.TryGetValue(node.Id, out var equation)
            && equation is not null
        )
        {
            if (!equation.IsCanonical || equation.UnsupportedNodeCount != 0)
            {
                state.UnsupportedNodeCount += Math.Max(1, equation.UnsupportedNodeCount);
                state.Warnings.Add("EQUATION_CONTENT_APPROXIMATED");
            }
            builder.Append("<span class=\"wt-equation wt-equation-text\" role=\"math\" data-equation-id=\"")
                .Append(Encode(equation.Id))
                .Append("\">")
                .Append(Encode(equation.Text))
                .Append("</span>");
            return;
        }
        state.UnsupportedNodeCount++;
        state.Warnings.Add("EQUATION_MAPPING_MISSING");
        builder.Append("<span class=\"wt-placeholder wt-equation\" role=\"math\">[Equation]</span>");
    }

    private static void RenderRevision(
        WordSemanticNode node,
        StringBuilder builder,
        RenderState state,
        CancellationToken cancellationToken
    )
    {
        state.Warnings.Add("TRACKED_REVISIONS_ANNOTATED");
        var kind = state.Revisions.TryGetValue(node.Id, out var revision)
            ? SnakeCase(revision.Kind)
            : "unknown";
        if (revision is null)
        {
            state.UnsupportedNodeCount++;
            state.Warnings.Add("REVISION_MAPPING_MISSING");
        }
        var tag = AdaptiveContainerTag(node);
        if (tag is null)
        {
            state.Warnings.Add("TABLE_CELL_WRAPPER_APPROXIMATED");
            RenderChildren(node, builder, state, cancellationToken);
            return;
        }
        builder.Append('<').Append(tag).Append(" class=\"wt-revision wt-revision-")
            .Append(Encode(kind))
            .Append("\" data-revision-kind=\"")
            .Append(Encode(kind))
            .Append("\">");
        RenderChildren(node, builder, state, cancellationToken);
        builder.Append("</").Append(tag).Append('>');
        if (tag is "div" or "tbody")
        {
            builder.Append('\n');
        }
    }

    private static void RenderAdaptiveContainer(
        string cssClass,
        WordSemanticNode node,
        StringBuilder builder,
        RenderState state,
        CancellationToken cancellationToken
    )
    {
        var tag = AdaptiveContainerTag(node);
        if (tag is null)
        {
            state.Warnings.Add("TABLE_CELL_WRAPPER_APPROXIMATED");
            RenderChildren(node, builder, state, cancellationToken);
            return;
        }
        RenderContainer(tag, cssClass, node, builder, state, cancellationToken);
    }

    private static string? AdaptiveContainerTag(WordSemanticNode node)
    {
        if (node.Children.Any(child => child.Kind == WordSemanticNodeKind.TableCell))
        {
            return null;
        }
        if (node.Children.Any(child => child.Kind == WordSemanticNodeKind.TableRow))
        {
            return "tbody";
        }
        return node.Children.Any(child =>
            child.Kind is WordSemanticNodeKind.Paragraph
                or WordSemanticNodeKind.Table
                or WordSemanticNodeKind.Footnote
                or WordSemanticNodeKind.Endnote
                or WordSemanticNodeKind.Comment
                or WordSemanticNodeKind.GlossaryEntry
                or WordSemanticNodeKind.TextBox
        )
            ? "div"
            : "span";
    }

    private static void RenderContainer(
        string tag,
        string cssClass,
        WordSemanticNode node,
        StringBuilder builder,
        RenderState state,
        CancellationToken cancellationToken
    )
    {
        builder.Append('<').Append(tag).Append(" class=\"")
            .Append(Encode(cssClass))
            .Append("\" data-node-id=\"")
            .Append(Encode(node.Id.Value))
            .Append("\">");
        RenderChildren(node, builder, state, cancellationToken);
        builder.Append("</").Append(tag).Append('>');
        if (tag is "div" or "table" or "tbody" or "tr")
        {
            builder.Append('\n');
        }
    }

    private static void RenderChildren(
        WordSemanticNode node,
        StringBuilder builder,
        RenderState state,
        CancellationToken cancellationToken
    )
    {
        foreach (var child in node.Children.OrderBy(child => child.SourceOrder))
        {
            RenderNode(child, builder, state, cancellationToken);
        }
    }

    private static string StoryKind(WordSemanticNode node) =>
        node.Properties.TryGetValue("story_kind", out var storyKind)
            ? storyKind
            : SnakeCase(node.Kind);

    private static string StoryLabel(WordSemanticNode node) =>
        node.Kind == WordSemanticNodeKind.Body
            ? "Main document"
            : node.Kind switch
            {
                WordSemanticNodeKind.Header => "Header",
                WordSemanticNodeKind.Footer => "Footer",
                WordSemanticNodeKind.Footnotes => "Footnotes",
                WordSemanticNodeKind.Endnotes => "Endnotes",
                WordSemanticNodeKind.Comments => "Comments",
                WordSemanticNodeKind.GlossaryDocument => "Glossary",
                _ => StoryKind(node),
            };

    private static string CssKind(WordSemanticNodeKind kind) => SnakeCase(kind);

    private static string SnakeCase<T>(T value)
        where T : struct, Enum =>
        string.Concat(
                value.ToString().Select((character, index) =>
                    char.IsUpper(character) && index != 0
                        ? "_" + char.ToLowerInvariant(character)
                        : char.ToLowerInvariant(character).ToString()
                )
            );

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private const string Css =
        ":root{color-scheme:light dark;font-family:system-ui,sans-serif;line-height:1.5}"
        + "body{margin:0;background:#ececec;color:#171717}"
        + ".wt-notice{padding:.75rem 1rem;background:#fff3cd;border-bottom:1px solid #d6b656;font-size:.9rem}"
        + ".wt-document{box-sizing:border-box;max-width:70rem;margin:2rem auto;padding:3rem;background:#fff;color:#171717;box-shadow:0 2px 16px #0002}"
        + ".wt-story+.wt-story{margin-top:3rem;padding-top:2rem;border-top:1px solid #bbb}"
        + ".wt-story-label{font:600 1rem system-ui,sans-serif;color:#555}"
        + ".wt-paragraph{white-space:pre-wrap;overflow-wrap:anywhere;min-height:1em}"
        + ".wt-table{border-collapse:collapse;max-width:100%}"
        + ".wt-table-cell{border:1px solid #888;padding:.25rem .5rem;vertical-align:top}"
        + ".wt-tab{white-space:pre}"
        + ".wt-hyperlink-inert{color:#065fd4;text-decoration:underline}"
        + ".wt-revision-insertion,.wt-revision-conflict_insertion,.wt-revision-move_to{background:#d7f5dd;text-decoration:underline}"
        + ".wt-revision-deletion,.wt-revision-conflict_deletion,.wt-revision-move_from{background:#ffd9d9;text-decoration:line-through}"
        + ".wt-equation{font-family:Cambria Math,STIX Two Math,serif;white-space:pre-wrap}"
        + ".wt-placeholder{display:inline-block;padding:0 .25rem;border:1px dashed #777;color:#555;font:italic .9em system-ui,sans-serif}"
        + "@media(max-width:48rem){.wt-document{margin:0;padding:1.25rem;box-shadow:none}}"
        + "@media(prefers-color-scheme:dark){body{background:#171717;color:#eee}.wt-document{background:#252525;color:#eee}.wt-notice{background:#443b16;color:#fff}.wt-story-label,.wt-placeholder{color:#ccc}}\n";

    private sealed class RenderState
    {
        private readonly WordStyleGraph _styles;

        public RenderState(
            WordSemanticDocument document,
            WordStyleGraph styles,
            WordReviewGraph reviews,
            WordEquationGraph equations,
            bool normalizeTableContexts
        )
        {
            _styles = styles;
            NormalizeTableContexts = normalizeTableContexts;
            Revisions = reviews.Revisions
                .Where(revision => revision.SemanticNodeId is not null)
                .GroupBy(revision => revision.SemanticNodeId!.Value)
                .ToDictionary(group => group.Key, group => group.First());
            Equations = equations.Equations
                .Where(equation => equation.SemanticNodeId is not null)
                .GroupBy(equation => equation.SemanticNodeId!.Value)
                .ToDictionary(group => group.Key, group => group.First());
            if (reviews.Revisions.Any(revision => IsFormattingRevision(revision.Kind)))
            {
                Warnings.Add("FORMATTING_REVISIONS_APPROXIMATED");
            }
            if (styles.Issues.Count != 0)
            {
                Warnings.Add("STYLE_GRAPH_WARNINGS");
            }
            if (reviews.Issues.Count != 0)
            {
                Warnings.Add("REVIEW_GRAPH_WARNINGS");
            }
            if (equations.Issues.Count != 0)
            {
                Warnings.Add("EQUATION_GRAPH_WARNINGS");
            }
            if (document.Root.Children.Count == 0)
            {
                Warnings.Add("EMPTY_SEMANTIC_DOCUMENT");
            }
        }

        public Dictionary<SemanticNodeId, WordRevisionDefinition> Revisions { get; }
        public Dictionary<SemanticNodeId, WordEquationDefinition> Equations { get; }
        public bool NormalizeTableContexts { get; }
        public HashSet<string> Warnings { get; } = new(StringComparer.Ordinal);
        public int RenderedStoryCount { get; set; }
        public int RenderedNodeCount { get; set; }
        public int ParagraphCount { get; set; }
        public int TableCount { get; set; }
        public int EquationCount { get; set; }
        public int DrawingPlaceholderCount { get; set; }
        public int UnsupportedNodeCount { get; set; }

        public int? HeadingLevel(WordSemanticNode node)
        {
            if (
                !node.Properties.TryGetValue("style_id", out var styleId)
                || !_styles.TryGetStyle(styleId, out var style)
                || style is null
                || !style.InheritanceResolvable
            )
            {
                return null;
            }
            int? result = null;
            foreach (var chainId in style.InheritanceChainStyleIds)
            {
                if (
                    _styles.TryGetStyle(chainId, out var chainStyle)
                    && chainStyle is not null
                    && chainStyle.ParagraphProperties.Values.TryGetValue(
                        "outline_level",
                        out var raw
                    )
                    && int.TryParse(
                        raw,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var value
                    )
                )
                {
                    result = value == 9 ? null : value + 1;
                }
            }
            return result;
        }

        private static bool IsFormattingRevision(WordRevisionKind kind) =>
            kind is WordRevisionKind.RunPropertiesChange
                or WordRevisionKind.ParagraphPropertiesChange
                or WordRevisionKind.TablePropertiesChange
                or WordRevisionKind.TableGridChange
                or WordRevisionKind.TableRowPropertiesChange
                or WordRevisionKind.TableCellPropertiesChange
                or WordRevisionKind.SectionPropertiesChange
                or WordRevisionKind.NumberingPropertiesChange
                or WordRevisionKind.NumberingChange
                or WordRevisionKind.OtherPropertyChange;
    }
}
