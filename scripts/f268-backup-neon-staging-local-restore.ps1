# F268: respaldo lógico de Neon STAGING y restauración QA en localhost.
# NO modifica la fuente Neon; no usa Neon/Render/Vercel Production.
# Solo se ejecuta -Ejecutar después de introducir credenciales localmente.
[CmdletBinding()]
param(
  [switch]$Ejecutar,
  [switch]$SoloPreflight
)
$ErrorActionPreference = 'Stop'
$hostStaging = 'ep-sparkling-smoke-bbcm2qma.c-15.us-east-1.aws.neon.tech'
$baseStaging = 'marketplaceerp_test'
$endpointId = 'ep-sparkling-smoke-bbcm2qma'
$repo = Split-Path $PSScriptRoot -Parent
$p5Script = Join-Path $PSScriptRoot 'p5-backup-restore-safe.ps1'
$huellas = Join-Path $PSScriptRoot 'p2-huellas-37-tablas-readonly.sql'
$huellasExtras = Join-Path $PSScriptRoot 'f268-finance-tablas-extra-readonly.sql'
$inventorySql = @'
SELECT (SELECT COUNT(*) FROM pg_tables WHERE schemaname IN ('core','catalog','finance'))::text
       || '|' || (SELECT COUNT(*) FROM core."__EFMigrationsHistory")::text
       || '|' || (SELECT COUNT(*) FROM catalog."__EFMigrationsHistory")::text
       || '|' || (SELECT COUNT(*) FROM finance."__EFMigrationsHistory")::text;
'@
$backupDir = Join-Path $env:USERPROFILE 'Documents\Logistica (ERP)\_Codex_F220_Cierre_20261112\F268_BACKUPS_PRIVADOS'
$oldEnvironment = @{}
$protectedKeys = @('PGHOST','PGPORT','PGUSER','PGPASSWORD','PGDATABASE',
  'PGSSLMODE','P5_SOURCE_PGHOST','P5_SOURCE_PGPORT','P5_SOURCE_PGUSER',
  'P5_SOURCE_PGPASSWORD','P5_SOURCE_PGDATABASE')
foreach($key in $protectedKeys) {
  $oldEnvironment[$key] = [Environment]::GetEnvironmentVariable($key,'Process')
}

function RequireTool([string]$name) {
  if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
    throw "Herramienta requerida ausente: $name"
  }
}

function Set-LocalPostgresFromTests {
  # TEST_PG_CONNECTION señala que ya existe un laboratorio PostgreSQL
  # autorizado. NO se imprime ni parsea su secreto en este script.
  if ([string]::IsNullOrWhiteSpace($env:TEST_PG_CONNECTION)) {
    throw 'No existe laboratorio PostgreSQL autorizado (TEST_PG_CONNECTION).'
  }
  if ([string]::IsNullOrWhiteSpace($script:localPgPassword)) {
    $localSegura = Read-Host 'Contraseña de PostgreSQL LOCAL (oculta)' -AsSecureString
    if ($localSegura.Length -eq 0) { throw 'Contraseña local vacía' }
    $script:localPgPassword =
      [System.Net.NetworkCredential]::new('', $localSegura).Password
    Remove-Variable localSegura -ErrorAction SilentlyContinue
  }
  # Solo loopback: jamás se acepta host remoto para el restore.
  $env:PGHOST = '127.0.0.1'
  $env:PGPORT = '5432'
  $env:PGUSER = 'postgres'
  $env:PGDATABASE = 'postgres'
  $env:PGPASSWORD = $script:localPgPassword
  $env:PGSSLMODE = 'disable'
}

function Checked([string]$program,[string[]]$args) {
  & $program @args
  if ($LASTEXITCODE -ne 0) {
    throw "$program finalizó con error $LASTEXITCODE"
  }
}

