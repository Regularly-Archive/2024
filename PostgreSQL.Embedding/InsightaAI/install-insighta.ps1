# InsightaAI Agent CLI - Install a published package from NuGet
# Usage: .\install-insighta.ps1 [-Version <version>] [-Prerelease:$false]

[CmdletBinding()]
param(
    [string] $Version,
    [bool] $Prerelease = $true
)

$ErrorActionPreference = "Stop"
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

Write-Host "=== InsightaAI NuGet Installer ===" -ForegroundColor Cyan
Write-Host ""

$runningProcesses = Get-Process -Name @($toolCommand, "InsightaAI.Agent.Cli") -ErrorAction SilentlyContinue
if ($runningProcesses) {
    Write-Host "A running Insighta CLI process was found." -ForegroundColor Red
    Write-Host "Please exit the running Insighta session and run this script again." -ForegroundColor Yellow
    exit 1
}

try {
    Write-Host "[1/2] Uninstalling previous version..." -ForegroundColor Yellow
    & dotnet tool uninstall --global $packageId
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  Previous version uninstalled." -ForegroundColor Green
    } else {
        Write-Host "  No previous version found." -ForegroundColor Gray
    }

    Write-Host "[2/2] Installing from NuGet..." -ForegroundColor Yellow
    $arguments = @("tool", "install", "--global", $packageId)
    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $arguments += @("--version", $Version)
    }
    if ($Prerelease) {
        $arguments += "--prerelease"
    }
    Invoke-Dotnet $arguments
    Write-Host "  Installed successfully!" -ForegroundColor Green
}
catch {
    Write-Host "  Installation failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== Done! ===" -ForegroundColor Cyan
Write-Host "Usage: $toolCommand chat" -ForegroundColor Green
