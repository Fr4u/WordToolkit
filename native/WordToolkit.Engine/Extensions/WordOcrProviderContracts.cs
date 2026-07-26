namespace WordToolkit.Engine.Extensions;

public static class WordOcrProviderContract
{
    public const string InterfaceContract = "wordtoolkit.ocr-provider";
    public const string InterfaceVersion = "1.0";
}

public enum WordOcrLayoutHint
{
    Automatic,
    SingleBlock,
    SparseText,
    SingleLine,
    SingleWord,
}

public sealed record WordOcrProviderConfiguration(
    string? ExecutablePath,
    string? ModelDirectory
);

public sealed record WordOcrProviderRequest(
    ReadOnlyMemory<byte> ImageBytes,
    string ContentType,
    string ImageSha256,
    IReadOnlyList<string> Languages,
    WordOcrLayoutHint LayoutHint,
    int TimeoutMilliseconds,
    int MaximumOutputCharacters,
    WordOcrProviderConfiguration Configuration
);

public sealed record WordOcrPixelBox(
    int Left,
    int Top,
    int Width,
    int Height
);

public sealed record WordOcrProviderWord(
    string Text,
    double? Confidence,
    WordOcrPixelBox Bounds
);

public sealed record WordOcrProviderLine(
    string Text,
    double? Confidence,
    WordOcrPixelBox Bounds,
    IReadOnlyList<WordOcrProviderWord> Words
);

public sealed record WordOcrProviderProvenance(
    string ProviderName,
    string ProviderVersion,
    string ProviderBinarySha256,
    string ModelSetSha256,
    IReadOnlyList<string> EffectiveLanguages,
    string ConfidenceScale,
    bool NetworkUsed,
    bool DeterministicForBoundInputs
);

public sealed record WordOcrProviderResult(
    int ImageWidthPixels,
    int ImageHeightPixels,
    string Text,
    IReadOnlyList<WordOcrProviderLine> Lines,
    IReadOnlyList<string> Warnings,
    WordOcrProviderProvenance Provenance
);

public interface IWordOcrProvider
{
    WordOcrProviderResult Recognize(
        WordOcrProviderRequest request,
        CancellationToken cancellationToken = default
    );
}
