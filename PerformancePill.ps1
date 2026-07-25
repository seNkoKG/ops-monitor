[CmdletBinding()]
param(
    [switch]$SelfTest,
    [string]$RenderPreview,
    [string]$RenderStyle
)

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

$script:AppRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$script:DataRoot = Join-Path $env:LOCALAPPDATA 'PerformancePill'
$script:SettingsPath = Join-Path $script:DataRoot 'settings.json'
$script:LauncherPath = Join-Path $script:AppRoot 'Launch-PerformancePill.vbs'
$script:BaseWidth = 184.0
$script:BaseHeight = 396.0
$script:Settings = [ordered]@{
    AlwaysOnTop     = $true
    LockPosition    = $false
    Draggable       = $true
    BackgroundOpacity = 88
    RefreshSeconds  = 2
    StartWithWindows = $false
    CpuSensorEnabled = $false
    ShowTemperatures = $true
    ShowLabels      = $true
    DesignStyle     = 'Pill'
    ScalePercent    = 100
    PositionX       = $null
    PositionY       = $null
}

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase, System.Xaml
Add-Type -AssemblyName System.Windows.Forms, System.Drawing

$collectorSource = [IO.File]::ReadAllText((Join-Path $script:AppRoot 'src\MetricCollector.cs'))
if (-not ('PerformancePill.Runtime.MetricCollector' -as [type])) {
    Add-Type -TypeDefinition $collectorSource -Language CSharp -ReferencedAssemblies @(
        'System.dll',
        'System.Core.dll',
        'System.Management.dll'
    )
}

function Read-Settings {
    if (-not [IO.File]::Exists($script:SettingsPath)) {
        return
    }

    try {
        $saved = [IO.File]::ReadAllText($script:SettingsPath) | ConvertFrom-Json
        foreach ($property in $saved.PSObject.Properties) {
            if ($script:Settings.Contains($property.Name)) {
                $script:Settings[$property.Name] = $property.Value
            }
        }
    }
    catch {
        # A malformed settings file should never prevent the widget from opening.
    }
}

function Save-Settings {
    if (-not [IO.Directory]::Exists($script:DataRoot)) {
        [void][IO.Directory]::CreateDirectory($script:DataRoot)
    }
    $json = $script:Settings | ConvertTo-Json
    $temporaryPath = $script:SettingsPath + '.tmp'
    [IO.File]::WriteAllText($temporaryPath, $json, [Text.UTF8Encoding]::new($false))
    if ([IO.File]::Exists($script:SettingsPath)) {
        [IO.File]::Copy($temporaryPath, $script:SettingsPath, $true)
        [IO.File]::Delete($temporaryPath)
    }
    else {
        [IO.File]::Move($temporaryPath, $script:SettingsPath)
    }
}

function Write-AppError([Exception]$ErrorObject) {
    try {
        if (-not [IO.Directory]::Exists($script:DataRoot)) {
            [void][IO.Directory]::CreateDirectory($script:DataRoot)
        }
        $path = Join-Path $script:DataRoot 'error.log'
        $entry = '[{0:u}] {1}{2}{3}{2}' -f [DateTime]::UtcNow, $ErrorObject, [Environment]::NewLine, (('-' * 48) -join '')
        if ([IO.File]::Exists($path) -and [IO.FileInfo]::new($path).Length -gt 256KB) {
            [IO.File]::WriteAllText($path, $entry)
        }
        else {
            [IO.File]::AppendAllText($path, $entry)
        }
    }
    catch {}
}

function Import-XamlWindow([string]$Path) {
    [xml]$xaml = [IO.File]::ReadAllText($Path)
    $reader = [System.Xml.XmlNodeReader]::new($xaml)
    try {
        return [Windows.Markup.XamlReader]::Load($reader)
    }
    finally {
        $reader.Close()
    }
}

function Get-Control([Windows.Window]$Window, [string]$Name) {
    return $Window.FindName($Name)
}

function Format-Rate([double]$BytesPerSecond) {
    if ($BytesPerSecond -lt 1024) {
        return '{0:0} B/s' -f $BytesPerSecond
    }
    if ($BytesPerSecond -lt 1MB) {
        return '{0:0} KB/s' -f ($BytesPerSecond / 1KB)
    }
    if ($BytesPerSecond -lt 1GB) {
        return '{0:0.0} MB/s' -f ($BytesPerSecond / 1MB)
    }
    return '{0:0.00} GB/s' -f ($BytesPerSecond / 1GB)
}

function Format-CompactRate([double]$BytesPerSecond) {
    if ($BytesPerSecond -lt 1024) {
        return '{0:0}B/s' -f $BytesPerSecond
    }
    if ($BytesPerSecond -lt 1MB) {
        return '{0:0}K/s' -f ($BytesPerSecond / 1KB)
    }
    if ($BytesPerSecond -lt 1GB) {
        return '{0:0.#}M/s' -f ($BytesPerSecond / 1MB)
    }
    return '{0:0.##}G/s' -f ($BytesPerSecond / 1GB)
}

function Set-Startup([bool]$Enabled, [switch]$Quiet) {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $valueName = 'PerformancePill'
    try {
        if ($Enabled) {
            $command = '"{0}" //B //NoLogo "{1}"' -f (Join-Path $env:WINDIR 'System32\wscript.exe'), $script:LauncherPath
            Set-ItemProperty -Path $runKey -Name $valueName -Value $command -Type String
        }
        else {
            Remove-ItemProperty -Path $runKey -Name $valueName -ErrorAction SilentlyContinue
        }
        return $true
    }
    catch {
        if (-not $Quiet) {
            [void][Windows.MessageBox]::Show(
                "Windows startup could not be updated.`n`n$($_.Exception.Message)",
                'Performance Pill',
                [Windows.MessageBoxButton]::OK,
                [Windows.MessageBoxImage]::Warning)
        }
        return $false
    }
}

