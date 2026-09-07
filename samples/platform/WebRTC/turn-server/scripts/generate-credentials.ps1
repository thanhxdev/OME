param (
    [int]$DurationSeconds = 86400,
    [string]$Prefix = "cam",
    [string]$Secret = "broadcast_shared_turn_secret_change_in_production"
)

$epoch = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$expiry = $epoch + $DurationSeconds
$username = "$expiry`:$Prefix"

$hmac = New-Object System.Security.Cryptography.HMACSHA1
$hmac.Key = [System.Text.Encoding]::UTF8.GetBytes($Secret)
$hash = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($username))
$password = [Convert]::ToBase64String($hash)

Write-Host "=== Generated Short-Lived TURN Credentials (PowerShell) ===" -ForegroundColor Cyan
Write-Host "Username: $username"
Write-Host "Password: $password"
Write-Host "Expires Epoch: $expiry"

@{
    username = $username
    password = $password
    ttl = $DurationSeconds
    expiresAt = $expiry
} | ConvertTo-Json
