using WordToolkit.Engine.Extensions;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Tests;

public sealed class ExtensionRegistryTests
{
    [Fact]
    public void CatalogHashIsStableAcrossRegistrationOrder()
    {
        var first = Builder("wordtoolkit.test.alpha", "wordtoolkit.test.beta");
        first.Register<ITestCapability>(
            Extension("wordtoolkit.test.beta"),
            Capability("wordtoolkit.capability.beta"),
            new EchoCapability()
        );
        first.Register<ITestCapability>(
            Extension("wordtoolkit.test.alpha"),
            Capability("wordtoolkit.capability.alpha"),
            new EchoCapability()
        );

        var second = Builder("wordtoolkit.test.alpha", "wordtoolkit.test.beta");
        second.Register<ITestCapability>(
            Extension("wordtoolkit.test.alpha"),
            Capability("wordtoolkit.capability.alpha"),
            new EchoCapability()
        );
        second.Register<ITestCapability>(
            Extension("wordtoolkit.test.beta"),
            Capability("wordtoolkit.capability.beta"),
            new EchoCapability()
        );

        Assert.Equal(
            first.Build().CatalogSha256,
            second.Build().CatalogSha256
        );
    }

    [Fact]
    public void RegistrationFailsClosedOnIdentityContractPermissionAndResources()
    {
        var policy = Policy(
            ["wordtoolkit.test.allowed"],
            WordToolkitExtensionPermission.ReadPackage,
            new WordToolkitExtensionResourceLimits(128, 128, 1, 100)
        );

        Assert.Equal(
            "EXTENSION_PERMISSION_DENIED",
            Assert.Throws<WordToolkitExtensionException>(() =>
                new WordToolkitExtensionRegistryBuilder(policy).Register<ITestCapability>(
                    Extension("wordtoolkit.test.denied"),
                    Capability("wordtoolkit.capability.denied"),
                    new EchoCapability()
                )
            ).Code
        );
        Assert.Equal(
            "EXTENSION_CONTRACT_MISMATCH",
            Assert.Throws<WordToolkitExtensionException>(() =>
                new WordToolkitExtensionRegistryBuilder(policy).Register<ITestCapability>(
                    Extension("wordtoolkit.test.allowed"),
                    Capability("wordtoolkit.capability.version") with
                    {
                        InterfaceVersion = "2.0",
                    },
                    new EchoCapability()
                )
            ).Code
        );
        Assert.Equal(
            "EXTENSION_PERMISSION_DENIED",
            Assert.Throws<WordToolkitExtensionException>(() =>
                new WordToolkitExtensionRegistryBuilder(policy).Register<ITestCapability>(
                    Extension("wordtoolkit.test.allowed"),
                    Capability("wordtoolkit.capability.network") with
                    {
                        Permissions = WordToolkitExtensionPermission.Network,
                    },
                    new EchoCapability()
                )
            ).Code
        );
        Assert.Equal(
            "EXTENSION_PERMISSION_DENIED",
            Assert.Throws<WordToolkitExtensionException>(() =>
                new WordToolkitExtensionRegistryBuilder(policy).Register<ITestCapability>(
                    Extension("wordtoolkit.test.allowed"),
                    Capability("wordtoolkit.capability.large") with
                    {
                        ResourceLimits = new WordToolkitExtensionResourceLimits(
                            129,
                            128,
                            1,
                            100
                        ),
                    },
                    new EchoCapability()
                )
            ).Code
        );
    }

    [Fact]
    public void BuilderRejectsDuplicatesConflictsAndUseAfterFreeze()
    {
        var builder = Builder("wordtoolkit.test.allowed");
        builder.Register<ITestCapability>(
            Extension("wordtoolkit.test.allowed"),
            Capability("wordtoolkit.capability.echo"),
            new EchoCapability()
        );
        Assert.Equal(
            "EXTENSION_REGISTRATION_INVALID",
            Assert.Throws<WordToolkitExtensionException>(() =>
                builder.Register<ITestCapability>(
                    Extension("wordtoolkit.test.allowed"),
                    Capability("wordtoolkit.capability.echo"),
                    new EchoCapability()
                )
            ).Code
        );
        _ = builder.Build();
        Assert.Equal(
            "EXTENSION_REGISTRATION_INVALID",
            Assert.Throws<WordToolkitExtensionException>(() => builder.Build()).Code
        );
    }

    [Fact]
    public void PublishedCatalogCollectionsCannotBeMutated()
    {
        var registry = Registry(Capability("wordtoolkit.capability.echo"));
        var extensions = Assert.IsAssignableFrom<
            IList<WordToolkitRegisteredExtension>
        >(registry.Extensions);
        Assert.Throws<NotSupportedException>(() => extensions[0] = extensions[0]);

        var capabilities = Assert.IsAssignableFrom<
            IList<WordToolkitExtensionCapabilityDescriptor>
        >(registry.Extensions[0].Capabilities);
        Assert.Throws<NotSupportedException>(() =>
            capabilities[0] = capabilities[0]
        );
    }

