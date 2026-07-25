[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$Publish,

    [switch]$SelfContained,

    [switch]$NoArchive
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = $PSScriptRoot
$solution = Join-Path $root 'OpsMonitor.slnx'
$artifacts = Join-Path $root 'artifacts'

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

        foreach ($requiredExecutable in @('OpsMonitor.Widget.exe', 'OpsMonitor.Studio.exe')) {
            if (-not (Test-Path -LiteralPath (Join-Path $stagingPackage $requiredExecutable))) {
                throw "The publish package is incomplete: $requiredExecutable is missing."
            }
        }

        foreach ($supportFile in @('Install.ps1', 'Uninstall.ps1', 'README.md')) {
            $supportPath = Join-Path $root $supportFile
            if (Test-Path -LiteralPath $supportPath) {
                Copy-Item -LiteralPath $supportPath -Destination $stagingPackage -Force
            }
        }

        New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
        if (Test-Path -LiteralPath $output) {
            Remove-Item -LiteralPath $output -Recurse -Force
        }
        Move-Item -LiteralPath $stagingPackage -Destination $output

        if (-not $NoArchive) {
            $archive = Join-Path $publishRoot "OPS-Monitor-$packageKind.zip"
            $temporaryArchive = Join-Path $stagingRoot "OPS-Monitor-$packageKind.zip"
            Compress-Archive -Path (Join-Path $output '*') -DestinationPath $temporaryArchive
            if (Test-Path -LiteralPath $archive) {
                Remove-Item -LiteralPath $archive -Force
            }
            Move-Item -LiteralPath $temporaryArchive -Destination $archive
            Write-Host "Release archive: $archive" -ForegroundColor Cyan
        }

        Write-Host "Published package: $output" -ForegroundColor Cyan
    }
    finally {
        if (Test-Path -LiteralPath $stagingRoot) {
            Remove-Item -LiteralPath $stagingRoot -Recurse -Force
        }
    }
}

Write-Host "OPS Monitor build and verification succeeded ($Configuration)." -ForegroundColor Green
