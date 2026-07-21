using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordEquationGraphTests
{
    private const string TransitionalWord =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string TransitionalMath =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string StrictWord =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string StrictMath =
        "http://purl.oclc.org/ooxml/officeDocument/math";

    [Fact]
    public void ModelsEveryStandardMathObjectWithSourceLinkedRolesAndProperties()
    {
        using var bytes = BuildPackage(ComprehensiveDocument());
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordEquationGraphBuilder().Build(package, semantic);

        var equation = Assert.Single(graph.Equations);
        Assert.Equal(WordEquationStatus.Complete, equation.Status);
        Assert.True(equation.IsDisplay);
        Assert.Equal(WordStoryKind.Main, equation.StoryKind);
        Assert.NotNull(equation.SemanticNodeId);
        Assert.NotNull(equation.ParagraphNodeId);
        Assert.NotNull(equation.MathParagraphId);
        Assert.Equal("centerGroup", Assert.Single(graph.MathParagraphs).Justification);
        Assert.Equal(0, equation.IndexInMathParagraph);
        Assert.Equal(0, equation.UnsupportedNodeCount);
        Assert.True(equation.NodeCount > 100);
        Assert.True(equation.MaximumDepth >= 4);
        Assert.Contains("matrix", equation.Text, StringComparison.Ordinal);

        var nodes = equation.Root.DescendantsAndSelf().ToArray();
        var requiredKinds = new[]
        {
            WordMathNodeKind.Accent,
            WordMathNodeKind.Bar,
            WordMathNodeKind.BorderBox,
            WordMathNodeKind.Box,
            WordMathNodeKind.Delimiter,
            WordMathNodeKind.EquationArray,
            WordMathNodeKind.Fraction,
            WordMathNodeKind.Function,
            WordMathNodeKind.GroupCharacter,
            WordMathNodeKind.LowerLimit,
            WordMathNodeKind.UpperLimit,
            WordMathNodeKind.Matrix,
            WordMathNodeKind.Nary,
            WordMathNodeKind.Phantom,
            WordMathNodeKind.Radical,
            WordMathNodeKind.PreSubSuperscript,
            WordMathNodeKind.Subscript,
            WordMathNodeKind.SubSuperscript,
            WordMathNodeKind.Superscript,
        };
        foreach (var kind in requiredKinds)
        {
            Assert.Contains(nodes, node => node.Kind == kind);
        }
        Assert.All(nodes, node =>
        {
            Assert.StartsWith("wdmn_", node.Id, StringComparison.Ordinal);
            Assert.True(node.SourceElementOrdinal >= equation.SourceElementOrdinal);
        });
        var fraction = Assert.Single(
            nodes,
            node => node.Kind == WordMathNodeKind.Fraction
        );
        Assert.Equal("skw", fraction.Properties["fraction_type"]);
        Assert.Equal(
            new[] { "numerator", "denominator" },
            fraction.Children.Select(child => child.Role).ToArray()
        );
        var delimiter = Assert.Single(
            nodes,
            node => node.Kind == WordMathNodeKind.Delimiter
        );
        Assert.Equal("[", delimiter.Properties["begin_character"]);
        Assert.Equal("]", delimiter.Properties["end_character"]);
        Assert.Equal(";", delimiter.Properties["separator_character"]);
        Assert.Equal("2", delimiter.Properties["argument_count"]);
        var matrix = Assert.Single(
            nodes,
            node => node.Kind == WordMathNodeKind.Matrix
        );
        Assert.Equal("2", matrix.Properties["declared_column_count"]);
        Assert.Equal("2", matrix.Properties["inferred_column_count"]);
        Assert.Equal(2, matrix.Children.Count);
        Assert.All(matrix.Children, row => Assert.Equal(2, row.Children.Count));
        var run = nodes.First(node =>
            node.Kind == WordMathNodeKind.Run && node.Properties.ContainsKey("script")
        );
        Assert.Equal("double-struck", run.Properties["script"]);
        Assert.Equal("bi", run.Properties["style"]);
        Assert.Empty(graph.Issues);
    }

    [Fact]
    public void KeepsInlineDisplayHeaderAndTextBoxEquationsInTheirRealStories()
    {
        using var bytes = BuildPackage(
            $"""
            <w:document xmlns:w="{TransitionalWord}" xmlns:m="{TransitionalMath}" xmlns:v="urn:schemas-microsoft-com:vml">
              <w:body>
                <w:p><w:r><w:t>before</w:t></w:r><m:oMath>{Run("inline")}</m:oMath></w:p>
                <w:p><m:oMathPara><m:oMath>{Run("display")}</m:oMath></m:oMathPara></w:p>
                <w:p><w:pict><v:shape><v:textbox><w:txbxContent><w:p><m:oMath>{Run("textbox")}</m:oMath></w:p></w:txbxContent></v:textbox></v:shape></w:pict></w:p>
              </w:body>
            </w:document>
            """,
            headerXml: $"""
            <w:hdr xmlns:w="{TransitionalWord}" xmlns:m="{TransitionalMath}"><w:p><m:oMath>{Run("header")}</m:oMath></w:p></w:hdr>
            """
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordEquationGraphBuilder().Build(package, semantic);

        Assert.Equal(4, graph.Equations.Count);
        Assert.Equal(1, graph.DisplayEquationCount);
        Assert.Equal(3, graph.InlineEquationCount);
        Assert.Equal(
            new[]
            {
                WordStoryKind.Main,
                WordStoryKind.Main,
                WordStoryKind.TextBox,
                WordStoryKind.Header,
            },
            graph.Equations.Select(equation => equation.StoryKind).ToArray()
        );
        Assert.All(graph.Equations, equation => Assert.NotNull(equation.StoryNodeId));
    }

    [Fact]
    public void ReadsStrictOmmlAndMainMathDefaults()
    {
        using var bytes = BuildPackage(
            $"""
            <w:document xmlns:w="{StrictWord}" xmlns:m="{StrictMath}"><w:body><w:p><m:oMath><m:f><m:fPr><m:type m:val="noBar"/></m:fPr><m:num>{Run("n")}</m:num><m:den>{Run("k")}</m:den></m:f></m:oMath></w:p></w:body></w:document>
            """,
            settingsXml: $"""
            <w:settings xmlns:w="{StrictWord}" xmlns:m="{StrictMath}"><m:mathPr><m:mathFont m:val="Cambria Math"/><m:brkBin m:val="before"/><m:brkBinSub m:val="+-"/><m:smallFrac m:val="on"/><m:defJc m:val="centerGroup"/><m:intLim m:val="subSup"/><m:naryLim m:val="undOvr"/></m:mathPr></w:settings>
            """,
            strictRelationships: true
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordEquationGraphBuilder().Build(package, semantic);

        var equation = Assert.Single(graph.Equations);
        Assert.Equal(WordEquationStatus.Complete, equation.Status);
        var fraction = Assert.Single(
            equation.Root.DescendantsAndSelf(),
            node => node.Kind == WordMathNodeKind.Fraction
        );
        Assert.Equal("noBar", fraction.Properties["fraction_type"]);
        Assert.NotNull(graph.Settings);
        Assert.Equal("Cambria Math", graph.Settings.Properties["math_font"]);
        Assert.Equal("before", graph.Settings.Properties["break_binary"]);
        Assert.Equal("+-", graph.Settings.Properties["break_binary_subtraction"]);
        Assert.Equal("true", graph.Settings.Properties["small_fraction"]);
        Assert.Equal("centerGroup", graph.Settings.Properties["default_justification"]);
        Assert.Equal("subSup", graph.Settings.Properties["integral_limit_location"]);
        Assert.Equal("undOvr", graph.Settings.Properties["nary_limit_location"]);
        Assert.Empty(graph.Issues);
    }

    [Fact]
    public void DiagnosesMissingArgumentsUnknownMathNestingAndOrphanObjects()
    {
        using var bytes = BuildPackage(
            $"""
            <w:document xmlns:w="{TransitionalWord}" xmlns:m="{TransitionalMath}" xmlns:x="urn:future-math">
              <w:body><w:p>
                <m:oMath>
                  stray-text
                  <m:f><m:num>{Run("x")}</m:num></m:f>
                  <m:future m:val="1"><m:r><m:t>future</m:t></m:r></m:future>
                  <m:opaque>secret-unknown-value</m:opaque>
                  <x:semantic><m:r><m:t>extension</m:t></m:r></x:semantic>
                  <m:oMath>{Run("nested")}</m:oMath>
                </m:oMath>
                <m:rad><m:deg/><m:e>{Run("outside")}</m:e></m:rad>
              </w:p></w:body>
            </w:document>
            """
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordEquationGraphBuilder().Build(package, semantic);

        Assert.Equal(2, graph.Equations.Count);
        Assert.All(graph.Equations, equation =>
            Assert.Equal(WordEquationStatus.Malformed, equation.Status)
        );
        Assert.Contains(graph.Issues, issue =>
            issue.Code == "MATH_REQUIRED_ARGUMENT_MISSING"
        );
        Assert.Contains(graph.Issues, issue =>
            issue.Code == "MATH_UNKNOWN_ELEMENT_PRESERVED"
        );
        Assert.Contains(graph.Issues, issue =>
            issue.Code == "MATH_EXTENSION_CONTENT_PRESERVED"
        );
        Assert.Contains(graph.Issues, issue => issue.Code == "MATH_NESTED_EQUATION");
        Assert.Contains(graph.Issues, issue =>
            issue.Code == "MATH_UNEXPECTED_DIRECT_TEXT"
        );
        Assert.Contains(graph.Issues, issue =>
            issue.Code == "MATH_CONTENT_OUTSIDE_EQUATION"
        );
        Assert.True(graph.Equations[0].UnsupportedNodeCount >= 2);
        var opaque = Assert.Single(
            graph.Equations[0].Root.DescendantsAndSelf(),
            node => node.SourceName == "m:opaque"
        );
        Assert.Equal("true", opaque.Properties["scalar_value_present"]);
        Assert.DoesNotContain("secret", string.Join("", opaque.Properties.Values));
    }

    [Fact]
    public void EquationIdsSurviveUnrelatedParagraphInsertion()
    {
        var equationParagraph = $"<w:p><m:oMath><m:sSup><m:e>{Run("x")}</m:e><m:sup>{Run("2")}</m:sup></m:sSup></m:oMath></w:p>";
        using var first = BuildPackage(
            Document($"<w:p><w:r><w:t>before</w:t></w:r></w:p>{equationParagraph}")
        );
        using var second = BuildPackage(
            Document($"<w:p><w:r><w:t>inserted</w:t></w:r></w:p><w:p><w:r><w:t>before</w:t></w:r></w:p>{equationParagraph}")
        );

        var (firstPackage, firstSemantic) = ReadSnapshots(first);
        var (secondPackage, secondSemantic) = ReadSnapshots(second);
        var firstEquation = Assert.Single(
            new WordEquationGraphBuilder().Build(firstPackage, firstSemantic).Equations
        );
        var secondEquation = Assert.Single(
            new WordEquationGraphBuilder().Build(secondPackage, secondSemantic).Equations
        );

        Assert.Equal(firstEquation.Id, secondEquation.Id);
        Assert.Equal(firstEquation.SemanticNodeId, secondEquation.SemanticNodeId);
        Assert.Equal(
            firstEquation.Root.DescendantsAndSelf().Select(node => node.Id),
            secondEquation.Root.DescendantsAndSelf().Select(node => node.Id)
        );
    }

    [Fact]
    public void EnforcesEquationNodeDepthAndTextLimits()
    {
        using var twoEquations = BuildPackage(
            Document($"<w:p><m:oMath>{Run("a")}</m:oMath><w:br/><m:oMath>{Run("b")}</m:oMath></w:p>")
        );
        var (package, semantic) = ReadSnapshots(twoEquations);
        Assert.Throws<WordEquationLimitException>(() =>
            new WordEquationGraphBuilder(
                new WordEquationGraphOptions { MaxEquations = 1 }
            ).Build(package, semantic)
        );

        using var deep = BuildPackage(
            Document($"<w:p><m:oMath><m:f><m:num>{Run("x")}</m:num><m:den>{Run("y")}</m:den></m:f></m:oMath></w:p>")
        );
        var (deepPackage, deepSemantic) = ReadSnapshots(deep);
        Assert.Throws<WordEquationLimitException>(() =>
            new WordEquationGraphBuilder(
                new WordEquationGraphOptions { MaxDepth = 2 }
            ).Build(deepPackage, deepSemantic)
        );

        using var longText = BuildPackage(
            Document($"<w:p><m:oMath>{Run(new string('x', 20))}</m:oMath></w:p>")
        );
        var (longPackage, longSemantic) = ReadSnapshots(longText);
        Assert.Throws<WordEquationLimitException>(() =>
            new WordEquationGraphBuilder(
                new WordEquationGraphOptions { MaxTextCharactersPerNode = 10 }
            ).Build(longPackage, longSemantic)
        );

        using var longProperty = BuildPackage(
            Document(
                $"<w:p><m:oMath><m:acc><m:accPr><m:chr m:val=\"{new string('x', 20)}\"/></m:accPr><m:e>{Run("x")}</m:e></m:acc></m:oMath></w:p>"
            )
        );
        var (propertyPackage, propertySemantic) = ReadSnapshots(longProperty);
        Assert.Throws<WordEquationLimitException>(() =>
            new WordEquationGraphBuilder(
                new WordEquationGraphOptions { MaxPropertyValueCharacters = 10 }
            ).Build(propertyPackage, propertySemantic)
        );
    }

    [Fact]
    public void KeepsEquationsWhenOptionalMathSettingsAreMalformed()
    {
        using var bytes = BuildPackage(
            Document($"<w:p><m:oMath>{Run("x")}</m:oMath></w:p>"),
            settingsXml: $"<w:broken xmlns:w=\"{TransitionalWord}\"/>"
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordEquationGraphBuilder().Build(package, semantic);

        Assert.Single(graph.Equations);
        Assert.Null(graph.Settings);
        Assert.Contains(graph.Issues, issue =>
            issue.Code == "MATH_SETTINGS_UNAVAILABLE"
        );
    }

    [Fact]
    public void CapsIssueFloodWithoutDiscardingParsedEquations()
    {
        using var bytes = BuildPackage(
            Document(
                "<w:p><m:oMath><m:f/><m:f/><m:f/></m:oMath></w:p>"
            )
        );
        var (package, semantic) = ReadSnapshots(bytes);

        var graph = new WordEquationGraphBuilder(
            new WordEquationGraphOptions { MaxIssues = 1 }
        ).Build(package, semantic);

        Assert.Single(graph.Issues);
        Assert.True(graph.IssuesTruncated);
        Assert.Single(graph.Equations);
        Assert.Equal(WordEquationStatus.Malformed, graph.Equations[0].Status);
    }

    [Fact]
    public void ParsesEveryBundledDocumentContainingRealEquations()
    {
        var root = FindRepositoryRoot();
        var paths = new[]
        {
            Path.Combine(root, "examples", "advanced", "WordToolkit-advanced-torture-test.docx"),
            Path.Combine(root, "examples", "generated", "WordToolkit-equations.docx"),
            Path.Combine(root, "examples", "generated", "WordToolkit-showcase.docx"),
        };
        Assert.All(paths, path => Assert.True(File.Exists(path), path));
        var reader = new OpcPackageReader();
        var equationCount = 0;
        foreach (var path in paths)
        {
            var package = reader.Read(path);
            var semantic = new WordSemanticProjector().Project(package);
            var graph = new WordEquationGraphBuilder().Build(package, semantic);
            Assert.Equal(package.Fingerprint, graph.PackageFingerprint);
            equationCount += graph.Equations.Count;
        }

        Assert.Equal(23, equationCount);
    }

    private static string ComprehensiveDocument() =>
        $"""
        <w:document xmlns:w="{TransitionalWord}" xmlns:m="{TransitionalMath}">
          <w:body><w:p><m:oMathPara><m:oMathParaPr><m:jc m:val="centerGroup"/></m:oMathParaPr><m:oMath>
            <m:acc><m:accPr><m:chr m:val="̂"/></m:accPr><m:e>{Run("accent")}</m:e></m:acc>
            <m:bar><m:barPr><m:pos m:val="bot"/></m:barPr><m:e>{Run("bar")}</m:e></m:bar>
            <m:borderBox><m:borderBoxPr><m:hideTop m:val="0"/><m:strikeH m:val="1"/><m:strikeBLTR m:val="on"/></m:borderBoxPr><m:e>{Run("border")}</m:e></m:borderBox>
            <m:box><m:boxPr><m:aln/><m:diff m:val="false"/><m:noBreak/><m:opEmu m:val="off"/></m:boxPr><m:e>{Run("box")}</m:e></m:box>
            <m:d><m:dPr><m:begChr m:val="["/><m:endChr m:val="]"/><m:sepChr m:val=";"/><m:grow/><m:shp m:val="match"/></m:dPr><m:e>{Run("a")}</m:e><m:e>{Run("b")}</m:e></m:d>
            <m:eqArr><m:eqArrPr><m:baseJc m:val="left"/><m:maxDist/><m:objDist m:val="0"/><m:rSp m:val="2"/><m:rSpRule m:val="3"/></m:eqArrPr><m:e>{Run("row1")}</m:e><m:e>{Run("row2")}</m:e></m:eqArr>
            <m:f><m:fPr><m:type m:val="skw"/></m:fPr><m:num>{Run("num")}</m:num><m:den>{Run("den")}</m:den></m:f>
            <m:func><m:funcPr/><m:fName>{Run("sin")}</m:fName><m:e>{Run("x")}</m:e></m:func>
            <m:groupChr><m:groupChrPr><m:chr m:val="⏞"/><m:pos m:val="top"/><m:vertJc m:val="bot"/></m:groupChrPr><m:e>{Run("group")}</m:e></m:groupChr>
            <m:limLow><m:limLowPr/><m:e>{Run("lim")}</m:e><m:lim>{Run("x→0")}</m:lim></m:limLow>
            <m:limUpp><m:limUppPr/><m:e>{Run("max")}</m:e><m:lim>{Run("n")}</m:lim></m:limUpp>
            <m:m><m:mPr><m:baseJc m:val="center"/><m:plcHide/><m:rSp m:val="1"/><m:rSpRule m:val="2"/><m:cGp m:val="1"/><m:cGpRule m:val="2"/><m:cSp m:val="0"/><m:mcs><m:mc><m:mcPr><m:count m:val="2"/><m:mcJc m:val="right"/></m:mcPr></m:mc></m:mcs></m:mPr><m:mr><m:e>{Run("matrix")}</m:e><m:e>{Run("12")}</m:e></m:mr><m:mr><m:e>{Run("21")}</m:e><m:e>{Run("22")}</m:e></m:mr></m:m>
            <m:nary><m:naryPr><m:chr m:val="∫"/><m:limLoc m:val="undOvr"/><m:grow/><m:subHide m:val="0"/><m:supHide m:val="false"/></m:naryPr><m:sub>{Run("0")}</m:sub><m:sup>{Run("1")}</m:sup><m:e>{Run("f(x)")}</m:e></m:nary>
            <m:phant><m:phantPr><m:show/><m:transp m:val="on"/><m:zeroAsc/><m:zeroDesc/><m:zeroWid/></m:phantPr><m:e>{Run("phantom")}</m:e></m:phant>
            <m:rad><m:radPr><m:degHide m:val="0"/></m:radPr><m:deg>{Run("3")}</m:deg><m:e>{Run("x")}</m:e></m:rad>
            <m:sPre><m:sPrePr/><m:sub>{Run("preSub")}</m:sub><m:sup>{Run("preSup")}</m:sup><m:e>{Run("base")}</m:e></m:sPre>
            <m:sSub><m:sSubPr/><m:e>{Run("base")}</m:e><m:sub>{Run("sub")}</m:sub></m:sSub>
            <m:sSubSup><m:sSubSupPr/><m:e>{Run("base")}</m:e><m:sub>{Run("sub")}</m:sub><m:sup>{Run("sup")}</m:sup></m:sSubSup>
            <m:sSup><m:sSupPr/><m:e>{Run("base")}</m:e><m:sup>{Run("sup")}</m:sup></m:sSup>
            <m:r><m:rPr><m:scr m:val="double-struck"/><m:sty m:val="bi"/><m:nor m:val="0"/></m:rPr><m:t>R</m:t></m:r>
          </m:oMath></m:oMathPara></w:p></w:body>
        </w:document>
        """;

    private static string Document(string body) =>
        $"<w:document xmlns:w=\"{TransitionalWord}\" xmlns:m=\"{TransitionalMath}\"><w:body>{body}</w:body></w:document>";

    private static string Run(string text) =>
        $"<m:r><m:t>{System.Security.SecurityElement.Escape(text)}</m:t></m:r>";

    private static (
        OpcPackageSnapshot Package,
        WordSemanticDocument Semantic
    ) ReadSnapshots(Stream bytes)
    {
        var package = new OpcPackageReader().Read(bytes);
        var semantic = new WordSemanticProjector().Project(package);
        return (package, semantic);
    }

    private static MemoryStream BuildPackage(
        string documentXml,
        string? settingsXml = null,
        string? headerXml = null,
        bool strictRelationships = false
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                "[Content_Types].xml",
                $"""
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  {(settingsXml is null ? string.Empty : "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>")}
                  {(headerXml is null ? string.Empty : "<Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/>")}
                </Types>
                """
            );
            var relationshipBase = strictRelationships
                ? "http://purl.oclc.org/ooxml/officeDocument/relationships/"
                : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
            WriteEntry(
                archive,
                "_rels/.rels",
                $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"{relationshipBase}officeDocument\" Target=\"word/document.xml\"/></Relationships>"
            );
            WriteEntry(archive, "word/document.xml", documentXml);
            var relationships = new List<string>();
            if (settingsXml is not null)
            {
                relationships.Add(
                    $"<Relationship Id=\"rIdSettings\" Type=\"{relationshipBase}settings\" Target=\"settings.xml\"/>"
                );
                WriteEntry(archive, "word/settings.xml", settingsXml);
            }
            if (headerXml is not null)
            {
                relationships.Add(
                    $"<Relationship Id=\"rIdHeader\" Type=\"{relationshipBase}header\" Target=\"header1.xml\"/>"
                );
                WriteEntry(archive, "word/header1.xml", headerXml);
            }
            if (relationships.Count > 0)
            {
                WriteEntry(
                    archive,
                    "word/_rels/document.xml.rels",
                    $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{string.Concat(relationships)}</Relationships>"
                );
            }
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

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "pyproject.toml")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate the WordToolkit repository root."
        );
    }
}
