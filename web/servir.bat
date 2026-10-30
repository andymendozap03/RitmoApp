@echo off
setlocal
cd /d "%~dp0"

where java >nul 2>nul
if errorlevel 1 (
  echo.
  echo   No encontre Java en el PATH.
  echo.
  echo   Opcion 1: instala el JDK 17 (adoptium.net) y vuelve a ejecutar este archivo.
  echo   Opcion 2: si tienes Python, abre una consola aqui y ejecuta:
  echo              python -m http.server 8101
  echo.
  pause
  exit /b 1
)

start "" /b cmd /c "timeout /t 2 >nul & start "" http://localhost:8101"
java Servidor.java 8101

echo.
echo Servidor detenido.
pause
