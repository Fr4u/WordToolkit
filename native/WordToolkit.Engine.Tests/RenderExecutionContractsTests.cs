using System.Collections.Immutable;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Rendering;

namespace WordToolkit.Engine.Tests;

public sealed class RenderExecutionContractsTests
{
    [Fact]
    public void ResolvesExactIntentAndRequiresExplicitApproximation()
    {
        var backend = Profile(RenderFidelityLevel.LayoutApproximate);
        var exact = Intent(RenderFidelityLevel.LayoutExact);

        var rejected = Assert.Throws<WordToolkitOperationException>(() =>
            RenderExecutionIntentValidator.ValidateAndResolve(exact, backend)
        );
        Assert.Equal("RENDER_APPROXIMATION_NOT_ALLOWED", rejected.Code);

        var optedIn = exact with
        {
            Fidelity = exact.Fidelity with { AllowApproximation = true },
        };
        var resolved = RenderExecutionIntentValidator.ValidateAndResolve(optedIn, backend);

        Assert.Contains(
            resolved.Resolutions,
            item => item.State == RenderResolutionState.Approximated
        );
    }

    [Fact]
    public void RejectsUnsupportedAndAmbiguousCapabilityWithoutFallback()
    {
        var intent = Intent(
            RenderFidelityLevel.SemanticPreview,
            requiredCapabilities: ["pagination"]
        );
        var unsupported = Assert.Throws<WordToolkitOperationException>(() =>
            RenderExecutionIntentValidator.ValidateAndResolve(intent, Profile())
        );
        Assert.Equal("RENDER_INTENT_UNSUPPORTED", unsupported.Code);

        var ambiguousProfile = Profile(
            capabilities:
            [
                new("pagination", RenderResolutionState.Resolved),
                new("PAGINATION", RenderResolutionState.Resolved),
            ]
        );
        var ambiguous = Assert.Throws<WordToolkitOperationException>(() =>
            RenderExecutionIntentValidator.ValidateAndResolve(intent, ambiguousProfile)
        );
        Assert.Equal("RENDER_INTENT_AMBIGUOUS", ambiguous.Code);
    }

    [Fact]
    public void ResolvesEveryBundleArtifactAndDoesNotTreatSingleOutputAsBundle()
    {
        var intent = new RenderExecutionIntent(
            new RenderSourceIntent(RenderSourceKind.SavedWordPackage, "package"),
            new RenderTargetIntent(RenderTargetKind.WholeDocument),
            new RenderOutputIntent(
                "page-bundle",
                [
                    new RenderArtifactIntent("page", "image/png"),
                    new RenderArtifactIntent("manifest", "application/json"),
                ]
            ),
            new RenderFidelityIntent(RenderFidelityLevel.SemanticPreview)
        );
        var bundle = new RenderBackendOutput(
            "page-bundle",
            RenderOutputCardinality.ArtifactBundle,
            [
                new RenderBackendArtifact("manifest", "application/json"),
                new RenderBackendArtifact("page", "image/png"),
            ]
        );
        var profile = Profile(outputs: [bundle]);

        var resolved = RenderExecutionIntentValidator.ValidateAndResolve(intent, profile);
        Assert.All(resolved.Resolutions, item => Assert.Equal(RenderResolutionState.Resolved, item.State));

        var singleOnly = Profile(
            outputs:
            [
                new RenderBackendOutput(
                    "page-bundle",
                    RenderOutputCardinality.SingleArtifact,
                    [new RenderBackendArtifact("page", "image/png")]
                ),
            ]
        );
        var unsupported = Assert.Throws<WordToolkitOperationException>(() =>
            RenderExecutionIntentValidator.ValidateAndResolve(intent, singleOnly)
        );
        Assert.Equal("RENDER_INTENT_UNSUPPORTED", unsupported.Code);

        var wrongSecondArtifact = intent with
        {
            Output = new RenderOutputIntent(
                "page-bundle",
                [
                    new RenderArtifactIntent("page", "image/png"),
                    new RenderArtifactIntent("manifest", "text/plain"),
                ]
            ),
        };
        var wrongMediaType = Assert.Throws<WordToolkitOperationException>(() =>
            RenderExecutionIntentValidator.ValidateAndResolve(wrongSecondArtifact, profile)
        );
        Assert.Equal("RENDER_INTENT_UNSUPPORTED", wrongMediaType.Code);
    }

