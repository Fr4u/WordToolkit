using System.Collections.ObjectModel;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordPackagePatchRiskSeverity
{
    Info,
    Review,
    Block,
}

public sealed record WordPackagePatchRiskItem(
    string Code,
    WordPackagePatchRiskSeverity Severity,
    string Message,
    int AffectedOperationCount = 0
);

public sealed record WordPackageProtectionRiskAssessment(
    bool BaseDocumentProtectionEnforced,
    string? BaseDocumentProtectionEditMode,
    bool ResultDocumentProtectionEnforced,
    string? ResultDocumentProtectionEditMode,
    bool DocumentProtectionMetadataChanged,
    int BasePermissionRangeCount,
    int ResultPermissionRangeCount,
    int MalformedPermissionRangeCount,
    bool PermissionIssuesTruncated,
    IReadOnlyList<string> PermissionIssueCodes,
    bool AuthorizationRequired
)
{
    public bool PermissionRangesPresent =>
        BasePermissionRangeCount != 0 || ResultPermissionRangeCount != 0;

    public bool HasMalformedPermissionMetadata =>
        MalformedPermissionRangeCount != 0 || PermissionIssuesTruncated;
}

public sealed record WordPackagePatchRiskAssessment(
    bool DigitalSignaturesPresent,
    bool DigitalSignatureMaterialChanged,
    int MacroOperationCount,
    int EmbeddedObjectOperationCount,
    int ActiveXOperationCount,
    int ExternalRelationshipAddedCount,
    int ExternalRelationshipRemovedCount,
    int OpaqueBinaryOperationCount,
    int CustomXmlOperationCount,
    int InfrastructureOperationCount,
    int BaselineStructuralErrorCount,
    int CandidateStructuralErrorCount,
    int NewStructuralErrorCount,
    WordPackageProtectionRiskAssessment Protection,
    IReadOnlyList<WordPackagePatchRiskItem> Items
)
{
    public bool ActiveContentChanged => MacroOperationCount != 0
        || EmbeddedObjectOperationCount != 0
        || ActiveXOperationCount != 0;

    public bool ExternalRelationshipsChanged => ExternalRelationshipAddedCount != 0
        || ExternalRelationshipRemovedCount != 0;

    public bool OpaqueBinaryChanged => OpaqueBinaryOperationCount != 0;
}

public sealed record WordPackagePatchApplyPolicy
{
    public bool AllowDigitalSignatureInvalidation { get; init; }

    public bool AllowActiveContentChanges { get; init; }

    public bool AllowExternalRelationshipChanges { get; init; }

    public bool AllowOpaqueBinaryChanges { get; init; }

    public bool AllowNewStructuralErrors { get; init; }

    public bool AllowProtectedDocumentEdit { get; init; }
}

public sealed record WordPackagePatchPolicyDecision(
    bool CanApply,
    IReadOnlyList<string> BlockCodes
);

public sealed class WordPackagePatchPlan
{
    internal WordPackagePatchPlan(
        OpcPackagePatch patch,
        WordSemanticDiffResult semanticDiff,
        WordPackagePatchRiskAssessment riskAssessment
    )
    {
        Patch = patch;
        SemanticDiff = semanticDiff;
        RiskAssessment = riskAssessment;
    }

    public OpcPackagePatch Patch { get; }

    public WordSemanticDiffResult SemanticDiff { get; }

    public WordPackagePatchRiskAssessment RiskAssessment { get; }

