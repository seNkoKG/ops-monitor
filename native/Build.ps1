[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$Publish,

    [switch]$SelfContained,

    [switch]$Installer,

    [switch]$NoArchive,

    [string]$CertificateThumbprint,

    [string]$InnoSetupCompiler
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = $PSScriptRoot
$solution = Join-Path $root 'OpsMonitor.slnx'
$artifacts = Join-Path $root 'artifacts'
$props = [xml](Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw)
$version = ([string]$props.Project.PropertyGroup.Version | Select-Object -First 1).Trim()
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'Directory.Build.props does not define a release version.'
}
if ($Installer -and (-not $Publish -or -not $SelfContained)) {
    throw '-Installer requires -Publish -SelfContained because the setup package carries the Windows x64 runtime.'
}

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnet = if ($dotnetCommand) {
    $dotnetCommand.Source
}
else {
    'C:\Program Files\dotnet\dotnet.exe'
}

if (-not (Test-Path -LiteralPath $dotnet)) {
    throw '.NET 10 SDK is required. Install Microsoft.DotNet.SDK.10 first.'
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

& $dotnet restore $solution
if ($LASTEXITCODE -ne 0) {
    throw 'Restore failed.'
}

& $dotnet build $solution --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    throw 'Build failed.'
}

& $dotnet run `
    --project (Join-Path $root 'tests\OpsMonitor.Tests\OpsMonitor.Tests.csproj') `
    --configuration $Configuration `
    --no-build
if ($LASTEXITCODE -ne 0) {
    throw 'Tests failed.'
}

& $dotnet run `
    --project (Join-Path $root 'tests\OpsMonitor.SensorBridge.Tests\OpsMonitor.SensorBridge.Tests.csproj') `
    --configuration $Configuration `
    --no-build
if ($LASTEXITCODE -ne 0) {
    throw 'CPU sensor bridge tests failed.'
}

