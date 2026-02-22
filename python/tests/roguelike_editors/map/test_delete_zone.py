import json
from pathlib import Path
from types import SimpleNamespace
import pytest

from roguelike_engine.config import config as engine_config
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.zone.zone_controller import ZonesService
from roguelike_engine.zone import zone_controller as zc
from roguelike_editors.map.map_editor_state import MapEditorState
from roguelike_editors.map.map_editor_controller import MapEditorController
from roguelike_editors.map.map_tool_bar_panel.delete_zone.delete_zone_model import DeleteZoneModel
from roguelike_editors.map.map_tool_bar_panel.delete_zone.delete_zone_events import DeleteZoneEvents


class _FakeRect:
    def __init__(self, x=0, y=0, w=10, h=10):
        self.x, self.y, self.w, self.h = x, y, w, h

    def collidepoint(self, pos):
        px, py = pos
        return (self.x <= px < self.x + self.w) and (self.y <= py < self.y + self.h)


class _FakeLoader:
    def __init__(self, cache_dir: Path):
        self.cache_dir = cache_dir


class _FakeMapManager:
    def __init__(self, map_name: str, cache_dir: Path):
        self.map_name = map_name
        self.loader = _FakeLoader(cache_dir)
        self.reload_called = False

    def reload_map(self):
        self.reload_called = True
        return self


@pytest.fixture()
def temp_data_layout(tmp_path: Path, monkeypatch):
    # Redirect DATA_DIR to tmp
    data_dir = tmp_path / "data"
    (data_dir / "map" / "zones").mkdir(parents=True, exist_ok=True)
    (data_dir / "map" / "zones" / "overlays").mkdir(parents=True, exist_ok=True)
    (data_dir / "map" / "collisions").mkdir(parents=True, exist_ok=True)

    # Patch engine DATA_DIR string
    monkeypatch.setattr(engine_config, "DATA_DIR", str(data_dir), raising=True)
    # Patch zone_controller DATA_DIR used by ZonesService helpers
    monkeypatch.setattr(zc, "DATA_DIR", str(data_dir), raising=True)

    # Point global_map_settings to the temp zones.json and force JSON mode
    zones_json = data_dir / "map" / "zones" / "zones.json"
    prev_use_json = global_map_settings.use_zones_json
    prev_index = global_map_settings.ZONES_INDEX
    global_map_settings.use_zones_json = True
    global_map_settings.ZONES_INDEX = zones_json
    # Clear cached zone_offsets to recompute on access
    global_map_settings.__dict__.pop("zone_offsets", None)

    ctx = SimpleNamespace(data_dir=data_dir, zones_json=zones_json)

    yield ctx

    # Restore globals
    global_map_settings.use_zones_json = prev_use_json
    global_map_settings.ZONES_INDEX = prev_index
    global_map_settings.__dict__.pop("zone_offsets", None)


def _write_zones(zones_json: Path, mapping: dict):
    zones_json.write_text(json.dumps(mapping, indent=2), encoding="utf-8")


def test_delete_zone_updates_persistence_cache_and_memory(tmp_path: Path, temp_data_layout, monkeypatch):
    # Arrange zones and files
    zones = {
        "lobby": [0, 0],
        "z1": [50, 0],
    }
    _write_zones(temp_data_layout.zones_json, zones)

    # Create associated files to be deleted
    overlay = temp_data_layout.data_dir / "map" / "zones" / "overlays" / "z1.overlay.json"
    collisions = temp_data_layout.data_dir / "map" / "collisions" / "z1.json"
    overlay.write_text("{}", encoding="utf-8")
    collisions.write_text("{}", encoding="utf-8")

    # Prepare cache file
    cache_dir = tmp_path / "cache"
    cache_dir.mkdir(parents=True, exist_ok=True)
    cache_file = cache_dir / "map_global_map.pkl"
    cache_file.write_bytes(b"cache")

    # Build controller without running heavy __init__ logic
    state = MapEditorState()
    state.selected_zone = "z1"
    fake_map = _FakeMapManager("global_map", cache_dir)

    controller = MapEditorController.__new__(MapEditorController)
    controller.state = state
    controller.map_manager = fake_map
    controller.zones = ZonesService()

    # Act
    ok = controller.delete_zone()

    # Assert persistence
    assert ok is True
    new_data = json.loads(temp_data_layout.zones_json.read_text(encoding="utf-8"))
    assert "z1" not in new_data
    # Files removed
    assert not overlay.exists()
    assert not collisions.exists()
    # Cache invalidated
    assert not cache_file.exists()
    # In-memory side-effects
    assert fake_map.reload_called is True
    assert state.selected_zone is None


