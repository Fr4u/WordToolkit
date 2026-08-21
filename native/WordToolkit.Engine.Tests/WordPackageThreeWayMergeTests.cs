using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordPackageThreeWayMergeTests
{
    [Fact]
    public void IdenticalInputsProduceDeterministicNoOp()
    {
        var ancestor = Read(BuildPackage("alpha", "beta"));

        var first = Plan(ancestor, ancestor, ancestor);
        var second = Plan(ancestor, ancestor, ancestor);

        Assert.Equal(first.MergeId, second.MergeId);
        Assert.Empty(first.Conflicts);
        Assert.True(first.CanMaterialize);
        Assert.True(first.Patch!.IsNoOp);
        Assert.Equal(ancestor.Package.Fingerprint, first.ResultPackageFingerprint);
        Assert.True(first.Evaluate().CanApply);
    }

    [Fact]
    public void ChangeOnOnlyOneBranchIsSelectedWithoutConflict()
    {
        var ancestor = Read(BuildPackage("alpha", "beta"));
        var left = Read(BuildPackage("left", "beta"));

        var plan = Plan(ancestor, left, ancestor);

        Assert.Empty(plan.Conflicts);
        Assert.Equal(left.Package.Fingerprint, plan.ResultPackageFingerprint);
        Assert.Contains(plan.EntryDecisions, decision =>
            decision.EntryName == "word/document.xml"
            && decision.Outcome == WordPackageMergeEntryOutcome.Left
        );
    }

    [Fact]
    public void DisjointLosslessTextChangesInOnePartAreMergedSemantically()
    {
        var ancestor = Read(BuildPackage("alpha", "beta"));
        var left = Read(BuildPackage("left", "beta"));
        var right = Read(BuildPackage("alpha", "right"));

        var plan = Plan(ancestor, left, right);

        Assert.Empty(plan.Conflicts);
        Assert.True(plan.CanMaterialize);
        Assert.Contains(plan.EntryDecisions, decision =>
            decision.EntryName == "word/document.xml"
            && decision.Outcome == WordPackageMergeEntryOutcome.SemanticTextMerge
            && decision.SemanticTextChangeCount == 2
        );
        Assert.Equal(
            ["left", "right"],
            TextValues(new WordSemanticProjector().Project(plan.CandidatePackage!))
        );
        Assert.False(plan.Patch!.IsNoOp);
        Assert.True(plan.Evaluate().CanApply);
    }

    [Fact]
    public void DivergentChangeToSameTextNodeRequiresExplicitResolution()
    {
        var ancestor = Read(BuildPackage("alpha", "beta"));
        var left = Read(BuildPackage("left", "beta"));
        var right = Read(BuildPackage("right", "beta"));
        var planner = new WordPackageThreeWayMergePlanner();

        var unresolved = planner.Plan(
            ancestor.Package,
            ancestor.Document,
            left.Package,
            left.Document,
            right.Package,
            right.Document
        );

        var conflict = Assert.Single(unresolved.Conflicts);
        Assert.Equal(
            WordPackageMergeConflictKind.SemanticTextChangedDifferently,
            conflict.Kind
        );
        Assert.StartsWith("wtmc_", conflict.ConflictId, StringComparison.Ordinal);
        Assert.False(unresolved.CanMaterialize);
        Assert.Equal(
            ["unresolved_merge_conflicts"],
            unresolved.Evaluate().BlockCodes
        );
        Assert.Equal(5, conflict.AncestorText!.CharacterCount);
        Assert.Equal("alpha", conflict.AncestorText.Preview);

        var resolved = planner.Plan(
            ancestor.Package,
            ancestor.Document,
            left.Package,
            left.Document,
            right.Package,
            right.Document,
            [new WordPackageMergeResolution(
                conflict.ConflictId,
                WordPackageMergeResolutionChoice.UseLeft
            )]
        );

        Assert.True(resolved.CanMaterialize);
        Assert.Equal(1, resolved.ResolvedConflictCount);
        Assert.Empty(resolved.UnresolvedConflictIds);
        Assert.Equal(
            ["left", "beta"],
            TextValues(new WordSemanticProjector().Project(resolved.CandidatePackage!))
        );
        Assert.True(resolved.Evaluate().CanApply);
        Assert.NotEqual(unresolved.MergeId, resolved.MergeId);
    }

    [Fact]
    public void SemanticMergePreservesUnknownMarkupOutsideExactTextRanges()
    {
        var ancestorXml = DocumentXml("alpha", "beta").Replace(
            "xmlns:w14=",
            "xmlns:private='urn:private-extension' private:flag='keep-me' xmlns:w14=",
            StringComparison.Ordinal
        );
        var leftXml = ancestorXml.Replace(">alpha<", ">left<", StringComparison.Ordinal);
        var rightXml = ancestorXml.Replace(">beta<", ">right<", StringComparison.Ordinal);
        var expectedXml = leftXml.Replace(">beta<", ">right<", StringComparison.Ordinal);
        var ancestor = Read(BuildRawPackage(ancestorXml));
        var left = Read(BuildRawPackage(leftXml));
        var right = Read(BuildRawPackage(rightXml));

        var plan = Plan(ancestor, left, right);

        Assert.Empty(plan.Conflicts);
        var actualXml = Encoding.UTF8.GetString(
            plan.CandidatePackage!.Parts["/word/document.xml"].Entry.Content.Span
        );
        Assert.Equal(expectedXml, actualXml);
        Assert.Contains("private:flag='keep-me'", actualXml, StringComparison.Ordinal);
    }

    [Fact]
    public void HiddenMarkupChangePreventsTextOnlyAutoMerge()
    {
        var ancestorXml = DocumentXml("alpha", "beta");
        var leftXml = ancestorXml.Replace(">alpha<", ">left<", StringComparison.Ordinal)
            .Replace(
                "w14:paraId='11111111'",
                "w14:paraId='11111111' w:rsidR='00112233'",
                StringComparison.Ordinal
            );
        var rightXml = ancestorXml.Replace(">beta<", ">right<", StringComparison.Ordinal);
        var ancestor = Read(BuildRawPackage(ancestorXml));
        var left = Read(BuildRawPackage(leftXml));
        var right = Read(BuildRawPackage(rightXml));

        var plan = Plan(ancestor, left, right);

        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal("word/document.xml", conflict.EntryName);
        Assert.Equal(WordPackageMergeConflictKind.ModifiedDifferently, conflict.Kind);
        Assert.False(plan.CanMaterialize);
    }

    [Fact]
    public void DivergentOpaquePayloadIsAnEntryConflictAndRetainsRiskGate()
    {
        var ancestor = Read(BuildPackage(
            "alpha",
            "beta",
            new Dictionary<string, byte[]> { ["custom/opaque.bin"] = [1] }
        ));
        var left = Read(BuildPackage(
            "alpha",
            "beta",
            new Dictionary<string, byte[]> { ["custom/opaque.bin"] = [2] }
        ));
        var right = Read(BuildPackage(
            "alpha",
            "beta",
            new Dictionary<string, byte[]> { ["custom/opaque.bin"] = [3] }
        ));
        var planner = new WordPackageThreeWayMergePlanner();
        var unresolved = planner.Plan(
            ancestor.Package,
            ancestor.Document,
            left.Package,
            left.Document,
            right.Package,
            right.Document
        );
        var conflict = Assert.Single(unresolved.Conflicts);

        Assert.Equal(WordPackageMergeConflictKind.ModifiedDifferently, conflict.Kind);
        var resolved = planner.Plan(
            ancestor.Package,
            ancestor.Document,
            left.Package,
            left.Document,
            right.Package,
            right.Document,
            [new WordPackageMergeResolution(
                conflict.ConflictId,
                WordPackageMergeResolutionChoice.UseRight
            )]
        );

        Assert.Equal(1, resolved.ResultPlan!.RiskAssessment.OpaqueBinaryOperationCount);
        Assert.Equal(
            ["opaque_binary_change_not_authorized"],
            resolved.Evaluate().BlockCodes
        );
        Assert.True(resolved.Evaluate(new WordPackagePatchApplyPolicy
        {
            AllowOpaqueBinaryChanges = true,
        }).CanApply);
    }

    [Fact]
    public void DeleteVersusModifyIsClassifiedWithoutGuessing()
    {
        var ancestor = Read(BuildPackage(
            "alpha",
            "beta",
            new Dictionary<string, byte[]>
            {
                ["custom/data.xml"] = Utf8("<data/>")
            }
        ));
        var left = Read(BuildPackage("alpha", "beta"));
        var right = Read(BuildPackage(
            "alpha",
            "beta",
            new Dictionary<string, byte[]>
            {
                ["custom/data.xml"] = Utf8("<data value='right'/>")
            }
        ));

        var plan = Plan(ancestor, left, right);

        var conflict = Assert.Single(plan.Conflicts, value =>
            value.EntryName == "custom/data.xml"
        );
        Assert.Equal(
            WordPackageMergeConflictKind.DeletedOnLeftModifiedOnRight,
            conflict.Kind
        );
    }

    [Fact]
    public void ResolutionOrderDoesNotChangeMergeIdentifier()
    {
        var ancestor = Read(BuildPackage(
            "alpha",
            "beta",
            new Dictionary<string, byte[]>
            {
                ["custom/a.bin"] = [1],
                ["custom/b.bin"] = [1],
            }
        ));
        var left = Read(BuildPackage(
            "alpha",
            "beta",
            new Dictionary<string, byte[]>
            {
                ["custom/a.bin"] = [2],
                ["custom/b.bin"] = [2],
            }
        ));
        var right = Read(BuildPackage(
            "alpha",
            "beta",
            new Dictionary<string, byte[]>
            {
                ["custom/a.bin"] = [3],
                ["custom/b.bin"] = [3],
            }
        ));
        var planner = new WordPackageThreeWayMergePlanner();
        var initial = Plan(ancestor, left, right);
        Assert.Equal(2, initial.ConflictCount);
        var resolutions = initial.Conflicts.Select(conflict =>
            new WordPackageMergeResolution(
                conflict.ConflictId,
                WordPackageMergeResolutionChoice.UseLeft
            )
        ).ToArray();

        var first = planner.Plan(
            ancestor.Package,
            ancestor.Document,
            left.Package,
            left.Document,
            right.Package,
            right.Document,
            resolutions
        );
        var second = planner.Plan(
            ancestor.Package,
            ancestor.Document,
            left.Package,
            left.Document,
            right.Package,
            right.Document,
            resolutions.Reverse()
        );

        Assert.Equal(first.MergeId, second.MergeId);
        Assert.Equal(first.ResultPackageFingerprint, second.ResultPackageFingerprint);
    }

    [Fact]
    public void RejectsUnknownAndDuplicateConflictResolutions()
    {
        var ancestor = Read(BuildPackage("alpha", "beta"));
        var left = Read(BuildPackage("left", "beta"));
        var right = Read(BuildPackage("right", "beta"));
        var planner = new WordPackageThreeWayMergePlanner();
        var initial = Plan(ancestor, left, right);
        var conflictId = Assert.Single(initial.Conflicts).ConflictId;

        Assert.Throws<WordPackageMergePreconditionException>(() => planner.Plan(
            ancestor.Package,
            ancestor.Document,
            left.Package,
            left.Document,
            right.Package,
            right.Document,
            [new WordPackageMergeResolution(
                "wtmc_unknown",
                WordPackageMergeResolutionChoice.UseLeft
            )]
        ));
        Assert.Throws<WordPackageMergePreconditionException>(() => planner.Plan(
            ancestor.Package,
            ancestor.Document,
            left.Package,
            left.Document,
            right.Package,
            right.Document,
            [
                new WordPackageMergeResolution(
                    conflictId,
                    WordPackageMergeResolutionChoice.UseLeft
                ),
                new WordPackageMergeResolution(
                    conflictId,
                    WordPackageMergeResolutionChoice.UseRight
                ),
            ]
        ));
    }

    [Fact]
    public void MacroSelectionRetainsIndependentActiveContentAuthorization()
    {
        const string macroType = "application/vnd.ms-office.vbaProject";
        var overrides = new Dictionary<string, string>
        {
            ["/word/vbaProject.bin"] = macroType,
        };
        var ancestor = Read(BuildPackage(
            "alpha",
            "beta",
            new Dictionary<string, byte[]> { ["word/vbaProject.bin"] = [1] },
            overrides
        ));
        var left = Read(BuildPackage(
            "alpha",
            "beta",
            new Dictionary<string, byte[]> { ["word/vbaProject.bin"] = [2] },
            overrides
        ));

        var plan = Plan(ancestor, left, ancestor);

        Assert.Equal(1, plan.ResultPlan!.RiskAssessment.MacroOperationCount);
        Assert.Equal(
            ["active_content_change_not_authorized"],
            plan.Evaluate().BlockCodes
        );
        Assert.True(plan.Evaluate(new WordPackagePatchApplyPolicy
        {
            AllowActiveContentChanges = true,
        }).CanApply);
    }

    [Fact]
    public void EnforcesEntryBudgetAndCancellation()
    {
        var ancestor = Read(BuildPackage("alpha", "beta"));
        var limited = new WordPackageThreeWayMergePlanner(
            new WordPackageMergeOptions { MaxEntries = 2 }
        );
        Assert.Throws<WordPackageMergeLimitException>(() => limited.Plan(
            ancestor.Package,
            ancestor.Document,
            ancestor.Package,
            ancestor.Document,
            ancestor.Package,
            ancestor.Document
        ));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            new WordPackageThreeWayMergePlanner().Plan(
                ancestor.Package,
                ancestor.Document,
                ancestor.Package,
                ancestor.Document,
                ancestor.Package,
                ancestor.Document,
                cancellationToken: cancellation.Token
            )
        );
    }

    private static WordPackageMergePlan Plan(
        PackageView ancestor,
        PackageView left,
        PackageView right
    ) => new WordPackageThreeWayMergePlanner().Plan(
        ancestor.Package,
        ancestor.Document,
        left.Package,
        left.Document,
        right.Package,
        right.Document
    );

    private static string[] TextValues(WordSemanticDocument document) =>
        document.Nodes.Where(node => node.Kind == WordSemanticNodeKind.Text)
            .Select(node => node.Text ?? string.Empty)
            .ToArray();

    private static PackageView Read(MemoryStream stream)
    {
        using (stream)
        {
            var package = new OpcPackageReader().Read(stream);
            return new PackageView(package, new WordSemanticProjector().Project(package));
        }
    }

    private static MemoryStream BuildPackage(
        string first,
        string second,
        IReadOnlyDictionary<string, byte[]>? extras = null,
        IReadOnlyDictionary<string, string>? overrides = null
    ) => BuildRawPackage(DocumentXml(first, second), extras, overrides);

    private static MemoryStream BuildRawPackage(
        string documentXml,
        IReadOnlyDictionary<string, byte[]>? extras = null,
        IReadOnlyDictionary<string, string>? overrides = null
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml", ContentTypes(extras, overrides));
            Write(archive, "_rels/.rels", RootRelationships());
            Write(archive, "word/document.xml", documentXml);
            foreach (var (name, content) in extras ?? new Dictionary<string, byte[]>())
            {
                Write(archive, name, content);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static string DocumentXml(string first, string second) =>
        "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main' "
        + "xmlns:w14='http://schemas.microsoft.com/office/word/2010/wordml'>"
        + $"<w:body><w:p w14:paraId='11111111'><w:r><w:t>{first}</w:t></w:r></w:p>"
        + $"<w:p w14:paraId='22222222'><w:r><w:t>{second}</w:t></w:r></w:p>"
        + "</w:body></w:document>";

    private static string ContentTypes(
        IReadOnlyDictionary<string, byte[]>? extras,
        IReadOnlyDictionary<string, string>? overrides
    )
    {
        var hasBin = extras?.Keys.Any(name =>
            name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
        ) == true;
        var builder = new StringBuilder(
            "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>"
        );
        builder.Append("<Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/>");
        builder.Append("<Default Extension='xml' ContentType='application/xml'/>");
        if (hasBin)
        {
            builder.Append("<Default Extension='bin' ContentType='application/octet-stream'/>");
        }
        builder.Append("<Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/>");
        foreach (var (partUri, contentType) in overrides ?? new Dictionary<string, string>())
        {
            builder.Append($"<Override PartName='{partUri}' ContentType='{contentType}'/>");
        }
        builder.Append("</Types>");
        return builder.ToString();
    }

    private static string RootRelationships() =>
        "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
        + "<Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='word/document.xml'/>"
        + "</Relationships>";

    private static void Write(ZipArchive archive, string name, string value) =>
        Write(archive, name, Utf8(value));

    private static void Write(ZipArchive archive, string name, byte[] value)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var output = entry.Open();
        output.Write(value);
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private sealed record PackageView(
        OpcPackageSnapshot Package,
        WordSemanticDocument Document
    );
}
