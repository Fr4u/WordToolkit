using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordBibliographyGraphTests
{
    [Fact]
    public void ProjectsTypedSourcesStyleLocaleAndContributorsWithoutMutatingPackage()
    {
        using var bytes = BuildPackage(BibliographyXml(
            """
            <b:Source>
              <b:Tag>Smith2026</b:Tag>
              <b:SourceType>Book</b:SourceType>
              <b:Guid>{6D86D06C-9022-4932-8D4C-84C2B0843381}</b:Guid>
              <b:LCID>1045</b:LCID>
              <b:Author><b:Author><b:NameList><b:Person><b:Last>Smith</b:Last><b:First>Jan</b:First></b:Person></b:NameList></b:Author></b:Author>
              <b:Title>Silniki dokumentów</b:Title>
              <b:Year>2026</b:Year>
              <b:City>Warszawa</b:City>
              <b:Publisher>Próba</b:Publisher>
            </b:Source>
            """
        ));
        var package = new OpcPackageReader().Read(bytes);
        var fingerprint = package.Fingerprint;
        var hashes = package.Entries.ToDictionary(entry => entry.Name, entry => entry.Sha256);

        var graph = new WordBibliographyGraphBuilder().Build(package);

        Assert.Equal(fingerprint, graph.PackageFingerprint);
        var collection = Assert.Single(graph.Collections);
        Assert.True(collection.IsPackageReachable);
        Assert.Equal("APA", collection.StyleName);
        Assert.Equal("6", collection.Version);
        var source = Assert.Single(graph.Sources);
        Assert.Equal("Smith2026", source.Tag);
        Assert.Equal("Book", source.SourceType);
        Assert.Equal("6d86d06c-9022-4932-8d4c-84c2b0843381", source.Guid);
        Assert.Equal(1045, source.Lcid);
        Assert.Equal("Silniki dokumentów", source.Title);
        Assert.Equal("2026", source.Year);
        Assert.True(source.IsTagUnique);
        Assert.True(source.IsGuidUnique);
        var contributor = Assert.Single(source.Contributors);
        Assert.Equal("Author", contributor.Role);
        var person = Assert.Single(contributor.People);
        Assert.Equal("Smith", person.Last);
        Assert.Equal("Jan", person.First);
        Assert.True(graph.TryResolveCitationTag("smith2026", out var resolved));
        Assert.Equal(source.Id, resolved!.Id);
        Assert.False(graph.TryResolveCitationTag(" ", out var empty));
        Assert.Null(empty);
        Assert.DoesNotContain(
            graph.Issues,
            issue => issue.Severity == WordBibliographyIssueSeverity.Error
        );
        Assert.Equal(hashes, package.Entries.ToDictionary(entry => entry.Name, entry => entry.Sha256));
        Assert.Equal(fingerprint, package.Fingerprint);
    }

    [Fact]
    public void AcceptsLegacyWordBibliographyNamespace()
    {
        using var bytes = BuildPackage(
            """
            <b:Sources xmlns:b="http://schemas.microsoft.com/office/word/2004/10/bibliography">
              <b:Source><b:Tag>Old01</b:Tag><b:SourceType>JournalArticle</b:SourceType><b:Title>Legacy</b:Title></b:Source>
            </b:Sources>
            """
        );
        var package = new OpcPackageReader().Read(bytes);

        var graph = new WordBibliographyGraphBuilder().Build(package);

        Assert.Equal(
            WordBibliographyGraphBuilder.LegacyBibliographyNamespace,
            Assert.Single(graph.Collections).NamespaceUri
        );
        Assert.Equal("Old01", Assert.Single(graph.Sources).Tag);
    }

    [Fact]
    public void StableSourceIdsSurviveUnrelatedSourceReordering()
    {
        const string first =
            "<b:Source><b:Tag>A</b:Tag><b:SourceType>Book</b:SourceType><b:Guid>{11111111-1111-1111-1111-111111111111}</b:Guid></b:Source>";
        const string second =
            "<b:Source><b:Tag>B</b:Tag><b:SourceType>Book</b:SourceType><b:Guid>{22222222-2222-2222-2222-222222222222}</b:Guid></b:Source>";
        using var forwardBytes = BuildPackage(BibliographyXml(first + second));
        using var reverseBytes = BuildPackage(BibliographyXml(second + first));
        var reader = new OpcPackageReader();

        var forward = new WordBibliographyGraphBuilder().Build(reader.Read(forwardBytes));
        var reverse = new WordBibliographyGraphBuilder().Build(reader.Read(reverseBytes));

        Assert.Equal(
            Assert.Single(forward.Collections).Id,
            Assert.Single(reverse.Collections).Id
        );
        Assert.Equal(
            forward.Sources.Single(source => source.Tag == "A").Id,
            reverse.Sources.Single(source => source.Tag == "A").Id
        );
        Assert.Equal(
            forward.Sources.Single(source => source.Tag == "B").Id,
            reverse.Sources.Single(source => source.Tag == "B").Id
        );

        const string duplicateGuidA =
            "<b:Source><b:Tag>A</b:Tag><b:SourceType>Book</b:SourceType><b:Guid>{33333333-3333-3333-3333-333333333333}</b:Guid></b:Source>";
        const string duplicateGuidB =
            "<b:Source><b:Tag>B</b:Tag><b:SourceType>Book</b:SourceType><b:Guid>{33333333-3333-3333-3333-333333333333}</b:Guid></b:Source>";
        using var duplicateForwardBytes = BuildPackage(BibliographyXml(
            duplicateGuidA + duplicateGuidB
        ));
        using var duplicateReverseBytes = BuildPackage(BibliographyXml(
            duplicateGuidB + duplicateGuidA
        ));
        var duplicateForward = new WordBibliographyGraphBuilder().Build(
            reader.Read(duplicateForwardBytes)
        );
        var duplicateReverse = new WordBibliographyGraphBuilder().Build(
            reader.Read(duplicateReverseBytes)
        );

        Assert.Equal(
            duplicateForward.Sources.Single(source => source.Tag == "A").Id,
            duplicateReverse.Sources.Single(source => source.Tag == "A").Id
        );
        Assert.Equal(
            duplicateForward.Sources.Single(source => source.Tag == "B").Id,
            duplicateReverse.Sources.Single(source => source.Tag == "B").Id
        );
    }

    [Fact]
    public void RejectsAmbiguousTagsAndReportsInvalidTypedValues()
    {
        using var bytes = BuildPackage(BibliographyXml(
            """
            <b:Source><b:Tag>Same</b:Tag><b:SourceType>UnknownKind</b:SourceType><b:Guid>broken</b:Guid><b:LCID>-1</b:LCID></b:Source>
            <b:Source><b:Tag>same</b:Tag><b:SourceType>Book</b:SourceType></b:Source>
            """
        ));
        var package = new OpcPackageReader().Read(bytes);

        var graph = new WordBibliographyGraphBuilder().Build(package);

        Assert.False(graph.TryResolveCitationTag("SAME", out _));
        Assert.All(graph.Sources, source => Assert.False(source.IsTagUnique));
        var duplicateTagIssues = graph.Issues.Where(issue =>
            issue.Code == "BIB_SOURCE_TAG_DUPLICATE"
        ).ToArray();
        Assert.Equal(2, duplicateTagIssues.Length);
        Assert.All(duplicateTagIssues, issue => Assert.NotNull(issue.SourceId));
        Assert.Contains(
            graph.Issues,
            issue => issue.Code == "BIB_SOURCE_TYPE_UNKNOWN" && issue.SourceId is not null
        );
        Assert.Contains(graph.Issues, issue => issue.Code == "BIB_SOURCE_GUID_INVALID");
        Assert.Contains(graph.Issues, issue => issue.Code == "BIB_SOURCE_LCID_INVALID");
    }

    [Fact]
    public void EnforcesSourceLimitsAndBoundsMalformedCustomXmlDiagnostics()
    {
        using var twoSources = BuildPackage(BibliographyXml(
            """
            <b:Source><b:Tag>A</b:Tag><b:SourceType>Book</b:SourceType></b:Source>
            <b:Source><b:Tag>B</b:Tag><b:SourceType>Book</b:SourceType></b:Source>
            """
        ));
        var package = new OpcPackageReader().Read(twoSources);
        Assert.Throws<WordBibliographyLimitException>(() =>
            new WordBibliographyGraphBuilder(
                new WordBibliographyGraphOptions { MaxSources = 1 }
            ).Build(package)
        );

        using var malformed = BuildPackage("<broken>");
        var malformedPackage = new OpcPackageReader().Read(malformed);
        var graph = new WordBibliographyGraphBuilder(
            new WordBibliographyGraphOptions { MaxIssues = 1 }
        ).Build(malformedPackage);
        Assert.Empty(graph.Collections);
        Assert.Equal("BIB_CUSTOM_XML_NOT_WELL_FORMED", Assert.Single(graph.Issues).Code);
    }

    [Fact]
    public void EnforcesPeopleAndCorporateNameLimitsAcrossAllContributorRoles()
    {
        const string contributors = """
            <b:Author><b:NameList><b:Person><b:Last>One</b:Last></b:Person></b:NameList></b:Author>
            <b:Editor><b:NameList><b:Person><b:Last>Two</b:Last></b:Person></b:NameList></b:Editor>
            """;
        using var peopleBytes = BuildPackage(BibliographyXml(
            $"<b:Source><b:Tag>A</b:Tag><b:SourceType>Book</b:SourceType>{contributors}</b:Source>"
        ));
        var reader = new OpcPackageReader();
        var peoplePackage = reader.Read(peopleBytes);

        Assert.Throws<WordBibliographyLimitException>(() =>
            new WordBibliographyGraphBuilder(
                new WordBibliographyGraphOptions { MaxPeoplePerSource = 1 }
            ).Build(peoplePackage)
        );

        const string corporateContributors = """
            <b:Author><b:NameList><b:Corporate>One Corp</b:Corporate></b:NameList></b:Author>
            <b:Editor><b:NameList><b:Corporate>Two Corp</b:Corporate></b:NameList></b:Editor>
            """;
        using var corporateBytes = BuildPackage(BibliographyXml(
            $"<b:Source><b:Tag>B</b:Tag><b:SourceType>Book</b:SourceType>{corporateContributors}</b:Source>"
        ));
        var corporatePackage = reader.Read(corporateBytes);

        Assert.Throws<WordBibliographyLimitException>(() =>
            new WordBibliographyGraphBuilder(
                new WordBibliographyGraphOptions { MaxCorporateNamesPerSource = 1 }
            ).Build(corporatePackage)
        );

        using var unmodeledBytes = BuildPackage(BibliographyXml(
            "<b:Source><b:Tag>C</b:Tag><b:SourceType>Book</b:SourceType><b:PrivateOne><b:Value/></b:PrivateOne><b:PrivateTwo><b:Value/></b:PrivateTwo></b:Source>"
        ));
        var unmodeledPackage = reader.Read(unmodeledBytes);
        Assert.Throws<WordBibliographyLimitException>(() =>
            new WordBibliographyGraphBuilder(
                new WordBibliographyGraphOptions { MaxUnmodeledElementsPerSource = 1 }
            ).Build(unmodeledPackage)
        );
    }

    [Fact]
    public void TypedProjectionHasDeterministicOperationResourceBoundary()
    {
        using var bytes = BuildPackage(BibliographyXml(
            """
            <b:Source><b:Tag>A</b:Tag><b:SourceType>Book</b:SourceType><b:Author><b:NameList><b:Person><b:Last>One</b:Last></b:Person></b:NameList></b:Author><b:Title>Title</b:Title></b:Source>
            """
        ));
        var package = new OpcPackageReader().Read(bytes);
        var probeLease = new WordOperationResourceLease();
        _ = new WordBibliographyGraphBuilder(null, probeLease).Build(package);
        var used = probeLease.Snapshot().AccountedBytes;

        var insufficientLease = new WordOperationResourceLease(used - 1);
        Assert.Throws<WordOperationResourceLimitException>(() =>
            new WordBibliographyGraphBuilder(null, insufficientLease).Build(package)
        );

        var exactLease = new WordOperationResourceLease(used);
        var graph = new WordBibliographyGraphBuilder(null, exactLease).Build(package);
        Assert.Single(graph.Sources);
        Assert.Equal(used, exactLease.Snapshot().AccountedBytes);
    }

    [Fact]
    public void UnifiedDependencyGraphResolvesCitationToConcreteBibliographySource()
    {
        using var bytes = BuildPackage(
            BibliographyXml(
                """
                <b:Source><b:Tag>Smith2026</b:Tag><b:SourceType>Book</b:SourceType><b:Title>Private title</b:Title></b:Source>
                """
            ),
            citationTag: "Smith2026"
        );
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);

        var graph = new WordDependencyGraphBuilder().Build(package, semantic);

        Assert.True(graph.Coverage.BibliographySources);
        Assert.DoesNotContain(
            "citations_bibliography_sources",
            graph.Coverage.ExplicitlyUnmodeledDomains
        );
        var sourceNode = Assert.Single(
            graph.Nodes,
            node => node.Kind == WordDependencyNodeKind.BibliographySource
        );
        var citationEdge = Assert.Single(
            graph.Edges,
            edge => edge.Kind == WordDependencyEdgeKind.FieldReference
                && edge.Qualifier == "Reads:Citation"
        );
        Assert.Equal(sourceNode.Id, citationEdge.TargetNodeId);
        Assert.True(citationEdge.IsResolved);
        Assert.DoesNotContain(graph.Issues, issue =>
            issue.Code == "WDG030" && issue.EdgeId == citationEdge.Id
        );
    }

    [Fact]
    public void DuplicateTagsKeepConcreteSourcesResolvedButCitationUnresolved()
    {
        using var bytes = BuildPackage(
            BibliographyXml(
                """
                <b:Source><b:Tag>Same</b:Tag><b:SourceType>Book</b:SourceType></b:Source>
                <b:Source><b:Tag>same</b:Tag><b:SourceType>Book</b:SourceType></b:Source>
                """
            ),
            citationTag: "Same"
        );
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);

        var graph = new WordDependencyGraphBuilder().Build(package, semantic);

        Assert.All(
            graph.Nodes.Where(node =>
                node.Kind == WordDependencyNodeKind.BibliographySource
            ),
            node => Assert.True(node.IsResolved)
        );
        Assert.All(
            graph.Edges.Where(edge =>
                edge.Kind == WordDependencyEdgeKind.BibliographyContainsSource
            ),
            edge => Assert.True(edge.IsResolved)
        );
        var citation = Assert.Single(graph.Edges, edge =>
            edge.Kind == WordDependencyEdgeKind.FieldReference
                && edge.Qualifier == "Reads:Citation"
        );
        Assert.False(citation.IsResolved);
        Assert.Equal(
            WordDependencyNodeKind.ReferenceTarget,
            graph.Nodes.Single(node => node.Id == citation.TargetNodeId).Kind
        );
    }

    [Fact]
    public void DuplicateSingletonIdentityFieldsFailClosed()
    {
        using var duplicateTagBytes = BuildPackage(
            BibliographyXml(
                """
                <b:Source><b:Tag>A</b:Tag><b:Tag>B</b:Tag><b:SourceType>Book</b:SourceType><b:SourceType>JournalArticle</b:SourceType></b:Source>
                """
            ),
            citationTag: "A"
        );
        var reader = new OpcPackageReader();
        var duplicateTagPackage = reader.Read(duplicateTagBytes);

        var bibliography = new WordBibliographyGraphBuilder().Build(duplicateTagPackage);

        var source = Assert.Single(bibliography.Sources);
        Assert.Null(source.Tag);
        Assert.True(source.HasAmbiguousTag);
        Assert.False(source.IsTagUnique);
        Assert.Null(source.SourceType);
        Assert.True(source.HasAmbiguousSourceType);
        Assert.False(bibliography.TryResolveCitationTag("A", out _));
        Assert.Contains(bibliography.Issues, issue =>
            issue.Code == "BIB_SOURCE_FIELD_DUPLICATE"
                && issue.SourceId == source.Id
        );
        Assert.DoesNotContain(bibliography.Issues, issue =>
            issue.Code is "BIB_SOURCE_TYPE_MISSING" or "BIB_SOURCE_TYPE_UNKNOWN"
        );

        var semantic = new WordSemanticProjector().Project(duplicateTagPackage);
        var dependencies = new WordDependencyGraphBuilder().Build(
            duplicateTagPackage,
            semantic
        );
        Assert.False(Assert.Single(dependencies.Edges, edge =>
            edge.Kind == WordDependencyEdgeKind.FieldReference
                && edge.Qualifier == "Reads:Citation"
        ).IsResolved);

        const string duplicateGuidForward =
            "<b:Source><b:Tag>Stable</b:Tag><b:SourceType>Book</b:SourceType><b:Guid>{11111111-1111-1111-1111-111111111111}</b:Guid><b:Guid>{22222222-2222-2222-2222-222222222222}</b:Guid></b:Source>";
        const string duplicateGuidReverse =
            "<b:Source><b:Tag>Stable</b:Tag><b:SourceType>Book</b:SourceType><b:Guid>{22222222-2222-2222-2222-222222222222}</b:Guid><b:Guid>{11111111-1111-1111-1111-111111111111}</b:Guid></b:Source>";
        using var forwardBytes = BuildPackage(BibliographyXml(duplicateGuidForward));
        using var reverseBytes = BuildPackage(BibliographyXml(duplicateGuidReverse));
        var forward = Assert.Single(
            new WordBibliographyGraphBuilder().Build(reader.Read(forwardBytes)).Sources
        );
        var reverse = Assert.Single(
            new WordBibliographyGraphBuilder().Build(reader.Read(reverseBytes)).Sources
        );

        Assert.Null(forward.Guid);
        Assert.True(forward.HasAmbiguousGuid);
        Assert.False(forward.IsGuidUnique);
        Assert.Equal(forward.Id, reverse.Id);
    }

    private static string BibliographyXml(string sources) =>
        $$"""
        <b:Sources xmlns:b="http://schemas.openxmlformats.org/officeDocument/2006/bibliography" SelectedStyle="\APASixthEditionOfficeOnline.xsl" StyleName="APA" Version="6">
          {{sources}}
        </b:Sources>
        """;

    private static MemoryStream BuildPackage(
        string bibliographyXml,
        string? citationTag = null
    )
    {
        var field = citationTag is null
            ? string.Empty
            : $"<w:fldSimple w:instr=\" CITATION {citationTag} \"><w:r><w:t>[1]</w:t></w:r></w:fldSimple>";
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                "[Content_Types].xml",
                """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """
            );
            WriteEntry(
                archive,
                "_rels/.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """
            );
            WriteEntry(
                archive,
                "word/document.xml",
                $$"""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p>{{field}}</w:p></w:body></w:document>
                """
            );
            WriteEntry(
                archive,
                "word/_rels/document.xml.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdSources" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml" Target="../customXml/item1.xml"/>
                </Relationships>
                """
            );
            WriteEntry(archive, "customXml/item1.xml", bibliographyXml);
        }
        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }
}
