using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Validation;
using WordToolkit.OpenXmlSdk;

namespace WordToolkit.Engine.Tests;

public sealed class TemplateStyleAlignmentWordPackageOperationTests
{
    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void InspectsPlansAndAtomicallyAppliesWithoutMutatingOrAttachingTemplate()
    {
        using var fixture = new Fixture();
        var targetPath = fixture.Write("target.docx", BuildPackage(
            Style("Normal", "<w:rPr><w:sz w:val=\"22\"/></w:rPr>"),
            Style("Base", "<w:rPr><w:b w:val=\"0\"/></w:rPr>"),
            Style("Heading", "<w:basedOn w:val=\"Base\"/><w:rPr><w:i/></w:rPr>"),
            Style("TargetOnly", "<w:rPr><w:u w:val=\"single\"/></w:rPr>")
        ));
        var templatePath = fixture.Write("template.dotx", BuildTemplatePackage(
            Style("Normal", "<w:rPr><w:sz w:val=\"22\"/></w:rPr>"),
            Style("Base", "<w:rPr><w:b/></w:rPr>"),
            Style("Heading", "<w:basedOn w:val=\"Base\"/><w:rPr><w:i/></w:rPr>"),
            mainContentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml"
        ));
        var reader = new OpcPackageReader();
        var target = reader.Read(targetPath);
        var template = reader.Read(templatePath);
        var templateHash = Sha256(File.ReadAllBytes(templatePath));
        var operation = new TemplateStyleAlignmentWordPackageOperation(
            new MicrosoftOpenXmlPackageValidator()
        );

        var inspection = operation.Inspect(new TemplateStyleAlignmentInspectRequest(
            targetPath,
            templatePath,
            target.Fingerprint,
            template.Fingerprint,
            IncludeDependencies: true
        ));
        var heading = Assert.Single(inspection.Candidates, item =>
            item.StyleId == "Heading"
        );
        Assert.Contains("Base", heading.DependencyStyleIds!);
        Assert.False(inspection.LocalizedNameMatchingUsed);
        Assert.False(inspection.TemplateAttached);
        Assert.False(inspection.TemplateMutationPerformed);
        Assert.False(inspection.DocumentTextReturned);
        Assert.False(inspection.RawXmlReturned);

        var commands = new[]
        {
            new TemplateStyleAlignmentCommandRequest(heading.Id, heading.Fingerprint),
        };
        var plan = operation.Plan(new TemplateStyleAlignmentPlanRequest(
            targetPath,
            templatePath,
            target.Fingerprint,
            template.Fingerprint,
            commands,
            IncludeDetails: true
        ));
        Assert.True(plan.CanApply);
        Assert.True(plan.EngineValidation.Passed);
        Assert.True(plan.CandidateValidation.Performed);
        Assert.True(plan.CandidateValidation.NoNewErrors);
        Assert.Equal(["Base", "Heading"], plan.AlignedStyleIds);

        var applied = operation.Apply(new TemplateStyleAlignmentApplyRequest(
            targetPath,
            templatePath,
            target.Fingerprint,
            template.Fingerprint,
            plan.PlanId,
            commands,
            KeepBackup: true
        ));

        Assert.True(applied.Applied);
        Assert.True(applied.MutationPerformed);
        Assert.Equal(plan.ResultPackageFingerprint, applied.PackageFingerprint);
        Assert.Equal(template.Fingerprint, applied.TemplatePackageFingerprint);
        Assert.NotNull(applied.BackupPath);
        Assert.True(File.Exists(applied.BackupPath));
        Assert.Equal(target.Fingerprint, reader.Read(applied.BackupPath!).Fingerprint);
        Assert.Equal(templateHash, Sha256(File.ReadAllBytes(templatePath)));
        Assert.Equal(template.Fingerprint, reader.Read(templatePath).Fingerprint);
        Assert.DoesNotContain(applied.ChangedEntryNames, name =>
            name.Contains("settings", StringComparison.OrdinalIgnoreCase)
        );

        var changed = reader.Read(targetPath);
        var styles = XDocument.Parse(Encoding.UTF8.GetString(
            changed.Parts["/word/styles.xml"].Entry.Content.Span
        ));
        XNamespace w = WordNamespace;
        var elements = styles.Root!.Elements(w + "style").ToArray();
        Assert.Contains(elements, element =>
            element.Attribute(w + "styleId")?.Value == "TargetOnly"
        );
        Assert.NotNull(elements.Single(element =>
            element.Attribute(w + "styleId")?.Value == "Base"
        ).Descendants(w + "b").SingleOrDefault());
    }

