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
Write-Host " OpenMedia - 1-Click Master Build & Packaging Pipeline" -ForegroundColor Cyan
Write-Host " Version: $Version" -ForegroundColor Cyan
Write-Host " Output:  $OutputFolder/installers" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path
Push-Location $RepoRoot

$startTime = Get-Date

try {
    $DistDir = if ([System.IO.Path]::IsPathRooted($OutputFolder)) { $OutputFolder } else { Join-Path $RepoRoot $OutputFolder }
    $InstallersDir = Join-Path $DistDir "installers"

    if (-not (Test-Path $InstallersDir)) {
        New-Item -ItemType Directory -Force -Path $InstallersDir | Out-Null
    }

    # =========================================================================
    # 1. Package OpenMedia SDK Runtime (C++ Engine, Native DLLs, FFmpeg, CLI)
    # =========================================================================
    if (-not $SkipSDK) {
        Write-Host "`n>>> [1/2] BUILDING OPENMEDIA SDK PACKAGE..." -ForegroundColor Magenta
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
    } else {
        Write-Host "`n>>> [1/2] Skipping SDK Package (-SkipSDK specified)..." -ForegroundColor Gray
    }

    # =========================================================================
    # 2. Package Client Applications (SRT & WebRTC)
    # =========================================================================
    if (-not $SkipApps) {
        Write-Host "`n>>> [2/2] BUILDING CLIENT APPLICATIONS (SRT & WebRTC)..." -ForegroundColor Magenta
        $appsScript = Join-Path $PSScriptRoot "package_apps.ps1"
        $appsParams = @{
            Version      = $Version
            OutputFolder = $OutputFolder
        }
        if (-not [string]::IsNullOrWhiteSpace($InnoSetupPath)) {
            $appsParams["InnoSetupPath"] = $InnoSetupPath
        }

        & $appsScript @appsParams
        if ($LASTEXITCODE -ne 0) {
            throw "package_apps.ps1 failed with exit code $LASTEXITCODE"
        }
    } else {
        Write-Host "`n>>> [2/2] Skipping Applications (-SkipApps specified)..." -ForegroundColor Gray
    }

    # =========================================================================
    # 3. Verification & Summary Report
    # =========================================================================
    $TargetInstallers = @(
        "OpenMedia_SDK_Setup.exe",
        "SRT_ENCODE_Setup.exe",
        "SRT_DECODE_Setup.exe",
        "WEBRTC_ENCODE_Setup.exe",
        "WEBRTC_DECODE_Setup.exe",
        "OME_PLAYOUT_Setup.exe"
    )

    $summary = @()
    $allPresent = $true

    foreach ($file in $TargetInstallers) {
        $filePath = Join-Path $InstallersDir $file
        if (Test-Path $filePath) {
            $item = Get-Item $filePath
            $sizeMB = [math]::Round($item.Length / 1MB, 2)
            $hash = (Get-FileHash -Path $filePath -Algorithm SHA256).Hash
            $summary += [PSCustomObject]@{
                Installer = $file
                SizeMB    = $sizeMB
                SHA256    = $hash
                Status    = "[READY]"
            }
        } else {
            $allPresent = $false
            $summary += [PSCustomObject]@{
                Installer = $file
                SizeMB    = 0
                SHA256    = "NOT FOUND"
                Status    = "[MISSING]"
            }
        }
    }

    $elapsed = (Get-Date) - $startTime

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host " MASTER BUILD PIPELINE COMPLETED!" -ForegroundColor Green
    Write-Host " Total Elapsed: $($elapsed.Minutes)m $($elapsed.Seconds)s" -ForegroundColor Green
    Write-Host " Installer Location: $InstallersDir" -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor Green

    $summary | Format-Table -AutoSize

    if (-not $allPresent) {
        throw "One or more installers were not generated in $InstallersDir!"
    }

    Write-Host "[OK] All $($TargetInstallers.Count) installers generated and verified successfully." -ForegroundColor Green
}
finally {
    Pop-Location
}
