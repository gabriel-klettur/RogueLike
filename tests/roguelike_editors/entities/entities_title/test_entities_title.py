import pygame
from types import SimpleNamespace

from roguelike_editors.entities.entities_title.entities_title_controller import (
    EntitiesTitleController,
)


def test_entities_title_controller_renders_without_errors():
    model = SimpleNamespace(title="Entities Editor")
    font = pygame.font.Font(None, 16)
    ctrl = EntitiesTitleController(editor_state=None, model=model, font=font)

    surface = pygame.Surface((200, 80))
    # Should not raise
    ctrl.render(surface)

    # Widget text mirrors model.title
    assert ctrl.view.widget.text == "Entities Editor"


def test_entities_title_controller_handle_event_returns_false():
    model = SimpleNamespace(title="Entities")
    font = pygame.font.Font(None, 16)
    ctrl = EntitiesTitleController(editor_state=None, model=model, font=font)

    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(0, 0))
    assert ctrl.handle_event(ev) is False
