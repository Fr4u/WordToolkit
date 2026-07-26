using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordMailMergeGraphTests
{
    [Fact]
    public void ProjectsConfigurationOdsoMappingsRecipientsAndFieldsWithoutFollowingSource()
    {
        using var bytes = BuildPackage();
        var sourceHash = SHA256.HashData(bytes.ToArray());
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);

        var graph = new WordMailMergeGraphBuilder().Build(package, semantic);

        var configuration = Assert.IsType<WordMailMergeConfiguration>(graph.Configuration);
        Assert.Equal("formLetters", configuration.MainDocumentType);
        Assert.Equal("database", configuration.DataType);
        Assert.Equal("newDocument", configuration.Destination);
        Assert.True(configuration.LinkToQuery);
        Assert.True(configuration.HasExternalDataSource);
        Assert.True(configuration.HasSensitiveConnectionMetadata);
        Assert.Equal("SELECT * FROM [Clients$]", configuration.Query);
        Assert.Equal("Provider=Sensitive", configuration.ConnectionString);
        Assert.True(configuration.DataSourceRelationship!.IsExternal);
        Assert.True(configuration.DataSourceRelationship.TargetExists);
        Assert.True(configuration.DataSourceRelationship.IsResolved);

        var odso = Assert.IsType<WordMailMergeDataSourceObject>(
            configuration.DataSourceObject
        );
        Assert.Equal("Clients$", odso.TableName);
        Assert.Equal(44, odso.ColumnDelimiter);
        Assert.True(odso.FirstRowIsHeader);
        Assert.True(odso.SourceRelationship!.IsExternal);
        Assert.True(odso.RecipientDataRelationship!.IsResolved);

        Assert.Equal(2, graph.Mappings.Count);
        var customerId = graph.Mappings[0];
        Assert.Equal(0, customerId.Position);
        Assert.Equal("CustomerId", customerId.SourceColumnName);
        Assert.Equal("IncorrectDeclaredName", customerId.DeclaredMappedName);
        Assert.Equal("Unique", customerId.WordEffectivePredefinedName);
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "MAIL_MERGE_WORD_POSITIONAL_MAPPING_OVERRIDE"
                && issue.SubjectId == customerId.Id
        );

        var recipientPart = Assert.IsType<WordMailMergeRecipientDataPart>(
            graph.RecipientDataPart
        );
        Assert.Equal("/word/recipients.xml", recipientPart.PartUri);
        Assert.True(recipientPart.IsPackageReachable);
        Assert.Equal(1, recipientPart.IncomingRelationshipCount);
        Assert.Equal(2, graph.Recipients.Count);
        Assert.True(graph.Recipients[0].IsIncluded);
        Assert.Equal(WordMailMergeRecipientIdentityKind.UniqueTag, graph.Recipients[0].IdentityKind);
        Assert.False(graph.Recipients[1].IsIncluded);
        Assert.Equal(WordMailMergeRecipientIdentityKind.Hash, graph.Recipients[1].IdentityKind);

        Assert.Equal(2, graph.Fields.Count);
        var customerField = graph.Fields.Single(field => field.TargetName == "CustomerId");
        Assert.Equal(
            WordMailMergeFieldBindingStatus.ResolvedBySourceColumnName,
            customerField.BindingStatus
        );
        Assert.Equal(customerId.Id, Assert.Single(customerField.MappingIds));
        var firstNameField = graph.Fields.Single(field => field.TargetName == "FirstName");
        Assert.Equal(
            WordMailMergeFieldBindingStatus.ResolvedBySourceColumnName,
            firstNameField.BindingStatus
        );
        Assert.DoesNotContain(
            graph.Issues,
            issue => issue.Severity == WordMailMergeIssueSeverity.Error
        );
        Assert.Equal(sourceHash, SHA256.HashData(bytes.ToArray()));
    }

    [Fact]
    public void AcceptsStrictWordAndRelationshipNamespaces()
    {
        using var bytes = BuildPackage(strict: true);
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);

        var graph = new WordMailMergeGraphBuilder().Build(package, semantic);

        Assert.True(graph.HasMailMergeEvidence);
        Assert.Equal(2, graph.Fields.Count);
        Assert.Equal(
            WordMailMergeGraphBuilder.StrictWordNamespace,
            Assert.IsType<WordMailMergeRecipientDataPart>(graph.RecipientDataPart).NamespaceUri
        );
        Assert.All(
            graph.Fields,
            field => Assert.Equal(
                WordMailMergeFieldBindingStatus.ResolvedBySourceColumnName,
                field.BindingStatus
            )
        );
        Assert.DoesNotContain(
            graph.Issues,
            issue => issue.Code == "MAIL_MERGE_RELATIONSHIP_TYPE_INVALID"
        );
    }

    [Fact]
    public void ReportsWrongRecipientRelationshipTypeMissingIdentityAndForbiddenRelationships()
    {
        using var bytes = BuildPackage(
            invalidRecipientRelationshipType: true,
            ambiguousRecipientIdentity: true,
            recipientOwnsRelationship: true
        );
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);

        var graph = new WordMailMergeGraphBuilder().Build(package, semantic);

        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "MAIL_MERGE_RELATIONSHIP_TYPE_INVALID"
                && issue.Severity == WordMailMergeIssueSeverity.Error
        );
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "MAIL_MERGE_RECIPIENT_IDENTITY_AMBIGUOUS"
        );
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "MAIL_MERGE_RECIPIENT_RELATIONSHIPS_FORBIDDEN"
        );
    }

    [Fact]
    public void ReportsMissingInternalRecipientTargetWithoutInventingRecipientRecords()
    {
        using var bytes = BuildPackage(missingRecipientTarget: true);
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);

        var graph = new WordMailMergeGraphBuilder().Build(package, semantic);

        Assert.Null(graph.RecipientDataPart);
        Assert.Empty(graph.Recipients);
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "MAIL_MERGE_RELATIONSHIP_TARGET_MISSING"
        );
    }

    [Fact]
    public void EnforcesMappingRecipientFieldAndOperationResourceLimits()
    {
        using var bytes = BuildPackage();
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);

        Assert.Throws<WordMailMergeLimitException>(() =>
            new WordMailMergeGraphBuilder(
                new WordMailMergeGraphOptions { MaxMappings = 1 }
            ).Build(package, semantic)
        );
        Assert.Throws<WordMailMergeLimitException>(() =>
            new WordMailMergeGraphBuilder(
                new WordMailMergeGraphOptions { MaxRecipients = 1 }
            ).Build(package, semantic)
        );
        Assert.Throws<WordMailMergeLimitException>(() =>
            new WordMailMergeGraphBuilder(
                new WordMailMergeGraphOptions { MaxFields = 1 }
            ).Build(package, semantic)
        );

        var probeLease = new WordOperationResourceLease();
        _ = new WordMailMergeGraphBuilder(null, probeLease).Build(package, semantic);
        var used = probeLease.Snapshot().AccountedBytes;
        Assert.True(used > 0);
        Assert.Throws<WordOperationResourceLimitException>(() =>
            new WordMailMergeGraphBuilder(
                null,
                new WordOperationResourceLease(used - 1)
            ).Build(package, semantic)
        );
        var exactLease = new WordOperationResourceLease(used);
        var graph = new WordMailMergeGraphBuilder(null, exactLease).Build(package, semantic);
        Assert.Equal(2, graph.Fields.Count);
        Assert.Equal(used, exactLease.Snapshot().AccountedBytes);
        Assert.Contains(
            exactLease.Snapshot().Stages,
            stage => stage.Stage == WordOperationResourceStage.MailMerge
        );
    }

    [Fact]
    public void JoinsMailMergeObjectsIntoSharedDependencyGraph()
    {
        using var bytes = BuildPackage();
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);

        var graph = new WordDependencyGraphBuilder().Build(package, semantic);

        Assert.True(graph.Coverage.MailMerge);
        Assert.Equal(0, graph.MailMergeIssueCount(issue =>
            issue.Severity == WordDependencyIssueSeverity.Error
        ));
        Assert.Single(
            graph.Nodes,
            node => node.Kind == WordDependencyNodeKind.MailMergeConfiguration
        );
        Assert.Single(
            graph.Nodes,
            node => node.Kind == WordDependencyNodeKind.MailMergeDataSourceObject
        );
        Assert.Equal(
            2,
            graph.Nodes.Count(node => node.Kind == WordDependencyNodeKind.MailMergeFieldMapping)
        );
        Assert.Equal(
            2,
            graph.Nodes.Count(node => node.Kind == WordDependencyNodeKind.MailMergeRecipient)
        );
        Assert.Equal(
            2,
            graph.Nodes.Count(node => node.Kind == WordDependencyNodeKind.MailMergeField)
        );
        Assert.Contains(
            graph.Edges,
            edge => edge.Kind == WordDependencyEdgeKind.MailMergeUsesDataSource
                && edge.IsExternal
        );
        Assert.Equal(
            2,
            graph.Edges.Count(edge => edge.Kind == WordDependencyEdgeKind.MailMergeFieldUsesMapping)
        );
        Assert.All(
            graph.Edges,
            edge =>
            {
                Assert.True(graph.TryGetNode(edge.SourceNodeId, out _));
                Assert.True(graph.TryGetNode(edge.TargetNodeId, out _));
            }
        );
    }

    [Fact]
    public void HighLevelAnalysisReturnsOnlyContentFreeMailMergeEvidence()
    {
        using var bytes = BuildPackage();
        var path = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-mail-merge-{Guid.NewGuid():N}.docx"
        );
        try
        {
            File.WriteAllBytes(path, bytes.ToArray());

            var result = new DocumentAnalysisWordPackageOperation().Analyze(
                new DocumentAnalysisRequest(path)
            );

            Assert.Equal("wordtoolkit.analyze_ooxml_document/1.1", result.OperationContract);
            Assert.True(result.MailMerge.Present);
            Assert.Equal(1, result.MailMerge.ConfigurationCount);
            Assert.Equal(2, result.MailMerge.MappingCount);
            Assert.Equal(2, result.MailMerge.RecipientCount);
            Assert.Equal(1, result.MailMerge.IncludedRecipientCount);
            Assert.Equal(2, result.MailMerge.FieldCount);
            Assert.Equal(2, result.MailMerge.ResolvedFieldCount);
            Assert.True(result.MailMerge.HasExternalDataSource);
            Assert.True(result.MailMerge.HasSensitiveConnectionMetadata);
            Assert.False(result.MailMerge.ExternalDataSourcesOpened);
            Assert.Contains(
                result.Signals,
                signal => signal.Code == "MAIL_MERGE_EVIDENCE"
                    && signal.NextAction == "inspect_ooxml_mail_merge"
                    && signal.BlocksAutomaticMutation
            );
            var json = WordToolkitOperationJson.Serialize(result);
            Assert.DoesNotContain("Provider=Sensitive", json, StringComparison.Ordinal);
            Assert.DoesNotContain("Clients$", json, StringComparison.Ordinal);
            Assert.DoesNotContain("CustomerId", json, StringComparison.Ordinal);
            Assert.DoesNotContain("first", json, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MemoryStream BuildPackage(
        bool strict = false,
        bool invalidRecipientRelationshipType = false,
        bool ambiguousRecipientIdentity = false,
        bool recipientOwnsRelationship = false,
        bool missingRecipientTarget = false
    )
    {
        var w = strict
            ? WordMailMergeGraphBuilder.StrictWordNamespace
            : WordMailMergeGraphBuilder.TransitionalWordNamespace;
        var r = strict
            ? WordMailMergeGraphBuilder.StrictRelationshipsNamespace
            : WordMailMergeGraphBuilder.TransitionalRelationshipsNamespace;
        var officeDocumentRelationship = r + "/officeDocument";
        var settingsRelationship = r + "/settings";
        var mailMergeRelationship = r + "/mailMergeSource";
        var recipientRelationship = invalidRecipientRelationshipType
            ? r + "/customXml"
            : r + "/recipientData";
        var recipientSecondIdentity = ambiguousRecipientIdentity
            ? "<w:uniqueTag w:val=\"second\"/><w:hash w:val=\"hash-two\"/>"
            : "<w:hash w:val=\"hash-two\"/>";
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(
                archive,
                "[Content_Types].xml",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/>
                  {(missingRecipientTarget ? "" : "<Override PartName=\"/word/recipients.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.mailMergeRecipientData+xml\"/>")}
                </Types>
                """
            );
            Add(
                archive,
                "_rels/.rels",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="{officeDocumentRelationship}" Target="word/document.xml"/>
                </Relationships>
                """
            );
            Add(
                archive,
                "word/document.xml",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="{w}" xmlns:r="{r}">
                  <w:body>
                    {ComplexField("CustomerId")}
                    {ComplexField("FirstName")}
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """
            );
            Add(
                archive,
                "word/_rels/document.xml.rels",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdSettings" Type="{settingsRelationship}" Target="settings.xml"/>
                </Relationships>
                """
            );
            Add(
                archive,
                "word/settings.xml",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:settings xmlns:w="{w}" xmlns:r="{r}">
                  <w:mailMerge>
                    <w:mainDocumentType w:val="formLetters"/>
                    <w:dataType w:val="database"/>
                    <w:destination w:val="newDocument"/>
                    <w:linkToQuery w:val="1"/>
                    <w:query w:val="SELECT * FROM [Clients$]"/>
                    <w:connectString w:val="Provider=Sensitive"/>
                    <w:dataSource r:id="rIdData"/>
                    <w:odso>
                      <w:udl w:val="Provider=Sensitive;Password=Secret"/>
                      <w:table w:val="Clients$"/>
                      <w:src r:id="rIdOdso"/>
                      <w:colDelim w:val="44"/>
                      <w:type w:val="database"/>
                      <w:fHdr w:val="1"/>
                      <w:fieldMapData>
                        <w:type w:val="dbColumn"/>
                        <w:name w:val="CustomerId"/>
                        <w:mappedName w:val="IncorrectDeclaredName"/>
                        <w:column w:val="0"/>
                      </w:fieldMapData>
                      <w:fieldMapData>
                        <w:type w:val="dbColumn"/>
                        <w:name w:val="FirstName"/>
                        <w:mappedName w:val="CourtesyTitle"/>
                        <w:column w:val="1"/>
                      </w:fieldMapData>
                      <w:recipientData r:id="rIdRecipients"/>
                    </w:odso>
                  </w:mailMerge>
                </w:settings>
                """
            );
            Add(
                archive,
                "word/_rels/settings.xml.rels",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdData" Type="{mailMergeRelationship}" Target="file:///C:/private/clients.xlsx" TargetMode="External"/>
                  <Relationship Id="rIdOdso" Type="{mailMergeRelationship}" Target="file:///C:/private/clients.xlsx" TargetMode="External"/>
                  <Relationship Id="rIdRecipients" Type="{recipientRelationship}" Target="recipients.xml"/>
                </Relationships>
                """
            );
            if (!missingRecipientTarget)
            {
                Add(
                    archive,
                    "word/recipients.xml",
                    $"""
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <w:recipients xmlns:w="{w}">
                      <w:recipientData>
                        <w:active w:val="1"/>
                        <w:column w:val="0"/>
                        <w:uniqueTag w:val="first"/>
                      </w:recipientData>
                      <w:recipientData>
                        <w:active w:val="0"/>
                        <w:column w:val="1"/>
                        {recipientSecondIdentity}
                      </w:recipientData>
                    </w:recipients>
                    """
                );
                if (recipientOwnsRelationship)
                {
                    Add(
                        archive,
                        "word/_rels/recipients.xml.rels",
                        """
                        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                          <Relationship Id="rIdForbidden" Type="urn:wordtoolkit:test" Target="document.xml"/>
                        </Relationships>
                        """
                    );
                }
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static string ComplexField(string name) =>
        $"""
        <w:p>
          <w:r><w:fldChar w:fldCharType="begin"/></w:r>
          <w:r><w:instrText xml:space="preserve"> MERGEFIELD "{name}" </w:instrText></w:r>
          <w:r><w:fldChar w:fldCharType="separate"/></w:r>
          <w:r><w:t>{name}</w:t></w:r>
          <w:r><w:fldChar w:fldCharType="end"/></w:r>
        </w:p>
        """;

    private static void Add(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var writer = new StreamWriter(
            entry.Open(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
        writer.Write(content);
    }
}

internal static class WordMailMergeDependencyAssertions
{
    public static int MailMergeIssueCount(
        this WordDependencyGraph graph,
        Func<WordDependencyIssue, bool> predicate
    ) => graph.Issues.Count(issue =>
        issue.Code == "WDG062" && predicate(issue)
    );
}
