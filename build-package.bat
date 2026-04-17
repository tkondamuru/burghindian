@echo off
setlocal enabledelayedexpansion

REM Build and package API Azure Functions into a zip.
REM Usage:
REM   build-package.bat
REM   build-package.bat Release

set CONFIG=%~1
if "%CONFIG%"=="" set CONFIG=Release

set ROOT=%~dp0
set PROJECT=%ROOT%api.csproj
set ZIPDIR=%ROOT%artifacts
set PACKAGE_DIR=%ZIPDIR%\publish
set ZIPFILE=%ZIPDIR%\api-functions-%CONFIG%.zip
set DEFAULT_PUBLISH_DIR=%ROOT%bin\%CONFIG%\net8.0\publish

if not exist "%PROJECT%" (
  echo [ERROR] Could not find project file: %PROJECT%
  exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] dotnet SDK is not installed or not on PATH.
  exit /b 1
)

echo [1/5] Cleaning old artifacts...
if exist "%PACKAGE_DIR%" rmdir /s /q "%PACKAGE_DIR%"
if exist "%ZIPFILE%" del /q "%ZIPFILE%"
if not exist "%ZIPDIR%" mkdir "%ZIPDIR%"

echo [2/5] Restoring dependencies...
dotnet restore "%PROJECT%"
if errorlevel 1 (
  echo [ERROR] dotnet restore failed.
  exit /b 1
)

echo [3/5] Publishing (%CONFIG%) to default output...
REM NOTE: We intentionally avoid "-o" because Microsoft.Azure.Functions.Worker.Sdk
REM can fail with DirectoryNotFound for .azurefunctions\extensions.json when custom
REM output paths are used during publish.
dotnet publish "%PROJECT%" -c %CONFIG%
if errorlevel 1 (
  echo [ERROR] dotnet publish failed.
  exit /b 1
)

if not exist "%DEFAULT_PUBLISH_DIR%" (
  echo [ERROR] Expected publish directory not found: %DEFAULT_PUBLISH_DIR%
  exit /b 1
)

echo [4/5] Copying publish output to artifacts folder...
robocopy "%DEFAULT_PUBLISH_DIR%" "%PACKAGE_DIR%" /E >nul
if errorlevel 8 (
  echo [ERROR] Failed to copy publish output to artifacts folder.
  exit /b 1
)

echo [5/5] Creating zip package...
where tar >nul 2>nul
if errorlevel 1 (
  echo [ERROR] tar is not available on PATH. Install bsdtar/Windows tar or use WSL zip.
  exit /b 1
)

tar -a -c -f "%ZIPFILE%" -C "%PACKAGE_DIR%" .
if errorlevel 1 (
  echo [ERROR] Failed to create zip package.
  exit /b 1
)

echo.
echo [SUCCESS] Package created:
echo   %ZIPFILE%
echo.
echo Upload options:
echo   1) Azure Portal - Function App - Deployment Center - Zip Deploy
echo   2) Azure CLI:
echo      az functionapp deployment source config-zip --resource-group ^<RG^> --name ^<FUNC_APP_NAME^> --src "%ZIPFILE%"

exit /b 0
