
# Unity Performance for Valkur

The full canonical knowledge base lives at:

**[.github/skills/unity-performance/SKILL.md](../../.github/skills/unity-performance/SKILL.md)**

Read it directly with the `read_files` tool when you need any of:

| Need | Section |
|---|---|
| Three-axis bottleneck model (CPU main / CPU render / GPU) | §1.1 |
| `get_frame_timing` + Recorder API workflow via MCP | §1.2-1.3 |
| Allocation hunting (`GC.CollectionCount`, alloc/frame thresholds) | §1.4 |
| `execute_code` measurement artifacts to watch for | §1.5 |
| Optimization hierarchy (highest impact first) | §2 |
| URP asset settings: HDR precision, shadows, post-process | §2.1 |
| UGUI canvas auto-disable pattern (`EmptyCanvasAutoDisable`) | §2.2 |
| Per-frame allocation patterns to forbid + fix recipes | §2.3 |
| Tick-rate throttling pattern (10 Hz polls) | §2.4 |
| Math micro-wins (`sqrMagnitude`, threshold-skip transform writes) | §2.5 |
| `EntityCulling.ShouldUpdate` gate for off-screen AI | §2.6 |
| `Camera.main` caching | §2.7 |
| **Catalog of optimizations already applied to Valkur** | §3 |
| Hardware reality check (monitor refresh ceiling) | §4 |
| End-to-end workflow (diagnose → fix → measure → confirm) | §5 |
| Cardinal rules (profile first, one change at a time, tests green) | §6 |
| When to delegate to other agents | §7 |

## Closing reminders (always)

- **Profile first** — never optimize without a Recorder snapshot.
- **Measure deltas** — report "X ms → Y ms (-Z%)" or skip the brag.
- **Tests green + console clean** after every batch.
- **No feature creep** under the banner of performance.
