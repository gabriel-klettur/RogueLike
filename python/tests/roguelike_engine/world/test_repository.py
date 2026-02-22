import json
from pathlib import Path
from datetime import datetime

import pytest

from roguelike_engine.world.repository import JSONWorldRepository
from roguelike_engine.world.models import WorldSnapshot


def make_snapshot(name: str = "partida_test", with_meta_ts: bool = True) -> WorldSnapshot:
    meta = {"name": name}
    if with_meta_ts:
        # Timestamps en formato ISO para index legible
        now = datetime.now().isoformat(timespec="seconds")
        meta["created_at"] = now
        meta["last_played"] = now
    return WorldSnapshot(
        version=1,
        player={"level": "lobby", "pos": [1, 2]},
        npcs={"npc1": {"level": "lobby"}},
        levels={"lobby": {"state": 123}},
        player_inventory={"capacity": 20, "player_id": "p1", "slots": []},
        npc_inventories={"npc1": {"slots": []}},
        meta=meta,
    )


def test_save_and_load_roundtrip(tmp_path: Path):
    repo = JSONWorldRepository()
    save_path = tmp_path / "partida_1.json"
    snap = make_snapshot()

    # Guardar
    repo.save_to_path(str(save_path), snap)
    assert save_path.exists()
    assert save_path.stat().st_size > 0

    # Cargar
    data = repo.load_from_path(str(save_path))
    assert isinstance(data, dict)
    assert data.get("version") == 1
    assert data.get("player", {}).get("level") == "lobby"
    assert (data.get("meta") or {}).get("name") == "partida_test"

    # Índice generado
    idx_path = repo.get_index_path(tmp_path)
    assert idx_path.exists()
    idx = json.loads(idx_path.read_text(encoding="utf-8"))
    assert isinstance(idx.get("slots"), list)
    assert any(s.get("path") == str(save_path) for s in idx.get("slots", []))


def test_create_new_slot_and_current_path(tmp_path: Path):
    repo = JSONWorldRepository()
    snap = make_snapshot("slot_A")
    path = repo.create_new_slot(tmp_path, "partida_A.json", snap)

    assert Path(path).exists()
    # current_path en índice
    cur = repo.get_current_path(tmp_path)
    assert cur == path

    # set_current_path cambia el slot activo
    other = tmp_path / "partida_B.json"
    repo.save_to_path(str(other), make_snapshot("slot_B"))
    repo.set_current_path(tmp_path, str(other))
    assert repo.get_current_path(tmp_path) == str(other)


def test_list_slots_from_index_and_legacy(tmp_path: Path):
    repo = JSONWorldRepository()
    # Caso con índice
    a = tmp_path / "partida_A.json"
    repo.save_to_path(str(a), make_snapshot("A"))
    slots = repo.list_slots(tmp_path)
    assert any(s.path == a for s in slots)

    # Caso fallback legacy: borrar índice y dejar solo un archivo legacy
    idx = repo.get_index_path(tmp_path)
    if idx.exists():
        idx.unlink()
    # Crear un archivo legacy a mano
    legacy = tmp_path / "partida_legacy.json"
    legacy.write_text(json.dumps({"player": {"level": "lobby"}, "version": 1}), encoding="utf-8")

    legacy_slots = repo.list_slots(tmp_path)
    assert any(s.path == legacy for s in legacy_slots)


def test_set_get_current_path(tmp_path: Path):
    repo = JSONWorldRepository()
    a = tmp_path / "partida_A.json"
    b = tmp_path / "partida_B.json"
    repo.save_to_path(str(a), make_snapshot("A"))
    repo.save_to_path(str(b), make_snapshot("B"))

    repo.set_current_path(tmp_path, str(a))
    assert repo.get_current_path(tmp_path) == str(a)

    repo.set_current_path(tmp_path, str(b))
    assert repo.get_current_path(tmp_path) == str(b)


def test_rename_meta_name_via_resave(tmp_path: Path):
    repo = JSONWorldRepository()
    p = tmp_path / "partida_A.json"
    repo.save_to_path(str(p), make_snapshot("Original"))

    # Cargar, cambiar meta.name y guardar como snapshot nuevo
    data = repo.load_from_path(str(p))
    data.setdefault("meta", {})
    data["meta"]["name"] = "NuevoNombre"
    snap2 = WorldSnapshot(
        version=data.get("version", 1),
        player=data.get("player"),
        npcs=data.get("npcs", {}),
        levels=data.get("levels", {}),
        player_inventory=data.get("player_inventory"),
        npc_inventories=data.get("npc_inventories"),
        meta=data.get("meta"),
    )
    repo.save_to_path(str(p), snap2)

    again = repo.load_from_path(str(p))
    assert (again.get("meta") or {}).get("name") == "NuevoNombre"
