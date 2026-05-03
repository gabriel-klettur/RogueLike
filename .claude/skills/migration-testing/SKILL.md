---
name: migration-testing
description: Parity testing methodology between Python (pytest) and Unity (NUnit) — how to express the same behavioural assertion in both runtimes, how to compare numerical outcomes (damage, speeds, cooldowns) without floating-point flake, headless fixture patterns, the migration-qa report format. Load before writing or reviewing parity tests, or when @migration-qa is asked to compare Python and Unity behaviour.
---

# Migration Testing — Valkur

The full canonical knowledge base lives at:

**[`.github/skills/migration-testing/SKILL.md`](../../../.github/skills/migration-testing/SKILL.md)**

Read it directly when you need:

| Need | Section in source |
|---|---|
| Parity test workflow (Python pytest ↔ Unity NUnit) | "Parity test patterns" |
| Numerical tolerance rules (damage / speed / radius) | "Numerical assertions" |
| Headless fixture set-up for Unity EditMode | "EditMode test fixtures" |
| `@migration-qa` report template | "Reporting parity gaps" |
| Conversion factors when comparing Python ticks → Unity seconds | "Unit conversions" |

## Quick reference

- Python ticks (60 Hz) → seconds: `÷ 60`.
- Python px → Unity world units: `÷ 16` (PPU=16; Buildings uses PPU=32).
- Python px / tick → Unity world-units / second: `× 3.75`.
- Python px / tick² → Unity world-units / second²: `× 225`.
- **Always** convert at the comparison site, never store converted values in either runtime.
- Tolerance default: `Mathf.Abs(unity - python) ≤ 0.001` for floats; exact match for ints.
- For randomised systems (loot, AI rolls), compare **distribution** over N samples, not single outcomes.
- Use `Assert.That(unity, Is.EqualTo(python).Within(tol))` — gives the diff in the message.

## Hard constraints

- **DO NOT** modify Python source under `python/src/` to fix a parity gap. Update the Unity port instead.
- **DO NOT** add platform-specific `#if UNITY_EDITOR` to a parity test that should run in both Editor and PlayMode.
- **DO** record the Python baseline (commit hash + test output) when a parity test is written, so future drift is detectable.
