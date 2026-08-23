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
    public void MixedPermissionAttributeNamespacesAreNonOverridable()
    {
        var before = Read(BuildPackage(
            "unused",
            documentXml: MixedNamespacePermissionDocumentXml("before")
        ));
        var after = Read(BuildPackage(
            "unused",
            documentXml: MixedNamespacePermissionDocumentXml("after")
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

        Assert.True(plan.RiskAssessment.Protection.HasMalformedProtectionMetadata);
        Assert.Contains(
            "PERMISSION_ATTRIBUTE_NAMESPACE_INVALID",
            plan.RiskAssessment.Protection.PermissionIssueCodes
        );
        Assert.False(decision.CanApply);
        Assert.Contains("protection_metadata_malformed", decision.BlockCodes);
    }

    [Fact]
    public void MixedPermissionMarkerNamespacesAreNonOverridable()
    {
        var before = Read(BuildPackage(
            "unused",
            documentXml: MixedMarkerNamespacePermissionDocumentXml("before")
        ));
        var after = Read(BuildPackage(
            "unused",
            documentXml: MixedMarkerNamespacePermissionDocumentXml("after")
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

        Assert.Contains(
            "PERMISSION_MARKER_NAMESPACE_INVALID",
            plan.RiskAssessment.Protection.PermissionIssueCodes
        );
        Assert.False(decision.CanApply);
        Assert.Contains("protection_metadata_malformed", decision.BlockCodes);
    }

    [Fact]
    public void MisplacedPermissionEndAttributesAreNonOverridable()
    {
        var before = Read(BuildPackage(
            "unused",
            documentXml: MisplacedPermissionEndAttributeDocumentXml("before")
        ));
        var after = Read(BuildPackage(
            "unused",
            documentXml: MisplacedPermissionEndAttributeDocumentXml("after")
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

        Assert.Contains(
            "PERMISSION_ATTRIBUTE_PLACEMENT_INVALID",
            plan.RiskAssessment.Protection.PermissionIssueCodes
        );
        Assert.False(decision.CanApply);
        Assert.Contains("protection_metadata_malformed", decision.BlockCodes);
    }

    [Fact]
    public void UnknownWordPermissionAttributesAreNonOverridable()
    {
        var before = Read(BuildPackage(
            "unused",
            documentXml: UnknownPermissionAttributeDocumentXml("before")
        ));
        var after = Read(BuildPackage(
            "unused",
            documentXml: UnknownPermissionAttributeDocumentXml("after")
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

        Assert.Contains(
            "PERMISSION_ATTRIBUTE_UNKNOWN",
            plan.RiskAssessment.Protection.PermissionIssueCodes
        );
        Assert.False(decision.CanApply);
        Assert.Contains("protection_metadata_malformed", decision.BlockCodes);
    }

    [Fact]
    public void UnqualifiedPermissionAttributesAreNonOverridable()
    {
        var before = Read(BuildPackage(
            "unused",
            documentXml: UnqualifiedPermissionAttributeDocumentXml("before")
        ));
        var after = Read(BuildPackage(
            "unused",
            documentXml: UnqualifiedPermissionAttributeDocumentXml("after")
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

        Assert.Contains(
            "PERMISSION_ATTRIBUTE_UNKNOWN",
            plan.RiskAssessment.Protection.PermissionIssueCodes
        );
        Assert.False(decision.CanApply);
        Assert.Contains("protection_metadata_malformed", decision.BlockCodes);
    }

    [Fact]
    public void UnqualifiedKnownPermissionAttributesAreNonOverridable()
    {
        var before = Read(BuildPackage(
            "unused",
            documentXml: UnqualifiedKnownPermissionAttributeDocumentXml("before")
        ));
        var after = Read(BuildPackage(
            "unused",
            documentXml: UnqualifiedKnownPermissionAttributeDocumentXml("after")
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

        Assert.Contains(
            "PERMISSION_ATTRIBUTE_NAMESPACE_INVALID",
            plan.RiskAssessment.Protection.PermissionIssueCodes
        );
        Assert.False(decision.CanApply);
        Assert.Contains("protection_metadata_malformed", decision.BlockCodes);
    }

    [Fact]
    public void ForeignPermissionAttributesAreNonOverridable()
    {
        var before = Read(BuildPackage(
            "unused",
            documentXml: ForeignPermissionAttributeDocumentXml("before")
        ));
        var after = Read(BuildPackage(
            "unused",
            documentXml: ForeignPermissionAttributeDocumentXml("after")
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

        Assert.Contains(
            "PERMISSION_ATTRIBUTE_NAMESPACE_INVALID",
            plan.RiskAssessment.Protection.PermissionIssueCodes
        );
        Assert.False(decision.CanApply);
        Assert.Contains("protection_metadata_malformed", decision.BlockCodes);
    }

    [Theory]
    [InlineData("<w:r/>", "")]
    [InlineData("", "text")]
    [InlineData("<![CDATA[]]>", "")]
    public void NonemptyPermissionMarkersAreNonOverridable(
        string startContent,
        string endContent
    )
    {
        var before = Read(BuildPackage(
            "unused",
            documentXml: NonemptyPermissionDocumentXml(
                "before",
                startContent,
                endContent
            )
        ));
        var after = Read(BuildPackage(
            "unused",
            documentXml: NonemptyPermissionDocumentXml(
                "after",
                startContent,
                endContent
            )
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

        Assert.Contains(
            "PERMISSION_MARKER_CONTENT_INVALID",
            plan.RiskAssessment.Protection.PermissionIssueCodes
        );
        Assert.False(decision.CanApply);
        Assert.Contains("protection_metadata_malformed", decision.BlockCodes);
    }

    [Fact]
    public void PermissionMarkersInInvalidParentsAreNonOverridable()
    {
        var before = Read(BuildPackage(
            "unused",
            documentXml: InvalidPermissionParentDocumentXml("before")
        ));
        var after = Read(BuildPackage(
            "unused",
            documentXml: InvalidPermissionParentDocumentXml("after")
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

        Assert.Contains(
            "PERMISSION_MARKER_PARENT_INVALID",
            plan.RiskAssessment.Protection.PermissionIssueCodes
        );
        Assert.Equal(2, plan.RiskAssessment.Protection.MalformedPermissionRangeCount);
        Assert.False(decision.CanApply);
        Assert.Contains("protection_metadata_malformed", decision.BlockCodes);
    }

    [Fact]
    public void CountsEveryMalformedCompletePermissionRange()
    {
        var before = Read(BuildPackage(
            "unused",
            documentXml: MultipleMalformedPermissionRangesDocumentXml("before")
        ));
        var after = Read(BuildPackage(
            "unused",
            documentXml: MultipleMalformedPermissionRangesDocumentXml("after")
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.Equal(4, plan.RiskAssessment.Protection.MalformedPermissionRangeCount);
        Assert.Contains(
            "PERMISSION_ATTRIBUTE_UNKNOWN",
            plan.RiskAssessment.Protection.PermissionIssueCodes
        );
        Assert.Contains(
            "PERMISSION_ATTRIBUTE_NAMESPACE_INVALID",
            plan.RiskAssessment.Protection.PermissionIssueCodes
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrelatedMalformedReviewMetadataDoesNotAbortPermissionRisk(
        bool includePermission
    )
    {
        var extras = new Dictionary<string, byte[]>
        {
            ["word/commentsExtended.xml"] = Utf8("<wrong/>")
        };
        var before = Read(BuildPackage(
            "unused",
            extras,
            CommentsExtendedContentTypeOverride(),
            CommentsExtendedRelationships(),
            documentXml: includePermission
                ? PermissionDocumentXml("before", includeEnd: true)
                : DocumentXml("before")
        ));
        var after = Read(BuildPackage(
            "unused",
            extras,
            CommentsExtendedContentTypeOverride(),
            CommentsExtendedRelationships(),
            documentXml: includePermission
                ? PermissionDocumentXml("after", includeEnd: true)
                : DocumentXml("after")
        ));

        Assert.Throws<WordReviewProjectionException>(() =>
            new WordReviewGraphBuilder().Build(before.Package, before.Document)
        );
        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.Equal(
            includePermission ? 1 : 0,
            plan.RiskAssessment.Protection.BasePermissionRangeCount
        );
        Assert.Equal(
            includePermission,
            plan.RiskAssessment.Protection.AuthorizationRequired
        );
        Assert.Equal(
            !includePermission,
            plan.Evaluate().CanApply
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MceWrappedDocumentProtectionIsNonOverridable(bool includeFallback)
    {
        var settings = AlternateContentSettingsXml(includeFallback);
        var before = Read(BuildPackage(
            "before",
            new Dictionary<string, byte[]>
            {
                ["word/settings.xml"] = Utf8(settings),
            },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));
        var after = Read(BuildPackage(
            "after",
            new Dictionary<string, byte[]>
            {
                ["word/settings.xml"] = Utf8(settings),
            },
            SettingsContentTypeOverride(),
            SettingsRelationships()
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

        Assert.True(
            plan.RiskAssessment.Protection.UnmodeledDocumentProtectionMetadata
        );
        Assert.True(plan.RiskAssessment.Protection.HasMalformedProtectionMetadata);
        Assert.True(plan.RiskAssessment.Protection.AuthorizationRequired);
        Assert.False(decision.CanApply);
        Assert.Contains("protection_metadata_malformed", decision.BlockCodes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedConformanceDocumentProtectionIsNonOverridable(bool strictRoot)
    {
        var settings = MixedConformanceProtectionSettingsXml(strictRoot);
        var before = Read(BuildPackage(
            "before",
            new Dictionary<string, byte[]>
            {
                ["word/settings.xml"] = Utf8(settings),
            },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));
        var after = Read(BuildPackage(
            "after",
            new Dictionary<string, byte[]>
            {
                ["word/settings.xml"] = Utf8(settings),
            },
            SettingsContentTypeOverride(),
            SettingsRelationships()
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

        Assert.True(
            plan.RiskAssessment.Protection.UnmodeledDocumentProtectionMetadata
        );
        Assert.True(plan.RiskAssessment.Protection.HasMalformedProtectionMetadata);
        Assert.False(decision.CanApply);
        Assert.Contains("protection_metadata_malformed", decision.BlockCodes);
    }

    [Fact]
    public void InvalidProtectionEnforcementIsNonOverridable()
    {
        var settings =
            "<w:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
            + "<w:documentProtection w:edit='readOnly' w:enforcement='invalid'/>"
            + "</w:settings>";
        var before = Read(BuildPackage(
            "before",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(settings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));
        var after = Read(BuildPackage(
            "after",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(settings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.True(
            plan.RiskAssessment.Protection.UnmodeledDocumentProtectionMetadata
        );
        var decision = plan.Evaluate(new WordPackagePatchApplyPolicy
        {
            AllowProtectedDocumentEdit = true,
        });
        Assert.False(decision.CanApply);
        Assert.Contains("protection_metadata_malformed", decision.BlockCodes);
    }

    [Theory]
    [InlineData("w:formatting='bogus'")]
    [InlineData("w:cryptSpinCount='-1'")]
    [InlineData("w:cryptSpinCount='5000001'")]
    [InlineData("w:cryptAlgorithmSid='11'")]
    [InlineData("w:hash='not-base64'")]
    [InlineData("w:algIdExt='00'")]
    [InlineData("w:bogus='x'")]
    [InlineData("bogus='x'")]
    [InlineData("w14:algorithmName='SHA-512'")]
    [InlineData("x:bogus='value'")]
    public void MalformedProtectionAttributesAreNonOverridable(string attribute)
    {
        var settings =
            "<w:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main' "
            + "xmlns:w14='http://schemas.microsoft.com/office/word/2010/wordml' "
            + "xmlns:x='urn:test'>"
            + $"<w:documentProtection w:edit='readOnly' w:enforcement='1' {attribute}/>"
            + "</w:settings>";
        var before = Read(BuildPackage(
            "before",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(settings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));
        var after = Read(BuildPackage(
            "after",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(settings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
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

        Assert.True(
            plan.RiskAssessment.Protection.UnmodeledDocumentProtectionMetadata
        );
        Assert.False(decision.CanApply);
        Assert.Contains("protection_metadata_malformed", decision.BlockCodes);
    }

    [Theory]
    [InlineData("<w:r/>")]
    [InlineData("<![CDATA[]]>")]
    [InlineData("not-empty")]
    public void ProtectionElementContentIsNonOverridable(string content)
    {
        var settings =
            "<w:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
            + $"<w:documentProtection w:edit='readOnly' w:enforcement='1'>{content}</w:documentProtection>"
            + "</w:settings>";
        var before = Read(BuildPackage(
            "before",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(settings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));
        var after = Read(BuildPackage(
            "after",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(settings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.True(
            plan.RiskAssessment.Protection.UnmodeledDocumentProtectionMetadata
        );
        Assert.Contains(
            "protection_metadata_malformed",
            plan.Evaluate(new WordPackagePatchApplyPolicy
            {
                AllowProtectedDocumentEdit = true,
            }).BlockCodes
        );
    }

    [Fact]
    public void ProtectionCommentsAndProcessingInstructionsRemainModeled()
    {
        const string settings =
            "<w:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
            + "<w:documentProtection w:edit='readOnly' w:enforcement='1'>"
            + "<!-- harmless --><?wordtoolkit preserve?>"
            + "</w:documentProtection>"
            + "</w:settings>";
        var before = Read(BuildPackage(
            "before",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(settings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));
        var after = Read(BuildPackage(
            "after",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(settings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.False(
            plan.RiskAssessment.Protection.UnmodeledDocumentProtectionMetadata
        );
        Assert.True(plan.RiskAssessment.Protection.AuthorizationRequired);
        Assert.False(plan.Evaluate().CanApply);
        Assert.True(plan.Evaluate(new WordPackagePatchApplyPolicy
        {
            AllowProtectedDocumentEdit = true,
        }).CanApply);
    }

    [Fact]
    public void ValidProtectionAttributesRemainModeled()
    {
        const string settings =
            "<w:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
            + "<w:documentProtection w:edit='readOnly' w:enforcement='1' w:formatting='on' "
            + "w:cryptProviderType='rsaAES' w:cryptAlgorithmClass='hash' w:cryptAlgorithmType='typeAny' "
            + "w:cryptAlgorithmSid='14' w:cryptSpinCount='5000000' w:cryptProvider='provider' "
            + "w:algIdExt='00112233' w:algIdExtSource='source' w:cryptProviderTypeExt='AABBCCDD' "
            + "w:cryptProviderTypeExtSource='source' w:hash='YWJj' w:salt='ZA==' "
            + "w:algorithmName='SHA-512' w:hashValue='YWJj' w:saltValue='ZA==' w:spinCount='100000'/>"
            + "</w:settings>";
        var before = Read(BuildPackage(
            "before",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(settings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));
        var after = Read(BuildPackage(
            "after",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(settings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.False(
            plan.RiskAssessment.Protection.UnmodeledDocumentProtectionMetadata
        );
        Assert.True(plan.Evaluate(new WordPackagePatchApplyPolicy
        {
            AllowProtectedDocumentEdit = true,
        }).CanApply);
    }

    [Fact]
    public void MissingProtectionEnforcementUsesWordEnforcedBehavior()
    {
        const string settings =
            "<w:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
            + "<w:documentProtection w:edit='readOnly'/>"
            + "</w:settings>";
        var before = Read(BuildPackage(
            "before",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(settings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));
        var after = Read(BuildPackage(
            "after",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(settings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.True(plan.RiskAssessment.Protection.BaseDocumentProtectionEnforced);
        Assert.True(plan.RiskAssessment.Protection.AuthorizationRequired);
        Assert.False(plan.Evaluate().CanApply);
        Assert.True(plan.Evaluate(new WordPackagePatchApplyPolicy
        {
            AllowProtectedDocumentEdit = true,
        }).CanApply);
    }

    [Theory]
    [InlineData("TRUE")]
    [InlineData("False")]
    [InlineData("ON")]
    [InlineData("Off")]
    public void ProtectionEnforcementLexicalValuesAreCaseSensitive(string enforcement)
    {
        var settings =
            "<w:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
            + $"<w:documentProtection w:edit='readOnly' w:enforcement='{enforcement}'/>"
            + "</w:settings>";
        var before = Read(BuildPackage(
            "before",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(settings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));
        var after = Read(BuildPackage(
            "after",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(settings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
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

        Assert.True(
            plan.RiskAssessment.Protection.UnmodeledDocumentProtectionMetadata
        );
        Assert.False(decision.CanApply);
        Assert.Contains("protection_metadata_malformed", decision.BlockCodes);
    }

    [Fact]
    public void InvalidProtectionEditModeIsNonOverridable()
    {
        const string settings =
            "<w:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
            + "<w:documentProtection w:edit='bogus' w:enforcement='1'/>"
            + "</w:settings>";
        var before = Read(BuildPackage(
            "before",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(settings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));
        var after = Read(BuildPackage(
            "after",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(settings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.True(
            plan.RiskAssessment.Protection.UnmodeledDocumentProtectionMetadata
        );
        Assert.Contains(
            "protection_metadata_malformed",
            plan.Evaluate(new WordPackagePatchApplyPolicy
            {
                AllowProtectedDocumentEdit = true,
            }).BlockCodes
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateOrMissingSettingsPartIsNonOverridable(
        bool duplicateRelationships
    )
    {
        const string settings =
            "<w:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
            + "<w:documentProtection w:edit='readOnly' w:enforcement='1'/>"
            + "</w:settings>";
        var extras = duplicateRelationships
            ? new Dictionary<string, byte[]>
            {
                ["word/settings.xml"] = Utf8(settings),
            }
            : null;
        var overrides = duplicateRelationships ? SettingsContentTypeOverride() : null;
        var relationships = duplicateRelationships
            ? DuplicateSettingsRelationships()
            : SettingsRelationships();
        var before = Read(BuildPackage(
            "before",
            extras,
            overrides,
            relationships
        ));
        var after = Read(BuildPackage(
            "after",
            extras,
            overrides,
            relationships
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.True(
            plan.RiskAssessment.Protection.UnmodeledDocumentProtectionMetadata
        );
        var decision = plan.Evaluate(new WordPackagePatchApplyPolicy
        {
            AllowProtectedDocumentEdit = true,
        });
        Assert.False(decision.CanApply);
        Assert.Contains("protection_metadata_malformed", decision.BlockCodes);
    }

    [Fact]
    public void InvalidSettingsContentTypeIsNonOverridable()
    {
        const string settings =
            "<w:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
            + "<w:documentProtection w:edit='readOnly' w:enforcement='1'/>"
            + "</w:settings>";
        var extras = new Dictionary<string, byte[]>
        {
            ["word/settings.xml"] = Utf8(settings),
        };
        var before = Read(BuildPackage(
            "before",
            extras,
            documentRelationships: SettingsRelationships()
        ));
        var after = Read(BuildPackage(
            "after",
            extras,
            documentRelationships: SettingsRelationships()
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.True(
            plan.RiskAssessment.Protection.UnmodeledDocumentProtectionMetadata
        );
        var decision = plan.Evaluate(new WordPackagePatchApplyPolicy
        {
            AllowProtectedDocumentEdit = true,
        });
        Assert.False(decision.CanApply);
        Assert.Contains("protection_metadata_malformed", decision.BlockCodes);
    }

    [Fact]
    public void MalformedSettingsXmlIsNonOverridable()
    {
        var extras = new Dictionary<string, byte[]>
        {
            ["word/settings.xml"] = Utf8(
                "<w:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:documentProtection"
            ),
        };
        var before = Read(BuildPackage(
            "before",
            extras,
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));
        var after = Read(BuildPackage(
            "after",
            extras,
            SettingsContentTypeOverride(),
            SettingsRelationships()
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

        Assert.True(
            plan.RiskAssessment.Protection.UnmodeledDocumentProtectionMetadata
        );
        Assert.False(decision.CanApply);
        Assert.Contains("protection_metadata_malformed", decision.BlockCodes);
    }

    [Fact]
    public void OverlongProtectionEditModeIsNonOverridable()
    {
        var editMode = new string('x', 65);
        var settings =
            "<w:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
            + $"<w:documentProtection w:edit='{editMode}' w:enforcement='1'/>"
            + "</w:settings>";
        var before = Read(BuildPackage(
            "before",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(settings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));
        var after = Read(BuildPackage(
            "after",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(settings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.True(
            plan.RiskAssessment.Protection.UnmodeledDocumentProtectionMetadata
        );
        Assert.Contains(
            "protection_metadata_malformed",
            plan.Evaluate(new WordPackagePatchApplyPolicy
            {
                AllowProtectedDocumentEdit = true,
            }).BlockCodes
        );
    }

    [Fact]
    public void UnrelatedSettingsProjectionErrorsDoNotAbortProtectionRiskPlanning()
    {
        const string settings =
            "<w:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
            + "<w:defaultTabStop w:val='720'/><w:defaultTabStop w:val='720'/>"
            + "</w:settings>";
        var before = Read(BuildPackage(
            "before",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(settings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));
        var after = Read(BuildPackage(
            "after",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(settings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.False(
            plan.RiskAssessment.Protection.UnmodeledDocumentProtectionMetadata
        );
        Assert.False(plan.RiskAssessment.Protection.AuthorizationRequired);
        Assert.True(plan.Evaluate().CanApply);
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
    public void ChangingProtectionConformanceRequiresAuthorizationWhenRawElementIsIdentical()
    {
        const string transitional =
            "<w:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
            + "<w:documentProtection w:edit='readOnly' w:enforcement='0'/>"
            + "</w:settings>";
        const string strict =
            "<w:settings xmlns:w='http://purl.oclc.org/ooxml/wordprocessingml/main'>"
            + "<w:documentProtection w:edit='readOnly' w:enforcement='0'/>"
            + "</w:settings>";
        var before = Read(BuildPackage(
            "same",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(transitional) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));
        var after = Read(BuildPackage(
            "same",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(strict) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));

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
    public void EquivalentProtectionPrefixesAndAttributeOrderKeepTheSameFingerprint()
    {
        const string beforeSettings =
            "<w:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
            + "<w:documentProtection w:edit='readOnly' w:enforcement='0'/>"
            + "</w:settings>";
        const string afterSettings =
            "<x:settings xmlns:x='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>\n"
            + "  <x:documentProtection x:enforcement='0' x:edit='readOnly'></x:documentProtection>\n"
            + "</x:settings>";
        var before = Read(BuildPackage(
            "same",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(beforeSettings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));
        var after = Read(BuildPackage(
            "same",
            new Dictionary<string, byte[]> { ["word/settings.xml"] = Utf8(afterSettings) },
            SettingsContentTypeOverride(),
            SettingsRelationships()
        ));

        var plan = new WordPackagePatchPlanner().Plan(
            before.Package,
            before.Document,
            after.Package,
            after.Document
        );

        Assert.False(plan.RiskAssessment.Protection.DocumentProtectionMetadataChanged);
        Assert.False(plan.RiskAssessment.Protection.AuthorizationRequired);
        Assert.True(plan.Evaluate().CanApply);
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

    private static string MixedNamespacePermissionDocumentXml(string text) =>
        "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main' "
        + "xmlns:ws='http://purl.oclc.org/ooxml/wordprocessingml/main'>"
        + "<w:body><w:p><w:permStart ws:id='7' ws:edGrp='everyone' ws:colFirst='0' ws:colLast='2'/>"
        + $"<w:r><w:t>{text}</w:t></w:r><w:permEnd ws:id='7'/>"
        + "</w:p></w:body></w:document>";

    private static string MixedMarkerNamespacePermissionDocumentXml(string text) =>
        "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main' "
        + "xmlns:ws='http://purl.oclc.org/ooxml/wordprocessingml/main'>"
        + "<w:body><w:p><ws:permStart ws:id='7' ws:edGrp='everyone'/>"
        + $"<w:r><w:t>{text}</w:t></w:r><ws:permEnd ws:id='7'/>"
        + "</w:p></w:body></w:document>";

    private static string MisplacedPermissionEndAttributeDocumentXml(string text) =>
        "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
        + "<w:body><w:p><w:permStart w:id='7'/>"
        + $"<w:r><w:t>{text}</w:t></w:r>"
        + "<w:permEnd w:id='7' w:edGrp='everyone' w:colFirst='0' w:colLast='2'/>"
        + "</w:p></w:body></w:document>";

    private static string UnknownPermissionAttributeDocumentXml(string text) =>
        "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
        + "<w:body><w:p><w:permStart w:id='7' w:bogus='x'/>"
        + $"<w:r><w:t>{text}</w:t></w:r><w:permEnd w:id='7'/>"
        + "</w:p></w:body></w:document>";

    private static string UnqualifiedPermissionAttributeDocumentXml(string text) =>
        "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
        + "<w:body><w:p><w:permStart w:id='7' bogus='x'/>"
        + $"<w:r><w:t>{text}</w:t></w:r><w:permEnd w:id='7'/>"
        + "</w:p></w:body></w:document>";

    private static string UnqualifiedKnownPermissionAttributeDocumentXml(string text) =>
        "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
        + "<w:body><w:p><w:permStart id='7' edGrp='everyone'/>"
        + $"<w:r><w:t>{text}</w:t></w:r><w:permEnd id='7'/>"
        + "</w:p></w:body></w:document>";

    private static string ForeignPermissionAttributeDocumentXml(string text) =>
        "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main' "
        + "xmlns:x='urn:test'>"
        + "<w:body><w:p><w:permStart w:id='7' x:bogus='value'/>"
        + $"<w:r><w:t>{text}</w:t></w:r><w:permEnd w:id='7'/>"
        + "</w:p></w:body></w:document>";

    private static string NonemptyPermissionDocumentXml(
        string text,
        string startContent,
        string endContent
    ) =>
        "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
        + $"<w:body><w:p><w:permStart w:id='7'>{startContent}</w:permStart>"
        + $"<w:r><w:t>{text}</w:t></w:r>"
        + $"<w:permEnd w:id='7'>{endContent}</w:permEnd>"
        + "</w:p></w:body></w:document>";

    private static string InvalidPermissionParentDocumentXml(string text) =>
        "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
        + $"<w:body><w:p><w:r><w:t>{text}<w:permStart w:id='7'/>"
        + "<w:permEnd w:id='7'/></w:t></w:r></w:p></w:body></w:document>";

    private static string MultipleMalformedPermissionRangesDocumentXml(string text) =>
        "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main' "
        + "xmlns:x='urn:test'>"
        + "<w:body><w:p><w:permStart w:id='7' w:bogus='one'/>"
        + "<w:permEnd w:id='7'/><w:permStart w:id='8' x:bogus='two'/>"
        + $"<w:r><w:t>{text}</w:t></w:r><w:permEnd w:id='8'/>"
        + "</w:p></w:body></w:document>";

    private static string SettingsXml(string? editMode, bool enforced) =>
        "<w:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'>"
        + (editMode is null
            ? string.Empty
            : $"<w:documentProtection w:edit='{editMode}' w:enforcement='{(enforced ? 1 : 0)}'/>")
        + "</w:settings>";

    private static string AlternateContentSettingsXml(bool includeFallback) =>
        "<w:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main' "
        + "xmlns:w14='http://schemas.microsoft.com/office/word/2010/wordml' "
        + "xmlns:mc='http://schemas.openxmlformats.org/markup-compatibility/2006' mc:Ignorable='w14'>"
        + "<mc:AlternateContent><mc:Choice Requires='w14'>"
        + "<w:documentProtection w:edit='readOnly' w:enforcement='1'/>"
        + "</mc:Choice>"
        + (includeFallback
            ? "<mc:Fallback><w:documentProtection w:edit='readOnly' w:enforcement='1'/></mc:Fallback>"
            : string.Empty)
        + "</mc:AlternateContent></w:settings>";

    private static string MixedConformanceProtectionSettingsXml(bool strictRoot) =>
        strictRoot
            ? "<ws:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main' "
                + "xmlns:ws='http://purl.oclc.org/ooxml/wordprocessingml/main'>"
                + "<w:documentProtection w:edit='readOnly' w:enforcement='1'/>"
                + "</ws:settings>"
            : "<w:settings xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main' "
                + "xmlns:ws='http://purl.oclc.org/ooxml/wordprocessingml/main'>"
                + "<ws:documentProtection ws:edit='readOnly' ws:enforcement='1'/>"
                + "</w:settings>";

    private static IReadOnlyDictionary<string, string> SettingsContentTypeOverride() =>
        new Dictionary<string, string>
        {
            ["/word/settings.xml"] =
                "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml",
        };

    private static IReadOnlyDictionary<string, string> CommentsExtendedContentTypeOverride() =>
        new Dictionary<string, string>
        {
            ["/word/commentsExtended.xml"] =
                "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsExtended+xml",
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

    private static string CommentsExtendedRelationships() =>
        "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
        + "<Relationship Id='rIdCommentsExtended' Type='http://schemas.microsoft.com/office/2011/relationships/commentsExtended' Target='commentsExtended.xml'/>"
        + "</Relationships>";

    private static string DuplicateSettingsRelationships() =>
        "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
        + "<Relationship Id='rIdSettings1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings' Target='settings.xml'/>"
        + "<Relationship Id='rIdSettings2' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings' Target='settings.xml'/>"
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
