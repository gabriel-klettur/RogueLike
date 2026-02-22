---
trigger: manual
---
# Python Workspace Rules (Legacy / Migration Source)

## Source-of-truth usage
- Treat `python/` as migration reference and parity baseline unless task is Python-only.
- Preserve schema expectations used by Unity migrators.
- Avoid breaking data contracts consumed by migration tooling.

## ECS and system discipline
- Respect explicit system order and deterministic update behavior.
- Avoid hidden cross-system coupling and side-effect-heavy utilities.
- Keep data mapping isolated from runtime behavior logic.

## Performance and correctness
- Prefer profiled, measurable changes over speculative optimization.
- Keep hot paths allocation-aware.
- Add/update tests for non-trivial behavior changes.

## Cross-stack parity guardrails
- When touching Python + Unity parity flows, document assumptions explicitly.
- Record any contract change impacting `unity/` importers or ScriptableObject generation.
- Never change save/data formats without migration notes.
