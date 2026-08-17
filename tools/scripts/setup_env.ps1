param(
    [switch]$DownloadSDKs,
    [switch]$SetupVcpkg,
    [switch]$All
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path "$PSScriptRoot/../..").Path

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  OpenMedia SDK Environment Setup" -ForegroundColor Cyan
Write-Host "  Project: $ProjectRoot" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan

# Setup vcpkg
if ($SetupVcpkg -or $All) {
    $VcpkgDir = Join-Path $ProjectRoot "third_party/vcpkg"

    if (-not (Test-Path $VcpkgDir)) {
        Write-Host "`nCloning vcpkg..." -ForegroundColor Cyan
        git clone https://github.com/microsoft/vcpkg.git $VcpkgDir
    }

    Write-Host "Bootstrapping vcpkg..." -ForegroundColor Cyan
    & "$VcpkgDir/bootstrap-vcpkg.bat"

    Write-Host "Installing dependencies..." -ForegroundColor Cyan
    Push-Location $ProjectRoot
    & "$VcpkgDir/vcpkg" install --triplet x64-windows
    Pop-Location
}

# Create required directories
Write-Host "`nCreating directory structure..." -ForegroundColor Cyan
$dirs = @(
    "third_party/ffmpeg/include",
    "third_party/ffmpeg/lib/x64-windows",
    "third_party/ndi_sdk/include",
    "third_party/ndi_sdk/lib",
    "third_party/decklink_sdk/include",
    "third_party/aja_sdk/include",
    "third_party/aja_sdk/lib",
    "third_party/magewell_sdk/include",
    "third_party/magewell_sdk/lib",
    "third_party/cef/include",
    "third_party/nvidia_codec_sdk",
    "third_party/intel_onevpl",
    "plugins/examples",
    "plugins/builtin",
    "samples/cpp",
    "samples/dotnet",
    "dist/demo/bin",
    "dist/demo/lib",
    "dist/production/bin",
    "dist/production/lib"
)

foreach ($dir in $dirs) {
    $fullPath = Join-Path $ProjectRoot $dir
    if (-not (Test-Path $fullPath)) {
        New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
        Write-Host "  Created: $dir" -ForegroundColor Gray
    }
}

Write-Host "`nEnvironment setup complete!" -ForegroundColor Green

if ($DownloadSDKs -or $All) {
    Write-Host "`nDownloading and setting up SDKs..." -ForegroundColor Cyan
    
    # Auto-download FFmpeg
    $FfmpegUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip"
    $FfmpegZip = Join-Path $ProjectRoot "third_party/ffmpeg.zip"
    $FfmpegDir = Join-Path $ProjectRoot "third_party/ffmpeg"
    
    if (-not (Test-Path "$FfmpegDir/include")) {
        Write-Host "  Downloading FFmpeg from $FfmpegUrl..." -ForegroundColor Gray
        Invoke-WebRequest -Uri $FfmpegUrl -OutFile $FfmpegZip
        Write-Host "  Extracting FFmpeg..." -ForegroundColor Gray
        Expand-Archive -Path $FfmpegZip -DestinationPath "$ProjectRoot/third_party/ffmpeg_temp" -Force
        
        $ExtractedFolder = Get-ChildItem -Path "$ProjectRoot/third_party/ffmpeg_temp" -Directory | Select-Object -First 1
        
        Copy-Item -Path "$($ExtractedFolder.FullName)/include" -Destination $FfmpegDir -Recurse -Force
        Copy-Item -Path "$($ExtractedFolder.FullName)/lib" -Destination $FfmpegDir -Recurse -Force
        Copy-Item -Path "$($ExtractedFolder.FullName)/bin" -Destination $FfmpegDir -Recurse -Force
        
        Remove-Item -Path $FfmpegZip -Force
        Remove-Item -Path "$ProjectRoot/third_party/ffmpeg_temp" -Recurse -Force
        Write-Host "  FFmpeg setup completed." -ForegroundColor Green
    } else {
        Write-Host "  FFmpeg already exists. Skipping download." -ForegroundColor Gray
    }

    Write-Host "`n[NOTE] Other SDK downloads require manual steps (license/login required):" -ForegroundColor Yellow
    Write-Host "  1. NDI SDK:      https://ndi.video/for-developers/ndi-sdk/" -ForegroundColor Gray
    Write-Host "  2. DeckLink SDK: https://www.blackmagicdesign.com/developer/" -ForegroundColor Gray
    Write-Host "  3. CEF:          https://cef-builds.spotifycdn.com/index.html" -ForegroundColor Gray
}
