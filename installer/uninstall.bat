@echo off
REM ============================================================================
REM NexusProd uninstaller
REM Run as Administrator.
REM ============================================================================
setlocal
set INSTALL_DIR=%ProgramFiles%\NexusProd
set SERVICE_NAME=NexusProd

echo Stopping service ...
net stop %SERVICE_NAME%

echo Unregistering service ...
NexusProd.exe uninstall

echo Removing install directory ...
if exist "%INSTALL_DIR%" rmdir /S /Q "%INSTALL_DIR%"

echo Done.
endlocal
