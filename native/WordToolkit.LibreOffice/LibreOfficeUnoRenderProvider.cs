using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Operations;

namespace WordToolkit.LibreOffice;

public sealed class LibreOfficeUnoRenderProvider : ILibreOfficeUnoRenderProvider
{
    private const int RequestMagic = 0x57545531;
    private const int ResponseMagic = 0x57545231;
    private const int ProtocolVersion = 1;
    private const int MaximumProtocolBytes = 4 * 1024;
    private static readonly TimeSpan OfficeExitGrace = TimeSpan.FromSeconds(10);
    private static readonly IReadOnlyList<string> Limitations = Array.AsReadOnly(
        new[]
        {
            "libreoffice_layout_not_microsoft_word_layout",
            "not_a_process_or_network_sandbox",
            "macro_policy_requested_but_not_behaviorally_probed",
            "external_update_policy_requested_but_not_behaviorally_probed",
            "no_vendor_signature_or_module_authenticity_proof",
            "no_atomic_executable_handle_binding",
            "dynamic_dependency_bytes_not_fully_bound",
            "filesystem_mount_network_status_not_proven_on_unix",
            "java_uno_local_context_released_by_one_shot_process_exit",
        }
    );

    public async Task<LibreOfficeUnoRenderObservation> RenderAsync(
        LibreOfficeUnoRenderProviderRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalized = Validate(request);
        cancellationToken.ThrowIfCancellationRequested();

        var libreOfficeBefore = ReadIdentity(
            normalized.LibreOfficeExecutablePath,
            normalized.MaximumExecutableBytes,
            "LibreOffice executable",
            cancellationToken
        );
        RequireExpectedHash(
            libreOfficeBefore,
            normalized.ExpectedLibreOfficeExecutableSha256,
            "LibreOffice executable"
        );
        var javaBefore = ReadIdentity(
            normalized.JavaExecutablePath,
            normalized.MaximumExecutableBytes,
            "Java executable",
            cancellationToken
        );
        RequireExpectedHash(
            javaBefore,
            normalized.ExpectedJavaExecutableSha256,
            "Java executable"
        );
        var libreOfficeJarBefore = ReadIdentity(
            normalized.LibreOfficeJarPath,
            normalized.MaximumJavaArchiveBytes,
            "LibreOffice Java archive",
            cancellationToken
        );
        RequireExpectedHash(
            libreOfficeJarBefore,
            normalized.ExpectedLibreOfficeJarSha256,
            "LibreOffice Java archive"
        );
        var helperBefore = ReadIdentity(
            normalized.HelperClasspathPath,
            normalized.MaximumJavaArchiveBytes,
            "WordToolkit UNO helper archive",
            cancellationToken
        );
        RequireExpectedHash(
            helperBefore,
            normalized.ExpectedHelperClasspathSha256,
            "WordToolkit UNO helper archive"
        );
        var sourceBefore = ReadIdentity(
            normalized.SourcePath,
            normalized.MaximumSourceBytes,
            "source package",
            cancellationToken
        );
        RequireExpectedHash(
            sourceBefore,
            normalized.ExpectedSourceSha256,
            "source package"
        );

        var outputDirectory = Path.GetDirectoryName(normalized.OutputPdfPath)!;
        var workspace = Path.Combine(
            outputDirectory,
            $".wordtoolkit-libreoffice-{Guid.NewGuid():N}.tmp"
        );
        var profile = Path.Combine(workspace, "profile");
        var processTemp = Path.Combine(workspace, "temp");
        var stagedInput = Path.Combine(
            workspace,
            "source" + Path.GetExtension(normalized.SourcePath).ToLowerInvariant()
        );
        var stagedPdf = Path.Combine(workspace, "render.pdf");
        var pipeName = "wtu_" + Guid.NewGuid().ToString("N");
        var outputPublished = false;
        var processTreeKillRequired = false;
        Process? officeProcess = null;
        Process? helperProcess = null;
        ProtocolResponse? response = null;
        var officeExited = false;

        try
        {
            Directory.CreateDirectory(profile);
            Directory.CreateDirectory(processTemp);
            File.Copy(normalized.SourcePath, stagedInput, overwrite: false);
            var stagedSource = ReadIdentity(
                stagedInput,
                normalized.MaximumSourceBytes,
                "staged source package",
                cancellationToken
            );
            if (stagedSource.Bytes != sourceBefore.Bytes
                || !string.Equals(
                    stagedSource.Sha256,
                    sourceBefore.Sha256,
                    StringComparison.Ordinal
                ))
            {
                throw Error(
                    "SOURCE_DRIFT",
                    "The isolated source copy does not match the inspected package"
                );
            }

            officeProcess = StartOffice(
                normalized.LibreOfficeExecutablePath,
                pipeName,
                profile,
                processTemp
            );
            var officeOutputTask = ReadBoundedTextAsync(
                officeProcess.StandardOutput,
                normalized.MaximumProcessOutputCharacters
            );
            var officeErrorTask = ReadBoundedTextAsync(
                officeProcess.StandardError,
                normalized.MaximumProcessOutputCharacters
            );

            helperProcess = StartHelper(
                normalized.JavaExecutablePath,
                normalized.HelperClasspathPath,
                normalized.LibreOfficeJarPath,
                Path.GetDirectoryName(normalized.LibreOfficeExecutablePath)!,
                workspace,
                processTemp
            );
            var helperOutputTask = ReadBoundedBytesAsync(
                helperProcess.StandardOutput.BaseStream,
                MaximumProtocolBytes
            );
            var helperErrorTask = ReadBoundedTextAsync(
                helperProcess.StandardError,
                normalized.MaximumProcessOutputCharacters
            );
            await WriteRequestAsync(
                    helperProcess.StandardInput.BaseStream,
                    pipeName,
                    new Uri(stagedInput).AbsoluteUri,
                    new Uri(stagedPdf).AbsoluteUri,
                    normalized.InputFilterName,
                    PageRange(normalized.FirstPage, normalized.LastPage),
                    normalized.PdfA1b,
                    normalized.ExportBookmarks,
                    Math.Min(normalized.TimeoutMilliseconds, 30_000),
                    cancellationToken
                )
                .ConfigureAwait(false);
            helperProcess.StandardInput.Close();

            using var timeout = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(normalized.TimeoutMilliseconds)
            );
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token
            );
            try
            {
                await helperProcess.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested
            )
            {
                processTreeKillRequired |= TryKill(helperProcess);
                processTreeKillRequired |= TryKill(officeProcess);
                throw Error(
                    "BACKEND_TIMEOUT",
                    "The isolated LibreOffice UNO render exceeded its configured timeout",
                    retryable: true
                );
            }
            catch
            {
                processTreeKillRequired |= TryKill(helperProcess);
                processTreeKillRequired |= TryKill(officeProcess);
                throw;
            }

