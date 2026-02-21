"""Entity lookup and hit-testing helpers for Entities editor.
"""
from __future__ import annotations

import pygame


def iter_clickable_entities(game):
    """Yield (eid, screen_rect) for clickable entities (players/NPCs)."""
    cam = game.camera
    ecs = game.ecs.ecs_world
    sprites = ecs.components.get('Sprite', {})
    positions = ecs.components.get('Position', {})
    scale_map = ecs.components.get('Scale', {})
    player_tags = ecs.components.get('PlayerTagComponent', {})
    npc_tags = ecs.components.get('NPCTagComponent', {})

    for eid, sprite_comp in sprites.items():
        # Only players/NPCs with a Position component
        if eid not in positions or (eid not in player_tags and eid not in npc_tags):
            continue
        pos = positions[eid]
        sx, sy = cam.apply((pos.x, pos.y))
        entity_scale = getattr(scale_map.get(eid), 'scale', 1.0)
        scale_factor = entity_scale * cam.zoom
        scaled_img = pygame.transform.rotozoom(sprite_comp.image, 0, scale_factor)
        rect = scaled_img.get_rect()
        rect.topleft = (int(sx), int(sy))
        yield eid, rect


def find_clickable_entity_at(game, mx: int, my: int):
    """Return entity id under the given screen position, limited to players/NPCs.

    Uses Sprite, Position, optional Scale, and camera zoom to compute screen-space
    bounding rectangles for clickable entities.
    """
    for eid, rect in iter_clickable_entities(game):
        if rect.collidepoint(mx, my):
            return eid
    return None

def find_clickable_entity_rect_at(game, mx: int, my: int):
    """Return (eid, rect) for entity under mouse, or (None, None) if none."""
    for eid, rect in iter_clickable_entities(game):
        if rect.collidepoint(mx, my):
            return eid, rect
    return None, None

