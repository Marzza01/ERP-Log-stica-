# Auditoría LOCAL, SOLO LECTURA. PowerShell 7+.
# No ejecuta merge, delete, branch, checkout, push, restore ni deploy.
# USO (desde cualquier carpeta): pwsh -File scripts/consolidacion-preflight.ps1
[CmdletBinding()]
param(
    [string]$OutputDir = (Join-Path (Get-Location).Path "auditoria-git-local")
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Git([string[]]$GitArgs) {
    $value = & git @GitArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($GitArgs -join ' ') falló: $value" }
    return @($value)
}

$top = (Git @('rev-parse', '--show-toplevel') | Select-Object -First 1).Trim()
$head = (Git @('rev-parse', 'HEAD') | Select-Object -First 1).Trim()
$dirty = @(Git @('status', '--porcelain'))
$remote = @(Git @('remote', '-v'))
$refs = @(Git @('for-each-ref', '--format=%(refname)%09%(objectname)', 'refs/heads', 'refs/remotes'))
$worktrees = @(Git @('worktree', 'list', '--porcelain'))
$tags = @(Git @('tag', '--list'))
$pendingChanges = @($dirty | Where-Object { $_ -ne '' })

$protected = @(
    'refs/heads/main',
    'refs/heads/staging/catalog-500-neon-test',
    'refs/heads/consolidacion/erp-segura-20261108',
    'refs/heads/integracion/main-reconciliacion-20261102',
    'refs/remotes/origin/main',
    'refs/remotes/origin/staging/catalog-500-neon-test',
    'refs/remotes/origin/consolidacion/erp-segura-20261108',
    'refs/remotes/origin/integracion/main-reconciliacion-20261102'
)
$refItems = foreach ($line in $refs) {
    $parts = $line -split "`t", 2
    if ($parts.Count -ne 2) { continue }
    [pscustomobject]@{
        Ref = $parts[0]
        Sha = $parts[1]
        ProtectedFromCleanup = ($parts[0] -in $protected)
    }
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$refItems | Export-Csv -NoTypeInformation -Encoding utf8 -Path (Join-Path $OutputDir 'git-refs.csv')
$worktrees | Set-Content -Encoding utf8 (Join-Path $OutputDir 'worktrees-porcelain.txt')
$remote | Set-Content -Encoding utf8 (Join-Path $OutputDir 'remotes.txt')
$tags | Set-Content -Encoding utf8 (Join-Path $OutputDir 'tags.txt')

$manifest = [ordered]@{
    generatedUTC = [DateTime]::UtcNow.ToString("o")
    repository = $top
    headSha = $head
    workingTreeDirty = $pendingChanges.Count -ne 0
    pendingChangeCount = $pendingChanges.Count
    localRefCount = $refItems.Count
    worktreeLines = $worktrees.Count
    tagCount = $tags.Count
    remoteReferencesMayBeStale = $true
    activeRenderProductionBranch = 'integracion/main-reconciliacion-20261102'
    activeRenderStagingBranch = 'staging/catalog-500-neon-test'
    activeVercelProductionBranch = 'main'
    warning = 'Esta auditoría NO determina qué ramas remotas se pueden borrar; revisar PRs, deployments, SHA y worktrees antes. Git remote refs pueden estar desactualizadas: ejecutar git fetch origin sin --prune si se desea actualizar antes.'
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 (Join-Path $OutputDir 'manifest.json')

Write-Host "Auditoría local generada: $OutputDir"
Write-Host "SHA HEAD: $head"
Write-Host "Cambios de trabajo sin confirmar: $($pendingChanges.Count)"
Write-Host 'No se han eliminado ni fusionado ramas.'
