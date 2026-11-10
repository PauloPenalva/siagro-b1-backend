<#
.SYNOPSIS
    Gera o SiagroB1-Client-Setup-<versão>.exe: publica o SiagroB1.Client (leitor da balança rodoviária)
    self-contained e o embute num assistente de instalação de uma tela.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File installer\build-client-installer.ps1
    powershell -ExecutionPolicy Bypass -File installer\build-client-installer.ps1 -Version 1.1.306 -CertificateThumbprint ABC123
#>
param(
    [string]$Version,
    [string]$CertificateThumbprint
)

$ErrorActionPreference = 'Stop'
$installer = $PSScriptRoot
$backend = Split-Path $installer -Parent
$frontend = Join-Path (Split-Path $backend -Parent) 'siagro-b1-frontend'
$out = Join-Path $installer 'out-client'
$payload = Join-Path $out 'payload'

function Step([string]$text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Run([string]$file, [string[]]$arguments) {
    Push-Location $backend
    try {
        & $file @arguments
        if ($LASTEXITCODE -ne 0) { throw "$file $($arguments -join ' ') falhou (código $LASTEXITCODE)" }
    } finally { Pop-Location }
}

if (-not $Version) {
    $Version = (Get-Content (Join-Path $frontend 'package.json') -Raw | ConvertFrom-Json).version
}
Write-Host "Versão: $Version"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $payload | Out-Null

Step 'Publicando o SiagroB1.Client (self-contained win-x64)'
Run 'dotnet' @('publish', 'SiagroB1.Client/SiagroB1.Client.csproj', '-c', 'Release', '-r', 'win-x64',
    '--self-contained', '-o', $payload, "-p:Version=$Version")

Step 'Removendo appsettings (o assistente grava o do cliente; nunca levar configuração de desenvolvimento)'
Get-ChildItem $payload -Filter 'appsettings*.json' | Remove-Item -Force
Get-ChildItem $payload -Filter '*.pdb' | Remove-Item -Force

Step 'Compactando'
$zip = Join-Path $out 'client.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Step 'Publicando o assistente'
$publishDir = Join-Path $out 'setup'
Run 'dotnet' @('publish', 'installer/SiagroB1.ClientInstaller/SiagroB1.ClientInstaller.csproj', '-c', 'Release', '-r', 'win-x64',
    '--self-contained', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    "-p:ClientZip=$zip", "-p:Version=$Version", '-o', $publishDir)

$exe = Join-Path $out "SiagroB1-Client-Setup-$Version.exe"
Move-Item (Join-Path $publishDir 'SiagroB1.ClientInstaller.exe') $exe -Force

if ($CertificateThumbprint) {
    Step 'Assinando'
    Run 'signtool' @('sign', '/sha1', $CertificateThumbprint, '/fd', 'SHA256', '/tr', 'http://timestamp.digicert.com', '/td', 'SHA256', $exe)
}

Write-Host "`nPronto: $exe" -ForegroundColor Green
