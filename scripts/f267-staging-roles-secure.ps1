# F267 aceptación HTTP de roles contra STAGING, sin escribir contraseñas.
# Uso sin credenciales: powershell -File ... -SoloAnonimo
# Uso completo: powershell -File ... -Autenticado (tres contraseñas ocultas)
[CmdletBinding()]
param([switch]$SoloAnonimo,[switch]$Autenticado)
$ErrorActionPreference = 'Stop'
$test = Join-Path $PSScriptRoot 'f267-staging-roles-acceptance.mjs'
if (-not (Test-Path $test)) { throw 'Falta prueba F267 local.' }
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw 'Node.js no disponible.' }
if ($SoloAnonimo -or -not $Autenticado) {
  & node $test --anonymous
  if ($LASTEXITCODE -ne 0) { throw 'F267 anónimo falló.' }
  return
}
$keys = @('F267_ADMIN_EMAIL','F267_ADMIN_PASSWORD','F267_BUYER_EMAIL',
          'F267_BUYER_PASSWORD','F267_SELLER_EMAIL','F267_SELLER_PASSWORD')
$old = @{}
foreach($key in $keys) {
  $old[$key] = [Environment]::GetEnvironmentVariable($key,'Process')
}
try {
  $adminEmail = Read-Host 'Email Admin de STAGING [admin.evaluacion@erp.example]'
  if ([string]::IsNullOrWhiteSpace($adminEmail)) {
    $adminEmail = 'admin.evaluacion@erp.example'
  }
  $buyerEmail = Read-Host 'Email Comprador QA STAGING'
  $sellerEmail = Read-Host 'Email Vendedor QA STAGING'
  if ($buyerEmail -notmatch '^[^@\s]+@[^@\s]+\.[^@\s]+$' -or
      $sellerEmail -notmatch '^[^@\s]+@[^@\s]+\.[^@\s]+$') {
    throw 'Emails QA incompletos; abortar sin llamar a API.'
  }
  $adminSecure = Read-Host 'Contraseña Admin STAGING (oculta)' -AsSecureString
  $buyerSecure = Read-Host 'Contraseña Comprador STAGING (oculta)' -AsSecureString
  $sellerSecure = Read-Host 'Contraseña Vendedor STAGING (oculta)' -AsSecureString
  if ($adminSecure.Length -eq 0 -or $buyerSecure.Length -eq 0 -or $sellerSecure.Length -eq 0) {
    throw 'Contraseña QA vacía.'
  }
  $env:F267_ADMIN_EMAIL = $adminEmail
  $env:F267_BUYER_EMAIL = $buyerEmail
  $env:F267_SELLER_EMAIL = $sellerEmail
  $env:F267_ADMIN_PASSWORD = [System.Net.NetworkCredential]::new('', $adminSecure).Password
  $env:F267_BUYER_PASSWORD = [System.Net.NetworkCredential]::new('', $buyerSecure).Password
  $env:F267_SELLER_PASSWORD = [System.Net.NetworkCredential]::new('', $sellerSecure).Password
  & node $test
  if ($LASTEXITCODE -ne 0) { throw 'F267 con identidades autenticadas falló.' }
} finally {
  foreach($key in $keys) {
    [Environment]::SetEnvironmentVariable($key,$old[$key],'Process')
  }
  Remove-Variable adminSecure,buyerSecure,sellerSecure -ErrorAction SilentlyContinue
}
