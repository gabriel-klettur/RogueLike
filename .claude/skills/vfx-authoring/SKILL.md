---
name: vfx-authoring
description: Valkur particle/VFX authoring — the art direction (HD glow over pixel-art world), the ParticleVfxParams field reference, recipes per `kind`, the gradient/curve rules, the additive-vs-alpha decision, per-preset budgets, naming, and the known engine gaps that cap how beautiful a preset can get. Load before creating or tuning any particle preset, touching ParticleEmitter, or working in the Particles Editor (F1).
---

# VFX Authoring for Valkur

The full canonical knowledge base lives at:

**[.github/skills/vfx-authoring/SKILL.md](../../../.github/skills/vfx-authoring/SKILL.md)**

Read it directly with the `Read` tool when you need any of:

| Need | Section |
|---|---|
| **Art direction — HD glow over pixel-art world** | §1 |
| Which pixel-art rules are suspended on the `VFX` layer | §1 (table) |
| The four beauty levers, ranked by payoff | §1 |
| Depth cues that sell "3D" on a 2D billboard | §1 |
| `ParticlePresetDefinition` structure + why its fields are public | §2 |
| **Full `ParticleVfxParams` field reference** (what each field really drives) | §2 |
| Procedural texture shapes + `Auto` mapping + material cache | §2.1 |
| **Simulation space — trail vs halo, and why a local-space trail cannot exist** | §2.2 |
| The three gradient paths and how `startColor` multiplies them | §2 |
| Per-`kind` shape + recipe table | §3 |
| Additive vs alpha decision + additive traps | §4 |
| Steady-state particle budgets per context | §5 |
| Naming, `id` stability, layered-preset suffixes, catalog registration | §6 |
| **Known engine gaps that cap beauty** (no trails module, no lights, no flipbooks) | §7 |
| Step-by-step workflow for authoring a new preset | §8 |

## Closing reminders (always)

- **The VFX layer is not pixel-art.** Smooth, colorful, glowing — deliberately.
- **`alphaOverLife` first.** Without it, `colorOverLife` is silently ignored.
- **Beauty comes from stacking presets**, not from one mega-preset. Spells stack through
  `vfxPresetLayers` / `impactPresetLayers` / `castPresetLayers`.
- **A trail needs `worldSpace`.** Local-space particles ride along with the emitter and
  leave nothing behind.
- **Budget the steady state** (`emitRate × lifespan`), not the `count`.
- **Never rename a preset `id`** — it orphans every placed instance in the world JSON.
- Console clean via MCP before declaring done.
