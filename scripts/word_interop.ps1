param(
    [Parameter(Mandatory = $false)]
    [string]$InputDirectory = "examples/generated"
)

$ErrorActionPreference = "Stop"
$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
$failures = @()

try {
    Get-ChildItem -Path $InputDirectory -Filter *.docx | ForEach-Object {
        $inputPath = $_.FullName
        $outputPath = Join-Path $_.DirectoryName ($_.BaseName + ".word-roundtrip.docx")
        try {
            $document = $word.Documents.Open($inputPath, $false, $true, $false)
            $document.SaveAs2($outputPath, 16)
            $document.Close($false)
            if (-not (Test-Path $outputPath) -or (Get-Item $outputPath).Length -lt 1000) {
                throw "Word did not produce a valid-size round-trip file"
            }
            Write-Host "PASS $($_.Name)"
        }
        catch {
            $failures += "$($_.Name): $($_.Exception.Message)"
        }
    }
}
finally {
    $word.Quit()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

