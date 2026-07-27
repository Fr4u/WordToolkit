using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordPackageSignatureInspectorTests
{
    private const string DocumentContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
    private const string SignatureContentType =
        "application/vnd.openxmlformats-package.digital-signature-xmlsignature+xml";
    private const string OriginContentType =
        "application/vnd.openxmlformats-package.digital-signature-origin";
    private static readonly byte[] DocumentBytes = Encoding.UTF8.GetBytes(
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Signed content</w:t></w:r></w:p><w:sectPr/></w:body></w:document>
        """
    );

    [Fact]
    public void ValidSignatureVerifiesIntegrityWithoutClaimingCertificateTrust()
    {
        using var packageStream = CreatePackage();
        var package = new OpcPackageReader().Read(packageStream);

        var result = new WordPackageSignatureInspector().Inspect(package);

        Assert.True(result.SignatureOriginDeclared);
        Assert.Equal(1, result.SignatureOriginCount);
        Assert.Equal(1, result.SignatureCount);
        Assert.True(
            result.ValidSignatureCount == 1,
            string.Join(",", result.Signatures.SelectMany(item => item.IssueCodes))
        );
        Assert.True(result.AllDiscoveredSignaturesValid);
        Assert.True(result.CryptographicIntegrityValidationPerformed);
        Assert.False(result.CertificateChainTrustVerified);
        Assert.False(result.RevocationChecked);
        var signature = Assert.Single(result.Signatures);
        Assert.True(
            signature.Status == WordPackageSignatureStatus.Valid,
            string.Join(",", signature.IssueCodes)
        );
        Assert.True(signature.SignatureValueVerified, string.Join(",", signature.IssueCodes));
        Assert.True(signature.ManifestReferencesVerified);
        Assert.False(signature.WeakAlgorithm);
        Assert.Null(signature.SignaturePartUri);
        Assert.True(signature.Certificate.Present);
        Assert.NotNull(signature.Certificate.Sha256);
        Assert.False(signature.Certificate.ChainTrustVerified);
        Assert.False(signature.Certificate.RevocationChecked);
        var reference = Assert.Single(signature.References);
        Assert.Equal(WordPackageSignatureReferenceKind.Part, reference.Kind);
        Assert.True(reference.DigestVerified);
        Assert.Equal("/word/document.xml", reference.PartUri);
    }

    [Fact]
    public void ValidUtf16SignaturePartIsAcceptedByTheXmlBoundary()
    {
        using var packageStream = CreatePackage(encodeSignatureAsUtf16: true);
        var package = new OpcPackageReader().Read(packageStream);

        var result = new WordPackageSignatureInspector().Inspect(package);

        var signature = Assert.Single(result.Signatures);
        Assert.Equal(WordPackageSignatureStatus.Valid, signature.Status);
        Assert.True(signature.SignatureValueVerified);
        Assert.True(signature.ManifestReferencesVerified);
    }

    [Fact]
    public void SignaturePartWithTheWrongOpcContentTypeIsInvalidTopology()
    {
        using var packageStream = CreatePackage(
            signatureContentType: "application/xml"
        );
        var package = new OpcPackageReader().Read(packageStream);

        var result = new WordPackageSignatureInspector().Inspect(package);

        var signature = Assert.Single(result.Signatures);
        Assert.Equal(WordPackageSignatureStatus.Invalid, signature.Status);
        Assert.False(signature.TopologyValid);
        Assert.Contains("signature_part_content_type_invalid", signature.IssueCodes);
        Assert.False(result.AllDiscoveredSignaturesValid);
    }

    [Fact]
    public void TamperedSignedPartInvalidatesManifestButNotTheSignedInfoValue()
    {
        using var packageStream = CreatePackage(tamperDocumentAfterSigning: true);
        var package = new OpcPackageReader().Read(packageStream);

        var result = new WordPackageSignatureInspector().Inspect(package);

        var signature = Assert.Single(result.Signatures);
        Assert.Equal(WordPackageSignatureStatus.Invalid, signature.Status);
        Assert.True(signature.SignatureValueVerified);
        Assert.False(signature.ManifestReferencesVerified);
        Assert.Contains("signature_reference_digest_mismatch", signature.IssueCodes);
        Assert.Equal(1, result.InvalidSignatureCount);
        Assert.False(result.AllDiscoveredSignaturesValid);
    }

    [Fact]
    public void MissingSignerCertificateIsIndeterminateInsteadOfTrustedOrInvalid()
    {
        using var packageStream = CreatePackage(removeEmbeddedCertificate: true);
        var package = new OpcPackageReader().Read(packageStream);

        var result = new WordPackageSignatureInspector().Inspect(package);

        var signature = Assert.Single(result.Signatures);
        Assert.Equal(WordPackageSignatureStatus.Indeterminate, signature.Status);
        Assert.False(signature.SignatureValueVerified);
        Assert.True(signature.ManifestReferencesVerified);
        Assert.False(signature.Certificate.Present);
        Assert.Contains("signature_certificate_missing", signature.IssueCodes);
    }

    [Fact]
    public void UnsupportedManifestTransformFailsClosedWithoutReadingAnExternalResource()
    {
        using var packageStream = CreatePackage(
            manifestTransform: "https://wordtoolkit.invalid/unsupported-transform"
        );
        var package = new OpcPackageReader().Read(packageStream);

        var result = new WordPackageSignatureInspector().Inspect(package);

        var signature = Assert.Single(result.Signatures);
        Assert.True(
            signature.Status == WordPackageSignatureStatus.Unsupported,
            string.Join(",", signature.IssueCodes)
        );
        Assert.True(signature.SignatureValueVerified);
        Assert.False(signature.ManifestReferencesVerified);
        Assert.Contains("signature_transform_unsupported", signature.IssueCodes);
        Assert.Equal(1, result.UnsupportedSignatureCount);
    }

    [Fact]
    public void ExternalSignedInfoReferenceFailsClosedWithoutResolvingIt()
    {
        using var packageStream = CreatePackage(useExternalSignedInfoReference: true);
        var package = new OpcPackageReader().Read(packageStream);

        var result = new WordPackageSignatureInspector().Inspect(package);

        var signature = Assert.Single(result.Signatures);
        Assert.Equal(WordPackageSignatureStatus.Invalid, signature.Status);
        Assert.False(signature.SignatureValueVerified);
        Assert.Contains("signature_signed_info_reference_unsupported", signature.IssueCodes);
        Assert.Contains("signature_manifest_unsigned", signature.IssueCodes);
        Assert.False(result.CertificateChainTrustVerified);
        Assert.False(result.RevocationChecked);
    }

    [Fact]
    public void DuplicateXmlIdIsRejectedAsAnAmbiguousWrappingShape()
    {
        using var packageStream = CreatePackage(duplicateObjectId: true);
        var package = new OpcPackageReader().Read(packageStream);

        var result = new WordPackageSignatureInspector().Inspect(package);

        var signature = Assert.Single(result.Signatures);
        Assert.Equal(WordPackageSignatureStatus.Invalid, signature.Status);
        Assert.Contains("signature_duplicate_xml_id", signature.IssueCodes);
        Assert.Contains("signature_signed_info_reference_ambiguous", signature.IssueCodes);
    }

    [Fact]
    public void UnsignedManifestObjectIsNotMixedIntoTheVerifiedSignedObject()
    {
        using var packageStream = CreatePackage(addUnsignedManifestObject: true);
        var package = new OpcPackageReader().Read(packageStream);

        var result = new WordPackageSignatureInspector().Inspect(package);

        var signature = Assert.Single(result.Signatures);
        Assert.Equal(WordPackageSignatureStatus.Invalid, signature.Status);
        Assert.True(signature.SignatureValueVerified);
        Assert.Equal(1, signature.ManifestReferenceCount);
        Assert.Contains("signature_manifest_unsigned", signature.IssueCodes);
    }

    [Fact]
    public void SignatureCountAndSignatureBytesAreBounded()
    {
        using var packageStream = CreatePackage();
        var package = new OpcPackageReader().Read(packageStream);

        var exception = Assert.Throws<WordPackageSignatureInspectionLimitException>(() =>
            new WordPackageSignatureInspector(new WordPackageSignatureInspectionLimits
            {
                MaximumSignatureBytes = 1024,
            }).Inspect(package)
        );

        Assert.DoesNotContain("word/document.xml", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedManifestReferenceIsContainedAndReportedWithoutLeakingXml()
    {
        using var packageStream = CreatePackage(duplicateDigestMethod: true);
        var package = new OpcPackageReader().Read(packageStream);

        var result = new WordPackageSignatureInspector().Inspect(package);

        var signature = Assert.Single(result.Signatures);
        Assert.Equal(WordPackageSignatureStatus.Invalid, signature.Status);
        var reference = Assert.Single(signature.References);
        Assert.False(reference.DigestVerified);
        Assert.Equal("signature_reference_structure_invalid", reference.FailureCode);
        Assert.Contains("signature_reference_structure_invalid", signature.IssueCodes);
        Assert.DoesNotContain("DigestMethod", string.Join(',', signature.IssueCodes));
    }

    [Fact]
    public void RelationshipTransformVerifiesOnlyTheDeclaredRelationshipSubset()
    {
        using var packageStream = CreatePackage(includeRelationshipReference: true);
        var package = new OpcPackageReader().Read(packageStream);

        var result = new WordPackageSignatureInspector().Inspect(package);

        var signature = Assert.Single(result.Signatures);
        Assert.True(
            signature.Status == WordPackageSignatureStatus.Valid,
            string.Join(",", signature.IssueCodes)
        );
        Assert.Equal(2, signature.ManifestReferenceCount);
        Assert.Equal(1, signature.SignedPartCount);
        Assert.Equal(1, signature.SignedRelationshipPartCount);
        Assert.Equal(1, signature.SelectedRelationshipCount);
        var relationshipReference = Assert.Single(
            signature.References,
            item => item.Kind == WordPackageSignatureReferenceKind.Relationships
        );
        Assert.True(relationshipReference.DigestVerified);
        Assert.Equal(1, relationshipReference.SelectedRelationshipCount);
    }

    [Fact]
    public void PublicProjectionIsPagedAndKeepsCertificateAndSourceDisclosureOptIn()
    {
        using var packageStream = CreatePackage(includeRelationshipReference: true);
        var package = new OpcPackageReader().Read(packageStream);
        var inspection = new WordPackageSignatureInspector().Inspect(
            package,
            includeSource: true
        );
        var operation = new InspectOoxmlSignaturesOperation();

        var summary = operation.Project(
            inspection,
            "signed.docx",
            new InspectOoxmlSignaturesRequest("ignored.docx")
        );
        Assert.Empty(summary.Signatures);
        Assert.Empty(summary.References);
        Assert.Empty(summary.Issues);
        Assert.False(summary.Security.ReturnsDocumentContent);
        Assert.False(summary.Security.ReturnsRawXml);
        Assert.False(summary.Security.ReturnsCertificateBytes);
        Assert.False(summary.Security.ReturnsCertificateIdentity);
        Assert.False(summary.Security.UsesNetwork);
        Assert.False(summary.CertificateChainTrustVerified);
        Assert.False(summary.RevocationChecked);

        var signaturePage = operation.Project(
            inspection,
            "signed.docx",
            new InspectOoxmlSignaturesRequest(
                "ignored.docx",
                View: "signatures",
                IncludeSource: false,
                IncludeCertificateHash: false
            )
        );
        var signature = Assert.Single(signaturePage.Signatures);
        Assert.Null(signature.CertificateSha256);
        Assert.Null(signature.PublicKeyAlgorithm);
        Assert.Null(signature.SignaturePartUri);

        var disclosedSignaturePage = operation.Project(
            inspection,
            "signed.docx",
            new InspectOoxmlSignaturesRequest(
                "ignored.docx",
                View: "signatures",
                IncludeSource: true,
                IncludeCertificateHash: true
            )
        );
        var disclosed = Assert.Single(disclosedSignaturePage.Signatures);
        Assert.NotNull(disclosed.CertificateSha256);
        Assert.NotNull(disclosed.PublicKeyAlgorithm);
        Assert.Equal("/_xmlsignatures/sig1.xml", disclosed.SignaturePartUri);

        var referencePage = operation.Project(
            inspection,
            "signed.docx",
            new InspectOoxmlSignaturesRequest(
                "ignored.docx",
                View: "references",
                Limit: 1
            )
        );
        Assert.Single(referencePage.References);
        Assert.Null(referencePage.References[0].PartUri);
        Assert.Equal(2, referencePage.Paging.Total);
        Assert.Equal(1, referencePage.Paging.NextOffset);
    }

    private static MemoryStream CreatePackage(
        bool tamperDocumentAfterSigning = false,
        bool removeEmbeddedCertificate = false,
        string manifestTransform = SignedXml.XmlDsigC14NTransformUrl,
        bool includeRelationshipReference = false,
        bool duplicateDigestMethod = false,
        bool useExternalSignedInfoReference = false,
        bool duplicateObjectId = false,
        bool addUnsignedManifestObject = false,
        bool encodeSignatureAsUtf16 = false,
        string signatureContentType = SignatureContentType
    )
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=WordToolkit signature fixture",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1)
        );
        var signature = CreateSignature(
            certificate,
            manifestTransform,
            includeRelationshipReference
        );
        if (duplicateDigestMethod)
        {
            var document = ParseXml(signature);
            var manager = new XmlNamespaceManager(document.NameTable);
            manager.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
            var digestMethod = document.SelectSingleNode(
                "/ds:Signature/ds:Object/ds:Manifest/ds:Reference/ds:DigestMethod",
                manager
            );
            Assert.NotNull(digestMethod?.ParentNode);
            digestMethod.ParentNode.InsertAfter(digestMethod.CloneNode(deep: true), digestMethod);
            signature = Serialize(document);
        }
        if (useExternalSignedInfoReference)
        {
            var document = ParseXml(signature);
            var manager = new XmlNamespaceManager(document.NameTable);
            manager.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
            var reference = document.SelectSingleNode(
                "/ds:Signature/ds:SignedInfo/ds:Reference",
                manager
            ) as XmlElement;
            Assert.NotNull(reference);
            reference.SetAttribute("URI", "https://wordtoolkit.invalid/external");
            signature = Serialize(document);
        }
        if (duplicateObjectId)
        {
            var document = ParseXml(signature);
            var manager = new XmlNamespaceManager(document.NameTable);
            manager.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
            var packageObject = document.SelectSingleNode(
                "/ds:Signature/ds:Object[@Id='idPackageObject']",
                manager
            );
            Assert.NotNull(packageObject?.ParentNode);
            packageObject.ParentNode.AppendChild(packageObject.CloneNode(deep: true));
            signature = Serialize(document);
        }
        if (addUnsignedManifestObject)
        {
            var document = ParseXml(signature);
            var manager = new XmlNamespaceManager(document.NameTable);
            manager.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
            var signedManifest = document.SelectSingleNode(
                "/ds:Signature/ds:Object[@Id='idPackageObject']/ds:Manifest",
                manager
            );
            Assert.NotNull(signedManifest);
            var unsignedObject = document.CreateElement(
                "Object",
                SignedXml.XmlDsigNamespaceUrl
            );
            unsignedObject.SetAttribute("Id", "unsignedPackageObject");
            unsignedObject.AppendChild(document.ImportNode(signedManifest, deep: true));
            document.DocumentElement!.AppendChild(unsignedObject);
            signature = Serialize(document);
        }
        if (removeEmbeddedCertificate)
        {
            var document = ParseXml(signature);
            var manager = new XmlNamespaceManager(document.NameTable);
            manager.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
            var keyInfo = document.SelectSingleNode("/ds:Signature/ds:KeyInfo", manager);
            Assert.NotNull(keyInfo?.ParentNode);
            keyInfo.ParentNode.RemoveChild(keyInfo);
            signature = Serialize(document);
        }
        if (encodeSignatureAsUtf16)
        {
            signature = SerializeUtf16(ParseXml(signature));
        }
        var documentBytes = tamperDocumentAfterSigning
            ? Encoding.UTF8.GetBytes(
                Encoding.UTF8.GetString(DocumentBytes).Replace(
                    "Signed content",
                    "Tampered content",
                    StringComparison.Ordinal
                )
            )
            : DocumentBytes;

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "[Content_Types].xml", Encoding.UTF8.GetBytes(
                $$"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="{{DocumentContentType}}"/>
                  <Override PartName="/_xmlsignatures/origin.sigs" ContentType="{{OriginContentType}}"/>
                  <Override PartName="/_xmlsignatures/sig1.xml" ContentType="{{signatureContentType}}"/>
                </Types>
                """
            ));
            AddEntry(archive, "_rels/.rels", Encoding.UTF8.GetBytes(
                $$"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdDocument" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                  <Relationship Id="rIdSignatureOrigin" Type="{{WordPackageSignatureInspector.SignatureOriginRelationshipType}}" Target="_xmlsignatures/origin.sigs"/>
                </Relationships>
                """
            ));
            AddEntry(archive, "word/document.xml", documentBytes);
            AddEntry(archive, "_xmlsignatures/origin.sigs", []);
            AddEntry(archive, "_xmlsignatures/_rels/origin.sigs.rels", Encoding.UTF8.GetBytes(
                $$"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdSignature" Type="{{WordPackageSignatureInspector.SignatureRelationshipType}}" Target="sig1.xml"/>
                </Relationships>
                """
            ));
            AddEntry(archive, "_xmlsignatures/sig1.xml", signature);
        }
        stream.Position = 0;
        return stream;
    }

    private static byte[] CreateSignature(
        X509Certificate2 certificate,
        string manifestTransform,
        bool includeRelationshipReference
    )
    {
        var transformed = manifestTransform == SignedXml.XmlDsigC14NTransformUrl
            ? Canonicalize(DocumentBytes)
            : DocumentBytes;
        var digest = Convert.ToBase64String(SHA256.HashData(transformed));
        var relationshipReference = string.Empty;
        if (includeRelationshipReference)
        {
            var transformedRelationships = Encoding.UTF8.GetBytes(
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdDocument" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml" /></Relationships>
                """
            );
            var relationshipDigest = Convert.ToBase64String(
                SHA256.HashData(Canonicalize(transformedRelationships))
            );
            relationshipReference =
                $$"""
                <Reference xmlns="{{SignedXml.XmlDsigNamespaceUrl}}" xmlns:mdssi="{{WordPackageSignatureInspector.OpcSignatureNamespace}}" URI="/_rels/.rels?ContentType={{Uri.EscapeDataString("application/vnd.openxmlformats-package.relationships+xml")}}"><Transforms><Transform Algorithm="{{WordPackageSignatureInspector.RelationshipTransformAlgorithm}}"><mdssi:RelationshipReference SourceId="rIdDocument"/></Transform><Transform Algorithm="{{SignedXml.XmlDsigC14NTransformUrl}}"/></Transforms><DigestMethod Algorithm="{{SignedXml.XmlDsigSHA256Url}}"/><DigestValue>{{relationshipDigest}}</DigestValue></Reference>
                """;
        }
        var objectDocument = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        objectDocument.LoadXml(
            $$"""
            <Object xmlns="{{SignedXml.XmlDsigNamespaceUrl}}" xmlns:mdssi="{{WordPackageSignatureInspector.OpcSignatureNamespace}}" Id="idPackageObject"><Manifest><Reference URI="/word/document.xml?ContentType={{Uri.EscapeDataString(DocumentContentType)}}"><Transforms><Transform Algorithm="{{manifestTransform}}"/></Transforms><DigestMethod Algorithm="{{SignedXml.XmlDsigSHA256Url}}"/><DigestValue>{{digest}}</DigestValue></Reference>{{relationshipReference}}</Manifest><SignatureProperties><SignatureProperty Id="idSignatureTime" Target="#idPackageSignature"><mdssi:SignatureTime><mdssi:Format>YYYY-MM-DDThh:mm:ssTZD</mdssi:Format><mdssi:Value>2026-07-27T00:00:00Z</mdssi:Value></mdssi:SignatureTime></SignatureProperty></SignatureProperties></Object>
            """
        );
        var objectDigest = Convert.ToBase64String(
            SHA256.HashData(Canonicalize(Serialize(objectDocument)))
        );
        var signedInfoDocument = new XmlDocument
        {
            PreserveWhitespace = true,
            XmlResolver = null,
        };
        signedInfoDocument.LoadXml(
            $$"""
            <SignedInfo xmlns="{{SignedXml.XmlDsigNamespaceUrl}}"><CanonicalizationMethod Algorithm="{{SignedXml.XmlDsigC14NTransformUrl}}"/><SignatureMethod Algorithm="{{SignedXml.XmlDsigRSASHA256Url}}"/><Reference URI="#idPackageObject"><Transforms><Transform Algorithm="{{SignedXml.XmlDsigC14NTransformUrl}}"/></Transforms><DigestMethod Algorithm="{{SignedXml.XmlDsigSHA256Url}}"/><DigestValue>{{objectDigest}}</DigestValue></Reference></SignedInfo>
            """
        );
        byte[] signatureValue;
        using (var privateKey = certificate.GetRSAPrivateKey())
        {
            Assert.NotNull(privateKey);
            signatureValue = privateKey.SignData(
                Canonicalize(Serialize(signedInfoDocument)),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
        }
        var signatureDocument = new XmlDocument
        {
            PreserveWhitespace = true,
            XmlResolver = null,
        };
        signatureDocument.LoadXml(
            $$"""
            <Signature xmlns="{{SignedXml.XmlDsigNamespaceUrl}}" Id="idPackageSignature">{{signedInfoDocument.DocumentElement!.OuterXml}}<SignatureValue>{{Convert.ToBase64String(signatureValue)}}</SignatureValue><KeyInfo><X509Data><X509Certificate>{{Convert.ToBase64String(certificate.RawData)}}</X509Certificate></X509Data></KeyInfo>{{objectDocument.DocumentElement!.OuterXml}}</Signature>
            """
        );
        var verifier = new SignedXml(signatureDocument) { Resolver = null! };
        verifier.LoadXml(signatureDocument.DocumentElement!);
        Assert.True(verifier.CheckSignature(certificate, verifySignatureOnly: true));
        return Serialize(signatureDocument);
    }

    private static byte[] Canonicalize(byte[] bytes)
    {
        var document = ParseXml(bytes);
        var transform = new XmlDsigC14NTransform();
        transform.LoadInput(document);
        using var output = (Stream)transform.GetOutput(typeof(Stream));
        using var buffer = new MemoryStream();
        output.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static XmlDocument ParseXml(byte[] bytes)
    {
        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        });
        document.Load(reader);
        return document;
    }

    private static byte[] Serialize(XmlDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = true,
            NewLineHandling = NewLineHandling.None,
            CloseOutput = false,
        }))
        {
            document.Save(writer);
        }
        return stream.ToArray();
    }

    private static byte[] SerializeUtf16(XmlDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UnicodeEncoding(
                bigEndian: false,
                byteOrderMark: true,
                throwOnInvalidBytes: true
            ),
            OmitXmlDeclaration = false,
            NewLineHandling = NewLineHandling.None,
            CloseOutput = false,
        }))
        {
            document.Save(writer);
        }
        return stream.ToArray();
    }

    private static void AddEntry(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero);
        using var stream = entry.Open();
        stream.Write(bytes);
    }
}