    [Fact]
    public void ApplyRequiresValidatorAndBlocksSignedTarget()
    {
        using var fixture = new Fixture();
        var targetPath = fixture.Write("target.docx", BuildPackage(
            Style("Normal", string.Empty),
            Style("Focus", "<w:rPr><w:b w:val=\"0\"/></w:rPr>")
        ));
        var templatePath = fixture.Write("template.docx", BuildPackage(
            Style("Normal", string.Empty),
            Style("Focus", "<w:rPr><w:b/></w:rPr>")
        ));
        var reader = new OpcPackageReader();
        var target = reader.Read(targetPath);
        var template = reader.Read(templatePath);
        var noValidator = new TemplateStyleAlignmentWordPackageOperation();
        var inspection = noValidator.Inspect(Request(targetPath, templatePath, target, template));
        var candidate = Assert.Single(inspection.Candidates, item => item.StyleId == "Focus");
        var commands = Commands(candidate);
        var plan = noValidator.Plan(new TemplateStyleAlignmentPlanRequest(
            targetPath, templatePath, target.Fingerprint, template.Fingerprint, commands
        ));
        Assert.Contains("schema_validator_unavailable", plan.ApplyBlockedReasons);
        Assert.Equal(
            "VALIDATOR_REQUIRED",
            Assert.Throws<WordToolkitOperationException>(() => noValidator.Apply(
                new TemplateStyleAlignmentApplyRequest(
                    targetPath,
                    templatePath,
                    target.Fingerprint,
                    template.Fingerprint,
                    plan.PlanId,
                    commands
                )
            )).Code
        );
        Assert.Equal(target.Fingerprint, reader.Read(targetPath).Fingerprint);

        var signedPath = fixture.Write("signed.docx", BuildPackage(
            Style("Normal", string.Empty),
            Style("Focus", "<w:rPr><w:b w:val=\"0\"/></w:rPr>"),
            includeSignatureMarker: true
        ));
        var signed = reader.Read(signedPath);
        var signedOperation = new TemplateStyleAlignmentWordPackageOperation(
            new PassingValidator()
        );
        var signedInspection = signedOperation.Inspect(Request(
            signedPath, templatePath, signed, template
        ));
        var signedCandidate = Assert.Single(signedInspection.Candidates, item =>
            item.StyleId == "Focus"
        );
        var signedCommands = Commands(signedCandidate);
        var signedPlan = signedOperation.Plan(new TemplateStyleAlignmentPlanRequest(
            signedPath,
            templatePath,
            signed.Fingerprint,
            template.Fingerprint,
            signedCommands
        ));
        Assert.Contains("target_digital_signature_present", signedPlan.ApplyBlockedReasons);
        Assert.Equal(
            "SIGNED_PACKAGE",
            Assert.Throws<WordToolkitOperationException>(() => signedOperation.Apply(
                new TemplateStyleAlignmentApplyRequest(
                    signedPath,
                    templatePath,
                    signed.Fingerprint,
                    template.Fingerprint,
                    signedPlan.PlanId,
                    signedCommands
                )
            )).Code
        );
        Assert.Equal(signed.Fingerprint, reader.Read(signedPath).Fingerprint);
    }

    [Fact]
    public void ApplyDetectsTemplateDriftAfterCandidateValidation()
    {
        using var fixture = new Fixture();
        var targetPath = fixture.Write("target.docx", BuildPackage(
            Style("Normal", string.Empty),
            Style("Focus", "<w:rPr><w:b w:val=\"0\"/></w:rPr>")
        ));
        var originalTemplateBytes = BuildPackage(
            Style("Normal", string.Empty),
            Style("Focus", "<w:rPr><w:b/></w:rPr>")
        );
        var templatePath = fixture.Write("template.docx", originalTemplateBytes);
        var reader = new OpcPackageReader();
        var target = reader.Read(targetPath);
        var template = reader.Read(templatePath);
        var inspectionOperation = new TemplateStyleAlignmentWordPackageOperation();
        var candidate = Assert.Single(inspectionOperation.Inspect(Request(
            targetPath, templatePath, target, template
        )).Candidates, item => item.StyleId == "Focus");
        var commands = Commands(candidate);
        var ordinaryPlan = inspectionOperation.Plan(new TemplateStyleAlignmentPlanRequest(
            targetPath, templatePath, target.Fingerprint, template.Fingerprint, commands
        ));
        var operation = new TemplateStyleAlignmentWordPackageOperation(
            new TemplateMutatingValidator(templatePath, BuildPackage(
                Style("Normal", string.Empty),
                Style("Focus", "<w:rPr><w:i/></w:rPr>")
            ))
        );

        var exception = Assert.Throws<WordToolkitOperationException>(() => operation.Apply(
            new TemplateStyleAlignmentApplyRequest(
                targetPath,
                templatePath,
                target.Fingerprint,
                template.Fingerprint,
                ordinaryPlan.PlanId,
                commands
            )
        ));

        Assert.Equal("VERSION_CONFLICT", exception.Code);
        Assert.True(exception.Retryable);
        Assert.Equal(target.Fingerprint, reader.Read(targetPath).Fingerprint);
        Assert.NotEqual(template.Fingerprint, reader.Read(templatePath).Fingerprint);
    }

