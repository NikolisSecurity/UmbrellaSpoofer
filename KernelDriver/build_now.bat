@echo off
cd /d "C:\Users\Nikolis\Documents\Umbrella Spoofer\UmbrellaSpoofer\KernelDriver"
"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" UmbrellaKernelDriver.vcxproj /p:Configuration=Release /p:Platform=x64 /p:SpectreMitigation=false /p:SpectreMitigationCheck=false /p:SignMode=Off /v:minimal
echo EXITCODE=%ERRORLEVEL%
