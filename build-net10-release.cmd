@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-net10-release.ps1"
echo.
if errorlevel 1 (
    echo Build failed. Keep this window open and send a screenshot of the error.
) else (
    echo Build completed. The EXE is in the publish folder.
)
pause
