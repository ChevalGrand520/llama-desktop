$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'dist\LlamaDesktop'
if (Test-Path $out) { Remove-Item -LiteralPath $out -Recurse -Force }
dotnet publish (Join-Path $root 'src\LlamaDesktop.App\LlamaDesktop.App.csproj') `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false `
  -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
Copy-Item (Join-Path $root 'llama-server.exe') $out -Force
# Copy the full llama.cpp runtime closure: the portable package's root DLLs are all
# runtime dependencies (llama-server-impl, llama-common, llama, mtmd, libomp, ggml*, CUDA).
Get-ChildItem (Join-Path $root '*.dll') -File -ErrorAction SilentlyContinue | Copy-Item -Destination $out -Force
Copy-Item (Join-Path $root '启动Llama.cmd') $out -Force
Copy-Item (Join-Path $root 'LlamaLauncher.ps1') $out -Force
Copy-Item (Join-Path $root 'Launcher.Core.psm1') $out -Force
New-Item -ItemType Directory -Path (Join-Path $out 'models') -Force | Out-Null
Write-Host "Published to $out"
