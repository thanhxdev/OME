param(
    [string]$Preset = "demo"
)

$ErrorActionPreference = "Stop"

Write-Host "Running tests for preset $Preset..."

$BuildDir = "build-$Preset"

if (!(Test-Path $BuildDir)) {
    Write-Error "Build directory $BuildDir does not exist. Please build first."
    exit 1
}

# CTest is run from the build directory
ctest --test-dir $BuildDir --output-on-failure -C Debug

if ($LASTEXITCODE -ne 0) {
    Write-Error "Tests failed!"
    exit $LASTEXITCODE
}

Write-Host "All tests passed successfully!"
