using System.Security.Cryptography;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Operations;

public static class InspectOoxmlSignaturesContract
{
    public const string OperationName = "inspect_ooxml_signatures";
    public const string Contract = "wordtoolkit.inspect_ooxml_signatures/1.0";
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 100;
    public const int MaximumLocalPathCharacters = 32_767;
    public const int MaximumFileNameCharacters = 512;
}

public sealed record InspectOoxmlSignaturesRequest(
    string LocalPath,
    string View = "summary",
    string? SignatureId = null,
    int Offset = 0,
    int Limit = InspectOoxmlSignaturesContract.DefaultLimit,
    bool IncludeSource = false,
    bool IncludeCertificateHash = false
);

public sealed record InspectOoxmlSignatureItem(
    string SignatureId,
    string Status,
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
    bool CertificatePresent,
    string? CertificateSha256,
    string? PublicKeyAlgorithm,
    bool? CertificateTimeValidAtInspection,
    string? SignaturePartUri,
    IReadOnlyList<string> IssueCodes
);

public sealed record InspectOoxmlSignatureReferenceItem(
    string SignatureId,
    int ReferenceIndex,
    string Kind,
    string TargetId,
    string? PartUri,
    string DigestAlgorithm,
    IReadOnlyList<string> TransformAlgorithms,
    bool DigestVerified,
    bool WeakAlgorithm,
    int SelectedRelationshipCount,
    string? FailureCode
);

public sealed record InspectOoxmlSignaturePaging(
    int Offset,
    int Limit,
    int Returned,
    int Total,
    int? NextOffset
);

public sealed record InspectOoxmlSignatureSecurity(
    bool ReturnsDocumentContent,
    bool ReturnsRawXml,
    bool ReturnsCertificateBytes,
    bool ReturnsCertificateIdentity,
    bool ReturnsPaths,
    bool OpensWord,
    bool UsesNetwork,
    bool CertificateChainTrustVerified,
    bool RevocationChecked,
    bool ExternalReferencesResolved
);

public sealed record InspectOoxmlSignaturesResult(
    string OperationContract,
    string FileName,
    string PackageFingerprint,
    string View,
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
    IReadOnlyList<InspectOoxmlSignatureItem> Signatures,
    IReadOnlyList<InspectOoxmlSignatureReferenceItem> References,
    IReadOnlyList<string> Issues,
    InspectOoxmlSignaturePaging Paging,
    InspectOoxmlSignatureSecurity Security
);

/// <summary>
/// Exposes bounded OPC signature integrity evidence without returning signer identity,
/// certificate bytes, raw XML, document content or a local path. Embedded certificates are
/// verification keys, not trusted identities; chain and revocation checks remain false.
/// </summary>
public sealed class InspectOoxmlSignaturesOperation
{
    private static readonly HashSet<string> SupportedExtensions = new(
        [".docx", ".docm", ".dotx", ".dotm"],
        StringComparer.OrdinalIgnoreCase
    );

    private readonly OpcPackageReader _reader;
    private readonly WordPackageSignatureInspector _inspector;

    public InspectOoxmlSignaturesOperation(
        OpcPackageLimits? packageLimits = null,
        WordPackageSignatureInspectionLimits? signatureLimits = null
    )
    {
        _reader = new OpcPackageReader(packageLimits);
        _inspector = new WordPackageSignatureInspector(signatureLimits);
    }

