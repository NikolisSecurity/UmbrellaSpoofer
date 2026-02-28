@echo off
setlocal enableextensions enabledelayedexpansion
set "ROOT_DIR=%~dp0"
pushd "%ROOT_DIR%" >nul
cls
title PhantomID Builder
color 0B
echo ==============================================================
echo   PhantomID Builder - One-file GUI EXE
echo ==============================================================
color 07
set "APP_NAME=PhantomID"
set "ENTRY="
set "PYI_GUI_FLAG=--windowed"
python --version >nul 2>&1
if errorlevel 1 (
  echo [Error] Python not found in PATH
  popd
  exit /b 1
)
if not exist ".venv\Scripts\python.exe" (
  echo [Env] Creating virtual environment .venv
  python -m venv ".venv"
  if errorlevel 1 (
    echo [Error] Failed to create virtual environment
    popd
    exit /b 1
  )
)
set "VENV_PY=.venv\Scripts\python.exe"
set "VENV_PIP=.venv\Scripts\pip.exe"
"%VENV_PY%" -m pip install --upgrade pip >nul
if errorlevel 1 (
  echo [Warn] Pip upgrade failed, continuing
)
if exist "requirements.txt" (
  echo [Deps] Installing requirements
  "%VENV_PIP%" install -r requirements.txt -q --no-input --disable-pip-version-check >nul 2>&1
  if errorlevel 1 (
    echo [Error] Failed to install requirements
    popd
    exit /b 1
  )
) else (
  echo [Deps] No requirements.txt found, skipping
)
"%VENV_PIP%" show pyinstaller >nul 2>&1
if errorlevel 1 (
  echo [Deps] Installing PyInstaller
  "%VENV_PIP%" install pyinstaller -q --no-input --disable-pip-version-check >nul 2>&1
  if errorlevel 1 (
    echo [Error] Failed to install PyInstaller
    popd
    exit /b 1
  )
)
if not defined ENTRY (
  if exist "spoofer.py" set "ENTRY=spoofer.py"
)
if not defined ENTRY (
  if exist "main.py" set "ENTRY=main.py"
)
if not defined ENTRY (
  echo [Error] Entry script not found. Ensure spoofer.py or main.py exists in project root.
  popd
  exit /b 1
)
set "APP_NAME=PhantomID"
if defined APP_NAME_OVERRIDE set "APP_NAME=%APP_NAME_OVERRIDE%"
if exist build (
  echo [Clean] Removing build
  rmdir /s /q build
)
if exist dist (
  echo [Clean] Removing dist
  rmdir /s /q dist
)
if exist *.spec del /q *.spec
echo [Build] Packaging "%ENTRY%" -> "%APP_NAME%.exe"
 ".venv\Scripts\pyinstaller.exe" --noconfirm --log-level=ERROR --onefile %PYI_GUI_FLAG% --name "%APP_NAME%" --paths "%ROOT_DIR%src" ^
  --hidden-import ui.gui --hidden-import core.database_manager --hidden-import spoofers.system_spoofers ^
  --hidden-import spoofers.game_spoofers --hidden-import utils.auto_updater --hidden-import utils.game_assets ^
  --add-data "%ROOT_DIR%src\assets;src\assets" --collect-all PySide6 "%ENTRY%" >nul 2>&1
if errorlevel 1 (
  echo [Error] Build failed
  popd
  exit /b 1
)
echo [Done] EXE created: "%ROOT_DIR%dist\%APP_NAME%.exe"
color 0A
echo [Success] Build complete!
color 07
set "FINAL_EXE=%ROOT_DIR%%APP_NAME%.exe"
if exist "%FINAL_EXE%" del /q "%FINAL_EXE%"
move /y "%ROOT_DIR%dist\%APP_NAME%.exe" "%FINAL_EXE%" >nul
if errorlevel 1 (
  copy /y "%ROOT_DIR%dist\%APP_NAME%.exe" "%FINAL_EXE%" >nul
)
echo [Post] Placed EXE at "%FINAL_EXE%"
if exist build rmdir /s /q build
if exist dist rmdir /s /q dist
if exist *.spec del /q *.spec
if exist ".env" del /q ".env"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Get-ChildItem -Directory -Recurse -Filter '__pycache__' | Remove-Item -Recurse -Force" >nul
popd
exit /b 0
