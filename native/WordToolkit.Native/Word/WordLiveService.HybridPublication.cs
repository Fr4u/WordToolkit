using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private async Task<object> PublishOoxmlPackageToLiveWordAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        RequireObject(arguments, "hybrid OOXML publication arguments");
        foreach (var property in arguments.EnumerateObject())
        {
            if (
                property.Name
                is not (
                    "local_path"
                    or "expected_package_fingerprint"
                    or "publication_mode"
                    or "read_only"
                    or "visible"
                    or "activate"
                    or "allow_macro_enabled"
                    or "launch_if_needed"
                )
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Unknown hybrid OOXML publication argument",
                    new { argument = property.Name }
                );
            }
        }
        var publicationMode = arguments.String(
            "publication_mode",
            "open_as_new_document"
        );
        if (publicationMode != "open_as_new_document")
        {
            throw new NativeToolException(
                "AUTH_FORBIDDEN",
                "Only open_as_new_document is supported because Word cannot prove an atomic full-package replacement of an existing live document"
            );
        }
        var path = ResolveInspectablePackagePath(arguments);
        var extension = Path.GetExtension(path);
        if (
            (string.Equals(extension, ".docm", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".dotm", StringComparison.OrdinalIgnoreCase))
            && !arguments.Boolean("allow_macro_enabled", false)
        )
        {
            throw new NativeToolException(
                "AUTH_FORBIDDEN",
                "Macro-enabled packages require allow_macro_enabled=true; macros remain disabled during Word open"
            );
        }
        var expectedFingerprint = arguments.String("expected_package_fingerprint");
        if (
            expectedFingerprint.Length != 64
            || expectedFingerprint.Any(character => !char.IsAsciiHexDigit(character))
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "expected_package_fingerprint must be a 64-character SHA-256 fingerprint"
            );
        }
        var started = Stopwatch.GetTimestamp();
        InspectWordPackageResult inspection;
        try
        {
            inspection = new InspectWordPackageOperation().Execute(
                new InspectWordPackageRequest(path),
                cancellationToken
            );
        }
        catch (WordToolkitOperationException exception)
        {
            throw new NativeToolException(
                exception.Code,
                exception.Message,
                exception.Reason is null ? null : new { reason = exception.Reason },
                exception.Retryable
            );
        }
        if (
            !string.Equals(
                inspection.PackageFingerprint,
                expectedFingerprint,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "The offline Word package changed after it was inspected",
                new
                {
                    expected_package_fingerprint = expectedFingerprint,
                    actual_package_fingerprint = inspection.PackageFingerprint,
                },
                retryable: true
            );
        }
        if (!inspection.ValidWordPackage)
        {
            throw new NativeToolException(
                "OOXML_INVALID",
                "The offline package did not pass bounded OPC and Word-package inspection",
                new
                {
                    structurally_valid = inspection.StructurallyValid,
                    word_document_detected = inspection.WordDocumentDetected,
                    diagnostic_errors = inspection.Diagnostics.Errors,
                }
            );
        }

        var sdkIssues = new List<object>();
        try
        {
            using var package = WordprocessingDocument.Open(path, false);
            var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
            foreach (var issue in validator.Validate(package).Take(200))
            {
                sdkIssues.Add(
                    new
                    {
                        id = issue.Id,
                        description = issue.Description,
                        error_type = issue.ErrorType.ToString(),
                        part = issue.Part?.Uri.ToString(),
                        path = issue.Path?.XPath,
                    }
                );
            }
        }
        catch (OpenXmlPackageException exception)
        {
            throw new NativeToolException(
                "OOXML_INVALID",
                "Microsoft Open XML SDK could not open the offline publication package",
                new { exception = exception.GetType().Name }
            );
        }
        if (sdkIssues.Count > 0)
        {
            throw new NativeToolException(
                "OOXML_INVALID",
                "The offline package has Microsoft Open XML SDK validation errors and was not opened in Word",
                new
                {
                    error_count = sdkIssues.Count,
                    issues = sdkIssues,
                    issues_truncated = sdkIssues.Count == 200,
                }
            );
        }

        var sourceSha256Before = FileSha256SharedRead(path);
        var readOnly = arguments.Boolean("read_only", false);
        var visible = arguments.Boolean("visible", true);
        var activate = arguments.Boolean("activate", true);
        var launchIfNeeded = arguments.Boolean("launch_if_needed", true);
        return await _host.InvokeAsync<object>(
            application =>
            {
                var initialDocumentCount = (int)application.Documents.Count;
                for (var index = 1; index <= initialDocumentCount; index++)
                {
                    dynamic existing = application.Documents.Item(index);
                    if (
                        string.Equals(
                            NormalizePath(DocumentFullName(existing)),
                            NormalizePath(path),
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        throw new NativeToolException(
                            "VERSION_CONFLICT",
                            "The verified offline package is already open in Word; hybrid publication requires a new document identity"
                        );
                    }
                }

                dynamic? document = null;
                var originalAutomationSecurity = (int)application.AutomationSecurity;
                var originalUpdateLinksAtOpen = (bool)application.Options.UpdateLinksAtOpen;
                try
                {
                    application.AutomationSecurity = OfficeAutomationSecurityForceDisable;
                    application.Options.UpdateLinksAtOpen = false;
                    document = application.Documents.Open(
                        FileName: path,
                        ConfirmConversions: false,
                        ReadOnly: readOnly,
                        AddToRecentFiles: false,
                        Revert: false,
                        Visible: visible,
                        OpenAndRepair: false,
                        NoEncodingDialog: true
                    );
                }
                finally
                {
                    application.Options.UpdateLinksAtOpen = originalUpdateLinksAtOpen;
                    application.AutomationSecurity = originalAutomationSecurity;
                }
                try
                {
                    if ((int)application.Documents.Count != initialDocumentCount + 1)
                    {
                        throw new NativeToolException(
                            "PUBLICATION_INVALID",
                            "Microsoft Word did not create exactly one new document for the verified package"
                        );
                    }
                    if (visible)
                    {
                        application.Visible = true;
                    }
                    if (activate)
                    {
                        document.Activate();
                    }
                    var sourceSha256After = FileSha256SharedRead(path);
                    if (!FixedHashEquals(sourceSha256Before, sourceSha256After))
                    {
                        throw new NativeToolException(
                            "PUBLICATION_INVALID",
                            "The source package changed while Microsoft Word opened it"
                        );
                    }
                    var name = DocumentName(document);
                    var fullName = DocumentFullName(document);
                    var record = new LiveDocumentRecord
                    {
                        Id = $"live_{Guid.NewGuid():N}",
                        Name = name,
                        FullName = fullName,
                        WindowHwnd = ActiveWindowHwnd(application),
                        Version = 0,
                    };
                    var documentInfo = DocumentInfo(application, document);
                    _records[record.Id] = record;
                    return new
                    {
                        live_document_id = record.Id,
                        live_version = record.Version,
                        publication_mode = publicationMode,
                        opened_as_new_document = true,
                        connected_document_replaced = false,
                        package_fingerprint = inspection.PackageFingerprint,
                        package_bytes = inspection.Bytes,
                        offline_validation = new
                        {
                            opc_valid = inspection.ValidWordPackage,
                            microsoft_open_xml_sdk_valid = true,
                            microsoft_open_xml_sdk_errors = 0,
                        },
                        security = new
                        {
                            macros_disabled_during_open = true,
                            external_links_updated = false,
                            added_to_recent_files = false,
                            source_file_unchanged = true,
                        },
                        document = documentInfo,
                        performance = Performance(started),
                    };
                }
                catch (Exception publicationFailure)
                {
                    Exception? closeFailure = null;
                    if (document is not null)
                    {
                        try
                        {
                            document.Close(WordDoNotSaveChanges);
                        }
                        catch (Exception exception)
                        {
                            closeFailure = exception;
                        }
                    }
                    if (closeFailure is not null)
                    {
                        var sourceHashVerified = TryFileSha256Equals(
                            path,
                            sourceSha256Before,
                            out var sourceFileUnchanged
                        );
                        throw new NativeToolException(
                            "TEMPORARY_DOCUMENT_CLEANUP_FAILED",
                            "WordToolkit could not close the unregistered document after hybrid publication failed",
                            new
                            {
                                original_error_code = publicationFailure
                                    is NativeToolException nativeFailure
                                    ? nativeFailure.ErrorCode
                                    : "EXTERNAL_TOOL_FAILED",
                                close_failed = true,
                                live_handle_registered = false,
                                source_hash_verified = sourceHashVerified,
                                source_file_unchanged = sourceHashVerified
                                    ? sourceFileUnchanged
                                    : (bool?)null,
                            }
                        );
                    }
                    throw;
                }
            },
            cancellationToken,
            launchIfMissing: launchIfNeeded
        );
    }

    private static string FileSha256SharedRead(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool TryFileSha256Equals(
        string path,
        string expected,
        out bool unchanged
    )
    {
        try
        {
            unchanged = FixedHashEquals(expected, FileSha256SharedRead(path));
            return true;
        }
        catch
        {
            unchanged = false;
            return false;
        }
    }
}
