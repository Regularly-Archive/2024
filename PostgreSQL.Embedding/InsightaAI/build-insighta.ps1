# InsightaAI Agent CLI - Build and install the local development version
# Usage: .\build-insighta.ps1

$ErrorActionPreference = "Stop"
$projectPath = "src/InsightaAI.Agent.Cli"
$outputPath = Join-Path $PSScriptRoot "nupkg"
$packageId = "InsightaAI.Agent.Cli"
$toolCommand = "insighta"

function Invoke-Dotnet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Write-Host "=== InsightaAI Local Build Installer ===" -ForegroundColor Cyan
Write-Host ""

# A running global tool keeps its executable and dependent files locked on Windows.
$runningProcesses = Get-Process -Name @($toolCommand, "InsightaAI.Agent.Cli") -ErrorAction SilentlyContinue
if ($runningProcesses) {
    Write-Host "A running Insighta CLI process was found." -ForegroundColor Red
    Write-Host "Please exit the running Insighta session and run this script again." -ForegroundColor Yellow
    exit 1
}

try {
    Write-Host "[1/3] Packing local development version..." -ForegroundColor Yellow
    Invoke-Dotnet @("pack", $projectPath, "-c", "Release", "-o", $outputPath, "--force")

    $nupkg = Get-ChildItem -LiteralPath $outputPath -Filter "*.nupkg" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $nupkg) {
        throw "No NuGet package was produced in '$outputPath'."
    }
    Write-Host "  Packed: $($nupkg.Name)" -ForegroundColor Green

    Write-Host "[2/3] Uninstalling previous version..." -ForegroundColor Yellow
    & dotnet tool uninstall --global $packageId
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  Previous version uninstalled." -ForegroundColor Green
    } else {
        Write-Host "  No previous version found." -ForegroundColor Gray
    }

    Write-Host "[3/3] Installing local package..." -ForegroundColor Yellow
    Invoke-Dotnet @("tool", "install", "--global", "--add-source", $outputPath, $packageId, "--prerelease")
    Write-Host "  Installed successfully!" -ForegroundColor Green
}
catch {
    Write-Host "  Installation failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== Done! ===" -ForegroundColor Cyan
Write-Host "Usage: $toolCommand chat" -ForegroundColor Green
