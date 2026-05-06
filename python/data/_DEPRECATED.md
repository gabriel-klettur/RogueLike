# `python/data/` is deprecated as a Unity data source

**Migration date:** 2026-05-06

The Valkur Python data tree (`python/data/`) is **no longer the source of truth**
for the Unity build of Valkur. The migration is complete: every catalog and
config that the Unity runtime cares about lives in Unity-side assets that are
edited natively (Inspector, runtime editors, dedicated tool windows).

## Where data lives now

| What | Source of truth (edit here) |
|---|---|
| Audio (music + SFX + scopes + ducking) | `unity/Valkur/Assets/_Project/Resources/AudioCatalog.asset` |
| Music BPM / beat metadata | Same asset (per-track fields), or runtime editor |
| Items | `unity/Valkur/Assets/_Project/Data/Catalogs/Items/ItemCatalog.asset` |
| Monsters | `unity/Valkur/Assets/_Project/Data/Catalogs/Monsters/*.asset` |
| Spells | `unity/Valkur/Assets/_Project/Data/Catalogs/Spells/SpellCatalog.asset` |
| Buildings | `unity/Valkur/Assets/_Project/Data/Catalogs/Buildings/BuildingCatalog.asset` |
| Particles | `unity/Valkur/Assets/_Project/Data/Catalogs/Particles/ParticlePresetCatalog.asset` |
| Spawners | `unity/Valkur/Assets/_Project/Data/Catalogs/Spawners/SpawnerTemplateCatalog.asset` |
| Lighting Presets | `unity/Valkur/Assets/_Project/Data/LightPresetCatalog.asset` |
| Chat Personas / Assignments | `unity/Valkur/Assets/_Project/Data/ChatPersonas/*.asset` + `ChatAssignmentCatalog.asset` |
| Vendors | `unity/Valkur/Assets/_Project/Data/Vendor/{EconomyGroups,Configs}/*.asset` |
| Players | `unity/Valkur/Assets/_Project/Data/Catalogs/Players/*.asset` |
| World state (placed buildings, lights, spawners, particles, tile overlays) | `unity/Valkur/Assets/StreamingAssets/{Buildings,Lights,Spawners,Particles,Maps}/*.json` (edited in-game via F1/F3/F8/F10/F11/Ctrl+F3) |

## What about the JSON files in this tree?

They are kept **only as recovery snapshots**. Do **not** treat them as the
source of truth — they will drift from the Unity-side data over time.

Every Unity importer that reads from `python/data/` has been renamed to
**`Re-Import * from Legacy Python (one-shot)`** and gates the actual import
behind a confirmation dialog that warns about overwriting Unity edits. Pick a
menu under `Valkur > …` in the Unity Editor and you'll see the new naming.

## When would I run a legacy importer?

Realistically: almost never. Valid scenarios are:

1. **Disaster recovery** — an `.asset` file got corrupted/deleted and you don't
   have a recent backup. The Python JSON is your last-known-good snapshot.
2. **Re-baselining** — you intentionally want to throw away Unity edits and
   restart from the Python data (very rare, and you should probably use git
   history instead).

In both cases, the confirmation dialog (shared via
`LegacyImporterDialog.Confirm`) makes the destructive nature explicit.

## I need to add new content to the game. Where do I put it?

**Don't edit JSON in this folder.** Edit the corresponding ScriptableObject
directly in the Unity Inspector, or use the system's runtime editor:

| To add… | Use |
|---|---|
| A music track | Drop the `.mp3` in `Audio/Music/{Biomes,Zones,Bosses,Events,Stingers}/`, then `Valkur > Audio > Music Scanner` |
| A new item / monster / spell | Edit the catalog `.asset` in the Inspector |
| A boss / NPC | Edit the matching definition `.asset` |
| A zone / map | F11 in-game (Map Editor) |
| Place buildings / lights / spawners / particles in a zone | F10 / Ctrl+F3 / F1 / F3 in-game |
| Tile overlays (dialogue, transitions) | F8 in-game |
| A new chat persona | `ChatPersonas/Persona_*.asset` |

The runtime editors persist via the `IRepository` pattern — your edits land in
`StreamingAssets/` directly, no Python round-trip.

## I'm porting an existing Python feature

If you're still in the migration window for some specific subsystem, the
`burn_system.py` (status-effect DoT) and `item_factory.py` (procedural item
rolls) are the only Python-only systems remaining per `CLAUDE.md`. Those would
follow the same pattern: design the SO, write a one-shot importer if needed,
mark legacy when done.

## Can I delete `python/data/` entirely?

Eventually yes, but not right now. Keep the JSONs around for at least one
release cycle as the snapshot-of-record. Once the Unity-side catalogs have
drifted enough that re-importing would be destructive (which it already would
be — that's why we gated the importers), the Python tree can be archived /
moved to a separate `python-legacy/` location or removed entirely.

Until then: **read-only**.
