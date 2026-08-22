using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordPackagePatchPlanTests
{
    [Fact]
    public void SafeDocumentChangeHasSemanticEvidenceAndPassesDefaultPolicy()
    {
        var (beforePackage, beforeDocument) = Read(BuildPackage("before"));
        var (afterPackage, afterDocument) = Read(BuildPackage("after"));

        var plan = new WordPackagePatchPlanner().Plan(
            beforePackage,
            beforeDocument,
            afterPackage,
            afterDocument
        );
        var decision = plan.Evaluate();

        Assert.False(plan.Patch.IsNoOp);
        Assert.False(plan.SemanticDiff.SemanticallyEquivalent);
        Assert.True(plan.SemanticDiff.MatchingComplete);
        Assert.Empty(plan.RiskAssessment.Items);
        Assert.True(decision.CanApply);
        Assert.Empty(decision.BlockCodes);
    }

    [Theory]
    [InlineData("readOnly")]
    [InlineData("comments")]
    [InlineData("trackedChanges")]
    [InlineData("forms")]
    public void EnforcedDocumentProtectionRequiresExplicitAuthorization(string editMode)
    {
        var before = Read(BuildProtectedPackage("before", editMode, enforced: true));
        var after = Read(BuildProtectedPackage("after", editMode, enforced: true));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.True(plan.RiskAssessment.Protection.BaseDocumentProtectionEnforced);
        Assert.Equal(editMode, plan.RiskAssessment.Protection.BaseDocumentProtectionEditMode);
        Assert.True(plan.RiskAssessment.Protection.AuthorizationRequired);
        Assert.False(plan.RiskAssessment.Protection.DocumentProtectionMetadataChanged);
        Assert.Contains(
            "protected_document_edit_not_authorized",
            plan.Evaluate().BlockCodes
        );
        Assert.True(plan.Evaluate(new WordPackagePatchApplyPolicy
        {
            AllowProtectedDocumentEdit = true,
        }).CanApply);
    }

    [Theory]
    [InlineData("readOnly")]
    [InlineData("comments")]
    [InlineData("trackedChanges")]
    [InlineData("forms")]
    public void NonEnforcedProtectionDoesNotBlockOrdinaryContentChanges(string editMode)
    {
        var before = Read(BuildProtectedPackage("before", editMode, enforced: false));
        var after = Read(BuildProtectedPackage("after", editMode, enforced: false));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.False(plan.RiskAssessment.Protection.BaseDocumentProtectionEnforced);
        Assert.False(plan.RiskAssessment.Protection.AuthorizationRequired);
        Assert.True(plan.Evaluate().CanApply);
    }

    [Fact]
    public void PermissionRangesRequireAuthorizationBecausePackageMutationHasNoUserIdentity()
    {
        var before = Read(BuildPackage(
            "unused",
            documentXml: PermissionDocumentXml("before", includeEnd: true)
        ));
        var after = Read(BuildPackage(
            "unused",
            documentXml: PermissionDocumentXml("after", includeEnd: true)
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.Equal(1, plan.RiskAssessment.Protection.BasePermissionRangeCount);
        Assert.Equal(0, plan.RiskAssessment.Protection.MalformedPermissionRangeCount);
        Assert.Contains(
            "protected_document_edit_not_authorized",
            plan.Evaluate().BlockCodes
        );
        Assert.True(plan.Evaluate(new WordPackagePatchApplyPolicy
        {
            AllowProtectedDocumentEdit = true,
        }).CanApply);
    }

    [Fact]
    public void MalformedPermissionRangesAreNonOverridable()
    {
        var before = Read(BuildPackage(
            "unused",
            documentXml: PermissionDocumentXml("before", includeEnd: false)
        ));
        var after = Read(BuildPackage(
            "unused",
            documentXml: PermissionDocumentXml("after", includeEnd: false)
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );
        var decision = plan.Evaluate(new WordPackagePatchApplyPolicy
        {
            AllowProtectedDocumentEdit = true,
        });

        Assert.True(plan.RiskAssessment.Protection.HasMalformedPermissionMetadata);
        Assert.Contains(
            "PERMISSION_RANGE_INCOMPLETE",
            plan.RiskAssessment.Protection.PermissionIssueCodes
        );
        Assert.False(decision.CanApply);
        Assert.Contains("protection_metadata_malformed", decision.BlockCodes);
    }

    [Fact]
    public void InvalidCompletePermissionAttributesAreNonOverridable()
    {
        var before = Read(BuildPackage(
            "unused",
            documentXml: InvalidPermissionDocumentXml("before")
        ));
        var after = Read(BuildPackage(
            "unused",
            documentXml: InvalidPermissionDocumentXml("after")
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );
        var decision = plan.Evaluate(new WordPackagePatchApplyPolicy
        {
            AllowProtectedDocumentEdit = true,
        });

        Assert.True(plan.RiskAssessment.Protection.HasMalformedPermissionMetadata);
        Assert.Contains(
            "PERMISSION_COLUMN_RANGE_INVALID",
            plan.RiskAssessment.Protection.PermissionIssueCodes
        );
        Assert.False(decision.CanApply);
        Assert.Contains("protection_metadata_malformed", decision.BlockCodes);
    }

    [Fact]
    public void ReversedPermissionColumnsAreNonOverridable()
    {
        var before = Read(BuildPackage(
            "unused",
            documentXml: ReversedPermissionDocumentXml("before")
        ));
        var after = Read(BuildPackage(
            "unused",
            documentXml: ReversedPermissionDocumentXml("after")
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );
        var decision = plan.Evaluate(new WordPackagePatchApplyPolicy
        {
            AllowProtectedDocumentEdit = true,
        });

        Assert.True(plan.RiskAssessment.Protection.HasMalformedPermissionMetadata);
        Assert.Contains(
            "PERMISSION_COLUMN_RANGE_INVALID",
            plan.RiskAssessment.Protection.PermissionIssueCodes
        );
        Assert.False(decision.CanApply);
        Assert.Contains("protection_metadata_malformed", decision.BlockCodes);
    }

    [Fact]
    public void ProtectedNoOpDoesNotDemandMeaninglessAuthorization()
    {
        var snapshot = Read(BuildProtectedPackage("same", "readOnly", enforced: true));

        var plan = new WordPackagePatchPlanner().Plan(
            snapshot.Package,
            snapshot.Document,
            snapshot.Package,
            snapshot.Document
        );

        Assert.True(plan.Patch.IsNoOp);
        Assert.False(plan.RiskAssessment.Protection.AuthorizationRequired);
        Assert.True(plan.Evaluate().CanApply);
    }

    [Fact]
    public void ChangingProtectionMetadataRequiresAuthorizationEvenWhenNotEnforced()
    {
        var before = Read(BuildPackage(
            "same",
            new Dictionary<string, byte[]>
            {
                ["word/settings.xml"] = Utf8(SettingsXml(null, enforced: false)),
            },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));
        var after = Read(BuildProtectedPackage("same", "readOnly", enforced: false));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.True(plan.RiskAssessment.Protection.DocumentProtectionMetadataChanged);
        Assert.True(plan.RiskAssessment.Protection.AuthorizationRequired);
        Assert.Contains(
            "protected_document_edit_not_authorized",
            plan.Evaluate().BlockCodes
        );
    }

    [Fact]
    public void MacroChangeRequiresOnlyExplicitActiveContentAuthorization()
    {
        var extrasBefore = new Dictionary<string, byte[]>
        {
            ["word/vbaProject.bin"] = [1, 2, 3],
        };
        var extrasAfter = new Dictionary<string, byte[]>
        {
            ["word/vbaProject.bin"] = [1, 2, 4],
        };
        var overrides = new Dictionary<string, string>
        {
            ["/word/vbaProject.bin"] = "application/vnd.ms-office.vbaProject",
        };
        var before = Read(BuildPackage("same", extrasBefore, overrides));
        var after = Read(BuildPackage("same", extrasAfter, overrides));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.Equal(1, plan.RiskAssessment.MacroOperationCount);
        Assert.False(plan.RiskAssessment.OpaqueBinaryChanged);
        Assert.Contains(plan.RiskAssessment.Items, item =>
            item.Code == "macro_content_changed"
            && item.Severity == WordPackagePatchRiskSeverity.Block
        );
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
    public void OleAndActiveXChangesRemainSeparateButShareActiveContentGate()
    {
        var overrides = new Dictionary<string, string>
        {
            ["/word/embeddings/oleObject1.bin"] =
                "application/vnd.openxmlformats-officedocument.oleObject",
            ["/word/activeX/activeX1.bin"] =
                "application/vnd.ms-office.activeX",
        };
        var before = Read(BuildPackage(
            "same",
            new Dictionary<string, byte[]>
            {
                ["word/embeddings/oleObject1.bin"] = [1],
                ["word/activeX/activeX1.bin"] = [2],
            },
            overrides
        ));
        var after = Read(BuildPackage(
            "same",
            new Dictionary<string, byte[]>
            {
                ["word/embeddings/oleObject1.bin"] = [3],
                ["word/activeX/activeX1.bin"] = [4],
            },
            overrides
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.Equal(1, plan.RiskAssessment.EmbeddedObjectOperationCount);
        Assert.Equal(1, plan.RiskAssessment.ActiveXOperationCount);
        Assert.Contains(plan.RiskAssessment.Items, item =>
            item.Code == "embedded_object_changed"
        );
        Assert.Contains(plan.RiskAssessment.Items, item =>
            item.Code == "activex_content_changed"
        );
        Assert.Equal(
            ["active_content_change_not_authorized"],
            plan.Evaluate().BlockCodes
        );
    }

    [Fact]
    public void ExternalRelationshipTargetChangeRequiresIndependentAuthorization()
    {
        var beforeRelationships = DocumentRelationships("https://before.invalid");
        var afterRelationships = DocumentRelationships("https://after.invalid");
        var before = Read(BuildPackage(
            "same",
            documentRelationships: beforeRelationships
        ));
        var after = Read(BuildPackage(
            "same",
            documentRelationships: afterRelationships
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.Equal(1, plan.RiskAssessment.ExternalRelationshipAddedCount);
        Assert.Equal(1, plan.RiskAssessment.ExternalRelationshipRemovedCount);
        Assert.False(plan.Evaluate().CanApply);
        Assert.Equal(
            ["external_relationship_change_not_authorized"],
            plan.Evaluate().BlockCodes
        );
        Assert.True(plan.Evaluate(new WordPackagePatchApplyPolicy
        {
            AllowExternalRelationshipChanges = true,
        }).CanApply);
    }

    [Fact]
    public void AnyChangeToSignedPackageRequiresSignatureInvalidationAuthorization()
    {
        var signature = new Dictionary<string, byte[]>
        {
            ["_xmlsignatures/sig1.xml"] = Utf8(
                "<Signature xmlns='http://www.w3.org/2000/09/xmldsig#'/>"
            ),
        };
        var before = Read(BuildPackage("before", signature));
        var after = Read(BuildPackage("after", signature));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.True(plan.RiskAssessment.DigitalSignaturesPresent);
        Assert.False(plan.RiskAssessment.DigitalSignatureMaterialChanged);
        Assert.Equal(
            ["digital_signature_invalidation_not_authorized"],
            plan.Evaluate().BlockCodes
        );
        Assert.True(plan.Evaluate(new WordPackagePatchApplyPolicy
        {
            AllowDigitalSignatureInvalidation = true,
        }).CanApply);
    }

    [Fact]
    public void SignedNoOpDoesNotDemandMeaninglessInvalidationApproval()
    {
        var signature = new Dictionary<string, byte[]>
        {
            ["_xmlsignatures/sig1.xml"] = Utf8(
                "<Signature xmlns='http://www.w3.org/2000/09/xmldsig#'/>"
            ),
        };
        var snapshot = Read(BuildPackage("same", signature));

        var plan = new WordPackagePatchPlanner().Plan(
            snapshot.Package,
            snapshot.Document,
            snapshot.Package,
            snapshot.Document
        );

        Assert.True(plan.Patch.IsNoOp);
        Assert.True(plan.RiskAssessment.DigitalSignaturesPresent);
        Assert.True(plan.Evaluate().CanApply);
    }

    [Fact]
    public void UnknownBinaryChangeHasItsOwnFailClosedGate()
    {
        var before = Read(BuildPackage(
            "same",
            new Dictionary<string, byte[]> { ["custom/opaque.bin"] = [1] }
        ));
        var after = Read(BuildPackage(
            "same",
            new Dictionary<string, byte[]> { ["custom/opaque.bin"] = [2] }
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.Equal(1, plan.RiskAssessment.OpaqueBinaryOperationCount);
        Assert.Equal(
            ["opaque_binary_change_not_authorized"],
            plan.Evaluate().BlockCodes
        );
        Assert.True(plan.Evaluate(new WordPackagePatchApplyPolicy
        {
            AllowOpaqueBinaryChanges = true,
        }).CanApply);
    }

    [Fact]
    public void ImagePayloadRemainsOpaqueUntilAFeatureParserClassifiesItsChange()
    {
        var overrides = new Dictionary<string, string>
        {
            ["/word/media/image1.png"] = "image/png",
        };
        var before = Read(BuildPackage(
            "same",
            new Dictionary<string, byte[]> { ["word/media/image1.png"] = [1] },
            overrides
        ));
        var after = Read(BuildPackage(
            "same",
            new Dictionary<string, byte[]> { ["word/media/image1.png"] = [2] },
            overrides
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.Equal(1, plan.RiskAssessment.OpaqueBinaryOperationCount);
        Assert.Equal(
            ["opaque_binary_change_not_authorized"],
            plan.Evaluate().BlockCodes
        );
    }

    [Fact]
    public void NewStructuralErrorBlocksButIdenticalBaselineErrorDoesNot()
    {
        var beforeValid = Read(BuildPackage(
            "before",
            new Dictionary<string, byte[]> { ["custom/data.bin"] = [1] },
            includeBinaryDefault: true
        ));
        var afterBroken = Read(BuildPackage(
            "after",
            new Dictionary<string, byte[]> { ["custom/data.bin"] = [2] },
            includeBinaryDefault: false
        ));
        var introduced = new WordPackagePatchPlanner().Plan(
            beforeValid.Package,
            beforeValid.Document,
            afterBroken.Package,
            afterBroken.Document
        );
        Assert.True(introduced.RiskAssessment.NewStructuralErrorCount > 0);
        var introducedDecision = introduced.Evaluate(new WordPackagePatchApplyPolicy
        {
            AllowOpaqueBinaryChanges = true,
        });
        Assert.Contains("new_structural_errors", introducedDecision.BlockCodes);

        var beforeBroken = Read(BuildPackage(
            "before",
            new Dictionary<string, byte[]> { ["custom/data.bin"] = [1] },
            includeBinaryDefault: false
        ));
        var afterSameBroken = Read(BuildPackage(
            "after",
            new Dictionary<string, byte[]> { ["custom/data.bin"] = [1] },
            includeBinaryDefault: false
        ));
        var inherited = new WordPackagePatchPlanner().Plan(
            beforeBroken.Package,
            beforeBroken.Document,
            afterSameBroken.Package,
            afterSameBroken.Document
        );

        Assert.True(inherited.RiskAssessment.BaselineStructuralErrorCount > 0);
        Assert.Equal(0, inherited.RiskAssessment.NewStructuralErrorCount);
        Assert.True(inherited.Evaluate().CanApply);
    }

    [Fact]
    public void AdditionalDuplicateDiagnosticIsNewEvenWhenItsKeyAlreadyExisted()
    {
        var before = Read(BuildPackage(
            "same",
            documentRelationships: DuplicateRelationships(2)
        ));
        var after = Read(BuildPackage(
            "same",
            documentRelationships: DuplicateRelationships(3)
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.True(plan.RiskAssessment.BaselineStructuralErrorCount > 0);
        Assert.True(
            plan.RiskAssessment.CandidateStructuralErrorCount
            > plan.RiskAssessment.BaselineStructuralErrorCount
        );
        Assert.True(plan.RiskAssessment.NewStructuralErrorCount > 0);
        Assert.Contains("new_structural_errors", plan.Evaluate().BlockCodes);
    }

    [Fact]
    public void CustomXmlIsReviewEvidenceButNotAnAutomaticBlock()
    {
        var before = Read(BuildPackage(
            "same",
            new Dictionary<string, byte[]>
            {
                ["customXml/item1.xml"] = Utf8("<root>before</root>"),
            }
        ));
        var after = Read(BuildPackage(
            "same",
            new Dictionary<string, byte[]>
            {
                ["customXml/item1.xml"] = Utf8("<root>after</root>"),
            }
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.Equal(1, plan.RiskAssessment.CustomXmlOperationCount);
        Assert.Contains(plan.RiskAssessment.Items, item =>
            item.Code == "custom_xml_changed"
            && item.Severity == WordPackagePatchRiskSeverity.Review
        );
        Assert.True(plan.Evaluate().CanApply);
    }

    [Fact]
    public void PlanApplyRecomputesSemanticAndRiskEvidenceFromArtifactPayload()
    {
        var before = Read(BuildPackage("before"));
        var after = Read(BuildPackage("after"));
        var created = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );
        using var artifact = new MemoryStream();
        new OpcPackagePatchCodec().Write(artifact, created.Patch);
        artifact.Position = 0;
        var decoded = new OpcPackagePatchCodec().Read(artifact);

        var applyPlan = new WordPackagePatchPlanner().PlanApply(
            before.Package,
            before.Document,
            decoded
        );

        Assert.Equal(decoded.PatchId, applyPlan.Patch.PatchId);
        Assert.Equal(created.SemanticDiff.DiffId, applyPlan.SemanticDiff.DiffId);
        Assert.True(applyPlan.Evaluate().CanApply);
    }

    private static (
        OpcPackageSnapshot Package,
        WordSemanticDocument Document
    ) Read(MemoryStream stream)
    {
        stream.Position = 0;
        var package = new OpcPackageReader().Read(stream);
        return (package, new WordSemanticProjector().Project(package));
    }

    private static MemoryStream BuildPackage(
        string text,
        IReadOnlyDictionary<string, byte[]>? extras = null,
        IReadOnlyDictionary<string, string>? overrides = null,
        string? documentRelationships = null,
        bool includeBinaryDefault = true,
        string? documentXml = null
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Create,
            leaveOpen: true
        ))
        {
            Write(archive, "[Content_Types].xml", ContentTypes(
                overrides,
                includeBinaryDefault
            ));
            Write(archive, "_rels/.rels", RootRelationships());
            Write(archive, "word/document.xml", documentXml ?? DocumentXml(text));
            if (documentRelationships is not null)
            {
                Write(
                    archive,
                    "word/_rels/document.xml.rels",
                    documentRelationships
                );
            }
            foreach (var extra in extras ?? new Dictionary<string, byte[]>())
            {
                Write(archive, extra.Key, extra.Value);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream BuildProtectedPackage(
        string text,
        string editMode,
        bool enforced
    ) => BuildPackage(
        text,
        new Dictionary<string, byte[]>
        {
            ["word/settings.xml"] = Utf8(SettingsXml(editMode, enforced)),
        },
        SettingsContentTypeOverride(),
        SettingsRelationships()
    );

    private static void Write(ZipArchive archive, string name, string value) =>
        Write(archive, name, Utf8(value));

    private static void Write(
        ZipArchive archive,
        string name,
        ReadOnlySpan<byte> value
    )
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        stream.Write(value);
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static string DocumentXml(string text) =>
        "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
        + $"<w:body><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:body></w:document>";

    private static string PermissionDocumentXml(string text, bool includeEnd) =>
        "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
        + "<w:body><w:p><w:permStart w:id='7' w:edGrp='everyone'/>"
        + $"<w:r><w:t>{text}</w:t></w:r>"
        + (includeEnd ? "<w:permEnd w:id='7'/>" : string.Empty)
        + "</w:p></w:body></w:document>";

    private static string InvalidPermissionDocumentXml(string text) =>
        "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
        + "<w:body><w:p><w:permStart w:id='7' w:edGrp='everyone' w:colFirst='invalid' w:colLast='2'/>"
        + $"<w:r><w:t>{text}</w:t></w:r><w:permEnd w:id='7'/>"
        + "</w:p></w:body></w:document>";

    private static string ReversedPermissionDocumentXml(string text) =>
        "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
        + "<w:body><w:p><w:permStart w:id='7' w:edGrp='everyone' w:colFirst='2' w:colLast='1'/>"
        + $"<w:r><w:t>{text}</w:t></w:r><w:permEnd w:id='7'/>"
        + "</w:p></w:body></w:document>";

    private static string SettingsXml(string? editMode, bool enforced) =>
        "<w:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
        + (editMode is null
            ? string.Empty
            : $"<w:documentProtection w:edit='{editMode}' w:enforcement='{(enforced ? 1 : 0)}'/>")
        + "</w:settings>";

    private static IReadOnlyDictionary<string, string> SettingsContentTypeOverride() =>
        new Dictionary<string, string>
        {
            ["/word/settings.xml"] =
                "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml",
        };

    private static string RootRelationships() =>
        "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
        + "<Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='word/document.xml'/>"
        + "</Relationships>";

    private static string DocumentRelationships(string target) =>
        "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
        + $"<Relationship Id='rIdExternal' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink' Target='{target}' TargetMode='External'/>"
        + "</Relationships>";

    private static string SettingsRelationships() =>
        "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
        + "<Relationship Id='rIdSettings' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings' Target='settings.xml'/>"
        + "</Relationships>";

    private static string DuplicateRelationships(int count) =>
        "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
        + string.Concat(Enumerable.Range(0, count).Select(_ =>
            "<Relationship Id='rIdDuplicate' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink' Target='https://example.invalid/' TargetMode='External'/>"
        ))
        + "</Relationships>";

    private static string ContentTypes(
        IReadOnlyDictionary<string, string>? overrides,
        bool includeBinaryDefault
    ) =>
        "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>"
        + "<Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/>"
        + "<Default Extension='xml' ContentType='application/xml'/>"
        + (includeBinaryDefault
            ? "<Default Extension='bin' ContentType='application/octet-stream'/>"
            : string.Empty)
        + "<Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/>"
        + string.Concat((overrides ?? new Dictionary<string, string>()).Select(pair =>
            $"<Override PartName='{pair.Key}' ContentType='{pair.Value}'/>"
        ))
        + "</Types>";
}
