using System.Globalization;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Rendering;

internal sealed record WordFixedFormatExportOptions(
    int FirstPage = 1,
    int? LastPage = null,
    bool IncludeMarkup = false,
    bool OptimizeForPrint = true,
    bool IncludeDocumentProperties = true,
    string BookmarkMode = "headings",
    bool PdfA = false
);

internal sealed record WordFixedFormatExportObservation(
    string ApplicationVersion,
    string ApplicationBuild,
    int CompatibilityMode,
    int DocumentPageCount,
    int ExportedFirstPage,
    int ExportedLastPage,
    int ExportedPageCount,
    bool ReadOnly,
    bool SavedBefore,
    bool SavedAfter,
    bool ClosedWithoutSave,
    bool MacrosForcedDisabled,
    bool LinkUpdatesDisabled,
    bool AddedToRecentFiles,
    bool OpenedVisible,
    bool IncludedMarkup,
    bool KeptIrm,
    bool IncludedDocumentProperties,
    bool IncludedStructureTags,
    bool BitmapMissingFonts,
    string BookmarkMode,
    bool PdfA
);

internal static class WordFixedFormatExporter
{
    private const int OfficeAutomationSecurityForceDisable = 3;
    private const int WordDoNotSaveChanges = 0;
    private const int WordExportFormatPdf = 17;
    private const int WordExportAllDocument = 0;
    private const int WordExportFromTo = 3;
    private const int WordExportDocumentContent = 0;
    private const int WordExportDocumentWithMarkup = 7;
    private const int WordStatisticPages = 2;
    private const int MaxRenderablePages = 10_000;

    public static WordFixedFormatExportObservation ExportSavedPackage(
        dynamic application,
        string sourcePath,
        string stagingPdfPath,
        WordFixedFormatExportOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingPdfPath);
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        RejectAlreadyOpenDocument(application, sourcePath);

        dynamic? document = null;
        var originalAutomationSecurity = (int)application.AutomationSecurity;
        var originalUpdateLinksAtOpen = (bool)application.Options.UpdateLinksAtOpen;
        var savedBefore = false;
        var savedAfter = false;
        var compatibilityMode = 0;
        var pageCount = 0;
        var firstPage = 0;
        var lastPage = 0;
        try
        {
            application.AutomationSecurity = OfficeAutomationSecurityForceDisable;
            application.Options.UpdateLinksAtOpen = false;
            document = application.Documents.Open(
                FileName: sourcePath,
                ConfirmConversions: false,
                ReadOnly: true,
                AddToRecentFiles: false,
                Revert: false,
                Visible: false,
                OpenAndRepair: false,
                NoEncodingDialog: true
            );
            if (!(bool)document.ReadOnly)
            {
                throw new NativeToolException(
                    "AUTH_FORBIDDEN",
                    "Microsoft Word did not open the render source read-only"
                );
            }

            savedBefore = SafeBoolean(() => (bool)document.Saved, fallback: false);
            compatibilityMode = SafeInteger(
                () => (int)document.CompatibilityMode,
                fallback: 0
            );
            document.Repaginate();
            pageCount = (int)document.ComputeStatistics(WordStatisticPages);
            if (pageCount is < 1 or > MaxRenderablePages)
            {
                throw new NativeToolException(
                    "LIMIT_EXCEEDED",
                    "The Word page count is outside the fixed-render limit",
                    new { page_count = pageCount, limit = MaxRenderablePages }
                );
            }

            firstPage = options.FirstPage;
            lastPage = options.LastPage ?? pageCount;
            if (firstPage > pageCount || lastPage > pageCount)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "The requested page interval exceeds the Word page count",
                    new
                    {
                        first_page = firstPage,
                        last_page = lastPage,
                        page_count = pageCount,
                    }
                );
            }

            var exportRange = firstPage == 1 && lastPage == pageCount
                ? WordExportAllDocument
                : WordExportFromTo;
            document.ExportAsFixedFormat(
                stagingPdfPath,
                WordExportFormatPdf,
                false,
                options.OptimizeForPrint ? 0 : 1,
                exportRange,
                firstPage,
                lastPage,
                options.IncludeMarkup
                    ? WordExportDocumentWithMarkup
                    : WordExportDocumentContent,
                options.IncludeDocumentProperties,
                false,
                BookmarkValue(options.BookmarkMode),
                true,
                true,
                options.PdfA
            );
            savedAfter = SafeBoolean(() => (bool)document.Saved, fallback: false);
        }
        finally
        {
            try
            {
                if (document is not null)
                {
                    document.Close(WordDoNotSaveChanges);
                }
            }
            finally
            {
                application.Options.UpdateLinksAtOpen = originalUpdateLinksAtOpen;
                application.AutomationSecurity = originalAutomationSecurity;
            }
        }

        return new WordFixedFormatExportObservation(
            SafeString(() => application.Version),
            SafeString(() => application.Build),
            compatibilityMode,
            pageCount,
            firstPage,
            lastPage,
            checked(lastPage - firstPage + 1),
            ReadOnly: true,
            savedBefore,
            savedAfter,
            ClosedWithoutSave: true,
            MacrosForcedDisabled: true,
            LinkUpdatesDisabled: true,
            AddedToRecentFiles: false,
            OpenedVisible: false,
            options.IncludeMarkup,
            KeptIrm: false,
            options.IncludeDocumentProperties,
            IncludedStructureTags: true,
            BitmapMissingFonts: true,
            options.BookmarkMode,
            options.PdfA
        );
    }

    private static void ValidateOptions(WordFixedFormatExportOptions options)
    {
        if (options.FirstPage < 1)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "first_page must be at least 1"
            );
        }
        if (options.LastPage is { } lastPage && lastPage < options.FirstPage)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "last_page must be greater than or equal to first_page"
            );
        }
        if (options.LastPage is > MaxRenderablePages)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                $"last_page cannot exceed {MaxRenderablePages}"
            );
        }
        _ = BookmarkValue(options.BookmarkMode);
    }

    private static int BookmarkValue(string mode) => mode switch
    {
        "none" => 0,
        "headings" => 1,
        "bookmarks" => 2,
        _ => throw new NativeToolException(
            "INVALID_INPUT",
            "bookmarks must be none, headings, or bookmarks"
        ),
    };

    private static void RejectAlreadyOpenDocument(dynamic application, string sourcePath)
    {
        var expected = NormalizePath(sourcePath);
        var count = (int)application.Documents.Count;
        for (var index = 1; index <= count; index++)
        {
            dynamic candidate = application.Documents.Item(index);
            var fullName = SafeString(() => candidate.FullName);
            if (
                fullName.Length != 0
                && string.Equals(
                    NormalizePath(fullName),
                    expected,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                throw new NativeToolException(
                    "VERSION_CONFLICT",
                    "The saved-package render source is already open in Word; connect to that live document instead"
                );
            }
        }
    }

    private static string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path);
        return OperatingSystem.IsWindows()
            ? full.TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()
            : full.TrimEnd(Path.DirectorySeparatorChar);
    }

    private static string SafeString(Func<dynamic> value)
    {
        try
        {
            return Convert.ToString(value(), CultureInfo.InvariantCulture) ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static int SafeInteger(Func<int> value, int fallback)
    {
        try
        {
            return value();
        }
        catch
        {
            return fallback;
        }
    }

    private static bool SafeBoolean(Func<bool> value, bool fallback)
    {
        try
        {
            return value();
        }
        catch
        {
            return fallback;
        }
    }
}