    public InspectOoxmlSignaturesResult Execute(
        InspectOoxmlSignaturesRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        var path = ResolvePath(request.LocalPath);
        try
        {
            var package = _reader.Read(path, cancellationToken);
            var inspection = _inspector.Inspect(
                package,
                includeSource: request.IncludeSource,
                cancellationToken
            );
            return Project(
                inspection,
                Path.GetFileName(path),
                request,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (WordPackageSignatureInspectionLimitException exception)
        {
            throw new WordToolkitOperationException(
                "SIGNATURE_INSPECTION_LIMIT",
                "The package exceeds a bounded digital-signature inspection limit",
                innerException: exception
            );
        }
        catch (OpcPackageLimitException exception)
        {
            throw new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "The package exceeds a bounded OOXML inspection limit",
                innerException: exception
            );
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new WordToolkitOperationException(
                "ACCESS_DENIED",
                "The package cannot be read with current permissions",
                innerException: exception
            );
        }
        catch (FileNotFoundException exception)
        {
            throw NotFound(exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            throw NotFound(exception);
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or System.Xml.XmlException
                or CryptographicException
        )
        {
            throw new WordToolkitOperationException(
                "PACKAGE_INVALID",
                "The package could not be inspected for digital signatures",
                innerException: exception
            );
        }
    }

    internal InspectOoxmlSignaturesResult Project(
        WordPackageSignatureInspection inspection,
        string fileName,
        InspectOoxmlSignaturesRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ValidateRequest(request);
        ValidateFileName(fileName, requireLeafName: true);
        var matchingSignatures = inspection.Signatures
            .Where(item => request.SignatureId is null
                || string.Equals(item.SignatureId, request.SignatureId, StringComparison.Ordinal))
            .ToArray();
        if (request.SignatureId is not null && matchingSignatures.Length == 0)
        {
            throw InvalidInput("signature_id does not identify a signature in this package");
        }

        IReadOnlyList<InspectOoxmlSignatureItem> signatures =
            Array.Empty<InspectOoxmlSignatureItem>();
        IReadOnlyList<InspectOoxmlSignatureReferenceItem> references =
            Array.Empty<InspectOoxmlSignatureReferenceItem>();
        IReadOnlyList<string> issues = Array.Empty<string>();
        int total;
        if (request.View == "signatures")
        {
            total = matchingSignatures.Length;
            signatures = Page(matchingSignatures, request.Offset, request.Limit)
                .Select(item => SignatureItem(item, request))
                .ToArray();
        }
        else if (request.View == "references")
        {
            var flattened = matchingSignatures
                .SelectMany(signature => signature.References.Select(reference => (
                    Signature: signature,
                    Reference: reference
                )))
                .ToArray();
            total = flattened.Length;
            references = Page(flattened, request.Offset, request.Limit)
                .Select(item => ReferenceItem(item.Signature, item.Reference, request))
                .ToArray();
        }
        else if (request.View == "issues")
        {
            var selected = request.SignatureId is null
                ? inspection.IssueCodes
                : matchingSignatures.SelectMany(item => item.IssueCodes)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToArray();
            total = selected.Count;
            issues = Page(selected, request.Offset, request.Limit).ToArray();
        }
        else
        {
            total = 0;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var returned = request.View switch
        {
            "signatures" => signatures.Count,
            "references" => references.Count,
            "issues" => issues.Count,
            _ => 0,
        };
        return new InspectOoxmlSignaturesResult(
            InspectOoxmlSignaturesContract.Contract,
            fileName,
            inspection.PackageFingerprint,
            request.View,
            inspection.SignatureOriginDeclared,
            inspection.SignatureOriginCount,
            inspection.SignatureCount,
            inspection.ValidSignatureCount,
            inspection.InvalidSignatureCount,
            inspection.UnsupportedSignatureCount,
            inspection.IndeterminateSignatureCount,
            inspection.AllDiscoveredSignaturesValid,
            inspection.CryptographicIntegrityValidationPerformed,
            inspection.CertificateChainTrustVerified,
            inspection.RevocationChecked,
            signatures,
            references,
            issues,
            new InspectOoxmlSignaturePaging(
                request.Offset,
                request.Limit,
                returned,
                total,
                request.Offset + returned < total ? request.Offset + returned : null
            ),
            new InspectOoxmlSignatureSecurity(
                ReturnsDocumentContent: false,
                ReturnsRawXml: false,
                ReturnsCertificateBytes: false,
                ReturnsCertificateIdentity: false,
                ReturnsPaths: false,
                OpensWord: false,
                UsesNetwork: false,
                CertificateChainTrustVerified: false,
                RevocationChecked: false,
                ExternalReferencesResolved: false
            )
        );
    }

    private static InspectOoxmlSignatureItem SignatureItem(
        WordPackageSignatureResult item,
        InspectOoxmlSignaturesRequest request
    ) => new(
        item.SignatureId,
        SnakeCase(item.Status.ToString()),
        item.TopologyValid,
        item.SignatureValueVerified,
        item.ManifestReferencesVerified,
        item.ManifestReferenceCount,
        item.SignedPartCount,
        item.SignedRelationshipPartCount,
        item.SelectedRelationshipCount,
        item.SignatureAlgorithm,
        item.CanonicalizationAlgorithm,
        item.WeakAlgorithm,
        item.Certificate.Present,
        request.IncludeCertificateHash ? item.Certificate.Sha256 : null,
        request.IncludeCertificateHash ? item.Certificate.PublicKeyAlgorithm : null,
        item.Certificate.TimeValidAtInspection,
        request.IncludeSource ? item.SignaturePartUri : null,
        item.IssueCodes
    );

    private static InspectOoxmlSignatureReferenceItem ReferenceItem(
        WordPackageSignatureResult signature,
        WordPackageSignatureReferenceResult item,
        InspectOoxmlSignaturesRequest request
    ) => new(
        signature.SignatureId,
        item.ReferenceIndex,
        SnakeCase(item.Kind.ToString()),
        item.TargetId,
        request.IncludeSource ? item.PartUri : null,
        item.DigestAlgorithm,
        item.TransformAlgorithms,
        item.DigestVerified,
        item.WeakAlgorithm,
        item.SelectedRelationshipCount,
        item.FailureCode
    );

    private static IReadOnlyList<T> Page<T>(
        IReadOnlyList<T> items,
        int offset,
        int limit
    ) => items.Skip(offset).Take(limit).ToArray();

    private static string SnakeCase(string value) => string.Concat(
        value.Select((character, index) =>
            char.IsUpper(character) && index > 0
                ? "_" + char.ToLowerInvariant(character)
                : char.ToLowerInvariant(character).ToString()
        )
    );

    private static void ValidateRequest(InspectOoxmlSignaturesRequest request)
    {
        if (request.View is not ("summary" or "signatures" or "references" or "issues"))
        {
            throw InvalidInput("view must be summary, signatures, references, or issues");
        }
        if (request.SignatureId is not null
            && (
                request.SignatureId.Length is < 8 or > 96
                || !request.SignatureId.StartsWith("wdsig_", StringComparison.Ordinal)
                || request.SignatureId.Any(character => !char.IsAsciiLetterOrDigit(character)
                    && character != '_')
            ))
        {
            throw InvalidInput("signature_id is invalid");
        }
        if (request.Offset < 0
            || request.Limit is < 1 or > InspectOoxmlSignaturesContract.MaximumLimit)
        {
            throw InvalidInput("offset or limit is outside the bounded paging contract");
        }
        if (request.View == "summary"
            && (
                request.Offset != 0
                || request.Limit != InspectOoxmlSignaturesContract.DefaultLimit
                || request.SignatureId is not null
                || request.IncludeSource
                || request.IncludeCertificateHash
            ))
        {
            throw InvalidInput("summary view does not accept paging, filters, or disclosure options");
        }
    }

    private static string ResolvePath(string localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath)
            || localPath.Length > InspectOoxmlSignaturesContract.MaximumLocalPathCharacters)
        {
            throw InvalidInput("local_path must be a non-empty bounded path");
        }
        string path;
        try
        {
            path = Path.GetFullPath(localPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException
        )
        {
            throw InvalidInput("local_path is not a valid filesystem path", exception);
        }
        if (!File.Exists(path))
        {
            throw new WordToolkitOperationException(
                "NOT_FOUND",
                "The requested Word package does not exist"
            );
        }
        ValidateFileName(path, requireLeafName: false);
        return path;
    }

    private static void ValidateFileName(string fileName, bool requireLeafName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw InvalidInput("A supported Word package file name is required");
        }
        var leaf = Path.GetFileName(fileName);
        if (leaf.Length > InspectOoxmlSignaturesContract.MaximumFileNameCharacters
            || !SupportedExtensions.Contains(Path.GetExtension(leaf)))
        {
            throw InvalidInput("A DOCX, DOCM, DOTX, or DOTM file is required");
        }
        if (requireLeafName
            && (
                leaf != fileName
                || fileName.Contains('/')
                || fileName.Contains('\\')
                || fileName.Contains(':')
            ))
        {
            throw InvalidInput("file_name must be a bounded leaf name");
        }
    }

    private static WordToolkitOperationException NotFound(Exception exception) => new(
        "NOT_FOUND",
        "The requested Word package does not exist",
        innerException: exception
    );

    private static WordToolkitOperationException InvalidInput(
        string message,
        Exception? innerException = null
    ) => new("INVALID_INPUT", message, innerException: innerException);
}
