#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build & publish AssetRipper.GUI.Free (PowerShell 7+, works on macOS, Linux and Windows).
    Mirrors .github/workflows/publish.yml: dotnet publish -c Release -r <rid> (Native AOT).

.EXAMPLE
    ./build.ps1                         # Release, Native AOT, host runtime
    ./build.ps1 -Runtime osx-x64 -Archive
    ./build.ps1 -Test -NoAot -Run
#>
[CmdletBinding()]
param(
    [ValidateSet('osx-arm64', 'osx-x64', 'linux-x64', 'linux-arm64', 'win-x64', 'win-arm64')]
    [string]$Runtime,
    [ValidateSet('Release', 'Debug')]
    [string]$Config = 'Release',
    [switch]$Test,      # run unit tests before publishing
    [switch]$Clean,     # delete Source/0Bins first
    [switch]$NoAot,     # self-contained JIT build instead of Native AOT
    [switch]$NoClassicLinker, # use Apple's default linker for Native AOT (dotnet/runtime#119380)
    [switch]$Archive,   # create AssetRipper_<name>.tar.xz / .zip like the CI
    [switch]$Run        # start the app after publishing
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Step([string]$Message) { Write-Host "`n==> $Message" -ForegroundColor Cyan }
function Invoke-Checked([string]$File, [string[]]$Arguments, [string]$WorkDir = $PWD) {
    Push-Location $WorkDir
    try {
        & $File @Arguments
        if ($LASTEXITCODE -ne 0) { throw "'$File $($Arguments -join ' ')' failed with exit code $LASTEXITCODE" }
    }
    finally { Pop-Location }
}

$Root       = $PSScriptRoot
$ProjectDir = Join-Path $Root 'Source/AssetRipper.GUI.Free'
$IsWin      = $IsWindows -or ($PSVersionTable.PSEdition -eq 'Desktop')
$Exe        = if ($IsWin) { 'AssetRipper.GUI.Free.exe' } else { 'AssetRipper.GUI.Free' }

if (-not $Runtime) {
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLower()
    $os   = if ($IsMacOS) { 'osx' } elseif ($IsLinux) { 'linux' } else { 'win' }
    $Runtime = "$os-$arch"
}

Step 'Checking prerequisites'
$Dotnet = if ($env:DOTNET) { $env:DOTNET } else { 'dotnet' }   # e.g. $env:DOTNET="$HOME/.dotnet/dotnet"
if (-not (Get-Command $Dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet not found. Install the .NET 10 SDK (see docs/articles/BuildingOnMac.md)'
}
$sdk = (& $Dotnet --version).Trim()
if ([int]($sdk.Split('.')[0]) -lt 10) { throw ".NET SDK $sdk found, but 10.0+ is required" }
$dotnetRoot = Split-Path ((& $Dotnet --list-sdks | Select-Object -Last 1) -replace '^.*\[(.*)\]$', '$1')
Write-Host "dotnet SDK: $sdk ($dotnetRoot)"
$extraPublishArgs = @()
$linkerTargets = $null
if (-not $NoAot -and $IsMacOS) {
    & xcode-select -p *> $null
    if ($LASTEXITCODE -ne 0) { throw "Native AOT needs Xcode Command Line Tools: run 'xcode-select --install' (or use -NoAot)" }

    # Source-built SDKs (e.g. Homebrew's dotnet) link NativeAOT against libssl/libcrypto/libbrotli*.
    $nonPortable = Get-ChildItem (Join-Path $dotnetRoot 'packs') -Recurse -Filter nonportable.txt -ErrorAction SilentlyContinue |
        Where-Object FullName -like '*NativeAOT*' | Select-Object -First 1
    if ($nonPortable) {
        Write-Warning 'This SDK is source-built (non-portable); Native AOT will link against Homebrew libraries.'
        if (-not (Get-Command brew -ErrorAction SilentlyContinue)) { throw 'Non-portable SDK needs Homebrew openssl@3 + brotli, or use the official SDK / -NoAot' }
        $libs = foreach ($pkg in 'openssl@3', 'brotli') {
            $prefix = (& brew --prefix $pkg 2>$null)
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path "$prefix/lib")) { throw "Missing Homebrew package '$pkg': brew install $pkg" }
            "$prefix/lib"
        }
        $env:LIBRARY_PATH = (@($libs) + @($env:LIBRARY_PATH | Where-Object { $_ })) -join ':'
        Write-Warning 'The binary will only run on Macs that have these Homebrew libraries. For a portable build: $env:DOTNET="$HOME/.dotnet/dotnet"'
    }

    if (-not $NoClassicLinker) {
        # Xcode 16+/26 ld crashes on large NativeAOT objects: https://github.com/dotnet/runtime/issues/119380
        $linkerTargets = Join-Path ([System.IO.Path]::GetTempPath()) "assetripper-classic-linker-$PID.targets"
        '<Project><ItemGroup><LinkerArg Include="-Wl,-ld_classic" /></ItemGroup></Project>' | Set-Content $linkerTargets
        $extraPublishArgs += "-p:CustomAfterMicrosoftCommonTargets=$linkerTargets"
        Write-Host 'Using classic Apple linker (-ld_classic); disable with -NoClassicLinker'
    }
}
Write-Host "Runtime: $Runtime | Config: $Config | Native AOT: $(-not $NoAot)"

