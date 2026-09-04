# Incident — Buildings Save Position Collapse

| Field | Value |
|---|---|
| First observed | 2026-05-08 |
| Status | **Mitigated** — root cause not yet identified |
| Severity | High (data loss) |
| Subsystem | F10 Buildings runtime editor — save flow |
| Affected file | `unity/Valkur/Assets/StreamingAssets/Buildings/buildings_instances.json` |

## TL;DR

After the user moved 2 buildings via the in-game F10 Buildings editor, the
auto-save persisted a corrupted JSON: every `BuildingObject`'s `rel_x` and
`rel_y` collapsed to one value per zone, leaving 16 unique positions across
337 entries (45–67 buildings stacked on the same cell). Visually most
buildings disappeared because they were all at zone-origin overlapping.
The on-disk `.bak` was overwritten with the same corrupted content.

Code review of the save loop, the move/place/fill paths, and the loader
showed no obvious bug. **Root cause is still unknown.** Three independent
guards now sit in front of the disk write so the disk file can no longer
take this hit. Next time it triggers, the guard log is the smoking gun for
finding the upstream cause.

## Symptom

- All buildings invisible in the Game View except a handful (most stacked,
  some at zone origins).
- `_Project/Data/Backups/buildings_instances.json.bak` had the same byte
  size as the corrupted file — the rolling backup was destroyed too.
- Console log fired normally: `[BuildingsEditor] Saved 337 buildings to ...`
  — no error or warning.

## Empirical diagnosis

Compared `git show HEAD:...buildings_instances.json` (good) vs the corrupted
file at the moment of incident:

| Metric | git HEAD (good) | corrupted |
|---|---|---|
| Total entries | 337 | 337 |
| Unique `(zone, rel_x, rel_y)` tuples | **337** | **16** |
| Max entries sharing one position | 1 | **67** |
| Unique `template_id` values | 117 | 117 |
| Top template multiplicity | `(8, 203)` | `(8, 203)` |
| Zone distribution per zone | identical | identical |

Interpretation: count, templates, and zone assignment all preserved.
**Only `rel_x` / `rel_y` collapsed**, and the collapse is one position per
zone (16 zones → 16 positions). World coordinates of the corrupted
positions (53.5, 154, 203.9, 253.9 for X) sit roughly at `zone_origin + 3.5`
— consistent with all in-zone buildings having been physically moved to
their zone's origin before save fired.

## Recovery procedure

> **Read all of step 2 before running anything.** Every restore path below OVERWRITES the
> corrupted file, and that file is the only artefact this incident has ever produced — the
> root cause is still unidentified, and the whole "Investigation that's still pending"
> checklist below needs the per-building positions that only the corrupted JSON holds.
> Restore second. Preserve first.

### 1. Stop Play Mode

So the in-memory corrupted state cannot re-save over anything you are about to inspect. Do
this before opening a shell — an autosave can fire while you are typing.

### 2. Preserve the evidence

Copy all three artefacts somewhere OUTSIDE the project, before touching any of them:

```bash
STAMP=$(date +%Y%m%d-%H%M%S)
mkdir -p ~/valkur-incident-$STAMP
cp unity/Valkur/Assets/StreamingAssets/Buildings/buildings_instances.json      ~/valkur-incident-$STAMP/ 2>/dev/null
cp unity/Valkur/Assets/StreamingAssets/Buildings/buildings_instances.json.prev ~/valkur-incident-$STAMP/ 2>/dev/null
cp unity/Valkur/Assets/_Project/Data/Backups/buildings_instances.json.bak      ~/valkur-incident-$STAMP/ 2>/dev/null
ls -la ~/valkur-incident-$STAMP
echo "$STAMP" > /tmp/valkur-incident-stamp   # step 4 reads this back
```

Outside the project because Unity imports everything under `Assets/`, and because a later
`git checkout` or `git clean` in the repo must not be able to reach them.

`$STAMP` goes to a file because step 4 restores from this folder, and a recovery is exactly
the situation where someone closes the terminal, restarts Unity and comes back — at which
point the variable is gone and so is the path. Resuming in a fresh shell, run
`STAMP=$(cat /tmp/valkur-incident-stamp)` before step 4. Note `~` and `/tmp` are your
shell's, not Unity's; under Git Bash on Windows they resolve inside the MSYS root.

