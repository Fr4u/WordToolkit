using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

var options = Arguments.Parse(args);
var report = options.Scenario switch
{
    "graph" => RunGraph(options),
    "patch" => RunPatch(options),
    _ => throw new ArgumentException("scenario must be 'graph' or 'patch'"),
};
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
{
    WriteIndented = true,
});
Console.WriteLine(json);
if (options.Output is not null)
{
    var path = Path.GetFullPath(options.Output);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, json + Environment.NewLine, new UTF8Encoding(false));
}

static object RunGraph(Arguments options)
{
    if (options.TargetNodes is < 100 or > 999_000)
    {
        throw new ArgumentOutOfRangeException(
            nameof(options.TargetNodes),
            "graph target must be between 100 and 999000 nodes"
        );
    }
    var paragraphCount = Math.Max(1, (options.TargetNodes - 8) / 3);
    var maxXmlElements = checked(
        (int)Math.Min(int.MaxValue, Math.Max(1_000_000L, options.TargetNodes * 2L))
    );
    var path = TemporaryPath("graph", ".docx");
    try
    {
        var generation = Measure(() => WriteGraphPackage(path, paragraphCount));
        Collect();
        var baseline = MemorySnapshot.Capture();

        OpcPackageSnapshot? package = null;
        WordSemanticDocument? semantic = null;
        WordDependencyGraph? graph = null;
        var read = Measure(() => package = new OpcPackageReader().Read(path));
        var project = Measure(() =>
            semantic = new WordSemanticProjector(
                new WordSemanticProjectionOptions
                {
                    MaxXmlCharacters = 256L * 1024 * 1024,
                    MaxXmlElements = maxXmlElements,
                    MaxTextCharacters = 64L * 1024 * 1024,
                }
            ).Project(package!)
        );
        var build = Measure(() =>
            graph = new WordDependencyGraphBuilder().Build(package!, semantic!)
        );
        var final = MemorySnapshot.Capture();
        GC.KeepAlive(graph);
        return CommonReport(
            "dependency_graph",
            new
            {
                requested_nodes = options.TargetNodes,
                paragraphs = paragraphCount,
                package_bytes = new FileInfo(path).Length,
                package_parts = package!.Entries.Count,
                semantic_nodes = semantic!.NodeCount,
                dependency_nodes = graph!.Nodes.Count,
                dependency_edges = graph.Edges.Count,
                issues = graph.Issues.Count,
                timings_ms = new
                {
                    generate = generation.TotalMilliseconds,
                    package_read = read.TotalMilliseconds,
                    semantic_projection = project.TotalMilliseconds,
                    dependency_build = build.TotalMilliseconds,
                    measured_total = read.TotalMilliseconds
                        + project.TotalMilliseconds
                        + build.TotalMilliseconds,
                },
                memory = MemoryReport(baseline, final),
            }
        );
    }
    finally
    {
        File.Delete(path);
    }
}

