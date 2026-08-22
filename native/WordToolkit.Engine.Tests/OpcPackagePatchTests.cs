using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Packaging;

namespace WordToolkit.Engine.Tests;

public sealed class OpcPackagePatchTests
{
    [Fact]
    public void DefaultLimitsReflectMeasuredInMemoryRisk()
    {
        var limits = OpcPackagePatchLimits.Default;

        Assert.Equal(128L * 1024 * 1024, limits.MaxPayloadBytes);
        Assert.Equal(64L * 1024 * 1024, limits.MaxPayloadBytesPerBlob);
        Assert.Equal(4L * 1024 * 1024, limits.MaxManifestBytes);
        Assert.Equal(100, limits.MaxCompressionRatio);
    }

    [Fact]
    public void CreatesContentExactPatchAcrossAddReplaceAndDelete()
    {
        using var beforeStream = BuildPackage(new Dictionary<string, byte[]>
        {
            ["word/document.xml"] = Utf8(DocumentXml("before")),
            ["custom/change.bin"] = [1, 2, 3],
            ["custom/remove.bin"] = [4, 5, 6],
        });
        using var afterStream = BuildPackage(new Dictionary<string, byte[]>
        {
            ["word/document.xml"] = Utf8(DocumentXml("after")),
            ["custom/change.bin"] = [1, 2, 4],
            ["custom/add.bin"] = [7, 8, 9],
        });
        var before = Read(beforeStream);
        var after = Read(afterStream);

        var patch = new OpcPackagePatchBuilder().Create(before, after);
        var candidate = patch.MaterializeCandidate(before);

        Assert.StartsWith("wtpatch_", patch.PatchId, StringComparison.Ordinal);
        Assert.Equal(4, patch.OperationCount);
        Assert.Equal(1, patch.AddedEntryCount);
        Assert.Equal(2, patch.ReplacedEntryCount);
        Assert.Equal(1, patch.DeletedEntryCount);
        Assert.Equal(after.Fingerprint, patch.ResultPackageFingerprint);
        Assert.Equal(after.Fingerprint, candidate.Fingerprint);
        AssertEntryContentEqual(after, candidate);
    }

    [Fact]
    public void CodecIsDeterministicAndReverseRestoresEveryEntryPayload()
    {
        var (before, after) = ComparedPackages();
        var patch = new OpcPackagePatchBuilder().Create(before, after);
        var codec = new OpcPackagePatchCodec();
        using var first = new MemoryStream();
        using var second = new MemoryStream();

        codec.Write(first, patch);
        codec.Write(second, patch);
        first.Position = 0;
        var decoded = codec.Read(first);
        var applied = decoded.MaterializeCandidate(before);
        var reversed = decoded.Reverse();
        var restored = reversed.MaterializeCandidate(applied);

        Assert.Equal(first.ToArray(), second.ToArray());
        Assert.Equal(patch.PatchId, decoded.PatchId);
        Assert.Equal(patch.OperationCount, decoded.OperationCount);
        Assert.Equal(before.Fingerprint, reversed.ResultPackageFingerprint);
        Assert.Equal(before.Fingerprint, restored.Fingerprint);
        AssertEntryContentEqual(before, restored);
        Assert.Equal(patch.PatchId, reversed.Reverse().PatchId);
    }

