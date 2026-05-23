---
name: unity-performance
description: "Performance diagnosis and optimization for the Valkur Unity project. Use when chasing low FPS, frame-time spikes, GC pauses, expensive Updates, GPU bottlenecks, render-thread stalls, or memory pressure. Covers Profiler/Recorder API workflow, hot-path patterns, URP/UGUI specifics, GC reduction, and Valkur's catalog of previously-applied optimizations."
argument-hint: "Describe the perf symptom (low FPS, stutter, freeze, etc.) and the scene/context where it appears"
---

# Unity Performance for Valkur

## When to Use
- FPS under target (60 baseline, 120+ if hardware allows) during gameplay, editors, or transitions.
- Visible **frame-time spikes** even at acceptable average FPS — typically GC pauses or one-off scene work.
- High **CPU main thread** time reported by `cpu_main_thread_frame_time_ms` (>10ms baseline).
- High **GPU frame time** while CPU is idle — render/post-process bottleneck.
- High **`Gfx.WaitForGfxCommandsFromMainThread`** — VSync or render-queue stall.
- Memory pressure: heap >1 GB, many `Gen0` collections, or "Garbage Collector" spikes in Profiler.
- Editor specific: dropped FPS only when a runtime editor (F6/F10/F11...) is open.

> **NOT for:** asset import optimization (use `asset-pipeline` skill), test perf (use `unity-testing` skill), or general C# correctness (use `unity-development`).

---

## 1. Diagnosis Always Comes First

Optimizing without measuring wastes effort. The mandatory first step is identifying **which axis** is the bottleneck, then **which subsystem** inside that axis.

### 1.1 The Three Axes

```
┌──────────────────────────┬──────────────────────────┐
│ CPU Main Thread          │ Scripts, UGUI rebuild,   │
│                          │ Physics2D, Animator,     │
│                          │ Tilemap apply, etc.      │
├──────────────────────────┼──────────────────────────┤
│ CPU Render Thread        │ Command-list build,      │
│                          │ resource bind, batching  │
├──────────────────────────┼──────────────────────────┤
│ GPU                      │ Draw calls, fragment     │
│                          │ shading, post-process,   │
│                          │ HDR composite            │
└──────────────────────────┴──────────────────────────┘
```

`cpu_main_thread_frame_time_ms` + `cpu_render_thread_frame_time_ms` + `gpu_frame_time_ms` are the headline numbers. The largest one is the bottleneck. Optimizing anywhere else gives ~0 improvement.

### 1.2 Capture frame timing via MCP

```csharp
// Via mcp_unity_manage_profiler
get_frame_timing  // Returns cpu_main_thread_*, cpu_render_thread_*, gpu_frame_time_ms
```

### 1.3 Per-marker timing via Recorder API

The most useful workflow when MCP profiler counters return "invalid":

```csharp
// Inside execute_code
var rec = UnityEngine.Profiling.Recorder.Get("BehaviourUpdate");
rec.enabled = true;
// Wait one frame (next execute_code call will see real data)
return rec.elapsedNanoseconds / 1e6;  // ms
```

Useful built-in markers:
- `BehaviourUpdate` — total `MonoBehaviour.Update()` cost
- `Camera.Render` — render submission
- `UGUI.Rendering.RenderOverlays` — per-canvas overlay passes
- `Canvas.SendWillRenderCanvases` — UGUI rebuild dispatch
- `LayoutRebuilder.Rebuild` — Layout Group rebuilds
- `TextMeshPro.UpdateMesh` — TMP text geometry rebuilds
- `Physics2D.Simulate`, `Physics2D.SyncTransforms`
- `Animator.Update`, `ParticleSystem.Update`
- `Gfx.WaitForGfxCommandsFromMainThread` — render/VSync stall

### 1.4 Allocation hunting

```csharp
return new {
    gcAllocCount = System.GC.CollectionCount(0),
    totalAllocatedMB = System.GC.GetTotalMemory(false) / (1024f * 1024f),
    incrementalGC = UnityEngine.Scripting.GarbageCollector.isIncremental
};
```

Rough thresholds:
- < 50 allocs/frame: healthy.
- 50-300 allocs/frame: tolerable but expect periodic Gen0 spikes.
- > 300 allocs/frame: **spike machine** — kills smooth FPS.

### 1.5 Beware of measurement artifacts

`execute_code` itself takes ~10-30ms to compile + invoke. It inflates the **frame in which it runs** by exactly that amount. When sampling FPS:
- Use `Time.smoothDeltaTime` (averaged) over `Time.unscaledDeltaTime` (instantaneous).
- Or sample the same `Recorder` across multiple `execute_code` calls — only the first one is inflated.
- Tell the user to observe their in-game HUD FPS without your interference for honest readings.

---

