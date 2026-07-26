using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordTemplateStyleAlignmentTests
{
    private const string TransitionalWord =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string StrictWord =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";

    [Fact]
    public void AlignsACompleteDependencyClosureAndPreservesTargetOnlyContent()
    {
        var target = Read(BuildPackage(
            Styles(
                TransitionalWord,
                Style("Normal", "paragraph", "<w:rPr><w:sz w:val=\"22\"/></w:rPr>"),
                Style("Base", "paragraph", "<w:rPr><w:b w:val=\"0\"/></w:rPr>"),
                Style("Heading", "paragraph", "<w:basedOn w:val=\"Base\"/><w:rPr><w:i/></w:rPr>"),
                Style("TargetOnly", "paragraph", "<w:rPr><w:u w:val=\"single\"/></w:rPr>")
            ),
            paragraphStyleId: "Heading",
            opaque: "target-opaque"
        ));
        var template = Read(BuildPackage(
            Styles(
                StrictWord,
                Style("Normal", "paragraph", "<w:rPr><w:sz w:val=\"22\"/></w:rPr>"),
                Style("Base", "paragraph", "<w:rPr><w:b/></w:rPr>"),
                Style("Heading", "paragraph", "<w:basedOn w:val=\"Base\"/><w:rPr><w:i/></w:rPr>"),
                Style("TemplateOnly", "character", "<w:rPr><w:smallCaps/></w:rPr>")
            ),
            wordNamespace: StrictWord,
            paragraphStyleId: "Heading",
            opaque: "template-opaque"
        ));
        var planner = new WordTemplateStyleAlignmentPlanner();

        var catalog = planner.Inspect(target, template);

        Assert.True(catalog.CanPlan);
        var heading = Assert.Single(catalog.Candidates, item => item.StyleId == "Heading");
        Assert.Equal(WordTemplateStyleAlignmentAction.AlignDependencyClosure, heading.Action);
        Assert.Contains("Base", heading.DependencyStyleIds);
        Assert.Equal(1, heading.ReplacedStyleCount);
        var plan = planner.Plan(target, template, [Command(heading)]);
        Assert.True(plan.Validation.Passed);
        Assert.Equal(["Base", "Heading"], plan.AlignedStyleIds);
        Assert.Single(plan.ChangedParts);
        Assert.Equal("/word/styles.xml", plan.ChangedParts[0].PartUri);

        var applied = Materialize(target, plan.CreateMutation(target));
        Assert.Equal(plan.ResultPackageFingerprint, applied.Fingerprint);
        var xml = XDocument.Parse(Encoding.UTF8.GetString(
            applied.Parts["/word/styles.xml"].Entry.Content.Span
        ));
        XNamespace w = TransitionalWord;
        Assert.NotNull(xml.Root!.Elements(w + "style").Single(element =>
            element.Attribute(w + "styleId")?.Value == "Base"
        ).Descendants(w + "b").SingleOrDefault());
        Assert.NotNull(xml.Root.Elements(w + "style").Single(element =>
            element.Attribute(w + "styleId")?.Value == "TargetOnly"
        ));
        Assert.DoesNotContain(xml.Root.DescendantsAndSelf(), element =>
            element.Name.NamespaceName == StrictWord
        );
        Assert.Equal(
            target.Parts["/word/document.xml"].Entry.Sha256,
            applied.Parts["/word/document.xml"].Entry.Sha256
        );
        Assert.Equal(
            target.Parts["/custom/opaque.bin"].Entry.Sha256,
            applied.Parts["/custom/opaque.bin"].Entry.Sha256
        );
        var inverse = Materialize(applied, plan.CreateInverseMutation(applied));
        Assert.Equal(target.Fingerprint, inverse.Fingerprint);
        Assert.Equal(template.Fingerprint, planner.Inspect(target, template)
            .TemplatePackageFingerprint);
    }

    [Fact]
    public void MirrorsStylesWithEffectsAndRemovesTemplateAbsentSelectedEffect()
    {
        var target = Read(BuildPackage(
            Styles(
                TransitionalWord,
                Style("Normal", "paragraph", string.Empty),
                Style("Focus", "paragraph", "<w:rPr><w:b w:val=\"0\"/></w:rPr>")
            ),
            effectsXml: Styles(
                TransitionalWord,
                Style("Normal", "paragraph", string.Empty),
                Style("Focus", "paragraph", "<w:rPr><w:color w:val=\"000000\"/></w:rPr>")
            )
        ));
        var template = Read(BuildPackage(
            Styles(
                TransitionalWord,
                Style("Normal", "paragraph", string.Empty),
                Style("Focus", "paragraph", "<w:rPr><w:b/></w:rPr>")
            ),
            effectsXml: Styles(
                TransitionalWord,
                Style("Normal", "paragraph", string.Empty)
            )
        ));
        var planner = new WordTemplateStyleAlignmentPlanner();
        var candidate = Assert.Single(planner.Inspect(target, template).Candidates, item =>
            item.StyleId == "Focus"
        );

        var plan = planner.Plan(target, template, [Command(candidate)]);
        var applied = Materialize(target, plan.CreateMutation(target));

        Assert.Equal(2, plan.ChangedParts.Count);
        Assert.True(plan.Validation.StylesWithEffectsMirrored);
        var effects = XDocument.Parse(Encoding.UTF8.GetString(
            applied.Parts["/word/stylesWithEffects.xml"].Entry.Content.Span
        ));
        XNamespace w = TransitionalWord;
        Assert.DoesNotContain(effects.Root!.Elements(w + "style"), element =>
            element.Attribute(w + "styleId")?.Value == "Focus"
        );
    }

    [Fact]
    public void RejectsAsymmetricEffectsTypeConflictsAndBrokenDependencies()
    {
        var target = Read(BuildPackage(Styles(
            TransitionalWord,
            Style("Normal", "paragraph", string.Empty),
            Style("Conflict", "paragraph", string.Empty)
        )));
        var effectsTemplate = Read(BuildPackage(
            Styles(
                TransitionalWord,
                Style("Normal", "paragraph", string.Empty),
                Style("Conflict", "paragraph", "<w:rPr><w:b/></w:rPr>")
            ),
            effectsXml: Styles(
                TransitionalWord,
                Style("Normal", "paragraph", string.Empty),
                Style("Conflict", "paragraph", "<w:rPr><w:b/></w:rPr>")
            )
        ));
        var planner = new WordTemplateStyleAlignmentPlanner();
        var asymmetric = planner.Inspect(target, effectsTemplate);
        Assert.False(asymmetric.CanPlan);
        Assert.Contains(asymmetric.Issues, issue =>
            issue.Code == "TEMPLATE_STYLE_EFFECTS_PART_ASYMMETRIC"
        );

        var typeTemplate = Read(BuildPackage(Styles(
            TransitionalWord,
            Style("Normal", "paragraph", string.Empty),
            Style("Conflict", "character", "<w:rPr><w:b/></w:rPr>"),
            Style("Good", "paragraph", "<w:rPr><w:i/></w:rPr>")
        )));
        var typeConflict = planner.Inspect(target, typeTemplate);
        Assert.True(typeConflict.CanPlan);
        Assert.Contains(typeConflict.Issues, issue =>
            issue.Code == "TEMPLATE_STYLE_TYPE_CONFLICT"
                && issue.StyleId == "Conflict"
        );
        Assert.Contains(typeConflict.Candidates, candidate => candidate.StyleId == "Good");

        var brokenTemplate = Read(BuildPackage(Styles(
            TransitionalWord,
            Style("Normal", "paragraph", string.Empty),
            Style("Broken", "paragraph", "<w:basedOn w:val=\"Missing\"/>")
        )));
        var broken = planner.Inspect(target, brokenTemplate);
        Assert.True(broken.CanPlan);
        Assert.Contains(broken.Issues, issue =>
            issue.Code.Contains("STYLE_BASED_ON_MISSING", StringComparison.Ordinal)
                || issue.Code == "TEMPLATE_STYLE_DEPENDENCY_MISSING"
        );
    }

    [Fact]
    public void RequiresEquivalentThemeAndNumberingDependencies()
    {
        const string themedStyle = "<w:rPr><w:color w:themeColor=\"accent1\"/></w:rPr>";
        var target = Read(BuildPackage(
            Styles(
                TransitionalWord,
                Style("Normal", "paragraph", string.Empty),
                Style("ThemeStyle", "paragraph", themedStyle)
            ),
            themeXml: Theme("FF0000")
        ));
        var template = Read(BuildPackage(
            Styles(
                TransitionalWord,
                Style("Normal", "paragraph", string.Empty),
                Style("ThemeStyle", "paragraph", themedStyle.Replace("</w:rPr>", "<w:b/></w:rPr>"))
            ),
            themeXml: Theme("00FF00")
        ));
        var planner = new WordTemplateStyleAlignmentPlanner();
        var themeCatalog = planner.Inspect(target, template);
        Assert.Contains(themeCatalog.Issues, issue =>
            issue.Code == "TEMPLATE_STYLE_THEME_CONTEXT_MISMATCH"
        );

        const string numbered = "<w:pPr><w:numPr><w:numId w:val=\"1\"/></w:numPr></w:pPr>";
        var numberedTarget = Read(BuildPackage(
            Styles(
                TransitionalWord,
                Style("Normal", "paragraph", string.Empty),
                Style("List", "paragraph", numbered)
            ),
            numberingXml: Numbering("%1.")
        ));
        var numberedTemplate = Read(BuildPackage(
            Styles(
                TransitionalWord,
                Style("Normal", "paragraph", string.Empty),
                Style("List", "paragraph", numbered.Replace("</w:pPr>", "<w:spacing w:after=\"120\"/></w:pPr>"))
            ),
            numberingXml: Numbering("(%1)")
        ));
        var numberingCatalog = planner.Inspect(numberedTarget, numberedTemplate);
        Assert.Contains(numberingCatalog.Issues, issue =>
            issue.Code == "TEMPLATE_STYLE_NUMBERING_DEPENDENCY_MISMATCH"
        );
    }

    [Fact]
    public void ResolvesBasedOnNextLinkedAndNumberingLinkedStyleClosureAtomically()
    {
        const string rootBody =
            "<w:basedOn w:val=\"Base\"/><w:next w:val=\"Body\"/>"
            + "<w:link w:val=\"HeadingChar\"/><w:pPr><w:numPr>"
            + "<w:numId w:val=\"1\"/></w:numPr></w:pPr>";
        var target = Read(BuildPackage(
            Styles(
                TransitionalWord,
                Style("Normal", "paragraph", string.Empty),
                Style("Base", "paragraph", "<w:rPr><w:b w:val=\"0\"/></w:rPr>"),
                Style("Body", "paragraph", string.Empty),
                Style("HeadingChar", "character", "<w:rPr><w:i/></w:rPr>"),
                Style("NumLink", "numbering", string.Empty),
                Style("Root", "paragraph", rootBody)
            ),
            paragraphStyleId: "Root",
            numberingXml: NumberingWithStyleLinks()
        ));
        var template = Read(BuildPackage(
            Styles(
                TransitionalWord,
                Style("Normal", "paragraph", string.Empty),
                Style("Base", "paragraph", "<w:rPr><w:b/></w:rPr>"),
                Style("Body", "paragraph", string.Empty),
                Style("HeadingChar", "character", "<w:rPr><w:i/></w:rPr>"),
                Style("NumLink", "numbering", string.Empty),
                Style("Root", "paragraph", rootBody)
            ),
            paragraphStyleId: "Root",
            numberingXml: NumberingWithStyleLinks()
        ));
        var planner = new WordTemplateStyleAlignmentPlanner();

        var candidate = Assert.Single(planner.Inspect(target, template).Candidates,
            item => item.StyleId == "Root");

        Assert.Equal(
            ["Base", "Body", "HeadingChar", "NumLink"],
            candidate.DependencyStyleIds
        );
        Assert.Equal(
            WordTemplateStyleAlignmentAction.AlignDependencyClosure,
            candidate.Action
        );
        var plan = planner.Plan(target, template, [Command(candidate)]);
        Assert.True(plan.Validation.DependencyClosureResolved);
        Assert.True(plan.Validation.NumberingDependenciesVerified);
        Assert.Equal(
            ["Base", "Body", "HeadingChar", "NumLink", "Root"],
            plan.AlignedStyleIds
        );
        Assert.Equal(
            target.Parts["/word/numbering.xml"].Entry.Sha256,
            Materialize(target, plan.CreateMutation(target))
                .Parts["/word/numbering.xml"].Entry.Sha256
        );
    }

    [Fact]
    public void PlanIdentityIsOrderIndependentAndStaleEvidenceFails()
    {
        var target = Read(BuildPackage(Styles(
            TransitionalWord,
            Style("Normal", "paragraph", string.Empty),
            Style("A", "paragraph", "<w:rPr><w:b w:val=\"0\"/></w:rPr>"),
            Style("B", "paragraph", "<w:rPr><w:i w:val=\"0\"/></w:rPr>")
        )));
        var template = Read(BuildPackage(Styles(
            TransitionalWord,
            Style("Normal", "paragraph", string.Empty),
            Style("A", "paragraph", "<w:rPr><w:b/></w:rPr>"),
            Style("B", "paragraph", "<w:rPr><w:i/></w:rPr>")
        )));
        var planner = new WordTemplateStyleAlignmentPlanner();
        var candidates = planner.Inspect(target, template).Candidates
            .Where(item => item.StyleId is "A" or "B")
            .OrderBy(item => item.StyleId)
            .ToArray();
        Assert.Equal(2, candidates.Length);

        var first = planner.Plan(target, template, candidates.Select(Command).ToArray());
        var second = planner.Plan(
            target,
            template,
            candidates.Reverse().Select(Command).ToArray()
        );

        Assert.Equal(first.PlanId, second.PlanId);
        var stale = Command(candidates[0]) with
        {
            ExpectedCandidateFingerprint = new string('a', 64),
        };
        Assert.Throws<WordSemanticPreconditionException>(() =>
            planner.Plan(target, template, [stale])
        );
    }

    [Fact]
    public void EnforcesCandidateClosureAndCommandLimits()
    {
        var target = Read(BuildPackage(Styles(
            TransitionalWord,
            Style("Normal", "paragraph", string.Empty)
        )));
        var template = Read(BuildPackage(Styles(
            TransitionalWord,
            Style("Normal", "paragraph", string.Empty),
            Style("Base", "paragraph", "<w:rPr><w:b/></w:rPr>"),
            Style("Child", "paragraph", "<w:basedOn w:val=\"Base\"/>")
        )));
        var limited = new WordTemplateStyleAlignmentPlanner(
            WordTemplateStyleAlignmentOptions.Default with { MaxDependencyClosure = 1 }
        );
        var catalog = limited.Inspect(target, template);
        Assert.Contains(catalog.Issues, issue =>
            issue.Code == "TEMPLATE_STYLE_DEPENDENCY_LIMIT"
                && issue.StyleId == "Child"
        );
        var ordinary = new WordTemplateStyleAlignmentPlanner();
        var candidate = ordinary.Inspect(target, template).Candidates.First();
        Assert.Throws<ArgumentException>(() => ordinary.Plan(
            target,
            template,
            Enumerable.Repeat(Command(candidate), 65).ToArray()
        ));

        var twoCandidates = Read(BuildPackage(Styles(
            TransitionalWord,
            Style("Normal", "paragraph", string.Empty),
            Style("A", "paragraph", "<w:rPr><w:b/></w:rPr>"),
            Style("B", "paragraph", "<w:rPr><w:i/></w:rPr>")
        )));
        var oneCandidateLimit = new WordTemplateStyleAlignmentPlanner(
            WordTemplateStyleAlignmentOptions.Default with { MaxCandidates = 1 }
        );
        Assert.Throws<WordSemanticTransactionLimitException>(() =>
            oneCandidateLimit.Inspect(target, twoCandidates)
        );
    }

    private static WordTemplateStyleAlignmentCommand Command(
        WordTemplateStyleAlignmentCandidate candidate
    ) => new(candidate.Id, candidate.Fingerprint);

    private static OpcPackageSnapshot Read(byte[] bytes) =>
        new OpcPackageReader().Read(new MemoryStream(bytes, writable: false));

    private static OpcPackageSnapshot Materialize(
        OpcPackageSnapshot package,
        OpcPackageMutationBuilder mutation
    )
    {
        using var stream = new MemoryStream();
        new OpcPackageSerializer().Write(stream, mutation);
        stream.Position = 0;
        return new OpcPackageReader().Read(stream);
    }

    private static byte[] BuildPackage(
        string stylesXml,
        string wordNamespace = TransitionalWord,
        string paragraphStyleId = "Normal",
        string? effectsXml = null,
        string? themeXml = null,
        string? numberingXml = null,
        string opaque = "opaque"
    )
    {
        var strict = wordNamespace == StrictWord;
        var officeRelationship = strict
            ? "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument"
            : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
        var stylesRelationship = strict
            ? "http://purl.oclc.org/ooxml/officeDocument/relationships/styles"
            : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";
        var numberingRelationship = strict
            ? "http://purl.oclc.org/ooxml/officeDocument/relationships/numbering"
            : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering";
        var themeRelationship = strict
            ? "http://purl.oclc.org/ooxml/officeDocument/relationships/theme"
            : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";
        var relationships = new StringBuilder()
            .Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">")
            .Append($"<Relationship Id=\"rStyles\" Type=\"{stylesRelationship}\" Target=\"styles.xml\"/>");
        if (effectsXml is not null)
        {
            relationships.Append("<Relationship Id=\"rEffects\" Type=\"http://schemas.microsoft.com/office/2007/relationships/stylesWithEffects\" Target=\"stylesWithEffects.xml\"/>");
        }
        if (themeXml is not null)
        {
            relationships.Append($"<Relationship Id=\"rTheme\" Type=\"{themeRelationship}\" Target=\"theme/theme1.xml\"/>");
        }
        if (numberingXml is not null)
        {
            relationships.Append($"<Relationship Id=\"rNumbering\" Type=\"{numberingRelationship}\" Target=\"numbering.xml\"/>");
        }
        relationships.Append("</Relationships>");
        var overrides = new StringBuilder()
            .Append("<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>")
            .Append("<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>");
        if (effectsXml is not null)
        {
            overrides.Append("<Override PartName=\"/word/stylesWithEffects.xml\" ContentType=\"application/vnd.ms-word.stylesWithEffects+xml\"/>");
        }
        if (themeXml is not null)
        {
            overrides.Append("<Override PartName=\"/word/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>");
        }
        if (numberingXml is not null)
        {
            overrides.Append("<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/>");
        }
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml", $"<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"bin\" ContentType=\"application/octet-stream\"/>{overrides}</Types>");
            Add(archive, "_rels/.rels", $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rDoc\" Type=\"{officeRelationship}\" Target=\"word/document.xml\"/></Relationships>");
            Add(archive, "word/_rels/document.xml.rels", relationships.ToString());
            Add(archive, "word/document.xml", $"<w:document xmlns:w=\"{wordNamespace}\"><w:body><w:p><w:pPr><w:pStyle w:val=\"{paragraphStyleId}\"/></w:pPr><w:r><w:t>content</w:t></w:r></w:p></w:body></w:document>");
            Add(archive, "word/styles.xml", stylesXml);
            if (effectsXml is not null)
            {
                Add(archive, "word/stylesWithEffects.xml", effectsXml);
            }
            if (themeXml is not null)
            {
                Add(archive, "word/theme/theme1.xml", themeXml);
            }
            if (numberingXml is not null)
            {
                Add(archive, "word/numbering.xml", numberingXml);
            }
            Add(archive, "custom/opaque.bin", opaque);
        }
        return stream.ToArray();
    }

    private static string Styles(string wordNamespace, params string[] styles)
    {
        var declaration = $"xmlns:w=\"{wordNamespace}\"";
        var content = string.Concat(styles.Select(style =>
            style.Replace("xmlns:w=\"urn:test\"", declaration, StringComparison.Ordinal)
        ));
        return $"<w:styles xmlns:w=\"{wordNamespace}\">{content}</w:styles>";
    }

    private static string Style(string id, string type, string body) =>
        $"<w:style xmlns:w=\"urn:test\" w:type=\"{type}\" w:styleId=\"{id}\"><w:name w:val=\"{id}\"/>{body}</w:style>";

    private static string Theme(string accent1) =>
        $"<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"T\"><a:themeElements><a:clrScheme name=\"C\"><a:dk1><a:srgbClr val=\"000000\"/></a:dk1><a:lt1><a:srgbClr val=\"FFFFFF\"/></a:lt1><a:dk2><a:srgbClr val=\"111111\"/></a:dk2><a:lt2><a:srgbClr val=\"EEEEEE\"/></a:lt2><a:accent1><a:srgbClr val=\"{accent1}\"/></a:accent1><a:accent2><a:srgbClr val=\"222222\"/></a:accent2><a:accent3><a:srgbClr val=\"333333\"/></a:accent3><a:accent4><a:srgbClr val=\"444444\"/></a:accent4><a:accent5><a:srgbClr val=\"555555\"/></a:accent5><a:accent6><a:srgbClr val=\"666666\"/></a:accent6><a:hlink><a:srgbClr val=\"0000FF\"/></a:hlink><a:folHlink><a:srgbClr val=\"800080\"/></a:folHlink></a:clrScheme><a:fontScheme name=\"F\"><a:majorFont><a:latin typeface=\"Arial\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont><a:minorFont><a:latin typeface=\"Calibri\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont></a:fontScheme><a:fmtScheme name=\"M\"><a:fillStyleLst/><a:lnStyleLst/><a:effectStyleLst/><a:bgFillStyleLst/></a:fmtScheme></a:themeElements></a:theme>";

    private static string Numbering(string levelText) =>
        $"<w:numbering xmlns:w=\"{TransitionalWord}\"><w:abstractNum w:abstractNumId=\"1\"><w:multiLevelType w:val=\"singleLevel\"/><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"{levelText}\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"1\"/></w:num></w:numbering>";

    private static string NumberingWithStyleLinks() =>
        $"<w:numbering xmlns:w=\"{TransitionalWord}\"><w:abstractNum w:abstractNumId=\"1\"><w:numStyleLink w:val=\"NumLink\"/><w:styleLink w:val=\"HeadingChar\"/><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:pStyle w:val=\"Body\"/><w:lvlText w:val=\"%1.\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"1\"/></w:num></w:numbering>";

    private static void Add(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var output = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        output.Write(bytes);
    }
}
