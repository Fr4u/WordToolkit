param(
    [Parameter(Mandatory = $true)]
    [string]$RuntimeExecutable
)

$ErrorActionPreference = "Stop"
$totalWatch = [Diagnostics.Stopwatch]::StartNew()
$runtime = [IO.Path]::GetFullPath($RuntimeExecutable)
if (-not (Test-Path -LiteralPath $runtime -PathType Leaf)) {
    throw "Native executable not found: $runtime"
}

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sampleImage = Join-Path $repositoryRoot "examples\generated\sample-figure.png"
if (-not (Test-Path -LiteralPath $sampleImage -PathType Leaf)) {
    throw "Acceptance image not found: $sampleImage"
}

$desktop = [Environment]::GetFolderPath("Desktop")
$stamp = Get-Date -Format "yyyy-MM-dd-HHmmss"
$suffix = [Guid]::NewGuid().ToString("N").Substring(0, 6)
$documentPath = Join-Path $desktop "WordToolkit-FULL-Live-Test-$stamp-$suffix.docx"
$pdfPath = Join-Path $desktop "WordToolkit-FULL-Live-Test-$stamp-$suffix.pdf"
$commentMarker = "WT_COMMENT_$suffix"
$replaceMarker = "WT_REPLACE_$suffix"
$replacementMarker = "WT_REPLACED_$suffix"
$cursorProbe = "WT_CURSOR_UNDO_$suffix"

$nativeTools = @(
    "list_live_word_documents",
    "start_word_application",
    "create_live_word_document",
    "open_live_word_document",
    "connect_live_word_document",
    "inspect_live_word_document",
    "map_live_word_structures",
    "inspect_live_word_structure_items",
    "inspect_live_word_equation_learning",
    "inspect_live_word_structure_learning",
    "inspect_live_word_object_model_types",
    "inspect_live_word_object_model_members",
    "inspect_live_word_member_capabilities",
    "preflight_live_word_member_operations",
    "execute_live_word_member_operations",
    "find_live_word_text",
    "replace_live_word_text",
    "inspect_live_word_review",
    "manage_live_word_review",
    "diagnose_live_word_layout",
    "get_live_word_selection",
    "inspect_live_word_undo",
    "undo_live_word_operation",
    "insert_live_word_text",
    "format_live_word_selection",
    "insert_live_word_table",
    "preflight_live_word_table_formulas",
    "insert_live_word_table_formulas",
    "update_live_word_table_fields",
    "insert_live_word_list",
    "preflight_live_word_bookmarks",
    "insert_live_word_bookmarks",
    "preflight_live_word_fields",
    "insert_live_word_fields",
    "insert_live_word_image",
    "insert_live_word_comment",
    "insert_live_word_note",
    "set_live_word_header_footer",
    "insert_live_word_equation",
    "insert_live_word_equations_batch",
    "preflight_live_word_equations",
    "apply_live_word_operations",
    "validate_live_word_document",
    "export_live_word_pdf",
    "save_live_word_document",
    "close_live_word_document",
    "quit_word_application",
    "disconnect_live_word_document"
)

$exposedTools = @(
    "list_live_word_documents",
    "start_word_application",
    "create_live_word_document",
    "open_live_word_document",
    "connect_live_word_document",
    "inspect_ooxml_package",
    "inspect_live_word_document",
    "get_live_word_selection",
    "apply_live_word_operations",
    "save_live_word_document",
    "disconnect_live_word_document",
    "search_wordtoolkit_actions",
    "inspect_wordtoolkit_action",
    "execute_wordtoolkit_action"
)
$catalogNames = @()

$toolStats = [ordered]@{}
foreach ($name in $nativeTools) {
    $toolStats[$name] = [ordered]@{
        status = "not_run"
        calls = 0
        total_ms = 0.0
        max_ms = 0.0
        note = ""
    }
}

$startInfo = [Diagnostics.ProcessStartInfo]::new($runtime)
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.CreateNoWindow = $true
$utf8NoBom = New-Object Text.UTF8Encoding($false)
$startInfo.StandardOutputEncoding = $utf8NoBom
$startInfo.StandardErrorEncoding = $utf8NoBom
$process = [Diagnostics.Process]::new()
$process.StartInfo = $startInfo
$previousInputEncoding = [Console]::InputEncoding
[Console]::InputEncoding = [Text.Encoding]::ASCII
try {
    [void]$process.Start()
    $mcpInput = $process.StandardInput
}
finally {
    [Console]::InputEncoding = $previousInputEncoding
}

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
    } | ConvertTo-Json -Depth 60 -Compress
    # Windows PowerShell 5.1 does not expose ProcessStartInfo.StandardInputEncoding.
    # Escape non-ASCII code units so MCP input stays valid under every console code page.
    $request = [regex]::Replace(
        $request,
        '[^\u0000-\u007F]',
        { param($match) '\u{0:x4}' -f [int][char]$match.Value }
    )
    $mcpInput.WriteLine($request)
    $mcpInput.Flush()
    $line = $process.StandardOutput.ReadLine()
    if (-not $line) {
        throw "Native MCP exited: $($process.StandardError.ReadToEnd())"
    }
    $response = $line | ConvertFrom-Json
    if ($response.error) {
        throw ($response.error | ConvertTo-Json -Depth 30 -Compress)
    }
    return $response
}

function Add-ToolTiming {
    param(
        [string]$Name,
        [double]$Milliseconds,
        [string]$Status = "passed",
        [string]$Note = ""
    )

    $entry = $toolStats[$Name]
    $entry.calls++
    $entry.total_ms = [Math]::Round(
        [double]$entry.total_ms + $Milliseconds,
        3
    )
    $entry.max_ms = [Math]::Round(
        [Math]::Max([double]$entry.max_ms, $Milliseconds),
        3
    )
    if ($entry.status -ne "failed") {
        $entry.status = $Status
    }
    if ($Note) {
        $entry.note = $Note
    }
}

function Invoke-TimedTool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [hashtable]$Arguments
    )

    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $callName = "execute_wordtoolkit_action"
        $callArguments = @{
            action = $Name
            arguments = $Arguments
            response_mode = "full"
        }
        $response = Invoke-Mcp `
            -Method "tools/call" `
            -Params @{ name = $callName; arguments = $callArguments }
        if ($response.result.isError) {
            throw (
                $response.result.structuredContent.error |
                    ConvertTo-Json -Depth 30 -Compress
            )
        }
        $watch.Stop()
        Add-ToolTiming -Name $Name -Milliseconds $watch.Elapsed.TotalMilliseconds
        return $response.result.structuredContent.data
    }
    catch {
        $watch.Stop()
        Add-ToolTiming `
            -Name $Name `
            -Milliseconds $watch.Elapsed.TotalMilliseconds `
            -Status "failed" `
            -Note $_.Exception.Message
        throw
    }
}

