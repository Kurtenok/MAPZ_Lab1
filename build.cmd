@echo off
setlocal
pushd "%~dp0"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
dotnet publish JobShop.Desktop\JobShop.Desktop.csproj -c Release --self-contained false --configfile NuGet.Config --disable-build-servers -m:1 -o app\desktop
if errorlevel 1 goto failure
dotnet publish JobShop.Cli\JobShop.Cli.csproj -c Release --self-contained false --configfile NuGet.Config --disable-build-servers -m:1 -o app\cli
if errorlevel 1 goto failure
popd
exit /b 0
:failure
popd
exit /b 1