    [Fact]
    public void PublishesValidatedMultiArtifactBatchCreateNew()
    {
        using var directory = new TestDirectory();
        var first = directory.PathFor("page-1.svg");
        var second = directory.PathFor("manifest.json");
        var publisher = new TransactionalRenderArtifactPublisher();

        var result = publisher.PublishCreateNew(
            [
                Artifact("page", first, "svg", "image/svg+xml", "<svg/>", bytes =>
                    bytes.Span.StartsWith("<svg"u8)
                        ? RenderArtifactValidationResult.Valid
                        : RenderArtifactValidationResult.Invalid("not svg")
                ),
                Artifact("manifest", second, "json", "application/json", "{}"),
            ]
        );

        Assert.Equal(2, result.Length);
        Assert.Equal("<svg/>", File.ReadAllText(first));
        Assert.Equal("{}", File.ReadAllText(second));
        Assert.All(result, item => Assert.Equal(64, item.Sha256.Length));
        Assert.Empty(directory.PrivateStagingFiles());
    }

    [Fact]
    public void RejectsDuplicateAndExistingOutputsBeforeStaging()
    {
        using var directory = new TestDirectory();
        var output = directory.PathFor("same.bin");
        var publisher = new TransactionalRenderArtifactPublisher();

        var duplicate = Assert.Throws<WordToolkitOperationException>(() =>
            publisher.PublishCreateNew(
                [
                    Artifact("one", output, "bin", "application/octet-stream", "one"),
                    Artifact("two", output, "bin", "application/octet-stream", "two"),
                ]
            )
        );
        Assert.Equal("DUPLICATE_OUTPUT_PATH", duplicate.Code);
        Assert.False(File.Exists(output));
        Assert.Empty(directory.PrivateStagingFiles());

        File.WriteAllText(output, "existing");
        var exists = Assert.Throws<WordToolkitOperationException>(() =>
            publisher.PublishCreateNew(
                [Artifact("one", output, "bin", "application/octet-stream", "new")]
            )
        );
        Assert.Equal("OUTPUT_EXISTS", exists.Code);
        Assert.Equal("existing", File.ReadAllText(output));
        Assert.Empty(directory.PrivateStagingFiles());
    }

