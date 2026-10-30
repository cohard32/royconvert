<#
Builds the Windows installer artifacts\royconvert-<version>-setup.exe:
  1. dotnet publish (Release, win-x64, self-contained: no .NET install needed) into artifacts\publish
  2. ffmpeg from a pinned build, checked against its SHA-256, into artifacts\publish\tools
  3. LibreOffice from a pinned MSI, checked the same way and unpacked (not installed) into artifacts\publish\tools\LibreOffice
  4. Inno Setup compiles installer\royconvert.iss

Needs: .NET 8 SDK, Visual Studio C++ build tools and Windows SDK (context menu handler), Inno Setup 6.
Usage: powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 [-InnoSetup <path to ISCC.exe>]
#>
param([string]$InnoSetup)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue' # the progress bar makes Invoke-WebRequest very slow
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# gyan.dev "essentials" build (GPLv3): has every encoder the presets use (libx264, libvpx, libmp3lame, libvorbis, aac).
$ffmpegVersion = '9.0.2'
$ffmpegUrl = "https://github.com/GyanD/codexffmpeg/releases/download/$ffmpegVersion/ffmpeg-$ffmpegVersion-essentials_build.zip"
$ffmpegSha256 = '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'

# LibreOffice (MPL-2.0) for Office -> PDF. The archive URL stays valid after newer releases replace "stable".
$libreOfficeVersion = '26.8.1.1'
$libreOfficeUrl = "https://downloadarchive.documentfoundation.org/libreoffice/old/$libreOfficeVersion/win/x86_64/LibreOffice_${libreOfficeVersion}_Win_x86-64.msi"
$libreOfficeSha256 = 'c6298b10cfa1748bcbedad94c62ce771e240e5021bfd6d7a967cd571b04b5312'

$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
$cache = Join-Path $artifacts 'cache'
$appProject = Join-Path $root 'src\RoyConvert.App\RoyConvert.App.csproj'

$iscc = @($InnoSetup, "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) was not found; install it or pass -InnoSetup.' }

# Downloads $url into the cache once and returns its path; fails if the file does not match $sha256.
function Get-Pinned([string]$url, [string]$fileName, [string]$sha256) {
    $file = Join-Path $cache $fileName
    if (-not (Test-Path $file)) {
        New-Item -ItemType Directory -Force $cache | Out-Null
        Invoke-WebRequest $url -OutFile "$file.part" -UseBasicParsing
        Move-Item "$file.part" $file -Force
    }
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $sha256) {
        Remove-Item $file
        throw "$fileName does not match the expected SHA-256 (got $hash)"
    }
    return $file
}

[xml]$project = Get-Content $appProject
$version = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if (-not $version) { throw "No <Version> in $appProject" }

Write-Host "== publish royconvert $version"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $appProject -c Release -r win-x64 --self-contained -o $publish -nologo
if ($LASTEXITCODE) { throw "dotnet publish failed ($LASTEXITCODE)" }
foreach ($file in 'royconvert.exe', 'royconvert_shell.dll', 'royconvert_shell.msix') {
    if (-not (Test-Path (Join-Path $publish $file))) { throw "$file is missing from the publish output" }
}

Write-Host "== ffmpeg $ffmpegVersion"
$zip = Get-Pinned $ffmpegUrl "ffmpeg-$ffmpegVersion-essentials_build.zip" $ffmpegSha256

$extract = Join-Path $cache "ffmpeg-$ffmpegVersion"
if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
Expand-Archive $zip $extract
$build = Get-ChildItem $extract -Directory | Select-Object -First 1
$tools = Join-Path $publish 'tools'
New-Item -ItemType Directory -Force $tools | Out-Null
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $publish 'royconvert-LICENSE.txt')
Copy-Item (Join-Path $build.FullName 'bin\ffmpeg.exe') $tools
# GPL: ship the license and where the build and its source come from.
Copy-Item (Join-Path $build.FullName 'LICENSE') (Join-Path $tools 'ffmpeg-LICENSE.txt')
Copy-Item (Join-Path $build.FullName 'README.txt') (Join-Path $tools 'ffmpeg-README.txt')

Write-Host "== LibreOffice $libreOfficeVersion"
$msi = Get-Pinned $libreOfficeUrl "LibreOffice_${libreOfficeVersion}_Win_x86-64.msi" $libreOfficeSha256
# An administrative install only unpacks the MSI: nothing is registered on this machine.
$unpacked = Join-Path $cache "libreoffice-$libreOfficeVersion"
if (-not (Test-Path (Join-Path $unpacked 'program\soffice.exe'))) {
    if (Test-Path $unpacked) { Remove-Item $unpacked -Recurse -Force }
    $msiexec = Start-Process msiexec.exe -ArgumentList '/a', "`"$msi`"", '/qn', "TARGETDIR=`"$unpacked`"" -Wait -PassThru
    if ($msiexec.ExitCode) { throw "Unpacking LibreOffice failed (msiexec $($msiexec.ExitCode))" }
    $soffice = Get-ChildItem $unpacked -Recurse -Filter soffice.exe | Select-Object -First 1
    if (-not $soffice) { throw "soffice.exe is missing from the unpacked LibreOffice" }
    # Keep only the installation folder (the one holding program\), whatever the MSI nests it in.
    $installRoot = $soffice.Directory.Parent.FullName
    if ($installRoot -ne $unpacked) {
        $flat = "$unpacked.flat"
        Move-Item $installRoot $flat
        Remove-Item $unpacked -Recurse -Force
        Move-Item $flat $unpacked
    }
}
$libreOffice = Join-Path $tools 'LibreOffice'
Copy-Item $unpacked $libreOffice -Recurse
# A real install puts these into Windows\System32 and Windows\Fonts. Instead: the Visual C++ runtime next to
# soffice.exe (in case the machine has none) and the fonts where LibreOffice looks for its own ones.
Copy-Item (Join-Path $libreOffice 'System64\*.dll') (Join-Path $libreOffice 'program')
$fonts = Join-Path $libreOffice 'share\fonts\truetype'
New-Item -ItemType Directory -Force $fonts | Out-Null
Move-Item (Join-Path $libreOffice 'Fonts\*') $fonts
# share\extensions: the unpacked MSI holds every bundled extension (58, mostly spelling dictionaries). PDF export needs
# none of them, and registering them into each job's fresh profile made soffice hang.
foreach ($leftover in 'System', 'System64', 'Fonts', '*.msi', 'share\extensions') {
    Remove-Item (Join-Path $libreOffice $leftover) -Recurse -Force
}

Write-Host "== installer"
& $iscc /Q "/DAppVersion=$version" "/DPublishDir=$publish" "/DOutputDir=$artifacts" (Join-Path $PSScriptRoot 'royconvert.iss')
if ($LASTEXITCODE) { throw "Inno Setup failed ($LASTEXITCODE)" }
Write-Host "== done: $(Join-Path $artifacts "royconvert-$version-setup.exe")"
