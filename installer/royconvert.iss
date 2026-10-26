; royconvert Windows installer (Inno Setup 6). Built by build-installer.ps1, which passes:
;   AppVersion  version from src\RoyConvert.App\RoyConvert.App.csproj
;   PublishDir  self-contained publish output, with ffmpeg and LibreOffice in tools\
;   OutputDir   where the setup .exe is written
; Installs for all users (Program Files) by default; /CURRENTUSER or the dialog installs for the current user without
; administrator rights.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

[Setup]
AppId={{E042C01D-42E1-4497-BF5C-FE6CE8CE4262}
AppName=royconvert
AppVersion={#AppVersion}
AppPublisher=royconvert
DefaultDirName={autopf}\royconvert
DisableProgramGroupPage=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; .NET 8 needs Windows 10 1607 or later.
MinVersion=10.0.14393
OutputDir={#OutputDir}
OutputBaseFilename=royconvert-{#AppVersion}-setup
SetupIconFile=..\src\RoyConvert.App\Assets\royconvert.ico
UninstallDisplayIcon={app}\royconvert.exe
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; Closes a running royconvert before files are replaced.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
tr.ShellMenu=Sağ tık menüsünü kur
en.ShellMenu=Install the right-click menu
tr.ShellMenuStatus=Sağ tık menüsü kuruluyor...
en.ShellMenuStatus=Installing the right-click menu...

[Tasks]
Name: "shellmenu"; Description: "{cm:ShellMenu}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "royconvert_shell.dll,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
; Explorer keeps the menu handler loaded; if it is in use, it is replaced or removed after a restart.
Source: "{#PublishDir}\royconvert_shell.dll"; DestDir: "{app}"; Flags: ignoreversion restartreplace uninsrestartdelete

[Icons]
Name: "{autoprograms}\royconvert"; Filename: "{app}\royconvert.exe"
Name: "{autodesktop}\royconvert"; Filename: "{app}\royconvert.exe"; Tasks: desktopicon

[Run]
; As the user who started setup: the menu lives in that user's registry (HKCU) and %LOCALAPPDATA%.
Filename: "{app}\royconvert.exe"; Parameters: "--register-shell"; StatusMsg: "{cm:ShellMenuStatus}"; Tasks: shellmenu; Flags: runasoriginaluser runhidden waituntilterminated
Filename: "{app}\royconvert.exe"; Description: "{cm:LaunchProgram,royconvert}"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{app}\royconvert.exe"; Parameters: "--unregister-shell"; RunOnceId: "UnregisterShell"; Flags: runhidden waituntilterminated

[UninstallDelete]
; Left by --register-shell (shell-menu.txt is removed by --unregister-shell above); kept if it holds anything else.
Type: dirifempty; Name: "{localappdata}\royconvert"
