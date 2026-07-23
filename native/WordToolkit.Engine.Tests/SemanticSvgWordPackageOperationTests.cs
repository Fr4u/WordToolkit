using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Rendering;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class SemanticSvgWordPackageOperationTests
{
    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string MathNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string RelationshipsNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    [Fact]
    public void RendersDeterministicAccessibleInertTableSvgWithoutMutatingSource()
    {
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "semantic.docx");
            var firstOutput = Path.Combine(directory, "first.svg");
            var secondOutput = Path.Combine(directory, "second.svg");
            CreatePackage(input);
            var sourceBytes = File.ReadAllBytes(input);
            var package = new OpcPackageReader().Read(input);
            var semantic = new WordSemanticProjector().Project(package);
            var table = Assert.Single(
                semantic.Nodes,
                node => node.Kind == WordSemanticNodeKind.Table
            );
            var operation = new SemanticSvgWordPackageOperation();

            var first = operation.Execute(
                new SemanticSvgWordPackageRequest(
                    input,
                    firstOutput,
                    package.Fingerprint,
                    table.Id.Value,
                    Language: "pl-PL",
                    ViewportWidthPx: 800
                )
            );
            var second = operation.Execute(
                new SemanticSvgWordPackageRequest(
                    input,
                    secondOutput,
                    package.Fingerprint,
                    table.Id.Value,
                    Language: "pl-PL",
                    ViewportWidthPx: 800
                )
            );

            Assert.Equal(sourceBytes, File.ReadAllBytes(input));
            Assert.Equal(File.ReadAllBytes(firstOutput), File.ReadAllBytes(secondOutput));
            Assert.Equal(first.ArtifactSha256, second.ArtifactSha256);
            Assert.Equal(SemanticSvgWordPackageContract.Contract, first.OperationContract);
            Assert.Equal("image/svg+xml", first.ArtifactMediaType);
            Assert.Equal("semantic_vector_preview_non_paginated", first.FidelityClass);
            Assert.Equal("semantic_flow_estimated", first.LayoutBasis);
            Assert.Equal("text", first.TextOutputMode);
            Assert.False(first.Paginated);
            Assert.False(first.ExactTextMetrics);
            Assert.False(first.PixelEquivalenceClaimed);
            Assert.True(first.SelectionApplied);
            Assert.Equal(table.Id.Value, first.TargetNodeId);
            Assert.Equal(table.SubtreeFingerprint, first.TargetSubtreeFingerprint);
            Assert.Equal(WordSemanticNodeKind.Table, first.TargetKind);
            Assert.Equal("main_document", first.TargetStoryKind);
            Assert.Equal(800, first.ViewportWidthPx);
            Assert.True(first.ViewportHeightPx > 0);
            Assert.Equal(1, first.TableCount);
            Assert.True(first.OutputCreated);
            Assert.False(first.SourceMutated);
            Assert.True(first.ArtifactContainsDocumentContent);
            Assert.False(first.ExternalResourcesLoaded);
            Assert.False(first.ActiveContentExecuted);
            Assert.False(first.RawXmlReturned);
            Assert.False(first.DocumentTextReturned);
            Assert.False(first.WordOpened);
            Assert.Contains("TABLE_GEOMETRY_APPROXIMATED", first.Warnings);
            Assert.Contains("TEXT_METRICS_ESTIMATED", first.Warnings);

            var document = XDocument.Load(firstOutput, LoadOptions.PreserveWhitespace);
            var root = Assert.IsType<XElement>(document.Root);
            Assert.Equal(Svg + "svg", root.Name);
            Assert.Equal("img", root.Attribute("role")?.Value);
            Assert.Equal("pl-PL", root.Attribute(XNamespace.Xml + "lang")?.Value);
            Assert.NotNull(root.Element(Svg + "title"));
            Assert.NotNull(root.Element(Svg + "desc"));
            Assert.NotEmpty(root.Descendants(Svg + "text"));
            Assert.NotEmpty(root.Descendants(Svg + "rect"));
            Assert.Contains(root.Descendants(Svg + "g"), element =>
                element.Attribute("role")?.Value == "cell"
                && element.Attribute("data-revision-kinds")?.Value == "insertion"
            );
            Assert.Contains("first cell", root.Value, StringComparison.Ordinal);
            Assert.Contains("second cell", root.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("paragraph sentinel", root.Value, StringComparison.Ordinal);
            Assert.Empty(root.Descendants(Svg + "script"));
            Assert.Empty(root.Descendants(Svg + "foreignObject"));
            Assert.DoesNotContain(
                root.DescendantsAndSelf().Attributes(),
                attribute =>
                    attribute.Name.LocalName.Equals("href", StringComparison.OrdinalIgnoreCase)
                    || attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                    || attribute.Value.Contains("https://", StringComparison.OrdinalIgnoreCase)
            );
            Assert.Contains("TABLE_REVISION_SPANS_FLATTENED", first.Warnings);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExactParagraphAndEquationTargetsDoNotRenderSiblingsOrFieldInstructions()
    {
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "targets.docx");
            CreatePackage(input);
            var package = new OpcPackageReader().Read(input);
            var semantic = new WordSemanticProjector().Project(package);
            var paragraph = Assert.Single(
                semantic.Nodes,
                node =>
                    node.Kind == WordSemanticNodeKind.Paragraph
                    && node.TextPreview().Contains("paragraph sentinel", StringComparison.Ordinal)
            );
            var equation = Assert.Single(
                semantic.Nodes,
                node => node.Kind == WordSemanticNodeKind.Equation
            );
            var fieldParagraph = Assert.Single(
                semantic.Nodes,
                node =>
                    node.Kind == WordSemanticNodeKind.Paragraph
                    && node.TextPreview().Contains("field result", StringComparison.Ordinal)
            );
            var hyperlinkParagraph = Assert.Single(
                semantic.Nodes,
                node =>
                    node.Kind == WordSemanticNodeKind.Paragraph
                    && node.TextPreview().Contains("inert link", StringComparison.Ordinal)
            );
            var operation = new SemanticSvgWordPackageOperation();
            var paragraphOutput = Path.Combine(directory, "paragraph.svg");
            var equationOutput = Path.Combine(directory, "equation.svg");
            var fieldOutput = Path.Combine(directory, "field.svg");
            var hyperlinkOutput = Path.Combine(directory, "hyperlink.svg");

            var paragraphResult = operation.Execute(
                new SemanticSvgWordPackageRequest(
                    input,
                    paragraphOutput,
                    package.Fingerprint,
                    paragraph.Id.Value
                )
            );
            var equationResult = operation.Execute(
                new SemanticSvgWordPackageRequest(
                    input,
                    equationOutput,
                    package.Fingerprint,
                    equation.Id.Value
                )
            );
            operation.Execute(
                new SemanticSvgWordPackageRequest(
                    input,
                    fieldOutput,
                    package.Fingerprint,
                    fieldParagraph.Id.Value
                )
            );
            operation.Execute(
                new SemanticSvgWordPackageRequest(
                    input,
                    hyperlinkOutput,
                    package.Fingerprint,
                    hyperlinkParagraph.Id.Value
                )
            );

            var paragraphSvg = XDocument.Load(paragraphOutput).Root!;
            Assert.Contains("paragraph sentinel", paragraphSvg.Value, StringComparison.Ordinal);
            Assert.Contains("<script>alert(1)</script>", paragraphSvg.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("first cell", paragraphSvg.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("SECRET-INSTRUCTION", paragraphSvg.Value, StringComparison.Ordinal);
            Assert.Equal(WordSemanticNodeKind.Paragraph, paragraphResult.TargetKind);

            var equationSvg = XDocument.Load(equationOutput).Root!;
            Assert.Contains("x+1", equationSvg.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("paragraph sentinel", equationSvg.Value, StringComparison.Ordinal);
            Assert.Equal(WordSemanticNodeKind.Equation, equationResult.TargetKind);
            Assert.Equal(1, equationResult.EquationCount);
            Assert.Contains("EQUATIONS_RENDERED_AS_LINEAR_TEXT", equationResult.Warnings);

            var fieldSvg = XDocument.Load(fieldOutput).Root!;
            Assert.Contains("field result", fieldSvg.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("SECRET-INSTRUCTION", fieldSvg.Value, StringComparison.Ordinal);

            var hyperlinkSvg = XDocument.Load(hyperlinkOutput).Root!;
            Assert.Contains("inert link", hyperlinkSvg.Value, StringComparison.Ordinal);
            Assert.Empty(hyperlinkSvg.Descendants(Svg + "script"));
            Assert.Empty(hyperlinkSvg.Descendants(Svg + "foreignObject"));
            Assert.DoesNotContain(
                hyperlinkSvg.DescendantsAndSelf().Attributes(),
                attribute =>
                    attribute.Name.LocalName.Equals("href", StringComparison.OrdinalIgnoreCase)
                    || attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                    || attribute.Value.Contains("example.invalid", StringComparison.OrdinalIgnoreCase)
                    || attribute.Value.Contains("https://", StringComparison.OrdinalIgnoreCase)
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TableRowAndCellTargetsHaveStableSvgTableRoles()
    {
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "table-targets.docx");
            CreatePackage(input);
            var package = new OpcPackageReader().Read(input);
            var semantic = new WordSemanticProjector().Project(package);
            var row = Assert.Single(
                semantic.Nodes,
                node => node.Kind == WordSemanticNodeKind.TableRow
            );
            var cell = semantic.Nodes.First(node =>
                node.Kind == WordSemanticNodeKind.TableCell
            );
            var operation = new SemanticSvgWordPackageOperation();

            var rowOutput = Path.Combine(directory, "row.svg");
            var rowResult = operation.Execute(
                new SemanticSvgWordPackageRequest(
                    input,
                    rowOutput,
                    package.Fingerprint,
                    row.Id.Value
                )
            );
            var rowSvg = XDocument.Load(rowOutput).Root!;
            Assert.Contains(rowSvg.Descendants(Svg + "g"), element =>
                element.Attribute("role")?.Value == "table"
            );
            Assert.Equal(2, rowSvg.Descendants(Svg + "g").Count(element =>
                element.Attribute("role")?.Value == "cell"
            ));
            Assert.Equal(WordSemanticNodeKind.TableRow, rowResult.TargetKind);

            var cellOutput = Path.Combine(directory, "cell.svg");
            var cellResult = operation.Execute(
                new SemanticSvgWordPackageRequest(
                    input,
                    cellOutput,
                    package.Fingerprint,
                    cell.Id.Value
                )
            );
            var cellSvg = XDocument.Load(cellOutput).Root!;
            Assert.Single(cellSvg.Descendants(Svg + "g"), element =>
                element.Attribute("role")?.Value == "cell"
            );
            Assert.Equal(WordSemanticNodeKind.TableCell, cellResult.TargetKind);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TrackedRevisionWarningIsBackedByRevisionMetadataInTheArtifact()
    {
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "revision.docx");
            var output = Path.Combine(directory, "revision.svg");
            CreatePackage(input);
            var package = new OpcPackageReader().Read(input);
            var target = Assert.Single(
                new WordSemanticProjector().Project(package).Nodes,
                node =>
                    node.Kind == WordSemanticNodeKind.Paragraph
                    && node.TextPreview().Contains("tracked insertion", StringComparison.Ordinal)
            );

            var result = new SemanticSvgWordPackageOperation().Execute(
                new SemanticSvgWordPackageRequest(
                    input,
                    output,
                    package.Fingerprint,
                    target.Id.Value
                )
            );

            var root = XDocument.Load(output).Root!;
            var revision = Assert.Single(root.Descendants(Svg + "g"), element =>
                element.Attribute("data-revision-kind")?.Value == "insertion"
            );
            Assert.Equal("group", revision.Attribute("role")?.Value);
            Assert.Contains("tracked insertion", revision.Value, StringComparison.Ordinal);
            Assert.Contains("TRACKED_REVISIONS_ANNOTATED", result.Warnings);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RejectsExcessiveTextDuringLayoutBeforeCreatingSvgTreeOrOutput()
    {
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "large.docx");
            var output = Path.Combine(directory, "large.svg");
            CreateTextPackage(input, new string('x', 1_500_000));
            var package = new OpcPackageReader().Read(input);
            var target = Assert.Single(
                new WordSemanticProjector().Project(package).Nodes,
                node => node.Kind == WordSemanticNodeKind.Paragraph
            );

            var exception = Assert.Throws<WordToolkitOperationException>(() =>
                new SemanticSvgWordPackageOperation().Execute(
                    new SemanticSvgWordPackageRequest(
                        input,
                        output,
                        package.Fingerprint,
                        target.Id.Value,
                        ViewportWidthPx: SemanticSvgWordPackageContract.MinimumViewportWidthPx
                    )
                )
            );

            Assert.Equal("PACKAGE_LIMIT", exception.Code);
            Assert.False(File.Exists(output));
            Assert.Empty(Directory.GetFiles(directory, ".wordtoolkit-render-*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RejectsUnboundStaleMissingOutOfScopeAndNonRenderableTargetsWithoutOutput()
    {
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "guards.docx");
            CreatePackage(input);
            var package = new OpcPackageReader().Read(input);
            var semantic = new WordSemanticProjector().Project(package);
            var paragraph = semantic.Nodes.First(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
                && node.SourcePartUri == semantic.MainPartUri
            );
            var headerParagraph = Assert.Single(
                semantic.Nodes,
                node =>
                    node.Kind == WordSemanticNodeKind.Paragraph
                    && node.SourcePartUri.EndsWith("header1.xml", StringComparison.Ordinal)
            );
            var drawing = Assert.Single(
                semantic.Nodes,
                node => node.Kind == WordSemanticNodeKind.Drawing
            );
            var operation = new SemanticSvgWordPackageOperation();

            AssertFailure(
                operation,
                new SemanticSvgWordPackageRequest(
                    input,
                    Path.Combine(directory, "bad-hash.svg"),
                    "bad",
                    paragraph.Id.Value
                ),
                "INVALID_INPUT"
            );
            AssertFailure(
                operation,
                new SemanticSvgWordPackageRequest(
                    input,
                    Path.Combine(directory, "stale.svg"),
                    new string('0', 64),
                    paragraph.Id.Value
                ),
                "VERSION_CONFLICT"
            );
            AssertFailure(
                operation,
                new SemanticSvgWordPackageRequest(
                    input,
                    Path.Combine(directory, "missing.svg"),
                    package.Fingerprint,
                    "wdn_missing"
                ),
                "TARGET_NOT_FOUND"
            );
            AssertFailure(
                operation,
                new SemanticSvgWordPackageRequest(
                    input,
                    Path.Combine(directory, "header.svg"),
                    package.Fingerprint,
                    headerParagraph.Id.Value
                ),
                "TARGET_OUT_OF_SCOPE"
            );
            AssertFailure(
                operation,
                new SemanticSvgWordPackageRequest(
                    input,
                    Path.Combine(directory, "drawing.svg"),
                    package.Fingerprint,
                    drawing.Id.Value
                ),
                "TARGET_NOT_RENDERABLE"
            );

            var allowed = operation.Execute(
                new SemanticSvgWordPackageRequest(
                    input,
                    Path.Combine(directory, "header-all.svg"),
                    package.Fingerprint,
                    headerParagraph.Id.Value,
                    SemanticRenderStoryScope.AllTextStories
                )
            );
            Assert.Equal("header", allowed.TargetStoryKind);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ParserAndFilesystemContractFailClosed()
    {
        Assert.Throws<JsonException>(() =>
            SemanticSvgWordPackageJson.ParseRequest(
                "{\"local_path\":\"a.docx\",\"output_path\":\"a.svg\",\"expected_package_fingerprint\":\""
                    + new string('0', 64)
                    + "\",\"target_node_id\":\"wdn_x\",\"execute_magic\":true}"
            )
        );

        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "contract.docx");
            CreatePackage(input);
            var package = new OpcPackageReader().Read(input);
            var target = new WordSemanticProjector()
                .Project(package)
                .Nodes.First(node => node.Kind == WordSemanticNodeKind.Paragraph);
            var operation = new SemanticSvgWordPackageOperation();
            AssertFailure(
                operation,
                new SemanticSvgWordPackageRequest(
                    input,
                    Path.Combine(directory, "wrong.html"),
                    package.Fingerprint,
                    target.Id.Value
                ),
                "INVALID_INPUT"
            );
            AssertFailure(
                operation,
                new SemanticSvgWordPackageRequest(
                    input,
                    Path.Combine(directory, "narrow.svg"),
                    package.Fingerprint,
                    target.Id.Value,
                    ViewportWidthPx: 319
                ),
                "INVALID_INPUT"
            );

            var existing = Path.Combine(directory, "existing.svg");
            File.WriteAllText(existing, "sentinel");
            AssertFailure(
                operation,
                new SemanticSvgWordPackageRequest(
                    input,
                    existing,
                    package.Fingerprint,
                    target.Id.Value
                ),
                "OUTPUT_EXISTS",
                outputMustNotExist: false
            );
            Assert.Equal("sentinel", File.ReadAllText(existing));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(@"\\server.invalid\share\document.docx")]
    [InlineData("//server.invalid/share/document.docx")]
    [InlineData(@"\\?\C:\document.docx")]
    [InlineData(@"\\.\pipe\wordtoolkit")]
    [InlineData(@"\??\UNC\server.invalid\share\document.docx")]
    public void SharedRenderPathPolicyRejectsNetworkAndDeviceNamespacesBeforeFilesystemIo(
        string unsafePath
    )
    {
        Assert.False(SemanticRenderPathPolicy.IsAllowedLocalPath(unsafePath));

        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "local.docx");
            CreatePackage(input);
            var package = new OpcPackageReader().Read(input);
            var target = new WordSemanticProjector()
                .Project(package)
                .Nodes.First(node => node.Kind == WordSemanticNodeKind.Paragraph);

            var htmlFailure = Assert.Throws<WordToolkitOperationException>(() =>
                new SemanticHtmlWordPackageOperation().Execute(
                    new SemanticHtmlWordPackageRequest(
                        unsafePath,
                        Path.Combine(directory, "safe.html")
                    )
                )
            );
            Assert.Equal("INVALID_INPUT", htmlFailure.Code);

            var unsafeSvgPath = unsafePath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
                ? unsafePath[..^5] + ".svg"
                : unsafePath + ".svg";
            var svgFailure = Assert.Throws<WordToolkitOperationException>(() =>
                new SemanticSvgWordPackageOperation().Execute(
                    new SemanticSvgWordPackageRequest(
                        input,
                        unsafeSvgPath,
                        package.Fingerprint,
                        target.Id.Value
                    )
                )
            );
            Assert.Equal("INVALID_INPUT", svgFailure.Code);
            Assert.False(File.Exists(Path.Combine(directory, "safe.html")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertFailure(
        SemanticSvgWordPackageOperation operation,
        SemanticSvgWordPackageRequest request,
        string expectedCode,
        bool outputMustNotExist = true
    )
    {
        var exception = Assert.Throws<WordToolkitOperationException>(() =>
            operation.Execute(request)
        );
        Assert.Equal(expectedCode, exception.Code);
        if (outputMustNotExist)
        {
            Assert.False(File.Exists(request.OutputPath));
        }
    }

    private static void CreatePackage(string path)
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
              <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
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
            $"""
            <w:document xmlns:w="{WordNamespace}" xmlns:m="{MathNamespace}" xmlns:r="{RelationshipsNamespace}">
              <w:body>
                <w:p><w:r><w:t>paragraph sentinel &lt;script&gt;alert(1)&lt;/script&gt;</w:t></w:r></w:p>
                <w:p>
                  <w:r><w:fldChar w:fldCharType="begin"/></w:r>
                  <w:r><w:instrText>SECRET-INSTRUCTION</w:instrText></w:r>
                  <w:r><w:fldChar w:fldCharType="separate"/></w:r>
                  <w:r><w:t>field result</w:t></w:r>
                  <w:r><w:fldChar w:fldCharType="end"/></w:r>
                </w:p>
                <w:p><w:hyperlink r:id="rIdExternal"><w:r><w:t>inert link</w:t></w:r></w:hyperlink></w:p>
                <w:p><w:ins w:id="7" w:author="Tester"><w:r><w:t>tracked insertion</w:t></w:r></w:ins></w:p>
                <w:p><m:oMath><m:r><m:t>x</m:t></m:r><m:r><m:t>+1</m:t></m:r></m:oMath></w:p>
                <w:p><w:r><w:drawing/></w:r></w:p>
                <w:tbl><w:tr>
                  <w:tc><w:p><w:ins w:id="8" w:author="Tester"><w:r><w:t>first cell</w:t></w:r></w:ins></w:p></w:tc>
                  <w:tc><w:p><w:r><w:t>second cell</w:t></w:r></w:p></w:tc>
                </w:tr></w:tbl>
                <w:sectPr><w:headerReference w:type="default" r:id="rIdHeader"/></w:sectPr>
              </w:body>
            </w:document>
            """
        );
        WriteEntry(
            archive,
            "word/header1.xml",
            $"<w:hdr xmlns:w=\"{WordNamespace}\"><w:p><w:r><w:t>header sentinel</w:t></w:r></w:p></w:hdr>"
        );
        WriteEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdHeader" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
              <Relationship Id="rIdExternal" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.invalid/attack" TargetMode="External"/>
            </Relationships>
            """
        );
    }

    private static void CreateTextPackage(string path, string text)
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
            $"<w:document xmlns:w=\"{WordNamespace}\"><w:body><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:body></w:document>"
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
            "wordtoolkit-semantic-svg-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }
}
