using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Extensions;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class OcrWordPackageOperationTests
{
    [Fact]
    public void CandidateGraphDeduplicatesReferencedRasterAndVerifiesItsSignature()
    {
        var path = TemporaryPath();
        try
        {
            CreatePackage(path, figureCount: 2);
            var operation = new OcrWordPackageOperation(Registry(new FixedProvider()));

            var result = operation.Inspect(new OcrCandidateInspectionRequest(path));

            var candidate = Assert.Single(result.Items);
            Assert.Equal(1, result.CandidateCount);
            Assert.Equal(1, result.EligibleCandidateCount);
            Assert.Equal(2, candidate.FigureCount);
            Assert.Equal(2, candidate.ResourceCount);
            Assert.Equal("image/png", candidate.DetectedContentType);
            Assert.True(candidate.SignatureValid);
            Assert.True(candidate.Eligible);
            Assert.Null(candidate.ImageSha256);
            Assert.Null(candidate.SourcePartUri);
            Assert.False(result.Disclosure.ProviderInvoked);
            Assert.False(result.Disclosure.ImageBytesReturned);
            Assert.False(result.Disclosure.NetworkUsed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CandidateInspectionRejectsDeclaredTypeAndPayloadSignatureMismatch()
    {
        var path = TemporaryPath();
        try
        {
            CreatePackage(path, contentType: "image/jpeg");
            var operation = new OcrWordPackageOperation(Registry(new FixedProvider()));

            var result = operation.Inspect(new OcrCandidateInspectionRequest(
                path,
                View: "issues"
            ));

            Assert.Equal(0, result.EligibleCandidateCount);
            Assert.Contains(result.Issues, item =>
                item.Code == "OCR_IMAGE_SIGNATURE_MISMATCH"
            );
            Assert.False(result.Disclosure.SourceReturned);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CandidateCoverageIsFalseForAnUnresolvedEmbeddedImageRelationship()
    {
        var path = TemporaryPath();
        try
        {
            CreatePackage(path, includeImageRelationship: false);
            var operation = new OcrWordPackageOperation(Registry(new FixedProvider()));

            var result = operation.Inspect(new OcrCandidateInspectionRequest(
                path,
                View: "issues"
            ));

            Assert.Equal(0, result.CandidateCount);
            Assert.False(result.CandidateCoverageComplete);
            Assert.Contains(result.Issues, item =>
                item.Code == "OCR_IMAGE_RESOURCE_UNRESOLVED"
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CandidateCoverageIsFalseWhenTheSourceFigureProjectionTruncatesIssues()
    {
        var path = TemporaryPath();
        try
        {
            CreatePackage(path, figureCount: 2);
            var operation = new OcrWordPackageOperation(
                Registry(new FixedProvider()),
                figureOptions: new WordFigureCaptionGraphOptions { MaxIssues = 1 }
            );

            var result = operation.Inspect(new OcrCandidateInspectionRequest(
                path,
                View: "issues"
            ));

            Assert.Equal(1, result.CandidateCount);
            Assert.Empty(result.Items);
            Assert.False(result.CandidateCoverageComplete);
            Assert.False(result.IssuesTruncated);
            Assert.Contains(result.Issues, item =>
                item.Code == "OCR_SOURCE_PROJECTION_ISSUES_TRUNCATED"
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RecognitionIsFingerprintBoundContentGatedAndProvenanceComplete()
    {
        var path = TemporaryPath();
        try
        {
            CreatePackage(path);
            var provider = new FixedProvider();
            var operation = new OcrWordPackageOperation(Registry(provider));
            var inspected = operation.Inspect(new OcrCandidateInspectionRequest(path));
            var candidateId = Assert.Single(inspected.Items).CandidateId;

            var result = operation.Recognize(new OcrRecognitionRequest(
                path,
                inspected.PackageFingerprint.ToUpperInvariant(),
                [candidateId],
                Languages: ["eng"],
                Detail: "words",
                IncludeText: true,
                IncludeHashes: true,
                MinimumMeanConfidence: 0.8
            ));

            Assert.Equal(1, provider.InvocationCount);
            var item = Assert.Single(result.Results);
            Assert.Equal("HELLO WORLD", item.Text);
            Assert.Equal(2, item.WordCount);
            Assert.Equal(1, item.LineCount);
            Assert.Equal(1, item.ReturnedLineCount);
            Assert.False(item.LinesTruncated);
            Assert.NotNull(item.SourceImageSha256);
            Assert.NotNull(item.TextSha256);
            Assert.Equal(0.925, item.Confidence.Mean!.Value, precision: 6);
            Assert.True(item.Confidence.MeetsRequiredMinimum);
            Assert.Equal("fake-ocr", item.Provenance.ProviderName);
            Assert.Equal(new string('a', 64), item.Provenance.ProviderBinarySha256);
            Assert.Equal(new string('b', 64), item.Provenance.ModelSetSha256);
            Assert.False(item.Provenance.NetworkUsed);
            Assert.True(result.Disclosure.SourceFingerprintVerified);
            Assert.True(result.Disclosure.SourceFileHashReverified);
            Assert.True(result.Disclosure.TextReturned);
            Assert.True(result.Disclosure.GeometryReturned);
            Assert.False(result.Disclosure.ImageBytesReturned);
            Assert.False(result.Disclosure.RawProviderOutputReturned);
            Assert.False(result.Disclosure.RawXmlReturned);
            Assert.False(result.Disclosure.MutationPerformed);
            Assert.False(result.Disclosure.WordOpened);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RecognitionKeepsTextAndGeometryOutOfCompactSummaryByDefault()
    {
        var path = TemporaryPath();
        try
        {
            CreatePackage(path);
            var operation = new OcrWordPackageOperation(Registry(new FixedProvider()));
            var inspected = operation.Inspect(new OcrCandidateInspectionRequest(path));

            var result = operation.Recognize(new OcrRecognitionRequest(
                path,
                inspected.PackageFingerprint,
                [Assert.Single(inspected.Items).CandidateId]
            ));

            var item = Assert.Single(result.Results);
            Assert.Null(item.Text);
            Assert.Null(item.TextSha256);
            Assert.Null(item.SourceImageSha256);
            Assert.Empty(item.Lines);
            Assert.False(result.Disclosure.TextReturned);
            Assert.False(result.Disclosure.GeometryReturned);
            var json = WordToolkitOperationJson.Serialize(result);
            Assert.DoesNotContain("HELLO", json, StringComparison.Ordinal);
            Assert.DoesNotContain("provider_executable", json, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RecognitionBoundsTextLinesAndWordsAndKeepsResultIdentityDeterministic()
    {
        var path = TemporaryPath();
        try
        {
            CreatePackage(path);
            var provider = new FixedProvider(extraLine: true);
            var operation = new OcrWordPackageOperation(Registry(provider));
            var inspected = operation.Inspect(new OcrCandidateInspectionRequest(path));
            var request = new OcrRecognitionRequest(
                path,
                inspected.PackageFingerprint,
                [Assert.Single(inspected.Items).CandidateId],
                Detail: "words",
                IncludeText: true,
                IncludeHashes: true,
                MaxReturnedTextCharacters: 5,
                MaxReturnedLines: 1,
                MaxReturnedWordsPerLine: 1
            );

            var first = operation.Recognize(request);
            var second = operation.Recognize(request);
            var differentLayout = operation.Recognize(request with
            {
                LayoutHint = WordOcrLayoutHint.SingleBlock,
            });

            var item = Assert.Single(first.Results);
            Assert.Equal("HELLO", item.Text);
            Assert.True(item.TextTruncated);
            Assert.Equal(2, item.LineCount);
            Assert.Equal(4, item.WordCount);
            Assert.Equal(1, item.ReturnedLineCount);
            Assert.True(item.LinesTruncated);
            var line = Assert.Single(item.Lines);
            Assert.Equal("HELLO", line.Text);
            Assert.True(line.TextTruncated);
            Assert.Equal(1, line.ReturnedWordCount);
            Assert.True(line.WordsTruncated);
            Assert.Equal("HELLO", Assert.Single(line.Words).Text);
            Assert.Equal(item.ResultId, Assert.Single(second.Results).ResultId);
            Assert.Equal(item.TextSha256, Assert.Single(second.Results).TextSha256);
            Assert.NotEqual(
                item.ResultId,
                Assert.Single(differentLayout.Results).ResultId
            );
            Assert.Equal(3, provider.InvocationCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LocalOnlyPrivacyRejectsNetworkOrCredentialProviderBeforeInvocation()
    {
        var path = TemporaryPath();
        try
        {
            CreatePackage(path);
            var provider = new FixedProvider();
            var operation = new OcrWordPackageOperation(Registry(
                provider,
                WordToolkitExtensionPermission.ReadDocumentContent
                    | WordToolkitExtensionPermission.Network
                    | WordToolkitExtensionPermission.Credentials
            ));
            var inspected = operation.Inspect(new OcrCandidateInspectionRequest(path));

            var exception = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Recognize(new OcrRecognitionRequest(
                    path,
                    inspected.PackageFingerprint,
                    [Assert.Single(inspected.Items).CandidateId]
                ))
            );

            Assert.Equal("OCR_PRIVACY_POLICY_DENIED", exception.Code);
            Assert.Equal(0, provider.InvocationCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RecognitionRejectsLowConfidenceInvalidProviderAndSourceDrift()
    {
        var lowPath = TemporaryPath();
        var invalidPath = TemporaryPath();
        var driftPath = TemporaryPath();
        try
        {
            CreatePackage(lowPath);
            var lowOperation = new OcrWordPackageOperation(Registry(new FixedProvider()));
            var lowInspect = lowOperation.Inspect(new OcrCandidateInspectionRequest(lowPath));
            Assert.Equal(
                "OCR_CONFIDENCE_TOO_LOW",
                Assert.Throws<WordToolkitOperationException>(() =>
                    lowOperation.Recognize(new OcrRecognitionRequest(
                        lowPath,
                        lowInspect.PackageFingerprint,
                        [Assert.Single(lowInspect.Items).CandidateId],
                        MinimumMeanConfidence: 0.99
                    ))
                ).Code
            );

            CreatePackage(invalidPath);
            var invalidOperation = new OcrWordPackageOperation(Registry(
                new FixedProvider(invalidGeometry: true)
            ));
            var invalidInspect = invalidOperation.Inspect(
                new OcrCandidateInspectionRequest(invalidPath)
            );
            Assert.Equal(
                "OCR_PROVIDER_CONTRACT_VIOLATION",
                Assert.Throws<WordToolkitOperationException>(() =>
                    invalidOperation.Recognize(new OcrRecognitionRequest(
                        invalidPath,
                        invalidInspect.PackageFingerprint,
                        [Assert.Single(invalidInspect.Items).CandidateId]
                    ))
                ).Code
            );

            CreatePackage(driftPath);
            var driftProvider = new FixedProvider(onInvoke: () =>
                File.AppendAllText(driftPath, "drift", Encoding.UTF8)
            );
            var driftOperation = new OcrWordPackageOperation(Registry(driftProvider));
            var driftInspect = driftOperation.Inspect(
                new OcrCandidateInspectionRequest(driftPath)
            );
            Assert.Equal(
                "VERSION_CONFLICT",
                Assert.Throws<WordToolkitOperationException>(() =>
                    driftOperation.Recognize(new OcrRecognitionRequest(
                        driftPath,
                        driftInspect.PackageFingerprint,
                        [Assert.Single(driftInspect.Items).CandidateId]
                    ))
                ).Code
            );
        }
        finally
        {
            File.Delete(lowPath);
            File.Delete(invalidPath);
            File.Delete(driftPath);
        }
    }

    [Fact]
    public void ParserIsClosedAndRequiresOneExplicitSelectionMode()
    {
        Assert.Equal(
            "INVALID_INPUT",
            Assert.Throws<WordToolkitOperationException>(() =>
                OcrOperationJson.ParseInspectRequest(
                    "{\"local_path\":\"x.docx\",\"image_bytes\":true}"
                )
            ).Code
        );
        var emptyProviderPath = OcrOperationJson.ParseRecognizeRequest(
            "{\"local_path\":\"x.docx\",\"expected_package_fingerprint\":\""
                + new string('a', 64)
                + "\",\"candidate_ids\":[\"wocr_aaaaaaaaaaaaaaaaaaaaaaaa\"],\"provider_executable_path\":\"\"}"
        );
        var operation = new OcrWordPackageOperation(Registry(new FixedProvider()));
        Assert.Equal(
            "INVALID_INPUT",
            Assert.Throws<WordToolkitOperationException>(() =>
                operation.Recognize(emptyProviderPath)
            ).Code
        );
        var noSelection = OcrOperationJson.ParseRecognizeRequest(
            "{\"local_path\":\"x.docx\",\"expected_package_fingerprint\":\""
                + new string('a', 64)
                + "\"}"
        );
        Assert.Equal(
            "INVALID_INPUT",
            Assert.Throws<WordToolkitOperationException>(() =>
                operation.Recognize(noSelection)
            ).Code
        );
        Assert.Equal(
            "INVALID_INPUT",
            Assert.Throws<WordToolkitOperationException>(() =>
                OcrOperationJson.ParseRecognizeRequest(
                    "{\"local_path\":\"x.docx\",\"expected_package_fingerprint\":\""
                        + new string('a', 64)
                        + "\",\"candidate_ids\":[],\"candidate_ids\":[]}"
                )
            ).Code
        );
    }

    private static WordToolkitExtensionRegistry Registry(
        IWordOcrProvider provider,
        WordToolkitExtensionPermission permissions =
            WordToolkitExtensionPermission.ReadDocumentContent
    )
    {
        var limits = new WordToolkitExtensionResourceLimits(
            32L * 1024 * 1024,
            8L * 1024 * 1024,
            1,
            120_000
        );
        var policy = WordToolkitExtensionPolicy.BuiltInOnly(
            ["wordtoolkit.test.ocr"],
            [new WordToolkitExtensionInterfaceSupport(
                WordOcrProviderContract.InterfaceContract,
                WordOcrProviderContract.InterfaceVersion,
                WordToolkitExtensionKind.OcrProvider
            )],
            permissions,
            limits
        );
        var builder = new WordToolkitExtensionRegistryBuilder(policy);
        builder.Register<IWordOcrProvider>(
            new WordToolkitExtensionDescriptor(
                "wordtoolkit.test.ocr",
                "Test OCR",
                "WordToolkit tests",
                "1.0.0",
                "1.0",
                WordToolkitExtensionTrust.BuiltIn,
                WordToolkitExtensionIsolation.TrustedInProcess
            ),
            new WordToolkitExtensionCapabilityDescriptor(
                OcrWordPackageContract.DefaultProviderCapabilityId,
                WordToolkitExtensionKind.OcrProvider,
                WordOcrProviderContract.InterfaceContract,
                WordOcrProviderContract.InterfaceVersion,
                permissions,
                limits,
                WordToolkitExtensionTimeoutEnforcement.Cooperative,
                Deterministic: true,
                Idempotent: true,
                ReturnsDocumentContent: true
            ),
            provider
        );
        return builder.Build();
    }

    private sealed class FixedProvider(
        bool invalidGeometry = false,
        Action? onInvoke = null,
        bool extraLine = false
    ) : IWordOcrProvider
    {
        public int InvocationCount { get; private set; }

        public WordOcrProviderResult Recognize(
            WordOcrProviderRequest request,
            CancellationToken cancellationToken = default
        )
        {
            InvocationCount++;
            onInvoke?.Invoke();
            var words = new[]
            {
                new WordOcrProviderWord(
                    "HELLO",
                    0.95,
                    new WordOcrPixelBox(0, 0, invalidGeometry ? 101 : 40, 20)
                ),
                new WordOcrProviderWord(
                    "WORLD",
                    0.9,
                    new WordOcrPixelBox(50, 0, 50, 20)
                ),
            };
            var lines = new List<WordOcrProviderLine>
            {
                new(
                    "HELLO WORLD",
                    0.925,
                    new WordOcrPixelBox(0, 0, 100, 20),
                    words
                ),
            };
            if (extraLine)
            {
                lines.Add(new WordOcrProviderLine(
                    "SECOND LINE",
                    0.85,
                    new WordOcrPixelBox(0, 25, 100, 20),
                    [
                        new WordOcrProviderWord(
                            "SECOND",
                            0.86,
                            new WordOcrPixelBox(0, 25, 60, 20)
                        ),
                        new WordOcrProviderWord(
                            "LINE",
                            0.84,
                            new WordOcrPixelBox(65, 25, 35, 20)
                        ),
                    ]
                ));
            }
            return new WordOcrProviderResult(
                100,
                50,
                string.Join('\n', lines.Select(item => item.Text)),
                lines,
                [],
                new WordOcrProviderProvenance(
                    "fake-ocr",
                    "1.0.0",
                    new string('a', 64),
                    new string('b', 64),
                    request.Languages.ToArray(),
                    "normalized_0_to_1",
                    NetworkUsed: false,
                    DeterministicForBoundInputs: true
                )
            );
        }
    }

    private static string TemporaryPath() => Path.Combine(
        Path.GetTempPath(),
        $"wordtoolkit-ocr-{Guid.NewGuid():N}.docx"
    );

    private static void CreatePackage(
        string path,
        int figureCount = 1,
        string contentType = "image/png",
        bool includeImageRelationship = true
    )
    {
        var figures = string.Concat(Enumerable.Range(1, figureCount).Select(index => $$"""
            <w:p><w:r><w:drawing>
              <wp:inline xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"
                xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <wp:extent cx="914400" cy="457200"/>
                <wp:docPr id="{{index}}" name="Scan {{index}}"/>
                <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                  <pic:pic><pic:nvPicPr><pic:cNvPr id="0" name="scan.png"/><pic:cNvPicPr/></pic:nvPicPr>
                    <pic:blipFill><a:blip r:embed="rIdImage"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
                    <pic:spPr><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>
                  </pic:pic>
                </a:graphicData></a:graphic>
              </wp:inline>
            </w:drawing></w:r></w:p>
            """));
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        Add(archive, "[Content_Types].xml", $$"""
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Default Extension="png" ContentType="{{contentType}}"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """);
        Add(archive, "_rels/.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        Add(archive, "word/document.xml", $$"""
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>{{figures}}<w:sectPr/></w:body>
            </w:document>
            """);
        Add(archive, "word/_rels/document.xml.rels", includeImageRelationship ? """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdImage" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/>
            </Relationships>
            """ : """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>
            """);
        Add(
            archive,
            "word/media/image1.png",
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9WlSAAAAAASUVORK5CYII="
            )
        );
    }

    private static void Add(ZipArchive archive, string name, string content) =>
        Add(archive, name, Encoding.UTF8.GetBytes(content));

    private static void Add(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var destination = entry.Open();
        destination.Write(content);
    }
}