def test_delete_zone_guard_on_lobby_no_side_effects(tmp_path: Path, temp_data_layout):
    zones = {
        "lobby": [0, 0],
        "z1": [50, 0],
    }
    _write_zones(temp_data_layout.zones_json, zones)

    cache_dir = tmp_path / "cache"
    cache_dir.mkdir(parents=True, exist_ok=True)
    cache_file = cache_dir / "map_global_map.pkl"
    cache_file.write_bytes(b"cache")

    state = MapEditorState()
    state.selected_zone = "lobby"
    fake_map = _FakeMapManager("global_map", cache_dir)

    controller = MapEditorController.__new__(MapEditorController)
    controller.state = state
    controller.map_manager = fake_map
    controller.zones = ZonesService()

    ok = controller.delete_zone()

    assert ok is False
    # zones.json unchanged
    data_after = json.loads(temp_data_layout.zones_json.read_text(encoding="utf-8"))
    assert data_after == zones
    # cache unchanged
    assert cache_file.exists()
    # reload not called
    assert fake_map.reload_called is False
    # selection not cleared
    assert state.selected_zone == "lobby"


def test_delete_zone_nonexistent_zone_returns_false(tmp_path: Path, temp_data_layout):
    zones = {
        "lobby": [0, 0],
    }
    _write_zones(temp_data_layout.zones_json, zones)

    cache_dir = tmp_path / "cache"
    cache_dir.mkdir(parents=True, exist_ok=True)
    cache_file = cache_dir / "map_global_map.pkl"
    cache_file.write_bytes(b"cache")

    state = MapEditorState()
    state.selected_zone = "does_not_exist"
    fake_map = _FakeMapManager("global_map", cache_dir)

    controller = MapEditorController.__new__(MapEditorController)
    controller.state = state
    controller.map_manager = fake_map
    controller.zones = ZonesService()

    ok = controller.delete_zone()

    assert ok is False
    # zones.json unchanged
    data_after = json.loads(temp_data_layout.zones_json.read_text(encoding="utf-8"))
    assert data_after == zones
    # cache unchanged
    assert cache_file.exists()
    # reload not called
    assert fake_map.reload_called is False
    # selection stays
    assert state.selected_zone == "does_not_exist"


def test_delete_zone_events_confirm_flow_triggers_controller(tmp_path: Path, temp_data_layout):
    # zones json with target zone
    zones = {
        "lobby": [0, 0],
        "z1": [50, 0],
    }
    _write_zones(temp_data_layout.zones_json, zones)

    cache_dir = tmp_path / "cache"
    cache_dir.mkdir(parents=True, exist_ok=True)

    state = MapEditorState()
    # prime delete dialog state
    state.confirm_delete_zone = True
    state.pending_delete_zone = "z1"
    state.confirm_yes_rect = _FakeRect(0, 0, 10, 10)
    state.confirm_no_rect = _FakeRect(100, 100, 10, 10)

    fake_map = _FakeMapManager("global_map", cache_dir)
    controller = MapEditorController.__new__(MapEditorController)
    controller.state = state
    controller.map_manager = fake_map
    controller.zones = ZonesService()

    model = DeleteZoneModel(state)
    # Build a minimal DeleteZoneController-like proxy with required attrs
    fake_delete_ctrl = SimpleNamespace(editor=state, map_controller=controller)
    events = DeleteZoneEvents(fake_delete_ctrl, model)

    # Click YES inside confirm_yes_rect
    handled = events.handle_confirm_click((5, 5))

    assert handled is True
    # Dialog reset
    assert state.confirm_delete_zone is False
    assert state.pending_delete_zone is None
    # Controller executed deletion and called reload
    assert fake_map.reload_called is True
