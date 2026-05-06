---
captured: 2026-05-06 10:54:46 (local)
slot: default
unity-product: Valkur (DefaultCompany)
backup-tool: manual snapshot
---

# Default map — physical backup

Snapshot of every file that, *at this point in time*, makes up what the
in-game Map Editor calls the **default** map. Two storage roots feed into
that map today:

1. **Persistent data** — per-user runtime state. Lives in Unity's
   `Application.persistentDataPath`.
   On Windows: `C:\Users\<user>\AppData\LocalLow\DefaultCompany\Valkur\`.
   This folder is **outside the repo** and never committed; this is what
   you need to back up to recover a destroyed local default map.

2. **StreamingAssets** — content shipped with the game. Lives at
   `<repo>/unity/Valkur/Assets/StreamingAssets/`. Already part of git
   history, so the StreamingAssets half of this snapshot is mainly here
   for completeness / quick rollback without consulting git.

## Folder layout in this snapshot

```
persistent/
  map_editor_zones.json          live working copy of zones (active slot)
  map_editor_zones.json.bak      atomic-write sidecar
  Maps/
    default.zones.json           the slot file the Map Editor reads
    _active.txt                  name of the currently active slot
  MapOverrides/
    <zone>.overlay.json          painted-tile overrides for each zone
                                 (40 files in this snapshot)

streaming/
  Buildings/
    buildings_instances.json
    buildings_collisions_by_building_instance_id.json
    buildings_collisions_by_image.json
  Spawners/
    spawners_instances.json
  Lights/
    light_instances.json
  Particles/
    particles_instances.json
```

## Origin paths (so you know where each file came from)

### Persistent (Unity user-data path)

| Backup file | Origin on disk |
|-------------|----------------|
| `persistent/map_editor_zones.json`         | `C:\Users\gabri\AppData\LocalLow\DefaultCompany\Valkur\map_editor_zones.json` |
| `persistent/map_editor_zones.json.bak`     | same dir, `.bak` sibling |
| `persistent/Maps/default.zones.json`       | `…\Valkur\Maps\default.zones.json` |
| `persistent/Maps/_active.txt`              | `…\Valkur\Maps\_active.txt` |
| `persistent/MapOverrides/*.overlay.json`   | `…\Valkur\MapOverrides\*.overlay.json` |

### StreamingAssets (in-repo)

| Backup file | Origin on disk |
|-------------|----------------|
| `streaming/Buildings/*`   | `unity/Valkur/Assets/StreamingAssets/Buildings/*` |
| `streaming/Spawners/*`    | `unity/Valkur/Assets/StreamingAssets/Spawners/*` |
| `streaming/Lights/*`      | `unity/Valkur/Assets/StreamingAssets/Lights/*` |
| `streaming/Particles/*`   | `unity/Valkur/Assets/StreamingAssets/Particles/*` |

## Restore procedure

> Close Unity (or at least exit Play Mode) before restoring so its
> in-memory state doesn't overwrite the files you just put back.

```powershell
# 1. Persistent — copy back to AppData (the persistentDataPath).
$src  = "d:\Python\RogueLike\backups\map_default_20260506_105446\persistent"
$dst  = "$env:USERPROFILE\AppData\LocalLow\DefaultCompany\Valkur"

Copy-Item -Recurse -Force "$src\Maps"          "$dst\"
Copy-Item -Recurse -Force "$src\MapOverrides"  "$dst\"
Copy-Item            -Force "$src\map_editor_zones.json"      "$dst\"
Copy-Item            -Force "$src\map_editor_zones.json.bak"  "$dst\"

# 2. StreamingAssets — copy back to the repo folder.
$srcS = "d:\Python\RogueLike\backups\map_default_20260506_105446\streaming"
$dstS = "d:\Python\RogueLike\unity\Valkur\Assets\StreamingAssets"

Copy-Item -Recurse -Force "$srcS\Buildings\*"  "$dstS\Buildings\"
Copy-Item -Recurse -Force "$srcS\Spawners\*"   "$dstS\Spawners\"
Copy-Item -Recurse -Force "$srcS\Lights\*"     "$dstS\Lights\"
Copy-Item -Recurse -Force "$srcS\Particles\*"  "$dstS\Particles\"
```

After Unity reopens, reimport the StreamingAssets folder so .meta files
are regenerated for any newly copied JSON. The Map Editor will load the
restored default automatically (it always boots into the slot named in
`Maps/_active.txt`, which falls back to "default").

## What is NOT in this snapshot

- **NPCs spawned at runtime** (those come from spawners + monster catalog
  ScriptableObjects, not a per-instance JSON).
- **Item drops on the ground** (they live under `Items/item_drops.json`
  in StreamingAssets which we did NOT capture — the default map ships
  with no persisted drops).
- **Loaded zones from the database** (`ZonesDatabase/zones_database.json`
  — controlled by the zone designer, not the Map Editor).
- **Audio settings, save profile, key bindings** — those are user
  preference, not part of the map.

If/when the multi-map persistence routing in
`.github/MAP_EDITOR_MULTIMAP_ROADMAP.md` lands, the StreamingAssets half
of this backup will move to `StreamingAssets/Worlds/default/` and the
restore script above will need its `streaming/` paths adjusted accordingly.
