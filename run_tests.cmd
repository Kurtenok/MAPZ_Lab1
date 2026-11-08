@echo off
setlocal
pushd "%~dp0"
if not exist "app\cli\JobShop.Cli.exe" call build.cmd
if errorlevel 1 goto failure
"app\cli\JobShop.Cli.exe" --self-test
if errorlevel 1 goto failure
pause
popd
exit /b 0
:failure
echo Verification failed.
pause
popd
exit /b 1