static object RunPatch(Arguments options)
{
    if (options.PayloadMiB is < 1 or > 200)
    {
        throw new ArgumentOutOfRangeException(
            nameof(options.PayloadMiB),
            "patch payload input must be between 1 and 200 MiB per package"
        );
    }
    if (options.Parts is < 1 or > 2_000)
    {
        throw new ArgumentOutOfRangeException(nameof(options.Parts));
    }
    var beforePath = TemporaryPath("patch-before", ".docx");
    var afterPath = TemporaryPath("patch-after", ".docx");
    try
    {
        var bytesPerPackage = checked((long)options.PayloadMiB * 1024 * 1024);
        var expectedPatchPayloadBytes = checked(bytesPerPackage * 2);
        var patchLimits = expectedPatchPayloadBytes <= OpcPackagePatchLimits.Default.MaxPayloadBytes
            ? OpcPackagePatchLimits.Default
            : new OpcPackagePatchLimits
            {
                MaxPayloadBytes = checked(expectedPatchPayloadBytes + 1024 * 1024),
                MaxPayloadBytesPerBlob = OpcPackagePatchLimits.Default.MaxPayloadBytesPerBlob,
                MaxManifestBytes = OpcPackagePatchLimits.Default.MaxManifestBytes,
                MaxCompressionRatio = OpcPackagePatchLimits.Default.MaxCompressionRatio,
            };
        var generation = Measure(() =>
        {
            WritePatchPackage(beforePath, bytesPerPackage, options.Parts, variant: 1);
            WritePatchPackage(afterPath, bytesPerPackage, options.Parts, variant: 2);
        });
        Collect();
        var baseline = MemorySnapshot.Capture();

        OpcPackageSnapshot? before = null;
        OpcPackageSnapshot? after = null;
        OpcPackagePatch? patch = null;
        OpcPackagePatch? decoded = null;
        MemoryStream? artifact = null;
        var read = Measure(() =>
        {
            var reader = new OpcPackageReader();
            before = reader.Read(beforePath);
            after = reader.Read(afterPath);
        });
        var create = Measure(() =>
            patch = new OpcPackagePatchBuilder(patchLimits).Create(before!, after!)
        );
        var write = Measure(() =>
        {
            artifact = new MemoryStream();
            new OpcPackagePatchCodec(patchLimits).Write(artifact, patch!);
        });
        var decode = Measure(() =>
        {
            artifact!.Position = 0;
            decoded = new OpcPackagePatchCodec(patchLimits).Read(artifact);
        });
        var final = MemorySnapshot.Capture();
        GC.KeepAlive(decoded);
        return CommonReport(
            "wtpatch_materialization",
            new
            {
                source_payload_mib_per_package = options.PayloadMiB,
                changed_parts = options.Parts,
                configured_max_patch_payload_bytes = patchLimits.MaxPayloadBytes,
                before_package_bytes = new FileInfo(beforePath).Length,
                after_package_bytes = new FileInfo(afterPath).Length,
                patch_operations = patch!.OperationCount,
                patch_payloads = patch.PayloadCount,
                patch_payload_bytes = patch.PayloadBytes,
                artifact_bytes = artifact!.Length,
                decoded_payload_bytes = decoded!.PayloadBytes,
                timings_ms = new
                {
                    generate = generation.TotalMilliseconds,
                    package_read = read.TotalMilliseconds,
                    patch_create = create.TotalMilliseconds,
                    patch_write = write.TotalMilliseconds,
                    patch_read = decode.TotalMilliseconds,
                    measured_total = read.TotalMilliseconds
                        + create.TotalMilliseconds
                        + write.TotalMilliseconds
                        + decode.TotalMilliseconds,
                },
                memory = MemoryReport(baseline, final),
            }
        );
    }
    finally
    {
        File.Delete(beforePath);
        File.Delete(afterPath);
    }
}

static object CommonReport(string scenario, object measurements)
{
    return new
    {
        schema = "wordtoolkit-engine-benchmark-v1",
        scenario,
        utc = DateTimeOffset.UtcNow,
        runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        operating_system = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        engine_version = typeof(WordDependencyGraph).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion,
        processor_count = Environment.ProcessorCount,
        server_gc = System.Runtime.GCSettings.IsServerGC,
        measurements,
    };
}

static object MemoryReport(MemorySnapshot baseline, MemorySnapshot final)
{
    return new
    {
        baseline_managed_bytes = baseline.ManagedBytes,
        final_managed_bytes = final.ManagedBytes,
        retained_managed_delta_bytes = final.ManagedBytes - baseline.ManagedBytes,
        allocated_delta_bytes = final.AllocatedBytes - baseline.AllocatedBytes,
        baseline_working_set_bytes = baseline.WorkingSetBytes,
        final_working_set_bytes = final.WorkingSetBytes,
        process_peak_working_set_bytes = final.PeakWorkingSetBytes,
    };
}

static TimeSpan Measure(Action action)
{
    var watch = Stopwatch.StartNew();
    action();
    watch.Stop();
    return watch.Elapsed;
}

static void Collect()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
}

static string TemporaryPath(string stem, string extension)
{
    return Path.Combine(
        Path.GetTempPath(),
        $"wordtoolkit-{stem}-{Guid.NewGuid():N}{extension}"
    );
}