Also capture the console: the guard message is ground truth, and `[BuildingsEditor] ABORTING
save` naming a specific reason tells you which of the three guards fired, which is
information that exists nowhere on disk.

### 3. Pick the source BEFORE restoring

Three candidates exist, freshest first — and in the original incident the `.bak` was ALREADY
corrupted (same byte size as the bad file), so freshest is not automatically best. Test each
one rather than assuming:

```bash
python3 - <<'EOF'
import json, io, collections, sys, os
for p in [
    "unity/Valkur/Assets/StreamingAssets/Buildings/buildings_instances.json",
    "unity/Valkur/Assets/StreamingAssets/Buildings/buildings_instances.json.prev",
    "unity/Valkur/Assets/_Project/Data/Backups/buildings_instances.json.bak",
]:
    if not os.path.exists(p):
        print(f"{p}: ABSENT"); continue
    d = json.load(io.open(p, encoding="utf-8"))
    pos = collections.Counter((e.get("zone"), e.get("rel_x"), e.get("rel_y")) for e in d)
    verdict = "HEALTHY" if len(pos) == len(d) else "COLLAPSED"
    print(f"{p}: {len(d)} entries, {len(pos)} unique positions, "
          f"max {max(pos.values())} sharing one -> {verdict}")
EOF
```

A healthy file has one unique `(zone, rel_x, rel_y)` per entry. The collapse signature is
unique positions equal to the ZONE COUNT — 16 in the original incident, against 337 entries
with 67 stacked on one cell. Verified against the shipped file (302 entries, 302 unique) and
against a synthetic collapse of it (302 entries, 16 unique, 111 stacked), so the check
distinguishes the two states rather than merely printing numbers.

Run the same check on the git copy before choosing it:

```bash
git show HEAD:unity/Valkur/Assets/StreamingAssets/Buildings/buildings_instances.json \
  > /tmp/head_candidate.json
```

### 4. Restore from the candidate that passed

Prefer a plain file copy from the artefacts you saved in step 2 — it restores exactly the
bytes you just verified. In a fresh shell, set `STAMP` back first (see step 2):

```bash
cp ~/valkur-incident-$STAMP/buildings_instances.json.prev \
   unity/Valkur/Assets/StreamingAssets/Buildings/buildings_instances.json
```

Only if git HEAD is the candidate that passed:

```bash
git checkout HEAD -- unity/Valkur/Assets/StreamingAssets/Buildings/buildings_instances.json
git checkout HEAD -- unity/Valkur/Assets/_Project/Data/Backups/buildings_instances.json.bak
```

**`git checkout HEAD --` restores the COMMITTED state, which discards every legitimate
uncommitted edit to that file along with the corruption.** If buildings were placed since the
last commit, those placements are lost too, and nothing reports it — the file simply comes
back older than you expected. Check `git status` on the file first, and prefer `.prev` or
`.bak` whenever either passed step 3. See the same trap, with the same shape, in
[BUILDING_TEMPLATES_MASS_DELETION.md](BUILDING_TEMPLATES_MASS_DELETION.md).

Beyond these three there is no automated recovery.

### 5. Verify before trusting it

Re-enter Play Mode: the loader should spawn the buildings at their original positions. Then
re-run the step-3 check against the restored file and confirm it reads HEALTHY — a restore
from a source nobody tested is how the `.bak` came to hold the corruption in the first place.

## Defenses currently in place

All defenses live in
`unity/Valkur/Assets/_Project/Scripts/Gameplay/Editors/Buildings/BuildingsRuntimeEditor.Persistence.cs`.

### 1. `ValidatePositionUniqueness` — in-memory guard

Pure function. Two thresholds, either triggers an abort:

- **Absolute**: any single `(zone, rel_x, rel_y)` tuple with ≥ 5 buildings
  (`MAX_BUILDINGS_PER_POSITION`).
- **Relative**: when `total ≥ 20`, `unique × 2 < total` aborts.

Pinned by 4 unit tests in
`unity/Valkur/Assets/Tests/EditMode/Editors/Buildings/BuildingsSaveFormatTests.cs`:

- `ValidatePositionUniqueness_AcceptsHealthyState`
- `ValidatePositionUniqueness_RejectsCorruptionSignature` (replays the real
  337/16/67 shape)
- `ValidatePositionUniqueness_RejectsAbsoluteThreshold`
- `ValidatePositionUniqueness_AllowsSmallFixturesWithoutFalsePositive`

### 2. `ValidateAgainstOnDisk` — disk-state regression guard

Reads the existing file (via `MiniJsonRuntime.Deserialize`) and counts both
`total` entries and `unique (zone, rel_x, rel_y)` tuples. Aborts when:

- on-disk has ≥ 20 unique positions, AND
- new total ≥ 50% of on-disk total (skips legitimate "replace map"), AND
- new unique × 2 < on-disk unique.

Designed to fire on the exact corruption signature (similar total,
collapsed unique) and stay silent on shrink-style operations.

### 3. `AtomicWriteJson` — atomic write + recovery sidecar

- Writes to `<path>.tmp` first.
- `File.Replace(tmp, real, prev)` — NTFS-atomic swap on Windows; the
  previous content lands in `<path>.prev` as a recovery breadcrumb.
- Falls back to delete+move on non-NTFS filesystems.

Effect: a process crash mid-write can never leave the canonical file
half-written, and the previous good content survives one extra save cycle
beyond the rolling `.bak`.

### 4. Failure UX

When any guard rejects:

```text
[BuildingsEditor] ABORTING save — <specific reason>. File NOT written.
Restart Play Mode to reload the last good on-disk state.
```

Status text under the F10 toolbar reads: `Save ABORTED — see console.`

## What's still NOT done — next-time playbook

If this incident triggers again, the guard message in the console is the
ground truth — start there. Then walk this checklist in order. Tick items
off as they get done so this doc tracks what has and hasn't been ruled out.

### Investigation that's still pending

- [ ] **Reproduce the corruption deterministically.** All attempts so far
  (manual move, drag, undo, repeated save) have not reproduced it. Without
  a repro the root cause stays speculative.
- [ ] **Add diagnostic instrumentation behind a debug flag.** Specifically,
  log the full `(InstanceId, ZoneName, transform.position)` for every
  building at the start of every save when the flag is on. Cheap, off by
  default, instant smoking gun if/when the bug fires.
- [ ] **Audit non-editor write paths.** The save loop reads
  `b.transform.position` per iteration — if something else is mutating it
  in bulk before save, the loop is innocent. Suspects (none confirmed):
  - `ApplyBuildingsVisibility` and the F10 hide/show toggle.
  - `RefreshSorting` chain inside `BuildingObject.Apply`.
  - Y-sort adjustments in `World/Navigation/YSortEntity`.
  - Any `SetParent(..., worldPositionStays: false)` that re-parents a
    `BuildingObject` (resets to local-zero relative to the new parent).
  - `MapEditor` (F11) regenerator paths that recreate zones from a seed —
    if F11 fires while F10 is open, it could re-spawn buildings at zone
    origin without F10 noticing.