    [Fact]
    public void InvocationEnforcesInputOutputContractAndCooperativeTimeout()
    {
        var registry = Registry(
            Capability("wordtoolkit.capability.echo") with
            {
                ResourceLimits = new WordToolkitExtensionResourceLimits(
                    MaxInputBytes: 8,
                    MaxOutputBytes: 8,
                    MaxConcurrentInvocations: 1,
                    TimeoutMilliseconds: 10
                ),
            }
        );

        Assert.Equal(
            "EXTENSION_LIMIT_EXCEEDED",
            Assert.Throws<WordToolkitExtensionException>(() =>
                registry.Invoke<ITestCapability, string>(
                    "wordtoolkit.capability.echo",
                    9,
                    (service, token) => service.Echo("x", token),
                    value => value.Length
                )
            ).Code
        );
        Assert.Equal(
            "EXTENSION_LIMIT_EXCEEDED",
            Assert.Throws<WordToolkitExtensionException>(() =>
                registry.Invoke<ITestCapability, string>(
                    "wordtoolkit.capability.echo",
                    1,
                    (service, token) => service.Echo("123456789", token),
                    value => value.Length
                )
            ).Code
        );
        Assert.Equal(
            "EXTENSION_CONTRACT_MISMATCH",
            Assert.Throws<WordToolkitExtensionException>(() =>
                registry.Invoke<IDisposable, string>(
                    "wordtoolkit.capability.echo",
                    1,
                    (_, _) => "x",
                    value => value.Length
                )
            ).Code
        );
        Assert.Equal(
            "EXTENSION_TIMEOUT",
            Assert.Throws<WordToolkitExtensionException>(() =>
                registry.Invoke<ITestCapability, string>(
                    "wordtoolkit.capability.echo",
                    1,
                    (_, _) =>
                    {
                        Thread.Sleep(30);
                        return "x";
                    },
                    value => value.Length
                )
            ).Code
        );
    }

    [Fact]
    public async Task InvocationRejectsWorkBeyondConcurrencyLimit()
    {
        var registry = Registry(Capability("wordtoolkit.capability.echo"));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var first = Task.Run(() => registry.Invoke<ITestCapability, string>(
            "wordtoolkit.capability.echo",
            1,
            (_, _) =>
            {
                entered.Set();
                release.Wait();
                return "x";
            },
            value => value.Length
        ));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        var exception = Assert.Throws<WordToolkitExtensionException>(() =>
            registry.Invoke<ITestCapability, string>(
                "wordtoolkit.capability.echo",
                1,
                (service, token) => service.Echo("y", token),
                value => value.Length
            )
        );
        Assert.Equal("EXTENSION_BUSY", exception.Code);
        Assert.True(exception.Retryable);
        release.Set();
        Assert.Equal("x", await first);
    }

