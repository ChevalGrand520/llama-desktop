@echo off
setlocal
start "Llama Quick Launcher" powershell.exe -NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0LlamaLauncher.ps1"
endlocal
