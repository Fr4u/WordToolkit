param(
    [string]$Configuration = "Release",
    [string]$RuntimeExecutable = ""
)

$ErrorActionPreference = "Stop"
$nativeRoot = Split-Path -Parent $PSScriptRoot
$repositoryRoot = Split-Path -Parent $nativeRoot
$executable = if ($RuntimeExecutable) {
    [IO.Path]::GetFullPath($RuntimeExecutable)
}
else {
    Join-Path `
        $nativeRoot `
        "WordToolkit.Native\bin\$Configuration\net8.0-windows\wordtoolkit-native.exe"
}
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Native executable not found: $executable"
}

$sampleImage = Join-Path $repositoryRoot "examples\generated\sample-figure.png"
if (-not (Test-Path -LiteralPath $sampleImage -PathType Leaf)) {
    throw "Acceptance-test image not found: $sampleImage"
}

$wordWasRunning = @(Get-Process -Name WINWORD -ErrorAction SilentlyContinue).Count -gt 0
$temporaryRoot = Join-Path `
    ([IO.Path]::GetTempPath()) `
    "wordtoolkit-expanded-$([Guid]::NewGuid().ToString('N'))"
[void](New-Item -ItemType Directory -Path $temporaryRoot)
$documentPath = Join-Path $temporaryRoot "expanded-live-test.docx"
$pdfPath = Join-Path $temporaryRoot "expanded-live-test.pdf"

$startInfo = [Diagnostics.ProcessStartInfo]::new($executable)
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.CreateNoWindow = $true
$process = [Diagnostics.Process]::new()
$process.StartInfo = $startInfo
[void]$process.Start()

$requestId = 0
function Invoke-Mcp {
    param(
        [string]$Method,
        [hashtable]$Params
    )
    $script:requestId++
    $request = @{
        jsonrpc = "2.0"
        id = $script:requestId
        method = $Method
        params = $Params
    } | ConvertTo-Json -Depth 40 -Compress
    $process.StandardInput.WriteLine($request)
    $process.StandardInput.Flush()
    $line = $process.StandardOutput.ReadLine()
    if (-not $line) {
        throw "Native MCP exited: $($process.StandardError.ReadToEnd())"
    }
    $response = $line | ConvertFrom-Json -Depth 40
    if ($response.error) {
        throw ($response.error | ConvertTo-Json -Depth 20 -Compress)
    }
    return $response
}

function Invoke-Tool {
    param(
        [string]$Name,
        [hashtable]$Arguments
    )
    $response = Invoke-Mcp `
        -Method "tools/call" `
        -Params @{
            name = "execute_wordtoolkit_action"
            arguments = @{
                action = $Name
                arguments = $Arguments
                response_mode = "full"
            }
        }
    if ($response.result.isError) {
        throw (
            $response.result.structuredContent.error |
                ConvertTo-Json -Depth 30 -Compress
        )
    }
    return $response.result.structuredContent.data
}

$documentId = ""
$version = 0L
$stage = "initialize"
$failure = $null
$closed = $false
$quitTested = $false
$report = [ordered]@{
    runtime = "dotnet-native"
    python_used = $false
    transport = "real MCP STDIO"
    word_was_running = $wordWasRunning
}

