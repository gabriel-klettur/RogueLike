from __future__ import annotations

import types

import pytest

from roguelike_game.managers.ecs import particles_loader as pl
from roguelike_engine.config.map_config import global_map_settings


class DummyWorld:
    def __init__(self) -> None:
        self.components: dict[str, dict[int, object]] = {}
        self._next_id: int = 1

    def create_entity(self) -> int:
        eid = self._next_id
        self._next_id += 1
        # Position will be filled by spawn_particles_from_instances
        return eid

    def remove_entity(self, eid: int) -> None:
        for store in self.components.values():
            if isinstance(store, dict):
                store.pop(eid, None)


def test_spawn_particles_skips_instances_for_unknown_zones(monkeypatch: pytest.MonkeyPatch) -> None:
    """Solo deben spawnearse instancias cuya zona exista en zone_offsets.

    Cuando use_zones_json=True, las instancias con "zone" no presente en
    global_map_settings.zone_offsets deben ser ignoradas.
    """

    # Stub de carga de instancias para controlar zonas
    monkeypatch.setattr(
        pl,
        "load_particles_instances",
        lambda: [
            {"preset_id": "p1", "zone": "zone_0_0", "rel_x": 0, "rel_y": 0},
            {"preset_id": "p2", "zone": "zone_999_999", "rel_x": 0, "rel_y": 0},
        ],
        raising=True,
    )

    # Guardar estado previo de zone_offsets y use_zones_json
    old_offsets = dict(getattr(global_map_settings, "zone_offsets", {}) or {})
    old_use_zones = getattr(global_map_settings, "use_zones_json", False)
    try:
        # Solo definimos una zona válida
        global_map_settings.zone_offsets = {"zone_0_0": (0, 0)}
        global_map_settings.use_zones_json = True

        world = DummyWorld()
        spawned = pl.spawn_particles_from_instances(world)

        # Debe haberse spawneado solo una instancia (para zone_0_0)
        assert spawned == 1
        presets = world.components.get("ParticlePresetComponent", {})
        assert len(presets) == 1
    finally:
        global_map_settings.zone_offsets = old_offsets
        global_map_settings.use_zones_json = old_use_zones


def test_refresh_particles_clears_existing_presets(monkeypatch: pytest.MonkeyPatch) -> None:
    """refresh_particles_from_world borra presets existentes antes de respawnear.

    Verificamos que:
    - Los eids previos desaparecen de ParticlePresetComponent.
    - spawn_particles_from_instances se invoca exactamente una vez.
    """

    world = DummyWorld()
    world.components["ParticlePresetComponent"] = {1: object(), 2: object()}

    calls = {"n": 0}

    def fake_spawn(w) -> int:  # noqa: ANN001
        calls["n"] += 1
        # Simular que se crean 3 instancias nuevas
        return 3

    monkeypatch.setattr(pl, "spawn_particles_from_instances", fake_spawn, raising=True)

    spawned = pl.refresh_particles_from_world(world)

    assert calls["n"] == 1
    assert spawned == 3
    assert world.components.get("ParticlePresetComponent", {}) == {}
