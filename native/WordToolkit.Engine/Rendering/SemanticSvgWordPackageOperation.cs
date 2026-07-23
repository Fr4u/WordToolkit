using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Rendering;

public static class SemanticSvgWordPackageContract
{
    public const string OperationName = "render_ooxml_semantic_svg";
    public const string Contract = "wordtoolkit.render_ooxml_semantic_svg/1.0";
    public const string Backend = "wordtoolkit-semantic-svg";
    public const string BackendVersion = "1.0";
    public const string OutputFormat = "svg";
    public const string ArtifactMediaType = "image/svg+xml";
    public const string FidelityClass = "semantic_vector_preview_non_paginated";
    public const string LayoutBasis = "semantic_flow_estimated";
    public const string TextOutputMode = "text";
    public const int DefaultViewportWidthPx = 1024;
    public const int MinimumViewportWidthPx = 320;
    public const int MaximumViewportWidthPx = 4096;
    public const int MaximumCanvasHeightPx = 1_000_000;
    public const int MaximumTextLineCount = 40_000;
    public const int MaximumSvgElementCount = 100_000;
    public const int MaximumLocalPathCharacters = 32_767;
    public const int MaximumLanguageCharacters = 35;
    public const int MaximumArtifactBytes = 256 * 1024 * 1024;
}

public sealed record SemanticSvgWordPackageRequest(
    string LocalPath,
    string OutputPath,
    string ExpectedPackageFingerprint,
    string TargetNodeId,
    SemanticRenderStoryScope StoryScope = SemanticRenderStoryScope.MainDocument,
    string Language = "und",
    int ViewportWidthPx = SemanticSvgWordPackageContract.DefaultViewportWidthPx
);

