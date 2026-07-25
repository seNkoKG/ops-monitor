$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$mainScript = Join-Path $projectRoot 'PerformancePill.ps1'

$requiredFiles = @(
    'PerformancePill.ps1',
    'Launch-PerformancePill.vbs',
    'Enable-CpuTemperature.ps1',
    'src\CpuTemperatureBridge.cs',
    'src\MetricCollector.cs',
    'src\MainWindow.xaml',
    'src\SettingsWindow.xaml'
)

foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $projectRoot $relativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing required file: $relativePath"
    }
}

foreach ($xamlFile in @('src\MainWindow.xaml', 'src\SettingsWindow.xaml')) {
    $path = Join-Path $projectRoot $xamlFile
    try {
        [xml][IO.File]::ReadAllText($path) | Out-Null
    }
    catch {
        throw "Invalid XML in ${xamlFile}: $($_.Exception.Message)"
    }
}

$mainXaml = [IO.File]::ReadAllText((Join-Path $projectRoot 'src\MainWindow.xaml'))
$settingsXaml = [IO.File]::ReadAllText((Join-Path $projectRoot 'src\SettingsWindow.xaml'))
$mainSource = [IO.File]::ReadAllText($mainScript)
$collectorSource = [IO.File]::ReadAllText((Join-Path $projectRoot 'src\MetricCollector.cs'))
$bridgeSource = [IO.File]::ReadAllText((Join-Path $projectRoot 'src\CpuTemperatureBridge.cs'))

if (-not $mainXaml.Contains('{DynamicResource CardBrush}') -or
    -not $mainXaml.Contains('{DynamicResource CardStroke}')) {
    throw 'Metric cards are not connected to the live opacity resources.'
}
foreach ($designName in @('PillDesign', 'RailDesign', 'DockDesign')) {
    if (-not $mainXaml.Contains("x:Name=`"$designName`"")) {
        throw "Missing widget design: $designName"
    }
}
foreach ($buttonName in @('PillDesignButton', 'RailDesignButton', 'DockDesignButton')) {
    if (-not $settingsXaml.Contains("x:Name=`"$buttonName`"")) {
        throw "Missing design selector: $buttonName"
    }
}
if ($settingsXaml -notmatch 'x:Name="OpacitySlider"\s+Minimum="30"\s+Maximum="100"') {
    throw 'The opacity slider no longer exposes the supported 30-100% range.'
}
if (-not $mainSource.Contains("Resources['CardBrush']") -or
    -not $mainSource.Contains("Resources['CardStroke']")) {
    throw 'Apply-Appearance no longer updates the metric card opacity.'
}
if (-not $collectorSource.Contains('ReadToEndAsync()') -or
    -not $bridgeSource.Contains('ReadToEndAsync()')) {
    throw 'Sensor child-process streams are not using bounded asynchronous reads.'
}

$resultJson = & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -STA -File $mainScript -SelfTest
if ($LASTEXITCODE -ne 0) {
    throw "Collector self-test failed: $resultJson"
}

$result = $resultJson | ConvertFrom-Json
if (-not $result.Passed) {
    throw 'Collector returned an invalid result.'
}

Write-Host ('Smoke checks passed. CPU {0}%, RAM {1} GB, GPU {2}% at {3}{4}C.' -f
    $result.CpuLoad, $result.RamTotalGB, $result.GpuLoad, $result.GpuTemperature, [char]0xB0)
