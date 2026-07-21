using System.IO.Compression;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class WordSemanticEditorTests
{
    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void ReplacesProjectedTextAndPreservesUnrelatedPackageAndXmlBytes()
    {
        const string documentXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <x:document xmlns:x="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:future="urn:future">
              <x:body data-order="untouched"><!--keep--><x:p><x:r><x:t data-z="2" data-a="1">old &amp; guarded</x:t></x:r></x:p><future:opaque future:value="stay"/><x:sectPr/></x:body>
            </x:document>
            """;
        using var package = BuildPackage(documentXml, opaqueBytes: [0, 1, 2, 255]);
        var reader = new OpcPackageReader();
        var snapshot = reader.Read(package);
        var semantics = new WordSemanticProjector().Project(snapshot);
        var text = Assert.Single(
            semantics.Nodes,
            node => node.Kind == WordSemanticNodeKind.Text
        );

        var mutation = new WordSemanticEditor().ReplaceText(
            snapshot,
            semantics,
            text.Id,
            " changed & guarded ",
            expectedText: "old & guarded"
        );
        using var output = new MemoryStream();
        new OpcPackageSerializer().Write(output, mutation);
        output.Position = 0;
        var changed = reader.Read(output);

        var changedXml = Encoding.UTF8.GetString(
            changed.Parts["/word/document.xml"].Entry.Content.Span
        );
        Assert.Equal(
            documentXml.Replace(
                "<x:t data-z=\"2\" data-a=\"1\">old &amp; guarded</x:t>",
                "<x:t data-z=\"2\" data-a=\"1\" xml:space=\"preserve\"> changed &amp; guarded </x:t>",
                StringComparison.Ordinal
            ),
            changedXml
        );
        Assert.Equal(
            snapshot.Parts["/custom/opaque.bin"].Entry.Sha256,
            changed.Parts["/custom/opaque.bin"].Entry.Sha256
        );
        Assert.Equal(
            " changed & guarded ",
            Assert.Single(
                new WordSemanticProjector().Project(changed).Nodes,
                node => node.Kind == WordSemanticNodeKind.Text
            ).Text
        );
    }

    [Fact]
    public void NoOpTextReplacementProducesEmptyMutation()
    {
        using var package = BuildPackage(DocumentXml("same"));
        var snapshot = new OpcPackageReader().Read(package);
        var semantics = new WordSemanticProjector().Project(snapshot);
        var text = semantics.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Text);

        var mutation = new WordSemanticEditor().ReplaceText(
            snapshot,
            semantics,
            text.Id,
            "same"
        );

        Assert.False(mutation.HasChanges);
    }

    [Fact]
    public void ExpandsSelfClosingWordTextWithWhitespacePreservation()
    {
        using var package = BuildPackage(DocumentXml(null));
        var reader = new OpcPackageReader();
        var snapshot = reader.Read(package);
        var semantics = new WordSemanticProjector().Project(snapshot);
        var text = semantics.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Text);

        var mutation = new WordSemanticEditor().ReplaceText(
            snapshot,
            semantics,
            text.Id,
            " value "
        );
        using var output = new MemoryStream();
        new OpcPackageSerializer().Write(output, mutation);
        output.Position = 0;

        Assert.Contains(
            "<w:t  xml:space=\"preserve\"> value </w:t>",
            Encoding.UTF8.GetString(
                reader.Read(output).Parts["/word/document.xml"].Entry.Content.Span
            ),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void RejectsWrongExpectedTextAndDifferentPackageSnapshot()
    {
        using var firstPackage = BuildPackage(DocumentXml("first"));
        var reader = new OpcPackageReader();
        var first = reader.Read(firstPackage);
        var semantics = new WordSemanticProjector().Project(first);
        var text = semantics.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Text);

        Assert.Throws<WordSemanticPreconditionException>(() =>
            new WordSemanticEditor().ReplaceText(
                first,
                semantics,
                text.Id,
                "new",
                expectedText: "wrong"
            )
        );

        using var secondPackage = BuildPackage(DocumentXml("second"));
        var second = reader.Read(secondPackage);
        Assert.Throws<WordSemanticPreconditionException>(() =>
            new WordSemanticEditor().ReplaceText(
                second,
                semantics,
                text.Id,
                "new"
            )
        );
    }

    [Fact]
    public void RejectsNonTextNodeAndTextContainingLexicalMarkup()
    {
        using var package = BuildPackage(
            $"""
            <w:document xmlns:w="{WordNamespace}"><w:body><w:p><w:r><w:t>be<!--keep-->fore</w:t></w:r></w:p></w:body></w:document>
            """
        );
        var snapshot = new OpcPackageReader().Read(package);
        var semantics = new WordSemanticProjector().Project(snapshot);
        var paragraph = semantics.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Paragraph);
        var text = semantics.Nodes.Single(node => node.Kind == WordSemanticNodeKind.Text);
        var editor = new WordSemanticEditor();

        Assert.Throws<WordSemanticEditException>(() =>
            editor.ReplaceText(snapshot, semantics, paragraph.Id, "new")
        );
        Assert.Throws<WordSemanticEditException>(() =>
            editor.ReplaceText(snapshot, semantics, text.Id, "new")
        );
    }

    private static string DocumentXml(string? text)
    {
        var textElement = text is null ? "<w:t />" : $"<w:t>{text}</w:t>";
        return $"""
            <w:document xmlns:w="{WordNamespace}"><w:body><w:p><w:r>{textElement}</w:r></w:p></w:body></w:document>
            """;
    }

    private static MemoryStream BuildPackage(
        string documentXml,
        byte[]? opaqueBytes = null
    )
    {
        var entries = new List<(string Name, byte[] Content)>
        {
            ("[Content_Types].xml", Encoding.UTF8.GetBytes(ContentTypes(opaqueBytes is not null))),
            ("_rels/.rels", Encoding.UTF8.GetBytes(RootRelationships())),
            ("word/document.xml", Encoding.UTF8.GetBytes(documentXml)),
        };
        if (opaqueBytes is not null)
        {
            entries.Add(("custom/opaque.bin", opaqueBytes));
        }

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static string ContentTypes(bool includeOpaque) => $"""
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
          <Default Extension="xml" ContentType="application/xml" />
          {(includeOpaque ? "<Default Extension=\"bin\" ContentType=\"application/octet-stream\" />" : string.Empty)}
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" />
        </Types>
        """;

    private static string RootRelationships() => """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml" />
        </Relationships>
        """;
}
