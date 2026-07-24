using System.Text;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Tests;

public sealed class LosslessXmlDocumentTests
{
    [Fact]
    public void RejectsTheXmlReservationBeforeParsingWhenTheOperationLeaseIsFull()
    {
        var bytes = Encoding.UTF8.GetBytes("<root><item>value</item></root>");
        var original = bytes.ToArray();
        var lease = new WordOperationResourceLease(4_096);

        var exception = Assert.Throws<WordOperationResourceLimitException>(() =>
            LosslessXmlDocument.Parse(
                bytes,
                LosslessXmlOptions.Default,
                lease,
                WordOperationResourceStage.Styles
            )
        );

        Assert.Equal(WordOperationResourceStage.Styles, exception.Stage);
        Assert.Equal(4_096, exception.AccountedBytes);
        Assert.True(exception.AttemptedBytes > bytes.Length);
        Assert.Equal(original, bytes);
    }

    [Fact]
    public void ParsesSourceBackedElementsAttributesAndExactNoOp()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <x:root z="last" xmlns:x="urn:test" xmlns:q="urn:other">
              <!--opaque--><x:item q:flag='yes'>A &amp; B</x:item>
            </x:root>
            """;
        var bytes = Encoding.UTF8.GetBytes(xml);

        var source = LosslessXmlDocument.Parse(bytes);
        var item = source.Elements.Single(element => element.LocalName == "item");
        var result = source.ReplaceElementText(
            item.Ordinal,
            "A & B",
            expectedValue: "A & B",
            expectedSourceSha256: source.SourceSha256
        );

        Assert.Equal(bytes, result);
        Assert.Equal("x:root", source.Root.QualifiedName);
        Assert.Equal("urn:test", item.NamespaceUri);
        Assert.Equal("yes", Assert.Single(item.Attributes).Value);
        Assert.Equal(
            Encoding.UTF8.GetBytes("<x:item q:flag='yes'>A &amp; B</x:item>"),
            source.SourceBytes.Slice(item.FullSpan.ByteOffset, item.FullSpan.ByteLength).ToArray()
        );
    }

    [Fact]
    public void ReplacesOnlyLeafContentAndEscapesXmlCharacters()
    {
        const string xml = "<r xmlns='urn:r' a='1' b=\"2\"><!--keep--><t k='v'>old</t><u q='z'/></r>";
        var bytes = Encoding.UTF8.GetBytes(xml);
        var source = LosslessXmlDocument.Parse(bytes);
        var target = source.Elements.Single(element => element.LocalName == "t");

        var result = source.ReplaceElementText(
            target.Ordinal,
            "A < B & C > D\rE",
            expectedValue: "old",
            expectedSourceSha256: source.SourceSha256
        );

        var expected = "<r xmlns='urn:r' a='1' b=\"2\"><!--keep--><t k='v'>A &lt; B &amp; C &gt; D&#xD;E</t><u q='z'/></r>";
        Assert.Equal(expected, Encoding.UTF8.GetString(result));
        var reparsed = LosslessXmlDocument.Parse(result);
        Assert.Equal(
            "A < B & C > D\rE",
            reparsed.Elements.Single(element => element.LocalName == "t").Value
        );
    }

    [Fact]
    public void AddsXmlSpaceOnlyWhenBoundaryWhitespaceRequiresIt()
    {
        const string xml = "<w:t xmlns:w='urn:w' data='untouched'>old</w:t>";
        var source = LosslessXmlDocument.Parse(Encoding.UTF8.GetBytes(xml));

        var result = source.ReplaceElementText(
            source.Root.Ordinal,
            " leading and trailing ",
            preserveBoundaryWhitespace: true
        );

        Assert.Equal(
            "<w:t xmlns:w='urn:w' data='untouched' xml:space=\"preserve\"> leading and trailing </w:t>",
            Encoding.UTF8.GetString(result)
        );
    }

    [Fact]
    public void RewritesExistingXmlSpaceValueWithoutDuplicatingAttribute()
    {
        const string xml = "<w:t xmlns:w='urn:w' xml:space='default'>old</w:t>";
        var source = LosslessXmlDocument.Parse(Encoding.UTF8.GetBytes(xml));

        var result = source.ReplaceElementText(
            source.Root.Ordinal,
            " padded ",
            preserveBoundaryWhitespace: true
        );

        Assert.Equal(
            "<w:t xmlns:w='urn:w' xml:space='preserve'> padded </w:t>",
            Encoding.UTF8.GetString(result)
        );
    }

    [Fact]
    public void ExpandsSelfClosingElementAtTheSlashAndKeepsOriginalSpacing()
    {
        const string xml = "<w:t xmlns:w='urn:w' data='v' />";
        var source = LosslessXmlDocument.Parse(Encoding.UTF8.GetBytes(xml));

        var result = source.ReplaceElementText(
            source.Root.Ordinal,
            " value ",
            preserveBoundaryWhitespace: true
        );

        Assert.Equal(
            "<w:t xmlns:w='urn:w' data='v'  xml:space=\"preserve\"> value </w:t>",
            Encoding.UTF8.GetString(result)
        );
    }

    [Fact]
    public void PreservesUtf8BomAndUntouchedBytes()
    {
        const string xml = "<?xml version='1.0' encoding='utf-8'?><r><t>stare</t><u a='1'/></r>";
        var payload = Encoding.UTF8.GetBytes(xml);
        var bytes = Encoding.UTF8.GetPreamble().Concat(payload).ToArray();
        var source = LosslessXmlDocument.Parse(bytes);

        var result = source.ReplaceElementText(
            source.Elements.Single(element => element.LocalName == "t").Ordinal,
            "nowe"
        );

        Assert.Equal(3, source.ByteOrderMarkLength);
        Assert.True(result.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.Equal(
            "<?xml version='1.0' encoding='utf-8'?><r><t>nowe</t><u a='1'/></r>",
            Encoding.UTF8.GetString(result.AsSpan(3))
        );
    }

    [Fact]
    public void PreservesUtf16LittleEndianEncodingAndBom()
    {
        const string xml = "<?xml version='1.0' encoding='utf-16'?><r><t>żółć</t><u/></r>";
        var encoding = new UnicodeEncoding(false, true, true);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(xml)).ToArray();
        var source = LosslessXmlDocument.Parse(bytes);

        var result = source.ReplaceElementText(
            source.Elements.Single(element => element.LocalName == "t").Ordinal,
            "gęślą"
        );

        Assert.Equal(2, source.ByteOrderMarkLength);
        Assert.True(result.AsSpan().StartsWith(encoding.GetPreamble()));
        Assert.Equal(
            "<?xml version='1.0' encoding='utf-16'?><r><t>gęślą</t><u/></r>",
            encoding.GetString(result.AsSpan(2))
        );
    }

    [Fact]
    public void UsesCharacterReferenceWhenSingleByteEncodingCannotRepresentText()
    {
        const string xml = "<?xml version='1.0' encoding='iso-8859-1'?><r><t>old</t></r>";
        var encoding = Encoding.GetEncoding(
            "iso-8859-1",
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback
        );
        var source = LosslessXmlDocument.Parse(encoding.GetBytes(xml));

        var result = source.ReplaceElementText(
            source.Elements.Single(element => element.LocalName == "t").Ordinal,
            "wynik 🧮"
        );

        Assert.Contains("wynik &#x1F9EE;", encoding.GetString(result), StringComparison.Ordinal);
        Assert.Equal(
            "wynik 🧮",
            LosslessXmlDocument.Parse(result)
                .Elements.Single(element => element.LocalName == "t").Value
        );
    }

    [Fact]
    public void RejectsDtdBeforeLexicalProjection()
    {
        const string xml = "<!DOCTYPE r [<!ENTITY x 'boom'>]><r>&x;</r>";

        var exception = Assert.Throws<LosslessXmlParseException>(() =>
            LosslessXmlDocument.Parse(Encoding.UTF8.GetBytes(xml))
        );

        Assert.Contains("safe", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsStaleSourceHash()
    {
        var source = LosslessXmlDocument.Parse(Encoding.UTF8.GetBytes("<r>old</r>"));

        Assert.Throws<LosslessXmlPreconditionException>(() =>
            source.ReplaceElementText(
                source.Root.Ordinal,
                "new",
                expectedSourceSha256: new string('0', 64)
            )
        );
    }

    [Fact]
    public void RejectsTextReplacementThatWouldEraseCommentOrCdata()
    {
        var withComment = LosslessXmlDocument.Parse(
            Encoding.UTF8.GetBytes("<r>before<!--keep-->after</r>")
        );
        var withCdata = LosslessXmlDocument.Parse(
            Encoding.UTF8.GetBytes("<r><![CDATA[raw]]></r>")
        );

        Assert.Throws<LosslessXmlEditException>(() =>
            withComment.ReplaceElementText(withComment.Root.Ordinal, "new")
        );
        Assert.Throws<LosslessXmlEditException>(() =>
            withCdata.ReplaceElementText(withCdata.Root.Ordinal, "new")
        );
    }

    [Fact]
    public void RejectsOverlappingAndAmbiguousPatches()
    {
        var source = LosslessXmlDocument.Parse(Encoding.UTF8.GetBytes("<r>value</r>"));

        Assert.Throws<LosslessXmlEditException>(() =>
            source.ApplyPatches(
                new[]
                {
                    new XmlSourcePatch(3, 3, Encoding.UTF8.GetBytes("a")),
                    new XmlSourcePatch(5, 2, Encoding.UTF8.GetBytes("b")),
                }
            )
        );
        Assert.Throws<LosslessXmlEditException>(() =>
            source.ApplyPatches(
                new[]
                {
                    new XmlSourcePatch(3, 0, Encoding.UTF8.GetBytes("a")),
                    new XmlSourcePatch(3, 0, Encoding.UTF8.GetBytes("b")),
                }
            )
        );
    }

    [Fact]
    public void RemovesUnwrapsAndRenamesElementsWithoutReserializingUntouchedMarkup()
    {
        const string xml = "<w:r xmlns:w='urn:w' a='keep'><!--opaque--><w:del><w:delText xml:space='preserve'> old </w:delText></w:del><w:tail z=\"1\"/></w:r>";
        var source = LosslessXmlDocument.Parse(Encoding.UTF8.GetBytes(xml));
        var deletion = source.Elements.Single(element => element.LocalName == "del");
        var deletedText = source.Elements.Single(element => element.LocalName == "delText");

        var rejected = source.ApplyPatches(
            source.CreateElementUnwrapPatches(deletion.Ordinal)
                .Concat(source.CreateElementLocalNameRenamePatches(deletedText.Ordinal, "t"))
        );
        var accepted = source.ApplyPatches(
            [source.CreateElementRemovalPatch(deletion.Ordinal)]
        );

        Assert.Equal(
            "<w:r xmlns:w='urn:w' a='keep'><!--opaque--><w:t xml:space='preserve'> old </w:t><w:tail z=\"1\"/></w:r>",
            Encoding.UTF8.GetString(rejected)
        );
        Assert.Equal(
            "<w:r xmlns:w='urn:w' a='keep'><!--opaque--><w:tail z=\"1\"/></w:r>",
            Encoding.UTF8.GetString(accepted)
        );
    }

    [Fact]
    public void SetsExistingAndMissingNamespacedAttributesWithoutReserializingElement()
    {
        const string xml = "<w:root xmlns:w='urn:w'><w:a w:val='old' keep=\"1\"/><w:b keep='2'/></w:root>";
        var source = LosslessXmlDocument.Parse(Encoding.UTF8.GetBytes(xml));
        var first = source.Elements.Single(element => element.LocalName == "a");
        var second = source.Elements.Single(element => element.LocalName == "b");

        var result = source.ApplyPatches(
            source.CreateElementAttributeValuePatches(
                first.Ordinal,
                "urn:w",
                "val",
                "new & 'value'",
                expectedValue: "old",
                preferredPrefix: "w"
            ).Concat(
                source.CreateElementAttributeValuePatches(
                    second.Ordinal,
                    "urn:new",
                    "flag",
                    "yes",
                    preferredPrefix: "wtk"
                )
            )
        );

        Assert.Equal(
            "<w:root xmlns:w='urn:w'><w:a w:val='new &amp; &apos;value&apos;' keep=\"1\"/><w:b keep='2' xmlns:wtk=\"urn:new\" wtk:flag=\"yes\"/></w:root>",
            Encoding.UTF8.GetString(result)
        );
        var reparsed = LosslessXmlDocument.Parse(result);
        Assert.Equal(
            "new & 'value'",
            reparsed.Elements.Single(element => element.LocalName == "a")
                .Attributes.Single(attribute => attribute.LocalName == "val")
                .Value
        );
    }

    [Fact]
    public void InsertsSelfContainedContentIntoNormalAndSelfClosingElements()
    {
        const string xml = "<w:root xmlns:w='urn:w'><w:a>tail</w:a><w:b keep='1'/></w:root>";
        var source = LosslessXmlDocument.Parse(Encoding.UTF8.GetBytes(xml));
        var first = source.Elements.Single(element => element.LocalName == "a");
        var second = source.Elements.Single(element => element.LocalName == "b");
        const string child = "<wtk:style xmlns:wtk=\"urn:w\" wtk:val=\"Definition\"/>";

        var result = source.ApplyPatches(
            new[]
            {
                source.CreateElementContentInsertionPatch(
                    first.Ordinal,
                    child,
                    XmlContentInsertionPosition.Prepend
                ),
                source.CreateElementContentInsertionPatch(second.Ordinal, child),
            }
        );

        Assert.Equal(
            "<w:root xmlns:w='urn:w'><w:a><wtk:style xmlns:wtk=\"urn:w\" wtk:val=\"Definition\"/>tail</w:a><w:b keep='1'><wtk:style xmlns:wtk=\"urn:w\" wtk:val=\"Definition\"/></w:b></w:root>",
            Encoding.UTF8.GetString(result)
        );
        Assert.Equal(2, LosslessXmlDocument.Parse(result).Elements.Count(element =>
            element.LocalName == "style"
        ));
    }

    [Fact]
    public void MissingAttributeNeverRebindsAnExistingNamespacePrefix()
    {
        const string xml = "<w:root xmlns:w='urn:w' xmlns:wtk='urn:other'><w:item wtk:keep='yes'/></w:root>";
        var source = LosslessXmlDocument.Parse(Encoding.UTF8.GetBytes(xml));
        var item = source.Elements.Single(element => element.LocalName == "item");

        var result = source.ApplyPatches(
            source.CreateElementAttributeValuePatches(
                item.Ordinal,
                "urn:new",
                "flag",
                "on",
                preferredPrefix: "wtk"
            )
        );

        Assert.Equal(
            "<w:root xmlns:w='urn:w' xmlns:wtk='urn:other'><w:item wtk:keep='yes' xmlns:wtk1=\"urn:new\" wtk1:flag=\"on\"/></w:root>",
            Encoding.UTF8.GetString(result)
        );
        var reparsed = LosslessXmlDocument.Parse(result);
        var attributes = reparsed.Elements.Single(element => element.LocalName == "item")
            .Attributes;
        Assert.Contains(attributes, attribute =>
            attribute.NamespaceUri == "urn:other"
            && attribute.LocalName == "keep"
            && attribute.Value == "yes"
        );
        Assert.Contains(attributes, attribute =>
            attribute.NamespaceUri == "urn:new"
            && attribute.LocalName == "flag"
            && attribute.Value == "on"
        );
    }

    [Fact]
    public void RejectsUnsafeFragmentsAndStaleAttributeValues()
    {
        var source = LosslessXmlDocument.Parse(
            Encoding.UTF8.GetBytes("<w:r xmlns:w='urn:w' w:val='old'/>")
        );

        Assert.Throws<LosslessXmlPreconditionException>(() =>
            source.CreateElementAttributeValuePatches(
                source.Root.Ordinal,
                "urn:w",
                "val",
                "new",
                expectedValue: "other"
            )
        );
        Assert.Throws<LosslessXmlEditException>(() =>
            source.CreateElementContentInsertionPatch(
                source.Root.Ordinal,
                "<x:broken/>"
            )
        );
    }

    [Fact]
    public void StructuralPatchesRespectUtf16ByteOffsetsAndSelfClosingUnwrap()
    {
        const string xml = "<?xml version='1.0' encoding='utf-16'?><r xmlns='urn:r'><empty/><old>żółć</old></r>";
        var encoding = new UnicodeEncoding(false, true, true);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(xml)).ToArray();
        var source = LosslessXmlDocument.Parse(bytes);
        var empty = source.Elements.Single(element => element.LocalName == "empty");
        var old = source.Elements.Single(element => element.LocalName == "old");

        var result = source.ApplyPatches(
            source.CreateElementUnwrapPatches(empty.Ordinal)
                .Concat(source.CreateElementLocalNameRenamePatches(old.Ordinal, "new"))
        );

        Assert.True(result.AsSpan().StartsWith(encoding.GetPreamble()));
        Assert.Equal(
            "<?xml version='1.0' encoding='utf-16'?><r xmlns='urn:r'><new>żółć</new></r>",
            encoding.GetString(result.AsSpan(2))
        );
    }

    [Fact]
    public void CancellationStopsParsingBeforeMaterialization()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            LosslessXmlDocument.Parse(
                Encoding.UTF8.GetBytes("<r/>"),
                cancellationToken: cancellation.Token
            )
        );
    }

    [Fact]
    public void RandomizedLeafSplicesPreserveEveryByteOutsideTargetContent()
    {
        var random = new Random(0x5EED_2026);
        var oldValues = new[]
        {
            "plain",
            "A & B",
            "2 < 3 > 1",
            "zażółć gęślą",
            "emoji 🧮",
            "line\rbreak",
        };
        var newValues = new[]
        {
            "replacement",
            "x & y",
            "a < b > c",
            "nowa gęślą",
            "math ∑🧮",
            "carriage\rreturn",
        };
        var prefixes = new[] { "w", "x", "word", "n7" };
        for (var iteration = 0; iteration < 300; iteration++)
        {
            var prefix = prefixes[random.Next(prefixes.Length)];
            var oldValue = oldValues[random.Next(oldValues.Length)];
            var newValue = newValues[random.Next(newValues.Length)];
            var quote = random.Next(2) == 0 ? '\'' : '"';
            var xml = $"<{prefix}:root xmlns:{prefix}='urn:test'><!--opaque--><{prefix}:t a={quote}v{quote}>{Escape(oldValue)}</{prefix}:t><{prefix}:u z='1'/></{prefix}:root>";
            var bytes = Encoding.UTF8.GetBytes(xml);
            var source = LosslessXmlDocument.Parse(bytes);
            var target = source.Elements.Single(element => element.LocalName == "t");

            var result = source.ReplaceElementText(
                target.Ordinal,
                newValue,
                expectedValue: oldValue,
                expectedSourceSha256: source.SourceSha256
            );
            var replacement = Encoding.UTF8.GetBytes(Escape(newValue));

            Assert.Equal(
                bytes.AsSpan(0, target.ContentSpan.ByteOffset).ToArray(),
                result.AsSpan(0, target.ContentSpan.ByteOffset).ToArray()
            );
            Assert.Equal(
                bytes.AsSpan(target.ContentSpan.EndByteOffset).ToArray(),
                result.AsSpan(target.ContentSpan.ByteOffset + replacement.Length).ToArray()
            );
            Assert.Equal(
                newValue,
                LosslessXmlDocument.Parse(result)
                    .Elements.Single(element => element.LocalName == "t").Value
            );
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PreservesAdditionalUnicodeByteOrders(int encodingKind)
    {
        Encoding encoding = encodingKind switch
        {
            0 => new UnicodeEncoding(true, true, true),
            1 => new UTF32Encoding(false, true, true),
            2 => new UTF32Encoding(true, true, true),
            _ => throw new ArgumentOutOfRangeException(nameof(encodingKind)),
        };
        var xml = $"<?xml version='1.0' encoding='{encoding.WebName}'?><r><t>stare</t></r>";
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(xml)).ToArray();
        var source = LosslessXmlDocument.Parse(bytes);

        var result = source.ReplaceElementText(
            source.Elements.Single(element => element.LocalName == "t").Ordinal,
            "nowe 🧮"
        );

        Assert.True(result.AsSpan().StartsWith(encoding.GetPreamble()));
        Assert.Equal(
            "nowe 🧮",
            LosslessXmlDocument.Parse(result)
                .Elements.Single(element => element.LocalName == "t").Value
        );
    }

    [Fact]
    public void InsertsSiblingWithoutRewritingEitherNeighbor()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "<w:numbering xmlns:w='urn:w'><w:num w:numId='4'/><w:numIdMacAtCleanup w:val='4'/></w:numbering>"
        );
        var source = LosslessXmlDocument.Parse(bytes);
        var cleanup = source.Elements.Single(element =>
            element.LocalName == "numIdMacAtCleanup"
        );

        var result = source.ApplyPatches(
            [source.CreateElementSiblingInsertionPatch(
                cleanup.Ordinal,
                "<w:num xmlns:w='urn:w' w:numId='5'/>",
                XmlSiblingInsertionPosition.Before
            )]
        );

        Assert.Equal(
            "<w:numbering xmlns:w='urn:w'><w:num w:numId='4'/><w:num xmlns:w='urn:w' w:numId='5'/><w:numIdMacAtCleanup w:val='4'/></w:numbering>",
            Encoding.UTF8.GetString(result)
        );
    }

    [Fact]
    public void StringReplacementUsesSourceEncodingAndRejectsUnsafeXml()
    {
        const string xml = "<?xml version='1.0' encoding='utf-16'?><r><a>stare</a><b/></r>";
        var encoding = new UnicodeEncoding(false, true, true);
        var source = LosslessXmlDocument.Parse(
            encoding.GetPreamble().Concat(encoding.GetBytes(xml)).ToArray()
        );
        var a = source.Elements.Single(element => element.LocalName == "a");

        var result = source.ApplyPatches(
            [source.CreateElementReplacementPatch(a.Ordinal, "<a>nowe ∑</a>")]
        );

        Assert.Equal(
            "<?xml version='1.0' encoding='utf-16'?><r><a>nowe ∑</a><b/></r>",
            encoding.GetString(result.AsSpan(encoding.GetPreamble().Length))
        );
        Assert.Throws<LosslessXmlEditException>(() =>
            source.CreateElementReplacementPatch(a.Ordinal, "<x:broken/>")
        );
    }

    private static string Escape(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            result.Append(
                character switch
                {
                    '&' => "&amp;",
                    '<' => "&lt;",
                    '>' => "&gt;",
                    '\r' => "&#xD;",
                    _ => character.ToString(),
                }
            );
        }

        return result.ToString();
    }
}
