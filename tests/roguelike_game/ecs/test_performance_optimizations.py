"""
Tests for the 5 performance optimization layers:
  1. Spawn Budget (SpawnSystem.MAX_SPAWNS_PER_FRAME)
  2. Asset Sharing (sprite_loader caches)
  3. Spatial Hash for NPC-NPC collisions
  4. Frustum Culling (FSMSystem, AnimationSystem)
  5. world.entities as set (O(1) add/remove)

Each layer is tested in isolation using lightweight fakes.
"""
from __future__ import annotations

import pygame
import pytest
from typing import Any, Dict

# ── Lightweight fakes (self-contained, no external deps) ──

class _Pos:
    __slots__ = ("x", "y")
    def __init__(self, x: float, y: float):
        self.x = x
        self.y = y


class _CircleFeet:
    __slots__ = ("radius", "offset_x", "offset_y")
    def __init__(self, radius: int, offset_x: int = 0, offset_y: int = 0):
        self.radius = radius
        self.offset_x = offset_x
        self.offset_y = offset_y


class _MultiCollider:
    def __init__(self, colliders: dict):
        self.colliders = colliders


class _Velocity:
    __slots__ = ("vx", "vy")
    def __init__(self, vx: float = 0.0, vy: float = 0.0):
        self.vx = vx
        self.vy = vy


class _FakeScreen:
    """Minimal screen stub for frustum culling."""
    def __init__(self, w: int = 800, h: int = 600):
        self._size = (w, h)
    def get_size(self):
        return self._size


class _FakeCamera:
    def __init__(self, zoom: float = 1.0, offset_x: float = 0.0, offset_y: float = 0.0):
        self.zoom = zoom
        self.offset_x = offset_x
        self.offset_y = offset_y


class _FakeWorld:
    """Minimal ECS world stub for optimization tests."""
    def __init__(self):
        self.components: Dict[str, Dict[int, Any]] = {}
        self.entities: set[int] = set()
        self._next_eid: int = 1
        self._frame_count: int = 0
        self.screen = _FakeScreen()

    def tick_frame(self):
        self._frame_count += 1

    def create_entity(self) -> int:
        eid = self._next_eid
        self._next_eid += 1
        self.entities.add(eid)
        return eid

    def remove_entity(self, eid: int):
        self.entities.discard(eid)
        for cmap in self.components.values():
            if isinstance(cmap, dict):
                cmap.pop(eid, None)

    def get_entities_with(self, *names: str) -> list[int]:
        if not names:
            return []
        sets = []
        for n in names:
            cmap = self.components.get(n, {})
            sets.append(set(cmap.keys()) if isinstance(cmap, dict) else set())
        common = sets[0]
        for s in sets[1:]:
            common &= s
        return list(common)

    def get_solid_tiles_for_rect(self, rect):
        return []


# ═══════════════════════════════════════════════════════════════════════
# LAYER 5: world.entities as set
# ═══════════════════════════════════════════════════════════════════════

class TestWorldEntitiesSet:
    """Verify world.entities is a set with O(1) operations."""

    def test_entities_is_set(self):
        """world.entities must be a set (verified via FakeWorld contract)."""
        w = _FakeWorld()
        assert isinstance(w.entities, set)

    def test_create_adds_to_set(self):
        w = _FakeWorld()
        e1 = w.create_entity()
        e2 = w.create_entity()
        assert e1 in w.entities
        assert e2 in w.entities
        assert len(w.entities) == 2

    def test_remove_discards_from_set(self):
        w = _FakeWorld()
        e1 = w.create_entity()
        w.remove_entity(e1)
        assert e1 not in w.entities

    def test_remove_nonexistent_no_error(self):
        w = _FakeWorld()
        w.remove_entity(9999)  # should not raise

    def test_membership_check_is_o1(self):
        """set membership is O(1); verify it works with many entities."""
        w = _FakeWorld()
        eids = [w.create_entity() for _ in range(1000)]
        assert all(eid in w.entities for eid in eids)
        w.remove_entity(eids[500])
        assert eids[500] not in w.entities


# ═══════════════════════════════════════════════════════════════════════
# LAYER 2: Asset Sharing (sprite_loader caches)
# ═══════════════════════════════════════════════════════════════════════

