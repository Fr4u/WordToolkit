using System.Collections.Frozen;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WordToolkit.Engine.Extensions;

public sealed record WordToolkitRegisteredExtension(
    WordToolkitExtensionDescriptor Extension,
    IReadOnlyList<WordToolkitExtensionCapabilityDescriptor> Capabilities
);

public sealed class WordToolkitExtensionRegistryBuilder
{
    private readonly WordToolkitExtensionPolicy _policy;
    private readonly Dictionary<string, WordToolkitExtensionDescriptor> _extensions =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Registration> _registrations =
        new(StringComparer.Ordinal);
    private bool _built;

    public WordToolkitExtensionRegistryBuilder(WordToolkitExtensionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policy = policy;
    }

    public WordToolkitExtensionRegistryBuilder Register<TCapability>(
        WordToolkitExtensionDescriptor extension,
        WordToolkitExtensionCapabilityDescriptor capability,
        TCapability implementation
    )
        where TCapability : class
    {
        if (_built)
        {
            throw Invalid("The extension registry builder is already frozen.");
        }
        ArgumentNullException.ThrowIfNull(extension);
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(implementation);
        if (!typeof(TCapability).IsInterface)
        {
            throw Invalid("Extension capability contracts must be interfaces.");
        }
        if (
            extension.Isolation == WordToolkitExtensionIsolation.OutOfProcess
            && implementation is not IWordToolkitProcessBoundaryProxy
        )
        {
            throw new WordToolkitExtensionException(
                "EXTENSION_ISOLATION_UNAVAILABLE",
                "An out-of-process extension requires an explicitly registered host-owned process-boundary proxy."
            );
        }
        if (
            extension.Isolation == WordToolkitExtensionIsolation.TrustedInProcess
            && implementation is IWordToolkitProcessBoundaryProxy
        )
        {
            throw Invalid(
                "A process-boundary proxy cannot be registered as trusted in-process code."
            );
        }

        WordToolkitExtensionValidation.ValidateDescriptor(extension);
        WordToolkitExtensionValidation.ValidateCapability(capability);
        _policy.Authorize(extension, capability);

        if (
            _extensions.TryGetValue(extension.ExtensionId, out var existing)
            && existing != extension
        )
        {
            throw Invalid(
                $"Extension '{extension.ExtensionId}' was registered with conflicting metadata."
            );
        }
        _extensions[extension.ExtensionId] = extension;
        if (
            !_registrations.TryAdd(
                capability.CapabilityId,
                new Registration(
                    extension.ExtensionId,
                    capability,
                    typeof(TCapability),
                    implementation
                )
            )
        )
        {
            throw Invalid(
                $"Capability '{capability.CapabilityId}' is already registered."
            );
        }
        return this;
    }

    public WordToolkitExtensionRegistry Build()
    {
        if (_built)
        {
            throw Invalid("The extension registry builder is already frozen.");
        }
        _built = true;
        return new WordToolkitExtensionRegistry(_extensions, _registrations);
    }

    private static WordToolkitExtensionException Invalid(string message) => new(
        "EXTENSION_REGISTRATION_INVALID",
        message
    );

    internal sealed class Registration
    {
        internal Registration(
            string extensionId,
            WordToolkitExtensionCapabilityDescriptor descriptor,
            Type serviceType,
            object implementation
        )
        {
            ExtensionId = extensionId;
            Descriptor = descriptor;
            ServiceType = serviceType;
            Implementation = implementation;
            Gate = new SemaphoreSlim(
                descriptor.ResourceLimits.MaxConcurrentInvocations,
                descriptor.ResourceLimits.MaxConcurrentInvocations
            );
        }

        internal string ExtensionId { get; }

        internal WordToolkitExtensionCapabilityDescriptor Descriptor { get; }

        internal Type ServiceType { get; }

        internal object Implementation { get; }

        internal SemaphoreSlim Gate { get; }
    }
}

