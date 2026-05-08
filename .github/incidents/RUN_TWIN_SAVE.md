# Incident — Run "twin-save" (duplicate save folder with identical content, distinct runId)

**Status:** mitigated — root cause identified (EditMode test pollution), production guard added 2026-05-08.
**First observed:** 2026-05-08 (user report; both autosaves dated `2026-05-08T10:07:46`)
**Severity:** medium — pollutes the Load Game panel; data is not lost; user can manually delete the orphan folders.
**NOT to be confused with:** the long-known "phantom Lv.0/Lobby" pattern documented in `SaveFileManager.Pruning.cs`. That pattern is fully mitigated. This was a **separate** pattern caused by EditMode tests writing into the user's real `Application.persistentDataPath`.

## Root cause (confirmed 2026-05-08, second occurrence)

The EditMode test fixture `SaveServiceDirtyAndImmediateTests` (and any sibling
fixture that uses the same pattern) does:

```csharp
[SetUp] {
    _saveServiceGo = new GameObject("TestSaveService_…");
    _saveService   = _saveServiceGo.AddComponent<SaveService>();
    ForceSingletonInit(_saveService);   // bypasses Awake's DontDestroyOnLoad
    _saveService.BeginNewRun();         // mints a real GUID
}

[Test] {
    _saveService.SetRunOrdinal(1);      // disarms the ordinal=0 guard
    // …events fired by the test (or by a leftover production MonoBehaviour
    //   like ZoneManager.Update) reach HandleZoneChanged → SaveImmediately
    //   → WriteAutosaveToDisk → real disk write.
}
```

`SaveFileManager.GetAutosavePath(runId)` resolves to
`Application.persistentDataPath/Saves/<runId>/autosave.json` — i.e. the user's
**real** Unity profile directory. The test never redirects this path, so every
write lands in production storage. Subsequently, when the user starts Play
Mode and clicks "Continue", the Load Game panel scans `Saves/` and surfaces
those test-leftover folders as if they were real runs. The user perceives
this as "a run was duplicated without me starting one."

A second amplification: the EditMode test SaveService GameObject sometimes
survives test teardown into the next Play Mode session (the exact mechanism
is unclear — `DestroyImmediate` should call `OnDestroy` and unwire events).
When that happens, **two** SaveService instances coexist in Play Mode, both
subscribed to `GameEvents.OnZoneChanged`. A single zone transition therefore
produces two near-simultaneous `WriteAutosaveToDisk` calls — one per
instance, each to its own `_currentRunId` — yielding the 7-ms-apart twin
write pattern visible in the original 2026-05-08 evidence.

## Production fix — `RefuseWriteOutsidePlayMode`

Added 2026-05-08 in `SaveService.cs`. A static guard placed at the top of
every method that derives a path from `Application.persistentDataPath`:

```csharp
private static bool RefuseWriteOutsidePlayMode(string callerName)
{
    if (!Application.isEditor || Application.isPlaying) return false;
    Debug.LogWarning($"[SaveService] {callerName} refused — Play Mode is not active. " +
                     "EditMode test pollution prevention; production code is unaffected.");
    return true;
}
```

Wired into `WriteAutosaveToDisk`, `Save(slotName)`, and `SavePositionCheckpoint`.
Tests that legitimately need disk I/O call `SaveFileManager.WriteSaveFile(Async)`
directly with explicit temp paths (the AsyncIO test fixture already does this);
that path is unaffected.

Regression coverage: `SaveImmediately_RefusesDiskWrite_OutsidePlayMode` and
`Save_RefusesDiskWrite_OutsidePlayMode` in
`SaveServiceDirtyAndImmediateTests`. Both verify (a) the call returns false
and (b) no `Saves/<runId>/` folder is created in `persistentDataPath`.

## Symptom

The Load Game panel shows two distinct RUNS for what is clearly the same play
session:

```text
RUNS
  Lv.3   ← runId folder A: 0db15a20941e44f795c48a5a74b6e4b6
  Lv.3   ← runId folder B: e5bf2927929543e8a41a38b4d280ba47
```