if ($Publish) {
    $packageKind = if ($SelfContained) {
        'win-x64-self-contained'
    }
    else {
        'framework-dependent'
    }
    $publishRoot = Join-Path $artifacts 'publish'
    $output = Join-Path $publishRoot $packageKind
    $stagingRoot = Join-Path $artifacts ('.publish-staging-' + [Guid]::NewGuid().ToString('N'))
    $stagingPackage = Join-Path $stagingRoot 'package'
    $resolvedArtifacts = [IO.Path]::GetFullPath($artifacts)
    $resolvedOutput = [IO.Path]::GetFullPath($output)

    if (-not $resolvedOutput.StartsWith(
            $resolvedArtifacts + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace a publish directory outside $resolvedArtifacts."
    }

    try {
        New-Item -ItemType Directory -Path $stagingPackage -Force | Out-Null

        $runtimeArguments = if ($SelfContained) {
            @('--runtime', 'win-x64', '--self-contained', 'true')
        }
        else {
            @('--self-contained', 'false')
        }
        $publishReadyToRun = $SelfContained.IsPresent

        foreach ($application in @('OpsMonitor.Widget', 'OpsMonitor.Studio')) {
            $project = Join-Path $root "src\$application\$application.csproj"
            $applicationOutput = Join-Path $stagingRoot $application
            & $dotnet publish $project `
                --configuration $Configuration `
                --output $applicationOutput `
                @runtimeArguments `
                -p:PublishReadyToRun=$publishReadyToRun `
                -p:PublishSingleFile=false
            if ($LASTEXITCODE -ne 0) {
                throw "Publishing $application failed."
            }

            Copy-Item `
                -Path (Join-Path $applicationOutput '*') `
                -Destination $stagingPackage `
                -Recurse `
                -Force
        }

        $sensorProject = Join-Path $root 'src\OpsMonitor.SensorBridge\OpsMonitor.SensorBridge.csproj'
        $sensorOutput = Join-Path $stagingRoot 'OpsMonitor.SensorBridge'
        $sensorRuntimeArguments = if ($SelfContained) {
            @('--runtime', 'win-x64', '--self-contained', 'true')
        }
        else {
            @('--runtime', 'win-x64', '--self-contained', 'false')
        }
        & $dotnet publish $sensorProject `
            --configuration $Configuration `
            --output $sensorOutput `
            @sensorRuntimeArguments `
            -p:PublishReadyToRun=$publishReadyToRun `
            -p:PublishSingleFile=false
        if ($LASTEXITCODE -ne 0) {
            throw 'Publishing OpsMonitor.SensorBridge failed.'
        }

        $sensorPackage = Join-Path $stagingPackage 'SensorBridge'
        New-Item -ItemType Directory -Path $sensorPackage -Force | Out-Null
        Copy-Item `
            -Path (Join-Path $sensorOutput '*') `
            -Destination $sensorPackage `
            -Recurse `
            -Force

        foreach ($requiredExecutable in @('OpsMonitor.Widget.exe', 'OpsMonitor.Studio.exe')) {
            if (-not (Test-Path -LiteralPath (Join-Path $stagingPackage $requiredExecutable))) {
                throw "The publish package is incomplete: $requiredExecutable is missing."
            }
        }
        if (-not (Test-Path -LiteralPath (
                    Join-Path $sensorPackage 'OpsMonitor.SensorBridge.exe'
                ))) {
            throw 'The publish package is incomplete: OpsMonitor.SensorBridge.exe is missing.'
        }

        Set-Content -LiteralPath (Join-Path $stagingPackage 'VERSION') -Value $version -Encoding ascii
        Set-Content -LiteralPath (Join-Path $stagingPackage '.ops-package-kind') -Value $packageKind -Encoding ascii

        foreach ($supportFile in @(
                'Install.ps1',
                'Uninstall.ps1',
                'Enable-CpuTemperature.ps1',
                'Disable-CpuTemperature.ps1',
                'Update.ps1',
                'Installer-CloseApps.ps1',
                'README.md',
                'THIRD-PARTY-NOTICES.md'
            )) {
            $supportPath = Join-Path $root $supportFile
            if (Test-Path -LiteralPath $supportPath) {
                Copy-Item -LiteralPath $supportPath -Destination $stagingPackage -Force
            }
        }
        Copy-Item `
            -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') `
            -Destination $sensorPackage `
            -Force

        foreach ($repositoryFile in @('LICENSE', 'SECURITY.md', 'ROADMAP.md', 'CHANGELOG.md')) {
            $repositoryPath = Join-Path (Split-Path -Parent $root) $repositoryFile
            if (Test-Path -LiteralPath $repositoryPath) {
                Copy-Item -LiteralPath $repositoryPath -Destination $stagingPackage -Force
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
            $normalizedThumbprint = $CertificateThumbprint.Replace(' ', '')
            $certificate = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My |
                Where-Object Thumbprint -EQ $normalizedThumbprint |
                Select-Object -First 1
            if ($null -eq $certificate) {
                throw "Code-signing certificate $normalizedThumbprint was not found."
            }

            foreach ($binary in Get-ChildItem -LiteralPath $stagingPackage -Filter '*.exe' -Recurse) {
                $signature = Set-AuthenticodeSignature `
                    -FilePath $binary.FullName `
                    -Certificate $certificate `
                    -TimestampServer 'http://timestamp.digicert.com' `
                    -HashAlgorithm SHA256
                if ($signature.Status -ne 'Valid') {
                    throw "Signing $($binary.Name) failed: $($signature.StatusMessage)"
                }
            }
        }

        New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
        if (Test-Path -LiteralPath $output) {
            Remove-Item -LiteralPath $output -Recurse -Force
        }
        Move-Item -LiteralPath $stagingPackage -Destination $output

        if (-not $NoArchive) {
            $archiveName = "OPS-Monitor-v$version-$packageKind.zip"
            $archive = Join-Path $publishRoot $archiveName
            $temporaryArchive = Join-Path $stagingRoot $archiveName
            foreach ($previous in Get-ChildItem `
                    -LiteralPath $publishRoot `
                    -Filter "OPS-Monitor-*$packageKind.zip*" `
                    -File `
                    -ErrorAction SilentlyContinue) {
                $resolvedPrevious = [IO.Path]::GetFullPath($previous.FullName)
                if (-not $resolvedPrevious.StartsWith(
                        [IO.Path]::GetFullPath($publishRoot) + [IO.Path]::DirectorySeparatorChar,
                        [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Refusing to replace an archive outside $publishRoot."
                }
                Remove-Item -LiteralPath $resolvedPrevious -Force
            }
            Compress-Archive -Path (Join-Path $output '*') -DestinationPath $temporaryArchive
            Move-Item -LiteralPath $temporaryArchive -Destination $archive
            $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
            Set-Content `
                -LiteralPath ($archive + '.sha256') `
                -Value "$hash *$archiveName" `
                -Encoding ascii
            Write-Host "Release archive: $archive" -ForegroundColor Cyan
        }

        Write-Host "Published package: $output" -ForegroundColor Cyan

        if ($Installer) {
            & (Join-Path $root 'Build-Installer.ps1') `
                -SourceDirectory $output `
                -Version $version `
                -CompilerPath $InnoSetupCompiler `
                -CertificateThumbprint $CertificateThumbprint
            if ($LASTEXITCODE -ne 0) {
                throw 'Building the Windows installer failed.'
            }
        }
    }
    finally {
        if (Test-Path -LiteralPath $stagingRoot) {
            Remove-Item -LiteralPath $stagingRoot -Recurse -Force
        }
    }
}

Write-Host "OPS Monitor build and verification succeeded ($Configuration)." -ForegroundColor Green
