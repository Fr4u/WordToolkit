using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class PackageInspectionServiceTests
{
    [Fact]
    public async Task InspectPackageReturnsBoundedGraphWithoutStartingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-package-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "sample.docx");
            CreatePackage(path);
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                include_details = true,
                max_items = 10,
            }));
            var host = new NoInvokeHost();
            var service = new WordLiveService(host);

            var result = await service.CallAsync(
                "inspect_ooxml_package",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;

            Assert.True(root.GetProperty("structurally_valid").GetBoolean());
            Assert.True(root.GetProperty("word_document_detected").GetBoolean());
            Assert.True(root.GetProperty("valid_word_package").GetBoolean());
            Assert.Equal(1, root.GetProperty("part_count").GetInt32());
            Assert.Equal(1, root.GetProperty("relationship_count").GetInt32());
            Assert.Equal(
                "/word/document.xml",
                root.GetProperty("office_document_part").GetString()
            );
            Assert.Equal(
                1,
                root.GetProperty("details").GetProperty("parts").GetArrayLength()
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectSemanticsReturnsStableBoundedOutlineWithoutStartingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-semantic-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "semantic.docx");
            CreatePackage(path);
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                max_nodes = 10,
                text_preview_chars = 5,
                include_source_paths = true,
            }));
            var service = new WordLiveService(new NoInvokeHost());

            var result = await service.CallAsync(
                "inspect_ooxml_semantics",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;

            Assert.True(root.GetProperty("semantic_node_count").GetInt32() >= 8);
            Assert.Equal(1, root.GetProperty("projected_part_count").GetInt32());
            Assert.Equal(
                "/word/document.xml",
                Assert.Single(
                    root.GetProperty("projected_part_uris").EnumerateArray()
                ).GetString()
            );
            Assert.Equal(
                1,
                root.GetProperty("node_counts").GetProperty("paragraph").GetInt32()
            );
            Assert.Equal(
                1,
                root.GetProperty("node_counts").GetProperty("equation").GetInt32()
            );
            var outline = root.GetProperty("outline");
            Assert.Contains(
                outline.EnumerateArray(),
                node => node.GetProperty("kind").GetString() == "paragraph"
                    && node.GetProperty("text_preview").GetString()!.Contains(
                        "Hello",
                        StringComparison.Ordinal
                    )
                    && node.GetProperty("text_preview_truncated").GetBoolean()
            );
            Assert.All(
                outline.EnumerateArray(),
                node =>
                {
                    Assert.StartsWith(
                        "wdn_",
                        node.GetProperty("node_id").GetString(),
                        StringComparison.Ordinal
                    );
                    Assert.True(
                        node.GetProperty("source_element_ordinal").GetInt32() >= 0
                    );
                }
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task QuerySemanticsReturnsTextNodeLocatorWithoutStartingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-query-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "query.docx");
            CreatePackage(path);
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                kinds = new[] { "text" },
                text = "hello",
                text_match = "contains",
                case_sensitive = false,
                max_results = 10,
                text_preview_chars = 20,
                include_source = true,
            }));
            var service = new WordLiveService(new NoInvokeHost());

            var result = await service.CallAsync(
                "query_ooxml_semantics",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;
            var match = Assert.Single(root.GetProperty("matches").EnumerateArray());

            Assert.Equal(1, root.GetProperty("matched_node_count").GetInt32());
            Assert.Equal(1, root.GetProperty("returned_node_count").GetInt32());
            Assert.Equal("text", match.GetProperty("kind").GetString());
            Assert.Equal("Hello ", match.GetProperty("text_preview").GetString());
            Assert.StartsWith(
                "wdn_",
                match.GetProperty("node_id").GetString(),
                StringComparison.Ordinal
            );
            Assert.Equal(
                "/word/document.xml",
                match.GetProperty("source_part_uri").GetString()
            );
            Assert.True(match.GetProperty("source_element_ordinal").GetInt32() >= 0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task QueryPlanAndApplyCanTargetAHeaderStoryWithoutStartingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-header-story-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "header.docx");
            CreatePackage(path, headerText: "Header token");
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var mainBytes = before.Parts["/word/document.xml"].Entry.Content.ToArray();
            var service = new WordLiveService(new NoInvokeHost());
            using var queryArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                kinds = new[] { "text" },
                text = "Header token",
                source_part_uri = "/word/header1.xml",
                include_source = true,
            }));

            var queryObject = await service.CallAsync(
                "query_ooxml_semantics",
                queryArguments.RootElement,
                CancellationToken.None
            );
            using var queryJson = JsonDocument.Parse(JsonSerializer.Serialize(queryObject));
            var query = queryJson.RootElement;
            var match = Assert.Single(query.GetProperty("matches").EnumerateArray());
            var nodeId = match.GetProperty("node_id").GetString();
            Assert.Equal(
                "/word/header1.xml",
                match.GetProperty("source_part_uri").GetString()
            );
            var commands = new[]
            {
                new
                {
                    node_id = nodeId,
                    new_text = "Changed header",
                    expected_text = "Header token",
                },
            };
            using var planArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                commands,
            }));
            var planObject = await service.CallAsync(
                "plan_ooxml_text_edits",
                planArguments.RootElement,
                CancellationToken.None
            );
            using var planJson = JsonDocument.Parse(JsonSerializer.Serialize(planObject));
            var planId = planJson.RootElement.GetProperty("plan_id").GetString();
            using var applyArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                expected_plan_id = planId,
                commands,
                keep_backup = false,
            }));

            var applyObject = await service.CallAsync(
                "apply_ooxml_text_edits",
                applyArguments.RootElement,
                CancellationToken.None
            );
            using var applyJson = JsonDocument.Parse(JsonSerializer.Serialize(applyObject));
            var after = reader.Read(path);

            Assert.True(applyJson.RootElement.GetProperty("applied").GetBoolean());
            Assert.Equal(
                mainBytes,
                after.Parts["/word/document.xml"].Entry.Content.ToArray()
            );
            Assert.Contains(
                new WordSemanticProjector().Project(after).Nodes,
                node => node.Kind == WordSemanticNodeKind.Text
                    && node.Text == "Changed header"
                    && node.SourcePartUri == "/word/header1.xml"
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectSectionsResolvesEffectiveHeaderBindingsWithoutStartingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-section-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "sections.docx");
            CreatePackage(path, headerText: "Section header");
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                binding_detail = "full",
                include_properties = true,
                include_story_part_uris = true,
            }));

            var result = await new WordLiveService(new NoInvokeHost()).CallAsync(
                "inspect_ooxml_sections",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;
            var section = Assert.Single(root.GetProperty("sections").EnumerateArray());
            var bindings = section.GetProperty("bindings");
            var defaultHeader = bindings.EnumerateArray().Single(binding =>
                binding.GetProperty("slot").GetString() == "header_default"
            );
            var firstHeader = bindings.EnumerateArray().Single(binding =>
                binding.GetProperty("slot").GetString() == "header_first"
            );

            Assert.Equal(1, root.GetProperty("section_count").GetInt32());
            Assert.False(root.GetProperty("even_and_odd_headers").GetBoolean());
            Assert.Equal(1, root.GetProperty("referenced_story_part_count").GetInt32());
            Assert.Equal(0, root.GetProperty("unbound_story_part_count").GetInt32());
            Assert.StartsWith(
                "wdn_",
                section.GetProperty("node_id").GetString(),
                StringComparison.Ordinal
            );
            Assert.Equal("nextPage", section.GetProperty("break_type").GetString());
            Assert.Equal("explicit", defaultHeader.GetProperty("origin").GetString());
            Assert.Equal(
                "/word/header1.xml",
                defaultHeader.GetProperty("effective_part_uri").GetString()
            );
            Assert.False(firstHeader.GetProperty("enabled").GetBoolean());
            Assert.Equal("blank", firstHeader.GetProperty("origin").GetString());
            Assert.Equal(
                "default",
                firstHeader.GetProperty("display_fallback_variant").GetString()
            );
            Assert.Equal(
                "/word/header1.xml",
                firstHeader.GetProperty("effective_part_uri").GetString()
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectStylesPagesTypedMetadataWithoutStartingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-style-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "styles.docx");
            CreatePackage(
                path,
                stylesXml: """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:docDefaults><w:rPrDefault><w:rPr><w:sz w:val="22"/></w:rPr></w:rPrDefault></w:docDefaults>
                  <w:latentStyles w:count="1"><w:lsdException w:name="Normal" w:qFormat="1"/></w:latentStyles>
                  <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
                  <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="Heading 1"/><w:basedOn w:val="Normal"/><w:pPr><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:b/></w:rPr></w:style>
                </w:styles>
                """
            );
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                style_type = "paragraph",
                detail = "inheritance",
                include_document_defaults = true,
                include_latent_styles = true,
            }));

            var result = await new WordLiveService(new NoInvokeHost()).CallAsync(
                "inspect_ooxml_styles",
                arguments.RootElement,
                CancellationToken.None
            );
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = json.RootElement;
            var styles = root.GetProperty("styles").EnumerateArray().ToArray();
            var heading = styles.Single(style =>
                style.GetProperty("style_id").GetString() == "Heading1"
            );

            Assert.True(root.GetProperty("has_styles_part").GetBoolean());
            Assert.Equal(2, root.GetProperty("style_count").GetInt32());
            Assert.Equal(2, root.GetProperty("matched_style_count").GetInt32());
            Assert.Equal(
                "Normal",
                root.GetProperty("default_style_ids")
                    .GetProperty("paragraph")
                    .GetString()
            );
            Assert.Equal(
                "22",
                root.GetProperty("document_defaults")
                    .GetProperty("run")
                    .GetProperty("values")
                    .GetProperty("size_half_points")
                    .GetString()
            );
            Assert.Equal(
                ["Normal", "Heading1"],
                heading.GetProperty("inheritance_chain_style_ids")
                    .EnumerateArray()
                    .Select(value => value.GetString()!)
                    .ToArray()
            );
            Assert.Equal(
                "0",
                heading.GetProperty("declared_properties")
                    .GetProperty("paragraph")
                    .GetProperty("values")
                    .GetProperty("outline_level")
                    .GetString()
            );
            Assert.Equal(
                1,
                root.GetProperty("latent_styles")
                    .GetProperty("exception_count")
                    .GetInt32()
            );
            Assert.Equal(0, root.GetProperty("issue_count").GetInt32());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PlansAndAtomicallyAppliesReviewedTextEditWithoutStartingWord()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-text-transaction-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "edit.docx");
            CreatePackage(path);
            var beforeFile = File.ReadAllBytes(path);
            var reader = new OpcPackageReader();
            var before = reader.Read(path);
            var semantic = new WordSemanticProjector().Project(before);
            var text = semantic.Nodes.Single(node =>
                node.Kind == WordSemanticNodeKind.Text && node.Text == "Hello "
            );
            var commands = new[]
            {
                new
                {
                    node_id = text.Id.Value,
                    new_text = " Changed & safe ",
                    expected_text = "Hello ",
                },
            };
            using var planArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                commands,
                include_details = true,
            }));
            var service = new WordLiveService(new NoInvokeHost());

            var plannedObject = await service.CallAsync(
                "plan_ooxml_text_edits",
                planArguments.RootElement,
                CancellationToken.None
            );
            using var plannedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(plannedObject)
            );
            var planned = plannedJson.RootElement;
            var planId = planned.GetProperty("plan_id").GetString()!;
            var predicted = planned
                .GetProperty("result_package_fingerprint")
                .GetString();

            Assert.Equal(beforeFile, File.ReadAllBytes(path));
            Assert.False(planned.GetProperty("apply_blocked").GetBoolean());
            Assert.True(planned.GetProperty("has_changes").GetBoolean());
            Assert.Equal(1, planned.GetProperty("operation_count").GetInt32());
            Assert.Equal(1, planned.GetProperty("changed_part_count").GetInt32());
            Assert.Single(planned.GetProperty("operations").EnumerateArray());

            using var applyArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = before.Fingerprint,
                expected_plan_id = planId,
                commands,
                keep_backup = true,
            }));
            var appliedObject = await service.CallAsync(
                "apply_ooxml_text_edits",
                applyArguments.RootElement,
                CancellationToken.None
            );
            using var appliedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(appliedObject)
            );
            var applied = appliedJson.RootElement;
            var backupPath = applied.GetProperty("backup_path").GetString();

            Assert.True(applied.GetProperty("applied").GetBoolean());
            Assert.False(applied.GetProperty("no_op").GetBoolean());
            Assert.Equal(predicted, applied.GetProperty("package_fingerprint").GetString());
            Assert.Equal(
                predicted,
                applied.GetProperty("predicted_package_fingerprint").GetString()
            );
            Assert.NotNull(backupPath);
            Assert.True(File.Exists(backupPath));
            Assert.Equal(before.Fingerprint, reader.Read(backupPath!).Fingerprint);
            var after = reader.Read(path);
            Assert.Equal(predicted, after.Fingerprint);
            Assert.Equal(
                " Changed & safe ",
                new WordSemanticProjector().Project(after).Nodes.Single(node =>
                    node.Kind == WordSemanticNodeKind.Text
                    && node.Text == " Changed & safe "
                ).Text
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyRejectsMismatchedPlanAndLeavesFileUntouched()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-plan-mismatch-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "mismatch.docx");
            CreatePackage(path);
            var bytes = File.ReadAllBytes(path);
            var package = new OpcPackageReader().Read(path);
            var text = new WordSemanticProjector().Project(package).Nodes.Single(node =>
                node.Kind == WordSemanticNodeKind.Text && node.Text == "Hello "
            );
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                expected_plan_id = "wplan_AAAAAAAAAAAAAAAAAAAA",
                commands = new[]
                {
                    new { node_id = text.Id.Value, new_text = "changed" },
                },
            }));

            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                new WordLiveService(new NoInvokeHost()).CallAsync(
                    "apply_ooxml_text_edits",
                    arguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("PLAN_MISMATCH", exception.ErrorCode);
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(directory, "*.bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyRejectsDigitallySignedPackageAndLeavesItUntouched()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-signed-package-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "signed.docx");
            CreatePackage(path, signed: true);
            var bytes = File.ReadAllBytes(path);
            var package = new OpcPackageReader().Read(path);
            var text = new WordSemanticProjector().Project(package).Nodes.Single(node =>
                node.Kind == WordSemanticNodeKind.Text && node.Text == "Hello "
            );
            var commands = new[]
            {
                new { node_id = text.Id.Value, new_text = "changed" },
            };
            var service = new WordLiveService(new NoInvokeHost());
            using var planArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                commands,
            }));
            var planObject = await service.CallAsync(
                "plan_ooxml_text_edits",
                planArguments.RootElement,
                CancellationToken.None
            );
            using var planJson = JsonDocument.Parse(JsonSerializer.Serialize(planObject));
            Assert.True(planJson.RootElement.GetProperty("apply_blocked").GetBoolean());
            var planId = planJson.RootElement.GetProperty("plan_id").GetString();
            using var applyArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                expected_plan_id = planId,
                commands,
            }));

            var exception = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "apply_ooxml_text_edits",
                    applyArguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("SIGNED_PACKAGE", exception.ErrorCode);
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReviewedNoOpDoesNotRewriteFileOrCreateBackup()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-native-noop-text-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "noop.docx");
            CreatePackage(path);
            var bytes = File.ReadAllBytes(path);
            var package = new OpcPackageReader().Read(path);
            var text = new WordSemanticProjector().Project(package).Nodes.Single(node =>
                node.Kind == WordSemanticNodeKind.Text && node.Text == "Hello "
            );
            var commands = new[]
            {
                new
                {
                    node_id = text.Id.Value,
                    new_text = "Hello ",
                    expected_text = "Hello ",
                },
            };
            var service = new WordLiveService(new NoInvokeHost());
            using var planArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                commands,
            }));
            var planObject = await service.CallAsync(
                "plan_ooxml_text_edits",
                planArguments.RootElement,
                CancellationToken.None
            );
            using var planJson = JsonDocument.Parse(JsonSerializer.Serialize(planObject));
            var planId = planJson.RootElement.GetProperty("plan_id").GetString();
            Assert.False(planJson.RootElement.GetProperty("has_changes").GetBoolean());
            using var applyArguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                local_path = path,
                expected_package_fingerprint = package.Fingerprint,
                expected_plan_id = planId,
                commands,
            }));

            var applyObject = await service.CallAsync(
                "apply_ooxml_text_edits",
                applyArguments.RootElement,
                CancellationToken.None
            );
            using var applyJson = JsonDocument.Parse(JsonSerializer.Serialize(applyObject));

            Assert.False(applyJson.RootElement.GetProperty("applied").GetBoolean());
            Assert.True(applyJson.RootElement.GetProperty("no_op").GetBoolean());
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(directory, "*.bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CreatePackage(
        string path,
        bool signed = false,
        string? headerText = null,
        string? stylesXml = null
    )
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var signatureOverride = signed
            ? "<Override PartName=\"/_xmlsignatures/sig1.xml\" ContentType=\"application/vnd.openxmlformats-package.digital-signature-xmlsignature+xml\" />"
            : string.Empty;
        var headerOverride = headerText is null
            ? string.Empty
            : "<Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\" />";
        var stylesOverride = stylesXml is null
            ? string.Empty
            : "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\" />";
        WriteEntry(
            archive,
            "[Content_Types].xml",
            $"""
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
              <Default Extension="xml" ContentType="application/xml" />
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" />
              {signatureOverride}
              {headerOverride}
              {stylesOverride}
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
        var headerReference = headerText is null
            ? string.Empty
            : "<w:sectPr><w:headerReference r:id=\"rIdHeader\" w:type=\"default\" /></w:sectPr>";
        WriteEntry(
            archive,
            "word/document.xml",
            $"""
            <w:document
                xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml"
                xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math"
                xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body>
                <w:p w14:paraId="00112233">
                  <w:r><w:t>Hello </w:t></w:r>
                  <m:oMath><m:f><m:num><m:r><m:t>a</m:t></m:r></m:num><m:den><m:r><m:t>b</m:t></m:r></m:den></m:f></m:oMath>
                </w:p>
                {headerReference}
              </w:body>
            </w:document>
            """
        );
        if (headerText is not null || stylesXml is not null)
        {
            var headerRelationship = headerText is null
                ? string.Empty
                : "<Relationship Id=\"rIdHeader\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header1.xml\" />";
            var stylesRelationship = stylesXml is null
                ? string.Empty
                : "<Relationship Id=\"rIdStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\" />";
            WriteEntry(
                archive,
                "word/_rels/document.xml.rels",
                $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  {headerRelationship}
                  {stylesRelationship}
                </Relationships>
                """
            );
        }
        if (headerText is not null)
        {
            WriteEntry(
                archive,
                "word/header1.xml",
                $"""
                <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:p><w:r><w:t>{headerText}</w:t></w:r></w:p>
                </w:hdr>
                """
            );
        }
        if (stylesXml is not null)
        {
            WriteEntry(archive, "word/styles.xml", stylesXml);
        }
        if (signed)
        {
            WriteEntry(
                archive,
                "_xmlsignatures/sig1.xml",
                "<Signature xmlns=\"http://www.w3.org/2000/09/xmldsig#\" />"
            );
        }
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
        )
        {
            throw new Xunit.Sdk.XunitException(
                "Package inspection must not invoke the Word COM host."
            );
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
