param(
    [string]$Configuration = "Release",
    [string]$RuntimeExecutable = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$executable = if ($RuntimeExecutable) {
    [IO.Path]::GetFullPath($RuntimeExecutable)
}
else {
    Join-Path `
        $projectRoot `
        "WordToolkit.Native\bin\$Configuration\net8.0-windows\wordtoolkit-native.exe"
}
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Native executable not found: $executable"
}

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
    } | ConvertTo-Json -Depth 30 -Compress
    $process.StandardInput.WriteLine($request)
    $process.StandardInput.Flush()
    $line = $process.StandardOutput.ReadLine()
    if (-not $line) {
        throw "Native MCP exited: $($process.StandardError.ReadToEnd())"
    }
    $response = $line | ConvertFrom-Json -Depth 30
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
        -Params @{ name = $Name; arguments = $Arguments }
    if ($response.result.isError) {
        throw (
            $response.result.structuredContent.error |
                ConvertTo-Json -Depth 20 -Compress
        )
    }
    return $response.result.structuredContent.data
}

function Undo-One {
    param(
        [string]$DocumentId,
        [long]$Version
    )
    $inspection = Invoke-Tool `
        -Name "inspect_live_word_undo" `
        -Arguments @{
            live_document_id = $DocumentId
            max_entries = 3
        }
    if (-not $inspection.wordtoolkit_undo_eligible) {
        throw "Expected a WordToolkit Undo entry, got: $($inspection.top_entry)"
    }
    return Invoke-Tool `
        -Name "undo_live_word_operation" `
        -Arguments @{
            live_document_id = $DocumentId
            expected_version = $Version
            undo_token = $inspection.undo_token
        }
}

$outstandingMutations = 0
$documentId = ""
$version = 0
$stage = "initialize"
$failure = $null
$report = [ordered]@{
    runtime = "dotnet-native"
    python_used = $false
    transport = "real MCP STDIO"
}

