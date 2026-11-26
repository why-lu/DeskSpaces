param([Parameter(Mandatory = $true)][int]$ProcessId, [Parameter(Mandatory = $true)][string]$StatePath)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
try {
    $process = Get-Process -Id $ProcessId -ErrorAction Stop
    Wait-Process -Id $ProcessId -ErrorAction Stop
}
catch { exit 0 }
try {
    if (Test-Path -LiteralPath $StatePath) {
        $state = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
        if (-not $state.OriginalIconsVisible -or $state.Restored) { exit 0 }
    }
    & (Join-Path $root 'restore-icons.ps1')
    $code = $LASTEXITCODE
    if ($code -eq 0) { Set-Content -LiteralPath (Join-Path $root 'watchdog-restored.txt') -Value (Get-Date).ToString('o') -Encoding ASCII }
    exit $code
}
catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $root 'watchdog-error.txt') -Encoding UTF8
    exit 1
}
