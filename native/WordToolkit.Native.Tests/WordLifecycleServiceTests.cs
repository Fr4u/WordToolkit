using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class WordLifecycleServiceTests
{
    [Fact]
    public async Task StartRequestsNativeComLaunchAndQuitRequiresExplicitPolicy()
    {
        await using var host = new LifecycleFakeHost();
        var service = new WordLiveService(host);
        using var startArguments = JsonDocument.Parse("""{"visible":true}""");

        _ = await service.CallAsync(
            "start_word_application",
            startArguments.RootElement,
            CancellationToken.None
        );

        Assert.True(host.LaunchIfMissing);
        Assert.True(host.Application.Visible);

        using var quitArguments = JsonDocument.Parse(
            """{"save_changes":"discard_all","confirm":true}"""
        );
        _ = await service.CallAsync(
            "quit_word_application",
            quitArguments.RootElement,
            CancellationToken.None
        );

        Assert.True(host.Application.QuitCalled);
        Assert.Equal(0, host.Application.QuitSaveOption);
    }

    [Fact]
    public async Task QuitFailsClosedWithoutConfirmation()
    {
        await using var host = new LifecycleFakeHost();
        var service = new WordLiveService(host);
        using var arguments = JsonDocument.Parse(
            """{"save_changes":"discard_all","confirm":false}"""
        );

        var error = await Assert.ThrowsAsync<NativeToolException>(
            () =>
                service.CallAsync(
                    "quit_word_application",
                    arguments.RootElement,
                    CancellationToken.None
                )
        );

        Assert.Equal("AUTH_FORBIDDEN", error.ErrorCode);
        Assert.False(host.Application.QuitCalled);
    }

    [Fact]
    public async Task OpenForceDisablesMacrosAndExternalLinksThenClosesExplicitly()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-open-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "macro-capable.docm");
        await File.WriteAllTextAsync(path, "fake Word input for COM contract test");
        try
        {
            await using var host = new LifecycleFakeHost();
            var service = new WordLiveService(host);
            using var openArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    new
                    {
                        file_path = path,
                        activate = true,
                        launch_if_needed = true,
                    }
                )
            );

            var opened = await service.CallAsync(
                "open_live_word_document",
                openArguments.RootElement,
                CancellationToken.None
            );
            using var openedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(opened, JsonDefaults.Compact)
            );
            var data = openedJson.RootElement;
            var documentId = data.GetProperty("live_document_id").GetString()!;
            var version = data.GetProperty("live_version").GetInt64();

            Assert.Equal(3, host.Application.Documents.AutomationSecurityDuringOpen);
            Assert.False(host.Application.Documents.UpdateLinksAtOpenDuringOpen);
            Assert.Equal(1, host.Application.AutomationSecurity);
            Assert.True(host.Application.Options.UpdateLinksAtOpen);
            Assert.True(host.LaunchIfMissing);

            using var closeArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    new
                    {
                        live_document_id = documentId,
                        save_changes = "discard",
                        expected_version = version,
                    }
                )
            );
            _ = await service.CallAsync(
                "close_live_word_document",
                closeArguments.RootElement,
                CancellationToken.None
            );

            Assert.True(host.Application.Documents.OpenedDocument!.CloseCalled);
            Assert.Equal(
                0,
                host.Application.Documents.OpenedDocument.CloseSaveOption
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HybridPublicationRequiresExactValidatedFingerprintAndOpensNewIdentity()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-hybrid-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "verified.docx");
        using (var package = WordprocessingDocument.Create(
            path,
            WordprocessingDocumentType.Document
        ))
        {
            var main = package.AddMainDocumentPart();
            main.Document = new Document(
                new Body(new Paragraph(new Run(new Text("verified"))))
            );
            main.Document.Save();
        }
        try
        {
            var fingerprint = new InspectWordPackageOperation()
                .Execute(new InspectWordPackageRequest(path))
                .PackageFingerprint;
            await using var host = new LifecycleFakeHost();
            var service = new WordLiveService(host);
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    new
                    {
                        local_path = path,
                        expected_package_fingerprint = fingerprint,
                        publication_mode = "open_as_new_document",
                        visible = false,
                        activate = true,
                    }
                )
            );

            var result = await service.CallAsync(
                "publish_ooxml_package_to_live_word",
                arguments.RootElement,
                CancellationToken.None
            );
            using var resultJson = JsonDocument.Parse(
                JsonSerializer.Serialize(result, JsonDefaults.Compact)
            );
            var data = resultJson.RootElement;

            Assert.True(data.GetProperty("opened_as_new_document").GetBoolean());
            Assert.False(data.GetProperty("connected_document_replaced").GetBoolean());
            Assert.Equal(fingerprint, data.GetProperty("package_fingerprint").GetString());
            Assert.True(
                data.GetProperty("offline_validation")
                    .GetProperty("microsoft_open_xml_sdk_valid")
                    .GetBoolean()
            );
            Assert.Equal(3, host.Application.Documents.AutomationSecurityDuringOpen);
            Assert.False(host.Application.Documents.UpdateLinksAtOpenDuringOpen);
            Assert.True(host.LaunchIfMissing);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HybridPublicationRejectsStaleFingerprintBeforeWordIsTouched()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"wordtoolkit-hybrid-stale-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "verified.docx");
        using (var package = WordprocessingDocument.Create(
            path,
            WordprocessingDocumentType.Document
        ))
        {
            var main = package.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph()));
            main.Document.Save();
        }
        try
        {
            await using var host = new LifecycleFakeHost();
            var service = new WordLiveService(host);
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    new
                    {
                        local_path = path,
                        expected_package_fingerprint = new string('0', 64),
                    }
                )
            );

            var error = await Assert.ThrowsAsync<NativeToolException>(() =>
                service.CallAsync(
                    "publish_ooxml_package_to_live_word",
                    arguments.RootElement,
                    CancellationToken.None
                )
            );

            Assert.Equal("VERSION_CONFLICT", error.ErrorCode);
            Assert.False(host.LaunchIfMissing);
            Assert.Equal(0, host.Application.Documents.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

internal sealed class LifecycleFakeHost : IWordComHost
{
    public LifecycleFakeApplication Application { get; } = new();
    public bool LaunchIfMissing { get; private set; }

    public Task<T> InvokeAsync<T>(
        Func<dynamic, T> operation,
        CancellationToken cancellationToken = default,
        bool launchIfMissing = false
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        LaunchIfMissing = launchIfMissing;
        return Task.FromResult(operation(Application));
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class LifecycleFakeApplication
{
    public LifecycleFakeApplication()
    {
        Documents = new LifecycleFakeDocuments(this);
    }

    public bool Visible { get; set; }
    public int AutomationSecurity { get; set; } = 1;
    public LifecycleFakeOptions Options { get; } = new();
    public LifecycleFakeDocuments Documents { get; }
    public LifecycleFakeDocument? ActiveDocument { get; set; }
    public bool QuitCalled { get; private set; }
    public int QuitSaveOption { get; private set; } = int.MinValue;

    public void Quit(int saveChanges)
    {
        QuitCalled = true;
        QuitSaveOption = saveChanges;
    }
}

public sealed class LifecycleFakeDocuments
{
    private readonly LifecycleFakeApplication _application;

    public LifecycleFakeDocuments(LifecycleFakeApplication application)
    {
        _application = application;
    }

    public LifecycleFakeDocument? OpenedDocument { get; private set; }
    public int AutomationSecurityDuringOpen { get; private set; }
    public bool UpdateLinksAtOpenDuringOpen { get; private set; } = true;
    public int Count => OpenedDocument is null || OpenedDocument.CloseCalled ? 0 : 1;

    public LifecycleFakeDocument Item(int index)
    {
        if (index != 1 || OpenedDocument is null || OpenedDocument.CloseCalled)
        {
            throw new IndexOutOfRangeException();
        }
        return OpenedDocument;
    }

    public LifecycleFakeDocument Open(
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
        OpenedDocument = new LifecycleFakeDocument(
            _application,
            FileName,
            ReadOnly
        );
        _application.ActiveDocument = OpenedDocument;
        return OpenedDocument;
    }
}

public sealed class LifecycleFakeOptions
{
    public bool UpdateLinksAtOpen { get; set; } = true;
}

public sealed class LifecycleFakeDocument
{
    private readonly LifecycleFakeApplication _application;

    public LifecycleFakeDocument(
        LifecycleFakeApplication application,
        string fullName,
        bool readOnly
    )
    {
        _application = application;
        FullName = fullName;
        Name = System.IO.Path.GetFileName(fullName);
        Path = System.IO.Path.GetDirectoryName(fullName) ?? "";
        ReadOnly = readOnly;
    }

    public string Name { get; }
    public string FullName { get; }
    public string Path { get; }
    public bool ReadOnly { get; }
    public bool Saved { get; set; } = true;
    public int CompatibilityMode => 15;
    public int ProtectionType => -1;
    public bool CloseCalled { get; private set; }
    public int CloseSaveOption { get; private set; } = int.MinValue;

    public void Activate()
    {
        _application.ActiveDocument = this;
    }

    public void Close(int saveChanges)
    {
        CloseCalled = true;
        CloseSaveOption = saveChanges;
    }
}