public sealed class WordToolkitExtensionRegistry
{
    public const string RegistryContract = "wordtoolkit.extension-registry/1.0";

    private readonly FrozenDictionary<
        string,
        WordToolkitExtensionRegistryBuilder.Registration
    > _registrations;

    internal WordToolkitExtensionRegistry(
        IReadOnlyDictionary<string, WordToolkitExtensionDescriptor> extensions,
        IReadOnlyDictionary<
            string,
            WordToolkitExtensionRegistryBuilder.Registration
        > registrations
    )
    {
        _registrations = registrations.ToFrozenDictionary(StringComparer.Ordinal);
        Extensions = Array.AsReadOnly(extensions
            .Values.OrderBy(item => item.ExtensionId, StringComparer.Ordinal)
            .Select(extension => new WordToolkitRegisteredExtension(
                extension,
                Array.AsReadOnly(_registrations
                    .Values.Where(registration =>
                        registration.ExtensionId == extension.ExtensionId
                    )
                    .Select(registration => registration.Descriptor)
                    .OrderBy(item => item.CapabilityId, StringComparer.Ordinal)
                    .ToArray())
            ))
            .ToArray());
        CatalogSha256 = ComputeCatalogHash(Extensions);
    }

    public IReadOnlyList<WordToolkitRegisteredExtension> Extensions { get; }

    public string CatalogSha256 { get; }

