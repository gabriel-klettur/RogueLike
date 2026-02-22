---
description: unity feature delivery expert flow
---
# Goal
Implement any Unity gameplay/UI feature in Valkur with production-level quality, minimal regressions, and clear architectural boundaries.

## 0) Load workspace rules first (mandatory)
1. Read `.windsurf/rules/workspace-engineering-rules.md`.
2. Read `.windsurf/rules/unity-workspace-rules.md`.
3. If Python parity/migration is involved, also read `.windsurf/rules/python-workspace-rules.md`.

## 1) Define scope and acceptance (non-negotiable)
1. Write a short scope block:
   - What is in scope.
   - What is out of scope.
   - Functional acceptance criteria.
   - Visual/UX acceptance criteria (if UI).
2. Identify impacted layers:
   - UI/Presentation
   - Gameplay logic
   - Data/config
   - Infrastructure/bootstrap

## 2) Map the current implementation before coding
1. Locate where the behavior currently starts (input, event, scene bootstrap).
2. Locate where it ends (UI update, damage application, VFX, save state, etc.).
3. Draw a 5-10 bullet data-flow path from source to sink.
4. List extension points and coupling risks.

## 3) Design the fix/feature
1. Prefer minimal root-cause changes.
2. Keep separation of concerns strict.
3. Use explicit contracts/events instead of hidden side effects.
4. Define fallback behavior for missing references/components.

## 4) Implement incrementally
1. Add/adjust interfaces/events first (if needed).
2. Implement core logic second.
3. Implement UI/rendering binding third.
4. Wire setup/bootstrapping last (`EntitySetup`, `HUDBootstrap`, scene setup).
5. Keep changes small and reviewable.

## 5) Unity-specific correctness checklist
- Input path resolves to correct action and timing (`Update` vs `FixedUpdate`).
- LayerMask/tag assumptions match `ProjectSettings/TagManager.asset`.
- Sorting layer + order are explicitly set for world-space visuals.
- UI `Image.Type.Filled` has a sprite assigned.
- Runtime-created prefabs/components are activated and initialized in correct order.
- URP-compatible rendering path is used (avoid GL-only assumptions).

## 6) Verification matrix
1. Happy path (feature works as intended).
2. Edge path (null refs, dead targets, no resources/mana, cooldowns, etc.).
3. Regression path (unrelated systems still work).
4. Visual path (colors, readability, layering, animation).

## 7) Closeout
1. Summarize changes by file and intent.
2. Document known limitations and next hardening steps.
3. Update persistent context notes so next session starts with full continuity.