try {
    [void](Invoke-Mcp `
        -Method "initialize" `
        -Params @{
            protocolVersion = "2025-06-18"
            capabilities = @{}
            clientInfo = @{
                name = "wordtoolkit-native-expanded-acceptance"
                version = "1"
            }
        })
    $tools = Invoke-Mcp -Method "tools/list" -Params @{}
    if ($tools.result.tools.Count -ne 14) {
        throw "Expected 14 exposed tools, got $($tools.result.tools.Count)"
    }
    $report.exposed_tool_count = $tools.result.tools.Count
    $report.available_action_count = 83

    $stage = "start Word"
    $started = Invoke-Tool `
        -Name "start_word_application" `
        -Arguments @{ visible = $true }
    if (-not $started.word_running) {
        throw "Word did not report a running state"
    }

    $stage = "create saved live document"
    $created = Invoke-Tool `
        -Name "create_live_word_document" `
        -Arguments @{
            output_path = $documentPath
            activate = $true
        }
    $documentId = $created.live_document_id
    $version = [long]$created.live_version

    $stage = "insert searchable text"
    $marker = "WT_EXPANDED_COMMENT_TARGET_9A4F"
    $inserted = Invoke-Tool `
        -Name "insert_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            text = "Expanded native acceptance: $marker"
            target = "document_end"
            as_new_paragraph = $true
            expected_version = $version
        }
    $version = [long]$inserted.live_version

    $stage = "find tokenized range"
    $found = Invoke-Tool `
        -Name "find_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            search_text = $marker
            match_case = $true
            whole_word = $true
            max_results = 2
        }
    if ($found.match_count -ne 1 -or -not $found.matches[0].range_token) {
        throw "Find did not return one content-bound range token"
    }

    $stage = "insert comment"
    $comment = Invoke-Tool `
        -Name "insert_live_word_comment" `
        -Arguments @{
            live_document_id = $documentId
            range_token = $found.matches[0].range_token
            text = "Native comment acceptance probe."
            expected_version = $version
        }
    $version = [long]$comment.live_version
    if (-not $comment.comment.native_verified) {
        throw "Comment was not verified"
    }

    $stage = "insert footnote"
    $note = Invoke-Tool `
        -Name "insert_live_word_note" `
        -Arguments @{
            live_document_id = $documentId
            kind = "footnote"
            text = "Native footnote acceptance probe."
            target = "document_end"
            expected_version = $version
        }
    $version = [long]$note.live_version
    if (-not $note.note.native_verified) {
        throw "Footnote was not verified"
    }

    $stage = "insert image"
    $image = Invoke-Tool `
        -Name "insert_live_word_image" `
        -Arguments @{
            live_document_id = $documentId
            file_path = $sampleImage
            target = "document_end"
            width_points = 180
            lock_aspect_ratio = $true
            alternative_text = "WordToolkit acceptance-test figure"
            expected_version = $version
        }
    $version = [long]$image.live_version
    if (-not $image.image.native_verified) {
        throw "Image was not verified"
    }

    $stage = "set header"
    $header = Invoke-Tool `
        -Name "set_live_word_header_footer" `
        -Arguments @{
            live_document_id = $documentId
            section_index = 1
            kind = "header"
            variant = "primary"
            text = "WordToolkit expanded acceptance"
            enabled = $true
            link_to_previous = $false
            formatting = @{
                font_size_pt = 9
                paragraph_alignment = "center"
            }
            expected_version = $version
        }
    $version = [long]$header.live_version

    $stage = "set footer"
    $footer = Invoke-Tool `
        -Name "set_live_word_header_footer" `
        -Arguments @{
            live_document_id = $documentId
            section_index = 1
            kind = "footer"
            variant = "primary"
            text = "Generated by the native COM runtime"
            enabled = $true
            link_to_previous = $false
            formatting = @{
                font_size_pt = 8
                paragraph_alignment = "right"
            }
            expected_version = $version
        }
    $version = [long]$footer.live_version

    $mathMl = @'
<math xmlns="http://www.w3.org/1998/Math/MathML"><mfrac><mn>1</mn><msup><mi>x</mi><mn>2</mn></msup></mfrac></math>
'@
    $omml = @'
