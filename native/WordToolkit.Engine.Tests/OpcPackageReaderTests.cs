using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;

namespace WordToolkit.Engine.Tests;

public sealed class OpcPackageReaderTests
{
    [Fact]
    public void ReadsPackageGraphAndPreservesOpaqueParts()
    {
        var image = new byte[] { 0, 1, 2, 253, 254, 255 };
        using var package = BuildPackage(
            ("[Content_Types].xml", ContentTypes(includePng: true)),
            ("_rels/.rels", RootRelationships()),
            ("word/document.xml", DocumentXml()),
            ("word/_rels/document.xml.rels", DocumentRelationships()),
            ("word/media/image1.png", image)
        );

        var snapshot = new OpcPackageReader().Read(package);

        Assert.True(snapshot.IsStructurallyValid);
        Assert.Equal(2, snapshot.Parts.Count);
        Assert.Equal("image/png", snapshot.Parts["/word/media/image1.png"].ContentType);
        Assert.Equal(
            image,
            snapshot.Parts["/word/media/image1.png"].Entry.Content.ToArray()
        );
        var imageRelationship = Assert.Single(
            snapshot.RelationshipsFrom("/word/document.xml")
        );
        Assert.Equal("/word/media/image1.png", imageRelationship.ResolvedTargetPartUri);
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Code == "OPC040");
    }

    [Fact]
    public void FingerprintDoesNotDependOnZipEntryOrder()
    {
        var ordered = new[]
        {
            ("[Content_Types].xml", (object)ContentTypes()),
            ("_rels/.rels", (object)RootRelationships()),
            ("word/document.xml", (object)DocumentXml()),
        };
        using var first = BuildPackage(ordered);
        using var second = BuildPackage(ordered.Reverse().ToArray());

        var reader = new OpcPackageReader();
        Assert.Equal(reader.Read(first).Fingerprint, reader.Read(second).Fingerprint);
    }

    [Fact]
    public void ReportsMissingRelationshipTargetAndOrphanPart()
    {
        using var package = BuildPackage(
            ("[Content_Types].xml", ContentTypes(includePng: true)),
            ("_rels/.rels", RootRelationships()),
            ("word/document.xml", DocumentXml()),
            ("word/_rels/document.xml.rels", DocumentRelationships("media/missing.png")),
            ("word/media/orphan.png", new byte[] { 1, 2, 3 })
        );

        var snapshot = new OpcPackageReader().Read(package);

        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Code == "OPC034");
        Assert.Contains(
            snapshot.Diagnostics,
            diagnostic => diagnostic.Code == "OPC040"
                && diagnostic.PartUri == "/word/media/orphan.png"
        );
        Assert.False(snapshot.IsStructurallyValid);
    }

    [Fact]
    public void RejectsUnsafeInternalRelationshipTarget()
    {
        using var package = BuildPackage(
            ("[Content_Types].xml", ContentTypes()),
            ("_rels/.rels", RootRelationships("file:///etc/passwd")),
            ("word/document.xml", DocumentXml())
        );

        var snapshot = new OpcPackageReader().Read(package);

        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Code == "OPC033");
        Assert.DoesNotContain(
            snapshot.Relationships,
            relationship => relationship.ResolvedTargetPartUri is not null
        );
    }

    [Fact]
    public void ReportsDuplicateRelationshipIds()
    {
        var relationships = RelationshipsXml(
            Relationship("rId1", "type-a", "document.xml"),
            Relationship("rId1", "type-b", "other.xml")
        );
        using var package = BuildPackage(
            ("[Content_Types].xml", ContentTypes()),
            ("_rels/.rels", relationships),
            ("word/document.xml", DocumentXml())
        );

        var snapshot = new OpcPackageReader().Read(package);

        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Code == "OPC032");
    }

    [Fact]
    public void ReportsInvalidRelationshipIdAndTypeUri()
    {
        var relationships = RelationshipsXml(
            Relationship("not an xml id", "type with spaces", "word/document.xml")
        );
        using var package = BuildPackage(
            ("[Content_Types].xml", ContentTypes()),
            ("_rels/.rels", relationships),
            ("word/document.xml", DocumentXml())
        );

        var snapshot = new OpcPackageReader().Read(package);

        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Code == "OPC037");
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Code == "OPC038");
        Assert.False(snapshot.IsStructurallyValid);
    }

    [Fact]
    public void ReportsInvalidExternalTargetWithoutDisclosingIt()
    {
        const string sensitiveTarget = "https://private.example/path with spaces";
        var relationships = RelationshipsXml(
            Relationship("rId1", "type-a", sensitiveTarget, "External")
        );
        using var package = BuildPackage(
            ("[Content_Types].xml", ContentTypes()),
            ("_rels/.rels", relationships),
            ("word/document.xml", DocumentXml())
        );

        var snapshot = new OpcPackageReader().Read(package);

        var diagnostic = Assert.Single(
            snapshot.Diagnostics,
            diagnostic => diagnostic.Code == "OPC039"
        );
        Assert.DoesNotContain(sensitiveTarget, diagnostic.Message, StringComparison.Ordinal);
        Assert.False(snapshot.IsStructurallyValid);
    }

    [Fact]
    public void RejectsRelationshipTargetingPackageInfrastructure()
    {
        using var package = BuildPackage(
            ("[Content_Types].xml", ContentTypes()),
            (
                "_rels/.rels",
                RelationshipsXml(Relationship("rId1", "type-a", "_rels/.rels"))
            ),
            ("word/document.xml", DocumentXml())
        );

        var snapshot = new OpcPackageReader().Read(package);

        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Code == "OPC043");
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Code == "OPC034");
        Assert.False(snapshot.IsStructurallyValid);
    }

    [Fact]
    public void ReportsRelationshipPartWithWrongContentType()
    {
        var contentTypes = ContentTypes().Replace(
            "application/vnd.openxmlformats-package.relationships+xml",
            "application/xml",
            StringComparison.Ordinal
        );
        using var package = BuildPackage(
            ("[Content_Types].xml", contentTypes),
            ("_rels/.rels", RootRelationships()),
            ("word/document.xml", DocumentXml())
        );

        var snapshot = new OpcPackageReader().Read(package);

        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Code == "OPC041");
        Assert.False(snapshot.IsStructurallyValid);
    }

    [Fact]
    public void ReportsRelationshipPartThatOwnsRelationships()
    {
        using var package = BuildPackage(
            ("[Content_Types].xml", ContentTypes()),
            ("_rels/.rels", RootRelationships()),
            (
                "_rels/_rels/.rels.rels",
                RelationshipsXml(Relationship("rId1", "type-a", "word/document.xml"))
            ),
            ("word/document.xml", DocumentXml())
        );

        var snapshot = new OpcPackageReader().Read(package);

        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Code == "OPC042");
        Assert.False(snapshot.IsStructurallyValid);
    }

    [Fact]
    public void RetainsInternalRelationshipFragmentSeparatelyFromPartUri()
    {
        using var package = BuildPackage(
            ("[Content_Types].xml", ContentTypes()),
            ("_rels/.rels", RootRelationships("word/document.xml#bookmark-1")),
            ("word/document.xml", DocumentXml())
        );

        var snapshot = new OpcPackageReader().Read(package);

        var relationship = Assert.Single(snapshot.Relationships);
        Assert.Equal("/word/document.xml", relationship.ResolvedTargetPartUri);
        Assert.Equal("bookmark-1", relationship.TargetFragment);
        Assert.True(snapshot.IsStructurallyValid);
    }

    [Fact]
    public void RejectsQueryComponentOnInternalRelationshipTarget()
    {
        using var package = BuildPackage(
            ("[Content_Types].xml", ContentTypes()),
            ("_rels/.rels", RootRelationships("word/document.xml?version=2")),
            ("word/document.xml", DocumentXml())
        );

        var snapshot = new OpcPackageReader().Read(package);

        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Code == "OPC033");
        Assert.Null(Assert.Single(snapshot.Relationships).ResolvedTargetPartUri);
        Assert.False(snapshot.IsStructurallyValid);
    }

    [Fact]
    public void ReportsCaseInsensitiveEntryCollision()
    {
        using var package = BuildPackage(
            ("[Content_Types].xml", ContentTypes()),
            ("_rels/.rels", RootRelationships()),
            ("word/document.xml", DocumentXml()),
            ("word/Document.xml", DocumentXml())
        );

        var snapshot = new OpcPackageReader().Read(package);

        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Code == "OPC011");
    }

    [Fact]
    public void MetadataXmlDtdIsNeverExpanded()
    {
        const string malicious = """
            <!DOCTYPE Types [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="xml" ContentType="&xxe;" />
            </Types>
            """;
        using var package = BuildPackage(
            ("[Content_Types].xml", malicious),
            ("_rels/.rels", RootRelationships()),
            ("word/document.xml", DocumentXml())
        );

        var snapshot = new OpcPackageReader().Read(package);

        Assert.Contains(
            snapshot.Diagnostics,
            diagnostic => diagnostic.Code == "OPC021"
                && diagnostic.Severity == OpcDiagnosticSeverity.Fatal
        );
    }

    [Fact]
    public void CompressionRatioLimitStopsHighlyCompressedPayload()
    {
        using var package = BuildPackage(("zeros.bin", new byte[64 * 1024]));
        var reader = new OpcPackageReader(
            new OpcPackageLimits { MaxCompressionRatio = 2 }
        );

        var exception = Assert.Throws<OpcPackageLimitException>(() => reader.Read(package));

        Assert.Contains("compression ratio", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExternalRelationshipsAreClassifiedWithoutDereferencing()
    {
        var relationships = RelationshipsXml(
            Relationship(
                "rId1",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink",
                "https://example.com",
                "External"
            )
        );
        using var package = BuildPackage(
            ("[Content_Types].xml", ContentTypes()),
            ("_rels/.rels", relationships),
            ("word/document.xml", DocumentXml())
        );

        var snapshot = new OpcPackageReader().Read(package);

        var relationship = Assert.Single(snapshot.Relationships);
        Assert.Equal(OpcRelationshipTargetMode.External, relationship.TargetMode);
        Assert.Null(relationship.ResolvedTargetPartUri);
        var diagnostic = Assert.Single(
            snapshot.Diagnostics,
            diagnostic => diagnostic.Code == "OPC035"
        );
        Assert.DoesNotContain("example.com", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidRelationshipTargetModeIsNotTreatedAsInternal()
    {
        var relationships = RelationshipsXml(
            Relationship("rId1", "type-a", "word/document.xml", "Sideways")
        );
        using var package = BuildPackage(
            ("[Content_Types].xml", ContentTypes()),
            ("_rels/.rels", relationships),
            ("word/document.xml", DocumentXml())
        );

        var snapshot = new OpcPackageReader().Read(package);

        Assert.Equal(
            OpcRelationshipTargetMode.Invalid,
            Assert.Single(snapshot.Relationships).TargetMode
        );
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Code == "OPC036");
        Assert.False(snapshot.IsStructurallyValid);
    }

    [Fact]
    public void CancellationStopsPackageReadBeforeEntryMaterialization()
    {
        using var package = BuildPackage(("payload.bin", new byte[1024]));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            new OpcPackageReader().Read(package, cancellation.Token)
        );
    }

    [Fact]
    public void MutationUsesEntryHashPreconditionsAndChangesOnlyTargetContent()
    {
        using var package = BuildPackage(
            ("[Content_Types].xml", ContentTypes()),
            ("_rels/.rels", RootRelationships()),
            ("word/document.xml", DocumentXml())
        );
        var reader = new OpcPackageReader();
        var original = reader.Read(package);
        var document = original.Parts["/word/document.xml"];
        var replacement = Encoding.UTF8.GetBytes(
            DocumentXml().Replace("<w:p />", "<w:p><w:r><w:t>changed</w:t></w:r></w:p>")
        );
        var mutation = new OpcPackageMutationBuilder(original).ReplacePart(
            document.Uri,
            replacement,
            document.Entry.Sha256
        );
        using var output = new MemoryStream();

        new OpcPackageSerializer().Write(output, mutation);
        output.Position = 0;
        var changed = reader.Read(output);

        Assert.NotEqual(original.Fingerprint, changed.Fingerprint);
        Assert.Equal(
            original.Entries.Single(entry => entry.Name == "_rels/.rels").Sha256,
            changed.Entries.Single(entry => entry.Name == "_rels/.rels").Sha256
        );
        Assert.Contains("changed", Encoding.UTF8.GetString(
            changed.Parts["/word/document.xml"].Entry.Content.Span
        ));
    }

    [Fact]
    public void MutationRejectsStaleEntryHash()
    {
        using var package = BuildPackage(
            ("[Content_Types].xml", ContentTypes()),
            ("_rels/.rels", RootRelationships()),
            ("word/document.xml", DocumentXml())
        );
        var snapshot = new OpcPackageReader().Read(package);

        var exception = Assert.Throws<OpcPackagePreconditionException>(() =>
            new OpcPackageMutationBuilder(snapshot).ReplacePart(
                "/word/document.xml",
                Encoding.UTF8.GetBytes(DocumentXml()),
                new string('0', 64)
            )
        );

        Assert.Contains("changed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeterministicSerializationProducesIdenticalBytes()
    {
        using var package = BuildPackage(
            ("word/document.xml", DocumentXml()),
            ("_rels/.rels", RootRelationships()),
            ("[Content_Types].xml", ContentTypes())
        );
        var snapshot = new OpcPackageReader().Read(package);
        var mutation = new OpcPackageMutationBuilder(snapshot);
        using var first = new MemoryStream();
        using var second = new MemoryStream();
        var serializer = new OpcPackageSerializer();

        serializer.Write(first, mutation, OpcSerializationMode.Deterministic);
        serializer.Write(second, mutation, OpcSerializationMode.Deterministic);

        Assert.Equal(first.ToArray(), second.ToArray());
    }

    [Fact]
    public void AtomicWriterRejectsInvalidCandidateWithoutTouchingDestination()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var destination = Path.Combine(directory, "document.docx");
            using (var package = BuildPackage(
                ("[Content_Types].xml", ContentTypes()),
                ("_rels/.rels", RootRelationships()),
                ("word/document.xml", DocumentXml())
            ))
            {
                File.WriteAllBytes(destination, package.ToArray());
            }

            var before = File.ReadAllBytes(destination);
            var snapshot = new OpcPackageReader().Read(destination);
            var mutation = new OpcPackageMutationBuilder(snapshot).DeleteEntry(
                "[Content_Types].xml"
            );

            Assert.Throws<OpcPackageValidationException>(() =>
                new OpcAtomicPackageWriter().Write(destination, mutation)
            );

            Assert.Equal(before, File.ReadAllBytes(destination));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AtomicWriterRejectsChangedDestination()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var destination = Path.Combine(directory, "document.docx");
            using var basePackage = BuildPackage(
                ("[Content_Types].xml", ContentTypes()),
                ("_rels/.rels", RootRelationships()),
                ("word/document.xml", DocumentXml())
            );
            var reader = new OpcPackageReader();
            var baseSnapshot = reader.Read(basePackage);
            var mutation = new OpcPackageMutationBuilder(baseSnapshot).ReplacePart(
                "/word/document.xml",
                Encoding.UTF8.GetBytes(DocumentXml())
            );
            using var otherPackage = BuildPackage(
                ("[Content_Types].xml", ContentTypes()),
                ("_rels/.rels", RootRelationships()),
                ("word/document.xml", DocumentXml().Replace("<w:p />", "<w:p><w:r /></w:p>"))
            );
            File.WriteAllBytes(destination, otherPackage.ToArray());
            var before = File.ReadAllBytes(destination);

            Assert.Throws<OpcPackageConcurrencyException>(() =>
                new OpcAtomicPackageWriter().Write(destination, mutation)
            );

            Assert.Equal(before, File.ReadAllBytes(destination));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AtomicWriterRequireNewDestinationNeverOverwritesExistingFile()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var destination = Path.Combine(directory, "merge.docx");
            File.WriteAllText(destination, "do not replace");
            using var package = BuildPackage(
                ("[Content_Types].xml", ContentTypes()),
                ("_rels/.rels", RootRelationships()),
                ("word/document.xml", DocumentXml())
            );
            var snapshot = new OpcPackageReader().Read(package);
            var mutation = new OpcPackageMutationBuilder(snapshot);

            Assert.Throws<OpcPackageConcurrencyException>(() =>
                new OpcAtomicPackageWriter().Write(
                    destination,
                    mutation,
                    new OpcAtomicWriteOptions { RequireNewDestination = true }
                )
            );

            Assert.Equal("do not replace", File.ReadAllText(destination));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AtomicWriterRejectsUnexpectedCandidateFingerprintBeforeReplacement()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var destination = Path.Combine(directory, "document.docx");
            using (var package = BuildPackage(
                ("[Content_Types].xml", ContentTypes()),
                ("_rels/.rels", RootRelationships()),
                ("word/document.xml", DocumentXml())
            ))
            {
                File.WriteAllBytes(destination, package.ToArray());
            }

            var before = File.ReadAllBytes(destination);
            var snapshot = new OpcPackageReader().Read(destination);
            var mutation = new OpcPackageMutationBuilder(snapshot).ReplacePart(
                "/word/document.xml",
                Encoding.UTF8.GetBytes(
                    DocumentXml().Replace("<w:p />", "<w:p><w:r /></w:p>")
                )
            );

            Assert.Throws<OpcPackageResultMismatchException>(() =>
                new OpcAtomicPackageWriter().Write(
                    destination,
                    mutation,
                    new OpcAtomicWriteOptions
                    {
                        ExpectedResultFingerprint = new string('0', 64),
                        KeepBackup = true,
                    }
                )
            );

            Assert.Equal(before, File.ReadAllBytes(destination));
            Assert.Empty(Directory.GetFiles(directory, "*.bak"));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AtomicWriterReplacesVersionMatchedDestinationAndKeepsBackupOnRequest()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var destination = Path.Combine(directory, "document.docx");
            using (var package = BuildPackage(
                ("[Content_Types].xml", ContentTypes()),
                ("_rels/.rels", RootRelationships()),
                ("word/document.xml", DocumentXml())
            ))
            {
                File.WriteAllBytes(destination, package.ToArray());
            }

            var reader = new OpcPackageReader();
            var snapshot = reader.Read(destination);
            var changedXml = DocumentXml().Replace(
                "<w:p />",
                "<w:p><w:r><w:t>atomic</w:t></w:r></w:p>"
            );
            var mutation = new OpcPackageMutationBuilder(snapshot).ReplacePart(
                "/word/document.xml",
                Encoding.UTF8.GetBytes(changedXml)
            );

            var result = new OpcAtomicPackageWriter().Write(
                destination,
                mutation,
                new OpcAtomicWriteOptions { KeepBackup = true }
            );

            Assert.NotNull(result.BackupPath);
            Assert.True(File.Exists(result.BackupPath));
            Assert.Equal(result.Fingerprint, reader.Read(destination).Fingerprint);
            Assert.Contains("atomic", Encoding.UTF8.GetString(
                reader.Read(destination).Parts["/word/document.xml"].Entry.Content.Span
            ));
            Assert.Equal(snapshot.Fingerprint, reader.Read(result.BackupPath!).Fingerprint);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AtomicWriterRestoresNonCooperativeChangeAtCommitBoundary()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var destination = Path.Combine(directory, "document.docx");
            using var originalPackage = BuildPackage(
                ("[Content_Types].xml", ContentTypes()),
                ("_rels/.rels", RootRelationships()),
                ("word/document.xml", DocumentXml())
            );
            File.WriteAllBytes(destination, originalPackage.ToArray());
            var reader = new OpcPackageReader();
            var original = reader.Read(destination);
            var mutation = new OpcPackageMutationBuilder(original).ReplacePart(
                "/word/document.xml",
                Encoding.UTF8.GetBytes(
                    DocumentXml().Replace("<w:p />", "<w:p><w:r /></w:p>")
                )
            );
            using var externalPackage = BuildPackage(
                ("[Content_Types].xml", ContentTypes()),
                ("_rels/.rels", RootRelationships()),
                (
                    "word/document.xml",
                    DocumentXml().Replace(
                        "<w:p />",
                        "<w:p><w:r><w:t>external</w:t></w:r></w:p>"
                    )
                )
            );
            var externalBytes = externalPackage.ToArray();
            var writer = new OpcAtomicPackageWriter(
                reader,
                new OpcPackageSerializer(),
                path => File.WriteAllBytes(path, externalBytes)
            );

            var conflict = Assert.Throws<OpcPackageConcurrencyException>(() =>
                writer.Write(
                    destination,
                    mutation,
                    new OpcAtomicWriteOptions
                    {
                        ExpectedDestinationFingerprint = original.Fingerprint,
                        KeepBackup = true,
                    }
                )
            );

            Assert.Contains("external version was restored", conflict.Message);
            Assert.Equal(externalBytes, File.ReadAllBytes(destination));
            Assert.Contains(
                "external",
                Encoding.UTF8.GetString(
                    reader.Read(destination).Parts["/word/document.xml"].Entry.Content.Span
                )
            );
            Assert.Empty(Directory.GetFiles(directory, "*.bak"));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            Assert.Empty(Directory.GetFiles(directory, "*.conflict"));

            File.WriteAllBytes(destination, originalPackage.ToArray());
            var deleteWriter = new OpcAtomicPackageWriter(
                reader,
                new OpcPackageSerializer(),
                File.Delete
            );
            var deleted = Assert.Throws<OpcPackageConcurrencyException>(() =>
                deleteWriter.Write(
                    destination,
                    mutation,
                    new OpcAtomicWriteOptions
                    {
                        ExpectedDestinationFingerprint = original.Fingerprint,
                    }
                )
            );
            Assert.Contains("removed", deleted.Message);
            Assert.False(File.Exists(destination));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AtomicWriterRetainsNewerChangeCreatedDuringCompensation()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var destination = Path.Combine(directory, "document.docx");
            using var originalPackage = BuildPackage(
                ("[Content_Types].xml", ContentTypes()),
                ("_rels/.rels", RootRelationships()),
                ("word/document.xml", DocumentXml())
            );
            File.WriteAllBytes(destination, originalPackage.ToArray());

            var reader = new OpcPackageReader();
            var original = reader.Read(destination);
            var mutation = new OpcPackageMutationBuilder(original).ReplacePart(
                "/word/document.xml",
                Encoding.UTF8.GetBytes(
                    DocumentXml().Replace(
                        "<w:p />",
                        "<w:p><w:r><w:t>candidate</w:t></w:r></w:p>"
                    )
                )
            );
            using var firstExternalPackage = BuildPackage(
                ("[Content_Types].xml", ContentTypes()),
                ("_rels/.rels", RootRelationships()),
                (
                    "word/document.xml",
                    DocumentXml().Replace(
                        "<w:p />",
                        "<w:p><w:r><w:t>first external</w:t></w:r></w:p>"
                    )
                )
            );
            using var secondExternalPackage = BuildPackage(
                ("[Content_Types].xml", ContentTypes()),
                ("_rels/.rels", RootRelationships()),
                (
                    "word/document.xml",
                    DocumentXml().Replace(
                        "<w:p />",
                        "<w:p><w:r><w:t>second external</w:t></w:r></w:p>"
                    )
                )
            );
            var firstExternalBytes = firstExternalPackage.ToArray();
            var secondExternalBytes = secondExternalPackage.ToArray();
            var writer = new OpcAtomicPackageWriter(
                reader,
                new OpcPackageSerializer(),
                path => File.WriteAllBytes(path, firstExternalBytes),
                path => File.WriteAllBytes(path, secondExternalBytes)
            );

            var recovery = Assert.Throws<OpcPackageRecoveryException>(() =>
                writer.Write(
                    destination,
                    mutation,
                    new OpcAtomicWriteOptions
                    {
                        ExpectedDestinationFingerprint = original.Fingerprint,
                        KeepBackup = false,
                    }
                )
            );

            Assert.Equal(firstExternalBytes, File.ReadAllBytes(destination));
            var recoveryPath = Assert.Single(recovery.RecoveryPaths);
            Assert.EndsWith(".conflict", recoveryPath, StringComparison.Ordinal);
            Assert.Equal(
                Path.GetDirectoryName(destination),
                Path.GetDirectoryName(recoveryPath)
            );
            Assert.Equal(secondExternalBytes, File.ReadAllBytes(recoveryPath));
            Assert.DoesNotContain("candidate", Encoding.UTF8.GetString(
                reader.Read(recoveryPath).Parts["/word/document.xml"].Entry.Content.Span
            ));
            Assert.Empty(Directory.GetFiles(directory, "*.bak"));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AtomicWriterDoesNotClaimMissingRecoveryArtifact()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var destination = Path.Combine(directory, "document.docx");
            using var originalPackage = BuildPackage(
                ("[Content_Types].xml", ContentTypes()),
                ("_rels/.rels", RootRelationships()),
                ("word/document.xml", DocumentXml())
            );
            File.WriteAllBytes(destination, originalPackage.ToArray());

            var reader = new OpcPackageReader();
            var original = reader.Read(destination);
            var mutation = new OpcPackageMutationBuilder(original).ReplacePart(
                "/word/document.xml",
                Encoding.UTF8.GetBytes(
                    DocumentXml().Replace("<w:p />", "<w:p><w:r /></w:p>")
                )
            );
            using var externalPackage = BuildPackage(
                ("[Content_Types].xml", ContentTypes()),
                ("_rels/.rels", RootRelationships()),
                (
                    "word/document.xml",
                    DocumentXml().Replace(
                        "<w:p />",
                        "<w:p><w:r><w:t>external</w:t></w:r></w:p>"
                    )
                )
            );
            var externalBytes = externalPackage.ToArray();
            var writer = new OpcAtomicPackageWriter(
                reader,
                new OpcPackageSerializer(),
                path => File.WriteAllBytes(path, externalBytes),
                _ =>
                {
                    foreach (var path in Directory.GetFiles(directory, "*.bak"))
                    {
                        File.Delete(path);
                    }
                }
            );

            var recovery = Assert.Throws<OpcPackageRecoveryException>(() =>
                writer.Write(
                    destination,
                    mutation,
                    new OpcAtomicWriteOptions
                    {
                        ExpectedDestinationFingerprint = original.Fingerprint,
                        KeepBackup = false,
                    }
                )
            );

            Assert.Empty(recovery.RecoveryPaths);
            Assert.Contains("no recovery artifact", recovery.Message);
            Assert.Empty(Directory.GetFiles(directory, "*.bak"));
            Assert.Empty(Directory.GetFiles(directory, "*.conflict"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static MemoryStream BuildPackage(
        params (string Name, object Content)[] entries
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, rawContent) in entries)
            {
                var content = rawContent switch
                {
                    string text => Encoding.UTF8.GetBytes(text),
                    byte[] bytes => bytes,
                    _ => throw new ArgumentException("Unsupported test entry content."),
                };
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "wordtoolkit-engine-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ContentTypes(bool includePng = false)
    {
        var png = includePng
            ? "<Default Extension=\"png\" ContentType=\"image/png\" />"
            : string.Empty;
        return $"""
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
              <Default Extension="xml" ContentType="application/xml" />
              {png}
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" />
            </Types>
            """;
    }

    private static string RootRelationships(string target = "word/document.xml") =>
        RelationshipsXml(
            Relationship(
                "rId1",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument",
                target
            )
        );

    private static string DocumentRelationships(string target = "media/image1.png") =>
        RelationshipsXml(
            Relationship(
                "rId2",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image",
                target
            )
        );

    private static string RelationshipsXml(params string[] relationships) => $"""
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          {string.Join(Environment.NewLine, relationships)}
        </Relationships>
        """;

    private static string Relationship(
        string id,
        string type,
        string target,
        string? targetMode = null
    ) => $"""
        <Relationship Id="{id}" Type="{type}" Target="{target}"{(targetMode is null ? string.Empty : $" TargetMode=\"{targetMode}\"")} />
        """;

    private static string DocumentXml() => """
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body><w:p /></w:body>
        </w:document>
        """;
}