static void WriteGraphPackage(string path, int paragraphCount)
{
    using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    using var archive = new ZipArchive(file, ZipArchiveMode.Create);
    WriteTextEntry(archive, "[Content_Types].xml", PackageFixture.ContentTypes);
    WriteTextEntry(archive, "_rels/.rels", PackageFixture.RootRelationships);
    var entry = archive.CreateEntry("word/document.xml", CompressionLevel.Fastest);
    using var stream = entry.Open();
    using var writer = new StreamWriter(stream, new UTF8Encoding(false), 64 * 1024);
    writer.Write(PackageFixture.DocumentStart);
    for (var index = 0; index < paragraphCount; index++)
    {
        writer.Write("<w:p w14:paraId=\"");
        writer.Write((index + 1).ToString("X8"));
        writer.Write("\"><w:r><w:t>Item ");
        writer.Write(index);
        writer.Write("</w:t></w:r></w:p>");
    }
    writer.Write(PackageFixture.DocumentEnd);
}

static void WritePatchPackage(
    string path,
    long payloadBytes,
    int parts,
    int variant
)
{
    using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    using var archive = new ZipArchive(file, ZipArchiveMode.Create);
    WriteTextEntry(archive, "[Content_Types].xml", PackageFixture.ContentTypes);
    WriteTextEntry(archive, "_rels/.rels", PackageFixture.RootRelationships);
    WriteTextEntry(
        archive,
        "word/document.xml",
        PackageFixture.DocumentStart + PackageFixture.DocumentEnd
    );
    var basePartBytes = payloadBytes / parts;
    var remainder = payloadBytes % parts;
    var buffer = new byte[64 * 1024];
    for (var part = 0; part < parts; part++)
    {
        var remaining = basePartBytes + (part < remainder ? 1 : 0);
        var entry = archive.CreateEntry(
            $"word/media/benchmark-{part:D5}.bin",
            CompressionLevel.NoCompression
        );
        using var stream = entry.Open();
        long offset = 0;
        while (remaining > 0)
        {
            var count = (int)Math.Min(buffer.Length, remaining);
            FillDeterministic(buffer.AsSpan(0, count), part, offset, variant);
            stream.Write(buffer, 0, count);
            remaining -= count;
            offset += count;
        }
    }
}

static void FillDeterministic(Span<byte> destination, int part, long offset, int variant)
{
    var state = unchecked((uint)(part * 2654435761U) ^ (uint)offset ^ (uint)variant);
    for (var index = 0; index < destination.Length; index++)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        destination[index] = (byte)state;
    }
}

static void WriteTextEntry(ZipArchive archive, string name, string value)
{
    var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
    using var stream = entry.Open();
    using var writer = new StreamWriter(stream, new UTF8Encoding(false));
    writer.Write(value);
}

internal sealed record Arguments(
    string Scenario,
    int TargetNodes,
    int PayloadMiB,
    int Parts,
    string? Output
)
{
    public static Arguments Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException(
                "usage: graph --target-nodes N | patch --payload-mib N [--parts N]"
            );
        }
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"invalid argument at position {index}");
            }
            values.Add(args[index][2..], args[index + 1]);
        }
        return new Arguments(
            args[0],
            IntValue(values, "target-nodes", 10_000),
            IntValue(values, "payload-mib", 16),
            IntValue(values, "parts", 16),
            values.GetValueOrDefault("output")
        );
    }

    private static int IntValue(
        IReadOnlyDictionary<string, string> values,
        string name,
        int fallback
    )
    {
        return values.TryGetValue(name, out var value)
            && int.TryParse(value, out var parsed)
            ? parsed
            : fallback;
    }
}

internal readonly record struct MemorySnapshot(
    long ManagedBytes,
    long AllocatedBytes,
    long WorkingSetBytes,
    long PeakWorkingSetBytes
)
{
    public static MemorySnapshot Capture()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new MemorySnapshot(
            GC.GetTotalMemory(forceFullCollection: false),
            GC.GetTotalAllocatedBytes(precise: false),
            process.WorkingSet64,
            process.PeakWorkingSet64
        );
    }
}

internal static class PackageFixture
{
    internal const string ContentTypes = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Default Extension="bin" ContentType="application/octet-stream"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
        </Types>
        """;

    internal const string RootRelationships = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
        </Relationships>
        """;

    internal const string DocumentStart = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml"><w:body>
        """;

    internal const string DocumentEnd = "<w:sectPr/></w:body></w:document>";
}
