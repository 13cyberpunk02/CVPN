param(
    [string]$Version = "1.0.0",
    [string]$SingBoxVersion = "1.13.19",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$payload = Join-Path $PSScriptRoot "payload"
$mode = if ($SelfContained) { "true" } else { "false" }

Write-Host "== Очистка ==" -ForegroundColor Cyan
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item -ItemType Directory -Path $payload | Out-Null

Write-Host "== Публикация приложения (self-contained: $mode) ==" -ForegroundColor Cyan
dotnet publish "$root\CVPN\CVPN.csproj" -c Release -r win-x64 `
    --self-contained $mode `
    -p:Version=$Version `
    -o $payload

Write-Host "== Публикация службы ==" -ForegroundColor Cyan
dotnet publish "$root\CVPN.Service\CVPN.Service.csproj" -c Release -r win-x64 `
    --self-contained $mode `
    -p:Version=$Version `
    -o "$payload\service"

Write-Host "== Загрузка sing-box $SingBoxVersion ==" -ForegroundColor Cyan
$archive = Join-Path $env:TEMP "sing-box.zip"
$extract = Join-Path $env:TEMP "sing-box"
$url = "https://github.com/SagerNet/sing-box/releases/download/v$SingBoxVersion/sing-box-$SingBoxVersion-windows-amd64.zip"

Invoke-WebRequest -Uri $url -OutFile $archive
if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
Expand-Archive -Path $archive -DestinationPath $extract -Force

$core = Get-ChildItem -Path $extract -Filter "sing-box.exe" -Recurse | Select-Object -First 1

New-Item -ItemType Directory -Path "$payload\core" -Force | Out-Null
Copy-Item $core.FullName -Destination "$payload\core"

Get-ChildItem -Path $extract -Filter "LICENSE" -Recurse |
    Select-Object -First 1 |
    Copy-Item -Destination "$payload\LICENSE.sing-box.txt"

$size = "{0:N0}" -f ((Get-ChildItem $payload -Recurse | Measure-Object Length -Sum).Sum / 1MB)
Write-Host "Размер payload: $size МБ" -ForegroundColor Yellow

Write-Host "== Сборка установщика ==" -ForegroundColor Cyan
$candidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe"
)

$iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "Не найден ISCC.exe. Установите Inno Setup 6: https://jrsoftware.org/isdl.php"
}

$defines = @("/DAppVersion=$Version")
if (-not $SelfContained) { $defines += "/DNeedsRuntime" }

& $iscc @defines "$PSScriptRoot\CVPN.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup вернул код $LASTEXITCODE" }

Write-Host "Готово: dist\CVPN-$Version-setup.exe" -ForegroundColor Green