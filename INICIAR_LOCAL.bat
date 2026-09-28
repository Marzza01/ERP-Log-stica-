@echo off
title Marketplace ERP - Servidores Locales
echo ======================================================
echo           MARKETPLACE ERP - INICIO LOCAL
echo ======================================================
echo.
echo Obteniendo direccion IP local...
for /f "tokens=4" %%a in ('route print ^| findstr 0.0.0.0 ^| findstr /v "::"') do (
    set LOCAL_IP=%%a
    goto :found_ip
)
:found_ip

echo.
echo ======================================================
echo Direcciones de acceso permanente (mientras la PC este encendida):
echo.
echo   En esta PC (Localhost):
echo   - Frontend: http://localhost:5173
echo   - Backend:  http://localhost:5000/api
echo   - Swagger:  http://localhost:5000/swagger
echo.
echo   Desde celulares, tablets o laptops conectadas a tu red Wi-Fi:
echo   - Acceso:   http://%LOCAL_IP%:5173
echo ======================================================
echo.

echo Iniciando Backend (.NET API en puerto 5000)...
start "Marketplace ERP - Backend API" cmd /k "cd /d "%~dp0" && dotnet run --project src/MarketplaceERP.API/MarketplaceERP.API.csproj --urls=http://localhost:5000"

echo Esperando 3 segundos...
timeout /t 3 /nobreak >nul

echo Iniciando Frontend (Vite en puerto 5173)...
start "Marketplace ERP - Frontend UI" cmd /k "cd /d "%~dp0frontend\marketplace-ui" && npm run dev"

echo.
echo Los servidores estan corriendo en ventanas independientes.
echo Para detenerlos, simplemente cierra las ventanas de comando creadas.
echo.
pause