    public TResult Invoke<TCapability, TResult>(
        string capabilityId,
        long inputBytes,
        Func<TCapability, CancellationToken, TResult> invocation,
        Func<TResult, long> measureOutputBytes,
        CancellationToken cancellationToken = default
    )
        where TCapability : class
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(measureOutputBytes);
        var registration = Resolve<TCapability>(capabilityId, inputBytes);
        cancellationToken.ThrowIfCancellationRequested();
        if (!registration.Gate.Wait(0, cancellationToken))
        {
            throw new WordToolkitExtensionException(
                "EXTENSION_BUSY",
                $"Capability '{capabilityId}' reached its concurrency limit.",
                retryable: true
            );
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            timeout.CancelAfter(registration.Descriptor.ResourceLimits.TimeoutMilliseconds);
            var started = Stopwatch.GetTimestamp();
            TResult result;
            try
            {
                result = invocation(
                    (TCapability)registration.Implementation,
                    timeout.Token
                );
            }
            catch (OperationCanceledException) when (
                timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested
            )
            {
                throw TimedOut(
                    capabilityId,
                    registration.Descriptor.TimeoutEnforcement
                );
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (WordToolkitExtensionException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new WordToolkitExtensionException(
                    "EXTENSION_EXECUTION_FAILED",
                    $"Capability '{capabilityId}' failed without publishing implementation details.",
                    innerException: exception
                );
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (
                timeout.IsCancellationRequested
                || Stopwatch.GetElapsedTime(started).TotalMilliseconds
                    > registration.Descriptor.ResourceLimits.TimeoutMilliseconds
            )
            {
                throw TimedOut(
                    capabilityId,
                    registration.Descriptor.TimeoutEnforcement
                );
            }
            long outputBytes;
            try
            {
                outputBytes = measureOutputBytes(result);
            }
            catch (Exception exception)
            {
                throw new WordToolkitExtensionException(
                    "EXTENSION_OUTPUT_MEASUREMENT_FAILED",
                    $"Capability '{capabilityId}' output could not be measured safely.",
                    innerException: exception
                );
            }
            if (
                outputBytes < 0
                || outputBytes > registration.Descriptor.ResourceLimits.MaxOutputBytes
            )
            {
                throw new WordToolkitExtensionException(
                    "EXTENSION_LIMIT_EXCEEDED",
                    $"Capability '{capabilityId}' exceeded its output limit."
                );
            }
            return result;
        }
        finally
        {
            registration.Gate.Release();
        }
    }

    private WordToolkitExtensionRegistryBuilder.Registration Resolve<TCapability>(
        string capabilityId,
        long inputBytes
    )
        where TCapability : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityId);
        if (inputBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputBytes));
        }
        if (!_registrations.TryGetValue(capabilityId, out var registration))
        {
            throw new WordToolkitExtensionException(
                "EXTENSION_NOT_FOUND",
                $"Capability '{capabilityId}' is not registered."
            );
        }
        if (registration.ServiceType != typeof(TCapability))
        {
            throw new WordToolkitExtensionException(
                "EXTENSION_CONTRACT_MISMATCH",
                $"Capability '{capabilityId}' was requested through the wrong interface."
            );
        }
        if (inputBytes > registration.Descriptor.ResourceLimits.MaxInputBytes)
        {
            throw new WordToolkitExtensionException(
                "EXTENSION_LIMIT_EXCEEDED",
                $"Capability '{capabilityId}' exceeded its input limit."
            );
        }
        return registration;
    }

    private static WordToolkitExtensionException TimedOut(
        string capabilityId,
        WordToolkitExtensionTimeoutEnforcement enforcement
    ) => new(
        "EXTENSION_TIMEOUT",
        $"Capability '{capabilityId}' exceeded its {TimeoutName(enforcement)} timeout.",
        retryable: true
    );

    private static string TimeoutName(
        WordToolkitExtensionTimeoutEnforcement enforcement
    ) => enforcement switch
    {
        WordToolkitExtensionTimeoutEnforcement.Cooperative => "cooperative",
        WordToolkitExtensionTimeoutEnforcement.ProcessBoundary => "process-boundary",
        _ => "declared",
    };

    private static string ComputeCatalogHash(
        IReadOnlyList<WordToolkitRegisteredExtension> extensions
    )
    {
        var canonical = new StringBuilder();
        Add(canonical, RegistryContract);
        foreach (var registered in extensions)
        {
            var extension = registered.Extension;
            Add(canonical, extension.ExtensionId);
            Add(canonical, extension.DisplayName);
            Add(canonical, extension.Publisher);
            Add(canonical, extension.ExtensionVersion);
            Add(canonical, extension.EngineContractVersion);
            Add(canonical, extension.Trust.ToString());
            Add(canonical, extension.Isolation.ToString());
            foreach (var capability in registered.Capabilities)
            {
                Add(canonical, capability.CapabilityId);
                Add(canonical, capability.Kind.ToString());
                Add(canonical, capability.InterfaceContract);
                Add(canonical, capability.InterfaceVersion);
                Add(
                    canonical,
                    ((int)capability.Permissions).ToString(
                        CultureInfo.InvariantCulture
                    )
                );
                Add(
                    canonical,
                    capability.ResourceLimits.MaxInputBytes.ToString(
                        CultureInfo.InvariantCulture
                    )
                );
                Add(
                    canonical,
                    capability.ResourceLimits.MaxOutputBytes.ToString(
                        CultureInfo.InvariantCulture
                    )
                );
                Add(
                    canonical,
                    capability.ResourceLimits.MaxConcurrentInvocations.ToString(
                        CultureInfo.InvariantCulture
                    )
                );
                Add(
                    canonical,
                    capability.ResourceLimits.TimeoutMilliseconds.ToString(
                        CultureInfo.InvariantCulture
                    )
                );
                Add(
                    canonical,
                    capability.ResourceLimits.MaxProcessMemoryBytes?.ToString(
                        CultureInfo.InvariantCulture
                    ) ?? ""
                );
                Add(canonical, capability.TimeoutEnforcement.ToString());
                Add(canonical, capability.Deterministic ? "1" : "0");
                Add(canonical, capability.Idempotent ? "1" : "0");
                Add(canonical, capability.ReturnsDocumentContent ? "1" : "0");
            }
        }
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))
            )
            .ToLowerInvariant();
    }

    private static void Add(StringBuilder target, string value)
    {
        target.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        target.Append(':');
        target.Append(value);
    }
}
