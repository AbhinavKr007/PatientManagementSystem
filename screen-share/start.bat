@echo off
rem Double-click this file on the Windows PC whose screen you want to share.
cd /d "%~dp0"
where node >nul 2>nul
if errorlevel 1 (
  echo Node.js is not installed. Download the LTS version from https://nodejs.org and run this again.
  pause
  exit /b 1
)
if not exist node_modules (
  echo Installing dependencies, first run only...
  call npm install --omit=dev
  if errorlevel 1 (
    echo npm install failed.
    pause
    exit /b 1
  )
)
node server.js --open
pause