- [ ] **Check `BuildingObject.Apply` for hidden side effects.** The Apply
  call is invoked from inspector edits, fill, and load. If it ever touches
  `transform.position` (it shouldn't), that's the bug.
- [ ] **Probe collider-driven repositioning.** F10's collider paint flow
  calls `ApplyGridOverrideToBuilding` → `ClearCollisionTiles` →
  `RestoreDefaultColliderState`. Verify none of those reach into the parent
  building's transform.
- [ ] **Race / re-entrancy.** `ExecutePersistedEdit` runs the doAction then
  calls `MarkInstanceDataDirty + PersistDirtyInstanceChanges(force:true)`.
  Check whether `_isPersistingInstanceChanges` actually serializes against
  collider painting (which also calls SaveInstancesToJson) — a partially
  applied collider write could leave buildings in a transient state that
  the save then captures.
- [ ] **Add a Play-mode integration test.** EditMode tests pin the
  formula; a Play-mode test should cover the full F10 flow: load 337
  buildings → move 2 → save → reload → assert positions intact.
- [ ] **Wire `BuildingsDataGuard.RefreshBackup` into `AtomicWriteJson`.**
  Today the rolling `.bak` is overwritten only on the next save. The
  atomic-write `.prev` sidecar gives one extra layer, but a second save
  while the user is still seeing corruption will still flatten everything.
  Consider keeping N rotating `.bak.0..N` instead of a single `.bak`.

### Defenses considered and deliberately NOT added

- **Refusing to save when `_activeBuilding` is null after a Move.** Too
  noisy — null is legitimate after Deactivate.
- **Mandatory backup-before-save inside `SaveInstancesToJson`.** The
  `BuildingsDataGuard.RefreshBackup` reflection call already does this for
  the default slot via `delayCall`. Adding a synchronous copy before the
  atomic write would make every save slower for marginal benefit on top of
  the `.prev` sidecar.
- **Hashing & roll-back on detection mismatch.** Heavy; the three guards
  cover the scenario without paying the hash cost on every save.

### Reproduction attempts that failed

- Cold load → move 1 building → save → diff: clean.
- Cold load → move 2 buildings sequentially → save → diff: clean.
- Cold load → move + undo + save → diff: clean.
- Cold load → drag fast across a zone boundary → save → diff: clean.
- Cold load → resize a building → save → diff: clean.

The corruption was triggered once by the user, between two screenshots
roughly an hour apart, with no recorded sequence of intermediate actions.

## Files & references

| File | Purpose |
|---|---|
| `unity/Valkur/Assets/_Project/Scripts/Gameplay/Editors/Buildings/BuildingsRuntimeEditor.Persistence.cs` | Save loop + the 3 guards + atomic write |
| `unity/Valkur/Assets/_Project/Scripts/Gameplay/Editors/Buildings/BuildingsRuntimeEditor.MapInteraction.cs` | Move drag commit (`FinalizeMoveDrag`) |
| `unity/Valkur/Assets/_Project/Scripts/Gameplay/World/Buildings/BuildingLoader.Spawning.cs` | Coordinate conversion (inverse of save formula) |
| `unity/Valkur/Assets/_Project/Scripts/Editor/Validation/BuildingsDataGuard.cs` | Editor-time deletion protection + `.bak` |
| `unity/Valkur/Assets/Tests/EditMode/Editors/Buildings/BuildingsSaveFormatTests.cs` | Save format + guard regression tests |
| `unity/Valkur/Assets/StreamingAssets/Buildings/buildings_instances.json` | The protected file |
| `unity/Valkur/Assets/_Project/Data/Backups/buildings_instances.json.bak` | One-deep rolling backup |
| `<streaming>/buildings_instances.json.prev` | Atomic-write sidecar (one save behind) |

## Decision log

- **2026-05-08** — Added `ValidatePositionUniqueness` (in-memory guard) +
  4 regression tests. Recovery via `git checkout HEAD`. Suite green.
- **2026-05-08** — Added `ValidateAgainstOnDisk` + `AtomicWriteJson` after
  the user requested deeper protection. Refined the disk guard to skip
  legitimate "replace map" scenarios (new total < 50% of on-disk total)
  after a test fixture false-positive. Suite green: 3351/3351.
- **2026-09-04** — Rewrote the recovery procedure. It had two ordering
  defects of the same class found that day in
  `BUILDING_TEMPLATES_MASS_DELETION.md`: **a warning placed after the
  command it guards is not a warning, because the reader is executing
  rather than reading ahead.** Concretely, step 2 ran
  `git checkout HEAD --` over the corrupted JSON *before* any instruction
  to preserve it — destroying the only artefact this incident has ever
  produced, which is exactly what the still-open investigation checklist
  needs — and the "if git HEAD is too old, fall back to `.prev`" caveat
  came after the block that made that choice unrecoverable. Now:
  preserve, then test all three candidates, then restore, then verify.
  The collapse-detection check is verified in both directions against the
  shipped data (302 entries / 302 unique positions = HEALTHY; a synthetic
  collapse of the same file = 302 entries / 16 unique / 111 stacked), and
  the `.prev` and `.bak` paths are read from `AtomicWriteJson` and
  `BuildingsDataGuard` rather than assumed. No production code touched.