    public WordPackagePatchPolicyDecision Evaluate(
        WordPackagePatchApplyPolicy? policy = null
    )
    {
        policy ??= new WordPackagePatchApplyPolicy();
        var blocks = new List<string>();
        if (
            !Patch.IsNoOp
            && RiskAssessment.DigitalSignaturesPresent
            && !policy.AllowDigitalSignatureInvalidation
        )
        {
            blocks.Add("digital_signature_invalidation_not_authorized");
        }
        if (
            RiskAssessment.ActiveContentChanged
            && !policy.AllowActiveContentChanges
        )
        {
            blocks.Add("active_content_change_not_authorized");
        }
        if (
            RiskAssessment.ExternalRelationshipsChanged
            && !policy.AllowExternalRelationshipChanges
        )
        {
            blocks.Add("external_relationship_change_not_authorized");
        }
        if (
            RiskAssessment.OpaqueBinaryChanged
            && !policy.AllowOpaqueBinaryChanges
        )
        {
            blocks.Add("opaque_binary_change_not_authorized");
        }
        if (
            RiskAssessment.NewStructuralErrorCount != 0
            && !policy.AllowNewStructuralErrors
        )
        {
            blocks.Add("new_structural_errors");
        }
        if (!Patch.IsNoOp && RiskAssessment.Protection.HasMalformedPermissionMetadata)
        {
            blocks.Add("protection_metadata_malformed");
        }
        else if (
            RiskAssessment.Protection.AuthorizationRequired
            && !policy.AllowProtectedDocumentEdit
        )
        {
            blocks.Add("protected_document_edit_not_authorized");
        }
        return new WordPackagePatchPolicyDecision(
            blocks.Count == 0,
            new ReadOnlyCollection<string>(blocks)
        );
    }
}

public sealed class WordPackagePatchPlanner
{
    private readonly OpcPackagePatchBuilder _patchBuilder;
    private readonly WordSemanticDiffOptions _diffOptions;

    public WordPackagePatchPlanner(
        OpcPackagePatchLimits? patchLimits = null,
        WordSemanticDiffOptions? diffOptions = null
    )
    {
        _patchBuilder = new OpcPackagePatchBuilder(patchLimits);
        _diffOptions = diffOptions ?? WordSemanticDiffOptions.Default;
    }

    public WordPackagePatchPlan Plan(
        OpcPackageSnapshot beforePackage,
        WordSemanticDocument beforeDocument,
        OpcPackageSnapshot afterPackage,
        WordSemanticDocument afterDocument,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(beforePackage);
        ArgumentNullException.ThrowIfNull(beforeDocument);
        ArgumentNullException.ThrowIfNull(afterPackage);
        ArgumentNullException.ThrowIfNull(afterDocument);
        cancellationToken.ThrowIfCancellationRequested();
        var diff = new WordSemanticDiffEngine(_diffOptions).Compare(
            beforePackage,
            beforeDocument,
            afterPackage,
            afterDocument,
            cancellationToken
        );
        var patch = _patchBuilder.Create(
            beforePackage,
            afterPackage,
            cancellationToken
        );
        if (
            !string.Equals(
                patch.BasePackageFingerprint,
                diff.BeforePackageFingerprint,
                StringComparison.Ordinal
            )
            || !string.Equals(
                patch.ResultPackageFingerprint,
                diff.AfterPackageFingerprint,
                StringComparison.Ordinal
            )
        )
        {
            throw new WordSemanticPreconditionException(
                "Semantic diff and package patch were built from different snapshots."
            );
        }
        var risk = WordPackagePatchRiskAnalyzer.Assess(
            beforePackage,
            beforeDocument,
            afterPackage,
            afterDocument,
            patch,
            cancellationToken
        );
        return new WordPackagePatchPlan(patch, diff, risk);
    }

    public WordPackagePatchPlan PlanApply(
        OpcPackageSnapshot basePackage,
        WordSemanticDocument baseDocument,
        OpcPackagePatch patch,
        CancellationToken cancellationToken = default
    ) => PlanApply(
        basePackage,
        baseDocument,
        patch,
        out _,
        cancellationToken
    );

