#ifndef AppVersion
  #error AppVersion must be supplied by Build-Installer.ps1
#endif

#ifndef SourceDir
  #error SourceDir must be supplied by Build-Installer.ps1
#endif

#ifndef RepoRoot
  #error RepoRoot must be supplied by Build-Installer.ps1
#endif

#define AppName "OPS Monitor"
#define AppPublisher "seNkoKG"
#define AppUrl "https://senkokg.github.io/ops-monitor/"
#define WidgetExe "OpsMonitor.Widget.exe"
#define StudioExe "OpsMonitor.Studio.exe"

[Setup]
AppId={{0E77B119-8DE0-4B54-BA24-C91E0764AD19}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL=https://github.com/seNkoKG/ops-monitor/issues
AppUpdatesURL=https://github.com/seNkoKG/ops-monitor/releases/latest
AppCopyright=Copyright (c) 2026 seNkoKG contributors
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Windows installer
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\OPS Monitor
DefaultGroupName=OPS Monitor
DisableDirPage=auto
DisableProgramGroupPage=auto
AllowNoIcons=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
WizardStyle=modern
WizardResizable=no
SetupIconFile={#RepoRoot}\native\assets\OpsMonitor.ico
UninstallDisplayIcon={app}\{#WidgetExe}
LicenseFile={#RepoRoot}\LICENSE
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter={#WidgetExe},{#StudioExe},OpsMonitor.Core.dll
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
ChangesEnvironment=no
OutputBaseFilename=OPS-Monitor-v{#AppVersion}-Setup

[Tasks]
Name: "startup"; Description: "Start OPS Monitor automatically when I sign in"; GroupDescription: "Startup:"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\OPS Monitor Widget"; Filename: "{app}\{#WidgetExe}"; WorkingDir: "{app}"; Comment: "Open the OPS Monitor desktop widget"
Name: "{group}\OPS Monitor Studio"; Filename: "{app}\{#StudioExe}"; WorkingDir: "{app}"; Comment: "Customize OPS Monitor"
Name: "{group}\Check for OPS Monitor updates"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoLogo -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File ""{app}\Update.ps1"" -Interactive"; WorkingDir: "{app}"; Comment: "Check for and install a verified OPS Monitor update"
Name: "{group}\Enable CPU Temperature"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoLogo -NoProfile -ExecutionPolicy Bypass -File ""{app}\Enable-CpuTemperature.ps1"""; WorkingDir: "{app}"; Comment: "Enable the secure OPS Monitor CPU temperature sensor"
Name: "{group}\Disable CPU Temperature"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoLogo -NoProfile -ExecutionPolicy Bypass -File ""{app}\Disable-CpuTemperature.ps1"""; WorkingDir: "{app}"; Comment: "Remove the OPS Monitor CPU sensor task and broker"
Name: "{group}\Uninstall OPS Monitor"; Filename: "{uninstallexe}"; Comment: "Remove OPS Monitor while preserving saved settings"
Name: "{userdesktop}\OPS Monitor"; Filename: "{app}\{#WidgetExe}"; WorkingDir: "{app}"; Comment: "Open the OPS Monitor desktop widget"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "OPS Monitor Widget"; ValueData: """{app}\{#WidgetExe}"""; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "OPS Monitor Widget"; Tasks: not startup; Flags: deletevalue

[Run]
Filename: "{app}\{#WidgetExe}"; Description: "Launch OPS Monitor"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoLogo -NoProfile -ExecutionPolicy Bypass -File ""{app}\Installer-CloseApps.ps1"" -InstallDirectory ""{app}"""; Flags: runhidden waituntilterminated; RunOnceId: "CloseOpsMonitorApps"
