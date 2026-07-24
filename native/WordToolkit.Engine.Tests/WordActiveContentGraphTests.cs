using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordActiveContentGraphTests
{
    [Fact]
    public void ProjectsOleActiveXVbaCustomUiAndSignaturesWithoutOpeningPayloads()
    {
        using var bytes = BuildFullPackage();
        var package = new OpcPackageReader().Read(bytes);
        var fingerprint = package.Fingerprint;
        var hashes = package.Entries.ToDictionary(entry => entry.Name, entry => entry.Sha256);

        var graph = new WordActiveContentGraphBuilder().Build(package);

        Assert.Equal(fingerprint, graph.PackageFingerprint);
        Assert.True(graph.MainDocumentMacroEnabled);
        Assert.False(graph.BinaryPayloadsDecoded);
        Assert.False(graph.EmbeddedPackagesOpened);
        Assert.False(graph.CryptographicSignatureValidationPerformed);
        Assert.Contains(graph.Declarations, item =>
            item.Kind == WordActiveContentDeclarationKind.OleObject
                && item.ProgramId == "Excel.Sheet.12"
                && item.IsResolved
        );
        var embedded = Assert.Single(graph.Declarations, item =>
            item.Kind == WordActiveContentDeclarationKind.EmbeddedObject
        );
        Assert.Equal("Embed", embedded.ObjectType);
        Assert.True(embedded.HasFieldCodes);
        Assert.Equal(12, embedded.FieldCodeCharacters);
        Assert.True(embedded.IsResolved);
        var linked = Assert.Single(graph.Declarations, item =>
            item.Kind == WordActiveContentDeclarationKind.LinkedObject
        );
        Assert.Equal("Link", linked.ObjectType);
        Assert.Equal("OnCall", linked.UpdateMode);
        Assert.Equal("Picture", linked.ServerFormat);
        Assert.True(linked.IsResolved);

        var activeX = Assert.Single(graph.Controls);
        Assert.Equal("{11111111-2222-3333-4444-555555555555}", activeX.ClassId);
        Assert.Equal("persistStreamInit", activeX.Persistence);
        Assert.Equal(2, activeX.PropertyCount);
        Assert.True(activeX.HasLicense);
        Assert.Equal(14, activeX.LicenseCharacters);
        Assert.True(activeX.IsResolved);
        Assert.Single(activeX.DeclarationIds);

        AssertPayload(graph, WordActiveContentPayloadKind.OleObject, "ole_compound", true);
        var workbook = AssertPayload(
            graph,
            WordActiveContentPayloadKind.EmbeddedPackage,
            "excel",
            false
        );
        Assert.False(workbook.IsXml);
        AssertPayload(graph, WordActiveContentPayloadKind.ActiveXXml, "activex", true);
        AssertPayload(graph, WordActiveContentPayloadKind.ActiveXBinary, "activex", true);
        AssertPayload(graph, WordActiveContentPayloadKind.VbaProject, "vba", true);
        AssertPayload(graph, WordActiveContentPayloadKind.VbaData, "vba", true);
        AssertPayload(graph, WordActiveContentPayloadKind.AttachedToolbar, "attached_toolbar", true);
        AssertPayload(graph, WordActiveContentPayloadKind.CustomUi, "office_custom_ui", true);
        AssertPayload(
            graph,
            WordActiveContentPayloadKind.QuickAccessToolbarCustomization,
            "office_custom_ui",
            true
        );
        AssertPayload(
            graph,
            WordActiveContentPayloadKind.KeyMapCustomization,
            "office_custom_ui",
            true
        );
        AssertPayload(
            graph,
            WordActiveContentPayloadKind.VbaProjectSignature,
            "vba_signature",
            true
        );
        AssertPayload(
            graph,
            WordActiveContentPayloadKind.DigitalSignatureOrigin,
            "digital_signature",
            false
        );
        AssertPayload(
            graph,
            WordActiveContentPayloadKind.DigitalSignature,
            "digital_signature",
            false
        );
        Assert.Contains(graph.Relationships, item =>
                item.Role == WordActiveContentRelationshipRole.OleObject
                && item.TargetMode == OpcRelationshipTargetMode.External
                && !item.IsResolved
                && item.PayloadId is null
        );
        Assert.DoesNotContain(
            graph.Issues,
            issue => issue.Severity == WordActiveContentIssueSeverity.Error
        );
        Assert.Equal(hashes, package.Entries.ToDictionary(entry => entry.Name, entry => entry.Sha256));
        Assert.Equal(fingerprint, package.Fingerprint);
    }

    [Fact]
    public void FindsOrphanDeclarationsWithoutTrustingADeclaredRelationshipInventory()
    {
        using var bytes = BuildPackage(
            documentXml:
                """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body><w:p><w:r><w:object><w:objectEmbed r:id="rMissing" progId="Word.Document.12"/></w:object></w:r></w:p></w:body>
                </w:document>
                """,
            documentRelationships: ""
        );
        var graph = new WordActiveContentGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        var declaration = Assert.Single(graph.Declarations);
        Assert.Equal(WordActiveContentDeclarationKind.EmbeddedObject, declaration.Kind);
        Assert.False(declaration.IsResolved);
        Assert.Contains(graph.Issues, issue =>
            issue.Code == "ACTIVE_DECLARATION_RELATIONSHIP_UNRESOLVED"
                && issue.SubjectId == declaration.Id
        );
    }

    [Fact]
    public void RejectsRelationshipIdAttributesFromLookalikeNamespaces()
    {
        using var bytes = BuildPackage(
            documentXml:
                """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:evil="https://attacker.invalid/relationships">
                  <w:body><w:p><w:r><w:object><w:objectEmbed evil:id="rOle" progId="Word.Document.12"/></w:object></w:r></w:p></w:body>
                </w:document>
                """,
            documentRelationships:
                """
                <Relationship Id="rOle" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject" Target="embeddings/oleObject1.bin"/>
                """,
            extraContentTypes:
                """
                <Override PartName="/word/embeddings/oleObject1.bin" ContentType="application/vnd.openxmlformats-officedocument.oleObject"/>
                """,
            entries: [("word/embeddings/oleObject1.bin", "ole")]
        );
        var graph = new WordActiveContentGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        var declaration = Assert.Single(graph.Declarations);
        Assert.Null(declaration.RelationshipId);
        Assert.Null(declaration.RelationshipNodeId);
        Assert.False(declaration.IsResolved);
        Assert.Contains(graph.Issues, issue =>
            issue.Code == "ACTIVE_DECLARATION_RELATIONSHIP_MISSING"
                && issue.SubjectId == declaration.Id
        );
    }

    [Fact]
    public void ResolvesStrictDeclarationsRelationshipsAndNamespacedMetadata()
    {
        using var bytes = BuildPackage(
            documentXml:
                """
                <w:document xmlns:w="http://purl.oclc.org/ooxml/wordprocessingml/main" xmlns:r="http://purl.oclc.org/ooxml/officeDocument/relationships">
                  <w:body><w:p><w:r><w:object><w:objectEmbed r:id="rOle" w:progId="Word.Document.12"/></w:object></w:r></w:p></w:body>
                </w:document>
                """,
            documentRelationships:
                """
                <Relationship Id="rOle" Type="http://purl.oclc.org/ooxml/officeDocument/relationships/oleObject" Target="embeddings/oleObject1.bin"/>
                """,
            extraContentTypes:
                """
                <Override PartName="/word/embeddings/oleObject1.bin" ContentType="application/vnd.openxmlformats-officedocument.oleObject"/>
                """,
            entries: [("word/embeddings/oleObject1.bin", "ole")]
        );
        var graph = new WordActiveContentGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        var declaration = Assert.Single(graph.Declarations);
        Assert.StartsWith("wdad_", declaration.Id, StringComparison.Ordinal);
        Assert.Equal("Word.Document.12", declaration.ProgramId);
        Assert.True(declaration.IsResolved);
        Assert.Contains(graph.Relationships, item =>
            item.Role == WordActiveContentRelationshipRole.OleObject
                && item.PayloadId is not null
                && item.IsResolved
        );
    }

    [Fact]
    public void IgnoresLookalikeRelationshipNamespacesAndReportsUnsafeActiveXXml()
    {
        using var bytes = BuildPackage(
            documentXml:
                """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p/></w:body></w:document>
                """,
            documentRelationships:
                """
                <Relationship Id="rFake" Type="https://attacker.invalid/oleObject" Target="media/fake.bin"/>
                <Relationship Id="rFakeOfficialBase" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/spoof/oleObject" Target="media/fake2.bin"/>
                <Relationship Id="rControl" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/control" Target="activeX/activeX1.xml"/>
                """,
            extraContentTypes:
                """
                <Override PartName="/word/activeX/activeX1.xml" ContentType="application/vnd.ms-office.activeX+xml"/>
                <Override PartName="/word/media/fake.bin" ContentType="application/octet-stream"/>
                <Override PartName="/word/media/fake2.bin" ContentType="application/octet-stream"/>
                """,
            entries:
            [
                ("word/activeX/activeX1.xml", "<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///secret'>]><x>&e;</x>"),
                ("word/media/fake.bin", "not active content"),
                ("word/media/fake2.bin", "still not active content"),
            ]
        );
        var graph = new WordActiveContentGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        Assert.DoesNotContain(graph.Relationships, item => item.RelationshipId == "rFake");
        Assert.DoesNotContain(graph.Relationships, item =>
            item.RelationshipId == "rFakeOfficialBase"
        );
        Assert.DoesNotContain(graph.Payloads, item => item.PartUri.EndsWith("fake.bin"));
        Assert.DoesNotContain(graph.Payloads, item => item.PartUri.EndsWith("fake2.bin"));
        Assert.Empty(graph.Controls);
        Assert.Contains(graph.Issues, issue =>
            issue.Code == "ACTIVE_XML_UNREADABLE"
                && issue.PartUri == "/word/activeX/activeX1.xml"
        );
    }

    [Fact]
    public void ReportsMacroSignatureAndOleTopologyContradictions()
    {
        using var bytes = BuildPackage(
            documentXml:
                """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body><w:p><w:r><w:object><w:objectEmbed r:id="rExternal"/><w:objectLink r:id="rInternal"/></w:object></w:r></w:p></w:body>
                </w:document>
                """,
            documentRelationships:
                """
                <Relationship Id="rExternal" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject" Target="https://example.invalid/object" TargetMode="External"/>
                <Relationship Id="rInternal" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject" Target="embeddings/oleObject1.bin"/>
                <Relationship Id="rVba" Type="http://schemas.microsoft.com/office/2006/relationships/vbaProject" Target="vbaProject.bin"/>
                <Relationship Id="rBadPackageSig" Type="http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/signature" Target="../_xmlsignatures/sig1.xml"/>
                <Relationship Id="rBadVbaSig" Type="http://schemas.microsoft.com/office/2006/relationships/vbaProjectSignature" Target="vbaProjectSignature.bin"/>
                """,
            extraContentTypes:
                """
                <Override PartName="/word/embeddings/oleObject1.bin" ContentType="application/vnd.openxmlformats-officedocument.oleObject"/>
                <Override PartName="/word/vbaProject.bin" ContentType="application/vnd.ms-office.vbaProject"/>
                <Override PartName="/word/vbaProjectSignature.bin" ContentType="application/vnd.ms-office.vbaProjectSignature"/>
                <Override PartName="/_xmlsignatures/origin.sigs" ContentType="application/vnd.openxmlformats-package.digital-signature-origin"/>
                <Override PartName="/_xmlsignatures/sig1.xml" ContentType="application/vnd.openxmlformats-package.digital-signature-xmlsignature+xml"/>
                """,
            entries:
            [
                ("word/embeddings/oleObject1.bin", "ole"),
                ("word/vbaProject.bin", "vba"),
                ("word/vbaProjectSignature.bin", "vba signature"),
                ("_xmlsignatures/origin.sigs", ""),
                ("_xmlsignatures/sig1.xml", "<Signature xmlns=\"http://www.w3.org/2000/09/xmldsig#\"/>"),
            ],
            rootRelationships:
                """
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                <Relationship Id="rSignatureOrigin" Type="http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/origin" Target="_xmlsignatures/origin.sigs"/>
                """
        );
        var graph = new WordActiveContentGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        Assert.False(graph.MainDocumentMacroEnabled);
        Assert.Contains(graph.Issues, issue =>
            issue.Code == "VBA_PROJECT_WITH_NON_MACRO_MAIN_PART"
        );
        Assert.Contains(graph.Issues, issue => issue.Code == "OLE_EMBED_TARGET_EXTERNAL");
        Assert.Contains(graph.Issues, issue => issue.Code == "OLE_LINK_TARGET_NOT_EXTERNAL");
        Assert.Contains(graph.Issues, issue => issue.Code == "SIGNATURE_ORIGIN_EMPTY");
        Assert.Contains(graph.Issues, issue => issue.Code == "SIGNATURE_SOURCE_INVALID");
        Assert.Contains(graph.Issues, issue =>
            issue.Code == "VBA_PROJECT_SIGNATURE_SOURCE_INVALID"
        );
    }

    [Fact]
    public void DoesNotTreatLookalikeMainContentTypesAsMacroEnabled()
    {
        using var bytes = BuildPackage(
            documentXml:
                """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p/></w:body></w:document>
                """,
            documentRelationships: "",
            mainContentType: "application/vnd.attacker.macroEnabled.main+xml"
        );
        var graph = new WordActiveContentGraphBuilder().Build(
            new OpcPackageReader().Read(bytes)
        );

        Assert.False(graph.MainDocumentMacroEnabled);
    }

    [Fact]
    public void EnforcesDeterministicLimitsAndOperationResourceBoundary()
    {
        using var bytes = BuildFullPackage();
        var package = new OpcPackageReader().Read(bytes);
        Assert.Throws<WordActiveContentLimitException>(() =>
            new WordActiveContentGraphBuilder(
                new WordActiveContentGraphOptions { MaxPayloads = 1 }
            ).Build(package)
        );
        Assert.Throws<WordActiveContentLimitException>(() =>
            new WordActiveContentGraphBuilder(
                new WordActiveContentGraphOptions { MaxDeclarations = 1 }
            ).Build(package)
        );
        Assert.Throws<WordActiveContentLimitException>(() =>
            new WordActiveContentGraphBuilder(
                new WordActiveContentGraphOptions { MaxTotalXmlBytes = 1 }
            ).Build(package)
        );
        Assert.Throws<WordActiveContentLimitException>(() =>
            new WordActiveContentGraphBuilder(
                new WordActiveContentGraphOptions { MaxTotalXmlElements = 1 }
            ).Build(package)
        );

        var probeLease = new WordOperationResourceLease();
        _ = new WordActiveContentGraphBuilder(null, probeLease).Build(package);
        var used = probeLease.Snapshot().AccountedBytes;
        Assert.Contains(
            probeLease.Snapshot().Stages,
            stage => stage.Stage == WordOperationResourceStage.ActiveContent
                && stage.AccountedBytes > 0
        );
        Assert.Throws<WordOperationResourceLimitException>(() =>
            new WordActiveContentGraphBuilder(
                null,
                new WordOperationResourceLease(used - 1)
            ).Build(package)
        );
        var exactLease = new WordOperationResourceLease(used);
        var graph = new WordActiveContentGraphBuilder(null, exactLease).Build(package);
        Assert.NotEmpty(graph.Payloads);
        Assert.Equal(used, exactLease.Snapshot().AccountedBytes);
    }

    [Fact]
    public void UnifiedDependencyGraphCarriesActiveContentTopologyAndItsSharedLease()
    {
        using var bytes = BuildFullPackage();
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        var lease = new WordOperationResourceLease();

        var graph = new WordDependencyGraphBuilder(null, lease).Build(package, semantic);

        Assert.True(graph.Coverage.ActiveContent);
        Assert.DoesNotContain(
            "ole_embedded_packages",
            graph.Coverage.ExplicitlyUnmodeledDomains
        );
        Assert.DoesNotContain(
            "macros_signatures_encryption",
            graph.Coverage.ExplicitlyUnmodeledDomains
        );
        Assert.Contains(
            "active_content_binary_internals_and_execution",
            graph.Coverage.ExplicitlyUnmodeledDomains
        );
        Assert.Contains(
            "signature_cryptographic_validation_and_resigning",
            graph.Coverage.ExplicitlyUnmodeledDomains
        );
        Assert.Contains(
            "encrypted_package_adapter",
            graph.Coverage.ExplicitlyUnmodeledDomains
        );
        Assert.Equal(
            13,
            graph.Nodes.Count(node =>
                node.Kind == WordDependencyNodeKind.ActiveContentPayload
            )
        );
        Assert.Equal(
            4,
            graph.Nodes.Count(node =>
                node.Kind == WordDependencyNodeKind.ActiveContentDeclaration
            )
        );
        Assert.Single(graph.Nodes, node => node.Kind == WordDependencyNodeKind.ActiveXControl);
        Assert.Contains(graph.Edges, edge =>
            edge.Kind == WordDependencyEdgeKind.ActiveContentDeclarationUsesPayload
                && edge.IsExternal
                && !edge.IsResolved
        );
        var controlBinary = Assert.Single(graph.Edges, edge =>
            edge.Kind == WordDependencyEdgeKind.ActiveXControlUsesBinaryPayload
        );
        Assert.True(controlBinary.IsResolved);
        Assert.Equal(0, graph.ActiveContentIssueCount);
        var usage = Assert.Single(
            graph.OperationResourceUsage!.Stages,
            stage => stage.Stage == WordOperationResourceStage.ActiveContent
        );
        Assert.True(usage.AccountedBytes > 0);
    }

    private static WordActiveContentPayload AssertPayload(
        WordActiveContentGraph graph,
        WordActiveContentPayloadKind kind,
        string containerFamily,
        bool potentiallyExecutable
    )
    {
        var payload = Assert.Single(graph.Payloads, item => item.Kind == kind);
        Assert.Equal(containerFamily, payload.ContainerFamily);
        Assert.Equal(potentiallyExecutable, payload.IsPotentiallyExecutable);
        Assert.True(graph.TryGetPayload(payload.Id, out var resolved));
        Assert.Equal(payload, resolved);
        return payload;
    }

    private static MemoryStream BuildFullPackage()
    {
        const string document =
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body><w:p><w:r>
                <w:object><o:OLEObject Type="Embed" ProgID="Excel.Sheet.12" ShapeID="_x0000_i1025" DrawAspect="Content" ObjectID="_1" r:id="rOle"/></w:object>
                <w:object><w:objectEmbed r:id="rOle" progId="Excel.Sheet.12" drawAspect="content" shapeId="42" fieldCodes=" EMBED test "/></w:object>
                <w:object><w:objectLink r:id="rLinked" progId="Package" updateMode="OnCall" serverFormat="Picture"/></w:object>
                <w:control r:id="rControl" name="CommandButton1" shapeid="shape1"/>
              </w:r></w:p></w:body>
            </w:document>
            """;
        const string documentRelationships =
            """
            <Relationship Id="rOle" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject" Target="embeddings/oleObject1.bin"/>
            <Relationship Id="rLinked" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject" Target="https://example.invalid/object" TargetMode="External"/>
            <Relationship Id="rPackage" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/package" Target="embeddings/package1.xlsx"/>
            <Relationship Id="rControl" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/control" Target="activeX/activeX1.xml"/>
            <Relationship Id="rVba" Type="http://schemas.microsoft.com/office/2006/relationships/vbaProject" Target="vbaProject.bin"/>
            <Relationship Id="rVbaData" Type="http://schemas.microsoft.com/office/2006/relationships/wordVbaData" Target="vbaData.xml"/>
            <Relationship Id="rToolbar" Type="http://schemas.microsoft.com/office/2006/relationships/attachedToolbars" Target="attachedToolbars.bin"/>
            <Relationship Id="rKeyMap" Type="http://schemas.microsoft.com/office/2006/relationships/keyMapCustomizations" Target="keyMapCustomizations.xml"/>
            """;
        const string extraContentTypes =
            """
            <Override PartName="/word/embeddings/oleObject1.bin" ContentType="application/vnd.openxmlformats-officedocument.oleObject"/>
            <Override PartName="/word/embeddings/package1.xlsx" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"/>
            <Override PartName="/word/activeX/activeX1.xml" ContentType="application/vnd.ms-office.activeX+xml"/>
            <Override PartName="/word/activeX/activeX1.bin" ContentType="application/vnd.ms-office.activeX"/>
            <Override PartName="/word/vbaProject.bin" ContentType="application/vnd.ms-office.vbaProject"/>
            <Override PartName="/word/vbaData.xml" ContentType="application/vnd.ms-word.vbaData+xml"/>
            <Override PartName="/word/attachedToolbars.bin" ContentType="application/vnd.ms-word.attachedToolbars"/>
            <Override PartName="/word/keyMapCustomizations.xml" ContentType="application/vnd.ms-word.keyMapCustomizations+xml"/>
            <Override PartName="/word/vbaProjectSignature.bin" ContentType="application/vnd.ms-office.vbaProjectSignature"/>
            <Override PartName="/_xmlsignatures/origin.sigs" ContentType="application/vnd.openxmlformats-package.digital-signature-origin"/>
            <Override PartName="/_xmlsignatures/sig1.xml" ContentType="application/vnd.openxmlformats-package.digital-signature-xmlsignature+xml"/>
            """;
        return BuildPackage(
            document,
            documentRelationships,
            extraContentTypes,
            entries:
            [
                ("word/embeddings/oleObject1.bin", "ole payload"),
                ("word/embeddings/package1.xlsx", "embedded workbook"),
                ("word/activeX/activeX1.xml", "<ax:ocx xmlns:ax=\"http://schemas.microsoft.com/office/2006/activeX\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" classid=\"{11111111-2222-3333-4444-555555555555}\" persistence=\"persistStreamInit\" license=\"license-secret\" r:id=\"rBin\"><ax:ocxPr name=\"Caption\" value=\"Run\"/><ax:ocxPr name=\"Enabled\" value=\"true\"/></ax:ocx>"),
                ("word/activeX/_rels/activeX1.xml.rels", Relationships("<Relationship Id=\"rBin\" Type=\"http://schemas.microsoft.com/office/2006/relationships/activeXControlBinary\" Target=\"activeX1.bin\"/>")),
                ("word/activeX/activeX1.bin", "activex payload"),
                ("word/vbaProject.bin", "vba payload"),
                ("word/_rels/vbaProject.bin.rels", Relationships("<Relationship Id=\"rSig\" Type=\"http://schemas.microsoft.com/office/2006/relationships/vbaProjectSignature\" Target=\"vbaProjectSignature.bin\"/>")),
                ("word/vbaData.xml", "<wne:vbaSuppData xmlns:wne=\"http://schemas.microsoft.com/office/word/2006/wordml\"/>") ,
                ("word/attachedToolbars.bin", "toolbar payload"),
                ("word/keyMapCustomizations.xml", "<wne:keymaps xmlns:wne=\"http://schemas.microsoft.com/office/word/2006/wordml\"/>") ,
                ("word/vbaProjectSignature.bin", "vba signature"),
                ("customUI/customUI.xml", "<customUI xmlns=\"http://schemas.microsoft.com/office/2006/01/customui\"/>") ,
                ("customUI/qat.xml", "<mso:customUI xmlns:mso=\"http://schemas.microsoft.com/office/2009/07/customui\"/>") ,
                ("_xmlsignatures/origin.sigs", ""),
                ("_xmlsignatures/_rels/origin.sigs.rels", Relationships("<Relationship Id=\"rSig1\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/signature\" Target=\"sig1.xml\"/>")),
                ("_xmlsignatures/sig1.xml", "<Signature xmlns=\"http://www.w3.org/2000/09/xmldsig#\"/>") ,
            ],
            rootRelationships:
                """
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                <Relationship Id="rCustomUi" Type="http://schemas.microsoft.com/office/2006/relationships/ui/extensibility" Target="customUI/customUI.xml"/>
                <Relationship Id="rQat" Type="http://schemas.microsoft.com/office/2006/relationships/ui/userCustomization" Target="customUI/qat.xml"/>
                <Relationship Id="rSignatureOrigin" Type="http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/origin" Target="_xmlsignatures/origin.sigs"/>
                """,
            macroEnabled: true
        );
    }

    private static MemoryStream BuildPackage(
        string documentXml,
        string documentRelationships,
        string extraContentTypes = "",
        IReadOnlyList<(string Name, string Content)>? entries = null,
        string? rootRelationships = null,
        bool macroEnabled = false,
        string? mainContentType = null
    )
    {
        rootRelationships ??=
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>";
        mainContentType ??= macroEnabled
            ? "application/vnd.ms-word.document.macroEnabled.main+xml"
            : "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                "[Content_Types].xml",
                $$"""
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="bin" ContentType="application/octet-stream"/>
                  <Override PartName="/word/document.xml" ContentType="{{mainContentType}}"/>
                  {{extraContentTypes}}
                </Types>
                """
            );
            WriteEntry(archive, "_rels/.rels", Relationships(rootRelationships));
            WriteEntry(archive, "word/document.xml", documentXml);
            if (!string.IsNullOrWhiteSpace(documentRelationships))
            {
                WriteEntry(
                    archive,
                    "word/_rels/document.xml.rels",
                    Relationships(documentRelationships)
                );
            }
            foreach (var (name, content) in entries ?? [])
            {
                WriteEntry(archive, name, content);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static string Relationships(string children) =>
        $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{children}</Relationships>";

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }
}
