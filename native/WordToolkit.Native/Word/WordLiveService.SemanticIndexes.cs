using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private Task<object> ManagePackageSemanticIndexAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        var operation = arguments.String("operation");
        return operation switch
        {
            "create" => Task.FromResult(
                CreateSemanticIndex(arguments, started, cancellationToken)
            ),
            "inspect" => Task.FromResult(
                InspectSemanticIndex(arguments, started, cancellationToken)
            ),
            "list" => Task.FromResult(
                ListSemanticIndexes(arguments, started, cancellationToken)
            ),
            "release" => Task.FromResult(
                ReleaseSemanticIndex(arguments, started, cancellationToken)
            ),
            _ => throw new NativeToolException(
                "INVALID_INPUT",
                "operation must be create, inspect, list, or release"
            ),
        };
    }

    private object CreateSemanticIndex(
        JsonElement arguments,
        long started,
        CancellationToken cancellationToken
    )
    {
        RejectPresent(arguments, "semantic_index_id", "create");
        var path = ResolveInspectablePackagePath(arguments);
        var ttlSeconds = arguments.NullableInt64("ttl_seconds") ?? 300;
        if (ttlSeconds is < 30 or > 1_800)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "ttl_seconds must be between 30 and 1800"
            );
        }

        try
        {
            var package = new OpcPackageReader().Read(path, cancellationToken);
            if (arguments.TryGetProperty("expected_package_fingerprint", out _))
            {
                var expected = RequiredSha256(
                    arguments,
                    "expected_package_fingerprint"
                );
                if (!string.Equals(
                        package.Fingerprint,
                        expected,
                        StringComparison.OrdinalIgnoreCase
                    ))
                {
                    throw new NativeToolException(
                        "VERSION_CONFLICT",
                        "The package does not match expected_package_fingerprint"
                    );
                }
            }

            var now = DateTimeOffset.UtcNow;
            var expiresAt = now.AddSeconds(ttlSeconds);
            var normalizedPath = NormalizePath(path);
            lock (_semanticIndexGate)
            {
                RemoveExpiredSemanticIndexes(now);
                var cached = FindMatchingSemanticIndex(
                    normalizedPath,
                    package.Fingerprint
                );
                if (cached is not null)
                {
                    var refreshed = cached with { ExpiresAtUtc = expiresAt };
                    _semanticIndexes[refreshed.Id] = refreshed;
                    return SemanticIndexResponse(
                        "create",
                        refreshed,
                        started,
                        cacheHit: true,
                        released: null
                    );
                }
            }

            var document = new WordSemanticProjector().Project(
                package,
                cancellationToken
            );
            var index = WordSemanticIndex.Build(
                document,
                new WordSemanticIndexOptions
                {
                    MaxNodeCount = MaxSemanticIndexNodesPerEntry,
                    MaxPropertyOccurrences = 1_000_000,
                },
                cancellationToken
            );
            now = DateTimeOffset.UtcNow;
            expiresAt = now.AddSeconds(ttlSeconds);
            CachedSemanticIndex entry;
            var cacheHit = false;
            lock (_semanticIndexGate)
            {
                RemoveExpiredSemanticIndexes(now);
                var existing = FindMatchingSemanticIndex(
                    normalizedPath,
                    index.PackageFingerprint
                );
                if (existing is not null)
                {
                    entry = existing with { ExpiresAtUtc = expiresAt };
                    _semanticIndexes[entry.Id] = entry;
                    cacheHit = true;
                }
                else
                {
                    var cachedNodes = _semanticIndexes.Values.Sum(item => item.Index.NodeCount);
                    if (
                        _semanticIndexes.Count >= MaxSemanticIndexEntries
                        || cachedNodes + index.NodeCount > MaxSemanticIndexCachedNodes
                    )
                    {
                        throw new NativeToolException(
                            "INDEX_CACHE_FULL",
                            "The bounded semantic-index cache is full; release an index or wait for expiry",
                            new
                            {
                                entry_count = _semanticIndexes.Count,
                                max_entry_count = MaxSemanticIndexEntries,
                                cached_node_count = cachedNodes,
                                max_cached_node_count = MaxSemanticIndexCachedNodes,
                            }
                        );
                    }

                    CachedSemanticIndex? created = null;
                    for (var attempt = 0; attempt < 4; attempt++)
                    {
                        var candidate = new CachedSemanticIndex(
                            CreateSemanticIndexId(),
                            normalizedPath,
                            Path.GetFileName(path),
                            index,
                            now,
                            expiresAt
                        );
                        if (_semanticIndexes.TryAdd(candidate.Id, candidate))
                        {
                            created = candidate;
                            break;
                        }
                    }
                    entry = created
                        ?? throw new NativeToolException(
                            "INTERNAL_ERROR",
                            "Could not allocate a semantic index handle"
                        );
                }
            }

            return SemanticIndexResponse(
                "create",
                entry,
                started,
                cacheHit,
                released: null
            );
        }
        catch (WordSemanticIndexLimitException exception)
        {
            throw new NativeToolException(
                "INDEX_LIMIT",
                "Semantic index exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordSemanticLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "Semantic projection exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordSemanticProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be projected as a Word semantic document",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (OpcPackageLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The package exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (InvalidDataException exception)
        {
            throw new NativeToolException(
                "INVALID_PACKAGE",
                "The file is not a readable OPC ZIP package",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (UnauthorizedAccessException)
        {
            throw new NativeToolException(
                "ACCESS_DENIED",
                "The Word package cannot be read with current permissions"
            );
        }
        catch (IOException exception)
        {
            throw new NativeToolException(
                "IO_ERROR",
                "The Word package could not be read",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
    }

    private object InspectSemanticIndex(
        JsonElement arguments,
        long started,
        CancellationToken cancellationToken
    )
    {
        RejectPresent(arguments, "local_path", "inspect");
        RejectPresent(arguments, "ttl_seconds", "inspect");
        cancellationToken.ThrowIfCancellationRequested();
        var entry = GetSemanticIndex(RequiredSemanticIndexId(arguments));
        ValidateExpectedIndexFingerprint(arguments, entry);
        return SemanticIndexResponse(
            "inspect",
            entry,
            started,
            cacheHit: true,
            released: null
        );
    }

    private object ListSemanticIndexes(
        JsonElement arguments,
        long started,
        CancellationToken cancellationToken
    )
    {
        RejectPresent(arguments, "local_path", "list");
        RejectPresent(arguments, "semantic_index_id", "list");
        RejectPresent(arguments, "expected_package_fingerprint", "list");
        RejectPresent(arguments, "ttl_seconds", "list");
        cancellationToken.ThrowIfCancellationRequested();
        CachedSemanticIndex[] entries;
        lock (_semanticIndexGate)
        {
            RemoveExpiredSemanticIndexes(DateTimeOffset.UtcNow);
            entries = _semanticIndexes.Values
                .OrderBy(entry => entry.CreatedAtUtc)
                .ToArray();
        }

        return new
        {
            operation = "list",
            entry_count = entries.Length,
            max_entry_count = MaxSemanticIndexEntries,
            cached_node_count = entries.Sum(entry => entry.Index.NodeCount),
            max_cached_node_count = MaxSemanticIndexCachedNodes,
            indexes = entries.Select(entry => new
            {
                semantic_index_id = entry.Id,
                package_fingerprint = entry.Index.PackageFingerprint,
                semantic_index_fingerprint = entry.Index.IndexFingerprint,
                semantic_node_count = entry.Index.NodeCount,
                expires_at_utc = entry.ExpiresAtUtc,
            }).ToArray(),
            persistence = "process_memory_only",
            raw_text_returned = false,
            word_opened = false,
            runtime = "dotnet-native",
            python_used = false,
            performance = new
            {
                total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            },
        };
    }

    private object ReleaseSemanticIndex(
        JsonElement arguments,
        long started,
        CancellationToken cancellationToken
    )
    {
        RejectPresent(arguments, "local_path", "release");
        RejectPresent(arguments, "ttl_seconds", "release");
        cancellationToken.ThrowIfCancellationRequested();
        var id = RequiredSemanticIndexId(arguments);
        CachedSemanticIndex entry;
        lock (_semanticIndexGate)
        {
            RemoveExpiredSemanticIndexes(DateTimeOffset.UtcNow);
            if (!_semanticIndexes.TryGetValue(id, out entry!))
            {
                throw SemanticIndexNotFound();
            }
            ValidateExpectedIndexFingerprint(arguments, entry);
            if (!_semanticIndexes.TryRemove(id, out _))
            {
                throw SemanticIndexNotFound();
            }
        }

        return SemanticIndexResponse(
            "release",
            entry,
            started,
            cacheHit: true,
            released: true
        );
    }

    private CachedSemanticIndex GetSemanticIndex(string id)
    {
        lock (_semanticIndexGate)
        {
            RemoveExpiredSemanticIndexes(DateTimeOffset.UtcNow);
            if (_semanticIndexes.TryGetValue(id, out var entry))
            {
                return entry;
            }
        }
        throw SemanticIndexNotFound();
    }

    private void RemoveExpiredSemanticIndexes(DateTimeOffset now)
    {
        foreach (var entry in _semanticIndexes.Values)
        {
            if (entry.ExpiresAtUtc <= now)
            {
                _semanticIndexes.TryRemove(entry.Id, out _);
            }
        }
    }

    private CachedSemanticIndex? FindMatchingSemanticIndex(
        string normalizedPath,
        string packageFingerprint
    ) => _semanticIndexes.Values.FirstOrDefault(candidate =>
        string.Equals(
            candidate.NormalizedSourcePath,
            normalizedPath,
            StringComparison.OrdinalIgnoreCase
        )
        && string.Equals(
            candidate.Index.PackageFingerprint,
            packageFingerprint,
            StringComparison.Ordinal
        )
    );

    private static object SemanticIndexResponse(
        string operation,
        CachedSemanticIndex entry,
        long started,
        bool cacheHit,
        bool? released
    )
    {
        var index = entry.Index;
        return new
        {
            operation,
            semantic_index_id = entry.Id,
            file_name = entry.FileName,
            package_fingerprint = index.PackageFingerprint,
            semantic_index_fingerprint = index.IndexFingerprint,
            semantic_node_count = index.NodeCount,
            indexed_part_count = index.PartCounts.Count,
            indexed_property_occurrence_count = index.PropertyOccurrenceCount,
            distinct_property_value_count = index.DistinctPropertyValueCount,
            indexed_property_names = index.IndexedPropertyNames,
            node_counts = index.KindCounts
                .OrderBy(item => item.Key)
                .ToDictionary(
                    item => ToSnakeCase(item.Key.ToString()),
                    item => item.Value,
                    StringComparer.Ordinal
                ),
            created_at_utc = entry.CreatedAtUtc,
            expires_at_utc = entry.ExpiresAtUtc,
            cache_hit = cacheHit,
            released,
            persistence = "process_memory_only",
            raw_text_returned = false,
            word_opened = false,
            runtime = "dotnet-native",
            python_used = false,
            performance = new
            {
                total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            },
        };
    }

    private static void ValidateExpectedIndexFingerprint(
        JsonElement arguments,
        CachedSemanticIndex entry
    )
    {
        if (!arguments.TryGetProperty("expected_package_fingerprint", out _))
        {
            return;
        }

        var expected = RequiredSha256(arguments, "expected_package_fingerprint");
        if (!string.Equals(
                expected,
                entry.Index.PackageFingerprint,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "The semantic index does not match expected_package_fingerprint"
            );
        }
    }

    private static string RequiredSemanticIndexId(JsonElement arguments)
    {
        _ = arguments.Required("semantic_index_id");
        var id = arguments.String("semantic_index_id");
        if (
            id.Length != 36
            || !id.StartsWith("wsi_", StringComparison.Ordinal)
            || id[4..].Any(character =>
                character is not (>= '0' and <= '9')
                && character is not (>= 'a' and <= 'f')
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "semantic_index_id must be a lowercase wsi_ handle"
            );
        }
        return id;
    }

    private static string CreateSemanticIndexId() =>
        "wsi_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static NativeToolException SemanticIndexNotFound() => new(
        "INDEX_NOT_FOUND",
        "The semantic index does not exist or has expired"
    );

    private static void RejectPresent(
        JsonElement arguments,
        string name,
        string operation
    )
    {
        if (arguments.TryGetProperty(name, out _))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} is not valid for semantic-index operation '{operation}'"
            );
        }
    }

    private sealed record CachedSemanticIndex(
        string Id,
        string NormalizedSourcePath,
        string FileName,
        WordSemanticIndex Index,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset ExpiresAtUtc
    );
}
