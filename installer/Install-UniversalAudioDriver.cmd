@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-UniversalAudioDriver.ps1" %*
set "SETUP_EXIT=%ERRORLEVEL%"
if not "%SETUP_EXIT%"=="0" (
    echo.
    echo UniversalAudioDriver setup ended with code %SETUP_EXIT%.
    pause
)
exit /b %SETUP_EXIT%
