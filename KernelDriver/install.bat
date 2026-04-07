@echo off
echo Installing Umbrella Kernel Driver...
set DRIVER_NAME=UmbrellaKernelDriver
set DRIVER_FILE=%DRIVER_NAME%.sys

if not exist "x64\Release\%DRIVER_FILE%" (
    echo ERROR: Driver file not found.
    exit /b 1
)

sc stop %DRIVER_NAME% >nul 2>&1
sc delete %DRIVER_NAME% >nul 2>&1

copy /Y "x64\Release\%DRIVER_FILE%" "%SystemRoot%\System32\drivers\"
sc create %DRIVER_NAME% binPath= "%SystemRoot%\System32\drivers\%DRIVER_FILE%" type= kernel start= demand error= normal
sc start %DRIVER_NAME%

echo Installation complete.
pause