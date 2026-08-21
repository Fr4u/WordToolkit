using WordToolkit.Native.Protocol;
using WordToolkit.Native.Rendering;

namespace WordToolkit.Native.Tests;

public sealed class WordFixedFormatExporterTests
{
    [Fact]
    public void ExportsExactPageRangeReadOnlyWithMacrosAndLinksDisabled()
    {
        var directory = TemporaryDirectory();
        try
        {
            var source = Path.Combine(directory, "source.docx");
            var output = Path.Combine(directory, "staging.pdf");
            File.WriteAllBytes(source, [1, 2, 3]);
            var application = new FixedFormatFakeApplication(pageCount: 7);

            var result = WordFixedFormatExporter.ExportSavedPackage(
                application,
                source,
                output,
                new WordFixedFormatExportOptions(
                    FirstPage: 2,
                    LastPage: 5,
                    IncludeMarkup: true,
                    OptimizeForPrint: false,
                    IncludeDocumentProperties: false,
                    BookmarkMode: "bookmarks",
                    PdfA: true
                )
            );

            Assert.True(File.Exists(output));
            Assert.Equal(7, result.DocumentPageCount);
            Assert.Equal(2, result.ExportedFirstPage);
            Assert.Equal(5, result.ExportedLastPage);
            Assert.Equal(4, result.ExportedPageCount);
            Assert.True(result.ReadOnly);
            Assert.True(result.ClosedWithoutSave);
            Assert.True(result.MacrosForcedDisabled);
            Assert.True(result.LinkUpdatesDisabled);
            Assert.True(result.IncludedMarkup);
            Assert.False(result.KeptIrm);
            Assert.False(result.IncludedDocumentProperties);
            Assert.True(result.IncludedStructureTags);
            Assert.True(result.BitmapMissingFonts);
            Assert.Equal("bookmarks", result.BookmarkMode);
            Assert.True(result.PdfA);
            Assert.Equal(3, application.Documents.AutomationSecurityDuringOpen);
            Assert.False(application.Documents.UpdateLinksAtOpenDuringOpen);
            Assert.Equal(1, application.AutomationSecurity);
            Assert.True(application.Options.UpdateLinksAtOpen);

            var document = Assert.IsType<FixedFormatFakeDocument>(
                application.Documents.LastOpenedDocument
            );
            Assert.True(document.ReadOnly);
            Assert.False(document.AddedToRecentFiles);
            Assert.False(document.OpenedVisible);
            Assert.True(document.CloseCalled);
            Assert.Equal(0, document.CloseSaveOption);
            Assert.Equal(3, document.ExportRange);
            Assert.Equal(2, document.ExportFrom);
            Assert.Equal(5, document.ExportTo);
            Assert.Equal(7, document.ExportItem);
            Assert.Equal(1, document.ExportOptimizeFor);
            Assert.Equal(2, document.ExportBookmarks);
            Assert.False(document.ExportKeepIrm);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WholeDocumentUsesAllDocumentRangeAndExactBounds()
    {
        var directory = TemporaryDirectory();
        try
        {
            var source = Path.Combine(directory, "source.docx");
            var output = Path.Combine(directory, "whole.pdf");
            File.WriteAllBytes(source, [1]);
            var application = new FixedFormatFakeApplication(pageCount: 3);

            var result = WordFixedFormatExporter.ExportSavedPackage(
                application,
                source,
                output,
                new WordFixedFormatExportOptions()
            );

            var document = application.Documents.LastOpenedDocument!;
            Assert.Equal(0, document.ExportRange);
            Assert.Equal(1, document.ExportFrom);
            Assert.Equal(3, document.ExportTo);
            Assert.Equal(3, result.ExportedPageCount);
            Assert.Equal(0, document.ExportItem);
            Assert.Equal(0, document.ExportOptimizeFor);
            Assert.Equal(1, document.ExportBookmarks);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RejectsOutOfBoundsRangeBeforeExportAndStillClosesDocument()
    {
        var directory = TemporaryDirectory();
        try
        {
            var source = Path.Combine(directory, "source.docx");
            var output = Path.Combine(directory, "out.pdf");
            File.WriteAllBytes(source, [1]);
            var application = new FixedFormatFakeApplication(pageCount: 2);

            var exception = Assert.Throws<NativeToolException>(() =>
                WordFixedFormatExporter.ExportSavedPackage(
                    application,
                    source,
                    output,
                    new WordFixedFormatExportOptions(FirstPage: 3)
                )
            );

            Assert.Equal("INVALID_INPUT", exception.ErrorCode);
            Assert.False(File.Exists(output));
            Assert.True(application.Documents.LastOpenedDocument!.CloseCalled);
            Assert.Equal(1, application.AutomationSecurity);
            Assert.True(application.Options.UpdateLinksAtOpen);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RejectsSourceAlreadyOpenWithoutTouchingIt()
    {
        var directory = TemporaryDirectory();
        try
        {
            var source = Path.Combine(directory, "source.docx");
            File.WriteAllBytes(source, [1]);
            var application = new FixedFormatFakeApplication(pageCount: 1);
            application.Documents.AddAlreadyOpen(source);

            var exception = Assert.Throws<NativeToolException>(() =>
                WordFixedFormatExporter.ExportSavedPackage(
                    application,
                    source,
                    Path.Combine(directory, "out.pdf"),
                    new WordFixedFormatExportOptions()
                )
            );

            Assert.Equal("VERSION_CONFLICT", exception.ErrorCode);
            Assert.Null(application.Documents.LastOpenedDocument);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-fixed-export-tests-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(path);
        return path;
    }
}

public sealed class FixedFormatFakeApplication
{
    public FixedFormatFakeApplication(int pageCount)
    {
        Documents = new FixedFormatFakeDocuments(this, pageCount);
    }

    public string Version => "16.0";
    public string Build => "20131";
    public int AutomationSecurity { get; set; } = 1;
    public FixedFormatFakeOptions Options { get; } = new();
    public FixedFormatFakeDocuments Documents { get; }
}

public sealed class FixedFormatFakeOptions
{
    public bool UpdateLinksAtOpen { get; set; } = true;
}

public sealed class FixedFormatFakeDocuments
{
    private readonly FixedFormatFakeApplication _application;
    private readonly int _pageCount;
    private readonly List<FixedFormatFakeDocument> _openDocuments = [];

    public FixedFormatFakeDocuments(
        FixedFormatFakeApplication application,
        int pageCount
    )
    {
        _application = application;
        _pageCount = pageCount;
    }

    public int AutomationSecurityDuringOpen { get; private set; }
    public bool UpdateLinksAtOpenDuringOpen { get; private set; } = true;
    public FixedFormatFakeDocument? LastOpenedDocument { get; private set; }
    public int Count => _openDocuments.Count(document => !document.CloseCalled);

    public FixedFormatFakeDocument Item(int index) => _openDocuments
        .Where(document => !document.CloseCalled)
        .ElementAt(index - 1);

    public void AddAlreadyOpen(string path) => _openDocuments.Add(
        new FixedFormatFakeDocument(path, _pageCount, readOnly: false, false, true)
    );

    public FixedFormatFakeDocument Open(
        string FileName,
        bool ConfirmConversions,
        bool ReadOnly,
        bool AddToRecentFiles,
        bool Revert,
        bool Visible,
        bool OpenAndRepair,
        bool NoEncodingDialog
    )
    {
        AutomationSecurityDuringOpen = _application.AutomationSecurity;
        UpdateLinksAtOpenDuringOpen = _application.Options.UpdateLinksAtOpen;
        LastOpenedDocument = new FixedFormatFakeDocument(
            FileName,
            _pageCount,
            ReadOnly,
            AddToRecentFiles,
            Visible
        );
        _openDocuments.Add(LastOpenedDocument);
        return LastOpenedDocument;
    }
}

public sealed class FixedFormatFakeDocument
{
    private readonly int _pageCount;

    public FixedFormatFakeDocument(
        string fullName,
        int pageCount,
        bool readOnly,
        bool addedToRecentFiles,
        bool openedVisible
    )
    {
        FullName = fullName;
        _pageCount = pageCount;
        ReadOnly = readOnly;
        AddedToRecentFiles = addedToRecentFiles;
        OpenedVisible = openedVisible;
    }

    public string FullName { get; }
    public bool ReadOnly { get; }
    public bool AddedToRecentFiles { get; }
    public bool OpenedVisible { get; }
    public bool Saved { get; set; } = true;
    public int CompatibilityMode => 15;
    public bool CloseCalled { get; private set; }
    public int CloseSaveOption { get; private set; } = int.MinValue;
    public int ExportRange { get; private set; } = int.MinValue;
    public int ExportFrom { get; private set; }
    public int ExportTo { get; private set; }
    public int ExportItem { get; private set; } = int.MinValue;
    public int ExportOptimizeFor { get; private set; } = int.MinValue;
    public int ExportBookmarks { get; private set; } = int.MinValue;
    public bool ExportKeepIrm { get; private set; }

    public void Repaginate() { }

    public int ComputeStatistics(int statistic) => statistic == 2
        ? _pageCount
        : throw new ArgumentOutOfRangeException(nameof(statistic));

    public void ExportAsFixedFormat(
        string outputFileName,
        int exportFormat,
        bool openAfterExport,
        int optimizeFor,
        int range,
        int from,
        int to,
        int item,
        bool includeDocumentProperties,
        bool keepIrm,
        int createBookmarks,
        bool documentStructureTags,
        bool bitmapMissingFonts,
        bool useIso19005_1
    )
    {
        ExportRange = range;
        ExportFrom = from;
        ExportTo = to;
        ExportItem = item;
        ExportOptimizeFor = optimizeFor;
        ExportBookmarks = createBookmarks;
        ExportKeepIrm = keepIrm;
        File.WriteAllBytes(outputFileName, "%PDF-fake"u8.ToArray());
    }

    public void Close(int saveChanges)
    {
        CloseCalled = true;
        CloseSaveOption = saveChanges;
    }
}