Selecting either RUN displays the same Auto-Save with the same metadata
(class Mague, zone_150_50, Lv.3, XP 708, HP 80/100, Saved
2026-05-08T10:07:46).

## Evidence captured 2026-05-08

The user's `%USERPROFILE%/AppData/LocalLow/DefaultCompany/Valkur/`
contained:

```text
profile.json                   ← only ONE of the two folders is registered here
profile.json.bak
Saves/
  0db15a20941e44f795c48a5a74b6e4b6/
    autosave.json    (7869 bytes, 2026-05-08 10:07:46)
    autosave.sha256
    .backups/
  e5bf2927929543e8a41a38b4d280ba47/
    autosave.json    (7869 bytes, 2026-05-08 10:07:46)
    autosave.sha256
    .backups/
  legacy/                       ← empty
  .recovery/                    ← empty
```

`profile.json` runs array:

```json
"runs": [
  { "runId": "e5bf2927929543e8a41a38b4d280ba47", "runOrdinal": 1, "startedAtIso": "2026-05-08T09:00:47.8424146Z", ... },
  { "runId": "d1a77b9ec7004f62bf5acc5dc1f84ae8", "runOrdinal": 2, "startedAtIso": "2026-05-07T13:21:07.1684040Z", ... }
],
"profile": [
  { "key": "run_counter",   "value": "2"  },
  { "key": "deaths_total",  "value": "12" }
]
```

Note: `0db15a20…` (folder A) is **NOT** in `runs[]`. `d1a77b9ec…`
is in `runs[]` but has **no folder** on disk (legacy stragglers — fine,
unrelated).

The two `autosave.json` files are byte-identical except for the trailing
`metadata` block:

```jsonc
// Folder A (0db15a20…)
"metadata": [
    { "key": "run_id",      "value": "0db15a20941e44f795c48a5a74b6e4b6" },
    { "key": "run_ordinal", "value": "1" }
]
// Folder B (e5bf2927…)
"metadata": [
    { "key": "run_id",      "value": "e5bf2927929543e8a41a38b4d280ba47" },
    { "key": "run_ordinal", "value": "1" }
]
```

Identical timestamps, identical player position to 4 decimals, identical
NPC FSM states, identical inventory — but different `meta.run_id` and the
**same** `meta.run_ordinal=1`. SHA256 hashes confirm the files are not
byte-equal because of the differing meta block.

## Why existing guardrails miss this

| Guardrail | Logic | Why it didn't fire |
| --- | --- | --- |
| `_sessionDirty` (SaveService.cs:103) | First autosave waits until player does something meaningful. | Player WAS doing things — Lv.3, zone change, XP gain. Dirty flag legitimately true. |
| `_currentRunOrdinal == 0` rejection (SaveService.cs:341) | Refuses save while ordinal is 0 (BeginNewRun→StartTelemetryRun race window). | `_currentRunOrdinal` was **1** during both writes. Guardrail bypassed. |
| `PrunePhantomRuns` at MainMenu Start (MainMenuUI.cs:118) | Drops Lv.0/Lobby and ordinal=0 folders. | Both runs are Lv.3 in zone_150_50 with ordinal=1 — fully legitimate-looking. |

## Mechanical theory

For the symptom to appear, `_currentRunId` must have CHANGED between the
two writes while `_currentRunOrdinal` remained `1`. The only callsites
that mutate `_currentRunId`:

1. **`BeginNewRun()`** (SaveService.cs:235) — also resets ordinal to 0,
   which would have triggered the ordinal=0 guard. **NOT this path.**
2. **`Load(path)`** (SaveService.cs:415) — sets runId from the loaded
   meta, restores ordinal from meta. Could happen twice if Load is called
   twice with two different saves; but the player only loaded one.
