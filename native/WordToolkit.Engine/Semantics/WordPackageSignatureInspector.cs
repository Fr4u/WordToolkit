using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using WordToolkit.Engine.Packaging;

namespace WordToolkit.Engine.Semantics;

public sealed record WordPackageSignatureInspectionLimits
{
    public int MaximumSignatures { get; init; } = 64;

    public int MaximumSignatureBytes { get; init; } = 4 * 1024 * 1024;

    public int MaximumManifestReferences { get; init; } = 50_000;

    public int MaximumCertificates { get; init; } = 32;

    public int MaximumCertificateBytes { get; init; } = 1024 * 1024;

    public int MaximumXmlDepth { get; init; } = 64;

    internal void Validate()
    {
        if (
            MaximumSignatures is < 1 or > 1024
            || MaximumSignatureBytes is < 1024 or > 32 * 1024 * 1024
            || MaximumManifestReferences is < 1 or > 1_000_000
            || MaximumCertificates is < 1 or > 1024
            || MaximumCertificateBytes is < 1024 or > 16 * 1024 * 1024
            || MaximumXmlDepth is < 8 or > 256
        )
        {
            throw new ArgumentOutOfRangeException(nameof(WordPackageSignatureInspectionLimits));
        }
    }
}

public enum WordPackageSignatureStatus
{
    Valid,
    Invalid,
    Unsupported,
    Indeterminate,
}

public enum WordPackageSignatureReferenceKind
{
    Part,
    Relationships,
    InternalObject,
}

public sealed record WordPackageSignatureReferenceResult(
    int ReferenceIndex,
    WordPackageSignatureReferenceKind Kind,
    string TargetId,
    string? PartUri,
    string DigestAlgorithm,
    IReadOnlyList<string> TransformAlgorithms,
    bool DigestVerified,
    bool WeakAlgorithm,
    int SelectedRelationshipCount,
    string? FailureCode
);

public sealed record WordPackageSignatureCertificateResult(
    bool Present,
    string? Sha256,
    string? PublicKeyAlgorithm,
    bool? TimeValidAtInspection,
    bool ChainTrustVerified,
    bool RevocationChecked
);

public sealed record WordPackageSignatureResult(
    string SignatureId,
    string? SignaturePartUri,
    WordPackageSignatureStatus Status,
    bool TopologyValid,
    bool SignatureValueVerified,
    bool ManifestReferencesVerified,
    int ManifestReferenceCount,
    int SignedPartCount,
    int SignedRelationshipPartCount,
    int SelectedRelationshipCount,
    string SignatureAlgorithm,
    string CanonicalizationAlgorithm,
    bool WeakAlgorithm,
    WordPackageSignatureCertificateResult Certificate,
    IReadOnlyList<WordPackageSignatureReferenceResult> References,
    IReadOnlyList<string> IssueCodes
);

public sealed record WordPackageSignatureInspection(
    string PackageFingerprint,
    bool SignatureOriginDeclared,
    int SignatureOriginCount,
    int SignatureCount,
    int ValidSignatureCount,
    int InvalidSignatureCount,
    int UnsupportedSignatureCount,
    int IndeterminateSignatureCount,
    bool AllDiscoveredSignaturesValid,
    bool CryptographicIntegrityValidationPerformed,
    bool CertificateChainTrustVerified,
    bool RevocationChecked,
    IReadOnlyList<WordPackageSignatureResult> Signatures,
    IReadOnlyList<string> IssueCodes
);

public sealed class WordPackageSignatureInspectionLimitException(string message)
    : IOException(message);

/// <summary>
/// Verifies OPC XML signature topology, XMLDSIG signature values and signed package-part or
/// relationship digests without opening Word, using the network or treating an embedded
/// certificate as a trusted identity. Certificate-chain and revocation policy are deliberately
/// outside this operation.
/// </summary>
public sealed class WordPackageSignatureInspector
{
    public const string SignatureOriginRelationshipType =
        "http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/origin";
    public const string SignatureRelationshipType =
        "http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/signature";
    public const string CertificateRelationshipType =
        "http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/certificate";
    public const string SignatureOriginContentType =
        "application/vnd.openxmlformats-package.digital-signature-origin";
    public const string SignatureContentType =
        "application/vnd.openxmlformats-package.digital-signature-xmlsignature+xml";
    public const string CertificateContentType =
        "application/vnd.openxmlformats-package.digital-signature-certificate";
    public const string XmlDsigNamespace = SignedXml.XmlDsigNamespaceUrl;
    public const string OpcSignatureNamespace =
        "http://schemas.openxmlformats.org/package/2006/digital-signature";
    public const string RelationshipTransformAlgorithm =
        "http://schemas.openxmlformats.org/package/2006/RelationshipTransform";

    private const string RelationshipsNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string Sha1Algorithm = SignedXml.XmlDsigSHA1Url;
    private const string Sha256Algorithm = SignedXml.XmlDsigSHA256Url;
    private const string Sha384Algorithm = "http://www.w3.org/2001/04/xmldsig-more#sha384";
    private const string Sha512Algorithm = SignedXml.XmlDsigSHA512Url;
    private const string RsaSha1Algorithm = SignedXml.XmlDsigRSASHA1Url;
    private const string RsaSha256Algorithm = SignedXml.XmlDsigRSASHA256Url;
    private const string RsaSha384Algorithm = SignedXml.XmlDsigRSASHA384Url;
    private const string RsaSha512Algorithm = SignedXml.XmlDsigRSASHA512Url;
    private const string EcdsaSha256Algorithm =
        "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256";
    private const string EcdsaSha384Algorithm =
        "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha384";
    private const string EcdsaSha512Algorithm =
        "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha512";