function Test-CpuSensorReading {
    $path = Join-Path $script:DataRoot 'cpu-temperature.txt'
    if (-not [IO.File]::Exists($path)) {
        return $false
    }
    try {
        $parts = [IO.File]::ReadAllText($path).Split('|')
        if ($parts.Count -ne 2) {
            return $false
        }
        $ticks = [long]$parts[1]
        $stamp = [DateTime]::new($ticks, [DateTimeKind]::Utc)
        return (([DateTime]::UtcNow - $stamp).TotalSeconds -le 20)
    }
    catch {
        return $false
    }
}

function Update-CpuSensorStatus([bool]$Connected) {
    if (-not $script:Controls.CpuSensorStatusText) {
        return
    }
    if ($Connected) {
        $script:Controls.CpuSensorStatusText.Text = 'Connected - elevated AMD sensor'
        $script:Controls.CpuSensorStatusText.Foreground = $script:MainWindow.Resources['MintAccent']
        $script:Controls.EnableCpuSensorButton.Content = 'Reconnect'
    }
    elseif ($script:Settings.CpuSensorEnabled) {
        $script:Controls.CpuSensorStatusText.Text = 'Waiting for the sensor bridge'
        $script:Controls.EnableCpuSensorButton.Content = 'Retry'
    }
    else {
        $script:Controls.CpuSensorStatusText.Text = 'Administrator approval is required once'
        $script:Controls.EnableCpuSensorButton.Content = 'Enable'
    }
}

function Start-CpuSensorTask {
    if (-not $script:Settings.CpuSensorEnabled) {
        return
    }
    try {
        Start-Process -FilePath (Join-Path $env:WINDIR 'System32\schtasks.exe') `
            -ArgumentList @('/Run', '/TN', 'PerformancePillCpuTemperature') `
            -WindowStyle Hidden | Out-Null
    }
    catch {}
}

