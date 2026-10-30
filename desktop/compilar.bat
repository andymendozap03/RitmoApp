@echo off
setlocal
cd /d "%~dp0"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
dotnet publish Ritmo.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish
if errorlevel 1 (
  echo No se pudo compilar. Revisa los errores anteriores.
  pause
  exit /b 1
)
echo Aplicacion lista en %~dp0publish\Ritmo.exe
pause
