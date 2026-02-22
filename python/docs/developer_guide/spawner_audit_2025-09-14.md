# Spawner Module Audit Report (2025-09-14)

Objective: Identify code incongruences, duplications, deprecated/legacy elements, and obsolete code in `src/roguelike_editors/spawner/` to guide refactoring and modernization.


## Summary of Findings

- Heavy use of broad exception handling (`except Exception:`) across multiple files; risk of hiding bugs.
- Duplicate/obsolete handling of Visuals events eliminated in this iteration (see Changes Applied).
- Legacy fields (`spawner_img`, `spawner_img_size`) are consistently sanitized/removed in persistence.
- No uses of `print(...)` for debugging, no `pdb.set_trace`/`breakpoint()`, and no star imports found.
- Naming inconsistency clarified between `spawner_manager` (templates list) and `spawners_manager` (template properties).


## Scans and Results

- Pattern: `except Exception:`
  - Top files by occurrences (approximate):
    - `spawner_instance_properties_panel/instance_properties_controller.py` — 194
    - `spawner_instance_properties_panel/visuals/visuals_controller.py` — 57
    - `events/split_drag.py` — 38
    - `services/persistence.py` — 36
    - `events/handler.py` — 32
    - `spawner_instance_toolbar/spawner_instance_toolbar_controller.py` — 19
    - `events/confirmations.py` — 17
    - `events/resize.py` — 16
    - `spawner_instance_properties_panel/services/buildings_service.py` — 15
    - `views/buildings_overlay.py` — 15
    - `spawner_instance_properties_panel/visuals/visuals_events.py` — 14
    - `views/orchestrator.py` — 14
    - `spawner_instance_properties_panel/instance_properties_view.py` — 12
  - Note: These counts were observed via repository grep; see codebase for current source-of-truth.

- Pattern: `legacy|deprecated|obsolete` (case-insensitive)
  - `spawner_instance_properties_panel/visuals/visuals_picker.py`: legacy image field mapping (`image`) in buildings templates.
  - `spawner_instance_properties_panel/instance_properties_controller.py`: comments noting obsolete helpers moved to `visuals`.
  - `services/persistence.py`: sanitizing legacy fields `spawner_img`, `spawner_img_size` in templates/instances and overrides; also notes about legacy int-valued visuals vs dict format.

- Pattern: `TODO|FIXME|HACK|BUG`
  - No matches found.

- Pattern: `print(` (debug prints)
  - No matches found.

- Pattern: star imports `from X import *`
  - No matches found.

- Pattern: Debug breakpoints `pdb.set_trace` / `breakpoint()`
  - No matches found.


## Concrete Incongruences and Resolutions

- Visuals events duplication/incongruence
  - Before: `InstancePropertiesEventHandler` hacía hit-testing de Visuals usando rects de `InstancePropertiesView` (obsoletos) mientras `VisualsEvents` + `VisualsModel` ya gestionaban todo.
  - Now: Toda la interacción de Visuals (browse/eye/clear/edición) se delega exclusivamente en `VisualsEvents`; los rects viven en `VisualsModel`.

- Obsolete fields in `InstancePropertiesView`
  - `visuals_*_rects` eliminados (eran solo por compatibilidad y ya no se actualizaban).

- Unused import
  - `config as _cfg` eliminado en `instance_properties_controller.py`.


## Recommendations

- Exceptions and Logging
  - Replace blanket `except Exception:` with either:
    - Narrow exceptions where known failure modes exist, or
    - `logger.debug("context", exc_info=True)` for non-critical UI paths (as started in `views/overlays.py`, `views/orchestrator.py`, `views/buildings_overlay.py`).
  - Prioritize reductions in:
    - `spawner_instance_properties_panel/instance_properties_controller.py`
    - `spawner_instance_properties_panel/visuals/visuals_controller.py`
    - `services/persistence.py`

- Legacy fields and formats
  - Keep sanitizing `spawner_img` and `spawner_img_size` on load/save until their presence is fully removed from any external data.
  - Document expected new visuals mapping format (dict with `instance_id`/`building_instance_id`) and deprecate legacy int mapping.

- Naming Consistency
  - Maintain documentation distinction:
    - `SpawnerManagerController` = Plantillas (lista) — tool key: `spawner_manager`.
    - `SpawnersManagerController` = Propiedades de plantilla (subpanel).
  - Optionally consider renaming to `SpawnerTemplatesManager` y `SpawnerTemplateProperties` en una futura major refactor.

- Prevent regressions (CI / linting)
  - Add a linter rule set (e.g., `ruff` or `flake8`) and CI checks:
    - Disallow `from X import *`.
    - Flag `except Exception:` and suggest alternatives.
    - Flag `print()` in non-test modules.
  - Optionally add a pre-commit hook to run `ruff`.

- Tests (smoke / interaction)
  - Add/augment tests that cover:
    - Visuals table: browse/eye/clear/edición confirm/cancel.
    - Template combobox scroll/hover/select.
  - Provide headless event sequences using `pygame` dummy drivers as per test setup.


## Proposed Refactor Batches

- Batch A (UI overlays and orchestration) — low risk
  - Convert remaining `except Exception:` in `views/` subtree to logged or narrowed exceptions.

- Batch B (Instance Properties Controller) — medium complexity
  - Narrow exceptions around specific operations (JSON manipulations, value parsing, building lookups) and add unit tests for edge cases.

- Batch C (Persistence) — medium risk
  - Replace generic catches with specific ones (`ValueError`, `KeyError`, `JSONDecodeError`, `OSError`) and add tests for malformed input.

- Batch D (Visuals Controller) — medium risk
  - Audit and document failure modes; narrow exceptions in critical paths.


## Status of Applied Changes (this iteration)

- Delegation of Visuals events to `VisualsEvents` exclusively — DONE.
- Removal of obsolete `visuals_*_rects` in `InstancePropertiesView` — DONE.
- Removal of unused import `_cfg` in `InstancePropertiesController` — DONE.
- Logging improvements in `views/overlays.py`, `views/orchestrator.py`, `views/buildings_overlay.py` — DONE.
- README updated to clarify naming of Spawner panels — DONE.


## Next Actions

- Decide which refactor batch to prioritize next (A/B/C/D).
- Introduce linter configuration (e.g., `ruff`) and a minimal GitHub Actions workflow or local pre-commit hook to prevent regressions.
- Add smoke tests for Visuals interactions and template combobox behavior.