function Enable-CpuSensor {
    $setupPath = Join-Path $script:AppRoot 'Enable-CpuTemperature.ps1'
    try {
        $arguments = @(
            '-NoLogo',
            '-NoProfile',
            '-ExecutionPolicy', 'Bypass',
            '-File', "`"$setupPath`"",
            '-Elevated'
        )
        Start-Process -FilePath 'powershell.exe' `
            -Verb RunAs `
            -ArgumentList $arguments `
            -WindowStyle Hidden | Out-Null
        $script:Settings.CpuSensorEnabled = $true
        Save-Settings
        Update-CpuSensorStatus $false
    }
    catch {
        $script:Settings.CpuSensorEnabled = $false
        Update-CpuSensorStatus $false
        [void][Windows.MessageBox]::Show(
            'CPU sensor access was not enabled. Approve the Windows administrator prompt to show the real temperature.',
            'Performance Pill',
            [Windows.MessageBoxButton]::OK,
            [Windows.MessageBoxImage]::Information)
    }
}

function Write-Preview([Windows.Window]$Window, [string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $folder = [IO.Path]::GetDirectoryName($resolved)
    if (-not [IO.Directory]::Exists($folder)) {
        [void][IO.Directory]::CreateDirectory($folder)
    }

    $Window.Show()
    $Window.Dispatcher.Invoke([Action]{}, [Windows.Threading.DispatcherPriority]::Render)
    $width = [Math]::Max(1, [int][Math]::Ceiling($Window.ActualWidth))
    $height = [Math]::Max(1, [int][Math]::Ceiling($Window.ActualHeight))
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new(
        $width, $height, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($Window)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Open($resolved, [IO.FileMode]::Create)
    try {
        $encoder.Save($stream)
    }
    finally {
        $stream.Dispose()
    }
    $Window.Close()
    return $resolved
}

if ($SelfTest) {
    $first = [PerformancePill.Runtime.MetricCollector]::Collect()
    Start-Sleep -Milliseconds 450
    $second = [PerformancePill.Runtime.MetricCollector]::Collect()
    $result = [ordered]@{
        Passed = ($second.RamTotalBytes -gt 0 -and $second.CpuLoad -ge 0 -and $second.CpuLoad -le 100)
        CpuLoad = [Math]::Round($second.CpuLoad, 1)
        CpuTemperature = $second.CpuTemperature
        GpuLoad = $second.GpuLoad
        GpuTemperature = $second.GpuTemperature
        RamTotalGB = [Math]::Round($second.RamTotalBytes / 1GB, 1)
        DownloadBytesPerSecond = [Math]::Round($second.DownloadBytesPerSecond)
        UploadBytesPerSecond = [Math]::Round($second.UploadBytesPerSecond)
        PingMilliseconds = $second.PingMilliseconds
        PacketLossPercent = $second.PacketLossPercent
        SignalPercent = $second.SignalPercent
        WifiName = $second.WifiName
    }
    $result | ConvertTo-Json
    if (-not $result.Passed) {
        exit 1
    }
    exit 0
}

Read-Settings

$script:MainWindow = Import-XamlWindow (Join-Path $script:AppRoot 'src\MainWindow.xaml')
$script:SettingsWindow = Import-XamlWindow (Join-Path $script:AppRoot 'src\SettingsWindow.xaml')
$script:SettingsWindow.WindowStartupLocation = [Windows.WindowStartupLocation]::Manual
$script:Exiting = $false
$script:SyncingSettings = $false
$script:CollectTask = $null
$script:NextCollection = [DateTime]::MinValue

$mainNames = @(
    'DesignCanvas', 'PillDesign', 'RailDesign', 'DockDesign',
    'Shell', 'HeaderDragZone', 'SettingsButton', 'LockBadge', 'ResizeThumb',
    'StatusDot', 'UpdatedText', 'HintText', 'CpuValueText', 'CpuTempText',
    'CpuTempPanel', 'CpuBar', 'CpuSparkline', 'GpuValueText', 'GpuTempText', 'GpuTempPanel',
    'GpuBar', 'GpuSparkline', 'MemoryText', 'MemoryPercentText', 'MemoryBar',
    'DownloadText', 'UploadText', 'PingText',
    'PacketLossText', 'CpuLabel', 'GpuLabel', 'MemoryLabel', 'NetworkLabel', 'LatencyLabel',
    'RailShell', 'RailHeaderDragZone', 'RailSettingsButton', 'RailLockBadge', 'RailResizeThumb',
    'RailStatusDot', 'RailUpdatedText', 'RailCpuValueText', 'RailCpuTempText', 'RailCpuTempPanel',
    'RailGpuValueText', 'RailGpuTempText', 'RailGpuTempPanel',
    'RailMemoryText', 'RailMemoryPercentText', 'RailDownloadText', 'RailUploadText',
    'RailPingText', 'RailPacketLossText', 'RailCpuLabel', 'RailGpuLabel', 'RailMemoryLabel',
    'RailNetworkLabel', 'RailLatencyLabel',
    'DockShell', 'DockHeaderDragZone', 'DockSettingsButton', 'DockLockBadge', 'DockResizeThumb',
    'DockStatusDot', 'DockUpdatedText', 'DockCpuValueText', 'DockCpuTempText', 'DockCpuTempPanel',
    'DockCpuBar', 'DockGpuValueText', 'DockGpuTempText', 'DockGpuTempPanel', 'DockGpuBar',
    'DockMemoryText', 'DockMemoryPercentText', 'DockMemoryBar', 'DockDownloadText', 'DockUploadText',
    'DockPingText', 'DockPacketLossText', 'DockCpuLabel', 'DockGpuLabel', 'DockMemoryLabel',
    'DockNetworkLabel', 'DockLatencyLabel'
)
$script:Main = @{}
foreach ($name in $mainNames) {
    $script:Main[$name] = Get-Control $script:MainWindow $name
}

$settingNames = @(
    'SettingsDragZone', 'SettingsCloseButton', 'DoneButton', 'AlwaysTopCheck',
    'LockCheck', 'DraggableCheck', 'StartupCheck', 'OpacitySlider',
    'OpacityValueText', 'DesignValueText', 'PillDesignButton', 'RailDesignButton', 'DockDesignButton',
    'ScaleSlider', 'ScaleValueText', 'MiniButton',
    'SlimButton', 'BalancedButton', 'ComfortButton', 'ShowTemperaturesCheck', 'ShowLabelsCheck',
    'CpuSensorStatusText', 'EnableCpuSensorButton', 'RefreshCombo', 'ResetPositionButton'
)
$script:Controls = @{}
foreach ($name in $settingNames) {
    $script:Controls[$name] = Get-Control $script:SettingsWindow $name
}
$script:MainWindow.Dispatcher.Add_UnhandledException({
    param($sender, $eventArgs)
    Write-AppError $eventArgs.Exception
    $eventArgs.Handled = $true
    $script:Main.UpdatedText.Text = 'Recovered from a UI error'
})
$script:CpuHistory = [Collections.Generic.List[double]]::new()
$script:GpuHistory = [Collections.Generic.List[double]]::new()

function Apply-Design([string]$Style, [bool]$Persist = $true) {
    if ($Style -notin @('Pill', 'Rail', 'Dock')) {
        $Style = 'Pill'
    }

    $spec = switch ($Style) {
        'Rail' { @{ Width = 160.0; Height = 286.0; Visible = 'RailDesign' } }
        'Dock' { @{ Width = 540.0; Height = 86.0; Visible = 'DockDesign' } }
        default { @{ Width = 184.0; Height = 396.0; Visible = 'PillDesign' } }
    }

    $script:BaseWidth = [double]$spec.Width
    $script:BaseHeight = [double]$spec.Height
    $script:Main.DesignCanvas.Width = $script:BaseWidth
    $script:Main.DesignCanvas.Height = $script:BaseHeight
    foreach ($designName in @('PillDesign', 'RailDesign', 'DockDesign')) {
        $script:Main[$designName].Visibility = if ($designName -eq $spec.Visible) {
            [Windows.Visibility]::Visible
        } else {
            [Windows.Visibility]::Collapsed
        }
    }

    $script:MainWindow.MinWidth = 0
    $script:MainWindow.MinHeight = 0
    $script:MainWindow.MaxWidth = $script:BaseWidth * 1.5
    $script:MainWindow.MaxHeight = $script:BaseHeight * 1.5
    $script:MainWindow.MinWidth = $script:BaseWidth * 0.8
    $script:MainWindow.MinHeight = $script:BaseHeight * 0.8
    if ($Persist) {
        $script:Settings.DesignStyle = $Style
    }

    Update-DesignSelector $Style
}

function Update-DesignSelector([string]$Style) {
    if (-not $script:Controls.DesignValueText) {
        return
    }
    $script:Controls.DesignValueText.Text = $Style
    foreach ($entry in @(
        @{ Name = 'PillDesignButton'; Style = 'Pill' },
        @{ Name = 'RailDesignButton'; Style = 'Rail' },
        @{ Name = 'DockDesignButton'; Style = 'Dock' }
    )) {
        $selected = $entry.Style -eq $Style
        $button = $script:Controls[$entry.Name]
        $button.Opacity = if ($selected) { 1.0 } else { 0.58 }
        $button.Background = if ($selected) {
            [Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromRgb(104, 122, 243))
        } else {
            [Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromArgb(16, 255, 255, 255))
        }
    }
}

function Apply-Scale([double]$Percent, [bool]$Persist = $true) {
    $clamped = [Math]::Max(80, [Math]::Min(150, $Percent))
    $script:MainWindow.Width = $script:BaseWidth * ($clamped / 100.0)
    $script:MainWindow.Height = $script:BaseHeight * ($clamped / 100.0)
    if ($Persist) {
        $script:Settings.ScalePercent = [int][Math]::Round($clamped)
    }
    if ($script:Controls.ScaleSlider) {
        $script:SyncingSettings = $true
        $script:Controls.ScaleSlider.Value = $clamped
        $script:Controls.ScaleValueText.Text = '{0:0}%' -f $clamped
        $script:SyncingSettings = $false
    }
}

function Apply-Appearance {
    $opacity = [Math]::Max(30, [Math]::Min(100, [double]$script:Settings.BackgroundOpacity))
    $shellAlpha = [byte][Math]::Round(255 * ($opacity / 100.0))
    $cardAlpha = [byte][Math]::Round(205 * ($opacity / 100.0))
    $strokeAlpha = [byte][Math]::Round(46 * ($opacity / 100.0))
    foreach ($shellName in @('Shell', 'RailShell', 'DockShell')) {
        $script:Main[$shellName].Background = [Windows.Media.SolidColorBrush]::new(
            [Windows.Media.Color]::FromArgb($shellAlpha, 18, 21, 28))
    }
    $script:MainWindow.Resources['CardBrush'] = [Windows.Media.SolidColorBrush]::new(
        [Windows.Media.Color]::FromArgb($cardAlpha, 30, 37, 49))
    $script:MainWindow.Resources['CardStroke'] = [Windows.Media.SolidColorBrush]::new(
        [Windows.Media.Color]::FromArgb($strokeAlpha, 255, 255, 255))
    $script:MainWindow.Topmost = [bool]$script:Settings.AlwaysOnTop
    $script:SettingsWindow.Topmost = [bool]$script:Settings.AlwaysOnTop
    foreach ($badgeName in @('LockBadge', 'RailLockBadge', 'DockLockBadge')) {
        $script:Main[$badgeName].Visibility = if ($script:Settings.LockPosition) {
            [Windows.Visibility]::Visible
        } else {
            [Windows.Visibility]::Collapsed
        }
    }
    foreach ($thumbName in @('ResizeThumb', 'RailResizeThumb', 'DockResizeThumb')) {
        $script:Main[$thumbName].Visibility = if ($script:Settings.LockPosition) {
            [Windows.Visibility]::Collapsed
        } else {
            [Windows.Visibility]::Visible
        }
    }
    $script:Main.HintText.Text = if ($script:Settings.LockPosition) {
        'POSITION LOCKED'
    } elseif (-not $script:Settings.Draggable) {
        'DRAGGING OFF'
    } else {
        'DRAG TO MOVE'
    }

    $temperatureVisibility = if ($script:Settings.ShowTemperatures) {
        [Windows.Visibility]::Visible
    } else {
        [Windows.Visibility]::Collapsed
    }
    foreach ($panelName in @(
        'CpuTempPanel', 'GpuTempPanel',
        'RailCpuTempPanel', 'RailGpuTempPanel',
        'DockCpuTempPanel', 'DockGpuTempPanel'
    )) {
        $script:Main[$panelName].Visibility = $temperatureVisibility
    }

    $labelVisibility = if ($script:Settings.ShowLabels) {
        [Windows.Visibility]::Visible
    } else {
        [Windows.Visibility]::Collapsed
    }
    foreach ($label in @(
        'CpuLabel', 'GpuLabel', 'MemoryLabel', 'NetworkLabel', 'LatencyLabel',
        'RailCpuLabel', 'RailGpuLabel', 'RailMemoryLabel', 'RailNetworkLabel', 'RailLatencyLabel',
        'DockCpuLabel', 'DockGpuLabel', 'DockMemoryLabel', 'DockNetworkLabel', 'DockLatencyLabel'
    )) {
        $script:Main[$label].Visibility = $labelVisibility
    }
}

function Place-MainWindow {
    $workArea = [Windows.SystemParameters]::WorkArea
    $hasPosition = $null -ne $script:Settings.PositionX -and $null -ne $script:Settings.PositionY
    if ($hasPosition) {
        $left = [double]$script:Settings.PositionX
        $top = [double]$script:Settings.PositionY
    }
    else {
        $left = $workArea.Right - $script:MainWindow.Width - 26
        $top = $workArea.Top + 42
    }
    $script:MainWindow.Left = [Math]::Max($workArea.Left, [Math]::Min($workArea.Right - $script:MainWindow.Width, $left))
    $script:MainWindow.Top = [Math]::Max($workArea.Top, [Math]::Min($workArea.Bottom - $script:MainWindow.Height, $top))
}

function Save-WindowState {
    if ($script:MainWindow.WindowState -eq [Windows.WindowState]::Normal) {
        $script:Settings.PositionX = [Math]::Round($script:MainWindow.Left, 1)
        $script:Settings.PositionY = [Math]::Round($script:MainWindow.Top, 1)
    }
    Save-Settings
}

function Position-SettingsWindow {
    $workArea = [Windows.SystemParameters]::WorkArea
    $rightCandidate = $script:MainWindow.Left + $script:MainWindow.Width + 10
    $leftCandidate = $script:MainWindow.Left - $script:SettingsWindow.Width - 10
    if (($rightCandidate + $script:SettingsWindow.Width) -le $workArea.Right) {
        $script:SettingsWindow.Left = $rightCandidate
    }
    else {
        $script:SettingsWindow.Left = [Math]::Max($workArea.Left, $leftCandidate)
    }
    $script:SettingsWindow.Top = [Math]::Max(
        $workArea.Top,
        [Math]::Min($workArea.Bottom - $script:SettingsWindow.Height, $script:MainWindow.Top))
}

function Sync-SettingsControls {
    $script:SyncingSettings = $true
    $script:Controls.AlwaysTopCheck.IsChecked = [bool]$script:Settings.AlwaysOnTop
    $script:Controls.LockCheck.IsChecked = [bool]$script:Settings.LockPosition
    $script:Controls.DraggableCheck.IsChecked = [bool]$script:Settings.Draggable
    $script:Controls.DraggableCheck.IsEnabled = -not [bool]$script:Settings.LockPosition
    $script:Controls.StartupCheck.IsChecked = [bool]$script:Settings.StartWithWindows
    $script:Controls.OpacitySlider.Value = [double]$script:Settings.BackgroundOpacity
    $script:Controls.OpacityValueText.Text = '{0:0}%' -f [double]$script:Settings.BackgroundOpacity
    Update-DesignSelector ([string]$script:Settings.DesignStyle)
    $script:Controls.ScaleSlider.Value = [double]$script:Settings.ScalePercent
    $script:Controls.ScaleValueText.Text = '{0:0}%' -f [double]$script:Settings.ScalePercent
    $script:Controls.ShowTemperaturesCheck.IsChecked = [bool]$script:Settings.ShowTemperatures
    $script:Controls.ShowLabelsCheck.IsChecked = [bool]$script:Settings.ShowLabels
    Update-CpuSensorStatus (Test-CpuSensorReading)

    $refresh = [int]$script:Settings.RefreshSeconds
    for ($index = 0; $index -lt $script:Controls.RefreshCombo.Items.Count; $index++) {
        $item = $script:Controls.RefreshCombo.Items[$index]
        if ([int]$item.Tag -eq $refresh) {
            $script:Controls.RefreshCombo.SelectedIndex = $index
            break
        }
    }
    $script:SyncingSettings = $false
}

function Show-Settings {
    Sync-SettingsControls
    Position-SettingsWindow
    if ($null -eq $script:SettingsWindow.Owner -and $script:MainWindow.IsVisible) {
        $script:SettingsWindow.Owner = $script:MainWindow
    }
    if (-not $script:SettingsWindow.IsVisible) {
        $script:SettingsWindow.Show()
    }
    else {
        $script:SettingsWindow.Activate()
    }
}

function Select-Design([string]$Style) {
    Apply-Design $Style
    Apply-Scale ([double]$script:Settings.ScalePercent) $false
    Apply-Appearance
    Place-MainWindow
    Save-WindowState
    Position-SettingsWindow
}

function Update-Sparkline(
    [Windows.Shapes.Polyline]$Polyline,
    [Collections.Generic.List[double]]$History,
    [double]$Value
) {
    $History.Add([Math]::Max(0, [Math]::Min(100, $Value)))
    while ($History.Count -gt 12) {
        $History.RemoveAt(0)
    }
    $points = [Windows.Media.PointCollection]::new()
    for ($index = 0; $index -lt $History.Count; $index++) {
        $x = if ($History.Count -le 1) { 128.0 } else { 128.0 * $index / ($History.Count - 1) }
        $y = 5.0 - (4.0 * $History[$index] / 100.0)
        $points.Add([Windows.Point]::new($x, $y))
    }
    $Polyline.Points = $points
}

function Update-Metrics([PerformancePill.Runtime.MetricSnapshot]$Snapshot) {
    $cpu = [Math]::Max(0, [Math]::Min(100, $Snapshot.CpuLoad))
    $script:Main.CpuValueText.Text = '{0:0}%' -f $cpu
    $script:Main.CpuBar.Value = $cpu
    Update-Sparkline $script:Main.CpuSparkline $script:CpuHistory $cpu

    if ($Snapshot.CpuTemperature -ge 0) {
        $script:Main.CpuTempText.Text = '{0:0}{1}' -f $Snapshot.CpuTemperature, [char]0xB0
        $script:Main.CpuTempText.ToolTip = 'CPU temperature'
    }
    else {
        $script:Main.CpuTempText.Text = 'N/A'
        $script:Main.CpuTempText.ToolTip = 'Open settings and enable CPU sensor access to show the real temperature.'
        Update-CpuSensorStatus $false
    }

    if ($Snapshot.GpuLoad -ge 0) {
        $gpu = [Math]::Max(0, [Math]::Min(100, $Snapshot.GpuLoad))
        $script:Main.GpuValueText.Text = '{0:0}%' -f $gpu
        $script:Main.GpuBar.Value = $gpu
        Update-Sparkline $script:Main.GpuSparkline $script:GpuHistory $gpu
    }
    else {
        $script:Main.GpuValueText.Text = '--%'
        $script:Main.GpuBar.Value = 0
        Update-Sparkline $script:Main.GpuSparkline $script:GpuHistory 0
    }

    if ($Snapshot.GpuTemperature -ge 0) {
        $script:Main.GpuTempText.Text = '{0:0}{1}' -f $Snapshot.GpuTemperature, [char]0xB0
        $script:Main.GpuTempText.ToolTip = 'GPU temperature'
    }
    else {
        $script:Main.GpuTempText.Text = '--{0}' -f [char]0xB0
        $script:Main.GpuTempText.ToolTip = 'GPU temperature is not exposed by the installed driver.'
    }
    if ($Snapshot.CpuTemperature -ge 0) {
        Update-CpuSensorStatus $true
    }

    $used = $Snapshot.RamUsedBytes / 1GB
    $total = $Snapshot.RamTotalBytes / 1GB
    $memoryPercent = if ($Snapshot.RamTotalBytes -gt 0) {
        100 * $Snapshot.RamUsedBytes / $Snapshot.RamTotalBytes
    } else { 0 }
    $script:Main.MemoryText.Text = '{0:0.0} / {1:0.0}' -f $used, $total
    $script:Main.MemoryPercentText.Text = '{0:0}%' -f $memoryPercent
    $script:Main.MemoryBar.Value = $memoryPercent
    $script:Main.DownloadText.Text = Format-Rate $Snapshot.DownloadBytesPerSecond
    $script:Main.UploadText.Text = Format-Rate $Snapshot.UploadBytesPerSecond
    $script:Main.PingText.Text = if ($Snapshot.PingMilliseconds -ge 0) {
        '{0:0} ms' -f $Snapshot.PingMilliseconds
    } else {
        'offline'
    }
    $script:Main.PacketLossText.Text = if ($Snapshot.PacketLossPercent -ge 0) {
        '{0:0.#}%' -f $Snapshot.PacketLossPercent
    } else {
        '--%'
    }
    $script:Main.PacketLossText.ToolTip = 'Rolling packet loss over the latest 20 connectivity checks'

    foreach ($prefix in @('Rail', 'Dock')) {
        $script:Main["${prefix}CpuValueText"].Text = $script:Main.CpuValueText.Text
        $script:Main["${prefix}CpuTempText"].Text = $script:Main.CpuTempText.Text
        $script:Main["${prefix}CpuTempText"].ToolTip = $script:Main.CpuTempText.ToolTip
        $script:Main["${prefix}GpuValueText"].Text = $script:Main.GpuValueText.Text
        $script:Main["${prefix}GpuTempText"].Text = $script:Main.GpuTempText.Text
        $script:Main["${prefix}GpuTempText"].ToolTip = $script:Main.GpuTempText.ToolTip
        $script:Main["${prefix}MemoryText"].Text = '{0:0.0}/{1:0.0}' -f $used, $total
        $script:Main["${prefix}MemoryPercentText"].Text = $script:Main.MemoryPercentText.Text
        $script:Main["${prefix}DownloadText"].Text = $script:Main.DownloadText.Text
        $script:Main["${prefix}UploadText"].Text = $script:Main.UploadText.Text
        $script:Main["${prefix}PingText"].Text = $script:Main.PingText.Text
        $script:Main["${prefix}PacketLossText"].Text = $script:Main.PacketLossText.Text
        $script:Main["${prefix}PacketLossText"].ToolTip = $script:Main.PacketLossText.ToolTip
        $script:Main["${prefix}UpdatedText"].Text = 'Updated now'
        $script:Main["${prefix}StatusDot"].Fill = $script:MainWindow.Resources['MintAccent']
    }
    $script:Main.DockCpuBar.Value = $cpu
    $script:Main.DockGpuBar.Value = if ($Snapshot.GpuLoad -ge 0) { $gpu } else { 0 }
    $script:Main.RailDownloadText.Text = Format-CompactRate $Snapshot.DownloadBytesPerSecond
    $script:Main.RailUploadText.Text = Format-CompactRate $Snapshot.UploadBytesPerSecond
    $script:Main.RailPingText.Text = if ($Snapshot.PingMilliseconds -ge 0) {
        '{0:0}ms' -f $Snapshot.PingMilliseconds
    } else {
        'offline'
    }
    $script:Main.DockMemoryText.Text = '{0:0.0}/{1:0.0}' -f $used, $total
    $script:Main.DockMemoryBar.Value = $memoryPercent
    $script:Main.DockDownloadText.Text = Format-CompactRate $Snapshot.DownloadBytesPerSecond
    $script:Main.DockUploadText.Text = Format-CompactRate $Snapshot.UploadBytesPerSecond
    $script:Main.DockUpdatedText.Text = 'LIVE'
    $script:Main.DockPingText.Text = if ($Snapshot.PingMilliseconds -ge 0) {
        '{0:0}ms' -f $Snapshot.PingMilliseconds
    } else {
        'offline'
    }
    $script:Main.UpdatedText.Text = 'Updated just now'
    $script:Main.StatusDot.Fill = $script:MainWindow.Resources['MintAccent']
}

function Exit-Application {
    if ($script:Exiting) {
        return
    }
    $script:Exiting = $true
    try { Save-WindowState } catch {}
    if ($script:PollTimer) { $script:PollTimer.Stop() }
    if ($script:NotifyIcon) {
        $script:NotifyIcon.Visible = $false
        $script:NotifyIcon.Dispose()
    }
    try { $script:SettingsWindow.Close() } catch {}
    try { $script:MainWindow.Close() } catch {}
}

$initialDesign = if ([string]::IsNullOrWhiteSpace($RenderStyle)) {
    [string]$script:Settings.DesignStyle
} else {
    $RenderStyle
}
Apply-Design $initialDesign $false
Apply-Scale ([double]$script:Settings.ScalePercent) $false
Apply-Appearance
Place-MainWindow

if ($script:Settings.StartWithWindows) {
    [void](Set-Startup $true -Quiet)
}
Start-CpuSensorTask

$dragHandler = {
    param($sender, $eventArgs)
    if ($eventArgs.ChangedButton -eq [Windows.Input.MouseButton]::Left -and
        -not $script:Settings.LockPosition -and $script:Settings.Draggable) {
        try {
            $script:MainWindow.DragMove()
            Save-WindowState
            if ($script:SettingsWindow.IsVisible) {
                Position-SettingsWindow
            }
        }
        catch {}
    }
}
foreach ($dragName in @('HeaderDragZone', 'RailHeaderDragZone', 'DockHeaderDragZone')) {
    $script:Main[$dragName].Add_MouseLeftButtonDown($dragHandler)
}

$resizeHandler = {
    param($sender, $eventArgs)
    if ($script:Settings.LockPosition) {
        return
    }
    $widthDeltaFromHeight = $eventArgs.VerticalChange * ($script:BaseWidth / $script:BaseHeight)
    $delta = if ([Math]::Abs($widthDeltaFromHeight) -gt [Math]::Abs($eventArgs.HorizontalChange)) {
        $widthDeltaFromHeight
    } else {
        $eventArgs.HorizontalChange
    }
    $newWidth = [Math]::Max(
        $script:BaseWidth * 0.8,
        [Math]::Min($script:BaseWidth * 1.5, $script:MainWindow.Width + $delta))
    Apply-Scale (($newWidth / $script:BaseWidth) * 100)
    if ($script:SettingsWindow.IsVisible) {
        Position-SettingsWindow
    }
}
foreach ($thumbName in @('ResizeThumb', 'RailResizeThumb', 'DockResizeThumb')) {
    $script:Main[$thumbName].Add_DragDelta($resizeHandler)
    $script:Main[$thumbName].Add_DragCompleted({ Save-WindowState })
}
foreach ($settingsButtonName in @('SettingsButton', 'RailSettingsButton', 'DockSettingsButton')) {
    $script:Main[$settingsButtonName].Add_Click({ Show-Settings })
}

$script:SettingsWindow.Add_Closing({
    param($sender, $eventArgs)
    if (-not $script:Exiting) {
        $eventArgs.Cancel = $true
        $script:SettingsWindow.Hide()
        Save-Settings
    }
})
$script:SettingsDragZone = $script:Controls.SettingsDragZone
$script:SettingsDragZone.Add_MouseLeftButtonDown({
    param($sender, $eventArgs)
    if ($eventArgs.ChangedButton -eq [Windows.Input.MouseButton]::Left) {
        try { $script:SettingsWindow.DragMove() } catch {}
    }
})
$script:Controls.SettingsCloseButton.Add_Click({ $script:SettingsWindow.Hide(); Save-Settings })
$script:Controls.DoneButton.Add_Click({ $script:SettingsWindow.Hide(); Save-Settings })

$script:Controls.AlwaysTopCheck.Add_Checked({
    if ($script:SyncingSettings) { return }
    $script:Settings.AlwaysOnTop = $true
    Apply-Appearance
    Save-Settings
})
$script:Controls.AlwaysTopCheck.Add_Unchecked({
    if ($script:SyncingSettings) { return }
    $script:Settings.AlwaysOnTop = $false
    Apply-Appearance
    Save-Settings
})
$script:Controls.LockCheck.Add_Checked({
    if ($script:SyncingSettings) { return }
    $script:Settings.LockPosition = $true
    $script:Controls.DraggableCheck.IsEnabled = $false
    Apply-Appearance
    Save-WindowState
})
$script:Controls.LockCheck.Add_Unchecked({
    if ($script:SyncingSettings) { return }
    $script:Settings.LockPosition = $false
    $script:Controls.DraggableCheck.IsEnabled = $true
    Apply-Appearance
    Save-Settings
})
$script:Controls.DraggableCheck.Add_Checked({
    if ($script:SyncingSettings) { return }
    $script:Settings.Draggable = $true
    Apply-Appearance
    Save-Settings
})
$script:Controls.DraggableCheck.Add_Unchecked({
    if ($script:SyncingSettings) { return }
    $script:Settings.Draggable = $false
    Apply-Appearance
    Save-Settings
})
$script:Controls.StartupCheck.Add_Checked({
    if ($script:SyncingSettings) { return }
    if (Set-Startup $true) {
        $script:Settings.StartWithWindows = $true
        Save-Settings
    }
    else {
        $script:SyncingSettings = $true
        $script:Controls.StartupCheck.IsChecked = $false
        $script:SyncingSettings = $false
    }
})
$script:Controls.StartupCheck.Add_Unchecked({
    if ($script:SyncingSettings) { return }
    if (Set-Startup $false) {
        $script:Settings.StartWithWindows = $false
        Save-Settings
    }
})
$script:Controls.ShowTemperaturesCheck.Add_Checked({
    if ($script:SyncingSettings) { return }
    $script:Settings.ShowTemperatures = $true
    Apply-Appearance
    Save-Settings
})
$script:Controls.ShowTemperaturesCheck.Add_Unchecked({
    if ($script:SyncingSettings) { return }
    $script:Settings.ShowTemperatures = $false
    Apply-Appearance
    Save-Settings
})
$script:Controls.ShowLabelsCheck.Add_Checked({
    if ($script:SyncingSettings) { return }
    $script:Settings.ShowLabels = $true
    Apply-Appearance
    Save-Settings
})
$script:Controls.ShowLabelsCheck.Add_Unchecked({
    if ($script:SyncingSettings) { return }
    $script:Settings.ShowLabels = $false
    Apply-Appearance
    Save-Settings
})
$script:Controls.EnableCpuSensorButton.Add_Click({ Enable-CpuSensor })
$script:Controls.PillDesignButton.Add_Click({ Select-Design 'Pill' })
$script:Controls.RailDesignButton.Add_Click({ Select-Design 'Rail' })
$script:Controls.DockDesignButton.Add_Click({ Select-Design 'Dock' })
$script:Controls.OpacitySlider.Add_ValueChanged({
    param($sender, $eventArgs)
    if ($script:SyncingSettings) { return }
    $script:Settings.BackgroundOpacity = [int][Math]::Round($sender.Value)
    $script:Controls.OpacityValueText.Text = '{0}%' -f $script:Settings.BackgroundOpacity
    Apply-Appearance
})
$script:Controls.OpacitySlider.Add_LostMouseCapture({ Save-Settings })
$script:Controls.ScaleSlider.Add_ValueChanged({
    param($sender, $eventArgs)
    if ($script:SyncingSettings) { return }
    Apply-Scale $sender.Value
    $script:Controls.ScaleValueText.Text = '{0:0}%' -f $sender.Value
    Position-SettingsWindow
})
$script:Controls.ScaleSlider.Add_LostMouseCapture({ Save-WindowState })
$script:Controls.MiniButton.Add_Click({ Apply-Scale 80; Save-WindowState; Position-SettingsWindow })
$script:Controls.SlimButton.Add_Click({ Apply-Scale 90; Save-WindowState; Position-SettingsWindow })
$script:Controls.BalancedButton.Add_Click({ Apply-Scale 100; Save-WindowState; Position-SettingsWindow })
$script:Controls.ComfortButton.Add_Click({ Apply-Scale 115; Save-WindowState; Position-SettingsWindow })
$script:Controls.RefreshCombo.Add_SelectionChanged({
    param($sender, $eventArgs)
    if ($script:SyncingSettings -or $null -eq $sender.SelectedItem) { return }
    $script:Settings.RefreshSeconds = [int]$sender.SelectedItem.Tag
    $script:NextCollection = [DateTime]::UtcNow
    Save-Settings
})
$script:Controls.ResetPositionButton.Add_Click({
    $script:Settings.PositionX = $null
    $script:Settings.PositionY = $null
    Place-MainWindow
    Position-SettingsWindow
    Save-WindowState
})

$script:MainWindow.Add_Closing({
    param($sender, $eventArgs)
    if (-not $script:Exiting) {
        $eventArgs.Cancel = $true
        $script:MainWindow.Hide()
    }
})

$contextMenu = [Windows.Controls.ContextMenu]::new()
$openSettingsItem = [Windows.Controls.MenuItem]::new()
$openSettingsItem.Header = 'Settings'
$openSettingsItem.Add_Click({ Show-Settings })
[void]$contextMenu.Items.Add($openSettingsItem)
$lockItem = [Windows.Controls.MenuItem]::new()
$lockItem.Header = 'Lock / unlock position'
$lockItem.Add_Click({
    $script:Settings.LockPosition = -not [bool]$script:Settings.LockPosition
    Apply-Appearance
    Sync-SettingsControls
    Save-Settings
})
[void]$contextMenu.Items.Add($lockItem)
$designMenu = [Windows.Controls.MenuItem]::new()
$designMenu.Header = 'Design'
foreach ($design in @('Pill', 'Rail', 'Dock')) {
    $designItem = [Windows.Controls.MenuItem]::new()
    $designItem.Header = $design
    $designName = $design
    $designItem.Add_Click({
        Select-Design $designName
    }.GetNewClosure())
    [void]$designMenu.Items.Add($designItem)
}
[void]$contextMenu.Items.Add($designMenu)
$sizeMenu = [Windows.Controls.MenuItem]::new()
$sizeMenu.Header = 'Size preset'
foreach ($preset in @(
    @{ Label = 'Mini (80%)'; Scale = 80 },
    @{ Label = 'Slim (90%)'; Scale = 90 },
    @{ Label = 'Balanced (100%)'; Scale = 100 },
    @{ Label = 'Comfort (115%)'; Scale = 115 }
)) {
    $presetItem = [Windows.Controls.MenuItem]::new()
    $presetItem.Header = $preset.Label
    $presetScale = [int]$preset.Scale
    $presetItem.Add_Click({
        Apply-Scale $presetScale
        Save-WindowState
    }.GetNewClosure())
    [void]$sizeMenu.Items.Add($presetItem)
}
[void]$contextMenu.Items.Add($sizeMenu)
[void]$contextMenu.Items.Add([Windows.Controls.Separator]::new())
$exitItem = [Windows.Controls.MenuItem]::new()
$exitItem.Header = 'Exit'
$exitItem.Add_Click({ Exit-Application })
[void]$contextMenu.Items.Add($exitItem)
foreach ($shellName in @('Shell', 'RailShell', 'DockShell')) {
    $script:Main[$shellName].ContextMenu = $contextMenu
}

$script:NotifyIcon = [Windows.Forms.NotifyIcon]::new()
$script:NotifyIcon.Icon = [Drawing.SystemIcons]::Information
$script:NotifyIcon.Text = 'Performance Pill'
$script:NotifyIcon.Visible = $true
$trayMenu = [Windows.Forms.ContextMenuStrip]::new()
$showTrayItem = $trayMenu.Items.Add('Show widget')
$settingsTrayItem = $trayMenu.Items.Add('Settings')
[void]$trayMenu.Items.Add('-')
$exitTrayItem = $trayMenu.Items.Add('Exit')
$showTrayItem.Add_Click({ $script:MainWindow.Show(); $script:MainWindow.Activate() })
$settingsTrayItem.Add_Click({ $script:MainWindow.Show(); Show-Settings })
$exitTrayItem.Add_Click({ Exit-Application })
$script:NotifyIcon.ContextMenuStrip = $trayMenu
$script:NotifyIcon.Add_DoubleClick({ $script:MainWindow.Show(); $script:MainWindow.Activate() })

$script:PollTimer = [Windows.Threading.DispatcherTimer]::new()
$script:PollTimer.Interval = [TimeSpan]::FromMilliseconds(250)
$script:PollTimer.Add_Tick({
    if ($null -ne $script:CollectTask -and $script:CollectTask.IsCompleted) {
        if ($script:CollectTask.Status -eq [Threading.Tasks.TaskStatus]::RanToCompletion) {
            Update-Metrics $script:CollectTask.Result
        }
        else {
            $script:Main.UpdatedText.Text = 'Sensor retry pending'
            $script:Main.StatusDot.Fill = [Windows.Media.SolidColorBrush]::new(
                [Windows.Media.Color]::FromRgb(255, 198, 109))
        }
        $script:CollectTask = $null
        $script:NextCollection = [DateTime]::UtcNow.AddSeconds([double]$script:Settings.RefreshSeconds)
    }

    if ($null -eq $script:CollectTask -and [DateTime]::UtcNow -ge $script:NextCollection) {
        $script:CollectTask = [PerformancePill.Runtime.MetricCollector]::CollectAsync()
    }
})

Sync-SettingsControls

if (-not [string]::IsNullOrWhiteSpace($RenderPreview)) {
    $previewSnapshot = [PerformancePill.Runtime.MetricCollector]::Collect()
    Start-Sleep -Milliseconds 300
    $previewSnapshot = [PerformancePill.Runtime.MetricCollector]::Collect()
    Update-Metrics $previewSnapshot
    $previewPath = Write-Preview $script:MainWindow $RenderPreview
    $script:NotifyIcon.Dispose()
    Write-Output $previewPath
    exit 0
}

$createdNew = $false
$script:InstanceMutex = [Threading.Mutex]::new($true, 'Local\PerformancePillWidget', [ref]$createdNew)
if (-not $createdNew) {
    $script:NotifyIcon.Dispose()
    exit 0
}

$script:PollTimer.Start()
try {
    [void]$script:MainWindow.ShowDialog()
}
finally {
    if ($createdNew) {
        try { $script:InstanceMutex.ReleaseMutex() } catch {}
    }
    $script:InstanceMutex.Dispose()
    if ($script:NotifyIcon) {
        $script:NotifyIcon.Dispose()
    }
}
