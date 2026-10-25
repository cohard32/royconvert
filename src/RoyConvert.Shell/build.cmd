@echo off
rem Builds royconvert_shell.dll (x64) with the newest Visual Studio C++ tools and packs the sparse package
rem royconvert_shell.msix (Windows 11 menu) with makeappx from the Windows SDK: build.cmd <output directory>
setlocal
set "OUT=%~1"
if "%OUT%"=="" set "OUT=%~dp0bin"
for /f "usebackq delims=" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VS=%%i"
if not defined VS (
  echo error: Visual Studio C++ build tools ^(x64^) were not found. 1>&2
  exit /b 1
)
rem vcvars looks for vswhere on PATH.
set "PATH=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer;%PATH%"
call "%VS%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
if not exist "%OUT%\obj" mkdir "%OUT%\obj"
cl /nologo /std:c++17 /O2 /W4 /WX /EHsc /MT /DUNICODE /D_UNICODE /LD "%~dp0RoyConvertShell.cpp" ^
   /Fo"%OUT%\obj\\" /Fe"%OUT%\royconvert_shell.dll" ^
   /link /DEF:"%~dp0RoyConvertShell.def" shlwapi.lib shell32.lib ole32.lib user32.lib gdi32.lib runtimeobject.lib || exit /b 1
rem The manifest is checked against the schema; /nv skips the check that royconvert.exe is inside the package,
rem which a sparse package never has.
"%WindowsSdkVerBinPath%x64\makeappx.exe" pack /o /nv /d "%~dp0package" /p "%OUT%\royconvert_shell.msix" >nul || exit /b 1
