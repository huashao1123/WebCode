# PWA缓存版本更新脚本
# 用法: .\update-cache-version.ps1 -Version "v2"

param(
    [Parameter(Mandatory=$true)]
    [string]$Version
)

$swPath = "WebCodeCli\wwwroot\service-worker.js"

if (-not (Test-Path $swPath)) {
    Write-Error "找不到 service-worker.js 文件"
    exit 1
}

Write-Host "正在更新缓存版本到: $Version" -ForegroundColor Green

# 读取文件内容
$content = Get-Content $swPath -Raw

# 替换版本号
$content = $content -replace "webcode-pwa-v\d+", "webcode-pwa-$Version"
$content = $content -replace "webcode-static-v\d+", "webcode-static-$Version"
$content = $content -replace "webcode-dynamic-v\d+", "webcode-dynamic-$Version"

# 写回文件
Set-Content -Path $swPath -Value $content -NoNewline

Write-Host "✓ 缓存版本更新成功!" -ForegroundColor Green
Write-Host "新版本: webcode-pwa-$Version" -ForegroundColor Cyan

# 显示更改
Write-Host "`n当前缓存名称:" -ForegroundColor Yellow
Get-Content $swPath | Select-String "CACHE_NAME.*=" | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
