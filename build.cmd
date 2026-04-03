@echo off
setlocal

set CONFIG=%1
if "%CONFIG%"=="" set CONFIG=Release

set MSBUILD=
set VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe

if exist "%VSWHERE%" (
  for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do (
    set MSBUILD=%%i
    goto :build
  )
)

if exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe" (
  set MSBUILD=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe
  goto :build
)

echo MSBuild.exe was not found.
exit /b 1

:build
echo Using MSBuild: %MSBUILD%
"%MSBUILD%" "src\OpenHwp.Automation.Cli\OpenHwp.Automation.Cli.csproj" /m /t:Build /p:Configuration=%CONFIG%
exit /b %ERRORLEVEL%
