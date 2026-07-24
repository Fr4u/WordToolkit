using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordFormatterTests
{
    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void RemovesOnlyProvenRedundantScalarFormattingAndRetainsExactInverse()
    {
        using var stream = BuildPackage(
            """
            <w:body>
              <w:p>
                <w:pPr>
                  <w:pStyle w:val="Normal"/>
                  <w:jc w:val="center"/>
                  <w:spacing w:after="200"/>
                  <w:ind w:left="720"/>
                  <w:keepNext w:val="0"/>
                  <w:shd w:val="clear" w:fill="FFFFFF"/>
                </w:pPr>
                <w:r>
                  <w:rPr>
                    <w:b/>
                    <w:sz w:val="24"/>
                    <w:i/>
                    <w:color w:val="112233"/>
                  </w:rPr>
                  <w:t>Formatter</w:t>
                </w:r>
              </w:p>
            </w:body>
            """,
            """
            <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
              <w:pPr>
                <w:jc w:val="center"/>
                <w:spacing w:after="200"/>
                <w:ind w:left="720"/>
                <w:keepNext w:val="0"/>
                <w:shd w:val="clear" w:fill="FFFFFF"/>
              </w:pPr>
              <w:rPr>
                <w:b/>
                <w:sz w:val="24"/>
                <w:color w:val="112233"/>
              </w:rPr>
            </w:style>
            """
        );
        var reader = new OpcPackageReader();
        var package = reader.Read(stream);
        var semantic = new WordSemanticProjector().Project(package);
        var planner = new WordFormatterPlanner();

        var plan = planner.Plan(
            package,
            semantic,
            package.Fingerprint,
            [WordFormatterPolicy.RemoveRedundantDirectFormatting]
        );
        var repeated = planner.Plan(
            package,
            semantic,
            package.Fingerprint,
            [WordFormatterPolicy.RemoveRedundantDirectFormatting]
        );

        Assert.StartsWith("wtfmt_", plan.PlanId, StringComparison.Ordinal);
        Assert.Equal(plan.PlanId, repeated.PlanId);
        Assert.True(plan.HasChanges);
        Assert.True(plan.Validation.Passed);
        Assert.Equal(8, plan.RemovedElementCount);
        Assert.Single(plan.ChangedParts);
        Assert.Equal("/word/document.xml", plan.ChangedParts[0].PartUri);
        Assert.Equal(
            ["b", "color", "ind", "jc", "keepNext", "shd", "spacing", "sz"],
            plan.Changes.Select(change => change.PropertyElementName)
                .Order(StringComparer.Ordinal)
                .ToArray()
        );
        Assert.All(plan.Changes, change =>
        {
            Assert.StartsWith("sha256:", change.SourceElementFingerprint, StringComparison.Ordinal);
            Assert.True(change.RemovedBytes > 0);
            Assert.True(change.PropertyCount > 0);
        });

        using var appliedStream = Serialize(plan.CreateMutation(package));
        var applied = reader.Read(appliedStream);
        Assert.Equal(plan.ResultPackageFingerprint, applied.Fingerprint);
        var xml = XDocument.Parse(
            Encoding.UTF8.GetString(
                applied.Parts["/word/document.xml"].Entry.Content.Span
            )
        );
        XNamespace w = WordNamespace;
        var paragraphProperties = xml.Descendants(w + "pPr").Single();
        var runProperties = xml.Descendants(w + "rPr").Single();
        Assert.Equal(["pStyle"], paragraphProperties.Elements()
            .Select(element => element.Name.LocalName).ToArray());
        Assert.Equal(["i"], runProperties.Elements()
            .Select(element => element.Name.LocalName).ToArray());
        Assert.Equal(
            package.Parts["/word/styles.xml"].Entry.Sha256,
            applied.Parts["/word/styles.xml"].Entry.Sha256
        );

        using var revertedStream = Serialize(plan.CreateInverseMutation(applied));
        var reverted = reader.Read(revertedStream);
        Assert.Equal(package.Fingerprint, reverted.Fingerprint);
        Assert.Equal(
            package.Parts["/word/document.xml"].Entry.Content.ToArray(),
            reverted.Parts["/word/document.xml"].Entry.Content.ToArray()
        );
    }

    [Fact]
    public void LeavesFormattingThatChangesTheEffectiveResultAsAStableNoOp()
    {
        using var stream = BuildPackage(
            """
            <w:body><w:p><w:pPr><w:pStyle w:val="Normal"/><w:jc w:val="right"/></w:pPr><w:r><w:rPr><w:b w:val="0"/><w:sz w:val="28"/></w:rPr><w:t>Different</w:t></w:r></w:p></w:body>
            """,
            """
            <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:pPr><w:jc w:val="left"/></w:pPr><w:rPr><w:b/><w:sz w:val="24"/></w:rPr></w:style>
            """
        );
        var package = new OpcPackageReader().Read(stream);
        var semantic = new WordSemanticProjector().Project(package);

        var plan = new WordFormatterPlanner().Plan(
            package,
            semantic,
            package.Fingerprint,
            [WordFormatterPolicy.RemoveRedundantDirectFormatting]
        );

        Assert.False(plan.HasChanges);
        Assert.Empty(plan.Changes);
        Assert.Empty(plan.ChangedParts);
        Assert.Equal(package.Fingerprint, plan.ResultPackageFingerprint);
        Assert.True(plan.Validation.Passed);
        Assert.Equal(0, plan.Validation.AffectedNodeCount);
    }

    [Fact]
    public void RemovesCompositePropertiesOnlyAfterAGroupAwareProof()
    {
        using var stream = BuildPackage(
            """
            <w:body><w:p><w:pPr><w:pStyle w:val="Normal"/><w:shd w:val="clear" w:fill="FFFFFF"/></w:pPr><w:r><w:rPr><w:rFonts w:ascii="Aptos"/><w:color w:val="112233"/><w:u w:val="single"/><w:shd w:val="clear" w:fill="FFFFFF"/></w:rPr><w:t>Composite</w:t></w:r></w:p></w:body>
            """,
            """
            <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:pPr><w:shd w:val="clear" w:fill="FFFFFF"/></w:pPr><w:rPr><w:rFonts w:ascii="Aptos"/><w:color w:val="112233"/><w:u w:val="single"/><w:shd w:val="clear" w:fill="FFFFFF"/></w:rPr></w:style>
            """
        );
        var package = new OpcPackageReader().Read(stream);
        var semantic = new WordSemanticProjector().Project(package);

        var plan = new WordFormatterPlanner().Plan(
            package,
            semantic,
            package.Fingerprint,
            [WordFormatterPolicy.RemoveRedundantDirectFormatting]
        );

        Assert.True(plan.HasChanges);
        Assert.Equal(5, plan.Changes.Count);
        Assert.Equal(5, plan.CandidateElementsScanned);
        Assert.Equal(5, plan.CompositeCandidateProofs);
        Assert.Equal(
            ["color", "rFonts", "shd", "shd", "u"],
            plan.Changes.Select(change => change.PropertyElementName)
                .Order(StringComparer.Ordinal)
                .ToArray()
        );

        using var appliedStream = Serialize(plan.CreateMutation(package));
        var applied = new OpcPackageReader().Read(appliedStream);
        var xml = XDocument.Parse(
            Encoding.UTF8.GetString(
                applied.Parts["/word/document.xml"].Entry.Content.Span
            )
        );
        XNamespace w = WordNamespace;
        Assert.Equal(
            ["pStyle"],
            xml.Descendants(w + "pPr").Single().Elements()
                .Select(element => element.Name.LocalName).ToArray()
        );
        Assert.Empty(xml.Descendants(w + "rPr").Single().Elements());

        using var revertedStream = Serialize(plan.CreateInverseMutation(applied));
        var reverted = new OpcPackageReader().Read(revertedStream);
        Assert.Equal(
            package.Parts["/word/document.xml"].Entry.Content.ToArray(),
            reverted.Parts["/word/document.xml"].Entry.Content.ToArray()
        );
    }

    [Fact]
    public void KeepsCompositeElementsWhenRemovalRestoresSupersededThemeMembers()
    {
        using var stream = BuildPackage(
            """
            <w:body><w:p><w:pPr><w:pStyle w:val="Normal"/><w:shd w:val="clear" w:fill="FFFFFF"/></w:pPr><w:r><w:rPr><w:rFonts w:ascii="Aptos"/><w:color w:val="112233"/><w:u w:val="single" w:color="445566"/><w:shd w:val="clear" w:fill="FFFFFF"/></w:rPr><w:t>Superseded theme members</w:t></w:r></w:p></w:body>
            """,
            """
            <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:pPr><w:shd w:val="clear" w:fill="FFFFFF" w:themeFill="accent3"/></w:pPr><w:rPr><w:rFonts w:ascii="Aptos" w:asciiTheme="minorHAnsi"/><w:color w:val="112233" w:themeColor="accent1"/><w:u w:val="single" w:color="445566" w:themeColor="accent2"/><w:shd w:val="clear" w:fill="FFFFFF" w:themeFill="accent3"/></w:rPr></w:style>
            """
        );
        var package = new OpcPackageReader().Read(stream);
        var semantic = new WordSemanticProjector().Project(package);

        var plan = new WordFormatterPlanner().Plan(
            package,
            semantic,
            package.Fingerprint,
            [WordFormatterPolicy.RemoveRedundantDirectFormatting]
        );

        Assert.False(plan.HasChanges);
        Assert.Empty(plan.Changes);
        Assert.Equal(5, plan.CompositeCandidateProofs);
    }

    [Fact]
    public void KeepsUnsafeCompositeAndStillRemovesAnIndependentProvenScalar()
    {
        using var stream = BuildPackage(
            """
            <w:body><w:p><w:pPr><w:pStyle w:val="Normal"/><w:jc w:val="center"/></w:pPr><w:r><w:rPr><w:rFonts w:ascii="Aptos"/></w:rPr><w:t>Mixed proof</w:t></w:r></w:p></w:body>
            """,
            """
            <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:pPr><w:jc w:val="center"/></w:pPr><w:rPr><w:rFonts w:ascii="Aptos" w:asciiTheme="minorHAnsi"/></w:rPr></w:style>
            """
        );
        var package = new OpcPackageReader().Read(stream);
        var semantic = new WordSemanticProjector().Project(package);

        var plan = new WordFormatterPlanner().Plan(
            package,
            semantic,
            package.Fingerprint,
            [WordFormatterPolicy.RemoveRedundantDirectFormatting]
        );

        Assert.True(plan.HasChanges);
        Assert.Equal(["jc"], plan.Changes.Select(change => change.PropertyElementName));
        Assert.Equal(1, plan.CompositeCandidateProofs);
    }

    [Fact]
    public void FailsClosedPastTheCompositeProofLimit()
    {
        using var stream = BuildPackage(
            """
            <w:body>
              <w:p><w:pPr><w:pStyle w:val="Normal"/></w:pPr><w:r><w:rPr><w:color w:val="112233"/></w:rPr><w:t>A</w:t></w:r></w:p>
              <w:p><w:pPr><w:pStyle w:val="Normal"/></w:pPr><w:r><w:rPr><w:color w:val="112233"/></w:rPr><w:t>B</w:t></w:r></w:p>
            </w:body>
            """,
            """
            <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:rPr><w:color w:val="112233"/></w:rPr></w:style>
            """
        );
        var package = new OpcPackageReader().Read(stream);
        var semantic = new WordSemanticProjector().Project(package);
        var planner = new WordFormatterPlanner(
            new WordFormatterOptions { MaxCompositeCandidateProofs = 1 }
        );

        Assert.Throws<WordFormatterLimitException>(() => planner.Plan(
            package,
            semantic,
            package.Fingerprint,
            [WordFormatterPolicy.RemoveRedundantDirectFormatting]
        ));
    }

    [Fact]
    public void SkipsNodesWhoseFormattingCascadeContainsAnUnresolvedLayer()
    {
        using var stream = BuildPackage(
            """
            <w:body>
              <w:tbl><w:tr><w:tc><w:p><w:pPr><w:pStyle w:val="Normal"/><w:jc w:val="center"/></w:pPr><w:r><w:t>Table</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
              <w:p><w:pPr><w:pStyle w:val="Normal"/></w:pPr><w:ins w:id="1"><w:r><w:rPr><w:color w:val="112233"/></w:rPr><w:t>Revision</w:t></w:r></w:ins></w:p>
            </w:body>
            """,
            """
            <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:pPr><w:jc w:val="center"/></w:pPr><w:rPr><w:color w:val="112233"/></w:rPr></w:style>
            """
        );
        var package = new OpcPackageReader().Read(stream);
        var semantic = new WordSemanticProjector().Project(package);

        var plan = new WordFormatterPlanner().Plan(
            package,
            semantic,
            package.Fingerprint,
            [WordFormatterPolicy.RemoveRedundantDirectFormatting]
        );

        Assert.False(plan.HasChanges);
        Assert.Equal(0, plan.CandidateElementsScanned);
        Assert.Equal(0, plan.CompositeCandidateProofs);
    }

    [Fact]
    public void FailsClosedInsteadOfPartiallyFormattingPastTheNodeLimit()
    {
        using var stream = BuildPackage(
            """
            <w:body>
              <w:p><w:pPr><w:pStyle w:val="Normal"/><w:jc w:val="left"/></w:pPr><w:r><w:t>A</w:t></w:r></w:p>
              <w:p><w:pPr><w:pStyle w:val="Normal"/><w:jc w:val="left"/></w:pPr><w:r><w:t>B</w:t></w:r></w:p>
            </w:body>
            """,
            """
            <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:pPr><w:jc w:val="left"/></w:pPr></w:style>
            """
        );
        var package = new OpcPackageReader().Read(stream);
        var semantic = new WordSemanticProjector().Project(package);
        var planner = new WordFormatterPlanner(
            new WordFormatterOptions { MaxDirectFormattingNodes = 1 }
        );

        Assert.Throws<WordFormatterLimitException>(() => planner.Plan(
            package,
            semantic,
            package.Fingerprint,
            [WordFormatterPolicy.RemoveRedundantDirectFormatting]
        ));
    }

    [Fact]
    public void RejectsAStalePackageFingerprintBeforePlanning()
    {
        using var stream = BuildPackage(
            "<w:body><w:p><w:r><w:t>Stable</w:t></w:r></w:p></w:body>",
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"/>"
        );
        var package = new OpcPackageReader().Read(stream);
        var semantic = new WordSemanticProjector().Project(package);

        Assert.Throws<WordFormatterPreconditionException>(() =>
            new WordFormatterPlanner().Plan(
                package,
                semantic,
                new string('0', 64),
                [WordFormatterPolicy.RemoveRedundantDirectFormatting]
            )
        );
    }

    private static MemoryStream BuildPackage(string bodyXml, string stylesXml)
    {
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
                  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
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
                "word/_rels/document.xml.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """
            );
            WriteEntry(
                archive,
                "word/document.xml",
                $"""
                <w:document xmlns:w="{WordNamespace}">{bodyXml}</w:document>
                """
            );
            WriteEntry(
                archive,
                "word/styles.xml",
                $"""
                <w:styles xmlns:w="{WordNamespace}">{stylesXml}</w:styles>
                """
            );
        }
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream Serialize(OpcPackageMutationBuilder mutation)
    {
        var stream = new MemoryStream();
        new OpcPackageSerializer().Write(stream, mutation, OpcSerializationMode.Preserve);
        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string name, string value)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var output = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(value);
        output.Write(bytes);
    }
}
