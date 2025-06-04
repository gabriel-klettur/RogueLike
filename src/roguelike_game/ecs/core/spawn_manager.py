# Path: src/roguelike_game/ecs/core/spawn_manager.py

from roguelike_engine.map.utils import calculate_lobby_offset
from roguelike_engine.config.map_config import global_map_settings
from roguelike_game.ecs.utils.collider_utils import build_collider_rect
from roguelike_game.ecs.factories.entity_factory import _load_caches_once, _DEFS, _create_sprite_component, _calculate_position, _create_collider_components
from roguelike_game.ecs.utils.spawn_utils import find_spawn_positions
from roguelike_game.ecs.components.spawn.spawn_request import SpawnRequest

class SpawnNPCManager:
    def __init__(self, world):
        """
        :param world: instancia de ECSWorld (se asume que ya tiene spatial_index, map_manager y buildings).
        """
        self.world = world
        self.map_manager = world.map_manager
        self.buildings = world.buildings

    def spawn_npc_initial(self):
        """
        Lógica de spawn inicial de NPCs, exactamente lo que antes estaba en _spawn_initial_npcs de ECSWorld,
        pero referenciando self.world para crear entidades y asignar componentes.
        """
        # 1) Preparar datos comunes
        lobby_offset = calculate_lobby_offset()
        zone_size = global_map_settings.zone_size

        # 2) Cargar definiciones y sprite base
        _load_caches_once()
        cfg = _DEFS["barbol"]
        sprite, _ = _create_sprite_component("barbol")
        spawned_rects = []

        # 3) Spawn en LOBBY
        positions = find_spawn_positions(self.map_manager, self.buildings, lobby_offset, zone_size, neighbor_padding=3, sample_count=10)
        filtered_positions = []
        for tx, ty in positions:
            px, py = _calculate_position(tx, ty, cfg, sprite)
            multi = _create_collider_components(sprite, cfg)
            feet = multi.colliders.get("feet")
            if feet:
                rect = build_collider_rect(px, py, feet)
                if not any(rect.colliderect(r) for r in spawned_rects):
                    spawned_rects.append(rect)
                    filtered_positions.append((tx, ty))

        print(f"[SpawnManager][Spawn] Lobby: candidatos={len(positions)}, válidos={len(filtered_positions)}")
        for tx, ty in filtered_positions:
            eid_req = self.world.create_entity()
            self.world.components['SpawnRequest'][eid_req] = SpawnRequest(prototype="barbol", position=(tx, ty))

        # 4) Spawn en EMPTY_LEFT (si existe)
        offsets = global_map_settings.zone_offsets
        empty_offset = offsets.get('empty_left')
        if not empty_offset:
            return

        empty_positions = find_spawn_positions(self.map_manager, self.buildings, empty_offset, zone_size, neighbor_padding=3, sample_count=100)
        filtered_empty = []
        for tx, ty in empty_positions:
            px, py = _calculate_position(tx, ty, cfg, sprite)
            multi = _create_collider_components(sprite, cfg)
            feet = multi.colliders.get("feet")
            if feet:
                rect = build_collider_rect(px, py, feet)
                if not any(rect.colliderect(r) for r in spawned_rects):
                    spawned_rects.append(rect)
                    filtered_empty.append((tx, ty))

        print(f"[SpawnManager][Spawn] Empty Left: candidatos={len(empty_positions)}, válidos={len(filtered_empty)}")
        for tx, ty in filtered_empty:
            eid_req = self.world.create_entity()
            self.world.components['SpawnRequest'][eid_req] = SpawnRequest(prototype="barbol", position=(tx, ty))
