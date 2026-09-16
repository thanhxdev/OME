#!/usr/bin/env pwsh
# Build signaling-server into a standalone .exe using Node.js SEA
# Usage: .\build-exe.ps1

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

Write-Host "`n=== Step 1: Bundle with esbuild ===" -ForegroundColor Cyan
if (Test-Path build) { Remove-Item -Recurse -Force build }
New-Item -ItemType Directory -Path build | Out-Null
node esbuild.config.mjs
if ($LASTEXITCODE -ne 0) { throw "esbuild bundle failed" }

Write-Host "`n=== Step 2: Generate SEA blob ===" -ForegroundColor Cyan
node --experimental-sea-config sea-config.json
if ($LASTEXITCODE -ne 0) { throw "SEA blob generation failed" }

Write-Host "`n=== Step 3: Copy node.exe ===" -ForegroundColor Cyan
$nodeExe = (Get-Command node).Source
Copy-Item $nodeExe build\signaling-server.exe
if ($LASTEXITCODE -ne 0) { throw "Failed to copy node.exe" }

Write-Host "`n=== Step 4: Remove signature (signtool optional) ===" -ForegroundColor Cyan
# Try using signtool to remove signature, fall back to postject without it
try {
    & signtool remove /s build\signaling-server.exe 2>$null
    Write-Host "Signature removed via signtool"
} catch {
    Write-Host "signtool not found, skipping signature removal (may still work)" -ForegroundColor Yellow
}

Write-Host "`n=== Step 5: Inject SEA blob with postject ===" -ForegroundColor Cyan
npx --yes postject build\signaling-server.exe NODE_SEA_BLOB build\sea-prep.blob `
    --sentinel-fuse NODE_SEA_FUSE_fce680ab2cc467b6e072b8b5df1996b2 `
    --overwrite
if ($LASTEXITCODE -ne 0) { throw "postject injection failed" }

# Cleanup intermediate files
Remove-Item build\sea-prep.blob -ErrorAction SilentlyContinue
Remove-Item build\signaling-server.cjs -ErrorAction SilentlyContinue

$exePath = Resolve-Path build\signaling-server.exe
$size = [math]::Round((Get-Item $exePath).Length / 1MB, 1)
Write-Host "`n=====================================================" -ForegroundColor Green
Write-Host "  Done! signaling-server.exe ($size MB)" -ForegroundColor Green
Write-Host "  Path: $exePath" -ForegroundColor Green
Write-Host "=====================================================" -ForegroundColor Green
Write-Host "`nUsage:  .\build\signaling-server.exe"
Write-Host "Config:  set PORT, JWT_SECRET, CORS_ORIGIN as env vars`n"
