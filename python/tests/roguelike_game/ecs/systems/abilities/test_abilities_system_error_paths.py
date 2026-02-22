from types import SimpleNamespace
import os
import json
import pytest

from roguelike_game.ecs.systems.abilities.combo_system import ComboSystem


def test_combo_system_handles_missing_rules_file_gracefully(monkeypatch, tmp_path):
    sys_under_test = ComboSystem()
    world = SimpleNamespace(components={})

    # Simular que getmtime falla y que no hay archivo
    monkeypatch.setattr(os.path, "getmtime", lambda p: (_ for _ in ()).throw(FileNotFoundError()))
    # También hacer que open falle si se llegara (defensivo)
    monkeypatch.setattr("builtins.open", lambda *a, **k: (_ for _ in ()).throw(FileNotFoundError()))

    # No debe lanzar excepción
    sys_under_test.update(world)


def test_combo_system_clears_queue_even_with_invalid_events():
    sys_under_test = ComboSystem()
    bad_events = [{"foo": "bar"}, {"type": "kill", "entity": None}]
    world = SimpleNamespace(components={
        "ComboEventQueue": list(bad_events),
        "ComboCounterComponent": {},
    })
    # No debe lanzar y debe drenar la cola
    sys_under_test.update(world)
    assert world.components.get("ComboEventQueue") == []
