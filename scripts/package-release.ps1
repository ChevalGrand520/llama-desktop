# package-release.ps1 - build a green portable zip from dist\LlamaDesktop
param(
    [string]$Version = "1.0.0"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist\LlamaDesktop'
$staging = Join-Path $root ("dist\release\llama-desktop-win-x64-v{0}" -f $Version)
$zip = Join-Path $root ("dist\llama-desktop-win-x64-v{0}.zip" -f $Version)

if (-not (Test-Path (Join-Path $dist 'LlamaDesktop.exe'))) { throw "dist\LlamaDesktop missing. Run dotnet publish first." }
if (-not (Test-Path (Join-Path $dist 'llama-server.exe'))) { throw "dist\LlamaDesktop\llama-server.exe missing." }

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging -Force | Out-Null

# Copy everything except the WebView2 runtime cache (regenerated on first run).
$exclude = @('LlamaDesktop.exe.WebView2')
Get-ChildItem $dist | Where-Object { $exclude -notcontains $_.Name } | ForEach-Object {
    Copy-Item $_.FullName -Destination $staging -Recurse -Force
}

# Ensure models/ exists (empty is fine; the user drops GGUF files here).
New-Item -ItemType Directory -Path (Join-Path $staging 'models') -Force | Out-Null

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -CompressionLevel Optimal
$sizeMB = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Packaged: $zip ($sizeMB MB)"
