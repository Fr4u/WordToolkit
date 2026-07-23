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

public sealed record SemanticHtmlWordPackageRequest(
    string LocalPath,
    string OutputPath,
    string? ExpectedPackageFingerprint = null,
    SemanticHtmlStoryScope StoryScope = SemanticHtmlStoryScope.MainDocument,
    string Language = "und"
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
            request.Language
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
    bool WordOpened
);

public sealed class SemanticHtmlWordPackageOperation
{
    private readonly OpcPackageReader _reader;
    private readonly WordSemanticProjector _projector;

    public SemanticHtmlWordPackageOperation(
        OpcPackageLimits? packageLimits = null,
        WordSemanticProjectionOptions? projectionOptions = null
    )
    {
        _reader = new OpcPackageReader(packageLimits);
        _projector = new WordSemanticProjector(projectionOptions);
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
        string? temporaryPath = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpcPackageSnapshot package;
            using (
                var stream = new FileStream(
                    paths.Input,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read
                )
            )
            {
                package = _reader.Read(stream, cancellationToken);
            }

            if (!package.IsStructurallyValid)
            {
                throw new WordToolkitOperationException(
                    "INVALID_PACKAGE",
                    "The OPC package failed structural validation"
                );
            }
            if (
                request.ExpectedPackageFingerprint is not null
                && !string.Equals(
                    request.ExpectedPackageFingerprint,
                    package.Fingerprint,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                throw new WordToolkitOperationException(
                    "VERSION_CONFLICT",
                    "The package does not match expected_package_fingerprint"
                );
            }

            var semantic = _projector.Project(package, cancellationToken);
            if (
                !package.Parts.TryGetValue(semantic.MainPartUri, out var mainPart)
                || !WordPackageConformance.IsMainContentTypeCompatibleWithFileName(
                    Path.GetFileName(paths.Input),
                    mainPart.ContentType
                )
            )
            {
                throw new WordToolkitOperationException(
                    "INVALID_WORD_PACKAGE",
                    "The file extension does not match the Word main-part content type"
                );
            }

            var styles = new WordStyleGraphBuilder().Build(
                package,
                semantic,
                cancellationToken
            );
            var reviews = new WordReviewGraphBuilder().Build(
                package,
                semantic,
                cancellationToken
            );
            var equations = new WordEquationGraphBuilder().Build(
                package,
                semantic,
                cancellationToken
            );
            var rendered = SemanticHtmlRenderer.Render(
                semantic,
                styles,
                reviews,
                equations,
                Path.GetFileName(paths.Input),
                request.StoryScope,
                request.Language,
                cancellationToken
            );

            temporaryPath = CreateTemporaryPath(paths.Output);
            using (
                var output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 64 * 1024,
                    FileOptions.WriteThrough
                )
            )
            {
                output.Write(rendered.Bytes);
                output.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Move(temporaryPath, paths.Output, overwrite: false);
            }
            catch (IOException exception) when (File.Exists(paths.Output))
            {
                throw new WordToolkitOperationException(
                    "OUTPUT_EXISTS",
                    "The semantic HTML output already exists",
                    innerException: exception
                );
            }
            temporaryPath = null;

            return new SemanticHtmlWordPackageResult(
                SemanticHtmlWordPackageContract.Contract,
                Path.GetFileName(paths.Input),
                Path.GetFileName(paths.Output),
                package.Fingerprint,
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
                WordOpened: false
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
            throw MapFailure(exception, paths.Input, paths.Output);
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception)
                {
                    // Preserve the operation failure; cleanup is best effort.
                }
            }
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

    private static string CreateTemporaryPath(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath)!;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var candidate = Path.Combine(
                directory,
                $".wordtoolkit-render-{Guid.NewGuid():N}.tmp"
            );
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new IOException("A private temporary output path could not be allocated.");
    }

    private static WordToolkitOperationException MapFailure(
        Exception exception,
        string inputPath,
        string outputPath
    ) =>
        exception switch
        {
            WordSemanticLimitException limit => PackageLimit(limit, inputPath, outputPath),
            WordStyleLimitException limit => PackageLimit(limit, inputPath, outputPath),
            WordReviewLimitException limit => PackageLimit(limit, inputPath, outputPath),
            WordEquationLimitException limit => PackageLimit(limit, inputPath, outputPath),
            OpcPackageLimitException limit => PackageLimit(limit, inputPath, outputPath),
            WordSemanticProjectionException projection => InvalidWordPackage(
                projection,
                inputPath,
                outputPath
            ),
            WordEquationProjectionException projection => InvalidWordPackage(
                projection,
                inputPath,
                outputPath
            ),
            WordStyleProjectionException projection => InvalidWordPackage(
                projection,
                inputPath,
                outputPath
            ),
            WordReviewProjectionException projection => InvalidWordPackage(
                projection,
                inputPath,
                outputPath
            ),
            InvalidDataException invalid => new WordToolkitOperationException(
                "INVALID_PACKAGE",
                "The file is not a readable OPC ZIP package",
                SafeReason(invalid.Message, inputPath, outputPath),
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
                SafeReason(io.Message, inputPath, outputPath),
                retryable: true,
                innerException: io
            ),
            ArgumentException invalid => InvalidInput(
                Bound(invalid.Message, 512) ?? "Invalid render request",
                invalid
            ),
            _ => new WordToolkitOperationException(
                "INTERNAL_ERROR",
                "The semantic HTML render operation failed",
                innerException: exception
            ),
        };

    private static WordToolkitOperationException PackageLimit(
        Exception exception,
        string inputPath,
        string outputPath
    ) =>
        new(
            "PACKAGE_LIMIT",
            "The package exceeds a bounded semantic rendering limit",
            SafeReason(exception.Message, inputPath, outputPath),
            innerException: exception
        );

    private static WordToolkitOperationException InvalidWordPackage(
        Exception exception,
        string inputPath,
        string outputPath
    ) =>
        new(
            "INVALID_WORD_PACKAGE",
            "The package cannot be projected as a Word semantic document",
            SafeReason(exception.Message, inputPath, outputPath),
            innerException: exception
        );

    private static string? SafeReason(
        string? message,
        string inputPath,
        string outputPath
    )
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }
        var safe = message
            .Replace(inputPath, "<input>", StringComparison.OrdinalIgnoreCase)
            .Replace(outputPath, "<output>", StringComparison.OrdinalIgnoreCase);
        return Bound(safe, 512);
    }

    private static string? Bound(string? value, int maxCharacters) =>
        value is null || value.Length <= maxCharacters ? value : value[..maxCharacters];

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
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(styles);
        ArgumentNullException.ThrowIfNull(reviews);
        ArgumentNullException.ThrowIfNull(equations);

        var state = new RenderState(document, styles, reviews, equations);
        var builder = new StringBuilder(Math.Max(4_096, document.NodeCount * 32));
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
            .Append("\">\n");

        var storyRoots = SelectStoryRoots(document, storyScope);
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
                .Append("\">\n");
            if (storyScope == SemanticHtmlStoryScope.AllTextStories)
            {
                builder.Append("<div class=\"wt-story-label\" aria-hidden=\"true\">")
                    .Append(Encode(StoryLabel(root)))
                    .Append("</div>\n");
            }
            RenderNode(root, builder, state, cancellationToken);
            builder.Append("</section>\n");
        }

        builder.Append("</main>\n</body>\n</html>\n");
        var warnings = state.Warnings
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
                state.TableCount++;
                RenderContainer("table", "wt-table", node, builder, state, cancellationToken);
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
            WordEquationGraph equations
        )
        {
            _styles = styles;
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
