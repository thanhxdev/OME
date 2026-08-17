Write-Host "Starting Stress Test for OpenMedia Pipeline..." -ForegroundColor Cyan

# Find the test executable
$testExe = "build/tests/integration/test_cg_pipeline.exe"
if (!(Test-Path $testExe)) {
    $testExe = "build/tests/integration/Debug/test_cg_pipeline.exe"
}
if (!(Test-Path $testExe)) {
    Write-Error "Cannot find test_cg_pipeline.exe. Please build the project first."
    exit 1
}

$durationSeconds = 60
$startTime = Get-Date

Write-Host "Running stress test for $durationSeconds seconds..."
$jobs = @()

# Start 5 concurrent pipelines
for ($i = 0; $i -lt 5; $i++) {
    $jobs += Start-Process -FilePath $testExe -NoNewWindow -PassThru
}

while (((Get-Date) - $startTime).TotalSeconds -lt $durationSeconds) {
    Start-Sleep -Seconds 5
    Write-Host "Stress test running... ($([math]::Round(((Get-Date) - $startTime).TotalSeconds))s)"
}

Write-Host "Stopping stress tests..."
foreach ($job in $jobs) {
    if (!$job.HasExited) {
        Stop-Process -Id $job.Id -Force
    }
}

Write-Host "Stress test completed successfully." -ForegroundColor Green
