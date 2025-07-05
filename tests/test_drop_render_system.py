import pytest
import pygame
from types import SimpleNamespace

from roguelike_game.ecs.systems.inventory.drop_render_system import DropRenderSystem
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.physical_item_component import PhysicalItemComponent

@pytest.fixture(autouse=True)
def patch_loaders(monkeypatch):
    # Mock load_items to return a test item model
    monkeypatch.setattr(
        'roguelike_game.ecs.systems.inventory.drop_render_system.load_items',
        lambda path: {'gold': SimpleNamespace(icon='g', icon_small=None, scale_map=1.0)}
    )
    # Mock load_image to return a surface of size 10x15
    monkeypatch.setattr(
        'roguelike_game.ecs.systems.inventory.drop_render_system.load_image',
        lambda path: pygame.Surface((10, 15))
    )
    return monkeypatch

class DummyWorld:
    def __init__(self):
        self.components = {'Position': {}, 'PhysicalItemComponent': {}}

    def get_entities_with(self, *comps):
        return [eid for eid in self.components['Position']
                if all(eid in self.components.get(c, {}) for c in comps)]

@pytest.fixture
def world():
    w = DummyWorld()
    eid = 1
    w.components['Position'][eid] = Position(2, 3)
    w.components['PhysicalItemComponent'][eid] = PhysicalItemComponent('d1', 'gold', 1)
    return w

@pytest.fixture
def screen():
    return pygame.Surface((100, 100))

@pytest.fixture
def camera():
    return SimpleNamespace(zoom=1.0, apply=lambda pos: (int(pos[0] * 10), int(pos[1] * 10)))


def test_drop_render_initial_scale(world, screen, camera):
    system = DropRenderSystem()
    # Before update, caches are empty
    assert not system._raw_surfaces
    assert not system._scaled_cache

    system.update(world, screen, camera)

    # raw surface cached per path
    assert 'g' in system._raw_surfaces
    # scaled_cache key: (eid, scale_map * zoom)
    key = (1, round(1.0 * 1.0, 2))
    assert key in system._scaled_cache
    surf = system._scaled_cache[key]
    # original size (10,15) scaled by 1.0
    assert surf.get_size() == (10, 15)


def test_drop_render_zoom_change(world, screen, camera):
    system = DropRenderSystem()
    system.update(world, screen, camera)
    # Change zoom
    camera.zoom = 2.0
    # Position unchanged but scale cache should get new entry
    system.update(world, screen, camera)

    key = (1, round(1.0 * 2.0, 2))
    assert key in system._scaled_cache
    surf2 = system._scaled_cache[key]
    # size should be original * 2.0
    assert surf2.get_size() == (20, 30)


def test_drop_render_logs_only_on_change(world, screen, camera, capsys):
    system = DropRenderSystem()
    # first update logs
    system.update(world, screen, camera)
    out1 = capsys.readouterr().out
    assert "Rendering drop eid=1" in out1

    # second update without position change: no logs
    system.update(world, screen, camera)
    out2 = capsys.readouterr().out
    assert out2 == ''

    # change position -> should log again
    world.components['Position'][1] = Position(5, 6)
    system.update(world, screen, camera)
    out3 = capsys.readouterr().out
    assert "Rendering drop eid=1" in out3
