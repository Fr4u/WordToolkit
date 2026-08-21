using WordToolkit.Engine.Extensions;
using WordToolkit.Engine.Validation;
using WordToolkit.Native.Ocr;
using WordToolkit.OpenXmlSdk;

namespace WordToolkit.Native.Protocol;

internal static class NativeExtensionHost
{
    internal const string OpenXmlValidatorExtensionId =
        "wordtoolkit.openxml-sdk";
    internal const string OpenXmlValidatorCapabilityId =
        "wordtoolkit.validator.openxml.microsoft365";
    internal const string TesseractOcrExtensionId = TesseractCliOcrProvider.ExtensionId;
    internal const string TesseractOcrCapabilityId =
        TesseractCliOcrProvider.DefaultCapabilityId;

    private static readonly Lazy<State> Current = new(CreateState);

    internal static WordToolkitExtensionRegistry Registry => Current.Value.Registry;

    internal static IWordPackageCandidateValidator CandidateValidator =>
        Current.Value.CandidateValidator;

    private static State CreateState()
    {
        var policyLimits = new WordToolkitExtensionResourceLimits(
            MaxInputBytes: 1024L * 1024 * 1024,
            MaxOutputBytes: 8L * 1024 * 1024,
            MaxConcurrentInvocations: McpServer.MaxConcurrentRequests,
            TimeoutMilliseconds: 125_000,
            MaxProcessMemoryBytes: ProcessBoundaryTesseractOcrProvider.MaximumProcessMemoryBytes
        );
        var validatorLimits = policyLimits with
        {
            MaxOutputBytes = 2L * 1024 * 1024,
            TimeoutMilliseconds = 120_000,
            MaxProcessMemoryBytes = null,
        };
        var policy = new WordToolkitExtensionPolicy(
            [OpenXmlValidatorExtensionId, TesseractOcrExtensionId],
            [WordToolkitExtensionTrust.BuiltIn],
            [
                WordToolkitExtensionIsolation.TrustedInProcess,
                WordToolkitExtensionIsolation.OutOfProcess,
            ],
            [
                new WordToolkitExtensionInterfaceSupport(
                    "wordtoolkit.package-candidate-validator",
                    "1.0",
                    WordToolkitExtensionKind.Validator
                ),
                new WordToolkitExtensionInterfaceSupport(
                    WordOcrProviderContract.InterfaceContract,
                    WordOcrProviderContract.InterfaceVersion,
                    WordToolkitExtensionKind.OcrProvider
                ),
            ],
            WordToolkitExtensionPermission.ReadPackage
                | WordToolkitExtensionPermission.ReadDocumentContent
                | WordToolkitExtensionPermission.FilesystemRead
                | WordToolkitExtensionPermission.FilesystemWrite
                | WordToolkitExtensionPermission.SpawnProcess,
            policyLimits
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
                validatorLimits,
                WordToolkitExtensionTimeoutEnforcement.Cooperative,
                Deterministic: true,
                Idempotent: true,
                ReturnsDocumentContent: false
            ),
            new MicrosoftOpenXmlPackageValidator()
        );
        builder.Register<IWordOcrProvider>(
            new WordToolkitExtensionDescriptor(
                TesseractOcrExtensionId,
                "Tesseract CLI OCR adapter",
                "WordToolkit project",
                "1.0.0",
                "1.0",
                WordToolkitExtensionTrust.BuiltIn,
                WordToolkitExtensionIsolation.OutOfProcess
            ),
            new WordToolkitExtensionCapabilityDescriptor(
                TesseractOcrCapabilityId,
                WordToolkitExtensionKind.OcrProvider,
                WordOcrProviderContract.InterfaceContract,
                WordOcrProviderContract.InterfaceVersion,
                WordToolkitExtensionPermission.ReadDocumentContent
                    | WordToolkitExtensionPermission.FilesystemRead
                    | WordToolkitExtensionPermission.FilesystemWrite
                    | WordToolkitExtensionPermission.SpawnProcess,
                new WordToolkitExtensionResourceLimits(
                    MaxInputBytes: 32L * 1024 * 1024,
                    MaxOutputBytes: 8L * 1024 * 1024,
                    MaxConcurrentInvocations: 1,
                    TimeoutMilliseconds: 125_000,
                    MaxProcessMemoryBytes: ProcessBoundaryTesseractOcrProvider.MaximumProcessMemoryBytes
                ),
                WordToolkitExtensionTimeoutEnforcement.ProcessBoundary,
                Deterministic: false,
                Idempotent: true,
                ReturnsDocumentContent: true,
                SandboxProfile: WordToolkitExtensionSandboxProfile.WindowsAppContainerNoNetworkBrokeredFilesystem,
                ProviderIdentityPolicy: WordToolkitExtensionProviderIdentityPolicy.SignedManifestSessionPinned
            ),
            new ProcessBoundaryTesseractOcrProvider()
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
