@echo off
setlocal enabledelayedexpansion
echo Building Umbrella Kernel Driver (Real Build)...
echo.

REM Try to find MSBuild in PATH
where msbuild >nul 2>&1
if %errorlevel% equ 0 (
    set "MSBUILD_EXE=msbuild"
    goto :FoundMSBuild
)

REM Try to find vswhere
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo ERROR: vswhere.exe not found. Visual Studio might not be installed correctly.
    goto :Error
)

REM Use vswhere to find MSBuild
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do (
    set "MSBUILD_EXE=%%i"
)

if not defined MSBUILD_EXE (
    echo ERROR: MSBuild not found. Please install Visual Studio with C++ workload.
    goto :Error
)

:FoundMSBuild
echo Found MSBuild: "%MSBUILD_EXE%"
echo.

REM Build the driver project
echo Building driver project...
"%MSBUILD_EXE%" UmbrellaKernelDriver.vcxproj /p:Configuration=Release /p:Platform=x64 /p:SpectreMitigation=false /p:SpectreMitigationCheck=false /p:VisualStudioVersion=17.0 /p:SignMode=Off /v:minimal

if %errorlevel% neq 0 (
    echo.
    echo ERROR: Build failed. Please check your WDK installation.
    goto :Error
)

echo.
echo Driver built successfully!
echo Location: x64\Release\UmbrellaKernelDriver.sys
goto :End

:Error
echo.
echo For kernel driver development, you need:
echo 1. Visual Studio 2022
echo 2. Windows Driver Kit (WDK)
echo 3. C++ desktop development workload
echo.
echo Troubleshooting:
echo - Ensure Visual Studio is installed.
echo - Ensure "Desktop development with C++" workload is installed.
echo - Ensure Windows Driver Kit (WDK) is installed and integrated with VS.

:End
pause
exit /b %errorlevel%