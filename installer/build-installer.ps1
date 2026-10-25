<#
Builds the Windows installer artifacts\royconvert-<version>-setup.exe:
  1. dotnet publish (Release, win-x64, self-contained: no .NET install needed) into artifacts\publish
  2. ffmpeg from a pinned build, checked against its SHA-256, into artifacts\publish\tools
  3. Inno Setup compiles installer\royconvert.iss

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

$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
$cache = Join-Path $artifacts 'cache'
$appProject = Join-Path $root 'src\RoyConvert.App\RoyConvert.App.csproj'

$iscc = @($InnoSetup, "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) was not found; install it or pass -InnoSetup.' }

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
$zip = Join-Path $cache "ffmpeg-$ffmpegVersion-essentials_build.zip"
if (-not (Test-Path $zip)) {
    New-Item -ItemType Directory -Force $cache | Out-Null
    Invoke-WebRequest $ffmpegUrl -OutFile "$zip.part" -UseBasicParsing
    Move-Item "$zip.part" $zip -Force
}
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $ffmpegSha256) {
    Remove-Item $zip
    throw "ffmpeg download does not match the expected SHA-256 (got $hash)"
}

$extract = Join-Path $cache "ffmpeg-$ffmpegVersion"
if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
Expand-Archive $zip $extract
$build = Get-ChildItem $extract -Directory | Select-Object -First 1
$tools = Join-Path $publish 'tools'
New-Item -ItemType Directory -Force $tools | Out-Null
Copy-Item (Join-Path $build.FullName 'bin\ffmpeg.exe') $tools
# GPL: ship the license and where the build and its source come from.
Copy-Item (Join-Path $build.FullName 'LICENSE') (Join-Path $tools 'ffmpeg-LICENSE.txt')
Copy-Item (Join-Path $build.FullName 'README.txt') (Join-Path $tools 'ffmpeg-README.txt')

Write-Host "== installer"
& $iscc /Q "/DAppVersion=$version" "/DPublishDir=$publish" "/DOutputDir=$artifacts" (Join-Path $PSScriptRoot 'royconvert.iss')
if ($LASTEXITCODE) { throw "Inno Setup failed ($LASTEXITCODE)" }
Write-Host "== done: $(Join-Path $artifacts "royconvert-$version-setup.exe")"
