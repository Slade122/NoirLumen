#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Uninstalls NoirLumen and removes the trusted signing certificate.
#>

$ErrorActionPreference = "Stop"

$packageName = "60699FD0-A452-414E-B522-61F9A0F5A99C"
$thumbprint  = "E90AA5FBE241BD4EBEA94B9DFAFDBC3FD26EE364"

Write-Host "Removing NoirLumen package..." -ForegroundColor Cyan
$pkg = Get-AppxPackage | Where-Object { $_.Name -like "*60699FD0*" -or $_.Name -like "*NativeScreenDimmer*" }
if ($pkg) {
    Remove-AppxPackage -Package $pkg.PackageFullName
    Write-Host "  Package removed." -ForegroundColor Green
} else {
    Write-Host "  Package not found (already uninstalled)." -ForegroundColor Yellow
}

Write-Host "Removing trusted certificate..." -ForegroundColor Cyan
$store = New-Object System.Security.Cryptography.X509Certificates.X509Store("TrustedPeople", "LocalMachine")
$store.Open("ReadWrite")
$certs = $store.Certificates | Where-Object Thumbprint -eq $thumbprint
foreach ($c in $certs) { $store.Remove($c) }
$store.Close()
Write-Host "  Certificate removed." -ForegroundColor Green