function Invoke-TimedToolExpectedError {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [hashtable]$Arguments,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedCode,
        [Parameter(Mandatory = $true)]
        [string]$Note
    )

    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $callName = "execute_wordtoolkit_action"
        $callArguments = @{
            action = $Name
            arguments = $Arguments
            response_mode = "full"
        }
        $response = Invoke-Mcp `
            -Method "tools/call" `
            -Params @{ name = $callName; arguments = $callArguments }
        $watch.Stop()
        if (-not $response.result.isError) {
            Add-ToolTiming `
                -Name $Name `
                -Milliseconds $watch.Elapsed.TotalMilliseconds `
                -Status "failed" `
                -Note "The required safety gate did not reject the call"
            throw "Expected $Name to fail closed"
        }
        $errorData = $response.result.structuredContent.error
        if ($errorData.code -ne $ExpectedCode) {
            Add-ToolTiming `
                -Name $Name `
                -Milliseconds $watch.Elapsed.TotalMilliseconds `
                -Status "failed" `
                -Note "Expected $ExpectedCode, got $($errorData.code)"
            throw "Unexpected safety error code from $Name"
        }
        Add-ToolTiming `
            -Name $Name `
            -Milliseconds $watch.Elapsed.TotalMilliseconds `
            -Status "guard_passed" `
            -Note $Note
        $script:safetyGuardPassCount++
        return $errorData
    }
    catch {
        if ($toolStats[$Name].status -eq "not_run") {
            $watch.Stop()
            Add-ToolTiming `
                -Name $Name `
                -Milliseconds $watch.Elapsed.TotalMilliseconds `
                -Status "failed" `
                -Note $_.Exception.Message
        }
        throw
    }
}

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )
    if (-not $Condition) {
        throw $Message
    }
}

function Select-FirstWordInActiveWord {
    param([string]$WindowTitle)

    $watch = [Diagnostics.Stopwatch]::StartNew()
    $shell = New-Object -ComObject WScript.Shell
    try {
        $activated = $shell.AppActivate($WindowTitle)
        if (-not $activated) {
            $activated = $shell.AppActivate("Microsoft Word")
        }
        if (-not $activated) {
            throw "Could not activate the real Microsoft Word window"
        }
        Start-Sleep -Milliseconds 120
        $shell.SendKeys("^{HOME}")
        Start-Sleep -Milliseconds 80
        $shell.SendKeys("^+{RIGHT}")
        Start-Sleep -Milliseconds 120
    }
    finally {
        if ($shell) {
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
        }
        $watch.Stop()
    }
    return $watch.Elapsed.TotalMilliseconds
}

$documentId = ""
$version = 0L
$documentOpen = $false
$stage = "initialize"
$failure = $null
$selectionSetupMs = 0.0
$compactPreflightCharacters = 0
$safetyGuardPassCount = 0
$actualWordQuitSkipped = $true
$actualWordQuitSkipReason = ""
$report = [ordered]@{
    runtime = $runtime
    transport = "real MCP STDIO"
    python_used = $false
    document_path = $documentPath
    pdf_path = $pdfPath
}

