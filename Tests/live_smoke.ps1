# Live smoke suite for RimBridge: exercises every read-only endpoint against the
# running game and fails loudly on the first refusal. Non-mutating by design
# (no dev.*, no launches, no trades); run targeted live tests separately.
# Usage: pwsh -File Tests/live_smoke.ps1  (bridge at 127.0.0.1:8765)
param([string]$Base = 'http://127.0.0.1:8765')
$ErrorActionPreference = 'Stop'
$script:pass = 0; $script:fail = @()
function Rpc($m, $p) {
    $b = @{ method = $m; params = $p } | ConvertTo-Json -Depth 8 -Compress
    Invoke-WebRequest "$Base/rpc" -Method POST -Body $b -ContentType 'application/json' -UseBasicParsing -TimeoutSec 60 |
        Select-Object -ExpandProperty Content | ConvertFrom-Json
}
function Check($name, $method, $params) {
    try {
        $r = Rpc $method $params
        if ($r.ok -eq $true) { $script:pass++; return $r.result }
        throw $r.error
    } catch {
        $script:fail += "$name :: $_"
        return $null
    }
}
function CheckSkipEmpty($name, $method, $params, $list, $key) {
    if (-not $list -or $list.Count -eq 0) { Write-Host "skip $name (empty)"; return $null }
    $id = if ($key) { $list[0].$key } else { $list[0].id }
    if (-not $id) { $id = $list[0].name }
    return Check $name $method ($params + @{ $key = $id })
}
Check 'health' 'game.status' @{} | Out-Null
$colonists = (Check 'pawns' 'state.pawns' @{ filter = 'colonists' })
$pawn = if ($colonists) { $colonists[0].id } else { $null }
$pos = if ($colonists) { $colonists[0].pos } else { @(125, 115) }
$nx = [int]$pos[0] + 5; $nz = [int]$pos[1]; $near = @($nx, $nz)
Check 'state.base' 'state.base' @{} | Out-Null
Check 'colony_context' 'state.colony_context' @{} | Out-Null
Check 'summary' 'state.summary' @{} | Out-Null
Check 'alerts' 'state.alerts' @{} | Out-Null
Check 'threats' 'state.threats' @{} | Out-Null
Check 'power' 'state.power' @{} | Out-Null
Check 'stocks' 'state.stocks' @{} | Out-Null
Check 'storage' 'state.storage' @{} | Out-Null
Check 'research' 'state.research' @{} | Out-Null
Check 'policies' 'state.policies' @{} | Out-Null
Check 'work_matrix' 'state.work_matrix' @{} | Out-Null
Check 'rooms' 'state.rooms' @{} | Out-Null
Check 'areas' 'state.areas' @{} | Out-Null
Check 'designations' 'state.designations' @{} | Out-Null
Check 'letters' 'state.letters' @{} | Out-Null
Check 'dialogs' 'state.dialogs' @{} | Out-Null
Check 'factions' 'state.factions' @{} | Out-Null
Check 'quests' 'state.quests' @{} | Out-Null
if ($pawn) {
    Check 'pawn' 'state.pawn' @{ pawn = $pawn } | Out-Null
    Check 'pawn_context' 'state.pawn_context' @{ pawn = $pawn } | Out-Null
    Check 'candidates' 'decision.candidates' @{ pawn = $pawn } | Out-Null
    Check 'life.detail' 'life.detail' @{ pawn = $pawn } | Out-Null
    Check 'medical.bills' 'medical.bills' @{ pawn = $pawn } | Out-Null
    Check 'medical.options' 'medical.options' @{ pawn = $pawn } | Out-Null
    Check 'reachable' 'map.reachable' @{ pawn = $pawn; target = $near } | Out-Null
}
Check 'children' 'life.children' @{} | Out-Null
Check 'platforms' 'life.platforms' @{} | Out-Null
Check 'ideo' 'ideo.detail' @{} | Out-Null
Check 'rituals' 'ritual.list' @{} | Out-Null
Check 'map.detail' 'map.detail' @{ w = 24; h = 24 } | Out-Null
Check 'map.overview' 'map.overview' @{ blocks = 20 } | Out-Null
Check 'map.survey' 'map.survey' @{ budget = 1500 } | Out-Null
Check 'map.cell' 'map.cell' @{ cell = $pos } | Out-Null
Check 'map.find' 'map.find' @{ kind = 'item'; limit = 5 } | Out-Null
Check 'map.path' 'map.path' @{ from = $pos; to = $near } | Out-Null
Check 'map.terrain' 'map.terrain_stats' @{} | Out-Null
Check 'map.view' 'map.view' @{ w = 30; h = 20 } | Out-Null
Check 'map.powercam' 'map.power' @{ w = 20; h = 20 } | Out-Null
Check 'map.rects' 'map.open_rects' @{ w = 5; h = 5 } | Out-Null
Check 'defs.buildable' 'defs.buildable' @{} | Out-Null
Check 'defs.search' 'defs.search' @{ query = 'Wall'; type = 'ThingDef' } | Out-Null
Check 'defs.get' 'defs.get' @{ def = 'Wall' } | Out-Null
Check 'defs.work_types' 'defs.work_types' @{} | Out-Null
Check 'caravans' 'world.caravans' @{} | Out-Null
Check 'w.overview' 'world.overview' @{} | Out-Null
Check 'settlements' 'world.settlements' @{ tradable = $true } | Out-Null
Check 'ships' 'world.ships' @{} | Out-Null
Check 'sites' 'world.sites' @{} | Out-Null
Check 'tile' 'world.tile' @{} | Out-Null
Check 'pods.list' 'pods.list' @{} | Out-Null
Check 'shuttle.list' 'shuttle.list' @{} | Out-Null
Check 'mech.list' 'mech.list' @{} | Out-Null
Check 'gene.status' 'gene.status' @{} | Out-Null
$factions = Check 'factions2' 'state.factions' @{}
if ($factions) {
    $other = $factions | Where-Object { $_.name -and -not $_.player } | Select-Object -First 1
    if ($other) { Check 'diplomacy' 'diplomacy.detail' @{ faction = $other.name } | Out-Null }
}
$quests = Check 'quests2' 'state.quests' @{}
if ($quests -and $quests.Count -gt 0) {
    $qid = $quests[0].id; if (-not $qid) { $qid = $quests[0].name }
    if ($qid) { Check 'quest.detail' 'quest.detail' @{ id = $qid } | Out-Null }
} else { Write-Host 'skip quest.detail (no quests)' }
$cars = Check 'caravans2' 'world.caravans' @{}
if ($cars -and $cars.Count -gt 0) { Check 'world.caravan' 'world.caravan' @{ id = $cars[0].id } | Out-Null } else { Write-Host 'skip world.caravan (none)' }
$ss = Check 'settlements2' 'world.settlements' @{}
if ($ss -and $ss.Count -gt 0) { Check 'world.settlement' 'world.settlement' @{ id = $ss[0].id } | Out-Null } else { Write-Host 'skip world.settlement (none)' }
Write-Host "`nPASS: $($script:pass)  FAIL: $($script:fail.Count)"
foreach ($f in $script:fail) { Write-Host "FAIL $f" }
if ($script:fail.Count -gt 0) { exit 1 }