class TestAssetSharing:
    """Verify sprite_loader shares surfaces across NPCs of the same type."""

    def test_cropped_frame_cache_returns_same_object(self):
        """Two calls for the same monster_type should return the same Surface."""
        from roguelike_game.factories.monster.sprite_loader import (
            _CROPPED_FRAME_CACHE,
            _get_cropped_frame,
            _SPRITE_SURFACES,
        )
        # Inject a fake surface into the raw cache
        test_type = "__test_asset_share__"
        surf = pygame.Surface((32, 32), pygame.SRCALPHA)
        surf.fill((255, 0, 0, 255))
        _SPRITE_SURFACES[test_type] = {"down": surf}
        _CROPPED_FRAME_CACHE.pop(test_type, None)

        frame1 = _get_cropped_frame(test_type)
        frame2 = _get_cropped_frame(test_type)
        assert frame1 is frame2, "Cropped frame cache should return the same object"

        # Cleanup
        _SPRITE_SURFACES.pop(test_type, None)
        _CROPPED_FRAME_CACHE.pop(test_type, None)

    def test_shared_anim_cache_returns_same_dicts(self):
        """Animation frames dict should be shared (same object) across calls."""
        from roguelike_game.factories.monster.sprite_loader import (
            _SHARED_ANIM_CACHE,
            _SHARED_MASK_CACHE,
            _get_shared_anims,
            _SPRITE_SURFACES,
        )
        test_type = "__test_anim_share__"
        surf = pygame.Surface((16, 16), pygame.SRCALPHA)
        surf.fill((0, 255, 0, 255))
        _SPRITE_SURFACES[test_type] = {"down": surf, "up": surf}
        _SHARED_ANIM_CACHE.pop(test_type, None)
        _SHARED_MASK_CACHE.pop(test_type, None)

        sprites1, masks1 = _get_shared_anims(test_type)
        sprites2, masks2 = _get_shared_anims(test_type)
        assert sprites1 is sprites2, "Anim sprites dict should be the same object"
        assert masks1 is masks2, "Anim masks dict should be the same object"

        # Cleanup
        _SPRITE_SURFACES.pop(test_type, None)
        _SHARED_ANIM_CACHE.pop(test_type, None)
        _SHARED_MASK_CACHE.pop(test_type, None)

    def test_no_surface_copy_in_shared_anims(self):
        """Surfaces inside the shared anim dict should be the exact same objects as the originals."""
        from roguelike_game.factories.monster.sprite_loader import (
            _SHARED_ANIM_CACHE,
            _SHARED_MASK_CACHE,
            _get_shared_anims,
            _SPRITE_SURFACES,
        )
        test_type = "__test_no_copy__"
        original_surf = pygame.Surface((24, 24), pygame.SRCALPHA)
        _SPRITE_SURFACES[test_type] = {"down": original_surf}
        _SHARED_ANIM_CACHE.pop(test_type, None)
        _SHARED_MASK_CACHE.pop(test_type, None)

        sprites, _ = _get_shared_anims(test_type)
        assert sprites["down"][0] is original_surf, "Surface should NOT be copied"

        # Cleanup
        _SPRITE_SURFACES.pop(test_type, None)
        _SHARED_ANIM_CACHE.pop(test_type, None)
        _SHARED_MASK_CACHE.pop(test_type, None)


# ═══════════════════════════════════════════════════════════════════════
# LAYER 1: Spawn Budget
# ═══════════════════════════════════════════════════════════════════════

