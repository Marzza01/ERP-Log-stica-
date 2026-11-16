@echo off
title LOGIMKT - servidor local
cd /d "%~dp0"
echo Instalando dependencias (solo la primera vez tarda)...
call npm install
echo.
echo Abriendo http://localhost:3000 ...  (cierra esta ventana para apagar la pagina)
start "" http://localhost:3000
call npm run dev
pause