    [Fact]
    public void CatalogInspectionIsBoundedSearchableAndContentFree()
    {
        var builder = Builder("wordtoolkit.test.alpha", "wordtoolkit.test.beta");
        builder.Register<ITestCapability>(
            Extension("wordtoolkit.test.alpha"),
            Capability("wordtoolkit.capability.alpha"),
            new EchoCapability()
        );
        builder.Register<ITestCapability>(
            Extension("wordtoolkit.test.beta") with { DisplayName = "Beta Validator" },
            Capability("wordtoolkit.capability.beta"),
            new EchoCapability()
        );
        var registry = builder.Build();
        var operation = new InspectExtensionCatalogOperation(registry);

        var first = operation.Execute(new InspectExtensionCatalogRequest(Limit: 1));
        Assert.Equal(InspectExtensionCatalogContract.Contract, first.OperationContract);
        Assert.Equal(2, first.ExtensionCount);
        Assert.Equal(2, first.CapabilityCount);
        Assert.Single(first.Items);
        Assert.Equal(1, first.Paging.NextOffset);
        Assert.False(first.Security.ReadsDocument);
        Assert.False(first.Security.LoadsAssemblies);

        var filtered = operation.Execute(
            new InspectExtensionCatalogRequest("beta", 0, 4)
        );
        Assert.Single(filtered.Items);
        Assert.Equal("wordtoolkit.capability.beta", filtered.Items[0].CapabilityId);
        var json = WordToolkitOperationJson.Serialize(filtered);
        Assert.DoesNotContain(nameof(EchoCapability), json, StringComparison.Ordinal);
        Assert.DoesNotContain("WordToolkit.Engine.Tests", json, StringComparison.Ordinal);
        Assert.DoesNotContain(".dll", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", json, StringComparison.Ordinal);
        Assert.DoesNotContain("document_text", json, StringComparison.Ordinal);
        Assert.Throws<WordToolkitOperationException>(() => operation.Execute(
            new InspectExtensionCatalogRequest(Limit: 33)
        ));
    }

    [Fact]
    public void ValidatorAdapterRoutesThroughRegistryLimits()
    {
        var capability = Capability("wordtoolkit.validator.test") with
        {
            InterfaceContract = "wordtoolkit.package-candidate-validator",
            Kind = WordToolkitExtensionKind.Validator,
            ResourceLimits = new WordToolkitExtensionResourceLimits(16, 4096, 1, 100),
        };
        var policy = WordToolkitExtensionPolicy.BuiltInOnly(
            ["wordtoolkit.test.validator"],
            [
                new WordToolkitExtensionInterfaceSupport(
                    "wordtoolkit.package-candidate-validator",
                    "1.0",
                    WordToolkitExtensionKind.Validator
                ),
            ],
            WordToolkitExtensionPermission.ReadPackage,
            capability.ResourceLimits
        );
        var builder = new WordToolkitExtensionRegistryBuilder(policy);
        builder.Register<IWordPackageCandidateValidator>(
            Extension("wordtoolkit.test.validator"),
            capability,
            new PassingValidator()
        );
        var adapter = new ExtensionWordPackageCandidateValidator(
            builder.Build(),
            capability.CapabilityId
        );

        using var baseline = new MemoryStream(new byte[8]);
        using var candidate = new MemoryStream(new byte[8]);
        Assert.True(adapter.Validate(baseline, candidate).NoNewErrors);
        using var tooLarge = new MemoryStream(new byte[9]);
        Assert.Equal(
            "EXTENSION_LIMIT_EXCEEDED",
            Assert.Throws<WordToolkitExtensionException>(() =>
                adapter.Validate(baseline, tooLarge)
            ).Code
        );
    }

    private static WordToolkitExtensionRegistryBuilder Builder(
        params string[] extensionIds
    ) => new(Policy(
        extensionIds,
        WordToolkitExtensionPermission.ReadPackage,
        new WordToolkitExtensionResourceLimits(1024, 1024, 1, 5000)
    ));

    private static WordToolkitExtensionPolicy Policy(
        IEnumerable<string> extensionIds,
        WordToolkitExtensionPermission permissions,
        WordToolkitExtensionResourceLimits maximum
    ) => WordToolkitExtensionPolicy.BuiltInOnly(
        extensionIds,
        [
            new WordToolkitExtensionInterfaceSupport(
                "wordtoolkit.test-interface",
                "1.0",
                WordToolkitExtensionKind.Validator
            ),
        ],
        permissions,
        maximum
    );

    private static WordToolkitExtensionRegistry Registry(
        WordToolkitExtensionCapabilityDescriptor capability
    )
    {
        var builder = Builder("wordtoolkit.test.allowed");
        builder.Register<ITestCapability>(
            Extension("wordtoolkit.test.allowed"),
            capability,
            new EchoCapability()
        );
        return builder.Build();
    }

    private static WordToolkitExtensionDescriptor Extension(string id) => new(
        id,
        "Test extension",
        "WordToolkit tests",
        "1.2.3",
        "1.0",
        WordToolkitExtensionTrust.BuiltIn,
        WordToolkitExtensionIsolation.TrustedInProcess
    );

    private static WordToolkitExtensionCapabilityDescriptor Capability(string id) => new(
        id,
        WordToolkitExtensionKind.Validator,
        "wordtoolkit.test-interface",
        "1.0",
        WordToolkitExtensionPermission.ReadPackage,
        new WordToolkitExtensionResourceLimits(1024, 1024, 1, 5000),
        WordToolkitExtensionTimeoutEnforcement.Cooperative,
        Deterministic: true,
        Idempotent: true,
        ReturnsDocumentContent: false
    );

    private interface ITestCapability
    {
        string Echo(string value, CancellationToken cancellationToken);
    }

    private sealed class EchoCapability : ITestCapability
    {
        public string Echo(string value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return value;
        }
    }

    private sealed class PassingValidator : IWordPackageCandidateValidator
    {
        public WordPackageCandidateValidationReport Validate(
            Stream baselinePackage,
            Stream candidatePackage,
            CancellationToken cancellationToken = default
        ) => new(
            Performed: true,
            CandidateValid: true,
            NoNewErrors: true,
            ErrorCount: 0,
            BaselineErrorCount: 0,
            CandidateErrorCount: 0,
            ErrorsTruncated: false,
            NotPerformedReason: null,
            Issues: []
        );
    }
}
