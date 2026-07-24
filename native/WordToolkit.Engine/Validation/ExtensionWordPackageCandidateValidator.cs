using System.Text.Json;
using WordToolkit.Engine.Extensions;

namespace WordToolkit.Engine.Validation;

/// <summary>
/// Routes candidate validation through the extension registry so the validator is subject
/// to the same allowlist, contract, concurrency, input, output and cooperative timeout policy.
/// </summary>
public sealed class ExtensionWordPackageCandidateValidator
    : IWordPackageCandidateValidator
{
    private readonly WordToolkitExtensionRegistry _registry;
    private readonly string _capabilityId;

    public ExtensionWordPackageCandidateValidator(
        WordToolkitExtensionRegistry registry,
        string capabilityId
    )
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityId);
        _registry = registry;
        _capabilityId = capabilityId;
    }

    public WordPackageCandidateValidationReport Validate(
        Stream baselinePackage,
        Stream candidatePackage,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(baselinePackage);
        ArgumentNullException.ThrowIfNull(candidatePackage);
        long inputBytes;
        try
        {
            inputBytes = checked(baselinePackage.Length + candidatePackage.Length);
        }
        catch (Exception exception) when (
            exception is NotSupportedException or OverflowException
        )
        {
            throw new ArgumentException(
                "Extension validation streams must expose bounded lengths.",
                nameof(baselinePackage),
                exception
            );
        }

        return _registry.Invoke<
            IWordPackageCandidateValidator,
            WordPackageCandidateValidationReport
        >(
            _capabilityId,
            inputBytes,
            (validator, token) => validator.Validate(
                baselinePackage,
                candidatePackage,
                token
            ),
            report => JsonSerializer.SerializeToUtf8Bytes(report).LongLength,
            cancellationToken
        );
    }
}
