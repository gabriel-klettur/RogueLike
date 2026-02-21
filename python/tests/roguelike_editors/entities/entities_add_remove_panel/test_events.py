import pygame
from types import SimpleNamespace

from roguelike_editors.entities.entities_add_remove_panel.entities_add_remove_panel_events import (
    EntitiesAddRemovePanelEventHandler,
)
from roguelike_editors.entities.entities_add_remove_panel.entities_add_remove_panel_model import (
    EntitiesAddRemovePanelModel,
)
from roguelike_editors.entities.services.constants import (
    ENTITIES_TOOL_ON_MAP,
    ADD_ENTITIES_ON_SYSTEM,
)


def _make_controller_stub():
    calls = {
        "enter_spawn_mode": 0,
        "exit_spawn_mode": 0,
        "enter_delete_mode": 0,
        "exit_delete_mode": 0,
        "open_new_monster_properties": 0,
        "enter_add_entities_on_system_mode": 0,
        "exit_add_entities_on_system_mode": 0,
    }

    def _m(name):
        def fn(*a, **k):
            calls[name] += 1
        return fn

    icon_rects = {
        "add_entitie": pygame.Rect(0, 0, 20, 20),
        "remove_entitie": pygame.Rect(30, 0, 20, 20),
        "add_entities_on_system": pygame.Rect(60, 0, 20, 20),
    }
    widget = SimpleNamespace(icon_rects=icon_rects)
    add_remove_controller = SimpleNamespace(view=SimpleNamespace(widget=widget))

    # Minimal properties controller model used by event handler
    pp_model = SimpleNamespace(
        show_add_system_selector=False,
        entity_type_rect=None,
    )
    properties_controller = SimpleNamespace(model=pp_model)

    controller = SimpleNamespace(
        model=SimpleNamespace(
            spawn_mode_active=False,
            delete_mode_active=False,
            toolbar_model=SimpleNamespace(active_tool=ENTITIES_TOOL_ON_MAP),
        ),
        add_remove_controller=add_remove_controller,
        toolbar_model=SimpleNamespace(active_tool=ENTITIES_TOOL_ON_MAP),
        properties_controller=properties_controller,
        enter_spawn_mode=_m("enter_spawn_mode"),
        exit_spawn_mode=_m("exit_spawn_mode"),
        enter_delete_mode=_m("enter_delete_mode"),
        exit_delete_mode=_m("exit_delete_mode"),
        open_new_monster_properties=_m("open_new_monster_properties"),
        enter_add_entities_on_system_mode=_m("enter_add_entities_on_system_mode"),
        exit_add_entities_on_system_mode=_m("exit_add_entities_on_system_mode"),
    )
    return controller, calls


def test_click_add_entitie_toggles_spawn_mode():
    model = EntitiesAddRemovePanelModel()
    controller, calls = _make_controller_stub()
    handler = EntitiesAddRemovePanelEventHandler(controller, model)

    # First click enters spawn mode
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONUP, button=1, pos=(5, 5))
    consumed = handler.handle_event(ev)
    assert consumed is True
    assert model.active_tool == "add_entitie"
    assert calls["enter_spawn_mode"] == 1

    # Mark controller as in spawn mode and click again -> exit
    controller.model.spawn_mode_active = True
    consumed = handler.handle_event(ev)
    assert consumed is True
    assert calls["exit_spawn_mode"] == 1


def test_click_remove_entitie_toggles_delete_mode():
    model = EntitiesAddRemovePanelModel()
    controller, calls = _make_controller_stub()
    handler = EntitiesAddRemovePanelEventHandler(controller, model)

    # Click on remove icon area
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONUP, button=1, pos=(35, 5))
    consumed = handler.handle_event(ev)
    assert consumed is True
    assert model.active_tool == "remove_entitie"
    assert calls["enter_delete_mode"] == 1

    # Second click while delete mode active should exit
    controller.model.delete_mode_active = True
    consumed = handler.handle_event(ev)
    assert consumed is True
    assert calls["exit_delete_mode"] == 1


def test_click_add_entities_on_system_opens_properties_and_sets_flag():
    model = EntitiesAddRemovePanelModel()
    controller, calls = _make_controller_stub()
    handler = EntitiesAddRemovePanelEventHandler(controller, model)

    ev = SimpleNamespace(type=pygame.MOUSEBUTTONUP, button=1, pos=(65, 5))
    consumed = handler.handle_event(ev)
    assert consumed is True
    assert model.active_tool == ADD_ENTITIES_ON_SYSTEM
    assert controller.properties_controller.model.show_add_system_selector is True
    assert calls["open_new_monster_properties"] == 1
    assert calls["enter_add_entities_on_system_mode"] == 1

    # Clicking again should close the mode
    consumed = handler.handle_event(ev)
    assert consumed is True
    assert model.active_tool is None
    assert controller.properties_controller.model.show_add_system_selector is False
    assert calls["exit_add_entities_on_system_mode"] == 1
