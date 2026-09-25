param(
    [string]$Version = "2.0.0",
    [string]$OutputFolder = "dist",
    [string]$BuildDir = "",
    [string]$DotNetProject = "samples/dotnet/WpfDemo/WpfDemo.csproj",
    [switch]$SkipDotNet,
    [switch]$SkipVCRedist,
    [switch]$NoZip,
    [switch]$Demo,
    [switch]$Production
)

$ErrorActionPreference = "Stop"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " OpenMedia SDK - Automated Packaging Pipeline v$Version" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# 1. Xác định thư mục gốc của repository
$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path
Push-Location $RepoRoot

try {
    # 2. Chuẩn bị thư mục đầu ra
    $DistDir = if ([System.IO.Path]::IsPathRooted($OutputFolder)) { $OutputFolder } else { Join-Path $RepoRoot $OutputFolder }
    if (!(Test-Path $DistDir)) {
        New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
    }

    $PackageName = "OpenMedia-v$Version"
    if ($Production) {
        $PackageName += "-Production"
    } elseif ($Demo) {
        $PackageName += "-Demo"
    }

    $PackageDir = Join-Path $DistDir $PackageName
    Write-Host "[1/6] Preparing staging directory: $PackageDir" -ForegroundColor Yellow
    if (Test-Path $PackageDir) {
        Remove-Item -Recurse -Force $PackageDir
    }
    New-Item -ItemType Directory -Force -Path $PackageDir | Out-Null

    # 3. Xác định thư mục build native
    if ([string]::IsNullOrWhiteSpace($BuildDir)) {
        if ($Production -and (Test-Path "build-production\bin\Release")) {
            $BuildDir = "build-production"
        } elseif ($Demo -and (Test-Path "build-demo\bin\Release")) {
            $BuildDir = "build-demo"
        } elseif (Test-Path "build\bin\Release") {
            $BuildDir = "build"
        } elseif (Test-Path "build-production") {
            $BuildDir = "build-production"
        } else {
            $BuildDir = "build"
        }
    }

    $NativeBinSrc = Join-Path $RepoRoot "$BuildDir\bin\Release"
    if (!(Test-Path $NativeBinSrc)) {
        $NativeBinSrc = Join-Path $RepoRoot "$BuildDir\bin"
    }
    if (!(Test-Path $NativeBinSrc)) {
        Write-Warning "Native build directory not found at $NativeBinSrc. Some native binaries might be missing."
    } else {
        Write-Host "       Source native binaries: $NativeBinSrc" -ForegroundColor Gray
    }

    # 4. Thu gom Native Binaries & vcpkg Dependencies vào bin/
    Write-Host "[2/6] Collecting Native Binaries & vcpkg Dependencies..." -ForegroundColor Yellow
    $BinDir = Join-Path $PackageDir "bin"
    New-Item -ItemType Directory -Force -Path $BinDir | Out-Null

    # Danh sách các file test cần bỏ qua trong gói phát hành thương mại
    $ExcludePatterns = @("*test*", "*gtest*", "*gmock*", "*d3d11_ipc_poc*")

    if (Test-Path $NativeBinSrc) {
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
    }

    # Quét và copy toàn bộ DLLs từ vcpkg (cả root và trong build)
    $VcpkgBinDirs = @(
        (Join-Path $RepoRoot "vcpkg_installed\x64-windows\bin"),
        (Join-Path $RepoRoot "$BuildDir\vcpkg_installed\x64-windows\bin")
    )
    foreach ($vcpkgBin in $VcpkgBinDirs) {
        if (Test-Path $vcpkgBin) {
            Write-Host "       Scanning vcpkg DLLs from: $vcpkgBin" -ForegroundColor Gray
            Get-ChildItem -Path $vcpkgBin -Filter "*.dll" -File | ForEach-Object {
                if (-not ($_.Name -like "*gtest*" -or $_.Name -like "*gmock*")) {
                    Copy-Item $_.FullName -Destination $BinDir -Force
                }
            }
        }
    }

    # Thu gom Plugins vào thư mục plugins/
    $PluginsDir = Join-Path $PackageDir "plugins"
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

    # 5. Publish ứng dụng .NET 10 (Self-Contained win-x64)
    $AppDir = Join-Path $PackageDir "app"
    New-Item -ItemType Directory -Force -Path $AppDir | Out-Null

    if (-not $SkipDotNet) {
        $DotNetProjPath = if ([System.IO.Path]::IsPathRooted($DotNetProject)) { $DotNetProject } else { Join-Path $RepoRoot $DotNetProject }
        if (Test-Path $DotNetProjPath) {
            Write-Host "[3/6] Publishing .NET App (Self-Contained win-x64)..." -ForegroundColor Yellow
            Write-Host "       Project: $DotNetProjPath" -ForegroundColor Gray

            $publishArgs = @(
                "publish",
                $DotNetProjPath,
                "-c", "Release",
                "-r", "win-x64",
                "--self-contained", "true",
                "-p:PublishReadyToRun=true",
                "-o", $AppDir
            )
            & dotnet @publishArgs
            if ($LASTEXITCODE -ne 0) {
                throw "dotnet publish failed with exit code $LASTEXITCODE"
            }

            # Đồng bộ toàn bộ Native DLLs, OpenMediaServer.exe vào thư mục app để giải quyết triệt để:
            # 1. NativeBridge P/Invoke (OpenMedia.Core.dll) tìm thấy DLL ngay tại BaseDirectory
            # 2. ServerDiscovery.cs ưu tiên tìm OpenMediaServer.exe ngay cạnh ứng dụng
            Write-Host "       Co-locating Engine binaries & DLLs for zero-dependency IPC discovery..." -ForegroundColor Gray
            Get-ChildItem -Path $BinDir -File | ForEach-Object {
                Copy-Item $_.FullName -Destination $AppDir -Force
            }

            # Copy thư mục plugins vào app/plugins để Client App cũng có thể nạp
            $AppPluginsDir = Join-Path $AppDir "plugins"
            if (!(Test-Path $AppPluginsDir)) {
                New-Item -ItemType Directory -Force -Path $AppPluginsDir | Out-Null
            }
            Get-ChildItem -Path $PluginsDir -File | ForEach-Object {
                Copy-Item $_.FullName -Destination $AppPluginsDir -Force
            }

            # Dọn dẹp các file test executables và PDB debug khỏi thư mục app
            Get-ChildItem -Path $AppDir -File | ForEach-Object {
                if ($_.Name -like "test_*.exe" -or $_.Name -like "*gtest*" -or $_.Name -like "*gmock*" -or $_.Name -like "*.pdb") {
                    Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue
                }
            }
        } else {
            Write-Warning "DotNet project not found at $DotNetProjPath. Skipping .NET publish."
        }
    } else {
        Write-Host "[3/6] Skipping .NET publish (-SkipDotNet specified)." -ForegroundColor Gray
    }

    # 6. Đóng gói Assets & Cấu hình
    Write-Host "[4/6] Packaging Assets & Configurations..." -ForegroundColor Yellow
    
    # Copy samples/cg_templates
    $CgTemplatesSrc = Join-Path $RepoRoot "samples\cg_templates"
    if (Test-Path $CgTemplatesSrc) {
        $CgTemplatesDest = Join-Path $PackageDir "cg_templates"
        Copy-Item -Path $CgTemplatesSrc -Destination $CgTemplatesDest -Recurse -Force
        
        # Cũng copy vào thư mục app để Client App truy cập trực tiếp
        $AppCgTemplatesDest = Join-Path $AppDir "cg_templates"
        Copy-Item -Path $CgTemplatesSrc -Destination $AppCgTemplatesDest -Recurse -Force
        Write-Host "       Copied cg_templates successfully." -ForegroundColor Gray
    }

    # Copy cấu hình .env
    $EnvSource = if (Test-Path (Join-Path $RepoRoot ".env.production")) {
        Join-Path $RepoRoot ".env.production"
    } elseif (Test-Path (Join-Path $RepoRoot ".env")) {
        Join-Path $RepoRoot ".env"
    } else {
        $null
    }

    if ($EnvSource) {
        Copy-Item $EnvSource -Destination (Join-Path $PackageDir ".env") -Force
        Copy-Item $EnvSource -Destination (Join-Path $BinDir ".env") -Force
        Copy-Item $EnvSource -Destination (Join-Path $AppDir ".env") -Force
        Write-Host "       Configured .env (from $($EnvSource | Split-Path -Leaf))" -ForegroundColor Gray
    }

    # 7. Tự động tải Visual C++ Redistributable
    Write-Host "[5/6] Checking & Downloading Visual C++ Redistributable (x64)..." -ForegroundColor Yellow
    $GlobalPrereqDir = Join-Path $DistDir "prerequisites"
    if (!(Test-Path $GlobalPrereqDir)) {
        New-Item -ItemType Directory -Force -Path $GlobalPrereqDir | Out-Null
    }

    $PackagePrereqDir = Join-Path $PackageDir "prerequisites"
    if (!(Test-Path $PackagePrereqDir)) {
        New-Item -ItemType Directory -Force -Path $PackagePrereqDir | Out-Null
    }

    $VcRedistPath = Join-Path $GlobalPrereqDir "vc_redist.x64.exe"
    $VcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe"

    if (-not $SkipVCRedist) {
        $needDownload = $true
        if (Test-Path $VcRedistPath) {
            $fileSize = (Get-Item $VcRedistPath).Length
            if ($fileSize -gt 10MB) {
                Write-Host "       Found existing valid vc_redist.x64.exe ($([math]::Round($fileSize/1MB, 2)) MB)" -ForegroundColor Gray
                $needDownload = $false
            }
        }

        if ($needDownload) {
            Write-Host "       Downloading vc_redist.x64.exe from Microsoft..." -ForegroundColor Cyan
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
            Invoke-WebRequest -Uri $VcRedistUrl -OutFile $VcRedistPath -UseBasicParsing
            Write-Host "       Download completed: $VcRedistPath" -ForegroundColor Green
        }

        Copy-Item $VcRedistPath -Destination (Join-Path $PackagePrereqDir "vc_redist.x64.exe") -Force
    } else {
        Write-Host "       Skipping VC++ Redist download (-SkipVCRedist specified)." -ForegroundColor Gray
    }

    # 8. Nén thư mục thành file ZIP
    if (-not $NoZip) {
        Write-Host "[6/6] Compressing to ZIP Archive..." -ForegroundColor Yellow
        $ZipPath = Join-Path $DistDir "$PackageName.zip"
        if (Test-Path $ZipPath) {
            Remove-Item -Force $ZipPath
        }
        Compress-Archive -Path "$PackageDir\*" -DestinationPath $ZipPath
        $zipSize = (Get-Item $ZipPath).Length
        Write-Host "       ZIP created: $ZipPath ($([math]::Round($zipSize/1MB, 2)) MB)" -ForegroundColor Green
    } else {
        Write-Host "[6/6] Skipping ZIP creation (-NoZip specified)." -ForegroundColor Gray
    }

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host " Packaging completed successfully!" -ForegroundColor Green
    Write-Host " Package Directory: $PackageDir" -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor Green
}
finally {
    Pop-Location
}