    [Fact]
    public void JsonRejectsUnknownAndDuplicateFields()
    {
        const string fingerprint =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var unknown = $$"""
            {"target_path":"a.docx","template_path":"b.docx","expected_target_package_fingerprint":"{{fingerprint}}","expected_template_package_fingerprint":"{{fingerprint}}","max_items":10,"surprise":true}
            """;
        Assert.Equal(
            "INVALID_INPUT",
            Assert.Throws<WordToolkitOperationException>(() =>
                TemplateStyleAlignmentOperationJson.ParseInspectRequest(unknown)
            ).Code
        );
        var duplicate = $$"""
            {"target_path":"a.docx","target_path":"b.docx","template_path":"c.docx","expected_target_package_fingerprint":"{{fingerprint}}","expected_template_package_fingerprint":"{{fingerprint}}"}
            """;
        Assert.Equal(
            "INVALID_INPUT",
            Assert.Throws<WordToolkitOperationException>(() =>
                TemplateStyleAlignmentOperationJson.ParseInspectRequest(duplicate)
            ).Code
        );
    }

    private static TemplateStyleAlignmentInspectRequest Request(
        string targetPath,
        string templatePath,
        OpcPackageSnapshot target,
        OpcPackageSnapshot template
    ) => new(targetPath, templatePath, target.Fingerprint, template.Fingerprint);

    private static TemplateStyleAlignmentCommandRequest[] Commands(
        TemplateStyleAlignmentInspectionCandidate candidate
    ) => [new(candidate.Id, candidate.Fingerprint)];

    private static string Style(string id, string body) =>
        $"<w:style w:type=\"paragraph\" w:styleId=\"{id}\"><w:name w:val=\"{id}\"/>{body}</w:style>";

    private static byte[] BuildPackage(
        params string[] styles
    ) => BuildPackage(styles, null, false);

    private static byte[] BuildPackage(
        string style,
        bool includeSignatureMarker
    ) => BuildPackage([style], null, includeSignatureMarker);

    private static byte[] BuildPackage(
        string style1,
        string style2,
        bool includeSignatureMarker
    ) => BuildPackage([style1, style2], null, includeSignatureMarker);

    private static byte[] BuildPackage(
        string style1,
        string style2,
        string style3,
        string style4
    ) => BuildPackage([style1, style2, style3, style4], null, false);

    private static byte[] BuildTemplatePackage(
        string style1,
        string style2,
        string style3,
        string mainContentType
    ) => BuildPackage([style1, style2, style3], mainContentType, false);

    private static byte[] BuildPackage(
        IReadOnlyList<string> styles,
        string? mainContentType,
        bool includeSignatureMarker
    )
    {
        mainContentType ??=
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml", $"<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"sig\" ContentType=\"application/vnd.openxmlformats-package.digital-signature-xmlsignature+xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"{mainContentType}\"/><Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/></Types>");
            Add(archive, "_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rDoc\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add(archive, "word/_rels/document.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            Add(archive, "word/document.xml", $"<w:document xmlns:w=\"{WordNamespace}\"><w:body><w:p><w:pPr><w:pStyle w:val=\"Normal\"/></w:pPr><w:r><w:t>content</w:t></w:r></w:p><w:sectPr/></w:body></w:document>");
            Add(archive, "word/styles.xml", $"<w:styles xmlns:w=\"{WordNamespace}\">{string.Concat(styles)}</w:styles>");
            if (includeSignatureMarker)
            {
                Add(archive, "_xmlsignatures/sig1.sig", "signature-marker");
            }
        }
        return stream.ToArray();
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void Add(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var output = entry.Open();
        output.Write(Encoding.UTF8.GetBytes(content));
    }

    private sealed class PassingValidator : IWordPackageCandidateValidator
    {
        public WordPackageCandidateValidationReport Validate(
            Stream baselinePackage,
            Stream candidatePackage,
            CancellationToken cancellationToken = default
        ) => new(
            Performed: true,
            CandidateValid: true,
            NoNewErrors: true,
            ErrorCount: 0,
            BaselineErrorCount: 0,
            CandidateErrorCount: 0,
            ErrorsTruncated: false,
            NotPerformedReason: null,
            Issues: Array.Empty<WordPackageValidationIssue>()
        );
    }

    private sealed class TemplateMutatingValidator(string path, byte[] replacement)
        : IWordPackageCandidateValidator
    {
        public WordPackageCandidateValidationReport Validate(
            Stream baselinePackage,
            Stream candidatePackage,
            CancellationToken cancellationToken = default
        )
        {
            File.WriteAllBytes(path, replacement);
            return new PassingValidator().Validate(
                baselinePackage,
                candidatePackage,
                cancellationToken
            );
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-template-style-" + Guid.NewGuid().ToString("N")
        );

        public Fixture() => Directory.CreateDirectory(_directory);

        public string Write(string name, byte[] bytes)
        {
            var path = Path.Combine(_directory, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
