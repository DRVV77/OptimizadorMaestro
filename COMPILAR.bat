@echo off
title Compilando OptimizadorMaestro...
color 0A
echo.
echo  ============================================
echo   OPTIMIZADOR MAESTRO - Compilador
echo  ============================================
echo.

:: Verificar que dotnet este instalado
where dotnet >nul 2>&1
if %errorlevel% neq 0 (
    echo  [ERROR] .NET SDK no encontrado.
    echo.
    echo  Descargalo gratis desde:
    echo  https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    echo  Instala el .NET 8 SDK ^(no solo el Runtime^)
    echo  y vuelve a ejecutar este archivo.
    echo.
    pause
    exit /b 1
)

echo  [OK] .NET SDK encontrado:
dotnet --version
echo.

:: Entrar a la carpeta del proyecto
cd /d "%~dp0"

echo  [1/3] Restaurando paquetes NuGet...
dotnet restore --verbosity quiet
if %errorlevel% neq 0 ( echo  [ERROR] Fallo al restaurar paquetes. & pause & exit /b 1 )

echo  [2/3] Compilando y generando EXE portable...
dotnet publish -c Release -r win-x64 ^
    -p:PublishSingleFile=true ^
    -p:SelfContained=true ^
    -p:PublishReadyToRun=true ^
    -p:EnableCompressionInSingleFile=true ^
    --verbosity quiet

if %errorlevel% neq 0 ( echo  [ERROR] La compilacion fallo. & pause & exit /b 1 )

echo  [3/3] Copiando EXE a la carpeta actual...
copy /Y "bin\Release\net8.0-windows\win-x64\publish\OptimizadorMaestro.exe" "OptimizadorMaestro.exe" >nul

echo.
echo  ============================================
echo   LISTO! Archivo generado:
echo   %~dp0OptimizadorMaestro.exe
echo.
echo   Es un archivo unico portable (~12 MB).
echo   Funciona en cualquier Windows 10/11 x64
echo   sin necesidad de instalar nada mas.
echo  ============================================
echo.

:: Preguntar si abrir la carpeta
set /p OPEN="  Abrir carpeta con el EXE? (S/N): "
if /i "%OPEN%"=="S" explorer "%~dp0"

pause
