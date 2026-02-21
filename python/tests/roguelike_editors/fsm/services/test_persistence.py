from __future__ import annotations

import json
from pathlib import Path

import pytest

from roguelike_editors.fsm.services import fsm_persistence as p
from tests.roguelike_editors.fsm.fixtures.fsm_sets_minimal import make_minimal_sets_doc
from tests.roguelike_editors.fsm.fixtures.layouts_samples import make_minimal_layouts_doc


def test_save_sets_roundtrip_and_normalization(tmp_path: Path, monkeypatch: pytest.MonkeyPatch):
    # Prevent codegen side-effect writing into repo
    monkeypatch.setattr(p, "_generate_code_ids", lambda data: None, raising=True)

    sets_doc = make_minimal_sets_doc()
    sets_path = tmp_path / "sets.json"

    # Save (will normalize, validate if schema present, and codegen is no-op)
    warns, errs = p.save_sets(sets_doc, sets_path)
    assert isinstance(warns, list) and isinstance(errs, list)
    # Load back
    loaded = p.load_sets(sets_path)

    # Basic invariants
    assert loaded["version"] >= 1
    assert isinstance(loaded["sets"], list) and len(loaded["sets"]) == 1
    s0 = loaded["sets"][0]
    # Normalization ensures props exists
    idle = next(st for st in s0["states"] if st["id"] == "Idle")
    assert "props" in idle and isinstance(idle["props"], dict)
    # AUTO_INCLUDE_DAMAGE may add a Damage state; tolerate presence
    all_ids = {st["id"] for st in s0["states"]}
    assert "Idle" in all_ids
    # initial must reference a state id
    assert s0.get("initial") in all_ids


def test_layouts_roundtrip(tmp_path: Path):
    data = make_minimal_layouts_doc()
    path = tmp_path / "layouts.json"
    p.save_layouts(data, path)
    loaded = p.load_layouts(path)
    assert loaded == data


def test_animation_map_roundtrip(tmp_path: Path):
    data = {
        "default": {"IdleState": "idle"},
        "overrides": {"TestSet": {"IdleState": "idle_set"}},
    }
    path = tmp_path / "animation_map.json"
    p.save_animation_map(data, path)
    loaded = p.load_animation_map(path)
    assert loaded == data