<m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math"><m:rad><m:radPr><m:degHide m:val="1"/></m:radPr><m:deg/><m:e><m:r><m:t>y</m:t></m:r></m:e></m:rad></m:oMath>
'@
    $stage = "preflight MathML and OMML"
    $preflight = Invoke-Tool `
        -Name "preflight_live_word_equations" `
        -Arguments @{
            equations = @(
                @{
                    value = $mathMl
                    input_format = "mathml"
                    display = $true
                },
                @{
                    value = $omml
                    input_format = "omml"
                    display = $true
                }
            )
        }
    if (-not $preflight.valid -or $preflight.equation_count -ne 2) {
        throw "MathML/OMML preflight failed"
    }

    $stage = "insert MathML and OMML"
    $equations = Invoke-Tool `
        -Name "insert_live_word_equations_batch" `
        -Arguments @{
            live_document_id = $documentId
            equations = @(
                @{
                    value = $mathMl
                    input_format = "mathml"
                    display = $true
                },
                @{
                    value = $omml
                    input_format = "omml"
                    display = $true
                }
            )
            expected_version = $version
        }
    $version = [long]$equations.live_version
    if ($equations.equation_operation_count -ne 2) {
        throw "Word did not create both markup equations"
    }

    $stage = "save"
    $saved = Invoke-Tool `
        -Name "save_live_word_document" `
        -Arguments @{
            live_document_id = $documentId
            expected_version = $version
        }
    if (-not $saved.saved) {
        throw "Document did not report a saved state"
    }

    $stage = "validate DOCX"
    $validated = Invoke-Tool `
        -Name "validate_live_word_document" `
        -Arguments @{ live_document_id = $documentId }
    if (-not $validated.validation.valid) {
        throw "Saved DOCX failed Open XML SDK validation"
    }

    $stage = "export PDF"
    $pdf = Invoke-Tool `
        -Name "export_live_word_pdf" `
        -Arguments @{
            live_document_id = $documentId
            output_path = $pdfPath
            overwrite = $false
            optimize_for = "print"
            bookmarks = "headings"
        }
    if (-not $pdf.exported -or $pdf.bytes -le 0) {
        throw "PDF export was empty"
    }

    $stage = "close saved document"
    [void](Invoke-Tool `
        -Name "close_live_word_document" `
        -Arguments @{
            live_document_id = $documentId
            save_changes = "save"
            expected_version = $version
        })
    $closed = $true
    $documentId = ""

    $stage = "reopen existing document"
    $reopened = Invoke-Tool `
        -Name "open_live_word_document" `
        -Arguments @{
            file_path = $documentPath
            read_only = $false
            activate = $true
            launch_if_needed = $false
        }
    $documentId = $reopened.live_document_id
    $version = [long]$reopened.live_version
    $closed = $false
    if (
        $reopened.document.comment_count -lt 1 -or
        $reopened.document.footnote_count -lt 1 -or
        $reopened.document.inline_image_count -lt 1 -or
        $reopened.document.equation_count -lt 2
    ) {
        throw "Reopened Word document lost one or more native structures"
    }

    $stage = "close reopened document"
    [void](Invoke-Tool `
        -Name "close_live_word_document" `
        -Arguments @{
            live_document_id = $documentId
            save_changes = "discard"
            expected_version = $version
        })
    $closed = $true
    $documentId = ""

    if (-not $wordWasRunning) {
        $stage = "quit owned Word application"
        $remaining = Invoke-Tool `
            -Name "list_live_word_documents" `
            -Arguments @{}
        if ($remaining.document_count -eq 0) {
            [void](Invoke-Tool `
                -Name "quit_word_application" `
                -Arguments @{
                    save_changes = "discard_all"
                    confirm = $true
                })
            $quitTested = $true
        }
    }

    $report.exercised_live_action_count = 48
    $report.start_word = $true
    $report.open_close = $true
    $report.comment = $true
    $report.footnote = $true
    $report.image = $true
    $report.header_footer = $true
    $report.mathml_omml = $true
    $report.pdf_export = $true
    $report.openxml_validation = $true
    $report.quit_word = if ($quitTested) { "tested" } else { "skipped_existing_user_instance" }
    $report.passed = $true
}
catch {
    $failure = $_
    $report.failed_stage = $stage
    $report.error = $_.Exception.Message
    $report.passed = $false
}
finally {
    if ($documentId -and -not $closed) {
        try {
            [void](Invoke-Tool `
                -Name "close_live_word_document" `
                -Arguments @{
                    live_document_id = $documentId
                    save_changes = "discard"
                    expected_version = $version
                })
        }
        catch {
            try {
                [void](Invoke-Tool `
                    -Name "disconnect_live_word_document" `
                    -Arguments @{ live_document_id = $documentId })
            }
            catch {
                # The original failure remains authoritative.
            }
        }
    }
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(5000)) {
        $process.Kill($true)
    }
    if (Test-Path -LiteralPath $temporaryRoot) {
        $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
        $resolvedSystemTemp = [IO.Path]::GetFullPath(
            [IO.Path]::GetTempPath()
        ).TrimEnd('\') + '\'
        if (-not $resolvedTemporaryRoot.StartsWith(
            $resolvedSystemTemp,
            [StringComparison]::OrdinalIgnoreCase
        )) {
            throw "Refusing to clean a test path outside the system temporary directory"
        }
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}

$report | ConvertTo-Json -Depth 30
if ($failure) {
    exit 1
}
