# Respaldo externo local del historial Git. PowerShell 7+.
# No hace push/merge/delete/reset/rebase y NO respalda datos de Neon.
# Ejecutar desde la raíz de un clon completo, SIN partial/shallow clone.
# Ejemplo: pwsh -File scripts/consolidacion-backup-bundle.ps1 -OutputDir "D:\\Backups\\MarketplaceERP"
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OutputDir)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'git no está instalado.' }
$root = (& git rev-parse --show-toplevel)
if ($LASTEXITCODE -ne 0 -or -not $root) { throw 'No se está dentro de un repositorio Git.' }
$root = $root.Trim()
if ((& git rev-parse --is-shallow-repository).Trim() -eq 'true') {
    throw 'Clon shallow: completar historial antes de generar el bundle.'
}
$target = [System.IO.Path]::GetFullPath($OutputDir)
$repoPath = [System.IO.Path]::GetFullPath($root)
if ($target.StartsWith($repoPath + [System.IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase) -or $target -eq $repoPath) {
    throw 'El respaldo debe quedar FUERA del repositorio.'
}

# No se usa --prune. Esto actualiza refs remotas del clon, NO las ramas de GitHub.
& git fetch origin '+refs/heads/*:refs/remotes/origin/*' --tags
if ($LASTEXITCODE -ne 0) { throw 'git fetch falló. No se creó respaldo.' }

New-Item -ItemType Directory -Path $target -Force | Out-Null
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$bundle = Join-Path $target "MarketplaceERP-history-$stamp.bundle"
$shaPath = "$bundle.sha256"
$manifestPath = "$bundle.manifest.json"
if (Test-Path $bundle) { throw 'No sobrescribir un respaldo existente.' }

& git bundle create $bundle --all
if ($LASTEXITCODE -ne 0) { throw 'git bundle create falló.' }
& git bundle verify $bundle
if ($LASTEXITCODE -ne 0) { throw 'git bundle verify falló: NO dar este respaldo por válido.' }

$hash = (Get-FileHash -Path $bundle -Algorithm SHA256).Hash
$head = (& git rev-parse HEAD).Trim()
$heads = @(& git for-each-ref '--format=%(refname) %(objectname)' refs/heads refs/remotes/origin)
[ordered]@{
    utc = [DateTime]::UtcNow.ToString('o')
    repository = $root
    localHead = $head
    bundleFile = $bundle
    sha256 = $hash
    gitBundleVerifyPassed = $true
    refsIncluded = $heads
    note = 'No incluye datos de Neon, secretos de Render/Vercel ni worktrees no confirmados. Mantener copia privada. Probar restauración en clon aislado antes de limpiar ramas.'
} | ConvertTo-Json -Depth 10 | Set-Content -Encoding utf8 $manifestPath
"$hash  $(Split-Path -Leaf $bundle)" | Set-Content -Encoding ascii $shaPath
Write-Host "Bundle verificado: $bundle"
Write-Host "SHA256: $hash"
Write-Host 'IMPORTANTE: conservar en ubicación externa segura y ensayar restore antes de borrar ramas.'
