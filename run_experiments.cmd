@echo off
setlocal
pushd "%~dp0"
if not exist "app\cli\JobShop.Cli.exe" call build.cmd
if errorlevel 1 goto failure
"app\cli\JobShop.Cli.exe" --experiments results_new
if errorlevel 1 goto failure
echo Results saved in results_new. The original results folder was preserved.
pause
popd
exit /b 0
:failure
echo The experiment could not be completed.
pause
popd
exit /b 1
