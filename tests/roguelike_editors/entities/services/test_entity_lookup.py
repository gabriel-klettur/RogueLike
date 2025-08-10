import pygame
from types import SimpleNamespace

from roguelike_editors.entities.services.entity_lookup import (
    iter_clickable_entities,
    find_clickable_entity_at,
    find_clickable_entity_rect_at,
)


class Cam:
    def __init__(self, zoom=1.0):
        self.zoom = zoom

    def apply(self, pos):
        return pos  # identity for tests


def make_game(zoom=1.0, with_scale=False):
    cam = Cam(zoom=zoom)

    # Components maps mimic ECS storage
    # Sprite images are 10x10
    img = pygame.Surface((10, 10))
    sprites = {
        1: SimpleNamespace(image=img),  # player
        2: SimpleNamespace(image=img),  # npc
        3: SimpleNamespace(image=img),  # not clickable (no tags)
    }
    positions = {
        1: SimpleNamespace(x=100, y=100),
        2: SimpleNamespace(x=200, y=200),
        3: SimpleNamespace(x=300, y=300),
    }
    scale_map = {}
    if with_scale:
        scale_map[2] = SimpleNamespace(scale=1.5)  # npc scaled

    player_tags = {1: True}
    npc_tags = {2: True}

    ecs_world = SimpleNamespace(components={
        'Sprite': sprites,
        'Position': positions,
        'Scale': scale_map,
        'PlayerTagComponent': player_tags,
        'NPCTagComponent': npc_tags,
    })
    ecs = SimpleNamespace(ecs_world=ecs_world)
    game = SimpleNamespace(camera=cam, ecs=ecs)
    return game


def test_iter_clickable_entities_yields_players_and_npcs_only():
    game = make_game(zoom=1.0)
    out = list(iter_clickable_entities(game))
    eids = [eid for eid, rect in out]
    assert set(eids) == {1, 2}
    # rect topleft matches camera.apply position
    rect1 = [r for eid, r in out if eid == 1][0]
    assert rect1.topleft == (100, 100)


def test_iter_clickable_entities_respects_zoom_and_scale_in_rect_size():
    # zoom 2.0 doubles size; entity 2 has extra scale 1.5 => total 3x
    game = make_game(zoom=2.0, with_scale=True)
    out = dict(iter_clickable_entities(game))
    r1 = out[1]
    r2 = out[2]
    assert r1.size == (20, 20)  # 10x10 * 2.0
    assert r2.size == (30, 30)  # 10x10 * 3.0


def test_find_clickable_entity_at_returns_eid_under_mouse():
    game = make_game(zoom=1.0)
    # inside player 1 rect at (100,100) size 10x10
    assert find_clickable_entity_at(game, 105, 105) == 1
    # inside npc 2 rect at (200,200)
    assert find_clickable_entity_at(game, 205, 205) == 2
    # outside any
    assert find_clickable_entity_at(game, 0, 0) is None


def test_find_clickable_entity_rect_at_returns_pair_or_none():
    game = make_game(zoom=1.0)
    eid, rect = find_clickable_entity_rect_at(game, 205, 205)
    assert eid == 2 and rect is not None
    eid2, rect2 = find_clickable_entity_rect_at(game, 0, 0)
    assert eid2 is None and rect2 is None