    private static readonly HashSet<string> SupportedCanonicalizationAlgorithms =
        new(StringComparer.Ordinal)
        {
            SignedXml.XmlDsigC14NTransformUrl,
            SignedXml.XmlDsigC14NWithCommentsTransformUrl,
            SignedXml.XmlDsigExcC14NTransformUrl,
            SignedXml.XmlDsigExcC14NWithCommentsTransformUrl,
        };

    private static readonly HashSet<string> SupportedSignatureAlgorithms =
        new(StringComparer.Ordinal)
        {
            RsaSha1Algorithm,
            RsaSha256Algorithm,
            RsaSha384Algorithm,
            RsaSha512Algorithm,
            EcdsaSha256Algorithm,
            EcdsaSha384Algorithm,
            EcdsaSha512Algorithm,
        };

    private static readonly HashSet<string> SupportedDigestAlgorithms =
        new(StringComparer.Ordinal)
        {
            Sha1Algorithm,
            Sha256Algorithm,
            Sha384Algorithm,
            Sha512Algorithm,
        };

    private readonly WordPackageSignatureInspectionLimits _limits;

    public WordPackageSignatureInspector(WordPackageSignatureInspectionLimits? limits = null)
    {
        _limits = limits ?? new WordPackageSignatureInspectionLimits();
        _limits.Validate();
    }

