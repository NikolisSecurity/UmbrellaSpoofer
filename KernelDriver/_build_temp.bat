@echo off
setlocal
set MSBUILDDISABLENR=1
cd /d "%~dp0"
"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe" ^
  UmbrellaKernelDriver.vcxproj ^
  /p:Configuration=Release ^
  /p:Platform=x64 ^
  /p:SpectreMitigation=false ^
  /p:SpectreMitigationCheck=false ^
  /p:SignMode=Off ^
  /p:VisualStudioVersion=17.0 ^
  /v:minimal
echo EXIT_CODE=%errorlevel%
