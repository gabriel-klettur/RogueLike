---
description: Capture a Unity profiling snapshot via MCP Recorder API — frame timing across the three axes (CPU main, CPU render, GPU), top Update markers, and GC allocation rate. Report a baseline to guide further optimization.
argument-hint: "(no arguments — user should be in Play Mode in the scene/context they want profiled)"
---

Produce a single-screen performance snapshot from the running Unity instance. Be concise; numbers first, prose second.

## Prerequisites

- Unity Editor running in **Play Mode**, ideally inside the gameplay scene with actual entities/AI/UI active (not in MainMenu — that gives misleading 200+ FPS readings).
- If the user is in MainMenu or paused, say so and stop. Don't fabricate numbers.

## Gather (via MCP)

### 1. Confirm we're in Play Mode and active scene

```text
mcp_unity_execute_code(action="execute", code="
return new {
    isPlaying = UnityEngine.Application.isPlaying,
    scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
    activeMBs = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>().Length,
    smoothFPS = 1f / Mathf.Max(0.0001f, UnityEngine.Time.smoothDeltaTime),
    smoothDt_ms = UnityEngine.Time.smoothDeltaTime * 1000f,
    targetFrameRate = UnityEngine.Application.targetFrameRate,
    vSyncCount = UnityEngine.QualitySettings.vSyncCount
};
")
```

If `isPlaying = false` OR `activeMBs < 50` (i.e. MainMenu) → stop and report that to the user.

### 2. Three-axis frame timing

```text
mcp_unity_manage_profiler(action="get_frame_timing")
```

Pull `cpu_frame_time_ms`, `cpu_main_thread_frame_time_ms`, `cpu_render_thread_frame_time_ms`, `gpu_frame_time_ms`.

### 3. Top-level markers via Recorder API

Issue a single `execute_code` that enables and samples a known marker set. The data is from the **previous** frame; if all are zero, call once more (one frame later) for real numbers.

```text
mcp_unity_execute_code(action="execute", code="
string[] names = {
    \"BehaviourUpdate\", \"FixedBehaviourUpdate\",
    \"Camera.Render\",
    \"UGUI.Rendering.RenderOverlays\", \"Canvas.SendWillRenderCanvases\",
    \"LayoutRebuilder.Rebuild\", \"TextMeshPro.UpdateMesh\",
    \"Physics2D.Simulate\",
    \"Animator.Update\", \"ParticleSystem.Update\",
    \"Gfx.WaitForGfxCommandsFromMainThread\"
};
var sb = new System.Text.StringBuilder();
foreach (var n in names) {
    var r = UnityEngine.Profiling.Recorder.Get(n);
    if (!r.isValid) { sb.AppendLine(n + \": invalid\"); continue; }
    r.enabled = true;
    sb.AppendLine(n + \": \" + (r.elapsedNanoseconds / 1e6).ToString(\"F3\") + \"ms (\" + r.sampleBlockCount + \"x)\");
}
return sb.ToString();
")
```

### 4. GC / memory baseline

```text
mcp_unity_execute_code(action="execute", code="
return new {
    totalAllocatedMB = System.GC.GetTotalMemory(false) / (1024f * 1024f),
    gen0Collects = System.GC.CollectionCount(0),
    incrementalGC = UnityEngine.Scripting.GarbageCollector.isIncremental,
    gcMode = UnityEngine.Scripting.GarbageCollector.GCMode.ToString()
};
")
```

### 5. Render scale + canvas state

```text
mcp_unity_execute_code(action="execute", code="
int activeCanvases = 0;
foreach (var c in UnityEngine.Object.FindObjectsOfType<UnityEngine.Canvas>(false))
    if (c.enabled && c.gameObject.activeInHierarchy) activeCanvases++;
return new {
    activeCanvases,
    drawCalls = UnityEngine.Profiling.Recorder.Get(\"Draw Calls Count\").elapsedNanoseconds,
    sceneObjects = UnityEngine.Object.FindObjectsOfType<UnityEngine.GameObject>(false).Length
};
")
```

## Report

Format strictly as below — table first, prose only if it adds context.

```markdown
## Profile snapshot — <scene> <timestamp>

### Frame timing
| Axis | Time | Headroom (vs 16.6 ms for 60 FPS) |
|---|---|---|
| CPU main thread | X.XX ms | +/- X.XX |
| CPU render thread | X.XX ms | — |
| GPU | X.XX ms | — |
| Frame total | X.XX ms | — |

**Smooth FPS**: XX.X • **vSync**: 0/1 • **targetFrameRate**: -1 / 60 / 120

### Top markers (last frame)
| Marker | ms | calls |
|---|---|---|
| BehaviourUpdate | ... | ... |
| UGUI.Rendering.RenderOverlays | ... | ... |
| Gfx.WaitForGfxCommandsFromMainThread | ... | ... |
| (others non-zero) | ... | ... |

### Memory / GC
- Heap: XXX MB
- Gen0 collections this session: NNN
- Incremental GC: enabled / disabled

### Scene
- Active canvases: N
- Active GameObjects: M

### Diagnosis
**Bottleneck**: <CPU main / CPU render / GPU / wait>

**Top suspect**: <largest marker that consumes budget>

**Next move**: <single-line recommendation>
```

## Cardinal rules

- Do **not** sleep `Thread.Sleep` inside `execute_code` — it blocks Unity and produces nonsense readings.
- Do **not** report a single instantaneous frame as the truth — Recorder + smoothDeltaTime give better signal.
- Do **not** invent numbers if any tool returns `invalid` / null — report "n/a" for those markers.
- If MainMenu / Paused / not Play Mode → say so and stop.
- After collecting, suggest invoking the `performance-optimizer` agent (or `/loop`) for next steps if the bottleneck is significant.
