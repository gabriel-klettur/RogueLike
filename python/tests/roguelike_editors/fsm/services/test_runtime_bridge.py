from __future__ import annotations

from typing import Dict, Any

from roguelike_editors.fsm.services import fsm_runtime_bridge as fbr


def _make_cached(sets_doc: Dict[str, Any]) -> fbr._Cached:
    by_id = {s["id"]: s for s in sets_doc.get("sets", [])}
    return fbr._Cached(
        sets=sets_doc,
        assignments={"by_archetype": {}, "by_eid": {}},
        by_id=by_id,
        anim_map={"default": {}, "overrides": {}},
    )


def test_build_fsm_from_set_attaches_anim_map_and_mappings(monkeypatch):
    sets_doc = {
        "sets": [
            {
                "id": "TestSet",
                "label": "Test Set",
                "initial": "Idle",
                "states": [
                    {"id": "Idle", "label": "Idle", "class": "IdleState"},
                ],
                "transitions": [],
            }
        ]
    }
    cache = _make_cached(sets_doc)
    # Provide default + override anim map
    cache.anim_map = {
        "default": {"IdleState": "idle"},
        "overrides": {"TestSet": {"IdleState": "idle_override"}},
    }
    monkeypatch.setattr(fbr, "_CACHE", cache, raising=True)

    set_def = sets_doc["sets"][0]
    fsm, initial = fbr.build_fsm_from_set(set_def)

    assert initial == "Idle"
    # Resolved anim map = default merged with override
    assert fsm.context.get("anim_map", {}).get("IdleState") == "idle_override"
    # Mappings
    assert fsm.context.get("id_to_class", {}).get("Idle") == "IdleState"
    assert fsm.context.get("class_to_id", {}).get("IdleState") == "Idle"


def test_build_fsm_for_archetype_uses_assignments(monkeypatch):
    sets_doc = {
        "sets": [
            {
                "id": "TestSet",
                "initial": "Idle",
                "states": [
                    {"id": "Idle", "class": "IdleState"},
                ],
                "transitions": [],
            }
        ]
    }
    cache = _make_cached(sets_doc)
    cache.assignments = {"by_archetype": {"player": "TestSet"}, "by_eid": {}}
    monkeypatch.setattr(fbr, "_CACHE", cache, raising=True)

    res = fbr.build_fsm_for_archetype("player")
    assert res is not None
    fsm, initial = res
    assert initial == "Idle"
    assert fsm.context.get("set_id") == "TestSet"


def test_monster_policy_damage_next_class_fallback(monkeypatch):
    # No explicit Damage transition; fallback should choose PatrolState if present
    sets_doc = {
        "sets": [
            {
                "id": "Monster_Goblin",
                "initial": "Idle",
                "states": [
                    {"id": "Idle", "class": "IdleState"},
                    {"id": "Patrol", "class": "PatrolState"},
                ],
                "transitions": [],
            }
        ]
    }
    cache = _make_cached(sets_doc)
    monkeypatch.setattr(fbr, "_CACHE", cache, raising=True)

    fsm, _ = fbr.build_fsm_from_set(sets_doc["sets"][0])
    # Allowed classes configured for Monster_* sets
    allowed = fsm.context.get("allowed_state_classes")
    assert isinstance(allowed, set) and "PatrolState" in allowed
    # Fallback damage_next_class should be PatrolState
    assert fsm.context.get("damage_next_class") == "PatrolState"
