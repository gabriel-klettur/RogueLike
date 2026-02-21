from __future__ import annotations

import pygame
from typing import Any

from roguelike_engine.z_layer.render import render_z_ordered

# Sprite scale cache: {(eid, scale_factor_rounded, id(orig)): Surface}
_scale_cache: dict[tuple[int, float, int], pygame.Surface] = {}


def render_z_entities(manager: Any, state, camera, screen, entities) -> None:
    """Render buildings and ECS-driven NPCs with Z-ordering.

    Optimized: NPCs are collected as lightweight tuples (z, y, image, dest)
    instead of creating _NPCWrapper objects per frame. Buildings still use
    their existing render API via render_z_ordered for compatibility.
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

    # --- Collect buildings as renderable parts ---
    building_parts = []

    editor_state = manager.tiles_editor.editor_state
    if not (
        (editor_state.active and not editor_state.toolbar_state.show_buildings)
        or (
            editor_state.active
            and editor_state.toolbar_state.show_collisions
            and not editor_state.toolbar_state.show_collisions_overlay
        )
    ):
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
                building_parts.append(part)

    # --- Collect NPC render data as lightweight tuples ---
    # Format: (z_layer, y_pos, image, dest_tuple, is_npc=True)
    # Buildings: (z_layer, y_pos, part_obj, None, is_npc=False)
    world = manager.ecs.ecs_world
    pos_map = world.components.get("Position", {})
    sprite_map = world.components.get("Sprite", {})
    zlayer_map = world.components.get("ZLayer", {})
    scale_map = world.components.get("Scale", {})

    zoom = camera.zoom
    zoom_key = round(zoom, 2)
    cam_apply = camera.apply
    is_in_view = camera.is_in_view

    # Build unified sort list: (z_layer, y_pos, render_type, data)
    # render_type: 0=building_part, 1=npc_tuple
    sort_list = []

    for part in building_parts:
        sort_list.append((state.z_state.get(part), part.y, 0, part))

    for eid in world.get_entities_with("Position", "Sprite", "ZLayer"):
        pos = pos_map.get(eid)
        sprite = sprite_map.get(eid)
        if pos is None or sprite is None:
            continue
        img = sprite.image
        if img is None:
            continue
        if not is_in_view(pos.x, pos.y, img.get_size()):
            continue

        # Compute scaled image (cached)
        scale_comp = scale_map.get(eid)
        entity_scale = scale_comp.scale if scale_comp else 1.0
        scale_factor = entity_scale * zoom_key

        if scale_factor != 1.0:
            key = (eid, scale_factor, id(img))
            scaled = _scale_cache.get(key)
            if scaled is None:
                scaled = pygame.transform.scale(
                    img,
                    (int(img.get_width() * scale_factor),
                     int(img.get_height() * scale_factor)),
                )
                _scale_cache[key] = scaled
            image = scaled
        else:
            image = img

        dest = cam_apply((pos.x, pos.y))
        layer = zlayer_map[eid].layer
        sort_list.append((layer, pos.y, 1, (image, dest)))

    if not sort_list:
        return

    # Sort by (z_layer, y_pos) — single sort for both buildings and NPCs
    sort_list.sort(key=lambda t: (t[0], t[1]))

    # Render in order
    blit = screen.blit
    for _, _, rtype, data in sort_list:
        if rtype == 0:
            # Building part: use its existing render method
            data.render(screen, camera)
        else:
            # NPC: direct blit (already scaled and positioned)
            blit(data[0], data[1])
