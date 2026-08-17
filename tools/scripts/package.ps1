param(
    [string]$Version = "1.0.0",
    [string]$OutputFolder = "dist",
    [switch]$Demo,
    [switch]$Production
)

$ErrorActionPreference = "Stop"

Write-Host "Packaging OpenMedia SDK v$Version..."

if (!(Test-Path $OutputFolder)) {
    New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
}

$PackageName = "OpenMedia-v$Version"
if ($Demo) {
    $PackageName += "-Demo"
} elseif ($Production) {
    $PackageName += "-Production"
}

$PackageDir = Join-Path $OutputFolder $PackageName
if (Test-Path $PackageDir) {
    Remove-Item -Recurse -Force $PackageDir
}
New-Item -ItemType Directory -Force -Path $PackageDir | Out-Null

# Determine build directory based on environment
$BuildDir = if ($Production) { "build-production" } elseif ($Demo) { "build-demo" } else { "build" }

Write-Host "Copying binaries from $BuildDir..."
# Copy binaries
$BinDir = Join-Path $PackageDir "bin"
New-Item -ItemType Directory -Force -Path $BinDir | Out-Null
Copy-Item "$BuildDir\bin\Release\*.dll" $BinDir -ErrorAction SilentlyContinue
Copy-Item "$BuildDir\bin\Release\*.exe" $BinDir -ErrorAction SilentlyContinue

Write-Host "Copying headers..."
$IncludeDir = Join-Path $PackageDir "include"
New-Item -ItemType Directory -Force -Path $IncludeDir | Out-Null
Copy-Item -Recurse "src\*\include\*" $IncludeDir -ErrorAction SilentlyContinue

Write-Host "Copying .NET Wrappers..."
$DotNetDir = Join-Path $PackageDir "dotnet"
New-Item -ItemType Directory -Force -Path $DotNetDir | Out-Null
Copy-Item -Recurse "wrappers\*\bin\Release\net10.0\*.dll" $DotNetDir -ErrorAction SilentlyContinue
Copy-Item -Recurse "wrappers\*\bin\Release\net10.0-windows\*.dll" $DotNetDir -ErrorAction SilentlyContinue

$ZipPath = Join-Path $OutputFolder "$PackageName.zip"
if (Test-Path $ZipPath) {
    Remove-Item -Force $ZipPath
}

Write-Host "Compressing to $ZipPath..."
Compress-Archive -Path "$PackageDir\*" -DestinationPath $ZipPath

Write-Host "Packaging completed successfully!"
