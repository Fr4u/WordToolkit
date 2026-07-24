using WordToolkit.Engine.Extensions;

namespace WordToolkit.Engine.Operations;

public static class InspectExtensionCatalogContract
{
    public const string OperationName = "inspect_wordtoolkit_extensions";
    public const string Contract = "wordtoolkit.inspect_extensions/1.0";
    public const int DefaultPageSize = 12;
    public const int MaximumPageSize = 32;
    public const int MaximumQueryCharacters = 128;
}

public sealed record InspectExtensionCatalogRequest(
    string? Query = null,
    int Offset = 0,
    int Limit = InspectExtensionCatalogContract.DefaultPageSize
);

public sealed record ExtensionCatalogPaging(
    int Offset,
    int Limit,
    int Returned,
    int? NextOffset
);

public sealed record ExtensionCatalogSecuritySummary(
    bool ReadsDocument,
    bool ReturnsDocumentContent,
    bool ReturnsImplementationTypes,
    bool ReturnsAssemblyPaths,
    bool LoadsAssemblies,
    bool OpensWord,
    bool UsesNetwork
);

public sealed record ExtensionCatalogCapabilityItem(
    string ExtensionId,
    string DisplayName,
    string Publisher,
    string ExtensionVersion,
    WordToolkitExtensionTrust Trust,
    WordToolkitExtensionIsolation Isolation,
    string CapabilityId,
    WordToolkitExtensionKind Kind,
    string InterfaceContract,
    string InterfaceVersion,
    IReadOnlyList<string> Permissions,
    WordToolkitExtensionResourceLimits ResourceLimits,
    WordToolkitExtensionTimeoutEnforcement TimeoutEnforcement,
    bool Deterministic,
    bool Idempotent,
    bool CapabilityReturnsDocumentContent
);

public sealed record InspectExtensionCatalogResult(
    string OperationContract,
    string RegistryContract,
    string CatalogSha256,
    int ExtensionCount,
    int CapabilityCount,
    int MatchedCapabilityCount,
    IReadOnlyDictionary<string, int> CountsByKind,
    IReadOnlyList<ExtensionCatalogCapabilityItem> Items,
    ExtensionCatalogPaging Paging,
    ExtensionCatalogSecuritySummary Security
);

/// <summary>
/// Returns a bounded, content-free view of explicitly registered engine extensions.
/// The operation neither discovers nor loads assemblies and exposes no implementation type.
/// </summary>
public sealed class InspectExtensionCatalogOperation
{
    private readonly WordToolkitExtensionRegistry _registry;

    public InspectExtensionCatalogOperation(WordToolkitExtensionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public InspectExtensionCatalogResult Execute(
        InspectExtensionCatalogRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var query = string.IsNullOrWhiteSpace(request.Query)
            ? null
            : request.Query.Trim();
        if (query?.Length > InspectExtensionCatalogContract.MaximumQueryCharacters)
        {
            throw InvalidInput(
                $"query must not exceed {InspectExtensionCatalogContract.MaximumQueryCharacters} characters"
            );
        }
        if (request.Offset < 0)
        {
            throw InvalidInput("offset must be zero or greater");
        }
        if (
            request.Limit < 1
            || request.Limit > InspectExtensionCatalogContract.MaximumPageSize
        )
        {
            throw InvalidInput(
                $"limit must be between 1 and {InspectExtensionCatalogContract.MaximumPageSize}"
            );
        }

        var all = _registry.Extensions
            .SelectMany(extension => extension.Capabilities.Select(capability =>
                CreateItem(extension.Extension, capability)
            ))
            .OrderBy(item => item.CapabilityId, StringComparer.Ordinal)
            .ToArray();
        var filtered = query is null
            ? all
            : all.Where(item => Matches(item, query)).ToArray();
        var items = filtered.Skip(request.Offset).Take(request.Limit).ToArray();
        var nextOffset = request.Offset + items.Length < filtered.Length
            ? request.Offset + items.Length
            : (int?)null;
        var counts = all
            .GroupBy(item => SnakeCase(item.Kind.ToString()), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal
            );

        return new InspectExtensionCatalogResult(
            InspectExtensionCatalogContract.Contract,
            WordToolkitExtensionRegistry.RegistryContract,
            _registry.CatalogSha256,
            _registry.Extensions.Count,
            all.Length,
            filtered.Length,
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(counts),
            Array.AsReadOnly(items),
            new ExtensionCatalogPaging(
                request.Offset,
                request.Limit,
                items.Length,
                nextOffset
            ),
            new ExtensionCatalogSecuritySummary(
                ReadsDocument: false,
                ReturnsDocumentContent: false,
                ReturnsImplementationTypes: false,
                ReturnsAssemblyPaths: false,
                LoadsAssemblies: false,
                OpensWord: false,
                UsesNetwork: false
            )
        );
    }

    private static ExtensionCatalogCapabilityItem CreateItem(
        WordToolkitExtensionDescriptor extension,
        WordToolkitExtensionCapabilityDescriptor capability
    ) => new(
        extension.ExtensionId,
        extension.DisplayName,
        extension.Publisher,
        extension.ExtensionVersion,
        extension.Trust,
        extension.Isolation,
        capability.CapabilityId,
        capability.Kind,
        capability.InterfaceContract,
        capability.InterfaceVersion,
        PermissionNames(capability.Permissions),
        capability.ResourceLimits,
        capability.TimeoutEnforcement,
        capability.Deterministic,
        capability.Idempotent,
        capability.ReturnsDocumentContent
    );

    private static bool Matches(ExtensionCatalogCapabilityItem item, string query) =>
        item.ExtensionId.Contains(query, StringComparison.OrdinalIgnoreCase)
        || item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || item.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase)
        || item.CapabilityId.Contains(query, StringComparison.OrdinalIgnoreCase)
        || item.InterfaceContract.Contains(query, StringComparison.OrdinalIgnoreCase)
        || item.Kind.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> PermissionNames(
        WordToolkitExtensionPermission permissions
    )
    {
        if (permissions == WordToolkitExtensionPermission.None)
        {
            return [];
        }
        return Array.AsReadOnly(Enum.GetValues<WordToolkitExtensionPermission>()
            .Where(value =>
                value != WordToolkitExtensionPermission.None
                && permissions.HasFlag(value)
            )
            .Select(value => SnakeCase(value.ToString()))
            .Order(StringComparer.Ordinal)
            .ToArray());
    }

    private static string SnakeCase(string value)
    {
        var result = new System.Text.StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (
                char.IsUpper(character)
                && index > 0
                && (
                    char.IsLower(value[index - 1])
                    || index + 1 < value.Length && char.IsLower(value[index + 1])
                )
            )
            {
                result.Append('_');
            }
            result.Append(char.ToLowerInvariant(character));
        }
        return result.ToString();
    }

    private static WordToolkitOperationException InvalidInput(string message) => new(
        "INVALID_INPUT",
        message
    );
}
