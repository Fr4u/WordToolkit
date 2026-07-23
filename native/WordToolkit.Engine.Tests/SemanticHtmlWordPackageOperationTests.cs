using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
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
            Assert.Null(first.SelectionApplied);
            Assert.Null(first.TargetNodeId);
            Assert.Null(first.TargetKind);
            Assert.Null(first.TargetStoryKind);
            Assert.Null(first.FragmentWrapper);
            Assert.Null(first.TargetRenderedNodeCount);

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

    [Fact]
    public void RendersFingerprintBoundTableEquationRowAndCellSubtreesDeterministically()
    {
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "targets.docx");
            CreatePackage(input);
            var package = new OpcPackageReader().Read(input);
            var semantic = new WordSemanticProjector().Project(package);
            var table = Assert.Single(
                semantic.Nodes,
                node => node.Kind == WordSemanticNodeKind.Table
            );
            var row = Assert.Single(
                table.Children,
                node => node.Kind == WordSemanticNodeKind.TableRow
            );
            var cell = Assert.Single(
                row.Children,
                node => node.Kind == WordSemanticNodeKind.TableCell
            );
            var equation = Assert.Single(
                semantic.Nodes,
                node => node.Kind == WordSemanticNodeKind.Equation
            );
            var operation = new SemanticHtmlWordPackageOperation();

            SemanticHtmlWordPackageResult Render(
                WordSemanticNode target,
                string name
            ) =>
                operation.Execute(
                    new SemanticHtmlWordPackageRequest(
                        input,
                        Path.Combine(directory, name + ".html"),
                        package.Fingerprint,
                        TargetNodeId: target.Id.Value
                    )
                );

            var firstTable = Render(table, "table-1");
            var secondTable = Render(table, "table-2");
            var equationResult = Render(equation, "equation");
            var rowResult = Render(row, "row");
            var cellResult = Render(cell, "cell");

            Assert.Equal(firstTable.ArtifactSha256, secondTable.ArtifactSha256);
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(directory, "table-1.html")),
                File.ReadAllBytes(Path.Combine(directory, "table-2.html"))
            );
            Assert.True(firstTable.SelectionApplied is true);
            Assert.Equal(table.Id.Value, firstTable.TargetNodeId);
            Assert.Equal(WordSemanticNodeKind.Table, firstTable.TargetKind);
            Assert.Equal("main_document", firstTable.TargetStoryKind);
            Assert.Equal(
                SemanticHtmlFragmentWrapper.TableBodies,
                firstTable.FragmentWrapper
            );
            Assert.Equal(firstTable.RenderedNodeCount, firstTable.TargetRenderedNodeCount);
            Assert.Equal(1, firstTable.RenderedStoryCount);
            Assert.Contains("SEMANTIC_SUBTREE_SELECTED", firstTable.Warnings);

            var tableHtml = File.ReadAllText(Path.Combine(directory, "table-1.html"));
            Assert.Contains("cell", tableHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("cached-result", tableHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("Header sentinel", tableHtml, StringComparison.Ordinal);

            var equationHtml = File.ReadAllText(Path.Combine(directory, "equation.html"));
            Assert.Contains("role=\"math\"", equationHtml, StringComparison.Ordinal);
            Assert.Contains("x+1", equationHtml, StringComparison.Ordinal);
            Assert.DoesNotContain(">cell<", equationHtml, StringComparison.Ordinal);
            Assert.Equal(WordSemanticNodeKind.Equation, equationResult.TargetKind);

            var rowHtml = File.ReadAllText(Path.Combine(directory, "row.html"));
            Assert.Contains(
                "<table class=\"wt-table wt-fragment-context\"><tbody><tr",
                rowHtml,
                StringComparison.Ordinal
            );
            Assert.Contains("</tr>\n</tbody></table>", rowHtml, StringComparison.Ordinal);
            Assert.Equal(SemanticHtmlFragmentWrapper.TableBody, rowResult.FragmentWrapper);
            Assert.Contains("FRAGMENT_TABLE_CONTEXT_SYNTHESIZED", rowResult.Warnings);

            var cellHtml = File.ReadAllText(Path.Combine(directory, "cell.html"));
            Assert.Contains(
                "<table class=\"wt-table wt-fragment-context\"><tbody><tr><td",
                cellHtml,
                StringComparison.Ordinal
            );
            Assert.Contains("</td></tr></tbody></table>", cellHtml, StringComparison.Ordinal);
            Assert.Equal(
                SemanticHtmlFragmentWrapper.TableBodyRow,
                cellResult.FragmentWrapper
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TargetGuardsRejectUnboundStaleMissingAndOutOfScopeIdsWithoutOutput()
    {
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "target-guards.docx");
            CreatePackage(input);
            var package = new OpcPackageReader().Read(input);
            var semantic = new WordSemanticProjector().Project(package);
            var paragraph = semantic.Nodes.First(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
                && node.SourcePartUri == semantic.MainPartUri
            );
            var headerParagraph = semantic.Nodes.First(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
                && node.SourcePartUri != semantic.MainPartUri
            );
            var body = Assert.Single(
                semantic.Nodes,
                node => node.Kind == WordSemanticNodeKind.Body
            );
            var operation = new SemanticHtmlWordPackageOperation();

            WordToolkitOperationException Reject(
                string name,
                string? fingerprint,
                string targetNodeId,
                SemanticHtmlStoryScope scope = SemanticHtmlStoryScope.MainDocument
            )
            {
                var output = Path.Combine(directory, name + ".html");
                var exception = Assert.Throws<WordToolkitOperationException>(() =>
                    operation.Execute(
                        new SemanticHtmlWordPackageRequest(
                            input,
                            output,
                            fingerprint,
                            scope,
                            TargetNodeId: targetNodeId
                        )
                    )
                );
                Assert.False(File.Exists(output));
                return exception;
            }

            Assert.Equal(
                "INVALID_INPUT",
                Reject("unbound", null, paragraph.Id.Value).Code
            );
            Assert.Equal(
                "INVALID_INPUT",
                Reject("malformed", package.Fingerprint, "bad id").Code
            );
            Assert.Equal(
                "VERSION_CONFLICT",
                Reject("stale", new string('0', 64), paragraph.Id.Value).Code
            );
            Assert.Equal(
                "TARGET_NOT_FOUND",
                Reject("missing", package.Fingerprint, "wdn_missing").Code
            );
            Assert.Equal(
                "TARGET_OUT_OF_SCOPE",
                Reject("scope", package.Fingerprint, headerParagraph.Id.Value).Code
            );

            var headerOutput = Path.Combine(directory, "header.html");
            var header = operation.Execute(
                new SemanticHtmlWordPackageRequest(
                    input,
                    headerOutput,
                    package.Fingerprint,
                    SemanticHtmlStoryScope.AllTextStories,
                    TargetNodeId: headerParagraph.Id.Value
                )
            );
            Assert.True(header.SelectionApplied is true);
            Assert.Equal("header", header.TargetStoryKind);
            Assert.Contains("Header sentinel", File.ReadAllText(headerOutput));
            Assert.DoesNotContain("cached-result", File.ReadAllText(headerOutput));

            var bodyOutput = Path.Combine(directory, "body.html");
            var bodyResult = operation.Execute(
                new SemanticHtmlWordPackageRequest(
                    input,
                    bodyOutput,
                    package.Fingerprint,
                    TargetNodeId: body.Id.Value
                )
            );
            Assert.Equal(WordSemanticNodeKind.Body, bodyResult.TargetKind);
            Assert.Equal(SemanticHtmlFragmentWrapper.None, bodyResult.FragmentWrapper);
            Assert.Contains("cached-result", File.ReadAllText(bodyOutput));
            Assert.DoesNotContain("Header sentinel", File.ReadAllText(bodyOutput));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SelectedContentControlsAroundRowsAndCellsReceiveValidTableContexts()
    {
        var directory = TemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "table-wrappers.docx");
            CreateTableWrapperPackage(input);
            var package = new OpcPackageReader().Read(input);
            var semantic = new WordSemanticProjector().Project(package);
            var rowWrapper = Assert.Single(
                semantic.Nodes,
                node =>
                    node.Kind == WordSemanticNodeKind.ContentControl
                    && node.Children.Count != 0
                    && node.Children.All(child =>
                        child.Kind == WordSemanticNodeKind.TableRow
                    )
                    && node.TextPreview().Contains("row target", StringComparison.Ordinal)
            );
            var cellWrapper = Assert.Single(
                semantic.Nodes,
                node =>
                    node.Kind == WordSemanticNodeKind.ContentControl
                    && node.Children.Count != 0
                    && node.Children.All(child =>
                        child.Kind == WordSemanticNodeKind.TableCell
                    )
                    && node.TextPreview().Contains("cell target", StringComparison.Ordinal)
            );
            var nestedRowWrapper = Assert.Single(
                semantic.Nodes,
                node =>
                    node.Kind == WordSemanticNodeKind.ContentControl
                    && node.Children.Count == 1
                    && node.Children[0].Kind == WordSemanticNodeKind.ContentControl
                    && SemanticHtmlTableFragment.IsRowContainer(node)
            );
            var nestedCellWrapper = Assert.Single(
                semantic.Nodes,
                node =>
                    node.Kind == WordSemanticNodeKind.ContentControl
                    && node.Children.Count == 1
                    && node.Children[0].Kind == WordSemanticNodeKind.ContentControl
                    && SemanticHtmlTableFragment.IsCellContainer(node)
            );
            var rowRevision = Assert.Single(
                semantic.Nodes,
                node =>
                    node.Kind == WordSemanticNodeKind.Revision
                    && node.Children.Count != 0
                    && node.Children.All(child =>
                        child.Kind == WordSemanticNodeKind.TableRow
                    )
            );
            var blockWrapper = Assert.Single(
                semantic.Nodes,
                node =>
                    node.Kind == WordSemanticNodeKind.ContentControl
                    && node.Children.Any(child =>
                        child.Kind == WordSemanticNodeKind.Table
                    )
            );
            var table = Assert.Single(
                blockWrapper.Children,
                node => node.Kind == WordSemanticNodeKind.Table
            );
            var nestedTable = Assert.Single(
                semantic.Nodes,
                node =>
                    node.Kind == WordSemanticNodeKind.Table
                    && node.Id != table.Id
            );
            var nestedTableCell = Assert.Single(
                semantic.Nodes,
                node =>
                    node.Kind == WordSemanticNodeKind.TableCell
                    && node.DescendantsAndSelf().Any(candidate =>
                        candidate.Id == nestedTable.Id
                    )
            );
            var nestedTableRow = Assert.Single(
                semantic.Nodes,
                node =>
                    node.Kind == WordSemanticNodeKind.TableRow
                    && node.DescendantsAndSelf().Any(candidate =>
                        candidate.Id == nestedTable.Id
                    )
            );
            var operation = new SemanticHtmlWordPackageOperation();
            var rowOutput = Path.Combine(directory, "rows.html");
            var cellOutput = Path.Combine(directory, "cells.html");
            var revisionOutput = Path.Combine(directory, "revision-rows.html");
            var tableOutput = Path.Combine(directory, "table.html");
            var nestedRowsOutput = Path.Combine(directory, "nested-rows.html");
            var nestedCellsOutput = Path.Combine(directory, "nested-cells.html");
            var blockOutput = Path.Combine(directory, "block.html");
            var nestedTableCellOutput = Path.Combine(directory, "nested-table-cell.html");
            var nestedTableRowOutput = Path.Combine(directory, "nested-table-row.html");

            var rows = operation.Execute(
                new SemanticHtmlWordPackageRequest(
                    input,
                    rowOutput,
                    package.Fingerprint,
                    TargetNodeId: rowWrapper.Id.Value
                )
            );
            var cells = operation.Execute(
                new SemanticHtmlWordPackageRequest(
                    input,
                    cellOutput,
                    package.Fingerprint,
                    TargetNodeId: cellWrapper.Id.Value
                )
            );
            var revision = operation.Execute(
                new SemanticHtmlWordPackageRequest(
                    input,
                    revisionOutput,
                    package.Fingerprint,
                    TargetNodeId: rowRevision.Id.Value
                )
            );
            var tableResult = operation.Execute(
                new SemanticHtmlWordPackageRequest(
                    input,
                    tableOutput,
                    package.Fingerprint,
                    TargetNodeId: table.Id.Value
                )
            );
            var nestedRows = operation.Execute(
                new SemanticHtmlWordPackageRequest(
                    input,
                    nestedRowsOutput,
                    package.Fingerprint,
                    TargetNodeId: nestedRowWrapper.Id.Value
                )
            );
            var nestedCells = operation.Execute(
                new SemanticHtmlWordPackageRequest(
                    input,
                    nestedCellsOutput,
                    package.Fingerprint,
                    TargetNodeId: nestedCellWrapper.Id.Value
                )
            );
            var block = operation.Execute(
                new SemanticHtmlWordPackageRequest(
                    input,
                    blockOutput,
                    package.Fingerprint,
                    TargetNodeId: blockWrapper.Id.Value
                )
            );
            var selectedNestedTableCell = operation.Execute(
                new SemanticHtmlWordPackageRequest(
                    input,
                    nestedTableCellOutput,
                    package.Fingerprint,
                    TargetNodeId: nestedTableCell.Id.Value
                )
            );
            var selectedNestedTableRow = operation.Execute(
                new SemanticHtmlWordPackageRequest(
                    input,
                    nestedTableRowOutput,
                    package.Fingerprint,
                    TargetNodeId: nestedTableRow.Id.Value
                )
            );

            var rowHtml = File.ReadAllText(rowOutput);
            Assert.Contains(
                "<table class=\"wt-table wt-fragment-context\"><tbody class=\"wt-fragment-target wt-content_control\"",
                rowHtml,
                StringComparison.Ordinal
            );
            Assert.Contains(">row target</", rowHtml, StringComparison.Ordinal);
            Assert.DoesNotContain(">cell target</", rowHtml, StringComparison.Ordinal);
            Assert.Equal(SemanticHtmlFragmentWrapper.Table, rows.FragmentWrapper);

            var cellHtml = File.ReadAllText(cellOutput);
            Assert.Contains(
                "<tbody class=\"wt-fragment-target wt-content_control\"",
                cellHtml,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "<tr class=\"wt-fragment-context\"><td",
                cellHtml,
                StringComparison.Ordinal
            );
            Assert.Contains(">cell target</", cellHtml, StringComparison.Ordinal);
            Assert.DoesNotContain(">row target</", cellHtml, StringComparison.Ordinal);
            Assert.Equal(
                SemanticHtmlFragmentWrapper.TableBodyRow,
                cells.FragmentWrapper
            );

            var revisionHtml = File.ReadAllText(revisionOutput);
            Assert.Contains("wt-revision-insertion", revisionHtml, StringComparison.Ordinal);
            Assert.Contains(
                "data-revision-kind=\"insertion\"",
                revisionHtml,
                StringComparison.Ordinal
            );
            Assert.Contains(">revision row target</", revisionHtml, StringComparison.Ordinal);
            Assert.Contains("TRACKED_REVISIONS_ANNOTATED", revision.Warnings);
            Assert.Equal(SemanticHtmlFragmentWrapper.Table, revision.FragmentWrapper);

            var tableHtml = File.ReadAllText(tableOutput);
            Assert.Contains(
                "<tbody class=\"wt-fragment-target wt-content_control\"",
                tableHtml,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "<tbody class=\"wt-fragment-context\"><tr",
                tableHtml,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "<tbody class=\"wt-fragment-target wt-revision wt-revision-insertion\"",
                tableHtml,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain("</tbody><tr", tableHtml, StringComparison.Ordinal);
            Assert.Equal(
                SemanticHtmlFragmentWrapper.TableBodies,
                tableResult.FragmentWrapper
            );
            Assert.Contains(
                "FRAGMENT_TABLE_CONTEXT_SYNTHESIZED",
                tableResult.Warnings
            );
            AssertTableHierarchy(tableHtml);

            var nestedRowHtml = File.ReadAllText(nestedRowsOutput);
            Assert.Contains(">row target</", nestedRowHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("<span><tbody", nestedRowHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("<tbody><tbody", nestedRowHtml, StringComparison.Ordinal);
            Assert.Contains(
                "NESTED_TABLE_FRAGMENT_WRAPPERS_FLATTENED",
                nestedRows.Warnings
            );
            Assert.Equal(SemanticHtmlFragmentWrapper.Table, nestedRows.FragmentWrapper);

            var nestedCellHtml = File.ReadAllText(nestedCellsOutput);
            Assert.Contains(">cell target</", nestedCellHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("<span><td", nestedCellHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("<tr><tr", nestedCellHtml, StringComparison.Ordinal);
            Assert.Contains(
                "NESTED_TABLE_FRAGMENT_WRAPPERS_FLATTENED",
                nestedCells.Warnings
            );
            Assert.Equal(
                SemanticHtmlFragmentWrapper.TableBodyRow,
                nestedCells.FragmentWrapper
            );

            var blockHtml = File.ReadAllText(blockOutput);
            Assert.Contains(">nested wrapped row</", blockHtml, StringComparison.Ordinal);
            Assert.Contains(">nested raw row</", blockHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("</tbody><tr", blockHtml, StringComparison.Ordinal);
            AssertTableHierarchy(blockHtml);
            Assert.Equal(SemanticHtmlFragmentWrapper.None, block.FragmentWrapper);

            var nestedTableCellHtml = File.ReadAllText(nestedTableCellOutput);
            Assert.Contains(">nested wrapped row</", nestedTableCellHtml, StringComparison.Ordinal);
            Assert.Contains(">nested raw row</", nestedTableCellHtml, StringComparison.Ordinal);
            AssertTableHierarchy(nestedTableCellHtml);
            Assert.Equal(
                SemanticHtmlFragmentWrapper.TableBodyRow,
                selectedNestedTableCell.FragmentWrapper
            );

            var nestedTableRowHtml = File.ReadAllText(nestedTableRowOutput);
            Assert.Contains(">nested wrapped row</", nestedTableRowHtml, StringComparison.Ordinal);
            Assert.Contains(">nested raw row</", nestedTableRowHtml, StringComparison.Ordinal);
            AssertTableHierarchy(nestedTableRowHtml);
            Assert.Equal(
                SemanticHtmlFragmentWrapper.TableBody,
                selectedNestedTableRow.FragmentWrapper
            );
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

    private static void CreateTableWrapperPackage(string path)
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
            $"""
            <w:document xmlns:w="{WordNamespace}">
              <w:body>
                <w:sdt><w:sdtContent><w:tbl>
                  <w:sdt><w:sdtContent>
                    <w:sdt><w:sdtContent>
                      <w:tr><w:tc><w:p><w:r><w:t>row target</w:t></w:r></w:p></w:tc></w:tr>
                    </w:sdtContent></w:sdt>
                  </w:sdtContent></w:sdt>
                  <w:tr><w:sdt><w:sdtContent>
                    <w:sdt><w:sdtContent>
                      <w:tc><w:p><w:r><w:t>cell target</w:t></w:r></w:p></w:tc>
                    </w:sdtContent></w:sdt>
                  </w:sdtContent></w:sdt></w:tr>
                  <w:ins w:id="1" w:author="Tester"><w:tr><w:tc>
                    <w:p><w:r><w:t>revision row target</w:t></w:r></w:p>
                    <w:tbl>
                      <w:sdt><w:sdtContent><w:tr><w:tc>
                        <w:p><w:r><w:t>nested wrapped row</w:t></w:r></w:p>
                      </w:tc></w:tr></w:sdtContent></w:sdt>
                      <w:tr><w:tc><w:p><w:r><w:t>nested raw row</w:t></w:r></w:p></w:tc></w:tr>
                    </w:tbl>
                  </w:tc></w:tr></w:ins>
                </w:tbl></w:sdtContent></w:sdt>
              </w:body>
            </w:document>
            """
        );
    }

    private static void AssertTableHierarchy(string html)
    {
        var start = html.IndexOf("<table", StringComparison.Ordinal);
        var end = html.LastIndexOf("</table>", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var table = XElement.Parse(html[start..(end + "</table>".Length)]);
        Assert.All(
            table.Descendants("tbody"),
            body => Assert.Equal("table", body.Parent?.Name.LocalName)
        );
        Assert.All(
            table.Descendants("tr"),
            row => Assert.Equal("tbody", row.Parent?.Name.LocalName)
        );
        Assert.All(
            table.Descendants("td"),
            cell => Assert.Equal("tr", cell.Parent?.Name.LocalName)
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
