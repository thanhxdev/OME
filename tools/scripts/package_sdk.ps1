param(
    [string]$Version = "1.0.0",
    [string]$OutputFolder = "dist",
    [string]$BuildDir = "",
    [string]$InnoSetupPath = "",
    [switch]$SkipVCRedist,
    [switch]$NoZip,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " OpenMedia SDK - Automated SDK Packaging Pipeline v$Version" -ForegroundColor Cyan
Write-Host " Modular Architecture: Core Runtime & Native Dependencies" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path
Push-Location $RepoRoot

try {
    # 1. Prepare output and staging directories
    $DistDir = if ([System.IO.Path]::IsPathRooted($OutputFolder)) { $OutputFolder } else { Join-Path $RepoRoot $OutputFolder }
    if (-not (Test-Path $DistDir)) {
        New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
    }

    $SdkStagingDir = Join-Path $DistDir "sdk_staging"
    Write-Host "[1/6] Preparing staging directory: $SdkStagingDir" -ForegroundColor Yellow
    if (Test-Path $SdkStagingDir) {
        Remove-Item -Recurse -Force $SdkStagingDir
    }
    New-Item -ItemType Directory -Force -Path $SdkStagingDir | Out-Null

    # 2. Locate native build directory
    if ([string]::IsNullOrWhiteSpace($BuildDir)) {
        if ((Test-Path "build\bin\Release") -or (Test-Path "build\bin")) {
            $BuildDir = "build"
        } elseif (Test-Path "build-production\bin\Release") {
            $BuildDir = "build-production"
        } elseif (Test-Path "build-demo\bin\Release") {
            $BuildDir = "build-demo"
        } else {
            $BuildDir = "build"
        }
    }

    $NativeBinSrc = Join-Path $RepoRoot "$BuildDir\bin\Release"
    if (-not (Test-Path $NativeBinSrc)) {
        $NativeBinSrc = Join-Path $RepoRoot "$BuildDir\bin"
    }

    $BinDir = Join-Path $SdkStagingDir "bin"
    New-Item -ItemType Directory -Force -Path $BinDir | Out-Null

    # 3. Collect Native Server & Core DLLs
    Write-Host "[2/6] Collecting Native Binaries, vcpkg Dependencies & FFmpeg..." -ForegroundColor Yellow
    $ExcludePatterns = @("*test*", "*gtest*", "*gmock*", "*d3d11_ipc_poc*")

    if (Test-Path $NativeBinSrc) {
        Write-Host "       Source native binaries: $NativeBinSrc" -ForegroundColor Gray
        Get-ChildItem -Path $NativeBinSrc -File | ForEach-Object {
            $fileName = $_.Name
            $isExcluded = $false
            foreach ($pattern in $ExcludePatterns) {
                if ($fileName -like $pattern) {
                    $isExcluded = $true
                    break
                }
            }
            if (-not $isExcluded) {
                Copy-Item $_.FullName -Destination $BinDir -Force
            }
        }
    } else {
        Write-Warning "Native build directory not found at $NativeBinSrc."
    }

    # Collect FFmpeg Native DLLs & Executables (BẮT BUỘC ĐỂ GIẢI MÃ VIDEO VÀ PHÁT SRT)
    $FFmpegBin = Join-Path $RepoRoot "third_party\ffmpeg\bin"
    if (Test-Path $FFmpegBin) {
        Write-Host "       Scanning & copying FFmpeg binaries from: $FFmpegBin" -ForegroundColor Green
        Get-ChildItem -Path $FFmpegBin -File | ForEach-Object {
            Copy-Item $_.FullName -Destination $BinDir -Force
            Write-Host "         + FFmpeg binary: $($_.Name)" -ForegroundColor Gray
        }
    } else {
        Write-Warning "FFmpeg binary folder not found at $FFmpegBin."
    }

    # Collect vcpkg DLL dependencies
    $VcpkgBinDirs = @(
        (Join-Path $RepoRoot "vcpkg_installed\x64-windows\bin"),
        (Join-Path $RepoRoot "$BuildDir\vcpkg_installed\x64-windows\bin")
    )
    foreach ($vcpkgBin in $VcpkgBinDirs) {
        if (Test-Path $vcpkgBin) {
            Write-Host "       Scanning vcpkg DLLs: $vcpkgBin" -ForegroundColor Gray
            Get-ChildItem -Path $vcpkgBin -Filter "*.dll" -File | ForEach-Object {
                if (-not ($_.Name -like "*gtest*" -or $_.Name -like "*gmock*")) {
                    Copy-Item $_.FullName -Destination $BinDir -Force
                }
            }
        }
    }

    # 4. Collect C# Wrapper DLLs
    Write-Host "[3/6] Collecting C# Wrapper DLLs..." -ForegroundColor Yellow
    $WrappersDir = Join-Path $SdkStagingDir "wrappers"
    New-Item -ItemType Directory -Force -Path $WrappersDir | Out-Null

    $WrapperSearchDirs = @(
        (Join-Path $RepoRoot "wrappers\OpenMedia.Platform\bin\Release"),
        (Join-Path $RepoRoot "wrappers\OpenMedia.Core.NET\bin\Release"),
        (Join-Path $RepoRoot "wrappers\OpenMedia.NDI.NET\bin\Release")
    )

    foreach ($wDir in $WrapperSearchDirs) {
        if (Test-Path $wDir) {
            Get-ChildItem -Path $wDir -Filter "*.dll" -Recurse -File | ForEach-Object {
                if (-not ($_.FullName -like "*ref*")) {
                    Copy-Item $_.FullName -Destination $WrappersDir -Force
                    # Also mirror in bin/ so native loader can resolve immediately
                    Copy-Item $_.FullName -Destination $BinDir -Force
                    Write-Host "       Found wrapper: $($_.Name)" -ForegroundColor Gray
                }
            }
        }
    }

    # Collect Plugins
    $PluginsDir = Join-Path $SdkStagingDir "plugins"
    New-Item -ItemType Directory -Force -Path $PluginsDir | Out-Null

    $PluginSearchPaths = @(
        (Join-Path $RepoRoot "plugins"),
        (Join-Path $RepoRoot "$BuildDir\plugins")
    )
    foreach ($pPath in $PluginSearchPaths) {
        if (Test-Path $pPath) {
            Get-ChildItem -Path $pPath -Filter "*Plugin.dll" -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
                if ($_.FullName -notmatch "Debug") {
                    Copy-Item $_.FullName -Destination $PluginsDir -Force
                    Write-Host "       Found plugin: $($_.Name)" -ForegroundColor Gray
                }
            }
        }
    }

    # 5. Packaging Assets & Configurations
    Write-Host "[4/6] Packaging Templates & Configuration..." -ForegroundColor Yellow
    $CgTemplatesSrc = Join-Path $RepoRoot "samples\cg_templates"
    if (Test-Path $CgTemplatesSrc) {
        $CgTemplatesDest = Join-Path $SdkStagingDir "cg_templates"
        Copy-Item -Path $CgTemplatesSrc -Destination $CgTemplatesDest -Recurse -Force
        Write-Host "       Copied cg_templates successfully." -ForegroundColor Gray
    }

    $EnvSource = if (Test-Path (Join-Path $RepoRoot ".env.production")) {
        Join-Path $RepoRoot ".env.production"
    } elseif (Test-Path (Join-Path $RepoRoot ".env")) {
        Join-Path $RepoRoot ".env"
    } else {
        $null
    }

    if ($EnvSource) {
        Copy-Item $EnvSource -Destination (Join-Path $SdkStagingDir ".env") -Force
        Copy-Item $EnvSource -Destination (Join-Path $BinDir ".env") -Force
        Write-Host "       Configured .env from $($EnvSource | Split-Path -Leaf)" -ForegroundColor Gray
    }

    # 6. Visual C++ Redistributable staging
    Write-Host "[5/6] Checking & Staging Visual C++ Redistributable (x64)..." -ForegroundColor Yellow
    $GlobalPrereqDir = Join-Path $DistDir "prerequisites"
    if (-not (Test-Path $GlobalPrereqDir)) {
        New-Item -ItemType Directory -Force -Path $GlobalPrereqDir | Out-Null
    }

    $SdkPrereqDir = Join-Path $SdkStagingDir "prerequisites"
    if (-not (Test-Path $SdkPrereqDir)) {
        New-Item -ItemType Directory -Force -Path $SdkPrereqDir | Out-Null
    }

    $VcRedistPath = Join-Path $GlobalPrereqDir "vc_redist.x64.exe"
    $VcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe"

    if (-not $SkipVCRedist) {
        $needDownload = $true
        if (Test-Path $VcRedistPath) {
            $fileLength = (Get-Item $VcRedistPath).Length
            if ($fileLength -gt 10MB) {
                Write-Host "       Found existing valid vc_redist.x64.exe ($([math]::Round($fileLength / 1MB, 2)) MB)" -ForegroundColor Gray
                $needDownload = $false
            }
        }

        if ($needDownload) {
            Write-Host "       Downloading vc_redist.x64.exe from Microsoft..." -ForegroundColor Cyan
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
            Invoke-WebRequest -Uri $VcRedistUrl -OutFile $VcRedistPath -UseBasicParsing
            Write-Host "       Download completed: $VcRedistPath" -ForegroundColor Green
        }

        Copy-Item $VcRedistPath -Destination (Join-Path $SdkPrereqDir "vc_redist.x64.exe") -Force
    }

    # Optional ZIP creation
    if (-not $NoZip) {
        $ZipPath = Join-Path $DistDir "OpenMedia_SDK_v$Version.zip"
        if (Test-Path $ZipPath) {
            Remove-Item -Force $ZipPath
        }
        Write-Host "       Compressing SDK archive: $ZipPath..." -ForegroundColor Gray
        Compress-Archive -Path "$SdkStagingDir\*" -DestinationPath $ZipPath
    }

    # 7. Compile Inno Setup Installer
    if (-not $SkipInstaller) {
        Write-Host "[6/6] Compiling OpenMedia SDK Installer with Inno Setup..." -ForegroundColor Yellow
        $IssFilePath = Join-Path $RepoRoot "tools\installer\sdk_setup.iss"
        if (-not (Test-Path $IssFilePath)) {
            throw "Inno Setup script not found at: $IssFilePath"
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

        $SetupBaseName = "OpenMedia_SDK_Setup_v$Version"
        $isccArgs = @(
            "/DMyAppVersion=$Version",
            "/DSourceDir=$SdkStagingDir",
            "/DOutputDir=$DistDir",
            "/DOutputBaseFilename=$SetupBaseName",
            $IssFilePath
        )

        Write-Host "       Executing: & `"$isccExe`" $($isccArgs -join ' ')" -ForegroundColor Gray
        & $isccExe @isccArgs
        if ($LASTEXITCODE -ne 0) {
            throw "ISCC compilation failed with exit code $LASTEXITCODE"
        }

        $SetupExe = Join-Path $DistDir "$SetupBaseName.exe"
        if (Test-Path $SetupExe) {
            # Also provide unversioned OpenMedia_SDK_Setup.exe
            $CanonicalSetup = Join-Path $DistDir "OpenMedia_SDK_Setup.exe"
            Copy-Item $SetupExe -Destination $CanonicalSetup -Force

            # Also mirror to dist/installers/
            $InstallersDir = Join-Path $DistDir "installers"
            if (-not (Test-Path $InstallersDir)) {
                New-Item -ItemType Directory -Force -Path $InstallersDir | Out-Null
            }
            Copy-Item $SetupExe -Destination (Join-Path $InstallersDir "$SetupBaseName.exe") -Force
            Copy-Item $SetupExe -Destination (Join-Path $InstallersDir "OpenMedia_SDK_Setup.exe") -Force

            $setupItem = Get-Item $SetupExe
            $fileSizeMB = [math]::Round($setupItem.Length / 1MB, 2)
            $hash = (Get-FileHash -Path $SetupExe -Algorithm SHA256).Hash

            Write-Host ""
            Write-Host "============================================================" -ForegroundColor Green
            Write-Host " [OK] OPENMEDIA SDK INSTALLER CREATED SUCCESSFULLY!" -ForegroundColor Green
            Write-Host "============================================================" -ForegroundColor Green
            Write-Host " File:      $SetupExe" -ForegroundColor White
            Write-Host " Mirror:    $CanonicalSetup" -ForegroundColor White
            Write-Host " Installer: $(Join-Path $InstallersDir 'OpenMedia_SDK_Setup.exe')" -ForegroundColor White
            Write-Host " Size:      $fileSizeMB MB" -ForegroundColor White
            Write-Host " SHA-256:   $hash" -ForegroundColor White
            Write-Host "============================================================" -ForegroundColor Green
        } else {
            Write-Warning "Setup output file not found at expected location: $SetupExe"
        }
    }

    Write-Host "[OK] SDK staging and packaging completed." -ForegroundColor Green
}
finally {
    Pop-Location
}
