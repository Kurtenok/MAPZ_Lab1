@echo off
setlocal
pushd "%~dp0"
if not exist "app\desktop\JobShop.Desktop.exe" call build.cmd
if errorlevel 1 goto failure
start "" "app\desktop\JobShop.Desktop.exe"
popd
exit /b 0
:failure
echo Build failed. Install the .NET 9 SDK and run build.cmd.
pause
popd
exit /b 1
