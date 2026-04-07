@echo off
setlocal
set MSBUILDDISABLENR=1
cd /d "C:\Users\Nikolis\Documents\Umbrella Spoofer\UmbrellaSpoofer\KernelDriver"
"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe" ^
  UmbrellaKernelDriver.vcxproj ^
  /p:Configuration=Release ^
  /p:Platform=x64 ^
  /p:SpectreMitigation=false ^
  /p:SpectreMitigationCheck=false ^
  /p:SignMode=Off ^
  /p:VisualStudioVersion=17.0 ^
  /v:minimal ^
  /nologo > _build_output.txt 2>&1
type _build_output.txt
del _build_output.txt
