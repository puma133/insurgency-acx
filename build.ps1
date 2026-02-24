# AC Screenshot client — сборка одного exe (требуется .NET 8 SDK)
$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "Установите .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Red
    exit 1
}

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

if ($LASTEXITCODE -eq 0) {
    $exe = Join-Path $scriptDir "bin\Release\net8.0\win-x64\publish\ac_screenshot_client.exe"
    Write-Host "Готово: $exe" -ForegroundColor Green
    Write-Host "Скопируйте рядом config.ini (из config.example.ini) и настройте steam_id, receiver_url."
}
