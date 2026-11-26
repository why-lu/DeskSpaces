param([Parameter(Mandatory = $true)][string]$Project)
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
& $dotnet restore $Project --configfile (Join-Path $root 'NuGet.Config') -p:SelfContained=false
exit $LASTEXITCODE
