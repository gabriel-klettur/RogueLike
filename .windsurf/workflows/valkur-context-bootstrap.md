---
description: valkur unity session bootstrap
---
# Goal
Start any new Valkur Unity session with full technical context, zero assumptions, and a regression-aware plan.

## 0) Operating context (MCP + Windsurf)
1. Assume all work is executed through Windsurf using MCP-connected tools.
2. Treat Unity as the runtime/editor authority: prefer Unity-side files, settings, and scene wiring over assumptions.
3. If behavior depends on scene state, ask for playmode/console evidence before broad refactors.
4. Keep edits deterministic and traceable with file references.

## 0.1) Load workspace rules first (mandatory)
1. Read `.windsurf/rules/workspace-engineering-rules.md`.
2. If task touches Unity, read `.windsurf/rules/unity-workspace-rules.md`.
3. If task touches Python, read `.windsurf/rules/python-workspace-rules.md`.
4. If task touches both, enforce both rule sets and document cross-stack assumptions.

## 1) Load canonical project context (must-read)
1. Read `unity/README.md`.
2. Read `unity/MIGRACION_PASO_A_PASO.md`.
3. Extract current migration status, completed phases, and pending steps.

## 2) Load runtime architecture entry points
1. Read `unity/Valkur/Assets/_Project/Scripts/Gameplay/GameplaySceneSetup.cs`.
2. Read `unity/Valkur/Assets/_Project/Scripts/Gameplay/EntitySetup.cs`.
3. Read `unity/Valkur/Assets/_Project/Scripts/UI/HUD/HUDBootstrap.cs`.
4. Read `unity/Valkur/Assets/_Project/Scripts/UI/HUD/HUDManager.cs`.

## 3) Load combat + HUD critical modules
1. Read `Gameplay/PlayerController.cs`, `Gameplay/MeleeCombat.cs`, `Gameplay/Spells/SpellCaster.cs`.
2. Read `UI/HUD/TargetHUD.cs`, `Gameplay/Combat/WorldHealthBar.cs`.
3. Read `Gameplay/Combat/CombatRangeVisualizer.cs`.

## 4) Load engine conventions
1. Read `unity/Valkur/ProjectSettings/TagManager.asset`.
2. Record tags/layers/sorting layers currently active.
3. Confirm render pipeline assumptions (URP 2D).

## 5) Build a Session Context Card (mandatory output)
Create a concise card with:
- Architecture map: Bootstrap -> Scene setup -> EntitySetup -> Runtime systems.
- Layer/tag map (Player/NPC/Projectile/World/Pickup/UIBlocker).
- Sorting layers map (Default, Ground, Entities, Projectiles, VFX, UI_World, Overlay).
- Input and combat bindings currently active.
- HUD wiring map (PlayerHUD, TargetHUD, WorldHealthBar, mouse targeting).
- Current migration progress and open debt.

## 6) Valkur regression checklist (run every session)
- `Image.Type.Filled` bars must have a sprite assigned.
- Components added after `Initialize()` must force-sync initial state.
- Inactive prefab templates create inactive clones unless explicitly activated.
- World-space UI bars must use the correct sorting layer/material.
- URP compatibility: avoid GL-only render paths.

## 7) Execution discipline
1. Create/update task TODO list before coding.
2. Propose a multi-step plan.
3. Implement minimal root-cause fix first.
4. Validate with a short scenario matrix.
5. Summarize with file references and what changed/why.
