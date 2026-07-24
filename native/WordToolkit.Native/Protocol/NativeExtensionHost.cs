using WordToolkit.Engine.Extensions;
using WordToolkit.Engine.Validation;
using WordToolkit.OpenXmlSdk;

namespace WordToolkit.Native.Protocol;

internal static class NativeExtensionHost
{
    internal const string OpenXmlValidatorExtensionId =
        "wordtoolkit.openxml-sdk";
    internal const string OpenXmlValidatorCapabilityId =
        "wordtoolkit.validator.openxml.microsoft365";

    private static readonly Lazy<State> Current = new(CreateState);

    internal static WordToolkitExtensionRegistry Registry => Current.Value.Registry;

    internal static IWordPackageCandidateValidator CandidateValidator =>
        Current.Value.CandidateValidator;

    private static State CreateState()
    {
        var limits = new WordToolkitExtensionResourceLimits(
            MaxInputBytes: 1024L * 1024 * 1024,
            MaxOutputBytes: 2L * 1024 * 1024,
            MaxConcurrentInvocations: McpServer.MaxConcurrentRequests,
            TimeoutMilliseconds: 120_000
        );
        var policy = WordToolkitExtensionPolicy.BuiltInOnly(
            [OpenXmlValidatorExtensionId],
            [
                new WordToolkitExtensionInterfaceSupport(
                    "wordtoolkit.package-candidate-validator",
                    "1.0",
                    WordToolkitExtensionKind.Validator
                ),
            ],
            WordToolkitExtensionPermission.ReadPackage
                | WordToolkitExtensionPermission.ReadDocumentContent,
            limits
        );
        var builder = new WordToolkitExtensionRegistryBuilder(policy);
        builder.Register<IWordPackageCandidateValidator>(
            new WordToolkitExtensionDescriptor(
                OpenXmlValidatorExtensionId,
                "Microsoft Open XML SDK validator",
                "WordToolkit project",
                "3.3.0+wordtoolkit.1",
                "1.0",
                WordToolkitExtensionTrust.BuiltIn,
                WordToolkitExtensionIsolation.TrustedInProcess
            ),
            new WordToolkitExtensionCapabilityDescriptor(
                OpenXmlValidatorCapabilityId,
                WordToolkitExtensionKind.Validator,
                "wordtoolkit.package-candidate-validator",
                "1.0",
                WordToolkitExtensionPermission.ReadPackage
                    | WordToolkitExtensionPermission.ReadDocumentContent,
                limits,
                WordToolkitExtensionTimeoutEnforcement.Cooperative,
                Deterministic: true,
                Idempotent: true,
                ReturnsDocumentContent: false
            ),
            new MicrosoftOpenXmlPackageValidator()
        );
        var registry = builder.Build();
        return new State(
            registry,
            new ExtensionWordPackageCandidateValidator(
                registry,
                OpenXmlValidatorCapabilityId
            )
        );
    }

    private sealed record State(
        WordToolkitExtensionRegistry Registry,
        IWordPackageCandidateValidator CandidateValidator
    );
}
