#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs NoirLumen (trusts cert + installs MSIX).
#>

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path $MyInvocation.MyCommand.Path -Parent
$msixDir   = Join-Path $scriptDir "AppPackages\NativeScreenDimmer.WinUI3_1.0.0.0_x64_Test"
$msix      = Join-Path $msixDir   "NativeScreenDimmer.WinUI3_1.0.0.0_x64.msix"
$certFile  = Join-Path $msixDir   "NativeScreenDimmer.WinUI3_1.0.0.0_x64.cer"

if (-not (Test-Path $msix)) {
    Write-Error "MSIX not found at: $msix`nRun the build first."
    exit 1
}

# Export cert from store if .cer not already present
if (-not (Test-Path $certFile)) {
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object Thumbprint -eq "E90AA5FBE241BD4EBEA94B9DFAFDBC3FD26EE364"
    if (-not $cert) { Write-Error "Signing cert not found in CurrentUser\My. Rebuild first."; exit 1 }
    $certBytes = $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
    [System.IO.File]::WriteAllBytes($certFile, $certBytes)
}

Write-Host "Removing any existing NoirLumen packages..." -ForegroundColor Cyan
Get-AppxPackage | Where-Object { $_.Name -like "*60699FD0*" } | ForEach-Object {
    Remove-AppxPackage -Package $_.PackageFullName -ErrorAction SilentlyContinue
    Write-Host "  Removed: $($_.PackageFullName)" -ForegroundColor Gray
}

Write-Host "Trusting signing certificate..." -ForegroundColor Cyan
$store = New-Object System.Security.Cryptography.X509Certificates.X509Store("TrustedPeople", "LocalMachine")
$store.Open("ReadWrite")
$imported = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certFile)
$store.Add($imported)
$store.Close()
Write-Host "  Certificate trusted." -ForegroundColor Green

Write-Host "Installing NoirLumen..." -ForegroundColor Cyan
Add-AppxPackage -Path $msix -ForceApplicationShutdown
Write-Host "  NoirLumen installed." -ForegroundColor Green