    public WordPackageSignatureInspection Inspect(
        OpcPackageSnapshot package,
        bool includeSource = false,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();
        var issues = new SortedSet<string>(StringComparer.Ordinal);
        var origins = package.RelationshipsFrom("/")
            .Where(item => string.Equals(
                item.Type,
                SignatureOriginRelationshipType,
                StringComparison.Ordinal
            ))
            .ToArray();
        if (origins.Length > _limits.MaximumSignatures)
        {
            throw Limit("Digital-signature origin count exceeds the inspection limit.");
        }
        if (origins.Any(item =>
            item.TargetMode != OpcRelationshipTargetMode.Internal
            || item.ResolvedTargetPartUri is null
        ))
        {
            issues.Add("signature_origin_target_invalid");
        }
        var originUris = origins
            .Where(item =>
                item.TargetMode == OpcRelationshipTargetMode.Internal
                && item.ResolvedTargetPartUri is not null
            )
            .Select(item => item.ResolvedTargetPartUri!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        if (originUris.Length != origins.Length)
        {
            issues.Add("signature_origin_duplicate_or_unresolved");
        }

        var signatureUris = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var originUri in originUris)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!package.Parts.TryGetValue(originUri, out var originPart))
            {
                issues.Add("signature_origin_part_missing");
                continue;
            }
            if (!string.Equals(
                originPart.ContentType,
                SignatureOriginContentType,
                StringComparison.OrdinalIgnoreCase
            ))
            {
                issues.Add("signature_origin_content_type_invalid");
                continue;
            }
            var signatureRelationships = package.RelationshipsFrom(originUri)
                .Where(item => string.Equals(
                    item.Type,
                    SignatureRelationshipType,
                    StringComparison.Ordinal
                ))
                .ToArray();
            if (signatureRelationships.Length == 0)
            {
                issues.Add("signature_origin_has_no_signatures");
            }
            foreach (var relationship in signatureRelationships)
            {
                if (
                    relationship.TargetMode != OpcRelationshipTargetMode.Internal
                    || relationship.ResolvedTargetPartUri is null
                )
                {
                    issues.Add("signature_part_target_invalid");
                    continue;
                }
                signatureUris.Add(relationship.ResolvedTargetPartUri);
                if (signatureUris.Count > _limits.MaximumSignatures)
                {
                    throw Limit("Digital-signature count exceeds the inspection limit.");
                }
            }
        }

        var signatures = new List<WordPackageSignatureResult>(signatureUris.Count);
        foreach (var signatureUri in signatureUris)
        {
            cancellationToken.ThrowIfCancellationRequested();
            signatures.Add(InspectSignature(
                package,
                signatureUri,
                includeSource,
                cancellationToken
            ));
        }

        var valid = signatures.Count(item => item.Status == WordPackageSignatureStatus.Valid);
        var invalid = signatures.Count(item => item.Status == WordPackageSignatureStatus.Invalid);
        var unsupported = signatures.Count(item =>
            item.Status == WordPackageSignatureStatus.Unsupported
        );
        var indeterminate = signatures.Count(item =>
            item.Status == WordPackageSignatureStatus.Indeterminate
        );
        foreach (var signature in signatures)
        {
            foreach (var issue in signature.IssueCodes)
            {
                issues.Add(issue);
            }
        }
        return new WordPackageSignatureInspection(
            package.Fingerprint,
            origins.Length > 0,
            originUris.Length,
            signatures.Count,
            valid,
            invalid,
            unsupported,
            indeterminate,
            signatures.Count > 0
                && valid == signatures.Count
                && !issues.Any(IsHardInvalidIssue),
            signatures.Any(item =>
                item.ManifestReferenceCount > 0
                || (
                    item.Certificate.Present
                    && SupportedSignatureAlgorithms.Contains(item.SignatureAlgorithm)
                    && SupportedCanonicalizationAlgorithms.Contains(
                        item.CanonicalizationAlgorithm
                    )
                )
            ),
            CertificateChainTrustVerified: false,
            RevocationChecked: false,
            signatures.AsReadOnly(),
            issues.ToArray()
        );
    }

    private WordPackageSignatureResult InspectSignature(
        OpcPackageSnapshot package,
        string signatureUri,
        bool includeSource,
        CancellationToken cancellationToken
    )
    {
        var issueCodes = new SortedSet<string>(StringComparer.Ordinal);
        if (!package.Parts.TryGetValue(signatureUri, out var signaturePart))
        {
            issueCodes.Add("signature_part_missing");
            return EmptySignature(signatureUri, includeSource, issueCodes);
        }
        if (!string.Equals(
            signaturePart.ContentType,
            SignatureContentType,
            StringComparison.OrdinalIgnoreCase
        ))
        {
            issueCodes.Add("signature_part_content_type_invalid");
        }
        if (signaturePart.Entry.Content.Length > _limits.MaximumSignatureBytes)
        {
            throw Limit("A digital-signature part exceeds the inspection limit.");
        }

        XmlDocument document;
        try
        {
            document = ParseXml(signaturePart.Entry.Content.Span, cancellationToken);
        }
        catch (Exception exception) when (exception is XmlException or DecoderFallbackException)
        {
            issueCodes.Add("signature_xml_invalid");
            return EmptySignature(signatureUri, includeSource, issueCodes);
        }
        var root = document.DocumentElement;
        if (root is null
            || root.LocalName != "Signature"
            || root.NamespaceURI != XmlDsigNamespace)
        {
            issueCodes.Add("signature_root_invalid");
            return EmptySignature(signatureUri, includeSource, issueCodes);
        }

        var signatureId = StableId("wdsig_", signatureUri, signaturePart.Entry.Sha256);
        if (!ValidateUniqueIds(document))
        {
            issueCodes.Add("signature_duplicate_xml_id");
        }
        SignedXml signedXml;
        string signatureAlgorithm;
        string canonicalizationAlgorithm;
        try
        {
            signedXml = new SignedXml(document) { Resolver = null! };
            signedXml.LoadXml(root);
            signatureAlgorithm = signedXml.SignedInfo?.SignatureMethod ?? string.Empty;
            canonicalizationAlgorithm =
                signedXml.SignedInfo?.CanonicalizationMethod ?? string.Empty;
        }
        catch (Exception exception) when (
            exception is CryptographicException
                or XmlException
                or InvalidOperationException
        )
        {
            issueCodes.Add("signature_structure_invalid");
            return EmptySignature(
                signatureUri,
                includeSource,
                issueCodes,
                signatureId
            );
        }

        var unsupported = false;
        if (!SupportedSignatureAlgorithms.Contains(signatureAlgorithm))
        {
            issueCodes.Add("signature_algorithm_unsupported");
            unsupported = true;
        }
        if (!SupportedCanonicalizationAlgorithms.Contains(canonicalizationAlgorithm))
        {
            issueCodes.Add("signature_canonicalization_unsupported");
            unsupported = true;
        }
        if (!ValidateSignedInfoReferences(signedXml, document, issueCodes))
        {
            unsupported = true;
        }

        var certificates = ReadCertificates(package, signatureUri, document, issueCodes);
        try
        {
            var verification = VerifySignatureValue(
                signedXml,
                certificates,
                unsupported,
                issueCodes
            );
            var references = VerifyManifestReferences(
                package,
                signedXml,
                document,
                cancellationToken,
                issueCodes,
                out var referenceUnsupported
            );
            unsupported |= referenceUnsupported;
            var manifestVerified = references.Count > 0
                && references.All(item => item.DigestVerified);
            if (references.Count == 0)
            {
                issueCodes.Add("signature_manifest_reference_missing");
            }
            if (references.Any(item => string.Equals(
                item.FailureCode,
                "signature_reference_digest_mismatch",
                StringComparison.Ordinal
            )))
            {
                issueCodes.Add("signature_manifest_digest_mismatch");
            }
            var certificateResult = CertificateResult(
                certificates,
                verification.Certificate
            );
            var status = Status(
                unsupported,
                verification.Certificate is null,
                verification.SignatureValueVerified,
                manifestVerified,
                issueCodes
            );
            return new WordPackageSignatureResult(
                signatureId,
                includeSource ? signatureUri : null,
                status,
                TopologyValid: !issueCodes.Any(IsTopologyIssue),
                verification.SignatureValueVerified,
                manifestVerified,
                references.Count,
                references.Count(item => item.Kind == WordPackageSignatureReferenceKind.Part),
                references.Count(item =>
                    item.Kind == WordPackageSignatureReferenceKind.Relationships
                ),
                references.Sum(item => item.SelectedRelationshipCount),
                signatureAlgorithm,
                canonicalizationAlgorithm,
                IsWeakAlgorithm(signatureAlgorithm)
                    || references.Any(item => item.WeakAlgorithm),
                certificateResult,
                references,
                issueCodes.ToArray()
            );
        }
        finally
        {
            foreach (var certificate in certificates)
            {
                certificate.Dispose();
            }
        }
    }

    private IReadOnlyList<WordPackageSignatureReferenceResult> VerifyManifestReferences(
        OpcPackageSnapshot package,
        SignedXml signedXml,
        XmlDocument signatureDocument,
        CancellationToken cancellationToken,
        SortedSet<string> issues,
        out bool unsupported
    )
    {
        unsupported = false;
        var nodes = SignedManifestReferences(signedXml, signatureDocument, issues);
        if (nodes.Count == 0)
        {
            return Array.Empty<WordPackageSignatureReferenceResult>();
        }
        if (nodes.Count > _limits.MaximumManifestReferences)
        {
            throw Limit("Digital-signature manifest references exceed the inspection limit.");
        }
        var results = new List<WordPackageSignatureReferenceResult>(nodes.Count);
        for (var index = 0; index < nodes.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reference = nodes[index];
            WordPackageSignatureReferenceResult result;
            try
            {
                result = VerifyManifestReference(
                    package,
                    reference,
                    index,
                    cancellationToken
                );
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                    or XmlException
                    or CryptographicException
                    or DecoderFallbackException
            )
            {
                var uri = reference.GetAttribute("URI");
                result = ReferenceFailure(
                    index,
                    StableId("wdsref_", index.ToString(), uri, "malformed"),
                    string.Empty,
                    Array.Empty<string>(),
                    weak: false,
                    "signature_reference_structure_invalid"
                );
            }
            results.Add(result);
            if (result.FailureCode is not null)
            {
                issues.Add(result.FailureCode);
                if (result.FailureCode.EndsWith("_unsupported", StringComparison.Ordinal))
                {
                    unsupported = true;
                }
            }
        }
        return results.AsReadOnly();
    }

    private static IReadOnlyList<XmlElement> SignedManifestReferences(
        SignedXml signedXml,
        XmlDocument signatureDocument,
        SortedSet<string> issues
    )
    {
        var signedObjects = new HashSet<XmlElement>(ReferenceEqualityComparer.Instance);
        if (signedXml.SignedInfo is not null)
        {
            foreach (Reference reference in signedXml.SignedInfo.References)
            {
                var uri = reference.Uri;
                if (string.IsNullOrWhiteSpace(uri) || !uri.StartsWith('#'))
                {
                    continue;
                }
                var id = uri[1..];
                var matches = Elements(signatureDocument)
                    .Where(item => Id(item) == id)
                    .ToArray();
                if (matches.Length == 1
                    && matches[0].LocalName == "Object"
                    && matches[0].NamespaceURI == XmlDsigNamespace)
                {
                    signedObjects.Add(matches[0]);
                }
            }
        }

        var references = new List<XmlElement>();
        foreach (var packageObject in Elements(signatureDocument).Where(item =>
            item.LocalName == "Object"
            && item.NamespaceURI == XmlDsigNamespace
        ))
        {
            var manifests = packageObject.ChildNodes
                .OfType<XmlElement>()
                .Where(item =>
                    item.LocalName == "Manifest"
                    && item.NamespaceURI == XmlDsigNamespace
                )
                .ToArray();
            if (manifests.Length > 1)
            {
                issues.Add("signature_manifest_structure_invalid");
            }
            if (manifests.Length > 0 && !signedObjects.Contains(packageObject))
            {
                issues.Add("signature_manifest_unsigned");
                continue;
            }
            foreach (var manifest in manifests)
            {
                references.AddRange(manifest.ChildNodes
                    .OfType<XmlElement>()
                    .Where(item =>
                        item.LocalName == "Reference"
                        && item.NamespaceURI == XmlDsigNamespace
                    ));
            }
        }
        return references.AsReadOnly();
    }

    private WordPackageSignatureReferenceResult VerifyManifestReference(
        OpcPackageSnapshot package,
        XmlElement reference,
        int index,
        CancellationToken cancellationToken
    )
    {
        var uri = reference.GetAttribute("URI");
        var digestMethod = Child(reference, "DigestMethod");
        var digestValue = Child(reference, "DigestValue");
        var digestAlgorithm = digestMethod?.GetAttribute("Algorithm") ?? string.Empty;
        var transforms = Children(Child(reference, "Transforms"), "Transform")
            .Select(item => item.GetAttribute("Algorithm"))
            .ToArray();
        var weak = IsWeakAlgorithm(digestAlgorithm);
        var targetId = StableId("wdsref_", index.ToString(), uri, digestAlgorithm);
        if (!SupportedDigestAlgorithms.Contains(digestAlgorithm))
        {
            return ReferenceFailure(
                index,
                targetId,
                digestAlgorithm,
                transforms,
                weak,
                "signature_digest_algorithm_unsupported"
            );
        }
        if (!TryParsePackageReferenceUri(uri, out var partUri, out var declaredContentType))
        {
            return ReferenceFailure(
                index,
                targetId,
                digestAlgorithm,
                transforms,
                weak,
                "signature_reference_uri_invalid"
            );
        }
        package.Parts.TryGetValue(partUri, out var part);
        var entry = part?.Entry ?? package.Entries.SingleOrDefault(item =>
            string.Equals(item.PartUri, partUri, StringComparison.Ordinal)
        );
        if (entry is null)
        {
            return ReferenceFailure(
                index,
                targetId,
                digestAlgorithm,
                transforms,
                weak,
                "signature_reference_part_missing",
                partUri
            );
        }
        if (declaredContentType is not null
            && !string.Equals(
                declaredContentType,
                part?.ContentType ?? package.ContentTypes.Resolve(partUri),
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return ReferenceFailure(
                index,
                targetId,
                digestAlgorithm,
                transforms,
                weak,
                "signature_reference_content_type_mismatch",
                partUri
            );
        }
        byte[] transformed;
        int selectedRelationships;
        WordPackageSignatureReferenceKind kind;
        try
        {
            transformed = ApplyTransforms(
                entry.Content,
                reference,
                transforms,
                cancellationToken,
                out selectedRelationships,
                out kind
            );
        }
        catch (UnsupportedSignatureTransformException)
        {
            return ReferenceFailure(
                index,
                targetId,
                digestAlgorithm,
                transforms,
                weak,
                "signature_transform_unsupported",
                partUri
            );
        }
        catch (Exception exception) when (
            exception is XmlException
                or CryptographicException
                or DecoderFallbackException
        )
        {
            return ReferenceFailure(
                index,
                targetId,
                digestAlgorithm,
                transforms,
                weak,
                "signature_transform_invalid",
                partUri
            );
        }
        var expected = DecodeDigest(digestValue?.InnerText);
        if (expected is null)
        {
            return ReferenceFailure(
                index,
                targetId,
                digestAlgorithm,
                transforms,
                weak,
                "signature_digest_value_invalid",
                partUri,
                kind,
                selectedRelationships
            );
        }
        var actual = Hash(digestAlgorithm, transformed);
        var verified = CryptographicOperations.FixedTimeEquals(actual, expected);
        return new WordPackageSignatureReferenceResult(
            index,
            kind,
            targetId,
            partUri,
            digestAlgorithm,
            transforms,
            verified,
            weak,
            selectedRelationships,
            verified ? null : "signature_reference_digest_mismatch"
        );
    }

    private byte[] ApplyTransforms(
        ReadOnlyMemory<byte> content,
        XmlElement reference,
        IReadOnlyList<string> transformAlgorithms,
        CancellationToken cancellationToken,
        out int selectedRelationshipCount,
        out WordPackageSignatureReferenceKind kind
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        selectedRelationshipCount = 0;
        kind = WordPackageSignatureReferenceKind.Part;
        byte[] current = content.ToArray();
        if (transformAlgorithms.Count == 0)
        {
            return current;
        }
        var transformElements = Children(Child(reference, "Transforms"), "Transform");
        if (transformElements.Count != transformAlgorithms.Count)
        {
            throw new XmlException("Transform structure is inconsistent.");
        }
        for (var index = 0; index < transformAlgorithms.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var algorithm = transformAlgorithms[index];
            if (string.Equals(algorithm, RelationshipTransformAlgorithm, StringComparison.Ordinal))
            {
                if (index != 0)
                {
                    throw new UnsupportedSignatureTransformException();
                }
                current = ApplyRelationshipTransform(
                    current,
                    transformElements[index],
                    out selectedRelationshipCount
                );
                kind = WordPackageSignatureReferenceKind.Relationships;
                continue;
            }
            if (SupportedCanonicalizationAlgorithms.Contains(algorithm))
            {
                current = Canonicalize(current, algorithm);
                continue;
            }
            throw new UnsupportedSignatureTransformException();
        }
        if (kind == WordPackageSignatureReferenceKind.Relationships
            && transformAlgorithms.Count != 2)
        {
            throw new UnsupportedSignatureTransformException();
        }
        return current;
    }

    private byte[] ApplyRelationshipTransform(
        byte[] bytes,
        XmlElement transform,
        out int selectedCount
    )
    {
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        var sourceTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (XmlNode node in transform.ChildNodes)
        {
            if (node is not XmlElement selector
                || selector.NamespaceURI != OpcSignatureNamespace)
            {
                continue;
            }
            if (selector.LocalName == "RelationshipReference")
            {
                var value = selector.GetAttribute("SourceId");
                if (string.IsNullOrEmpty(value) || !sourceIds.Add(value))
                {
                    throw new XmlException("Relationship selector is invalid.");
                }
            }
            else if (selector.LocalName == "RelationshipsGroupReference")
            {
                var value = selector.GetAttribute("SourceType");
                if (string.IsNullOrEmpty(value) || !sourceTypes.Add(value))
                {
                    throw new XmlException("Relationship selector is invalid.");
                }
            }
            else
            {
                throw new UnsupportedSignatureTransformException();
            }
        }
        if (sourceIds.Count == 0 && sourceTypes.Count == 0)
        {
            throw new XmlException("Relationship transform has no selectors.");
        }
        var document = ParseXml(bytes, CancellationToken.None);
        var root = document.DocumentElement;
        if (root is null
            || root.LocalName != "Relationships"
            || root.NamespaceURI != RelationshipsNamespace)
        {
            throw new XmlException("Relationship part root is invalid.");
        }
        var selected = root.ChildNodes
            .OfType<XmlElement>()
            .Where(item =>
                item.LocalName == "Relationship"
                && item.NamespaceURI == RelationshipsNamespace
                && (
                    sourceIds.Contains(item.GetAttribute("Id"))
                    || sourceTypes.Contains(item.GetAttribute("Type"))
                )
            )
            .OrderBy(item => item.GetAttribute("Id"), StringComparer.Ordinal)
            .ToArray();
        selectedCount = selected.Length;
        var output = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        var outputRoot = output.CreateElement("Relationships", RelationshipsNamespace);
        output.AppendChild(outputRoot);
        foreach (var relationship in selected)
        {
            var copied = output.CreateElement("Relationship", RelationshipsNamespace);
            foreach (var name in new[] { "Id", "Type", "Target", "TargetMode" })
            {
                if (relationship.HasAttribute(name))
                {
                    copied.SetAttribute(name, relationship.GetAttribute(name));
                }
            }
            outputRoot.AppendChild(copied);
        }
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = true,
            NewLineHandling = NewLineHandling.None,
            CloseOutput = false,
        }))
        {
            output.Save(writer);
        }
        return stream.ToArray();
    }

    private static byte[] Canonicalize(byte[] bytes, string algorithm)
    {
        var document = ParseXmlStatic(bytes);
        Transform transform = algorithm switch
        {
            SignedXml.XmlDsigC14NTransformUrl => new XmlDsigC14NTransform(),
            SignedXml.XmlDsigC14NWithCommentsTransformUrl =>
                new XmlDsigC14NWithCommentsTransform(),
            SignedXml.XmlDsigExcC14NTransformUrl => new XmlDsigExcC14NTransform(),
            SignedXml.XmlDsigExcC14NWithCommentsTransformUrl =>
                new XmlDsigExcC14NWithCommentsTransform(),
            _ => throw new UnsupportedSignatureTransformException(),
        };
        transform.Resolver = null;
        transform.LoadInput(document);
        using var output = (Stream)transform.GetOutput(typeof(Stream));
        using var buffer = new MemoryStream();
        output.CopyTo(buffer);
        return buffer.ToArray();
    }

    private IReadOnlyList<X509Certificate2> ReadCertificates(
        OpcPackageSnapshot package,
        string signatureUri,
        XmlDocument signatureDocument,
        SortedSet<string> issues
    )
    {
        var encoded = new List<byte[]>();
        var manager = new XmlNamespaceManager(signatureDocument.NameTable);
        manager.AddNamespace("ds", XmlDsigNamespace);
        var nodes = signatureDocument.SelectNodes("//ds:KeyInfo/ds:X509Data/ds:X509Certificate", manager);
        if (nodes is not null)
        {
            foreach (XmlNode node in nodes)
            {
                if (encoded.Count >= _limits.MaximumCertificates)
                {
                    throw Limit("Embedded signature certificates exceed the inspection limit.");
                }
                var value = DecodeCertificate(node.InnerText);
                if (value is null)
                {
                    issues.Add("signature_certificate_invalid");
                    continue;
                }
                encoded.Add(value);
            }
        }
        foreach (var relationship in package.RelationshipsFrom(signatureUri).Where(item =>
            string.Equals(item.Type, CertificateRelationshipType, StringComparison.Ordinal)
        ))
        {
            if (encoded.Count >= _limits.MaximumCertificates)
            {
                throw Limit("Signature certificate parts exceed the inspection limit.");
            }
            if (
                relationship.TargetMode != OpcRelationshipTargetMode.Internal
                || relationship.ResolvedTargetPartUri is null
                || !package.Parts.TryGetValue(relationship.ResolvedTargetPartUri, out var part)
            )
            {
                issues.Add("signature_certificate_part_invalid");
                continue;
            }
            if (!string.Equals(
                part.ContentType,
                CertificateContentType,
                StringComparison.OrdinalIgnoreCase
            ))
            {
                issues.Add("signature_certificate_content_type_invalid");
                continue;
            }
            if (part.Entry.Content.Length > _limits.MaximumCertificateBytes)
            {
                throw Limit("A signature certificate part exceeds the inspection limit.");
            }
            encoded.Add(part.Entry.Content.ToArray());
        }
        var certificates = new List<X509Certificate2>();
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bytes in encoded)
        {
            try
            {
                var certificate = new X509Certificate2(bytes);
                var hash = Sha256(certificate.RawData);
                if (!hashes.Add(hash))
                {
                    certificate.Dispose();
                    continue;
                }
                certificates.Add(certificate);
            }
            catch (CryptographicException)
            {
                issues.Add("signature_certificate_invalid");
            }
        }
        return certificates.AsReadOnly();
    }

    private static SignatureVerification VerifySignatureValue(
        SignedXml signedXml,
        IReadOnlyList<X509Certificate2> certificates,
        bool unsupported,
        SortedSet<string> issues
    )
    {
        if (unsupported)
        {
            return new SignatureVerification(false, null);
        }
        foreach (var certificate in certificates)
        {
            try
            {
                if (signedXml.CheckSignature(certificate, verifySignatureOnly: true))
                {
                    return new SignatureVerification(true, certificate);
                }
            }
            catch (Exception exception) when (
                exception is CryptographicException
                    or InvalidOperationException
                    or XmlException
            )
            {
                issues.Add("signature_value_invalid");
            }
        }
        if (certificates.Count > 0)
        {
            issues.Add("signature_value_invalid");
        }
        else
        {
            issues.Add("signature_certificate_missing");
        }
        return new SignatureVerification(false, null);
    }

    private static bool ValidateSignedInfoReferences(
        SignedXml signedXml,
        XmlDocument document,
        SortedSet<string> issues
    )
    {
        var valid = true;
        if (signedXml.SignedInfo is null || signedXml.SignedInfo.References.Count == 0)
        {
            issues.Add("signature_signed_info_reference_missing");
            return false;
        }
        foreach (Reference reference in signedXml.SignedInfo.References)
        {
            var uri = reference.Uri;
            if (string.IsNullOrWhiteSpace(uri) || !uri.StartsWith('#'))
            {
                issues.Add("signature_signed_info_reference_unsupported");
                valid = false;
                continue;
            }
            var id = uri[1..];
            var matches = Elements(document)
                .Count(item => Id(item) == id);
            if (matches != 1)
            {
                issues.Add("signature_signed_info_reference_ambiguous");
                valid = false;
            }
            var digestMethod = reference.DigestMethod ?? string.Empty;
            if (!SupportedDigestAlgorithms.Contains(digestMethod))
            {
                issues.Add("signature_signed_info_digest_unsupported");
                valid = false;
            }
            foreach (Transform transform in reference.TransformChain)
            {
                if (!SupportedCanonicalizationAlgorithms.Contains(
                    transform.Algorithm ?? string.Empty
                ))
                {
                    issues.Add("signature_signed_info_transform_unsupported");
                    valid = false;
                }
            }
        }
        return valid;
    }

    private static bool ValidateUniqueIds(XmlDocument document)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in Elements(document))
        {
            var id = Id(element);
            if (id is not null && !ids.Add(id))
            {
                return false;
            }
        }
        return true;
    }

    private static IEnumerable<XmlElement> Elements(XmlDocument document)
    {
        var nodes = document.SelectNodes("//*");
        if (nodes is null)
        {
            yield break;
        }
        foreach (XmlNode node in nodes)
        {
            if (node is XmlElement element)
            {
                yield return element;
            }
        }
    }

    private static string? Id(XmlElement element)
    {
        foreach (var name in new[] { "Id", "ID", "id" })
        {
            if (element.HasAttribute(name))
            {
                return element.GetAttribute(name);
            }
        }
        return null;
    }

    private XmlDocument ParseXml(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ParseXmlStatic(bytes.ToArray(), _limits.MaximumXmlDepth);
    }

    private static XmlDocument ParseXmlStatic(byte[] bytes, int maximumDepth = 64)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = bytes.Length,
            IgnoreComments = false,
            IgnoreWhitespace = false,
            CloseInput = false,
        };
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, settings);
        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        document.Load(new DepthLimitedXmlReader(reader, maximumDepth));
        return document;
    }

    private static XmlElement? Child(XmlElement? parent, string localName)
    {
        if (parent is null)
        {
            return null;
        }
        return parent.ChildNodes
            .OfType<XmlElement>()
            .SingleOrDefault(item =>
                item.LocalName == localName
                && item.NamespaceURI == XmlDsigNamespace
            );
    }

    private static IReadOnlyList<XmlElement> Children(XmlElement? parent, string localName)
    {
        if (parent is null)
        {
            return Array.Empty<XmlElement>();
        }
        return parent.ChildNodes
            .OfType<XmlElement>()
            .Where(item =>
                item.LocalName == localName
                && item.NamespaceURI == XmlDsigNamespace
            )
            .ToArray();
    }

    private static bool TryParsePackageReferenceUri(
        string uri,
        out string partUri,
        out string? contentType
    )
    {
        partUri = string.Empty;
        contentType = null;
        if (string.IsNullOrWhiteSpace(uri) || !uri.StartsWith('/'))
        {
            return false;
        }
        var question = uri.IndexOf('?');
        var rawPart = question < 0 ? uri : uri[..question];
        try
        {
            if (!OpcPartUri.TryFromEntryName(rawPart[1..], out var normalized, out _)
                || normalized is null)
            {
                return false;
            }
            partUri = normalized;
        }
        catch (Exception exception) when (exception is ArgumentException or UriFormatException)
        {
            return false;
        }
        if (question < 0)
        {
            return true;
        }
        var query = uri[(question + 1)..];
        const string prefix = "ContentType=";
        if (!query.StartsWith(prefix, StringComparison.Ordinal)
            || query.Length == prefix.Length
            || query.Contains('&'))
        {
            return false;
        }
        try
        {
            contentType = Uri.UnescapeDataString(query[prefix.Length..]);
            return !string.IsNullOrWhiteSpace(contentType);
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static WordPackageSignatureReferenceResult ReferenceFailure(
        int index,
        string targetId,
        string digestAlgorithm,
        IReadOnlyList<string> transforms,
        bool weak,
        string failureCode,
        string? partUri = null,
        WordPackageSignatureReferenceKind kind = WordPackageSignatureReferenceKind.Part,
        int selectedRelationshipCount = 0
    ) => new(
        index,
        kind,
        targetId,
        partUri,
        digestAlgorithm,
        transforms,
        DigestVerified: false,
        weak,
        selectedRelationshipCount,
        failureCode
    );

    private static WordPackageSignatureResult EmptySignature(
        string signatureUri,
        bool includeSource,
        SortedSet<string> issues,
        string? signatureId = null
    ) => new(
        signatureId ?? StableId("wdsig_", signatureUri),
        includeSource ? signatureUri : null,
        WordPackageSignatureStatus.Invalid,
        TopologyValid: false,
        SignatureValueVerified: false,
        ManifestReferencesVerified: false,
        ManifestReferenceCount: 0,
        SignedPartCount: 0,
        SignedRelationshipPartCount: 0,
        SelectedRelationshipCount: 0,
        SignatureAlgorithm: string.Empty,
        CanonicalizationAlgorithm: string.Empty,
        WeakAlgorithm: false,
        new WordPackageSignatureCertificateResult(
            Present: false,
            Sha256: null,
            PublicKeyAlgorithm: null,
            TimeValidAtInspection: null,
            ChainTrustVerified: false,
            RevocationChecked: false
        ),
        Array.Empty<WordPackageSignatureReferenceResult>(),
        issues.ToArray()
    );

    private static WordPackageSignatureCertificateResult CertificateResult(
        IReadOnlyList<X509Certificate2> certificates,
        X509Certificate2? signer
    )
    {
        var selected = signer ?? certificates.FirstOrDefault();
        return selected is null
            ? new WordPackageSignatureCertificateResult(
                Present: false,
                Sha256: null,
                PublicKeyAlgorithm: null,
                TimeValidAtInspection: null,
                ChainTrustVerified: false,
                RevocationChecked: false
            )
            : new WordPackageSignatureCertificateResult(
                Present: true,
                Sha256(selected.RawData),
                selected.PublicKey.Oid?.Value,
                DateTime.UtcNow >= selected.NotBefore.ToUniversalTime()
                    && DateTime.UtcNow <= selected.NotAfter.ToUniversalTime(),
                ChainTrustVerified: false,
                RevocationChecked: false
            );
    }

    private static WordPackageSignatureStatus Status(
        bool unsupported,
        bool certificateMissing,
        bool signatureValueVerified,
        bool manifestVerified,
        IReadOnlyCollection<string> issues
    )
    {
        if (issues.Any(IsHardInvalidIssue) || (!signatureValueVerified && !certificateMissing))
        {
            return WordPackageSignatureStatus.Invalid;
        }
        if (unsupported)
        {
            return WordPackageSignatureStatus.Unsupported;
        }
        if (certificateMissing)
        {
            return WordPackageSignatureStatus.Indeterminate;
        }
        return signatureValueVerified && manifestVerified
            ? WordPackageSignatureStatus.Valid
            : WordPackageSignatureStatus.Invalid;
    }

    private static bool IsTopologyIssue(string code) => code.Contains(
        "topology",
        StringComparison.Ordinal
    ) || code is "signature_part_missing"
        or "signature_root_invalid"
        or "signature_origin_part_missing"
        or "signature_part_target_invalid"
        or "signature_origin_content_type_invalid"
        or "signature_part_content_type_invalid"
        or "signature_certificate_content_type_invalid";

    private static bool IsHardInvalidIssue(string code) => code.EndsWith(
        "_mismatch",
        StringComparison.Ordinal
    ) || code.EndsWith("_invalid", StringComparison.Ordinal)
        || code is "signature_duplicate_xml_id"
        or "signature_manifest_unsigned"
        or "signature_manifest_reference_missing"
        or "signature_signed_info_reference_ambiguous";

    private static bool IsWeakAlgorithm(string algorithm) => string.Equals(
        algorithm,
        Sha1Algorithm,
        StringComparison.Ordinal
    ) || string.Equals(algorithm, RsaSha1Algorithm, StringComparison.Ordinal);

    private static byte[] Hash(string algorithm, byte[] bytes) => algorithm switch
    {
        Sha1Algorithm => SHA1.HashData(bytes),
        Sha256Algorithm => SHA256.HashData(bytes),
        Sha384Algorithm => SHA384.HashData(bytes),
        Sha512Algorithm => SHA512.HashData(bytes),
        _ => throw new CryptographicException("Unsupported digest algorithm."),
    };

    private static byte[]? DecodeDigest(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        try
        {
            return Convert.FromBase64String(value.Trim());
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private byte[]? DecodeCertificate(string value)
    {
        try
        {
            var decoded = Convert.FromBase64String(value.Trim());
            return decoded.Length is > 0 && decoded.Length <= _limits.MaximumCertificateBytes
                ? decoded
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string StableId(string prefix, params string[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var value in values)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(BitConverter.GetBytes(bytes.Length));
            hash.AppendData(bytes);
        }
        return prefix + Convert.ToHexString(hash.GetHashAndReset())[..24].ToLowerInvariant();
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(
        SHA256.HashData(bytes)
    ).ToLowerInvariant();

    private static WordPackageSignatureInspectionLimitException Limit(string message) =>
        new(message);

    private sealed record SignatureVerification(
        bool SignatureValueVerified,
        X509Certificate2? Certificate
    );

    private sealed class UnsupportedSignatureTransformException : CryptographicException;

    private sealed class DepthLimitedXmlReader(XmlReader inner, int maximumDepth) : XmlReader
    {
        public override int AttributeCount => inner.AttributeCount;
        public override string BaseURI => inner.BaseURI;
        public override int Depth => inner.Depth;
        public override bool EOF => inner.EOF;
        public override bool HasValue => inner.HasValue;
        public override bool IsEmptyElement => inner.IsEmptyElement;
        public override string LocalName => inner.LocalName;
        public override string NamespaceURI => inner.NamespaceURI;
        public override XmlNameTable NameTable => inner.NameTable;
        public override XmlNodeType NodeType => inner.NodeType;
        public override string Prefix => inner.Prefix;
        public override ReadState ReadState => inner.ReadState;
        public override string Value => inner.Value;
        public override string Name => inner.Name;
        public override string? GetAttribute(string name) => inner.GetAttribute(name);
        public override string? GetAttribute(string name, string? namespaceURI) =>
            inner.GetAttribute(name, namespaceURI);
        public override string GetAttribute(int i) => inner.GetAttribute(i);
        public override string? LookupNamespace(string prefix) => inner.LookupNamespace(prefix);
        public override bool MoveToAttribute(string name) => inner.MoveToAttribute(name);
        public override bool MoveToAttribute(string name, string? ns) =>
            inner.MoveToAttribute(name, ns);
        public override void MoveToAttribute(int i) => inner.MoveToAttribute(i);
        public override bool MoveToElement() => inner.MoveToElement();
        public override bool MoveToFirstAttribute() => inner.MoveToFirstAttribute();
        public override bool MoveToNextAttribute() => inner.MoveToNextAttribute();
        public override bool Read()
        {
            var result = inner.Read();
            if (result && inner.Depth > maximumDepth)
            {
                throw new XmlException("Signature XML exceeds the depth limit.");
            }
            return result;
        }
        public override bool ReadAttributeValue() => inner.ReadAttributeValue();
        public override void ResolveEntity() => inner.ResolveEntity();
    }
}
