# P5: backup lógico privado de staging y restore en una base independiente VACÍA.
# Windows PowerShell 7/5.1 + PostgreSQL client pg_dump / pg_restore / psql.
# Nunca escribir passwords ni backups en el repositorio.
param(
  [Parameter(Mandatory=$true)]
  [ValidateSet('Backup','Restore')]
  [string]$Mode,
  [Parameter(Mandatory=$true)]
  [string]$BackupFile
)
$ErrorActionPreference = 'Stop'
$original = @{}
foreach ($key in @('PGHOST','PGPORT','PGUSER','PGPASSWORD','PGDATABASE','PGSSLMODE')) {
  $original[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
}
function Required([string]$name) {
  $v = [Environment]::GetEnvironmentVariable($name, 'Process')
  if ([string]::IsNullOrWhiteSpace($v)) { throw "Falta variable $name (no introduzca valores en el repositorio)." }
  return $v
}
function Configure([string]$prefix) {
  $hostName = Required "${prefix}_PGHOST"
  if ($hostName -notmatch '\.neon\.tech$') { throw 'Seguridad: solo se admiten hosts directos *.neon.tech.' }
  $port = [Environment]::GetEnvironmentVariable("${prefix}_PGPORT", 'Process')
  if (-not $port) { $port = '5432' }
  foreach ($part in @('PGHOST','PGUSER','PGPASSWORD','PGDATABASE')) {
    [Environment]::SetEnvironmentVariable($part, (Required "${prefix}_$part"), 'Process')
  }
  [Environment]::SetEnvironmentVariable('PGPORT', $port, 'Process')
  [Environment]::SetEnvironmentVariable('PGSSLMODE', 'require', 'Process')
}
function ExecChecked([string]$binary, [string[]]$arguments) {
  & $binary @arguments
  if ($LASTEXITCODE -ne 0) { throw "$binary falló; exit code $LASTEXITCODE. Se detiene sin continuar." }
}
try {
  foreach ($tool in @('pg_dump','pg_restore','psql')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "No está disponible $tool en PATH." }
  }
  $repo = Split-Path $PSScriptRoot -Parent
  $fingerprintSql = Join-Path $repo 'scripts/p2-huellas-37-tablas-readonly.sql'
  if (-not (Test-Path $fingerprintSql)) { throw "Falta $fingerprintSql. Ejecutar desde una copia actualizada del repo." }
  $fullBackup = [System.IO.Path]::GetFullPath($BackupFile)
  $fingerprintFile = "$fullBackup.fingerprints.txt"
  if ($Mode -eq 'Backup') {
    Configure 'P5_SOURCE'
    if ($env:PGDATABASE -ne 'marketplaceerp_test') {
      throw "La base origen debe ser marketplaceerp_test; detectada $($env:PGDATABASE)."
    }
    $actualDb = (& psql -X -A -t -v ON_ERROR_STOP=1 -c 'SELECT current_database();' | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualDb -ne 'marketplaceerp_test') { throw 'Identidad de la base de origen incorrecta.' }
    $dir = Split-Path $fullBackup -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    if (Test-Path $fullBackup) { throw 'El archivo de backup ya existe; no se sobrescribe.' }
    ExecChecked 'pg_dump' @('--format=custom','--no-owner','--no-acl','--file',$fullBackup)
    & psql -X -A -t -v ON_ERROR_STOP=1 -f $fingerprintSql |
      Out-File -FilePath $fingerprintFile -Encoding ascii -NoNewline:$false
    if ($LASTEXITCODE -ne 0) { throw 'Fallo al generar fingerprints de origen.' }
    $digest = (Get-FileHash -Algorithm SHA256 -Path $fullBackup).Hash
    Write-Output "BACKUP_CREADO=$fullBackup"
    Write-Output "SHA256=$digest"
    Write-Output "HUELLAS=$fingerprintFile"
    Write-Output 'Confirme que el origen es la rama Neon STAGING en la consola antes de conservar este backup.'
    Write-Output 'Guardar el archivo y huellas en almacenamiento cifrado FUERA del repositorio.'
  } else {
    if (-not (Test-Path $fullBackup) -or -not (Test-Path $fingerprintFile)) {
      throw 'Faltan el dump o las huellas de origen.'
    }
    $srcHost = Required 'P5_SOURCE_PGHOST'
    $srcDb = Required 'P5_SOURCE_PGDATABASE'
    Configure 'P5_TARGET'
    if ($env:PGHOST -eq $srcHost) {
      throw 'La restauración requiere un endpoint y rama Neon INDEPENDIENTES; host igual al origen.'
    }
    if ($env:PGDATABASE -eq $srcDb) {
      throw 'La restauración NO puede ejecutarse contra el nombre de base de origen.'
    }
    if ($env:PGDATABASE -notmatch '^p5_restore_[a-z0-9_]+$') {
      throw 'La base destino debe ser independiente y llamarse p5_restore_*.'
    }
    $actualDb = (& psql -X -A -t -v ON_ERROR_STOP=1 -c 'SELECT current_database();' | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualDb -ne $env:PGDATABASE) {
      throw 'No se pudo comprobar la identidad del destino.'
    }
    $tables = (& psql -X -A -t -v ON_ERROR_STOP=1 -c "SELECT COUNT(*) FROM pg_tables WHERE schemaname NOT IN ('pg_catalog','information_schema');" | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $tables -ne '0') {
      throw 'Destino NO vacío; se cancela la restauración para evitar sobrescrituras.'
    }
    ExecChecked 'pg_restore' @('--exit-on-error','--no-owner','--no-acl','--dbname',$env:PGDATABASE,$fullBackup)
    $restoredFingerprints = (& psql -X -A -t -v ON_ERROR_STOP=1 -f $fingerprintSql | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'La consulta de integridad del destino falló.' }
    $sourceFingerprints = (Get-Content -Raw $fingerprintFile).Trim()
    if ($restoredFingerprints -ne $sourceFingerprints) { throw 'INTEGRIDAD FALLIDA: huellas origen y destino no coinciden.' }
    Write-Output 'RESTORE_VERIFICADO=37_tablas_con_huellas_iguales'
    Write-Output "DESTINO=$($env:PGDATABASE)"
    Write-Output 'ATENCIÓN: la integridad de datos no equivale a un E2E de aplicación.'
  }
} finally {
  foreach ($key in $original.Keys) {
    [Environment]::SetEnvironmentVariable($key, $original[$key], 'Process')
  }
}
