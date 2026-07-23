using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class QueryWordPackageOperationTests
{
    [Fact]
    public void ReturnsHighLevelObjectsWithoutMutatingOrLeakingSensitiveProperties()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "semantic.docx");
            File.WriteAllBytes(path, PackageBytes());
            var before = SHA256.HashData(File.ReadAllBytes(path));
            var query = new WordSemanticQuery
            {
                Kinds = [WordSemanticNodeKind.Bookmark, WordSemanticNodeKind.Field],
                IncludeProperties = true,
                IncludeSource = true,
            };

            var result = new QueryWordPackageOperation().Execute(
                new QueryWordPackageRequest(path, query)
            );
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            stream.Position = 7;
            var streamResult = new QueryWordPackageOperation().Execute(
                stream,
                "semantic.docx",
                query
            );

            Assert.Equal(QueryWordPackageContract.Contract, result.OperationContract);
            Assert.Equal(7, stream.Position);
            Assert.Equal(
                WordToolkitOperationJson.Serialize(result),
                WordToolkitOperationJson.Serialize(streamResult)
            );
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal(2, result.MatchedNodeCount);
            Assert.Equal(2, result.ReturnedNodeCount);
            Assert.All(result.Matches, match =>
            {
                Assert.StartsWith("wdn_", match.NodeId, StringComparison.Ordinal);
                Assert.Equal("reference", match.ObjectCategory);
                Assert.Equal("main_document", match.StoryKind);
                Assert.NotEmpty(match.IdentityFingerprint);
                Assert.NotEmpty(match.IdentityKind);
                Assert.Equal("/word/document.xml", match.SourcePartUri);
            });
            var bookmark = result.Matches.Single(match => match.Kind == "bookmark");
            var field = result.Matches.Single(match => match.Kind == "field");
            Assert.DoesNotContain("name", bookmark.Properties?.Keys ?? []);
            Assert.Contains("name", bookmark.RedactedPropertyNames!);
            Assert.DoesNotContain("instruction", field.Properties?.Keys ?? []);
            Assert.Contains("instruction", field.RedactedPropertyNames!);
            var json = WordToolkitOperationJson.Serialize(result);
            Assert.DoesNotContain("SecretAnchor", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("operation_contract\":null", json, StringComparison.Ordinal);
            Assert.False(result.Disclosure.SensitivePropertiesReturned);
            Assert.False(result.Disclosure.SensitiveTextPreviewsReturned);
            Assert.False(result.Disclosure.RawXmlReturned);
            Assert.False(result.Disclosure.ExternalRelationshipsFollowed);
            Assert.False(result.Disclosure.WordOpened);
            Assert.True(result.Disclosure.DocumentContentIsUntrusted);

            var equation = Assert.Single(
                new QueryWordPackageOperation()
                    .Execute(
                        new QueryWordPackageRequest(
                            path,
                            new WordSemanticQuery
                            {
                                Kinds = [WordSemanticNodeKind.Equation],
                            }
                        )
                    )
                    .Matches
            );
            Assert.Equal("math", equation.ObjectCategory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SensitivePropertyDisclosureRequiresTwoExplicitFlags()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "sensitive.docx");
            File.WriteAllBytes(path, PackageBytes());
            var invalid = Assert.Throws<WordToolkitOperationException>(() =>
                new QueryWordPackageOperation().Execute(
                    new QueryWordPackageRequest(
                        path,
                        new WordSemanticQuery
                        {
                            Kinds = [WordSemanticNodeKind.Bookmark],
                        },
                        IncludeSensitiveProperties: true
                    )
                )
            );
            Assert.Equal("INVALID_INPUT", invalid.Code);

            var result = new QueryWordPackageOperation().Execute(
                new QueryWordPackageRequest(
                    path,
                    new WordSemanticQuery
                    {
                        Kinds = [WordSemanticNodeKind.Bookmark],
                        IncludeProperties = true,
                    },
                    IncludeSensitiveProperties: true
                )
            );

            var bookmark = Assert.Single(result.Matches);
            Assert.Equal("SecretAnchor", bookmark.Properties!["name"]);
            Assert.Null(bookmark.RedactedPropertyNames);
            Assert.True(result.Disclosure.SensitivePropertiesReturned);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReportsEveryPropertyValueShortenedByThePublicResponseBudget()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "bounded.docx");
            var longName = new string('A', 200);
            File.WriteAllBytes(path, PackageBytes(longName));

            var result = new QueryWordPackageOperation().Execute(
                new QueryWordPackageRequest(
                    path,
                    new WordSemanticQuery
                    {
                        Kinds = [WordSemanticNodeKind.Bookmark],
                        IncludeProperties = true,
                    },
                    IncludeSensitiveProperties: true
                )
            );

            var bookmark = Assert.Single(result.Matches);
            Assert.Equal(161, bookmark.Properties!["name"].Length);
            Assert.EndsWith("…", bookmark.Properties["name"], StringComparison.Ordinal);
            Assert.Contains("name", bookmark.TruncatedPropertyNames!);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ComplexFieldInstructionsNeedTheSecondOptInForTextPreviews()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "field-preview.docx");
            File.WriteAllBytes(
                path,
                PackageBytes(
                    extraBodyXml:
                        "<w:p><w:r><w:instrText> PRIVATE_FIELD_INSTRUCTION </w:instrText></w:r></w:p>"
                )
            );
            var query = new WordSemanticQuery
            {
                Kinds = [WordSemanticNodeKind.Field],
                IncludeProperties = true,
                TextPreviewCharacters = 160,
            };

            var redacted = new QueryWordPackageOperation().Execute(
                new QueryWordPackageRequest(path, query)
            );
            var redactedJson = WordToolkitOperationJson.Serialize(redacted);
            Assert.DoesNotContain(
                "PRIVATE_FIELD_INSTRUCTION",
                redactedJson,
                StringComparison.Ordinal
            );
            Assert.False(redacted.Disclosure.SensitiveTextPreviewsReturned);

            var disclosed = new QueryWordPackageOperation().Execute(
                new QueryWordPackageRequest(
                    path,
                    query,
                    IncludeSensitiveProperties: true
                )
            );
            Assert.Contains(
                disclosed.Matches,
                match => match.TextPreview?.Contains(
                    "PRIVATE_FIELD_INSTRUCTION",
                    StringComparison.Ordinal
                ) == true
            );
            Assert.True(disclosed.Disclosure.SensitiveTextPreviewsReturned);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RejectsStaleFingerprintAndPreservesThePackage()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "stale.docx");
            File.WriteAllBytes(path, PackageBytes());
            var before = File.ReadAllBytes(path);

            var exception = Assert.Throws<WordToolkitOperationException>(() =>
                new QueryWordPackageOperation().Execute(
                    new QueryWordPackageRequest(
                        path,
                        new WordSemanticQuery(),
                        new string('0', 64)
                    )
                )
            );

            Assert.Equal("VERSION_CONFLICT", exception.Code);
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ProjectedAndIndexedExecutionShareTheSameResultContract()
    {
        using var stream = new MemoryStream(PackageBytes());
        var document = new WordSemanticProjector().Project(
            new OpcPackageReader().Read(stream)
        );
        var index = WordSemanticIndex.Build(document);
        var query = new WordSemanticQuery
        {
            Kinds = [WordSemanticNodeKind.Paragraph],
            Descendant = new WordSemanticRelatedNodePredicate
            {
                Kinds = [WordSemanticNodeKind.Field],
            },
        };
        var operation = new QueryWordPackageOperation();

        var linear = operation.ExecuteProjected(document, "sample.docx", query);
        var indexed = operation.ExecuteProjected(
            document,
            "sample.docx",
            query,
            semanticIndex: index,
            semanticIndexId: "wsi_0123456789abcdef0123456789abcdef"
        );

        Assert.Equal(
            linear.Matches.Select(match => match.NodeId),
            indexed.Matches.Select(match => match.NodeId)
        );
        Assert.False(linear.SemanticIndexUsed);
        Assert.True(indexed.SemanticIndexUsed);
        Assert.Equal("wsi_0123456789abcdef0123456789abcdef", indexed.SemanticIndexId);
        Assert.NotNull(indexed.SemanticIndexFingerprint);
        Assert.True(indexed.ScannedNodeCount < linear.ScannedNodeCount);

        var maximumPropertySeed = operation.ExecuteProjected(
            document,
            "sample.docx",
            new WordSemanticQuery
            {
                PropertyEquals = new Dictionary<string, string>
                {
                    [new string('p', 128)] = "absent",
                },
            },
            semanticIndex: index,
            semanticIndexId: "wsi_0123456789abcdef0123456789abcdef"
        );
        Assert.Equal(137, maximumPropertySeed.CandidateSeed.Length);

        var invalidId = Assert.Throws<WordToolkitOperationException>(() =>
            operation.ExecuteProjected(
                document,
                "sample.docx",
                query,
                semanticIndex: index,
                semanticIndexId: "not-a-semantic-index-id"
            )
        );
        Assert.Equal("INVALID_INPUT", invalidId.Code);

        var invalidNodeId = Assert.Throws<WordToolkitOperationException>(() =>
            operation.ExecuteProjected(
                document,
                "sample.docx",
                new WordSemanticQuery
                {
                    WithinNodeId = new SemanticNodeId("third-paragraph"),
                }
            )
        );
        Assert.Equal("INVALID_INPUT", invalidNodeId.Code);
    }

    [Fact]
    public void RejectsAContentTypeExtensionMismatchAndClosedStreamsWithStableCodes()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "mismatch.docm");
            File.WriteAllBytes(path, PackageBytes());

            var mismatch = Assert.Throws<WordToolkitOperationException>(() =>
                new QueryWordPackageOperation().Execute(
                    new QueryWordPackageRequest(path, new WordSemanticQuery())
                )
            );
            Assert.Equal("INVALID_WORD_PACKAGE", mismatch.Code);

            var closed = new MemoryStream(PackageBytes());
            closed.Dispose();
            var invalidStream = Assert.Throws<WordToolkitOperationException>(() =>
                new QueryWordPackageOperation().Execute(
                    closed,
                    "sample.docx",
                    new WordSemanticQuery()
                )
            );
            Assert.Equal("INVALID_INPUT", invalidStream.Code);

            var invalidQuery = Assert.Throws<WordToolkitOperationException>(() =>
                new QueryWordPackageOperation().Execute(
                    new QueryWordPackageRequest(
                        Path.Combine(directory, "missing.docx"),
                        new WordSemanticQuery
                        {
                            Ancestor = new WordSemanticRelatedNodePredicate(),
                        }
                    )
                )
            );
            Assert.Equal("INVALID_INPUT", invalidQuery.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CanonicalJsonUsesSnakeCaseEnumsAndAllowsAdditiveResultMembers()
    {
        var json = WordToolkitOperationJson.Serialize(
            new JsonCodecProbe(WordSemanticNodeKind.TableRow)
        );
        Assert.Equal("{\"kind\":\"table_row\"}", json);
        Assert.Equal(
            WordSemanticNodeKind.TableRow,
            WordToolkitOperationJson.Deserialize<JsonCodecProbe>(json).Kind
        );
        Assert.Equal(
            WordSemanticNodeKind.TableRow,
            WordToolkitOperationJson.Deserialize<JsonCodecProbe>(
                "{\"kind\":\"table_row\",\"future_addition\":true}"
            ).Kind
        );

        var duplicateKinds = Assert.Throws<WordToolkitOperationException>(() =>
            new QueryWordPackageOperation().ExecuteProjected(
                new WordSemanticProjector().Project(
                    new OpcPackageReader().Read(new MemoryStream(PackageBytes()))
                ),
                "sample.docx",
                new WordSemanticQuery
                {
                    Kinds = [
                        WordSemanticNodeKind.Paragraph,
                        WordSemanticNodeKind.Paragraph,
                    ],
                }
            )
        );
        Assert.Equal("INVALID_INPUT", duplicateKinds.Code);
    }

    [Fact]
    public void CancellationRemainsCancellation()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "cancel.docx");
            File.WriteAllBytes(path, PackageBytes());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                new QueryWordPackageOperation().Execute(
                    new QueryWordPackageRequest(path, new WordSemanticQuery()),
                    cancellation.Token
                )
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] PackageBytes(
        string bookmarkName = "SecretAnchor",
        string extraBodyXml = ""
    )
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                "[Content_Types].xml",
                """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
                  <Default Extension="xml" ContentType="application/xml" />
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" />
                </Types>
                """
            );
            WriteEntry(
                archive,
                "_rels/.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml" />
                </Relationships>
                """
            );
            WriteEntry(
                archive,
                "word/document.xml",
                $"""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml" xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
                  <w:body>
                    <w:p w14:paraId="00112233">
                      <w:bookmarkStart w:id="9" w:name="{bookmarkName}" />
                      <w:fldSimple w:instr=" REF {bookmarkName} ">
                        <w:r><w:t>Visible result</w:t></w:r>
                      </w:fldSimple>
                      <m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>
                      <w:bookmarkEnd w:id="9" />
                    </w:p>
                    {extraBodyXml}
                  </w:body>
                </w:document>
                """
            );
        }
        return stream.ToArray();
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-engine-query-operation-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var target = entry.Open();
        target.Write(Encoding.UTF8.GetBytes(content));
    }

    private sealed record JsonCodecProbe(WordSemanticNodeKind Kind);
}
