using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class InspectWordPackageOperationTests
{
    [Fact]
    public void EncryptedOoxmlHasADistinctFailureInsteadOfPretendingToBeCorruptZip()
    {
        using var encrypted = new MemoryStream(
            InspectOoxmlEncryptionOperationTests.CompoundFile(4, 4)
        );

        var exception = Assert.Throws<WordToolkitOperationException>(() =>
            new InspectWordPackageOperation().Execute(encrypted, "protected.docx")
        );

        Assert.Equal("DOCUMENT_ENCRYPTED", exception.Code);
        Assert.Contains(
            "inspect_ooxml_encryption",
            exception.Reason,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void PathAndStreamUseTheSameCanonicalReadOnlyContract()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            const string externalTarget = "https://private.example/client-a";
            var path = Path.Combine(directory, "sample.docx");
            File.WriteAllBytes(path, PackageBytes(externalTarget: externalTarget));
            var before = SHA256.HashData(File.ReadAllBytes(path));
            var operation = new InspectWordPackageOperation();

            var pathResult = operation.Execute(
                new InspectWordPackageRequest(path, IncludeDetails: true, MaxItems: 20)
            );
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            stream.Position = 7;
            var streamResult = operation.Execute(
                stream,
                "sample.docx",
                includeDetails: true,
                maxItems: 20
            );

            Assert.Equal(7, stream.Position);
            Assert.Equal(
                WordToolkitOperationJson.Serialize(pathResult),
                WordToolkitOperationJson.Serialize(streamResult)
            );
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal(InspectWordPackageContract.Contract, pathResult.OperationContract);
            Assert.True(pathResult.ValidWordPackage);
            var relationship = Assert.Single(
                pathResult.Details!.Relationships,
                item => item.TargetMode == "external"
            );
            Assert.True(relationship.ExternalTargetRedacted);
            Assert.Null(relationship.ResolvedTargetPartUri);

            var json = WordToolkitOperationJson.Serialize(pathResult);
            Assert.Contains("\"operation_contract\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("OperationContract", json, StringComparison.Ordinal);
            Assert.DoesNotContain(externalTarget, json, StringComparison.Ordinal);
            Assert.DoesNotContain(path, json, StringComparison.OrdinalIgnoreCase);
            var roundTrip = WordToolkitOperationJson.Deserialize<InspectWordPackageResult>(json);
            Assert.Equal(json, WordToolkitOperationJson.Serialize(roundTrip));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PathInspectionWorksWhileAnotherHandleSharesReadWrite()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "open-in-word.docx");
            File.WriteAllBytes(path, PackageBytes());
            using var openDocument = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.ReadWrite
            );

            var result = new InspectWordPackageOperation().Execute(
                new InspectWordPackageRequest(path, IncludeDetails: true, MaxItems: 20)
            );

            Assert.NotEqual("IO_ERROR", result.Diagnostics.Items.FirstOrDefault()?.Code);
            Assert.True(result.ValidWordPackage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PathInspectionRetriesWhenTheSourceChangesDuringSnapshotCapture()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "saving-in-word.docx");
            File.WriteAllBytes(path, PackageBytes());
            var replacement = PackageBytes(externalTarget: "https://example.test/replaced");
            using var expectedStream = new MemoryStream(replacement);
            var expected = new InspectWordPackageOperation().Execute(
                expectedStream,
                "saving-in-word.docx"
            );
            var copyCount = 0;
            var operation = new InspectWordPackageOperation(
                limits: null,
                afterSnapshotCopy: attempt =>
                {
                    copyCount++;
                    if (attempt == 1)
                    {
                        File.WriteAllBytes(path, replacement);
                    }
                }
            );

            var result = operation.Execute(new InspectWordPackageRequest(path));

            Assert.Equal(2, copyCount);
            Assert.Equal(expected.PackageFingerprint, result.PackageFingerprint);
            Assert.Equal(replacement.Length, result.Bytes);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PathInspectionFailsRetryablyWhenTheSourceNeverStabilizes()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "continuously-saving.docx");
            var first = PackageBytes();
            var second = PackageBytes(externalTarget: "https://example.test/second");
            File.WriteAllBytes(path, first);
            var operation = new InspectWordPackageOperation(
                limits: null,
                afterSnapshotCopy: attempt =>
                    File.WriteAllBytes(path, attempt % 2 == 0 ? first : second)
            );

            var exception = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Execute(new InspectWordPackageRequest(path))
            );

            Assert.Equal("SOURCE_CHANGED", exception.Code);
            Assert.True(exception.Retryable);
            Assert.DoesNotContain(path, exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(path, exception.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("", 10)]
    [InlineData("sample.txt", 10)]
    [InlineData("sample.docx", 0)]
    [InlineData("sample.docx", 201)]
    public void RejectsInvalidArgumentsWithStableCode(string path, long maxItems)
    {
        var exception = Assert.Throws<WordToolkitOperationException>(() =>
            new InspectWordPackageOperation().Execute(
                new InspectWordPackageRequest(path, MaxItems: maxItems)
            )
        );

        Assert.Equal("INVALID_INPUT", exception.Code);
        Assert.False(exception.Retryable);
    }

    [Fact]
    public void MapsMissingCorruptAndBoundedPackagesToStableCodes()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var missing = Assert.Throws<WordToolkitOperationException>(() =>
                new InspectWordPackageOperation().Execute(
                    new InspectWordPackageRequest(Path.Combine(directory, "missing.docx"))
                )
            );
            Assert.Equal("NOT_FOUND", missing.Code);

            var corruptPath = Path.Combine(directory, "corrupt.docx");
            File.WriteAllText(corruptPath, "not a ZIP package");
            var corrupt = Assert.Throws<WordToolkitOperationException>(() =>
                new InspectWordPackageOperation().Execute(
                    new InspectWordPackageRequest(corruptPath)
                )
            );
            Assert.Equal("INVALID_PACKAGE", corrupt.Code);

            using var package = new MemoryStream(PackageBytes());
            var bounded = Assert.Throws<WordToolkitOperationException>(() =>
                new InspectWordPackageOperation(
                    new OpcPackageLimits { MaxEntries = 2 }
                ).Execute(package, "bounded.docx")
            );
            Assert.Equal("PACKAGE_LIMIT", bounded.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CancellationAndInvalidStreamsKeepTheirPublicSemantics()
    {
        using var canceledPackage = new MemoryStream(PackageBytes());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            new InspectWordPackageOperation().Execute(
                canceledPackage,
                "canceled.docx",
                cancellationToken: cancellation.Token
            )
        );

        var disposed = new MemoryStream(PackageBytes());
        disposed.Dispose();
        var disposedError = Assert.Throws<WordToolkitOperationException>(() =>
            new InspectWordPackageOperation().Execute(disposed, "disposed.docx")
        );
        Assert.Equal("INVALID_INPUT", disposedError.Code);

        using var nonSeekable = new NonSeekableReadStream(PackageBytes());
        var streamError = Assert.Throws<WordToolkitOperationException>(() =>
            new InspectWordPackageOperation().Execute(nonSeekable, "stream.docx")
        );
        Assert.Equal("INVALID_INPUT", streamError.Code);
    }

    [Theory]
    [InlineData("urn:evil/officeDocument", "application/xml", true)]
    [InlineData(
        WordPackageConformance.TransitionalOfficeDocumentRelationship,
        "application/xml",
        true
    )]
    [InlineData(
        WordPackageConformance.TransitionalOfficeDocumentRelationship,
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
        false
    )]
    public void RejectsPackagesThatOnlyPretendToBeWord(
        string relationshipType,
        string contentType,
        bool wordRoot
    )
    {
        using var package = new MemoryStream(
            PackageBytes(
                officeRelationshipType: relationshipType,
                mainContentType: contentType,
                wordRoot: wordRoot
            )
        );

        var result = new InspectWordPackageOperation().Execute(package, "pretend.docx");

        Assert.True(result.StructurallyValid);
        Assert.False(result.ValidWordPackage);
    }

    [Fact]
    public void InspectorAndSemanticProjectorRejectNonStandardOfficeRelationship()
    {
        using var package = new MemoryStream(
            PackageBytes(
                officeRelationshipType: "urn:evil/officeDocument",
                mainContentType:
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
                wordRoot: true
            )
        );
        var operationResult = new InspectWordPackageOperation().Execute(
            package,
            "pretend.docx"
        );
        var snapshot = new OpcPackageReader().Read(package);

        Assert.False(operationResult.WordDocumentDetected);
        Assert.False(operationResult.ValidWordPackage);
        Assert.Throws<WordSemanticProjectionException>(() =>
            new WordSemanticProjector().Project(snapshot)
        );
    }

    [Fact]
    public void DefaultDiagnosticsDoNotExposePackageEntryNames()
    {
        using var package = new MemoryStream(PackageBytes(unsafeEntryName: "client-a/../payroll-secret.xml"));

        var summary = new InspectWordPackageOperation().Execute(
            package,
            "private.docx",
            includeDetails: false
        );
        var json = WordToolkitOperationJson.Serialize(summary);

        Assert.NotEmpty(summary.Diagnostics.Items);
        Assert.All(summary.Diagnostics.Items, diagnostic =>
        {
            Assert.Null(diagnostic.PartUri);
            Assert.Null(diagnostic.RelationshipId);
        });
        Assert.DoesNotContain("payroll-secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidWordPackageRequiresBodyAndExtensionContentTypeAgreement()
    {
        using var noBody = new MemoryStream(PackageBytes(includeBody: false));
        var noBodyResult = new InspectWordPackageOperation().Execute(
            noBody,
            "missing-body.docx"
        );
        Assert.True(noBodyResult.WordDocumentDetected);
        Assert.False(noBodyResult.ValidWordPackage);

        using var macroAsDocx = new MemoryStream(
            PackageBytes(
                mainContentType:
                    "application/vnd.ms-word.document.macroEnabled.main+xml"
            )
        );
        var mismatchedResult = new InspectWordPackageOperation().Execute(
            macroAsDocx,
            "macro-as-docx.docx"
        );
        Assert.True(mismatchedResult.WordDocumentDetected);
        Assert.False(mismatchedResult.ValidWordPackage);
    }

    [Theory]
    [InlineData("folder/input.docx")]
    [InlineData("folder\\input.docx")]
    [InlineData("C:secret.docx")]
    public void StreamFileNameMustBeABoundedLeaf(string fileName)
    {
        using var package = new MemoryStream(PackageBytes());
        var pathLike = Assert.Throws<WordToolkitOperationException>(() =>
            new InspectWordPackageOperation().Execute(package, fileName)
        );
        Assert.Equal("INVALID_INPUT", pathLike.Code);

        var oversizedName = new string('a', 1_000_000) + ".docx";
        var oversized = Assert.Throws<WordToolkitOperationException>(() =>
            new InspectWordPackageOperation().Execute(package, oversizedName)
        );
        Assert.Equal("INVALID_INPUT", oversized.Code);
    }

    [Fact]
    public void AcceptsStrictRelationshipWithStrictWordprocessingNamespace()
    {
        using var package = new MemoryStream(
            PackageBytes(
                officeRelationshipType:
                    WordPackageConformance.StrictOfficeDocumentRelationship,
                wordNamespace: WordPackageConformance.StrictWordNamespace
            )
        );

        var result = new InspectWordPackageOperation().Execute(package, "strict.docx");

        Assert.True(result.WordDocumentDetected);
        Assert.True(result.ValidWordPackage);
    }

    private static byte[] PackageBytes(
        string? externalTarget = null,
        string officeRelationshipType =
            WordPackageConformance.TransitionalOfficeDocumentRelationship,
        string mainContentType =
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
        bool wordRoot = true,
        string? unsafeEntryName = null,
        bool includeBody = true,
        string wordNamespace = WordPackageConformance.TransitionalWordNamespace
    )
    {
        using var stream = new MemoryStream();
        using (
            var archive = new ZipArchive(
                stream,
                ZipArchiveMode.Create,
                leaveOpen: true
            )
        )
        {
            WriteEntry(
                archive,
                "[Content_Types].xml",
                $"""
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
                  <Default Extension="xml" ContentType="application/xml" />
                  <Override PartName="/word/document.xml" ContentType="{mainContentType}" />
                </Types>
                """
            );
            WriteEntry(
                archive,
                "_rels/.rels",
                $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="{officeRelationshipType}" Target="word/document.xml" />
                </Relationships>
                """
            );
            WriteEntry(
                archive,
                "word/document.xml",
                wordRoot
                    ? includeBody
                        ? $"""
                          <w:document xmlns:w="{wordNamespace}"><w:body><w:p /></w:body></w:document>
                          """
                        : $"""
                          <w:document xmlns:w="{wordNamespace}" />
                          """
                    : "<root />"
            );
            if (externalTarget is not null)
            {
                WriteEntry(
                    archive,
                    "word/_rels/document.xml.rels",
                    $"""
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                      <Relationship Id="rIdExternal" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="{externalTarget}" TargetMode="External" />
                    </Relationships>
                    """
                );
            }
            if (unsafeEntryName is not null)
            {
                WriteEntry(archive, unsafeEntryName, "private");
            }
        }
        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(Encoding.UTF8.GetBytes(content));
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-inspect-operation-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class NonSeekableReadStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableReadStream(byte[] content)
        {
            _inner = new MemoryStream(content);
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