public static class SemanticSvgWordPackageJson
{
    public static SemanticSvgWordPackageRequest ParseRequest(string json)
    {
        var request = WordToolkitOperationJson.Deserialize<RequestJson>(json);
        return new SemanticSvgWordPackageRequest(
            request.LocalPath,
            request.OutputPath,
            request.ExpectedPackageFingerprint,
            request.TargetNodeId,
            request.StoryScope,
            request.Language,
            request.ViewportWidthPx
        );
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record RequestJson
    {
        public required string LocalPath { get; init; }
        public required string OutputPath { get; init; }
        public required string ExpectedPackageFingerprint { get; init; }
        public required string TargetNodeId { get; init; }
        public SemanticRenderStoryScope StoryScope { get; init; } =
            SemanticRenderStoryScope.MainDocument;
        public string Language { get; init; } = "und";
        public int ViewportWidthPx { get; init; } =
            SemanticSvgWordPackageContract.DefaultViewportWidthPx;
    }
}

public sealed record SemanticSvgWordPackageResult(
    string OperationContract,
    string InputFileName,
    string OutputFileName,
    string PackageFingerprint,
    string ArtifactSha256,
    long ArtifactBytes,
    string ArtifactMediaType,
    string OutputFormat,
    string Backend,
    string BackendVersion,
    string FidelityClass,
    string LayoutBasis,
    string TextOutputMode,
    bool Paginated,
    bool ExactTextMetrics,
    bool PixelEquivalenceClaimed,
    SemanticRenderStoryScope StoryScope,
    bool SelectionApplied,
    string TargetNodeId,
    WordSemanticNodeKind TargetKind,
    string TargetStoryKind,
    string TargetSubtreeFingerprint,
    int ViewportWidthPx,
    int ViewportHeightPx,
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

public sealed class SemanticSvgWordPackageOperation
{
    private readonly SemanticRenderPackageLoader _loader;
    private readonly ISemanticRenderBackend<
        SemanticSvgBackendRequest,
        SemanticSvgRenderArtifact
    > _backend;

    public SemanticSvgWordPackageOperation(
        OpcPackageLimits? packageLimits = null,
        WordSemanticProjectionOptions? projectionOptions = null
    )
    {
        _loader = new SemanticRenderPackageLoader(packageLimits, projectionOptions);
        _backend = SemanticSvgRenderBackend.Instance;
    }

    public SemanticSvgWordPackageResult Execute(
        SemanticSvgWordPackageRequest request,
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
            var selection = SemanticRenderTargetSelection.Resolve(
                context.Document,
                request.TargetNodeId,
                request.StoryScope == SemanticRenderStoryScope.AllTextStories
            );
            EnsureRenderableTarget(selection.Target);

            var rendered = _backend.Render(
                context,
                new SemanticSvgBackendRequest(
                    selection,
                    request.Language,
                    request.ViewportWidthPx
                ),
                cancellationToken
            );
            SemanticRenderArtifactPublisher.PublishCreateNew(
                paths.Output,
                rendered.Bytes,
                "The semantic SVG output already exists",
                cancellationToken
            );

            return new SemanticSvgWordPackageResult(
                SemanticSvgWordPackageContract.Contract,
                Path.GetFileName(paths.Input),
                Path.GetFileName(paths.Output),
                context.Package.Fingerprint,
                Convert.ToHexString(SHA256.HashData(rendered.Bytes)).ToLowerInvariant(),
                rendered.Bytes.LongLength,
                SemanticSvgWordPackageContract.ArtifactMediaType,
                SemanticSvgWordPackageContract.OutputFormat,
                _backend.Descriptor.Backend,
                _backend.Descriptor.BackendVersion,
                _backend.Descriptor.FidelityClass,
                SemanticSvgWordPackageContract.LayoutBasis,
                SemanticSvgWordPackageContract.TextOutputMode,
                _backend.Descriptor.Paginated,
                _backend.Descriptor.ExactTextMetrics,
                PixelEquivalenceClaimed: false,
                request.StoryScope,
                SelectionApplied: true,
                selection.Target.Id.Value,
                selection.Target.Kind,
                selection.StoryKind,
                selection.Target.SubtreeFingerprint,
                request.ViewportWidthPx,
                rendered.ViewportHeightPx,
                RenderedStoryCount: 1,
                rendered.RenderedNodeCount,
                rendered.ParagraphCount,
                rendered.TableCount,
                rendered.EquationCount,
                rendered.DrawingPlaceholderCount,
                rendered.UnsupportedNodeCount,
                rendered.Warnings,
                OutputCreated: true,
                SourceMutated: false,
                ArtifactContainsDocumentContent: true,
                ExternalResourcesLoaded: _backend.Descriptor.LoadsExternalResources,
                ActiveContentExecuted: _backend.Descriptor.ExecutesActiveContent,
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
            throw MapFailure(exception);
        }
    }

    private static (string Input, string Output) ValidateAndResolve(
        SemanticSvgWordPackageRequest request
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
            request.LocalPath.Length > SemanticSvgWordPackageContract.MaximumLocalPathCharacters
            || request.OutputPath.Length
                > SemanticSvgWordPackageContract.MaximumLocalPathCharacters
        )
        {
            throw InvalidInput(
                $"Paths cannot exceed {SemanticSvgWordPackageContract.MaximumLocalPathCharacters} characters"
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
            throw InvalidInput("Semantic SVG rendering accepts DOCX, DOCM, DOTX, or DOTM files");
        }
        if (
            !string.Equals(
                Path.GetExtension(request.OutputPath),
                ".svg",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw InvalidInput("output_path must use the .svg extension");
        }
        if (!IsSha256(request.ExpectedPackageFingerprint))
        {
            throw InvalidInput(
                "expected_package_fingerprint must be exactly 64 hexadecimal characters"
            );
        }
        if (!SemanticNodeId.HasValidSyntax(request.TargetNodeId))
        {
            throw InvalidInput(
                $"target_node_id must use the wdn_ prefix, contain only URL-safe identifier characters, and not exceed {SemanticNodeId.MaximumCharacters} characters"
            );
        }
        if (!Enum.IsDefined(request.StoryScope))
        {
            throw InvalidInput("story_scope is not supported");
        }
        ValidateLanguage(request.Language);
        if (
            request.ViewportWidthPx < SemanticSvgWordPackageContract.MinimumViewportWidthPx
            || request.ViewportWidthPx
                > SemanticSvgWordPackageContract.MaximumViewportWidthPx
        )
        {
            throw InvalidInput(
                $"viewport_width_px must be from {SemanticSvgWordPackageContract.MinimumViewportWidthPx} to {SemanticSvgWordPackageContract.MaximumViewportWidthPx}"
            );
        }

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
                    "The semantic SVG output already exists"
                );
            }
            var outputDirectory = Path.GetDirectoryName(output);
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
            {
                throw new WordToolkitOperationException(
                    "NOT_FOUND",
                    "The semantic SVG output directory does not exist"
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

    private static void EnsureRenderableTarget(WordSemanticNode target)
    {
        if (
            target.Kind
                is WordSemanticNodeKind.Drawing
                    or WordSemanticNodeKind.ExtensionIsland
                    or WordSemanticNodeKind.Bookmark
                    or WordSemanticNodeKind.BookmarkEnd
                    or WordSemanticNodeKind.CommentAnchor
                    or WordSemanticNodeKind.HeaderReference
                    or WordSemanticNodeKind.FooterReference
                    or WordSemanticNodeKind.FootnoteReference
                    or WordSemanticNodeKind.EndnoteReference
                    or WordSemanticNodeKind.Section
        )
        {
            throw new WordToolkitOperationException(
                "TARGET_NOT_RENDERABLE",
                "The requested semantic target has no supported standalone SVG representation"
            );
        }
    }

    private static void ValidateLanguage(string language)
    {
        if (
            string.IsNullOrWhiteSpace(language)
            || language.Length > SemanticSvgWordPackageContract.MaximumLanguageCharacters
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
                "The semantic SVG input or output cannot be accessed with current permissions",
                innerException: denied
            ),
            FileNotFoundException missing => new WordToolkitOperationException(
                "NOT_FOUND",
                "The semantic SVG input or output path no longer exists",
                innerException: missing
            ),
            DirectoryNotFoundException missing => new WordToolkitOperationException(
                "NOT_FOUND",
                "The semantic SVG input or output path no longer exists",
                innerException: missing
            ),
            IOException io => new WordToolkitOperationException(
                "IO_ERROR",
                "The semantic SVG artifact could not be written",
                retryable: true,
                innerException: io
            ),
            ArgumentException invalid => InvalidInput(
                "The semantic SVG render request is invalid",
                invalid
            ),
            _ => new WordToolkitOperationException(
                "INTERNAL_ERROR",
                "The semantic SVG render operation failed",
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

    private static bool IsSha256(string? value) =>
        value?.Length == 64 && value.All(Uri.IsHexDigit);

    private static WordToolkitOperationException InvalidInput(
        string message,
        Exception? innerException = null
    ) => new("INVALID_INPUT", message, innerException: innerException);
}

internal sealed record SemanticSvgBackendRequest(
    SemanticRenderTargetSelection Selection,
    string Language,
    int ViewportWidthPx
);

internal sealed record SemanticSvgRenderArtifact(
    byte[] Bytes,
    int ViewportHeightPx,
    int RenderedNodeCount,
    int ParagraphCount,
    int TableCount,
    int EquationCount,
    int DrawingPlaceholderCount,
    int UnsupportedNodeCount,
    IReadOnlyList<string> Warnings
);

internal sealed class SemanticSvgRenderBackend
    : ISemanticRenderBackend<SemanticSvgBackendRequest, SemanticSvgRenderArtifact>
{
    public static SemanticSvgRenderBackend Instance { get; } = new();

    private SemanticSvgRenderBackend() { }

    public SemanticRenderBackendDescriptor Descriptor { get; } = new(
        SemanticSvgWordPackageContract.Backend,
        SemanticSvgWordPackageContract.BackendVersion,
        SemanticSvgWordPackageContract.OutputFormat,
        SemanticSvgWordPackageContract.ArtifactMediaType,
        SemanticSvgWordPackageContract.FidelityClass,
        Paginated: false,
        ExactTextMetrics: false,
        LoadsExternalResources: false,
        ExecutesActiveContent: false
    );

    public SemanticSvgRenderArtifact Render(
        SemanticRenderPackageContext context,
        SemanticSvgBackendRequest request,
        CancellationToken cancellationToken
    ) => SemanticSvgRenderer.Render(context, request, cancellationToken);
}

internal static class SemanticSvgRenderer
{
    private const double Margin = 24;
    private const double BaseFontSize = 16;
    private const double BaseLineHeight = 22;
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    public static SemanticSvgRenderArtifact Render(
        SemanticRenderPackageContext context,
        SemanticSvgBackendRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var nodes = request.Selection.Target.DescendantsAndSelf().ToArray();
        var warnings = new HashSet<string>(StringComparer.Ordinal)
        {
            "FONT_RESOLUTION_NOT_PERFORMED",
            "SEMANTIC_VECTOR_PREVIEW_NON_PAGINATED",
            "TEXT_METRICS_ESTIMATED",
            "VISUAL_FORMATTING_APPROXIMATED",
        };
        if (context.Document.Warnings.Count != 0)
        {
            warnings.Add("SEMANTIC_PROJECTION_WARNINGS");
        }
        if (context.Styles.Issues.Count != 0)
        {
            warnings.Add("STYLE_GRAPH_WARNINGS");
        }
        if (context.Equations.Issues.Count != 0)
        {
            warnings.Add("EQUATION_GRAPH_WARNINGS");
        }
        if (context.Reviews.Issues.Count != 0)
        {
            warnings.Add("REVIEW_GRAPH_WARNINGS");
        }
        if (nodes.Any(node => node.Kind == WordSemanticNodeKind.Hyperlink))
        {
            warnings.Add("HYPERLINKS_RENDERED_INERT");
        }
        if (nodes.Any(node => node.Kind == WordSemanticNodeKind.Field))
        {
            warnings.Add("FIELD_INSTRUCTIONS_SUPPRESSED");
        }
        if (nodes.Any(node => node.Kind == WordSemanticNodeKind.Revision))
        {
            warnings.Add("TRACKED_REVISIONS_ANNOTATED");
        }
        if (nodes.Any(node => node.Kind == WordSemanticNodeKind.Equation))
        {
            warnings.Add("EQUATIONS_RENDERED_AS_LINEAR_TEXT");
        }
        if (nodes.Any(node => node.Kind == WordSemanticNodeKind.Drawing))
        {
            warnings.Add("DRAWINGS_RENDERED_AS_PLACEHOLDERS");
        }
        if (
            nodes.Any(node => node.Kind == WordSemanticNodeKind.AlternateContent)
        )
        {
            warnings.Add("ALTERNATE_CONTENT_APPROXIMATED");
        }
        if (nodes.Any(node => node.Kind == WordSemanticNodeKind.ExtensionIsland))
        {
            warnings.Add("EXTENSION_CONTENT_RENDERED_AS_PLACEHOLDER");
        }

        var equationMap = context.Equations.Equations
            .Where(equation => equation.SemanticNodeId is not null)
            .GroupBy(equation => equation.SemanticNodeId!.Value)
            .ToDictionary(group => group.Key, group => group.First());
        var revisionMap = context.Reviews.Revisions
            .Where(revision => revision.SemanticNodeId is not null)
            .GroupBy(revision => revision.SemanticNodeId!.Value)
            .ToDictionary(group => group.Key, group => group.First());
        var content = new XElement(
            Svg + "g",
            new XAttribute("id", "wt-content"),
            new XAttribute("role", "group"),
            new XAttribute("aria-label", "Selected Word semantic object"),
            new XAttribute("data-node-id", request.Selection.Target.Id.Value),
            new XAttribute("data-kind", SnakeCase(request.Selection.Target.Kind))
        );
        var state = new LayoutState(
            request.ViewportWidthPx,
            content,
            context.Document,
            equationMap,
            revisionMap,
            warnings,
            cancellationToken
        );
        state.RenderRoot(request.Selection.Target);
        var height = state.CanvasHeightPx;
        if (height > SemanticSvgWordPackageContract.MaximumCanvasHeightPx)
        {
            throw new WordSemanticLimitException(
                $"Semantic SVG canvas exceeds {SemanticSvgWordPackageContract.MaximumCanvasHeightPx} pixels."
            );
        }

        var root = new XElement(
            Svg + "svg",
            new XAttribute("version", "1.1"),
            new XAttribute(XNamespace.Xml + "lang", request.Language),
            new XAttribute("role", "img"),
            new XAttribute("aria-labelledby", "wt-title wt-desc"),
            new XAttribute(
                "viewBox",
                $"0 0 {request.ViewportWidthPx.ToString(CultureInfo.InvariantCulture)} {height.ToString(CultureInfo.InvariantCulture)}"
            ),
            new XAttribute("width", request.ViewportWidthPx),
            new XAttribute("height", height),
            new XAttribute("data-wordtoolkit-backend", SemanticSvgWordPackageContract.Backend),
            new XAttribute("data-backend-version", SemanticSvgWordPackageContract.BackendVersion),
            new XAttribute("data-fidelity-class", SemanticSvgWordPackageContract.FidelityClass),
            new XAttribute("data-package-fingerprint", context.Package.Fingerprint),
            new XElement(
                Svg + "title",
                new XAttribute("id", "wt-title"),
                $"Semantic SVG preview: {SnakeCase(request.Selection.Target.Kind)}"
            ),
            new XElement(
                Svg + "desc",
                new XAttribute("id", "wt-desc"),
                "Fingerprint-bound semantic object preview with estimated non-paginated geometry. Pixel equivalence with Microsoft Word is not claimed."
            ),
            new XElement(
                Svg + "rect",
                new XAttribute("x", "0"),
                new XAttribute("y", "0"),
                new XAttribute("width", "100%"),
                new XAttribute("height", "100%"),
                new XAttribute("fill", "#ffffff")
            ),
            content
        );
        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        byte[] bytes;
        using (var stream = new MemoryStream())
        {
            using (
                var writer = XmlWriter.Create(
                    stream,
                    new XmlWriterSettings
                    {
                        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                        Indent = false,
                        NewLineChars = "\n",
                        NewLineHandling = NewLineHandling.None,
                        OmitXmlDeclaration = false,
                        CloseOutput = false,
                    }
                )
            )
            {
                document.Save(writer);
            }
            bytes = stream.ToArray();
        }
        if (bytes.Length > SemanticSvgWordPackageContract.MaximumArtifactBytes)
        {
            throw new WordSemanticLimitException(
                $"Semantic SVG artifact exceeds {SemanticSvgWordPackageContract.MaximumArtifactBytes} bytes."
            );
        }

        var unsupported = nodes.Count(node =>
            node.Kind
                is WordSemanticNodeKind.Drawing
                    or WordSemanticNodeKind.AlternateContent
                    or WordSemanticNodeKind.ExtensionIsland
        );
        return new SemanticSvgRenderArtifact(
            bytes,
            height,
            nodes.Length,
            nodes.Count(node => node.Kind == WordSemanticNodeKind.Paragraph),
            nodes.Count(node => node.Kind == WordSemanticNodeKind.Table),
            nodes.Count(node => node.Kind == WordSemanticNodeKind.Equation),
            nodes.Count(node => node.Kind == WordSemanticNodeKind.Drawing),
            unsupported,
            warnings.Order(StringComparer.Ordinal).ToArray()
        );
    }

    private sealed class LayoutState
    {
        private readonly int _viewportWidth;
        private readonly XElement _content;
        private readonly WordSemanticDocument _document;
        private readonly IReadOnlyDictionary<SemanticNodeId, WordEquationDefinition> _equations;
        private readonly IReadOnlyDictionary<SemanticNodeId, WordRevisionDefinition> _revisions;
        private readonly HashSet<string> _warnings;
        private readonly CancellationToken _cancellationToken;
        private double _y = Margin;
        private int _textLineCount;
        private int _svgElementCount;

        public LayoutState(
            int viewportWidth,
            XElement content,
            WordSemanticDocument document,
            IReadOnlyDictionary<SemanticNodeId, WordEquationDefinition> equations,
            IReadOnlyDictionary<SemanticNodeId, WordRevisionDefinition> revisions,
            HashSet<string> warnings,
            CancellationToken cancellationToken
        )
        {
            _viewportWidth = viewportWidth;
            _content = content;
            _document = document;
            _equations = equations;
            _revisions = revisions;
            _warnings = warnings;
            _cancellationToken = cancellationToken;
        }

        public int CanvasHeightPx
        {
            get
            {
                var height = Math.Ceiling(_y + Margin);
                if (
                    !double.IsFinite(height)
                    || height > SemanticSvgWordPackageContract.MaximumCanvasHeightPx
                )
                {
                    throw new WordSemanticLimitException(
                        $"Semantic SVG canvas exceeds {SemanticSvgWordPackageContract.MaximumCanvasHeightPx} pixels."
                    );
                }
                return Math.Max(1, (int)height);
            }
        }

        private double ContentWidth => Math.Max(1, _viewportWidth - (Margin * 2));

        public void RenderRoot(WordSemanticNode target)
        {
            RenderNode(target, _content, Margin, ContentWidth);
            if (!_content.Elements().Any())
            {
                _warnings.Add("SELECTED_NODE_HAS_NO_VISIBLE_OUTPUT");
                DrawTextBlock(
                    target,
                    "[No visible semantic text]",
                    _content,
                    Margin,
                    ContentWidth,
                    "note",
                    BaseFontSize
                );
            }
        }

        private void RenderNode(
            WordSemanticNode node,
            XElement parent,
            double x,
            double width
        )
        {
            _cancellationToken.ThrowIfCancellationRequested();
            switch (node.Kind)
            {
                case WordSemanticNodeKind.Table:
                case WordSemanticNodeKind.TableRow:
                case WordSemanticNodeKind.TableCell:
                    DrawTable(node, parent, x, width);
                    break;
                case WordSemanticNodeKind.Paragraph:
                    if (HasStructuredInlineDescendant(node))
                    {
                        DrawStructuredContainer(node, parent, x, width, "paragraph");
                    }
                    else
                    {
                        DrawTextBlock(
                            node,
                            ExtractText(node),
                            parent,
                            x,
                            width,
                            "paragraph",
                            BaseFontSize
                        );
                    }
                    break;
                case WordSemanticNodeKind.Equation:
                    DrawTextBlock(
                        node,
                        EquationText(node),
                        parent,
                        x,
                        width,
                        "math",
                        BaseFontSize + 2
                    );
                    break;
                case WordSemanticNodeKind.Text:
                case WordSemanticNodeKind.Run:
                case WordSemanticNodeKind.Tab:
                case WordSemanticNodeKind.Break:
                case WordSemanticNodeKind.Hyperlink:
                case WordSemanticNodeKind.Field:
                case WordSemanticNodeKind.EquationComponent:
                    if (HasStructuredInlineDescendant(node))
                    {
                        DrawStructuredContainer(node, parent, x, width, "text");
                    }
                    else
                    {
                        DrawTextBlock(
                            node,
                            ExtractText(node),
                            parent,
                            x,
                            width,
                            "text",
                            BaseFontSize
                        );
                    }
                    break;
                case WordSemanticNodeKind.Drawing:
                    DrawTextBlock(
                        node,
                        "[Drawing not rendered]",
                        parent,
                        x,
                        width,
                        "img",
                        BaseFontSize
                    );
                    break;
                case WordSemanticNodeKind.ExtensionIsland:
                    DrawTextBlock(
                        node,
                        "[Unsupported extension content]",
                        parent,
                        x,
                        width,
                        "note",
                        BaseFontSize
                    );
                    break;
                case WordSemanticNodeKind.Revision:
                    DrawRevision(node, parent, x, width);
                    break;
                default:
                    foreach (var child in node.Children.OrderBy(child => child.SourceOrder))
                    {
                        RenderNode(child, parent, x, width);
                    }
                    break;
            }
        }

        private void DrawTable(
            WordSemanticNode target,
            XElement parent,
            double x,
            double width
        )
        {
            _warnings.Add("TABLE_GEOMETRY_APPROXIMATED");
            if (
                target.DescendantsAndSelf().Skip(1)
                    .Any(node => node.Kind == WordSemanticNodeKind.Table)
            )
            {
                _warnings.Add("NESTED_TABLE_GEOMETRY_FLATTENED");
            }

            var rows = target.Kind switch
            {
                WordSemanticNodeKind.TableRow => new[] { target },
                WordSemanticNodeKind.TableCell => Array.Empty<WordSemanticNode>(),
                _ => CollectRows(target).ToArray(),
            };
            IReadOnlyList<IReadOnlyList<WordSemanticNode>> cellsByRow =
                target.Kind == WordSemanticNodeKind.TableCell
                    ? new IReadOnlyList<WordSemanticNode>[] { new[] { target } }
                    : rows.Select(row => (IReadOnlyList<WordSemanticNode>)CollectCells(row).ToArray())
                        .ToArray();
            if (cellsByRow.Count == 0 || cellsByRow.All(cells => cells.Count == 0))
            {
                DrawTextBlock(
                    target,
                    ExtractText(target),
                    parent,
                    x,
                    width,
                    "table",
                    BaseFontSize
                );
                return;
            }

            var columnCount = Math.Max(1, cellsByRow.Max(cells => cells.Count));
            var columnWidth = width / columnCount;
            var tableGroup = new XElement(
                Svg + "g",
                new XAttribute("role", "table"),
                new XAttribute("data-node-id", target.Id.Value),
                new XAttribute("data-kind", SnakeCase(target.Kind))
            );
            AccountSvgElements(1);
            parent.Add(tableGroup);
            for (var rowIndex = 0; rowIndex < cellsByRow.Count; rowIndex++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var cells = cellsByRow[rowIndex];
                var wrapped = cells.Select(cell =>
                    WrapText(ExtractText(cell), Math.Max(1, columnWidth - 16), BaseFontSize)
                ).ToArray();
                var maximumLines = Math.Max(1, wrapped.Select(lines => lines.Count).DefaultIfEmpty(1).Max());
                var rowHeight = Math.Max(36, 16 + (maximumLines * BaseLineHeight));
                var rowNode = target.Kind == WordSemanticNodeKind.TableCell
                    ? target
                    : rows[rowIndex];
                var rowGroup = new XElement(
                    Svg + "g",
                    new XAttribute("role", "row"),
                    new XAttribute("data-node-id", rowNode.Id.Value)
                );
                AnnotateFlattenedRevisionContext(rowGroup, rowNode);
                AccountSvgElements(1);
                tableGroup.Add(rowGroup);
                for (var columnIndex = 0; columnIndex < cells.Count; columnIndex++)
                {
                    var cell = cells[columnIndex];
                    var cellX = x + (columnIndex * columnWidth);
                    var cellGroup = new XElement(
                        Svg + "g",
                        new XAttribute("role", "cell"),
                        new XAttribute("data-node-id", cell.Id.Value),
                        new XElement(
                            Svg + "rect",
                            new XAttribute("x", Number(cellX)),
                            new XAttribute("y", Number(_y)),
                            new XAttribute("width", Number(columnWidth)),
                            new XAttribute("height", Number(rowHeight)),
                            new XAttribute("fill", "none"),
                            new XAttribute("stroke", "#555555"),
                            new XAttribute("stroke-width", "1")
                        )
                    );
                    AnnotateFlattenedRevisionContext(cellGroup, cell);
                    AccountSvgElements(2);
                    rowGroup.Add(cellGroup);
                    AddTextLines(
                        cell,
                        wrapped[columnIndex],
                        cellGroup,
                        cellX + 8,
                        _y + 8 + BaseFontSize,
                        BaseFontSize,
                        "#111111"
                    );
                }
                AdvanceY(rowHeight);
            }
            AdvanceY(12);
        }

        private void DrawRevision(
            WordSemanticNode node,
            XElement parent,
            double x,
            double width
        )
        {
            var kind = _revisions.TryGetValue(node.Id, out var revision)
                ? SnakeCase(revision.Kind)
                : "unknown";
            if (revision is null)
            {
                _warnings.Add("REVISION_MAPPING_MISSING");
            }
            var group = new XElement(
                Svg + "g",
                new XAttribute("role", "group"),
                new XAttribute("aria-label", $"Tracked revision: {kind}"),
                new XAttribute("data-node-id", node.Id.Value),
                new XAttribute("data-kind", SnakeCase(node.Kind)),
                new XAttribute("data-revision-kind", kind)
            );
            AccountSvgElements(1);
            parent.Add(group);
            foreach (var child in node.Children.OrderBy(child => child.SourceOrder))
            {
                RenderNode(child, group, x, width);
            }
        }

        private void AnnotateFlattenedRevisionContext(
            XElement element,
            WordSemanticNode node
        )
        {
            var revisionNodes = node.DescendantsAndSelf()
                .Where(candidate => candidate.Kind == WordSemanticNodeKind.Revision)
                .ToList();
            WordSemanticNode? current = node;
            while (current?.ParentId is SemanticNodeId parentId)
            {
                if (!_document.TryGetNode(parentId, out current) || current is null)
                {
                    break;
                }
                if (current.Kind == WordSemanticNodeKind.Revision)
                {
                    revisionNodes.Add(current);
                }
            }
            var kinds = revisionNodes
                .Select(RevisionKind)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (kinds.Length == 0)
            {
                return;
            }
            element.Add(new XAttribute("data-revision-kinds", string.Join(" ", kinds)));
            _warnings.Add("TABLE_REVISION_SPANS_FLATTENED");
        }

        private string RevisionKind(WordSemanticNode node)
        {
            if (_revisions.TryGetValue(node.Id, out var revision))
            {
                return SnakeCase(revision.Kind);
            }
            _warnings.Add("REVISION_MAPPING_MISSING");
            return "unknown";
        }

        private void DrawStructuredContainer(
            WordSemanticNode node,
            XElement parent,
            double x,
            double width,
            string role
        )
        {
            var group = new XElement(
                Svg + "g",
                new XAttribute("role", role),
                new XAttribute("data-node-id", node.Id.Value),
                new XAttribute("data-kind", SnakeCase(node.Kind))
            );
            AccountSvgElements(1);
            parent.Add(group);
            foreach (var child in node.Children.OrderBy(child => child.SourceOrder))
            {
                RenderNode(child, group, x, width);
            }
        }

        private static bool HasStructuredInlineDescendant(WordSemanticNode node) =>
            node.DescendantsAndSelf().Skip(1).Any(descendant =>
                descendant.Kind
                    is WordSemanticNodeKind.Revision
                        or WordSemanticNodeKind.Equation
                        or WordSemanticNodeKind.Drawing
                        or WordSemanticNodeKind.ExtensionIsland
            );

        private void DrawTextBlock(
            WordSemanticNode node,
            string text,
            XElement parent,
            double x,
            double width,
            string role,
            double fontSize
        )
        {
            var lines = WrapText(text, width, fontSize);
            if (lines.Count == 0)
            {
                return;
            }
            var group = new XElement(
                Svg + "g",
                new XAttribute("role", role),
                new XAttribute("data-node-id", node.Id.Value),
                new XAttribute("data-kind", SnakeCase(node.Kind))
            );
            AccountSvgElements(1);
            parent.Add(group);
            AddTextLines(
                node,
                lines,
                group,
                x,
                _y + fontSize,
                fontSize,
                "#111111"
            );
            AdvanceY(Math.Max(BaseLineHeight, lines.Count * (fontSize + 6)) + 8);
        }

        private void AddTextLines(
            WordSemanticNode node,
            IReadOnlyList<string> lines,
            XElement parent,
            double x,
            double firstBaseline,
            double fontSize,
            string fill
        )
        {
            for (var index = 0; index < lines.Count; index++)
            {
                AccountSvgElements(1);
                parent.Add(
                    new XElement(
                        Svg + "text",
                        new XAttribute("x", Number(x)),
                        new XAttribute(
                            "y",
                            Number(firstBaseline + (index * (fontSize + 6)))
                        ),
                        new XAttribute("font-family", "sans-serif"),
                        new XAttribute("font-size", Number(fontSize)),
                        new XAttribute("fill", fill),
                        new XAttribute(XNamespace.Xml + "space", "preserve"),
                        new XAttribute("data-node-id", node.Id.Value),
                        lines[index]
                    )
                );
            }
        }

        private string ExtractText(WordSemanticNode root)
        {
            var builder = new StringBuilder();
            AppendText(root, builder);
            return builder.ToString();
        }

        private void AppendText(WordSemanticNode node, StringBuilder builder)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            switch (node.Kind)
            {
                case WordSemanticNodeKind.Text:
                    AppendXmlSafe(builder, node.Text ?? string.Empty);
                    return;
                case WordSemanticNodeKind.Tab:
                    builder.Append("    ");
                    return;
                case WordSemanticNodeKind.Break:
                    builder.Append('\n');
                    return;
                case WordSemanticNodeKind.Equation:
                    AppendXmlSafe(builder, EquationText(node));
                    return;
                case WordSemanticNodeKind.Drawing:
                    builder.Append("[Drawing not rendered]");
                    return;
                case WordSemanticNodeKind.ExtensionIsland:
                    builder.Append("[Unsupported extension content]");
                    return;
            }
            foreach (var child in node.Children.OrderBy(child => child.SourceOrder))
            {
                AppendText(child, builder);
            }
        }

        private string EquationText(WordSemanticNode node)
        {
            if (_equations.TryGetValue(node.Id, out var equation))
            {
                if (!equation.IsCanonical || equation.UnsupportedNodeCount != 0)
                {
                    _warnings.Add("EQUATION_CONTENT_APPROXIMATED");
                }
                return equation.Text;
            }
            _warnings.Add("EQUATION_MAPPING_MISSING");
            return "[Equation]";
        }

        private void AppendXmlSafe(StringBuilder builder, string value)
        {
            foreach (var rune in value.EnumerateRunes())
            {
                if (IsXmlScalar(rune.Value))
                {
                    builder.Append(rune.ToString());
                }
                else
                {
                    builder.Append('\uFFFD');
                    _warnings.Add("INVALID_XML_CHARACTERS_REPLACED");
                }
            }
        }

        private static bool IsXmlScalar(int value) =>
            value is 0x9 or 0xA or 0xD
            || value is >= 0x20 and <= 0xD7FF
            || value is >= 0xE000 and <= 0xFFFD
            || value is >= 0x10000 and <= 0x10FFFF;

        private IReadOnlyList<string> WrapText(
            string value,
            double width,
            double fontSize
        )
        {
            if (string.IsNullOrEmpty(value))
            {
                return Array.Empty<string>();
            }
            var maximumUnits = Math.Max(1, width / fontSize);
            var lines = new List<string>();
            var line = new StringBuilder();
            var units = 0d;
            foreach (var rune in value.EnumerateRunes())
            {
                if (rune.Value == '\r')
                {
                    continue;
                }
                if (rune.Value == '\n')
                {
                    AddWrappedLine(lines, line);
                    line.Clear();
                    units = 0;
                    continue;
                }
                var runeUnits = EstimatedUnits(rune);
                if (line.Length != 0 && units + runeUnits > maximumUnits)
                {
                    AddWrappedLine(lines, line);
                    line.Clear();
                    units = 0;
                }
                line.Append(rune.ToString());
                units += runeUnits;
            }
            if (line.Length != 0 || lines.Count == 0)
            {
                AddWrappedLine(lines, line);
            }
            return lines;
        }

        private void AddWrappedLine(List<string> lines, StringBuilder line)
        {
            _textLineCount++;
            if (_textLineCount > SemanticSvgWordPackageContract.MaximumTextLineCount)
            {
                throw new WordSemanticLimitException(
                    $"Semantic SVG text exceeds the {SemanticSvgWordPackageContract.MaximumTextLineCount}-line layout budget."
                );
            }
            lines.Add(line.ToString());
        }

        private void AccountSvgElements(int count)
        {
            _svgElementCount = checked(_svgElementCount + count);
            if (_svgElementCount > SemanticSvgWordPackageContract.MaximumSvgElementCount)
            {
                throw new WordSemanticLimitException(
                    $"Semantic SVG exceeds the {SemanticSvgWordPackageContract.MaximumSvgElementCount}-element layout budget."
                );
            }
        }

        private void AdvanceY(double amount)
        {
            _y += amount;
            if (
                !double.IsFinite(_y)
                || _y + Margin > SemanticSvgWordPackageContract.MaximumCanvasHeightPx
            )
            {
                throw new WordSemanticLimitException(
                    $"Semantic SVG canvas exceeds {SemanticSvgWordPackageContract.MaximumCanvasHeightPx} pixels."
                );
            }
        }

        private static double EstimatedUnits(Rune rune)
        {
            if (Rune.IsWhiteSpace(rune))
            {
                return 0.45;
            }
            return rune.Value >= 0x2E80 ? 1.0 : 0.62;
        }

        private static IEnumerable<WordSemanticNode> CollectRows(
            WordSemanticNode node
        )
        {
            foreach (var child in node.Children.OrderBy(child => child.SourceOrder))
            {
                if (child.Kind == WordSemanticNodeKind.TableRow)
                {
                    yield return child;
                    continue;
                }
                if (child.Kind == WordSemanticNodeKind.Table)
                {
                    continue;
                }
                foreach (var nested in CollectRows(child))
                {
                    yield return nested;
                }
            }
        }

        private static IEnumerable<WordSemanticNode> CollectCells(
            WordSemanticNode node
        )
        {
            foreach (var child in node.Children.OrderBy(child => child.SourceOrder))
            {
                if (child.Kind == WordSemanticNodeKind.TableCell)
                {
                    yield return child;
                    continue;
                }
                if (
                    child.Kind
                        is WordSemanticNodeKind.Table
                            or WordSemanticNodeKind.TableRow
                )
                {
                    continue;
                }
                foreach (var nested in CollectCells(child))
                {
                    yield return nested;
                }
            }
        }
    }

    private static string Number(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string SnakeCase<T>(T value)
        where T : struct, Enum =>
        string.Concat(
            value.ToString().Select((character, index) =>
                char.IsUpper(character) && index != 0
                    ? "_" + char.ToLowerInvariant(character)
                    : char.ToLowerInvariant(character).ToString()
            )
        );
}
