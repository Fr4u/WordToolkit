using System.Reflection;
using System.Text.Json;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class StructureItemSerializationTests
{
    [Fact]
    public void ResolvesStyleObjectsAndOmitsUnusableWrapperNames()
    {
        var unresolved = StructureItem(new UnresolvedStyle());
        var properties = unresolved.GetProperty("properties");
        Assert.False(properties.TryGetProperty("style", out _));
        Assert.DoesNotContain(
            "System.__ComObject",
            unresolved.GetRawText(),
            StringComparison.Ordinal
        );

        var resolved = StructureItem(new NamedStyle("Normal style"));
        Assert.Equal(
            "Normal style",
            resolved.GetProperty("properties").GetProperty("style").GetString()
        );

        var wrapperLiteral = StructureItem("System.__ComObject");
        Assert.False(
            wrapperLiteral.GetProperty("properties").TryGetProperty("style", out _)
        );
    }

    private static JsonElement StructureItem(object style)
    {
        var payload = typeof(WordLiveService)
            .GetMethod(
                "StructureItemPayload",
                BindingFlags.NonPublic | BindingFlags.Static
            )!
            .Invoke(
                null,
                [
                    "list_paragraphs",
                    new FakeListParagraph(style),
                    1,
                    false,
                    500,
                ]
            );
        return JsonSerializer.SerializeToElement(payload);
    }

    public sealed class FakeListParagraph
    {
        public FakeListParagraph(object style) => Style = style;

        public object Style { get; }
    }

    public sealed class NamedStyle
    {
        public NamedStyle(string nameLocal) => NameLocal = nameLocal;

        public string NameLocal { get; }

        public override string ToString() => "System.__ComObject";
    }

    public sealed class UnresolvedStyle
    {
        public override string ToString() => "System.__ComObject";
    }
}
