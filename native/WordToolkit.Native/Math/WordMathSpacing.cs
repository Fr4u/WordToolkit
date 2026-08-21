namespace WordToolkit.Native.Equations;

internal static class WordMathSpacing
{
    // Word ignores ordinary U+0020 outside quoted math text. These two Unicode
    // spaces survive OMath.BuildUp(), save/reopen and Word's PDF renderer.
    internal const char CaseColumn = '\u2003';
    internal const char TextBoundary = '\u2005';

    internal static bool IsSignificant(char value) =>
        value is CaseColumn or TextBoundary;
}