try {
    [void](Invoke-Mcp `
        -Method "initialize" `
        -Params @{
            protocolVersion = "2025-06-18"
            capabilities = @{}
            clientInfo = @{
                name = "wordtoolkit-native-live-acceptance"
                version = "1"
            }
        })
    $children = @(
        Get-CimInstance Win32_Process |
            Where-Object { $_.ParentProcessId -eq $process.Id } |
            Select-Object -ExpandProperty Name
    )
    $report.child_processes = $children
    $report.python_children = @(
        $children |
            Where-Object { $_ -match "^(python|pythonw|uv)(\.exe)?$" }
    ).Count

    $stage = "connect"
    $connected = Invoke-Tool `
        -Name "connect_live_word_document" `
        -Arguments @{ use_active = $true; activate = $true }
    $documentId = $connected.live_document_id
    $version = [long]$connected.live_version
    $report.document = $connected.document.name

    $stage = "insert probe"
    $marker = "WT_NATIVE_PROBE_7F3A91"
    $inserted = Invoke-Tool `
        -Name "insert_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            text = $marker
            target = "document_end"
            as_new_paragraph = $true
            activate = $true
            expected_version = $version
        }
    $outstandingMutations++
    $version = [long]$inserted.live_version

    $stage = "find probe"
    $found = Invoke-Tool `
        -Name "find_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            search_text = $marker
            match_case = $true
            whole_word = $true
            context_chars = 12
            max_results = 3
        }
    if ($found.match_count -ne 1) {
        throw "Find returned $($found.match_count), expected 1"
    }

    $stage = "replace probe"
    $replacement = "WT_NATIVE_REPLACED_7F3A91"
    $replaced = Invoke-Tool `
        -Name "replace_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            search_text = $marker
            replacement_text = $replacement
            match_case = $true
            whole_word = $true
            replace_all = $true
            max_replacements = 3
            expected_version = $version
        }
    $outstandingMutations++
    $version = [long]$replaced.live_version
    if ($replaced.replacements -ne 1) {
        throw "Replace returned $($replaced.replacements), expected 1"
    }

    $stage = "find replacement"
    $foundReplacement = Invoke-Tool `
        -Name "find_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            search_text = $replacement
            match_case = $true
            whole_word = $true
            context_chars = 12
            max_results = 3
        }
    if ($foundReplacement.match_count -ne 1) {
        throw "Replacement readback failed"
    }

    $stage = "undo replacement"
    $undone = Undo-One -DocumentId $documentId -Version $version
    $outstandingMutations--
    $version = [long]$undone.live_version
    $stage = "undo probe"
    $undone = Undo-One -DocumentId $documentId -Version $version
    $outstandingMutations--
    $version = [long]$undone.live_version
    $report.find_replace = [ordered]@{
        passed = $true
        insert_ms = $inserted.performance.total_ms
        find_ms = $found.performance.total_ms
        replace_ms = $replaced.performance.total_ms
    }

    $stage = "table"
    $table = Invoke-Tool `
        -Name "insert_live_word_table" `
        -Arguments @{
            live_document_id = $documentId
            rows = @(
                @("Kolumna A", "Kolumna B"),
                @("1", "2"),
                @("3", "4")
            )
            target = "document_end"
            header_row = $true
            autofit = "window"
            alignment = "center"
            expected_version = $version
        }
    $outstandingMutations++
    $version = [long]$table.live_version
    if (-not $table.table.native_verified) {
        throw "Table was not verified by Word"
    }
    $stage = "undo table"
    $undone = Undo-One -DocumentId $documentId -Version $version
    $outstandingMutations--
    $version = [long]$undone.live_version
    $report.table = [ordered]@{
        passed = $true
        milliseconds = $table.performance.total_ms
        rows = $table.table.rows
        columns = $table.table.columns
    }

    $stage = "list"
    $list = Invoke-Tool `
        -Name "insert_live_word_list" `
        -Arguments @{
            live_document_id = $documentId
            items = @(
                "Pierwszy element",
                "Drugi element",
                "Trzeci element"
            )
            list_kind = "numbered"
            target = "document_end"
            expected_version = $version
        }
    $outstandingMutations++
    $version = [long]$list.live_version
    if (-not $list.list.native_verified) {
        throw "List was not verified by Word"
    }
    $stage = "undo list"
    $undone = Undo-One -DocumentId $documentId -Version $version
    $outstandingMutations--
    $version = [long]$undone.live_version
    $report.list = [ordered]@{
        passed = $true
        milliseconds = $list.performance.total_ms
        items = $list.list.item_count
    }

    $stage = "equation"
    $equation = Invoke-Tool `
        -Name "insert_live_word_equation" `
        -Arguments @{
            live_document_id = $documentId
            value = "\frac{x^2+1}{\sqrt[3]{y}}+\sum_{i=1}^{n} i^2"
            input_format = "latex"
            display = $true
            target = "document_end"
            expected_version = $version
        }
    $outstandingMutations++
    $version = [long]$equation.live_version
    $stage = "undo equation"
    $undone = Undo-One -DocumentId $documentId -Version $version
    $outstandingMutations--
    $version = [long]$undone.live_version
    $report.equation = [ordered]@{
        passed = $true
        milliseconds = $equation.performance.total_ms
        equation_operations = $equation.equation_operation_count
    }

    $report.final_version = $version
    $report.document_restored = $true
    $report.passed = $true
}
catch {
    $failure = $_
    $report.failed_stage = $stage
    $report.error = $_.Exception.Message
    $report.passed = $false
}
finally {
    while ($outstandingMutations -gt 0 -and $documentId) {
        try {
            $undone = Undo-One -DocumentId $documentId -Version $version
            $version = [long]$undone.live_version
            $outstandingMutations--
        }
        catch {
            break
        }
    }
    if ($documentId) {
        try {
            [void](Invoke-Tool `
                -Name "disconnect_live_word_document" `
                -Arguments @{ live_document_id = $documentId })
        }
        catch {
            # The mutation cleanup above is the safety boundary.
        }
    }
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(5000)) {
        $process.Kill($true)
    }
    $report.outstanding_mutations = $outstandingMutations
}

$report | ConvertTo-Json -Depth 20
if ($failure -or $outstandingMutations -gt 0) {
    exit 1
}
