---
trigger: manual
---
# Unity Workspace Rules (Valkur)

## Runtime and scene truth
- Unity scenes, ProjectSettings, and runtime bootstrap are source of truth.
- Validate lifecycle order (`Awake`, `OnEnable`, `Start`, runtime `AddComponent`).
- If scene state matters, request playmode repro + console evidence.

## Rendering and UI invariants
- Set explicit sorting layer + sorting order for world-space visuals.
- For UGUI `Image.Type.Filled`, always assign a sprite.
- Use URP-compatible rendering paths; avoid GL-only assumptions.
- Confirm LayerMask/tag assumptions against `ProjectSettings/TagManager.asset`.

## Gameplay and wiring
- Verify input timing (`Update`) vs physics timing (`FixedUpdate`).
- For runtime-added components, force initial state sync if needed.
- Prefer event-driven wiring over polling when practical.
- Keep bootstrap wiring centralized (e.g., scene setup / HUD bootstrap / entity setup).

## Regression checklist (Unity)
- Original bug fixed.
- HUD/UI values match runtime data.
- Layering/visibility correct in-world and screen-space.
- No new null refs or lifecycle race conditions.
