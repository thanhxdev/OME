param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("SRT_ENCODE", "SRT_DECODE", "WEBRTC_ENCODE", "WEBRTC_DECODE", "OME_PLAYOUT", "SRT_GATEWAY")]
    [string]$AppName,

    [string]$Version = "2.0.0",
    [string]$OutputFolder = "dist",
    [string]$InnoSetupPath = "",
    [switch]$SkipPublish,
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " OpenMedia SDK - Automated App Packaging Pipeline" -ForegroundColor Cyan
Write-Host " Target Application: $AppName (v$Version)" -ForegroundColor Cyan
Write-Host " Architecture: Modular Client (.NET 10 Self-Contained win-x64)" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path
Push-Location $RepoRoot

try {
    # 1. Map application project path and GUID
    $ProjectMap = @{
        "SRT_ENCODE" = @{
            "Csproj"  = "samples\platform\SRT_ENCODE\SRT_ENCODE.csproj"
            "AppGuid" = "{{B3C84260-6FF1-4D6C-81A1-21B91E345678}}"
        }
        "SRT_DECODE" = @{
            "Csproj"  = "samples\platform\SRT_DECODE\SRT_DECODE.csproj"
            "AppGuid" = "{{E7D91370-7AA2-4E7D-92B2-32C92E456789}}"
        }
        "WEBRTC_ENCODE" = @{
            "Csproj"  = "samples\platform\WEBRTC_ENCODE\WEBRTC_ENCODE.csproj"
            "AppGuid" = "{{C4D95371-8BB3-4E7D-92C2-43DA3E567890}}"
        }
        "WEBRTC_DECODE" = @{
            "Csproj"  = "samples\platform\WEBRTC_DECODE\WEBRTC_DECODE.csproj"
            "AppGuid" = "{{F8EA2481-9CC4-4F8E-A3D3-54EB4F678901}}"
        }
        "OME_PLAYOUT" = @{
            "Csproj"  = "samples\platform\OME_PLAYOUT\OME_PLAYOUT.csproj"
            "AppGuid" = "{{D4E5F6A1-B2C3-4D5E-8F9A-1B2C3D4E5F6A}}"
        }
        "SRT_GATEWAY" = @{
            "Csproj"  = "samples\platform\SRT_GATEWAY\SRT_GATEWAY.csproj"
            "AppGuid" = "{{A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D}}"
        }
    }

    $appConfig = $ProjectMap[$AppName]
    $csprojRelative = $appConfig["Csproj"]
    $appGuid = $appConfig["AppGuid"]
    $csprojPath = Join-Path $RepoRoot $csprojRelative

    if (-not (Test-Path $csprojPath)) {
        throw "Project file not found at: $csprojPath"
    }

    # 2. Prepare staging directories
    $DistDir = if ([System.IO.Path]::IsPathRooted($OutputFolder)) { $OutputFolder } else { Join-Path $RepoRoot $OutputFolder }
    if (-not (Test-Path $DistDir)) {
        New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
    }

    $AppStagingDir = Join-Path $DistDir "apps\$AppName"
    Write-Host "[1/4] Preparing application staging directory: $AppStagingDir" -ForegroundColor Yellow
    if (Test-Path $AppStagingDir) {
        Remove-Item -Recurse -Force $AppStagingDir
    }
    New-Item -ItemType Directory -Force -Path $AppStagingDir | Out-Null

    # 3. Publish .NET 10 Self-Contained application
    if (-not $SkipPublish) {
        Write-Host "[2/4] Publishing $AppName (.NET 10 Self-Contained win-x64)..." -ForegroundColor Yellow
        Write-Host "       Project: $csprojPath" -ForegroundColor Gray

        $publishArgs = @(
            "publish",
            $csprojPath,
            "-c", "Release",
            "-r", "win-x64",
            "--self-contained", "true",
            "-p:PublishReadyToRun=true",
            "-p:ErrorOnDuplicatePublishOutputFiles=false",
            "-o", $AppStagingDir
        )

        & dotnet @publishArgs
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE"
        }

        # Clean debug symbols from staging
        Get-ChildItem -Path $AppStagingDir -File | ForEach-Object {
            if ($_.Name -like "*.pdb" -or $_.Name -like "*test*") {
                Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue
            }
        }
    } else {
        Write-Host "[2/4] Skipping publish (-SkipPublish specified)..." -ForegroundColor Gray
    }

    # 4. Optional ZIP packaging
    if (-not $NoZip) {
        $ZipPath = Join-Path $DistDir "${AppName}_v$Version.zip"
        if (Test-Path $ZipPath) {
            Remove-Item -Force $ZipPath
        }
        Write-Host "[3/4] Creating ZIP archive: $ZipPath..." -ForegroundColor Yellow
        Compress-Archive -Path "$AppStagingDir\*" -DestinationPath $ZipPath
    } else {
        Write-Host "[3/4] Skipping ZIP archive (-NoZip specified)..." -ForegroundColor Gray
    }

    # 5. Compile Application Setup with Inno Setup
    Write-Host "[4/4] Compiling Application Installer with Inno Setup..." -ForegroundColor Yellow
    $IssTemplate = Join-Path $RepoRoot "tools\installer\app_template.iss"
    if (-not (Test-Path $IssTemplate)) {
        throw "Inno Setup template not found at: $IssTemplate"
    }

    # Locate ISCC.exe
    $isccExe = $null
    if ((-not [string]::IsNullOrWhiteSpace($InnoSetupPath)) -and (Test-Path $InnoSetupPath)) {
        $isccExe = $InnoSetupPath
    }

    if (-not $isccExe) {
        $cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
        if ($cmd) {
            $isccExe = $cmd.Source
        }
    }

    $CandidatePaths = @(
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    )
    if (-not $isccExe) {
        foreach ($path in $CandidatePaths) {
            if (Test-Path $path) {
                $isccExe = $path
                break
            }
        }
    }

    if (-not $isccExe) {
        throw "Inno Setup 6 compiler (iscc.exe) not found. Please ensure Inno Setup 6 is installed."
    }

    Write-Host "       Found Inno Setup Compiler: $isccExe" -ForegroundColor Green

    $SetupBaseFilename = "${AppName}_Setup"
    $isccArgs = @(
        "/DMyAppName=$AppName",
        "/DMyAppExeName=$AppName.exe",
        "/DMyAppVersion=$Version",
        "/DAppGuid=$appGuid",
        "/DSourceDir=$AppStagingDir",
        "/DOutputDir=$DistDir",
        "/DOutputBaseFilename=$SetupBaseFilename",
        $IssTemplate
    )

    Write-Host "       Executing: & `"$isccExe`" $($isccArgs -join ' ')" -ForegroundColor Gray
    & $isccExe @isccArgs
    if ($LASTEXITCODE -ne 0) {
        throw "ISCC compilation failed with exit code $LASTEXITCODE"
    }

    $SetupExe = Join-Path $DistDir "$SetupBaseFilename.exe"
    if (Test-Path $SetupExe) {
        $setupItem = Get-Item $SetupExe
        $fileSizeMB = [math]::Round($setupItem.Length / 1MB, 2)
        $hash = (Get-FileHash -Path $SetupExe -Algorithm SHA256).Hash

        # Also mirror to dist\installers
        $InstallersDir = Join-Path $DistDir "installers"
        if (-not (Test-Path $InstallersDir)) {
            New-Item -ItemType Directory -Force -Path $InstallersDir | Out-Null
        }
        Copy-Item -Path $SetupExe -Destination (Join-Path $InstallersDir "$SetupBaseFilename.exe") -Force

        Write-Host ""
        Write-Host "============================================================" -ForegroundColor Green
        Write-Host " [OK] $AppName INSTALLER CREATED SUCCESSFULLY!" -ForegroundColor Green
        Write-Host "============================================================" -ForegroundColor Green
        Write-Host " File:      $SetupExe" -ForegroundColor White
        Write-Host " Mirror:    $(Join-Path $InstallersDir "$SetupBaseFilename.exe")" -ForegroundColor White
        Write-Host " Size:      $fileSizeMB MB" -ForegroundColor White
        Write-Host " SHA-256:   $hash" -ForegroundColor White
        Write-Host "============================================================" -ForegroundColor Green
    } else {
        Write-Warning "Setup output file not found at expected location: $SetupExe"
    }

    Write-Host "[OK] Application packaging completed." -ForegroundColor Green
}
finally {
    Pop-Location
}