    [Fact]
    public void PartialPublicationFailureRollsBackEveryCreatedArtifact()
    {
        using var directory = new TestDirectory();
        var fileSystem = new FaultInjectingFileSystem(failPublicationNumber: 2);
        var publisher = new TransactionalRenderArtifactPublisher(fileSystem);
        var first = directory.PathFor("one.bin");
        var second = directory.PathFor("two.bin");

        var failure = Assert.Throws<IOException>(() =>
            publisher.PublishCreateNew(
                [
                    Artifact("one", first, "bin", "application/octet-stream", "one"),
                    Artifact("two", second, "bin", "application/octet-stream", "two"),
                ]
            )
        );

        Assert.Contains("Injected publication failure", failure.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
        Assert.Empty(directory.PrivateStagingFiles());
    }

    [Fact]
    public void FailureAfterHardLinkCreationStillRollsBackOwnedOutput()
    {
        using var directory = new TestDirectory();
        var fileSystem = new FaultInjectingFileSystem(throwAfterPublicationNumber: 1);
        var publisher = new TransactionalRenderArtifactPublisher(fileSystem);
        var output = directory.PathFor("partial.bin");

        var failure = Assert.Throws<IOException>(() =>
            publisher.PublishCreateNew(
                [Artifact("partial", output, "bin", "application/octet-stream", "bytes")]
            )
        );

        Assert.Contains("after publication", failure.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(output));
        Assert.Empty(directory.PrivateStagingFiles());
    }

    [Fact]
    public void FailureAfterStagingDeleteRollsBackWithoutClaimingRollbackFailure()
    {
        using var directory = new TestDirectory();
        var fileSystem = new FaultInjectingFileSystem(throwAfterFirstStagingDelete: true);
        var publisher = new TransactionalRenderArtifactPublisher(fileSystem);
        var output = directory.PathFor("delete-partial.bin");

        var failure = Assert.Throws<IOException>(() =>
            publisher.PublishCreateNew(
                [Artifact("partial", output, "bin", "application/octet-stream", "bytes")]
            )
        );

        Assert.Contains("after staging delete", failure.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(output));
        Assert.Empty(directory.PrivateStagingFiles());
    }

    [Fact]
    public void InvalidStagedArtifactPreventsAnyPublication()
    {
        using var directory = new TestDirectory();
        var first = directory.PathFor("one.bin");
        var second = directory.PathFor("two.bin");
        var publisher = new TransactionalRenderArtifactPublisher();

        var failure = Assert.Throws<WordToolkitOperationException>(() =>
            publisher.PublishCreateNew(
                [
                    Artifact("one", first, "bin", "application/octet-stream", "one"),
                    Artifact(
                        "two",
                        second,
                        "bin",
                        "application/octet-stream",
                        "two",
                        _ => RenderArtifactValidationResult.Invalid("injected invalid artifact")
                    ),
                ]
            )
        );

        Assert.Equal("RENDER_ARTIFACT_INVALID", failure.Code);
        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
        Assert.Empty(directory.PrivateStagingFiles());
    }

    [Fact]
    public void UnremovableStagingArtifactIsExplicitRollbackFailure()
    {
        using var directory = new TestDirectory();
        var fileSystem = new FaultInjectingFileSystem(refuseStagingDeletes: true);
        var publisher = new TransactionalRenderArtifactPublisher(fileSystem);

        var failure = Assert.Throws<WordToolkitOperationException>(() =>
            publisher.PublishCreateNew(
                [
                    Artifact(
                        "invalid",
                        directory.PathFor("invalid.bin"),
                        "bin",
                        "application/octet-stream",
                        "bad",
                        _ => RenderArtifactValidationResult.Invalid("invalid")
                    ),
                ]
            )
        );

        Assert.Equal("ROLLBACK_FAILED", failure.Code);
        var details = Assert.IsType<RenderPublicationRollbackDetails>(failure.Details);
        Assert.Single(details.UnverifiedPaths);
        Assert.Contains(
            ".wordtoolkit-render-transaction-",
            details.UnverifiedPaths[0],
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void RejectsReparsePointAndNormalizedPathAliasesBeforeStaging()
    {
        using var directory = new TestDirectory();
        var reparsePublisher = new TransactionalRenderArtifactPublisher(
            new FaultInjectingFileSystem(reportReparsePoint: true)
        );
        var reparse = Assert.Throws<WordToolkitOperationException>(() =>
            reparsePublisher.PublishCreateNew(
                [
                    Artifact(
                        "reparse",
                        directory.PathFor("reparse.bin"),
                        "bin",
                        "application/octet-stream",
                        "bad"
                    ),
                ]
            )
        );
        Assert.Equal("OUTPUT_PATH_ALIAS_REJECTED", reparse.Code);

        directory.CreateDirectory("nested");
        var canonical = directory.PathFor("same.bin");
        var alias = Path.Combine(directory.PathFor("nested"), "..", "same.bin");
        var duplicate = Assert.Throws<WordToolkitOperationException>(() =>
            new TransactionalRenderArtifactPublisher().PublishCreateNew(
                [
                    Artifact("one", canonical, "bin", "application/octet-stream", "one"),
                    Artifact("two", alias, "bin", "application/octet-stream", "two"),
                ]
            )
        );
        Assert.Equal("DUPLICATE_OUTPUT_PATH", duplicate.Code);
        Assert.Empty(directory.PrivateStagingFiles());
    }

    [Fact]
    public void FailedRollbackIsReportedAndNamesOnlyUnverifiedPaths()
    {
        using var directory = new TestDirectory();
        var first = directory.PathFor("one.bin");
        var fileSystem = new FaultInjectingFileSystem(
            failPublicationNumber: 2,
            undeletablePath: first
        );
        var publisher = new TransactionalRenderArtifactPublisher(fileSystem);

        var failure = Assert.Throws<WordToolkitOperationException>(() =>
            publisher.PublishCreateNew(
                [
                    Artifact("one", first, "bin", "application/octet-stream", "one"),
                    Artifact(
                        "two",
                        directory.PathFor("two.bin"),
                        "bin",
                        "application/octet-stream",
                        "two"
                    ),
                ]
            )
        );

        Assert.Equal("ROLLBACK_FAILED", failure.Code);
        var details = Assert.IsType<RenderPublicationRollbackDetails>(failure.Details);
        Assert.Equal("IOException", details.OriginalFailure);
        Assert.Equal(new[] { first }, details.UnverifiedPaths.ToArray());
        Assert.True(File.Exists(first));
    }

    [Fact]
    public void CancellationAfterFirstPublicationRollsBackBatch()
    {
        using var directory = new TestDirectory();
        using var cancellation = new CancellationTokenSource();
        var fileSystem = new FaultInjectingFileSystem(
            afterPublication: count =>
            {
                if (count == 1)
                {
                    cancellation.Cancel();
                }
            }
        );
        var publisher = new TransactionalRenderArtifactPublisher(fileSystem);
        var first = directory.PathFor("one.bin");
        var second = directory.PathFor("two.bin");

        Assert.Throws<OperationCanceledException>(() =>
            publisher.PublishCreateNew(
                [
                    Artifact("one", first, "bin", "application/octet-stream", "one"),
                    Artifact("two", second, "bin", "application/octet-stream", "two"),
                ],
                cancellation.Token
            )
        );

        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
        Assert.Empty(directory.PrivateStagingFiles());
    }

    private static RenderExecutionIntent Intent(
        RenderFidelityLevel fidelity,
        IEnumerable<string>? requiredCapabilities = null
    ) =>
        new(
            new RenderSourceIntent(RenderSourceKind.SavedWordPackage, "package", "sha256"),
            new RenderTargetIntent(RenderTargetKind.WholeDocument),
            new RenderOutputIntent(
                "svg",
                [new RenderArtifactIntent("page", "image/svg+xml")]
            ),
            new RenderFidelityIntent(fidelity, requiredCapabilities: requiredCapabilities)
        );

    private static RenderBackendProfile Profile(
        RenderFidelityLevel maximumFidelity = RenderFidelityLevel.SemanticPreview,
        IEnumerable<RenderBackendCapability>? capabilities = null,
        IEnumerable<RenderBackendOutput>? outputs = null
    ) =>
        new(
            "test",
            "1",
            [RenderSourceKind.SavedWordPackage],
            [RenderTargetKind.WholeDocument],
            outputs
                ?? [
                    new RenderBackendOutput(
                        "svg",
                        RenderOutputCardinality.SingleArtifact,
                        [new RenderBackendArtifact("page", "image/svg+xml")]
                    ),
                ],
            maximumFidelity,
            capabilities
        );

    private static RenderArtifactPublication Artifact(
        string id,
        string path,
        string format,
        string mediaType,
        string content,
        RenderArtifactValidator? validator = null
    ) => new(id, path, format, mediaType, System.Text.Encoding.UTF8.GetBytes(content), validator);

    private sealed class FaultInjectingFileSystem(
        int? failPublicationNumber = null,
        string? undeletablePath = null,
        Action<int>? afterPublication = null,
        int? throwAfterPublicationNumber = null,
        bool throwAfterFirstStagingDelete = false,
        bool refuseStagingDeletes = false,
        bool reportReparsePoint = false
    ) : IRenderArtifactPublicationFileSystem
    {
        private int _publicationCount;

        public bool DirectoryExists(string path) => Directory.Exists(path);

        public bool FileExists(string path) => File.Exists(path);

        public bool ContainsReparsePoint(string directoryPath) => reportReparsePoint;

        public string CanonicalizeDirectory(string directoryPath) =>
            Path.GetFullPath(directoryPath);

        public void WriteCreateNew(string path, ReadOnlyMemory<byte> bytes) =>
            File.WriteAllBytes(path, bytes.ToArray());

        public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

        public void PublishNoClobber(string temporaryPath, string outputPath)
        {
            _publicationCount++;
            if (_publicationCount == failPublicationNumber)
            {
                throw new IOException("Injected publication failure.");
            }
            File.Copy(temporaryPath, outputPath, overwrite: false);
            afterPublication?.Invoke(_publicationCount);
            if (_publicationCount == throwAfterPublicationNumber)
            {
                throw new IOException("Injected failure after publication.");
            }
        }

        public void DeleteFile(string path)
        {
            var isStaging = Path.GetFileName(path).StartsWith(
                ".wordtoolkit-render-transaction-",
                StringComparison.Ordinal
            );
            if (refuseStagingDeletes && isStaging)
            {
                throw new IOException("Injected staging cleanup failure.");
            }
            if (
                undeletablePath is not null
                && string.Equals(path, undeletablePath, StringComparison.OrdinalIgnoreCase)
            )
            {
                throw new IOException("Injected cleanup failure.");
            }
            File.Delete(path);
            if (throwAfterFirstStagingDelete && isStaging)
            {
                throwAfterFirstStagingDelete = false;
                throw new IOException("Injected failure after staging delete.");
            }
        }
    }

    private sealed class TestDirectory : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-render-contracts-" + Guid.NewGuid().ToString("N")
        );

        public TestDirectory() => Directory.CreateDirectory(_path);

        public string PathFor(string name) => Path.Combine(_path, name);

        public void CreateDirectory(string name) => Directory.CreateDirectory(PathFor(name));

        public IEnumerable<string> PrivateStagingFiles() =>
            Directory.EnumerateFiles(_path, ".wordtoolkit-render-transaction-*.tmp");

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}
