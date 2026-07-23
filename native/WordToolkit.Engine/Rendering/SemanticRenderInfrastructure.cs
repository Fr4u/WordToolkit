using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Rendering;

public enum SemanticRenderStoryScope
{
    MainDocument,
    AllTextStories,
}

internal sealed record SemanticRenderBackendDescriptor(
    string Backend,
    string BackendVersion,
    string OutputFormat,
    string MediaType,
    string FidelityClass,
    bool Paginated,
    bool ExactTextMetrics,
    bool LoadsExternalResources,
    bool ExecutesActiveContent
);

internal sealed record SemanticRenderPackageContext(
    OpcPackageSnapshot Package,
    WordSemanticDocument Document,
    WordStyleGraph Styles,
    WordReviewGraph Reviews,
    WordEquationGraph Equations,
    string InputFileName
);

internal interface ISemanticRenderBackend<in TRequest, out TArtifact>
{
    SemanticRenderBackendDescriptor Descriptor { get; }

    TArtifact Render(
        SemanticRenderPackageContext context,
        TRequest request,
        CancellationToken cancellationToken
    );
}

internal static class SemanticRenderPathPolicy
{
    public static bool IsAllowedLocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var candidate = path.TrimStart();
        return !candidate.StartsWith(@"\\", StringComparison.Ordinal)
            && !candidate.StartsWith("//", StringComparison.Ordinal)
            && !candidate.StartsWith(@"\??\", StringComparison.Ordinal)
            && !candidate.StartsWith("/??/", StringComparison.Ordinal);
    }
}

internal sealed class SemanticRenderPackageLoader
{
    private readonly OpcPackageReader _reader;
    private readonly WordSemanticProjector _projector;

    public SemanticRenderPackageLoader(
        OpcPackageLimits? packageLimits = null,
        WordSemanticProjectionOptions? projectionOptions = null
    )
    {
        _reader = new OpcPackageReader(packageLimits);
        _projector = new WordSemanticProjector(projectionOptions);
    }

    public SemanticRenderPackageContext Load(
        string inputPath,
        string? expectedPackageFingerprint,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        OpcPackageSnapshot package;
        using (
            var stream = new FileStream(
                inputPath,
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
            expectedPackageFingerprint is not null
            && !string.Equals(
                expectedPackageFingerprint,
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
                Path.GetFileName(inputPath),
                mainPart.ContentType
            )
        )
        {
            throw new WordToolkitOperationException(
                "INVALID_WORD_PACKAGE",
                "The file extension does not match the Word main-part content type"
            );
        }

        return new SemanticRenderPackageContext(
            package,
            semantic,
            new WordStyleGraphBuilder().Build(package, semantic, cancellationToken),
            new WordReviewGraphBuilder().Build(package, semantic, cancellationToken),
            new WordEquationGraphBuilder().Build(package, semantic, cancellationToken),
            Path.GetFileName(inputPath)
        );
    }
}

internal sealed record SemanticRenderTargetSelection(
    WordSemanticNode Target,
    WordSemanticNode StoryRoot,
    string StoryKind
)
{
    public static SemanticRenderTargetSelection Resolve(
        WordSemanticDocument document,
        string targetNodeId,
        bool includeAllTextStories
    )
    {
        if (
            !document.TryGetNode(new SemanticNodeId(targetNodeId), out var target)
            || target is null
        )
        {
            throw new WordToolkitOperationException(
                "TARGET_NOT_FOUND",
                "The requested semantic target does not exist in the fingerprint-bound package"
            );
        }
        if (target.Kind == WordSemanticNodeKind.Document)
        {
            throw new WordToolkitOperationException(
                "TARGET_NOT_RENDERABLE",
                "The semantic document root is not a renderable story fragment"
            );
        }

        var storyRoot = FindStoryRoot(document, target);
        if (storyRoot is null)
        {
            throw new WordToolkitOperationException(
                "TARGET_NOT_RENDERABLE",
                "The requested semantic target does not belong to a renderable text story"
            );
        }
        if (
            !includeAllTextStories
            && (
                storyRoot.Kind != WordSemanticNodeKind.Body
                || !string.Equals(
                    storyRoot.SourcePartUri,
                    document.MainPartUri,
                    StringComparison.Ordinal
                )
            )
        )
        {
            throw new WordToolkitOperationException(
                "TARGET_OUT_OF_SCOPE",
                "The requested semantic target is outside story_scope"
            );
        }

        return new SemanticRenderTargetSelection(
            target,
            storyRoot,
            ResolveStoryKind(document, storyRoot)
        );
    }

    private static WordSemanticNode? FindStoryRoot(
        WordSemanticDocument document,
        WordSemanticNode node
    )
    {
        WordSemanticNode? current = node;
        while (current is not null)
        {
            if (IsStoryRoot(current.Kind))
            {
                return current;
            }
            if (
                current.ParentId is null
                || !document.TryGetNode(current.ParentId.Value, out current)
            )
            {
                break;
            }
        }
        return null;
    }

    private static string ResolveStoryKind(
        WordSemanticDocument document,
        WordSemanticNode storyRoot
    ) =>
        storyRoot.Properties.TryGetValue("story_kind", out var storyKind)
            ? storyKind
            : storyRoot.Kind == WordSemanticNodeKind.Body
                && string.Equals(
                    storyRoot.SourcePartUri,
                    document.MainPartUri,
                    StringComparison.Ordinal
                )
            ? "main_document"
            : SnakeCase(storyRoot.Kind);

    private static bool IsStoryRoot(WordSemanticNodeKind kind) =>
        kind is WordSemanticNodeKind.Body
            or WordSemanticNodeKind.Header
            or WordSemanticNodeKind.Footer
            or WordSemanticNodeKind.Footnotes
            or WordSemanticNodeKind.Endnotes
            or WordSemanticNodeKind.Comments
            or WordSemanticNodeKind.GlossaryDocument;

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

internal static class SemanticRenderArtifactPublisher
{
    public static void PublishCreateNew(
        string outputPath,
        ReadOnlySpan<byte> bytes,
        string outputExistsMessage,
        CancellationToken cancellationToken
    )
    {
        string? temporaryPath = null;
        try
        {
            temporaryPath = CreateTemporaryPath(outputPath);
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
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Move(temporaryPath, outputPath, overwrite: false);
            }
            catch (IOException exception) when (File.Exists(outputPath))
            {
                throw new WordToolkitOperationException(
                    "OUTPUT_EXISTS",
                    outputExistsMessage,
                    innerException: exception
                );
            }
            temporaryPath = null;
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
}
