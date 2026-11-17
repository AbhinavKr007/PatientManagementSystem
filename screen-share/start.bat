@echo off
rem Double-click this file on the Windows PC whose screen you want to share.
cd /d "%~dp0"
rem Use a portable Node.js unpacked to %USERPROFILE%\node if there is one (no admin rights needed).
if exist "%USERPROFILE%\node\node.exe" set "PATH=%USERPROFILE%\node;%PATH%"
node -v >nul 2>nul
if errorlevel 1 (
  echo Node.js was not found.
  echo Install the LTS version from https://nodejs.org, or without admin rights unpack the
  echo Windows "standalone binary (.zip)" so that node.exe is in %USERPROFILE%\node
  pause
  exit /b 1
)
rem Dependencies are included in the download; this only runs if they are missing.
if not exist node_modules\ws (
  echo Installing dependencies...
  call npm install --omit=dev
  if errorlevel 1 (
    echo npm install failed.
    pause
    exit /b 1
  )
)
node server.js --open
pause
