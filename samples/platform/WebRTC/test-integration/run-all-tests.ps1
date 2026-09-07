# ==============================================================================
# 10-Camera WebRTC Broadcast Production Suite - Master Integration Test Suite
# ==============================================================================

Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host " 🚀 EXECUTING PHASE 4 MASTER INTEGRATION TEST SUITE (10-CAMERA WEBRTC)" -ForegroundColor Cyan
Write-Host "================================================================================" -ForegroundColor Cyan

$env:NODE_PATH = "..\monitoring-service\node_modules;..\signaling-server\node_modules;..\recording-service\node_modules"

$tests = @(
    @{ Name = "Test 1: Single Camera H.264 Pipeline"; Script = "test-1-camera-h264.js" },
    @{ Name = "Test 2: Sub-Second Codec Switching"; Script = "test-codec-switching.js" },
    @{ Name = "Test 3: High-Load 10-Camera Concurrent"; Script = "test-10-cameras.js" },
    @{ Name = "Test 4: Failover & Watchdog Recovery"; Script = "test-failover.js" },
    @{ Name = "Test 5: Bandwidth & WAN Overload"; Script = "test-bandwidth.js" }
)

$passed = 0
$failed = 0

foreach ($t in $tests) {
    Write-Host "`n>>> Running $($t.Name)..." -ForegroundColor Yellow
    node $t.Script
    if ($LASTEXITCODE -eq 0) {
        Write-Host ">>> [PASS] $($t.Name)" -ForegroundColor Green
        $passed++
    } else {
        Write-Host ">>> [FAIL] $($t.Name) (Exit Code: $LASTEXITCODE)" -ForegroundColor Red
        $failed++
    }
    Start-Sleep -Seconds 1
}

Write-Host "`n================================================================================" -ForegroundColor Cyan
Write-Host " 🏁 TEST SUITE SUMMARY: $passed PASSED | $failed FAILED" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host "================================================================================" -ForegroundColor Cyan

if ($failed -gt 0) {
    exit 1
} else {
    exit 0
}