## 2. Optimization Hierarchy

Apply in this order. Earlier items have higher impact:

### 2.1 Render-side (URP asset / camera)

Lives in `unity/Valkur/Assets/Settings/UniversalRP.asset` + camera per-target settings.

| Setting | Default | Optimized | Why |
|---|---|---|---|
| `m_HDRColorBufferPrecision` | `0` (32-bit) | `1` (16-bit) | Halves color-buffer bandwidth + memory |
| `m_MainLightShadowsSupported` | `1` | `0` (2D game) | Skips shadow pass entirely |
| `m_MainLightShadowmapResolution` | `2048` | `512` | VRAM win even when shadows enabled |
| `m_AdditionalLightsShadowmapResolution` | `2048` | `512` | Same |
| `m_AdditionalLightsCookieResolution` | `2048` | `512` | Same |
| Per-camera `renderPostProcessing` | `true` | `false` when no Volume is active | Skips full-screen UberPostProcess (~18ms on mid GPU) |

**Key pattern**: `UberPostProcess` runs EVERY frame `renderPostProcessing = true`, even when all Volumes have `weight = 0`. Toggle the camera flag on-demand from the system that owns the Volume (see `GrayscaleVolumeController.cs` for the canonical pattern).

### 2.2 UGUI canvases

Every active `Canvas` in `ScreenSpaceOverlay` mode becomes one `Canvas.RenderOverlays` pass each frame, even with zero `Graphic` descendants.

**Pattern in Valkur**: `EmptyCanvasAutoDisable` + `EmptyCanvasGuardBootstrap`.
- Attached at scene start to every overlay Canvas.
- Polls 4×/sec for active `Graphic` descendants.
- Disables both the `Canvas` and its `GraphicRaycaster` when empty.
- Re-enables instantly on first poll after content appears.

If you find a system that creates a canvas it forgets to disable on close, attach `EmptyCanvasAutoDisable` rather than wiring per-system disable.

### 2.3 Per-frame allocations (GC pressure)

The cheapest spike: pre-allocated buffers in fields, **cleared and reused** each call instead of `new`-ing.

Canonical examples in this codebase:
- `StatusEffectManager._removalBuffer` (replaces `var toRemove = new List<Type>()`)
- `SpellCaster._spellBookTickBuffer` (replaces `var keysToUpdate = new List<string>()`)

Forbidden patterns inside `Update`/`LateUpdate`/`FixedUpdate`:
- `new List<T>()`, `new Dictionary<,>()`, `new HashSet<T>()`, `new StringBuilder()`
- String interpolation: `$"text {value}"` allocates a string every frame.
  Even if assigning to `TextMeshProUGUI.text`, cache the last value and only rebuild on change (see `VendorShopUI` pattern).
- `Object.FindObjectOfType<T>()` — cache once in `Awake` and re-resolve only on null.
- `GetComponent<T>()` — cache the reference; only re-look-up when the owning GameObject identity changes (see `SaveService` pattern).
- `Resources.Load` — never in hot path. Load at boot or via catalog ScriptableObject.
- LINQ chains (`.Where()`, `.ToList()`, `.Select()`) — each operator allocates a closure + iterator.

### 2.4 Tick-rate throttling

Not everything needs 60 Hz. Bring AI / proximity / UI counters down to 5-10 Hz where the user can't tell the difference.

Pattern:

```csharp
private const float PollInterval = 0.1f;  // 10 Hz
private float _nextPollUnscaled;

private void Update()
{
    if (Time.unscaledTime < _nextPollUnscaled) return;
    _nextPollUnscaled = Time.unscaledTime + PollInterval;
    // ... expensive work
}
```

Applied in:
- `SpawnerInstance.UpdateIdle` (proximity check 10 Hz)
- `EmptyCanvasAutoDisable.Update` (canvas-empty poll 4 Hz)
- `WorldHealthBar.Update` (early-out when hidden)

### 2.5 Math micro-wins

- `sqrMagnitude` over `Vector*.Distance` whenever you compare to a threshold (squared once, no sqrt each frame).
- Precompute `radius * radius` outside the loop.
- `Mathf.Abs(currentY - lastY) < threshold` to skip transform writes when nothing changed (`YSortEntity`).
- `Mathf.Approximately` over `==` for floats.

### 2.6 Cull-aware MonoBehaviours

If your `Update`/`LateUpdate` only matters while the entity is on-screen, gate on `EntityCulling.ShouldUpdate`. Pattern in `FSMMonsterBrain.Update`:

```csharp
if (_culling != null && !_culling.ShouldUpdate) return;
```

Off-screen AI ticks at a low interval (every 8th frame by default) instead of per-frame.

### 2.7 Camera.main caching

