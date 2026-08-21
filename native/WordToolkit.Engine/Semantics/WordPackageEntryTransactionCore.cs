using System.Collections.ObjectModel;
using System.Security.Cryptography;
using WordToolkit.Engine.Packaging;

namespace WordToolkit.Engine.Semantics;

internal enum WordPackageEntryChangeKind
{
    Add,
    Replace,
    Delete,
}

internal sealed class WordPackageEntryPayload
{
    internal WordPackageEntryPayload(
        string entryName,
        string? partUri,
        byte[]? beforeContent,
        byte[]? afterContent
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);
        if (beforeContent is null && afterContent is null)
        {
            throw new ArgumentException(
                "An entry transaction must contain a before or after payload."
            );
        }

        EntryName = entryName;
        PartUri = partUri;
        BeforeContent = beforeContent?.ToArray();
        AfterContent = afterContent?.ToArray();
        BeforeSha256 = BeforeContent is null ? null : HashBytes(BeforeContent);
        AfterSha256 = AfterContent is null ? null : HashBytes(AfterContent);
        Kind = beforeContent is null
            ? WordPackageEntryChangeKind.Add
            : afterContent is null
                ? WordPackageEntryChangeKind.Delete
                : WordPackageEntryChangeKind.Replace;
    }

    internal string EntryName { get; }

    internal string? PartUri { get; }

    internal byte[]? BeforeContent { get; }

    internal byte[]? AfterContent { get; }

    internal string? BeforeSha256 { get; }

    internal string? AfterSha256 { get; }

    internal WordPackageEntryChangeKind Kind { get; }

    private static string HashBytes(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}

internal sealed class WordPackageEntryTransactionCore
{
    private readonly IReadOnlyDictionary<string, WordPackageEntryPayload> _entries;

    internal WordPackageEntryTransactionCore(
        string basePackageFingerprint,
        string resultPackageFingerprint,
        IReadOnlyDictionary<string, WordPackageEntryPayload> entries
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePackageFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultPackageFingerprint);
        ArgumentNullException.ThrowIfNull(entries);

        BasePackageFingerprint = basePackageFingerprint;
        ResultPackageFingerprint = resultPackageFingerprint;
        _entries = new ReadOnlyDictionary<string, WordPackageEntryPayload>(
            new Dictionary<string, WordPackageEntryPayload>(entries, StringComparer.Ordinal)
        );
    }

    internal string BasePackageFingerprint { get; }

    internal string ResultPackageFingerprint { get; }

    internal IEnumerable<WordPackageEntryPayload> Entries => _entries.Values;

    internal bool HasChanges => _entries.Count != 0;

    internal OpcPackageMutationBuilder CreateMutation(OpcPackageSnapshot currentSnapshot)
    {
        ArgumentNullException.ThrowIfNull(currentSnapshot);
        VerifyFingerprint(
            currentSnapshot,
            BasePackageFingerprint,
            "Package changed after the entry transaction was planned."
        );
        return BuildMutation(currentSnapshot, inverse: false);
    }

    internal OpcPackageMutationBuilder CreateInverseMutation(
        OpcPackageSnapshot appliedSnapshot
    )
    {
        ArgumentNullException.ThrowIfNull(appliedSnapshot);
        VerifyFingerprint(
            appliedSnapshot,
            ResultPackageFingerprint,
            "Applied package changed before the inverse entry transaction was created."
        );
        return BuildMutation(appliedSnapshot, inverse: true);
    }

    private OpcPackageMutationBuilder BuildMutation(
        OpcPackageSnapshot snapshot,
        bool inverse
    )
    {
        var mutation = new OpcPackageMutationBuilder(snapshot);
        foreach (var payload in _entries.Values.OrderBy(
            item => item.EntryName,
            StringComparer.Ordinal
        ))
        {
            var before = inverse ? payload.AfterContent : payload.BeforeContent;
            var after = inverse ? payload.BeforeContent : payload.AfterContent;
            var beforeHash = inverse ? payload.AfterSha256 : payload.BeforeSha256;
            if (before is null)
            {
                VerifyEntryAbsent(snapshot, payload.EntryName);
                mutation.AddEntry(payload.EntryName, after!);
                continue;
            }
            if (after is null)
            {
                VerifyEntryHash(snapshot, payload.EntryName, beforeHash!);
                mutation.DeleteEntry(payload.EntryName, beforeHash);
                continue;
            }

            VerifyEntryHash(snapshot, payload.EntryName, beforeHash!);
            mutation.ReplaceEntry(payload.EntryName, after, beforeHash);
        }
        return mutation;
    }

    private static void VerifyFingerprint(
        OpcPackageSnapshot snapshot,
        string expected,
        string message
    )
    {
        if (!string.Equals(snapshot.Fingerprint, expected, StringComparison.Ordinal))
        {
            throw new WordSemanticPreconditionException(message);
        }
    }

    private static void VerifyEntryAbsent(
        OpcPackageSnapshot snapshot,
        string entryName
    )
    {
        if (snapshot.Entries.Any(entry => string.Equals(
            entry.Name,
            entryName,
            StringComparison.Ordinal
        )))
        {
            throw new WordSemanticPreconditionException(
                $"Package entry '{entryName}' appeared before the transaction could be applied."
            );
        }
    }

    private static void VerifyEntryHash(
        OpcPackageSnapshot snapshot,
        string entryName,
        string expectedSha256
    )
    {
        var entry = snapshot.Entries.SingleOrDefault(item => string.Equals(
            item.Name,
            entryName,
            StringComparison.Ordinal
        ));
        if (entry is null || !string.Equals(
            entry.Sha256,
            expectedSha256,
            StringComparison.OrdinalIgnoreCase
        ))
        {
            throw new WordSemanticPreconditionException(
                $"Package entry '{entryName}' changed before the transaction could be applied."
            );
        }
    }
}