if ($Clean) {
    Step 'Cleaning Source/0Bins'
    Remove-Item -Recurse -Force (Join-Path $Root 'Source/0Bins') -ErrorAction SilentlyContinue
}

if ($Test) {
    Step 'Running tests'
    Invoke-Checked $Dotnet @('test', (Join-Path $Root 'AssetRipper.slnx'), '-c', 'Debug')
}

Step "Publishing $Exe"
$publishArgs = @('publish', '-c', $Config, '-r', $Runtime) + $extraPublishArgs
if ($NoAot) { $publishArgs += @('-p:PublishAot=false', '--self-contained', 'true') }
try { Invoke-Checked $Dotnet $publishArgs $ProjectDir }
finally { if ($linkerTargets) { Remove-Item $linkerTargets -ErrorAction SilentlyContinue } }

$Out = Join-Path $Root "Source/0Bins/AssetRipper.GUI.Free/$Config/$Runtime/publish"
$ExePath = Join-Path $Out $Exe
if (-not (Test-Path $ExePath)) { throw "publish finished but $ExePath was not found" }
(Get-Date).ToUniversalTime().ToString('ddd MMM dd HH:mm:ss UTC yyyy') | Set-Content (Join-Path $Out 'compile_time.txt')
if ($IsMacOS -or $IsLinux) { & chmod +x $ExePath }
if ($IsMacOS) { & xattr -dr com.apple.quarantine $Out 2>$null }

if ($Archive) {
    Step 'Creating archive'
    $name = $Runtime -replace '^osx', 'mac' -replace '-', '_'
    if ($IsWin) {
        $archivePath = Join-Path $Out "AssetRipper_$name.zip"
        Remove-Item $archivePath -ErrorAction SilentlyContinue
        Get-ChildItem $Out -Exclude '*.zip', '*.pdb' | Compress-Archive -DestinationPath $archivePath
    }
    else {
        $archivePath = Join-Path $Out "AssetRipper_$name.tar.xz"
        $files = if ($NoAot) { @('.') } else {
            @($Exe, 'compile_time.txt') + @(Get-ChildItem -Path (Join-Path $Out '*') -Include '*.dll', '*.so', '*.dylib' -Name)
        }
        Invoke-Checked tar (@('cJf', $archivePath, '--exclude=*.tar.xz', '--exclude=*.dSYM') + $files) $Out
    }
    Write-Host "Archive: $archivePath"
}

Step 'Done'
$size = (Get-ChildItem $Out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Output:  {0}`nSize:    {1:N1} MB`nRun:     `"{2}`"   (add --headless --port 8080 for server mode)" -f $Out, $size, $ExePath)

if ($Run) {
    Step 'Starting AssetRipper'
    & $ExePath
}
