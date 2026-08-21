using System.ComponentModel;
using System.Runtime.InteropServices;
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
    WordPresentationSnapshot Snapshot,
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
    private readonly WordPresentationSnapshotBuilder _snapshotBuilder;

    public SemanticRenderPackageLoader(
        OpcPackageLimits? packageLimits = null,
        WordSemanticProjectionOptions? projectionOptions = null
    )
    {
        _reader = new OpcPackageReader(packageLimits);
        _snapshotBuilder = new WordPresentationSnapshotBuilder(projectionOptions);
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

        var snapshot = _snapshotBuilder.Build(package, cancellationToken);
        if (
            !package.Parts.TryGetValue(snapshot.Document.MainPartUri, out var mainPart)
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
            snapshot,
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
                // File.Move(overwrite: false) is not a no-clobber primitive on
                // every platform: two concurrent Unix callers can both pass
                // the managed existence check before rename(2), allowing the
                // later rename to replace the first artifact. A hard-link
                // publication is an atomic create-new directory operation on
                // the same filesystem. The temporary file is already closed,
                // flushed and never written again before its private link is
                // removed.
                PublishNoClobberHardLink(temporaryPath, outputPath);
            }
            catch (IOException exception) when (File.Exists(outputPath))
            {
                throw new WordToolkitOperationException(
                    "OUTPUT_EXISTS",
                    outputExistsMessage,
                    innerException: exception
                );
            }
            try
            {
                File.Delete(temporaryPath);
                temporaryPath = null;
            }
            catch (IOException)
            {
                // The public artifact is already complete. The finally block
                // retries cleanup without turning a successful publication
                // into an ambiguous operation failure.
            }
            catch (UnauthorizedAccessException)
            {
                // Same retry policy as the I/O case above.
            }
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

    internal static void PublishNoClobberHardLink(
        string temporaryPath,
        string outputPath
    )
    {
        int error;
        if (OperatingSystem.IsWindows())
        {
            if (CreateHardLinkWindows(outputPath, temporaryPath, IntPtr.Zero))
            {
                return;
            }
            error = Marshal.GetLastWin32Error();
        }
        else
        {
            if (CreateHardLinkUnix(temporaryPath, outputPath) == 0)
            {
                return;
            }
            error = Marshal.GetLastWin32Error();
        }
        throw new IOException(
            "Atomic create-new artifact publication failed.",
            new Win32Exception(error)
        );
    }

    internal static bool IsAlreadyExistsError(IOException exception)
    {
        var code = (exception.InnerException as Win32Exception)?.NativeErrorCode;
        return code is 17 or 80 or 183;
    }

    [DllImport(
        "kernel32.dll",
        EntryPoint = "CreateHardLinkW",
        CharSet = CharSet.Unicode,
        SetLastError = true
    )]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkWindows(
        string newFileName,
        string existingFileName,
        IntPtr securityAttributes
    );

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int CreateHardLinkUnix(
        string existingPath,
        string newPath
    );
}
