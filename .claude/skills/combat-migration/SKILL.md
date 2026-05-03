---
name: combat-migration
description: Combat-specific porting reference — melee slash arcs, projectile spell pipeline, NPC casting (NPCAutoCast + NPCCastState + BossPhaseController), spell executor strategy pattern (ProjectileExecutor / AreaExecutor / DashExecutor / 22 others), damage application order, mana / cooldown gating, status-effect hooks, audio / VFX wiring. Load when porting any combat or spell behaviour from Python.
---

# Combat Migration — Valkur

The full canonical knowledge base lives at:

**[`.github/skills/combat-migration/SKILL.md`](../../../.github/skills/combat-migration/SKILL.md)**

Read it directly when you need:

| Need | Section in source |
|---|---|
| Slash arc geometry (windup → swing → recover) | "Melee combat" |
| Projectile pipeline (spawn → travel → impact) | "Projectile spells" |
| `ISpellExecutor` strategy table (24 executors) | "Spell executors" |
| NPC casting state machine (NPCAutoCast → NPCCastState → BossPhaseController) | "NPC casting" |
| Damage formula + invincibility frames | "Damage application" |
| Mana / cooldown gating | "Resource gating" |
| Audio + VFX wiring per spell | "Combat feedback" |

## Quick reference

- All spells go through `SpellCaster.TryCast(slot, direction)` or `TryCastByKey(key, direction)`.
- The phase chain is `Ready → Prepare → Channel → Cooldown → Ready`. NPCs lock movement during the whole chain via `NPCCastState`.
- `SpellDefinition.element` is the data-driven hint for `ProjectileExecutor.ResolveElement`; legacy spell-key switch is the fallback.
- Boss rotations come from `BossDefinition.phases[i].autoCastList`; `BossConfigurator` rewires `NPCAutoCast` on each `BossPhaseController.OnPhaseChanged`.
- Audio: melee → `CombatAudioSystem.OnHitDealt`; spells → `IAudioService.PlaySfxById` directly from the executor.
- VFX: projectile body → `IProjectileVisual` on the prefab; impact → `ElementalImpactFX.Spawn` or `AreaFXRig`.

## Hard constraints

- **DO NOT** add a new spell type without an `ISpellExecutor` and a `SpellDefinition.type` enum entry.
- **DO NOT** bypass `SpellCaster` — it owns the phase FSM and mana deduction.
- **DO NOT** hardcode damage numbers; they belong on `SpellDefinition` (or on `MonsterDefinition.stats` for melee).
- **ALWAYS** preserve Python parity for prepare / channel / cooldown durations and damage values; conversion = `ticks ÷ 60`.