    public WordPackagePatchPlan PlanApply(
        OpcPackageSnapshot basePackage,
        WordSemanticDocument baseDocument,
        OpcPackagePatch patch,
        out OpcPackageSnapshot candidatePackage,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(basePackage);
        ArgumentNullException.ThrowIfNull(baseDocument);
        ArgumentNullException.ThrowIfNull(patch);
        cancellationToken.ThrowIfCancellationRequested();
        candidatePackage = patch.MaterializeCandidate(
            basePackage,
            cancellationToken: cancellationToken
        );
        var candidateDocument = new WordSemanticProjector().Project(
            candidatePackage,
            cancellationToken
        );
        var applyPlan = Plan(
            basePackage,
            baseDocument,
            candidatePackage,
            candidateDocument,
            cancellationToken
        );
        if (!string.Equals(
                applyPlan.Patch.PatchId,
                patch.PatchId,
                StringComparison.Ordinal
            ))
        {
            throw new OpcPackagePatchResultMismatchException(
                "The decoded patch does not rebuild to the same canonical patch identifier."
            );
        }
        return applyPlan;
    }
}

public static class WordPackagePatchRiskAnalyzer
{
    public static WordPackagePatchRiskAssessment Assess(
        OpcPackageSnapshot before,
        WordSemanticDocument beforeDocument,
        OpcPackageSnapshot after,
        WordSemanticDocument afterDocument,
        OpcPackagePatch patch,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(beforeDocument);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(afterDocument);
        ArgumentNullException.ThrowIfNull(patch);
        cancellationToken.ThrowIfCancellationRequested();
        if (
            !string.Equals(
                before.Fingerprint,
                patch.BasePackageFingerprint,
                StringComparison.Ordinal
            )
            || !string.Equals(
                after.Fingerprint,
                patch.ResultPackageFingerprint,
                StringComparison.Ordinal
            )
        )
        {
            throw new WordSemanticPreconditionException(
                "Patch risk analysis requires its exact base and result snapshots."
            );
        }

        var signaturePresent = HasDigitalSignatures(before)
            || HasDigitalSignatures(after);
        var signatureChanges = patch.Operations.Count(IsSignatureMaterial);
        var macroChanges = patch.Operations.Count(IsMacroMaterial);
        var embeddedChanges = patch.Operations.Count(IsEmbeddedObjectMaterial);
        var activeXChanges = patch.Operations.Count(IsActiveXMaterial);
        var customXmlChanges = patch.Operations.Count(operation =>
            operation.EntryName.StartsWith("customXml/", StringComparison.OrdinalIgnoreCase)
        );
        var infrastructureChanges = patch.Operations.Count(operation =>
            operation.IsInfrastructure
        );
        var opaqueBinaryChanges = patch.Operations.Count(operation =>
            IsOpaqueBinary(operation)
            && !IsMacroMaterial(operation)
            && !IsEmbeddedObjectMaterial(operation)
            && !IsActiveXMaterial(operation)
            && !IsSignatureMaterial(operation)
        );
        var beforeExternal = ExternalRelationshipKeys(before);
        var afterExternal = ExternalRelationshipKeys(after);
        var externalAdded = afterExternal.Except(beforeExternal, StringComparer.Ordinal).Count();
        var externalRemoved = beforeExternal.Except(afterExternal, StringComparer.Ordinal).Count();
        var baselineErrors = StructuralErrors(before);
        var candidateErrors = StructuralErrors(after);
        var beforeProtection = ProtectionEvidenceFor(
            before,
            beforeDocument,
            cancellationToken
        );
        var afterProtection = ProtectionEvidenceFor(
            after,
            afterDocument,
            cancellationToken
        );
        var permissionIssueCodes = beforeProtection.PermissionIssueCodes
            .Concat(afterProtection.PermissionIssueCodes)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var malformedPermissionRangeCount = checked(
            beforeProtection.MalformedPermissionRangeCount
            + afterProtection.MalformedPermissionRangeCount
        );
        var permissionIssuesTruncated = beforeProtection.PermissionIssuesTruncated
            || afterProtection.PermissionIssuesTruncated;
        var protectionMetadataChanged = !string.Equals(
            beforeProtection.DocumentProtectionFingerprint,
            afterProtection.DocumentProtectionFingerprint,
            StringComparison.Ordinal
        );
        var protectionAuthorizationRequired = !patch.IsNoOp
            && (
                beforeProtection.DocumentProtectionEnforced
                || afterProtection.DocumentProtectionEnforced
                || beforeProtection.PermissionRangeCount != 0
                || afterProtection.PermissionRangeCount != 0
                || protectionMetadataChanged
            );
        var protection = new WordPackageProtectionRiskAssessment(
            beforeProtection.DocumentProtectionEnforced,
            beforeProtection.DocumentProtectionEditMode,
            afterProtection.DocumentProtectionEnforced,
            afterProtection.DocumentProtectionEditMode,
            protectionMetadataChanged,
            beforeProtection.PermissionRangeCount,
            afterProtection.PermissionRangeCount,
            malformedPermissionRangeCount,
            permissionIssuesTruncated,
            new ReadOnlyCollection<string>(permissionIssueCodes),
            protectionAuthorizationRequired
        );
        var baselineErrorCounts = baselineErrors.GroupBy(
            value => value,
            StringComparer.Ordinal
        ).ToDictionary(
            group => group.Key,
            group => group.Count(),
            StringComparer.Ordinal
        );
        var newErrors = new List<string>();
        foreach (var error in candidateErrors)
        {
            if (
                baselineErrorCounts.TryGetValue(error, out var count)
                && count > 0
            )
            {
                baselineErrorCounts[error] = count - 1;
            }
            else
            {
                newErrors.Add(error);
            }
        }
        var items = new List<WordPackagePatchRiskItem>();
        if (signaturePresent && !patch.IsNoOp)
        {
            items.Add(new WordPackagePatchRiskItem(
                "digital_signature_invalidation_unverified",
                WordPackagePatchRiskSeverity.Block,
                "The base or result package contains OPC digital signatures; any changed signed part or relationship may invalidate them and cryptographic validity has not been verified.",
                patch.OperationCount
            ));
        }
        if (macroChanges != 0)
        {
            items.Add(new WordPackagePatchRiskItem(
                "macro_content_changed",
                WordPackagePatchRiskSeverity.Block,
                "The patch changes VBA or macro-enabled package material.",
                macroChanges
            ));
        }
        if (embeddedChanges != 0)
        {
            items.Add(new WordPackagePatchRiskItem(
                "embedded_object_changed",
                WordPackagePatchRiskSeverity.Block,
                "The patch changes an OLE object or embedded package.",
                embeddedChanges
            ));
        }
        if (activeXChanges != 0)
        {
            items.Add(new WordPackagePatchRiskItem(
                "activex_content_changed",
                WordPackagePatchRiskSeverity.Block,
                "The patch changes ActiveX or control material.",
                activeXChanges
            ));
        }
        if (externalAdded != 0 || externalRemoved != 0)
        {
            items.Add(new WordPackagePatchRiskItem(
                "external_relationship_changed",
                WordPackagePatchRiskSeverity.Block,
                "The patch changes one or more external relationship targets.",
                externalAdded + externalRemoved
            ));
        }
        if (opaqueBinaryChanges != 0)
        {
            items.Add(new WordPackagePatchRiskItem(
                "opaque_binary_changed",
                WordPackagePatchRiskSeverity.Block,
                "The patch changes binary payloads whose behavior is not fully classified.",
                opaqueBinaryChanges
            ));
        }
        if (customXmlChanges != 0)
        {
            items.Add(new WordPackagePatchRiskItem(
                "custom_xml_changed",
                WordPackagePatchRiskSeverity.Review,
                "The patch changes custom XML data or bindings.",
                customXmlChanges
            ));
        }
        if (infrastructureChanges != 0)
        {
            items.Add(new WordPackagePatchRiskItem(
                "package_infrastructure_changed",
                WordPackagePatchRiskSeverity.Info,
                "The patch changes content types or relationship parts.",
                infrastructureChanges
            ));
        }
        if (newErrors.Count != 0)
        {
            items.Add(new WordPackagePatchRiskItem(
                "new_structural_errors",
                WordPackagePatchRiskSeverity.Block,
                "The result introduces OPC structural errors not present in the base package.",
                newErrors.Count
            ));
        }
        if (protection.HasMalformedPermissionMetadata && !patch.IsNoOp)
        {
            items.Add(new WordPackagePatchRiskItem(
                "protection_metadata_malformed",
                WordPackagePatchRiskSeverity.Block,
                "The base or result package contains incomplete or ambiguous Word permission-range metadata; a generic package mutation cannot determine an authorized edit scope.",
                patch.OperationCount
            ));
        }
        else if (protection.AuthorizationRequired)
        {
            items.Add(new WordPackagePatchRiskItem(
                "protected_document_edit_requires_authorization",
                WordPackagePatchRiskSeverity.Block,
                "The base or result package enforces Word editing protection, contains permission ranges, or changes document-protection metadata. Generic package mutation requires an explicit plan-bound authorization.",
                patch.OperationCount
            ));
        }
        return new WordPackagePatchRiskAssessment(
            signaturePresent,
            signatureChanges != 0,
            macroChanges,
            embeddedChanges,
            activeXChanges,
            externalAdded,
            externalRemoved,
            opaqueBinaryChanges,
            customXmlChanges,
            infrastructureChanges,
            baselineErrors.Count,
            candidateErrors.Count,
            newErrors.Count,
            protection,
            new ReadOnlyCollection<WordPackagePatchRiskItem>(items)
        );
    }

