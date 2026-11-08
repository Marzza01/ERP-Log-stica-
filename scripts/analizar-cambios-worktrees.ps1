# Auditoria comparativa SOLO LECTURA de cambios locales en TODOS los worktrees.
# Compara el contenido actual contra origin/consolidacion/erp-segura-20261108
# y origin/staging/catalog-500-neon-test sin checkout, stash, reset ni merge.
# No envia codigo ni secretos a GitHub. Solo crea CSV/JSON fuera del repositorio.
# Uso: powershell -NoProfile -File scripts/analizar-cambios-worktrees.ps1
# Requiere Git y PowerShell 5.1+.
[CmdletBinding()]
param(
    [string]$Repo = "C:\Users\Marcelo\Documents\Logistica (ERP)\MarketplaceERP",
    [string]$Destino = "C:\Users\Marcelo\Documents\ERP_Resguardo_PreConsolidacion_20261108",
    [string]$RamaCandidata = "origin/consolidacion/erp-segura-20261108",
    [string]$RamaStaging = "origin/staging/catalog-500-neon-test"
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw "Git no disponible" }
if (-not (Test-Path -LiteralPath $Repo -PathType Container)) { throw "Repositorio no encontrado" }
$root = (& git -C $Repo rev-parse --show-toplevel)
if ($LASTEXITCODE -ne 0) { throw "Ruta no corresponde a un repositorio Git" }
$root = $root.Trim()

foreach ($ref in @($RamaCandidata,$RamaStaging)) {
    & git -C $Repo rev-parse --verify "$ref^{commit}" 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Referencia ausente: $ref. Ejecutar git fetch origin sin --prune primero." }
}

if (-not (Test-Path -LiteralPath $Destino -PathType Container)) {
    throw "La carpeta de respaldo externo no existe: $Destino"
}
$rootFull = [IO.Path]::GetFullPath($root).TrimEnd('\')
$outFull = [IO.Path]::GetFullPath($Destino).TrimEnd('\')
if ($outFull.Equals($rootFull,[StringComparison]::OrdinalIgnoreCase) -or
    $outFull.StartsWith($rootFull+'\',[StringComparison]::OrdinalIgnoreCase)) {
    throw "La salida tiene que estar FUERA del repositorio"
}
$stamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
$reportDir = Join-Path $Destino ("Comparacion_Worktrees_"+$stamp)
New-Item -ItemType Directory -Path $reportDir -ErrorAction Stop | Out-Null

function Try-Blob([string]$Ref,[string]$Path) {
    $spec = $Ref + ":" + $Path
    $result = & git -C $root rev-parse --verify $spec 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return ($result | Select-Object -First 1).Trim()
}
function Git-Safe([string]$Worktree,[string[]]$GitArguments) {
    # $Args es una variable automatica especial de PowerShell.
    # Usar ese nombre como parametro provocaba que git se ejecutara sin subcomando.
    if ($null -eq $GitArguments -or $GitArguments.Count -eq 0) {
        throw "Git-Safe recibio una lista vacia de argumentos para $Worktree"
    }
    $r = @(& git -c core.quotepath=false -C $Worktree @GitArguments)
    if ($LASTEXITCODE -ne 0) {
        throw "Git fallo en ${Worktree}: $($GitArguments -join ' ')"
    }
    return $r
}
$worktrees = @(Git-Safe $root @('worktree','list','--porcelain') |
    Where-Object { $_ -like 'worktree *' } |
    ForEach-Object { $_.Substring(9) })
$registros = New-Object System.Collections.Generic.List[object]
$estados = New-Object System.Collections.Generic.List[object]
foreach ($wt in $worktrees) {
    $head = (Git-Safe $wt @('rev-parse','HEAD') | Select-Object -First 1).Trim()
    $branch = (Git-Safe $wt @('branch','--show-current') | Select-Object -First 1)
    if (-not $branch) { $branch = '(detached HEAD)' }
    $tracked = @(Git-Safe $wt @('diff','--name-only','HEAD','--') |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $untracked = @(Git-Safe $wt @('ls-files','--others','--exclude-standard') |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $deleted = @(Git-Safe $wt @('diff','--name-only','--diff-filter=D','HEAD','--'))
    $all = @($tracked+$untracked | Sort-Object -Unique)
    $counts = @{ 'Identico consolidacion'=0; 'Identico staging'=0; 'Diferente de ambas'=0; 'Nuevo en ambas'=0; 'Eliminado localmente'=0 }
    foreach ($path in $all) {
        $file = Join-Path $wt $path
        $candidate = Try-Blob $RamaCandidata $path
        $staging = Try-Blob $RamaStaging $path
        $baseline = Try-Blob $head $path
        $exists = Test-Path -LiteralPath $file -PathType Leaf
        $actual = $null
        if ($exists) {
            # --path aplica filtros Git (.gitattributes / conversion de fin de linea).
            $res = & git -C $wt hash-object "--path=$path" -- $file
            if ($LASTEXITCODE -ne 0) { throw "No se pudo calcular hash Git de $file" }
            $actual = ($res | Select-Object -First 1).Trim()
        }
        if (-not $exists) { $status = 'Eliminado localmente' }
        elseif ($candidate -and $actual -eq $candidate) { $status = 'Identico consolidacion' }
        elseif ($staging -and $actual -eq $staging) { $status = 'Identico staging' }
        elseif (-not $candidate -and -not $staging) { $status = 'Nuevo en ambas' }
        else { $status = 'Diferente de ambas' }
        $counts[$status]++
        $registros.Add([PSCustomObject]@{
            Worktree=(Split-Path $wt -Leaf); Branch=$branch; HeadSha=$head;
            RutaRelativa=$path; Estado=$status; BlobActual=$actual;
            BlobEnHead=$baseline; BlobEnConsolidacion=$candidate;
            BlobEnStaging=$staging; ArchivoExiste=$exists
        })
    }
    $estados.Add([PSCustomObject]@{
        Worktree=(Split-Path $wt -Leaf); Rama=$branch; Commit=$head;
        Cambios=$all.Count; Eliminados=$deleted.Count;
        IdenticoConsolidacion=$counts['Identico consolidacion'];
        IdenticoStaging=$counts['Identico staging'];
        DiferenteDeAmbas=$counts['Diferente de ambas'];
        NuevoEnAmbas=$counts['Nuevo en ambas'];
        EliminadoLocalmente=$counts['Eliminado localmente']
    })
}
$registros | Export-Csv -NoTypeInformation -Encoding UTF8 (Join-Path $reportDir 'Archivos_Comparados.csv')
$estados | Export-Csv -NoTypeInformation -Encoding UTF8 (Join-Path $reportDir 'Resumen_Worktrees.csv')
[PSCustomObject]@{
    FechaUtc=[DateTime]::UtcNow.ToString('o'); Worktrees=$worktrees.Count;
    TotalArchivos=$registros.Count; ReferenciaCandidata=$RamaCandidata;
    ShaCandidata=(Git-Safe $root @('rev-parse', $RamaCandidata) | Select-Object -First 1);
    ShaStaging=(Git-Safe $root @('rev-parse', $RamaStaging) | Select-Object -First 1);
    Directorio=$reportDir
} | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 (Join-Path $reportDir 'Manifiesto.json')
$estados | Format-Table -AutoSize
Write-Host ""
Write-Host "Analisis terminado (SOLO LECTURA): $reportDir" -ForegroundColor Green
Write-Host "Ninguna rama, worktree ni archivo de origen fue modificado."
