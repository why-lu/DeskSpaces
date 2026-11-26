[CmdletBinding()]
param(
    [ValidateSet('build', 'test', 'probe', 'publish')]
    [string]$Target = 'build',
    [switch]$SelfContained,
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0-preview.1'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$localDotnet = Join-Path $root '.dotnet\dotnet.exe'
$env:DOTNET_ROOT = $(if (Test-Path -LiteralPath $localDotnet) { Join-Path $root '.dotnet' } else { $env:DOTNET_ROOT })
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:APPDATA = $(if ($env:CI) { $env:APPDATA } else { Join-Path $root '.build-appdata' })
$env:USERPROFILE = $(if ($env:CI) { $env:USERPROFILE } else { Join-Path $root '.build-home' })
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = Join-Path $root '.packages'
$env:TEMP = Join-Path $root '.tmp'
$env:TMP = $env:TEMP
$dotnet = $(if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source })
New-Item -ItemType Directory -Path $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES, $env:TEMP -Force | Out-Null

switch ($Target) {
    'build' { & $dotnet build (Join-Path $root 'src\DeskSpaces.csproj') -c Release }
    'test' { & $dotnet run --project (Join-Path $root 'tests\CoreTests.csproj') -c Release }
    'probe' { & (Join-Path $root 'src\bin\Release\net8.0-windows\DeskSpaces.Probe.exe') }
    'publish' {
        $output = Join-Path $root 'artifacts\DeskSpaces'
        $arguments = @('publish', (Join-Path $root 'src\DeskSpaces.csproj'), '-c', 'Release', '-o', $output, "-p:Version=$Version")
        if ($SelfContained) {
            $arguments += @('--self-contained', 'true', '-r', 'win-x64')
        } else {
            $arguments += @('--self-contained', 'false')
        }
        & $dotnet @arguments
        if ($LASTEXITCODE -eq 0) {
            Copy-Item -LiteralPath (Join-Path $root 'restore-icons.ps1'), (Join-Path $root 'watchdog.ps1'), (Join-Path $root 'restore-desktop.cmd'), (Join-Path $root 'LICENSE'), (Join-Path $root 'README.md'), (Join-Path $root 'SECURITY.md') -Destination $output -Force
            Get-ChildItem -LiteralPath $output -File | Where-Object { $_.Name -match '^DeskSpaces.*\.(exe|dll)$' } | ForEach-Object {
                $signature = Get-AuthenticodeSignature -LiteralPath $_.FullName
                [pscustomobject]@{ File = $_.Name; SignatureStatus = $signature.Status; ProductVersion = $_.VersionInfo.ProductVersion }
            } | Format-Table -AutoSize | Out-String | Write-Output
        }
    }
}
if ($LASTEXITCODE -ne 0) { throw "Build command failed with exit code $LASTEXITCODE." }
