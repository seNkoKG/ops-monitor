[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourceDirectory,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$CompilerPath,

    [string]$CertificateThumbprint
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$repositoryRoot = Split-Path -Parent $root
$publishRoot = Join-Path $root 'artifacts\publish'
$scriptPath = Join-Path $root 'installer\OPS-Monitor.iss'
$resolvedSource = [IO.Path]::GetFullPath($SourceDirectory)

foreach ($requiredPath in @(
        $resolvedSource,
        $scriptPath,
        (Join-Path $resolvedSource 'OpsMonitor.Widget.exe'),
        (Join-Path $resolvedSource 'OpsMonitor.Studio.exe'),
        (Join-Path $resolvedSource 'Update.ps1'),
        (Join-Path $repositoryRoot 'LICENSE'),
        (Join-Path $root 'assets\OpsMonitor.ico')
    )) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "The installer input is incomplete: $requiredPath was not found."
    }
}

$compilerCandidates = @(
    $CompilerPath,
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)
$programFilesX86 = ${env:ProgramFiles(x86)}
if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) {
    $compilerCandidates += Join-Path $programFilesX86 'Inno Setup 7\ISCC.exe'
    $compilerCandidates += Join-Path $programFilesX86 'Inno Setup 6\ISCC.exe'
}
$pathCompiler = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (-not [string]::IsNullOrWhiteSpace($pathCompiler)) {
    $compilerCandidates += $pathCompiler
}
$compilerCandidates = $compilerCandidates |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

$compiler = $compilerCandidates |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1
if (-not $compiler) {
    throw 'Inno Setup 6.7 or newer is required to build the installer. Install JRSoftware.InnoSetup with winget or pass -CompilerPath.'
}

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
$baseName = "OPS-Monitor-v$Version-Setup"
$installerPath = Join-Path $publishRoot ($baseName + '.exe')
$hashPath = $installerPath + '.sha256'

foreach ($previous in @($installerPath, $hashPath)) {
    if (Test-Path -LiteralPath $previous) {
        Remove-Item -LiteralPath $previous -Force
    }
}

& $compiler `
    '/Qp' `
    "/DAppVersion=$Version" `
    "/DSourceDir=$resolvedSource" `
    "/DRepoRoot=$repositoryRoot" `
    "/O$publishRoot" `
    "/F$baseName" `
    $scriptPath
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Inno Setup did not create $installerPath."
}

if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $normalizedThumbprint = $CertificateThumbprint.Replace(' ', '')
    $certificate = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My |
        Where-Object Thumbprint -EQ $normalizedThumbprint |
        Select-Object -First 1
    if ($null -eq $certificate) {
        throw "Code-signing certificate $normalizedThumbprint was not found."
    }

    $signature = Set-AuthenticodeSignature `
        -FilePath $installerPath `
        -Certificate $certificate `
        -TimestampServer 'http://timestamp.digicert.com' `
        -HashAlgorithm SHA256
    if ($signature.Status -ne 'Valid') {
        throw "Signing the Windows installer failed: $($signature.StatusMessage)"
    }
}

$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $hashPath -Value "$hash *$($baseName).exe" -Encoding ascii
Write-Host "Windows installer: $installerPath" -ForegroundColor Cyan