3. **`EnsureRunId()`** (SaveService.cs:406) — **prime suspect**. When
   `_currentRunId == ""`, mints a new GUID. Crucially does NOT touch
   `_currentRunOrdinal`. If `_currentRunId` is somehow nulled between
   loads (e.g. during a scene transition where a fresh SaveService
   instance overwrites a stale Persisted one, or a static reset path
   misfires), the next `WriteAutosaveToDisk` call mints a new runId but
   keeps `_currentRunOrdinal=1` from a prior session — producing
   exactly this symptom.

Hypothesis A (most likely): the session bootstrapped via `Load(...)`
(setting `_currentRunId=e5bf2927`, ordinal=1). Some later flow nulled
`_currentRunId` mid-session (suspect: a SaveService teardown/recreate
cycle around scene transition with `Persist=true`). The next autosave
called `EnsureRunId()`, minted `0db15a20`, and wrote the same in-memory
state to a NEW folder. Result: twin-save.

Hypothesis B: `Load()` was actually called twice — once on bootstrap
(setting `e5bf2927`), and again later from a different path that loaded
a save WITHOUT a `meta.run_id` block, falling into the "Loaded legacy
save without run_id" branch (SaveService.cs:449) which mints a fresh
GUID directly. Less likely because the user reported only loading once,
but still possible if a quick-load shortcut fired.

## Diagnostic logging plan (this commit)

Adds `[RunTwinSave-Diag]` log statements at every callsite that mutates
`_currentRunId` or `_currentRunOrdinal`, plus a stacktrace in
`EnsureRunId()` to capture the caller chain when minting a new GUID
mid-session. All logs gated on a `DIAG_RUN_TWIN_SAVE` const so they're
trivial to flip off once the cause is pinned down.

## How to reproduce (request to the user)

1. Boot the game from a clean Editor session (or Build).
2. From the main menu, "Continue" / "Load" your saved Mague run.
3. Play for ~30 seconds — wander, gain XP, change zone if possible.
4. Quit to the main menu.
5. Open the Load Game panel.
6. Check whether two RUNS appear. If yes, paste the
   `[RunTwinSave-Diag]` lines from the Unity console here.

## Recovery for affected users

Until the root cause is fixed, manually delete the orphan folder:

```text
%USERPROFILE%/AppData/LocalLow/DefaultCompany/Valkur/Saves/<orphan-runId>/
```

The "orphan" is whichever folder name is **NOT** present in
`profile.json#runs[].runId`. Take a backup before deleting.

## Resolution checklist

- [x] Logs reproduce the call chain (Editor.log lines ~1779382–2727880,
      2026-05-08): `BeginNewRun` from
      `SaveServiceDirtyAndImmediateTests.SetUp` → `SetRunOrdinal(1)` from
      `WriteAutosaveToDisk_ProceedsAfterRunOrdinalIsSet` → real
      `WriteAutosaveToDisk` to `persistentDataPath/Saves/<guid>/`.
      Confirmed twin-write at 7-ms gap with two SaveService instances
      receiving the same `GameEvents.OnZoneChanged`.
- [x] Production guard `RefuseWriteOutsidePlayMode` added in
      `SaveService.cs` covering `WriteAutosaveToDisk`, `Save`, and
      `SavePositionCheckpoint`. Refuses every disk write when
      `Application.isEditor && !Application.isPlaying`.
- [x] Regression tests added:
      `SaveImmediately_RefusesDiskWrite_OutsidePlayMode` and
      `Save_RefusesDiskWrite_OutsidePlayMode` in
      `SaveServiceDirtyAndImmediateTests`.
- [x] `[RunTwinSave-Diag]` instrumentation flipped to dormant
      (`DIAG_RUN_TWIN_SAVE = false`); kept in source as a re-enable
      switch in case a non-test recurrence ever shows up.
- [ ] Optional follow-up: extend `IsPhantomRun` to detect "ordinal
      duplicates" (two folders sharing `meta.run_ordinal` with identical
      body bytes modulo meta) — defence-in-depth against any future
      mechanism that bypasses the guard.
