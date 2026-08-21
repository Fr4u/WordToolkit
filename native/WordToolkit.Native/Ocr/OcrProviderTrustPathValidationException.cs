namespace WordToolkit.Native.Ocr;

internal sealed class OcrProviderTrustPathValidationException : IOException
{
    internal OcrProviderTrustPathValidationException(string _, Exception inner)
        : base("OCR provider trust paths cannot contain reparse points.", inner) { }
}
