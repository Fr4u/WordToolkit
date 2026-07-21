using System.Collections.ObjectModel;
using System.Security.Cryptography;

namespace WordToolkit.Engine.Packaging;

public sealed class OpcPackageMutationBuilder
{
    private readonly OpcPackageSnapshot _snapshot;
    private readonly Dictionary<string, PendingChange> _changes = new(StringComparer.Ordinal);

    public OpcPackageMutationBuilder(OpcPackageSnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        var duplicate = snapshot.Entries
            .GroupBy(entry => entry.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Cannot mutate a package with duplicate entry '{duplicate.Key}'."
            );
        }
    }

    public OpcPackageSnapshot BaseSnapshot => _snapshot;

    public string BaseFingerprint => _snapshot.Fingerprint;

    public bool HasChanges => _changes.Count > 0;

    public IReadOnlyCollection<string> ChangedEntryNames => new ReadOnlyCollection<string>(
        _changes.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray()
    );

    public OpcPackageMutationBuilder ReplacePart(
        string partUri,
        ReadOnlyMemory<byte> content,
        string? expectedSha256 = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partUri);
        if (!_snapshot.Parts.TryGetValue(partUri, out var part))
        {
            throw new KeyNotFoundException($"Package part '{partUri}' does not exist.");
        }

        return ReplaceEntry(part.Entry.Name, content, expectedSha256);
    }

    public OpcPackageMutationBuilder ReplaceEntry(
        string entryName,
        ReadOnlyMemory<byte> content,
        string? expectedSha256 = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);
        var entry = FindBaseEntry(entryName);
        VerifyHashPrecondition(entry, expectedSha256);
        _changes[entryName] = PendingChange.Replace(content.ToArray());
        return this;
    }

    public OpcPackageMutationBuilder AddEntry(
        string entryName,
        ReadOnlyMemory<byte> content
    )
    {
        ValidateNewEntryName(entryName);
        if (FindBaseEntryOrDefault(entryName) is not null)
        {
            throw new InvalidOperationException($"Package entry '{entryName}' already exists.");
        }

        if (_changes.TryGetValue(entryName, out var existing) && !existing.IsDelete)
        {
            throw new InvalidOperationException(
                $"Package entry '{entryName}' is already being added."
            );
        }

        _changes[entryName] = PendingChange.Add(content.ToArray());
        return this;
    }

    public OpcPackageMutationBuilder DeleteEntry(
        string entryName,
        string? expectedSha256 = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);
        var entry = FindBaseEntryOrDefault(entryName);
        if (entry is null)
        {
            if (_changes.TryGetValue(entryName, out var pending) && pending.IsAdd)
            {
                _changes.Remove(entryName);
                return this;
            }

            throw new KeyNotFoundException($"Package entry '{entryName}' does not exist.");
        }

        VerifyHashPrecondition(entry, expectedSha256);
        _changes[entryName] = PendingChange.Delete();
        return this;
    }

    internal IReadOnlyList<OpcWritableEntry> Materialize(OpcSerializationMode mode)
    {
        var entries = new List<OpcWritableEntry>(_snapshot.Entries.Count + _changes.Count);
        foreach (var entry in _snapshot.Entries)
        {
            if (_changes.TryGetValue(entry.Name, out var change))
            {
                if (change.IsDelete)
                {
                    continue;
                }

                entries.Add(
                    new OpcWritableEntry(
                        entry.Name,
                        change.Content!,
                        entry.LastWriteTime,
                        entry.ExternalAttributes
                    )
                );
                continue;
            }

            entries.Add(
                new OpcWritableEntry(
                    entry.Name,
                    entry.Content.ToArray(),
                    entry.LastWriteTime,
                    entry.ExternalAttributes
                )
            );
        }

        foreach (
            var addition in _changes
                .Where(pair => pair.Value.IsAdd)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
        )
        {
            entries.Add(
                new OpcWritableEntry(
                    addition.Key,
                    addition.Value.Content!,
                    OpcPackageSerializer.DeterministicTimestamp,
                    0
                )
            );
        }

        if (mode == OpcSerializationMode.Deterministic)
        {
            return entries
                .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                .Select(entry => entry with
                {
                    LastWriteTime = OpcPackageSerializer.DeterministicTimestamp,
                    ExternalAttributes = 0,
                })
                .ToArray();
        }

        return entries;
    }

    private OpcPackageEntry FindBaseEntry(string entryName) =>
        FindBaseEntryOrDefault(entryName)
        ?? throw new KeyNotFoundException($"Package entry '{entryName}' does not exist.");

    private OpcPackageEntry? FindBaseEntryOrDefault(string entryName) =>
        _snapshot.Entries.SingleOrDefault(
            entry => string.Equals(entry.Name, entryName, StringComparison.Ordinal)
        );

    private static void VerifyHashPrecondition(
        OpcPackageEntry entry,
        string? expectedSha256
    )
    {
        if (expectedSha256 is null)
        {
            return;
        }

        if (
            !string.Equals(
                entry.Sha256,
                expectedSha256.Trim(),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new OpcPackagePreconditionException(
                $"Entry '{entry.Name}' changed: expected SHA-256 '{expectedSha256}', "
                    + $"actual '{entry.Sha256}'."
            );
        }
    }

    private static void ValidateNewEntryName(string entryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);
        if (!OpcPartUri.TryFromEntryName(entryName, out _, out var error))
        {
            throw new ArgumentException(
                error ?? "Entry name is not a valid OPC name.",
                nameof(entryName)
            );
        }
    }

    private sealed record PendingChange(byte[]? Content, ChangeKind Kind)
    {
        public bool IsAdd => Kind == ChangeKind.Add;

        public bool IsDelete => Kind == ChangeKind.Delete;

        public static PendingChange Add(byte[] content) => new(content, ChangeKind.Add);

        public static PendingChange Replace(byte[] content) =>
            new(content, ChangeKind.Replace);

        public static PendingChange Delete() => new(null, ChangeKind.Delete);
    }

    private enum ChangeKind
    {
        Add,
        Replace,
        Delete,
    }
}

public sealed class OpcPackagePreconditionException : InvalidOperationException
{
    public OpcPackagePreconditionException(string message)
        : base(message)
    {
    }
}

internal sealed record OpcWritableEntry(
    string Name,
    byte[] Content,
    DateTimeOffset LastWriteTime,
    int ExternalAttributes
);
