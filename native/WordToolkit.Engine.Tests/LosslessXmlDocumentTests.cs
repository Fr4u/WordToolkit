using System.Text;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Tests;

public sealed class LosslessXmlDocumentTests
{
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