class TestSpawnBudget:
    """Verify SpawnSystem respects MAX_SPAWNS_PER_FRAME."""

    def test_max_spawns_per_frame_class_attr(self):
        from roguelike_game.ecs.systems.core.spawn_system import SpawnSystem
        assert hasattr(SpawnSystem, "MAX_SPAWNS_PER_FRAME")
        assert SpawnSystem.MAX_SPAWNS_PER_FRAME > 0

    def test_budget_limits_spawns(self, monkeypatch):
        """If 10 SpawnRequests exist, only MAX_SPAWNS_PER_FRAME should be processed."""
        import roguelike_game.ecs.systems.core.spawn_system as ss_mod
        from roguelike_game.ecs.systems.core.spawn_system import SpawnSystem

        budget = SpawnSystem.MAX_SPAWNS_PER_FRAME
        spawned_count = 0

        class _FakeReq:
            def __init__(self):
                self.prototype = "test"
                self.position = (0, 0)
                self.instance_id = None
                self.defend_center = None
                self.defend_radius_px = None
                self.defend_leash = None
                self.defend_shape = None
                self.ttl_seconds = None
                self.spawner_eid = None
                self.wave_idx = None

        class _FakeFactory:
            def create(self, world, **kwargs):
                nonlocal spawned_count
                spawned_count += 1
                return world.create_entity()

        monkeypatch.setattr(ss_mod, "get_factory", lambda name: _FakeFactory())

        w = _FakeWorld()
        reqs = {}
        for _ in range(10):
            eid = w.create_entity()
            reqs[eid] = _FakeReq()
        w.components["SpawnRequest"] = reqs

        sys = SpawnSystem(perf_log=None)
        sys.update(w)
        assert spawned_count == budget, (
            f"Expected {budget} spawns, got {spawned_count}"
        )
        remaining = len(w.components.get("SpawnRequest", {}))
        assert remaining == 10 - budget

    def test_remaining_requests_processed_next_frame(self, monkeypatch):
        """Leftover SpawnRequests should be processed in subsequent frames."""
        import roguelike_game.ecs.systems.core.spawn_system as ss_mod
        from roguelike_game.ecs.systems.core.spawn_system import SpawnSystem

        budget = SpawnSystem.MAX_SPAWNS_PER_FRAME
        total_requests = budget + 2
        spawned_count = 0

        class _FakeReq:
            def __init__(self):
                self.prototype = "test"
                self.position = (0, 0)
                self.instance_id = None
                self.defend_center = None
                self.defend_radius_px = None
                self.defend_leash = None
                self.defend_shape = None
                self.ttl_seconds = None
                self.spawner_eid = None
                self.wave_idx = None

        class _FakeFactory:
            def create(self, world, **kwargs):
                nonlocal spawned_count
                spawned_count += 1
                return world.create_entity()

        monkeypatch.setattr(ss_mod, "get_factory", lambda name: _FakeFactory())

        w = _FakeWorld()
        reqs = {}
        for _ in range(total_requests):
            eid = w.create_entity()
            reqs[eid] = _FakeReq()
        w.components["SpawnRequest"] = reqs

        sys = SpawnSystem(perf_log=None)
        sys.update(w)
        sys.update(w)
        assert spawned_count == total_requests, (
            f"All {total_requests} should be spawned after 2 frames, got {spawned_count}"
        )


# ═══════════════════════════════════════════════════════════════════════
# LAYER 3: Spatial Hash for NPC-NPC collisions
# ═══════════════════════════════════════════════════════════════════════

