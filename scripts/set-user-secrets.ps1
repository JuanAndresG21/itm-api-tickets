# Set user-secrets for local development
# Run from repo root: powershell -ExecutionPolicy Bypass -File .\scripts\set-user-secrets.ps1

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

function Set-SecretsForProject {
    param(
        [string]$ProjectPath,
        [hashtable]$Secrets
    )

    if (-not (Test-Path $ProjectPath)) {
        throw "Project path not found: $ProjectPath"
    }

    Push-Location $ProjectPath
    dotnet user-secrets init | Out-Null

    foreach ($pair in $Secrets.GetEnumerator()) {
        dotnet user-secrets set $pair.Key $pair.Value | Out-Null
    }

    Pop-Location
}

# TODO: Replace with your real values
$jwtKey = "9fK2xP7LmQ8vRtY4zNw6AaBcDeFgHi12"
$jwtIssuer = "Itm.Booking.Api"
$rabbitHost = "amqp://guest:guest@localhost:5672"
$redisConn = "localhost:6379"

Set-SecretsForProject "$root\Itm.Booking.Api" @{
    "Jwt:Key" = $jwtKey
    "Jwt:Issuer" = $jwtIssuer
    "RabbitMq:Host" = $rabbitHost
}

Set-SecretsForProject "$root\Itm.Gateway.Api" @{
    "Jwt:Key" = $jwtKey
    "Jwt:Issuer" = $jwtIssuer
}

Set-SecretsForProject "$root\Itm.Event.Api" @{
    "ConnectionStrings:Redis" = $redisConn
}

Write-Host "User-secrets configured." -ForegroundColor Green
