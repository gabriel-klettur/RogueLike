import pytest
import pygame

from roguelike_game.ecs.systems.inventory.inventory_editor_system import InventoryEditorSystem
from roguelike_game.ecs.components.input_component import InputComponent

class DummyWorld:
    pass

@pytest.fixture(autouse=True)
def init_pygame(request):
    pygame.init()
    def fin():
        pygame.quit()
    request.addfinalizer(fin)


def test_toggle_opens_and_closes(capsys):
    system = InventoryEditorSystem()
    world = DummyWorld()
    # Prepare world.components with one InputComponent
    inp = InputComponent()
    world.components = {'InputComponent': {1: inp}}

    # Initially inactive
    assert not system.active

    # Toggle to open
    inp.toggle_editor = True
    system.update(world)
    captured = capsys.readouterr()
    assert "[InventoryEditorOpened]" in captured.out
    assert system.active is True
    assert inp.toggle_editor is False

    # Toggle to close
    inp.toggle_editor = True
    system.update(world)
    captured = capsys.readouterr()
    assert "[InventoryEditorClosed]" in captured.out
    assert system.active is False
    assert inp.toggle_editor is False


def test_no_toggle_when_flag_false(capsys):
    system = InventoryEditorSystem()
    world = DummyWorld()
    inp = InputComponent()
    world.components = {'InputComponent': {1: inp}}
    system.active = False

    # Without flag, no output and state unchanged
    inp.toggle_editor = False
    system.update(world)
    captured = capsys.readouterr()
    assert captured.out == ""
    assert system.active is False

# Note: rendering tested indirectly; ensure no exceptions

def test_render_inactive_does_nothing():
    system = InventoryEditorSystem()
    surface = pygame.Surface((10, 10), pygame.SRCALPHA)
    # Should not raise when inactive
    system.render(None, surface)


def test_render_active_draws_overlay():
    system = InventoryEditorSystem()
    surface = pygame.Surface((10, 10), pygame.SRCALPHA)
    # Activate
    system.active = True
    # Fill surface with distinct color
    surface.fill((0, 0, 0, 0))
    # Should draw overlay without error
    system.render(None, surface)
    # Pixel at (0,0) should now be non-zero alpha
    pixel = surface.get_at((0, 0))
    assert pixel.a != 0
