# RimBridge coverage audit

Goal: an API into the entire game. Per-system rule: **state snapshot + validated actions**
(raw `engine.*` reflection stays as the fallback, not the interface).

Generated 2026-09-20 from `bridge.methods` (87 methods) plus a live playtest.

## Covered

| System | State | Control |
|---|---|---|
| Colony overview | `state.summary`, `state.colony_context` | — |
| Pawns | `state.pawns`, `state.pawn` (full), `state.pawn_context` | `ui.*` pawn verbs |
| Decisions | `decision.candidates` (all/combat/triage/work/emergency) | `action.execute` (tick-staleness + legality recheck) |
| Events | `events?since=` + ledger file; decision triggers (`pawn_idle`, `job_finished`, `job_failed`, `pawn_injured`, `enemy_entered_range`, `target_died`, `fire_started`, `patient_needs_tending`, `colonist_mental_break_risk`, `resource_critical`, `pawn_downed`) | — |
| Map | `map.view/detail/find/cell/overview/survey/path/power/reachable/terrain_stats/screenshot` | `ui.build/build_many/designate/zone/wire` |
| Stocks/storage | `state.stocks`, `state.storage` | `ui.storage` |
| Work | `state.work_matrix`, `state.bills`, `defs.work_types` | `ui.set_work/set_work_many/set_schedule/add_bill/bill` |
| Power | `state.power`, `map.power` | — (read-only) |
| Rooms/zones/areas | `state.rooms`, `state.areas` | `ui.zone`, `ui.area` |
| Research | `state.research` | `ui.set_research` |
| Factions | `state.factions` | — (read-only) |
| Quests | `state.quests` | — (read-only) |
| Threats/alerts | `state.threats`, `state.alerts` | `dev.kill_hostiles` only |
| Letters/dialogs | `state.letters`, `state.dialogs` | `ui.letter`, `ui.dialog` |
| Policies | `state.policies` | `ui.set_policies`, `ui.animal`, `ui.prisoner` |
| Designations | `state.designations` | `ui.designate` |
| Defs/reflection | `defs.get/search/buildable`, `engine.get/call/set/members/types/new` | `engine.*` (escape hatch) |
| Game control | `game.status` | `game.speed/pause/save/load/new_game/quit_to_menu/dev_mode` |
| Dev/test | — | `dev.*` (spawn/damage/heal/kill/incident/weather/…) |
| Anchors | `anchor.list` | `anchor.set/delete` |

## Gaps (build in this order)

1. **World layer** — DONE state (`world.overview/tile/caravans/caravan/settlements/settlement/sites/ships`,
   `settlement.abandon`, `caravan.form/stop/reroute/pause`, `pods.list/load/board/launch/cancel` with
   gift/trade/visit/attack arrivals). Live-verified 2026-09-20: `pods.launch` liftoff (visit),
   `caravan.form` + full travel, `trade.execute` at destination (buy+sell lines), `caravan.reroute`.
   Still open: caravan merging mid-travel.
2. **Quest choices** — DONE detail (`quest.detail`: parts, look targets, linked letters; accept/decline
   already flow through `ui.letter`/`ui.dialog`). Still open: reward choice dialogs for edge cases.
3. **Diplomacy actions** — DONE detail (`diplomacy.detail`: posture, leader, prisoners held, nearest
   settlement, available levers). Still open: gift execution (needs loaded transport pods), peace talks
   (needs a caravan on site), ally calls.
4. **Medical bills** — DONE (`medical.bills/options/add_bill/bill`: vanilla CreateSurgeryBill,
   worker legality, implant items, faction-anger warnings).
5. **Rituals/ideology** — DONE state + `ritual.list/start` (vanilla lord job, role auto-fill).
   Live-verified 2026-09-20: LeaderSpeech start; same-ideo disambiguation; honest refusals for
   missing roles/behavior. Open: animal-sacrifice positive path (needs slaughter-festival ideo),
   role-less gatherings (vanilla lord job refuses them; direct-execute path unknown).
6. **Mechs/genes/children/anomaly** — DONE state + `mech.list/setmode` (live-verified: Lifter→Escort),
   `gene.status/extract/cancel`. Live-verified 2026-09-20: Greaser-Xenohuman prisoner Gretty
   captured, contained via `gene.extract` (powered extractor + vanometric cell). Note: spawned
   pawns generate baseliner; hostile downed refused by vanilla `CanAcceptPawn`; escort hostile
   grenadiers with `dev.kill_hostiles` FIRST — they bomb buildings.
7. **Alert verbs** — DONE (`alerts.detail`: culprits + suggested fix for 60+ alert classes).
8. **Power control** — assessed: covered via `ui.press` toggles + `ui.designate`/`ui.build`; no dedicated
   verbs needed.
7. **Alert resolution verbs** — map each active alert to the action that clears it.
8. **Power control** — switch/battery/fuel management (currently read-only).
9. **Trade** — DONE via `trade.execute` (live-verified at settlement: sold Traci's knife for
   silver, `trade.execute` at Hazli; dialog-trade Buy counts delegate to vanilla `AdjustTo` clamp);
   `shuttle.list/launch`
   (live-verified: passenger-shuttle visit flight). Imperial quest shuttles fly on quest orders
   (no free-flight verb in vanilla; endpoint reports readiness honestly).

## Live-test notes (2026-09-20; quicktest colony "bridge", then RimBridgeQuick save)

- `action.execute` verified end to end: cut + tend jobs taken through the real job system;
  stale ids rejected by tick age.
- Decision events firing live: enemy_entered_range, pawn_idle, job_finished, resource_critical.
- Rescue/tend generation follows vanilla rules (no bed / enemy near / no medicine = no candidate, correctly).
- Combat execute verified live (draft + melee job vs spawned hostile). `pods.launch` verified
  live (fuel via launcher-built pod only — dev-spawned pods never link fuel; boarded pawn inside).
- `game.log_tail` Windows LocalLow path fixed 2026-09-20; `dev.set_need` dead-pawn guard added.

- `pawn_context` + `colony_context` correct; fixed threat filter (`ThreatDisabled`) to match `state.summary`.
- `decision.candidates` empty on a fresh map is correct (nothing legal yet); `continue_current_job`
  appeared live once vanilla took the harvestwood job.
- Vanilla legality bites: grown trees need `harvestwood`, not `cut` (empty-reason refusal is the game,
  not the bridge). Workgiver class names verified against the 1.6 DLL: `WorkGiver_Miner`,
  `WorkGiver_GrowerHarvest`, `WorkGiver_PlantsCut`, `WorkGiver_GrowerSow`, `WorkGiver_HunterHunt`,
  `WorkGiver_Researcher` (fixed 2026-09-20).
