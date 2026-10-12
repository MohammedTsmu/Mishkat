@echo off
setlocal
cd /d "%~dp0"

if "%~1"=="portable" (
  echo Building self-contained portable exe...
  dotnet publish QuranWidget\QuranWidget.csproj -c Release -o dist -p:SelfContained=true -p:PublishSingleFile=true
) else (
  echo Building framework-dependent exe...
  dotnet publish QuranWidget\QuranWidget.csproj -c Release -o dist
)

if errorlevel 1 (
  echo Build FAILED.
  exit /b 1
)
echo.
echo Done: %~dp0dist\QuranWidget.exe
