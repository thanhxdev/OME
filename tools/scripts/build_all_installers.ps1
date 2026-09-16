param(
    [string]$Version = "1.0.0",
    [string]$OutputFolder = "dist",
    [string]$BuildDir = "",
    [string]$InnoSetupPath = "",
    [switch]$SkipSDK,
    [switch]$SkipApps
)

$ErrorActionPreference = "Stop"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " OpenMedia SDK - 1-Click All Installers Build Pipeline" -ForegroundColor Cyan
Write-Host " Version: $Version" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path
Push-Location $RepoRoot

$startTime = Get-Date
$results = @()

try {
    $DistDir = if ([System.IO.Path]::IsPathRooted($OutputFolder)) { $OutputFolder } else { Join-Path $RepoRoot $OutputFolder }
    if (-not (Test-Path $DistDir)) {
        New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
    }

    # STEP 1: Build OpenMedia SDK Runtime Installer
    if (-not $SkipSDK) {
        Write-Host "`n>>> [1/3] Packaging OpenMedia SDK Runtime..." -ForegroundColor Magenta
        $sdkScript = Join-Path $PSScriptRoot "package_sdk.ps1"
        $sdkParams = @{
            Version      = $Version
            OutputFolder = $OutputFolder
        }
        if (-not [string]::IsNullOrWhiteSpace($BuildDir)) {
            $sdkParams["BuildDir"] = $BuildDir
        }
        if (-not [string]::IsNullOrWhiteSpace($InnoSetupPath)) {
            $sdkParams["InnoSetupPath"] = $InnoSetupPath
        }

        & $sdkScript @sdkParams
        if ($LASTEXITCODE -ne 0) {
            throw "package_sdk.ps1 failed with exit code $LASTEXITCODE"
        }

        $sdkExe = Join-Path $DistDir "OpenMedia_SDK_Setup_v$Version.exe"
        if (Test-Path $sdkExe) {
            $item = Get-Item $sdkExe
            $results += [PSCustomObject]@{
                Component = "OpenMedia SDK Runtime"
                File      = $item.Name
                SizeMB    = [math]::Round($item.Length / 1MB, 2)
                SHA256    = (Get-FileHash -Path $sdkExe -Algorithm SHA256).Hash.Substring(0, 16) + "..."
                Status    = "[OK]"
            }
        }
    } else {
        Write-Host "`n>>> [1/3] Skipping OpenMedia SDK Runtime (-SkipSDK specified)..." -ForegroundColor Gray
    }

    # STEP 2: Build SRT_ENCODE Application Installer
    if (-not $SkipApps) {
        Write-Host "`n>>> [2/3] Packaging SRT_ENCODE Application..." -ForegroundColor Magenta
        $appScript = Join-Path $PSScriptRoot "package_app.ps1"
        $encodeParams = @{
            AppName      = "SRT_ENCODE"
            Version      = $Version
            OutputFolder = $OutputFolder
        }
        if (-not [string]::IsNullOrWhiteSpace($InnoSetupPath)) {
            $encodeParams["InnoSetupPath"] = $InnoSetupPath
        }

        & $appScript @encodeParams
        if ($LASTEXITCODE -ne 0) {
            throw "package_app.ps1 (SRT_ENCODE) failed with exit code $LASTEXITCODE"
        }

        $encodeExe = Join-Path $DistDir "SRT_ENCODE_Setup.exe"
        if (Test-Path $encodeExe) {
            $item = Get-Item $encodeExe
            $results += [PSCustomObject]@{
                Component = "SRT_ENCODE App"
                File      = $item.Name
                SizeMB    = [math]::Round($item.Length / 1MB, 2)
                SHA256    = (Get-FileHash -Path $encodeExe -Algorithm SHA256).Hash.Substring(0, 16) + "..."
                Status    = "[OK]"
            }
        }

        # STEP 3: Build SRT_DECODE Application Installer
        Write-Host "`n>>> [3/5] Packaging SRT_DECODE Application..." -ForegroundColor Magenta
        $decodeParams = @{
            AppName      = "SRT_DECODE"
            Version      = $Version
            OutputFolder = $OutputFolder
        }
        if (-not [string]::IsNullOrWhiteSpace($InnoSetupPath)) {
            $decodeParams["InnoSetupPath"] = $InnoSetupPath
        }

        & $appScript @decodeParams
        if ($LASTEXITCODE -ne 0) {
            throw "package_app.ps1 (SRT_DECODE) failed with exit code $LASTEXITCODE"
        }

        $decodeExe = Join-Path $DistDir "SRT_DECODE_Setup.exe"
        if (Test-Path $decodeExe) {
            $item = Get-Item $decodeExe
            $results += [PSCustomObject]@{
                Component = "SRT_DECODE App"
                File      = $item.Name
                SizeMB    = [math]::Round($item.Length / 1MB, 2)
                SHA256    = (Get-FileHash -Path $decodeExe -Algorithm SHA256).Hash.Substring(0, 16) + "..."
                Status    = "[OK]"
            }
        }

        # STEP 4: Build WEBRTC_ENCODE Application Installer
        Write-Host "`n>>> [4/5] Packaging WEBRTC_ENCODE Application..." -ForegroundColor Magenta
        $webrtcEncodeParams = @{
            AppName      = "WEBRTC_ENCODE"
            Version      = $Version
            OutputFolder = $OutputFolder
        }
        if (-not [string]::IsNullOrWhiteSpace($InnoSetupPath)) {
            $webrtcEncodeParams["InnoSetupPath"] = $InnoSetupPath
        }

        & $appScript @webrtcEncodeParams
        if ($LASTEXITCODE -ne 0) {
            throw "package_app.ps1 (WEBRTC_ENCODE) failed with exit code $LASTEXITCODE"
        }

        $webrtcEncodeExe = Join-Path $DistDir "WEBRTC_ENCODE_Setup.exe"
        if (Test-Path $webrtcEncodeExe) {
            $item = Get-Item $webrtcEncodeExe
            $results += [PSCustomObject]@{
                Component = "WEBRTC_ENCODE App"
                File      = $item.Name
                SizeMB    = [math]::Round($item.Length / 1MB, 2)
                SHA256    = (Get-FileHash -Path $webrtcEncodeExe -Algorithm SHA256).Hash.Substring(0, 16) + "..."
                Status    = "[OK]"
            }
        }

        # STEP 5: Build WEBRTC_DECODE Application Installer
        Write-Host "`n>>> [5/5] Packaging WEBRTC_DECODE Application..." -ForegroundColor Magenta
        $webrtcDecodeParams = @{
            AppName      = "WEBRTC_DECODE"
            Version      = $Version
            OutputFolder = $OutputFolder
        }
        if (-not [string]::IsNullOrWhiteSpace($InnoSetupPath)) {
            $webrtcDecodeParams["InnoSetupPath"] = $InnoSetupPath
        }

        & $appScript @webrtcDecodeParams
        if ($LASTEXITCODE -ne 0) {
            throw "package_app.ps1 (WEBRTC_DECODE) failed with exit code $LASTEXITCODE"
        }

        $webrtcDecodeExe = Join-Path $DistDir "WEBRTC_DECODE_Setup.exe"
        if (Test-Path $webrtcDecodeExe) {
            $item = Get-Item $webrtcDecodeExe
            $results += [PSCustomObject]@{
                Component = "WEBRTC_DECODE App"
                File      = $item.Name
                SizeMB    = [math]::Round($item.Length / 1MB, 2)
                SHA256    = (Get-FileHash -Path $webrtcDecodeExe -Algorithm SHA256).Hash.Substring(0, 16) + "..."
                Status    = "[OK]"
            }
        }
    } else {
        Write-Host "`n>>> Skipping Applications (-SkipApps specified)..." -ForegroundColor Gray
    }

    $elapsed = (Get-Date) - $startTime
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host " ALL INSTALLERS BUILT SUCCESSFULLY! (Total Time: $($elapsed.Minutes)m $($elapsed.Seconds)s)" -ForegroundColor Green
    Write-Host " Output Directory: $DistDir" -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor Green

    $results | Format-Table -AutoSize
}
finally {
    Pop-Location
}
