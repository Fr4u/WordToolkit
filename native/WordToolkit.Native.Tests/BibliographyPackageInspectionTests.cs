using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class BibliographyPackageInspectionTests
{
    [Fact]
    public async Task InspectBibliographyIsCompactRedactedAndNeverStartsWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-bibliography-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "bibliography.docx");
            CreatePackage(path);
            var service = new WordLiveService(new NoInvokeHost());
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path })
            );

            var result = await service.CallAsync(
                "inspect_ooxml_bibliography",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;

            Assert.Equal("summary", root.GetProperty("view").GetString());
            Assert.Equal(1, root.GetProperty("collection_count").GetInt32());
            Assert.Equal(1, root.GetProperty("source_count").GetInt32());
            Assert.Equal(1, root.GetProperty("citation_count").GetInt32());
            Assert.Equal(1, root.GetProperty("resolved_citation_count").GetInt32());
            Assert.Equal(0, root.GetProperty("unresolved_citation_count").GetInt32());
            Assert.False(root.GetProperty("word_opened").GetBoolean());
            Assert.False(root.GetProperty("fields_evaluated").GetBoolean());
            Assert.False(root.GetProperty("bibliography_xslt_executed").GetBoolean());
            Assert.False(root.GetProperty("external_targets_followed").GetBoolean());
            var operationBudget = root.GetProperty("operation_budget");
            Assert.Equal("wop1", operationBudget.GetProperty("model").GetString());
            Assert.True(operationBudget.GetProperty("used").GetInt64() > 0);
            Assert.Equal(
                WordToolkit.Engine.Resources.WordOperationResourceLease
                    .DefaultMaximumAccountedBytes,
                operationBudget.GetProperty("maximum").GetInt64()
            );
            Assert.Equal(
                "process_hmac_sha256_64",
                root.GetProperty("fingerprint_scope").GetString()
            );
            var responseBudget = root.GetProperty("response_budget");
            Assert.Equal(
                "bibliography_projected_payload_characters_v1",
                responseBudget.GetProperty("model").GetString()
            );
            Assert.True(
                responseBudget.GetProperty("used").GetInt32()
                    <= responseBudget.GetProperty("maximum").GetInt32()
            );
            var raw = root.GetRawText();
            Assert.DoesNotContain("Secret2026", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("Private bibliography title", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("HiddenSurname", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("Private Publisher", raw, StringComparison.Ordinal);
            Assert.True(raw.Length < 5_000, $"Default response is too large: {raw.Length}");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SensitiveSourceAndCitationViewsRemainBoundedAndSourceLinked()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-bibliography-detail-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "bibliography.docx");
            CreatePackage(path);
            var service = new WordLiveService(new NoInvokeHost());
            using var sourcesArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                view = "sources",
                include_sensitive = true,
                include_source = true,
            }));

            var sourcesResult = await service.CallAsync(
                "inspect_ooxml_bibliography",
                sourcesArguments.RootElement,
                CancellationToken.None
            );
            using var sourcesJson = JsonDocument.Parse(
                JsonSerializer.Serialize(sourcesResult)
            );
            var source = Assert.Single(
                sourcesJson.RootElement.GetProperty("items").EnumerateArray()
            );
            Assert.Equal("Secret2026", source.GetProperty("tag").GetString());
            Assert.Equal(
                "Private bibliography title",
                source.GetProperty("title").GetString()
            );
            Assert.Equal("2026", source.GetProperty("year").GetString());
            var publicYearFingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes("2026"))
            ).ToLowerInvariant()[..16];
            Assert.NotEqual(
                publicYearFingerprint,
                source.GetProperty("year_fingerprint").GetString()
            );
            Assert.Equal(1045, source.GetProperty("lcid").GetInt32());
            Assert.StartsWith(
                "wbs_",
                source.GetProperty("source_id").GetString(),
                StringComparison.Ordinal
            );
            Assert.Equal("/customXml/item1.xml", source.GetProperty("part_uri").GetString());

            using var contributorArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                view = "contributors",
                include_sensitive = true,
            }));
            var contributorResult = await service.CallAsync(
                "inspect_ooxml_bibliography",
                contributorArguments.RootElement,
                CancellationToken.None
            );
            using var contributorJson = JsonDocument.Parse(
                JsonSerializer.Serialize(contributorResult)
            );
            var contributor = Assert.Single(
                contributorJson.RootElement.GetProperty("items").EnumerateArray()
            );
            Assert.Equal(5, contributor.GetProperty("person_count").GetInt32());
            Assert.Equal(4, contributor.GetProperty("people").GetArrayLength());
            Assert.True(contributor.GetProperty("people_truncated").GetBoolean());

            using var citationArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                view = "citations",
            }));
            var citationResult = await service.CallAsync(
                "inspect_ooxml_bibliography",
                citationArguments.RootElement,
                CancellationToken.None
            );
            using var citationJson = JsonDocument.Parse(
                JsonSerializer.Serialize(citationResult)
            );
            var citation = Assert.Single(
                citationJson.RootElement.GetProperty("items").EnumerateArray()
            );
            Assert.True(citation.GetProperty("resolved").GetBoolean());
            Assert.Equal(JsonValueKind.Null, citation.GetProperty("citation_tag").ValueKind);
            Assert.Equal(
                source.GetProperty("source_id").GetString(),
                citation.GetProperty("source_id").GetString()
            );
            Assert.Equal(
                16,
                citation.GetProperty("citation_tag_fingerprint").GetString()!.Length
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectBibliographyRejectsUnknownArguments()
    {
        var service = new WordLiveService(new NoInvokeHost());
        using var arguments = JsonDocument.Parse(
            """{"local_path":"missing.docx","raw_xml":true}"""
        );

        var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
            service.CallAsync(
                "inspect_ooxml_bibliography",
                arguments.RootElement,
                CancellationToken.None
            )
        );

        Assert.Equal("INVALID_INPUT", exception.ErrorCode);
    }

    [Fact]
    public async Task InspectBibliographyRejectsMalformedSourceIdBeforePackageParsing()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-bibliography-source-id-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "bibliography.docx");
            File.WriteAllText(path, "not an OPC package", Encoding.UTF8);
            var service = new WordLiveService(new NoInvokeHost());
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                source_id = "not-a-source-id",
            }));

            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_bibliography",
                    arguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("INVALID_INPUT", exception.ErrorCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SensitiveFieldViewHasAggregateProjectedPayloadBudget()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-bibliography-response-budget-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "bibliography.docx");
            var extraFields = string.Concat(
                Enumerable.Range(0, 40).Select(index =>
                    $"<b:PrivateField{index}>{new string('x', 4_096)}</b:PrivateField{index}>"
                )
            );
            CreatePackage(path, extraFields: extraFields);
            var service = new WordLiveService(new NoInvokeHost());
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                view = "fields",
                include_sensitive = true,
                include_issues = false,
                max_items = 100,
            }));

            var result = await service.CallAsync(
                "inspect_ooxml_bibliography",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;
            var responseBudget = root.GetProperty("response_budget");

            Assert.True(root.GetProperty("response_budget_truncated").GetBoolean());
            Assert.True(
                responseBudget.GetProperty("used").GetInt32()
                    <= responseBudget.GetProperty("maximum").GetInt32()
            );
            Assert.True(
                root.GetProperty("returned_item_count").GetInt32()
                    < root.GetProperty("matched_item_count").GetInt32()
            );
            Assert.Equal(
                root.GetProperty("returned_item_count").GetInt32(),
                root.GetProperty("next_offset").GetInt32()
            );
            Assert.True(
                root.GetRawText().Length < 75_000,
                $"Bounded bibliography response is too large: {root.GetRawText().Length}"
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SourceViewRedactsTypedValuesAndUnknownSourceTypeByDefault()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-bibliography-redaction-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "bibliography.docx");
            CreatePackage(path, "PrivateExperimentalType");
            var service = new WordLiveService(new NoInvokeHost());
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                view = "sources",
            }));

            var result = await service.CallAsync(
                "inspect_ooxml_bibliography",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var source = Assert.Single(
                json.RootElement.GetProperty("items").EnumerateArray()
            );

            Assert.Equal("unknown", source.GetProperty("source_type_status").GetString());
            Assert.Equal(JsonValueKind.Null, source.GetProperty("source_type").ValueKind);
            Assert.Equal(JsonValueKind.Null, source.GetProperty("lcid").ValueKind);
            Assert.Equal(JsonValueKind.Null, source.GetProperty("year").ValueKind);
            var raw = json.RootElement.GetRawText();
            Assert.DoesNotContain("PrivateExperimentalType", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("Private bibliography title", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("HiddenSurname", raw, StringComparison.Ordinal);

            using var summaryArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path })
            );
            var summaryResult = await service.CallAsync(
                "inspect_ooxml_bibliography",
                summaryArguments.RootElement,
                CancellationToken.None
            );
            using var summaryJson = JsonDocument.Parse(
                JsonSerializer.Serialize(summaryResult)
            );
            Assert.Equal(
                "(unknown)",
                Assert.Single(
                    summaryJson.RootElement.GetProperty("items").EnumerateArray()
                ).GetProperty("source_type").GetString()
            );
            Assert.DoesNotContain(
                "PrivateExperimentalType",
                summaryJson.RootElement.GetRawText(),
                StringComparison.Ordinal
            );

            using var collectionArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                view = "collections",
            }));
            var collectionResult = await service.CallAsync(
                "inspect_ooxml_bibliography",
                collectionArguments.RootElement,
                CancellationToken.None
            );
            using var collectionJson = JsonDocument.Parse(
                JsonSerializer.Serialize(collectionResult)
            );
            var collection = Assert.Single(
                collectionJson.RootElement.GetProperty("items").EnumerateArray()
            );
            Assert.Equal(JsonValueKind.Null, collection.GetProperty("style_name").ValueKind);
            Assert.Equal(JsonValueKind.Null, collection.GetProperty("version").ValueKind);
            Assert.Equal(
                JsonValueKind.Null,
                collection.GetProperty("selected_style").ValueKind
            );
            Assert.DoesNotContain(
                "PrivateStyle",
                collectionJson.RootElement.GetRawText(),
                StringComparison.Ordinal
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectBibliographyMapsSharedOperationBudgetExhaustion()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-bibliography-budget-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "bibliography.docx");
            CreatePackage(path);
            var service = new WordLiveService(
                new NoInvokeHost(),
                () => new WordToolkit.Engine.Resources.WordOperationResourceLease(4_096)
            );
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path })
            );

            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "inspect_ooxml_bibliography",
                    arguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("PACKAGE_LIMIT", exception.ErrorCode);
            var details = JsonSerializer.Serialize(exception.Details);
            Assert.Contains("operation_budget", details, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CreatePackage(
        string path,
        string sourceType = "Book",
        string extraFields = ""
    )
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
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
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:fldSimple w:instr=" CITATION Secret2026 "><w:r><w:t>[1]</w:t></w:r></w:fldSimple></w:p></w:body></w:document>
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
        WriteEntry(
            archive,
            "customXml/item1.xml",
            $$"""
            <b:Sources xmlns:b="http://schemas.openxmlformats.org/officeDocument/2006/bibliography" SelectedStyle="\PrivateStyle.xsl" StyleName="APA" Version="6">
              <b:Source>
                <b:Tag>Secret2026</b:Tag><b:SourceType>{{sourceType}}</b:SourceType>
                <b:Guid>{6D86D06C-9022-4932-8D4C-84C2B0843381}</b:Guid><b:LCID>1045</b:LCID>
                <b:Author><b:Author><b:NameList>
                  <b:Person><b:Last>HiddenSurname</b:Last><b:First>PrivateName</b:First></b:Person>
                  <b:Person><b:Last>HiddenTwo</b:Last></b:Person>
                  <b:Person><b:Last>HiddenThree</b:Last></b:Person>
                  <b:Person><b:Last>HiddenFour</b:Last></b:Person>
                  <b:Person><b:Last>HiddenFive</b:Last></b:Person>
                </b:NameList></b:Author></b:Author>
                <b:Title>Private bibliography title</b:Title><b:Year>2026</b:Year><b:Publisher>Private Publisher</b:Publisher>{{extraFields}}
              </b:Source>
            </b:Sources>
            """
        );
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    private sealed class NoInvokeHost : IWordComHost
    {
        public Task<T> InvokeAsync<T>(
            Func<dynamic, T> operation,
            CancellationToken cancellationToken = default,
            bool launchIfMissing = false
        ) => throw new Xunit.Sdk.XunitException(
            "Package inspection must not invoke the Word COM host."
        );

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
