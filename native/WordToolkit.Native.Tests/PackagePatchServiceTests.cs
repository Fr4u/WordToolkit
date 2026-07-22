using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class PackagePatchServiceTests
{
    [Fact]
    public async Task PlanIsCompactSemanticRiskAwareAndNeverOpensWord()
    {
        var files = CreatePatchFiles("before text", "after text");
        try
        {
            var before = new OpcPackageReader().Read(files.BeforePath);
            var after = new OpcPackageReader().Read(files.AfterPath);
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                before_path = files.BeforePath,
                after_path = files.AfterPath,
                expected_before_fingerprint = before.Fingerprint,
                expected_after_fingerprint = after.Fingerprint,
            }));

            var result = await Service().CallAsync(
                "plan_ooxml_patch",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = ToJson(result);
            var root = json.RootElement;

            Assert.StartsWith("wtpatch_", root.GetProperty("patch_id").GetString());
            Assert.False(root.GetProperty("no_op").GetBoolean());
            Assert.Equal(1, root.GetProperty("operation_count").GetInt32());
            Assert.False(
                root.GetProperty("semantic")
                    .GetProperty("semantically_equivalent")
                    .GetBoolean()
            );
            Assert.True(
                root.GetProperty("default_policy")
                    .GetProperty("can_apply")
                    .GetBoolean()
            );
            Assert.Empty(root.GetProperty("items").EnumerateArray());
            Assert.False(root.GetProperty("word_opened").GetBoolean());
            Assert.DoesNotContain("before text", root.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("after text", root.GetRawText(), StringComparison.Ordinal);
            Assert.True(root.GetRawText().Length < 4_500);
        }
        finally
        {
            files.Dispose();
        }
    }

    [Fact]
    public async Task CreateIsDeterministicInspectIsBoundedAndExistingArtifactIsRejected()
    {
        var files = CreatePatchFiles("before text", "after text");
        try
        {
            var service = Service();
            var plan = await Plan(service, files);
            var artifactOne = files.NewArtifactPath("one");
            var artifactTwo = files.NewArtifactPath("two");

            using var first = await CreateArtifact(service, files, plan, artifactOne);
            using var second = await CreateArtifact(service, files, plan, artifactTwo);
            Assert.Equal(
                first.RootElement.GetProperty("artifact_sha256").GetString(),
                second.RootElement.GetProperty("artifact_sha256").GetString()
            );
            Assert.True(
                File.ReadAllBytes(artifactOne).AsSpan().SequenceEqual(
                    File.ReadAllBytes(artifactTwo)
                )
            );

            using var inspectArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                patch_path = artifactOne,
                expected_patch_id = plan.PatchId,
                view = "operations",
                max_items = 1,
            }));
            var inspected = await service.CallAsync(
                "inspect_ooxml_patch",
                inspectArguments.RootElement,
                CancellationToken.None
            );
            using var inspectJson = ToJson(inspected);
            var operation = Assert.Single(
                inspectJson.RootElement.GetProperty("items").EnumerateArray()
            );
            Assert.Equal("replace", operation.GetProperty("kind").GetString());
            Assert.Equal(
                "word/document.xml",
                operation.GetProperty("entry_name").GetString()
            );
            Assert.Equal(JsonValueKind.Null, operation.GetProperty("before_sha256").ValueKind);
            Assert.True(inspectJson.RootElement.GetProperty("reversible").GetBoolean());
            Assert.False(
                inspectJson.RootElement.GetProperty("zip_container_byte_exact").GetBoolean()
            );

            var exception = await Assert.ThrowsAsync<NativeToolException>(async () =>
            {
                using var ignored = await CreateArtifact(
                    service,
                    files,
                    plan,
                    artifactOne
                );
            });
            Assert.Equal("ALREADY_EXISTS", exception.ErrorCode);
        }
        finally
        {
            files.Dispose();
        }
    }

    [Fact]
    public async Task PlanApplyThenAtomicApplyProducesExactResultAndRecoveryBackup()
    {
        var files = CreatePatchFiles("before text", "after text");
        try
        {
            var service = Service();
            var plan = await Plan(service, files);
            var artifact = files.NewArtifactPath("apply");
            using var created = await CreateArtifact(service, files, plan, artifact);
            var applyPlan = await PlanApply(service, files.BeforePath, artifact, plan);

            Assert.True(applyPlan.CanApply);
            Assert.Empty(applyPlan.HardBlockCodes);
            using var applyArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = files.BeforePath,
                patch_path = artifact,
                expected_package_fingerprint = plan.BeforeFingerprint,
                expected_patch_id = plan.PatchId,
                expected_apply_plan_id = applyPlan.ApplyPlanId,
                keep_backup = true,
            }));
            var applied = await service.CallAsync(
                "apply_ooxml_patch",
                applyArguments.RootElement,
                CancellationToken.None
            );
            using var appliedJson = ToJson(applied);
            var root = appliedJson.RootElement;
            var backupPath = root.GetProperty("backup_path").GetString();

            Assert.True(root.GetProperty("applied").GetBoolean());
            Assert.Equal(
                plan.AfterFingerprint,
                root.GetProperty("package_fingerprint").GetString()
            );
            Assert.NotNull(backupPath);
            Assert.True(File.Exists(backupPath));
            Assert.Equal(
                plan.AfterFingerprint,
                new OpcPackageReader().Read(files.BeforePath).Fingerprint
            );
            Assert.Equal(
                plan.BeforeFingerprint,
                new OpcPackageReader().Read(backupPath!).Fingerprint
            );
            files.Track(backupPath!);
        }
        finally
        {
            files.Dispose();
        }
    }

    [Fact]
    public async Task MacroPatchIsBlockedUntilItsSpecificAuthorizationIsPresent()
    {
        var files = CreatePatchFiles(
            "same",
            "same",
            beforeMacro: [1, 2, 3],
            afterMacro: [1, 2, 4]
        );
        try
        {
            var service = Service();
            var plan = await Plan(service, files);
            var artifact = files.NewArtifactPath("macro");
            using var created = await CreateArtifact(service, files, plan, artifact);
            var applyPlan = await PlanApply(service, files.BeforePath, artifact, plan);

            Assert.False(applyPlan.CanApply);
            Assert.Contains(
                "allow_active_content_changes",
                applyPlan.RequiredAuthorizations
            );
            Assert.DoesNotContain(
                "allow_opaque_binary_changes",
                applyPlan.RequiredAuthorizations
            );

            using var blockedArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = files.BeforePath,
                patch_path = artifact,
                expected_package_fingerprint = plan.BeforeFingerprint,
                expected_patch_id = plan.PatchId,
                expected_apply_plan_id = applyPlan.ApplyPlanId,
            }));
            var blocked = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "apply_ooxml_patch",
                    blockedArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("PATCH_POLICY_BLOCKED", blocked.ErrorCode);

            using var authorizedArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = files.BeforePath,
                patch_path = artifact,
                expected_package_fingerprint = plan.BeforeFingerprint,
                expected_patch_id = plan.PatchId,
                expected_apply_plan_id = applyPlan.ApplyPlanId,
                allow_active_content_changes = true,
                keep_backup = false,
            }));
            var applied = await service.CallAsync(
                "apply_ooxml_patch",
                authorizedArguments.RootElement,
                CancellationToken.None
            );
            using var appliedJson = ToJson(applied);
            Assert.True(appliedJson.RootElement.GetProperty("applied").GetBoolean());
            Assert.Equal(
                plan.AfterFingerprint,
                new OpcPackageReader().Read(files.BeforePath).Fingerprint
            );
        }
        finally
        {
            files.Dispose();
        }
    }

    [Fact]
    public async Task TamperedArtifactAndStaleBaseFailClosed()
    {
        var files = CreatePatchFiles("before text", "after text");
        try
        {
            var service = Service();
            var plan = await Plan(service, files);
            var artifact = files.NewArtifactPath("tampered");
            using var created = await CreateArtifact(service, files, plan, artifact);
            using (var stream = new FileStream(artifact, FileMode.Open, FileAccess.ReadWrite))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Update))
            {
                var entry = archive.CreateEntry("unreferenced.bin");
                using var target = entry.Open();
                target.WriteByte(42);
            }

            using var inspectArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                patch_path = artifact,
            }));
            var invalid = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_patch",
                    inspectArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_PATCH", invalid.ErrorCode);

            var cleanArtifact = files.NewArtifactPath("clean");
            using var clean = await CreateArtifact(service, files, plan, cleanArtifact);
            using var staleArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = files.BeforePath,
                patch_path = cleanArtifact,
                expected_package_fingerprint = new string('0', 64),
                expected_patch_id = plan.PatchId,
            }));
            var stale = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "plan_ooxml_patch_apply",
                    staleArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("VERSION_CONFLICT", stale.ErrorCode);
        }
        finally
        {
            files.Dispose();
        }
    }

    [Fact]
    public async Task ApplyPlanIsBoundToTheReviewedDestinationPath()
    {
        var files = CreatePatchFiles("before text", "after text");
        try
        {
            var service = Service();
            var plan = await Plan(service, files);
            var artifact = files.NewArtifactPath("path-bound");
            using var created = await CreateArtifact(service, files, plan, artifact);
            var reviewed = await PlanApply(service, files.BeforePath, artifact, plan);
            var otherPath = files.Stem + "-other.docx";
            File.Copy(files.BeforePath, otherPath);
            files.Track(otherPath);

            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = otherPath,
                patch_path = artifact,
                expected_package_fingerprint = plan.BeforeFingerprint,
                expected_patch_id = plan.PatchId,
                expected_apply_plan_id = reviewed.ApplyPlanId,
            }));
            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "apply_ooxml_patch",
                    arguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("PLAN_MISMATCH", exception.ErrorCode);
            Assert.Equal(
                plan.BeforeFingerprint,
                new OpcPackageReader().Read(otherPath).Fingerprint
            );
        }
        finally
        {
            files.Dispose();
        }
    }

    [Fact]
    public async Task ResultPackageTypeMustMatchTheInPlaceDestinationExtension()
    {
        var files = CreateCrossFormatPatchFiles();
        try
        {
            var service = Service();
            var plan = await Plan(service, files);
            var artifact = files.NewArtifactPath("cross-format");
            using var created = await CreateArtifact(service, files, plan, artifact);
            var applyPlan = await PlanApply(
                service,
                files.BeforePath,
                artifact,
                plan
            );

            Assert.False(applyPlan.CanApply);
            Assert.Contains(
                "result_package_type_does_not_match_destination_extension",
                applyPlan.HardBlockCodes
            );
        }
        finally
        {
            files.Dispose();
        }
    }

    private static WordLiveService Service() => new(new NoInvokeHost());

    private static async Task<PatchPlanResult> Plan(
        WordLiveService service,
        PatchFiles files
    )
    {
        var reader = new OpcPackageReader();
        var beforeFingerprint = reader.Read(files.BeforePath).Fingerprint;
        var afterFingerprint = reader.Read(files.AfterPath).Fingerprint;
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            before_path = files.BeforePath,
            after_path = files.AfterPath,
            expected_before_fingerprint = beforeFingerprint,
            expected_after_fingerprint = afterFingerprint,
        }));
        var result = await service.CallAsync(
            "plan_ooxml_patch",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = ToJson(result);
        return new PatchPlanResult(
            json.RootElement.GetProperty("patch_id").GetString()!,
            beforeFingerprint,
            afterFingerprint
        );
    }

    private static async Task<JsonDocument> CreateArtifact(
        WordLiveService service,
        PatchFiles files,
        PatchPlanResult plan,
        string artifactPath
    )
    {
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            before_path = files.BeforePath,
            after_path = files.AfterPath,
            expected_before_fingerprint = plan.BeforeFingerprint,
            expected_after_fingerprint = plan.AfterFingerprint,
            expected_patch_id = plan.PatchId,
            patch_path = artifactPath,
        }));
        var result = await service.CallAsync(
            "create_ooxml_patch",
            arguments.RootElement,
            CancellationToken.None
        );
        return ToJson(result);
    }

    private static async Task<PatchApplyPlanResult> PlanApply(
        WordLiveService service,
        string packagePath,
        string artifactPath,
        PatchPlanResult plan
    )
    {
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            local_path = packagePath,
            patch_path = artifactPath,
            expected_package_fingerprint = plan.BeforeFingerprint,
            expected_patch_id = plan.PatchId,
        }));
        var result = await service.CallAsync(
            "plan_ooxml_patch_apply",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = ToJson(result);
        var root = json.RootElement;
        return new PatchApplyPlanResult(
            root.GetProperty("apply_plan_id").GetString()!,
            root.GetProperty("default_policy").GetProperty("can_apply").GetBoolean(),
            root.GetProperty("required_authorizations")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray(),
            root.GetProperty("hard_block_codes")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray()
        );
    }

    private static JsonDocument ToJson(object value) => JsonDocument.Parse(
        JsonSerializer.Serialize(value)
    );

    private static PatchFiles CreatePatchFiles(
        string beforeText,
        string afterText,
        byte[]? beforeMacro = null,
        byte[]? afterMacro = null
    )
    {
        var stem = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-patch-service-{Guid.NewGuid():N}"
        );
        var extension = beforeMacro is null && afterMacro is null ? ".docx" : ".docm";
        var beforePath = stem + "-before" + extension;
        var afterPath = stem + "-after" + extension;
        WriteDocument(beforePath, beforeText, beforeMacro);
        WriteDocument(afterPath, afterText, afterMacro);
        return new PatchFiles(stem, beforePath, afterPath);
    }

    private static PatchFiles CreateCrossFormatPatchFiles()
    {
        var stem = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-patch-service-{Guid.NewGuid():N}"
        );
        var beforePath = stem + "-before.docx";
        var afterPath = stem + "-after.docm";
        WriteDocument(beforePath, "before", macro: null);
        WriteDocument(afterPath, "after", macro: [1, 2, 3]);
        return new PatchFiles(stem, beforePath, afterPath);
    }

    private static void WriteDocument(string path, string text, byte[]? macro)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        Write(archive, "[Content_Types].xml", ContentTypes(macro is not null));
        Write(archive, "_rels/.rels", RootRelationships());
        Write(archive, "word/document.xml", DocumentXml(text));
        if (macro is not null)
        {
            Write(archive, "word/_rels/document.xml.rels", DocumentRelationships());
            Write(archive, "word/vbaProject.bin", macro);
        }
    }

    private static string ContentTypes(bool macro) =>
        "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>"
        + "<Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/>"
        + "<Default Extension='xml' ContentType='application/xml'/>"
        + (macro
            ? "<Default Extension='bin' ContentType='application/octet-stream'/>"
                + "<Override PartName='/word/document.xml' ContentType='application/vnd.ms-word.document.macroEnabled.main+xml'/>"
                + "<Override PartName='/word/vbaProject.bin' ContentType='application/vnd.ms-office.vbaProject'/>"
            : "<Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/>")
        + "</Types>";

    private static string DocumentXml(string text) =>
        "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
        + $"<w:body><w:p><w:r><w:t>{text}</w:t></w:r></w:p><w:sectPr/></w:body>"
        + "</w:document>";

    private static string RootRelationships() =>
        "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
        + "<Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='word/document.xml'/>"
        + "</Relationships>";

    private static string DocumentRelationships() =>
        "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
        + "<Relationship Id='rIdVba' Type='http://schemas.microsoft.com/office/2006/relationships/vbaProject' Target='vbaProject.bin'/>"
        + "</Relationships>";

    private static void Write(ZipArchive archive, string name, string value) =>
        Write(archive, name, Encoding.UTF8.GetBytes(value));

    private static void Write(ZipArchive archive, string name, byte[] value)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var target = entry.Open();
        target.Write(value);
    }

    private sealed record PatchPlanResult(
        string PatchId,
        string BeforeFingerprint,
        string AfterFingerprint
    );

    private sealed record PatchApplyPlanResult(
        string ApplyPlanId,
        bool CanApply,
        IReadOnlyList<string> RequiredAuthorizations,
        IReadOnlyList<string> HardBlockCodes
    );

    private sealed class PatchFiles : IDisposable
    {
        private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);

        public PatchFiles(string stem, string beforePath, string afterPath)
        {
            Stem = stem;
            BeforePath = beforePath;
            AfterPath = afterPath;
            Track(beforePath);
            Track(afterPath);
        }

        public string Stem { get; }

        public string BeforePath { get; }

        public string AfterPath { get; }

        public string NewArtifactPath(string suffix)
        {
            var path = $"{Stem}-{suffix}.wtpatch";
            Track(path);
            return path;
        }

        public void Track(string path) => _paths.Add(path);

        public void Dispose()
        {
            foreach (var path in _paths)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private sealed class NoInvokeHost : IWordComHost
    {
        public Task<T> InvokeAsync<T>(
            Func<dynamic, T> operation,
            CancellationToken cancellationToken = default,
            bool launchIfMissing = false
        ) => throw new Xunit.Sdk.XunitException(
            "Saved-package patch actions must not invoke the Word COM host."
        );

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