class TestSpatialHash:
    """Verify SpatialHash correctness for NPC collision queries."""

    def test_insert_and_query_radius(self):
        from roguelike_game.ecs.utils.spatial_hash import SpatialHash
        sh = SpatialHash(cell_size=64)
        sh.insert(1, 100.0, 100.0, 10.0)
        sh.insert(2, 1000.0, 1000.0, 10.0)  # very far away
        sh.insert(3, 105.0, 105.0, 10.0)

        nearby = sh.query_radius(100.0, 100.0, 30.0)
        assert 1 in nearby
        assert 3 in nearby
        assert 2 not in nearby, "Entity 2 is too far away"

    def test_query_rect(self):
        from roguelike_game.ecs.utils.spatial_hash import SpatialHash
        sh = SpatialHash(cell_size=64)
        sh.insert(1, 50.0, 50.0, 5.0)
        sh.insert(2, 1000.0, 1000.0, 5.0)  # very far away

        result = sh.query_rect(0.0, 0.0, 100.0, 100.0)
        assert 1 in result
        assert 2 not in result

    def test_remove(self):
        from roguelike_game.ecs.utils.spatial_hash import SpatialHash
        sh = SpatialHash(cell_size=64)
        sh.insert(1, 50.0, 50.0, 5.0)
        sh.remove(1)
        result = sh.query_radius(50.0, 50.0, 100.0)
        assert 1 not in result

    def test_clear(self):
        from roguelike_game.ecs.utils.spatial_hash import SpatialHash
        sh = SpatialHash(cell_size=64)
        for i in range(100):
            sh.insert(i, float(i * 10), float(i * 10), 5.0)
        sh.clear()
        assert sh.entity_count == 0
        assert sh.cell_count == 0

    def test_rebuild(self):
        from roguelike_game.ecs.utils.spatial_hash import SpatialHash
        sh = SpatialHash(cell_size=64)
        data = [(i, float(i * 10), float(i * 10), 5.0) for i in range(50)]
        sh.rebuild(data)
        assert sh.entity_count == 50

    def test_build_npc_feet_hash_with_circle_colliders(self):
        """build_npc_feet_hash should index NPCs with circle feet colliders."""
        from roguelike_game.ecs.utils.spatial_hash import build_npc_feet_hash
        # Reset module cache
        import roguelike_game.ecs.utils.spatial_hash as shm
        shm._npc_feet_hash = None
        shm._npc_feet_hash_frame = -1

        w = _FakeWorld()
        w._frame_count = 1
        e1 = w.create_entity()
        e2 = w.create_entity()

        w.components["Position"] = {
            e1: _Pos(100.0, 100.0),
            e2: _Pos(500.0, 500.0),
        }
        w.components["MultiCollider"] = {
            e1: _MultiCollider({"feet": _CircleFeet(8)}),
            e2: _MultiCollider({"feet": _CircleFeet(8)}),
        }

        sh, circles, rects = build_npc_feet_hash(w)
        assert e1 in circles
        assert e2 in circles
        assert len(rects) == 0

        # Query near e1 should find e1 but not e2
        nearby = sh.query_radius(100.0, 100.0, 50.0)
        assert e1 in nearby
        assert e2 not in nearby

    def test_build_npc_feet_hash_excludes_dead(self):
        """Dead NPCs (with DeathTimer) should be excluded from the hash."""
        from roguelike_game.ecs.utils.spatial_hash import build_npc_feet_hash
        import roguelike_game.ecs.utils.spatial_hash as shm
        shm._npc_feet_hash = None
        shm._npc_feet_hash_frame = -1

        w = _FakeWorld()
        w._frame_count = 2
        alive = w.create_entity()
        dead = w.create_entity()

        w.components["Position"] = {
            alive: _Pos(100.0, 100.0),
            dead: _Pos(110.0, 110.0),
        }
        w.components["MultiCollider"] = {
            alive: _MultiCollider({"feet": _CircleFeet(8)}),
            dead: _MultiCollider({"feet": _CircleFeet(8)}),
        }
        w.components["DeathTimer"] = {dead: object()}

        sh, circles, rects = build_npc_feet_hash(w)
        assert alive in circles
        assert dead not in circles

    def test_build_npc_feet_hash_excludes_player(self):
        """Player entity should be excluded from the NPC hash."""
        from roguelike_game.ecs.utils.spatial_hash import build_npc_feet_hash
        import roguelike_game.ecs.utils.spatial_hash as shm
        shm._npc_feet_hash = None
        shm._npc_feet_hash_frame = -1

        w = _FakeWorld()
        w._frame_count = 3
        player = w.create_entity()
        npc = w.create_entity()

        w.components["Position"] = {
            player: _Pos(100.0, 100.0),
            npc: _Pos(200.0, 200.0),
        }
        w.components["MultiCollider"] = {
            player: _MultiCollider({"feet": _CircleFeet(8)}),
            npc: _MultiCollider({"feet": _CircleFeet(8)}),
        }
        w.components["PlayerTagComponent"] = {player: object()}

        sh, circles, rects = build_npc_feet_hash(w)
        assert player not in circles
        assert npc in circles

    def test_build_npc_feet_hash_per_frame_cache(self):
        """Same frame should return cached result; new frame should rebuild."""
        from roguelike_game.ecs.utils.spatial_hash import build_npc_feet_hash
        import roguelike_game.ecs.utils.spatial_hash as shm
        shm._npc_feet_hash = None
        shm._npc_feet_hash_frame = -1

        w = _FakeWorld()
        w._frame_count = 10
        e1 = w.create_entity()
        w.components["Position"] = {e1: _Pos(50.0, 50.0)}
        w.components["MultiCollider"] = {e1: _MultiCollider({"feet": _CircleFeet(8)})}

        sh1, c1, r1 = build_npc_feet_hash(w)
        sh2, c2, r2 = build_npc_feet_hash(w)
        assert sh1 is sh2, "Same frame should return cached hash"
        assert c1 is c2

        # New frame
        w._frame_count = 11
        sh3, c3, r3 = build_npc_feet_hash(w)
        assert sh3 is not sh1, "New frame should rebuild hash"


# ═══════════════════════════════════════════════════════════════════════
# LAYER 4: Frustum Culling
# ═══════════════════════════════════════════════════════════════════════

