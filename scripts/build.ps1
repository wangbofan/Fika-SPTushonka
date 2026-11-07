<#
    一键构建三个工程。
    用法:
        .\scripts\build.ps1 -GameRoot "E:/TKF"
#>
param(
    [Parameter(Mandatory = $true)][string]$GameRoot,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path "$GameRoot\BepInEx\interop")) {
    throw "找不到 $GameRoot\BepInEx\interop —— GameRoot 要指向已装好 SPT 5.0 + BepInDX 6 的游戏目录"
}

Write-Host "== Fika.Core ==" -ForegroundColor Cyan
dotnet build "$root\Fika-Plugin\Fika.Core\Fika.Core.csproj" -c $Configuration -p:GameRoot="$GameRoot"
if ($LASTEXITCODE -ne 0) { throw "Fika.Core 构建失败" }

Write-Host "== Fika.Headless ==" -ForegroundColor Cyan
dotnet build "$root\Fika-Headless\Fika.Headless\Fika.Headless.csproj" -c $Configuration -p:GameRoot="$GameRoot"
if ($LASTEXITCODE -ne 0) { throw "Fika.Headless 构建失败" }

Write-Host "== FikaServer ==" -ForegroundColor Cyan
dotnet build "$root\Fika-Server\FikaServer\FikaServer.csproj" -c $Configuration -p:GameRoot="$GameRoot"
if ($LASTEXITCODE -ne 0) { throw "FikaServer 构建失败" }

Write-Host ""
Write-Host "产物:" -ForegroundColor Green
Write-Host "  $root\Fika-Plugin\Fika.Core\bin\$Configuration\Fika.Core.dll"
Write-Host "  $root\Fika-Headless\Fika.Headless\bin\$Configuration\Fika.Headless.dll"
Write-Host "  $root\Fika-Server\FikaServer\bin\$Configuration\FikaServer.dll"
Write-Host ""
Write-Host "打包安装器 payload: python FikaSetup\make-payload.py --help" -ForegroundColor Yellow
