param(
    [int]$PostgresPort = 5433,
    [switch]$OmitirBaseDatos
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

function Ejecutar-Nativo {
    param(
        [string]$Nombre,
        [scriptblock]$Accion
    )

    Write-Host "`n=== $Nombre ===" -ForegroundColor Cyan
    & $Accion
    if ($LASTEXITCODE -ne 0) {
        throw "$Nombre terminó con código $LASTEXITCODE."
    }
    Write-Host "OK: $Nombre" -ForegroundColor Green
}

Write-Host "LOGIMKT - Fase 8 / Validación integral Grupo A" -ForegroundColor Yellow
Write-Host "Raíz: $Root"

Ejecutar-Nativo "Frontend TypeScript + Vite" {
    Push-Location "frontend\marketplace-ui"
    try { & npm.cmd run build }
    finally { Pop-Location }
}

Ejecutar-Nativo ".NET build" {
    & dotnet build MarketplaceERP.sln
}

Ejecutar-Nativo ".NET tests" {
    & dotnet test MarketplaceERP.sln --no-build
}

if (-not $OmitirBaseDatos) {
    Write-Host "`n=== PostgreSQL / migraciones ===" -ForegroundColor Cyan

    & docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "Docker Desktop no está disponible. Inícialo o usa -OmitirBaseDatos solo si deseas ejecutar la validación de código sin revisar migraciones."
    }

    $env:ConnectionStrings__DefaultConnection = "Host=127.0.0.1;Port=$PostgresPort;Database=marketplace_erp;Username=marketplace_user;Password=marketplace_pass;Include Error Detail=true"

    $migrations = & dotnet ef migrations list `
        --project src/MarketplaceERP.Catalog `
        --startup-project src/MarketplaceERP.API `
        --context CatalogDbContext 2>&1

    $migrations | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo consultar el historial de migraciones."
    }

    if (($migrations -join "`n") -match '\(Pending\)') {
        throw "Hay migraciones Catalog pendientes. No cerrar Fase 8 hasta aplicarlas y volver a ejecutar este script."
    }

    Write-Host "OK: no hay migraciones Catalog pendientes." -ForegroundColor Green
}

Write-Host "`n=== Guardas de arquitectura ===" -ForegroundColor Cyan
$catalogExtensions = Get-Content "src\MarketplaceERP.Catalog\CatalogServiceExtensions.cs" -Raw
if ($catalogExtensions -notmatch 'AddScoped<IStockService,\s*StockService>') {
    throw "IStockService no está registrado contra StockService real."
}
if ($catalogExtensions -match 'AddScoped<IStockService,\s*StockServiceMock>') {
    throw "StockServiceMock volvió a quedar registrado como implementación activa."
}
Write-Host "OK: IStockService usa StockService real." -ForegroundColor Green

$grupoAFrontend = @(
    "frontend\marketplace-ui\src\pages\productos",
    "frontend\marketplace-ui\src\pages\wms",
    "frontend\marketplace-ui\src\pages\fulfillment",
    "frontend\marketplace-ui\src\pages\rma",
    "frontend\marketplace-ui\src\pages\compras"
) | Where-Object { Test-Path $_ }

$hallazgos = @()
foreach ($ruta in $grupoAFrontend) {
    $hallazgos += Get-ChildItem $ruta -Recurse -File -Include *.ts,*.tsx |
        Select-String -Pattern 'mockData|datosDemo|fallbackDemo|DEMO_' -CaseSensitive:$false
}
if ($hallazgos.Count -gt 0) {
    $hallazgos | ForEach-Object { Write-Host $_ }
    throw "Se detectaron marcadores explícitos de datos demo/fallback en pantallas del Grupo A."
}
Write-Host "OK: no se detectaron marcadores explícitos de demo/fallback en las pantallas auditadas." -ForegroundColor Green

Write-Host "`nVALIDACIÓN FASE 8 COMPLETADA SIN ERRORES." -ForegroundColor Green
