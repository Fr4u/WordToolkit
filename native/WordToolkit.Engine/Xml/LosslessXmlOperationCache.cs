using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using WordToolkit.Engine.Resources;

namespace WordToolkit.Engine.Xml;

internal static class LosslessXmlOperationCache
{
    private static readonly ConditionalWeakTable<WordOperationResourceLease, Cache> Caches = new();

    public static LosslessXmlDocument GetOrParse(
        ReadOnlyMemory<byte> source,
        LosslessXmlOptions? options,
        WordOperationResourceLease resourceLease,
        WordOperationResourceStage stage,
        CancellationToken cancellationToken
    )
    {
        options ??= LosslessXmlOptions.Default;
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (source.IsEmpty)
        {
            throw new LosslessXmlParseException("XML source is empty.");
        }
        if (source.Length > options.MaxSourceBytes)
        {
            throw new LosslessXmlLimitException(
                $"XML source exceeds {options.MaxSourceBytes} bytes."
            );
        }
        return Caches.GetValue(resourceLease, static _ => new Cache()).GetOrParse(
            source,
            options,
            resourceLease,
            stage,
            cancellationToken
        );
    }

    private sealed class Cache
    {
        private readonly object _gate = new();
        private readonly Dictionary<ArrayCacheKey, LosslessXmlDocument> _arrayDocuments = [];
        private readonly Dictionary<CacheKey, List<LosslessXmlDocument>> _documents = [];

        public LosslessXmlDocument GetOrParse(
            ReadOnlyMemory<byte> source,
            LosslessXmlOptions options,
            WordOperationResourceLease resourceLease,
            WordOperationResourceStage stage,
            CancellationToken cancellationToken
        )
        {
            ArrayCacheKey? arrayKey = null;
            if (
                MemoryMarshal.TryGetArray(source, out var segment)
                && segment.Array is not null
            )
            {
                arrayKey = new ArrayCacheKey(
                    segment.Array,
                    segment.Offset,
                    segment.Count
                );
            }

            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    arrayKey is { } exactArrayKey
                    && _arrayDocuments.TryGetValue(exactArrayKey, out var arrayCandidate)
                )
                {
                    if (arrayCandidate.SourceBytes.Span.SequenceEqual(source.Span))
                    {
                        arrayCandidate.EnsureWithinLimits(options);
                        resourceLease.RecordXmlParseCacheResult(
                            cacheHit: true,
                            source.Length
                        );
                        return arrayCandidate.WithOptions(options);
                    }

                    // ReadOnlyMemory<byte> may wrap a mutable array. Never let the
                    // identity fast path bypass byte-exact content verification.
                    _arrayDocuments.Remove(exactArrayKey);
                }

                var key = new CacheKey(
                    source.Length,
                    Convert.ToHexString(SHA256.HashData(source.Span))
                );
                if (_documents.TryGetValue(key, out var candidates))
                {
                    foreach (var candidate in candidates)
                    {
                        if (!candidate.SourceBytes.Span.SequenceEqual(source.Span))
                        {
                            continue;
                        }
                        candidate.EnsureWithinLimits(options);
                        resourceLease.RecordXmlParseCacheResult(
                            cacheHit: true,
                            source.Length
                        );
                        if (arrayKey is { } matchedArrayKey)
                        {
                            _arrayDocuments[matchedArrayKey] = candidate;
                        }
                        return candidate.WithOptions(options);
                    }
                }

                var parsed = LosslessXmlDocument.ParseUncached(
                    source,
                    options,
                    resourceLease,
                    stage,
                    cancellationToken
                );
                candidates ??= [];
                candidates.Add(parsed);
                _documents[key] = candidates;
                if (arrayKey is { } parsedArrayKey)
                {
                    _arrayDocuments[parsedArrayKey] = parsed;
                }
                resourceLease.RecordXmlParseCacheResult(
                    cacheHit: false,
                    source.Length
                );
                return parsed;
            }
        }
    }

    private readonly record struct ArrayCacheKey(byte[] Array, int Offset, int Count);

    private readonly record struct CacheKey(int SourceBytes, string Sha256);
}
