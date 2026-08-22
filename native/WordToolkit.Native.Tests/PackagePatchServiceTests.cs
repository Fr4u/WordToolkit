using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;
using WordToolkit.OpenXmlSdk;

namespace WordToolkit.Native.Tests;

public sealed class PackagePatchServiceTests
{
    [Fact]
    public void RollbackActionsPublishExplicitClosedContracts()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        var plan = catalog.InspectAction("plan_ooxml_patch_rollback")["tool"]!.AsObject();
        var apply = catalog.InspectAction("apply_ooxml_patch_rollback")["tool"]!.AsObject();

        Assert.Equal("1.0", plan["operationVersion"]!.GetValue<string>());
        Assert.Equal("1.0", apply["operationVersion"]!.GetValue<string>());
        Assert.False(plan["inputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.False(apply["inputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.False(plan["outputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.False(apply["outputSchema"]!["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(
            "^wtrollback_[A-Za-z0-9_-]+$",
            apply["inputSchema"]!["properties"]!["expected_rollback_plan_id"]!["pattern"]!.GetValue<string>()
        );
        Assert.Equal(
            "^wtrollback_[A-Za-z0-9_-]+$",
            apply["inputSchema"]!["properties"]!["protected_edit_authorization"]!["pattern"]!.GetValue<string>()
        );
        Assert.Equal(
            "^wtrollback_[A-Za-z0-9_-]+$",
            plan["outputSchema"]!["properties"]!["data"]!["properties"]!["protection_authorization_id"]!["pattern"]!.GetValue<string>()
        );
        Assert.Equal(
            "wordtoolkit.plan_ooxml_patch_rollback/1.0",
            plan["outputSchema"]!["properties"]!["data"]!["properties"]!["operation_contract"]!["const"]!.GetValue<string>()
        );
        Assert.Equal(
            "wordtoolkit.apply_ooxml_patch_rollback/1.0",
            apply["outputSchema"]!["properties"]!["data"]!["properties"]!["operation_contract"]!["const"]!.GetValue<string>()
        );
        var protection = plan["outputSchema"]!["$defs"]!["risk"]!["properties"]!["protection"]!;
        Assert.Contains(
            "unmodeled_document_protection_metadata",
            protection["required"]!.AsArray().Select(item => item!.GetValue<string>())
        );
        Assert.Equal(
            "boolean",
            protection["properties"]!["unmodeled_document_protection_metadata"]!["type"]!.GetValue<string>()
        );
        Assert.False(plan["reversibility"]!["applicable"]!.GetValue<bool>());
        Assert.Equal(
            "reverse_patch_with_destination_bound_plan_and_atomic_redo_backup",
            apply["reversibility"]!["mechanism"]!.GetValue<string>()
        );
    }

    [Fact]
    public void PatchApplyContractPublishesExactProtectedEditTokenRoundTrip()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        var plan = catalog.InspectAction("plan_ooxml_patch_apply")["tool"]!.AsObject();
        var apply = catalog.InspectAction("apply_ooxml_patch")["tool"]!.AsObject();

        Assert.Equal(
            "^wtapply_[A-Za-z0-9_-]+$",
            plan["outputSchema"]!["properties"]!["protection_authorization_id"]!["pattern"]!.GetValue<string>()
        );
        Assert.Equal(
            "^wtapply_[A-Za-z0-9_-]+$",
            apply["inputSchema"]!["properties"]!["protected_edit_authorization"]!["pattern"]!.GetValue<string>()
        );
        var applyOutput = apply["outputSchema"]!;
        Assert.Contains(
            "apply_plan_id",
            applyOutput["required"]!.AsArray().Select(item => item!.GetValue<string>())
        );
        Assert.DoesNotContain(
            "plan_id",
            applyOutput["required"]!.AsArray().Select(item => item!.GetValue<string>())
        );
        Assert.Equal(
            "^wtapply_[A-Za-z0-9_-]+$",
            applyOutput["properties"]!["apply_plan_id"]!["pattern"]!.GetValue<string>()
        );
    }

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
            var inspectedArtifactBytes = File.ReadAllBytes(artifactOne);
            Assert.Equal(
                inspectedArtifactBytes.LongLength,
                inspectJson.RootElement.GetProperty("artifact_bytes").GetInt64()
            );
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(inspectedArtifactBytes))
                    .ToLowerInvariant(),
                inspectJson.RootElement.GetProperty("artifact_sha256").GetString()
            );
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
    public async Task ProtectedPatchAndRollbackRequireExactPlanBoundAuthorization()
    {
        var files = CreateProtectedPatchFiles("before text", "after text", "readOnly");
        try
        {
            var service = Service();
            var plan = await Plan(service, files);
            var artifact = files.NewArtifactPath("protected");
            using var created = await CreateArtifact(service, files, plan, artifact);
            var applyPlan = await PlanApply(service, files.BeforePath, artifact, plan);
            var originalBytes = File.ReadAllBytes(files.BeforePath);

            Assert.False(applyPlan.CanApply);
            Assert.Equal(applyPlan.ApplyPlanId, applyPlan.ProtectionAuthorizationId);
            Assert.Contains(
                "protected_edit_authorization",
                applyPlan.RequiredAuthorizations
            );

            using var wrongAuthorization = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = files.BeforePath,
                patch_path = artifact,
                expected_package_fingerprint = plan.BeforeFingerprint,
                expected_patch_id = plan.PatchId,
                expected_apply_plan_id = applyPlan.ApplyPlanId,
                protected_edit_authorization = "wtapply_wrong",
                keep_backup = false,
            }));
            var blocked = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "apply_ooxml_patch",
                    wrongAuthorization.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("PATCH_POLICY_BLOCKED", blocked.ErrorCode);
            Assert.Equal(originalBytes, File.ReadAllBytes(files.BeforePath));

            using var authorization = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = files.BeforePath,
                patch_path = artifact,
                expected_package_fingerprint = plan.BeforeFingerprint,
                expected_patch_id = plan.PatchId,
                expected_apply_plan_id = applyPlan.ApplyPlanId,
                protected_edit_authorization = applyPlan.ProtectionAuthorizationId,
                keep_backup = false,
            }));
            var applied = await service.CallAsync(
                "apply_ooxml_patch",
                authorization.RootElement,
                CancellationToken.None
            );
            using var appliedJson = ToJson(applied);
            Assert.Contains(
                "protected_edit_authorization",
                appliedJson.RootElement.GetProperty("explicit_authorizations")
                    .EnumerateArray()
                    .Select(item => item.GetString())
            );

            var rollbackPlan = await PlanRollback(
                service,
                files.BeforePath,
                artifact,
                plan
            );
            Assert.False(rollbackPlan.CanRollback);
            Assert.Equal(
                rollbackPlan.RollbackPlanId,
                rollbackPlan.ProtectionAuthorizationId
            );
            Assert.Contains(
                "protected_edit_authorization",
                rollbackPlan.RequiredAuthorizations
            );

            using var rollbackAuthorization = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = files.BeforePath,
                patch_path = artifact,
                expected_package_fingerprint = plan.AfterFingerprint,
                expected_patch_id = plan.PatchId,
                expected_rollback_plan_id = rollbackPlan.RollbackPlanId,
                protected_edit_authorization = rollbackPlan.ProtectionAuthorizationId,
                keep_backup = false,
            }));
            var rolledBack = await service.CallAsync(
                "apply_ooxml_patch_rollback",
                rollbackAuthorization.RootElement,
                CancellationToken.None
            );
            using var rollbackJson = ToJson(rolledBack);
            Assert.True(rollbackJson.RootElement.GetProperty("rolled_back").GetBoolean());
            Assert.Equal(
                plan.BeforeFingerprint,
                new OpcPackageReader().Read(files.BeforePath).Fingerprint
            );
        }
        finally
        {
            files.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedProtectionMetadataHardBlocksPatchAndRollbackWithoutMutation(
        bool useUnmodeledDocumentProtection
    )
    {
        var files = useUnmodeledDocumentProtection
            ? CreateUnmodeledProtectionPatchFiles("before text", "after text")
            : CreateMalformedPermissionPatchFiles("before text", "after text");
        try
        {
            var service = Service();
            var plan = await Plan(service, files);
            var artifact = files.NewArtifactPath("malformed-permissions");
            using var created = await CreateArtifact(service, files, plan, artifact);
            var artifactBytes = File.ReadAllBytes(artifact);
            var beforeBytes = File.ReadAllBytes(files.BeforePath);
            var applyPlan = await PlanApply(service, files.BeforePath, artifact, plan);

            Assert.False(applyPlan.CanApply);
            Assert.Null(applyPlan.ProtectionAuthorizationId);
            Assert.Contains("protection_metadata_malformed", applyPlan.HardBlockCodes);

            using var applyArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = files.BeforePath,
                patch_path = artifact,
                expected_package_fingerprint = plan.BeforeFingerprint,
                expected_patch_id = plan.PatchId,
                expected_apply_plan_id = applyPlan.ApplyPlanId,
                protected_edit_authorization = applyPlan.ApplyPlanId,
                keep_backup = false,
            }));
            var blockedApply = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "apply_ooxml_patch",
                    applyArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("PATCH_POLICY_BLOCKED", blockedApply.ErrorCode);
            Assert.Equal(beforeBytes, File.ReadAllBytes(files.BeforePath));
            Assert.Equal(artifactBytes, File.ReadAllBytes(artifact));

            File.WriteAllBytes(files.BeforePath, File.ReadAllBytes(files.AfterPath));
            var resultBytes = File.ReadAllBytes(files.BeforePath);
            var rollbackPlan = await PlanRollback(
                service,
                files.BeforePath,
                artifact,
                plan
            );
            Assert.False(rollbackPlan.CanRollback);
            Assert.Null(rollbackPlan.ProtectionAuthorizationId);
            Assert.Contains("protection_metadata_malformed", rollbackPlan.HardBlockCodes);

            using var rollbackArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = files.BeforePath,
                patch_path = artifact,
                expected_package_fingerprint = plan.AfterFingerprint,
                expected_patch_id = plan.PatchId,
                expected_rollback_plan_id = rollbackPlan.RollbackPlanId,
                protected_edit_authorization = rollbackPlan.RollbackPlanId,
                keep_backup = false,
            }));
            var blockedRollback = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "apply_ooxml_patch_rollback",
                    rollbackArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("PATCH_POLICY_BLOCKED", blockedRollback.ErrorCode);
            Assert.Equal(resultBytes, File.ReadAllBytes(files.BeforePath));
            Assert.Equal(artifactBytes, File.ReadAllBytes(artifact));
        }
        finally
        {
            files.Dispose();
        }
    }

    [Fact]
    public async Task ProtectedNoOpPatchNeedsNoAuthorizationAndPerformsNoWrite()
    {
        var files = CreateProtectedPatchFiles("same text", "same text", "comments");
        try
        {
            var service = Service();
            var plan = await Plan(service, files);
            var artifact = files.NewArtifactPath("protected-noop");
            using var created = await CreateArtifact(service, files, plan, artifact);
            var beforeBytes = File.ReadAllBytes(files.BeforePath);
            var applyPlan = await PlanApply(service, files.BeforePath, artifact, plan);

            Assert.True(applyPlan.CanApply);
            Assert.Null(applyPlan.ProtectionAuthorizationId);
            Assert.DoesNotContain(
                "protected_edit_authorization",
                applyPlan.RequiredAuthorizations
            );

            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
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
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = ToJson(applied);
            Assert.False(json.RootElement.GetProperty("applied").GetBoolean());
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("backup_path").ValueKind);
            Assert.Equal(beforeBytes, File.ReadAllBytes(files.BeforePath));
        }
        finally
        {
            files.Dispose();
        }
    }

    [Fact]
    public async Task CreatePatchConcurrentCompetitorIsAlreadyExistsAndCleansTemporaryArtifact()
    {
        var files = CreatePatchFiles("before text", "after text");
        try
        {
            var service = Service();
            var plan = await Plan(service, files);
            var artifact = files.NewArtifactPath("concurrent");
            var competitor = Encoding.UTF8.GetBytes("competitor");
            service.BeforeCreateNewPublication = (_, destination) =>
                File.WriteAllBytes(destination, competitor);

            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                CreateArtifact(service, files, plan, artifact));

            Assert.Equal("ALREADY_EXISTS", exception.ErrorCode);
            Assert.Equal(competitor, File.ReadAllBytes(artifact));
            Assert.DoesNotContain(
                Directory.EnumerateFiles(Path.GetDirectoryName(artifact)!),
                path => Path.GetFileName(path).StartsWith(
                    ".wordtoolkit-patch-",
                    StringComparison.Ordinal
                )
            );
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

    [Fact]
    public async Task AppliedPatchCanBeRolledBackExactlyWithRecoveryBackup()
    {
        var files = CreatePatchFiles("before text", "after text");
        try
        {
            var service = Service();
            var plan = await Plan(service, files);
            var artifact = files.NewArtifactPath("rollback");
            using var created = await CreateArtifact(service, files, plan, artifact);
            await ApplyForward(service, files.BeforePath, artifact, plan);

            var rollbackPlan = await PlanRollback(
                service,
                files.BeforePath,
                artifact,
                plan
            );
            Assert.StartsWith("wtrollback_", rollbackPlan.RollbackPlanId);
            Assert.StartsWith("wtpatch_", rollbackPlan.ReversePatchId);
            Assert.NotEqual(plan.PatchId, rollbackPlan.ReversePatchId);
            Assert.True(rollbackPlan.CanRollback);
            Assert.Empty(rollbackPlan.HardBlockCodes);
            Assert.True(rollbackPlan.SerializedLength < 5_000);
            Assert.False(rollbackPlan.ContainsFixtureText);

            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = files.BeforePath,
                patch_path = artifact,
                expected_package_fingerprint = plan.AfterFingerprint,
                expected_patch_id = plan.PatchId,
                expected_rollback_plan_id = rollbackPlan.RollbackPlanId,
                keep_backup = true,
            }));
            var result = await service.CallAsync(
                "apply_ooxml_patch_rollback",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = ToJson(result);
            var root = json.RootElement;
            var backupPath = root.GetProperty("backup_path").GetString();

            Assert.True(root.GetProperty("rolled_back").GetBoolean());
            Assert.Equal(plan.PatchId, root.GetProperty("source_patch_id").GetString());
            Assert.Equal(
                plan.BeforeFingerprint,
                root.GetProperty("package_fingerprint").GetString()
            );
            Assert.NotNull(backupPath);
            Assert.True(File.Exists(backupPath));
            Assert.Equal(
                plan.BeforeFingerprint,
                new OpcPackageReader().Read(files.BeforePath).Fingerprint
            );
            Assert.Equal(
                plan.AfterFingerprint,
                new OpcPackageReader().Read(backupPath!).Fingerprint
            );
            Assert.False(root.GetProperty("word_opened").GetBoolean());
            Assert.DoesNotContain("before text", root.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("after text", root.GetRawText(), StringComparison.Ordinal);
            files.Track(backupPath!);
        }
        finally
        {
            files.Dispose();
        }
    }

    [Fact]
    public async Task RollbackRejectsStalePackageAndPlanFromAnotherDestination()
    {
        var files = CreatePatchFiles("before text", "after text");
        try
        {
            var service = Service();
            var plan = await Plan(service, files);
            var artifact = files.NewArtifactPath("rollback-bound");
            using var created = await CreateArtifact(service, files, plan, artifact);
            await ApplyForward(service, files.BeforePath, artifact, plan);

            using var staleArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = files.BeforePath,
                patch_path = artifact,
                expected_package_fingerprint = plan.BeforeFingerprint,
                expected_patch_id = plan.PatchId,
            }));
            var stale = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "plan_ooxml_patch_rollback",
                    staleArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("VERSION_CONFLICT", stale.ErrorCode);

            var reviewed = await PlanRollback(
                service,
                files.BeforePath,
                artifact,
                plan
            );
            var otherPath = files.Stem + "-rollback-other.docx";
            File.Copy(files.BeforePath, otherPath);
            files.Track(otherPath);
            using var wrongPathArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = otherPath,
                patch_path = artifact,
                expected_package_fingerprint = plan.AfterFingerprint,
                expected_patch_id = plan.PatchId,
                expected_rollback_plan_id = reviewed.RollbackPlanId,
            }));
            var mismatch = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "apply_ooxml_patch_rollback",
                    wrongPathArguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("PLAN_MISMATCH", mismatch.ErrorCode);
            Assert.Equal(
                plan.AfterFingerprint,
                new OpcPackageReader().Read(otherPath).Fingerprint
            );
        }
        finally
        {
            files.Dispose();
        }
    }

    [Fact]
    public async Task MacroRollbackRequiresItsSpecificAuthorization()
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
            var artifact = files.NewArtifactPath("macro-rollback");
            using var created = await CreateArtifact(service, files, plan, artifact);
            await ApplyForward(
                service,
                files.BeforePath,
                artifact,
                plan,
                allowActiveContentChanges: true
            );
            var rollbackPlan = await PlanRollback(
                service,
                files.BeforePath,
                artifact,
                plan
            );

            Assert.False(rollbackPlan.CanRollback);
            Assert.Contains(
                "allow_active_content_changes",
                rollbackPlan.RequiredAuthorizations
            );
            using var blockedArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = files.BeforePath,
                patch_path = artifact,
                expected_package_fingerprint = plan.AfterFingerprint,
                expected_patch_id = plan.PatchId,
                expected_rollback_plan_id = rollbackPlan.RollbackPlanId,
            }));
            var blocked = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "apply_ooxml_patch_rollback",
                    blockedArguments.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("PATCH_POLICY_BLOCKED", blocked.ErrorCode);

            using var allowedArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = files.BeforePath,
                patch_path = artifact,
                expected_package_fingerprint = plan.AfterFingerprint,
                expected_patch_id = plan.PatchId,
                expected_rollback_plan_id = rollbackPlan.RollbackPlanId,
                allow_active_content_changes = true,
                keep_backup = false,
            }));
            var rolledBack = await service.CallAsync(
                "apply_ooxml_patch_rollback",
                allowedArguments.RootElement,
                CancellationToken.None
            );
            using var rollbackJson = ToJson(rolledBack);
            Assert.True(
                rollbackJson.RootElement.GetProperty("rolled_back").GetBoolean()
            );
            Assert.Equal(
                plan.BeforeFingerprint,
                new OpcPackageReader().Read(files.BeforePath).Fingerprint
            );
        }
        finally
        {
            files.Dispose();
        }
    }

    [Fact]
    public async Task NoOpRollbackDoesNotMutateOrCreateBackup()
    {
        var files = CreatePatchFiles("same text", "same text");
        try
        {
            var service = Service();
            var plan = await Plan(service, files);
            var artifact = files.NewArtifactPath("noop-rollback");
            using var created = await CreateArtifact(service, files, plan, artifact);
            var rollbackPlan = await PlanRollback(
                service,
                files.BeforePath,
                artifact,
                plan
            );
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = files.BeforePath,
                patch_path = artifact,
                expected_package_fingerprint = plan.AfterFingerprint,
                expected_patch_id = plan.PatchId,
                expected_rollback_plan_id = rollbackPlan.RollbackPlanId,
                keep_backup = true,
            }));
            var result = await service.CallAsync(
                "apply_ooxml_patch_rollback",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = ToJson(result);
            var root = json.RootElement;

            Assert.False(root.GetProperty("rolled_back").GetBoolean());
            Assert.True(root.GetProperty("no_op").GetBoolean());
            Assert.False(root.GetProperty("mutation_performed").GetBoolean());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("backup_path").ValueKind);
            Assert.Equal(
                plan.BeforeFingerprint,
                new OpcPackageReader().Read(files.BeforePath).Fingerprint
            );
        }
        finally
        {
            files.Dispose();
        }
    }

    [Fact]
    public async Task RollbackSdkCliAndMcpShareOneCanonicalPlanAndCliApplyPath()
    {
        var files = CreatePatchFiles("before text", "after text");
        try
        {
            var service = Service();
            var forwardPlan = await Plan(service, files);
            var artifact = files.NewArtifactPath("rollback-parity");
            using var created = await CreateArtifact(
                service,
                files,
                forwardPlan,
                artifact
            );
            await ApplyForward(service, files.BeforePath, artifact, forwardPlan);
            var planRequestJson = JsonSerializer.Serialize(new
            {
                local_path = files.BeforePath,
                patch_path = artifact,
                expected_package_fingerprint = forwardPlan.AfterFingerprint,
                expected_patch_id = forwardPlan.PatchId,
            });

            var direct = new PatchRollbackWordPackageOperation(
                new MicrosoftOpenXmlPackageValidator()
            ).Plan(PatchRollbackOperationJson.ParsePlanRequest(planRequestJson));
            var directNode = WordToolkitOperationJson.SerializeToNode(direct);

            var cliOutput = new StringWriter();
            var cliError = new StringWriter();
            Assert.Equal(
                0,
                PatchRollbackPackageCli.Run(
                    ["--mode", "plan", "--request", "-", "--format", "json"],
                    new StringReader(planRequestJson),
                    cliOutput,
                    cliError
                )
            );
            Assert.Equal(string.Empty, cliError.ToString());
            var cliNode = JsonNode.Parse(cliOutput.ToString());

            using var mcpArguments = JsonDocument.Parse(planRequestJson);
            var mcpResult = await service.CallAsync(
                "plan_ooxml_patch_rollback",
                mcpArguments.RootElement,
                CancellationToken.None
            );
            var mcpNode = JsonNode.Parse(JsonSerializer.Serialize(mcpResult))!.AsObject();
            mcpNode.Remove("runtime");
            mcpNode.Remove("python_used");
            mcpNode.Remove("performance");

            Assert.True(JsonNode.DeepEquals(directNode, cliNode));
            Assert.True(JsonNode.DeepEquals(directNode, mcpNode));
            Assert.NotNull(mcpNode["risk"]!["activex_operation_count"]);
            Assert.Null(mcpNode["risk"]!["active_x_operation_count"]);

            var applyRequestJson = JsonSerializer.Serialize(new
            {
                local_path = files.BeforePath,
                patch_path = artifact,
                expected_package_fingerprint = forwardPlan.AfterFingerprint,
                expected_patch_id = forwardPlan.PatchId,
                expected_rollback_plan_id = direct.RollbackPlanId,
                keep_backup = true,
            });
            cliOutput.GetStringBuilder().Clear();
            cliError.GetStringBuilder().Clear();
            Assert.Equal(
                0,
                PatchRollbackPackageCli.Run(
                    ["--mode", "apply", "--request", "-", "--format", "json"],
                    new StringReader(applyRequestJson),
                    cliOutput,
                    cliError
                )
            );
            Assert.Equal(string.Empty, cliError.ToString());
            var apply = JsonNode.Parse(cliOutput.ToString())!.AsObject();
            Assert.True(apply["rolled_back"]!.GetValue<bool>());
            Assert.Equal(
                forwardPlan.BeforeFingerprint,
                apply["package_fingerprint"]!.GetValue<string>()
            );
            var backupPath = apply["backup_path"]!.GetValue<string>();
            Assert.Equal(
                forwardPlan.AfterFingerprint,
                new OpcPackageReader().Read(backupPath).Fingerprint
            );
            files.Track(backupPath);
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
                .ToArray(),
            root.GetProperty("protection_authorization_id").ValueKind == JsonValueKind.Null
                ? null
                : root.GetProperty("protection_authorization_id").GetString()
        );
    }

    private static async Task ApplyForward(
        WordLiveService service,
        string packagePath,
        string artifactPath,
        PatchPlanResult plan,
        bool allowActiveContentChanges = false
    )
    {
        var applyPlan = await PlanApply(service, packagePath, artifactPath, plan);
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            local_path = packagePath,
            patch_path = artifactPath,
            expected_package_fingerprint = plan.BeforeFingerprint,
            expected_patch_id = plan.PatchId,
            expected_apply_plan_id = applyPlan.ApplyPlanId,
            allow_active_content_changes = allowActiveContentChanges,
            keep_backup = false,
        }));
        _ = await service.CallAsync(
            "apply_ooxml_patch",
            arguments.RootElement,
            CancellationToken.None
        );
    }

    private static async Task<PatchRollbackPlanResult> PlanRollback(
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
            expected_package_fingerprint = plan.AfterFingerprint,
            expected_patch_id = plan.PatchId,
        }));
        var result = await service.CallAsync(
            "plan_ooxml_patch_rollback",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = ToJson(result);
        var root = json.RootElement;
        var serialized = root.GetRawText();
        return new PatchRollbackPlanResult(
            root.GetProperty("rollback_plan_id").GetString()!,
            root.GetProperty("reverse_patch_id").GetString()!,
            root.GetProperty("default_policy").GetProperty("can_rollback").GetBoolean(),
            root.GetProperty("required_authorizations")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray(),
            root.GetProperty("hard_block_codes")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray(),
            root.TryGetProperty("protection_authorization_id", out var authorization)
                && authorization.ValueKind != JsonValueKind.Null
                    ? authorization.GetString()
                    : null,
            serialized.Length,
            serialized.Contains("before text", StringComparison.Ordinal)
                || serialized.Contains("after text", StringComparison.Ordinal)
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

    private static PatchFiles CreateProtectedPatchFiles(
        string beforeText,
        string afterText,
        string protectionMode
    )
    {
        var stem = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-patch-service-{Guid.NewGuid():N}"
        );
        var beforePath = stem + "-before.docx";
        var afterPath = stem + "-after.docx";
        WriteDocument(beforePath, beforeText, macro: null, protectionMode);
        WriteDocument(afterPath, afterText, macro: null, protectionMode);
        return new PatchFiles(stem, beforePath, afterPath);
    }

    private static PatchFiles CreateMalformedPermissionPatchFiles(
        string beforeText,
        string afterText
    )
    {
        var stem = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-patch-service-{Guid.NewGuid():N}"
        );
        var beforePath = stem + "-before.docx";
        var afterPath = stem + "-after.docx";
        const string invalidPermission =
            "<w:permStart ws:id='7' ws:edGrp='everyone' ws:colFirst='0' ws:colLast='2'/>"
            + "<w:permEnd ws:id='7'/>";
        WriteDocument(beforePath, beforeText, macro: null, permissionMarkup: invalidPermission);
        WriteDocument(afterPath, afterText, macro: null, permissionMarkup: invalidPermission);
        return new PatchFiles(stem, beforePath, afterPath);
    }

    private static PatchFiles CreateUnmodeledProtectionPatchFiles(
        string beforeText,
        string afterText
    )
    {
        var stem = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-patch-service-{Guid.NewGuid():N}"
        );
        var beforePath = stem + "-before.docx";
        var afterPath = stem + "-after.docx";
        var settingsXml = AlternateContentSettingsXml();
        WriteDocument(beforePath, beforeText, macro: null, settingsXml: settingsXml);
        WriteDocument(afterPath, afterText, macro: null, settingsXml: settingsXml);
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

    private static void WriteDocument(
        string path,
        string text,
        byte[]? macro,
        string? protectionMode = null,
        string? permissionMarkup = null,
        string? settingsXml = null
    )
    {
        var hasSettings = protectionMode is not null || settingsXml is not null;
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        Write(
            archive,
            "[Content_Types].xml",
            ContentTypes(macro is not null, hasSettings)
        );
        Write(archive, "_rels/.rels", RootRelationships());
        Write(archive, "word/document.xml", DocumentXml(text, permissionMarkup));
        if (macro is not null || hasSettings)
        {
            Write(
                archive,
                "word/_rels/document.xml.rels",
                DocumentRelationships(macro is not null, hasSettings)
            );
        }
        if (macro is not null)
        {
            Write(archive, "word/vbaProject.bin", macro);
        }
        if (hasSettings)
        {
            Write(
                archive,
                "word/settings.xml",
                settingsXml ?? SettingsXml(protectionMode!)
            );
        }
    }

    private static string ContentTypes(bool macro, bool settings = false) =>
        "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>"
        + "<Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/>"
        + "<Default Extension='xml' ContentType='application/xml'/>"
        + (macro
            ? "<Default Extension='bin' ContentType='application/octet-stream'/>"
                + "<Override PartName='/word/document.xml' ContentType='application/vnd.ms-word.document.macroEnabled.main+xml'/>"
                + "<Override PartName='/word/vbaProject.bin' ContentType='application/vnd.ms-office.vbaProject'/>"
            : "<Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/>")
        + (settings
            ? "<Override PartName='/word/settings.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml'/>"
            : string.Empty)
        + "</Types>";

    private static string DocumentXml(string text, string? permissionMarkup = null) =>
        "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main' "
        + "xmlns:ws='http://purl.oclc.org/ooxml/wordprocessingml/main'>"
        + $"<w:body><w:p>{permissionMarkup}<w:r><w:t>{text}</w:t></w:r></w:p><w:sectPr/></w:body>"
        + "</w:document>";

    private static string RootRelationships() =>
        "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
        + "<Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='word/document.xml'/>"
        + "</Relationships>";

    private static string DocumentRelationships(bool macro = true, bool settings = false) =>
        "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
        + (macro
            ? "<Relationship Id='rIdVba' Type='http://schemas.microsoft.com/office/2006/relationships/vbaProject' Target='vbaProject.bin'/>"
            : string.Empty)
        + (settings
            ? "<Relationship Id='rIdSettings' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings' Target='settings.xml'/>"
            : string.Empty)
        + "</Relationships>";

    private static string SettingsXml(string protectionMode) =>
        "<w:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
        + $"<w:documentProtection w:edit='{protectionMode}' w:enforcement='1'/>"
        + "</w:settings>";

    private static string AlternateContentSettingsXml() =>
        "<w:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main' "
        + "xmlns:w14='http://schemas.microsoft.com/office/word/2010/wordml' "
        + "xmlns:mc='http://schemas.openxmlformats.org/markup-compatibility/2006' mc:Ignorable='w14'>"
        + "<mc:AlternateContent><mc:Choice Requires='w14'>"
        + "<w:documentProtection w:edit='readOnly' w:enforcement='1'/>"
        + "</mc:Choice><mc:Fallback>"
        + "<w:documentProtection w:edit='readOnly' w:enforcement='1'/>"
        + "</mc:Fallback></mc:AlternateContent></w:settings>";

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
        IReadOnlyList<string> HardBlockCodes,
        string? ProtectionAuthorizationId
    );

    private sealed record PatchRollbackPlanResult(
        string RollbackPlanId,
        string ReversePatchId,
        bool CanRollback,
        IReadOnlyList<string> RequiredAuthorizations,
        IReadOnlyList<string> HardBlockCodes,
        string? ProtectionAuthorizationId,
        int SerializedLength,
        bool ContainsFixtureText
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
