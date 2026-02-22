---
description: unity bugfix and regression hardening
---
# Goal
Resolve Unity bugs with root-cause precision and protect Valkur from regressions.

## 1) Reproduce and bound the bug
1. Define exact repro steps (scene, input, expected vs actual).
2. Confirm frequency (always/intermittent).
3. Identify impacted systems (UI, gameplay, data, render, bootstrap).

## 2) Trace runtime flow end-to-end
1. Find trigger source (input/event/bootstrap).
2. Follow data flow through gameplay logic and state transitions.
3. Confirm final render/UI sink receives correct values.
4. Record any timing/order dependency (`Awake`, `OnEnable`, `Start`, runtime `AddComponent`).

## 3) Validate Unity invariants
- LayerMask/tag assumptions are valid.
- Sorting layer/order are explicit for world-space visuals.
- UI fill bars have sprites.
- Runtime-instantiated objects are active when required.
- URP-compatible rendering path is used.

## 4) Implement the smallest upstream fix
1. Fix the earliest broken point (do not patch downstream symptoms first).
2. Keep change set minimal and cohesive.
3. Add guard rails for null/missing dependencies.

## 5) Regression verification matrix
1. Original bug fixed.
2. Related features still work.
3. Neighbor systems unaffected.
4. Visual/readability checks pass on desktop aspect ratios.

## 6) Session closeout and continuity
1. Document root cause and final fix in concise bullets.
2. Capture "pitfall -> prevention" notes for next session.
3. Update project context/workflow docs when conventions change.