    [Fact]
    public void PathReadUsesAStableSnapshotAndRejectsConcurrentRewrite()
    {
        var (before, after) = ComparedPackages();
        var patch = new OpcPackagePatchBuilder().Create(before, after);
        using var artifact = WritePatch(patch);
        var original = artifact.ToArray();
        var changed = original.ToArray();
        changed[changed.Length / 2] ^= 0xff;
        var path = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-{Guid.NewGuid():N}.wtpatch"
        );
        File.WriteAllBytes(path, original);
        try
        {
            using (var writer = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete
            ))
            {
                var codec = new OpcPackagePatchCodec();
                var decoded = codec.ReadFileFromPath(path);
                Assert.Equal(patch.PatchId, decoded.Patch.PatchId);
                Assert.Equal(original.LongLength, decoded.SerializedBytes);
                Assert.Equal(
                    Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant(),
                    decoded.SerializedSha256
                );

                var exception = Assert.Throws<OpcPackageSourceChangedException>(() =>
                    codec.ReadPath(
                        path,
                        CancellationToken.None,
                        attempt =>
                        {
                            var replacement = attempt == 1 ? changed : original;
                            writer.Position = 0;
                            writer.Write(replacement);
                            writer.SetLength(replacement.Length);
                            writer.Flush(flushToDisk: true);
                        }
                    )
                );

                Assert.DoesNotContain(path, exception.Message, StringComparison.Ordinal);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PathSnapshotBoundScalesForWorstCaseDeflateExpansion()
    {
        const long oneGibibyte = 1024L * 1024 * 1024;
        var limits = new OpcPackagePatchLimits
        {
            MaxPayloads = 1,
            MaxPayloadBytes = oneGibibyte,
            MaxPayloadBytesPerBlob = oneGibibyte,
            MaxManifestBytes = 1,
        };
        var expandedBytes = limits.MaxPayloadBytes + limits.MaxManifestBytes;
        const long entryCount = 2;
        var expected = expandedBytes
            + (expandedBytes >> 3)
            + (expandedBytes >> 8)
            + (expandedBytes >> 9)
            + (entryCount * 22)
            + (entryCount * 512)
            + (64 * 1024);

        var actual = new OpcPackagePatchCodec(limits)
            .MaximumSerializedArchiveBytes();

        Assert.Equal(expected, actual);
        Assert.True(
            actual
                > expandedBytes
                    + (entryCount * 512)
                    + (64 * 1024),
            "The serialized cap must include data-dependent DEFLATE expansion."
        );
    }

    [Fact]
    public void PathReadClassifiesOversizedArtifactAsPatchLimit()
    {
        var codec = new OpcPackagePatchCodec(new OpcPackagePatchLimits
        {
            MaxPayloads = 1,
            MaxPayloadBytes = 1,
            MaxPayloadBytesPerBlob = 1,
            MaxManifestBytes = 1,
        });
        var path = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-{Guid.NewGuid():N}.wtpatch"
        );
        using (var oversized = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            oversized.SetLength(codec.MaximumSerializedArchiveBytes() + 1);
        }
        try
        {
            var exception = Assert.Throws<OpcPackagePatchLimitException>(() =>
                codec.ReadFromPath(path)
            );

            Assert.IsType<OpcPackageLimitException>(exception.InnerException);
            Assert.DoesNotContain(path, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(31, 1)]
    [InlineData(4, 65_535)]
    [InlineData(3, 65_536)]
    [InlineData(2, 1_048_576)]
    public void SerializedPatchFitsBoundAcrossPayloadDistributions(
        int payloadCount,
        int payloadBytes
    )
    {
        var beforeEntries = new Dictionary<string, byte[]>
        {
            ["word/document.xml"] = Utf8(DocumentXml("same")),
        };
        var afterEntries = new Dictionary<string, byte[]>(beforeEntries);
        for (var index = 0; index < payloadCount; index++)
        {
            var payload = new byte[payloadBytes];
            new Random(unchecked(0x5eed_1234 + index)).NextBytes(payload);
            payload[0] = (byte)index;
            afterEntries[$"custom/payload-{index:D4}.bin"] = payload;
        }
        using var beforeStream = BuildPackage(beforeEntries);
        using var afterStream = BuildPackage(afterEntries);
        var totalPayloadBytes = checked((long)payloadCount * payloadBytes);
        var limits = new OpcPackagePatchLimits
        {
            MaxOperations = payloadCount,
            MaxPayloads = payloadCount,
            MaxPayloadBytes = totalPayloadBytes,
            MaxPayloadBytesPerBlob = payloadBytes,
            MaxManifestBytes = 4L * 1024 * 1024,
            MaxCompressionRatio = double.MaxValue,
        };
        var patch = new OpcPackagePatchBuilder(limits).Create(
            Read(beforeStream),
            Read(afterStream)
        );
        var codec = new OpcPackagePatchCodec(limits);
        using var first = new MemoryStream();
        using var second = new MemoryStream();

        codec.Write(first, patch);
        codec.Write(second, patch);
        first.Position = 0;
        var decoded = codec.Read(first);

        Assert.Equal(payloadCount, patch.PayloadCount);
        Assert.True(first.Length <= codec.MaximumSerializedArchiveBytes());
        Assert.Equal(first.ToArray(), second.ToArray());
        Assert.Equal(patch.PatchId, decoded.PatchId);
    }

    [Fact]
    public void ExtremeCustomLimitsRemainOverflowSafeForPathRoundTrip()
    {
        var limits = new OpcPackagePatchLimits
        {
            MaxOperations = int.MaxValue,
            MaxPayloads = int.MaxValue,
            MaxPayloadBytes = long.MaxValue,
            MaxPayloadBytesPerBlob = long.MaxValue,
            MaxManifestBytes = long.MaxValue,
            MaxCompressionRatio = double.MaxValue,
        };
        var codec = new OpcPackagePatchCodec(limits);
        var (before, after) = ComparedPackages();
        var patch = new OpcPackagePatchBuilder().Create(before, after);
        using var artifact = new MemoryStream();
        codec.Write(artifact, patch);
        var path = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-{Guid.NewGuid():N}.wtpatch"
        );
        File.WriteAllBytes(path, artifact.ToArray());
        try
        {
            var decoded = codec.ReadFromPath(path);

            Assert.Equal(long.MaxValue, codec.MaximumSerializedArchiveBytes());
            Assert.Equal(patch.PatchId, decoded.PatchId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NoOpPatchRoundTripsWithoutPayloadOrMutation()
    {
        using var source = BuildPackage(new Dictionary<string, byte[]>
        {
            ["word/document.xml"] = Utf8(DocumentXml("same")),
        });
        var package = Read(source);
        var patch = new OpcPackagePatchBuilder().Create(package, package);
        using var artifact = new MemoryStream();
        var codec = new OpcPackagePatchCodec();

        codec.Write(artifact, patch);
        artifact.Position = 0;
        var decoded = codec.Read(artifact);
        var candidate = decoded.MaterializeCandidate(package);

        Assert.True(decoded.IsNoOp);
        Assert.Equal(0, decoded.OperationCount);
        Assert.Equal(0, decoded.PayloadCount);
        Assert.Equal(package.Fingerprint, candidate.Fingerprint);
        Assert.False(decoded.CreateMutation(package).HasChanges);
        Assert.Equal(decoded.PatchId, decoded.Reverse().PatchId);
    }

    [Fact]
    public void DeduplicatesEqualPayloadsAcrossOperations()
    {
        using var beforeStream = BuildPackage(new Dictionary<string, byte[]>
        {
            ["word/document.xml"] = Utf8(DocumentXml("same")),
            ["custom/a.bin"] = [1, 1, 1],
            ["custom/b.bin"] = [1, 1, 1],
        });
        using var afterStream = BuildPackage(new Dictionary<string, byte[]>
        {
            ["word/document.xml"] = Utf8(DocumentXml("same")),
            ["custom/a.bin"] = [2, 2, 2],
            ["custom/b.bin"] = [2, 2, 2],
        });

        var patch = new OpcPackagePatchBuilder().Create(
            Read(beforeStream),
            Read(afterStream)
        );

        Assert.Equal(2, patch.OperationCount);
        Assert.Equal(2, patch.PayloadCount);
        Assert.Equal(6, patch.PayloadBytes);
    }

    [Fact]
    public void PreservesExplicitDirectoryEntryAsPackageEvidence()
    {
        using var beforeStream = BuildPackage(new Dictionary<string, byte[]>
        {
            ["word/document.xml"] = Utf8(DocumentXml("same")),
        });
        using var afterStream = BuildPackage(
            new Dictionary<string, byte[]>
            {
                ["word/document.xml"] = Utf8(DocumentXml("same")),
            },
            directoryEntries: ["custom/"]
        );
        var before = Read(beforeStream);
        var after = Read(afterStream);

        var patch = new OpcPackagePatchBuilder().Create(before, after);
        using var artifact = WritePatch(patch);
        artifact.Position = 0;
        var decoded = new OpcPackagePatchCodec().Read(artifact);
        var candidate = decoded.MaterializeCandidate(before);

        var operation = Assert.Single(decoded.Operations);
        Assert.Equal("custom/", operation.EntryName);
        Assert.Equal(OpcPackagePatchOperationKind.Add, operation.Kind);
        Assert.Contains(candidate.Entries, entry =>
            entry.Name == "custom/" && entry.IsDirectory
        );
        Assert.Equal(after.Fingerprint, candidate.Fingerprint);
    }

    [Fact]
    public void RejectsPackageThatDoesNotMatchPatchBase()
    {
        var (before, after) = ComparedPackages();
        var patch = new OpcPackagePatchBuilder().Create(before, after);
        using var unrelatedStream = BuildPackage(new Dictionary<string, byte[]>
        {
            ["word/document.xml"] = Utf8(DocumentXml("unrelated")),
        });
        var unrelated = Read(unrelatedStream);

        var exception = Assert.Throws<OpcPackagePatchPreconditionException>(() =>
            patch.CreateMutation(unrelated)
        );

        Assert.Contains("base fingerprint", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTamperedPayloadBeforeItCanBecomeAMutation()
    {
        var (before, after) = ComparedPackages();
        var patch = new OpcPackagePatchBuilder().Create(before, after);
        using var artifact = WritePatch(patch);
        RewriteFirstPayload(artifact, bytes =>
        {
            bytes[0] ^= 0xff;
            return bytes;
        });

        artifact.Position = 0;
        var exception = Assert.Throws<OpcPackagePatchFormatException>(() =>
            new OpcPackagePatchCodec().Read(artifact)
        );

        Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnknownManifestPropertyAndMismatchedPatchId()
    {
        var (before, after) = ComparedPackages();
        var patch = new OpcPackagePatchBuilder().Create(before, after);
        using var unknownProperty = WritePatch(patch);
        RewriteManifest(unknownProperty, json => "{\"unknown\":1," + json[1..]);
        unknownProperty.Position = 0;
        Assert.Throws<OpcPackagePatchFormatException>(() =>
            new OpcPackagePatchCodec().Read(unknownProperty)
        );

        using var wrongId = WritePatch(patch);
        RewriteManifest(wrongId, json => json.Replace(
            patch.PatchId,
            "wtpatch_" + new string('A', patch.PatchId.Length - "wtpatch_".Length),
            StringComparison.Ordinal
        ));
        wrongId.Position = 0;
        var exception = Assert.Throws<OpcPackagePatchFormatException>(() =>
            new OpcPackagePatchCodec().Read(wrongId)
        );
        Assert.Contains("Patch ID", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../manifest.json")]
    [InlineData("/manifest.json")]
    [InlineData("payloads\\escape.bin")]
    public void RejectsUnsafeArchiveEntryNames(string entryName)
    {
        using var artifact = new MemoryStream();
        using (var archive = new ZipArchive(
            artifact,
            ZipArchiveMode.Create,
            leaveOpen: true
        ))
        {
            WriteEntry(archive, entryName, []);
        }
        artifact.Position = 0;

        Assert.Throws<OpcPackagePatchFormatException>(() =>
            new OpcPackagePatchCodec().Read(artifact)
        );
    }

    [Fact]
    public void RejectsDuplicateManifestAndUnreferencedArchiveEntry()
    {
        using var duplicate = new MemoryStream();
        using (var archive = new ZipArchive(
            duplicate,
            ZipArchiveMode.Create,
            leaveOpen: true
        ))
        {
            WriteEntry(archive, "manifest.json", []);
            WriteEntry(archive, "manifest.json", []);
        }
        duplicate.Position = 0;
        Assert.Throws<OpcPackagePatchFormatException>(() =>
            new OpcPackagePatchCodec().Read(duplicate)
        );

        var (before, after) = ComparedPackages();
        using var extra = WritePatch(
            new OpcPackagePatchBuilder().Create(before, after)
        );
        using (var archive = new ZipArchive(
            extra,
            ZipArchiveMode.Update,
            leaveOpen: true
        ))
        {
            WriteEntry(
                archive,
                "payloads/" + new string('0', 64) + ".bin",
                [0]
            );
        }
        extra.Position = 0;
        Assert.Throws<OpcPackagePatchFormatException>(() =>
            new OpcPackagePatchCodec().Read(extra)
        );
    }

    [Fact]
    public void RejectsNonCanonicalOperationOrderAndDeclaredPayloadLength()
    {
        var (before, after) = ComparedPackages();
        var patch = new OpcPackagePatchBuilder().Create(before, after);
        Assert.True(patch.OperationCount > 1);

        using var reordered = WritePatch(patch);
        RewriteManifestNode(reordered, root =>
        {
            var operations = root["operations"]!.AsArray();
            var first = operations[0];
            var last = operations[^1];
            operations[0] = last!.DeepClone();
            operations[^1] = first!.DeepClone();
        });
        reordered.Position = 0;
        var orderException = Assert.Throws<OpcPackagePatchFormatException>(() =>
            new OpcPackagePatchCodec().Read(reordered)
        );
        Assert.Contains("canonical", orderException.Message, StringComparison.Ordinal);

        using var wrongLength = WritePatch(patch);
        RewriteManifestNode(wrongLength, root =>
        {
            var operation = root["operations"]!.AsArray()
                .First(node => node!["after_bytes"] is not null)!;
            operation["after_bytes"] = operation["after_bytes"]!.GetValue<long>() + 1;
        });
        wrongLength.Position = 0;
        var lengthException = Assert.Throws<OpcPackagePatchFormatException>(() =>
            new OpcPackagePatchCodec().Read(wrongLength)
        );
        Assert.Contains("payload length", lengthException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsPatchArchiveWithExcessiveCompressionRatio()
    {
        using var beforeStream = BuildPackage(new Dictionary<string, byte[]>
        {
            ["word/document.xml"] = Utf8(DocumentXml("same")),
            ["custom/data.bin"] = [1],
        });
        using var afterStream = BuildPackage(new Dictionary<string, byte[]>
        {
            ["word/document.xml"] = Utf8(DocumentXml("same")),
            ["custom/data.bin"] = new byte[128 * 1024],
        });
        using var artifact = WritePatch(new OpcPackagePatchBuilder().Create(
            Read(beforeStream),
            Read(afterStream)
        ));
        artifact.Position = 0;

        var exception = Assert.Throws<OpcPackagePatchLimitException>(() =>
            new OpcPackagePatchCodec(new OpcPackagePatchLimits
            {
                MaxCompressionRatio = 10,
            }).Read(artifact)
        );

        Assert.Contains("compression-ratio", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnforcesPayloadLimitBeforeCreatingPatchCopy()
    {
        using var beforeStream = BuildPackage(new Dictionary<string, byte[]>
        {
            ["word/document.xml"] = Utf8(DocumentXml("same")),
            ["custom/data.bin"] = [1, 2, 3],
        });
        using var afterStream = BuildPackage(new Dictionary<string, byte[]>
        {
            ["word/document.xml"] = Utf8(DocumentXml("same")),
            ["custom/data.bin"] = [4, 5, 6],
        });
        var builder = new OpcPackagePatchBuilder(new OpcPackagePatchLimits
        {
            MaxPayloadBytesPerBlob = 2,
        });

        Assert.Throws<OpcPackagePatchLimitException>(() =>
            builder.Create(Read(beforeStream), Read(afterStream))
        );
    }

    [Fact]
    public void HonorsCancellationBeforePatchConstructionAndRead()
    {
        var (before, after) = ComparedPackages();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            new OpcPackagePatchBuilder().Create(
                before,
                after,
                cancellation.Token
            )
        );
        using var artifact = WritePatch(
            new OpcPackagePatchBuilder().Create(before, after)
        );
        artifact.Position = 0;
        Assert.Throws<OperationCanceledException>(() =>
            new OpcPackagePatchCodec().Read(artifact, cancellation.Token)
        );
    }

    [Fact]
    public void CreatesEmptyPatchForEveryBundledDocumentAgainstItself()
    {
        var root = FindRepositoryRoot();
        var paths = new[]
        {
            Path.Combine(root, "examples"),
            Path.Combine(root, "tests", "upstream", "fixtures"),
            Path.Combine(root, "tests", "upstream", "fuzz", "corpus"),
        }
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(
                path,
                "*.docx",
                SearchOption.AllDirectories
            ))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.True(paths.Length >= 40);
        var reader = new OpcPackageReader();
        var builder = new OpcPackagePatchBuilder();

        foreach (var path in paths)
        {
            var package = reader.Read(path);
            var patch = builder.Create(package, package);
            Assert.True(patch.IsNoOp, path);
            Assert.Equal(0, patch.PayloadBytes);
        }
    }

    private static (OpcPackageSnapshot Before, OpcPackageSnapshot After)
        ComparedPackages()
    {
        using var beforeStream = BuildPackage(new Dictionary<string, byte[]>
        {
            ["word/document.xml"] = Utf8(DocumentXml("before")),
            ["custom/change.bin"] = [1, 2, 3],
            ["custom/remove.bin"] = [4, 5, 6],
        });
        using var afterStream = BuildPackage(new Dictionary<string, byte[]>
        {
            ["word/document.xml"] = Utf8(DocumentXml("after")),
            ["custom/change.bin"] = [1, 2, 4],
            ["custom/add.bin"] = [7, 8, 9],
        });
        return (Read(beforeStream), Read(afterStream));
    }

    private static MemoryStream WritePatch(OpcPackagePatch patch)
    {
        var stream = new MemoryStream();
        new OpcPackagePatchCodec().Write(stream, patch);
        stream.Position = 0;
        return stream;
    }

    private static void RewriteFirstPayload(
        MemoryStream artifact,
        Func<byte[], byte[]> transform
    )
    {
        using var archive = new ZipArchive(
            artifact,
            ZipArchiveMode.Update,
            leaveOpen: true
        );
        var entry = archive.Entries.First(item =>
            item.FullName.StartsWith("payloads/", StringComparison.Ordinal)
        );
        var bytes = ReadEntry(entry);
        entry.Delete();
        WriteEntry(archive, entry.FullName, transform(bytes));
    }

    private static void RewriteManifest(
        MemoryStream artifact,
        Func<string, string> transform
    )
    {
        using var archive = new ZipArchive(
            artifact,
            ZipArchiveMode.Update,
            leaveOpen: true
        );
        var entry = archive.GetEntry("manifest.json")!;
        var json = Encoding.UTF8.GetString(ReadEntry(entry));
        entry.Delete();
        WriteEntry(archive, "manifest.json", Utf8(transform(json)));
    }

    private static void RewriteManifestNode(
        MemoryStream artifact,
        Action<JsonObject> transform
    )
    {
        using var archive = new ZipArchive(
            artifact,
            ZipArchiveMode.Update,
            leaveOpen: true
        );
        var entry = archive.GetEntry("manifest.json")!;
        var root = JsonNode.Parse(ReadEntry(entry))!.AsObject();
        transform(root);
        entry.Delete();
        WriteEntry(archive, "manifest.json", Utf8(root.ToJsonString()));
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static void AssertEntryContentEqual(
        OpcPackageSnapshot expected,
        OpcPackageSnapshot actual
    )
    {
        var expectedEntries = expected.Entries.ToDictionary(
            entry => entry.Name,
            StringComparer.Ordinal
        );
        var actualEntries = actual.Entries.ToDictionary(
            entry => entry.Name,
            StringComparer.Ordinal
        );
        Assert.Equal(expectedEntries.Keys.Order(), actualEntries.Keys.Order());
        foreach (var (name, expectedEntry) in expectedEntries)
        {
            Assert.Equal(
                expectedEntry.Content.ToArray(),
                actualEntries[name].Content.ToArray()
            );
        }
    }

    private static OpcPackageSnapshot Read(MemoryStream stream)
    {
        stream.Position = 0;
        return new OpcPackageReader().Read(stream);
    }

    private static MemoryStream BuildPackage(
        IReadOnlyDictionary<string, byte[]> entries,
        IReadOnlyList<string>? directoryEntries = null
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Create,
            leaveOpen: true
        ))
        {
            WriteEntry(archive, "[Content_Types].xml", Utf8(ContentTypes()));
            WriteEntry(archive, "_rels/.rels", Utf8(RootRelationships()));
            foreach (var entry in entries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                WriteEntry(archive, entry.Key, entry.Value);
            }
            foreach (var directory in directoryEntries ?? [])
            {
                WriteEntry(archive, directory, []);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(
        ZipArchive archive,
        string name,
        ReadOnlySpan<byte> content
    )
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static string DocumentXml(string text) =>
        "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
        + $"<w:body><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:body></w:document>";

    private static string ContentTypes() =>
        "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>"
        + "<Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/>"
        + "<Default Extension='xml' ContentType='application/xml'/>"
        + "<Default Extension='bin' ContentType='application/octet-stream'/>"
        + "<Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/>"
        + "</Types>";

    private static string RootRelationships() =>
        "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
        + "<Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='word/document.xml'/>"
        + "</Relationships>";

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "pyproject.toml")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