`Camera.main` walks the tag index on every access. Cache `transform` once and re-resolve only on null. Pattern in `ChatBubble`.

```csharp
private Transform _camTransform;

private void LateUpdate()
{
    if (_camTransform == null)
    {
        var c = Camera.main;
        if (c != null) _camTransform = c.transform;
    }
    if (_camTransform != null)
        transform.forward = _camTransform.forward;
}
```

---

## 3. Valkur-specific Catalog (already applied)

When you spot a similar pattern elsewhere, apply the same fix. **Each row links the file that holds the canonical implementation** so you can copy the pattern verbatim.

| Subsystem | File | Pattern |
|---|---|---|
| URP HDR / shadows | `Assets/Settings/UniversalRP.asset` | HDR 16-bit, shadows off, shadowmap 512 |
| Post-process on-demand | `Core/Rendering/GrayscaleVolumeController.cs` | `renderPostProcessing` toggled from FadeIn/FadeOut |
| Canvas auto-disable | `Core/Rendering/EmptyCanvasAutoDisable.cs` + Bootstrap | Polls children for active Graphics, disables Canvas + Raycaster when empty |
| Reused alloc buffer | `Gameplay/Combat/StatusEffects/StatusEffectManager.cs` | `_removalBuffer` hoisted to field |
| Reused alloc buffer | `Gameplay/Spells/Core/SpellCaster.cs` | `_spellBookTickBuffer` hoisted to field + early-out when empty |
| Conditional text rebuild | `Gameplay/Vendors/VendorShopUI.cs` | Only assign `.text` when underlying value changes |
| Early-out in Update | `Gameplay/Combat/WorldUI/WorldHealthBar.cs` | Skip body when bar hidden / lerp settled |
| Tick throttle (10 Hz) | `Gameplay/Spawners/SpawnerInstance.cs` | `_proximityNextPoll` timer + `sqrMagnitude` |
| Threshold skip | `Gameplay/World/Navigation/YSortEntity.cs` | Skip LateUpdate body when Y change < threshold |
| Camera.main cache | `Gameplay/Chat/ChatBubble.cs` | Lazy `_camTransform` |
| GetComponent cache | `Gameplay/Save/SaveService.cs` | `_cachedPlayerHealth` re-resolved only on GO change |
| Cull-aware Update | `Gameplay/Enemies/FSM/FSMMonsterBrain.cs` | `if (!_culling.ShouldUpdate) return;` |

---

## 4. Hardware Reality Check

Before chasing "120 FPS", confirm what the **monitor** physically supports:

```csharp
// Via execute_code
return new {
    currentRefresh = UnityEngine.Screen.currentResolution.refreshRate,
    monitorMaxRefresh = ...  // from Screen.resolutions enumeration
};
```

Or read via Windows:

```powershell
Get-WmiObject Win32_VideoController | Select MaxRefreshRate
```

If the monitor is 60 Hz, **60 FPS is the visible ceiling**. Anything beyond is rendered but never displayed. Optimization beyond that point is justified only by the project's "runs on any computer" goal — there's still value (lower-end users see fewer dips), but the developer's own monitor can't validate it.

---

## 5. Workflow: Diagnose → Fix → Measure → Confirm

1. **Capture baseline** (`get_frame_timing` + Recorder counters + `GC.CollectionCount`).
2. **Attribute to axis**: CPU main, CPU render, GPU, or wait.
3. **Drill into top suspect** with targeted markers or per-component A/B disable.
4. **Apply fix** following the catalog above.
5. **Refresh + run tests** (`mcp_unity_refresh_unity` + `mcp_unity_run_tests`).
6. **Re-measure** the same metrics, compare against baseline.
7. **Report delta** (e.g., "GPU 21.5ms → 2.83ms, -87%").

Never report optimizations without delta numbers. If you can't measure (e.g., user not in gameplay), say so and provide the optimization rationale anyway.

---

## 6. Cardinal Rules

- **Profile first** — never optimize without a Recorder/timing snapshot.
- **One optimization at a time** for the big ones, so deltas are attributable.
- **Tests must stay green** — `mcp_unity_run_tests(mode="EditMode")` after every batch.
- **Console must stay clean** — `mcp_unity_read_console` with zero errors / warnings.
- **Never** add features under the banner of "performance"; if scope creeps, hand off to `unity-architect`.
- **Hardware caveats matter** — a 60Hz monitor user will not see 120 FPS no matter the GPU.

---

## 7. When to delegate

- Need new gameplay system or architectural changes → `unity-architect`.
- Need asset re-import / atlas / texture compression review → `asset-pipeline`.
- Need to verify console is clean post-changes → `unity-mcp-guardian`.
- Need to write/fix tests → `unity-tester`.
- Need to split an oversized file or extract reusable helpers → `refactor-modularizer`.
