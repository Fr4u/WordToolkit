using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class ReviewDecisionServiceTests
{
    [Fact]
    public async Task PlansCompactRedactedAcceptAllWithMicrosoftSchemaValidation()
    {
        var path = Fixture("real_tracked_changes.docx");
        var package = new OpcPackageReader().Read(path);
        var graph = BuildGraph(package);
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            local_path = path,
            expected_package_fingerprint = package.Fingerprint,
            decision = "accept",
            select_all = true,
        }));

        var result = await new WordLiveService(new NoInvokeHost()).CallAsync(
            "plan_ooxml_review_decisions",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        var root = json.RootElement;
        var raw = root.GetRawText();

        Assert.StartsWith("wrplan_", root.GetProperty("plan_id").GetString());
        Assert.True(root.GetProperty("can_apply").GetBoolean());
        Assert.True(root.GetProperty("has_changes").GetBoolean());
        Assert.False(root.GetProperty("apply_blocked").GetBoolean());
        Assert.Equal(graph.Revisions.Count, root.GetProperty("selected_revision_count").GetInt32());
        Assert.True(root.GetProperty("candidate_validation").GetProperty("performed").GetBoolean());
        Assert.False(root.GetProperty("candidate_validation").GetProperty("valid").GetBoolean());
        Assert.True(root.GetProperty("candidate_validation").GetProperty("no_new_errors").GetBoolean());
        Assert.Equal(
            12,
            root.GetProperty("candidate_validation").GetProperty("baseline_error_count").GetInt32()
        );
        Assert.False(root.GetProperty("word_opened").GetBoolean());
        Assert.False(root.GetProperty("mutation_performed").GetBoolean());
        Assert.False(root.GetProperty("sensitive_values_included").GetBoolean());
        Assert.True(raw.Length < 6_000, $"Compact review plan is too large: {raw.Length}");
        foreach (var author in graph.Revisions.Select(revision => revision.Author).Where(author => author is not null))
        {
            Assert.DoesNotContain(author!, raw, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("<w:", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppliesReviewedPlanAtomicallyWithoutOpeningWord()
    {
        var path = TemporaryCopy("real_tracked_changes.docx");
        string? backupPath = null;
        try
        {
            var beforeBytes = File.ReadAllBytes(path);
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var service = new WordLiveService(new NoInvokeHost());
            using var planArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                decision = "accept",
                select_all = true,
            }));
            var planObject = await service.CallAsync(
                "plan_ooxml_review_decisions",
                planArguments.RootElement,
                CancellationToken.None
            );
            using var planJson = JsonDocument.Parse(JsonSerializer.Serialize(planObject));
            var planId = planJson.RootElement.GetProperty("plan_id").GetString();
            using var applyArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                expected_plan_id = planId,
                decision = "accept",
                select_all = true,
            }));

            var applyObject = await service.CallAsync(
                "apply_ooxml_review_decisions",
                applyArguments.RootElement,
                CancellationToken.None
            );
            using var applyJson = JsonDocument.Parse(JsonSerializer.Serialize(applyObject));
            var root = applyJson.RootElement;
            backupPath = root.GetProperty("backup_path").GetString();
            var after = reader.Read(path);
            var afterGraph = BuildGraph(after);

            Assert.True(root.GetProperty("applied").GetBoolean());
            Assert.False(root.GetProperty("no_op").GetBoolean());
            Assert.False(root.GetProperty("word_opened").GetBoolean());
            Assert.False(root.GetProperty("microsoft_schema_valid").GetBoolean());
            Assert.True(root.GetProperty("microsoft_schema_no_new_errors").GetBoolean());
            Assert.Equal(
                root.GetProperty("predicted_package_fingerprint").GetString(),
                after.Fingerprint
            );
            Assert.NotNull(backupPath);
            Assert.True(File.Exists(backupPath));
            Assert.Equal(beforeBytes, File.ReadAllBytes(backupPath));
            Assert.Empty(afterGraph.Revisions);
            Assert.Empty(afterGraph.MoveRanges);
        }
        finally
        {
            DeleteIfExists(backupPath);
            DeleteIfExists(path);
            DeleteIfExists(path + ".wordtoolkit.lock");
        }
    }

    [Fact]
    public async Task ReportsUnsupportedParagraphMergesWithoutRunningCandidateValidation()
    {
        var path = Fixture("poi_tracked_changes_delins.docx");
        var package = new OpcPackageReader().Read(path);
        var graph = BuildGraph(package);
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            local_path = path,
            expected_package_fingerprint = package.Fingerprint,
            decision = "accept",
            select_all = true,
        }));

        var result = await new WordLiveService(new NoInvokeHost()).CallAsync(
            "plan_ooxml_review_decisions",
            arguments.RootElement,
            CancellationToken.None
        );
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        var root = json.RootElement;

        Assert.False(root.GetProperty("can_apply").GetBoolean());
        Assert.True(root.GetProperty("apply_blocked").GetBoolean());
        Assert.Contains(
            root.GetProperty("block_codes").EnumerateArray(),
            item => item.GetProperty("code").GetString() == "unsupported_structural_deletion"
        );
        Assert.False(root.GetProperty("candidate_validation").GetProperty("performed").GetBoolean());
        Assert.Equal(
            "review_plan_blocked",
            root.GetProperty("candidate_validation").GetProperty("not_performed_reason").GetString()
        );
        var raw = root.GetRawText();
        foreach (
            var author in graph.Revisions
                .Select(revision => revision.Author)
                .Where(author => author is not null)
        )
        {
            Assert.DoesNotContain(author!, raw, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SignedPackageIsStructurallyPlannedButNeverReportedAsApplicable()
    {
        var path = TemporaryCopy("real_tracked_changes.docx");
        try
        {
            MarkPackageSigned(path);
            var beforeBytes = File.ReadAllBytes(path);
            var package = new OpcPackageReader().Read(path);
            var service = new WordLiveService(new NoInvokeHost());
            using var planArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                decision = "accept",
                select_all = true,
            }));
            var planObject = await service.CallAsync(
                "plan_ooxml_review_decisions",
                planArguments.RootElement,
                CancellationToken.None
            );
            using var planJson = JsonDocument.Parse(JsonSerializer.Serialize(planObject));
            var plan = planJson.RootElement;

            Assert.True(plan.GetProperty("structural_plan_supported").GetBoolean());
            Assert.False(plan.GetProperty("can_apply").GetBoolean());
            Assert.True(plan.GetProperty("apply_blocked").GetBoolean());
            Assert.Contains(
                plan.GetProperty("apply_blocked_reasons").EnumerateArray(),
                reason => reason.GetString() == "digital_signature_present"
            );
            Assert.False(plan.GetProperty("candidate_validation").GetProperty("performed").GetBoolean());
            using var applyArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                expected_plan_id = plan.GetProperty("plan_id").GetString(),
                decision = "accept",
                select_all = true,
            }));

            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "apply_ooxml_review_decisions",
                    applyArguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("SIGNED_PACKAGE", exception.ErrorCode);
            Assert.Equal(beforeBytes, File.ReadAllBytes(path));
        }
        finally
        {
            DeleteIfExists(path);
            DeleteIfExists(path + ".wordtoolkit.lock");
        }
    }

    [Fact]
    public async Task SelectsByRedactedAuthorFingerprintAndRejectsImplicitAll()
    {
        var path = Fixture("real_tracked_changes.docx");
        var package = new OpcPackageReader().Read(path);
        var service = new WordLiveService(new NoInvokeHost());
        using var inspectArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            local_path = path,
            view = "revisions",
            max_items = 1,
        }));
        var inspectObject = await service.CallAsync(
            "inspect_ooxml_review",
            inspectArguments.RootElement,
            CancellationToken.None
        );
        using var inspectJson = JsonDocument.Parse(JsonSerializer.Serialize(inspectObject));
        var fingerprint = inspectJson.RootElement.GetProperty("items")[0]
            .GetProperty("author_fingerprint")
            .GetString();
        using var planArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            local_path = path,
            expected_package_fingerprint = package.Fingerprint,
            decision = "accept",
            author_fingerprints = new[] { fingerprint },
        }));
        var planObject = await service.CallAsync(
            "plan_ooxml_review_decisions",
            planArguments.RootElement,
            CancellationToken.None
        );
        using var planJson = JsonDocument.Parse(JsonSerializer.Serialize(planObject));

        Assert.True(planJson.RootElement.GetProperty("selected_revision_count").GetInt32() > 0);
        Assert.DoesNotContain("author", planJson.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);

        using var unsafeArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            local_path = path,
            expected_package_fingerprint = package.Fingerprint,
            decision = "accept",
        }));
        var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
            service.CallAsync(
                "plan_ooxml_review_decisions",
                unsafeArguments.RootElement,
                CancellationToken.None
            )
        );
        Assert.Equal("INVALID_INPUT", exception.ErrorCode);
    }

    [Fact]
    public async Task EnforcesSelectorArrayBoundsWithoutRelyingOnJsonSchema()
    {
        var path = Fixture("real_tracked_changes.docx");
        var package = new OpcPackageReader().Read(path);
        var service = new WordLiveService(new NoInvokeHost());
        var selectorCases = new[]
        {
            (
                Values: Enumerable.Range(0, 201).Select(index => $"wdr_{index}").ToArray(),
                Message: "at most 200 values"
            ),
            (
                Values: Enumerable.Repeat(BuildGraph(package).Revisions[0].Id, 2).ToArray(),
                Message: "unique values"
            ),
        };

        foreach (var selectorCase in selectorCases)
        {
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                decision = "accept",
                revision_ids = selectorCase.Values,
            }));

            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "plan_ooxml_review_decisions",
                    arguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("INVALID_INPUT", exception.ErrorCode);
            Assert.Contains(selectorCase.Message, exception.Message, StringComparison.Ordinal);
        }
    }

    private static WordReviewGraph BuildGraph(OpcPackageSnapshot package) =>
        new WordReviewGraphBuilder().Build(
            package,
            new WordSemanticProjector().Project(package)
        );

    private static string Fixture(string name) => Path.Combine(
        FindRepositoryRoot(),
        "tests",
        "upstream",
        "fixtures",
        name
    );

    private static string TemporaryCopy(string fixtureName)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-review-{Guid.NewGuid():N}.docx"
        );
        File.Copy(Fixture(fixtureName), path, overwrite: false);
        return path;
    }

    private static void MarkPackageSigned(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update);
        var contentTypesEntry = archive.GetEntry("[Content_Types].xml")
            ?? throw new InvalidDataException("Fixture has no content-types part.");
        string contentTypes;
        using (var reader = new StreamReader(
            contentTypesEntry.Open(),
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: false
        ))
        {
            contentTypes = reader.ReadToEnd();
        }
        contentTypesEntry.Delete();
        var replacement = archive.CreateEntry("[Content_Types].xml");
        using (var writer = new StreamWriter(
            replacement.Open(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: false
        ))
        {
            writer.Write(contentTypes.Replace(
                "</Types>",
                "<Override PartName=\"/_xmlsignatures/sig1.xml\" "
                    + "ContentType=\"application/vnd.openxmlformats-package.digital-signature-xmlsignature+xml\"/>"
                    + "</Types>",
                StringComparison.Ordinal
            ));
        }
        var signature = archive.CreateEntry("_xmlsignatures/sig1.xml");
        using var signatureWriter = new StreamWriter(
            signature.Open(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: false
        );
        signatureWriter.Write(
            "<Signature xmlns=\"http://www.w3.org/2000/09/xmldsig#\"/>"
        );
    }

    private static void DeleteIfExists(string? path)
    {
        if (path is not null && File.Exists(path))
        {
            File.Delete(path);
        }
    }

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

    private sealed class NoInvokeHost : IWordComHost
    {
        public Task<T> InvokeAsync<T>(
            Func<dynamic, T> operation,
            CancellationToken cancellationToken = default,
            bool launchIfMissing = false
        ) => throw new Xunit.Sdk.XunitException(
            "Saved-package review decisions must not invoke the Word COM host."
        );

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
