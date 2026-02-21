"""Orchestrator: reload Python code and all game data, preserving editor state.

Public entry point: reload_all_game_data_and_code(game, force=False) -> dict[str, int]
"""
from __future__ import annotations

from typing import Any, Dict
import logging

from .code_reload import reload_changed_python_modules
from .data_full import reload_all_game_data as _reload_all_game_data, _reload_spawners as _ensure_spawners

logger = logging.getLogger(__name__)


def _snapshot_state(game: Any) -> dict:
    """Capture minimal state to keep UX consistent across reloads."""
    snap: dict = {
        "debug_spawner": False,
        "spawner_editor_active": False,
        "spawner_ui_state": {},
        "spawner_editor_visible": False,
    }
    try:
        import roguelike_engine.config.config as _cfg
        snap["debug_spawner"] = bool(getattr(_cfg, "DEBUG_SPAWNER", False))
    except Exception:
        pass

    try:
        world = getattr(getattr(game, "ecs", None), "ecs_world", None)
        if world is not None and hasattr(world, "state"):
            st = world.state
            snap["spawner_editor_active"] = bool(getattr(st, "spawner_editor_active", False))
            # Snapshot commonly used UI state keys so UX persists after reload
            for k in (
                "spawner_editor_hovered_eid",
                "spawner_selected_eid",
                "spawner_remove_candidate_eid",
                "spawner_info_pos",
                "spawner_input_suppressed",
            ):
                if hasattr(st, k):
                    snap["spawner_ui_state"][k] = getattr(st, k)
        # Snapshot UI editor visibility (controller/model)
        try:
            se = getattr(game, "spawner_editor", None)
            snap["spawner_editor_visible"] = bool(
                getattr(getattr(se, "model", None), "visible", False)
            )
        except Exception:
            pass
    except Exception:
        pass
    return snap


essential_overlay_modules = (
    "roguelike_game.ecs.systems.rendering.spawner.spawner_anchor_debug_system",
    "roguelike_game.ecs.systems.rendering.spawner.spawner_info_overlay_system",
    "roguelike_game.ecs.systems.rendering.spawner.collider_velocity_debug_system",
    "roguelike_game.ecs.systems.rendering.spawner_debug_system",
)


def _restore_state(game: Any, snap: dict) -> None:
    """Restore state after reload, with defensive guards."""
    try:
        import roguelike_engine.config.config as _cfg2
        # Restore DEBUG_SPAWNER exactly as it was before reload
        setattr(_cfg2, "DEBUG_SPAWNER", bool(snap.get("debug_spawner", False)))
    except Exception:
        pass

    try:
        world2 = getattr(getattr(game, "ecs", None), "ecs_world", None)
        if world2 is not None and hasattr(world2, "state"):
            prev_active = bool(snap.get("spawner_editor_active", False))
            prev_visible = bool(snap.get("spawner_editor_visible", False))
            # Keep editor_active only if it was previously active or the editor UI remains visible
            setattr(
                world2.state,
                "spawner_editor_active",
                bool(prev_active or prev_visible),
            )
            # Restore UI state snapshot
            try:
                for k, v in (snap.get("spawner_ui_state") or {}).items():
                    setattr(world2.state, k, v)
            except Exception:
                pass
        # Restore editor UI visibility
        try:
            se2 = getattr(game, "spawner_editor", None)
            if se2 is not None and hasattr(se2, "model"):
                prev_active = bool(snap.get("spawner_editor_active", False))
                prev_visible = bool(snap.get("spawner_editor_visible", False))
                setattr(se2.model, "visible", bool(prev_visible or prev_active))
        except Exception:
            pass
        # Force-enable DEBUG_SPAWNER on overlay modules that kept old config alias
        try:
            if bool(snap.get("spawner_editor_active", False)):
                for _mn in essential_overlay_modules:
                    try:
                        _m = __import__(_mn, fromlist=["*"])
                        _cfg_alias = getattr(_m, "config", None)
                        if _cfg_alias is not None:
                            setattr(_cfg_alias, "DEBUG_SPAWNER", True)
                    except Exception:
                        continue
        except Exception:
            pass
        # Ensure spawners exist if the component map is empty after reload (edge case)
        try:
            comps2 = getattr(world2, "components", {}) if world2 is not None else {}
            if not (comps2.get("SpawnerConfig") or {}):
                _ = _ensure_spawners(game)
        except Exception:
            pass
        # Reinitialize ECS systems so hot-reloaded classes are picked up by instances
        try:
            if world2 is not None and hasattr(world2, "reinit_systems_preserving_state"):
                world2.reinit_systems_preserving_state()
        except Exception:
            pass
    except Exception:
        pass


# --- Public orchestrator -------------------------------------------------------

def reload_all_game_data_and_code(game: Any, *, force: bool = False) -> Dict[str, int]:
    """Reload Python code (under src/) and all JSON/DB-backed game data.

    Returns a dict summary that includes 'Code' key for modules reloaded.
    """
    return run_reload(
        game,
        force=bool(force),
        code_reload=reload_changed_python_modules,
        data_reload=_reload_all_game_data,
    )


def run_reload(
    game: Any,
    *,
    force: bool,
    code_reload,
    data_reload,
) -> Dict[str, int]:
    """Perform a full reload using injectable callables.

    This indirection enables tests to monkeypatch the top-level wrapper
    and still affect the orchestrated flow.
    """
    snap = _snapshot_state(game)

    try:
        code_cnt = int(code_reload(force=force) or 0)
    except Exception:
        logger.exception("[hot_reload] code_reload failed")
        code_cnt = 0
    try:
        data_summary = data_reload(game, force=force) or {}
    except Exception:
        logger.exception("[hot_reload] data_reload failed")
        data_summary = {}

    _restore_state(game, snap)

    try:
        summary: Dict[str, int] = {"Code": int(code_cnt)}
        summary.update({k: int(v or 0) for k, v in (data_summary or {}).items()})
    except Exception:
        summary = {"Code": int(code_cnt)}
    return summary