$temporal = $null
try {
  $pgPath = 'C:\Program Files\PostgreSQL\18\bin'
  if (Test-Path $pgPath) { $env:PATH = $pgPath + ';' + $env:PATH }
  foreach($tool in @('pg_dump','pg_restore','psql')) { RequireTool $tool }
  foreach($file in @($p5Script,$huellas,$huellasExtras)) {
    if (-not (Test-Path $file)) { throw "Falta archivo de control: $file" }
  }
  if (-not $Ejecutar -or $SoloPreflight) {
    Write-Output 'F268_PREFLIGHT_OK=tools_y_scripts_disponibles'
    Write-Output "NEON_PROJECT=tiny-sound-11266101; BRANCH=br-aged-morning-bbordmzw; ENDPOINT=$endpointId"
    Write-Output 'No se consultó contraseña ni se generó backup; para ejecutar use -Ejecutar.'
    return
  }

  # Proceso interactivo: el usuario introduce la contraseña en Windows,
  # NUNCA en ChatGPT, GitHub, argumentos, logs ni ficheros de texto.
  $rol = Read-Host 'Nombre de rol PostgreSQL de Neon STAGING'
  if ($rol -ne 'marketplaceerp_test_owner') {
    throw 'Rol inesperado para la base STAGING; cancelar antes de conectarse.'
  }
  $secure = Read-Host 'Contraseña de Neon STAGING (oculta)' -AsSecureString
  if ($secure.Length -eq 0) { throw 'Contraseña vacía' }
  $env:P5_SOURCE_PGHOST = $hostStaging
  $env:P5_SOURCE_PGPORT = '5432'
  $env:P5_SOURCE_PGUSER = $rol
  $env:P5_SOURCE_PGDATABASE = $baseStaging
  $env:P5_SOURCE_PGPASSWORD = [System.Net.NetworkCredential]::new('', $secure).Password

  if (-not (Test-Path $backupDir)) {
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
  }
  $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
  $destino = Join-Path $backupDir ("erp-staging-f268-" + $stamp + "-" + [guid]::NewGuid().ToString('N') + ".dump")
  # Backup del P5 usa la fuente SOLO para SELECT/pg_dump y compara DB:
  & $p5Script -Mode Backup -BackupFile $destino
  if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Backup P5 terminó con error.' }
  if (-not (Test-Path $destino) -or -not (Test-Path "$destino.fingerprints.txt")) {
    throw 'Respaldo o huellas ausentes'
  }
  $digest = (Get-FileHash -Path $destino -Algorithm SHA256).Hash
  if ((& pg_restore --list $destino | Measure-Object -Line).Lines -lt 40) {
    throw 'El archivo no contiene el inventario mínimo del esquema'
  }
  Write-Output 'F268_BACKUP_LOGICO=OK'
  Write-Output ('F268_SHA256='+$digest)
  Write-Output ('F268_ARCHIVO='+$destino)

  # Inventario actual: 39 tablas (37 históricas más 2 Finance),
  # historial Core2/Catalog7/Finance10. Fallar cerrado si difiere.
  $env:PGHOST = $hostStaging
  $env:PGPORT = '5432'
  $env:PGUSER = $rol
  $env:PGDATABASE = $baseStaging
  $env:PGPASSWORD = $env:P5_SOURCE_PGPASSWORD
  $env:PGSSLMODE = 'require'
  $metadataOrigen = (& psql -X -A -t -v ON_ERROR_STOP=1 -c $inventorySql | Out-String).Trim()
  if ($LASTEXITCODE -ne 0 -or $metadataOrigen -ne '39|2|7|10') {
    throw 'El inventario STAGING no coincide con 39 tablas y migraciones 2/7/10.'
  }
  $extrasOrigen = (& psql -X -A -t -v ON_ERROR_STOP=1 -f $huellasExtras | Out-String).Trim()
  if ($LASTEXITCODE -ne 0 -or -not $extrasOrigen) {
    throw 'No se obtuvieron huellas de las 2 tablas Finance adicionales.'
  }
  $extrasOrigen | Out-File -FilePath "$destino.finance-extra.txt" -Encoding ascii
  Write-Output 'F268_INVENTARIO_ORIGEN=39_tablas_migraciones_2_7_10'

  # Restauración de prueba EXCLUSIVAMENTE a BD efímera en localhost.
  # Se verifica el prefijo y se borra solo la base creada en ESTE proceso.
  Set-LocalPostgresFromTests
  $identidadLocal = (& psql -X -A -t -v ON_ERROR_STOP=1 -c 'SELECT current_database(), current_user;' | Out-String).Trim()
  if ($LASTEXITCODE -ne 0 -or $identidadLocal -ne 'postgres|postgres') {
    throw 'Identidad del PostgreSQL local inesperada; abortar sin CREATE DATABASE.'
  }
  $temporal = 'ci_f268_restore_' + [guid]::NewGuid().ToString('N')
  if ($temporal -notmatch '^ci_f268_restore_[0-9a-f]{32}$') {
    throw 'Nombre de DB temporal no autorizado'
  }
  Checked 'psql' @('-X','-v','ON_ERROR_STOP=1','-c',("CREATE DATABASE " + $temporal))
  $databaseCreated = $true
  $env:PGDATABASE = $temporal
  Checked 'pg_restore' @('--exit-on-error','--no-owner','--no-acl','--dbname',$temporal,$destino)
  $restaurado = (& psql -X -A -t -v ON_ERROR_STOP=1 -f $huellas | Out-String).Trim()
  if ($LASTEXITCODE -ne 0) { throw 'No se calcularon huellas de restauración' }
  $original = (Get-Content -Raw "$destino.fingerprints.txt").Trim()
  if ($restaurado -ne $original) { throw 'HUELLAS DISTINTAS: no certificar restauración' }
  $metadataDestino = (& psql -X -A -t -v ON_ERROR_STOP=1 -c $inventorySql | Out-String).Trim()
  if ($LASTEXITCODE -ne 0 -or $metadataDestino -ne $metadataOrigen) {
    throw 'RESTAURACION INCOMPLETA: tablas o historiales EF difieren.'
  }
  $extrasDestino = (& psql -X -A -t -v ON_ERROR_STOP=1 -f $huellasExtras | Out-String).Trim()
  if ($LASTEXITCODE -ne 0 -or
      $extrasDestino -ne (Get-Content -Raw "$destino.finance-extra.txt").Trim()) {
    throw 'HUELLAS FINANCE 1:N/PREENVIO DISTINTAS: no certificar.'
  }
  Write-Output 'F268_RESTORE_LOCAL=39_tablas_y_huellas_con_historiales_2_7_10'
  Write-Output 'F268_RESULTADO=BACKUP_Y_RESTORE_VERIFICADOS'
}
finally {
  if ($temporal -and $temporal -match '^ci_f268_restore_[0-9a-f]{32}$' -and $databaseCreated) {
    try {
      Set-LocalPostgresFromTests
      Checked 'psql' @('-X','-v','ON_ERROR_STOP=1','-c',("DROP DATABASE IF EXISTS " + $temporal + " WITH (FORCE)"))
      Write-Output 'F268_LIMPIEZA_LOCAL=OK'
    } catch {
      Write-Warning 'La base efímera local no pudo limpiarse; requiere revisión manual sin tocar otra base.'
    }
  }
  foreach($key in $protectedKeys) {
    [Environment]::SetEnvironmentVariable($key,$oldEnvironment[$key],'Process')
  }
  Remove-Variable secure -ErrorAction SilentlyContinue
  Remove-Variable localPgPassword -Scope Script -ErrorAction SilentlyContinue
}