            var helperOutput = await helperOutputTask.ConfigureAwait(false);
            var helperError = await helperErrorTask.ConfigureAwait(false);
            if (helperOutput.Truncated || helperError.Truncated)
            {
                throw Error(
                    "OUTPUT_LIMIT",
                    "The isolated UNO helper exceeded its bounded output contract"
                );
            }
            response = ParseResponse(helperOutput.Bytes);
            if (helperProcess.ExitCode != 0 || !response.Success)
            {
                throw Error(
                    MapHelperError(response.Code),
                    "The isolated UNO helper rejected or could not complete the render",
                    new
                    {
                        helper_status = response.Code,
                        helper_exit_code = helperProcess.ExitCode,
                        response.DocumentClosed,
                        response.DesktopTerminated,
                    },
                    retryable: response.Code is "CONNECT_TIMEOUT" or "CONNECT_FAILED"
                );
            }

            using (var exitTimeout = new CancellationTokenSource(OfficeExitGrace))
            using (var exitLinked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                exitTimeout.Token
            ))
            {
                try
                {
                    await officeProcess.WaitForExitAsync(exitLinked.Token)
                        .ConfigureAwait(false);
                    officeExited = true;
                }
                catch (OperationCanceledException) when (
                    exitTimeout.IsCancellationRequested
                    && !cancellationToken.IsCancellationRequested
                )
                {
                    processTreeKillRequired |= TryKill(officeProcess);
                }
            }
            var officeOutput = await officeOutputTask.ConfigureAwait(false);
            var officeError = await officeErrorTask.ConfigureAwait(false);
            if (officeOutput.Truncated || officeError.Truncated)
            {
                throw Error(
                    "OUTPUT_LIMIT",
                    "The isolated LibreOffice process exceeded its bounded output contract"
                );
            }
            if (!officeExited || officeProcess.ExitCode != 0)
            {
                throw Error(
                    "CLEANUP_FAILED",
                    "The private LibreOffice process did not terminate cleanly"
                );
            }

            var pdf = ReadPdfIdentity(
                stagedPdf,
                normalized.MaximumPdfBytes,
                cancellationToken
            );
            var sourceAfter = ReadIdentity(
                normalized.SourcePath,
                normalized.MaximumSourceBytes,
                "source package",
                cancellationToken
            );
            RequireStable(sourceBefore, sourceAfter, "source package", "SOURCE_DRIFT");
            var libreOfficeAfter = ReadIdentity(
                normalized.LibreOfficeExecutablePath,
                normalized.MaximumExecutableBytes,
                "LibreOffice executable",
                cancellationToken
            );
            var javaAfter = ReadIdentity(
                normalized.JavaExecutablePath,
                normalized.MaximumExecutableBytes,
                "Java executable",
                cancellationToken
            );
            var libreOfficeJarAfter = ReadIdentity(
                normalized.LibreOfficeJarPath,
                normalized.MaximumJavaArchiveBytes,
                "LibreOffice Java archive",
                cancellationToken
            );
            var helperAfter = ReadIdentity(
                normalized.HelperClasspathPath,
                normalized.MaximumJavaArchiveBytes,
                "WordToolkit UNO helper archive",
                cancellationToken
            );
            RequireStable(
                libreOfficeBefore,
                libreOfficeAfter,
                "LibreOffice executable",
                "EXECUTABLE_DRIFT"
            );
            RequireStable(
                javaBefore,
                javaAfter,
                "Java executable",
                "EXECUTABLE_DRIFT"
            );
            RequireStable(
                libreOfficeJarBefore,
                libreOfficeJarAfter,
                "LibreOffice Java archive",
                "EXECUTABLE_DRIFT"
            );
            RequireStable(
                helperBefore,
                helperAfter,
                "WordToolkit UNO helper archive",
                "EXECUTABLE_DRIFT"
            );

            File.Move(stagedPdf, normalized.OutputPdfPath);
            outputPublished = true;
            if (!TryDeleteDirectory(workspace))
            {
                if (!TryDeleteFile(normalized.OutputPdfPath))
                {
                    throw Error(
                        "ROLLBACK_FAILED",
                        "LibreOffice workspace cleanup failed and the staged PDF could not be removed"
                    );
                }
                outputPublished = false;
                throw Error(
                    "CLEANUP_FAILED",
                    "LibreOffice private profile or workspace cleanup could not be proved"
                );
            }

            return new LibreOfficeUnoRenderObservation(
                LibreOfficeUnoRenderContract.ProviderContract,
                Identity(
                    normalized.LibreOfficeExecutablePath,
                    libreOfficeBefore,
                    expected: true
                ),
                Identity(normalized.JavaExecutablePath, javaBefore, expected: true),
                Identity(
                    normalized.LibreOfficeJarPath,
                    libreOfficeJarBefore,
                    expected: true
                ),
                Identity(
                    normalized.HelperClasspathPath,
                    helperBefore,
                    expected: true
                ),
                sourceBefore.Sha256,
                SourceHashStable: true,
                new LibreOfficeUnoDocumentPolicyEvidence(
                    response.HiddenRequested,
                    ReadOnlyRequested: true,
                    response.ReadOnlyVerified,
                    response.PickListDisabledRequested,
                    response.RepairDisabledRequested,
                    response.MacroNeverExecuteRequested,
                    MacroPreventionBehaviorallyVerified: false,
                    response.UpdateNoUpdateRequested,
                    ExternalUpdatePreventionBehaviorallyVerified: false,
                    normalized.InputFilterName,
                    InputFilterExplicit: true
                ),
                new LibreOfficeUnoExportEvidence(
                    response.UnoConnectionVerified,
                    response.WriterComponentVerified,
                    response.WriterPdfExportVerified,
                    response.PdfFilterExplicit,
                    response.OverwriteDisabled,
                    response.SourceLocationPreserved,
                    normalized.FirstPage,
                    normalized.LastPage,
                    normalized.PdfA1b,
                    normalized.ExportBookmarks,
                    pdf.Bytes,
                    pdf.Sha256
                ),
                new LibreOfficeUnoCleanupEvidence(
                    response.DocumentClosed,
                    response.DesktopTerminated,
                    HelperExited: helperProcess.HasExited,
                    LibreOfficeExited: officeExited,
                    ProcessTreeKillRequired: processTreeKillRequired,
                    PrivateProfileDeleted: true,
                    PrivateWorkspaceDeleted: true
                ),
                HostOperatingSystem(),
                ArchitectureName(RuntimeInformation.OSArchitecture),
                ArchitectureName(RuntimeInformation.ProcessArchitecture),
                Limitations
            );
        }
        catch (OperationCanceledException)
        {
            processTreeKillRequired |= TryKill(helperProcess);
            processTreeKillRequired |= TryKill(officeProcess);
            RequireFailureCleanup(
                workspace,
                normalized.OutputPdfPath,
                outputPublished,
                "CANCELLED",
                processTreeKillRequired
            );
            throw;
        }
        catch (WordToolkitOperationException exception)
        {
            processTreeKillRequired |= TryKill(helperProcess);
            processTreeKillRequired |= TryKill(officeProcess);
            RequireFailureCleanup(
                workspace,
                normalized.OutputPdfPath,
                outputPublished,
                exception.Code,
                processTreeKillRequired
            );
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or Win32Exception
                or InvalidOperationException
        )
        {
            processTreeKillRequired |= TryKill(helperProcess);
            processTreeKillRequired |= TryKill(officeProcess);
            var mapped = Error(
                "BACKEND_UNAVAILABLE",
                "The isolated LibreOffice UNO render could not complete",
                innerException: exception,
                retryable: true
            );
            RequireFailureCleanup(
                workspace,
                normalized.OutputPdfPath,
                outputPublished,
                mapped.Code,
                processTreeKillRequired
            );
            throw mapped;
        }
        finally
        {
            helperProcess?.Dispose();
            officeProcess?.Dispose();
        }
    }

    private static LibreOfficeUnoRenderProviderRequest Validate(
        LibreOfficeUnoRenderProviderRequest request
    )
    {
        if (request.TimeoutMilliseconds
                is < LibreOfficeUnoRenderContract.MinimumTimeoutMilliseconds
                or > LibreOfficeUnoRenderContract.MaximumTimeoutMilliseconds
            || request.MaximumExecutableBytes < 1
            || request.MaximumExecutableBytes > LibreOfficeUnoRenderContract.MaximumExecutableBytes
            || request.MaximumJavaArchiveBytes < 1
            || request.MaximumJavaArchiveBytes > LibreOfficeUnoRenderContract.MaximumJavaArchiveBytes
            || request.MaximumSourceBytes < 1
            || request.MaximumSourceBytes > LibreOfficeUnoRenderContract.MaximumSourceBytes
            || request.MaximumPdfBytes < 1
            || request.MaximumPdfBytes > LibreOfficeUnoRenderContract.MaximumPdfBytes
            || request.MaximumProcessOutputCharacters < 1_024
            || request.MaximumProcessOutputCharacters
                > LibreOfficeUnoRenderContract.MaximumProcessOutputCharacters)
        {
            throw Error("INVALID_INPUT", "The UNO render limits are invalid");
        }
        if (request.FirstPage < 1
            || request.FirstPage > LibreOfficeUnoRenderContract.MaximumPages
            || request.LastPage is < 1 or > LibreOfficeUnoRenderContract.MaximumPages
            || (request.LastPage is not null && request.LastPage < request.FirstPage)
            || (request.FirstPage != 1 && request.LastPage is null))
        {
            throw Error(
                "INVALID_INPUT",
                "A non-default first page requires a bounded last page"
            );
        }
        if (request.InputFilterName is not (
            "Office Open XML Text" or "Office Open XML Text Template"
        ))
        {
            throw Error("INVALID_INPUT", "The Writer input filter is unsupported");
        }
        var requestedExtension = Path.GetExtension(request.SourcePath).ToLowerInvariant();
        var requestedFilter = requestedExtension switch
        {
            ".docx" or ".docm" => "Office Open XML Text",
            ".dotx" or ".dotm" => "Office Open XML Text Template",
            _ => string.Empty,
        };
        if (!string.Equals(
                requestedFilter,
                request.InputFilterName,
                StringComparison.Ordinal
            ))
        {
            throw Error(
                "INVALID_INPUT",
                "input_filter_name does not match the Word package extension"
            );
        }
        foreach (var hash in new[]
        {
            request.ExpectedLibreOfficeExecutableSha256,
            request.ExpectedJavaExecutableSha256,
            request.ExpectedLibreOfficeJarSha256,
            request.ExpectedHelperClasspathSha256,
            request.ExpectedSourceSha256,
        })
        {
            if (!IsSha256(hash))
            {
                throw Error(
                    "INVALID_INPUT",
                    "Every expected SHA-256 must be exactly 64 hexadecimal characters"
                );
            }
        }

        var libreOffice = ResolveExistingLocalFile(
            request.LibreOfficeExecutablePath,
            "LibreOffice executable"
        );
        var java = ResolveExistingLocalFile(request.JavaExecutablePath, "Java executable");
        var libreOfficeJar = ResolveExistingLocalFile(
            request.LibreOfficeJarPath,
            "LibreOffice Java archive"
        );
        var helper = ResolveExistingLocalFile(
            request.HelperClasspathPath,
            "WordToolkit UNO helper archive"
        );
        var source = ResolveExistingLocalFile(request.SourcePath, "source package");
        var output = ResolveNewLocalFile(request.OutputPdfPath, "output PDF");
        if (!Path.GetExtension(libreOfficeJar).Equals(".jar", StringComparison.OrdinalIgnoreCase)
            || !Path.GetExtension(helper).Equals(".jar", StringComparison.OrdinalIgnoreCase))
        {
            throw Error("INVALID_INPUT", "UNO Java classpath entries must be JAR files");
        }
        if (!Path.GetExtension(output).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw Error("INVALID_INPUT", "output_pdf_path must end in .pdf");
        }
        if (!InspectWordPackageContract.IsSupportedFileName(source))
        {
            throw Error("UNSUPPORTED_FORMAT", "UNO rendering accepts Word OOXML packages");
        }
        var programDirectory = Path.GetDirectoryName(libreOffice)!;
        var jarProgramDirectory = Directory.GetParent(
            Path.GetDirectoryName(libreOfficeJar)!
        )?.FullName;
        if (!string.Equals(
                Path.GetFullPath(programDirectory).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                ),
                Path.GetFullPath(jarProgramDirectory ?? string.Empty).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                ),
                PathComparison
            ))
        {
            throw Error(
                "INVALID_INPUT",
                "LibreOffice executable and libreoffice.jar must belong to one program directory"
            );
        }

        return request with
        {
            LibreOfficeExecutablePath = libreOffice,
            ExpectedLibreOfficeExecutableSha256 =
                request.ExpectedLibreOfficeExecutableSha256.ToLowerInvariant(),
            JavaExecutablePath = java,
            ExpectedJavaExecutableSha256 =
                request.ExpectedJavaExecutableSha256.ToLowerInvariant(),
            LibreOfficeJarPath = libreOfficeJar,
            ExpectedLibreOfficeJarSha256 =
                request.ExpectedLibreOfficeJarSha256.ToLowerInvariant(),
            HelperClasspathPath = helper,
            ExpectedHelperClasspathSha256 =
                request.ExpectedHelperClasspathSha256.ToLowerInvariant(),
            SourcePath = source,
            ExpectedSourceSha256 = request.ExpectedSourceSha256.ToLowerInvariant(),
            OutputPdfPath = output,
        };
    }

    private static Process StartOffice(
        string executable,
        string pipeName,
        string profile,
        string processTemp
    )
    {
        var info = NewProcessInfo(executable, Path.GetDirectoryName(executable)!);
        ConfigureEnvironment(info, Path.GetDirectoryName(executable)!, profile, processTemp);
        foreach (var argument in new[]
        {
            "--headless",
            "--invisible",
            "--nologo",
            "--nodefault",
            "--nolockcheck",
            "--norestore",
            "--nofirststartwizard",
            $"--accept=pipe,name={pipeName};urp;StarOffice.ComponentContext",
            $"-env:UserInstallation={new Uri(profile).AbsoluteUri}",
        })
        {
            info.ArgumentList.Add(argument);
        }
        return Start(info, "LibreOffice");
    }

    private static Process StartHelper(
        string java,
        string helperJar,
        string libreOfficeJar,
        string libreOfficeProgramDirectory,
        string workspace,
        string processTemp
    )
    {
        var info = NewProcessInfo(java, workspace);
        info.RedirectStandardInput = true;
        ConfigureEnvironment(
            info,
            libreOfficeProgramDirectory,
            workspace,
            processTemp
        );
        info.ArgumentList.Add("-Dfile.encoding=UTF-8");
        info.ArgumentList.Add($"-Djava.library.path={libreOfficeProgramDirectory}");
        info.ArgumentList.Add("-cp");
        info.ArgumentList.Add(
            string.Join(Path.PathSeparator, helperJar, libreOfficeJar)
        );
        info.ArgumentList.Add("wordtoolkit.uno.LibreOfficeUnoRender");
        return Start(info, "UNO helper");
    }

    private static ProcessStartInfo NewProcessInfo(string executable, string workingDirectory) =>
        new()
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

    private static Process Start(ProcessStartInfo info, string name)
    {
        try
        {
            var process = new Process { StartInfo = info };
            if (!process.Start())
            {
                process.Dispose();
                throw Error("BACKEND_UNAVAILABLE", $"The exact {name} process did not start");
            }
            return process;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or IOException
        )
        {
            throw Error(
                "BACKEND_UNAVAILABLE",
                $"The exact {name} process could not be started",
                innerException: exception,
                retryable: true
            );
        }
    }

    private static void ConfigureEnvironment(
        ProcessStartInfo info,
        string libreOfficeProgramDirectory,
        string home,
        string processTemp
    )
    {
        info.Environment.Clear();
        info.Environment["HOME"] = home;
        info.Environment["TMPDIR"] = processTemp;
        info.Environment["TMP"] = processTemp;
        info.Environment["TEMP"] = processTemp;
        info.Environment["SAL_USE_VCLPLUGIN"] = "svp";
        info.Environment["UNO_PATH"] = libreOfficeProgramDirectory;
        if (OperatingSystem.IsWindows())
        {
            var systemRoot = Environment.GetEnvironmentVariable("SystemRoot")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            info.Environment["SystemRoot"] = systemRoot;
            info.Environment["WINDIR"] = systemRoot;
            info.Environment["USERPROFILE"] = home;
            info.Environment["APPDATA"] = Path.Combine(home, "AppData", "Roaming");
            info.Environment["LOCALAPPDATA"] = Path.Combine(home, "AppData", "Local");
            info.Environment["PATH"] = string.Join(
                Path.PathSeparator,
                libreOfficeProgramDirectory,
                Path.Combine(systemRoot, "System32"),
                systemRoot
            );
        }
        else
        {
            info.Environment["PATH"] = "/usr/bin:/bin";
            info.Environment["LD_LIBRARY_PATH"] = libreOfficeProgramDirectory;
            info.Environment["LANG"] = "C.UTF-8";
            info.Environment["LC_ALL"] = "C.UTF-8";
        }
    }

    private static async Task WriteRequestAsync(
        Stream stream,
        string pipeName,
        string sourceUrl,
        string outputUrl,
        string inputFilterName,
        string pageRange,
        bool pdfA1b,
        bool exportBookmarks,
        int connectTimeoutMilliseconds,
        CancellationToken cancellationToken
    )
    {
        using var payload = new MemoryStream();
        WriteInt32(payload, RequestMagic);
        WriteInt32(payload, ProtocolVersion);
        WriteString(payload, pipeName);
        WriteString(payload, sourceUrl);
        WriteString(payload, outputUrl);
        WriteString(payload, inputFilterName);
        WriteString(payload, pageRange);
        payload.WriteByte(pdfA1b ? (byte)1 : (byte)0);
        payload.WriteByte(exportBookmarks ? (byte)1 : (byte)0);
        WriteInt32(payload, connectTimeoutMilliseconds);
        payload.Position = 0;
        await payload.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ProtocolResponse ParseResponse(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            if (ReadInt32(stream) != ResponseMagic
                || ReadInt32(stream) != ProtocolVersion)
            {
                throw Error("INVALID_BACKEND", "The UNO helper response header is invalid");
            }
            var success = ReadBoolean(stream);
            var code = ReadString(stream, 128);
            if (success)
            {
                var response = new ProtocolResponse(
                    true,
                    code,
                    ReadBoolean(stream),
                    ReadBoolean(stream),
                    ReadBoolean(stream),
                    ReadBoolean(stream),
                    ReadBoolean(stream),
                    ReadBoolean(stream),
                    ReadBoolean(stream),
                    ReadBoolean(stream),
                    ReadBoolean(stream),
                    ReadBoolean(stream),
                    ReadBoolean(stream),
                    ReadBoolean(stream),
                    ReadBoolean(stream),
                    ReadBoolean(stream),
                    ReadBoolean(stream)
                );
                if (stream.Position != stream.Length
                    || code != "OK"
                    || !response.AllSuccessEvidence)
                {
                    throw Error(
                        "INVALID_BACKEND",
                        "The UNO helper success response is incomplete"
                    );
                }
                return response;
            }

            var failure = ProtocolResponse.Failure(
                code,
                ReadBoolean(stream),
                ReadBoolean(stream),
                ReadBoolean(stream)
            );
            if (stream.Position != stream.Length || !IsHelperCode(code))
            {
                throw Error("INVALID_BACKEND", "The UNO helper failure response is invalid");
            }
            return failure;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or IOException or DecoderFallbackException
        )
        {
            throw Error(
                "INVALID_BACKEND",
                "The UNO helper returned a malformed bounded response",
                innerException: exception
            );
        }
    }

    private static string MapHelperError(string code) => code switch
    {
        "PROTOCOL_ERROR" => "INVALID_BACKEND",
        "CONNECT_TIMEOUT" => "BACKEND_TIMEOUT",
        "CONNECT_CANCELLED" => "CANCELLED",
        "CONNECT_FAILED" or "LOCAL_CONTEXT_FAILED" => "BACKEND_UNAVAILABLE",
        "NOT_WRITER_DOCUMENT" => "UNSUPPORTED_FORMAT",
        "READ_ONLY_NOT_VERIFIED" => "DOCUMENT_POLICY_UNVERIFIED",
        "SOURCE_LOCATION_NOT_PRESERVED" => "DOCUMENT_POLICY_UNVERIFIED",
        "CLOSE_FAILED" or "TERMINATE_FAILED" => "CLEANUP_FAILED",
        "LOAD_FAILED" => "DOCUMENT_LOAD_FAILED",
        "EXPORT_FAILED" => "RENDER_VALIDATION_FAILED",
        _ => "INVALID_BACKEND",
    };

    private static bool IsHelperCode(string code) => code is
        "PROTOCOL_ERROR"
        or "LOCAL_CONTEXT_FAILED"
        or "CONNECT_FAILED"
        or "CONNECT_TIMEOUT"
        or "CONNECT_CANCELLED"
        or "LOAD_FAILED"
        or "NOT_WRITER_DOCUMENT"
        or "READ_ONLY_NOT_VERIFIED"
        or "SOURCE_LOCATION_NOT_PRESERVED"
        or "EXPORT_FAILED"
        or "CLOSE_FAILED"
        or "TERMINATE_FAILED"
        or "INTERNAL_ERROR";

    private static async Task<BoundedBytes> ReadBoundedBytesAsync(
        Stream stream,
        int maximumBytes
    )
    {
        using var output = new MemoryStream(Math.Min(maximumBytes, 1024));
        var buffer = new byte[1024];
        var truncated = false;
        while (true)
        {
            var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            var remaining = maximumBytes - checked((int)output.Length);
            if (remaining > 0)
            {
                output.Write(buffer, 0, Math.Min(read, remaining));
            }
            if (read > remaining)
            {
                truncated = true;
            }
        }
        return new BoundedBytes(output.ToArray(), truncated);
    }

    private static async Task<BoundedText> ReadBoundedTextAsync(
        StreamReader reader,
        int maximumCharacters
    )
    {
        var builder = new StringBuilder(Math.Min(maximumCharacters, 1024));
        var buffer = new char[1024];
        var truncated = false;
        while (true)
        {
            var read = await reader.ReadAsync(buffer).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            var remaining = maximumCharacters - builder.Length;
            if (remaining > 0)
            {
                builder.Append(buffer, 0, Math.Min(read, remaining));
            }
            if (read > remaining)
            {
                truncated = true;
            }
        }
        return new BoundedText(builder.ToString(), truncated);
    }

    private static FileIdentity ReadPdfIdentity(
        string path,
        long maximumBytes,
        CancellationToken cancellationToken
    )
    {
        var identity = ReadIdentity(path, maximumBytes, "rendered PDF", cancellationToken);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> signature = stackalloc byte[5];
        if (stream.Read(signature) != signature.Length || !signature.SequenceEqual("%PDF-"u8))
        {
            throw Error(
                "RENDER_VALIDATION_FAILED",
                "LibreOffice output does not have a PDF signature"
            );
        }
        return identity;
    }

    private static FileIdentity ReadIdentity(
        string path,
        long maximumBytes,
        string label,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var length = new FileInfo(path).Length;
            if (length < 1 || length > maximumBytes)
            {
                throw Error("LIMIT_EXCEEDED", $"The {label} size is outside its limit");
            }
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.SequentialScan
            );
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1024 * 1024];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }
                hash.AppendData(buffer, 0, read);
            }
            return new FileIdentity(
                length,
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()
            );
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (FileNotFoundException exception)
        {
            throw Error("NOT_FOUND", $"The {label} does not exist", innerException: exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Error(
                "ACCESS_DENIED",
                $"The {label} could not be read",
                innerException: exception
            );
        }
    }

    private static void RequireExpectedHash(
        FileIdentity identity,
        string expected,
        string label
    )
    {
        if (!string.Equals(identity.Sha256, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw Error("EXECUTABLE_MISMATCH", $"The {label} does not match its expected SHA-256");
        }
    }

    private static void RequireStable(
        FileIdentity before,
        FileIdentity after,
        string label,
        string errorCode
    )
    {
        if (before.Bytes != after.Bytes
            || !string.Equals(before.Sha256, after.Sha256, StringComparison.Ordinal))
        {
            throw Error(errorCode, $"The {label} changed during isolated UNO rendering");
        }
    }

    private static LibreOfficeUnoBinaryIdentity Identity(
        string path,
        FileIdentity identity,
        bool expected
    ) => new(
        Path.GetFileName(path),
        identity.Bytes,
        identity.Sha256,
        expected,
        HashStable: true
    );

    private static string ResolveExistingLocalFile(string configured, string label)
    {
        var path = ResolveLocalPath(configured, label);
        if (!File.Exists(path))
        {
            throw Error("NOT_FOUND", $"The {label} does not exist");
        }
        EnsureNoLinks(path, includeLeaf: true, label);
        return path;
    }

    private static string ResolveNewLocalFile(string configured, string label)
    {
        var path = ResolveLocalPath(configured, label);
        if (File.Exists(path) || Directory.Exists(path))
        {
            throw Error("OUTPUT_EXISTS", $"The {label} already exists");
        }
        var directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory))
        {
            throw Error("NOT_FOUND", $"The {label} directory does not exist");
        }
        EnsureNoLinks(directory, includeLeaf: true, $"{label} directory");
        return path;
    }

    private static string ResolveLocalPath(string configured, string label)
    {
        if (string.IsNullOrWhiteSpace(configured)
            || configured.Length > LibreOfficeUnoRenderContract.MaximumPathCharacters
            || !Path.IsPathFullyQualified(configured)
            || configured.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            throw Error("INVALID_INPUT", $"The {label} path must be explicit and absolute");
        }
        if (configured.StartsWith("//", StringComparison.Ordinal)
            || configured.StartsWith(@"\\", StringComparison.Ordinal)
            || configured.StartsWith(@"\\?\", StringComparison.Ordinal)
            || configured.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            throw Error("INVALID_INPUT", $"The {label} cannot use a network or device path");
        }
        string path;
        try
        {
            path = Path.GetFullPath(configured);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException
        )
        {
            throw Error("INVALID_INPUT", $"The {label} path is invalid", innerException: exception);
        }
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var root = Path.GetPathRoot(path);
                if (!string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Network)
                {
                    throw Error("INVALID_INPUT", $"The {label} cannot use a mapped network drive");
                }
            }
            catch (WordToolkitOperationException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw Error("ACCESS_DENIED", $"The {label} drive could not be inspected", innerException: exception);
            }
        }
        return path;
    }

    private static void EnsureNoLinks(string path, bool includeLeaf, string label)
    {
        FileSystemInfo? current = includeLeaf
            ? File.Exists(path) ? new FileInfo(path) : new DirectoryInfo(path)
            : Directory.GetParent(path);
        while (current is not null)
        {
            try
            {
                current.Refresh();
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0
                    || current.LinkTarget is not null)
                {
                    throw Error("INVALID_INPUT", $"The {label} cannot use symbolic or reparse paths");
                }
            }
            catch (WordToolkitOperationException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is FileNotFoundException
                    or DirectoryNotFoundException
                    or UnauthorizedAccessException
                    or IOException
            )
            {
                throw Error("ACCESS_DENIED", $"The {label} path could not be inspected", innerException: exception);
            }
            current = current switch
            {
                FileInfo file => file.Directory,
                DirectoryInfo directory => directory.Parent,
                _ => null,
            };
        }
    }

    private static bool TryKill(Process? process)
    {
        if (process is null)
        {
            return false;
        }
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
                return true;
            }
        }
        catch
        {
            return true;
        }
        return false;
    }

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            return !Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            return !File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static void RequireFailureCleanup(
        string workspace,
        string outputPath,
        bool outputPublished,
        string originalErrorCode,
        bool processTreeKillRequired
    )
    {
        var outputRemoved = !outputPublished || TryDeleteFile(outputPath);
        var workspaceRemoved = TryDeleteDirectory(workspace);
        if (!outputRemoved || !workspaceRemoved)
        {
            throw Error(
                "ROLLBACK_FAILED",
                "The failed LibreOffice UNO render left unverified private or staged state",
                new
                {
                    original_error_code = originalErrorCode,
                    output_removed = outputRemoved,
                    workspace_removed = workspaceRemoved,
                    process_tree_kill_required = processTreeKillRequired,
                }
            );
        }
    }

    private static string PageRange(int first, int? last) =>
        first == 1 && last is null ? string.Empty : $"{first}-{last}";

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static int ReadInt32(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadInt32BigEndian(buffer);
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteInt32(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static string ReadString(Stream stream, int maximumBytes)
    {
        var length = ReadInt32(stream);
        if (length < 0 || length > maximumBytes)
        {
            throw new EndOfStreamException();
        }
        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    private static bool ReadBoolean(Stream stream) => stream.ReadByte() switch
    {
        0 => false,
        1 => true,
        _ => throw new EndOfStreamException(),
    };

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static string HostOperatingSystem() => OperatingSystem.IsWindows()
        ? "windows"
        : OperatingSystem.IsLinux()
            ? "linux"
            : OperatingSystem.IsMacOS()
                ? "macos"
                : "other";

    private static string ArchitectureName(Architecture architecture) =>
        architecture.ToString().ToLowerInvariant();

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static WordToolkitOperationException Error(
        string code,
        string message,
        object? details = null,
        Exception? innerException = null,
        bool retryable = false
    ) => new(
        code,
        message,
        reason: null,
        retryable,
        innerException,
        details
    );

    private sealed record FileIdentity(long Bytes, string Sha256);

    private sealed record BoundedBytes(byte[] Bytes, bool Truncated);

    private sealed record BoundedText(string Text, bool Truncated);

    private sealed record ProtocolResponse(
        bool Success,
        string Code,
        bool UnoConnectionVerified,
        bool WriterComponentVerified,
        bool ReadOnlyVerified,
        bool HiddenRequested,
        bool PickListDisabledRequested,
        bool RepairDisabledRequested,
        bool MacroNeverExecuteRequested,
        bool UpdateNoUpdateRequested,
        bool WriterPdfExportVerified,
        bool PdfFilterExplicit,
        bool OverwriteDisabled,
        bool SourceLocationPreserved,
        bool DocumentClosed,
        bool DesktopTerminated,
        bool LocalContextReleaseDeferredToProcessExit
    )
    {
        public bool AllSuccessEvidence =>
            UnoConnectionVerified
            && WriterComponentVerified
            && ReadOnlyVerified
            && HiddenRequested
            && PickListDisabledRequested
            && RepairDisabledRequested
            && MacroNeverExecuteRequested
            && UpdateNoUpdateRequested
            && WriterPdfExportVerified
            && PdfFilterExplicit
            && OverwriteDisabled
            && SourceLocationPreserved
            && DocumentClosed
            && DesktopTerminated
            && LocalContextReleaseDeferredToProcessExit;

        public static ProtocolResponse Failure(
            string code,
            bool documentClosed,
            bool desktopTerminated,
            bool localContextReleaseDeferredToProcessExit
        ) => new(
            false,
            code,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            documentClosed,
            desktopTerminated,
            localContextReleaseDeferredToProcessExit
        );
    }
}
