param(
    [string]$Output = "",
    [string]$Archive = "",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dist = [IO.Path]::GetFullPath((Join-Path $root "dist"))
$pluginSource = Join-Path $root "plugin\wordtoolkit"
$manifestPath = Join-Path $pluginSource ".codex-plugin\plugin.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if (-not $Output) {
    $Output = Join-Path $dist "wordtoolkit"
}
if (-not $Archive) {
    $Archive = Join-Path $dist "WordToolkit-$($manifest.version)-native-win-x64.zip"
}

function Assert-ChildPath {
    param(
        [string]$Root,
        [string]$Candidate,
        [switch]$AllowFile
    )
    $resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $resolvedCandidate = [IO.Path]::GetFullPath($Candidate)
    if (-not $resolvedCandidate.StartsWith(
        $resolvedRoot,
        [StringComparison]::OrdinalIgnoreCase
    )) {
        throw "Build path escapes dist: $resolvedCandidate"
    }
    if (-not $AllowFile -and $resolvedCandidate.TrimEnd('\') -eq $resolvedRoot.TrimEnd('\')) {
        throw "Build output cannot equal the dist root"
    }
    return $resolvedCandidate
}

function Get-RelativePathCompat {
    param(
        [string]$BasePath,
        [string]$TargetPath
    )
    $resolvedBase = [IO.Path]::GetFullPath($BasePath).TrimEnd('\', '/') + '\'
    $resolvedTarget = [IO.Path]::GetFullPath($TargetPath)
    $baseUri = [Uri]::new($resolvedBase)
    $targetUri = [Uri]::new($resolvedTarget)
    if (-not [string]::Equals(
        $baseUri.Scheme,
        $targetUri.Scheme,
        [StringComparison]::OrdinalIgnoreCase
    )) {
        throw "Cannot compute a relative path across URI schemes"
    }
    return [Uri]::UnescapeDataString(
        $baseUri.MakeRelativeUri($targetUri).ToString()
    ).Replace('/', '\')
}

$resolvedOutput = Assert-ChildPath -Root $dist -Candidate $Output
$resolvedArchive = Assert-ChildPath -Root $dist -Candidate $Archive -AllowFile
$project = Join-Path $root "native\WordToolkit.Native\WordToolkit.Native.csproj"
$tests = Join-Path `
    $root `
    "native\WordToolkit.Native.Tests\WordToolkit.Native.Tests.csproj"
$engineTests = Join-Path `
    $root `
    "native\WordToolkit.Engine.Tests\WordToolkit.Engine.Tests.csproj"

if (-not $SkipTests) {
    & dotnet test $engineTests -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "Document engine tests failed"
    }
    & dotnet test $tests -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "Native tests failed"
    }
}

if (Test-Path -LiteralPath $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
Copy-Item `
    -LiteralPath (Join-Path $pluginSource ".codex-plugin") `
    -Destination $resolvedOutput `
    -Recurse
Copy-Item `
    -LiteralPath (Join-Path $pluginSource "assets") `
    -Destination $resolvedOutput `
    -Recurse
Copy-Item `
    -LiteralPath (Join-Path $pluginSource "skills") `
    -Destination $resolvedOutput `
    -Recurse
Copy-Item `
    -LiteralPath (Join-Path $pluginSource ".mcp.json") `
    -Destination $resolvedOutput

$runtime = Join-Path $resolvedOutput "runtime\win-x64"
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
& dotnet publish `
    $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $runtime
if ($LASTEXITCODE -ne 0) {
    throw "Native publish failed"
}

Get-ChildItem -LiteralPath $runtime -Filter "*.pdb" -File |
    Remove-Item -Force

$runtimeExecutable = Join-Path $runtime "wordtoolkit-native.exe"
if (-not (Test-Path -LiteralPath $runtimeExecutable -PathType Leaf)) {
    throw "Published runtime executable is missing"
}

$forbidden = @(
    Get-ChildItem -LiteralPath $resolvedOutput -Recurse -File |
        Where-Object {
            $_.Extension -in @(".py", ".pyc", ".pyo") -or
            $_.Name -in @("uv", "uv.exe", "uv.lock", "pyproject.toml") -or
            $_.FullName -match "[\\/]\.venv[\\/]"
        }
)
if ($forbidden.Count -gt 0) {
    throw "Native plugin contains forbidden Python runtime files: $($forbidden.FullName -join ', ')"
}

$mcp = Get-Content `
    -LiteralPath (Join-Path $resolvedOutput ".mcp.json") `
    -Raw |
    ConvertFrom-Json
$command = [string]$mcp.mcpServers.wordtoolkit.command
if (
    $command -notmatch "wordtoolkit-native\.exe$" -or
    $command -match "python|uv"
) {
    throw "Packaged MCP command is not the native executable: $command"
}

if (Test-Path -LiteralPath $resolvedArchive) {
    Remove-Item -LiteralPath $resolvedArchive -Force
}
New-Item `
    -ItemType Directory `
    -Path (Split-Path -Parent $resolvedArchive) `
    -Force |
    Out-Null
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream = [IO.File]::Open(
    $resolvedArchive,
    [IO.FileMode]::CreateNew,
    [IO.FileAccess]::ReadWrite,
    [IO.FileShare]::None
)
try {
    $zip = [IO.Compression.ZipArchive]::new(
        $stream,
        [IO.Compression.ZipArchiveMode]::Create,
        $false
    )
    try {
        foreach (
            $file in Get-ChildItem -LiteralPath $resolvedOutput -Recurse -File |
                Sort-Object FullName
        ) {
            $relative = Get-RelativePathCompat `
                -BasePath $resolvedOutput `
                -TargetPath $file.FullName
            $relative = $relative.Replace('\', '/')
            $entryName = "wordtoolkit/$relative"
            $entry = $zip.CreateEntry(
                $entryName,
                [IO.Compression.CompressionLevel]::Optimal
            )
            $entry.LastWriteTime = [DateTimeOffset]::new(
                1980,
                1,
                1,
                0,
                0,
                0,
                [TimeSpan]::Zero
            )
            $source = [IO.File]::OpenRead($file.FullName)
            try {
                $target = $entry.Open()
                try {
                    $source.CopyTo($target)
                }
                finally {
                    $target.Dispose()
                }
            }
            finally {
                $source.Dispose()
            }
        }
    }
    finally {
        $zip.Dispose()
    }
}
finally {
    $stream.Dispose()
}

$files = @(Get-ChildItem -LiteralPath $resolvedOutput -Recurse -File)
$result = [ordered]@{
    name = $manifest.name
    version = $manifest.version
    runtime = "dotnet-self-contained-win-x64"
    python_runtime = $false
    mcp_command = $command
    output = $resolvedOutput
    archive = $resolvedArchive
    files = $files.Count
    bytes = ($files | Measure-Object Length -Sum).Sum
    archive_bytes = (Get-Item -LiteralPath $resolvedArchive).Length
    archive_sha256 = (
        Get-FileHash -LiteralPath $resolvedArchive -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    executable_sha256 = (
        Get-FileHash -LiteralPath $runtimeExecutable -Algorithm SHA256
    ).Hash.ToLowerInvariant()
}
$result | ConvertTo-Json -Depth 10
