# Python SQLite → Unity Audit (2026-05-03)

**Database:** `python/data/roguelike.sqlite3` (~500 KB, ~1,500 rows across 14 tables)
**Stack:** SQLAlchemy 2.0 + Alembic + SQLite (WAL, synchronous=NORMAL)
**Source-of-truth:** the JSONs in `python/data/`. The DB was a *queryable cache* of those JSONs — `import_log.content_hash` confirms re-imports skipped when the JSON didn't change.

---

## Verdict

**Do NOT port `roguelike.sqlite3` to Unity.** Every Python table either:
- already has a 1:1 Unity equivalent (ScriptableObject catalog or JSON in `StreamingAssets/`), OR
- exists ONLY because Python lacked Unity's asset system (e.g. `entities_payload_archive` was a forward-compat insurance — Unity gets that for free via versioned `.asset` files in git).

Migrating the data would duplicate work (we'd re-create catalogs that already exist as 469 SO assets) without adding any capability the SO + JSON layer doesn't already provide.

---

## Per-table mapping

| Python table | Rows | Purpose in Python | Unity equivalent | Action |
|---|---|---|---|---|
| `alembic_version` | 1 | Track active Alembic migration | `MigrationChain<T>` schema-version field per JSON | None — already covered |
| `entities` | 22 | Monster + player definitions (55 columns) | `MonsterDefinition.asset` (×11) + `PlayerDefinition.asset` | Already migrated |
| `entities_assets_no_set` | 1185 | Animation paths per (entity, action, direction) | `EntityAssetConfig` referenced by `MonsterDefinition.assetConfig` | Already migrated |
| `entities_assets_set` | 10 | Animation pose-set variants | Part of same `EntityAssetConfig` | Already migrated |
| `entities_payload_archive` | 22 | JSON snapshot of every entity for forward compat | `.asset` files in git ARE the archive | Free in Unity |
| `items` | 53 | Item catalog (29 columns) | `ItemDefinition.asset` | Already migrated |
| `item_prices` | 53 | Buy/sell prices per item | Fields on `ItemDefinition` (`buyPrice`, `sellPrice`) | Already merged into the SO |
| `spells` | 25 | Spell catalog | `SpellDefinition.asset` (×25) + `SpellCatalog.asset` | Already migrated |
| `spawner_templates` | 8 | Reusable spawner blueprints | `SpawnerDefinition.asset` (catalog) | Already migrated |
| `spawner_waves` | 3 | Wave timings (JSON column inside SQLite!) | Fields on `SpawnerDefinition` | Already migrated |
| `spawners_instances` | 12 | Placed spawners in zones | `StreamingAssets/Spawners/spawner_instances.json` | Already in JSON |
| `building_instances` | 143 | Placed buildings in zones | `StreamingAssets/Buildings/buildings_instances.json` | Already in JSON |
| `building_collisions` | 95 | Per-instance collision polygons (WKT) | `StreamingAssets/Buildings/buildings_collisions_*.json` | Already in JSON |
| `import_log` | 19 | Audit: which JSON was imported when | git history of `.asset` and `.json` files in this repo | Free in Unity |

**Score:** 14 / 14 tables already covered by Unity assets or JSON.

---

## What this means for Unity

The Python project used SQLite as a *queryable cache* of source-of-truth JSONs. Unity's catalog assets (469 `.asset` files) and the 45 `StreamingAssets/*.json` files together fill the same role — with two advantages:

1. **No native plugin required** (SQLite needs `sqlite3.dll/.so/.dylib`; SOs use Unity's built-in serialiser).
2. **Diff-friendly in git** — every commit shows which catalog entry changed, in YAML for SOs and pretty JSON for instances.

The Python tooling (`scripts/migrate_json_to_sqlite/import_*.py`, Alembic migrations) is a development-time pipeline that has no purpose in Unity, where SOs are edited directly in the inspector and instance JSONs are written by the F6/F10/F11 in-game editors with `MigrationChain<T>` schema versioning.

---

## What SQLite SHOULD be used for in Unity (new content)

A *new* SQLite layer makes sense for data that didn't exist in Python at all — meta-progression, statistics, telemetry:

| Future Unity table | Why SQLite fits |
|---|---|
| `runs` (run history: timestamp, duration, depth, killed_by) | Aggregate queries: "average run length", "boss reach rate" |
| `kill_stats` (entity_key → total_kills, last_kill_at) | "Which monster has the player killed most?" — group-by query |
| `achievements` (id, unlocked_at) | Set membership + timestamp; fast "is X unlocked?" |
| `profile` (single-row settings: total_runs, total_playtime, currency_meta) | Trivial single-row but persists across runs |
| `boss_attempts` (boss_id, run_id, phase_died, duration) | Telemetry: "what % of players reach phase 3?" |

Implementation lives in `Valkur.Infrastructure.Persistence.Profile` (created in Phase 2 of this plan), uses `Mono.Data.Sqlite` (bundled with Unity, works on every non-WebGL platform), and stays orthogonal to the existing SO + JSON data layer.
