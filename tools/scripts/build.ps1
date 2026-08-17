param(
    [ValidateSet("demo", "production")]
    [string]$Environment = "demo",

    [ValidateSet("Debug", "Release", "RelWithDebInfo")]
    [string]$BuildType = "",

    [switch]$Clean,
    [switch]$BuildTests,
    [switch]$RunTests,
    [switch]$BuildDotNet,
    [switch]$Package
)

$ErrorActionPreference = "Stop"

# Auto-set build type from environment
if (-not $BuildType) {
    $BuildType = if ($Environment -eq "production") { "Release" } else { "Debug" }
}

$BuildDir = "build-$Environment"
$DistDir = "dist/$Environment"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  OpenMedia SDK Build" -ForegroundColor Cyan
Write-Host "  Environment: $Environment" -ForegroundColor Yellow
Write-Host "  Build Type:  $BuildType" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan

# Clean
if ($Clean -and (Test-Path $BuildDir)) {
    Write-Host "Cleaning $BuildDir..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $BuildDir
}

# CMake Configure
Write-Host "`nConfiguring CMake..." -ForegroundColor Cyan
cmake --preset $Environment `
    "-DCMAKE_BUILD_TYPE=$BuildType" `
    "-DOME_BUILD_TESTS:BOOL=$($BuildTests -or $RunTests)"

if ($LASTEXITCODE -ne 0) {
    Write-Host "CMake configure failed!" -ForegroundColor Red
    exit 1
}

# CMake Build
Write-Host "`nBuilding..." -ForegroundColor Cyan
cmake --build $BuildDir --config $BuildType --parallel

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Run Tests
if ($RunTests) {
    Write-Host "`nRunning tests..." -ForegroundColor Cyan
    ctest --test-dir $BuildDir -C $BuildType --output-on-failure
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Tests failed!" -ForegroundColor Red
        exit 1
    }
}

# Build .NET
if ($BuildDotNet) {
    Write-Host "`nBuilding .NET wrappers..." -ForegroundColor Cyan
    $DotNetConfig = if ($Environment -eq "production") { "Release" } else { "Debug" }
    dotnet build wrappers/OpenMedia.NET.slnx -c $DotNetConfig
    if ($LASTEXITCODE -ne 0) {
        Write-Host ".NET build failed!" -ForegroundColor Red
        exit 1
    }
}

# Package
if ($Package) {
    Write-Host "`nPackaging..." -ForegroundColor Cyan
    cmake --install $BuildDir --prefix $DistDir
    Write-Host "Package created at: $DistDir" -ForegroundColor Green
}

Write-Host "`nBuild completed successfully!" -ForegroundColor Green
