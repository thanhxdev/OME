param(
    [string]$Version = "2.0.0",
    [string]$OutputFolder = "dist",
    [string]$InnoSetupPath = "",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " OpenMedia Apps - Packaging Pipeline (SRT & WebRTC)" -ForegroundColor Cyan
Write-Host " Version: $Version" -ForegroundColor Cyan
Write-Host " Architecture: Modular Client (.NET 10 Self-Contained win-x64)" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path
Push-Location $RepoRoot

$results = @()

try {
    # 1. Output & Installer Directories
    $DistDir = if ([System.IO.Path]::IsPathRooted($OutputFolder)) { $OutputFolder } else { Join-Path $RepoRoot $OutputFolder }
    $InstallersDir = Join-Path $DistDir "installers"
    
    if (-not (Test-Path $DistDir)) {
        New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
    }
    if (-not (Test-Path $InstallersDir)) {
        New-Item -ItemType Directory -Force -Path $InstallersDir | Out-Null
    }

    # 2. Locate Inno Setup Compiler (ISCC.exe)
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
        throw "Inno Setup 6 compiler (iscc.exe) not found. Please install Inno Setup 6 or provide -InnoSetupPath."
    }
    Write-Host "Inno Setup Compiler: $isccExe" -ForegroundColor Green

    # 3. Application configurations
    $Apps = @(
        @{
            Name     = "SRT_ENCODE"
            Csproj   = Join-Path $RepoRoot "samples\platform\SRT_ENCODE\SRT_ENCODE.csproj"
            IssFile  = Join-Path $RepoRoot "tools\installer\srt_encode.iss"
            Guid     = "{{B3C84260-6FF1-4D6C-81A1-21B91E345678}}"
            OutputExe= "SRT_ENCODE_Setup.exe"
        },
        @{
            Name     = "SRT_DECODE"
            Csproj   = Join-Path $RepoRoot "samples\platform\SRT_DECODE\SRT_DECODE.csproj"
            IssFile  = Join-Path $RepoRoot "tools\installer\srt_decode.iss"
            Guid     = "{{E7D91370-7AA2-4E7D-92B2-32C92E456789}}"
            OutputExe= "SRT_DECODE_Setup.exe"
        },
        @{
            Name     = "WEBRTC_ENCODE"
            Csproj   = Join-Path $RepoRoot "samples\platform\WEBRTC_ENCODE\WEBRTC_ENCODE.csproj"
            IssFile  = Join-Path $RepoRoot "tools\installer\webrtc_encode.iss"
            Guid     = "{{C4D95371-8BB3-4E7D-92C2-43DA3E567890}}"
            OutputExe= "WEBRTC_ENCODE_Setup.exe"
        },
        @{
            Name     = "WEBRTC_DECODE"
            Csproj   = Join-Path $RepoRoot "samples\platform\WEBRTC_DECODE\WEBRTC_DECODE.csproj"
            IssFile  = Join-Path $RepoRoot "tools\installer\webrtc_decode.iss"
            Guid     = "{{F8EA2481-9CC4-4F8E-A3D3-54EB4F678901}}"
            OutputExe= "WEBRTC_DECODE_Setup.exe"
        },
        @{
            Name     = "OME_PLAYOUT"
            Csproj   = Join-Path $RepoRoot "samples\platform\OME_PLAYOUT\OME_PLAYOUT.csproj"
            IssFile  = Join-Path $RepoRoot "tools\installer\ome_playout.iss"
            Guid     = "{{D4E5F6A1-B2C3-4D5E-8F9A-1B2C3D4E5F6A}}"
            OutputExe= "OME_PLAYOUT_Setup.exe"
        }
    )

    $step = 1
    $totalSteps = $Apps.Count
    foreach ($app in $Apps) {
        $appName = $app.Name
        $csprojPath = $app.Csproj
        $issFile = $app.IssFile
        $appGuid = $app.Guid
        $outputExeName = $app.OutputExe
        $appStagingDir = Join-Path $DistDir "apps\$appName"

        Write-Host "`n>>> [$step/$totalSteps] Processing Application: $appName..." -ForegroundColor Magenta

        if (-not (Test-Path $csprojPath)) {
            throw "Csproj not found: $csprojPath"
        }
        if (-not (Test-Path $issFile)) {
            throw "Inno Setup script not found: $issFile"
        }

        # Staging
        if (Test-Path $appStagingDir) {
            Remove-Item -Recurse -Force $appStagingDir
        }
        New-Item -ItemType Directory -Force -Path $appStagingDir | Out-Null

        # Publish
        if (-not $SkipPublish) {
            Write-Host "    Publishing $appName (.NET 10 Self-Contained win-x64)..." -ForegroundColor Yellow
            $publishArgs = @(
                "publish",
                $csprojPath,
                "-c", "Release",
                "-r", "win-x64",
                "--self-contained", "true",
                "-p:PublishReadyToRun=true",
                "-p:ErrorOnDuplicatePublishOutputFiles=false",
                "-o", $appStagingDir
            )

            & dotnet @publishArgs
            if ($LASTEXITCODE -ne 0) {
                throw "dotnet publish failed for $appName with exit code $LASTEXITCODE"
            }

            # Remove debug symbols
            Get-ChildItem -Path $appStagingDir -File | ForEach-Object {
                if ($_.Name -like "*.pdb" -or $_.Name -like "*test*") {
                    Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue
                }
            }
        } else {
            Write-Host "    Skipping publish (-SkipPublish specified)..." -ForegroundColor Gray
        }

        # Inno Setup compile
        Write-Host "    Compiling installer with Inno Setup..." -ForegroundColor Yellow
        $isccArgs = @(
            "/DMyAppName=$appName",
            "/DMyAppExeName=$appName.exe",
            "/DMyAppVersion=$Version",
            "/DAppGuid=$appGuid",
            "/DSourceDir=$appStagingDir",
            "/DOutputDir=$InstallersDir",
            "/DOutputBaseFilename=$($appName)_Setup",
            $issFile
        )

        & $isccExe @isccArgs
        if ($LASTEXITCODE -ne 0) {
            throw "ISCC compilation failed for $appName with exit code $LASTEXITCODE"
        }

        $installerPath = Join-Path $InstallersDir $outputExeName
        if (-not (Test-Path $installerPath)) {
            throw "Installer not generated at expected location: $installerPath"
        }

        # Mirror to dist root for convenience
        $distMirrorPath = Join-Path $DistDir $outputExeName
        Copy-Item -Path $installerPath -Destination $distMirrorPath -Force

        $item = Get-Item $installerPath
        $sizeMB = [math]::Round($item.Length / 1MB, 2)
        $hash = (Get-FileHash -Path $installerPath -Algorithm SHA256).Hash

        $results += [PSCustomObject]@{
            Application = $appName
            Installer   = $outputExeName
            SizeMB      = $sizeMB
            SHA256      = $hash
            Status      = "[OK]"
        }

        Write-Host "    [OK] $outputExeName created ($sizeMB MB)" -ForegroundColor Green
        $step++
    }

    Write-Host "`n============================================================" -ForegroundColor Green
    Write-Host " APPS PACKAGING COMPLETED!" -ForegroundColor Green
    Write-Host " Installers directory: $InstallersDir" -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor Green
    $results | Format-Table -AutoSize
}
finally {
    Pop-Location
}
