param(
    [switch]$Clean = $false
)

Write-Host "Running AddressSanitizer Build & Tests..." -ForegroundColor Cyan

$buildDir = "build_asan"

if ($Clean -and (Test-Path $buildDir)) {
    Remove-Item -Recurse -Force $buildDir
}

if (!(Test-Path $buildDir)) {
    New-Item -ItemType Directory -Path $buildDir | Out-Null
}

Set-Location $buildDir

# Configure with ASan (requires Clang or MSVC ASan support)
# We assume the compiler supports /fsanitize=address for MSVC or -fsanitize=address for Clang
cmake .. -DCMAKE_BUILD_TYPE=Debug -DCMAKE_CXX_FLAGS="/fsanitize=address"

if ($LASTEXITCODE -ne 0) {
    Write-Error "CMake configuration failed."
    exit $LASTEXITCODE
}

# Build
cmake --build . --parallel

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed."
    exit $LASTEXITCODE
}

# Run tests
ctest --output-on-failure

Set-Location ..
Write-Host "ASan tests completed." -ForegroundColor Green
