using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Engine.Packaging;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class MailMergePackageInspectionTests
{
    [Fact]
    public async Task DefaultInspectionIsCompactRedactedAndNeverStartsWordOrSource()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "mail-merge.docx");
            CreatePackage(path);
            var service = new WordLiveService(new NoInvokeHost());
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path })
            );

            var result = await service.CallAsync(
                "inspect_ooxml_mail_merge",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;

            Assert.Equal("summary", root.GetProperty("view").GetString());
            Assert.True(root.GetProperty("present").GetBoolean());
            Assert.Equal(1, root.GetProperty("configuration_count").GetInt32());
            Assert.Equal(2, root.GetProperty("mapping_count").GetInt32());
            Assert.Equal(2, root.GetProperty("recipient_count").GetInt32());
            Assert.Equal(2, root.GetProperty("field_count").GetInt32());
            Assert.False(root.GetProperty("word_opened").GetBoolean());
            Assert.False(root.GetProperty("mail_merge_executed").GetBoolean());
            Assert.False(root.GetProperty("data_sources_opened").GetBoolean());
            Assert.False(root.GetProperty("queries_executed").GetBoolean());
            Assert.False(root.GetProperty("external_targets_followed").GetBoolean());
            Assert.False(root.GetProperty("sensitive_values_included").GetBoolean());
            Assert.False(root.GetProperty("relationship_targets_included").GetBoolean());
            Assert.Equal(
                "process_hmac_sha256_64",
                root.GetProperty("fingerprint_scope").GetString()
            );
            Assert.Equal(4, root.GetProperty("items").GetArrayLength());
            Assert.Equal(
                "mail_merge_projected_payload_characters_v1",
                root.GetProperty("response_budget").GetProperty("model").GetString()
            );
            Assert.Equal(
                "wop1",
                root.GetProperty("operation_budget").GetProperty("model").GetString()
            );
            var raw = root.GetRawText();
            Assert.DoesNotContain("Provider=Sensitive", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("Clients$", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("CustomerId", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("identity-first", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("C:/private/clients.xlsx", raw, StringComparison.Ordinal);
            Assert.True(raw.Length < 6_000, $"Default response is too large: {raw.Length}");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExplicitViewsRevealOnlyTheRequestedSensitiveClasses()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "mail-merge.docx");
            CreatePackage(path);
            var service = new WordLiveService(new NoInvokeHost());

            using var configurationArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    local_path = path,
                    view = "configuration",
                    include_sensitive = true,
                    include_source = true,
                })
            );
            using var configurationJson = JsonDocument.Parse(JsonSerializer.Serialize(
                await service.CallAsync(
                    "inspect_ooxml_mail_merge",
                    configurationArguments.RootElement,
                    CancellationToken.None
                )
            ));
            var configuration = Assert.Single(
                configurationJson.RootElement.GetProperty("items").EnumerateArray()
            );
            Assert.Equal(
                "SELECT * FROM [Clients$]",
                configuration.GetProperty("query").GetString()
            );
            Assert.Equal(
                "Provider=Sensitive",
                configuration.GetProperty("connection_string").GetString()
            );
            Assert.Equal("/word/settings.xml", configuration.GetProperty("part_uri").GetString());
            var publicFingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes("Provider=Sensitive"))
            ).ToLowerInvariant()[..16];
            Assert.NotEqual(
                publicFingerprint,
                configuration.GetProperty("connection_string_fingerprint").GetString()
            );

            using var relationshipArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                view = "relationships",
                include_relationship_targets = true,
            }));
            using var relationshipJson = JsonDocument.Parse(JsonSerializer.Serialize(
                await service.CallAsync(
                    "inspect_ooxml_mail_merge",
                    relationshipArguments.RootElement,
                    CancellationToken.None
                )
            ));
            Assert.Contains(
                relationshipJson.RootElement.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("target").GetString()
                    == "file:///C:/private/clients.xlsx"
            );

            using var recipientArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                view = "recipients",
                include_sensitive = true,
            }));
            using var recipientJson = JsonDocument.Parse(JsonSerializer.Serialize(
                await service.CallAsync(
                    "inspect_ooxml_mail_merge",
                    recipientArguments.RootElement,
                    CancellationToken.None
                )
            ));
            Assert.Contains(
                recipientJson.RootElement.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("identity_value").GetString() == "identity-first"
            );

            using var fieldArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                view = "fields",
                include_sensitive = true,
            }));
            using var fieldJson = JsonDocument.Parse(JsonSerializer.Serialize(
                await service.CallAsync(
                    "inspect_ooxml_mail_merge",
                    fieldArguments.RootElement,
                    CancellationToken.None
                )
            ));
            Assert.Contains(
                fieldJson.RootElement.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("target_name").GetString() == "CustomerId"
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsUnknownArgumentsBeforeOpeningPackage()
    {
        var service = new WordLiveService(new NoInvokeHost());
        using var arguments = JsonDocument.Parse(
            """{"local_path":"missing.docx","execute_query":true}"""
        );

        var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
            service.CallAsync(
                "inspect_ooxml_mail_merge",
                arguments.RootElement,
                CancellationToken.None
            )
        );

        Assert.Equal("INVALID_INPUT", exception.ErrorCode);
    }

    [Fact]
    public async Task MapsSharedOperationBudgetExhaustion()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "mail-merge.docx");
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
                    "inspect_ooxml_mail_merge",
                    arguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("PACKAGE_LIMIT", exception.ErrorCode);
            Assert.Contains(
                "operation_budget",
                JsonSerializer.Serialize(exception.Details),
                StringComparison.Ordinal
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PublishedContractIsClosedPermissionedAndMatchesFullResponse()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "mail-merge.docx");
            CreatePackage(path);
            var catalog = ToolCatalog.LoadNativeWordTools();
            var tool = catalog.InspectAction("inspect_ooxml_mail_merge")["tool"]!
                .AsObject();
            Assert.Equal("1.0", tool["operationVersion"]!.GetValue<string>());
            Assert.Equal(
                "none",
                tool["permissions"]!["network"]!.GetValue<string>()
            );
            Assert.Equal(
                "none",
                tool["permissions"]!["microsoft_word"]!.GetValue<string>()
            );
            Assert.False(
                tool["inputSchema"]!["additionalProperties"]!.GetValue<bool>()
            );
            var service = new WordLiveService(new NoInvokeHost());
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { local_path = path })
            );
            var result = await service.CallAsync(
                "inspect_ooxml_mail_merge",
                arguments.RootElement,
                CancellationToken.None
            );
            var envelope = new JsonObject
            {
                ["ok"] = true,
                ["data"] = JsonSerializer.SerializeToNode(result, JsonDefaults.Compact),
            };
            var schema = tool["outputSchema"]!.AsObject();

            PublishedOutputSchemaAssertions.AssertConforms(
                envelope,
                schema,
                schema
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SchemaBindingPlanIsDeterministicRedactedAndNeverExecutesMerge()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "mail-merge.docx");
            CreatePackage(path);
            var fingerprint = new OpcPackageReader().Read(path).Fingerprint;
            var service = new WordLiveService(new NoInvokeHost());
            var request = new
            {
                local_path = path,
                expected_package_fingerprint = fingerprint,
                source_columns = new[]
                {
                    new { name = "CustomerId", data_kind = "number" },
                    new { name = "FirstName", data_kind = "text" },
                    new { name = "PrivateUnused", data_kind = "text" },
                },
            };
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(request));

            var first = await service.CallAsync(
                "plan_ooxml_mail_merge_schema_binding",
                arguments.RootElement,
                CancellationToken.None
            );
            var second = await service.CallAsync(
                "plan_ooxml_mail_merge_schema_binding",
                arguments.RootElement,
                CancellationToken.None
            );
            using var firstJson = JsonDocument.Parse(JsonSerializer.Serialize(first));
            using var secondJson = JsonDocument.Parse(JsonSerializer.Serialize(second));
            var root = firstJson.RootElement;

            Assert.Equal(
                root.GetProperty("plan_id").GetString(),
                secondJson.RootElement.GetProperty("plan_id").GetString()
            );
            Assert.True(root.GetProperty("can_bind_schema").GetBoolean());
            Assert.False(root.GetProperty("execution_supported").GetBoolean());
            Assert.False(root.GetProperty("contains_record_values").GetBoolean());
            Assert.False(root.GetProperty("input_record_values_accepted").GetBoolean());
            Assert.False(root.GetProperty("word_opened").GetBoolean());
            Assert.False(root.GetProperty("mail_merge_executed").GetBoolean());
            Assert.False(root.GetProperty("data_sources_opened").GetBoolean());
            Assert.False(root.GetProperty("queries_executed").GetBoolean());
            Assert.False(root.GetProperty("external_targets_followed").GetBoolean());
            Assert.True(root.GetProperty("external_source_ignored").GetBoolean());
            Assert.Equal(2, root.GetProperty("resolved_exact_count").GetInt32());
            Assert.Equal(1, root.GetProperty("unused_source_column_count").GetInt32());
            Assert.Equal(
                "execution_backend_not_implemented",
                Assert.Single(root.GetProperty("execution_blocked_reasons").EnumerateArray())
                    .GetString()
            );
            var raw = root.GetRawText();
            Assert.DoesNotContain("CustomerId", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("FirstName", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("PrivateUnused", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("Provider=Sensitive", raw, StringComparison.Ordinal);
            Assert.True(raw.Length < 5_000, $"Default plan response is too large: {raw.Length}");

            using var detailArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                request.local_path,
                request.expected_package_fingerprint,
                request.source_columns,
                view = "bindings",
                include_sensitive = true,
                include_source = true,
            }));
            using var detailJson = JsonDocument.Parse(JsonSerializer.Serialize(
                await service.CallAsync(
                    "plan_ooxml_mail_merge_schema_binding",
                    detailArguments.RootElement,
                    CancellationToken.None
                )
            ));
            var detailItems = detailJson.RootElement.GetProperty("items").EnumerateArray()
                .ToArray();
            Assert.Equal(2, detailItems.Length);
            Assert.Contains(
                detailItems,
                item => item.GetProperty("required_column_name").GetString()
                    == "CustomerId"
                    && item.GetProperty("source_column_ordinal").GetInt32() == 0
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SchemaBindingPlanRejectsStaleFingerprintsRecordValuesAndUnknownFields()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "mail-merge.docx");
            CreatePackage(path);
            var service = new WordLiveService(new NoInvokeHost());
            using var stale = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = new string('0', 64),
                source_columns = Array.Empty<object>(),
            }));
            var staleException = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "plan_ooxml_mail_merge_schema_binding",
                    stale.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("VERSION_CONFLICT", staleException.ErrorCode);

            using var forbidden = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = new string('0', 64),
                source_columns = Array.Empty<object>(),
                record_values = new[] { new { CustomerId = 1 } },
            }));
            var forbiddenException = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "plan_ooxml_mail_merge_schema_binding",
                    forbidden.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_INPUT", forbiddenException.ErrorCode);

            var fingerprint = new OpcPackageReader().Read(path).Fingerprint;
            using var invalidColumn = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = fingerprint,
                source_columns = new[] { new { name = "CustomerId", secret = "value" } },
            }));
            var invalidException = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "plan_ooxml_mail_merge_schema_binding",
                    invalidColumn.RootElement,
                    CancellationToken.None
                )
            );
            Assert.Equal("INVALID_INPUT", invalidException.ErrorCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SchemaBindingPlanContractIsClosedAndMatchesFullResponse()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "mail-merge.docx");
            CreatePackage(path);
            var fingerprint = new OpcPackageReader().Read(path).Fingerprint;
            var catalog = ToolCatalog.LoadNativeWordTools();
            var tool = catalog.InspectAction(
                "plan_ooxml_mail_merge_schema_binding"
            )["tool"]!.AsObject();
            Assert.Equal("1.0", tool["operationVersion"]!.GetValue<string>());
            Assert.Equal("none", tool["permissions"]!["network"]!.GetValue<string>());
            Assert.Equal(
                "none",
                tool["permissions"]!["microsoft_word"]!.GetValue<string>()
            );
            Assert.False(tool["inputSchema"]!["additionalProperties"]!.GetValue<bool>());
            var service = new WordLiveService(new NoInvokeHost());
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = fingerprint,
                source_columns = new[] { new { name = "CustomerId" }, new { name = "FirstName" } },
            }));
            var result = await service.CallAsync(
                "plan_ooxml_mail_merge_schema_binding",
                arguments.RootElement,
                CancellationToken.None
            );
            var envelope = new JsonObject
            {
                ["ok"] = true,
                ["data"] = JsonSerializer.SerializeToNode(result, JsonDefaults.Compact),
            };
            var schema = tool["outputSchema"]!.AsObject();

            PublishedOutputSchemaAssertions.AssertConforms(envelope, schema, schema);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RecordControlOperandsAreTypedAndComparisonValuesStayRedactedByDefault()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "mail-merge-controls.docx");
            CreatePackage(
                path,
                """
                <w:p><w:fldSimple w:instr=" NEXTIF CustomerId &gt;= &quot;private-threshold&quot; "><w:r><w:t>next</w:t></w:r></w:fldSimple></w:p>
                <w:p><w:fldSimple w:instr=" SKIPIF FirstName = &quot;&quot; "><w:r><w:t>skip</w:t></w:r></w:fldSimple></w:p>
                """
            );
            var fingerprint = new OpcPackageReader().Read(path).Fingerprint;
            var service = new WordLiveService(new NoInvokeHost());

            using var inspectArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                view = "fields",
            }));
            using var inspected = JsonDocument.Parse(JsonSerializer.Serialize(
                await service.CallAsync(
                    "inspect_ooxml_mail_merge",
                    inspectArguments.RootElement,
                    CancellationToken.None
                )
            ));
            var inspectedRaw = inspected.RootElement.GetRawText();
            Assert.DoesNotContain("private-threshold", inspectedRaw, StringComparison.Ordinal);
            Assert.Contains(
                inspected.RootElement.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("field_type").GetString() == "NEXTIF"
                    && item.GetProperty("control_kind").GetString() == "next_record_if"
                    && item.GetProperty("control_parse_status").GetString() == "complete"
                    && item.GetProperty("comparison_operator").GetString()
                        == "greater_than_or_equal"
                    && item.GetProperty("compare_to").ValueKind == JsonValueKind.Null
            );

            using var planArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = fingerprint,
                source_columns = new[]
                {
                    new { name = "CustomerId", data_kind = "number" },
                    new { name = "FirstName", data_kind = "text" },
                },
                view = "bindings",
                include_sensitive = true,
            }));
            using var planned = JsonDocument.Parse(JsonSerializer.Serialize(
                await service.CallAsync(
                    "plan_ooxml_mail_merge_schema_binding",
                    planArguments.RootElement,
                    CancellationToken.None
                )
            ));
            Assert.True(planned.RootElement.GetProperty("can_bind_schema").GetBoolean());
            Assert.Contains(
                planned.RootElement.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("field_type").GetString() == "NEXTIF"
                    && item.GetProperty("compare_to").GetString() == "private-threshold"
                    && item.GetProperty("comparison_operator").GetString()
                        == "greater_than_or_equal"
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-mail-merge-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        return directory;
    }

    internal static void CreatePackage(string path, string additionalDocumentBody = "")
    {
        const string w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        const string r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(
            archive,
            "[Content_Types].xml",
            """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/>
              <Override PartName="/word/recipients.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.mailMergeRecipientData+xml"/>
            </Types>
            """
        );
        Write(
            archive,
            "_rels/.rels",
            """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdRoot" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """
        );
        Write(
            archive,
            "word/document.xml",
            $"""
            <w:document xmlns:w="{w}"><w:body>
              {Field("CustomerId")}
              {Field("FirstName")}
              {additionalDocumentBody}
              <w:sectPr/>
            </w:body></w:document>
            """
        );
        Write(
            archive,
            "word/_rels/document.xml.rels",
            """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdSettings" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings" Target="settings.xml"/>
            </Relationships>
            """
        );
        Write(
            archive,
            "word/settings.xml",
            $"""
            <w:settings xmlns:w="{w}" xmlns:r="{r}"><w:mailMerge>
              <w:mainDocumentType w:val="formLetters"/><w:dataType w:val="database"/>
              <w:destination w:val="newDocument"/><w:linkToQuery w:val="1"/>
              <w:query w:val="SELECT * FROM [Clients$]"/>
              <w:connectString w:val="Provider=Sensitive"/>
              <w:dataSource r:id="rIdData"/>
              <w:odso>
                <w:udl w:val="Provider=Sensitive;Password=Secret"/><w:table w:val="Clients$"/>
                <w:src r:id="rIdOdso"/><w:fHdr w:val="1"/>
                <w:fieldMapData><w:type w:val="dbColumn"/><w:name w:val="CustomerId"/><w:mappedName w:val="PrivateMapped"/><w:column w:val="0"/></w:fieldMapData>
                <w:fieldMapData><w:type w:val="dbColumn"/><w:name w:val="FirstName"/><w:mappedName w:val="CourtesyTitle"/><w:column w:val="1"/></w:fieldMapData>
                <w:recipientData r:id="rIdRecipients"/>
              </w:odso>
            </w:mailMerge></w:settings>
            """
        );
        Write(
            archive,
            "word/_rels/settings.xml.rels",
            """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdData" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/mailMergeSource" Target="file:///C:/private/clients.xlsx" TargetMode="External"/>
              <Relationship Id="rIdOdso" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/mailMergeSource" Target="file:///C:/private/clients.xlsx" TargetMode="External"/>
              <Relationship Id="rIdRecipients" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/recipientData" Target="recipients.xml"/>
            </Relationships>
            """
        );
        Write(
            archive,
            "word/recipients.xml",
            $"""
            <w:recipients xmlns:w="{w}">
              <w:recipientData><w:active w:val="1"/><w:column w:val="0"/><w:uniqueTag w:val="identity-first"/></w:recipientData>
              <w:recipientData><w:active w:val="0"/><w:column w:val="1"/><w:hash w:val="identity-second"/></w:recipientData>
            </w:recipients>
            """
        );
    }

    private static string Field(string name) =>
        $"""
        <w:p><w:r><w:fldChar w:fldCharType="begin"/></w:r><w:r><w:instrText xml:space="preserve"> MERGEFIELD "{name}" </w:instrText></w:r><w:r><w:fldChar w:fldCharType="separate"/></w:r><w:r><w:t>{name}</w:t></w:r><w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>
        """;

    private static void Write(ZipArchive archive, string name, string content)
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
