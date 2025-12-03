from __future__ import annotations

import pygame  # noqa: F401 - kept for type hints/compat
from typing import Any

from roguelike_engine.z_layer.render import render_z_ordered
from .npc_render_proxy import _NPCWrapper


def render_z_entities(manager: Any, state, camera, screen, entities) -> None:
    """Render buildings and ECS-driven NPCs with Z-ordering.

    Mirrors the original logic from RendererManager._render_z_entities but
    extracted for clarity and testability.
    """
    # Map Editor short-circuit: only buildings if enabled
    if manager.map_editor.editor_state.active:
        if manager.map_editor.editor_state.show_buildings:
            parts = []
            for b in entities.buildings:
                if not camera.is_in_view(b.x, b.y, b.image.get_size()):
                    continue
                for part in b.get_parts():
                    state.z_state.set(part, part.z)
                    parts.append(part)
            render_z_ordered(parts, screen, camera, state.z_state)
        return

    all_entities = []

    # Buildings gate by Tiles Editor visibility and collision-only mode
    editor_state = manager.tiles_editor.editor_state
    if not (
        (editor_state.active and not editor_state.toolbar_state.show_buildings)
        or (
            editor_state.active
            and editor_state.toolbar_state.show_collisions
            and not editor_state.toolbar_state.show_collisions_overlay
        )
    ):
        # Spawner editor may hide editor_hidden buildings
        spawner_editor_active = False
        try:
            w = manager.ecs.ecs_world
            spawner_editor_active = bool(getattr(getattr(w, "state", None), "spawner_editor_active", False))
        except Exception:
            spawner_editor_active = False

        for b in entities.buildings:
            try:
                if (spawner_editor_active and getattr(b, "editor_hidden", False)) or getattr(
                    b, "runtime_hidden", False
                ):
                    continue
            except Exception:
                pass
            try:
                if hasattr(b, "visible") and not getattr(b, "visible", True):
                    continue
            except Exception:
                pass
            if not camera.is_in_view(b.x, b.y, b.image.get_size()):
                continue
            for part in b.get_parts():
                state.z_state.set(part, part.z)
                all_entities.append(part)

    # ECS NPCs: wrap for uniform render API and assign Z-layer
    # Apply view culling BEFORE creating wrappers to avoid unnecessary object creation
    world = manager.ecs.ecs_world
    pos_map = world.components.get("Position", {})
    sprite_map = world.components.get("Sprite", {})
    zlayer_map = world.components.get("ZLayer", {})
    
    for eid in world.get_entities_with("Position", "Sprite", "ZLayer"):
        pos = pos_map.get(eid)
        sprite = sprite_map.get(eid)
        if pos is None or sprite is None:
            continue
        # Culling: skip entities outside camera view
        img = sprite.image
        if img is not None and not camera.is_in_view(pos.x, pos.y, img.get_size()):
            continue
        layer = zlayer_map[eid].layer
        npc = _NPCWrapper(world, eid)
        state.z_state.set(npc, layer)
        all_entities.append(npc)

    # Early exit if nothing to render
    if not all_entities:
        return

    render_z_ordered(all_entities, screen, camera, state.z_state)
