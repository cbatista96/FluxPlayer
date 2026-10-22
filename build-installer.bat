@echo off
setlocal

echo.
echo ========================================
echo  FluxPlayer - Build Release
echo ========================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-release.ps1" %*
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo.
    echo The build failed. Review the messages above.
    pause
    exit /b %EXIT_CODE%
)

echo.
echo Build completed successfully.
pause
