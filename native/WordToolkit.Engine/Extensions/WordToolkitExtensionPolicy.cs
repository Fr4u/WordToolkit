namespace WordToolkit.Engine.Extensions;

public sealed class WordToolkitExtensionPolicy
{
    private readonly HashSet<string> _allowedExtensionIds;
    private readonly HashSet<WordToolkitExtensionTrust> _allowedTrustLevels;
    private readonly HashSet<WordToolkitExtensionIsolation> _allowedIsolationModes;
    private readonly Dictionary<string, WordToolkitExtensionInterfaceSupport>
        _supportedInterfaces;

    public WordToolkitExtensionPolicy(
        IEnumerable<string> allowedExtensionIds,
        IEnumerable<WordToolkitExtensionTrust> allowedTrustLevels,
        IEnumerable<WordToolkitExtensionIsolation> allowedIsolationModes,
        IEnumerable<WordToolkitExtensionInterfaceSupport> supportedInterfaces,
        WordToolkitExtensionPermission allowedPermissions,
        WordToolkitExtensionResourceLimits maximumResourceLimits,
        int supportedEngineContractMajor = 1,
        int supportedEngineContractMinor = 0
    )
    {
        ArgumentNullException.ThrowIfNull(allowedExtensionIds);
        ArgumentNullException.ThrowIfNull(allowedTrustLevels);
        ArgumentNullException.ThrowIfNull(allowedIsolationModes);
        ArgumentNullException.ThrowIfNull(supportedInterfaces);
        ArgumentNullException.ThrowIfNull(maximumResourceLimits);
        if (supportedEngineContractMajor < 1 || supportedEngineContractMinor < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(supportedEngineContractMajor)
            );
        }