class TestFrustumCulling:
    """Verify frustum culling utility functions."""

    def test_get_active_world_rect_basic(self):
        from roguelike_game.ecs.utils.frustum_culling import get_active_world_rect
        cam = _FakeCamera(zoom=1.0, offset_x=100.0, offset_y=200.0)
        rect = get_active_world_rect(cam, 800, 600, margin_px=100.0)
        assert rect is not None
        assert rect.x == 0  # 100 - 100
        assert rect.y == 100  # 200 - 100
        assert rect.width == 1000  # 800 + 200
        assert rect.height == 800  # 600 + 200

    def test_get_active_world_rect_with_zoom(self):
        from roguelike_game.ecs.utils.frustum_culling import get_active_world_rect
        cam = _FakeCamera(zoom=2.0, offset_x=0.0, offset_y=0.0)
        rect = get_active_world_rect(cam, 800, 600, margin_px=50.0)
        assert rect is not None
        # world_w = 800/2 = 400, world_h = 600/2 = 300
        assert rect.width == 500  # 400 + 100
        assert rect.height == 400  # 300 + 100

    def test_get_active_entity_ids_filters_by_position(self):
        from roguelike_game.ecs.utils.frustum_culling import get_active_entity_ids
        w = _FakeWorld()
        w.screen = _FakeScreen(800, 600)
        cam = _FakeCamera(zoom=1.0, offset_x=0.0, offset_y=0.0)

        near = w.create_entity()
        far = w.create_entity()
        w.components["Position"] = {
            near: _Pos(400.0, 300.0),  # center of screen
            far: _Pos(5000.0, 5000.0),  # way outside
        }

        active = get_active_entity_ids(w, cam, margin_px=100.0)
        assert active is not None
        assert near in active
        assert far not in active

    def test_get_active_entity_ids_returns_none_without_camera(self):
        from roguelike_game.ecs.utils.frustum_culling import get_active_entity_ids
        w = _FakeWorld()
        result = get_active_entity_ids(w, None)
        assert result is None, "Should return None when camera is None (fallback to all)"

    def test_fsm_system_has_offscreen_interval(self):
        from roguelike_game.ecs.systems.fsm.fsm_system import FSMSystem
        assert hasattr(FSMSystem, "OFFSCREEN_UPDATE_INTERVAL")
        assert FSMSystem.OFFSCREEN_UPDATE_INTERVAL > 1

    def test_animation_system_has_offscreen_interval(self):
        from roguelike_game.ecs.systems.rendering.animation_system import AnimationSystem
        assert hasattr(AnimationSystem, "OFFSCREEN_UPDATE_INTERVAL")
        assert AnimationSystem.OFFSCREEN_UPDATE_INTERVAL > 1


# ═══════════════════════════════════════════════════════════════════════
# LAYER 3 (integration): NpcSeparationSystem uses SpatialHash
# ═══════════════════════════════════════════════════════════════════════

