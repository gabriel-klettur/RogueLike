# Entity Properties Panel — Refactor Overview

This module implements the UI and logic for the in-editor properties panel of entities (players and monsters). The controller was refactored to be thin (≤200 lines) and delegate heavy logic to services.

## Structure

- `entities_properties_panel_controller.py` — Orchestrates view/model, delegates actions.
- `entities_properties_panel_view.py` — Rendering of panel and lists.
- `entities_properties_panel_events.py` — Input handling and interactions.
- `services/`
  - `asset_choice_service.py` — Apply chosen asset (in-memory vs command/persist).
  - `active_set_service.py` — React to active_set toggles and update ECS.
  - `edit_commit_service.py` — Commit text edits (add-system mode vs normal).
  - `add_entity_service.py` — Confirm and persist new entity into the system.
  - `panel_ui_utils.py` — Small UI cache/reset helpers.

These services preserve the exact API used by commands and other systems (`_on_asset_chosen`, `_on_active_set_toggled`, `_commit_edit`, `confirm_add_entity_on_system`, `_reset_edit_state`).

## Rationale

- Separation of concerns: controller coordinates, services implement business logic.
- Testability: services can be unit-tested independently.
- Maintainability: smaller files, clearer responsibilities, easier onboarding.

## Notes

- No functionality removed; commands and view integration remain intact.
- All persistence continues to use existing service functions.
- Active-set toggles and asset changes still update ECS and refresh UI.
