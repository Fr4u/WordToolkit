using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
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
    bool UnmodeledDocumentProtectionMetadata,
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

    public bool HasMalformedProtectionMetadata =>
        UnmodeledDocumentProtectionMetadata || HasMalformedPermissionMetadata;
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
        if (!Patch.IsNoOp && RiskAssessment.Protection.HasMalformedProtectionMetadata)
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
        var unmodeledDocumentProtectionMetadata =
            beforeProtection.UnmodeledDocumentProtectionMetadata
            || afterProtection.UnmodeledDocumentProtectionMetadata;
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
                || unmodeledDocumentProtectionMetadata
            );
        var protection = new WordPackageProtectionRiskAssessment(
            beforeProtection.DocumentProtectionEnforced,
            beforeProtection.DocumentProtectionEditMode,
            afterProtection.DocumentProtectionEnforced,
            afterProtection.DocumentProtectionEditMode,
            protectionMetadataChanged,
            unmodeledDocumentProtectionMetadata,
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
        if (protection.HasMalformedProtectionMetadata && !patch.IsNoOp)
        {
            items.Add(new WordPackagePatchRiskItem(
                "protection_metadata_malformed",
                WordPackagePatchRiskSeverity.Block,
                "The base or result package contains unmodeled document-protection metadata or incomplete or ambiguous Word permission-range metadata; a generic package mutation cannot determine an authorized edit scope.",
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
        var documentProtectionMetadata = DocumentProtectionMetadata(
            package,
            document.MainPartUri,
            cancellationToken
        );
        var permissions = new WordReviewGraphBuilder().BuildPermissions(
            package,
            document,
            cancellationToken
        );
        var permissionIssueCodes = permissions.Issues
            .Where(issue => issue.Code.StartsWith("PERMISSION_", StringComparison.Ordinal))
            .Select(issue => issue.Code)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var malformedPermissionRangeCount = permissions.Permissions.Count(permission =>
            permission.Status != WordReviewRangeStatus.Complete
        );
        if (permissionIssueCodes.Length != 0)
        {
            malformedPermissionRangeCount = Math.Max(
                malformedPermissionRangeCount,
                1
            );
        }
        return new ProtectionEvidence(
            documentProtectionMetadata.Enforced,
            documentProtectionMetadata.EditMode,
            documentProtectionMetadata.Fingerprint,
            documentProtectionMetadata.Unmodeled,
            permissions.Permissions.Count,
            malformedPermissionRangeCount,
            permissions.IssuesTruncated && permissions.Permissions.Count != 0,
            permissionIssueCodes
        );
    }

    private static DocumentProtectionMetadataEvidence DocumentProtectionMetadata(
        OpcPackageSnapshot package,
        string mainPartUri,
        CancellationToken cancellationToken
    )
    {
        const string transitionalSettingsRelationship =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings";
        const string strictSettingsRelationship =
            "http://purl.oclc.org/ooxml/officeDocument/relationships/settings";
        const string settingsContentType =
            "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml";
        var relationships = package.RelationshipsFrom(mainPartUri)
            .Where(relationship =>
                relationship.Type is
                    transitionalSettingsRelationship
                    or strictSettingsRelationship
            )
            .ToArray();
        if (relationships.Length == 0)
        {
            return new DocumentProtectionMetadataEvidence(false, null);
        }
        if (
            relationships.Length != 1
            || relationships[0].ResolvedTargetPartUri is not { } partUri
            || !package.Parts.TryGetValue(partUri, out var part)
            || !string.Equals(
                part.ContentType,
                settingsContentType,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return new DocumentProtectionMetadataEvidence(true, null);
        }
        LosslessXmlDocument source;
        try
        {
            source = LosslessXmlDocument.Parse(
                part.Entry.Content,
                cancellationToken: cancellationToken
            );
        }
        catch (LosslessXmlException)
        {
            return new DocumentProtectionMetadataEvidence(true, null);
        }
        if (
            source.Root.LocalName != "settings"
            || source.Root.NamespaceUri is not
                "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                and not "http://purl.oclc.org/ooxml/wordprocessingml/main"
        )
        {
            return new DocumentProtectionMetadataEvidence(true, source.SourceSha256);
        }
        var elements = source.Elements.Where(candidate =>
            candidate.LocalName == "documentProtection"
        ).ToArray();
        if (elements.Length == 0)
        {
            return new DocumentProtectionMetadataEvidence(false, null, false, null);
        }
        var unmodeled = elements.Length != 1
            || elements[0].ParentOrdinal != source.Root.Ordinal
            || elements[0].NamespaceUri != source.Root.NamespaceUri
            || elements[0].NamespaceUri is not
                "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                and not "http://purl.oclc.org/ooxml/wordprocessingml/main";
        var enforced = false;
        string? editMode = null;
        if (!unmodeled)
        {
            var element = elements[0];
            var rawEnforcement = element.Attributes.FirstOrDefault(attribute =>
                attribute.LocalName == "enforcement"
                && attribute.NamespaceUri == element.NamespaceUri
            )?.Value;
            // Word enforces documentProtection when enforcement is omitted,
            // despite the default described by the base OOXML standard.
            var parsedEnforcement = rawEnforcement is null
                ? true
                : ParseOnOff(rawEnforcement);
            editMode = element.Attributes.FirstOrDefault(attribute =>
                attribute.LocalName == "edit"
                && attribute.NamespaceUri == element.NamespaceUri
            )?.Value;
            if (
                !HasModeledDocumentProtectionContent(element)
                || !HasModeledDocumentProtectionAttributes(element)
                || parsedEnforcement is null
                || editMode?.Length > 64
                || !IsDocumentProtectionEditMode(editMode)
            )
            {
                unmodeled = true;
                editMode = null;
            }
            else
            {
                enforced = parsedEnforcement.Value;
            }
        }
        var fingerprint = !unmodeled && elements.Length == 1
            ? DocumentProtectionFingerprint(source.Root, elements[0])
            : Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        elements.Length == 1
                            ? source.SourceBytes.Span.Slice(
                                elements[0].FullSpan.ByteOffset,
                                elements[0].FullSpan.ByteLength
                            )
                            : source.SourceBytes.Span
                    )
                )
                .ToLowerInvariant();
        return new DocumentProtectionMetadataEvidence(
            unmodeled,
            fingerprint,
            enforced,
            editMode
        );
    }

    private static bool HasModeledDocumentProtectionContent(
        XmlSourceElement element
    ) => element.Children.Count == 0
        && !element.HasLexicalMarkupInContent
        && string.IsNullOrWhiteSpace(element.Value);

    private static bool HasModeledDocumentProtectionAttributes(
        XmlSourceElement element
    )
    {
        const string xmlNamespaceDeclaration = "http://www.w3.org/2000/xmlns/";
        foreach (var attribute in element.Attributes)
        {
            if (attribute.NamespaceUri == xmlNamespaceDeclaration)
            {
                continue;
            }
            if (attribute.NamespaceUri != element.NamespaceUri)
            {
                if (
                    attribute.NamespaceUri.Length == 0
                    || DocumentProtectionAttributeNames.Contains(attribute.LocalName)
                )
                {
                    return false;
                }
                continue;
            }
            if (
                !DocumentProtectionAttributeNames.Contains(attribute.LocalName)
                || !IsValidDocumentProtectionAttribute(
                    attribute.LocalName,
                    attribute.Value
                )
            )
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsValidDocumentProtectionAttribute(
        string localName,
        string value
    ) => localName switch
    {
        "edit" => value.Length <= 64 && IsDocumentProtectionEditMode(value),
        "enforcement" or "formatting" => ParseOnOff(value) is not null,
        "cryptProviderType" => value is "rsaAES" or "rsaFull" or "custom",
        "cryptAlgorithmClass" => value is "hash" or "custom",
        "cryptAlgorithmType" => value is "typeAny" or "custom",
        "cryptAlgorithmSid" => value.Trim() is "1" or "2" or "3" or "4" or "12" or "13" or "14",
        "cryptSpinCount" => TryParseBoundedUInt32(value, 5_000_000),
        "spinCount" => TryParseNonNegativeInt32(value),
        "algIdExt" or "cryptProviderTypeExt" => IsLongHexNumber(value),
        "hash" or "salt" or "hashValue" or "saltValue" => IsBoundedBase64(value),
        "algorithmName" => value.Length <= 256,
        "cryptProvider" or "algIdExtSource" or "cryptProviderTypeExtSource" => value.Length <= 2_048,
        _ => false,
    };

    private static bool TryParseBoundedUInt32(string value, uint maximum) =>
        uint.TryParse(
            value.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsed
        ) && parsed <= maximum;

    private static bool TryParseNonNegativeInt32(string value) =>
        int.TryParse(
            value.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed
        ) && parsed >= 0;

    private static bool IsLongHexNumber(string value) => value.Length == 8
        && value.All(character =>
            character is >= '0' and <= '9'
                or >= 'a' and <= 'f'
                or >= 'A' and <= 'F'
        );

    private static bool IsBoundedBase64(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 8_192)
        {
            return false;
        }
        return Convert.TryFromBase64String(
            value,
            new byte[value.Length],
            out _
        );
    }

    private static readonly IReadOnlySet<string> DocumentProtectionAttributeNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "algIdExt",
            "algIdExtSource",
            "algorithmName",
            "cryptAlgorithmClass",
            "cryptAlgorithmSid",
            "cryptAlgorithmType",
            "cryptProvider",
            "cryptProviderType",
            "cryptProviderTypeExt",
            "cryptProviderTypeExtSource",
            "cryptSpinCount",
            "edit",
            "enforcement",
            "formatting",
            "hash",
            "hashValue",
            "salt",
            "saltValue",
            "spinCount",
        };

    private static bool? ParseOnOff(string? value) => value switch
    {
        "false" or "0" or "off" => false,
        "true" or "1" or "on" => true,
        _ => null,
    };

    private static bool IsDocumentProtectionEditMode(string? value) => value is
        null
        or "none"
        or "readOnly"
        or "comments"
        or "trackedChanges"
        or "forms";

    private static string DocumentProtectionFingerprint(
        XmlSourceElement root,
        XmlSourceElement element
    )
    {
        const string xmlNamespaceDeclaration = "http://www.w3.org/2000/xmlns/";
        var canonical = new StringBuilder();
        AppendFingerprintField(canonical, root.NamespaceUri);
        AppendFingerprintField(canonical, root.LocalName);
        AppendFingerprintField(canonical, element.NamespaceUri);
        AppendFingerprintField(canonical, element.LocalName);
        foreach (
            var attribute in element.Attributes
                .Where(attribute =>
                    attribute.NamespaceUri != xmlNamespaceDeclaration
                    && attribute.QualifiedName != "xmlns"
                )
                .OrderBy(attribute => attribute.NamespaceUri, StringComparer.Ordinal)
                .ThenBy(attribute => attribute.LocalName, StringComparer.Ordinal)
        )
        {
            AppendFingerprintField(canonical, attribute.NamespaceUri);
            AppendFingerprintField(canonical, attribute.LocalName);
            AppendFingerprintField(canonical, attribute.Value);
        }
        return Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    Encoding.UTF8.GetBytes(canonical.ToString())
                )
            )
            .ToLowerInvariant();
    }

    private static void AppendFingerprintField(StringBuilder builder, string value)
    {
        builder.Append(value.Length);
        builder.Append(':');
        builder.Append(value);
    }

    private sealed record ProtectionEvidence(
        bool DocumentProtectionEnforced,
        string? DocumentProtectionEditMode,
        string? DocumentProtectionFingerprint,
        bool UnmodeledDocumentProtectionMetadata,
        int PermissionRangeCount,
        int MalformedPermissionRangeCount,
        bool PermissionIssuesTruncated,
        IReadOnlyList<string> PermissionIssueCodes
    );

    private sealed record DocumentProtectionMetadataEvidence(
        bool Unmodeled,
        string? Fingerprint,
        bool Enforced = false,
        string? EditMode = null
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