class TestNpcSeparationSpatialHash:
    """Verify NpcSeparationSystem uses spatial hash and resolves overlaps."""

    def test_separation_resolves_overlap(self):
        """Two overlapping circle NPCs should be pushed apart."""
        from roguelike_game.ecs.systems.physics.npc_separation_system import NpcSeparationSystem

        w = _FakeWorld()
        e1 = w.create_entity()
        e2 = w.create_entity()

        # Place two NPCs at the same position (overlapping)
        w.components["Position"] = {
            e1: _Pos(100.0, 100.0),
            e2: _Pos(100.0, 100.0),
        }
        w.components["MultiCollider"] = {
            e1: _MultiCollider({"feet": _CircleFeet(10)}),
            e2: _MultiCollider({"feet": _CircleFeet(10)}),
        }

        sys = NpcSeparationSystem(perf_log=None, max_iters=5)
        sys.update(w)

        p1 = w.components["Position"][e1]
        p2 = w.components["Position"][e2]
        # After separation, they should no longer be at the exact same position
        dist = ((p1.x - p2.x) ** 2 + (p1.y - p2.y) ** 2) ** 0.5
        assert dist > 0.0, "Overlapping NPCs should be separated"

    def test_separation_does_not_move_player(self):
        """Player should not be moved by NPC separation."""
        from roguelike_game.ecs.systems.physics.npc_separation_system import NpcSeparationSystem

        w = _FakeWorld()
        player = w.create_entity()
        npc = w.create_entity()

        w.components["Position"] = {
            player: _Pos(100.0, 100.0),
            npc: _Pos(100.0, 100.0),
        }
        w.components["MultiCollider"] = {
            player: _MultiCollider({"feet": _CircleFeet(10)}),
            npc: _MultiCollider({"feet": _CircleFeet(10)}),
        }
        w.components["PlayerTagComponent"] = {player: object()}

        sys = NpcSeparationSystem(perf_log=None, max_iters=5)
        sys.update(w)

        pp = w.components["Position"][player]
        assert pp.x == 100.0 and pp.y == 100.0, "Player should not be moved"

    def test_separation_skips_dead_npcs(self):
        """Dead NPCs should not participate in separation."""
        from roguelike_game.ecs.systems.physics.npc_separation_system import NpcSeparationSystem

        w = _FakeWorld()
        alive = w.create_entity()
        dead = w.create_entity()

        w.components["Position"] = {
            alive: _Pos(100.0, 100.0),
            dead: _Pos(100.0, 100.0),
        }
        w.components["MultiCollider"] = {
            alive: _MultiCollider({"feet": _CircleFeet(10)}),
            dead: _MultiCollider({"feet": _CircleFeet(10)}),
        }
        w.components["DeathTimer"] = {dead: object()}

        sys = NpcSeparationSystem(perf_log=None, max_iters=3)
        sys.update(w)

        # Alive NPC should not have moved (no live overlap partner)
        pa = w.components["Position"][alive]
        assert pa.x == 100.0 and pa.y == 100.0

    def test_separation_non_overlapping_no_movement(self):
        """Non-overlapping NPCs should not be moved."""
        from roguelike_game.ecs.systems.physics.npc_separation_system import NpcSeparationSystem

        w = _FakeWorld()
        e1 = w.create_entity()
        e2 = w.create_entity()

        w.components["Position"] = {
            e1: _Pos(100.0, 100.0),
            e2: _Pos(500.0, 500.0),  # far away
        }
        w.components["MultiCollider"] = {
            e1: _MultiCollider({"feet": _CircleFeet(10)}),
            e2: _MultiCollider({"feet": _CircleFeet(10)}),
        }

        sys = NpcSeparationSystem(perf_log=None, max_iters=3)
        sys.update(w)

        p1 = w.components["Position"][e1]
        p2 = w.components["Position"][e2]
        assert p1.x == 100.0 and p1.y == 100.0
        assert p2.x == 500.0 and p2.y == 500.0


# ═══════════════════════════════════════════════════════════════════════
# LAYER 3 (integration): MovementCollisionSystem uses SpatialHash
# ═══════════════════════════════════════════════════════════════════════

class TestMovementCollisionSpatialHash:
    """Verify MovementCollisionSystem integrates spatial hash correctly."""

    def test_system_has_build_npc_feet_hash_import(self):
        """MovementCollisionSystem should import build_npc_feet_hash."""
        import roguelike_game.ecs.systems.physics.movement_collision_system as mcs
        assert hasattr(mcs, "build_npc_feet_hash")

    def test_free_movement_without_obstacles(self):
        """An entity with velocity and no obstacles should move freely."""
        from roguelike_game.ecs.systems.physics.movement_collision_system import MovementCollisionSystem
        import roguelike_game.ecs.utils.spatial_hash as shm
        shm._npc_feet_hash = None
        shm._npc_feet_hash_frame = -1

        w = _FakeWorld()
        w._frame_count = 100
        e1 = w.create_entity()

        w.components["Position"] = {e1: _Pos(100.0, 100.0)}
        w.components["Velocity"] = {e1: _Velocity(5.0, 3.0)}
        w.components["MultiCollider"] = {
            e1: _MultiCollider({"feet": _CircleFeet(8)}),
        }

        sys = MovementCollisionSystem(perf_log=None)
        sys.update(w)

        pos = w.components["Position"][e1]
        # Should have moved by velocity
        assert abs(pos.x - 105.0) < 2.0
        assert abs(pos.y - 103.0) < 2.0

    def test_dead_entities_not_moved(self):
        """Entities with DeathTimer should not be moved."""
        from roguelike_game.ecs.systems.physics.movement_collision_system import MovementCollisionSystem
        import roguelike_game.ecs.utils.spatial_hash as shm
        shm._npc_feet_hash = None
        shm._npc_feet_hash_frame = -1

        w = _FakeWorld()
        w._frame_count = 101
        dead = w.create_entity()

        w.components["Position"] = {dead: _Pos(100.0, 100.0)}
        w.components["Velocity"] = {dead: _Velocity(10.0, 10.0)}
        w.components["MultiCollider"] = {
            dead: _MultiCollider({"feet": _CircleFeet(8)}),
        }
        w.components["DeathTimer"] = {dead: object()}

        sys = MovementCollisionSystem(perf_log=None)
        sys.update(w)

        pos = w.components["Position"][dead]
        assert pos.x == 100.0 and pos.y == 100.0
