@echo off
setlocal

REM Este script lanza el ejecutable WPF compilado de Camera Inspector.
REM El manifiesto del EXE solicita elevación administrativa mediante UAC.
set "APP_DIR=%~dp0CameraInspector.App\bin\Debug\net9.0-windows"
set "APP_EXE=%APP_DIR%\CameraInspector.exe"

if not exist "%APP_EXE%" (
    echo No se encontro CameraInspector.exe.
    echo.
    echo Primero compile el proyecto con:
    echo   dotnet build
    echo.
    pause
    exit /b 1
)

start "Camera Inspector" "%APP_EXE%"
exit /b 0