        _allowedExtensionIds = allowedExtensionIds.ToHashSet(StringComparer.Ordinal);
        _allowedTrustLevels = allowedTrustLevels.ToHashSet();
        _allowedIsolationModes = allowedIsolationModes.ToHashSet();
        _supportedInterfaces = new Dictionary<
            string,
            WordToolkitExtensionInterfaceSupport
        >(StringComparer.Ordinal);
        foreach (var support in supportedInterfaces)
        {
            ArgumentNullException.ThrowIfNull(support);
            WordToolkitExtensionValidation.ValidateInterfaceSupport(support);
            if (!_supportedInterfaces.TryAdd(support.InterfaceContract, support))
            {
                throw new ArgumentException(
                    $"Duplicate supported interface '{support.InterfaceContract}'.",
                    nameof(supportedInterfaces)
                );
            }
        }
        AllowedPermissions = allowedPermissions;
        MaximumResourceLimits = maximumResourceLimits;
        SupportedEngineContractMajor = supportedEngineContractMajor;
        SupportedEngineContractMinor = supportedEngineContractMinor;
        WordToolkitExtensionValidation.ValidateLimits(
            maximumResourceLimits,
            nameof(maximumResourceLimits)
        );
        if (_allowedExtensionIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Allowed extension identifiers must not be empty.",
                nameof(allowedExtensionIds)
            );
        }
    }

    public WordToolkitExtensionPermission AllowedPermissions { get; }

    public WordToolkitExtensionResourceLimits MaximumResourceLimits { get; }

    public int SupportedEngineContractMajor { get; }

    public int SupportedEngineContractMinor { get; }

    public static WordToolkitExtensionPolicy BuiltInOnly(
        IEnumerable<string> allowedExtensionIds,
        IEnumerable<WordToolkitExtensionInterfaceSupport> supportedInterfaces,
        WordToolkitExtensionPermission allowedPermissions,
        WordToolkitExtensionResourceLimits? maximumResourceLimits = null
    ) => new(
        allowedExtensionIds,
        [WordToolkitExtensionTrust.BuiltIn],
        [WordToolkitExtensionIsolation.TrustedInProcess],
        supportedInterfaces,
        allowedPermissions,
        maximumResourceLimits
            ?? WordToolkitExtensionResourceLimits.ConservativeDefault
    );

    internal void Authorize(
        WordToolkitExtensionDescriptor extension,
        WordToolkitExtensionCapabilityDescriptor capability
    )
    {
        if (!_allowedExtensionIds.Contains(extension.ExtensionId))
        {
            throw Denied(
                $"Extension '{extension.ExtensionId}' is not on the host allowlist."
            );
        }
        if (!_allowedTrustLevels.Contains(extension.Trust))
        {
            throw Denied(
                $"Extension '{extension.ExtensionId}' uses a disallowed trust level."
            );
        }
        if (!_allowedIsolationModes.Contains(extension.Isolation))
        {
            throw Denied(
                $"Extension '{extension.ExtensionId}' uses a disallowed isolation mode."
            );
        }
        if (
            extension.Isolation == WordToolkitExtensionIsolation.TrustedInProcess
            && capability.TimeoutEnforcement
                != WordToolkitExtensionTimeoutEnforcement.Cooperative
        )
        {
            throw Invalid(
                "An in-process extension cannot claim process-boundary timeout enforcement."
            );
        }
        if (
            extension.Isolation == WordToolkitExtensionIsolation.TrustedInProcess
            && capability.ResourceLimits.MaxProcessMemoryBytes is not null
        )
        {
            throw Invalid(
                "An in-process extension cannot claim a process-memory boundary."
            );
        }
        if (
            extension.Isolation == WordToolkitExtensionIsolation.OutOfProcess
            && capability.TimeoutEnforcement
                != WordToolkitExtensionTimeoutEnforcement.ProcessBoundary
        )
        {
            throw Invalid(
                "An out-of-process extension must use process-boundary timeout enforcement."
            );
        }
        if (
            extension.Isolation == WordToolkitExtensionIsolation.OutOfProcess
            && capability.ResourceLimits.MaxProcessMemoryBytes is null
        )
        {
            throw Invalid(
                "An out-of-process extension must declare a positive process-memory ceiling."
            );
        }

        var (major, minor) = WordToolkitExtensionValidation.ParseContractVersion(
            extension.EngineContractVersion,
            nameof(extension.EngineContractVersion)
        );
        if (
            major != SupportedEngineContractMajor
            || minor > SupportedEngineContractMinor
        )
        {
            throw new WordToolkitExtensionException(
                "EXTENSION_CONTRACT_MISMATCH",
                $"Extension '{extension.ExtensionId}' requires unsupported engine contract {major}.{minor}."
            );
        }
        if ((capability.Permissions & ~AllowedPermissions) != 0)
        {
            throw Denied(
                $"Capability '{capability.CapabilityId}' requests permissions outside host policy."
            );
        }

        if (
            !_supportedInterfaces.TryGetValue(
                capability.InterfaceContract,
                out var interfaceSupport
            )
            || interfaceSupport.Kind != capability.Kind
        )
        {
            throw new WordToolkitExtensionException(
                "EXTENSION_CONTRACT_MISMATCH",
                $"Capability '{capability.CapabilityId}' uses an unsupported interface contract."
            );
        }
        var requestedInterfaceVersion =
            WordToolkitExtensionValidation.ParseContractVersion(
                capability.InterfaceVersion,
                nameof(capability.InterfaceVersion)
            );
        var supportedInterfaceVersion =
            WordToolkitExtensionValidation.ParseContractVersion(
                interfaceSupport.MaximumVersion,
                nameof(interfaceSupport.MaximumVersion)
            );
        if (
            requestedInterfaceVersion.Major != supportedInterfaceVersion.Major
            || requestedInterfaceVersion.Minor > supportedInterfaceVersion.Minor
        )
        {
            throw new WordToolkitExtensionException(
                "EXTENSION_CONTRACT_MISMATCH",
                $"Capability '{capability.CapabilityId}' requires unsupported interface version {capability.InterfaceVersion}."
            );
        }

        var limits = capability.ResourceLimits;
        var maximum = MaximumResourceLimits;
        if (
            limits.MaxInputBytes > maximum.MaxInputBytes
            || limits.MaxOutputBytes > maximum.MaxOutputBytes
            || limits.MaxConcurrentInvocations > maximum.MaxConcurrentInvocations
            || limits.TimeoutMilliseconds > maximum.TimeoutMilliseconds
            || (
                limits.MaxProcessMemoryBytes is not null
                && (
                    maximum.MaxProcessMemoryBytes is null
                    || limits.MaxProcessMemoryBytes > maximum.MaxProcessMemoryBytes
                )
            )
        )
        {
            throw Denied(
                $"Capability '{capability.CapabilityId}' requests resources outside host policy."
            );
        }
    }

    private static WordToolkitExtensionException Denied(string message) => new(
        "EXTENSION_PERMISSION_DENIED",
        message
    );

    private static WordToolkitExtensionException Invalid(string message) => new(
        "EXTENSION_REGISTRATION_INVALID",
        message
    );
}
