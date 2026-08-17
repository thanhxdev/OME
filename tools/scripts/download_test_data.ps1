param(
    [string]$OutputDir = "tests/data"
)

$ErrorActionPreference = "Stop"

Write-Host "Downloading test data to $OutputDir..."

if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
}

# Example file downloads (these would point to actual URLs in a real project)
$TestFiles = @(
    @{ Name = "sample_1080p.mp4"; Url = "https://test-videos.co.uk/vids/bigbuckbunny/mp4/h264/1080/Big_Buck_Bunny_1080_10s_1MB.mp4" },
    @{ Name = "test_audio.wav"; Url = "https://file-examples.com/storage/fe31215bbd669ff866d92ec/2017/11/file_example_WAV_1MG.wav" }
)

foreach ($File in $TestFiles) {
    $FilePath = Join-Path $OutputDir $File.Name
    if (!(Test-Path $FilePath)) {
        Write-Host "Downloading $($File.Name)..."
        try {
            Invoke-WebRequest -Uri $File.Url -OutFile $FilePath
        } catch {
            Write-Warning "Failed to download $($File.Name): $_"
        }
    } else {
        Write-Host "$($File.Name) already exists, skipping."
    }
}

Write-Host "Test data download complete."
