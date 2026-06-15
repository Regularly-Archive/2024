# InsightaAI Agent CLI - 全局工具安装脚本
# 用法: .\install-tool.ps1

$ErrorActionPreference = "SilentlyContinue"
$projectPath = "src/InsightaAI.Agent.Cli"
$outputPath = "./nupkg"

Write-Host "=== InsightaAI Agent CLI Installer ===" -ForegroundColor Cyan
Write-Host ""

# 1. 卸载旧版本
Write-Host "[1/3] Uninstalling old version..." -ForegroundColor Yellow
dotnet tool uninstall --global InsightaAI.Agent.Cli 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "  Old version uninstalled." -ForegroundColor Green
} else {
    Write-Host "  No previous version found." -ForegroundColor Gray
}

# 2. 打包新版本
Write-Host "[2/3] Packing new version..." -ForegroundColor Yellow
dotnet pack $projectPath -c Release -o $outputPath --force 2>$null
if ($LASTEXITCODE -eq 0) {
    $nupkg = Get-ChildItem "$outputPath/*.nupkg" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    Write-Host "  Packed: $($nupkg.Name)" -ForegroundColor Green
} else {
    Write-Host "  Pack failed!" -ForegroundColor Red
    exit 1
}

# 3. 安装新版本
Write-Host "[3/3] Installing new version..." -ForegroundColor Yellow
dotnet tool install --global --add-source $outputPath InsightaAI.Agent.Cli --prerelease 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "  Installed successfully!" -ForegroundColor Green
} else {
    Write-Host "  Install failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== Done! ===" -ForegroundColor Cyan
Write-Host "Usage: insighta chat" -ForegroundColor Green
