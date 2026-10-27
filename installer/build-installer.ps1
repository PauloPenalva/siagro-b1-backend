<#
.SYNOPSIS
    Gera o SiagroB1-Setup-<versão>.exe: publica Web, Gateway e Reports, builda o frontend, gera os
    bundles de migration, limpa os appsettings padrão e embute tudo no assistente.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
    powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -Version 1.1.300 -CertificateThumbprint ABC123
#>
param(
    [string]$Version,
    [string]$FrontendPath,
    [string]$CertificateThumbprint,
    [switch]$SkipFrontendBuild
)

$ErrorActionPreference = 'Stop'
$installer = $PSScriptRoot
$backend = Split-Path $installer -Parent
if (-not $FrontendPath) { $FrontendPath = Join-Path (Split-Path $backend -Parent) 'siagro-b1-frontend' }
$out = Join-Path $installer 'out'
$payload = Join-Path $out 'payload'

function Step([string]$text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Run([string]$file, [string[]]$arguments, [string]$workDir = $backend) {
    Push-Location $workDir
    try {
        & $file @arguments
        if ($LASTEXITCODE -ne 0) { throw "$file $($arguments -join ' ') falhou (código $LASTEXITCODE)" }
    } finally { Pop-Location }
}

if (-not $Version) {
    $Version = (Get-Content (Join-Path $FrontendPath 'package.json') -Raw | ConvertFrom-Json).version
}
Write-Host "Versão: $Version"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $payload | Out-Null

Step 'Publicando Web, Gateway e Reports (self-contained win-x64)'
foreach ($kind in 'Web', 'Gateway', 'Reports') {
    Run 'dotnet' @('publish', "SiagroB1.$kind/SiagroB1.$kind.csproj", '-c', 'Release', '-r', 'win-x64',
        '--self-contained', '-o', (Join-Path $payload $kind), "-p:Version=$Version")
}

Step 'Frontend'
if (-not $SkipFrontendBuild) {
    # yarn build é o único build válido: o cachebuster do manifest depende dele.
    Run 'yarn' @('build') $FrontendPath
}
$wwwroot = Join-Path $payload 'Gateway\wwwroot'
New-Item -ItemType Directory -Force $wwwroot | Out-Null
Copy-Item (Join-Path $FrontendPath 'dist\*') $wwwroot -Recurse -Force

Step 'Limpando appsettings (nunca levar credencial nem arquivo de cliente)'
Run 'dotnet' @('build', 'installer/SiagroB1.Installer.Tools', '-c', 'Release', '-o', (Join-Path $out 'tools'))
$tools = Join-Path $out 'tools\SiagroB1.Installer.Tools.exe'
foreach ($kind in 'Web', 'Gateway', 'Reports') {
    $dir = Join-Path $payload $kind
    Get-ChildItem $dir -Filter 'appsettings.*.json' | Where-Object Name -ne 'appsettings.json' | Remove-Item -Force
    # Os csproj excluem appsettings do publish (para não sobrescrever servidor); o padrão vem do repositório.
    $file = Join-Path $dir 'appsettings.json'
    Run $tools @('sanitize', (Join-Path $backend "SiagroB1.$kind\appsettings.json"), $file)
}

Step 'Bundles de migration'
$migrations = Join-Path $payload 'migrations'
New-Item -ItemType Directory -Force $migrations | Out-Null
foreach ($pair in @(@('AppDbContext', 'app'), @('CommonDbContext', 'common'))) {
    $context, $name = $pair
    Run 'dotnet' @('ef', 'migrations', 'bundle', '--context', $context, '--project', 'SiagroB1.Migrations',
        '--startup-project', 'SiagroB1.Web', '--self-contained', '-r', 'win-x64', '--force',
        '-o', (Join-Path $migrations "efbundle-$name.exe"))
    Push-Location $backend
    # No PowerShell 5.1 qualquer linha no stderr (avisos do NuGet) vira erro fatal com 'Stop'.
    $ErrorActionPreference = 'Continue'
    try {
        $raw = & dotnet ef migrations list --context $context --project SiagroB1.Migrations --startup-project SiagroB1.Web --no-connect --no-build --json 2>$null
        if ($LASTEXITCODE -ne 0) { throw "dotnet ef migrations list ($context) falhou" }
    } finally { $ErrorActionPreference = 'Stop'; Pop-Location }
    # O dotnet ef mistura logs (ex.: "warn: ...[10400]") com o JSON: pega da linha "[" até a linha "]".
    $lines = @($raw | ForEach-Object { "$_" })
    $start = [Array]::IndexOf($lines, '[')
    $end = [Array]::LastIndexOf($lines, ']')
    if ($start -lt 0 -or $end -lt $start) { throw "Saída inesperada de dotnet ef migrations list ($context)" }
    $json = $lines[$start..$end] -join "`n"
    ($json | ConvertFrom-Json).id | Set-Content (Join-Path $migrations "migrations-$name.txt") -Encoding UTF8
}

Step 'Manifesto do pacote'
$manifest = [ordered]@{
    version        = $Version
    backendCommit  = (& git -C $backend rev-parse --short HEAD)
    frontendCommit = (& git -C $FrontendPath rev-parse --short HEAD)
    builtAt        = (Get-Date).ToString('s')
}
$manifest | ConvertTo-Json | Set-Content (Join-Path $payload 'manifest.json') -Encoding UTF8

Step 'Conferindo que não há senha no pacote'
$leak = Get-ChildItem $payload -Recurse -Filter 'appsettings*.json' | Select-String -Pattern 'Password=[^;"*]'
if ($leak) { throw "Senha encontrada no pacote: $($leak -join '; ')" }

Step 'Compactando'
$zip = Join-Path $out 'payload.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Step 'Publicando o assistente'
$setupName = "SiagroB1-Setup-$Version"
$publishDir = Join-Path $out 'setup'
$embedded = $true
try {
    Run 'dotnet' @('publish', 'installer/SiagroB1.Installer/SiagroB1.Installer.csproj', '-c', 'Release', '-r', 'win-x64',
        '--self-contained', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
        "-p:PayloadZip=$zip", "-p:Version=$Version", '-o', $publishDir)
} catch {
    # Pacote grande demais para recurso embutido: entrega o exe com o payload.zip ao lado.
    Write-Warning "Não foi possível embutir o pacote ($($_.Exception.Message)). Gerando exe + payload.zip."
    $embedded = $false
    Run 'dotnet' @('publish', 'installer/SiagroB1.Installer/SiagroB1.Installer.csproj', '-c', 'Release', '-r', 'win-x64',
        '--self-contained', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
        "-p:Version=$Version", '-o', $publishDir)
}

$exe = Join-Path $out "$setupName.exe"
Move-Item (Join-Path $publishDir 'SiagroB1.Installer.exe') $exe -Force
if (-not $embedded) {
    $folder = Join-Path $out $setupName
    New-Item -ItemType Directory -Force $folder | Out-Null
    Move-Item $exe $folder
    Move-Item $zip $folder
    $exe = Join-Path $folder "$setupName.exe"
}

if ($CertificateThumbprint) {
    Step 'Assinando'
    Run 'signtool' @('sign', '/sha1', $CertificateThumbprint, '/fd', 'SHA256', '/tr', 'http://timestamp.digicert.com', '/td', 'SHA256', $exe)
}

Write-Host "`nPronto: $exe" -ForegroundColor Green
