# AINE Paint 配布用ビルド
#
# .NET ランタイムを同梱した単一 exe を作る。
# 利用者が .NET をインストールしなくても動くようにするため。
#
# 使い方:  powershell -ExecutionPolicy Bypass -File build\publish.ps1

$ErrorActionPreference = "Stop"

$root    = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\AINEPaint\AINEPaint.csproj"
$output  = Join-Path $root "publish"

Write-Host "=== AINE Paint を発行します ===" -ForegroundColor Cyan

if (Test-Path $output) {
    Remove-Item $output -Recurse -Force
}

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $output

if ($LASTEXITCODE -ne 0) {
    Write-Host "発行に失敗しました。" -ForegroundColor Red
    exit 1
}

$exe = Join-Path $output "AINEPaint.exe"
if (-not (Test-Path $exe)) {
    Write-Host "AINEPaint.exe が見つかりません。" -ForegroundColor Red
    exit 1
}

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "完了: $exe ($sizeMb MB)" -ForegroundColor Green
Write-Host "この exe だけで動きます（Portable 版としてもそのまま配れます）。"
