# Every particle in the world disappeared, and the suite stayed green

**When:** 2026-09-10, ~12:17 UTC
**What was lost:** `StreamingAssets/Particles/particles_instances.json` —
**337,852 bytes and 188 placed emitters, down to 162 bytes and ONE record**
**Recovered from:** `git HEAD` (the file is tracked; nothing was committed in the damaged state)
**Reported by:** a person playing the game — *"por alguna razón todas las partículas
desaparecieron del mapa, solo se ven las de lighting en la noche"*

## What the file looked like afterwards

```json
{"version":4,"instances":[{"id":"9c2eb9d0d174486e8676d246079731f0","preset_id":"aura_smoke",
"zone":"zone_0_0","rel_x":64,"rel_y":1504,"scale_multiplier":1.0000}]}
```

The one surviving record is not authored content. `aura_smoke` is a preset **no catalogue
contains**, and the string appears in exactly one place in the whole repository:

```text
Tests/EditMode/Editors/Particles/ParticlesDeleteInstanceTests.cs:105:  _preset.id = "aura_smoke";
```

An EditMode fixture wrote its own scene over the authored world.

## Why nothing caught it

Four separate mechanisms were in place and none of them fired.

1. **`FileParticleInstanceStore` had an EditMode write guard** — and it is opt-out through a
   bare `public static bool AllowEditModeWritesToRealPath`. One fixture arms it in `[SetUp]`
   and clears it in `[TearDown]`; that TearDown deletes files, and deleting a StreamingAssets
   file Unity still holds mapped throws **Win32 1224**, which the fixture's own comment already
   documents. A throw before the clearing line leaves the opt-in armed **for every test that
   follows in the session**.
2. **The anti-wipe guard in `ParticlesRuntimeEditor` refuses a catastrophic drop** — by
   comparing the scene's count against the count *on disk*. Once the file had already been
   reduced by an earlier fixture, a later 1-record write was no longer a catastrophic drop.
   The guard measures the wrong baseline once the first write has landed.
3. **Ten of the eleven JSON world repositories had no write guard at all.** Only
   `JsonFileMapEditorZonesRepository` did. Buildings, entities, lights, spawners, tile
   overrides, item drops, world damage, chunk deltas, the zone database and particles all
   wrote the shipped path from a test without complaint.
4. **Nothing asserted anything about the shipped data.** 8,306 tests passed. The only trace in
   the console was a warning nobody was reading:
   `[ParticleInstancesLoader] Preset not found in catalog: 'aura_smoke'`.

There is also a fifth, structural reason: **eight EditMode fixtures build a
`ParticlesRuntimeEditor` and inject no store**, so all eight fall through to the production
file path. Patching the eight is not a fix — the ninth arrives next week.

## The shape, which this project has now paid for four times

| Incident | File | Cause |
|---|---|---|
| `RUN_TWIN_SAVE.md` | `Saves/<runId>/` | EditMode test pollution |
| 2026-05-23 (38 zones) | `map_editor_zones.json` | test seed clobbered user data |
| 2026-09-10 (this) | `particles_instances.json` | fixture wrote the production path |
| same week | `map_editor_zones.json.bak` | an orphaned test runner wrote fixture zones |

Every one of them: a test wrote authored data, nothing failed, and a human found it later.

## The fix

**`Valkur.Core.WorldDataWriteGuard`** asks a different question from the guards it replaces.
The old shape was `!Application.isPlaying && !AllowEditModeWritesToRealPath`, which is wrong in
both directions — a **PlayMode test** has `isPlaying == true` and was never guarded, while an
**editor tool a human clicks** is not playing and must be allowed to write. The new rule is:

> refuse when a **test run** is in progress, the target is the **real shipped path**, and no
> scope has been opened for it.

- **`WorldDataWriteGuardTestHook`** registers with `TestRunnerApi` from `[InitializeOnLoad]` and
  tells the guard when a run starts and ends — and **disarms every opt-in before EVERY test**,
  which is what makes a fixture unable to leave a hole open for the rest of the session.
- **`AllowRealPathWrites(reason)` returns an `IDisposable`**, so an opt-in cannot leak inside a
  fixture either, throw or no throw. The two legacy bools now delegate to it, so one disarm
  clears both.
- **The guard sits at `WorldStreamingFileRepositoryBase.WriteFileAtomic`** — the one method all
  eleven repositories write through. A repository constructed with a `streamingRootOverride` is
  already isolated and is deliberately **not** refused: that is the correct way to write these
  tests and refusing it would be the fastest route to somebody deleting the guard.
- **A refusal is a `LogError`**, so the run that would have destroyed the data goes red.
- **`ParticlesRuntimeEditor` defaults to `InMemoryParticleInstanceStore` during a test run**
  unless a scope is open. Forgetting to inject is now harmless, which is the only version of
  this that survives the next fixture nobody reviews for it.

**`ShippedWorldDataIntegrityTests`** is the second line, because a guard can be bypassed and a
floor cannot. It asserts the emitter count against a floor well below the authored 188, the
building count, the zone database — and, sharper than any threshold, that **every placed
particle names a preset the game actually ships**. A record naming `aura_smoke` is not a
designer's work, and that check needs no number to tune.

**`WorldDataWriteGuardTests`** verifies the guard from inside a test run, which is the only
place its central claim is falsifiable. The important one is `TheHookIsRegistered`: if the
`[InitializeOnLoad]` registration ever stops happening, every protection built on it turns off
silently, and this is what says so.

## Verification

A full EditMode run before the fix destroyed the file. After it, `md5sum` of
`particles_instances.json` is **identical before and after a full 8,326-test run** —
`944006b4378f81989c1395aea80e945d`, 188 records, byte for byte.

## Still open

- The anti-wipe guard in `ParticlesRuntimeEditor` compares against the on-disk count, so it is
  blind once a first bad write has landed. It is a second line rather than a first, and the
  write guard is what makes that acceptable.
- `ParticlesPersistenceTests` still writes the production file deliberately (with a disk
  backup and a retrying restore). It now disarms on the FIRST line of its TearDown, before
  anything that can throw — the order was the bug.
