# AINE Paint インストーラー作成
#
# publish.ps1 を先に実行しておくこと。
# Inno Setup (ISCC.exe) の場所は自動で探す。
#
# 使い方:  powershell -ExecutionPolicy Bypass -File build\make-installer.ps1

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$iss  = Join-Path $PSScriptRoot "AINEPaint.iss"
$exe  = Join-Path $root "publish\AINEPaint.exe"

if (-not (Test-Path $exe)) {
    Write-Host "publish\AINEPaint.exe がありません。先に build\publish.ps1 を実行してください。" -ForegroundColor Red
    exit 1
}

# ISCC.exe を順に探す
$candidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)

$iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host "決め打ちの場所に無いので検索します..." -ForegroundColor Yellow
    $roots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}, "$env:LOCALAPPDATA\Programs") |
             Where-Object { $_ -and (Test-Path $_) }

    $iscc = Get-ChildItem -Path $roots -Filter "ISCC.exe" -Recurse -Depth 3 -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty FullName
}

if (-not $iscc) {
    Write-Host ""
    Write-Host "Inno Setup が見つかりませんでした。次でインストールしてください:" -ForegroundColor Red
    Write-Host "  winget install --id JRSoftware.InnoSetup -e"
    Write-Host "インストール後は PowerShell を開き直してから、もう一度実行してください。"
    exit 1
}

Write-Host "ISCC: $iscc" -ForegroundColor Cyan
& $iscc $iss

if ($LASTEXITCODE -ne 0) {
    Write-Host "インストーラーの作成に失敗しました。" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "完了: $(Join-Path $root 'dist')" -ForegroundColor Green
Get-ChildItem (Join-Path $root "dist") -Filter *.exe |
    ForEach-Object { Write-Host ("  {0}  ({1} MB)" -f $_.Name, [math]::Round($_.Length/1MB,1)) }
