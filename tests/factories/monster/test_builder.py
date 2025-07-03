import pytest
from collections import defaultdict
from roguelike_game.factories.monster.builder import MonsterBuilder
import roguelike_game.factories.monster.builder as builder_mod
import roguelike_game.factories.monster.config as config_mod

class DummyWorld:
    def __init__(self):
        self.components = defaultdict(dict)
        self.entities = []
        self.next_entity_id = 1
    def create_entity(self):
        eid = self.next_entity_id
        self.next_entity_id += 1
        self.entities.append(eid)
        return eid

@pytest.fixture(autouse=True)
def patch_dependencies(monkeypatch):
    monkeypatch.setattr(builder_mod, "_load_caches_once", lambda: None)
    monkeypatch.setattr(builder_mod, "create_sprite_component", lambda mt: ("sprite", None))
    monkeypatch.setattr(builder_mod, "create_patrol_components", lambda x,y,mt,cfg: ("patrol", "move", "anim"))
    monkeypatch.setattr(builder_mod, "create_physics_components", lambda cfg: ("scale", "vel"))
    monkeypatch.setattr(builder_mod, "create_collider_components", lambda sprite, cfg: "collider")
    monkeypatch.setattr(builder_mod, "create_zlayer_component", lambda cfg: "zlayer")
    config_mod.MONSTER_DEFS["dummy"] = {
        "hp": 5, "power": 1, "defense": 1,
        "melee_damage": 2, "melee_cooldown": 1,
        "aggro_range": 3, "melee_range": 2,
        "damage_duration": 1, "faction": "ENEMY"
    }

def test_builder_build():
    dw = DummyWorld()
    builder = MonsterBuilder(dw)
    eid = builder.build(7, 8, "dummy")
    assert eid == 1
    comps = dw.components
    for comp in [
        "Sprite", "Patrol", "MovementSpeed", "Animator",
        "Scale", "Velocity", "MultiCollider", "ZLayer",
        "Health", "Identity", "CombatStats", "MeleeWeapon",
        "AggroRange", "MeleeRange", "DamageConfig",
        "PatrolRoute", "NPCState"
    ]:
        assert eid in comps[comp]
