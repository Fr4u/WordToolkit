param(
    [Parameter(Mandatory = $true)]
    [string]$RuntimeExecutable
)

$ErrorActionPreference = "Stop"
$runtime = [IO.Path]::GetFullPath($RuntimeExecutable)
if (-not (Test-Path -LiteralPath $runtime -PathType Leaf)) {
    throw "Native executable not found: $runtime"
}

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sampleImage = Join-Path $repositoryRoot "examples\generated\sample-figure.png"
if (-not (Test-Path -LiteralPath $sampleImage -PathType Leaf)) {
    throw "Demo image not found: $sampleImage"
}

$desktop = [Environment]::GetFolderPath("Desktop")
$stamp = Get-Date -Format "yyyy-MM-dd-HHmmss"
$suffix = [Guid]::NewGuid().ToString("N").Substring(0, 6)
$documentPath = Join-Path $desktop "WordToolkit-Live-Test-$stamp-$suffix.docx"
$pdfPath = Join-Path $desktop "WordToolkit-Live-Test-$stamp-$suffix.pdf"
$marker = "WT_LIVE_COMMENT_TARGET_$suffix"

$startInfo = [Diagnostics.ProcessStartInfo]::new($runtime)
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
        [Parameter(Mandatory = $true)]
        [string]$Method,
        [Parameter(Mandatory = $true)]
        [hashtable]$Params
    )

    $script:requestId++
    $request = @{
        jsonrpc = "2.0"
        id = $script:requestId
        method = $Method
        params = $Params
    } | ConvertTo-Json -Depth 50 -Compress
    $process.StandardInput.WriteLine($request)
    $process.StandardInput.Flush()
    $line = $process.StandardOutput.ReadLine()
    if (-not $line) {
        throw "Native MCP exited: $($process.StandardError.ReadToEnd())"
    }
    $response = $line | ConvertFrom-Json -Depth 50
    if ($response.error) {
        throw ($response.error | ConvertTo-Json -Depth 30 -Compress)
    }
    return $response
}

function Invoke-Tool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
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
$documentOpen = $false
$stage = "initialize"
$failure = $null
$report = [ordered]@{
    runtime = $runtime
    transport = "real MCP STDIO"
    python_used = $false
    docx_path = $documentPath
    pdf_path = $pdfPath
}

