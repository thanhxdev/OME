param(
    [string]$Version = "1.0.0",
    [string]$OutputFolder = "dist",
    [string]$BuildDir = "",
    [string]$DotNetProject = "samples/dotnet/WpfDemo/WpfDemo.csproj",
    [string]$InnoSetupPath = "",
    [switch]$SkipPackage,
    [switch]$SkipDotNet,
    [switch]$InstallInnoSetup,
    [switch]$Demo,
    [switch]$Production = $true
)

$ErrorActionPreference = "Stop"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " OpenMedia SDK - 1-Click Installer Build Pipeline" -ForegroundColor Cyan
Write-Host " Version: $Version" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path
Push-Location $RepoRoot

try {
    $DistDir = if ([System.IO.Path]::IsPathRooted($OutputFolder)) { $OutputFolder } else { Join-Path $RepoRoot $OutputFolder }
    $IssFilePath = Join-Path $RepoRoot "tools\installer\setup.iss"

    if (!(Test-Path $IssFilePath)) {
        throw "Inno Setup script not found at: $IssFilePath"
    }

    # 1. Chạy quy trình đóng gói (package.ps1) nếu không bị bỏ qua
    if (-not $SkipPackage) {
        Write-Host "`n>>> [STEP 1/2] Packaging binaries, dependencies and assets..." -ForegroundColor Yellow
        $packageScript = Join-Path $PSScriptRoot "package.ps1"

        $packageParams = @{
            Version       = $Version
            OutputFolder  = $OutputFolder
            DotNetProject = $DotNetProject
        }
        if (-not [string]::IsNullOrWhiteSpace($BuildDir)) {
            $packageParams["BuildDir"] = $BuildDir
        }
        if ($SkipDotNet) {
            $packageParams["SkipDotNet"] = $true
        }
        if ($Production) {
            $packageParams["Production"] = $true
        } elseif ($Demo) {
            $packageParams["Demo"] = $true
        }

        & $packageScript @packageParams
        if ($LASTEXITCODE -ne 0) {
            throw "package.ps1 failed with exit code $LASTEXITCODE"
        }
    } else {
        Write-Host "`n>>> [STEP 1/2] Skipping package step (-SkipPackage specified)..." -ForegroundColor Gray
    }

    # 2. Tìm kiếm trình biên dịch Inno Setup (iscc.exe)
    Write-Host "`n>>> [STEP 2/2] Locating Inno Setup 6 Compiler (iscc.exe)..." -ForegroundColor Yellow

    $isccExe = $null

    # 2.1 Kiểm tra tham số chỉ định
    if (-not [string]::IsNullOrWhiteSpace($InnoSetupPath) -and (Test-Path $InnoSetupPath)) {
        $isccExe = $InnoSetupPath
    }

    # 2.2 Kiểm tra trong PATH
    if (-not $isccExe) {
        $cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
        if ($cmd) {
            $isccExe = $cmd.Source
        }
    }

    # 2.3 Kiểm tra các đường dẫn cài đặt tiêu chuẩn trên Windows
    $CandidatePaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
    )
    if (-not $isccExe) {
        foreach ($path in $CandidatePaths) {
            if (Test-Path $path) {
                $isccExe = $path
                break
            }
        }
    }

    # 2.4 Kiểm tra qua Windows Registry
    if (-not $isccExe) {
        $regKeys = @(
            "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
            "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
            "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1"
        )
        foreach ($rk in $regKeys) {
            if (Test-Path $rk) {
                $loc = (Get-ItemProperty -Path $rk -ErrorAction SilentlyContinue).InstallLocation
                if ($loc -and (Test-Path (Join-Path $loc "ISCC.exe"))) {
                    $isccExe = Join-Path $loc "ISCC.exe"
                    break
                }
            }
        }
    }

    # 2.5 Nếu chưa có và người dùng bật switch -InstallInnoSetup, tự động cài qua winget
    if (-not $isccExe -and $InstallInnoSetup) {
        Write-Host "Inno Setup not detected. Attempting automatic installation via winget..." -ForegroundColor Cyan
        & winget install JRSoftware.InnoSetup -e --accept-source-agreements --accept-package-agreements
        # Thử tìm lại sau khi cài đặt
        foreach ($path in $CandidatePaths) {
            if (Test-Path $path) {
                $isccExe = $path
                break
            }
        }
    }

    # 2.6 Báo lỗi nếu không tìm thấy
    if (-not $isccExe) {
        Write-Host ""
        Write-Host "============================================================" -ForegroundColor Red
        Write-Host " [ERROR] Inno Setup 6 (ISCC.exe) was not found!" -ForegroundColor Red
        Write-Host "============================================================" -ForegroundColor Red
        Write-Host "To compile the installer, Inno Setup 6 must be installed." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "You can easily install it by running either:" -ForegroundColor Yellow
        Write-Host "  1) winget install JRSoftware.InnoSetup -e" -ForegroundColor White
        Write-Host "  2) Or re-run this script with the -InstallInnoSetup flag:" -ForegroundColor White
        Write-Host "     .\tools\scripts\build_installer.ps1 -InstallInnoSetup" -ForegroundColor White
        Write-Host "  3) Or download directly from: https://jrsoftware.org/isdl.php" -ForegroundColor White
        Write-Host ""
        throw "Inno Setup 6 compiler (iscc.exe) is required to build the installer."
    }

    Write-Host "       Found Inno Setup Compiler: $isccExe" -ForegroundColor Green

    # 3. Xác định thư mục Staging cho installer
    $PackageName = "OpenMedia-v$Version"
    if ($Production) {
        $PackageName += "-Production"
    } elseif ($Demo) {
        $PackageName += "-Demo"
    }
    $StagingDir = Join-Path $DistDir $PackageName

    if (!(Test-Path $StagingDir)) {
        throw "Staging directory not found: $StagingDir. Run package.ps1 first."
    }

    # 4. Thực thi ISCC để biên dịch bộ cài đặt
    Write-Host "`n>>> Compiling installer with ISCC..." -ForegroundColor Yellow
    Write-Host "       AppVersion : $Version" -ForegroundColor Gray
    Write-Host "       SourceDir  : $StagingDir" -ForegroundColor Gray
    Write-Host "       OutputDir  : $DistDir" -ForegroundColor Gray

    $isccArgs = @(
        "/DMyAppVersion=$Version",
        "/DSourceDir=$StagingDir",
        "/DOutputDir=$DistDir",
        $IssFilePath
    )

    Write-Host "       Executing: & `"$isccExe`" $($isccArgs -join ' ')" -ForegroundColor Gray
    & $isccExe @isccArgs
    if ($LASTEXITCODE -ne 0) {
        throw "ISCC compilation failed with exit code $LASTEXITCODE"
    }

    # 5. Xác minh file cài đặt đầu ra
    $SetupExe = Join-Path $DistDir "OpenMedia_Setup_v$Version.exe"
    if (Test-Path $SetupExe) {
        $setupItem = Get-Item $SetupExe
        $fileSizeMB = [math]::Round($setupItem.Length / 1MB, 2)
        $hash = (Get-FileHash -Path $SetupExe -Algorithm SHA256).Hash

        Write-Host ""
        Write-Host "============================================================" -ForegroundColor Green
        Write-Host " INSTALLER CREATED SUCCESSFULLY!" -ForegroundColor Green
        Write-Host "============================================================" -ForegroundColor Green
        Write-Host " File:      $SetupExe" -ForegroundColor White
        Write-Host " Size:      $fileSizeMB MB" -ForegroundColor White
        Write-Host " SHA-256:   $hash" -ForegroundColor White
        Write-Host "============================================================" -ForegroundColor Green
    } else {
        Write-Warning "Setup output file not found at expected location: $SetupExe"
    }
}
finally {
    Pop-Location
}
