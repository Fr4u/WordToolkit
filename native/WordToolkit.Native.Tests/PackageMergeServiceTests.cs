using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class PackageMergeServiceTests
{
    [Fact]
    public void MergeContractPublishesExactProtectedEditTokenRoundTrip()
    {
        var catalog = ToolCatalog.LoadNativeWordTools();
        var plan = catalog.InspectAction("plan_ooxml_merge")["tool"]!.AsObject();
        var apply = catalog.InspectAction("apply_ooxml_merge")["tool"]!.AsObject();
        var output = plan["outputSchema"]!;

        Assert.Contains(
            "merge_apply_plan_id",
            output["required"]!.AsArray().Select(item => item!.GetValue<string>())
        );
        Assert.Contains(
            "protection_authorization_id",
            output["required"]!.AsArray().Select(item => item!.GetValue<string>())
        );
        Assert.Equal(
            "^wtmergeapply_[A-Za-z0-9_-]+$",
            output["properties"]!["merge_apply_plan_id"]!["pattern"]!.GetValue<string>()
        );
        Assert.Equal(
            "^wtmergeapply_[A-Za-z0-9_-]+$",
            output["properties"]!["protection_authorization_id"]!["pattern"]!.GetValue<string>()
        );
        Assert.NotNull(output["properties"]!["risk"]!["properties"]!["protection"]);
        Assert.Equal(
            "^wtmergeapply_[A-Za-z0-9_-]+$",
            apply["inputSchema"]!["properties"]!["protected_edit_authorization"]!["pattern"]!.GetValue<string>()
        );
        Assert.Contains(
            "merge_apply_plan_id",
            apply["outputSchema"]!["required"]!.AsArray()
                .Select(item => item!.GetValue<string>())
        );
    }

    [Fact]
    public async Task DisjointTextPlanIsCompactAndApplyCreatesNewMergedDocument()
    {
        using var files = CreateFiles(
            "secret ancestor",
            "beta",
            "secret left",
            "beta",
            "secret ancestor",
            "secret right"
        );
        var service = Service();
        using var plan = await Plan(service, files, files.OutputPath("merged"));
        var root = plan.RootElement;

        Assert.StartsWith("wtmerge_", root.GetProperty("merge_id").GetString());
        Assert.StartsWith(
            "wtmergeapply_",
            root.GetProperty("merge_apply_plan_id").GetString()
        );
        Assert.True(root.GetProperty("candidate_materialized").GetBoolean());
        Assert.Equal(0, root.GetProperty("conflict_count").GetInt32());
        Assert.Equal(2, root.GetProperty("semantic_text_change_count").GetInt32());
        Assert.True(
            root.GetProperty("default_policy").GetProperty("can_apply").GetBoolean()
        );
        Assert.Empty(root.GetProperty("items").EnumerateArray());
        Assert.False(root.GetProperty("word_opened").GetBoolean());
        Assert.DoesNotContain("secret ancestor", root.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret left", root.GetRawText(), StringComparison.Ordinal);
        Assert.True(root.GetRawText().Length < 5_500);

        var outputPath = root.GetProperty("output_path").GetString()!;
        using var applied = await Apply(service, files, root, outputPath);
        var appliedRoot = applied.RootElement;
        Assert.True(appliedRoot.GetProperty("created").GetBoolean());
        Assert.False(appliedRoot.GetProperty("overwritten").GetBoolean());
        Assert.True(File.Exists(outputPath));
        Assert.Equal(
            ["secret left", "secret right"],
            TextValues(new OpcPackageReader().Read(outputPath))
        );
        Assert.False(appliedRoot.GetProperty("word_opened").GetBoolean());
    }

    [Fact]
    public async Task SemanticConflictIsPrivateByDefaultAndRequiresResolution()
    {
        using var files = CreateFiles("alpha", "beta", "left", "beta", "right", "beta");
        var service = Service();
        var outputPath = files.OutputPath("resolved");
        using var conflicts = await Plan(
            service,
            files,
            outputPath,
            view: "conflicts"
        );
        var root = conflicts.RootElement;
        var conflict = Assert.Single(root.GetProperty("items").EnumerateArray());
        var conflictId = conflict.GetProperty("conflict_id").GetString()!;

        Assert.Equal(
            "semantic_text_changed_differently",
            conflict.GetProperty("kind").GetString()
        );
        Assert.Equal(
            JsonValueKind.Null,
            conflict.GetProperty("ancestor").GetProperty("text_preview").ValueKind
        );
        Assert.False(
            root.GetProperty("default_policy").GetProperty("can_apply").GetBoolean()
        );
        Assert.Contains(
            root.GetProperty("hard_block_codes").EnumerateArray(),
            item => item.GetString() == "unresolved_merge_conflicts"
        );

        using var preview = await Plan(
            service,
            files,
            outputPath,
            view: "conflicts",
            includeTextPreviews: true
        );
        Assert.Equal(
            "alpha",
            Assert.Single(preview.RootElement.GetProperty("items").EnumerateArray())
                .GetProperty("ancestor")
                .GetProperty("text_preview")
                .GetString()
        );

        var resolutions = new[] { new { conflict_id = conflictId, choice = "use_left" } };
        using var resolved = await Plan(
            service,
            files,
            outputPath,
            resolutions
        );
        Assert.True(resolved.RootElement.GetProperty("candidate_materialized").GetBoolean());
        Assert.Equal(1, resolved.RootElement.GetProperty("resolved_conflict_count").GetInt32());

        using var applied = await Apply(
            service,
            files,
            resolved.RootElement,
            outputPath,
            resolutions
        );
        Assert.Equal(
            ["left", "beta"],
            TextValues(new OpcPackageReader().Read(outputPath))
        );
    }

    [Fact]
    public async Task ApplyPlanIsBoundToOutputPathAndNeverOverwrites()
    {
        using var files = CreateFiles("alpha", "beta", "left", "beta", "alpha", "right");
        var service = Service();
        var reviewedPath = files.OutputPath("reviewed");
        var otherPath = files.OutputPath("other");
        using var plan = await Plan(service, files, reviewedPath);

        var mismatch = await Assert.ThrowsAsync<NativeToolException>(() =>
            Apply(service, files, plan.RootElement, otherPath)
        );
        Assert.Equal("PLAN_MISMATCH", mismatch.ErrorCode);
        Assert.False(File.Exists(reviewedPath));
        Assert.False(File.Exists(otherPath));

        File.WriteAllText(reviewedPath, "existing");
        var exists = await Assert.ThrowsAsync<NativeToolException>(() =>
            Apply(service, files, plan.RootElement, reviewedPath)
        );
        Assert.Equal("ALREADY_EXISTS", exists.ErrorCode);
        Assert.Equal("existing", File.ReadAllText(reviewedPath));
    }

    [Fact]
    public async Task MacroSelectionRequiresOnlyActiveContentAuthorization()
    {
        using var files = CreateFiles(
            "same",
            "text",
            "same",
            "text",
            "same",
            "text",
            ancestorMacro: [1],
            leftMacro: [2],
            rightMacro: [1]
        );
        var service = Service();
        var outputPath = files.OutputPath("macro", ".docm");
        using var plan = await Plan(service, files, outputPath);
        var root = plan.RootElement;

        Assert.Contains(
            root.GetProperty("required_authorizations").EnumerateArray(),
            item => item.GetString() == "allow_active_content_changes"
        );
        Assert.DoesNotContain(
            root.GetProperty("required_authorizations").EnumerateArray(),
            item => item.GetString() == "allow_opaque_binary_changes"
        );
        var blocked = await Assert.ThrowsAsync<NativeToolException>(() =>
            Apply(service, files, root, outputPath)
        );
        Assert.Equal("MERGE_POLICY_BLOCKED", blocked.ErrorCode);

        using var applied = await Apply(
            service,
            files,
            root,
            outputPath,
            allowActiveContentChanges: true
        );
        Assert.True(applied.RootElement.GetProperty("created").GetBoolean());
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public async Task ProtectedMergeRequiresExactMergePlanAuthorization()
    {
        using var files = CreateFiles(
            "ancestor",
            "beta",
            "left",
            "beta",
            "ancestor",
            "right",
            protectionMode: "readOnly"
        );
        var service = Service();
        var outputPath = files.OutputPath("protected");
        using var plan = await Plan(service, files, outputPath);
        var root = plan.RootElement;
        var authorizationId = root.GetProperty("protection_authorization_id").GetString();

        Assert.False(
            root.GetProperty("default_policy").GetProperty("can_apply").GetBoolean()
        );
        Assert.Equal(
            root.GetProperty("merge_apply_plan_id").GetString(),
            authorizationId
        );
        Assert.Contains(
            root.GetProperty("required_authorizations").EnumerateArray(),
            item => item.GetString() == "protected_edit_authorization"
        );

        var blocked = await Assert.ThrowsAsync<NativeToolException>(() =>
            Apply(
                service,
                files,
                root,
                outputPath,
                protectedEditAuthorization: "wtmergeapply_wrong"
            )
        );
        Assert.Equal("MERGE_POLICY_BLOCKED", blocked.ErrorCode);
        Assert.False(File.Exists(outputPath));

        using var applied = await Apply(
            service,
            files,
            root,
            outputPath,
            protectedEditAuthorization: authorizationId
        );
        Assert.True(applied.RootElement.GetProperty("created").GetBoolean());
        Assert.Contains(
            applied.RootElement.GetProperty("explicit_authorizations").EnumerateArray(),
            item => item.GetString() == "protected_edit_authorization"
        );
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public async Task MalformedPermissionRangesHardBlockMergeWithoutTouchingInputs()
    {
        const string invalidPermission =
            "<w:permStart w:id='7' w:edGrp='everyone' w:colFirst='invalid' w:colLast='2'/>"
            + "<w:permEnd w:id='7'/>";
        using var files = CreateFiles(
            "ancestor",
            "beta",
            "left",
            "beta",
            "ancestor",
            "right",
            permissionMarkup: invalidPermission
        );
        var ancestorBytes = File.ReadAllBytes(files.AncestorPath);
        var leftBytes = File.ReadAllBytes(files.LeftPath);
        var rightBytes = File.ReadAllBytes(files.RightPath);
        var service = Service();
        var outputPath = files.OutputPath("malformed-permissions");
        using var plan = await Plan(service, files, outputPath);
        var root = plan.RootElement;

        Assert.Contains(
            root.GetProperty("hard_block_codes").EnumerateArray(),
            item => item.GetString() == "protection_metadata_malformed"
        );
        Assert.Equal(JsonValueKind.Null, root.GetProperty("protection_authorization_id").ValueKind);

        var blocked = await Assert.ThrowsAsync<NativeToolException>(() =>
            Apply(
                service,
                files,
                root,
                outputPath,
                protectedEditAuthorization: root.GetProperty("merge_apply_plan_id").GetString()
            )
        );
        Assert.Equal("MERGE_POLICY_BLOCKED", blocked.ErrorCode);
        Assert.False(File.Exists(outputPath));
        Assert.Equal(ancestorBytes, File.ReadAllBytes(files.AncestorPath));
        Assert.Equal(leftBytes, File.ReadAllBytes(files.LeftPath));
        Assert.Equal(rightBytes, File.ReadAllBytes(files.RightPath));
    }

    [Fact]
    public async Task ResultTypeMismatchIsAVisibleNonOverridableHardBlock()
    {
        using var files = CreateFiles("alpha", "beta", "left", "beta", "alpha", "right");
        var service = Service();
        var outputPath = files.OutputPath("wrong-type", ".docm");
        using var plan = await Plan(service, files, outputPath);

        Assert.Contains(
            plan.RootElement.GetProperty("hard_block_codes").EnumerateArray(),
            item => item.GetString()
                == "result_package_type_does_not_match_destination_extension"
        );
        var blocked = await Assert.ThrowsAsync<NativeToolException>(() =>
            Apply(
                service,
                files,
                plan.RootElement,
                outputPath,
                allowActiveContentChanges: true,
                allowNewStructuralErrors: true
            )
        );
        Assert.Equal("MERGE_POLICY_BLOCKED", blocked.ErrorCode);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task ExactBranchFingerprintsAreRecheckedBeforeApply()
    {
        using var files = CreateFiles("alpha", "beta", "left", "beta", "alpha", "right");
        var service = Service();
        var outputPath = files.OutputPath("stale");
        using var plan = await Plan(service, files, outputPath);
        WriteDocument(files.LeftPath, "changed again", "beta", macro: null, overwrite: true);

        var stale = await Assert.ThrowsAsync<NativeToolException>(() =>
            Apply(service, files, plan.RootElement, outputPath)
        );

        Assert.Equal("VERSION_CONFLICT", stale.ErrorCode);
        Assert.False(File.Exists(outputPath));
    }

    private static WordLiveService Service() => new(new NoInvokeHost());

    private static async Task<JsonDocument> Plan(
        WordLiveService service,
        MergeFiles files,
        string outputPath,
        object? resolutions = null,
        string view = "summary",
        bool includeTextPreviews = false
    )
    {
        var reader = new OpcPackageReader();
        var arguments = new Dictionary<string, object?>
        {
            ["ancestor_path"] = files.AncestorPath,
            ["left_path"] = files.LeftPath,
            ["right_path"] = files.RightPath,
            ["output_path"] = outputPath,
            ["expected_ancestor_fingerprint"] = reader.Read(files.AncestorPath).Fingerprint,
            ["expected_left_fingerprint"] = reader.Read(files.LeftPath).Fingerprint,
            ["expected_right_fingerprint"] = reader.Read(files.RightPath).Fingerprint,
            ["resolutions"] = resolutions ?? Array.Empty<object>(),
            ["view"] = view,
            ["include_text_previews"] = includeTextPreviews,
        };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        var result = await service.CallAsync(
            "plan_ooxml_merge",
            json.RootElement,
            CancellationToken.None
        );
        return ToJson(result);
    }

    private static async Task<JsonDocument> Apply(
        WordLiveService service,
        MergeFiles files,
        JsonElement plan,
        string outputPath,
        object? resolutions = null,
        bool allowActiveContentChanges = false,
        bool allowNewStructuralErrors = false,
        string? protectedEditAuthorization = null
    )
    {
        var arguments = new Dictionary<string, object?>
        {
            ["ancestor_path"] = files.AncestorPath,
            ["left_path"] = files.LeftPath,
            ["right_path"] = files.RightPath,
            ["output_path"] = outputPath,
            ["expected_ancestor_fingerprint"] = plan.GetProperty("ancestor_package_fingerprint").GetString(),
            ["expected_left_fingerprint"] = plan.GetProperty("left_package_fingerprint").GetString(),
            ["expected_right_fingerprint"] = plan.GetProperty("right_package_fingerprint").GetString(),
            ["expected_merge_apply_plan_id"] = plan.GetProperty("merge_apply_plan_id").GetString(),
            ["resolutions"] = resolutions ?? Array.Empty<object>(),
            ["allow_active_content_changes"] = allowActiveContentChanges,
            ["allow_new_structural_errors"] = allowNewStructuralErrors,
        };
        if (protectedEditAuthorization is not null)
        {
            arguments["protected_edit_authorization"] = protectedEditAuthorization;
        }
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        var result = await service.CallAsync(
            "apply_ooxml_merge",
            json.RootElement,
            CancellationToken.None
        );
        return ToJson(result);
    }

    private static string[] TextValues(OpcPackageSnapshot package) =>
        new WordSemanticProjector().Project(package).Nodes
            .Where(node => node.Kind == WordSemanticNodeKind.Text)
            .Select(node => node.Text ?? string.Empty)
            .ToArray();

    private static MergeFiles CreateFiles(
        string ancestorFirst,
        string ancestorSecond,
        string leftFirst,
        string leftSecond,
        string rightFirst,
        string rightSecond,
        byte[]? ancestorMacro = null,
        byte[]? leftMacro = null,
        byte[]? rightMacro = null,
        string? protectionMode = null,
        string? permissionMarkup = null
    )
    {
        var stem = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-merge-service-{Guid.NewGuid():N}"
        );
        var extension = ancestorMacro is null && leftMacro is null && rightMacro is null
            ? ".docx"
            : ".docm";
        var ancestorPath = stem + "-ancestor" + extension;
        var leftPath = stem + "-left" + extension;
        var rightPath = stem + "-right" + extension;
        WriteDocument(
            ancestorPath,
            ancestorFirst,
            ancestorSecond,
            ancestorMacro,
            protectionMode: protectionMode,
            permissionMarkup: permissionMarkup
        );
        WriteDocument(
            leftPath,
            leftFirst,
            leftSecond,
            leftMacro,
            protectionMode: protectionMode,
            permissionMarkup: permissionMarkup
        );
        WriteDocument(
            rightPath,
            rightFirst,
            rightSecond,
            rightMacro,
            protectionMode: protectionMode,
            permissionMarkup: permissionMarkup
        );
        return new MergeFiles(stem, ancestorPath, leftPath, rightPath);
    }

    private static void WriteDocument(
        string path,
        string first,
        string second,
        byte[]? macro,
        bool overwrite = false,
        string? protectionMode = null,
        string? permissionMarkup = null
    )
    {
        using var stream = new FileStream(
            path,
            overwrite ? FileMode.Create : FileMode.CreateNew,
            FileAccess.ReadWrite
        );
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        Write(
            archive,
            "[Content_Types].xml",
            ContentTypes(macro is not null, protectionMode is not null)
        );
        Write(archive, "_rels/.rels", RootRelationships());
        Write(archive, "word/document.xml", DocumentXml(first, second, permissionMarkup));
        if (macro is not null || protectionMode is not null)
        {
            Write(
                archive,
                "word/_rels/document.xml.rels",
                DocumentRelationships(macro is not null, protectionMode is not null)
            );
        }
        if (macro is not null)
        {
            Write(archive, "word/vbaProject.bin", macro);
        }
        if (protectionMode is not null)
        {
            Write(archive, "word/settings.xml", SettingsXml(protectionMode));
        }
    }

    private static string DocumentXml(
        string first,
        string second,
        string? permissionMarkup = null
    ) =>
        "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main' "
        + "xmlns:w14='http://schemas.microsoft.com/office/word/2010/wordml'>"
        + $"<w:body><w:p w14:paraId='11111111'>{permissionMarkup}<w:r><w:t>{first}</w:t></w:r></w:p>"
        + $"<w:p w14:paraId='22222222'><w:r><w:t>{second}</w:t></w:r></w:p>"
        + "<w:sectPr/></w:body></w:document>";

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

    private static void Write(ZipArchive archive, string name, string value) =>
        Write(archive, name, Encoding.UTF8.GetBytes(value));

    private static void Write(ZipArchive archive, string name, byte[] value)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var target = entry.Open();
        target.Write(value);
    }

    private static JsonDocument ToJson(object value) => JsonDocument.Parse(
        JsonSerializer.Serialize(value)
    );

    private sealed class MergeFiles : IDisposable
    {
        private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);

        public MergeFiles(
            string stem,
            string ancestorPath,
            string leftPath,
            string rightPath
        )
        {
            Stem = stem;
            AncestorPath = ancestorPath;
            LeftPath = leftPath;
            RightPath = rightPath;
            Track(ancestorPath);
            Track(leftPath);
            Track(rightPath);
        }

        public string Stem { get; }

        public string AncestorPath { get; }

        public string LeftPath { get; }

        public string RightPath { get; }

        public string OutputPath(string suffix, string? extension = null)
        {
            var path = Stem + "-" + suffix + (extension ?? Path.GetExtension(AncestorPath));
            Track(path);
            return path;
        }

        private void Track(string path) => _paths.Add(path);

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
            "Saved-package merge actions must not invoke the Word COM host."
        );

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
