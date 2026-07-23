using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Operations;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Rendering;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class SemanticHtmlWordPackageOperationTests
{
    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string MathNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string RelationshipsNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    [Fact]
    public void RendersDeterministicInertSemanticHtmlWithoutMutatingSource()
    {
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "semantic.docx");
            var firstOutput = Path.Combine(directory, "first.html");
            var secondOutput = Path.Combine(directory, "second.html");
            CreatePackage(input);
            var inputBytes = File.ReadAllBytes(input);
            var fingerprint = new OpcPackageReader().Read(input).Fingerprint;
            var operation = new SemanticHtmlWordPackageOperation();

            var first = operation.Execute(
                new SemanticHtmlWordPackageRequest(
                    input,
                    firstOutput,
                    fingerprint,
                    SemanticHtmlStoryScope.MainDocument,
                    "pl-PL"
                )
            );
            var second = operation.Execute(
                new SemanticHtmlWordPackageRequest(
                    input,
                    secondOutput,
                    fingerprint,
                    SemanticHtmlStoryScope.MainDocument,
                    "pl-PL"
                )
            );

            Assert.Equal(inputBytes, File.ReadAllBytes(input));
            Assert.Equal(File.ReadAllBytes(firstOutput), File.ReadAllBytes(secondOutput));
            Assert.Equal(first.ArtifactSha256, second.ArtifactSha256);
            Assert.Equal(SemanticHtmlWordPackageContract.Contract, first.OperationContract);
            Assert.Equal("semantic_preview_non_paginated", first.FidelityClass);
            Assert.Equal(1, first.RenderedStoryCount);
            Assert.Equal(1, first.TableCount);
            Assert.Equal(1, first.EquationCount);
            Assert.Equal(1, first.DrawingPlaceholderCount);
            Assert.True(first.OutputCreated);
            Assert.False(first.SourceMutated);
            Assert.True(first.ArtifactContainsDocumentContent);
            Assert.False(first.ExternalResourcesLoaded);
            Assert.False(first.ActiveContentExecuted);
            Assert.False(first.RawXmlReturned);
            Assert.False(first.DocumentTextReturned);
            Assert.False(first.WordOpened);

            var html = File.ReadAllText(firstOutput, Encoding.UTF8);
            Assert.Contains("<!doctype html>", html, StringComparison.Ordinal);
            Assert.Contains("<html lang=\"pl-PL\">", html, StringComparison.Ordinal);
            Assert.Contains(
                "default-src 'none'; style-src 'unsafe-inline'",
                html,
                StringComparison.Ordinal
            );
            Assert.Contains("<h1 class=\"wt-paragraph\"", html, StringComparison.Ordinal);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SECRET-INSTRUCTION", html, StringComparison.Ordinal);
            Assert.Contains("cached-result", html, StringComparison.Ordinal);
            Assert.Contains("wt-hyperlink-inert", html, StringComparison.Ordinal);
            Assert.DoesNotContain("href=", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://example.invalid", html, StringComparison.Ordinal);
            Assert.Contains("wt-revision-insertion", html, StringComparison.Ordinal);
            Assert.Contains("wt-revision-deletion", html, StringComparison.Ordinal);
            Assert.Contains("role=\"math\"", html, StringComparison.Ordinal);
            Assert.Contains("x+1", html, StringComparison.Ordinal);
            Assert.Contains("[Drawing]", html, StringComparison.Ordinal);
            Assert.Contains("<table", html, StringComparison.Ordinal);
            Assert.Contains(
                "<div class=\"wt-content-control\"",
                html,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain(
                "<span class=\"wt-content-control\"><p",
                html,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain("Header sentinel", html, StringComparison.Ordinal);
            Assert.Contains("HYPERLINKS_RENDERED_INERT", first.Warnings);
            Assert.Contains("TRACKED_REVISIONS_ANNOTATED", first.Warnings);
            Assert.Contains("EQUATIONS_RENDERED_AS_LINEAR_TEXT", first.Warnings);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AllStoriesIncludesRelatedStoriesAndExistingOutputIsNeverOverwritten()
    {
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "stories.docx");
            var output = Path.Combine(directory, "stories.html");
            CreatePackage(input);
            var operation = new SemanticHtmlWordPackageOperation();

            var result = operation.Execute(
                new SemanticHtmlWordPackageRequest(
                    input,
                    output,
                    StoryScope: SemanticHtmlStoryScope.AllTextStories
                )
            );

            Assert.Equal(2, result.RenderedStoryCount);
            Assert.Contains("Header sentinel", File.ReadAllText(output), StringComparison.Ordinal);
            var before = File.ReadAllBytes(output);
            var exception = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Execute(new SemanticHtmlWordPackageRequest(input, output))
            );
            Assert.Equal("OUTPUT_EXISTS", exception.Code);
            Assert.Equal(before, File.ReadAllBytes(output));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RejectsVersionConflictAndUnsafeLanguageBeforeCreatingOutput()
    {
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "guarded.docx");
            var output = Path.Combine(directory, "guarded.html");
            CreatePackage(input);
            var operation = new SemanticHtmlWordPackageOperation();

            var version = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Execute(
                    new SemanticHtmlWordPackageRequest(input, output, new string('0', 64))
                )
            );
            Assert.Equal("VERSION_CONFLICT", version.Code);
            Assert.False(File.Exists(output));

            var language = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Execute(
                    new SemanticHtmlWordPackageRequest(
                        input,
                        output,
                        Language: "pl\" onload=\"alert(1)"
                    )
                )
            );
            Assert.Equal("INVALID_INPUT", language.Code);
            Assert.False(File.Exists(output));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentCreateNewWritersProduceOneArtifactAndNoPrivateTempLeak()
    {
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "race.docx");
            var output = Path.Combine(directory, "race.html");
            CreatePackage(input);
            var operation = new SemanticHtmlWordPackageOperation();
            var gate = new Barrier(2);

            async Task<(SemanticHtmlWordPackageResult? Result, Exception? Error)> RunAsync()
            {
                return await Task.Run(() =>
                {
                    gate.SignalAndWait(TimeSpan.FromSeconds(10));
                    try
                    {
                        return (
                            operation.Execute(
                                new SemanticHtmlWordPackageRequest(input, output)
                            ),
                            (Exception?)null
                        );
                    }
                    catch (Exception exception)
                    {
                        return ((SemanticHtmlWordPackageResult?)null, exception);
                    }
                });
            }

            var attempts = await Task.WhenAll(RunAsync(), RunAsync());

            Assert.Single(attempts, attempt => attempt.Result is not null);
            var rejected = Assert.Single(attempts, attempt => attempt.Error is not null);
            var conflict = Assert.IsType<WordToolkitOperationException>(rejected.Error);
            Assert.Equal("OUTPUT_EXISTS", conflict.Code);
            Assert.True(File.Exists(output));
            Assert.DoesNotContain(
                Directory.EnumerateFiles(directory),
                path => Path.GetFileName(path).StartsWith(
                    ".wordtoolkit-render-",
                    StringComparison.Ordinal
                )
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PackageDerivedEntryNamesNeverEnterPublicErrors()
    {
        const string marker = "CLIENT-ACME-SSN";
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "private-name.docx");
            var output = Path.Combine(directory, "private-name.html");
            using (var stream = new FileStream(input, FileMode.CreateNew, FileAccess.Write))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteEntry(archive, $"secret/{marker}.xml", "private");
            }
            var operation = new SemanticHtmlWordPackageOperation(
                new OpcPackageLimits { MaxEntryUncompressedBytes = 1 }
            );

            var exception = Assert.Throws<WordToolkitOperationException>(() =>
                operation.Execute(new SemanticHtmlWordPackageRequest(input, output))
            );

            Assert.Equal("PACKAGE_LIMIT", exception.Code);
            Assert.Null(exception.Reason);
            Assert.DoesNotContain(marker, exception.Message, StringComparison.Ordinal);
            Assert.Null(exception.Details);
            Assert.False(File.Exists(output));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
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
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
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
                <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>&lt;script&gt;alert(1)&lt;/script&gt;</w:t></w:r></w:p>
                <w:p>
                  <w:r><w:fldChar w:fldCharType="begin"/></w:r>
                  <w:r><w:instrText>SECRET-INSTRUCTION</w:instrText></w:r>
                  <w:r><w:fldChar w:fldCharType="separate"/></w:r>
                  <w:r><w:t>cached-result</w:t></w:r>
                  <w:r><w:fldChar w:fldCharType="end"/></w:r>
                </w:p>
                <w:p><w:hyperlink r:id="rIdExternal"><w:r><w:t>inert link</w:t></w:r></w:hyperlink></w:p>
                <w:p>
                  <w:ins w:id="1" w:author="Tester"><w:r><w:t>inserted</w:t></w:r></w:ins>
                  <w:del w:id="2" w:author="Tester"><w:r><w:delText>deleted</w:delText></w:r></w:del>
                </w:p>
                <w:p><m:oMath><m:r><m:t>x</m:t></m:r><m:r><m:t>+1</m:t></m:r></m:oMath></w:p>
                <w:p><w:r><w:drawing/></w:r></w:p>
                <w:tbl><w:tr><w:tc><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
                <w:sdt><w:sdtContent><w:p><w:r><w:t>block control</w:t></w:r></w:p></w:sdtContent></w:sdt>
                <w:sectPr><w:headerReference w:type="default" r:id="rIdHeader"/></w:sectPr>
              </w:body>
            </w:document>
            """
        );
        WriteEntry(
            archive,
            "word/styles.xml",
            $"""
            <w:styles xmlns:w="{WordNamespace}">
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
              <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:pPr><w:outlineLvl w:val="0"/></w:pPr></w:style>
            </w:styles>
            """
        );
        WriteEntry(
            archive,
            "word/header1.xml",
            $"<w:hdr xmlns:w=\"{WordNamespace}\"><w:p><w:r><w:t>Header sentinel</w:t></w:r></w:p></w:hdr>"
        );
        WriteEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
              <Relationship Id="rIdHeader" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
              <Relationship Id="rIdExternal" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.invalid/attack" TargetMode="External"/>
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
            "wordtoolkit-semantic-html-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }
}
