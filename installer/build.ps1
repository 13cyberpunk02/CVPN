<#
  Локальная сборка релиза. Делает то же, что и CI, чтобы установщик
  можно было проверить до пуша тега.

  Пример:  .\installer\build.ps1 -Version 1.0.0 -SingBoxVersion 1.13.19
#>
param(
    [string]$Version = "1.0.0",
    [string]$SingBoxVersion = "1.13.19"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$payload = Join-Path $PSScriptRoot "payload"

Write-Host "== Очистка ==" -ForegroundColor Cyan
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item -ItemType Directory -Path $payload | Out-Null

Write-Host "== Публикация приложения ==" -ForegroundColor Cyan
# self-contained: пользователю не нужно ставить .NET отдельно
dotnet publish "$root\CVPN\CVPN.csproj" -c Release -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:Version=$Version `
    -o $payload

Write-Host "== Публикация службы ==" -ForegroundColor Cyan
dotnet publish "$root\CVPN.Service\CVPN.Service.csproj" -c Release -r win-x64 `
    --self-contained true `
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

# Две копии ядра: одна для запуска из приложения, вторая для службы.
# У службы своя, потому что она исполняется под SYSTEM и каталог
# не должен быть доступен обычному пользователю на запись.
foreach ($dir in @("$payload\core", "$payload\service\core")) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    Copy-Item $core.FullName -Destination $dir
}

# GPL требует передавать текст лицензии вместе с бинарником
Get-ChildItem -Path $extract -Filter "LICENSE" -Recurse |
    Select-Object -First 1 |
    Copy-Item -Destination "$payload\LICENSE.sing-box.txt"

Write-Host "== Сборка установщика ==" -ForegroundColor Cyan
# Inno Setup ставится и в Program Files, и в Program Files (x86) — ищем в обоих
$candidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)

$iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "Не найден ISCC.exe. Установите Inno Setup 6: https://jrsoftware.org/isdl.php"
}

& $iscc "/DAppVersion=$Version" "$PSScriptRoot\CVPN.iss"

Write-Host "Готово: dist\CVPN-$Version-setup.exe" -ForegroundColor Green
