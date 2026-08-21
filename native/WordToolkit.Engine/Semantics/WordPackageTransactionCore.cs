using System.Collections.ObjectModel;
using System.Security.Cryptography;
using WordToolkit.Engine.Packaging;

namespace WordToolkit.Engine.Semantics;

internal sealed class WordPackagePartPayload
{
    internal WordPackagePartPayload(
        string partUri,
        string entryName,
        byte[] beforeContent,
        byte[] afterContent
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);
        ArgumentNullException.ThrowIfNull(beforeContent);
        ArgumentNullException.ThrowIfNull(afterContent);

        PartUri = partUri;
        EntryName = entryName;
        BeforeContent = beforeContent.ToArray();
        AfterContent = afterContent.ToArray();
        BeforeSha256 = HashBytes(BeforeContent);
        AfterSha256 = HashBytes(AfterContent);
    }

    internal string PartUri { get; }

    internal string EntryName { get; }

    internal byte[] BeforeContent { get; }

    internal byte[] AfterContent { get; }

    internal string BeforeSha256 { get; }

    internal string AfterSha256 { get; }

    private static string HashBytes(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}

internal sealed class WordPackageTransactionCore
{
    private readonly IReadOnlyDictionary<string, WordPackagePartPayload> _parts;

    internal WordPackageTransactionCore(
        string basePackageFingerprint,
        string resultPackageFingerprint,
        IReadOnlyDictionary<string, WordPackagePartPayload> parts
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePackageFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultPackageFingerprint);
        ArgumentNullException.ThrowIfNull(parts);

        BasePackageFingerprint = basePackageFingerprint;
        ResultPackageFingerprint = resultPackageFingerprint;
        _parts = new ReadOnlyDictionary<string, WordPackagePartPayload>(
            new Dictionary<string, WordPackagePartPayload>(parts, StringComparer.Ordinal)
        );
    }

    internal string BasePackageFingerprint { get; }

    internal string ResultPackageFingerprint { get; }

    internal IEnumerable<WordPackagePartPayload> Parts => _parts.Values;

    internal bool HasChanges => _parts.Count != 0;

    internal OpcPackageMutationBuilder CreateMutation(OpcPackageSnapshot currentSnapshot)
    {
        ArgumentNullException.ThrowIfNull(currentSnapshot);
        VerifyFingerprint(
            currentSnapshot,
            BasePackageFingerprint,
            "Package changed after the transaction was planned."
        );
        var mutation = new OpcPackageMutationBuilder(currentSnapshot);
        foreach (var part in _parts.Values.OrderBy(part => part.PartUri, StringComparer.Ordinal))
        {
            VerifyPartHash(currentSnapshot, part.PartUri, part.BeforeSha256);
            mutation.ReplacePart(part.PartUri, part.AfterContent, part.BeforeSha256);
        }

        return mutation;
    }

    internal OpcPackageMutationBuilder CreateInverseMutation(
        OpcPackageSnapshot appliedSnapshot
    )
    {
        ArgumentNullException.ThrowIfNull(appliedSnapshot);
        VerifyFingerprint(
            appliedSnapshot,
            ResultPackageFingerprint,
            "Applied package changed before the inverse transaction was created."
        );
        var mutation = new OpcPackageMutationBuilder(appliedSnapshot);
        foreach (var part in _parts.Values.OrderBy(part => part.PartUri, StringComparer.Ordinal))
        {
            VerifyPartHash(appliedSnapshot, part.PartUri, part.AfterSha256);
            mutation.ReplacePart(part.PartUri, part.BeforeContent, part.AfterSha256);
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

    private static void VerifyPartHash(
        OpcPackageSnapshot snapshot,
        string partUri,
        string expectedSha256
    )
    {
        if (
            !snapshot.Parts.TryGetValue(partUri, out var part)
            || !string.Equals(
                part.Entry.Sha256,
                expectedSha256,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new WordSemanticPreconditionException(
                $"Source part '{partUri}' changed before the transaction could be applied."
            );
        }
    }
}
