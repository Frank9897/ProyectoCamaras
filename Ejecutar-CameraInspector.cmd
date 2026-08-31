@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM ============================================================
REM Camera Inspector - Compilacion, publicacion y ejecucion
REM Produce un EXE self-contained para Windows x64 y extrae
REM contenido nativo/LibVLC necesario para el runtime.
REM ============================================================

set "ROOT=%~dp0"
set "APP_PROJECT=%ROOT%CameraInspector.App\CameraInspector.App.csproj"
set "PUBLISH_DIR=%ROOT%CameraInspector.App\bin\Portable\win-x64"
set "APP_EXE=%PUBLISH_DIR%\CameraInspector.exe"
set "BUILD_LOG=%ROOT%CameraInspector_build.log"
set "ERROR_LOG=%ROOT%CameraInspector_error.txt"

cd /d "%ROOT%"

echo ============================================================ > "%BUILD_LOG%"
echo Camera Inspector - %date% %time% >> "%BUILD_LOG%"
echo ============================================================ >> "%BUILD_LOG%"

echo.
echo [1/3] Verificando .NET SDK...
dotnet --info >> "%BUILD_LOG%" 2>&1
if errorlevel 1 (
    echo ERROR: No se encontro un SDK de .NET compatible.
    echo Revisa CameraInspector_build.log en la raiz.
    >> "%ERROR_LOG%" echo [ERROR SDK] %date% %time%
    >> "%ERROR_LOG%" dotnet --info 2^>^&1
    pause
    exit /b 1
)

if exist "%PUBLISH_DIR%" (
    echo Limpiando publicacion anterior...
    rmdir /s /q "%PUBLISH_DIR%" >> "%BUILD_LOG%" 2>&1
)

echo [2/3] Publicando Camera Inspector portable...
echo Comando: dotnet publish "%APP_PROJECT%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o "%PUBLISH_DIR%" >> "%BUILD_LOG%"
dotnet publish "%APP_PROJECT%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o "%PUBLISH_DIR%" >> "%BUILD_LOG%" 2>&1
if errorlevel 1 (
    echo ERROR: Fallo la publicacion.
    echo Revisa CameraInspector_build.log en la raiz.
    >> "%ERROR_LOG%" echo [ERROR PUBLISH] %date% %time%
    type "%BUILD_LOG%" >> "%ERROR_LOG%"
    pause
    exit /b 1
)

if not exist "%APP_EXE%" (
    echo ERROR: No se genero CameraInspector.exe.
    >> "%ERROR_LOG%" echo [ERROR EXE] %date% %time%
    >> "%ERROR_LOG%" echo No se encontro: %APP_EXE%
    pause
    exit /b 1
)

echo [3/3] Iniciando Camera Inspector...
echo EXE: %APP_EXE% >> "%BUILD_LOG%"
start "Camera Inspector" /wait "%APP_EXE%"
set "APP_EXIT=%ERRORLEVEL%"

if not "%APP_EXIT%"=="0" (
    echo La aplicacion termino con codigo %APP_EXIT%.
    >> "%ERROR_LOG%" echo [ERROR EXIT] %date% %time% - Codigo %APP_EXIT%
    echo Se registro el estado en CameraInspector_error.txt
) else (
    echo Camera Inspector termino correctamente.
)

exit /b %APP_EXIT%
