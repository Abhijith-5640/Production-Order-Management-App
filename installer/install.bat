@echo off
REM ============================================================================
REM NexusProd installer
REM
REM 1. Copies the published files to %ProgramFiles%\NexusProd
REM 2. Registers the WinSW-managed service
REM 3. Starts the service
REM
REM Run as Administrator.
REM ============================================================================

setlocal
set INSTALL_DIR=%ProgramFiles%\NexusProd
set SERVICE_NAME=NexusProd

echo Installing NexusProd to %INSTALL_DIR% ...

if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"

REM Copy the published single-file + supporting files (resources, wwwroot)
xcopy /E /I /Y "%~dp0publish\*" "%INSTALL_DIR%\"

cd /d "%INSTALL_DIR%"

echo Registering Windows service ...
NexusProd.exe install

echo Starting service ...
net start %SERVICE_NAME%

echo.
echo Done. Open http://localhost:5000 in your browser.
endlocal
