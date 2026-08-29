param(
    [Parameter(Mandatory = $true)]
    [string]$RuntimeExecutable
)

$ErrorActionPreference = "Stop"
$runtime = [IO.Path]::GetFullPath($RuntimeExecutable)
if (-not (Test-Path -LiteralPath $runtime -PathType Leaf)) {
    throw "Native executable not found: $runtime"
}

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
$documentId = ""
$disconnected = $false
$stage = "initialize"
$failure = $null

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

function Invoke-FullTool {
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
                ConvertTo-Json -Depth 20 -Compress
        )
    }
    return $response.result.structuredContent.data
}

$report = [ordered]@{
    runtime = "dotnet-native"
    transport = "real MCP STDIO"
    lifecycle = "scratch"
    active_user_document_targeted = $false
}

try {
    $initialized = Invoke-Mcp `
        -Method "initialize" `
        -Params @{
            protocolVersion = "2025-06-18"
            capabilities = @{}
            clientInfo = @{
                name = "wordtoolkit-character-formatting-smoke"
                version = "1"
            }
        }
    $report.server_version = $initialized.result.serverInfo.version

    $stage = "list tools"
    $tools = Invoke-Mcp -Method "tools/list" -Params @{}
    if (@($tools.result.tools).Count -ne 17) {
        throw "Expected 17 public tools, received $(@($tools.result.tools).Count)"
    }
    $report.public_tools = @($tools.result.tools).Count

    $stage = "create scratch document"
    $created = Invoke-Tool `
        -Name "create_live_word_document" `
        -Arguments @{
            lifecycle = "scratch"
            activate = $false
        }
    $documentId = [string]$created.live_document_id
    $version = [long]$created.live_version

    $stage = "apply character formatting"
    $applied = Invoke-FullTool `
        -Name "apply_live_word_operations" `
        -Arguments @{
            live_document_id = $documentId
            expected_version = $version
            activate = $false
            operations = @(
                @{
                    type = "text"
                    runs = @(
                        @{
                            text = "subscript"
                            formatting = @{
                                subscript = $true
                                underline_style = "double"
                                underline_color = "#C00000"
                            }
                        },
                        @{
                            text = " superscript"
                            formatting = @{
                                superscript = $true
                                underline_style = "wavy_double"
                                font_color_rgb = "#0070C0"
                            }
                        },
                        @{
                            text = " OpenType 0123"
                            formatting = @{
                                ligatures = "standard_contextual"
                                number_form = "lining"
                                number_spacing = "tabular"
                                stylistic_sets = @(1)
                                contextual_alternates = $true
                            }
                        }
                    )
                    as_new_paragraph = $true
                    formatting = @{
                        font_name = "Aptos"
                        font_size_pt = 14
                    }
                },
                @{
                    type = "text"
                    runs = @(
                        @{
                            text = "clear proof"
                            formatting = @{
                                clear_character_formatting = $true
                                bold = $true
                            }
                        }
                    )
                    as_new_paragraph = $true
                    formatting = @{
                        font_size_pt = 18
                        italic = $true
                        underline_style = "wavy_double"
                        highlight_color_index = 7
                    }
                }
            )
        }
    $version = [long]$applied.live_version
    if (@($applied.operations).Count -ne 2) {
        throw "Expected two applied text operations"
    }
    foreach ($operation in @($applied.operations)) {
        if (-not $operation.native_formatting_verified) {
            throw "Packaged runtime did not verify native formatting"
        }
    }

    $stage = "inspect scratch document"
    $inspected = Invoke-Tool `
        -Name "inspect_live_word_document" `
        -Arguments @{ live_document_id = $documentId }
    if ([long]$inspected.live_version -ne $version) {
        throw "Live version drifted after inspection"
    }
    if ([int]$inspected.document.paragraph_count -lt 2) {
        throw "Scratch document did not retain the formatted paragraphs"
    }

    $stage = "disconnect scratch document"
    [void](Invoke-Tool `
        -Name "disconnect_live_word_document" `
        -Arguments @{ live_document_id = $documentId })
    $disconnected = $true
    $report.live_version = $version
    $report.operations = @($applied.operations).Count
    $report.native_formatting_verified = $true
    $report.scratch_closed_without_save = $true
    $report.passed = $true
}
catch {
    $failure = $_
    $report.failed_stage = $stage
    $report.error = $_.Exception.Message
    $report.passed = $false
}
finally {
    if ($documentId -and -not $disconnected -and -not $process.HasExited) {
        try {
            [void](Invoke-Tool `
                -Name "disconnect_live_word_document" `
                -Arguments @{ live_document_id = $documentId })
        }
        catch {
            $report.cleanup_error = $_.Exception.Message
        }
    }
    try {
        $process.StandardInput.Close()
    }
    catch {
    }
    if (-not $process.WaitForExit(10000)) {
        $process.Kill($true)
        $report.process_killed = $true
    }
    $stderr = $process.StandardError.ReadToEnd()
    if ($stderr) {
        $report.stderr = $stderr.Substring(0, [Math]::Min(2000, $stderr.Length))
    }
    $process.Dispose()
}

$report | ConvertTo-Json -Depth 10
if ($failure -or -not $report.passed) {
    exit 1
}