try {
    [void](Invoke-Mcp `
        -Method "initialize" `
        -Params @{
            protocolVersion = "2025-06-18"
            capabilities = @{}
            clientInfo = @{
                name = "wordtoolkit-live-capabilities-demo"
                version = "1"
            }
        })

    $tools = Invoke-Mcp -Method "tools/list" -Params @{}
    if ($tools.result.tools.Count -ne 15) {
        throw "Expected 15 exposed tools, got $($tools.result.tools.Count)"
    }
    $report.exposed_tools = $tools.result.tools.Count
    $report.available_actions = 84

    $stage = "start or attach Word"
    $started = Invoke-Tool `
        -Name "start_word_application" `
        -Arguments @{ visible = $true }
    if (-not $started.word_running) {
        throw "Word did not report a running state"
    }

    $stage = "create visible saved document"
    $created = Invoke-Tool `
        -Name "create_live_word_document" `
        -Arguments @{
            output_path = $documentPath
            activate = $true
        }
    $documentId = $created.live_document_id
    $version = [long]$created.live_version
    $documentOpen = $true

    $stage = "apply one fast mixed batch"
    $batchWatch = [Diagnostics.Stopwatch]::StartNew()
    $batch = Invoke-Tool `
        -Name "apply_live_word_operations" `
        -Arguments @{
            live_document_id = $documentId
            expected_version = $version
            activate = $true
            optimize_screen_updates = $true
            operations = @(
                @{
                    type = "text"
                    text = "WordToolkit — test live nowych możliwości"
                    as_new_paragraph = $true
                    formatting = @{
                        font_name = "Aptos Display"
                        font_size_pt = 24
                        bold = $true
                        paragraph_alignment = "center"
                        space_after_pt = 14
                        keep_with_next = $true
                    }
                },
                @{
                    type = "text"
                    text = "Ten dokument powstał bezpośrednio w uruchomionym Microsoft Wordzie przez natywny runtime .NET i COM. Nie został odbudowany obok Worda, nie użyto Pythona i nie wklejono obrazu udającego równanie."
                    as_new_paragraph = $true
                    formatting = @{
                        font_name = "Aptos"
                        font_size_pt = 11
                        paragraph_alignment = "justify"
                        space_after_pt = 8
                    }
                },
                @{
                    type = "text"
                    text = "Co sprawdzam"
                    as_new_paragraph = $true
                    formatting = @{
                        font_name = "Aptos Display"
                        font_size_pt = 16
                        bold = $true
                        space_before_pt = 8
                        space_after_pt = 6
                        keep_with_next = $true
                    }
                },
                @{
                    type = "text"
                    text = "Jednym szybkim wywołaniem powstał sformatowany tekst. Dalej wtyczka doda natywny komentarz do wskazanego fragmentu, przypis dolny, osadzony obraz, nagłówek, stopkę oraz dwa edytowalne równania Worda zapisane pierwotnie jako MathML i OMML."
                    as_new_paragraph = $true
                    formatting = @{
                        font_name = "Aptos"
                        font_size_pt = 11
                        paragraph_alignment = "justify"
                        space_after_pt = 8
                    }
                },
                @{
                    type = "text"
                    text = "Cel komentarza: $marker"
                    as_new_paragraph = $true
                    formatting = @{
                        font_name = "Aptos"
                        font_size_pt = 11
                        italic = $true
                        font_color_rgb = "#2457A6"
                        space_after_pt = 8
                    }
                },
                @{
                    type = "text"
                    text = "Równania natywne"
                    as_new_paragraph = $true
                    formatting = @{
                        font_name = "Aptos Display"
                        font_size_pt = 16
                        bold = $true
                        space_before_pt = 8
                        space_after_pt = 6
                        keep_with_next = $true
                    }
                },
                @{
                    type = "text"
                    text = "Poniższe obiekty można kliknąć i edytować w edytorze równań Worda."
                    as_new_paragraph = $true
                    formatting = @{
                        font_name = "Aptos"
                        font_size_pt = 11
                        space_after_pt = 4
                    }
                }
            )
        }
    $batchWatch.Stop()
    $version = [long]$batch.live_version
    $report.fast_batch_ms = [Math]::Round($batchWatch.Elapsed.TotalMilliseconds, 1)
    $report.fast_batch_operations = $batch.operation_count

    $stage = "find exact comment target"
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

    $stage = "insert native comment"
    $comment = Invoke-Tool `
        -Name "insert_live_word_comment" `
        -Arguments @{
            live_document_id = $documentId
            range_token = $found.matches[0].range_token
            text = "Komentarz został przypięty do dokładnie znalezionego tekstu przez natywną kolekcję Comments w Wordzie."
            expected_version = $version
        }
    $version = [long]$comment.live_version
    if (-not $comment.comment.native_verified) {
        throw "Native comment verification failed"
    }

    $stage = "insert native footnote"
    $note = Invoke-Tool `
        -Name "insert_live_word_note" `
        -Arguments @{
            live_document_id = $documentId
            kind = "footnote"
            text = "To jest prawdziwy przypis dolny Worda, a nie dopisany ręcznie tekst."
            target = "document_end"
            expected_version = $version
        }
    $version = [long]$note.live_version
    if (-not $note.note.native_verified) {
        throw "Native footnote verification failed"
    }

    $stage = "insert native inline image"
    $image = Invoke-Tool `
        -Name "insert_live_word_image" `
        -Arguments @{
            live_document_id = $documentId
            file_path = $sampleImage
            target = "document_end"
            width_points = 190
            lock_aspect_ratio = $true
            alternative_text = "Przykładowy wykres osadzony przez WordToolkit"
            expected_version = $version
        }
    $version = [long]$image.live_version
    if (-not $image.image.native_verified) {
        throw "Native image verification failed"
    }

    $stage = "set native header"
    $header = Invoke-Tool `
        -Name "set_live_word_header_footer" `
        -Arguments @{
            live_document_id = $documentId
            section_index = 1
            kind = "header"
            variant = "primary"
            text = "WORDTOOLKIT · TEST LIVE · NATYWNY WORD"
            enabled = $true
            link_to_previous = $false
            formatting = @{
                font_name = "Aptos"
                font_size_pt = 9
                font_color_rgb = "#666666"
                paragraph_alignment = "center"
            }
            expected_version = $version
        }
    $version = [long]$header.live_version

    $stage = "set native footer"
    $footer = Invoke-Tool `
        -Name "set_live_word_header_footer" `
        -Arguments @{
            live_document_id = $documentId
            section_index = 1
            kind = "footer"
            variant = "primary"
            text = "Wygenerowano bezpośrednio w Microsoft Word"
            enabled = $true
            link_to_previous = $false
            formatting = @{
                font_name = "Aptos"
                font_size_pt = 8
                font_color_rgb = "#777777"
                paragraph_alignment = "right"
            }
            expected_version = $version
        }
    $version = [long]$footer.live_version

    $mathMl = @'
<math xmlns="http://www.w3.org/1998/Math/MathML"><mfrac><mn>1</mn><mrow><mi>x</mi><mo>+</mo><mn>1</mn></mrow></mfrac></math>
'@
    $omml = @'
<m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math"><m:rad><m:radPr><m:degHide m:val="1"/></m:radPr><m:deg/><m:e><m:r><m:t>x+1</m:t></m:r></m:e></m:rad></m:oMath>
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

    $stage = "insert editable MathML and OMML equations"
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
        throw "Word did not create both native equations"
    }

    $stage = "save DOCX"
    $saved = Invoke-Tool `
        -Name "save_live_word_document" `
        -Arguments @{
            live_document_id = $documentId
            expected_version = $version
        }
    if (-not $saved.saved) {
        throw "Document did not report a saved state"
    }

    $stage = "validate saved DOCX"
    $validated = Invoke-Tool `
        -Name "validate_live_word_document" `
        -Arguments @{ live_document_id = $documentId }
    if (-not $validated.validation.valid) {
        throw "Saved DOCX failed Open XML SDK validation"
    }

    $stage = "export native Word PDF"
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

    $stage = "close saved test document"
    [void](Invoke-Tool `
        -Name "close_live_word_document" `
        -Arguments @{
            live_document_id = $documentId
            save_changes = "save"
            expected_version = $version
        })
    $documentOpen = $false
    $documentId = ""

    $stage = "reopen exact DOCX in Word"
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
    $documentOpen = $true
    $document = $reopened.document
    if (
        $document.comment_count -lt 1 -or
        $document.footnote_count -lt 1 -or
        $document.inline_image_count -lt 1 -or
        $document.equation_count -lt 2 -or
        $document.section_count -lt 1
    ) {
        throw "Reopened document lost one or more native Word structures"
    }

    $report.comment_count = $document.comment_count
    $report.footnote_count = $document.footnote_count
    $report.inline_image_count = $document.inline_image_count
    $report.equation_count = $document.equation_count
    $report.section_count = $document.section_count
    $report.pdf_bytes = [long]$pdf.bytes
    $report.openxml_valid = $true
    $report.close_reopen_passed = $true
    $report.left_open_in_word = $true
    $report.passed = $true

    $stage = "disconnect while leaving document open"
    [void](Invoke-Tool `
        -Name "disconnect_live_word_document" `
        -Arguments @{ live_document_id = $documentId })
    $documentId = ""
    $documentOpen = $false
}
catch {
    $failure = $_
    $report.failed_stage = $stage
    $report.error = $_.Exception.Message
    $report.passed = $false
}
finally {
    if ($documentId -and $documentOpen) {
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
                # Preserve the first failure.
            }
        }
    }

    $process.StandardInput.Close()
    if (-not $process.WaitForExit(5000)) {
        $process.Kill($true)
    }
}

$report | ConvertTo-Json -Depth 30
if ($failure) {
    exit 1
}
