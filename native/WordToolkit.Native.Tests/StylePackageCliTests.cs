using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;
using WordToolkit.OpenXmlSdk;

namespace WordToolkit.Native.Tests;

public sealed class StylePackageCliTests
{
    [Fact]
    public async Task EngineCliAndMcpShareTheSameStylePlanContract()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "parity.docx");
            CreatePackage(path);
            var package = new OpcPackageReader().Read(path);
            var requestJson = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                commands = new[]
                {
                    new
                    {
                        type = "clone_style",
                        source_style_id = "Definition",
                        style_id = "DefinitionClone",
                        name = "Definition clone",
                    },
                },
                include_details = true,
            });
            var request = StyleEditOperationJson.ParsePlanRequest(requestJson);
            var engine = new StyleWordPackageOperation(
                new MicrosoftOpenXmlPackageValidator()
            ).Plan(request);

            var output = new StringWriter();
            var error = new StringWriter();
            var cliExit = StylePackageCli.Run(
                ["--mode", "plan", "--request", "-", "--format", "json"],
                new StringReader(requestJson),
                output,
                error
            );
            Assert.Equal(0, cliExit);
            Assert.Equal(string.Empty, error.ToString());
            using var cliJson = JsonDocument.Parse(output.ToString());

            var host = new NoInvokeHost();
            var service = new WordLiveService(host);
            using var arguments = JsonDocument.Parse(requestJson);
            var mcpObject = await service.CallAsync(
                StyleWordPackageContract.PlanOperationName,
                arguments.RootElement,
                CancellationToken.None
            );
            using var mcpJson = JsonDocument.Parse(JsonSerializer.Serialize(mcpObject));

            foreach (var field in new[]
            {
                "operation_contract",
                "plan_id",
                "base_package_fingerprint",
                "result_package_fingerprint",
                "operation_count",
                "changed_part_count",
                "can_apply",
            })
            {
                Assert.Equal(
                    cliJson.RootElement.GetProperty(field).GetRawText(),
                    mcpJson.RootElement.GetProperty(field).GetRawText()
                );
            }
            Assert.Equal(engine.PlanId, cliJson.RootElement.GetProperty("plan_id").GetString());
            Assert.Equal(
                engine.ResultPackageFingerprint,
                cliJson.RootElement.GetProperty("result_package_fingerprint").GetString()
            );
            var planEnvelope = new JsonObject
            {
                ["ok"] = true,
                ["data"] = ToolResponseCompactor.Compact(
                    StyleWordPackageContract.PlanOperationName,
                    mcpObject
                ),
            };
            var planSchema = ToolCatalog
                .LoadNativeWordTools()
                .InspectAction(StyleWordPackageContract.PlanOperationName)["tool"]!["outputSchema"]!
                .AsObject();
            AssertConforms(planEnvelope, planSchema, "$");

            var applyRequestJson = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                expected_plan_id = engine.PlanId,
                commands = new[]
                {
                    new
                    {
                        type = "clone_style",
                        source_style_id = "Definition",
                        style_id = "DefinitionClone",
                        name = "Definition clone",
                    },
                },
                keep_backup = false,
            });
            using var applyArguments = JsonDocument.Parse(applyRequestJson);
            var appliedObject = await service.CallAsync(
                StyleWordPackageContract.ApplyOperationName,
                applyArguments.RootElement,
                CancellationToken.None
            );
            var applyEnvelope = new JsonObject
            {
                ["ok"] = true,
                ["data"] = ToolResponseCompactor.Compact(
                    StyleWordPackageContract.ApplyOperationName,
                    appliedObject
                ),
            };
            var applySchema = ToolCatalog
                .LoadNativeWordTools()
                .InspectAction(StyleWordPackageContract.ApplyOperationName)["tool"]!["outputSchema"]!
                .AsObject();
            AssertConforms(applyEnvelope, applySchema, "$");
            Assert.Equal(0, host.InvocationCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void JsonCliAppliesOnlyTheReviewedPlanAndUsesStableExitCodes()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "apply.docx");
            CreatePackage(path);
            var before = new OpcPackageReader().Read(path);
            var planJson = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                commands = new[]
                {
                    new
                    {
                        type = "rename_style",
                        style_id = "Definition",
                        name = "Definition renamed",
                    },
                },
            });
            var planOutput = new StringWriter();
            Assert.Equal(
                0,
                StylePackageCli.Run(
                    ["--mode", "plan", "--request", "-"],
                    new StringReader(planJson),
                    planOutput,
                    new StringWriter()
                )
            );
            using var plan = JsonDocument.Parse(planOutput.ToString());
            var applyJson = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                expected_plan_id = plan.RootElement.GetProperty("plan_id").GetString(),
                commands = new[]
                {
                    new
                    {
                        type = "rename_style",
                        style_id = "Definition",
                        name = "Definition renamed",
                    },
                },
                keep_backup = false,
            });
            var applyOutput = new StringWriter();
            var applyError = new StringWriter();
            Assert.Equal(
                0,
                StylePackageCli.Run(
                    ["--mode", "apply", "--request", "-"],
                    new StringReader(applyJson),
                    applyOutput,
                    applyError
                )
            );
            Assert.Equal(string.Empty, applyError.ToString());
            using var applied = JsonDocument.Parse(applyOutput.ToString());
            Assert.True(applied.RootElement.GetProperty("applied").GetBoolean());
            Assert.False(applied.RootElement.GetProperty("word_opened").GetBoolean());

            var staleError = new StringWriter();
            var staleExit = StylePackageCli.Run(
                ["--mode", "apply", "--request", "-"],
                new StringReader(applyJson),
                new StringWriter(),
                staleError
            );
            Assert.Equal(75, staleExit);
            using var stale = JsonDocument.Parse(staleError.ToString());
            Assert.Equal(
                "VERSION_CONFLICT",
                stale.RootElement.GetProperty("error").GetProperty("code").GetString()
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task McpAdapterPreservesBoundedSchemaFailureDiagnostics()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "invalid-style.docx");
            CreatePackage(path, invalidStyle: true);
            var package = new OpcPackageReader().Read(path);
            var commands = new[]
            {
                new
                {
                    type = "clone_style",
                    source_style_id = "Definition",
                    style_id = "DefinitionClone",
                    name = "Definition clone",
                },
            };
            var planJson = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                commands,
            });
            var service = new WordLiveService(new NoInvokeHost());
            using var planArguments = JsonDocument.Parse(planJson);
            var planObject = await service.CallAsync(
                StyleWordPackageContract.PlanOperationName,
                planArguments.RootElement,
                CancellationToken.None
            );
            var plan = JsonNode.Parse(JsonSerializer.Serialize(planObject))!.AsObject();
            var applyJson = JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                expected_plan_id = plan["plan_id"]!.GetValue<string>(),
                commands,
                keep_backup = false,
            });
            using var applyArguments = JsonDocument.Parse(applyJson);

            var failure = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    StyleWordPackageContract.ApplyOperationName,
                    applyArguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("OOXML_SCHEMA_INVALID", failure.ErrorCode);
            var detailsJson = JsonSerializer.Serialize(
                failure.Details,
                JsonDefaults.Compact
            );
            using var details = JsonDocument.Parse(detailsJson);
            Assert.True(details.RootElement.GetProperty("error_count").GetInt32() > 0);
            Assert.True(
                details.RootElement.GetProperty("candidate_error_count").GetInt32()
                > details.RootElement.GetProperty("baseline_error_count").GetInt32()
            );
            Assert.InRange(
                details.RootElement.GetProperty("issues").GetArrayLength(),
                1,
                20
            );
            Assert.Equal(package.Fingerprint, new OpcPackageReader().Read(path).Fingerprint);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CreatePackage(string path, bool invalidStyle = false)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "[Content_Types].xml",
            """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
            </Types>
            """
        );
        WriteEntry(
            archive,
            "_rels/.rels",
            $"""
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="{WordPackageConformance.TransitionalOfficeDocumentRelationship}" Target="word/document.xml"/>
            </Relationships>
            """
        );
        WriteEntry(
            archive,
            "word/document.xml",
            """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Alpha</w:t></w:r></w:p></w:body></w:document>
            """
        );
        WriteEntry(
            archive,
            "word/styles.xml",
            $"""
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
              <w:style w:type="paragraph" w:customStyle="1" w:styleId="Definition"><w:name w:val="Definition"/><w:basedOn w:val="Normal"/>{(invalidStyle ? "<w:bogus/>" : string.Empty)}</w:style>
            </w:styles>
            """
        );
        WriteEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """
        );
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var target = entry.Open();
        target.Write(Encoding.UTF8.GetBytes(content));
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-style-cli-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertConforms(
        JsonNode? actual,
        JsonObject schema,
        string path
    )
    {
        if (schema["const"] is { } constant)
        {
            Assert.True(
                JsonNode.DeepEquals(actual, constant),
                $"{path} does not equal its published const value"
            );
        }
        if (schema["enum"] is JsonArray allowed)
        {
            Assert.Contains(allowed, candidate => JsonNode.DeepEquals(actual, candidate));
        }

        var declaredType = schema["type"];
        if (declaredType is JsonArray typeArray && actual is null)
        {
            Assert.Contains(typeArray, item => item?.GetValue<string>() == "null");
            return;
        }
        var type = declaredType is JsonValue ? declaredType.GetValue<string>() : null;
        switch (type)
        {
            case "object":
                {
                    var value = Assert.IsType<JsonObject>(actual);
                    var properties = schema["properties"] as JsonObject ?? new JsonObject();
                    if (schema["required"] is JsonArray required)
                    {
                        foreach (var requiredProperty in required)
                        {
                            var name = requiredProperty!.GetValue<string>();
                            Assert.True(
                                value.ContainsKey(name),
                                $"{path} is missing required property '{name}'"
                            );
                        }
                    }
                    foreach (var property in value)
                    {
                        if (properties[property.Key] is JsonObject propertySchema)
                        {
                            AssertConforms(
                                property.Value,
                                propertySchema,
                                $"{path}.{property.Key}"
                            );
                            continue;
                        }
                        Assert.False(
                            schema["additionalProperties"]?.GetValue<bool>() == false,
                            $"{path} contains undeclared property '{property.Key}'"
                        );
                    }
                    break;
                }
            case "array":
                {
                    var value = Assert.IsType<JsonArray>(actual);
                    if (schema["maxItems"]?.GetValue<int>() is { } maxItems)
                    {
                        Assert.True(value.Count <= maxItems, $"{path} has too many items");
                    }
                    if (schema["uniqueItems"]?.GetValue<bool>() == true)
                    {
                        Assert.Equal(
                            value.Count,
                            value.Select(item => item?.ToJsonString() ?? "null")
                                .Distinct(StringComparer.Ordinal)
                                .Count()
                        );
                    }
                    if (schema["items"] is JsonObject itemSchema)
                    {
                        for (var index = 0; index < value.Count; index++)
                        {
                            AssertConforms(
                                value[index],
                                itemSchema,
                                $"{path}[{index}]"
                            );
                        }
                    }
                    break;
                }
            case "string":
                {
                    var value = actual!.GetValue<string>();
                    if (schema["maxLength"]?.GetValue<int>() is { } maxLength)
                    {
                        Assert.True(value.Length <= maxLength, $"{path} is too long");
                    }
                    if (schema["pattern"]?.GetValue<string>() is { } pattern)
                    {
                        Assert.Matches(new Regex(pattern, RegexOptions.CultureInvariant), value);
                    }
                    break;
                }
            case "integer":
                {
                    var value = actual!.GetValue<long>();
                    if (schema["minimum"]?.GetValue<long>() is { } minimum)
                    {
                        Assert.True(value >= minimum, $"{path} is below its minimum");
                    }
                    if (schema["maximum"]?.GetValue<long>() is { } maximum)
                    {
                        Assert.True(value <= maximum, $"{path} exceeds its maximum");
                    }
                    break;
                }
            case "number":
                _ = actual!.GetValue<double>();
                break;
            case "boolean":
                _ = actual!.GetValue<bool>();
                break;
        }
    }

    private sealed class NoInvokeHost : IWordComHost
    {
        public int InvocationCount { get; private set; }

        public Task<T> InvokeAsync<T>(
            Func<dynamic, T> operation,
            CancellationToken cancellationToken = default,
            bool launchIfMissing = false
        )
        {
            InvocationCount++;
            throw new Xunit.Sdk.XunitException(
                "Saved-package style operations must not invoke the Word COM host."
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