    private static ProtectionEvidence ProtectionEvidenceFor(
        OpcPackageSnapshot package,
        WordSemanticDocument document,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = new WordSettingsGraphBuilder().Build(
            package,
            document,
            cancellationToken
        );
        var review = new WordReviewGraphBuilder().Build(
            package,
            document,
            cancellationToken
        );
        var permissionIssueCodes = review.Issues
            .Where(issue => issue.Code.StartsWith("PERMISSION_", StringComparison.Ordinal))
            .Select(issue => issue.Code)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var malformedPermissionRangeCount = review.Permissions.Count(permission =>
            permission.Status != WordReviewRangeStatus.Complete
        );
        if (permissionIssueCodes.Contains(
                "PERMISSION_COLUMN_RANGE_INCOMPLETE",
                StringComparer.Ordinal
            ))
        {
            malformedPermissionRangeCount = Math.Max(
                malformedPermissionRangeCount,
                1
            );
        }
        return new ProtectionEvidence(
            settings.DocumentProtection?.IsEnforced == true,
            settings.DocumentProtection?.EditMode,
            DocumentProtectionFingerprint(package, settings, cancellationToken),
            review.Permissions.Count,
            malformedPermissionRangeCount,
            review.IssuesTruncated && review.Permissions.Count != 0,
            permissionIssueCodes
        );
    }