try {
    [void](Invoke-Mcp `
        -Method "initialize" `
        -Params @{
            protocolVersion = "2025-06-18"
            capabilities = @{}
            clientInfo = @{
                name = "wordtoolkit-full-live-timed-acceptance"
                version = "1"
            }
        })

    $stage = "verify the token-lean 14-tool catalog and lazy live actions"
    $catalog = Invoke-Mcp -Method "tools/list" -Params @{}
    $script:catalogNames = @($catalog.result.tools | ForEach-Object { $_.name })
    Assert-True `
        -Condition ($script:catalogNames.Count -eq 14) `
        -Message "Expected 14 exposed tools, got $($script:catalogNames.Count)"
    foreach ($name in $exposedTools) {
        Assert-True `
            -Condition ($script:catalogNames -contains $name) `
            -Message "Installed runtime is missing $name"
    }

    $search = Invoke-Mcp `
        -Method "tools/call" `
        -Params @{
            name = "search_wordtoolkit_actions"
            arguments = @{ query = "equation"; max_results = 12 }
        }
    Assert-True `
        -Condition (
            -not $search.result.isError -and
            $search.result.structuredContent.data.match_count -gt 0
        ) `
        -Message "The lazy action search gateway returned no equation actions"
    foreach ($name in $nativeTools) {
        $inspection = Invoke-Mcp `
            -Method "tools/call" `
            -Params @{
                name = "inspect_wordtoolkit_action"
                arguments = @{ action = $name }
            }
        Assert-True `
            -Condition (
                -not $inspection.result.isError -and
                $inspection.result.structuredContent.data.action -eq $name
            ) `
            -Message "Lazy action catalog is missing $name"
    }

    $stage = "list real Word documents"
    $listedBefore = Invoke-TimedTool `
        -Name "list_live_word_documents" `
        -Arguments @{}
    $wordWasRunning = [bool]$listedBefore.word_running
    $preexistingDocuments = @($listedBefore.documents)
    $unrelatedDirtyDocuments = @(
        $preexistingDocuments |
            Where-Object { -not $_.saved -and $_.full_name -ne $documentPath }
    )

    $stage = "start or attach real Microsoft Word"
    $started = Invoke-TimedTool `
        -Name "start_word_application" `
        -Arguments @{ visible = $true }
    Assert-True `
        -Condition ($started.word_running -and $started.visible) `
        -Message "Word did not report a visible running state"

    $stage = "verify the quit confirmation safety gate"
    $quitGate = Invoke-TimedToolExpectedError `
        -Name "quit_word_application" `
        -Arguments @{
            save_changes = "discard_all"
            confirm = $false
        } `
        -ExpectedCode "AUTH_FORBIDDEN" `
        -Note "Confirmation gate passed; no unconfirmed application quit was allowed"

    $stage = "create a new saved live DOCX"
    $created = Invoke-TimedTool `
        -Name "create_live_word_document" `
        -Arguments @{
            output_path = $documentPath
            activate = $true
        }
    $documentId = $created.live_document_id
    $version = [long]$created.live_version
    $documentOpen = $true

    $stage = "inspect the new real Word document"
    $initialInspection = Invoke-TimedTool `
        -Name "inspect_live_word_document" `
        -Arguments @{ live_document_id = $documentId }
    Assert-True `
        -Condition ($initialInspection.document.full_name -eq $documentPath) `
        -Message "The live handle does not point to the new DOCX"

    $stage = "apply one mixed text and equation batch"
    $mixedBatch = Invoke-TimedTool `
        -Name "apply_live_word_operations" `
        -Arguments @{
            live_document_id = $documentId
            expected_version = $version
            activate = $true
            optimize_screen_updates = $true
            operations = @(
                @{
                    type = "text"
                    text = "WordToolkit FULL Live Acceptance"
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
                    text = "Full native path test: model -> MCP -> .NET -> COM -> Microsoft Word."
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
                    text = "Znacznik komentarza: $commentMarker"
                    as_new_paragraph = $true
                    formatting = @{
                        font_name = "Aptos"
                        font_size_pt = 11
                        italic = $true
                        font_color_rgb = "#2457A6"
                        space_after_pt = 6
                    }
                },
                @{
                    type = "text"
                    text = "Znacznik zamiany: $replaceMarker"
                    as_new_paragraph = $true
                    formatting = @{
                        font_name = "Aptos"
                        font_size_pt = 11
                        space_after_pt = 6
                    }
                },
                @{
                    type = "text"
                    text = "Equation created inside a mixed operation batch:"
                    as_new_paragraph = $true
                    formatting = @{
                        font_name = "Aptos Display"
                        font_size_pt = 15
                        bold = $true
                        space_before_pt = 8
                        space_after_pt = 4
                    }
                },
                @{
                    type = "equation"
                    value = "\int_0^1 x^2\,d x=\frac{1}{3}"
                    input_format = "latex"
                    display = $true
                }
            )
        }
    $version = [long]$mixedBatch.live_version
    Assert-True `
        -Condition (
            $mixedBatch.operation_count -eq 6 -and
            $mixedBatch.equation_operation_count -eq 1
        ) `
        -Message "Mixed native batch did not create the expected operations"

    $stage = "scan the installed Word object-model type library"
    $objectTypes = Invoke-TimedTool `
        -Name "inspect_live_word_object_model_types" `
        -Arguments @{
            query = ""
            limit = 5
            refresh = $true
        }
    Assert-True `
        -Condition (
            $objectTypes.stats.type_count -gt 0 -and
            $objectTypes.stats.member_count -gt 0 -and
            $objectTypes.stats.scan_errors -eq 0 -and
            -not $objectTypes.stats.truncated
        ) `
        -Message "The installed Word type-library scan is incomplete"

    $stage = "inspect installed Range.Text member metadata"
    $rangeTextMembers = Invoke-TimedTool `
        -Name "inspect_live_word_object_model_members" `
        -Arguments @{
            type_name = "Range"
            query = "Text"
            limit = 20
        }
    Assert-True `
        -Condition (
            @(
                $rangeTextMembers.members |
                    Where-Object { $_.name -eq "Text" }
            ).Count -ge 2
        ) `
        -Message "Installed Range.Text accessors were not cataloged"

    $stage = "derive the controlled Range.Text read capability"
    $rangeTextCapabilities = Invoke-TimedTool `
        -Name "inspect_live_word_member_capabilities" `
        -Arguments @{
            type_name = "Range"
            query = "Text"
            member_kind = "property_get"
            execution = "read_allowed"
            limit = 20
        }
    $rangeTextRead = @(
        $rangeTextCapabilities.capabilities |
            Where-Object {
                $_.member.name -eq "Text" -and
                $_.member.kind -eq "property_get"
            }
    )[0]
    Assert-True `
        -Condition ([bool]$rangeTextRead.capability_id) `
        -Message "No executable Range.Text read capability was derived"

    $memberOperations = @(
        @{
            operation_id = "read_document_text"
            capability_id = $rangeTextRead.capability_id
            target = @{ kind = "document_content" }
            arguments = @()
            result_id = "document_text"
        }
    )

    $stage = "preflight one catalog-backed Word member operation"
    $memberPreflight = Invoke-TimedTool `
        -Name "preflight_live_word_member_operations" `
        -Arguments @{ operations = $memberOperations }
    Assert-True `
        -Condition (
            $memberPreflight.valid -and
            $memberPreflight.mutating_count -eq 0
        ) `
        -Message "The safe Range.Text read did not pass member preflight"

    $stage = "execute one catalog-backed read against the real document"
    $memberExecution = Invoke-TimedTool `
        -Name "execute_live_word_member_operations" `
        -Arguments @{
            live_document_id = $documentId
            operations = $memberOperations
            activate = $true
        }
    Assert-True `
        -Condition (
            $memberExecution.executed_count -eq 1 -and
            -not $memberExecution.mutating -and
            $memberExecution.results[0].result.kind -eq "text"
        ) `
        -Message "Catalog-backed Range.Text execution did not return bounded text"

    $stage = "derive the bounded native Range.Select view capability"
    $rangeSelectCapabilities = Invoke-TimedTool `
        -Name "inspect_live_word_member_capabilities" `
        -Arguments @{
            type_name = "Range"
            query = "Select"
            member_kind = "method"
            execution = "write_allowed"
            limit = 20
        }
    $rangeSelect = @(
        $rangeSelectCapabilities.capabilities |
            Where-Object {
                $_.member.name -eq "Select" -and
                $_.member.kind -eq "method"
            }
    )[0]
    Assert-True `
        -Condition ([bool]$rangeSelect.capability_id) `
        -Message "No bounded Range.Select capability was derived"

    $stage = "derive the bounded native Document.Range factory"
    $documentRangeCapabilities = Invoke-TimedTool `
        -Name "inspect_live_word_member_capabilities" `
        -Arguments @{
            type_name = "_Document"
            query = "Range"
            member_kind = "method"
            execution = "read_allowed"
            limit = 20
        }
    $documentRange = @(
        $documentRangeCapabilities.capabilities |
            Where-Object {
                $_.member.name -eq "Range" -and
                $_.member.kind -eq "method"
            }
    )[0]
    Assert-True `
        -Condition ([bool]$documentRange.capability_id) `
        -Message "No bounded Document.Range factory was derived"

    $selectionOperation = @(
        @{
            operation_id = "create_first_word_range"
            capability_id = $documentRange.capability_id
            target = @{ kind = "document" }
            arguments = @(0, 11)
            result_id = "first_word_range"
        },
        @{
            operation_id = "select_first_word_range"
            capability_id = $rangeSelect.capability_id
            target = @{
                kind = "result"
                result_id = "first_word_range"
            }
            arguments = @()
        }
    )
    $stage = "select the real document range through native COM"
    $selectionSetupWatch = [Diagnostics.Stopwatch]::StartNew()
    $selectionExecution = Invoke-TimedTool `
        -Name "execute_live_word_member_operations" `
        -Arguments @{
            live_document_id = $documentId
            operations = $selectionOperation
            activate = $true
        }
    $selectionSetupWatch.Stop()
    $selectionSetupMs = $selectionSetupWatch.Elapsed.TotalMilliseconds
    Assert-True `
        -Condition (
            $selectionExecution.executed_count -eq 2 -and
            -not $selectionExecution.mutating
        ) `
        -Message "The bounded native Range.Select call did not execute"

    $stage = "read the real native Word selection"
    $selection = Invoke-TimedTool `
        -Name "get_live_word_selection" `
        -Arguments @{ live_document_id = $documentId }
    Assert-True `
        -Condition (
            -not $selection.selection.collapsed -and
            $selection.selection.text_preview.Length -gt 0
        ) `
        -Message "The real Word selection is empty"

    $stage = "format the verified live selection"
    $formatted = Invoke-TimedTool `
        -Name "format_live_word_selection" `
        -Arguments @{
            live_document_id = $documentId
            selection_token = $selection.selection.selection_token
            expected_version = $version
            formatting = @{
                bold = $true
                underline = $true
                font_color_rgb = "#9C1C1C"
            }
        }
    $version = [long]$formatted.live_version

    $stage = "refresh and replace the verified selection"
    $selectionAfterFormat = Invoke-TimedTool `
        -Name "get_live_word_selection" `
        -Arguments @{ live_document_id = $documentId }
    Assert-True `
        -Condition (-not $selectionAfterFormat.selection.collapsed) `
        -Message "Word lost the selected range after formatting"
    $selectionReplacement = Invoke-TimedTool `
        -Name "insert_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            text = $selectionAfterFormat.selection.text_preview
            target = "selection"
            selection_token = $selectionAfterFormat.selection.selection_token
            replace_selection = $true
            activate = $true
            expected_version = $version
        }
    $version = [long]$selectionReplacement.live_version

    $stage = "move the real cursor to the document end"
    $cursorShell = New-Object -ComObject WScript.Shell
    try {
        [void]$cursorShell.AppActivate($created.document.name)
        Start-Sleep -Milliseconds 80
        $cursorShell.SendKeys("^{END}")
        Start-Sleep -Milliseconds 100
    }
    finally {
        if ($cursorShell) {
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($cursorShell)
        }
    }

    $stage = "read a fresh live cursor token"
    $cursor = Invoke-TimedTool `
        -Name "get_live_word_selection" `
        -Arguments @{ live_document_id = $documentId }
    Assert-True `
        -Condition $cursor.selection.collapsed `
        -Message "Expected a collapsed cursor at the document end"

    $stage = "insert text at the verified live cursor"
    $cursorInserted = Invoke-TimedTool `
        -Name "insert_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            text = $cursorProbe
            target = "cursor"
            selection_token = $cursor.selection.selection_token
            as_new_paragraph = $true
            activate = $true
            expected_version = $version
        }
    $version = [long]$cursorInserted.live_version

    $stage = "inspect guarded Word Undo"
    $undoInspection = Invoke-TimedTool `
        -Name "inspect_live_word_undo" `
        -Arguments @{
            live_document_id = $documentId
            max_entries = 5
        }
    Assert-True `
        -Condition $undoInspection.wordtoolkit_undo_eligible `
        -Message "The current Word Undo entry is not guarded as WordToolkit"

    $stage = "undo exactly one WordToolkit transaction"
    $undone = Invoke-TimedTool `
        -Name "undo_live_word_operation" `
        -Arguments @{
            live_document_id = $documentId
            undo_token = $undoInspection.undo_token
            expected_version = $version
        }
    $version = [long]$undone.live_version

    $stage = "find the exact comment range"
    $foundComment = Invoke-TimedTool `
        -Name "find_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            search_text = $commentMarker
            match_case = $true
            whole_word = $true
            context_chars = 30
            max_results = 2
        }
    Assert-True `
        -Condition (
            $foundComment.match_count -eq 1 -and
            $foundComment.matches[0].range_token
        ) `
        -Message "Native Word Find did not return one verified range"

    $stage = "insert a native Word comment"
    $comment = Invoke-TimedTool `
        -Name "insert_live_word_comment" `
        -Arguments @{
            live_document_id = $documentId
            range_token = $foundComment.matches[0].range_token
            text = "Full test: comment attached to the range returned by native Word Find."
            expected_version = $version
        }
    $version = [long]$comment.live_version
    Assert-True `
        -Condition $comment.comment.native_verified `
        -Message "Word did not verify the native comment"

    $stage = "inspect tokenized native Word review items"
    $review = Invoke-TimedTool `
        -Name "inspect_live_word_review" `
        -Arguments @{
            live_document_id = $documentId
            kind = "comments"
            include_text = $true
            max_text_chars = 200
            limit = 20
        }
    Assert-True `
        -Condition (
            $review.total_count -ge 1 -and
            [bool]$review.items[0].review_token
        ) `
        -Message "Tokenized review inspection did not return the native comment"

    $stage = "enable Track Changes through the review controller"
    $reviewMutation = Invoke-TimedTool `
        -Name "manage_live_word_review" `
        -Arguments @{
            live_document_id = $documentId
            action = "set_track_changes"
            tracking_enabled = $true
            expected_version = $version
            optimize_screen_updates = $true
        }
    $version = [long]$reviewMutation.live_version
    Assert-True `
        -Condition (
            $reviewMutation.mutated -and
            $reviewMutation.result.track_changes
        ) `
        -Message "The review controller did not enable Track Changes"

    $stage = "restore Track Changes through verified manual rollback policy"
    $reviewRestore = Invoke-TimedTool `
        -Name "manage_live_word_review" `
        -Arguments @{
            live_document_id = $documentId
            action = "set_track_changes"
            tracking_enabled = $false
            expected_version = $version
            optimize_screen_updates = $true
        }
    $version = [long]$reviewRestore.live_version
    Assert-True `
        -Condition (
            $reviewRestore.mutated -and
            -not $reviewRestore.result.track_changes
        ) `
        -Message "The review controller did not restore Track Changes"

    $stage = "replace text through native Word ranges"
    $replaced = Invoke-TimedTool `
        -Name "replace_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            search_text = $replaceMarker
            replacement_text = $replacementMarker
            match_case = $true
            whole_word = $true
            replace_all = $true
            track_changes = "preserve"
            max_replacements = 2
            optimize_screen_updates = $true
            expected_version = $version
        }
    $version = [long]$replaced.live_version
    Assert-True `
        -Condition ($replaced.replacements -eq 1) `
        -Message "Native replacement count is not one"

    $stage = "verify the replacement through Word Find"
    $foundReplacement = Invoke-TimedTool `
        -Name "find_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            search_text = $replacementMarker
            match_case = $true
            whole_word = $true
            max_results = 2
        }
    Assert-True `
        -Condition ($foundReplacement.match_count -eq 1) `
        -Message "Replacement readback failed"

    $stage = "insert a native Word table"
    $table = Invoke-TimedTool `
        -Name "insert_live_word_table" `
        -Arguments @{
            live_document_id = $documentId
            rows = @(
                @("Module", "Result", "Notes"),
                @("Tekst", "1", "OK"),
                @("Equations", "1", "OK"),
                @("DOCX/PDF", "2", "OK"),
                @("Total", "", "")
            )
            target = "document_end"
            header_row = $true
            autofit = "window"
            alignment = "center"
            expected_version = $version
        }
    $version = [long]$table.live_version
    Assert-True `
        -Condition $table.table.native_verified `
        -Message "Word did not verify the native table"

    $tableFormulaBatch = @(
        @{
            row = 5
            column = 2
            function = "sum"
            directions = @("above")
            numeric_format = "0"
            replace_existing = $false
        }
    )

    $stage = "preflight one typed native table formula"
    $formulaPreflight = Invoke-TimedTool `
        -Name "preflight_live_word_table_formulas" `
        -Arguments @{ formulas = $tableFormulaBatch }
    Assert-True `
        -Condition (
            $formulaPreflight.valid -and
            -not $formulaPreflight.raw_field_codes_accepted
        ) `
        -Message "Typed table formula preflight failed"

    $stage = "insert and calculate one native table formula"
    $tableFormula = Invoke-TimedTool `
        -Name "insert_live_word_table_formulas" `
        -Arguments @{
            live_document_id = $documentId
            table_index = 1
            formulas = $tableFormulaBatch
            expected_version = $version
            activate = $true
            optimize_screen_updates = $true
            force_update = $true
        }
    $version = [long]$tableFormula.live_version
    Assert-True `
        -Condition (
            $tableFormula.formula_count -eq 1 -and
            $tableFormula.formulas[0].native_verified
        ) `
        -Message "Word did not verify the native table formula"

    $stage = "recalculate every native field in the table"
    $updatedTableFields = Invoke-TimedTool `
        -Name "update_live_word_table_fields" `
        -Arguments @{
            live_document_id = $documentId
            table_index = 1
            expected_version = $version
            activate = $true
            optimize_screen_updates = $true
        }
    $version = [long]$updatedTableFields.live_version
    Assert-True `
        -Condition (
            $updatedTableFields.updated -and
            $updatedTableFields.native_verified
        ) `
        -Message "Word did not verify the table field recalculation"

    $stage = "insert a native bullet list"
    $bulletList = Invoke-TimedTool `
        -Name "insert_live_word_list" `
        -Arguments @{
            live_document_id = $documentId
            items = @(
                "Natywny tekst i formatowanie",
                "Natywne obiekty dokumentu",
                "Native OMath equations"
            )
            list_kind = "bullet"
            target = "document_end"
            formatting = @{
                font_name = "Aptos"
                font_size_pt = 10
            }
            expected_version = $version
        }
    $version = [long]$bulletList.live_version
    Assert-True `
        -Condition $bulletList.list.native_verified `
        -Message "Word did not verify the bullet list"

    $stage = "insert a native numbered list"
    $numberedList = Invoke-TimedTool `
        -Name "insert_live_word_list" `
        -Arguments @{
            live_document_id = $documentId
            items = @(
                "Zapis DOCX",
                "Walidacja Open XML",
                "Eksport przez Word do PDF"
            )
            list_kind = "numbered"
            target = "document_end"
            expected_version = $version
        }
    $version = [long]$numberedList.live_version
    Assert-True `
        -Condition $numberedList.list.native_verified `
        -Message "Word did not verify the numbered list"

    $bookmarkName = "WT_Result_$suffix"
    $bookmarkBatch = @(
        @{
            name = $bookmarkName
            text = "Wynik kontrolny: 42"
            as_new_paragraph = $true
            formatting = @{
                bold = $true
                font_color_rgb = "#2457A6"
            }
        }
    )

    $stage = "preflight one bounded native Word bookmark"
    $bookmarkPreflight = Invoke-TimedTool `
        -Name "preflight_live_word_bookmarks" `
        -Arguments @{ bookmarks = $bookmarkBatch }
    Assert-True `
        -Condition (
            $bookmarkPreflight.valid -and
            -not $bookmarkPreflight.word_attached
        ) `
        -Message "Native bookmark preflight failed"

    $stage = "insert and verify one native Word bookmark"
    $bookmark = Invoke-TimedTool `
        -Name "insert_live_word_bookmarks" `
        -Arguments @{
            live_document_id = $documentId
            bookmarks = $bookmarkBatch
            target = "document_end"
            expected_version = $version
            activate = $true
            optimize_screen_updates = $true
        }
    $version = [long]$bookmark.live_version
    Assert-True `
        -Condition (
            $bookmark.bookmarks.Count -eq 1 -and
            $bookmark.bookmarks[0].native_verified
        ) `
        -Message "Word did not verify the native bookmark"

    $fieldBatch = @(
        @{
            kind = "reference"
            bookmark = $bookmarkName
            hyperlink = $false
            prefix_text = "Internal reference: "
            suffix_text = " "
            as_new_paragraph = $true
        },
        @{
            kind = "sequence"
            identifier = "WTSEQ"
            restart_at = 1
            prefix_text = "Numer sekwencji: "
            as_new_paragraph = $true
        }
    )

    $stage = "preflight allowlisted native Word fields"
    $fieldPreflight = Invoke-TimedTool `
        -Name "preflight_live_word_fields" `
        -Arguments @{ fields = $fieldBatch }
    Assert-True `
        -Condition (
            $fieldPreflight.valid -and
            -not $fieldPreflight.raw_field_codes_accepted
        ) `
        -Message "Allowlisted Word field preflight failed"

    $stage = "insert and update allowlisted native Word fields"
    $fields = Invoke-TimedTool `
        -Name "insert_live_word_fields" `
        -Arguments @{
            live_document_id = $documentId
            fields = $fieldBatch
            target = "document_end"
            expected_version = $version
            activate = $true
            optimize_screen_updates = $true
        }
    $version = [long]$fields.live_version
    Assert-True `
        -Condition (
            $fields.fields.Count -eq 2 -and
            @($fields.fields | Where-Object { $_.native_verified }).Count -eq 2
        ) `
        -Message "Word did not verify every allowlisted native field"

    $stage = "insert a native inline image"
    $image = Invoke-TimedTool `
        -Name "insert_live_word_image" `
        -Arguments @{
            live_document_id = $documentId
            file_path = $sampleImage
            target = "document_end"
            width_points = 190
            lock_aspect_ratio = $true
            alternative_text = "Wykres testowy osadzony natywnie przez WordToolkit"
            title = "WordToolkit full live acceptance image"
            expected_version = $version
        }
    $version = [long]$image.live_version
    Assert-True `
        -Condition $image.image.native_verified `
        -Message "Word did not verify the inline image"

    $stage = "insert a native footnote"
    $footnote = Invoke-TimedTool `
        -Name "insert_live_word_note" `
        -Arguments @{
            live_document_id = $documentId
            kind = "footnote"
            text = "Native footnote created through the Word Footnotes collection."
            target = "document_end"
            expected_version = $version
        }
    $version = [long]$footnote.live_version
    Assert-True `
        -Condition $footnote.note.native_verified `
        -Message "Word did not verify the footnote"

    $stage = "insert a native endnote"
    $endnote = Invoke-TimedTool `
        -Name "insert_live_word_note" `
        -Arguments @{
            live_document_id = $documentId
            kind = "endnote"
            text = "Native endnote created through the Word Endnotes collection."
            target = "document_end"
            custom_mark = "E"
            expected_version = $version
        }
    $version = [long]$endnote.live_version
    Assert-True `
        -Condition $endnote.note.native_verified `
        -Message "Word did not verify the endnote"

    $headerFooterCases = @(
        @{
            kind = "header"
            variant = "primary"
            text = "WORDTOOLKIT - FULL LIVE TEST"
            alignment = "center"
        },
        @{
            kind = "header"
            variant = "first_page"
            text = "FIRST PAGE - NATIVE WORD"
            alignment = "left"
        },
        @{
            kind = "header"
            variant = "even_pages"
            text = "EVEN PAGE - TEST"
            alignment = "right"
        },
        @{
            kind = "footer"
            variant = "primary"
            text = "Model -> MCP -> .NET -> COM -> Word"
            alignment = "center"
        },
        @{
            kind = "footer"
            variant = "first_page"
            text = "Full automation without Python"
            alignment = "left"
        },
        @{
            kind = "footer"
            variant = "even_pages"
            text = "WordToolkit native runtime"
            alignment = "right"
        }
    )
    foreach ($case in $headerFooterCases) {
        $stage = "set $($case.kind) $($case.variant)"
        $headerFooter = Invoke-TimedTool `
            -Name "set_live_word_header_footer" `
            -Arguments @{
                live_document_id = $documentId
                section_index = 1
                kind = $case.kind
                variant = $case.variant
                text = $case.text
                enabled = $true
                link_to_previous = $false
                formatting = @{
                    font_name = "Aptos"
                    font_size_pt = 8
                    font_color_rgb = "#666666"
                    paragraph_alignment = $case.alignment
                }
                expected_version = $version
            }
        $version = [long]$headerFooter.live_version
        Assert-True `
            -Condition $headerFooter.header_footer.native_verified `
            -Message "Word did not verify $($case.kind) $($case.variant)"
    }

    $mathMl = @'
<math xmlns="http://www.w3.org/1998/Math/MathML"><mi mathvariant="normal">a</mi><mo>+</mo><mi mathvariant="bold">b</mi><mo>+</mo><mi mathvariant="italic">c</mi><mo>+</mo><mi mathvariant="bold-italic">d</mi><mo>+</mo><mi mathvariant="double-struck">R</mi><mo>+</mo><mi mathvariant="script">A</mi><mo>+</mo><mi mathvariant="bold-script">B</mi><mo>+</mo><mi mathvariant="fraktur">C</mi><mo>+</mo><mi mathvariant="bold-fraktur">D</mi><mo>+</mo><mi mathvariant="sans-serif">E</mi><mo>+</mo><mi mathvariant="bold-sans-serif">F</mi><mo>+</mo><mi mathvariant="sans-serif-italic">G</mi><mo>+</mo><mi mathvariant="sans-serif-bold-italic">H</mi><mo>+</mo><mi mathvariant="monospace">I</mi></math>
'@
    $omml = @'
<m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math" xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><m:f><m:fPr><m:ctrlPr><w:rPr><w:i/></w:rPr></m:ctrlPr></m:fPr><m:num><m:r><m:rPr><m:sty m:val="p"/></m:rPr><m:t>x</m:t></m:r><m:r><m:rPr><m:sty m:val="b"/></m:rPr><m:t>u</m:t></m:r></m:num><m:den><m:r><m:rPr><m:sty m:val="i"/></m:rPr><m:t>y</m:t></m:r><m:r><m:rPr><m:sty m:val="bi"/></m:rPr><m:t>v</m:t></m:r></m:den></m:f></m:oMath>
'@

    $stage = "verify compact lazy equation preflight"
    $compactPreflightResponse = Invoke-Mcp `
        -Method "tools/call" `
        -Params @{
            name = "execute_wordtoolkit_action"
            arguments = @{
                action = "preflight_live_word_equations"
                arguments = @{
                    equations = @(
                        @{
                            value = "\frac{\mathrm{d}y}{\mathrm{d}x}=3x^2"
                            input_format = "latex"
                            display = $true
                        }
                    )
                }
            }
        }
    Assert-True `
        -Condition (-not $compactPreflightResponse.result.isError) `
        -Message "Compact equation preflight failed"
    $compactPreflight = $compactPreflightResponse.result.structuredContent.data
    $compactPreflightCharacters = (
        $compactPreflight | ConvertTo-Json -Depth 30 -Compress
    ).Length
    Assert-True `
        -Condition (
            -not $compactPreflight.word_linear_returned -and
            $compactPreflightCharacters -lt 600 -and
            $compactPreflight.equations[0].PSObject.Properties.Name -notcontains "word_linear"
        ) `
        -Message "Compact equation preflight leaked linear math or exceeded 599 characters"

    $stage = "preflight all four equation input formats"
    $preflight = Invoke-TimedTool `
        -Name "preflight_live_word_equations" `
        -Arguments @{
            equations = @(
                @{
                    value = "\frac{x^2+1}{\sqrt{y}}"
                    input_format = "latex"
                    display = $true
                },
                @{
                    value = "sum_(i=1)^n i^2"
                    input_format = "unicodemath"
                    display = $true
                },
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
    Assert-True `
        -Condition (
            $preflight.valid -and
            $preflight.equation_count -eq 4 -and
            $preflight.equations[2].formatting_region_count -eq 14 -and
            $preflight.equations[2].formatting_regions.plain -eq 6 -and
            $preflight.equations[2].formatting_regions.bold -eq 4 -and
            $preflight.equations[2].formatting_regions.italic -eq 2 -and
            $preflight.equations[2].formatting_regions.bold_italic -eq 2 -and
            $preflight.equations[3].formatting_region_count -eq 5 -and
            $preflight.equations[3].formatting_regions.first_control -eq 1
        ) `
        -Message "Equation preflight lost MathML or OMML native style scopes"

    $stage = "insert one native LaTeX equation"
    $singleEquation = Invoke-TimedTool `
        -Name "insert_live_word_equation" `
        -Arguments @{
            live_document_id = $documentId
            value = "\frac{\mathrm{d}y}{\mathrm{d}x}=3x^2"
            input_format = "latex"
            display = $true
            verify_readback = $true
            target = "document_end"
            expected_version = $version
        }
    $version = [long]$singleEquation.live_version
    Assert-True `
        -Condition ($singleEquation.equation_operation_count -eq 1) `
        -Message "Single native equation insertion failed"

    $stage = "insert UnicodeMath, MathML and OMML in one batch"
    $equationBatch = Invoke-TimedTool `
        -Name "insert_live_word_equations_batch" `
        -Arguments @{
            live_document_id = $documentId
            equations = @(
                @{
                    value = (
                        ([char]0x2211).ToString() +
                        "_(i=1)^(n)" +
                        ([char]0x2592).ToString() +
                        "i^(2)=(n(n+1)(2n+1))/(6)"
                    )
                    input_format = "unicodemath"
                    display = $true
                },
                @{
                    value = $mathMl
                    input_format = "mathml"
                    display = $true
                },
                @{
                    value = $omml
                    input_format = "omml"
                    display = $true
                },
                @{
                    value = "\lim_{x\to0}\frac{\sin x}{x}=1"
                    input_format = "latex"
                    display = $true
                    verify_readback = $true
                },
                @{
                    value = "\min_{x\in S}f(x)+\max_{x\in S}g(x)"
                    input_format = "latex"
                    display = $true
                    verify_readback = $true
                },
                @{
                    value = "\frac{u\cdot v}{\left\|u\right\|\left\|v\right\|}"
                    input_format = "latex"
                    display = $true
                    verify_readback = $true
                },
                @{
                    value = "f(x)=\begin{cases}x^2&\text{gdy }x\ge0\\-x&\text{gdy }x<0\end{cases}"
                    input_format = "latex"
                    display = $true
                    verify_readback = $true
                },
                @{
                    value = "\mathbb{R}+\mathcal{F}+\mathfrak{R}+\mathsf{A}+\mathtt{x}"
                    input_format = "latex"
                    display = $true
                    verify_readback = $true
                },
                @{
                    value = "\mathbf{x+\boldsymbol{y}}"
                    input_format = "latex"
                    display = $true
                },
                @{
                    value = "\boldsymbol{\frac{\alpha+\beta}{\gamma}}"
                    input_format = "latex"
                    display = $true
                }
            )
            expected_version = $version
        }
    $version = [long]$equationBatch.live_version
    Assert-True `
        -Condition (
            $equationBatch.equation_operation_count -eq 10 -and
            $equationBatch.operations[1].equation.native_style_verified -and
            $equationBatch.operations[1].equation.formatting.region_count -eq 14 -and
            $equationBatch.operations[1].equation.formatting.plain_run_count -ge 6 -and
            $equationBatch.operations[1].equation.formatting.bold_run_count -ge 4 -and
            $equationBatch.operations[1].equation.formatting.italic_run_count -ge 2 -and
            $equationBatch.operations[1].equation.formatting.bold_italic_run_count -ge 2 -and
            $equationBatch.operations[2].equation.native_style_verified -and
            $equationBatch.operations[2].equation.formatting.plain_run_count -ge 1 -and
            $equationBatch.operations[2].equation.formatting.bold_run_count -ge 1 -and
            $equationBatch.operations[2].equation.formatting.italic_run_count -ge 1 -and
            $equationBatch.operations[2].equation.formatting.bold_italic_run_count -ge 1 -and
            $equationBatch.operations[2].equation.formatting.italic_control_count -ge 1 -and
            $equationBatch.operations[8].equation.native_style_verified -and
            $equationBatch.operations[8].equation.formatting.region_count -eq 2 -and
            $equationBatch.operations[9].equation.native_style_verified -and
            $equationBatch.operations[9].equation.formatting.bold_italic_run_count -ge 2 -and
            $equationBatch.operations[9].equation.formatting.bold_italic_control_count -ge 1
        ) `
        -Message "Native equation batch lost MathML, OMML or LaTeX equation styles"

    $stage = "map every supported native Word structure collection"
    $structureMap = Invoke-TimedTool `
        -Name "map_live_word_structures" `
        -Arguments @{
            live_document_id = $documentId
            include_type_histograms = $true
            adaptive_type_histograms = $true
            max_type_items = 500
        }
    Assert-True `
        -Condition (
            $structureMap.inspectable_structures.Count -eq 23 -and
            $structureMap.structures.tables -ge 1 -and
            $structureMap.structures.bookmarks -ge 1 -and
            $structureMap.structures.fields -ge 3 -and
            -not $structureMap.content_returned
        ) `
        -Message "The native Word structure map is incomplete"

    $stage = "inspect bounded semantic bookmark metadata"
    $structureItems = Invoke-TimedTool `
        -Name "inspect_live_word_structure_items" `
        -Arguments @{
            live_document_id = $documentId
            structure = "bookmarks"
            limit = 20
            include_text = $false
            adaptive_property_probing = $true
        }
    Assert-True `
        -Condition (
            $structureItems.available -and
            $structureItems.returned_count -ge 1 -and
            -not $structureItems.external_addresses_returned
        ) `
        -Message "Bounded native structure inspection failed"

    $stage = "read privacy-preserving equation learning"
    $equationLearning = Invoke-TimedTool `
        -Name "inspect_live_word_equation_learning" `
        -Arguments @{}
    Assert-True `
        -Condition (
            $equationLearning.observation_count -ge 5 -and
            -not $equationLearning.formula_text_stored
        ) `
        -Message "Equation learning did not retain aggregate native outcomes"

    $stage = "read privacy-preserving structure learning"
    $structureLearning = Invoke-TimedTool `
        -Name "inspect_live_word_structure_learning" `
        -Arguments @{}
    Assert-True `
        -Condition (
            $structureLearning.observation_count -ge 23 -and
            $structureLearning.inspection_observation_count -ge 1 -and
            -not $structureLearning.document_counts_stored
        ) `
        -Message "Structure learning did not retain aggregate scan outcomes"

    $stage = "diagnose bounded live Word pagination risks"
    $layoutDiagnosis = Invoke-TimedTool `
        -Name "diagnose_live_word_layout" `
        -Arguments @{
            live_document_id = $documentId
            max_paragraphs = 5000
            max_issues = 200
            keep_with_next_threshold = 5
            long_heading_chars = 100
            long_keep_together_chars = 1200
        }
    Assert-True `
        -Condition (
            $layoutDiagnosis.scanned_paragraphs -gt 0 -and
            -not $layoutDiagnosis.document_text_returned
        ) `
        -Message "Bounded live layout diagnosis failed"

    $stage = "save the live DOCX"
    $saved = Invoke-TimedTool `
        -Name "save_live_word_document" `
        -Arguments @{
            live_document_id = $documentId
            expected_version = $version
        }
    Assert-True -Condition $saved.saved -Message "Word did not save the DOCX"

    $stage = "validate the saved DOCX snapshot"
    $validated = Invoke-TimedTool `
        -Name "validate_live_word_document" `
        -Arguments @{ live_document_id = $documentId }
    Assert-True `
        -Condition $validated.validation.valid `
        -Message "Microsoft Open XML SDK validation failed"

    $stage = "export the current real Word state to PDF"
    $pdf = Invoke-TimedTool `
        -Name "export_live_word_pdf" `
        -Arguments @{
            live_document_id = $documentId
            output_path = $pdfPath
            overwrite = $false
            optimize_for = "print"
            bookmarks = "headings"
            include_document_properties = $true
            pdf_a = $false
        }
    Assert-True `
        -Condition ($pdf.exported -and $pdf.bytes -gt 0) `
        -Message "Word produced an empty PDF"

    $stage = "inspect native structures before close"
    $beforeClose = Invoke-TimedTool `
        -Name "inspect_live_word_document" `
        -Arguments @{ live_document_id = $documentId }
    Assert-True `
        -Condition (
            $beforeClose.document.comment_count -ge 1 -and
            $beforeClose.document.footnote_count -ge 1 -and
            $beforeClose.document.endnote_count -ge 1 -and
            $beforeClose.document.inline_image_count -ge 1 -and
            $beforeClose.document.table_count -ge 1 -and
            $beforeClose.document.equation_count -ge 5
        ) `
        -Message (
            "One or more native Word structures are missing before close: " +
            "comments=$($beforeClose.document.comment_count), " +
            "footnotes=$($beforeClose.document.footnote_count), " +
            "endnotes=$($beforeClose.document.endnote_count), " +
            "images=$($beforeClose.document.inline_image_count), " +
            "tables=$($beforeClose.document.table_count), " +
            "equations=$($beforeClose.document.equation_count)"
        )

    $stage = "close only the saved acceptance document"
    [void](Invoke-TimedTool `
        -Name "close_live_word_document" `
        -Arguments @{
            live_document_id = $documentId
            save_changes = "save"
            expected_version = $version
        })
    $documentId = ""
    $documentOpen = $false

    $stage = "open the exact existing DOCX through Word"
    $opened = Invoke-TimedTool `
        -Name "open_live_word_document" `
        -Arguments @{
            file_path = $documentPath
            read_only = $false
            activate = $true
            visible = $true
            add_to_recent_files = $false
            open_and_repair = $false
            launch_if_needed = $false
        }
    $documentId = $opened.live_document_id
    $version = [long]$opened.live_version
    $documentOpen = $true
    Assert-True `
        -Condition (
            $opened.document.comment_count -ge 1 -and
            $opened.document.footnote_count -ge 1 -and
            $opened.document.endnote_count -ge 1 -and
            $opened.document.inline_image_count -ge 1 -and
            $opened.document.table_count -ge 1 -and
            $opened.document.equation_count -ge 5
        ) `
        -Message "Reopened DOCX lost one or more native structures"

    $stage = "disconnect without closing the reopened document"
    [void](Invoke-TimedTool `
        -Name "disconnect_live_word_document" `
        -Arguments @{ live_document_id = $documentId })
    $documentId = ""
    $documentOpen = $false

    $stage = "connect again by the exact live full path"
    $connected = Invoke-TimedTool `
        -Name "connect_live_word_document" `
        -Arguments @{
            full_path = $documentPath
            use_active = $false
            activate = $true
        }
    $documentId = $connected.live_document_id
    $version = [long]$connected.live_version
    $documentOpen = $true

    $stage = "inspect after exact live reconnect"
    $afterConnect = Invoke-TimedTool `
        -Name "inspect_live_word_document" `
        -Arguments @{ live_document_id = $documentId }
    Assert-True `
        -Condition ($afterConnect.document.full_name -eq $documentPath) `
        -Message "Exact live reconnect targeted the wrong document"

    $stage = "close the reconnected test document"
    [void](Invoke-TimedTool `
        -Name "close_live_word_document" `
        -Arguments @{
            live_document_id = $documentId
            save_changes = "save"
            expected_version = $version
        })
    $documentId = ""
    $documentOpen = $false

    $stage = "open the final result for last live verification"
    $finalOpen = Invoke-TimedTool `
        -Name "open_live_word_document" `
        -Arguments @{
            file_path = $documentPath
            read_only = $false
            activate = $true
            visible = $true
            add_to_recent_files = $false
            open_and_repair = $false
            launch_if_needed = $false
        }
    $documentId = $finalOpen.live_document_id
    $version = [long]$finalOpen.live_version
    $documentOpen = $true

    $stage = "final list of real Word documents"
    $listedAfter = Invoke-TimedTool `
        -Name "list_live_word_documents" `
        -Arguments @{}
    $finalMatch = @(
        $listedAfter.documents |
            Where-Object { $_.full_name -eq $documentPath -and $_.active }
    )
    Assert-True `
        -Condition ($finalMatch.Count -eq 1) `
        -Message "The final acceptance DOCX is not active in Word"

    $stage = "close the final verified acceptance document"
    [void](Invoke-TimedTool `
        -Name "close_live_word_document" `
        -Arguments @{
            live_document_id = $documentId
            save_changes = "save"
            expected_version = $version
        })
    $documentId = ""
    $documentOpen = $false

    $stage = "verify acceptance document cleanup"
    $listedAfterClose = Invoke-TimedTool `
        -Name "list_live_word_documents" `
        -Arguments @{}
    $remainingAcceptanceDocuments = @(
        $listedAfterClose.documents |
            Where-Object { $_.full_name -eq $documentPath }
    )
    Assert-True `
        -Condition ($remainingAcceptanceDocuments.Count -eq 0) `
        -Message "The final acceptance DOCX remained open after explicit close"

    if (-not $wordWasRunning -and @($listedAfterClose.documents).Count -eq 0) {
        $stage = "quit only the Word application started by this acceptance run"
        [void](Invoke-TimedTool `
            -Name "quit_word_application" `
            -Arguments @{
                save_changes = "discard_all"
                confirm = $true
            })
        $actualWordQuitSkipped = $false
        $actualWordQuitSkipReason = ""
    }
    else {
        $actualWordQuitSkipped = $true
        $actualWordQuitSkipReason = if ($wordWasRunning) {
            "Word was already running before acceptance; the user-owned application was preserved"
        }
        else {
            "Other Word documents remained open; application quit was not authorized"
        }
    }

    $totalWatch.Stop()
    $toolResultRows = foreach ($name in $nativeTools) {
        $entry = $toolStats[$name]
        [ordered]@{
            name = $name
            status = $entry.status
            calls = $entry.calls
            total_ms = $entry.total_ms
            average_ms = if ($entry.calls -gt 0) {
                [Math]::Round($entry.total_ms / $entry.calls, 3)
            }
            else {
                0.0
            }
            max_ms = $entry.max_ms
            note = $entry.note
        }
    }
    $positivePassed = @(
        $toolResultRows |
            Where-Object { $_.status -eq "passed" }
    ).Count
    $notPassed = @(
        $toolResultRows |
            Where-Object { $_.status -notin @("passed", "guard_passed") }
    )
    Assert-True `
        -Condition ($notPassed.Count -eq 0) `
        -Message "$($notPassed.Count) installed tools were not exercised successfully"

    $report.total_seconds = [Math]::Round($totalWatch.Elapsed.TotalSeconds, 3)
    $report.total_mcp_requests = $requestId
    $report.exposed_tool_count = $script:catalogNames.Count
    $report.available_action_count = 77
    $report.exercised_live_action_count = $nativeTools.Count
    $report.positive_tools_passed = $positivePassed
    $report.safety_guard_tools_passed = $safetyGuardPassCount
    $report.selection_setup_ms = [Math]::Round($selectionSetupMs, 3)
    $report.compact_equation_preflight_characters = $compactPreflightCharacters
    $report.preexisting_document_count = $preexistingDocuments.Count
    $report.unrelated_dirty_documents_protected = $unrelatedDirtyDocuments.Count
    $report.actual_word_quit_skipped = $actualWordQuitSkipped
    $report.actual_word_quit_skip_reason = $actualWordQuitSkipReason
    $report.document = [ordered]@{
        paragraphs = $finalOpen.document.paragraph_count
        tables = $finalOpen.document.table_count
        equations = $finalOpen.document.equation_count
        comments = $finalOpen.document.comment_count
        footnotes = $finalOpen.document.footnote_count
        endnotes = $finalOpen.document.endnote_count
        inline_images = $finalOpen.document.inline_image_count
        sections = $finalOpen.document.section_count
        saved = $finalOpen.document.saved
        active = $false
        left_open_in_word = $false
    }
    $report.pdf_bytes = [long]$pdf.bytes
    $report.openxml_valid = $validated.validation.valid
    $report.close_open_reconnect_passed = $true
    $report.tool_results = $toolResultRows
    $report.passed = $true
}
catch {
    if ($totalWatch.IsRunning) {
        $totalWatch.Stop()
    }
    $failure = $_
    $report.total_seconds = [Math]::Round($totalWatch.Elapsed.TotalSeconds, 3)
    $report.failed_stage = $stage
    $report.error = $_.Exception.Message
    $report.tool_results = foreach ($name in $nativeTools) {
        $entry = $toolStats[$name]
        [ordered]@{
            name = $name
            status = $entry.status
            calls = $entry.calls
            total_ms = $entry.total_ms
            max_ms = $entry.max_ms
            note = $entry.note
        }
    }
    $report.passed = $false
}
finally {
    if ($documentId -and $documentOpen) {
        try {
            [void](Invoke-TimedTool `
                -Name "close_live_word_document" `
                -Arguments @{
                    live_document_id = $documentId
                    save_changes = "discard"
                    expected_version = $version
                })
        }
        catch {
            try {
                [void](Invoke-TimedTool `
                    -Name "disconnect_live_word_document" `
                    -Arguments @{ live_document_id = $documentId })
            }
            catch {
                # Preserve the original acceptance failure.
            }
        }
    }

    $mcpInput.Close()
    if (-not $process.WaitForExit(5000)) {
        $process.Kill()
    }
}

$report | ConvertTo-Json -Depth 40
if ($failure) {
    exit 1
}
