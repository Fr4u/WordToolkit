using System.Text.RegularExpressions;

namespace WordToolkit.Engine.Extensions;

internal static partial class WordToolkitExtensionValidation
{
    [GeneratedRegex(
        "^[a-z0-9](?:[a-z0-9._-]{0,126}[a-z0-9])?$",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex SemanticVersionPattern();

    [GeneratedRegex(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex ContractVersionPattern();

    internal static void ValidateDescriptor(WordToolkitExtensionDescriptor value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateIdentifier(value.ExtensionId, nameof(value.ExtensionId));
        ValidateBoundedText(value.DisplayName, 128, nameof(value.DisplayName));
        ValidateBoundedText(value.Publisher, 128, nameof(value.Publisher));
        if (!SemanticVersionPattern().IsMatch(value.ExtensionVersion))
        {
            throw Invalid(
                $"{nameof(value.ExtensionVersion)} must be a canonical semantic version."
            );
        }
        _ = ParseContractVersion(
            value.EngineContractVersion,
            nameof(value.EngineContractVersion)
        );
    }

    internal static void ValidateCapability(
        WordToolkitExtensionCapabilityDescriptor value
    )
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateIdentifier(value.CapabilityId, nameof(value.CapabilityId));
        ValidateIdentifier(value.InterfaceContract, nameof(value.InterfaceContract));
        _ = ParseContractVersion(
            value.InterfaceVersion,
            nameof(value.InterfaceVersion)
        );
        ValidateLimits(value.ResourceLimits, nameof(value.ResourceLimits));
        const WordToolkitExtensionPermission all =
            WordToolkitExtensionPermission.ReadPackage
            | WordToolkitExtensionPermission.ReadDocumentContent
            | WordToolkitExtensionPermission.MutatePackage
            | WordToolkitExtensionPermission.ReadSensitiveMetadata
            | WordToolkitExtensionPermission.FilesystemRead
            | WordToolkitExtensionPermission.FilesystemWrite
            | WordToolkitExtensionPermission.Network
            | WordToolkitExtensionPermission.SpawnProcess
            | WordToolkitExtensionPermission.LiveWord
            | WordToolkitExtensionPermission.Credentials;
        if ((value.Permissions & ~all) != 0)
        {
            throw Invalid("Capability permissions contain undefined bits.");
        }
        if (
            value.ReturnsDocumentContent
            && !value.Permissions.HasFlag(
                WordToolkitExtensionPermission.ReadDocumentContent
            )
        )
        {
            throw Invalid(
                "A capability that returns document content must request read_document_content permission."
            );
        }
    }

    internal static void ValidateInterfaceSupport(
        WordToolkitExtensionInterfaceSupport value
    )
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateIdentifier(value.InterfaceContract, nameof(value.InterfaceContract));
        _ = ParseContractVersion(value.MaximumVersion, nameof(value.MaximumVersion));
    }

    internal static void ValidateLimits(
        WordToolkitExtensionResourceLimits value,
        string name
    )
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (
            value.MaxInputBytes < 1
            || value.MaxOutputBytes < 1
            || value.MaxConcurrentInvocations < 1
            || value.TimeoutMilliseconds < 1
            || value.MaxProcessMemoryBytes is <= 0
        )
        {
            throw new ArgumentOutOfRangeException(
                name,
                "All extension resource limits must be positive."
            );
        }
    }

    internal static (int Major, int Minor) ParseContractVersion(
        string value,
        string name
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        var match = ContractVersionPattern().Match(value);
        if (
            !match.Success
            || !int.TryParse(match.Groups[1].Value, out var major)
            || !int.TryParse(match.Groups[2].Value, out var minor)
        )
        {
            throw Invalid($"{name} must use canonical major.minor form.");
        }
        return (major, minor);
    }

    private static void ValidateIdentifier(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (!IdentifierPattern().IsMatch(value))
        {
            throw Invalid(
                $"{name} must be a lowercase stable identifier of at most 128 characters."
            );
        }
    }

    private static void ValidateBoundedText(string value, int maximum, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length > maximum || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw Invalid($"{name} must be trimmed and at most {maximum} characters.");
        }
    }

    private static WordToolkitExtensionException Invalid(string message) => new(
        "EXTENSION_REGISTRATION_INVALID",
        message
    );
}