    private static string? DocumentProtectionFingerprint(
        OpcPackageSnapshot package,
        WordSettingsGraph settings,
        CancellationToken cancellationToken
    )
    {
        if (
            settings.SettingsPartUri is not { } partUri
            || !package.Parts.TryGetValue(partUri, out var part)
        )
        {
            return null;
        }
        var source = LosslessXmlDocument.Parse(
            part.Entry.Content,
            cancellationToken: cancellationToken
        );
        var element = source.Elements.SingleOrDefault(candidate =>
            candidate.LocalName == "documentProtection"
            && candidate.NamespaceUri is
                "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                or "http://purl.oclc.org/ooxml/wordprocessingml/main"
        );
        if (element is null)
        {
            return null;
        }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                source.SourceBytes.Span.Slice(
                    element.FullSpan.ByteOffset,
                    element.FullSpan.ByteLength
                )
            ))
            .ToLowerInvariant();
    }

    private sealed record ProtectionEvidence(
        bool DocumentProtectionEnforced,
        string? DocumentProtectionEditMode,
        string? DocumentProtectionFingerprint,
        int PermissionRangeCount,
        int MalformedPermissionRangeCount,
        bool PermissionIssuesTruncated,
        IReadOnlyList<string> PermissionIssueCodes
    );

    public static bool HasDigitalSignatures(OpcPackageSnapshot package) =>
        package.Entries.Any(entry =>
            entry.Name.StartsWith("_xmlsignatures/", StringComparison.OrdinalIgnoreCase)
        )
        || package.Parts.Values.Any(part => Contains(
            part.ContentType,
            "digital-signature"
        ))
        || package.Relationships.Any(relationship => Contains(
            relationship.Type,
            "digital-signature"
        ));

    private static bool IsSignatureMaterial(OpcPackagePatchOperation operation) =>
        operation.EntryName.StartsWith(
            "_xmlsignatures/",
            StringComparison.OrdinalIgnoreCase
        )
        || Contains(operation.BeforeContentType, "digital-signature")
        || Contains(operation.AfterContentType, "digital-signature")
        || operation.EntryName.Contains(
            "vbaProjectSignature",
            StringComparison.OrdinalIgnoreCase
        );

    private static bool IsMacroMaterial(OpcPackagePatchOperation operation) =>
        operation.EntryName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase)
        || operation.EntryName.Contains("vbaData", StringComparison.OrdinalIgnoreCase)
        || Contains(operation.BeforeContentType, "macroEnabled")
        || Contains(operation.AfterContentType, "macroEnabled")
        || Contains(operation.BeforeContentType, "vbaProject")
        || Contains(operation.AfterContentType, "vbaProject");

    private static bool IsEmbeddedObjectMaterial(
        OpcPackagePatchOperation operation
    ) => operation.EntryName.StartsWith(
            "word/embeddings/",
            StringComparison.OrdinalIgnoreCase
        )
        || Contains(operation.BeforeContentType, "oleObject")
        || Contains(operation.AfterContentType, "oleObject")
        || Contains(operation.BeforeContentType, "embeddedPackage")
        || Contains(operation.AfterContentType, "embeddedPackage");

    private static bool IsActiveXMaterial(OpcPackagePatchOperation operation) =>
        operation.EntryName.StartsWith(
            "word/activeX/",
            StringComparison.OrdinalIgnoreCase
        )
        || operation.EntryName.StartsWith(
            "word/ctrlProps/",
            StringComparison.OrdinalIgnoreCase
        )
        || Contains(operation.BeforeContentType, "activeX")
        || Contains(operation.AfterContentType, "activeX");

    private static bool IsOpaqueBinary(OpcPackagePatchOperation operation) =>
        IsNonXmlContentType(operation.BeforeContentType)
        || IsNonXmlContentType(operation.AfterContentType)
        || (
            !operation.IsInfrastructure
            && !operation.EntryName.EndsWith("/", StringComparison.Ordinal)
            && !operation.EntryName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            && !operation.EntryName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)
        );

    private static bool IsNonXmlContentType(string? value) => value is not null
        && !Contains(value, "xml")
        && !value.StartsWith("text/", StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> ExternalRelationshipKeys(
        OpcPackageSnapshot package
    ) => package.Relationships.Where(relationship =>
            relationship.TargetMode == OpcRelationshipTargetMode.External
        )
        .Select(relationship => string.Join(
            '\u001f',
            relationship.SourcePartUri,
            relationship.Id,
            relationship.Type,
            relationship.Target
        ))
        .ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyList<string> StructuralErrors(OpcPackageSnapshot package) =>
        package.Diagnostics.Where(diagnostic =>
            diagnostic.Severity is OpcDiagnosticSeverity.Error
                or OpcDiagnosticSeverity.Fatal
        ).Select(diagnostic => string.Join(
            '\u001f',
            diagnostic.Code,
            diagnostic.PartUri,
            diagnostic.RelationshipId,
            diagnostic.Message
        )).ToArray();

    private static bool Contains(string? value, string fragment) => value?.Contains(
        fragment,
        StringComparison.OrdinalIgnoreCase
    ) == true;
}
